using System.Text;
using LuaDC1.Decompile;
using LuaDC1.Format;
using LuaDC1.IO;
using LuaDC1.Naming;
using LuaDC1.Verify;

namespace LuaDC1.Gui;

/// <summary>
/// A small Windows GUI for the decompiler, modeled on BAD-AL's special_unluac: open a .lvl (or a
/// single .script), pick a script from the list, and view it decompiled / as a luac-style listing /
/// as a summary, with Lua syntax highlighting and a round-trip verification overview. A second
/// compiled source can be loaded to diff the decompiled output side-by-side.
/// </summary>
public sealed class MainForm : Form
{
    private static readonly Color DiffLeftColor = Color.FromArgb(255, 230, 230);   // lines only on the left
    private static readonly Color DiffRightColor = Color.FromArgb(228, 245, 230);  // lines only on the right

    private readonly ListView _list = new();
    private readonly RichTextBox _text = new();
    private readonly RichTextBox _compareText = new();
    private readonly SplitContainer _outputSplit = new();
    private readonly Label _leftHeader = new();
    private readonly Label _rightHeader = new();
    private readonly ComboBox _mode = new();
    private readonly CheckBox _verify = new();
    private readonly CheckBox _names = new();
    private readonly ToolStripStatusLabel _status = new();

    private readonly List<UcfbExtractor.Script> _scripts = new();
    private List<UcfbExtractor.Script>? _compareScripts;
    private string _compareName = "";
    private readonly NameDictionary _dict = NameDictionary.LoadDefault();
    private readonly Verifier _verifier = new(null);
    private string _sourceName = "";

    public static int Run(string[] args)
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        var form = new MainForm();
        if (args.Length > 0 && File.Exists(args[0])) form.LoadPath(args[0]);
        Application.Run(form);
        return 0;
    }

    public MainForm()
    {
        Text = "LuaDC1 — Star Wars Battlefront Lua 4.0 Decompiler";
        Width = 1200;
        Height = 760;
        StartPosition = FormStartPosition.CenterScreen;

        var split = new SplitContainer { Dock = DockStyle.Fill, SplitterDistance = 280 };

        _list.View = View.Details;
        _list.FullRowSelect = true;
        _list.HideSelection = false;
        _list.MultiSelect = false;
        _list.Dock = DockStyle.Fill;
        _list.Columns.Add("Script", 180);
        _list.Columns.Add("Round-trip", 95);
        _list.SelectedIndexChanged += (_, _) => RenderSelected();
        split.Panel1.Controls.Add(_list);

        // Right side: two stacked panes (current | comparison); comparison hidden until used.
        _outputSplit.Dock = DockStyle.Fill;
        _outputSplit.Orientation = Orientation.Vertical;
        _outputSplit.Panel2Collapsed = true;
        _outputSplit.Panel1.Controls.Add(BuildPane(_text, _leftHeader, "Decompiled"));
        _outputSplit.Panel2.Controls.Add(BuildPane(_compareText, _rightHeader, "Comparison"));
        split.Panel2.Controls.Add(_outputSplit);

        var tool = new ToolStrip { GripStyle = ToolStripGripStyle.Hidden };
        _mode.DropDownStyle = ComboBoxStyle.DropDownList;
        _mode.Width = 150;
        _mode.Items.AddRange(new object[] { "Decompiled Lua", "Listing (luac -l)", "Summary" });
        _mode.SelectedIndex = 0;
        _mode.SelectedIndexChanged += (_, _) => RenderSelected();
        _verify.Text = "Verify";
        _verify.Checked = true;
        _verify.CheckedChanged += (_, _) => RenderSelected();
        _names.Text = "Names";
        _names.Checked = true;
        _names.CheckedChanged += (_, _) => RenderSelected();
        var verifyAll = new ToolStripButton("Verify All");
        verifyAll.Click += async (_, _) => await VerifyAllAsync();
        var compare = new ToolStripButton("Compare…") { Alignment = ToolStripItemAlignment.Right };
        compare.Click += (_, _) => CompareWith();
        tool.Items.Add(new ToolStripLabel("View:"));
        tool.Items.Add(new ToolStripControlHost(_mode));
        tool.Items.Add(new ToolStripControlHost(_verify));
        tool.Items.Add(new ToolStripControlHost(_names));
        tool.Items.Add(verifyAll);
        tool.Items.Add(compare);

        var menu = new MenuStrip();
        var file = new ToolStripMenuItem("&File");
        file.DropDownItems.Add("Open &.lvl…", null, (_, _) => Open("Battlefront level (*.lvl)|*.lvl|All files (*.*)|*.*", false));
        file.DropDownItems.Add("Open &script…", null, (_, _) => Open("Compiled script (*.script;*.luac)|*.script;*.luac|All files (*.*)|*.*", false));
        file.DropDownItems.Add(new ToolStripSeparator());
        file.DropDownItems.Add("&Compare with .lvl/.script…", null, (_, _) => CompareWith());
        file.DropDownItems.Add("Close c&omparison", null, (_, _) => CloseComparison());
        file.DropDownItems.Add(new ToolStripSeparator());
        file.DropDownItems.Add("&Save current .lua…", null, (_, _) => SaveCurrent());
        file.DropDownItems.Add(new ToolStripSeparator());
        file.DropDownItems.Add("E&xit", null, (_, _) => Close());
        menu.Items.Add(file);

        var statusStrip = new StatusStrip();
        _status.Text = "Open a .lvl or .script to begin.";
        _status.Spring = true;
        _status.TextAlign = ContentAlignment.MiddleLeft;
        statusStrip.Items.Add(_status);

        Controls.Add(split);
        Controls.Add(statusStrip);
        Controls.Add(tool);
        Controls.Add(menu);
        MainMenuStrip = menu;
    }

    private static Control BuildPane(RichTextBox rtb, Label header, string title)
    {
        var host = new Panel { Dock = DockStyle.Fill };
        rtb.Multiline = true;
        rtb.ReadOnly = true;
        rtb.WordWrap = false;
        rtb.ScrollBars = RichTextBoxScrollBars.Both;
        rtb.Dock = DockStyle.Fill;
        rtb.Font = new Font("Consolas", 9.5f);
        rtb.BackColor = Color.White;
        rtb.DetectUrls = false;
        header.Text = title;
        header.Dock = DockStyle.Top;
        header.Height = 20;
        header.TextAlign = ContentAlignment.MiddleLeft;
        header.BackColor = SystemColors.ControlLight;
        header.Padding = new Padding(4, 0, 0, 0);
        host.Controls.Add(rtb);
        host.Controls.Add(header);
        return host;
    }

    // ---- loading -----------------------------------------------------------

    private void Open(string filter, bool _)
    {
        using var dlg = new OpenFileDialog { Filter = filter };
        if (dlg.ShowDialog(this) == DialogResult.OK) LoadPath(dlg.FileName);
    }

    public void LoadPath(string path)
    {
        _scripts.Clear();
        _list.Items.Clear();
        _text.Clear();
        CloseComparison();
        _sourceName = Path.GetFileName(path);
        try
        {
            _scripts.AddRange(LoadScripts(path));
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Could not open", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        foreach (var s in _scripts)
            _list.Items.Add(new ListViewItem(new[] { s.Name, "" }));

        _leftHeader.Text = _sourceName;
        _status.Text = $"{_sourceName} — {_scripts.Count} script(s)";
        if (_list.Items.Count > 0) _list.Items[0].Selected = true;
        _list.Focus();
    }

    private static List<UcfbExtractor.Script> LoadScripts(string path)
    {
        byte[] bytes = File.ReadAllBytes(path);
        if (path.EndsWith(".lvl", StringComparison.OrdinalIgnoreCase))
            return UcfbExtractor.EnumerateScripts(bytes);
        var ex = UcfbExtractor.Extract(bytes);
        return new List<UcfbExtractor.Script> { new(ex.ScriptName ?? Path.GetFileNameWithoutExtension(path), ex.Bytecode) };
    }

    // ---- comparison --------------------------------------------------------

    private void CompareWith()
    {
        if (_scripts.Count == 0) { _status.Text = "Open a .lvl/.script first, then choose something to compare against."; return; }
        using var dlg = new OpenFileDialog { Filter = "Compiled (*.lvl;*.script;*.luac)|*.lvl;*.script;*.luac|All files (*.*)|*.*" };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            _compareScripts = LoadScripts(dlg.FileName);
            _compareName = Path.GetFileName(dlg.FileName);
            _rightHeader.Text = _compareName;
            _outputSplit.Panel2Collapsed = false;
            RenderSelected();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Could not open comparison", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void CloseComparison()
    {
        _compareScripts = null;
        _compareName = "";
        _compareText.Clear();
        _outputSplit.Panel2Collapsed = true;
        if (_scripts.Count > 0) RenderSelected();
    }

    private UcfbExtractor.Script? FindMatch(UcfbExtractor.Script current)
    {
        if (_compareScripts == null) return null;
        if (_compareScripts.Count == 1) return _compareScripts[0];
        foreach (var s in _compareScripts) if (s.Name == current.Name) return s;
        return null;
    }

    // ---- rendering ---------------------------------------------------------

    private void RenderSelected()
    {
        if (_list.SelectedIndices.Count == 0) return;
        int idx = _list.SelectedIndices[0];
        var current = _scripts[idx];
        string mode = _mode.SelectedItem as string ?? "Decompiled Lua";
        string textA = RenderText(current, mode);

        if (_compareScripts != null)
        {
            var match = FindMatch(current);
            if (match == null)
            {
                LuaHighlighter.Apply(_text, textA);
                _compareText.Clear();
                _compareText.Text = $"(no script named \"{current.Name}\" in {_compareName})";
                _status.Text = $"{current.Name}: no matching script in {_compareName}";
                return;
            }
            string textB = RenderText(match.Value, mode);
            var (leftDiff, rightDiff) = TextDiff.DiffLines(textA.Split('\n'), textB.Split('\n'));
            LuaHighlighter.Apply(_text, textA, leftDiff, DiffLeftColor);
            LuaHighlighter.Apply(_compareText, textB, rightDiff, DiffRightColor);
            int n = TextDiff.Count(leftDiff) + TextDiff.Count(rightDiff);
            _status.Text = n == 0
                ? $"{current.Name}: identical to {_compareName}"
                : $"{current.Name}: {n} differing line(s) vs {_compareName}";
            return;
        }

        LuaHighlighter.Apply(_text, textA);
        if (_verify.Checked && mode == "Decompiled Lua")
        {
            var result = VerifyScript(current, textA);
            SetRowStatus(idx, ShortStatus(result));
            _status.Text = $"{_sourceName} — {current.Name}: {result}";
        }
        else
        {
            _status.Text = $"{_sourceName} — {current.Name}";
        }
    }

    private string RenderText(UcfbExtractor.Script script, string mode)
    {
        try
        {
            var main = BytecodeReader.Read(script.Bytecode);
            string text = mode switch
            {
                "Listing (luac -l)" => Disassembler.RenderListing(main),
                "Summary" => BuildSummary(script, main),
                _ => LuaEmitter.Emit(main, _names.Checked ? _dict : null),
            };
            return text.Replace("\r\n", "\n").Replace('\r', '\n');
        }
        catch (Exception ex)
        {
            return $"-- could not decompile {script.Name}: {ex.Message}";
        }
    }

    private Verifier.Result VerifyScript(UcfbExtractor.Script script, string lua)
    {
        string tmp = Path.Combine(Path.GetTempPath(), $"luadc_{Sanitize(script.Name)}.lua");
        try
        {
            File.WriteAllText(tmp, lua);
            return _verifier.Verify(tmp, BytecodeReader.Read(script.Bytecode));
        }
        finally
        {
            try { if (File.Exists(tmp)) File.Delete(tmp); } catch { /* best effort */ }
        }
    }

    private static string BuildSummary(UcfbExtractor.Script script, Prototype main)
    {
        var funcs = Disassembler.Flatten(main);
        var sb = new StringBuilder();
        sb.Append($"Script:     {script.Name}\n");
        sb.Append($"Bytecode:   {script.Bytecode.Length} bytes\n");
        sb.Append($"Functions:  {funcs.Count}\n\n");
        sb.Append("  #   instructions\n");
        sb.Append("  --  ------------\n");
        for (int i = 0; i < funcs.Count; i++)
            sb.Append($"  {i,-3} {funcs[i].Instrs.Count,6}{(funcs[i].IsMain ? "   (main)" : "")}\n");
        return sb.ToString();
    }

    // ---- verify all --------------------------------------------------------

    private async Task VerifyAllAsync()
    {
        if (_scripts.Count == 0 || !_verifier.LuacAvailable)
        {
            if (!_verifier.LuacAvailable) _status.Text = "luac.exe not found — cannot verify.";
            return;
        }

        Cursor = Cursors.WaitCursor;
        var dict = _names.Checked ? _dict : null;
        var snapshot = _scripts.ToArray();
        for (int i = 0; i < snapshot.Length; i++)
        {
            var script = snapshot[i];
            string statusText = await Task.Run(() =>
            {
                try
                {
                    var main = BytecodeReader.Read(script.Bytecode);
                    return ShortStatus(VerifyScript(script, LuaEmitter.Emit(main, dict)));
                }
                catch { return "error"; }
            });
            SetRowStatus(i, statusText);
            _status.Text = $"Verifying… {i + 1}/{snapshot.Length}";
            Application.DoEvents();
        }

        int ok = 0;
        foreach (ListViewItem item in _list.Items) if (item.SubItems[1].Text == "exact") ok++;
        _status.Text = $"{_sourceName} — {ok}/{_scripts.Count} scripts round-trip exactly";
        Cursor = Cursors.Default;
    }

    // ---- helpers -----------------------------------------------------------

    private void SaveCurrent()
    {
        if (_list.SelectedIndices.Count == 0) return;
        var script = _scripts[_list.SelectedIndices[0]];
        using var dlg = new SaveFileDialog { Filter = "Lua (*.lua)|*.lua", FileName = Sanitize(script.Name) + ".lua" };
        if (dlg.ShowDialog(this) == DialogResult.OK)
            File.WriteAllText(dlg.FileName, _text.Text.Replace("\r\n", "\n"));
    }

    private void SetRowStatus(int idx, string status)
    {
        if (idx >= 0 && idx < _list.Items.Count) _list.Items[idx].SubItems[1].Text = status;
    }

    private static string ShortStatus(Verifier.Result r) => r.Status switch
    {
        Verifier.Status.Match => "exact",
        Verifier.Status.Partial => $"{r.FunctionsMatched}/{r.FunctionsTotal}",
        Verifier.Status.CompileFailed => "compile-fail",
        _ => "—",
    };

    private static string Sanitize(string name)
    {
        if (string.IsNullOrEmpty(name)) return "script";
        foreach (char c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
        return name;
    }
}
