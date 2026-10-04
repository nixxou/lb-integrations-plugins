// A PSP game's UPDATES, from its right-click entry (Mehdi, 04/10: all that is left of the PPSSPP options window - a game's
// own settings are PPSSPP's own, its "Game settings", <ID>_ppsspp.ini). The updates found for the game and the one
// installed, installed or removed at once by their buttons: nothing is installed unasked (Mehdi, 03/10). See PpssppUpdates.

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
            Text = "Nixx-PPSSPP - Updates - " + title;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MinimizeBox = MaximizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            ShowInTaskbar = false;
            Font = new Font("Segoe UI", 9f);
            ClientSize = new Size(584, 400);

            string rom = null;
            try { rom = LbIntegrations.Lbip.LbipImportWatch.Full(game?.ApplicationPath); } catch { }
            var discVersion = rom == null ? null : PspDiscId.SfoOf(rom)?.GetString("DISC_VERSION")?.Trim();
            Controls.Add(new Label { AutoSize = false, Location = new Point(12, 10), Size = new Size(560, 20), Text = title + "   (" + discId + ")" });
            Controls.Add(new Label
            {
                AutoSize = false, Location = new Point(12, 32), Size = new Size(560, 48), ForeColor = SystemColors.GrayText,
                Text = "A game update is a PBOOT.PBP that PPSSPP starts in place of the disc's executable, when it is made for this disc's "
                     + "version (" + (discVersion ?? "?") + "). Installed into the memory stick (PSP\\GAME\\" + discId + ") once, and kept there: "
                     + "never installed unasked.",
            });
            var installed = new Label { AutoSize = false, Location = new Point(12, 84), Size = new Size(560, 34) };
            var list = new ListView { Location = new Point(12, 122), Size = new Size(560, 200), View = View.Details, FullRowSelect = true, MultiSelect = false, HideSelection = false, ShowItemToolTips = true };
            list.Columns.Add("Update", 250);
            list.Columns.Add("For disc version", 110);
            list.Columns.Add("File", 190);
            var install = new Button { Text = "Install the selected", AutoSize = true, Location = new Point(12, 330) };
            var remove = new Button { Text = "Remove the installed one", AutoSize = true, Location = new Point(170, 330) };
            var close = new Button { Text = "Close", Width = 90, Location = new Point(482, 364), DialogResult = DialogResult.OK };
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
    }
}
