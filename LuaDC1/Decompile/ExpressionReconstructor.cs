using LuaDC1.Format;

namespace LuaDC1.Decompile;

/// <summary>
/// Simulates the Lua 4.0 operand stack to turn a function's linear instruction stream into a
/// sequence of statements and expression trees. The governing invariant is that a function's
/// locals occupy the contiguous bottom of the stack, with temporaries above; therefore any
/// instruction that names a stack slot as a base (CALL A, RETURN U, GETLOCAL L, SETTABLE A …)
/// proves that everything below that slot is a local, which is how <see cref="EnsureLocals"/>
/// recovers <c>local</c> declarations even though Lua 4.0 emits no opcode for them.
/// </summary>
public sealed class ExpressionReconstructor
{
    private const int MultRet = 255;

    private readonly Prototype _p;
    private readonly List<Expr> _stack = new();
    private readonly List<LinearItem> _items = new();
    private readonly Stack<Merge> _pendingMerges = new();
    private readonly Stack<int> _forBases = new();
    private readonly Dictionary<CallExpr, List<(int Index, Expr Target)>> _pendingStores = new();
    private readonly Dictionary<int, Expr> _slotValues = new();   // local slot -> its defining value (for naming)
    private readonly Dictionary<int, int> _slotPc = new();        // stack index -> pc its current value was pushed
    private int _localBase;
    private int _pc;
    private bool _hadControlFlow;

    private ExpressionReconstructor(Prototype p) => _p = p;

    public static ReconstructResult Reconstruct(Prototype p) => new ExpressionReconstructor(p).Run();

    private ReconstructResult Run()
    {
        // Parameters are the first locals; pre-seed them so GETLOCAL on a param resolves.
        for (int i = 0; i < _p.NumParams; i++)
            _stack.Add(new LocalExpr(i, Name(i)));
        _localBase = _p.NumParams;

        for (_pc = 0; _pc < _p.Code.Length; _pc++)
            Step(_p.Code[_pc]);

        // Locals are emitted at their creation pc, which can be earlier than the flush point that
        // produced them; a stable sort restores the pc order the structurer relies on.
        var ordered = _items.OrderBy(it => it.Pc).ToList();
        return new ReconstructResult(ordered, _hadControlFlow, _p.NumParams, _slotValues);
    }

    private void Step(Instruction ins)
    {
        // Resolve any short-circuit and/or whose merge point we've now reached.
        while (_pendingMerges.Count > 0 && _pendingMerges.Peek().MergePc == _pc)
        {
            var m = _pendingMerges.Pop();
            var right = Pop();
            Push(new BinaryExpr(m.Op, m.Left, right, m.Prec, false));
        }

        switch (ins.Op)
        {
            case OpCode.PushNil: for (int i = 0; i < ins.U; i++) Push(NilExpr.Instance); break;
            case OpCode.PushInt: Push(new NumberExpr(ins.S, true)); break;
            case OpCode.PushNum: Push(new NumberExpr(Num(ins.U), false)); break;
            case OpCode.PushNegNum: Push(new NumberExpr(-Num(ins.U), false)); break;
            case OpCode.PushString: Push(new StringExpr(Str(ins.U))); break;
            case OpCode.PushUpValue: Push(new UpvalExpr(ins.U)); break;

            case OpCode.GetLocal: Push(Local(ins.U)); break;
            case OpCode.GetGlobal: Push(new GlobalExpr(Str(ins.U))); break;
            case OpCode.GetTable: { var k = Pop(); var t = Pop(); Push(new IndexExpr(t, k, false)); break; }
            case OpCode.GetDotted: { var t = Pop(); Push(new IndexExpr(t, new StringExpr(Str(ins.U)), true)); break; }
            case OpCode.GetIndexed: { var key = Local(ins.U); var t = Pop(); Push(new IndexExpr(t, key, false)); break; }
            case OpCode.PushSelf: { var t = Pop(); Push(new MethodMarkerExpr(t, Str(ins.U))); Push(t); break; }

            case OpCode.CreateTable: Push(new TableExpr(new List<Expr>(), new List<KeyVal>())); break;
            case OpCode.SetList: DoSetList(ins); break;
            case OpCode.SetMap: DoSetMap(ins); break;
            case OpCode.SetTable: DoSetTable(ins); break;
            case OpCode.SetLocal: DoSetLocal(ins); break;
            case OpCode.SetGlobal: DoSetGlobal(ins); break;

            case OpCode.Add: Binary("+", Precedences.AddSub, false); break;
            case OpCode.Sub: Binary("-", Precedences.AddSub, false); break;
            case OpCode.Mult: Binary("*", Precedences.MulDiv, false); break;
            case OpCode.Div: Binary("/", Precedences.MulDiv, false); break;
            case OpCode.Pow: Binary("^", Precedences.Pow, true); break;
            case OpCode.AddI: { var x = Pop(); Push(new BinaryExpr("+", x, new NumberExpr(ins.S, true), Precedences.AddSub, false)); break; }
            case OpCode.Minus: { var x = Pop(); Push(new UnaryExpr("-", x)); break; }
            case OpCode.Not: { var x = Pop(); Push(new UnaryExpr("not", x)); break; }
            case OpCode.Concat: { var parts = PopN(ins.U); Push(new ConcatExpr(parts)); break; }

            case OpCode.Call: DoCall(ins); break;
            case OpCode.TailCall: DoTailCall(ins); break;
            case OpCode.Return: DoReturn(ins); break;
            case OpCode.Pop: DoPop(ins); break;
            case OpCode.Closure: { var up = PopN(ins.B); Push(new ClosureExpr(_p.Protos[ins.A], up)); break; }

            case OpCode.End: EnsureLocals(_stack.Count); break;

            // Short-circuit and/or in a VALUE context leave their result on the stack.
            case OpCode.JmpOnT: ShortCircuit(ins, "or", Precedences.Or); break;
            case OpCode.JmpOnF: ShortCircuit(ins, "and", Precedences.And); break;
            // Real branches → control items for the structurer.
            case OpCode.Jmp: Control(ins, 0); break;
            case OpCode.JmpF or OpCode.JmpT: Control(ins, 1); break;
            case OpCode.JmpEQ or OpCode.JmpNE or OpCode.JmpLT or OpCode.JmpLE or OpCode.JmpGT or OpCode.JmpGE:
                // A comparison immediately followed by PUSHNILJMP is a boolean materialized as a
                // value (local x = a < b), not a branch. Otherwise it's a real conditional.
                if (_pc + 1 < _p.Code.Length && _p.Code[_pc + 1].Op == OpCode.PushNilJmp)
                    MaterializeCompare(ins);
                else
                    Control(ins, 2);
                break;
            case OpCode.PushNilJmp: Push(NilExpr.Instance); _pc++; break; // ternary idiom: push nil, skip next
            case OpCode.ForPrep: ForPrep(ins); break;
            case OpCode.LForPrep: LForPrep(ins); break;
            case OpCode.ForLoop or OpCode.LForLoop: ForLoopEnd(ins); break;

            default: throw new NotSupportedException($"opcode {Ops.Name(ins.Op)} not handled");
        }
    }

    // ---- statement builders -----------------------------------------------

    private void DoCall(Instruction ins)
    {
        int a = ins.A, b = ins.B, h = _stack.Count;
        var callee = _stack[a];
        CallExpr call = BuildCall(callee, a, h, b);
        RemoveFrom(a);

        if (b == 0)
        {
            EnsureLocals(a);
            Emit(new CallStmt(call));
        }
        else if (b == MultRet)
        {
            Push(call);
        }
        else
        {
            Push(call);
            for (int i = 1; i < b; i++) Push(new CallResultExpr(call, i));
        }
    }

    private void DoTailCall(Instruction ins)
    {
        int a = ins.A, h = _stack.Count;
        var call = BuildCall(_stack[a], a, h, MultRet);
        RemoveFrom(a);
        EnsureLocals(a);
        Emit(new ReturnStmt(new Expr[] { call }));
    }

    private CallExpr BuildCall(Expr callee, int a, int h, int b)
    {
        bool multi = b == 0 || b == MultRet;
        if (callee is MethodMarkerExpr mm)
        {
            // slot a+1 is the implicit self; real args start at a+2.
            var margs = _stack.GetRange(a + 2, h - (a + 2));
            return new CallExpr(mm.Receiver, margs, true, mm.Method, multi);
        }
        var args = _stack.GetRange(a + 1, h - (a + 1));
        return new CallExpr(callee, args, false, null, multi);
    }

    private void DoReturn(Instruction ins)
    {
        int u = ins.U;
        EnsureLocals(u);
        var vals = _stack.GetRange(u, _stack.Count - u);
        RemoveFrom(u);
        Emit(new ReturnStmt(vals));
    }

    private void DoPop(Instruction ins)
    {
        // Discard u temporaries; a discarded call result is actually a statement call.
        for (int i = 0; i < ins.U; i++)
        {
            var e = Pop();
            if (e is CallExpr c) Emit(new CallStmt(c));
        }
        // POP can also close a block, removing block-scoped locals; lower the base so the compiler's
        // reuse of those stack slots is recovered as fresh local declarations rather than globals.
        if (_stack.Count < _localBase) _localBase = _stack.Count;
    }

    private void DoSetLocal(Instruction ins)
    {
        var v = Pop();
        if (TryDeferMultiStore(Local(ins.U), v)) return;
        EnsureLocals(_stack.Count);
        Emit(new AssignStmt(new Expr[] { Local(ins.U) }, new[] { v }));
    }

    private void DoSetGlobal(Instruction ins)
    {
        var v = Pop();
        var target = new GlobalExpr(Str(ins.U));
        if (v is ClosureExpr cl) { EnsureLocals(_stack.Count); Emit(new FunctionStmt(target, cl, false)); return; }
        if (TryDeferMultiStore(target, v)) return;
        EnsureLocals(_stack.Count);
        Emit(new AssignStmt(new Expr[] { target }, new[] { v }));
    }

    /// <summary>
    /// Handle <c>a, b, c = f()</c>: the SETLOCAL/SETGLOBAL stores pop the call's results in reverse,
    /// so buffer them and emit one multi-target assignment when the base call value is stored.
    /// Returns true if the store was consumed (deferred or finalized) here.
    /// </summary>
    private bool TryDeferMultiStore(Expr target, Expr value)
    {
        if (value is CallResultExpr cr)
        {
            if (!_pendingStores.TryGetValue(cr.Call, out var list))
                _pendingStores[cr.Call] = list = new List<(int, Expr)>();
            list.Add((cr.Index, target));
            return true;
        }
        if (value is CallExpr call && _pendingStores.TryGetValue(call, out var pending))
        {
            pending.Add((0, target));
            var targets = pending.OrderBy(t => t.Item1).Select(t => t.Item2).ToList();
            _pendingStores.Remove(call);
            EnsureLocals(_stack.Count);
            Emit(new AssignStmt(targets, new Expr[] { call }));
            return true;
        }
        return false;
    }

    private void DoSetTable(Instruction ins)
    {
        int a = ins.A, b = ins.B, h = _stack.Count;
        var table = _stack[h - a];
        var key = _stack[h - a + 1];
        var value = _stack[h - 1];
        EnsureLocals(h - a);
        RemoveTop(b);
        Emit(new AssignStmt(new Expr[] { new IndexExpr(table, key, IsIdentifier(key)) }, new[] { value }));
    }

    private void DoSetList(Instruction ins)
    {
        var elems = PopN(ins.B);
        if (Peek() is TableExpr t) t.ArrayPart.AddRange(elems);
    }

    private void DoSetMap(Instruction ins)
    {
        var pairs = PopN(ins.U * 2);
        if (Peek() is TableExpr t)
            for (int i = 0; i < ins.U; i++)
                t.HashPart.Add(new KeyVal(pairs[2 * i], pairs[2 * i + 1]));
    }

    private void Binary(string op, int prec, bool rightAssoc)
    {
        var r = Pop();
        var l = Pop();
        Push(new BinaryExpr(op, l, r, prec, rightAssoc));
    }

    private void Control(Instruction ins, int popCount)
    {
        _hadControlFlow = true;
        var ops = popCount == 0 ? Array.Empty<Expr>() : PopN(popCount).ToArray();
        _items.Add(new ControlItem(_pc, ins, ins.JumpTarget(_pc), ops));
    }

    private void ShortCircuit(Instruction ins, string op, int prec)
    {
        var left = Pop();
        _pendingMerges.Push(new Merge(ins.JumpTarget(_pc), op, prec, left));
    }

    /// <summary>
    /// Build a comparison value from the materialize-boolean idiom
    /// (<c>JMP&lt;cmp&gt; -&gt; L; PUSHNILJMP; L: PUSH&lt;true&gt;</c>) and skip its two scaffolding
    /// instructions, leaving the comparison expression on the stack.
    /// </summary>
    private void MaterializeCompare(Instruction ins)
    {
        var right = Pop();
        var left = Pop();
        Push(new BinaryExpr(CompareOp(ins.Op), left, right, Precedences.Compare, false));
        _pc = ins.JumpTarget(_pc);   // jump over PUSHNILJMP and the pushed `true` value
    }

    private static string CompareOp(OpCode op) => op switch
    {
        OpCode.JmpEQ => "==", OpCode.JmpNE => "~=", OpCode.JmpLT => "<",
        OpCode.JmpLE => "<=", OpCode.JmpGT => ">", OpCode.JmpGE => ">=",
        _ => "?",
    };

    private void ForPrep(Instruction ins)
    {
        _hadControlFlow = true;
        int baseSlot = _stack.Count - 3;           // init, limit, step
        var init = _stack[baseSlot];
        var limit = _stack[baseSlot + 1];
        var step = _stack[baseSlot + 2];
        EnsureLocals(baseSlot);                    // settle real locals below the loop controls
        // The loop variable lives at baseSlot; limit/step occupy the next two hidden slots. Treat
        // all three as "locals" so EnsureLocals won't redeclare them, and expose the loop var.
        _stack[baseSlot] = new LocalExpr(baseSlot, Name(baseSlot));
        _localBase = baseSlot + 3;
        _forBases.Push(baseSlot);
        _items.Add(new ControlItem(_pc, ins, ins.JumpTarget(_pc), new[] { init, limit, step }, baseSlot));
    }

    private void LForPrep(Instruction ins)
    {
        _hadControlFlow = true;
        int baseSlot = _stack.Count - 1;           // the table being iterated
        var table = _stack[baseSlot];
        EnsureLocals(baseSlot);
        // Lua 4.0 keeps an internal control + key + value; expose key/value as the loop locals.
        _stack[baseSlot] = new LocalExpr(baseSlot, "_for");
        while (_stack.Count < baseSlot + 3) _stack.Add(NilExpr.Instance);
        _stack[baseSlot + 1] = new LocalExpr(baseSlot + 1, Name(baseSlot + 1));
        _stack[baseSlot + 2] = new LocalExpr(baseSlot + 2, Name(baseSlot + 2));
        _localBase = baseSlot + 3;
        _forBases.Push(baseSlot);
        _items.Add(new ControlItem(_pc, ins, ins.JumpTarget(_pc), new[] { table }, baseSlot + 1));
    }

    private void ForLoopEnd(Instruction ins)
    {
        _hadControlFlow = true;
        int baseSlot = _forBases.Count > 0 ? _forBases.Pop() : _stack.Count;
        if (baseSlot < _stack.Count) RemoveFrom(baseSlot);   // drop loop controls + body locals
        _localBase = Math.Min(_localBase, baseSlot);
        _items.Add(new ControlItem(_pc, ins, ins.JumpTarget(_pc), Array.Empty<Expr>(), baseSlot));
    }

    // ---- local recovery ----------------------------------------------------

    /// <summary>
    /// Declare stack slots <c>[_localBase, level)</c> as locals — they're below a freshly
    /// revealed frame base, so they must be locals rather than temporaries. Consecutive results
    /// of one multi-result call are coalesced into a single <c>local a, b = f()</c>.
    /// </summary>
    private void EnsureLocals(int level)
    {
        while (_localBase < level)
        {
            int slot = _localBase;
            var e = _stack[slot];

            if (e is CallExpr call)
            {
                // Coalesce a multi-result call (local a, b = f()) into one declaration. The extra
                // result slots are always locals, so extend through them even past `level`.
                var names = new List<string> { Name(slot) };
                int next = slot + 1;
                while (next < _stack.Count && _stack[next] is CallResultExpr cr && ReferenceEquals(cr.Call, call))
                {
                    names.Add(Name(next));
                    next++;
                }
                int groupPc = _slotPc.GetValueOrDefault(slot, _pc);
                for (int s = slot; s < next; s++) { _stack[s] = new LocalExpr(s, Name(s)); _slotValues[s] = call; }
                _items.Add(new StmtItem(groupPc, new LocalStmt(names, new Expr[] { call })));
                _localBase = next;
                continue;
            }

            _slotValues[slot] = e;
            _stack[slot] = new LocalExpr(slot, Name(slot));
            // Declare at the pc the value was created, not the (possibly much later) flush point, so
            // the local lands in the right scope instead of inside whatever branch triggered the flush.
            _items.Add(new StmtItem(_slotPc.GetValueOrDefault(slot, _pc), new LocalStmt(new[] { Name(slot) }, new[] { e })));
            _localBase++;
        }
    }

    // ---- stack + helpers ---------------------------------------------------

    private void Emit(Stmt s) => _items.Add(new StmtItem(_pc, s));

    private void Push(Expr e)
    {
        _slotPc[_stack.Count] = _pc;   // remember where this value (and thus a local at this slot) was created
        _stack.Add(e);
    }
    private Expr Peek() => _stack[^1];

    private Expr Pop()
    {
        var e = _stack[^1];
        _stack.RemoveAt(_stack.Count - 1);
        return e;
    }

    private List<Expr> PopN(int n)
    {
        var r = _stack.GetRange(_stack.Count - n, n);
        _stack.RemoveRange(_stack.Count - n, n);
        return r;
    }

    private void RemoveFrom(int index) => _stack.RemoveRange(index, _stack.Count - index);
    private void RemoveTop(int n) => _stack.RemoveRange(_stack.Count - n, n);

    private Expr Local(int slot)
    {
        if (slot >= _localBase) EnsureLocals(Math.Min(slot + 1, _stack.Count));
        return new LocalExpr(slot, Name(slot));
    }

    private string Name(int slot)
    {
        // Use the real debug name when present, but skip Lua's internal placeholder locals -- the
        // for-loop control variables it names "(for limit)", "(table)", etc. -- which aren't valid
        // identifiers and don't denote a real source local.
        if (slot < _p.Locals.Length && IsValidName(_p.Locals[slot].Name))
            return _p.Locals[slot].Name;
        return "var" + slot;
    }

    private static bool IsValidName(string? n) =>
        !string.IsNullOrEmpty(n) && (char.IsLetter(n[0]) || n[0] == '_') && n.All(c => char.IsLetterOrDigit(c) || c == '_');

    private string Str(int i) => i >= 0 && i < _p.Strings.Length ? _p.Strings[i] : "?str" + i;
    private double Num(int i) => i >= 0 && i < _p.Numbers.Length ? _p.Numbers[i] : double.NaN;

    private static bool IsIdentifier(Expr key) =>
        key is StringExpr s && s.Value.Length > 0 &&
        (char.IsLetter(s.Value[0]) || s.Value[0] == '_') &&
        s.Value.All(c => char.IsLetterOrDigit(c) || c == '_');

    // ---- transient stack markers (never emitted directly) ------------------

    private sealed record MethodMarkerExpr(Expr Receiver, string Method) : Expr;
    private sealed record CallResultExpr(CallExpr Call, int Index) : Expr;

    /// <summary>A deferred short-circuit and/or: combine <see cref="Left"/> with the value present
    /// at <see cref="MergePc"/>.</summary>
    private readonly record struct Merge(int MergePc, string Op, int Prec, Expr Left);
}
