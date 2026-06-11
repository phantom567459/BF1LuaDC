using LuaDC1.Format;

namespace LuaDC1.Decompile;

/// <summary>
/// An expression tree node produced by the stack simulator. <see cref="Precedence"/> drives
/// parenthesization in the emitter. Lua 4.0 has no boolean type — truth is "not nil" — so the
/// only literals are nil, numbers and strings.
/// </summary>
public abstract record Expr
{
    /// <summary>Binding tightness; higher binds tighter. Primary/atomic = 99.</summary>
    public virtual int Precedence => 99;
}

public sealed record NilExpr : Expr
{
    public static readonly NilExpr Instance = new();
}

/// <summary>A numeric literal. <see cref="IsInteger"/> distinguishes PUSHINT from PUSHNUM.</summary>
public sealed record NumberExpr(double Value, bool IsInteger) : Expr;

public sealed record StringExpr(string Value) : Expr;

/// <summary>A local variable (or parameter), identified by its stack slot.</summary>
public sealed record LocalExpr(int Slot, string Name) : Expr;

public sealed record GlobalExpr(string Name) : Expr;

public sealed record UpvalExpr(int Index) : Expr;

public sealed record VarargExpr : Expr
{
    public static readonly VarargExpr Instance = new();
}

/// <summary>Table access: <c>Target.Field</c> when dotted, else <c>Target[Key]</c>.</summary>
public sealed record IndexExpr(Expr Target, Expr Key, bool Dotted) : Expr;

/// <summary>
/// A function call. <see cref="IsMethod"/> renders <c>recv:Name(args)</c> (from PUSHSELF).
/// <see cref="Multi"/> marks a call whose results spill (CALL with B==0) so the emitter does
/// not wrap it in parentheses when it is the last item of a call/return/assignment.
/// </summary>
public sealed record CallExpr(Expr Callee, IReadOnlyList<Expr> Args, bool IsMethod, string? MethodName, bool Multi) : Expr;

public sealed record BinaryExpr(string Op, Expr Left, Expr Right, int Prec, bool RightAssoc) : Expr
{
    public override int Precedence => Prec;
}

public sealed record UnaryExpr(string Op, Expr Operand) : Expr
{
    public override int Precedence => Precedences.Unary;
}

/// <summary>A run of values joined by <c>..</c> (right-associative).</summary>
public sealed record ConcatExpr(IReadOnlyList<Expr> Parts) : Expr
{
    public override int Precedence => Precedences.Concat;
}

/// <summary>A table constructor mixing an array part and a keyed part: <c>{a, b, k = v}</c>.</summary>
public sealed record TableExpr(List<Expr> ArrayPart, List<KeyVal> HashPart) : Expr;

public sealed record KeyVal(Expr Key, Expr Value);

/// <summary>An (anonymous) function value; named functions are emitted via a statement.</summary>
public sealed record ClosureExpr(Prototype Proto, IReadOnlyList<Expr> UpValues) : Expr;

/// <summary>Fallback wrapper carrying verbatim text when an expression can't be reconstructed.</summary>
public sealed record RawExpr(string Text) : Expr;

/// <summary>Lua 4.0 operator precedence levels (low binds loosest).</summary>
public static class Precedences
{
    public const int Or = 1;
    public const int And = 2;
    public const int Compare = 3;
    public const int Concat = 4;
    public const int AddSub = 5;
    public const int MulDiv = 6;
    public const int Unary = 7;
    public const int Pow = 8;
}
