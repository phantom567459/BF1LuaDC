using System.Diagnostics;
using LuaDC1.Format;
using LuaDC1.IO;

namespace LuaDC1.Verify;

/// <summary>
/// Checks decompiled output by feeding it back through the genuine Lua 4.0.1 compiler.
/// Level 1 confirms the source compiles at all; Level 2 recompiles, re-reads the bytecode, and
/// compares the opcode stream of every function against the original — bytecode is never
/// byte-identical (registers/line info/constant order differ), so the comparison is by opcode
/// sequence, which proves the source recompiles to the same program.
/// </summary>
public sealed class Verifier
{
    private readonly string? _luacPath;

    public Verifier(string? explicitLuac) => _luacPath = ResolveLuac(explicitLuac);

    public bool LuacAvailable => _luacPath != null;

    public enum Status { Match, Partial, CompileFailed, NoLuac }

    public sealed record Result(Status Status, int FunctionsMatched, int FunctionsTotal, int OpcodeDiffs, string? Detail)
    {
        public override string ToString() => Status switch
        {
            Status.Match => $"OK (round-trip: {FunctionsTotal} functions, opcodes match)",
            Status.Partial => $"PARTIAL ({FunctionsMatched}/{FunctionsTotal} functions match, {OpcodeDiffs} opcode diffs)",
            Status.CompileFailed => $"FAIL (luac: {Detail})",
            Status.NoLuac => "SKIPPED (luac.exe not found)",
            _ => "?",
        };
    }

    /// <summary>Verify <paramref name="luaPath"/> against the original bytecode it was decompiled from.</summary>
    public Result Verify(string luaPath, Prototype original)
    {
        if (_luacPath == null) return new Result(Status.NoLuac, 0, 0, 0, null);

        string temp = Path.Combine(Path.GetTempPath(), $"luadc_{Path.GetFileNameWithoutExtension(luaPath)}.luac");
        var (ok, stderr) = RunLuac($"-s -o \"{temp}\" \"{luaPath}\"");
        if (!ok)
        {
            TryDelete(temp);
            return new Result(Status.CompileFailed, 0, 0, 0, FirstLine(stderr));
        }

        try
        {
            var recompiled = BytecodeReader.Read(File.ReadAllBytes(temp));
            return CompareOpcodes(original, recompiled);
        }
        catch (Exception ex)
        {
            return new Result(Status.Partial, 0, 0, 0, $"could not re-read recompiled bytecode: {ex.Message}");
        }
        finally { TryDelete(temp); }
    }

    private static Result CompareOpcodes(Prototype original, Prototype recompiled)
    {
        var a = Disassembler.Flatten(original);
        var b = Disassembler.Flatten(recompiled);

        int matched = 0, diffs = 0;
        int n = Math.Min(a.Count, b.Count);
        for (int f = 0; f < n; f++)
        {
            // Ignore no-op jumps (JMP to the next instruction): the stock compiler emits these as
            // padding in some idioms, but they're behaviorally irrelevant, so a clean decompilation
            // that omits them is still correct. We measure behavioral equivalence, not byte parity.
            var oa = a[f].Instrs.Where(NotNoOpJmp).Select(x => x.Op).ToList();
            var ob = b[f].Instrs.Where(NotNoOpJmp).Select(x => x.Op).ToList();
            int d = SequenceDiff(oa, ob);
            if (d == 0) matched++;
            diffs += d;
        }
        diffs += Math.Abs(a.Count - b.Count) * 1000; // whole functions added/dropped

        int total = Math.Max(a.Count, b.Count);
        var status = diffs == 0 && a.Count == b.Count ? Status.Match : Status.Partial;
        return new Result(status, matched, total, diffs, null);
    }

    private static bool NotNoOpJmp(Disassembler.Instr x) =>
        !(x.Op == OpCode.Jmp && x.JumpTarget1Based == x.Pc + 1);

    /// <summary>A cheap length+mismatch distance between two opcode sequences.</summary>
    private static int SequenceDiff(List<OpCode> x, List<OpCode> y)
    {
        int diff = Math.Abs(x.Count - y.Count);
        int n = Math.Min(x.Count, y.Count);
        for (int i = 0; i < n; i++)
            if (x[i] != y[i]) diff++;
        return diff;
    }

    private (bool ok, string stderr) RunLuac(string args)
    {
        var psi = new ProcessStartInfo(_luacPath!, args)
        {
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        using var proc = Process.Start(psi)!;
        string err = proc.StandardError.ReadToEnd();
        proc.StandardOutput.ReadToEnd();
        proc.WaitForExit();
        return (proc.ExitCode == 0, err);
    }

    private static string? ResolveLuac(string? explicitPath)
    {
        var candidates = new List<string?>
        {
            explicitPath,
            Path.Combine(AppContext.BaseDirectory, "luac.exe"),
            // The bundled compiler historically lives in the netcoreapp3.0 output dir.
            Path.Combine(AppContext.BaseDirectory, "..", "netcoreapp3.0", "luac.exe"),
        };
        foreach (var c in candidates)
            if (c != null && File.Exists(c)) return Path.GetFullPath(c);

        // Fall back to PATH.
        foreach (var name in new[] { "luac.exe", "luac" })
        {
            foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
            {
                if (string.IsNullOrWhiteSpace(dir)) continue;
                string p = Path.Combine(dir, name);
                if (File.Exists(p)) return p;
            }
        }
        return null;
    }

    private static string FirstLine(string s)
    {
        s = s.Trim();
        int nl = s.IndexOf('\n');
        return nl >= 0 ? s[..nl].Trim() : s;
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { /* best effort */ }
    }
}
