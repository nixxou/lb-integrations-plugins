// The rows of XeniaOptions, for the Nixx window's Xenia tab (every game) and a game's own options window - the same
// control, told what "not set" falls back to.
//
// ONE CONTROL PER OPTION, AND IT SHOWS THE STATE, as SUPER ZSNES's page (Mehdi, 01/10): until set, an option is what
// it falls back to, and the control says so - a three-state box reading "Xenia's own: on" / "on" / "off", a list whose
// first entry is "<Xenia's own: 1x (native, 720p)>", a field left empty whose grey hint says the value. The bar at the
// left of a row: blue, passed on the command line; grey, not.
//
// AN EMULATOR UPDATE (Mehdi, 04/10): an option whose cvar this Xenia no longer has (not in its config.toml) is greyed,
// with a note, never passed - and its value kept, for a Xenia that has it again. A value of ours no list entry has any
// more (a choice taken out) is shown as it is and kept, never replaced by the fallback behind the user's back.

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace LbIntegrations.Xenia
{
    internal sealed class XeniaOptionRows : FlowLayoutPanel
    {
        public static readonly Color Passed = Color.FromArgb(0, 120, 215), NotPassed = Color.FromArgb(175, 175, 175);

        private sealed class Row
        {
            public XeniaOption Option;
            public Control Editor;
            public Panel Bar;
            public string Fallback;     // what an unset row stands for, in words
            public string Kept;         // the value of an option this Xenia does not have, or of a choice no longer listed
            public bool Gone;           // this Xenia does not have the option
        }

        private readonly List<Row> _rows = new List<Row>();

        /// <summary>Raised when any row changes.</summary>
        public event Action Changed;

        /// <param name="saved">The values set, by option key.</param>
        /// <param name="fallback">What an unset option stands for, in words - "Xenia's own: on", "every game's: 2x (1440p)".</param>
        /// <param name="known">The cvars this Xenia has (XeniaSettings.KnownCvars) - null: not known, every option shown.</param>
        public XeniaOptionRows(IDictionary<string, string> saved, Func<XeniaOption, string> fallback, HashSet<string> known = null)
        {
            FlowDirection = FlowDirection.TopDown;
            WrapContents = false;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            Margin = Padding.Empty;

            foreach (var group in XeniaOptions.All.GroupBy(o => o.Group))
            {
                var box = new GroupBox { Text = group.Key, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(8, 4, 8, 8), Margin = new Padding(4, 4, 4, 10) };
                var table = new TableLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 3, Location = new Point(8, 20) };
                foreach (var w in new[] { 12, 210, 270 }) table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, w));
                foreach (var o in group)
                {
                    saved.TryGetValue(o.Key, out var current);
                    var row = new Row { Option = o, Fallback = fallback(o), Gone = !XeniaSettings.Knows(known, o) };
                    row.Editor = Editor(row, current);
                    row.Bar = new Panel { Width = 4, Height = 18, Margin = new Padding(0, 6, 8, 0) };
                    var label = new Label { Text = o.Label, AutoSize = true, MaximumSize = new Size(206, 0), Margin = new Padding(0, 7, 4, 0) };
                    // One tooltip for the row (LbipHint's): its whole help, then the cvars it passes.
                    var cvars = "--" + string.Join(", --", o.Sends ?? new[] { o.Key });
                    var tip = string.IsNullOrEmpty(o.Help) ? cvars : o.Help + "\n" + cvars;
                    LbIntegrations.Lbip.LbipHint.Attach(label, tip, row.Editor);
                    if (row.Gone)
                    {
                        row.Kept = string.IsNullOrWhiteSpace(current) ? null : current;
                        row.Editor.Enabled = false;
                        label.ForeColor = SystemColors.GrayText;
                        var why = "This Xenia no longer has this setting (--" + string.Join(", --", o.Sends ?? new[] { o.Key }) + " is not in its config.toml): it is not passed."
                                  + (row.Kept != null ? " Your choice is kept, for a Xenia that has it again." : "");
                        LbIntegrations.Lbip.LbipHint.Attach(label, why, row.Editor);
                        label.Text = o.Label + " (not in this Xenia)";
                    }
                    Hook(row.Editor, () => { Show(row); Changed?.Invoke(); });
                    Show(row);

                    int r = table.RowCount++;
                    table.Controls.Add(row.Bar, 0, r);
                    table.Controls.Add(label, 1, r);
                    table.Controls.Add(row.Editor, 2, r);
                    if (!string.IsNullOrEmpty(o.Help))
                    {
                        // A short sentence under it, the whole of it on hover (Mehdi, 05/10) - over the row: its name, its control, its sentence.
                        var help = new Label { Text = o.Short ?? o.Help, AutoSize = true, MaximumSize = new Size(470, 0), ForeColor = SystemColors.GrayText, Margin = new Padding(0, 0, 0, 6) };
                        LbIntegrations.Lbip.LbipHint.Attach(help, row.Gone ? o.Help : tip);
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
            switch (o.Kind)
            {
                case XeniaOptionKind.Bool:
                    return new CheckBox
                    {
                        AutoSize = true, ThreeState = true, Margin = new Padding(3, 6, 0, 0),
                        CheckState = string.IsNullOrEmpty(current) ? CheckState.Indeterminate : XeniaSettings.IsTrue(current) ? CheckState.Checked : CheckState.Unchecked,
                    };
                case XeniaOptionKind.Choice:
                {
                    var c = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 260, Margin = new Padding(3, 3, 0, 0) };
                    c.Items.Add("<" + row.Fallback + ">");
                    foreach (var ch in o.Choices) c.Items.Add(ch.Label);
                    int at = Array.FindIndex(o.Choices, x => string.Equals(x.Value, current, StringComparison.OrdinalIgnoreCase));
                    if (at < 0 && !string.IsNullOrWhiteSpace(current))
                    {
                        // Ours, but no entry of the list any more: shown as it is, the last entry, and kept.
                        row.Kept = current.Trim();
                        c.Items.Add(row.Kept + " (not in this list)");
                        c.SelectedIndex = c.Items.Count - 1;
                    }
                    else c.SelectedIndex = 1 + at;
                    return c;
                }
                default:
                {
                    var t = new TextBox { Text = current ?? "", Width = 260, Margin = new Padding(3, 3, 0, 0) };
                    var hint = row.Fallback + " (" + Range(o) + ")";
                    t.HandleCreated += (_, _) => SendMessage(t.Handle, 0x1501 /* EM_SETCUEBANNER */, (IntPtr)1, hint);
                    return t;
                }
            }
        }

        private static string Range(XeniaOption o)
            => o.Min.ToString(CultureInfo.InvariantCulture) + " to " + o.Max.ToString(CultureInfo.InvariantCulture);

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

        private static void Show(Row row)
        {
            if (row.Editor is CheckBox c)
                c.Text = c.CheckState == CheckState.Indeterminate ? row.Fallback : c.Checked ? "on" : "off";
            row.Bar.BackColor = !row.Gone && ValueOf(row) != null ? Passed : NotPassed;
        }

        /// <summary>The row's value to keep. An option this Xenia does not have keeps what it had.</summary>
        private static string ValueOf(Row row) => row.Gone ? row.Kept : Chosen(row);

        /// <summary>What the editor says - for the bar too, which is grey for a row not passed.</summary>
        private static string Chosen(Row row)
        {
            switch (row.Editor)
            {
                case CheckBox c: return c.CheckState == CheckState.Indeterminate ? null : c.Checked ? "true" : "false";
                case ComboBox b:
                    if (b.SelectedIndex <= 0) return null;
                    return b.SelectedIndex - 1 < row.Option.Choices.Length ? row.Option.Choices[b.SelectedIndex - 1].Value : row.Kept;
                case TextBox t: { var v = t.Text.Trim(); return v.Length == 0 ? null : v; }
            }
            return null;
        }

        /// <summary>Every row - null for one not set - for LbipGameEdit: OK changes only what changed.</summary>
        public Dictionary<string, string> Shown()
        {
            var shown = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var row in _rows) shown[row.Option.Key] = ValueOf(row);
            return shown;
        }

        /// <summary>The rows that are set, by option key.</summary>
        public Dictionary<string, string> Values()
        {
            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var row in _rows)
            {
                var v = ValueOf(row);
                if (v != null) values[row.Option.Key] = v;
            }
            return values;
        }

        /// <summary>Why the rows cannot be saved, or null.</summary>
        public string Problem()
        {
            foreach (var row in _rows)
            {
                if (!(row.Editor is TextBox t) || row.Gone) continue;
                var v = t.Text.Trim();
                if (v.Length == 0) continue;
                var o = row.Option;
                if (!decimal.TryParse(v.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var d) || d < o.Min || d > o.Max
                    || (o.Kind == XeniaOptionKind.Int && d != decimal.Truncate(d)))
                    return "\"" + o.Label + "\": " + v + " is not " + (o.Kind == XeniaOptionKind.Int ? "a whole number" : "a number") + " from " + Range(o)
                           + ". Empty it to leave it as it is.";
            }
            return null;
        }

        /// <summary>A legend for the bars, to put under the rows.</summary>
        public static Control Legend()
        {
            var legend = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 22, WrapContents = false };
            void Item(Color c, string text)
            {
                legend.Controls.Add(new Panel { BackColor = c, Size = new Size(4, 14), Margin = new Padding(0, 3, 4, 0) });
                legend.Controls.Add(new Label { Text = text, AutoSize = true, Margin = new Padding(0, 2, 14, 0) });
            }
            Item(Passed, "Passed on the command line");
            Item(NotPassed, "Not passed: left as it falls back");
            return legend;
        }
    }
}
