# Heuristic Variable Naming — Design Plan

## Problem

The decompiler synthesizes `var0`, `var1`, … for every recovered **local** and **parameter**.
(Globals and table fields already come out with their real names — they live in the bytecode's
string constants via `GETGLOBAL`/`GETDOTTED`/`SETGLOBAL`, so only locals and params are nameless.)

Goal: replace `varN` with meaningful names derived from BF1's actual conventions, mined from the
original game scripts in `BFBuilder\Assets\Mission LUA Samples\` and
`ShellandCommonBuildersu2\Common\Scripts\`.

## Safety property (why this is low-risk)

Variable names are **cosmetic**: renaming a local changes no opcodes. So a naming pass can never
break the `--verify` round-trip — recompiling renamed output yields the same bytecode. We can apply
aggressive heuristics and still keep the correctness guarantee. (A `--names off|safe|full` flag can
gate aggressiveness for users who prefer literal `varN`.)

## Where it plugs in

A **naming pass between reconstruction and emission**, producing a per-function `slot → name` map:

```
BytecodeReader → ExpressionReconstructor → [VariableNamer] → ControlFlowStructurer → LuaEmitter
                                                  │
                                          slot→name map per Prototype
```

`LocalExpr` already carries `Slot` (its identity) and a default `Name`. The namer computes a name
map; the emitter resolves `LocalExpr`/params through that map instead of the baked-in `var{slot}`.
Because naming keys off the reconstructed statements (calls, assignments, loops) the pass has all the
context it needs (which function defines a slot, what call produced it, how it's used).

## Confidence ladder

Apply in order; the **first rule that fires wins** for a given slot. Ordered most- to
least-reliable, so high-confidence names dominate.

| # | Rule | Source | Confidence |
|---|------|--------|-----------|
| 1 | **Embedded debug names** — use `LocVar.Name` if the chunk wasn't stripped | bytecode | exact |
| 2 | **Parameter dictionary** — name a function's params from its recovered global name | corpus | high |
| 3 | **Multi-return dictionary** — `local a,b = ScriptCB_GetScreenInfo()` → `right,bottom,b,w` | corpus | high |
| 4 | **Team/faction constants** (mission scripts) — `local X = 1/2` used in `SetTeamName(X,"Empire")` → `IMP` | usage scan | high |
| 5 | **Single-result call** — `local x = GetFontHeight(...)` → `height` (strip `Get`/`ScriptCB_`) | rule | medium |
| 6 | **Assigned-to-field** — a local later stored to `t.foo` (or returned as `foo`) takes that field name | dataflow | medium |
| 7 | **Loop variables** — numeric → `i`,`j`,`k` by nesting depth; generic → `k,v` (or singularized table) | rule | high |
| 8 | **Type-prefix fallback** — infer table/bool/number → `t`/`b`/`n` + index (optional) | rule | low |
| 9 | **`varN` fallback** — unchanged | — | — |

### Rule details

**2 — Parameter dictionary.** The decompiler recovers each function's global name (the
`CLOSURE; SETGLOBAL <name>` pattern), so params can be restored by lookup. Three tiers:
- *Exact name* in dictionary → use its param list verbatim, e.g.
  `ifelem_shellscreen_fnStartMovie(movieName, loop, nextMovieName, fullscreen, left, top, width, height)`.
- *`_fn*` pattern* → pattern defaults, e.g. `*_fnSetSize(this,w,h)`, `*_fnSelect(this,on,labelonly)`,
  `*_fnHilight(this,on,fDt)`, `*_fnUpdate(this,fDt)`, `*_fnEnter/Leave(this,bFwd)`.
- *Any `X_fnY` method* → first param is `this` (universal in the corpus — never `self`/`screen`).

**3 — Multi-return dictionary** (engine API is stable across all mods, so this generalizes well):

| Function | Return names |
|----------|--------------|
| `ScriptCB_GetScreenInfo()` | `right, bottom, b, w` |
| `ScriptCB_GetSafeScreenInfo()` | `w, h` |
| `ScriptCB_GetError()` / `GetLatestError()` | `ErrorLevel, ErrorMessage` |
| `ScriptCB_GetMouseSensitivity()` | `mouseSens, mouseSensStep, mouseSensMax` |
| `ScriptCB_GetJoySensitivity()` | `joysense, joysenseStep, joysenseMax` |
| `ScriptCB_GetVolumes()` | `VolMusic, VolSfx, VolVoice, MaxVol, VolMaster` |
| `ScriptCB_GetLobbyPlayerFlags()` | `muted, friend, bCanBoot` |

**4 — Team/faction constants.** Mission scripts open with `local ALL/IMP/REP/CIS/ATT/DEF = 1|2`. The
value alone (1 or 2) can't disambiguate, but **usage does**. Scan the function for the first tell:
- `SetTeamName(X, "Empire")` → `IMP`; `"Alliance"` → `ALL`; `"Republic"` → `REP`; `"CIS"` → `CIS`;
  other strings → derive (e.g. `"Naboo Guard"` → `GAR`, else `LOC`/team-number).
- No `SetTeamName` tell but the slot is a small-int constant used in `AddUnitClass`/`SetUnitCount`/
  `SetTeamAsEnemy` → fall back to `ATT` (value 1) / `DEF` (value 2).

**5 — Single-result call.** `local x = Func(args)`:
- Strip a leading `ScriptCB_` and/or `Get`/`Is`/`Read` verb → `GetFontHeight` → `height`,
  `metagame_GetNumPlanets` → `numPlanets`. Lowercase first letter.
- `getn(t)` → `count` (or `n`). Booleans (`Is*`/`Are*`/`Has*`) → `b` + Pascal remainder (`bIsHost`).

**6 — Assigned-to-field.** If slot `s`'s value is later written to `t.foo`/`SETGLOBAL gFoo` or returned
in a position the caller names, borrow that field name (`foo`). Cheap dataflow over the statement list.

**7 — Loop variables.** Numeric `for`: `i`, then `j`, `k` for nested depth. Generic `for`: `k, v`;
if the iterated table name is plural and well-known, optionally singularize (`gMetaAttackableList` →
`item`/`attackable`) — but `k,v` is the safe default (~90% of the corpus).

## Conflict resolution

- **Per-function uniqueness:** if a chosen name is already used in the same function, append the
  smallest free integer (`right`, `right2`). Deterministic.
- **Avoid keywords** (`end`, `function`, `local`, …) and **avoid shadowing a global referenced in the
  same function** (don't name a local `getn` if the body calls `getn`).
- **Stability:** the map is computed once per function and is pure (no randomness), so output is
  reproducible run-to-run.

## The name dictionary (data + how to (re)generate it)

Ship a small data file (`names/bf1-names.json`) with two maps — `funcParams` and `apiReturns` —
plus the `_fn*` pattern table. Generate it from the originals so it's reproducible and extensible:

- Add a hidden CLI mode `--mine <dir>`:
  - Scan every `*.lua`, regex `function <name>(<params>)` → `funcParams[name] = params`.
  - Regex `local <names> = <Func>(` → accumulate `apiReturns[Func]` by majority vote across call sites.
  - Emit/merge `bf1-names.json`.
- Users can re-mine their own script collections to extend coverage. The shipped dictionary is built
  from the two reference corpora (`Mission LUA Samples`, `Common/Scripts`, `Common/GOGscripts`).

This keeps the heuristics data-driven: new API or new mod conventions = re-run `--mine`, no code change.

## Implementation phases

1. **Dictionary + param/return naming** (biggest readability win, lowest risk): load `bf1-names.json`;
   apply rules 1–3. Add the `--mine` tool to build it. Wire the slot→name map into the emitter.
2. **Loop vars + single-result + field naming** (rules 5–7): mostly local, rule-based.
3. **Mission team constants** (rule 4): the usage scan; high payoff for the mission `.script` corpus.
4. **Polish**: type-prefix fallback (rule 8), dedupe edge cases, `--names off|safe|full` flag.

## Validation

- Run the full corpus with `--verify` before/after: **opcode round-trip must be unchanged** (names are
  cosmetic) — this is the regression guard.
- Spot-check renamed output against the matching original in `Mission LUA Samples` / `Common\Scripts`
  to confirm names line up with the developers' intent (e.g. `local var1,var2 = GetSafeScreenInfo()`
  becomes `local w, h = ScriptCB_GetSafeScreenInfo()`).
