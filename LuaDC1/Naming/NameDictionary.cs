using System.Reflection;
using System.Text.Json;

namespace LuaDC1.Naming;

/// <summary>
/// Maps BF1 function names to their parameter and return-value names, so the decompiler can
/// restore meaningful names for recovered locals and parameters. Combines a small curated core
/// (the stable engine <c>ScriptCB_*</c> API and the universal <c>_fn*</c> UI-callback patterns)
/// with a larger table mined from the original game scripts (see CorpusMiner / <c>--mine</c>).
/// </summary>
public sealed class NameDictionary
{
    private readonly Dictionary<string, string[]> _params;
    private readonly Dictionary<string, string[]> _returns;

    public NameDictionary(Dictionary<string, string[]> @params, Dictionary<string, string[]> returns)
    {
        _params = @params;
        _returns = returns;
    }

    /// <summary>Parameter names for <paramref name="funcName"/>, sized to <paramref name="numParams"/>;
    /// unknown slots are null. Falls back to the <c>_fn*</c> pattern table, then the universal
    /// "first param of a method is <c>this</c>" rule.</summary>
    public string?[]? ParamsFor(string? funcName, int numParams)
    {
        if (funcName == null || numParams == 0) return null;

        if (_params.TryGetValue(funcName, out var exact))
            return Fit(exact, numParams);

        // Pattern: <prefix>_fn<Suffix> — restore the documented signature for known callbacks.
        int fn = funcName.IndexOf("_fn", StringComparison.Ordinal);
        if (fn >= 0)
        {
            string suffix = funcName[(fn + 3)..];
            if (FnPatterns.TryGetValue(suffix, out var pat)) return Fit(pat, numParams);
            // Any method-shaped function: the first parameter is conventionally `this`.
            var names = new string?[numParams];
            names[0] = "this";
            return names;
        }
        return null;
    }

    /// <summary>Return-value names for <paramref name="funcName"/>, or null if unknown.</summary>
    public string[]? ReturnsFor(string? funcName) =>
        funcName != null && _returns.TryGetValue(funcName, out var r) ? r : null;

    private static string?[] Fit(string[] names, int n)
    {
        var result = new string?[n];
        for (int i = 0; i < n; i++) result[i] = i < names.Length ? names[i] : null;
        return result;
    }

    // ---- defaults ----------------------------------------------------------

    /// <summary>Stable engine-API return names (curated from the original scripts).</summary>
    private static readonly Dictionary<string, string[]> CuratedReturns = new()
    {
        ["ScriptCB_GetScreenInfo"] = new[] { "right", "bottom", "b", "w" },
        ["ScriptCB_GetSafeScreenInfo"] = new[] { "w", "h" },
        ["ScriptCB_GetError"] = new[] { "ErrorLevel", "ErrorMessage" },
        ["ScriptCB_GetLatestError"] = new[] { "ErrorLevel", "ErrorMessage" },
        ["ScriptCB_GetMouseSensitivity"] = new[] { "mouseSens", "mouseSensStep", "mouseSensMax" },
        ["ScriptCB_GetJoySensitivity"] = new[] { "joysense", "joysenseStep", "joysenseMax" },
        ["ScriptCB_GetVolumes"] = new[] { "VolMusic", "VolSfx", "VolVoice", "MaxVol", "VolMaster" },
        ["ScriptCB_GetLobbyPlayerFlags"] = new[] { "muted", "friend", "bCanBoot" },
    };

    /// <summary><c>_fn&lt;Suffix&gt;</c> -> parameter names (the universal UI-callback signatures).</summary>
    private static readonly Dictionary<string, string[]> FnPatterns = new()
    {
        ["SetSize"] = new[] { "this", "w", "h" },
        ["SetSelection"] = new[] { "this", "sel" },
        ["Select"] = new[] { "this", "on", "labelonly" },
        ["Hilight"] = new[] { "this", "on", "fDt" },
        ["Highlight"] = new[] { "this", "on", "fDt" },
        ["Update"] = new[] { "this", "fDt" },
        ["Enter"] = new[] { "this", "bFwd" },
        ["Leave"] = new[] { "this", "bFwd" },
        ["Activate"] = new[] { "this", "vis" },
        ["SetVis"] = new[] { "this", "vis" },
        ["BuildScreen"] = new[] { "this", "mode" },
    };

    /// <summary>Load the shipped dictionary: curated core merged with the embedded mined table.</summary>
    public static NameDictionary LoadDefault()
    {
        var @params = new Dictionary<string, string[]>();
        var returns = new Dictionary<string, string[]>(CuratedReturns);

        // Embedded JSON produced by `--mine` (optional; absent during early bring-up).
        var asm = Assembly.GetExecutingAssembly();
        string? resource = asm.GetManifestResourceNames().FirstOrDefault(n => n.EndsWith("bf1-names.json"));
        if (resource != null)
        {
            using var stream = asm.GetManifestResourceStream(resource)!;
            Merge(Parse(stream), @params, returns);
        }
        return new NameDictionary(@params, returns);
    }

    public static NameDictionary LoadFrom(string path)
    {
        var d = LoadDefault();
        using var stream = File.OpenRead(path);
        var parsed = Parse(stream);
        Merge(parsed, d._params, d._returns);
        return d;
    }

    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    private static (Dictionary<string, string[]> p, Dictionary<string, string[]> r) Parse(Stream stream)
    {
        var doc = JsonSerializer.Deserialize<MinedNames>(stream, JsonOpts) ?? new MinedNames();
        return (doc.Params ?? new(), doc.Returns ?? new());
    }

    private static void Merge((Dictionary<string, string[]> p, Dictionary<string, string[]> r) src,
        Dictionary<string, string[]> @params, Dictionary<string, string[]> returns)
    {
        foreach (var (k, v) in src.p) @params[k] = v;
        foreach (var (k, v) in src.r) returns.TryAdd(k, v); // curated returns win
    }

    private sealed class MinedNames
    {
        public Dictionary<string, string[]>? Params { get; set; }
        public Dictionary<string, string[]>? Returns { get; set; }
    }
}
