namespace LuaDC1.Format;

/// <summary>
/// Lua 4.0 virtual-machine opcodes, in their exact binary order (the numeric value of
/// each enum member is the value stored in the bytecode). This order comes from
/// lua-4.0.1/src/lopcodes.h and is the single source of truth for decoding — it is NOT
/// the same order as the legacy decompiler's <c>vals[]</c> array.
/// </summary>
public enum OpCode : byte
{
    End = 0,
    Return,       // 1
    Call,         // 2
    TailCall,     // 3
    PushNil,      // 4
    Pop,          // 5
    PushInt,      // 6
    PushString,   // 7
    PushNum,      // 8
    PushNegNum,   // 9
    PushUpValue,  // 10
    GetLocal,     // 11
    GetGlobal,    // 12
    GetTable,     // 13
    GetDotted,    // 14
    GetIndexed,   // 15
    PushSelf,     // 16
    CreateTable,  // 17
    SetLocal,     // 18
    SetGlobal,    // 19
    SetTable,     // 20
    SetList,      // 21
    SetMap,       // 22
    Add,          // 23
    AddI,         // 24
    Sub,          // 25
    Mult,         // 26
    Div,          // 27
    Pow,          // 28
    Concat,       // 29
    Minus,        // 30
    Not,          // 31
    JmpNE,        // 32
    JmpEQ,        // 33
    JmpLT,        // 34
    JmpLE,        // 35
    JmpGT,        // 36
    JmpGE,        // 37
    JmpT,         // 38
    JmpF,         // 39
    JmpOnT,       // 40
    JmpOnF,       // 41
    Jmp,          // 42
    PushNilJmp,   // 43
    ForPrep,      // 44
    ForLoop,      // 45
    LForPrep,     // 46
    LForLoop,     // 47
    Closure,      // 48
}

/// <summary>How an instruction's argument field is interpreted / displayed.</summary>
public enum ArgMode
{
    None, // no argument
    U,    // unsigned argument
    S,    // signed argument (PUSHINT, ADDI)
    AB,   // two arguments: A (high) and B (low)
    K,    // string-constant index   (kStr[U])
    N,    // number-constant index   (kNum[U])
    L,    // local-variable index    (LOC[U])
    J,    // jump (signed offset; target = pc + 1 + S)
}

/// <summary>Static metadata for an opcode: its luac mnemonic and argument mode.</summary>
public readonly record struct OpInfo(string Name, ArgMode Mode);

public static class Ops
{
    public const int Count = 49;

    // Indexed by (int)OpCode. Names match the mnemonics luac -l prints.
    private static readonly OpInfo[] Table =
    {
        new("END",         ArgMode.None), // 0
        new("RETURN",      ArgMode.U),    // 1
        new("CALL",        ArgMode.AB),   // 2
        new("TAILCALL",    ArgMode.AB),   // 3
        new("PUSHNIL",     ArgMode.U),    // 4
        new("POP",         ArgMode.U),    // 5
        new("PUSHINT",     ArgMode.S),    // 6
        new("PUSHSTRING",  ArgMode.K),    // 7
        new("PUSHNUM",     ArgMode.N),    // 8
        new("PUSHNEGNUM",  ArgMode.N),    // 9
        new("PUSHUPVALUE", ArgMode.U),    // 10
        new("GETLOCAL",    ArgMode.L),    // 11
        new("GETGLOBAL",   ArgMode.K),    // 12
        new("GETTABLE",    ArgMode.None), // 13
        new("GETDOTTED",   ArgMode.K),    // 14
        new("GETINDEXED",  ArgMode.L),    // 15
        new("PUSHSELF",    ArgMode.K),    // 16
        new("CREATETABLE", ArgMode.U),    // 17
        new("SETLOCAL",    ArgMode.L),    // 18
        new("SETGLOBAL",   ArgMode.K),    // 19
        new("SETTABLE",    ArgMode.AB),   // 20
        new("SETLIST",     ArgMode.AB),   // 21
        new("SETMAP",      ArgMode.U),    // 22
        new("ADD",         ArgMode.None), // 23
        new("ADDI",        ArgMode.S),    // 24
        new("SUB",         ArgMode.None), // 25
        new("MULT",        ArgMode.None), // 26
        new("DIV",         ArgMode.None), // 27
        new("POW",         ArgMode.None), // 28
        new("CONCAT",      ArgMode.U),    // 29
        new("MINUS",       ArgMode.None), // 30
        new("NOT",         ArgMode.None), // 31
        new("JMPNE",       ArgMode.J),    // 32
        new("JMPEQ",       ArgMode.J),    // 33
        new("JMPLT",       ArgMode.J),    // 34
        new("JMPLE",       ArgMode.J),    // 35
        new("JMPGT",       ArgMode.J),    // 36
        new("JMPGE",       ArgMode.J),    // 37
        new("JMPT",        ArgMode.J),    // 38
        new("JMPF",        ArgMode.J),    // 39
        new("JMPONT",      ArgMode.J),    // 40
        new("JMPONF",      ArgMode.J),    // 41
        new("JMP",         ArgMode.J),    // 42
        new("PUSHNILJMP",  ArgMode.None), // 43
        new("FORPREP",     ArgMode.J),    // 44
        new("FORLOOP",     ArgMode.J),    // 45
        new("LFORPREP",    ArgMode.J),    // 46
        new("LFORLOOP",    ArgMode.J),    // 47
        new("CLOSURE",     ArgMode.AB),   // 48
    };

    public static bool IsValid(OpCode op) => (int)op < Count;

    public static OpInfo Info(OpCode op) => Table[(int)op];

    public static string Name(OpCode op) => IsValid(op) ? Table[(int)op].Name : $"OP_{(int)op}";

    public static ArgMode Mode(OpCode op) => Table[(int)op].Mode;
}
