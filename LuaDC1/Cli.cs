using LuaDC1.Decompile;
using LuaDC1.Diagnostics;
using LuaDC1.Format;
using LuaDC1.IO;
using LuaDC1.Naming;
using LuaDC1.Verify;

namespace LuaDC1;

/// <summary>Command-line front end: single-file and batch decompilation, plus diagnostics.</summary>
public static class Cli
{
    private sealed class Options
    {
        public string? Input;
        public string? Out;
        public string? Luac;
        public string Pattern = "*.script";
        public bool Batch;
        public bool Verify;
        public bool KeepBytecode;
        public bool Disasm;
        public bool Verbose;
        public string? OracleTxt;
        public bool Names = true;       // heuristic variable naming on by default
        public string? NamesFile;
        public string? MineDir;
        public bool List;               // just list the scripts in a .lvl
    }

    public static int Run(string[] args)
    {
        if (args.Length == 0 || args[0] is "-h" or "--help")
        {
            PrintUsage();
            return args.Length == 0 ? 1 : 0;
        }

        var o = new Options();
        for (int i = 0; i < args.Length; i++)
        {
            string a = args[i];
            switch (a)
            {
                case "--out": o.Out = NextArg(args, ref i, a); break;
                case "--luac": o.Luac = NextArg(args, ref i, a); break;
                case "--pattern": o.Pattern = NextArg(args, ref i, a); break;
                case "--batch": o.Batch = true; break;
                case "--verify": o.Verify = true; break;
                case "--keep-bytecode": o.KeepBytecode = true; break;
                case "--disasm": o.Disasm = true; break;
                case "--verbose": o.Verbose = true; break;
                case "--oracle": o.OracleTxt = NextArg(args, ref i, a); break;
                case "--names": o.Names = NextArg(args, ref i, a) is not ("off" or "none" or "false"); break;
                case "--names-file": o.NamesFile = NextArg(args, ref i, a); break;
                case "--mine": o.MineDir = NextArg(args, ref i, a); break;
                case "--list": o.List = true; break;
                default:
                    if (a.StartsWith('-')) { Console.Error.WriteLine($"Unknown option: {a}"); return 2; }
                    o.Input ??= a;
                    break;
            }
        }

        if (o.MineDir != null)
            return CorpusMiner.Mine(o.MineDir, o.Out ?? "bf1-names.json");

        if (o.Input == null) { Console.Error.WriteLine("No input given."); return 2; }

        var dict = o.Names ? (o.NamesFile != null ? NameDictionary.LoadFrom(o.NamesFile) : NameDictionary.LoadDefault()) : null;

        if (o.Input.EndsWith(".lvl", StringComparison.OrdinalIgnoreCase) && File.Exists(o.Input))
            return RunLvl(o, dict);

        if (o.Batch || Directory.Exists(o.Input))
            return RunBatch(o, dict);

        if (!File.Exists(o.Input)) { Console.Error.WriteLine($"Input not found: {o.Input}"); return 2; }
        return RunSingle(o, o.Input, o.Out, dict) ? 0 : 1;
    }

    // ---- .lvl bundle -------------------------------------------------------

    private static int RunLvl(Options o, NameDictionary? dict)
    {
        var scripts = UcfbExtractor.EnumerateScripts(File.ReadAllBytes(o.Input!));
        if (scripts.Count == 0) { Console.Error.WriteLine($"No Lua scripts found in {o.Input}"); return 1; }

        if (o.List)
        {
            Console.WriteLine($"{scripts.Count} script(s) in {Path.GetFileName(o.Input)}:");
            foreach (var s in scripts) Console.WriteLine($"  {s.Name,-32} {s.Bytecode.Length} bytes");
            return 0;
        }

        string outDir = o.Out ?? Path.Combine(
            Path.GetDirectoryName(Path.GetFullPath(o.Input!)) ?? ".",
            Path.GetFileNameWithoutExtension(o.Input) + "_scripts");
        Directory.CreateDirectory(outDir);
        Console.Error.WriteLine($"Decompiling {scripts.Count} script(s) from {Path.GetFileName(o.Input)} -> {outDir}");

        var verifier = o.Verify ? new Verifier(o.Luac) : null;
        int ok = 0, failed = 0;
        foreach (var s in scripts)
        {
            string dest = Path.Combine(outDir, MakeFileName(s.Name) + ".lua");
            string status;
            try
            {
                var main = BytecodeReader.Read(s.Bytecode);
                File.WriteAllText(dest, LuaEmitter.Emit(main, dict));
                status = verifier != null ? verifier.Verify(dest, main).ToString() : "decompiled";
                ok++;
            }
            catch (Exception ex) { status = $"ERROR: {ex.Message}"; failed++; }
            Console.WriteLine($"  {s.Name,-32} {status}");
        }
        Console.Error.WriteLine($"Done: {ok} ok, {failed} failed.");
        return failed == 0 ? 0 : 1;
    }

    private static string MakeFileName(string name)
    {
        if (string.IsNullOrEmpty(name)) return "script";
        foreach (char c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
        return name;
    }

    // ---- single file -------------------------------------------------------

    private static bool RunSingle(Options o, string input, string? outPath, NameDictionary? dict)
    {
        try
        {
            var extracted = UcfbExtractor.Extract(File.ReadAllBytes(input));
            var main = BytecodeReader.Read(extracted.Bytecode);

            if (o.Verbose)
                Console.Error.WriteLine($"{Path.GetFileName(input)}: {extracted.Kind}" +
                    (extracted.ScriptName != null ? $" \"{extracted.ScriptName}\"" : "") +
                    $", {extracted.Bytecode.Length} bytecode bytes");

            // Diagnostic modes (single file only).
            if (o.OracleTxt != null)
            {
                var report = OracleCheck.Compare(main, File.ReadAllText(o.OracleTxt));
                Console.Write(OracleCheck.Format(report));
                return report.Passed;
            }
            if (o.Disasm)
            {
                Console.Write(Disassembler.RenderListing(main));
                return true;
            }

            string lua = LuaEmitter.Emit(main, dict);
            string dest = outPath ?? Path.ChangeExtension(input, ".lua");
            File.WriteAllText(dest, lua);

            if (o.KeepBytecode)
                File.WriteAllBytes(Path.ChangeExtension(dest, ".luac"), extracted.Bytecode);

            string msg = $"wrote {dest}";
            if (o.Verify)
            {
                var v = new Verifier(o.Luac);
                msg += "  " + v.Verify(dest, main);
            }
            Console.WriteLine(msg);
            return true;
        }
        catch (Exception ex) when (ex is BadBytecodeException or InvalidDataException or IOException)
        {
            Console.Error.WriteLine($"{Path.GetFileName(input)}: error: {ex.Message}");
            return false;
        }
    }

    // ---- batch -------------------------------------------------------------

    private static int RunBatch(Options o, NameDictionary? dict)
    {
        if (!Directory.Exists(o.Input)) { Console.Error.WriteLine($"Not a directory: {o.Input}"); return 2; }

        var files = Directory.GetFiles(o.Input, o.Pattern).OrderBy(f => f).ToArray();
        if (files.Length == 0) { Console.Error.WriteLine($"No files matching {o.Pattern} in {o.Input}"); return 1; }

        // Don't clobber hand-written .lua sitting next to the inputs: default to an 'out' subfolder.
        string outDir = o.Out ?? Path.Combine(o.Input, "decompiled");
        Directory.CreateDirectory(outDir);
        Console.Error.WriteLine($"Decompiling {files.Length} file(s) -> {outDir}");

        var verifier = o.Verify ? new Verifier(o.Luac) : null;
        if (o.Verify && verifier!.LuacAvailable == false)
            Console.Error.WriteLine("warning: --verify requested but luac.exe not found; skipping verification");

        int ok = 0, failed = 0;
        foreach (var file in files)
        {
            string dest = Path.Combine(outDir, Path.GetFileNameWithoutExtension(file) + ".lua");
            string status;
            try
            {
                var extracted = UcfbExtractor.Extract(File.ReadAllBytes(file));
                var main = BytecodeReader.Read(extracted.Bytecode);
                File.WriteAllText(dest, LuaEmitter.Emit(main, dict));
                if (o.KeepBytecode) File.WriteAllBytes(Path.ChangeExtension(dest, ".luac"), extracted.Bytecode);
                status = verifier != null ? verifier.Verify(dest, main).ToString() : "decompiled";
                ok++;
            }
            catch (Exception ex)
            {
                status = $"ERROR: {ex.Message}";
                failed++;
            }
            Console.WriteLine($"  {Path.GetFileName(file),-32} {status}");
        }

        Console.Error.WriteLine($"Done: {ok} ok, {failed} failed.");
        return failed == 0 ? 0 : 1;
    }

    private static string NextArg(string[] args, ref int i, string flag)
    {
        if (i + 1 >= args.Length) throw new ArgumentException($"{flag} requires a value");
        return args[++i];
    }

    private static void PrintUsage()
    {
        Console.WriteLine(
            """
            LuaDC1 - Star Wars Battlefront (2004) Lua 4.0 decompiler

            Usage:
              LuaDC1 <input> [options]      <input> = a .script/.luac file OR a folder (batch)

            Options:
              --out <path>        output file (single) or directory (batch)
              --batch             force folder mode (auto when <input> is a directory)
              --pattern <glob>    batch filter (default *.script)
              --verify            recompile output with bundled luac and report round-trip
              --luac <path>       explicit luac.exe (else auto-probe)
              --keep-bytecode     also write the stripped raw bytecode (.luac)
              --names <full|off>  heuristic variable naming (default full; off = literal varN)
              --names-file <json> extra name dictionary (merged over the built-in one)
              --mine <dir>        mine original .lua scripts -> name dictionary JSON (--out path)
              --disasm            print a luac-style disassembly instead of decompiling
              --oracle <txt>      compare our decode against a genuine luac -l listing (self-test)
              --verbose           print source kind, script name and bytecode size
              -h, --help          show this help

            Examples:
              LuaDC1 dpk02a.script                 strip UCFB + decompile -> dpk02a.lua
              LuaDC1 dpk02a.script --verify        decompile and confirm it round-trips
              LuaDC1 .\scripts --verify            batch a folder -> scripts\decompiled\
            """);
    }
}
