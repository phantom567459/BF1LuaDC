using System.Text;
using System.Text.RegularExpressions;
using LuaDC1.Format;

namespace LuaDC1.Diagnostics;

/// <summary>
/// Stage-1 self-test: decode a bytecode file ourselves and compare the result, instruction
/// for instruction, against a genuine <c>luac -l</c> listing (the <c>*.txt</c> oracle files).
/// Matching opcode names, printed args, and jump targets across a whole script validates the
/// opcode enum order, the instruction field extraction, the constant tables, the nested-proto
/// recursion, and the jump-target math all at once.
/// </summary>
public static partial class OracleCheck
{
    public sealed record Mismatch(int FuncIndex, int Pc, string Detail);

    public sealed record Report(int FunctionsExpected, int FunctionsActual, int InstrsCompared, List<Mismatch> Mismatches)
    {
        public bool Passed => FunctionsExpected == FunctionsActual && Mismatches.Count == 0;
    }

    public static Report Compare(Prototype main, string luacListing)
    {
        var expected = ParseListing(luacListing);
        var actual = Disassembler.Flatten(main);

        var mismatches = new List<Mismatch>();
        int compared = 0;
        int funcs = Math.Min(expected.Count, actual.Count);

        for (int f = 0; f < funcs; f++)
        {
            var exp = expected[f].Instrs;
            var act = actual[f].Instrs;

            if (exp.Count != act.Count)
                mismatches.Add(new Mismatch(f, 0, $"instruction count {act.Count} != luac {exp.Count}"));

            int n = Math.Min(exp.Count, act.Count);
            for (int i = 0; i < n; i++)
            {
                compared++;
                var e = exp[i];
                var a = act[i];

                if (!string.Equals(e.Op, Ops.Name(a.Op), StringComparison.Ordinal))
                {
                    mismatches.Add(new Mismatch(f, a.Pc, $"opcode {Ops.Name(a.Op)} != luac {e.Op}"));
                    continue; // args meaningless once the opcode disagrees
                }

                if (!e.Args.SequenceEqual(a.Args))
                    mismatches.Add(new Mismatch(f, a.Pc,
                        $"{e.Op} args [{string.Join(',', a.Args)}] != luac [{string.Join(',', e.Args)}]"));

                if (e.JumpTarget is int jt && a.JumpTarget1Based is int mjt && jt != mjt)
                    mismatches.Add(new Mismatch(f, a.Pc, $"{e.Op} jump to {mjt} != luac to {jt}"));
            }
        }

        return new Report(expected.Count, actual.Count, compared, mismatches);
    }

    // ---- luac -l listing parser -------------------------------------------

    private sealed record OracleInstr(int Pc, string Op, IReadOnlyList<int> Args, int? JumpTarget);
    private sealed record OracleFunc(List<OracleInstr> Instrs);

    private static List<OracleFunc> ParseListing(string text)
    {
        var funcs = new List<OracleFunc>();
        OracleFunc? current = null;

        foreach (var rawLine in text.Split('\n'))
        {
            string line = rawLine.TrimEnd('\r');
            if (HeaderRegex().IsMatch(line))
            {
                current = new OracleFunc(new List<OracleInstr>());
                funcs.Add(current);
                continue;
            }

            var m = InstrRegex().Match(line);
            if (!m.Success || current == null) continue;

            int pc = int.Parse(m.Groups[1].Value);
            string op = m.Groups[2].Value;
            string rest = m.Groups[3].Value;

            string argsPart = rest;
            string comment = "";
            int semi = rest.IndexOf(';');
            if (semi >= 0)
            {
                argsPart = rest[..semi];
                comment = rest[(semi + 1)..];
            }

            var args = new List<int>();
            foreach (Match num in IntRegex().Matches(argsPart))
                args.Add(int.Parse(num.Value));

            int? jump = null;
            var tm = JumpRegex().Match(comment);
            if (tm.Success) jump = int.Parse(tm.Groups[1].Value);

            current.Instrs.Add(new OracleInstr(pc, op, args, jump));
        }

        return funcs;
    }

    public static string Format(Report r)
    {
        var sb = new StringBuilder();
        sb.Append(r.Passed ? "ORACLE PASS" : "ORACLE FAIL")
          .Append(": ").Append(r.FunctionsActual).Append('/').Append(r.FunctionsExpected)
          .Append(" functions, ").Append(r.InstrsCompared).Append(" instructions compared, ")
          .Append(r.Mismatches.Count).Append(" mismatch(es)\n");
        foreach (var m in r.Mismatches.Take(30))
            sb.Append("  fn ").Append(m.FuncIndex).Append(" pc ").Append(m.Pc).Append(": ").Append(m.Detail).Append('\n');
        if (r.Mismatches.Count > 30)
            sb.Append("  … ").Append(r.Mismatches.Count - 30).Append(" more\n");
        return sb.ToString();
    }

    [GeneratedRegex(@"^(main|function)\s+<.*?>\s+\(\d+\s+instruction")]
    private static partial Regex HeaderRegex();

    [GeneratedRegex(@"^\s*(\d+)\s+\[[^\]]*\]\s+(\S+)(.*)$")]
    private static partial Regex InstrRegex();

    [GeneratedRegex(@"-?\d+")]
    private static partial Regex IntRegex();

    [GeneratedRegex(@"to\s+(\d+)")]
    private static partial Regex JumpRegex();
}
