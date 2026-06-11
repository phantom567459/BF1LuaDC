namespace LuaDC1.Format;

/// <summary>
/// A single 32-bit Lua 4.0 instruction. Field layout (from lua-4.0.1 lopcodes.h):
/// SIZE_OP=6, SIZE_B=9, POS_B=6, POS_A=15, SIZE_A=17, SIZE_U=26.
/// </summary>
public readonly struct Instruction
{
    /// <summary>Bias subtracted from the unsigned field to obtain a signed argument (MAXARG_S).</summary>
    public const int SignBias = 0x1FFFFFF; // (1 << 25) - 1 = 33554431

    public readonly uint Raw;

    public Instruction(uint raw) => Raw = raw;

    /// <summary>Low 6 bits: the opcode.</summary>
    public OpCode Op => (OpCode)(Raw & 0x3F);

    /// <summary>Unsigned argument (bits 6..31). Also used for K/N/L index modes.</summary>
    public int U => (int)(Raw >> 6);

    /// <summary>Signed argument (used by jumps and FOR): U - MAXARG_S.</summary>
    public int S => (int)(Raw >> 6) - SignBias;

    /// <summary>A field (bits 15..31).</summary>
    public int A => (int)(Raw >> 15);

    /// <summary>B field (bits 6..14).</summary>
    public int B => (int)((Raw >> 6) & 0x1FF);

    /// <summary>
    /// 0-based target index of a jump/FOR instruction located at <paramref name="pc"/>:
    /// after the VM advances pc it adds the signed offset, so target = pc + 1 + S.
    /// (luac's "; to N" annotation prints this 1-based, i.e. target + 1.)
    /// </summary>
    public int JumpTarget(int pc) => pc + 1 + S;

    public override string ToString() => $"{Ops.Name(Op)} (0x{Raw:X8})";
}
