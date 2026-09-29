// The "Preview result" window of the options window's Advanced tab: the file as a launch of this game
// would write it, the lines that differ from the file as it is now marked. Nothing is written - see
// MelonDsGameSettings.Preview. (A copy of it lives in each plugin that has an Advanced tab: the shared folders
// are also built into plugins without WinForms.)

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace LbIntegrations.MelonDs
{
    internal static class ConfigPreviewWindow
    {
        public static void Show(IWin32Window owner, string title, string summary, string before, string after)
        {
            before = (before ?? "").Replace("\r\n", "\n");
            after = (after ?? "").Replace("\r\n", "\n");
            using var form = new Form
            {
                Text = title, StartPosition = FormStartPosition.CenterParent, ShowInTaskbar = false,
                MinimizeBox = false, Font = new Font("Segoe UI", 9f), ClientSize = new Size(760, 560),
            };
            var head = new Label { Dock = DockStyle.Top, Height = 40, Padding = new Padding(10, 8, 10, 0), Text = summary };
            var box = new RichTextBox
            {
                Dock = DockStyle.Fill, ReadOnly = true, WordWrap = false, Font = new Font("Consolas", 9f),
                BackColor = SystemColors.Window, DetectUrls = false,
            };
            var bottom = new Panel { Dock = DockStyle.Bottom, Height = 44 };
            var close = new Button { Text = "Close", Width = 90, DialogResult = DialogResult.OK };
            bottom.Controls.Add(close);
            bottom.Layout += (_, _) => close.Location = new Point(bottom.ClientSize.Width - 12 - close.Width, 9);
            form.AcceptButton = close;
            form.CancelButton = close;
            form.Controls.Add(box);
            form.Controls.Add(head);
            form.Controls.Add(bottom);

            // A line is marked when the file as it is now has no such line - a changed or added one.
            var had = before.Split('\n').GroupBy(l => l).ToDictionary(g => g.Key, g => g.Count());
            var lines = after.Split('\n');
            box.Text = string.Join("\n", lines);
            int start = 0, marked = 0;
            foreach (var line in lines)
            {
                if (had.TryGetValue(line, out var n) && n > 0) had[line] = n - 1;
                else if (line.Trim().Length > 0)
                {
                    box.Select(start, line.Length);
                    box.SelectionBackColor = Color.FromArgb(255, 236, 160);
                    marked++;
                }
                start += line.Length + 1;
            }
            box.Select(0, 0);
            head.Text = summary + (marked == 0 ? "" : "  " + marked + " line(s) marked: they differ from the file as it is now.");
            form.ShowDialog(owner);
        }
    }
}
