# BF1LuaDC

A decompiler for **Star Wars Battlefront (2004)** Lua 4.0.1 scripts.

It reads the precompiled Lua bytecode directly (no external `luac` step needed to decompile),
automatically strips the UCFB container off an extracted `.script` chunk, reconstructs expressions
and control flow, and writes readable Lua. It can optionally verify its own output by recompiling
with the bundled Lua 4.0.1 `luac` and comparing the opcode streams.

## Usage

```
LuaDC1 --ui [file]            launch the GUI (optionally pre-loading a .lvl/.script)
LuaDC1 <input> [options]      <input> = a .lvl, a .script/.luac file, OR a folder (batch)

  --out <path>        output file (single) or directory (batch/.lvl)
  --list              just list the scripts contained in a .lvl
  --batch             force folder mode (auto when <input> is a directory)
  --pattern <glob>    batch filter (default *.script)
  --verify            recompile output with bundled luac and report round-trip accuracy
  --luac <path>       explicit luac.exe (else auto-probe)
  --keep-bytecode     also write the stripped raw bytecode (.luac)
  --names <full|off>  heuristic variable naming (default full; off = literal varN)
  --names-file <json> extra name dictionary (merged over the built-in one)
  --mine <dir>        mine original .lua scripts -> name dictionary JSON (--out path)
  --disasm            print a luac-style disassembly instead of decompiling
  --oracle <txt>      compare our decode against a genuine luac -l listing (self-test)
  --verbose           print source kind, script name and bytecode size
  -h, --help          show this help
```

### Examples

```
LuaDC1 --ui                          # open the GUI
LuaDC1 shell.lvl --list              # list the scripts inside a .lvl
LuaDC1 shell.lvl --verify            # decompile every script -> shell_scripts\
LuaDC1 dpk02a.script                 # auto-strip UCFB + decompile -> dpk02a.lua
LuaDC1 dpk02a.script --verify        # decompile and confirm it round-trips
LuaDC1 .\scripts --verify            # batch a folder -> scripts\decompiled\
```

`<input>` can be a whole `.lvl` (every contained script is extracted and decompiled), a single
extracted `.script` chunk (the UCFB header and trailing padding are detected and stripped
automatically), or already-massaged raw Lua 4.0 bytecode.

## GUI

`LuaDC1 --ui` opens a Windows desktop front end (modeled on BAD-AL's `special_unluac`): **File →
Open .lvl** lists every script in the level on the left; selecting one decompiles it on the right.
A **View** dropdown switches between *Decompiled Lua*, *Listing* (luac-style), and *Summary*, the
**Verify** toggle adds a round-trip overview (per-script status in the list, full result atop the
text), **Names** toggles heuristic naming, and **Verify All** fills the round-trip column for the
whole level. *Save current .lua* writes the displayed source.

## What it handles

- **UCFB extraction** — walks `ucfb -> scr_ -> BODY` and takes exactly the bytecode bytes.
- **Lua 4.0 bytecode** — full undump of the precompiled chunk (note: this BF1 build uses 4-byte
  *float* numbers, not 8-byte doubles).
- **Expressions** — locals/globals, table indexing and dotted access, arithmetic with correct
  precedence/parenthesization, string concatenation, method calls (`a:b()`), table constructors
  (array + keyed parts), nested closures, multiple-return calls and multiple assignment.
- **Control flow** — `if/elseif/else`, numeric `for`, generic `for`, `while`, `repeat/until`,
  `break`, and short-circuit `and`/`or` conditions.

Straight-line scripts (e.g. mission `.script` files) decompile to bytecode-exact Lua. For
control-flow-heavy UI scripts most functions decompile exactly; the few with constructs the
structurer can't yet recover are emitted as a clearly-marked raw-disassembly comment block rather
than as wrong code, and `--verify` reports per-function accuracy so you can spot them.

## Variable naming

Stripped BF1 chunks carry no local/parameter names, so the decompiler synthesizes them. By default
(`--names full`) it applies heuristics learned from the original game scripts to produce meaningful
names instead of `var0`, `var1`, …:

- **Parameters** from a dictionary keyed on the recovered function name (e.g. `IFButton_fnSelect(this,
  on, labelonly)`); the first parameter of a UI method is `this`.
- **Multi-return calls** from the engine API (`local w, h = ScriptCB_GetSafeScreenInfo()`).
- **Single-result calls** by stripping the verb/`ScriptCB_` prefix (`local numPlanets =
  metagame_GetNumPlanets()`).
- **Team constants** in mission scripts (`local ALL = 1` recognized from `SetTeamName(ALL, "Alliance")`).
- **Loop variables** (`for i = …`), and field/global-derived names.

Names are purely cosmetic — they never change the emitted opcodes, so naming can't affect the
`--verify` round-trip. Use `--names off` for literal `varN`.

The built-in dictionary is mined from the original scripts. Regenerate or extend it:

```
LuaDC1 --mine "path\to\original\scripts" --out bf1-names.json
LuaDC1 foo.script --names-file bf1-names.json
```

## Verifying accuracy

`--verify` recompiles the generated `.lua` with the genuine Lua 4.0.1 `luac` and compares the
opcode stream of every function against the original. The comparison measures *behavioral*
equivalence — it ignores no-op jumps the stock compiler emits as padding, since a clean
decompilation that omits them is still correct. Output is one of:

- `OK (round-trip: N functions, opcodes match)` — behaviorally exact.
- `PARTIAL (m/N functions match, K opcode diffs)` — some functions need review.
- `FAIL (luac: …)` — the generated source didn't compile.

## Building

```
dotnet build LuaDC1/LuaDC1.csproj
```

.NET 8 (or newer) SDK. The bundled `luac.exe` (Lua 4.0.1) ships under the build output and is used
only for `--verify`.

## Project layout

- `Format/` — opcodes, instruction decoding, the prototype IR, and the disassembler.
- `IO/` — UCFB extractor and the Lua 4.0 bytecode reader (undump).
- `Decompile/` — stack-based expression reconstruction, control-flow structuring, Lua emitter.
- `Verify/` — round-trip verification against the bundled `luac`.
- `Cli.cs` — single-file and batch front end.
