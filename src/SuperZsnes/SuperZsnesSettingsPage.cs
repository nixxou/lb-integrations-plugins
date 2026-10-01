// The SUPER ZSNES tab of the pack's configuration window - the pack's own settings, for every game - and the
// same page for ONE GAME'S options (its right-click window, SuperZsnesGameMenu): which options it shows is
// the catalogue's scope (SuperZsnesOptions.ScopeOf). Nixx-Menus owns the configuration window (its Tools
// menu entry "Nixx Integration Plugins Configuration..."), this plugin builds what is inside its own tab.
//
// THE CONTRACT IS A NAME, as for Vita3K (src\Menus\Settings.cs has it): a public static class
// LbIntegrations.<assembly name>.Settings with Title, Control CreatePage(), string Save(Control).
//
// ONE CONTROL PER OPTION, AND IT SHOWS THE STATE (Mehdi, 01/10 - the first version had a tick to override
// AND an editor, which said one thing while the help said another). Two kinds of row:
//   - the pack's own settings (the "Integration" group: BepInEx, the log, Escape, the menu key...): they
//     have a known default, so the control shows the value in use - "on (default)" ticked, for instance.
//     Only what differs from the default is written;
//   - the emulator's options: until set here, they are SUPER ZSNES's own, and the control says so - a
//     three-state box reading "SUPER ZSNES's own" / "on" / "off", a list whose first entry is
//     "<SUPER ZSNES's own>", a field left empty (its grey hint says so). Anything else goes on the
//     command line at every launch. A switch that is nothing unless sent (--loadstate, -popupwindow, the
//     primary display) is a plain on/off box.
// The bar at the left of each row says it at a glance: blue, passed on the command line; grey, not. The
// command line the page adds up to is shown live at the bottom, so what will be passed is never a guess.

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace LbIntegrations.SuperZsnes
{
    /// <summary>What Nixx-Menus calls, by name. Public, its signatures fixed.</summary>
    public static class Settings
    {
        public static string Title => "SUPER ZSNES";

        public static Control CreatePage()
            => new SuperZsnesSettingsPage(o => o.Scope == OptionScope.Global, SuperZsnesSettings.Read(),
                   "What the pack's plugin does inside SUPER ZSNES, for every game. A game's own options - window, display, "
                   + "gameplay, audio - are in its right-click menu: Nixx-SuperZSNES : Options...",
                   deploy: true, where: "Settings file: " + SuperZsnesSettings.SettingsPath);

        public static string Save(Control page)
        {
            if (!(page is SuperZsnesSettingsPage ours)) return "this is not the SUPER ZSNES page";
            try
            {
                var problem = ours.Problem();
                if (problem != null) return problem;
                SuperZsnesSettings.WriteAll(ours.Values());
                return null;
            }
            catch (Exception ex) { return "the settings could not be written: " + ex.Message; }
        }
    }

    internal sealed class SuperZsnesSettingsPage : UserControl
    {
        private const string Own = "SUPER ZSNES's own";
        private static readonly Color Passed = Color.FromArgb(0, 120, 215), NotPassed = Color.FromArgb(175, 175, 175);

        /// <summary>How a row is edited - see the header.</summary>
        private enum Shape { Ours, Switch, Tri, Choice, Field }

        private sealed class Row
        {
            public Option Option;
            public Shape Shape;
            public Control Editor;
            public Panel Bar;
        }

        private readonly List<Row> _rows = new List<Row>();

        /// <summary>What SUPER ZSNES runs on when nothing is passed, by IniKey - read off its files (SuperZsnesCurrent).</summary>
        private static Dictionary<string, string> _current = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>"SUPER ZSNES's own", with its value when it is known.</summary>
        private static string OwnOf(Option o) => _current.TryGetValue(o.IniKey, out var v) && !string.IsNullOrEmpty(v) ? Own + ": " + v : Own;
        private readonly TextBox _line;
        private readonly Label _count;

        /// <summary>The options <paramref name="show"/> picks, with <paramref name="saved"/>'s values; the in-process plugin's
        /// box under them when <paramref name="deploy"/>; <paramref name="where"/> at the bottom.</summary>
        public SuperZsnesSettingsPage(Func<Option, bool> show, Dictionary<string, string> saved, string intro, bool deploy, string where,
                                      Dictionary<string, string> running = null)
        {
            saved = saved ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            _current = running ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var top = new Label { Dock = DockStyle.Top, Height = 52, Padding = new Padding(12, 8, 12, 0), ForeColor = SystemColors.GrayText, Text = intro };

            var scroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(8) };
            var stack = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink };
            scroll.Controls.Add(stack);

            foreach (var group in SuperZsnesOptions.All.Where(show).GroupBy(o => o.Group))
            {
                // The table is PLACED, not docked: a docked child in a group box that sizes itself on its children
                // is sized on the box - neither had a width, and each group came out a sliver (01/10).
                var box = Group(group.Key);
                var table = Table(12, 240, 214);
                foreach (var o in group)
                {
                    saved.TryGetValue(o.IniKey, out var current);
                    var row = new Row { Option = o, Shape = ShapeOf(o) };
                    row.Editor = Editor(row, current);
                    row.Bar = new Panel { Width = 4, Height = 18, Margin = new Padding(0, 6, 8, 0) };
                    var label = new Label { Text = o.Label, AutoSize = true, MaximumSize = new Size(236, 0), Margin = new Padding(0, 7, 4, 0) };
                    new ToolTip().SetToolTip(label, o.IniKey);
                    Hook(row.Editor, () => { Show(row); Refresh(); });
                    Show(row);

                    int r = table.RowCount++;
                    table.Controls.Add(row.Bar, 0, r);
                    table.Controls.Add(label, 1, r);
                    table.Controls.Add(row.Editor, 2, r);
                    if (!string.IsNullOrEmpty(o.Help))
                    {
                        var help = new Label { Text = o.Help, AutoSize = true, MaximumSize = new Size(450, 0), ForeColor = SystemColors.GrayText, Margin = new Padding(0, 0, 0, 6) };
                        int h = table.RowCount++;
                        table.Controls.Add(help, 1, h);
                        table.SetColumnSpan(help, 2);
                    }
                    _rows.Add(row);
                }
                box.Controls.Add(table);
                stack.Controls.Add(box);
            }

            if (deploy) stack.Controls.Add(DeployBox());

            var bottom = new Panel { Dock = DockStyle.Bottom, Height = 132, Padding = new Padding(12, 4, 12, 8) };
            var legend = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 22, WrapContents = false };
            void Item(Color c, string text)
            {
                legend.Controls.Add(new Panel { BackColor = c, Size = new Size(4, 14), Margin = new Padding(0, 3, 4, 0) });
                legend.Controls.Add(new Label { Text = text, AutoSize = true, Margin = new Padding(0, 2, 14, 0) });
            }
            Item(Passed, "Passed on the command line");
            Item(NotPassed, "Not passed: " + Own + ", or the pack's default");
            _count = new Label { Dock = DockStyle.Top, Height = 20, ForeColor = SystemColors.GrayText };
            _line = new TextBox { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Font = new Font("Consolas", 9f), BackColor = SystemColors.Window };
            // A text box, not a label: a long path in a label wraps at its first space and the line is lost (01/10).
            var file = new TextBox { Dock = DockStyle.Bottom, ReadOnly = true, BorderStyle = BorderStyle.None, BackColor = SystemColors.Control,
                                     ForeColor = SystemColors.GrayText, Text = where ?? "", TabStop = false };
            bottom.Controls.Add(_line);
            bottom.Controls.Add(_count);
            bottom.Controls.Add(legend);
            bottom.Controls.Add(file);

            Controls.Add(scroll);
            Controls.Add(top);
            Controls.Add(bottom);
            Refresh();
        }

        // ── the rows ─────────────────────────────────────────────────────────

        private static Shape ShapeOf(Option o)
        {
            if (o.Family == OptionFamily.Plugin && o.Key != "display") return Shape.Ours;
            if (o.Kind == OptionKind.Bool)
                return o.Family == OptionFamily.Setting || o.Family == OptionFamily.Game ? Shape.Tri : Shape.Switch;
            return o.Kind == OptionKind.Choice ? Shape.Choice : Shape.Field;
        }

        private static bool OnByDefault(Option o) => SuperZsnesSettings.IsTrue(o.Default);

        private static Control Editor(Row row, string current)
        {
            var o = row.Option;
            switch (row.Shape)
            {
                case Shape.Ours when o.Kind == OptionKind.Bool:
                    return new CheckBox { AutoSize = true, Checked = current != null ? SuperZsnesSettings.IsTrue(current) : OnByDefault(o), Margin = new Padding(3, 6, 0, 0) };
                case Shape.Ours:
                    return Field(string.IsNullOrEmpty(current) ? o.Default ?? "" : current, o.Default != null ? "the default, " + o.Default : null);
                case Shape.Switch:
                    return new CheckBox { AutoSize = true, Checked = current != null && SuperZsnesSettings.IsTrue(current), Margin = new Padding(3, 6, 0, 0) };
                case Shape.Tri:
                    return new CheckBox
                    {
                        AutoSize = true, ThreeState = true, Margin = new Padding(3, 6, 0, 0),
                        CheckState = current == null ? CheckState.Indeterminate : SuperZsnesSettings.IsTrue(current) ? CheckState.Checked : CheckState.Unchecked,
                    };
                case Shape.Choice:
                {
                    var c = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 200, Margin = new Padding(3, 3, 0, 0) };
                    c.Items.Add("<" + OwnOf(o) + ">");
                    c.Items.AddRange(o.Choices);
                    c.SelectedIndex = 1 + Array.FindIndex(o.Choices, x => string.Equals(x, current, StringComparison.OrdinalIgnoreCase));
                    return c;
                }
                default:
                    return Field(current ?? "", (_current.ContainsKey(o.IniKey) ? OwnOf(o) : Own + (o.Default != null ? ": " + o.Default : ""))
                                               + (o.Kind != OptionKind.Text ? " (" + Range(o) + ")" : ""));
            }
        }

        private static string Range(Option o)
            => o.Min.ToString(CultureInfo.InvariantCulture) + " to " + o.Max.ToString(CultureInfo.InvariantCulture);

        /// <summary>A text field with a grey hint while it is empty - what an empty one means.</summary>
        private static TextBox Field(string text, string hint)
        {
            var t = new TextBox { Text = text, Width = 200, Margin = new Padding(3, 3, 0, 0) };
            if (hint != null) t.HandleCreated += (_, _) => SendMessage(t.Handle, 0x1501 /* EM_SETCUEBANNER */, (IntPtr)1, hint);
            return t;
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, string lParam);

        private static void Hook(Control editor, Action changed)
        {
            switch (editor)
            {
                case CheckBox c: c.CheckStateChanged += (_, _) => changed(); break;
                case ComboBox b: b.SelectedIndexChanged += (_, _) => changed(); break;
                case TextBox t: t.TextChanged += (_, _) => changed(); break;
            }
        }

        /// <summary>The row's box says its state in words, and its bar whether it is passed.</summary>
        private static void Show(Row row)
        {
            if (row.Editor is CheckBox c)
            {
                bool on = c.CheckState == CheckState.Checked;
                c.Text = row.Shape == Shape.Tri && c.CheckState == CheckState.Indeterminate ? OwnOf(row.Option)
                       : (on ? "on" : "off") + (row.Shape == Shape.Ours && on == OnByDefault(row.Option) ? " (default)" : "");
            }
            if (row.Bar != null) row.Bar.BackColor = ValueOf(row) != null ? Passed : NotPassed;
        }

        /// <summary>What the row writes, or null for nothing: the emulator's own value, or the pack's default.</summary>
        private static string ValueOf(Row row)
        {
            var o = row.Option;
            switch (row.Editor)
            {
                case CheckBox c when row.Shape == Shape.Tri:
                    return c.CheckState == CheckState.Indeterminate ? null : c.Checked ? "true" : "false";
                case CheckBox c when row.Shape == Shape.Ours:
                    return c.Checked == OnByDefault(o) ? null : c.Checked ? "true" : "false";
                case CheckBox c:
                    return c.Checked ? "true" : null;
                case ComboBox b:
                    return b.SelectedIndex <= 0 ? null : b.SelectedItem?.ToString();
                case TextBox t:
                {
                    var v = t.Text.Trim();
                    if (v.Length == 0) return null;
                    if (row.Shape == Shape.Ours && string.Equals(v, o.Default, StringComparison.OrdinalIgnoreCase)) return null;
                    return v;
                }
            }
            return null;
        }

        /// <summary>What Save writes: the rows that pass something.</summary>
        public Dictionary<string, string> Values()
        {
            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var row in _rows)
            {
                var v = ValueOf(row);
                if (v != null) values[row.Option.IniKey] = v;
            }
            return values;
        }

        /// <summary>Why the page cannot be saved, or null.</summary>
        public string Problem()
        {
            foreach (var row in _rows)
            {
                var o = row.Option;
                if (!(row.Editor is TextBox t)) continue;
                var v = t.Text.Trim();
                if (row.Shape == Shape.Ours && v.Length == 0)
                    return "\"" + o.Label + "\" is empty. Type a key, or " + o.Default + " for the default.";
                if (v.Length == 0 || o.Kind == OptionKind.Text) continue;
                if (!decimal.TryParse(v.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var d) || d < o.Min || d > o.Max
                    || (o.Kind == OptionKind.Int && d != decimal.Truncate(d)))
                    return "\"" + o.Label + "\": " + v + " is not " + (o.Kind == OptionKind.Int ? "a whole number" : "a number") + " from " + Range(o)
                           + ". Empty it to leave " + Own + " value.";
            }
            return null;
        }

        private new void Refresh()
        {
            if (_line == null) return;
            var flags = SuperZsnesSettings.Flags(Values());
            _count.Text = flags.Count == 0 ? "Nothing added to the command line." : flags.Count + " option(s) added to the command line, before the ROM path:";
            _line.Text = string.Join(" ", flags.Select(f => f.Text));
        }

        // ── the in-process plugin, per emulator ──────────────────────────────

        /// <summary>The in-process plugin's state in every SUPER ZSNES the library knows, and a button
        /// to install or repair it there now - the way to get BepInEx into an emulator that was
        /// installed before this plugin, or by hand. The download runs on a worker thread; the label
        /// shows the steps.</summary>
        private GroupBox DeployBox()
        {
            var box = Group("In-process plugin (BepInEx " + SuperZsnesBepInEx.Build + ")");
            var table = Table(340, 136);

            var exes = KnownExecutables();
            if (exes.Count == 0)
            {
                table.Controls.Add(new Label { Text = "No SUPER ZSNES emulator entry in the library yet. Install it from Add Emulator: BepInEx goes in with it.", AutoSize = true, MaximumSize = new Size(470, 0), ForeColor = SystemColors.GrayText, Margin = new Padding(0, 6, 0, 6) }, 0, 0);
                table.SetColumnSpan(table.Controls[0], 2);
            }
            foreach (var exe in exes)
            {
                var exeDir = System.IO.Path.GetDirectoryName(exe);
                int r = table.RowCount++;
                var where = new Label { Text = exeDir, AutoSize = true, MaximumSize = new Size(470, 0), Margin = new Padding(0, 9, 0, 0) };
                var status = new Label { Text = SuperZsnesBepInEx.Describe(exeDir), AutoSize = true, MaximumSize = new Size(330, 0), ForeColor = SystemColors.GrayText, Margin = new Padding(0, 4, 0, 6) };
                var button = new Button { Text = SuperZsnesBepInEx.IsDeployed(exeDir) ? "Repair / refresh" : "Install now", AutoSize = true, Margin = new Padding(3, 4, 0, 4) };
                button.Click += (_, _) =>
                {
                    button.Enabled = false;
                    status.Text = "working...";
                    var worker = new System.Threading.Thread(() =>
                    {
                        var result = SuperZsnesBepInEx.Deploy(exe, (m, p) => SafeSet(status, m + (p.HasValue ? " " + (int)(p.Value * 100) + "%" : "")), () => false);
                        var text = result.Ok
                            ? (result.Changed ? string.Join("; ", result.Steps) : "already in place") + ". " + SuperZsnesBepInEx.Describe(exeDir)
                            : "failed: " + result.Problem;
                        SafeSet(status, text);
                        try { button.BeginInvoke(new Action(() => { button.Enabled = true; button.Text = "Repair / refresh"; })); } catch { }
                    }) { IsBackground = true };
                    worker.Start();
                };
                // The folder on its own line, its state and the button under it.
                table.Controls.Add(where, 0, r);
                table.SetColumnSpan(where, 2);
                int s = table.RowCount++;
                table.Controls.Add(status, 0, s);
                table.Controls.Add(button, 1, s);
            }
            box.Controls.Add(table);
            return box;
        }

        /// <summary>A group box that sizes itself on the table in it.</summary>
        private static GroupBox Group(string title)
            => new GroupBox { Text = title, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(8, 4, 8, 8), Margin = new Padding(4, 4, 4, 10) };

        /// <summary>A table of fixed columns, PLACED under the group's caption - never docked, see the constructor.</summary>
        private static TableLayoutPanel Table(params int[] widths)
        {
            var table = new TableLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = widths.Length, Location = new Point(8, 20) };
            foreach (var w in widths) table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, w));
            return table;
        }

        private static void SafeSet(Label label, string text)
        {
            try
            {
                if (label.IsHandleCreated) label.BeginInvoke(new Action(() => label.Text = text));
                else label.Text = text;
            }
            catch { }
        }

        /// <summary>Every SUPER ZSNES executable the library points at, resolved. Empty under a host
        /// with no data manager, such as the probe.</summary>
        private static List<string> KnownExecutables()
        {
            var found = new List<string>();
            try
            {
                var dm = Unbroken.LaunchBox.Plugins.PluginHelper.DataManager;
                if (dm == null) return found;
                foreach (var emu in dm.GetAllEmulators())
                {
                    string path;
                    try { path = emu.ApplicationPath; } catch { continue; }
                    if (!SuperZsnesPaths.IsSuperZsnesExecutable(path)) continue;
                    var full = SuperZsnesPlugin.ResolveFullPathForUi(path);
                    if (full != null && System.IO.File.Exists(full) && !found.Contains(full, StringComparer.OrdinalIgnoreCase)) found.Add(full);
                }
            }
            catch (Exception ex) { Log.Warn("listing the emulators", ex); }
            return found;
        }
    }
}
