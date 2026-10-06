// The rows of XemuOptions, for the Nixx window's xemu tab (every game) and a game's options window - CxbxOptionRows' control,
// told what "not set" falls back to:
//
// ONE CONTROL PER OPTION, AND IT SHOWS THE STATE: until set, an option is what it falls back to, and the control says so with
// the value - a three-state box reading "xemu's own: on" / "on" / "off", a list whose first entry is "<xemu's own: OpenGL>"
// or "<every game's: Vulkan>". The bar at the left of a row: blue, set here; grey, not.
//
// OVER SEVERAL GAMES (the pack's rule, Mehdi 06/10 - Shared.Menu\LbipSelection): an on / off option is a short list too
// (<its fallback>, on, off), never the three-state box - its grey square already means "not set", so it could not also say
// "the games do not agree". A list then starts with "Various (n of N)" when they do not, its bar purple.

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using LbIntegrations.Lbip;

namespace LbIntegrations.Xemu
{
    internal sealed class XemuOptionRows : FlowLayoutPanel
    {
        public static readonly Color SetHere = OptionMarks.HereColor, NotSet = OptionMarks.EmulatorColor;

        private sealed class Row
        {
            public XemuOption Option;
            public Control Editor;
            public Panel Bar;
            public string Fallback;
            public List<string> Values = new List<string>();   // a list's items stand for: Various, null (not set), a value
        }

        private readonly List<Row> _rows = new List<Row>();

        /// <param name="saved">this level's values (keys "opt.&lt;key&gt;")</param>
        /// <param name="fallback">what an unset option stands for, in words - "xemu's own: on", "every game's: 2x"</param>
        /// <param name="include">the options shown here - null: all of them</param>
        /// <param name="several">a window over several games: lists only, filled by <see cref="Fill(IDictionary{string, string}, Func{string, string})"/></param>
        public XemuOptionRows(IDictionary<string, string> saved, Func<XemuOption, string> fallback, Func<XemuOption, bool> include = null, bool several = false)
        {
            FlowDirection = FlowDirection.TopDown;
            WrapContents = false;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            Margin = Padding.Empty;

            foreach (var group in XemuOptions.All.Where(o => include == null || include(o)).GroupBy(o => o.Group))
            {
                var box = new GroupBox { Text = group.Key, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(8, 4, 8, 8), Margin = new Padding(4, 4, 4, 10) };
                var table = new TableLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 3, Location = new Point(8, 20) };
                foreach (var w in new[] { 12, 190, 300 }) table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, w));
                foreach (var o in group)
                {
                    var row = new Row { Option = o, Fallback = fallback(o) };
                    row.Editor = Editor(row, several);
                    Fill(row, XemuOptions.Get(saved, o.Key), null);
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
                        // A short sentence under it, the whole of it on hover (Mehdi, 05/10) - over the row: its name, its control, its sentence.
                        var help = new Label { Text = o.Short ?? o.Help, AutoSize = true, MaximumSize = new Size(480, 0), ForeColor = SystemColors.GrayText, Margin = new Padding(0, 0, 0, 6) };
                        LbIntegrations.Lbip.LbipHint.Attach(help, o.Help, label, row.Editor);
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

        private static Control Editor(Row row, bool several)
        {
            if (row.Option.Bool && !several)
                return new CheckBox { AutoSize = true, ThreeState = true, Margin = new Padding(3, 6, 0, 0) };
            return new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 290, Margin = new Padding(3, 3, 0, 0) };
        }

        /// <summary>A row's control shows a value: Various (the list's first item says so), null (not set) or a value.</summary>
        private static void Fill(Row row, string current, string variousText)
        {
            var o = row.Option;
            if (row.Editor is CheckBox box)
            {
                box.CheckState = current == null ? CheckState.Indeterminate : current == "on" ? CheckState.Checked : CheckState.Unchecked;
                return;
            }
            var c = (ComboBox)row.Editor;
            c.Items.Clear(); row.Values.Clear();
            if (current == LbipSelection.Various) { c.Items.Add(variousText ?? OptionMarks.VariousText); row.Values.Add(LbipSelection.Various); }
            c.Items.Add("<" + row.Fallback + ">"); row.Values.Add(null);
            if (o.Bool) { c.Items.Add("on"); row.Values.Add("on"); c.Items.Add("off"); row.Values.Add("off"); }
            else foreach (var ch in o.Choices) { c.Items.Add(ch.Label); row.Values.Add(ch.Value); }
            if (o.Bool && current != null && current != LbipSelection.Various) current = current == "on" ? "on" : "off";
            int at = current == null ? row.Values.IndexOf(null) : current == LbipSelection.Various ? 0
                   : row.Values.FindIndex(v => v != null && v != LbipSelection.Various && string.Equals(v, current, StringComparison.OrdinalIgnoreCase));
            if (at < 0 && !string.IsNullOrWhiteSpace(current))
            {
                // Set, but no entry of the list any more (a graphics card since removed): shown as it is and kept - not read as
                // unset, then emptied at the next OK.
                c.Items.Add(current.Trim() + " (not in this list)"); row.Values.Add(current.Trim());
                at = c.Items.Count - 1;
            }
            c.SelectedIndex = Math.Max(0, at);
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
            if (row.Bar == null) return;
            if (row.Editor is CheckBox c)
                c.Text = c.CheckState == CheckState.Indeterminate ? row.Fallback : c.Checked ? "on" : "off";
            row.Bar.BackColor = LbipSelection.BarOf(ValueOf(row));
        }

        private static string ValueOf(Row row)
        {
            switch (row.Editor)
            {
                case CheckBox c: return c.CheckState == CheckState.Indeterminate ? null : c.Checked ? "on" : "off";
                case ComboBox b: return b.SelectedIndex < 0 || b.SelectedIndex >= row.Values.Count ? null : row.Values[b.SelectedIndex];
            }
            return null;
        }

        /// <summary>Every row, keyed "opt.&lt;key&gt;" - "" for unset, so that a row emptied is removed from the file.</summary>
        public Dictionary<string, string> Values()
            => _rows.ToDictionary(r => XemuOptions.Prefix + r.Option.Key, r => ValueOf(r) is string v && v != LbipSelection.Various ? v : "", StringComparer.OrdinalIgnoreCase);

        /// <summary>The keys of the rows, "opt.&lt;key&gt;".</summary>
        public IEnumerable<string> Keys => _rows.Select(r => XemuOptions.Prefix + r.Option.Key);

        /// <summary>What the rows show, keyed "opt.&lt;key&gt;": null (not set), Various or a value - for LbipSelection.</summary>
        public Dictionary<string, string> Shown()
            => _rows.ToDictionary(r => XemuOptions.Prefix + r.Option.Key, ValueOf, StringComparer.OrdinalIgnoreCase);

        /// <summary>The rows show these values (keyed "opt.&lt;key&gt;"; a key absent: not set) - the window over several games.</summary>
        public void Fill(IDictionary<string, string> shown, Func<string, string> variousText)
        {
            foreach (var r in _rows)
            {
                var key = XemuOptions.Prefix + r.Option.Key;
                string v = null;
                shown?.TryGetValue(key, out v);
                Fill(r, v, variousText?.Invoke(key));
                Show(r);
            }
        }

        /// <summary>Every row back to unset - nothing saved before OK.</summary>
        public void Reset()
        {
            foreach (var r in _rows)
            {
                if (r.Editor is CheckBox c) c.CheckState = CheckState.Indeterminate;
                else if (r.Editor is ComboBox b) b.SelectedIndex = Math.Max(0, r.Values.IndexOf(null));
            }
        }

        /// <summary>A legend for the bars - and, over several games, the purple of "Various".</summary>
        public static Control Legend(string unsetMeans, bool various = false)
        {
            var legend = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = false, Margin = new Padding(4, 2, 0, 6) };
            void Item(Color c, string text)
            {
                legend.Controls.Add(new Panel { BackColor = c, Size = new Size(4, 14), Margin = new Padding(0, 3, 4, 0) });
                legend.Controls.Add(new Label { Text = text, AutoSize = true, Margin = new Padding(0, 2, 14, 0) });
            }
            Item(SetHere, "Set here");
            Item(NotSet, unsetMeans);
            if (various) Item(OptionMarks.VariousColor, "Various: each game keeps its own");
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
            => o => o.IsXemuSetting ? EveryGameFallback(userToml)(o)          // xemu's settings have no every-game level (XemuOptions.Effective)
                  : XemuOptions.Get(every, o.Key) is string v && v != XemuOptions.OwnValue ? "every game's: " + o.LabelOf(v)
                  : XemuOptions.Get(every, o.Key) == XemuOptions.OwnValue ? "every game's: xemu's own: " + OwnLabel(o, userToml)
                  : EveryGameFallback(userToml)(o);
    }
}
