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
/// as a summary, with a round-trip verification overview. Naming and verification are toggles.
/// </summary>
public sealed class MainForm : Form
{
    private readonly ListView _list = new();
    private readonly TextBox _text = new();
    private readonly ComboBox _mode = new();
    private readonly CheckBox _verify = new();
    private readonly CheckBox _names = new();
    private readonly ToolStripStatusLabel _status = new();

    private readonly List<UcfbExtractor.Script> _scripts = new();
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
        Width = 1100;
        Height = 720;
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

        _text.Multiline = true;
        _text.ReadOnly = true;
        _text.ScrollBars = ScrollBars.Both;
        _text.WordWrap = false;
        _text.Dock = DockStyle.Fill;
        _text.Font = new Font("Consolas", 9.5f);
        _text.BackColor = Color.White;
        split.Panel2.Controls.Add(_text);

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
        var verifyAll = new ToolStripButton("Verify All") { Alignment = ToolStripItemAlignment.Right };
        verifyAll.Click += async (_, _) => await VerifyAllAsync();
        tool.Items.Add(new ToolStripLabel("View:"));
        tool.Items.Add(new ToolStripControlHost(_mode));
        tool.Items.Add(new ToolStripControlHost(_verify));
        tool.Items.Add(new ToolStripControlHost(_names));
        tool.Items.Add(verifyAll);

        var menu = new MenuStrip();
        var file = new ToolStripMenuItem("&File");
        file.DropDownItems.Add("Open &.lvl…", null, (_, _) => Open("Battlefront level (*.lvl)|*.lvl|All files (*.*)|*.*"));
        file.DropDownItems.Add("Open &script…", null, (_, _) => Open("Compiled script (*.script;*.luac)|*.script;*.luac|All files (*.*)|*.*"));
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

        // Docked controls fill from the outside in, in reverse add order; add Fill first, menu last.
        Controls.Add(split);
        Controls.Add(statusStrip);
        Controls.Add(tool);
        Controls.Add(menu);
        MainMenuStrip = menu;
    }

    // ---- loading -----------------------------------------------------------

    private void Open(string filter)
    {
        using var dlg = new OpenFileDialog { Filter = filter };
        if (dlg.ShowDialog(this) == DialogResult.OK) LoadPath(dlg.FileName);
    }

    public void LoadPath(string path)
    {
        _scripts.Clear();
        _list.Items.Clear();
        _text.Clear();
        _sourceName = Path.GetFileName(path);
        try
        {
            byte[] bytes = File.ReadAllBytes(path);
            if (path.EndsWith(".lvl", StringComparison.OrdinalIgnoreCase))
            {
                _scripts.AddRange(UcfbExtractor.EnumerateScripts(bytes));
            }
            else
            {
                var ex = UcfbExtractor.Extract(bytes);
                _scripts.Add(new UcfbExtractor.Script(ex.ScriptName ?? Path.GetFileNameWithoutExtension(path), ex.Bytecode));
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Could not open", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        foreach (var s in _scripts)
            _list.Items.Add(new ListViewItem(new[] { s.Name, "" }));

        _status.Text = $"{_sourceName} — {_scripts.Count} script(s)";
        if (_list.Items.Count > 0) _list.Items[0].Selected = true;
        _list.Focus();
    }

    // ---- rendering ---------------------------------------------------------

    private void RenderSelected()
    {
        if (_list.SelectedIndices.Count == 0) return;
        int idx = _list.SelectedIndices[0];
        var script = _scripts[idx];

        Prototype main;
        try
        {
            main = BytecodeReader.Read(script.Bytecode);
        }
        catch (Exception ex)
        {
            _text.Text = $"-- could not read bytecode: {ex.Message}";
            SetRowStatus(idx, "read error");
            return;
        }

        string mode = _mode.SelectedItem as string ?? "Decompiled Lua";
        switch (mode)
        {
            case "Listing (luac -l)":
                _text.Text = Normalize(Disassembler.RenderListing(main));
                break;
            case "Summary":
                _text.Text = Normalize(BuildSummary(script, main));
                break;
            default:
                RenderDecompiled(idx, script, main);
                break;
        }
        _text.SelectionStart = 0;
        _text.ScrollToCaret();
    }

    private void RenderDecompiled(int idx, UcfbExtractor.Script script, Prototype main)
    {
        string lua = LuaEmitter.Emit(main, _names.Checked ? _dict : null);
        string header = "";
        if (_verify.Checked)
        {
            var result = VerifyScript(script, lua);
            SetRowStatus(idx, ShortStatus(result));
            _status.Text = $"{_sourceName} — {script.Name}: {result}";
            header = $"-- {script.Name}: {result}\n\n";
        }
        else
        {
            _status.Text = $"{_sourceName} — {script.Name}";
        }
        _text.Text = Normalize(header + lua);
    }

    private Verifier.Result VerifyScript(UcfbExtractor.Script script, string lua)
    {
        string tmp = Path.Combine(Path.GetTempPath(), $"luadc_{Sanitize(script.Name)}.lua");
        try
        {
            File.WriteAllText(tmp, lua);
            var main = BytecodeReader.Read(script.Bytecode);
            return _verifier.Verify(tmp, main);
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
                    string lua = LuaEmitter.Emit(main, dict);
                    return ShortStatus(VerifyScript(script, lua));
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

    // TextBox wants CRLF for line breaks; the emitter uses LF.
    private static string Normalize(string s) => s.Replace("\r\n", "\n").Replace("\n", "\r\n");
}
