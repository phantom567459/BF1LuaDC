using System.Buffers.Binary;
using System.Text;
using LuaDC1.Format;

namespace LuaDC1.IO;

/// <summary>
/// Reads a Lua 4.0 precompiled chunk (the "undump" step) into a <see cref="Prototype"/> tree.
/// Field order and semantics follow lua-4.0.1/src/lundump.c exactly. Throws
/// <see cref="BadBytecodeException"/> on any header/structure inconsistency.
/// </summary>
public sealed class BytecodeReader
{
    private const byte EscChar = 0x1B;
    private const byte Version = 0x40;   // Lua 4.0
    private const int ExpectedSizeOp = 6;
    private const int ExpectedSizeB = 9;

    private readonly byte[] _data;
    private int _pos;

    private bool _littleEndian = true;
    private int _sizeInt = 4;
    private int _sizeSizeT = 4;
    private int _sizeInstruction = 4;
    private int _sizeNumber = 8;

    public BytecodeReader(byte[] data) => _data = data;

    /// <summary>Header fields captured for diagnostics (<c>--verbose</c>).</summary>
    public string HeaderSummary { get; private set; } = "";

    public static Prototype Read(byte[] bytecode) => new BytecodeReader(bytecode).ReadTopLevel();

    public Prototype ReadTopLevel()
    {
        ReadHeader();
        var main = ReadFunction();
        return main;
    }

    private void ReadHeader()
    {
        if (_data.Length < 13)
            throw new BadBytecodeException(0, "file too small to contain a Lua header");

        if (Byte() != EscChar || Byte() != (byte)'L' || Byte() != (byte)'u' || Byte() != (byte)'a')
            throw new BadBytecodeException(0, "missing Lua signature (1B 'L' 'u' 'a')");

        byte version = Byte();
        if (version != Version)
            throw new BadBytecodeException(_pos - 1,
                $"unsupported Lua bytecode version 0x{version:X2} (expected 0x40 for Lua 4.0)");

        _littleEndian = Byte() != 0;
        _sizeInt = Byte();
        _sizeSizeT = Byte();
        _sizeInstruction = Byte();
        int sizeInstrBits = Byte();   // SIZE_INSTRUCTION (total bits, 0x20)
        int sizeOp = Byte();          // SIZE_OP (6)
        int sizeB = Byte();           // SIZE_B (9)
        _sizeNumber = Byte();

        if (_sizeInstruction != 4)
            throw new BadBytecodeException(_pos - 1,
                $"sizeof(Instruction)={_sizeInstruction}; only 4-byte instructions are supported");
        if (sizeOp != ExpectedSizeOp || sizeB != ExpectedSizeB)
            throw new BadBytecodeException(_pos - 1,
                $"unexpected instruction layout SIZE_OP={sizeOp} SIZE_B={sizeB} (expected 6/9)");
        // BF1's Lua build uses 4-byte float Numbers; stock Lua 4.0 uses 8-byte doubles. Support both.
        if (_sizeNumber != 4 && _sizeNumber != 8)
            throw new BadBytecodeException(_pos - 1,
                $"sizeof(Number)={_sizeNumber}; only 4-byte float or 8-byte double are supported");

        double testNumber = ReadNumber();   // sanity value written by ldump (≈ 3.14159265e8)

        HeaderSummary =
            $"Lua {version >> 4}.{version & 0xF}, {(_littleEndian ? "little" : "big")}-endian, " +
            $"int={_sizeInt} size_t={_sizeSizeT} instr={_sizeInstruction}({sizeInstrBits}b op{sizeOp}/b{sizeB}) " +
            $"number={_sizeNumber} test={testNumber:g}";
    }

    private Prototype ReadFunction()
    {
        string? source = ReadString();
        int lineDefined = ReadInt();
        int numParams = ReadInt();
        bool isVararg = Byte() != 0;
        int maxStack = ReadInt();

        var locals = ReadLocals();
        var lineInfo = ReadLineInfo();
        var (strings, numbers, protos) = ReadConstants();
        var code = ReadCode();

        return new Prototype
        {
            Source = source,
            LineDefined = lineDefined,
            NumParams = numParams,
            IsVararg = isVararg,
            MaxStackSize = maxStack,
            Locals = locals,
            LineInfo = lineInfo,
            Strings = strings,
            Numbers = numbers,
            Protos = protos,
            Code = code,
        };
    }

    private LocVar[] ReadLocals()
    {
        int n = ReadInt();
        CheckCount(n, 3);
        if (n == 0) return Array.Empty<LocVar>();
        var locals = new LocVar[n];
        for (int i = 0; i < n; i++)
        {
            string name = ReadString() ?? "";
            int startPc = ReadInt();
            int endPc = ReadInt();
            locals[i] = new LocVar(name, startPc, endPc);
        }
        return locals;
    }

    private int[] ReadLineInfo()
    {
        int n = ReadInt();
        CheckCount(n, _sizeInt);
        if (n == 0) return Array.Empty<int>();
        var lines = new int[n];
        for (int i = 0; i < n; i++) lines[i] = ReadInt();
        return lines;
    }

    private (string[] strings, double[] numbers, Prototype[] protos) ReadConstants()
    {
        int nStr = ReadInt();
        CheckCount(nStr, 1);
        var strings = new string[nStr];
        for (int i = 0; i < nStr; i++) strings[i] = ReadString() ?? "";

        int nNum = ReadInt();
        CheckCount(nNum, _sizeNumber);
        var numbers = new double[nNum];
        for (int i = 0; i < nNum; i++) numbers[i] = ReadNumber();

        int nProto = ReadInt();
        CheckCount(nProto, 1);
        var protos = new Prototype[nProto];
        for (int i = 0; i < nProto; i++) protos[i] = ReadFunction();

        return (strings, numbers, protos);
    }

    private Instruction[] ReadCode()
    {
        int size = ReadInt();
        CheckCount(size, _sizeInstruction);
        if (size <= 0)
            throw new BadBytecodeException(_pos, "function has no code");

        var code = new Instruction[size];
        for (int i = 0; i < size; i++)
            code[i] = new Instruction(ReadUInt32());

        if (code[size - 1].Op != OpCode.End)
            throw new BadBytecodeException(_pos, "code does not terminate with END");

        return code;
    }

    // ---- primitive readers -------------------------------------------------

    private byte Byte()
    {
        if (_pos >= _data.Length) throw new BadBytecodeException(_pos, "unexpected end of bytecode");
        return _data[_pos++];
    }

    private ReadOnlySpan<byte> Take(int count)
    {
        if (count < 0 || _pos + count > _data.Length)
            throw new BadBytecodeException(_pos, $"unexpected end of bytecode (wanted {count} bytes)");
        var slice = _data.AsSpan(_pos, count);
        _pos += count;
        return slice;
    }

    private uint ReadUInt32()
    {
        var b = Take(4);
        return _littleEndian ? BinaryPrimitives.ReadUInt32LittleEndian(b) : BinaryPrimitives.ReadUInt32BigEndian(b);
    }

    private int ReadInt()
    {
        // Ints are signed: lineinfo entries in particular can be negative, so read with sign
        // extension rather than an unsigned-then-checked cast (which overflowed on high bits).
        if (_sizeInt == 4)
        {
            var b = Take(4);
            return _littleEndian ? BinaryPrimitives.ReadInt32LittleEndian(b) : BinaryPrimitives.ReadInt32BigEndian(b);
        }
        return unchecked((int)ReadSizedUnsigned(_sizeInt));
    }

    private long ReadSizeT() => ReadSizedUnsigned(_sizeSizeT);

    private long ReadSizedUnsigned(int size)
    {
        var b = Take(size);
        long v = 0;
        if (_littleEndian)
            for (int i = size - 1; i >= 0; i--) v = (v << 8) | b[i];
        else
            for (int i = 0; i < size; i++) v = (v << 8) | b[i];
        return v;
    }

    private double ReadNumber()
    {
        var b = Take(_sizeNumber);
        if (_sizeNumber == 4)
            return _littleEndian ? BinaryPrimitives.ReadSingleLittleEndian(b) : BinaryPrimitives.ReadSingleBigEndian(b);
        return _littleEndian ? BinaryPrimitives.ReadDoubleLittleEndian(b) : BinaryPrimitives.ReadDoubleBigEndian(b);
    }

    private string? ReadString()
    {
        long len = ReadSizeT();
        if (len == 0) return null;
        if (len < 0 || len > _data.Length - _pos)
            throw new BadBytecodeException(_pos, $"string length {len} out of range");
        var bytes = Take((int)len);
        // Stored length includes the trailing NUL Lua appends; the value is the first len-1 bytes.
        // Latin1 keeps every byte 1:1 so arbitrary script bytes survive round-trips.
        return Encoding.Latin1.GetString(bytes[..^1]);
    }

    /// <summary>Sanity-bounds a length prefix so a corrupt count can't allocate gigabytes.</summary>
    private void CheckCount(int n, int elementSize)
    {
        if (n < 0 || (long)n * elementSize > _data.Length - _pos)
            throw new BadBytecodeException(_pos, $"implausible element count {n}");
    }
}

public sealed class BadBytecodeException : Exception
{
    public BadBytecodeException(int offset, string message)
        : base($"Bad Lua 4.0 bytecode at offset 0x{offset:X}: {message}") { }
}
