using LuaDC1.Format;

namespace LuaDC1.Decompile;

/// <summary>
/// The reconstructor's per-pc output. A single linear pass over a function's code yields a
/// sequence of these: completed <see cref="StmtItem"/>s and <see cref="ControlItem"/>s for the
/// jump/loop opcodes (carrying the operands popped at the branch). The control-flow structurer
/// (Stage 3) folds the control items into nested statements; the Stage-2 structurer renders any
/// control item as a raw comment.
/// </summary>
public abstract record LinearItem(int Pc);

public sealed record StmtItem(int Pc, Stmt Stmt) : LinearItem(Pc);

/// <summary>
/// A control-transfer instruction with the expression operands it consumed. For comparison
/// jumps <see cref="Operands"/> holds [left, right]; for JMPF/JMPT it holds the single tested
/// value; for FORPREP it holds [init, limit, step]; for LFORPREP it holds [table]; JMP/FORLOOP
/// carry none. <see cref="LoopVarSlot"/> is the loop variable's stack slot for FORPREP/LFORPREP.
/// </summary>
public sealed record ControlItem(int Pc, Instruction Ins, int Target, IReadOnlyList<Expr> Operands, int LoopVarSlot = -1)
    : LinearItem(Pc);

public sealed record ReconstructResult(
    IReadOnlyList<LinearItem> Items,
    bool HadControlFlow,
    int NumParams,
    IReadOnlyDictionary<int, Expr> SlotValues);
