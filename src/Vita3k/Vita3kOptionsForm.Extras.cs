// The options window's "Updates & DLC" tab: what a launch would find for the game - by the usual rules
// and in the import's index (Vita3kExtras.Evaluate) - and which of it the game is launched with.
//
//   - the update: "Automatic" (the highest found, today and whenever a newer one appears), one of those
//     found, or "None";
//   - the DLC: one box each, ticked unless left out. A DLC found later is installed: the choice
//     records what is LEFT OUT (Vita3kExtrasChoice).
//
// ONE GAME AT A TIME: a selection gets a sentence saying so. Read when the tab is first shown, not when
// the window opens - every candidate archive is opened, and the Session tab should not wait for that.
//
// On OK a change asks first: nobody knows what another update, or fewer DLC, does to the saves a game
// already has. The console is rebuilt at the next launch - the extras are part of what it is built
// with - and the save check there still asks if the save was made with something else.

using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace LbIntegrations.Vita3k
{
    internal sealed partial class Vita3kOptionsForm
    {
        private TabPage _extrasTab;
        private bool _extrasLoaded;
        private FoundExtras _found;
        private Vita3kExtrasChoice _choiceWas;
        private RadioButton _updateAuto, _updateNone;
        private readonly List<(RadioButton Button, VitaExtra Update)> _updates = new List<(RadioButton, VitaExtra)>();
        private readonly List<(CheckBox Box, VitaExtra Dlc)> _dlc = new List<(CheckBox, VitaExtra)>();

        private TabPage ExtrasTab(TabControl tabs)
        {
            _extrasTab = new TabPage("Updates & DLC") { Padding = new Padding(12), UseVisualStyleBackColor = true, AutoScroll = true };
            if (_games.Count != 1)
            {
                _extrasTab.Controls.Add(new Label
                {
                    Dock = DockStyle.Fill, ForeColor = SystemColors.GrayText,
                    Text = "Select a single game to choose the update and the DLC it is launched with.",
                });
                return _extrasTab;
            }
            _extrasTab.Controls.Add(new Label { Dock = DockStyle.Fill, Text = "Looking for the game's updates and DLC...", ForeColor = SystemColors.GrayText });
            tabs.SelectedIndexChanged += (_, _) => { if (tabs.SelectedTab == _extrasTab) LoadExtras(); };
            return _extrasTab;
        }

        private void LoadExtras()
        {
            if (_extrasLoaded) return;
            _extrasLoaded = true;
            var g = _games[0];
            Cursor = Cursors.WaitCursor;
            string problem = null;
            try
            {
                var content = Vita3kContent.Describe(g.RomFull, out problem);
                if (content != null && !content.IsGame) problem = "this is not a game";
                if (problem == null)
                {
                    _found = Vita3kExtras.Evaluate(g.RomFull, content, g.Title, g.InstallDir);
                    _choiceWas = Vita3kExtrasChoice.Load(g.InstallDir, g.GameId);
                }
            }
            catch (Exception ex) { problem = ex.Message; }
            finally { Cursor = Cursors.Default; }

            _extrasTab.Controls.Clear();
            if (_found == null)
            {
                _extrasTab.Controls.Add(new Label { Dock = DockStyle.Fill, ForeColor = Color.Firebrick, Text = "The game could not be read: " + problem });
                return;
            }

            int y = 8;
            void Add(Control c, int x) { c.Location = new Point(x, y); _extrasTab.Controls.Add(c); }
            Label Bold(string t) => new Label { Text = t, AutoSize = true, Font = new Font(Font, FontStyle.Bold) };
            Label Grey(string t) => new Label { Text = t, AutoSize = true, ForeColor = SystemColors.GrayText };

            Add(Bold("Update"), 8); y += 24;
            var highest = _found.Updates.FirstOrDefault();
            _updateAuto = new RadioButton { AutoSize = true, Text = "Automatic - the highest found" + (highest != null ? " (" + highest.Content.AppVer + " today)" : " (none today)") };
            Add(_updateAuto, 18); y += 24;
            foreach (var u in _found.Updates)
            {
                var b = new RadioButton { AutoSize = true, Text = (u.Content.AppVer ?? "?") + "  -  " + Path.GetFileName(u.Path) };
                Add(b, 18); y += 20;
                Add(Grey(u.FoundBy + ", " + Vita3kExtras.Mb(u.Bytes)), 36); y += 22;
                _updates.Add((b, u));
            }
            _updateNone = new RadioButton { AutoSize = true, Text = "None - the game as it is" };
            Add(_updateNone, 18); y += 32;

            Add(Bold("DLC"), 8); y += 24;
            if (_found.Addons.Count == 0) { Add(Grey("None found."), 18); y += 22; }
            foreach (var d in _found.Addons)
            {
                var id = d.Content.ContentId ?? "";
                var box = new CheckBox { AutoSize = true, Checked = _choiceWas == null || !_choiceWas.LeftOut.Contains(id),
                                         Text = (d.Content.Title ?? id) + "  -  " + Path.GetFileName(d.Path) };
                Add(box, 18); y += 20;
                Add(Grey(id + ", " + d.FoundBy + ", " + Vita3kExtras.Mb(d.Bytes)), 36); y += 22;
                _dlc.Add((box, d));
            }

            // What is chosen now.
            if (_choiceWas == null) _updateAuto.Checked = true;
            else if (_choiceWas.NoUpdate) _updateNone.Checked = true;
            else
            {
                var chosen = _updates.FirstOrDefault(u => string.Equals(Path.GetFullPath(u.Update.Path), Path.GetFullPath(_choiceWas.UpdatePath ?? ""), StringComparison.OrdinalIgnoreCase));
                if (chosen.Button != null) chosen.Button.Checked = true; else _updateAuto.Checked = true;
            }
        }

        /// <summary>The choice the tab shows - null when it was never opened (nothing to change).</summary>
        private Vita3kExtrasChoice ExtrasChoice()
        {
            if (_found == null) return null;
            var c = new Vita3kExtrasChoice();
            if (_updateNone.Checked) c.NoUpdate = true;
            else foreach (var (button, update) in _updates) if (button.Checked) c.UpdatePath = update.Path;
            foreach (var (box, dlc) in _dlc) if (!box.Checked) c.LeftOut.Add(dlc.Content.ContentId ?? "");
            return c;
        }

        private static string Describe(Vita3kExtrasChoice c)
            => c == null || c.Automatic ? "automatic" : (c.NoUpdate ? "none" : c.UpdatePath ?? "the highest") + "|" + string.Join(",", c.LeftOut.OrderBy(x => x));

        /// <summary>On OK: the choice saved - after the warning, when it changed. False when the user
        /// backed out: nothing of the window is applied then.</summary>
        private bool ApplyExtras()
        {
            var now = ExtrasChoice();
            if (now == null || Describe(now) == Describe(_choiceWas)) return true;
            var g = _games[0];
            if (MessageBox.Show(this,
                    "You are changing the update or the DLC " + g.Title + " is launched with.\n\n"
                    + "Nobody knows what that does to the saves it already has: a save made with another version or other DLC "
                    + "may not load, or may load wrong. The save itself is kept either way, and the next launch asks before "
                    + "loading one made with something else.\n\nThe console is rebuilt at the next launch.",
                    Text, MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) != DialogResult.OK)
                return false;
            try { Vita3kExtrasChoice.Save(g.InstallDir, g.GameId, now); }
            catch (Exception ex)
            {
                MessageBox.Show(this, "The choice could not be saved: " + ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }
            return true;
        }
    }
}
