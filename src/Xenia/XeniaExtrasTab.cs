// The "Updates & DLC" tab of a game's options window: which title update it runs with - one, or none - and which DLC.
//
// What it lists comes from the scan of the game's folder (XeniaScan, looked at again when the tab opens and on "Look
// again": quick but for what is new), by content only (XeniaExtras.For): the updates whose patch names this game's
// executable - those for another version of the game are shown, greyed, and cannot be picked - and the DLC of its
// title id, the same package in two files once. Nothing is extracted here: the choice is put down at the next launch.

using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace LbIntegrations.Xenia
{
    internal sealed class XeniaExtrasTab : Panel
    {
        private readonly string _rom, _gameId;
        private readonly XeniaLayout _layout;
        private readonly FlowLayoutPanel _stack;
        private XeniaGameExtras _extras;
        private readonly List<(RadioButton Button, XeniaExtra Update)> _updates = new List<(RadioButton, XeniaExtra)>();
        private readonly List<(CheckBox Box, XeniaExtra Dlc)> _dlc = new List<(CheckBox, XeniaExtra)>();

        public XeniaExtrasTab(string rom, string gameId, string exe)
        {
            _rom = rom;
            _gameId = gameId;
            _layout = exe != null ? XeniaPaths.Resolve(exe) : null;
            Dock = DockStyle.Fill;
            AutoScroll = true;
            Padding = new Padding(10);
            _stack = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, Dock = DockStyle.Top };
            Controls.Add(_stack);
            Build(look: false);   // the cache only: the folder is scanned on the button (Mehdi, 01/10)
        }

        private string Folder => XeniaScan.FolderFor(_rom);

        private void Build(bool look)
        {
            var keep = _extras != null ? Values() : XeniaExtras.ReadChoice(_gameId);
            _stack.SuspendLayout();
            _stack.Controls.Clear();
            _updates.Clear();
            _dlc.Clear();
            Label Line(string text, bool grey = true) => new Label { Text = text, AutoSize = true, MaximumSize = new Size(560, 0), ForeColor = grey ? SystemColors.GrayText : SystemColors.ControlText, Margin = new Padding(0, 2, 0, 6) };

            if (_layout == null || Folder == null) { _stack.Controls.Add(Line("No Xenia or no game file: nothing to look at.", false)); _stack.ResumeLayout(); return; }
            if (look) XeniaScan.ScanShowing(Folder, "Nixx-Xenia - Looking at the game's folder");
            _extras = XeniaExtras.For(_rom, _layout);
            if (_extras == null)
            {
                _stack.Controls.Add(Line(look
                    ? "This game was not recognised in its folder (" + Folder + "): its title id could not be read, so nothing can be matched to it."
                    : "This game's folder has not been looked at yet. Click Look at the folder to look in " + Folder + ".", false));
                if (!look)
                {
                    var first = new Button { Text = "Look at the folder", AutoSize = true, Margin = new Padding(0, 4, 0, 0) };
                    first.Click += (_, _) => Build(look: true);
                    _stack.Controls.Add(first);
                }
                _stack.ResumeLayout();
                return;
            }
            var (update, dlc) = XeniaExtras.Chosen(_extras, keep);

            _stack.Controls.Add(Line("Title id " + _extras.Game.TitleId + (_extras.Game.Digest.Length > 0 ? "  -  executable " + _extras.Game.Digest.Substring(0, 8) : "")
                                     + (look ? ". Looked for in " + Folder + " and its subfolders." : ". What was seen so far - click Look at the folder to look in " + Folder + ".")));

            // ── the update ──
            var ub = XeniaConsolePanel.Group("Title update (one at most - each holds every earlier one)");
            var uf = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, Location = new Point(8, 20) };
            // What the Patches tab has for each version (XeniaPatches.FileFor) - from the files and copies already here.
            List<XeniaPatchFile> patchFiles;
            try { patchFiles = XeniaPatches.Known(_layout, _extras.Game.TitleId); } catch { patchFiles = new List<XeniaPatchFile>(); }
            Label Patches(string version)
            {
                if (patchFiles.Count == 0) return null;
                var (file, sure) = XeniaPatches.FileFor(patchFiles, _extras.Game.TitleId, version);
                var names = file?.Patches.Where(p => p.Name != null).Select(p => p.Name).Distinct().ToList() ?? new List<string>();
                var text = names.Count == 0 ? (sure ? "No patch for this version." : "No patch named for this version.")
                         : names.Count + " patch" + (names.Count > 1 ? "es" : "") + " for this version in the Patches tab: " + string.Join(", ", names)
                           + (sure ? "" : " (by the file's name)") + ".";
                return new Label { Text = text, AutoSize = true, MaximumSize = new Size(520, 0), ForeColor = names.Count > 0 ? SystemColors.ControlText : SystemColors.GrayText, Margin = new Padding(20, 0, 0, 4) };
            }

            var none = new RadioButton { Text = "None: the game as released", AutoSize = true, Checked = update == null, Margin = new Padding(3, 3, 0, 3) };
            uf.Controls.Add(none);
            if (Patches("") is Label np) uf.Controls.Add(np);
            _updates.Add((none, null));
            foreach (var u in _extras.Updates.OrderBy(x => x.Matches ? 0 : 1).ThenByDescending(x => x.Entry.PatchTo))
            {
                var text = u.Entry.Name + "  -  " + XeniaScan.VersionText(u.Entry.PatchFrom) + " -> " + XeniaScan.VersionText(u.Entry.PatchTo) + ", " + SizeText(u.Size)
                           + (u.Matches ? (u.InStore ? ", put down already" : "") : "  (for another version of the game)");
                var r = new RadioButton { Text = text, AutoSize = true, Enabled = u.Matches, Checked = update == u, Margin = new Padding(3, 3, 0, 0) };
                new ToolTip().SetToolTip(r, string.Join("\n", u.Copies.Select(c => c.Path)) + (u.Entry.Problem.Length > 0 ? "\n" + u.Entry.Problem : ""));
                uf.Controls.Add(r);
                uf.Controls.Add(new Label { Text = Source(u), AutoSize = true, ForeColor = SystemColors.GrayText, Margin = new Padding(20, 0, 0, 4) });
                if (u.Matches && Patches(XeniaScan.VersionText(u.Entry.PatchTo)) is Label up) uf.Controls.Add(up);
                _updates.Add((r, u));
            }
            if (_extras.Updates.Count == 0) uf.Controls.Add(Line("No title update for this game in its folder."));
            ub.Controls.Add(uf);
            _stack.Controls.Add(ub);

            // ── the DLC ──
            var db = XeniaConsolePanel.Group("DLC (" + _extras.Dlc.Count + ")");
            var df = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, Location = new Point(8, 20) };
            foreach (var d in _extras.Dlc.OrderBy(x => x.Entry.Name, StringComparer.CurrentCultureIgnoreCase))
            {
                var c = new CheckBox
                {
                    Text = d.Entry.Name + "  -  " + SizeText(d.Size) + (d.InStore ? ", put down already" : "") + (d.Copies.Count > 1 ? ", in " + d.Copies.Count + " files" : ""),
                    AutoSize = true, Checked = dlc.Contains(d), Margin = new Padding(3, 3, 0, 0),
                };
                new ToolTip().SetToolTip(c, string.Join("\n", d.Copies.Select(x => x.Path)));
                df.Controls.Add(c);
                df.Controls.Add(new Label { Text = Source(d), AutoSize = true, ForeColor = SystemColors.GrayText, Margin = new Padding(20, 0, 0, 4) });
                _dlc.Add((c, d));
            }
            if (_extras.Dlc.Count == 0) df.Controls.Add(Line("No DLC for this game in its folder."));
            else
            {
                var all = new LinkLabel { Text = "all", AutoSize = true, Margin = new Padding(3, 6, 0, 0) };
                var nothing = new LinkLabel { Text = "none", AutoSize = true, Margin = new Padding(3, 0, 0, 0) };
                all.LinkClicked += (_, _) => _dlc.ForEach(x => x.Box.Checked = true);
                nothing.LinkClicked += (_, _) => _dlc.ForEach(x => x.Box.Checked = false);
                df.Controls.Add(all);
                df.Controls.Add(nothing);
            }
            db.Controls.Add(df);
            _stack.Controls.Add(db);

            _stack.Controls.Add(Line("Put in place at the next launch - on a RAM disk or on the disk: the Session tab says which, and why. Unpacked once on the disk, "
                                     + "changing the choice later unpacks nothing again. Your own files are never changed."));
            var again = new Button { Text = look ? "Look at the folder again" : "Look at the folder", AutoSize = true, Margin = new Padding(0, 4, 0, 0) };
            again.Click += (_, _) => Build(look: true);
            _stack.Controls.Add(again);
            _stack.ResumeLayout();
        }

        private static string Source(XeniaExtra x)
        {
            var p = x.Entry.Path;
            return p.Contains('|') ? Path.GetFileName(p.Substring(0, p.IndexOf('|'))) : Path.GetFileName(p);
        }

        private static string SizeText(long bytes)
            => bytes >= 1L << 30 ? (bytes / (double)(1L << 30)).ToString("0.0") + " GB" : bytes >= 1L << 20 ? (bytes / (double)(1L << 20)).ToString("0.0") + " MB" : Math.Max(1, bytes / 1024) + " KB";

        /// <summary>The choice as the controls say: only what differs from the default is kept.</summary>
        public Dictionary<string, string> Values()
        {
            var v = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (_extras == null) return XeniaExtras.ReadChoice(_gameId);
            var picked = _updates.FirstOrDefault(x => x.Button.Checked);
            var def = XeniaExtras.Default(_extras);
            if (picked.Button != null && picked.Update != def) v["update"] = picked.Update == null ? "none" : picked.Update.ContentId;
            foreach (var (box, d) in _dlc) if (!box.Checked) v["dlc." + d.ContentId] = "off";
            return v;
        }

        /// <summary>The choice written - <paramref name="also"/> adds what other tabs keep in the same file (the Session tab's).</summary>
        public void Save(Action<IDictionary<string, string>> also = null)
        {
            var v = Values();
            also?.Invoke(v);
            XeniaExtras.WriteChoice(_gameId, v);
            Log.Info("updates and DLC of " + _gameId + ": " + (v.Count == 0 ? "the defaults" : string.Join(", ", v.Select(kv => kv.Key + "=" + kv.Value))));
        }
    }
}
