// The rows of CxbxOptions, for the Nixx window's Cxbx-Reloaded tab (every game) and a game's options window - the same
// control, told what "not set" falls back to. XeniaOptionRows' shape (Mehdi, 03/10: the same logic as the other plugins):
//
// ONE CONTROL PER OPTION, AND IT SHOWS THE STATE: until set, an option is what it falls back to, and the control says so
// with the value - a three-state box reading "Cxbx-Reloaded's own: on" / "on" / "off", a list whose first entry is
// "<Cxbx-Reloaded's own: 2560 x 1440 (144 Hz)>" or "<every game's: PAL 60 Hz>". The bar at the left of a row: blue, set
// here; grey, not.

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace LbIntegrations.Cxbx
{
    internal sealed class CxbxOptionRows : FlowLayoutPanel
    {
        public static readonly Color SetHere = Color.FromArgb(0, 120, 215), NotSet = Color.FromArgb(175, 175, 175);

        private sealed class Row
        {
            public CxbxOption Option;
            public Control Editor;
            public Panel Bar;
            public string Fallback;
        }

        private readonly List<Row> _rows = new List<Row>();

        /// <param name="saved">this level's values (keys "opt.&lt;key&gt;")</param>
        /// <param name="fallback">what an unset option stands for, in words - "Cxbx-Reloaded's own: on", "every game's: 2x"</param>
        public CxbxOptionRows(IDictionary<string, string> saved, Func<CxbxOption, string> fallback)
        {
            FlowDirection = FlowDirection.TopDown;
            WrapContents = false;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            Margin = Padding.Empty;

            foreach (var group in CxbxOptions.All.GroupBy(o => o.Group))
            {
                var box = new GroupBox { Text = group.Key, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(8, 4, 8, 8), Margin = new Padding(4, 4, 4, 10) };
                var table = new TableLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 3, Location = new Point(8, 20) };
                foreach (var w in new[] { 12, 190, 300 }) table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, w));
                foreach (var o in group)
                {
                    var row = new Row { Option = o, Fallback = fallback(o) };
                    row.Editor = Editor(row, CxbxOptions.Get(saved, o.Key));
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
            c.SelectedIndex = 1 + o.Choices.FindIndex(x => string.Equals(x.Value, current, StringComparison.OrdinalIgnoreCase));
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
                case ComboBox b: return b.SelectedIndex <= 0 ? null : row.Option.Choices[b.SelectedIndex - 1].Value;
            }
            return null;
        }

        /// <summary>Every row, keyed "opt.&lt;key&gt;" - "" for unset, so that a row emptied is removed from the file.</summary>
        public Dictionary<string, string> Values()
            => _rows.ToDictionary(r => CxbxOptions.Prefix + r.Option.Key, r => ValueOf(r) ?? "", StringComparer.OrdinalIgnoreCase);

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

        /// <summary>Every game's level: the plugin's default, else Cxbx-Reloaded's own value.</summary>
        public static Func<CxbxOption, string> EveryGameFallback(CxbxOwn own)
            => o => o.Default != null ? "default: " + o.LabelOf(o.Default) : "Cxbx-Reloaded's own: " + own.Label(o);

        /// <summary>One game's level: every game's value, else as above.</summary>
        public static Func<CxbxOption, string> GameFallback(IDictionary<string, string> every, CxbxOwn own)
            => o => CxbxOptions.Get(every, o.Key) is string v && v != CxbxOptions.OwnValue ? "every game's: " + o.LabelOf(v)
                  : CxbxOptions.Get(every, o.Key) == CxbxOptions.OwnValue ? "every game's: Cxbx-Reloaded's own: " + own.Label(o)
                  : EveryGameFallback(own)(o);
    }
}
