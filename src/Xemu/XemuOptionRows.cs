// The rows of XemuOptions, for the Nixx window's xemu tab (every game) and a game's options window - CxbxOptionRows' control,
// told what "not set" falls back to:
//
// ONE CONTROL PER OPTION, AND IT SHOWS THE STATE: until set, an option is what it falls back to, and the control says so with
// the value - a three-state box reading "xemu's own: on" / "on" / "off", a list whose first entry is "<xemu's own: OpenGL>"
// or "<every game's: Vulkan>". The bar at the left of a row: blue, set here; grey, not.

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace LbIntegrations.Xemu
{
    internal sealed class XemuOptionRows : FlowLayoutPanel
    {
        public static readonly Color SetHere = Color.FromArgb(0, 120, 215), NotSet = Color.FromArgb(175, 175, 175);

        private sealed class Row
        {
            public XemuOption Option;
            public Control Editor;
            public Panel Bar;
            public string Fallback;
        }

        private readonly List<Row> _rows = new List<Row>();

        /// <param name="saved">this level's values (keys "opt.&lt;key&gt;")</param>
        /// <param name="fallback">what an unset option stands for, in words - "xemu's own: on", "every game's: 2x"</param>
        public XemuOptionRows(IDictionary<string, string> saved, Func<XemuOption, string> fallback)
        {
            FlowDirection = FlowDirection.TopDown;
            WrapContents = false;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            Margin = Padding.Empty;

            foreach (var group in XemuOptions.All.GroupBy(o => o.Group))
            {
                var box = new GroupBox { Text = group.Key, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(8, 4, 8, 8), Margin = new Padding(4, 4, 4, 10) };
                var table = new TableLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 3, Location = new Point(8, 20) };
                foreach (var w in new[] { 12, 190, 300 }) table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, w));
                foreach (var o in group)
                {
                    var row = new Row { Option = o, Fallback = fallback(o) };
                    row.Editor = Editor(row, XemuOptions.Get(saved, o.Key));
                    row.Bar = new Panel { Width = 4, Height = 18, Margin = new Padding(0, 6, 8, 0) };
                    var label = new Label { Text = o.Label, AutoSize = true, MaximumSize = new Size(186, 0), Margin = new Padding(0, 7, 4, 0) };
                    Hook(row.Editor, () => Show(row));
                    Show(row);

                    int r = table.RowCount++;
                    table.Controls.Add(row.Bar, 0, r);
                    table.Controls.Add(label, 1, r);
                    table.Controls.Add(row.Editor, 2, r);
                    if (!string.IsNullOrEmpty(o.Help))
                    {
                        var help = new Label { Text = o.Help, AutoSize = true, MaximumSize = new Size(480, 0), ForeColor = SystemColors.GrayText, Margin = new Padding(0, 0, 0, 6) };
                        int h = table.RowCount++;
                        table.Controls.Add(help, 1, h);
                        table.SetColumnSpan(help, 2);
                    }
                    _rows.Add(row);
                }
                box.Controls.Add(table);
                Controls.Add(box);
            }
        }

        private static Control Editor(Row row, string current)
        {
            var o = row.Option;
            if (o.Bool)
                return new CheckBox
                {
                    AutoSize = true, ThreeState = true, Margin = new Padding(3, 6, 0, 0),
                    CheckState = current == null ? CheckState.Indeterminate : current == "on" ? CheckState.Checked : CheckState.Unchecked,
                };
            var c = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 290, Margin = new Padding(3, 3, 0, 0) };
            c.Items.Add("<" + row.Fallback + ">");
            foreach (var ch in o.Choices) c.Items.Add(ch.Label);
            int at = o.Choices.FindIndex(x => string.Equals(x.Value, current, StringComparison.OrdinalIgnoreCase));
            if (at < 0 && !string.IsNullOrWhiteSpace(current))
            {
                // Set, but no entry of the list any more (a graphics card since removed): shown as it is and kept - not read as
                // unset, then emptied at the next OK.
                c.Tag = current.Trim();
                c.Items.Add(current.Trim() + " (not in this list)");
                c.SelectedIndex = c.Items.Count - 1;
            }
            else c.SelectedIndex = 1 + at;
            return c;
        }

        private static void Hook(Control editor, Action changed)
        {
            switch (editor)
            {
                case CheckBox c: c.CheckStateChanged += (_, _) => changed(); break;
                case ComboBox b: b.SelectedIndexChanged += (_, _) => changed(); break;
            }
        }

        private static void Show(Row row)
        {
            if (row.Editor is CheckBox c)
                c.Text = c.CheckState == CheckState.Indeterminate ? row.Fallback : c.Checked ? "on" : "off";
            row.Bar.BackColor = ValueOf(row) != null ? SetHere : NotSet;
        }

        private static string ValueOf(Row row)
        {
            switch (row.Editor)
            {
                case CheckBox c: return c.CheckState == CheckState.Indeterminate ? null : c.Checked ? "on" : "off";
                case ComboBox b:
                    if (b.SelectedIndex <= 0) return null;
                    return b.SelectedIndex - 1 < row.Option.Choices.Count ? row.Option.Choices[b.SelectedIndex - 1].Value : b.Tag as string;
            }
            return null;
        }

        /// <summary>Every row, keyed "opt.&lt;key&gt;" - "" for unset, so that a row emptied is removed from the file.</summary>
        public Dictionary<string, string> Values()
            => _rows.ToDictionary(r => XemuOptions.Prefix + r.Option.Key, r => ValueOf(r) ?? "", StringComparer.OrdinalIgnoreCase);

        /// <summary>Every row back to unset - nothing saved before OK.</summary>
        public void Reset()
        {
            foreach (var r in _rows)
            {
                if (r.Editor is CheckBox c) c.CheckState = CheckState.Indeterminate;
                else if (r.Editor is ComboBox b) b.SelectedIndex = 0;
            }
        }

        /// <summary>A legend for the bars.</summary>
        public static Control Legend(string unsetMeans)
        {
            var legend = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = false, Margin = new Padding(4, 2, 0, 6) };
            void Item(Color c, string text)
            {
                legend.Controls.Add(new Panel { BackColor = c, Size = new Size(4, 14), Margin = new Padding(0, 3, 4, 0) });
                legend.Controls.Add(new Label { Text = text, AutoSize = true, Margin = new Padding(0, 2, 14, 0) });
            }
            Item(SetHere, "Set here");
            Item(NotSet, unsetMeans);
            return legend;
        }

        // ── what an unset row stands for ─────────────────────────────────────

        /// <summary>xemu's own value of an option, in words - from its xemu.toml, else its default.</summary>
        public static string OwnLabel(XemuOption o, XemuTomlDoc userToml)
        {
            if (!o.IsXemuSetting) return "the plugin's";
            var v = XemuOptions.OwnOf(o, userToml);
            if (string.IsNullOrEmpty(v)) return o.Key == "display.gpu" ? "xemu's choice" : "none";
            return o.LabelOf(v);
        }

        /// <summary>Every game's level: the plugin's default, else xemu's own value.</summary>
        public static Func<XemuOption, string> EveryGameFallback(XemuTomlDoc userToml)
            => o => o.Default != null ? "default: " + o.LabelOf(o.Default) : "xemu's own: " + OwnLabel(o, userToml);

        /// <summary>One game's level: every game's value, else as above.</summary>
        public static Func<XemuOption, string> GameFallback(IDictionary<string, string> every, XemuTomlDoc userToml)
            => o => XemuOptions.Get(every, o.Key) is string v && v != XemuOptions.OwnValue ? "every game's: " + o.LabelOf(v)
                  : XemuOptions.Get(every, o.Key) == XemuOptions.OwnValue ? "every game's: xemu's own: " + OwnLabel(o, userToml)
                  : EveryGameFallback(userToml)(o);
    }
}
