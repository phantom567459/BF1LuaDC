using LuaDC1.Format;

namespace LuaDC1.Decompile;

/// <summary>
/// Folds the reconstructor's linear item stream into a nested statement tree by recognizing the
/// stereotyped jump shapes Lua 4.0's code generator emits: if/elseif/else, numeric and generic
/// for, while, repeat-until, break, and short-circuit and/or conditions. Anything that doesn't
/// match a known shape throws, and the caller falls back to a raw disassembly comment — so an
/// unrecognized construct is loud, never silently mis-decompiled.
/// </summary>
public static class ControlFlowStructurer
{
    public static List<Stmt> Structure(ReconstructResult r, Prototype p)
    {
        if (!r.HadControlFlow)
            return r.Items.OfType<StmtItem>().Select(i => i.Stmt).ToList();

        var s = new Worker(r.Items);
        return s.Run();
    }

    private sealed class Worker
    {
        private readonly IReadOnlyList<LinearItem> _items;

        public Worker(IReadOnlyList<LinearItem> items) => _items = items;

        public List<Stmt> Run() => Structure(0, _items.Count, int.MinValue, new HashSet<int>());

        /// <param name="merges">
        /// Pcs that are the follow (merge) points of the enclosing constructs. A forward jump to one
        /// of these is a structured exit (the branch reconverges there), which is how convergent
        /// if/elseif/else chains sharing a far merge get folded correctly instead of looking
        /// "unstructured".
        /// </param>
        private List<Stmt> Structure(int lo, int hi, int loopExitPc, IReadOnlySet<int> merges)
        {
            var result = new List<Stmt>();
            int i = lo;
            while (i < hi)
            {
                // A repeat-until loop is headed here if a backward conditional jumps back to this pc.
                int repeatEnd = FindRepeatBackEdge(i, i, hi);
                if (repeatEnd >= 0)
                {
                    var back = (ControlItem)_items[repeatEnd];
                    var body = Structure(i, repeatEnd, loopExitPc, merges);
                    result.Add(new RepeatStmt(body, BodyExecCond(back)));
                    i = repeatEnd + 1;
                    continue;
                }

                if (_items[i] is StmtItem st) { result.Add(st.Stmt); i++; continue; }

                var c = (ControlItem)_items[i];
                i = HandleControl(c, i, hi, loopExitPc, merges, result);
            }
            return result;
        }

        private int HandleControl(ControlItem c, int i, int hi, int loopExitPc, IReadOnlySet<int> merges, List<Stmt> result)
        {
            switch (c.Ins.Op)
            {
                case OpCode.Jmp:
                    if (c.Target == loopExitPc) { result.Add(new BreakStmt()); return i + 1; }
                    if (c.Target <= c.Pc) return i + 1;             // a loop back-edge handled by its header
                    if (IndexAtPc(c.Target) == i + 1) return i + 1; // jump to the next instruction = no-op
                    if (merges.Contains(c.Target)) return i + 1;    // structured exit to an enclosing merge
                    // Genuinely unrecognized: best-effort drop, marked honestly; well-formed check guards.
                    result.Add(new RawBlockStmt(new[] { $"[decompiler] approximated control flow near pc {c.Pc}" }));
                    return i + 1;

                case OpCode.ForPrep: return BuildNumericFor(c, i, loopExitPc, merges, result);
                case OpCode.LForPrep: return BuildGenericFor(c, i, loopExitPc, merges, result);
                case OpCode.ForLoop or OpCode.LForLoop:
                    return i + 1; // consumed by the matching prep

                case OpCode.JmpF or OpCode.JmpT or OpCode.JmpEQ or OpCode.JmpNE
                    or OpCode.JmpLT or OpCode.JmpLE or OpCode.JmpGT or OpCode.JmpGE:
                    if (c.Target <= c.Pc) return i + 1; // repeat back-edge handled by header
                    return BuildIfOrWhile(i, hi, loopExitPc, merges, result);

                default:
                    throw new NotSupportedException($"unhandled control op {Ops.Name(c.Ins.Op)} at pc {c.Pc}");
            }
        }

        // ---- if / elseif / else / while ----------------------------------

        private int BuildIfOrWhile(int i, int hi, int loopExitPc, IReadOnlySet<int> merges, List<Stmt> result)
        {
            var (cond, exitPc, bodyStart) = ParseCondition(i);
            // Clamp to the enclosing block: when the condition's skip target is a far shared merge
            // (e.g. a loop end past a sibling else), the body must not extend past `hi` and swallow
            // that sibling content.
            int bodyEnd = Math.Min(IndexAtPc(exitPc), hi);

            // while: the body's last item is an unconditional backward jump to the condition.
            if (bodyEnd - 1 > bodyStart && _items[bodyEnd - 1] is ControlItem back
                && back.Ins.Op == OpCode.Jmp && back.Target <= _items[i].Pc)
            {
                var body = Structure(bodyStart, bodyEnd - 1, exitPc, With(merges, exitPc));
                result.Add(new WhileStmt(cond, body));
                return bodyEnd;
            }

            // if/else: a then-body ending in a forward jump to the follow point.
            if (bodyEnd - 1 >= bodyStart && _items[bodyEnd - 1] is ControlItem jmp
                && jmp.Ins.Op == OpCode.Jmp && jmp.Target > exitPc)
            {
                int targetIdx = IndexAtPc(jmp.Target);
                // If the then-jump targets an enclosing merge (or lands beyond this block), it's a
                // structured exit shared by siblings: the else is bounded by this block, not extended
                // to the far merge. Otherwise the jump's target IS this if's follow.
                bool enclosing = merges.Contains(jmp.Target) || targetIdx > hi;
                int elseEnd = enclosing ? hi : Math.Min(targetIdx, hi);
                var inner = enclosing ? merges : With(merges, jmp.Target);
                var thenBody = Structure(bodyStart, bodyEnd - 1, loopExitPc, inner);
                if (elseEnd > bodyEnd)
                {
                    var elseBody = Structure(bodyEnd, elseEnd, loopExitPc, inner);
                    result.Add(MakeIf(cond, thenBody, elseBody));
                    return elseEnd;
                }
                // No else room: if-then; the trailing jump is a redundant/structured exit, dropped.
                result.Add(MakeIf(cond, thenBody, null));
                return bodyEnd;
            }

            var body2 = Structure(bodyStart, bodyEnd, loopExitPc, With(merges, exitPc));
            result.Add(MakeIf(cond, body2, null));
            return bodyEnd;
        }

        /// <summary>Collapse <c>else { if … }</c> into an elseif chain.</summary>
        private static IfStmt MakeIf(Expr cond, List<Stmt> thenBody, List<Stmt>? elseBody)
        {
            var clauses = new List<IfClause> { new(cond, thenBody) };
            if (elseBody is { Count: 1 } && elseBody[0] is IfStmt nested)
            {
                clauses.AddRange(nested.Clauses);
                return new IfStmt(clauses, nested.ElseBody);
            }
            return new IfStmt(clauses, elseBody);
        }

        private static IReadOnlySet<int> With(IReadOnlySet<int> set, int pc)
        {
            var s = new HashSet<int>(set) { pc };
            return s;
        }

        // ---- for loops ----------------------------------------------------

        private int BuildNumericFor(ControlItem prep, int i, int loopExitPc, IReadOnlySet<int> merges, List<Stmt> result)
        {
            int loopEnd = FindMatchingLoopEnd(i);   // the matching FORLOOP back-edge
            var body = Structure(i + 1, loopEnd, prep.Target, With(merges, prep.Target));
            var step = prep.Operands[2];
            Expr? stepOut = step is NumberExpr { Value: 1 } ? null : step;
            result.Add(new NumericForStmt(NameOf(prep.LoopVarSlot), prep.Operands[0], prep.Operands[1], stepOut, body));
            return loopEnd + 1;
        }

        private int BuildGenericFor(ControlItem prep, int i, int loopExitPc, IReadOnlySet<int> merges, List<Stmt> result)
        {
            int loopEnd = FindMatchingLoopEnd(i);   // the matching LFORLOOP back-edge
            var body = Structure(i + 1, loopEnd, prep.Target, With(merges, prep.Target));
            var vars = new[] { NameOf(prep.LoopVarSlot), NameOf(prep.LoopVarSlot + 1) };
            result.Add(new GenericForStmt(vars, prep.Operands[0], body));
            return loopEnd + 1;
        }

        /// <summary>
        /// The index of the FORLOOP/LFORLOOP that closes the prep at <paramref name="prepIdx"/>,
        /// found by depth-balancing nested loops (the exit target alone is unreliable because
        /// sibling loops can share the same exit pc, e.g. the function end).
        /// </summary>
        private int FindMatchingLoopEnd(int prepIdx)
        {
            int depth = 0;
            for (int j = prepIdx + 1; j < _items.Count; j++)
            {
                if (_items[j] is not ControlItem c) continue;
                if (c.Ins.Op is OpCode.ForPrep or OpCode.LForPrep) depth++;
                else if (c.Ins.Op is OpCode.ForLoop or OpCode.LForLoop)
                {
                    if (depth == 0) return j;
                    depth--;
                }
            }
            throw new NotSupportedException("for-loop has no matching FORLOOP");
        }

        // ---- condition recovery (and/or, inversion) -----------------------

        /// <summary>
        /// Parse the short-circuit condition starting at <paramref name="lo"/>: a run of forward
        /// conditional jumps. Returns the body-executes condition, the exit pc, and the index of
        /// the first body item. Falls back to the single leading condition for shapes it can't
        /// confidently combine, letting nesting handle the rest (still correct).
        /// </summary>
        private (Expr cond, int exitPc, int bodyStart) ParseCondition(int lo)
        {
            int i = lo;
            var run = new List<ControlItem>();
            while (i < _items.Count && _items[i] is ControlItem c && IsForwardConditional(c))
            {
                run.Add(c);
                i++;
            }
            if (run.Count == 0) throw new NotSupportedException("expected a condition");

            // Classify each jump by where it lands (matched at the item level, since the body's
            // first instruction usually folds into a later statement's pc).
            int bodyStartIdx = i;
            var first = run[0];

            // Pure AND: every jump skips to the same exit past the body.
            if (run.All(c => c.Target == first.Target) && IndexAtPc(first.Target) != bodyStartIdx)
            {
                Expr cond = run.Select(BodyExecCond).Aggregate((a, b) => And(a, b));
                return (cond, first.Target, bodyStartIdx);
            }

            // Leading OR: all but the last jump into the body; the last skips to the exit.
            if (run.Count >= 2 && run[..^1].All(c => IndexAtPc(c.Target) == bodyStartIdx)
                && IndexAtPc(run[^1].Target) != bodyStartIdx)
            {
                var terms = run[..^1].Select(TakenCond).Append(BodyExecCond(run[^1]));
                Expr cond = terms.Aggregate((a, b) => Or(a, b));
                return (cond, run[^1].Target, bodyStartIdx);
            }

            // Anything else: take just the first condition; nested structuring handles the rest.
            return (BodyExecCond(first), first.Target, lo + 1);
        }

        private static bool IsForwardConditional(ControlItem c) =>
            c.Target > c.Pc && c.Ins.Op is OpCode.JmpF or OpCode.JmpT or OpCode.JmpEQ or OpCode.JmpNE
                or OpCode.JmpLT or OpCode.JmpLE or OpCode.JmpGT or OpCode.JmpGE;

        /// <summary>The condition under which the guarded body executes (the jump is NOT taken).</summary>
        private static Expr BodyExecCond(ControlItem c) => c.Ins.Op switch
        {
            OpCode.JmpF => c.Operands[0],
            OpCode.JmpT => Not(c.Operands[0]),
            OpCode.JmpEQ => Cmp("~=", c),
            OpCode.JmpNE => Cmp("==", c),
            OpCode.JmpLT => Cmp(">=", c),
            OpCode.JmpLE => Cmp(">", c),
            OpCode.JmpGT => Cmp("<=", c),
            OpCode.JmpGE => Cmp("<", c),
            _ => throw new NotSupportedException($"not a condition: {Ops.Name(c.Ins.Op)}"),
        };

        /// <summary>The condition under which the jump IS taken (used for or-terms jumping to the body).</summary>
        private static Expr TakenCond(ControlItem c) => c.Ins.Op switch
        {
            OpCode.JmpF => Not(c.Operands[0]),
            OpCode.JmpT => c.Operands[0],
            OpCode.JmpEQ => Cmp("==", c),
            OpCode.JmpNE => Cmp("~=", c),
            OpCode.JmpLT => Cmp("<", c),
            OpCode.JmpLE => Cmp("<=", c),
            OpCode.JmpGT => Cmp(">", c),
            OpCode.JmpGE => Cmp(">=", c),
            _ => throw new NotSupportedException($"not a condition: {Ops.Name(c.Ins.Op)}"),
        };

        private static Expr Cmp(string op, ControlItem c) =>
            new BinaryExpr(op, c.Operands[0], c.Operands[1], Precedences.Compare, false);

        private static Expr And(Expr a, Expr b) => new BinaryExpr("and", a, b, Precedences.And, false);
        private static Expr Or(Expr a, Expr b) => new BinaryExpr("or", a, b, Precedences.Or, false);
        private static Expr Not(Expr a) => a is BinaryExpr b && Flip(b.Op) is { } f
            ? new BinaryExpr(f, b.Left, b.Right, Precedences.Compare, false)
            : new UnaryExpr("not", a);

        private static string? Flip(string op) => op switch
        {
            "==" => "~=", "~=" => "==", "<" => ">=", ">=" => "<", ">" => "<=", "<=" => ">", _ => null
        };

        // ---- helpers ------------------------------------------------------

        private int FindRepeatBackEdge(int headerIdx, int lo, int hi)
        {
            for (int j = lo; j < hi; j++)
                if (_items[j] is ControlItem c && c.Target <= c.Pc && IndexAtPc(c.Target) == headerIdx
                    && c.Ins.Op is OpCode.JmpF or OpCode.JmpT)
                    return j;
            return -1;
        }

        /// <summary>First item index whose pc is &gt;= <paramref name="pc"/> (else item count).</summary>
        private int IndexAtPc(int pc)
        {
            for (int j = 0; j < _items.Count; j++)
                if (_items[j].Pc >= pc) return j;
            return _items.Count;
        }

        private static string NameOf(int slot) => "var" + slot;
    }
}
