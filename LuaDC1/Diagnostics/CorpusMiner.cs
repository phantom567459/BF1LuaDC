using System.Text.Json;
using System.Text.RegularExpressions;

namespace LuaDC1.Diagnostics;

/// <summary>
/// Mines a folder of original Lua scripts for naming data: function definitions yield
/// <c>funcname -&gt; [param names]</c>, and <c>local a,b = Func(...)</c> call sites yield
/// <c>funcname -&gt; [return names]</c> (by majority vote). Output is the JSON the decompiler's
/// NameDictionary consumes. Invoked via <c>--mine &lt;dir&gt;</c>.
/// </summary>
public static partial class CorpusMiner
{
    public static int Mine(string dir, string outPath)
    {
        if (!Directory.Exists(dir)) { Console.Error.WriteLine($"Not a directory: {dir}"); return 2; }

        var files = Directory.GetFiles(dir, "*.lua", SearchOption.AllDirectories);
        var @params = new Dictionary<string, string[]>();
        var returnVotes = new Dictionary<string, Dictionary<string, int>>(); // func -> (joined names -> count)

        foreach (var file in files)
        {
            string text;
            try { text = File.ReadAllText(file); } catch { continue; }
            text = StripComments(text);

            foreach (Match m in DefRegex().Matches(text))
            {
                string name = m.Groups[1].Value;
                var ps = SplitNames(m.Groups[2].Value);
                if (ps.Length > 0 && !@params.ContainsKey(name)) @params[name] = ps;
            }

            foreach (Match m in CallAssignRegex().Matches(text))
            {
                var names = SplitNames(m.Groups[1].Value);
                string func = m.Groups[2].Value;
                if (names.Length == 0 || names.Any(n => n == "...")) continue;
                string key = string.Join(",", names);
                var votes = returnVotes.TryGetValue(func, out var v) ? v : returnVotes[func] = new();
                votes[key] = votes.GetValueOrDefault(key) + 1;
            }
        }

        var returns = new Dictionary<string, string[]>();
        foreach (var (func, votes) in returnVotes)
        {
            // Pick the most-voted name list; ties break toward the longer (more informative) list.
            var best = votes.OrderByDescending(kv => kv.Value).ThenByDescending(kv => kv.Key.Length).First();
            returns[func] = best.Key.Split(',');
        }

        var json = JsonSerializer.Serialize(
            new { @params, returns },
            new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(outPath, json);

        Console.Error.WriteLine(
            $"Mined {files.Length} files: {@params.Count} function signatures, {returns.Count} return shapes -> {outPath}");
        return 0;
    }

    private static string[] SplitNames(string list) => list
        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Where(IsName)
        .ToArray();

    private static bool IsName(string s) =>
        s == "..." || (s.Length > 0 && (char.IsLetter(s[0]) || s[0] == '_') && s.All(c => char.IsLetterOrDigit(c) || c == '_'));

    private static string StripComments(string text) => LineCommentRegex().Replace(text, "");

    [GeneratedRegex(@"function\s+([A-Za-z_]\w*)\s*\(([^)]*)\)")]
    private static partial Regex DefRegex();

    [GeneratedRegex(@"local\s+([\w\s,]+?)\s*=\s*([A-Za-z_][\w]*)\s*\(")]
    private static partial Regex CallAssignRegex();

    [GeneratedRegex(@"--[^\n]*")]
    private static partial Regex LineCommentRegex();
}
