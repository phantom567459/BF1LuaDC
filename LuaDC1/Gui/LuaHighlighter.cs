using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace LuaDC1.Gui;

/// <summary>
/// Lightweight Lua syntax colouring for a <see cref="RichTextBox"/>: one tokenizing pass colours
/// comments, strings, numbers and keywords, with redraw suspended so even large scripts paint in
/// one go. Optionally tints whole lines (used by the side-by-side compare view to mark diffs).
/// </summary>
internal static partial class LuaHighlighter
{
    private static readonly Color Comment = Color.FromArgb(0, 128, 0);
    private static readonly Color StringLit = Color.FromArgb(163, 21, 21);
    private static readonly Color Keyword = Color.FromArgb(0, 0, 205);
    private static readonly Color Number = Color.FromArgb(9, 134, 134);
    private static readonly Color Default = Color.Black;

    [GeneratedRegex(
        @"(?<comment>--[^\n]*)" +
        @"|(?<string>""(?:\\.|[^""\\])*""|'(?:\\.|[^'\\])*')" +
        @"|(?<number>\b\d+(?:\.\d+)?\b)" +
        @"|(?<keyword>\b(?:and|break|do|else|elseif|end|for|function|if|in|local|nil|not|or|repeat|return|then|until|while)\b)")]
    private static partial Regex Tokens();

    /// <summary>Set the text and apply Lua colouring; optionally tint the given line indices.</summary>
    public static void Apply(RichTextBox rtb, string text, bool[]? diffLines = null, Color diffColor = default)
    {
        BeginUpdate(rtb);
        try
        {
            rtb.Clear();
            rtb.Text = text;
            rtb.Select(0, text.Length);
            rtb.SelectionColor = Default;
            rtb.SelectionBackColor = rtb.BackColor;

            foreach (Match m in Tokens().Matches(text))
            {
                Color c = m.Groups["comment"].Success ? Comment
                        : m.Groups["string"].Success ? StringLit
                        : m.Groups["number"].Success ? Number
                        : Keyword;
                rtb.Select(m.Index, m.Length);
                rtb.SelectionColor = c;
            }

            if (diffLines != null)
                TintLines(rtb, diffLines, diffColor);

            rtb.Select(0, 0);
        }
        finally
        {
            EndUpdate(rtb);
        }
    }

    private static void TintLines(RichTextBox rtb, bool[] diffLines, Color color)
    {
        int lineCount = rtb.Lines.Length;
        for (int i = 0; i < diffLines.Length && i < lineCount; i++)
        {
            if (!diffLines[i]) continue;
            int start = rtb.GetFirstCharIndexFromLine(i);
            if (start < 0) continue;
            int len = rtb.Lines[i].Length;
            rtb.Select(start, len);
            rtb.SelectionBackColor = color;
        }
    }

    // ---- redraw suspension (avoids per-token flicker on large scripts) -----

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    private const int WM_SETREDRAW = 0x000B;

    private static void BeginUpdate(Control c) => SendMessage(c.Handle, WM_SETREDRAW, IntPtr.Zero, IntPtr.Zero);

    private static void EndUpdate(Control c)
    {
        SendMessage(c.Handle, WM_SETREDRAW, new IntPtr(1), IntPtr.Zero);
        c.Invalidate();
    }
}
