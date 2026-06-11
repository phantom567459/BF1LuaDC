namespace LuaDC1.Decompile;

/// <summary>A reconstructed statement. Control-flow statements are populated in Stage 3.</summary>
public abstract record Stmt;

/// <summary>One or more <c>local</c> declarations: <c>local a, b = x, y</c>.</summary>
public sealed record LocalStmt(IReadOnlyList<string> Names, IReadOnlyList<Expr> Values) : Stmt;

/// <summary>Assignment to existing targets (locals, globals, table fields): <c>a, t.x = e, f</c>.</summary>
public sealed record AssignStmt(IReadOnlyList<Expr> Targets, IReadOnlyList<Expr> Values) : Stmt;

/// <summary>A call evaluated for its side effects.</summary>
public sealed record CallStmt(CallExpr Call) : Stmt;

public sealed record ReturnStmt(IReadOnlyList<Expr> Values) : Stmt;

/// <summary>A named function definition: <c>[local] function Name(params) … end</c>.</summary>
public sealed record FunctionStmt(Expr Target, ClosureExpr Closure, bool IsLocal) : Stmt;

// ---- control flow (Stage 3) ----------------------------------------------

public sealed record IfStmt(IReadOnlyList<IfClause> Clauses, IReadOnlyList<Stmt>? ElseBody) : Stmt;
public sealed record IfClause(Expr Condition, IReadOnlyList<Stmt> Body);

public sealed record NumericForStmt(string Var, Expr Start, Expr Limit, Expr? Step, IReadOnlyList<Stmt> Body) : Stmt;

public sealed record GenericForStmt(IReadOnlyList<string> Vars, Expr Table, IReadOnlyList<Stmt> Body) : Stmt;

public sealed record WhileStmt(Expr Condition, IReadOnlyList<Stmt> Body) : Stmt;

public sealed record RepeatStmt(IReadOnlyList<Stmt> Body, Expr Condition) : Stmt;

public sealed record BreakStmt : Stmt;

/// <summary>
/// Fallback: a region the structurer couldn't fold. Emitted as a clearly-marked comment block
/// listing the raw ops so accuracy gaps are visible (never silently dropped).
/// </summary>
public sealed record RawBlockStmt(IReadOnlyList<string> Lines) : Stmt;
