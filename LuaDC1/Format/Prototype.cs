namespace LuaDC1.Format;

/// <summary>
/// A decoded Lua 4.0 function prototype (the in-memory IR produced by the bytecode reader).
/// Mirrors the on-disk <c>Proto</c> serialized by lua-4.0.1 ldump.c / read by lundump.c.
/// Nested functions live in <see cref="Protos"/> and are referenced by <c>CLOSURE A</c>.
/// </summary>
public sealed class Prototype
{
    /// <summary>Source chunk name, or null when stripped (size 0).</summary>
    public string? Source { get; init; }

    public int LineDefined { get; init; }
    public int NumParams { get; init; }
    public bool IsVararg { get; init; }
    public int MaxStackSize { get; init; }

    /// <summary>Local-variable debug info (empty when stripped).</summary>
    public LocVar[] Locals { get; init; } = Array.Empty<LocVar>();

    /// <summary>Per-instruction source lines (empty when stripped).</summary>
    public int[] LineInfo { get; init; } = Array.Empty<int>();

    /// <summary>String constants (kstr).</summary>
    public string[] Strings { get; init; } = Array.Empty<string>();

    /// <summary>Number constants (knum).</summary>
    public double[] Numbers { get; init; } = Array.Empty<double>();

    /// <summary>Nested function prototypes (kproto), referenced by CLOSURE A.</summary>
    public Prototype[] Protos { get; init; } = Array.Empty<Prototype>();

    /// <summary>The instruction array; terminated by an <see cref="OpCode.End"/>.</summary>
    public Instruction[] Code { get; init; } = Array.Empty<Instruction>();
}
