using System.Globalization;
using System.Text;

namespace LuaDC1.Format;

/// <summary>
/// Renders a <see cref="Prototype"/> as a luac-style disassembly and exposes the same
/// instructions in a structured form. Used both for the <c>--disasm</c> diagnostic and as
/// the Stage-1 oracle: the structured output is compared against genuine <c>luac -l</c>
/// listings to prove the reader decodes correctly. Functions are emitted in luac's order —
/// pre-order DFS: a function's own code first, then each nested prototype in turn.
/// </summary>
public static class Disassembler
{
    /// <summary>One disassembled instruction. <see cref="Args"/> are the values luac prints.</summary>
    public sealed record Instr(int Pc, OpCode Op, IReadOnlyList<int> Args, int? JumpTarget1Based);

    public sealed record Func(bool IsMain, int LineDefined, IReadOnlyList<Instr> Instrs);

    /// <summary>Flatten a prototype tree into luac's pre-order sequence of functions.</summary>
    public static List<Func> Flatten(Prototype main)
    {
        var funcs = new List<Func>();
        Walk(main, isMain: true);
        return funcs;

        void Walk(Prototype p, bool isMain)
        {
            var instrs = new List<Instr>(p.Code.Length);
            for (int pc = 0; pc < p.Code.Length; pc++)
            {
                var ins = p.Code[pc];
                instrs.Add(new Instr(pc + 1, ins.Op, DisplayArgs(ins), JumpTargetOf(ins, pc)));
            }
            funcs.Add(new Func(isMain, p.LineDefined, instrs));
            foreach (var child in p.Protos) Walk(child, isMain: false);
        }
    }

    /// <summary>The argument values luac prints for an instruction, by its arg mode.</summary>
    public static IReadOnlyList<int> DisplayArgs(Instruction ins) => Ops.Mode(ins.Op) switch
    {
        ArgMode.None => Array.Empty<int>(),
        ArgMode.AB => new[] { ins.A, ins.B },
        ArgMode.S => new[] { ins.S },
        ArgMode.J => new[] { ins.S },
        _ => new[] { ins.U },   // U, K, N, L all print the unsigned field
    };

    private static int? JumpTargetOf(Instruction ins, int pc) =>
        Ops.Mode(ins.Op) == ArgMode.J ? ins.JumpTarget(pc) + 1 : null;   // +1 → 1-based, like luac

    /// <summary>Render a full luac-style listing (used by <c>--disasm</c>).</summary>
    public static string RenderListing(Prototype main)
    {
        var sb = new StringBuilder();
        Render(main, isMain: true);
        return sb.ToString();

        void Render(Prototype p, bool isMain)
        {
            sb.Append('\n')
              .Append(isMain ? "main" : "function")
              .Append(" <").Append(p.LineDefined).Append(':').Append(p.Source ?? "(none)").Append("> (")
              .Append(p.Code.Length).Append(" instructions, ")
              .Append(p.NumParams).Append(" params, ").Append(p.MaxStackSize).Append(" stacks, ")
              .Append(p.Strings.Length).Append(" strings, ").Append(p.Numbers.Length).Append(" numbers, ")
              .Append(p.Protos.Length).Append(" functions)\n");

            for (int pc = 0; pc < p.Code.Length; pc++)
                sb.Append(FormatLine(p, pc)).Append('\n');

            foreach (var child in p.Protos) Render(child, isMain: false);
        }
    }

    /// <summary>Formatted disassembly lines for a single prototype's own code (no children).</summary>
    public static List<string> InstructionLines(Prototype p)
    {
        var lines = new List<string>(p.Code.Length);
        for (int pc = 0; pc < p.Code.Length; pc++)
            lines.Add(FormatLine(p, pc).Replace("\t", " ").Trim());
        return lines;
    }

    private static string FormatLine(Prototype p, int pc)
    {
        var ins = p.Code[pc];
        var sb = new StringBuilder();
        sb.Append((pc + 1).ToString().PadLeft(6)).Append("\t[-]\t").Append(Ops.Name(ins.Op).PadRight(11)).Append('\t');

        foreach (var (a, idx) in DisplayArgs(ins).Select((a, i) => (a, i)))
        {
            if (idx > 0) sb.Append(' ');
            sb.Append(a);
        }

        string? comment = CommentFor(p, ins, pc);
        if (comment != null) sb.Append("\t; ").Append(comment);
        return sb.ToString();
    }

    /// <summary>The trailing <c>; …</c> annotation luac prints for constant/jump opcodes.</summary>
    public static string? CommentFor(Prototype p, Instruction ins, int pc)
    {
        switch (Ops.Mode(ins.Op))
        {
            case ArgMode.K:
                string s = ins.U >= 0 && ins.U < p.Strings.Length ? p.Strings[ins.U] : "?";
                return ins.Op == OpCode.PushString ? Quote(s) : s;
            case ArgMode.N:
                double n = ins.U >= 0 && ins.U < p.Numbers.Length ? p.Numbers[ins.U] : double.NaN;
                if (ins.Op == OpCode.PushNegNum) n = -n;
                return NumberToString(n);
            case ArgMode.J:
                return $"to {ins.JumpTarget(pc) + 1}";
            default:
                return ins.Op == OpCode.Closure ? $"closure {ins.A}" : null;
        }
    }

    public static string NumberToString(double n)
    {
        if (n == Math.Floor(n) && !double.IsInfinity(n) && Math.Abs(n) < 1e15)
            return ((long)n).ToString(CultureInfo.InvariantCulture);
        return n.ToString("R", CultureInfo.InvariantCulture);
    }

    public static string Quote(string s)
    {
        var sb = new StringBuilder(s.Length + 2);
        sb.Append('"');
        foreach (char c in s)
        {
            switch (c)
            {
                case '"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                case '\0': sb.Append("\\0"); break;
                default: sb.Append(c); break;
            }
        }
        sb.Append('"');
        return sb.ToString();
    }
}
