// A PSP game's window, from its right-click entry (Mehdi, 04/10: all that is left of the PPSSPP options window - a game's
// own settings are PPSSPP's own, its "Game settings", <ID>_ppsspp.ini):
//   - under the title, its state in PPSSPP's compatibility reports and the link to its page (PpssppCompat): what the
//     database knows at once, then the game's own page read in the background when its line is a week old;
//   - its UPDATES: the ones found and the one installed, installed or removed at once by their buttons - nothing is
//     installed unasked (Mehdi, 03/10). See PpssppUpdates.

using System;
using System.Drawing;
using System.Windows.Forms;
using Unbroken.LaunchBox.Plugins.Data;

namespace LbIntegrations.Ppsspp
{
    internal sealed class PpssppUpdatesForm : Form
    {
        public PpssppUpdatesForm(IGame game, string title, string discId, PpssppLayout layout)
        {
            Text = "Nixx-PPSSPP - " + title;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MinimizeBox = MaximizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            ShowInTaskbar = false;
            Font = new Font("Segoe UI", 9f);
            ClientSize = new Size(584, 428);

            string rom = null;
            try { rom = LbIntegrations.Lbip.LbipImportWatch.Full(game?.ApplicationPath); } catch { }
            var discVersion = rom == null ? null : PspDiscId.SfoOf(rom)?.GetString("DISC_VERSION")?.Trim();
            Controls.Add(new Label { AutoSize = false, Location = new Point(12, 10), Size = new Size(560, 20), Text = title + "   (" + discId + (discVersion != null ? " " + discVersion : "") + ")" });
            Controls.Add(CompatRow(discId, discVersion, new Point(12, 32)));
            Controls.Add(new Label { AutoSize = false, Location = new Point(12, 60), Size = new Size(560, 20), Text = "Updates", Font = new Font(Font, FontStyle.Bold) });
            Controls.Add(new Label
            {
                AutoSize = false, Location = new Point(12, 80), Size = new Size(560, 48), ForeColor = SystemColors.GrayText,
                Text = "A game update is a PBOOT.PBP that PPSSPP starts in place of the disc's executable, when it is made for this disc's "
                     + "version (" + (discVersion ?? "?") + "). Installed into the memory stick (PSP\\GAME\\" + discId + ") once, and kept there: "
                     + "never installed unasked.",
            });
            var installed = new Label { AutoSize = false, Location = new Point(12, 130), Size = new Size(560, 20) };
            var list = new ListView { Location = new Point(12, 152), Size = new Size(560, 196), View = View.Details, FullRowSelect = true, MultiSelect = false, HideSelection = false, ShowItemToolTips = true };
            list.Columns.Add("Update", 250);
            list.Columns.Add("For disc version", 110);
            list.Columns.Add("File", 190);
            var install = new Button { Text = "Install the selected", AutoSize = true, Location = new Point(12, 356) };
            var remove = new Button { Text = "Remove the installed one", AutoSize = true, Location = new Point(170, 356) };
            var close = new Button { Text = "Close", Width = 90, Location = new Point(482, 392), DialogResult = DialogResult.OK };
            void Show_()
            {
                list.Items.Clear();
                foreach (var u in PpssppUpdates.For(discId, rom))
                {
                    var item = new ListViewItem(new[] { (u.Title ?? "update") + " - version " + (u.AppVer ?? "?"), u.DiscVersion ?? "?", System.IO.Path.GetFileName(u.Path) }) { Tag = u };
                    if (discVersion != null && u.DiscVersion != null && u.DiscVersion != discVersion) { item.ForeColor = Color.DarkGoldenrod; item.ToolTipText = "made for another version of the disc: PPSSPP would not start it"; }
                    list.Items.Add(item);
                }
                if (list.Items.Count == 0) list.Items.Add(new ListViewItem(new[] { "No update found for " + discId, "", "" }) { ForeColor = SystemColors.GrayText });
                var now = PpssppUpdates.Installed(layout, discId);
                installed.Text = now == null ? "Installed: none - the game runs as on its disc."
                    : "Installed: " + now.Value.Update + (now.Value.Ours ? "" : " - put there by hand: this window leaves it alone.");
                remove.Enabled = now != null && now.Value.Ours;
            }
            install.Click += (_, _) =>
            {
                if (list.SelectedItems.Count == 0 || !(list.SelectedItems[0].Tag is PspUpdate u)) return;
                var why = PpssppUpdates.Install(layout, u);
                if (why != null) MessageBox.Show(this, why, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                Show_();
            };
            remove.Click += (_, _) =>
            {
                var why = PpssppUpdates.Remove(layout, discId);
                if (why != null) MessageBox.Show(this, why, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                Show_();
            };
            Controls.AddRange(new Control[] { installed, list, install, remove, close });
            AcceptButton = CancelButton = close;
            Show_();
        }

        /// <summary>"● PPSSPP compatibility: Perfect   report page" - the database's at once, the game's page read in the
        /// background when its line is a week old. The link is there whatever is known.</summary>
        private Control CompatRow(string discId, string discVersion, Point at)
        {
            var row = new FlowLayoutPanel { Location = at, Size = new Size(560, 24), WrapContents = false, Margin = Padding.Empty };
            var dot = new Label { Text = "●", AutoSize = true, Margin = new Padding(0, 2, 2, 0) };
            var what = new Label { Text = "PPSSPP compatibility:", AutoSize = true, Margin = new Padding(0, 3, 4, 0) };
            var rating = new Label { AutoSize = true, Font = new Font(Font, FontStyle.Bold), Margin = new Padding(0, 3, 12, 0) };
            var link = new LinkLabel { Text = "report page", AutoSize = true, Margin = new Padding(0, 3, 0, 0) };
            var url = PpssppCompat.PageOf(discId, discVersion);
            new ToolTip().SetToolTip(link, url);
            link.LinkClicked += (_, _) => { try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true }); } catch (Exception ex) { Log.Warn("could not open " + url, ex); } };
            row.Controls.AddRange(new Control[] { dot, what, rating, link });

            void Put(PspCompat c)
            {
                var r = c?.Rating;
                bool known = !string.IsNullOrEmpty(r);
                rating.Text = known ? r : c == null ? (discVersion == null ? "unknown (no disc version)" : "not known yet") : "not reported yet";
                rating.ForeColor = known ? SystemColors.ControlText : SystemColors.GrayText;
                dot.ForeColor = r switch
                {
                    "Perfect" => Color.FromArgb(70, 136, 71), "Playable" => Color.FromArgb(58, 135, 173), "Ingame" => Color.FromArgb(248, 148, 6),
                    "Menu/Intro" => Color.FromArgb(153, 102, 0), "Doesn't Boot" => Color.FromArgb(185, 74, 72), _ => Color.FromArgb(175, 175, 175),
                };
            }
            Put(PpssppCompat.Of(discId, discVersion));
            PpssppCompat.EnsureBuilt();
            if (discVersion != null)
                new System.Threading.Thread(() =>
                {
                    var fresh = PpssppCompat.Refresh(discId, discVersion);
                    try { if (IsHandleCreated && !IsDisposed) BeginInvoke(new Action(() => Put(fresh))); } catch { }
                }) { IsBackground = true, Name = "PPSSPP compatibility of " + discId }.Start();
            return row;
        }
    }
}
