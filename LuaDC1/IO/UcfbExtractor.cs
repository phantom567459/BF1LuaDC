using System.Buffers.Binary;
using System.Text;

namespace LuaDC1.IO;

/// <summary>
/// Strips the Star Wars Battlefront UCFB container off an extracted <c>.script</c> chunk,
/// yielding the raw Lua 4.0 bytecode. A UCFB chunk is a 4-byte ASCII FourCC tag + a
/// little-endian uint32 payload size + that many payload bytes, with each following chunk
/// aligned to a 4-byte boundary. Containers (<c>ucfb</c>, <c>scr_</c>) hold child chunks;
/// the Lua bytecode is the payload of the <c>BODY</c> leaf, taken at exactly its declared
/// size (so no trailing-padding guesswork). Inputs that already start at the Lua signature
/// are passed through unchanged.
/// </summary>
public static class UcfbExtractor
{
    /// <summary>The Lua 4.0 precompiled-chunk signature: ESC 'L' 'u' 'a'.</summary>
    private static ReadOnlySpan<byte> LuaSignature => new byte[] { 0x1B, 0x4C, 0x75, 0x61 };

    public enum SourceKind { RawBytecode, Ucfb }

    public readonly record struct Result(byte[] Bytecode, SourceKind Kind, string? ScriptName);

    public static Result Extract(byte[] file)
    {
        var span = file.AsSpan();

        if (span.Length >= 4 && span[..4].SequenceEqual(LuaSignature))
            return new Result(file, SourceKind.RawBytecode, null);

        if (StartsWithTag(span, "ucfb") || StartsWithTag(span, "scr_"))
        {
            if (TryFindBody(span, out var body, out var name))
                return new Result(body.ToArray(), SourceKind.Ucfb, name);
            throw new InvalidDataException("UCFB container found but no scr_/BODY chunk with Lua bytecode inside.");
        }

        // Last resort: scan for the Lua signature anywhere (handles odd containers/offsets).
        int sig = IndexOf(span, LuaSignature);
        if (sig >= 0)
            return new Result(span[sig..].ToArray(), SourceKind.Ucfb, null);

        throw new InvalidDataException(
            "Input is neither raw Lua 4.0 bytecode (1B 4C 75 61) nor a recognizable UCFB script chunk.");
    }

    /// <summary>One Lua script found inside a .lvl: its NAME and raw bytecode (the BODY payload).</summary>
    public readonly record struct Script(string Name, byte[] Bytecode);

    /// <summary>
    /// Enumerate every Lua script in a .lvl (or any UCFB container): walk the chunk tree and, for
    /// each <c>scr_</c> chunk, return its NAME and BODY bytecode. Descends into <c>ucfb</c> and
    /// <c>lvl_</c> sub-bundles so scripts at any nesting depth are found.
    /// </summary>
    public static List<Script> EnumerateScripts(byte[] file)
    {
        var results = new List<Script>();
        WalkScripts(file.AsSpan(), results);
        return results;
    }

    private static void WalkScripts(ReadOnlySpan<byte> region, List<Script> results)
    {
        int offset = 0;
        while (offset + 8 <= region.Length)
        {
            string tag = Encoding.ASCII.GetString(region.Slice(offset, 4));
            uint size = BinaryPrimitives.ReadUInt32LittleEndian(region.Slice(offset + 4, 4));
            int payloadStart = offset + 8;
            if (size > (uint)(region.Length - payloadStart)) break;
            var payload = region.Slice(payloadStart, (int)size);

            switch (tag)
            {
                case "scr_":
                    if (TryFindBody(payload, out var body, out var name))
                        results.Add(new Script(name ?? "", body.ToArray()));
                    break;
                case "ucfb":
                    WalkScripts(payload, results);
                    break;
                case "lvl_":
                    // lvl_ payload begins with a 4-byte name hash, then child chunks.
                    WalkScripts(payload.Length >= 4 ? payload[4..] : payload, results);
                    break;
            }

            offset = payloadStart + Align4((int)size);
        }
    }

    private static bool StartsWithTag(ReadOnlySpan<byte> span, string tag) =>
        span.Length >= 4 && span[0] == tag[0] && span[1] == tag[1] && span[2] == tag[2] && span[3] == tag[3];

    /// <summary>
    /// Recursively walks chunks in <paramref name="region"/>, descending into ucfb/scr_
    /// containers, and returns the first BODY payload found (plus the nearest NAME, if any).
    /// </summary>
    private static bool TryFindBody(ReadOnlySpan<byte> region, out ReadOnlySpan<byte> body, out string? name)
    {
        body = default;
        name = null;
        int offset = 0;

        while (offset + 8 <= region.Length)
        {
            string tag = Encoding.ASCII.GetString(region.Slice(offset, 4));
            uint size = BinaryPrimitives.ReadUInt32LittleEndian(region.Slice(offset + 4, 4));
            int payloadStart = offset + 8;

            // Guard against corrupt/oversized lengths.
            if (size > (uint)(region.Length - payloadStart))
                break;

            var payload = region.Slice(payloadStart, (int)size);

            switch (tag)
            {
                case "BODY":
                    body = payload;
                    return true;
                case "NAME":
                    name = TrimNul(Encoding.ASCII.GetString(payload));
                    break;
                case "ucfb":
                case "scr_":
                    if (TryFindBody(payload, out var innerBody, out var innerName))
                    {
                        body = innerBody;
                        name = innerName ?? name;
                        return true;
                    }
                    break;
            }

            // Advance to the next 4-byte-aligned chunk.
            offset = payloadStart + Align4((int)size);
        }

        return false;
    }

    private static int Align4(int n) => (n + 3) & ~3;

    private static string TrimNul(string s)
    {
        int nul = s.IndexOf('\0');
        return nul >= 0 ? s[..nul] : s;
    }

    private static int IndexOf(ReadOnlySpan<byte> haystack, ReadOnlySpan<byte> needle)
    {
        for (int i = 0; i + needle.Length <= haystack.Length; i++)
            if (haystack.Slice(i, needle.Length).SequenceEqual(needle))
                return i;
        return -1;
    }
}
