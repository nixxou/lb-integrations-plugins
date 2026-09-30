// The SUPER ZSNES tab of the pack's configuration window. Nixx-Menus owns the window (its Tools menu
// entry "Nixx Integration Plugins Configuration..."), this plugin builds what is inside its own tab.
//
// THE CONTRACT IS A NAME, as for Vita3K (src\Menus\Settings.cs has it): a public static class
// LbIntegrations.<assembly name>.Settings with Title, Control CreatePage(), string Save(Control).
//
// One row per option of the catalogue, grouped: a tick for "override", the label, an editor of the
// option's kind, and the help in grey. Untick and the emulator's own value stands. The command line
// the ticks add up to is shown live at the bottom, so what will be passed is never a guess.

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;

namespace LbIntegrations.SuperZsnes
{
    /// <summary>What Nixx-Menus calls, by name. Public, its signatures fixed.</summary>
    public static class Settings
    {
        public static string Title => "SUPER ZSNES";

        public static Control CreatePage() => new SuperZsnesSettingsPage();

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
        private sealed class Row
        {
            public Option Option;
            public CheckBox Override;
            public Control Editor;
        }

        private readonly List<Row> _rows = new List<Row>();
        private readonly TextBox _line;
        private readonly Label _count;

        public SuperZsnesSettingsPage()
        {
            var saved = SuperZsnesSettings.Read();

            var top = new Label
            {
                Dock = DockStyle.Top, Height = 48, Padding = new Padding(12, 10, 12, 0), ForeColor = SystemColors.GrayText,
                Text = "Tick an option to pass it on the emulator's command line at every launch; unticked, the emulator's own "
                     + "value stands. --nixx-* options need the pack's BepInEx plugin inside the emulator.",
            };

            var scroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(8) };
            var stack = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink };
            scroll.Controls.Add(stack);

            foreach (var group in SuperZsnesOptions.All.GroupBy(o => o.Group))
            {
                var box = new GroupBox { Text = group.Key, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(8, 4, 8, 8), Margin = new Padding(4, 4, 4, 10) };
                var table = new TableLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 4, Dock = DockStyle.Top };
                table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 26));
                table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 250));
                table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 180));
                table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 460));
                foreach (var o in group)
                {
                    saved.TryGetValue(o.IniKey, out var current);
                    var row = new Row { Option = o };
                    row.Override = new CheckBox { Checked = !string.IsNullOrEmpty(current), Margin = new Padding(3, 6, 0, 0), AutoSize = true };
                    var label = new Label { Text = o.Label, AutoSize = true, Margin = new Padding(0, 7, 0, 0) };
                    row.Editor = Editor(o, current);
                    var help = new Label
                    {
                        Text = o.Help + (o.Default != null ? (o.Help.Length > 0 ? "  " : "") + "Default: " + o.Default + "." : ""),
                        AutoSize = true, MaximumSize = new Size(450, 0), ForeColor = SystemColors.GrayText, Margin = new Padding(0, 7, 0, 4),
                    };
                    var tip = new ToolTip();
                    tip.SetToolTip(label, o.IniKey);
                    row.Override.CheckedChanged += (_, _) => { row.Editor.Enabled = row.Override.Checked; Refresh(); };
                    row.Editor.Enabled = row.Override.Checked;
                    Hook(row.Editor, () => { if (!row.Override.Checked) row.Override.Checked = true; Refresh(); });
                    int r = table.RowCount++;
                    table.Controls.Add(row.Override, 0, r);
                    table.Controls.Add(label, 1, r);
                    table.Controls.Add(row.Editor, 2, r);
                    table.Controls.Add(help, 3, r);
                    _rows.Add(row);
                }
                box.Controls.Add(table);
                stack.Controls.Add(box);
            }

            stack.Controls.Add(DeployBox());

            var bottom = new Panel { Dock = DockStyle.Bottom, Height = 112, Padding = new Padding(12, 6, 12, 8) };
            _count = new Label { Dock = DockStyle.Top, Height = 20, ForeColor = SystemColors.GrayText };
            _line = new TextBox { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Font = new Font("Consolas", 9f), BackColor = SystemColors.Window };
            var where = new Label { Dock = DockStyle.Bottom, Height = 18, ForeColor = SystemColors.GrayText, Text = "Settings file: " + SuperZsnesSettings.SettingsPath };
            bottom.Controls.Add(_line);
            bottom.Controls.Add(_count);
            bottom.Controls.Add(where);

            Controls.Add(scroll);
            Controls.Add(top);
            Controls.Add(bottom);
            Refresh();
        }

        /// <summary>The in-process plugin's state in every SUPER ZSNES the library knows, and a button
        /// to install or repair it there now - the way to get BepInEx into an emulator that was
        /// installed before this plugin, or by hand. The download runs on a worker thread; the label
        /// shows the steps.</summary>
        private GroupBox DeployBox()
        {
            var box = new GroupBox { Text = "In-process plugin (BepInEx " + SuperZsnesBepInEx.Build + ")", AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(8, 4, 8, 8), Margin = new Padding(4, 4, 4, 10) };
            var table = new TableLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 3, Dock = DockStyle.Top };
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 440));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 300));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 170));

            var exes = KnownExecutables();
            if (exes.Count == 0)
            {
                table.Controls.Add(new Label { Text = "No SUPER ZSNES emulator entry in the library yet. Install it from Add Emulator: BepInEx goes in with it.", AutoSize = true, MaximumSize = new Size(880, 0), ForeColor = SystemColors.GrayText, Margin = new Padding(0, 6, 0, 6) }, 0, 0);
                table.SetColumnSpan(table.Controls[0], 3);
            }
            foreach (var exe in exes)
            {
                var exeDir = System.IO.Path.GetDirectoryName(exe);
                int r = table.RowCount++;
                var where = new Label { Text = exeDir, AutoSize = true, MaximumSize = new Size(430, 0), Margin = new Padding(0, 9, 0, 0) };
                var status = new Label { Text = SuperZsnesBepInEx.Describe(exeDir), AutoSize = true, MaximumSize = new Size(290, 0), ForeColor = SystemColors.GrayText, Margin = new Padding(0, 9, 0, 0) };
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
                table.Controls.Add(where, 0, r);
                table.Controls.Add(status, 1, r);
                table.Controls.Add(button, 2, r);
            }
            box.Controls.Add(table);
            return box;
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

        private static Control Editor(Option o, string current)
        {
            switch (o.Kind)
            {
                case OptionKind.Bool:
                    return new CheckBox { Text = "on", AutoSize = true, Checked = current != null && SuperZsnesSettings.IsTrue(current), Margin = new Padding(3, 6, 0, 0) };
                case OptionKind.Int:
                {
                    var n = new NumericUpDown { Minimum = o.Min, Maximum = o.Max, Width = 110, Margin = new Padding(3, 3, 0, 0) };
                    if (current != null && decimal.TryParse(current, NumberStyles.Number, CultureInfo.InvariantCulture, out var v)) n.Value = Math.Min(o.Max, Math.Max(o.Min, v));
                    return n;
                }
                case OptionKind.Float:
                {
                    var n = new NumericUpDown { Minimum = o.Min, Maximum = o.Max, DecimalPlaces = o.Decimals, Increment = 0.05m, Width = 110, Margin = new Padding(3, 3, 0, 0) };
                    if (current != null && decimal.TryParse(current.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var v)) n.Value = Math.Min(o.Max, Math.Max(o.Min, v));
                    return n;
                }
                case OptionKind.Choice:
                {
                    var c = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 170, Margin = new Padding(3, 3, 0, 0) };
                    c.Items.AddRange(o.Choices);
                    c.SelectedIndex = Math.Max(0, Array.FindIndex(o.Choices, x => string.Equals(x, current, StringComparison.OrdinalIgnoreCase)));
                    return c;
                }
                default:
                    return new TextBox { Text = current ?? "", Width = 170, Margin = new Padding(3, 3, 0, 0) };
            }
        }

        private static void Hook(Control editor, Action changed)
        {
            switch (editor)
            {
                case CheckBox c: c.CheckedChanged += (_, _) => changed(); break;
                case NumericUpDown n: n.ValueChanged += (_, _) => changed(); break;
                case ComboBox b: b.SelectedIndexChanged += (_, _) => changed(); break;
                case TextBox t: t.TextChanged += (_, _) => changed(); break;
            }
        }

        private static string ValueOf(Row row)
        {
            switch (row.Editor)
            {
                case CheckBox c: return c.Checked ? "true" : "false";
                case NumericUpDown n: return n.Value.ToString(CultureInfo.InvariantCulture);
                case ComboBox b: return b.SelectedItem?.ToString() ?? "";
                case TextBox t: return t.Text.Trim();
            }
            return "";
        }

        /// <summary>What Save writes: only the ticked rows.</summary>
        public Dictionary<string, string> Values()
        {
            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var row in _rows)
            {
                if (!row.Override.Checked) continue;
                var v = ValueOf(row);
                if (v.Length > 0) values[row.Option.IniKey] = v;
            }
            return values;
        }

        /// <summary>Why the page cannot be saved, or null.</summary>
        public string Problem()
        {
            foreach (var row in _rows)
            {
                if (!row.Override.Checked) continue;
                if (row.Option.Kind == OptionKind.Text && ValueOf(row).Length == 0)
                    return "\"" + row.Option.Label + "\" is ticked but empty. Untick it to leave the emulator's value, or type one.";
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
    }
}
