using System.Globalization;
using System.Text;
using LuaDC1.Format;
using LuaDC1.Naming;

namespace LuaDC1.Decompile;

/// <summary>
/// Pretty-prints a reconstructed <see cref="Prototype"/> tree as Lua 4.0 source. Each function is
/// prepared independently (reconstruct -> name -> structure); a per-function <c>slot -&gt; name</c>
/// map resolves locals and parameters at emission time. Naming is cosmetic and never changes the
/// emitted opcodes. Pass a null dictionary to keep literal <c>varN</c> names.
/// </summary>
public sealed class LuaEmitter
{
    private readonly StringBuilder _sb = new();
    private readonly NameDictionary? _dict;
    private Dictionary<int, string> _names = new();

    private LuaEmitter(NameDictionary? dict) => _dict = dict;

    public static string Emit(Prototype main, NameDictionary? dict)
    {
        var e = new LuaEmitter(dict);
        e._sb.Append("-- Decompiled by LuaDC1 (Star Wars Battlefront Lua 4.0 decompiler)\n\n");
        var (names, stmts) = e.Prepare(main, null);
        e._names = names;
        e.EmitStmts(stmts, 0);
        return e._sb.ToString();
    }

    /// <summary>Reconstruct, name and structure one prototype; raw-dump fallback on failure.</summary>
    private (Dictionary<int, string> names, List<Stmt> stmts) Prepare(Prototype p, string? funcName)
    {
        try
        {
            var result = ExpressionReconstructor.Reconstruct(p);
            var names = _dict != null ? VariableNamer.Compute(p, result, funcName, _dict) : new();
            var stmts = ControlFlowStructurer.Structure(result, p);
            if (!WellFormed(stmts))
                throw new InvalidOperationException("structured output is not well-formed (return/break not last in block)");
            return (names, stmts);
        }
        catch (Exception ex)
        {
            var lines = new List<string> { $"[decompiler] could not reconstruct this function: {ex.Message}" };
            lines.AddRange(Disassembler.InstructionLines(p));
            return (new(), new List<Stmt> { new RawBlockStmt(lines) });
        }
    }

    private void EmitStmts(IReadOnlyList<Stmt> stmts, int indent)
    {
        foreach (var s in stmts) EmitStmt(s, indent);
    }

    /// <summary>
    /// Lua requires <c>return</c>/<c>break</c> to be the last statement of their block. A structuring
    /// error can place code after one; reject such trees so the caller falls back to a raw dump that
    /// still compiles, rather than emitting invalid Lua.
    /// </summary>
    private static bool WellFormed(IReadOnlyList<Stmt> stmts)
    {
        for (int i = 0; i < stmts.Count; i++)
        {
            var s = stmts[i];
            if ((s is ReturnStmt or BreakStmt) && i != stmts.Count - 1) return false;
            switch (s)
            {
                case IfStmt iff:
                    foreach (var c in iff.Clauses) if (!WellFormed(c.Body)) return false;
                    if (iff.ElseBody != null && !WellFormed(iff.ElseBody)) return false;
                    break;
                case NumericForStmt nf when !WellFormed(nf.Body): return false;
                case GenericForStmt gf when !WellFormed(gf.Body): return false;
                case WhileStmt w when !WellFormed(w.Body): return false;
                case RepeatStmt rp when !WellFormed(rp.Body): return false;
            }
        }
        return true;
    }

    private void EmitStmt(Stmt s, int indent)
    {
        string pad = Pad(indent);
        switch (s)
        {
            case LocalStmt l:
                _sb.Append(pad).Append("local ").Append(string.Join(", ", l.Names.Select(MapName)));
                if (l.Values.Count > 0) _sb.Append(" = ").Append(RenderList(l.Values, indent));
                _sb.Append('\n');
                break;

            case AssignStmt a:
                _sb.Append(pad).Append(RenderList(a.Targets, indent)).Append(" = ")
                   .Append(RenderList(a.Values, indent)).Append('\n');
                break;

            case CallStmt c:
                _sb.Append(pad).Append(RenderExpr(c.Call, 0, indent)).Append('\n');
                break;

            case ReturnStmt ret:
                _sb.Append(pad).Append("return");
                if (ret.Values.Count > 0) _sb.Append(' ').Append(RenderList(ret.Values, indent));
                _sb.Append('\n');
                break;

            case FunctionStmt f: EmitFunction(f, indent); break;

            case IfStmt iff: EmitIf(iff, indent); break;
            case NumericForStmt nf: EmitNumericFor(nf, indent); break;
            case GenericForStmt gf: EmitGenericFor(gf, indent); break;
            case WhileStmt w:
                _sb.Append(pad).Append("while ").Append(RenderExpr(w.Condition, 0, indent)).Append(" do\n");
                EmitStmts(w.Body, indent + 1);
                _sb.Append(pad).Append("end\n");
                break;
            case RepeatStmt rp:
                _sb.Append(pad).Append("repeat\n");
                EmitStmts(rp.Body, indent + 1);
                _sb.Append(pad).Append("until ").Append(RenderExpr(rp.Condition, 0, indent)).Append('\n');
                break;
            case BreakStmt: _sb.Append(pad).Append("break\n"); break;

            case RawBlockStmt rb:
                foreach (var line in rb.Lines) _sb.Append(pad).Append("-- ").Append(line).Append('\n');
                break;
        }
    }

    private void EmitFunction(FunctionStmt f, int indent)
    {
        string pad = Pad(indent);
        var child = f.Closure.Proto;
        string? fname = (f.Target as GlobalExpr)?.Name;
        var (names, stmts) = Prepare(child, fname);

        _sb.Append('\n').Append(pad).Append(f.IsLocal ? "local function " : "function ")
           .Append(RenderExpr(f.Target, 0, indent)).Append('(').Append(ParamList(child, names)).Append(")\n");

        var saved = _names;
        _names = names;
        EmitStmts(stmts, indent + 1);
        _names = saved;

        _sb.Append(pad).Append("end\n");
    }

    private void EmitIf(IfStmt iff, int indent)
    {
        string pad = Pad(indent);
        for (int i = 0; i < iff.Clauses.Count; i++)
        {
            _sb.Append(pad).Append(i == 0 ? "if " : "elseif ")
               .Append(RenderExpr(iff.Clauses[i].Condition, 0, indent)).Append(" then\n");
            EmitStmts(iff.Clauses[i].Body, indent + 1);
        }
        if (iff.ElseBody != null)
        {
            _sb.Append(pad).Append("else\n");
            EmitStmts(iff.ElseBody, indent + 1);
        }
        _sb.Append(pad).Append("end\n");
    }

    private void EmitNumericFor(NumericForStmt nf, int indent)
    {
        string pad = Pad(indent);
        _sb.Append(pad).Append("for ").Append(MapName(nf.Var)).Append(" = ")
           .Append(RenderExpr(nf.Start, 0, indent)).Append(", ").Append(RenderExpr(nf.Limit, 0, indent));
        if (nf.Step != null) _sb.Append(", ").Append(RenderExpr(nf.Step, 0, indent));
        _sb.Append(" do\n");
        EmitStmts(nf.Body, indent + 1);
        _sb.Append(pad).Append("end\n");
    }

    private void EmitGenericFor(GenericForStmt gf, int indent)
    {
        string pad = Pad(indent);
        _sb.Append(pad).Append("for ").Append(string.Join(", ", gf.Vars.Select(MapName))).Append(" in ")
           .Append(RenderExpr(gf.Table, 0, indent)).Append(" do\n");
        EmitStmts(gf.Body, indent + 1);
        _sb.Append(pad).Append("end\n");
    }

    // ---- expressions -------------------------------------------------------

    private string RenderList(IReadOnlyList<Expr> exprs, int indent) =>
        string.Join(", ", exprs.Select(e => RenderExpr(e, 0, indent)));

    private string RenderExpr(Expr e, int parentPrec, int indent)
    {
        switch (e)
        {
            case NilExpr: return "nil";
            case NumberExpr n: return RenderNumber(n);
            case StringExpr s: return Disassembler.Quote(s.Value);
            case LocalExpr l: return _names.GetValueOrDefault(l.Slot, l.Name);
            case GlobalExpr g: return g.Name;
            case UpvalExpr u: return "upval" + u.Index;
            case VarargExpr: return "...";

            case IndexExpr ix:
            {
                string target = RenderExpr(ix.Target, 99, indent);
                return ix.Key is StringExpr fs && IsIdentifier(fs.Value)
                    ? $"{target}.{fs.Value}"
                    : $"{target}[{RenderExpr(ix.Key, 0, indent)}]";
            }

            case CallExpr c:
            {
                string callee = RenderExpr(c.Callee, 99, indent);
                string args = RenderList(c.Args, indent);
                return c.IsMethod ? $"{callee}:{c.MethodName}({args})" : $"{callee}({args})";
            }

            case BinaryExpr b:
            {
                string l = RenderExpr(b.Left, b.RightAssoc ? b.Prec + 1 : b.Prec, indent);
                string r = RenderExpr(b.Right, b.RightAssoc ? b.Prec : b.Prec + 1, indent);
                return Paren($"{l} {b.Op} {r}", b.Prec, parentPrec);
            }

            case UnaryExpr u:
            {
                string operand = RenderExpr(u.Operand, Precedences.Unary, indent);
                string text = u.Op == "not" ? $"not {operand}" : $"{u.Op}{operand}";
                return Paren(text, Precedences.Unary, parentPrec);
            }

            case ConcatExpr cc:
            {
                string body = string.Join(" .. ", cc.Parts.Select(p => RenderExpr(p, Precedences.Concat + 1, indent)));
                return Paren(body, Precedences.Concat, parentPrec);
            }

            case TableExpr t: return RenderTable(t, indent);
            case ClosureExpr cl: return RenderClosure(cl, indent);
            case RawExpr rw: return rw.Text;
            default: return "nil --[[unrecognized expression]]";
        }
    }

    private string RenderTable(TableExpr t, int indent)
    {
        var parts = new List<string>();
        foreach (var item in t.ArrayPart) parts.Add(RenderExpr(item, 0, indent));
        foreach (var kv in t.HashPart)
        {
            string key = kv.Key is StringExpr s && IsIdentifier(s.Value)
                ? s.Value
                : $"[{RenderExpr(kv.Key, 0, indent)}]";
            parts.Add($"{key} = {RenderExpr(kv.Value, 0, indent)}");
        }
        if (parts.Count == 0) return "{}";
        return "{ " + string.Join(", ", parts) + " }";
    }

    private string RenderClosure(ClosureExpr cl, int indent)
    {
        var (names, stmts) = Prepare(cl.Proto, null);
        var sub = new LuaEmitter(_dict) { _names = names };
        sub.EmitStmts(stmts, indent + 1);
        return $"function({ParamList(cl.Proto, names)})\n{sub._sb}{Pad(indent)}end";
    }

    private static string RenderNumber(NumberExpr n)
    {
        if (n.IsInteger) return ((long)n.Value).ToString(CultureInfo.InvariantCulture);
        return ((float)n.Value).ToString("R", CultureInfo.InvariantCulture);
    }

    // ---- name resolution ---------------------------------------------------

    /// <summary>Resolve a declared <c>var{slot}</c> name through the current function's name map.</summary>
    private string MapName(string declared) =>
        declared.Length > 3 && declared.StartsWith("var", StringComparison.Ordinal)
        && int.TryParse(declared.AsSpan(3), out int slot) && _names.TryGetValue(slot, out var mapped)
            ? mapped : declared;

    private string ParamList(Prototype p, Dictionary<int, string> names)
    {
        var ps = new List<string>();
        for (int i = 0; i < p.NumParams; i++) ps.Add(names.GetValueOrDefault(i, "var" + i));
        if (p.IsVararg) ps.Add("...");
        return string.Join(", ", ps);
    }

    private static string Paren(string text, int prec, int parentPrec) =>
        prec < parentPrec ? $"({text})" : text;

    private static bool IsIdentifier(string s) =>
        s.Length > 0 && (char.IsLetter(s[0]) || s[0] == '_') && s.All(c => char.IsLetterOrDigit(c) || c == '_')
        && !IsKeyword(s);

    private static bool IsKeyword(string s) => s is "and" or "break" or "do" or "else" or "elseif" or "end"
        or "for" or "function" or "if" or "in" or "local" or "nil" or "not" or "or" or "repeat" or "return"
        or "then" or "until" or "while";

    private static string Pad(int indent) => new(' ', indent * 2);
}
