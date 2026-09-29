// One line saying where a game stands in Vita3K's compatibility list (Vita3kCompat): a dot of the
// state's own colour, the state, the report's other labels, and a link to the report. For the launch's
// progress window and the Options window's Compatibility tab.

using System;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;

namespace LbIntegrations.Vita3k
{
    internal static class Vita3kCompatRow
    {
        /// <summary>The line, <paramref name="width"/> wide; null when there is nothing to say.</summary>
        public static Control Build(VitaCompat compat, int width)
        {
            if (compat == null) return null;
            var row = new FlowLayoutPanel
            {
                Width = width, Height = 22, WrapContents = false, AutoSize = false,
                FlowDirection = FlowDirection.LeftToRight, Margin = Padding.Empty, Padding = Padding.Empty,
            };
            var tip = new ToolTip();

            if (compat.State != null)
            {
                var color = ParseColor(compat.StateColor);
                var dot = new Panel { Size = new Size(14, 14), Margin = new Padding(0, 3, 6, 0) };
                dot.Paint += (_, e) =>
                {
                    e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                    using var b = new SolidBrush(color);
                    using var p = new Pen(ControlPaint.Dark(color), 1f);
                    e.Graphics.FillEllipse(b, 1, 1, 11, 11);
                    e.Graphics.DrawEllipse(p, 1, 1, 11, 11);
                };
                row.Controls.Add(dot);
                var state = new Label { AutoSize = true, Margin = new Padding(0, 3, 0, 0), Text = "Vita3K compatibility: " };
                var name = new Label { AutoSize = true, Margin = new Padding(0, 3, 0, 0), Text = compat.State, Font = new Font(SystemFonts.MessageBoxFont, FontStyle.Bold) };
                row.Controls.Add(state);
                row.Controls.Add(name);
                if (!string.IsNullOrWhiteSpace(compat.StateDescription))
                {
                    tip.SetToolTip(dot, compat.StateDescription);
                    tip.SetToolTip(name, compat.StateDescription);
                }
            }
            else
                row.Controls.Add(new Label { AutoSize = true, Margin = new Padding(0, 3, 0, 0), Text = "Vita3K compatibility:" });

            if (compat.Problems.Count > 0)
            {
                var problems = new Label
                {
                    AutoSize = true, Margin = new Padding(6, 3, 0, 0), ForeColor = SystemColors.GrayText,
                    Text = "- " + string.Join(", ", compat.Problems),
                };
                tip.SetToolTip(problems, string.Join("\n", compat.Problems));
                row.Controls.Add(problems);
            }

            var link = new LinkLabel { AutoSize = true, Margin = new Padding(8, 3, 0, 0), Text = "report #" + compat.IssueId.ToString(CultureInfo.InvariantCulture) };
            link.LinkClicked += (_, _) =>
            {
                try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(compat.Url) { UseShellExecute = true }); }
                catch (Exception ex) { Log.Warn("could not open " + compat.Url, ex); }
            };
            tip.SetToolTip(link, compat.Url);
            row.Controls.Add(link);
            return row;
        }

        private static Color ParseColor(string hex)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(hex) && hex.Length == 6)
                    return Color.FromArgb(int.Parse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture) | unchecked((int)0xFF000000));
            }
            catch { }
            return Color.Gray;
        }
    }
}
