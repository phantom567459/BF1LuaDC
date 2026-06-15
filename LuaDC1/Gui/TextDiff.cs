namespace LuaDC1.Gui;

/// <summary>
/// Line-level diff for the side-by-side compare view: returns, for each side, which lines are not
/// part of the longest common subsequence (i.e. the lines that differ). Falls back to a positional
/// comparison for very large inputs to bound memory.
/// </summary>
internal static class TextDiff
{
    public static (bool[] left, bool[] right) DiffLines(string[] a, string[] b)
    {
        var leftDiff = new bool[a.Length];
        var rightDiff = new bool[b.Length];

        if ((long)a.Length * b.Length > 4_000_000)
        {
            // Too big for the LCS table — mark anything that doesn't match positionally.
            for (int i = 0; i < a.Length; i++) leftDiff[i] = i >= b.Length || a[i] != b[i];
            for (int j = 0; j < b.Length; j++) rightDiff[j] = j >= a.Length || a[j] != b[j];
            return (leftDiff, rightDiff);
        }

        int n = a.Length, m = b.Length;
        var dp = new int[n + 1, m + 1];
        for (int i = n - 1; i >= 0; i--)
            for (int j = m - 1; j >= 0; j--)
                dp[i, j] = a[i] == b[j] ? dp[i + 1, j + 1] + 1 : Math.Max(dp[i + 1, j], dp[i, j + 1]);

        Array.Fill(leftDiff, true);
        Array.Fill(rightDiff, true);
        int x = 0, y = 0;
        while (x < n && y < m)
        {
            if (a[x] == b[y]) { leftDiff[x] = false; rightDiff[y] = false; x++; y++; }
            else if (dp[x + 1, y] >= dp[x, y + 1]) x++;
            else y++;
        }
        return (leftDiff, rightDiff);
    }

    public static int Count(bool[] flags)
    {
        int c = 0;
        foreach (bool f in flags) if (f) c++;
        return c;
    }
}
