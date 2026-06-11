using LuaDC1.Format;
using LuaDC1.Naming;

namespace LuaDC1.Decompile;

/// <summary>
/// Computes a per-function <c>slot -&gt; name</c> map for recovered locals and parameters, applying
/// the heuristic ladder (debug names &gt; param dictionary &gt; multi-return dictionary &gt; team
/// constants &gt; single-result &gt; field/global &gt; loop vars), then resolving collisions. Names are
/// cosmetic, so this never affects the emitted opcodes. Slots left unnamed fall back to <c>varN</c>.
/// </summary>
public static class VariableNamer
{
    private static readonly Dictionary<string, string> TeamByName = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Empire"] = "IMP", ["Alliance"] = "ALL", ["Republic"] = "REP", ["CIS"] = "CIS",
    };

    public static Dictionary<int, string> Compute(Prototype p, ReconstructResult result, string? funcName, NameDictionary dict)
    {
        var names = new Dictionary<int, string>();
        var taken = new HashSet<string>();
        var globals = CollectGlobals(result.Items);

        void Assign(int slot, string? name)
        {
            if (name == null || names.ContainsKey(slot)) return;
            string final = Unique(Sanitize(name), taken, globals);
            names[slot] = final;
            taken.Add(final);
        }

        // 0. Real debug names, if the chunk wasn't stripped.
        foreach (var lv in p.Locals)
            if (!string.IsNullOrEmpty(lv.Name)) { /* slot index isn't directly stored; skipped for stripped BF1 chunks */ }

        // 1. Parameters from the dictionary / _fn pattern / this-rule.
        var paramNames = dict.ParamsFor(funcName, p.NumParams);
        for (int i = 0; i < p.NumParams; i++) Assign(i, paramNames?[i]);
        if (p.NumParams > 0 && !names.ContainsKey(0) && IsMethodShaped(funcName, result.Items))
            Assign(0, "this");

        // 2. Loop variables.
        int numericDepth = 0;
        foreach (var item in result.Items)
        {
            if (item is not ControlItem c) continue;
            if (c.Ins.Op == OpCode.ForPrep) Assign(c.LoopVarSlot, NumericLoopName(numericDepth++));
            else if (c.Ins.Op == OpCode.LForPrep) { Assign(c.LoopVarSlot, "k"); Assign(c.LoopVarSlot + 1, "v"); }
        }

        // 3. Locals named from their defining value (calls, fields, globals).
        var processed = new HashSet<int>();
        foreach (int slot in result.SlotValues.Keys.OrderBy(s => s))
        {
            if (names.ContainsKey(slot) || processed.Contains(slot)) continue;
            var value = result.SlotValues[slot];

            if (value is CallExpr call)
            {
                var group = new List<int> { slot };
                while (result.SlotValues.TryGetValue(group[^1] + 1, out var nv) && ReferenceEquals(nv, call))
                    group.Add(group[^1] + 1);
                foreach (int g in group) processed.Add(g);

                string? fn = (call.Callee as GlobalExpr)?.Name;
                var rets = dict.ReturnsFor(fn);
                if (rets != null)
                    for (int i = 0; i < group.Count; i++) Assign(group[i], i < rets.Length ? rets[i] : null);
                else if (group.Count == 1)
                    Assign(slot, NameFromFunc(fn));
            }
            else if (value is IndexExpr { Key: StringExpr fieldKey } && IsIdent(fieldKey.Value))
                Assign(slot, fieldKey.Value);
            else if (value is GlobalExpr gv)
                Assign(slot, StripGlobalPrefix(gv.Name));
        }

        // 4. Team/faction constants, recognized from SetTeamName(slot, "Empire") usage.
        foreach (var item in result.Items)
        {
            if (item is StmtItem { Stmt: CallStmt cs } && cs.Call.Callee is GlobalExpr { Name: "SetTeamName" }
                && cs.Call.Args.Count >= 2 && cs.Call.Args[0] is LocalExpr le
                && cs.Call.Args[1] is StringExpr team && TeamByName.TryGetValue(team.Value, out var c))
                Assign(le.Slot, c);
        }

        return names;
    }

    // ---- naming rules ------------------------------------------------------

    /// <summary>Strip an engine/verb prefix and lower-camel the noun; flag booleans with a `b` prefix.</summary>
    private static string? NameFromFunc(string? fn)
    {
        if (fn == null) return null;
        string s = fn;
        if (s.StartsWith("ScriptCB_", StringComparison.Ordinal)) s = s["ScriptCB_".Length..];
        int us = s.LastIndexOf('_');                  // metagame_GetNumPlanets -> GetNumPlanets
        if (us >= 0 && us < s.Length - 1) s = s[(us + 1)..];
        if (string.Equals(fn, "getn", StringComparison.Ordinal)) return "count";

        foreach (var (verb, isBool) in new[] { ("Get", false), ("Read", false), ("New", false), ("Create", false),
                                               ("Find", false), ("Is", true), ("Are", true), ("Has", true) })
        {
            if (s.StartsWith(verb, StringComparison.Ordinal) && s.Length > verb.Length && char.IsUpper(s[verb.Length]))
            {
                string noun = s[verb.Length..];
                return isBool ? "b" + noun : LowerFirst(noun);
            }
        }
        return s.Length > 0 ? LowerFirst(s) : null;
    }

    private static string NumericLoopName(int depth) => depth switch { 0 => "i", 1 => "j", 2 => "k", _ => "i" + depth };

    private static bool IsMethodShaped(string? funcName, IReadOnlyList<LinearItem> items)
    {
        if (funcName != null && funcName.Contains("_fn", StringComparison.Ordinal)) return true;
        // Anonymous closure: treat as a method if its first parameter is used as a table.
        foreach (var e in AllExprs(items))
            if (e is IndexExpr { Target: LocalExpr { Slot: 0 } }) return true;
        return false;
    }

    private static string StripGlobalPrefix(string name) =>
        name.Length > 1 && name[0] == 'g' && char.IsUpper(name[1]) ? LowerFirst(name[1..]) : name;

    // ---- collision handling ------------------------------------------------

    private static string Unique(string name, HashSet<string> taken, HashSet<string> globals)
    {
        if (!taken.Contains(name) && !globals.Contains(name)) return name;
        for (int i = 2; ; i++)
        {
            string candidate = name + i;
            if (!taken.Contains(candidate) && !globals.Contains(candidate)) return candidate;
        }
    }

    private static string Sanitize(string name)
    {
        if (name.Length == 0) return "v";
        if (!char.IsLetter(name[0]) && name[0] != '_') name = "v" + name;
        return IsKeyword(name) ? name + "_" : name;
    }

    // ---- small helpers -----------------------------------------------------

    private static string LowerFirst(string s) => s.Length == 0 ? s : char.ToLowerInvariant(s[0]) + s[1..];

    private static bool IsIdent(string s) =>
        s.Length > 0 && (char.IsLetter(s[0]) || s[0] == '_') && s.All(c => char.IsLetterOrDigit(c) || c == '_') && !IsKeyword(s);

    private static bool IsKeyword(string s) => s is "and" or "break" or "do" or "else" or "elseif" or "end"
        or "for" or "function" or "if" or "in" or "local" or "nil" or "not" or "or" or "repeat" or "return"
        or "then" or "until" or "while";

    private static HashSet<string> CollectGlobals(IReadOnlyList<LinearItem> items)
    {
        var set = new HashSet<string>();
        foreach (var e in AllExprs(items))
            if (e is GlobalExpr g) set.Add(g.Name);
        return set;
    }

    private static IEnumerable<Expr> AllExprs(IReadOnlyList<LinearItem> items)
    {
        foreach (var item in items)
        {
            switch (item)
            {
                case StmtItem s: foreach (var e in StmtExprs(s.Stmt)) foreach (var x in Sub(e)) yield return x; break;
                case ControlItem c: foreach (var op in c.Operands) foreach (var x in Sub(op)) yield return x; break;
            }
        }
    }

    private static IEnumerable<Expr> StmtExprs(Stmt stmt) => stmt switch
    {
        LocalStmt l => l.Values,
        AssignStmt a => a.Targets.Concat(a.Values),
        CallStmt c => new Expr[] { c.Call },
        ReturnStmt r => r.Values,
        FunctionStmt f => new Expr[] { f.Target },
        _ => Enumerable.Empty<Expr>(),
    };

    private static IEnumerable<Expr> Sub(Expr e)
    {
        yield return e;
        switch (e)
        {
            case IndexExpr ix: foreach (var x in Sub(ix.Target)) yield return x; foreach (var x in Sub(ix.Key)) yield return x; break;
            case CallExpr c: foreach (var x in Sub(c.Callee)) yield return x; foreach (var a in c.Args) foreach (var x in Sub(a)) yield return x; break;
            case BinaryExpr b: foreach (var x in Sub(b.Left)) yield return x; foreach (var x in Sub(b.Right)) yield return x; break;
            case UnaryExpr u: foreach (var x in Sub(u.Operand)) yield return x; break;
            case ConcatExpr cc: foreach (var p in cc.Parts) foreach (var x in Sub(p)) yield return x; break;
            case TableExpr t:
                foreach (var a in t.ArrayPart) foreach (var x in Sub(a)) yield return x;
                foreach (var kv in t.HashPart) { foreach (var x in Sub(kv.Key)) yield return x; foreach (var x in Sub(kv.Value)) yield return x; }
                break;
        }
    }
}
