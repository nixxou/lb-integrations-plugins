// The "Session" tab of a game's options window (Mehdi, 01/10, after Vita3K's): what the next launch would unpack for the
// game - the game itself when it comes in an archive, its title update, its DLC - how big it is, and where it would go:
// a RAM disk or the disk, and why (XeniaExtras.Plan). And the game's own say in it:
//   - where its content goes: Automatic (the rule), always the RAM disk, always the disk;
//   - "keep on the disk": its folder is never purged by the size limit, and does not count towards it;
//   - what was unpacked once and is no longer chosen, freed on a click.
// Read from the scan's cache: nothing is scanned or unpacked by opening it.

using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace LbIntegrations.Xenia
{
    internal sealed class XeniaSessionTab : Panel
    {
        private readonly string _rom, _gameId;
        private readonly XeniaLayout _layout;
        private readonly Func<Dictionary<string, string>> _choice;   // the Updates & DLC tab's choice, as it stands
        private readonly FlowLayoutPanel _stack;
        private RadioButton _auto, _ram, _disk;
        private CheckBox _keep;
        private string _placement;
        private bool _keepOn;

        public XeniaSessionTab(string rom, string gameId, string exe, Func<Dictionary<string, string>> choice)
        {
            _rom = rom;
            _gameId = gameId;
            _layout = exe != null ? XeniaPaths.Resolve(exe) : null;
            _choice = choice;
            var saved = XeniaExtras.ReadChoice(gameId);
            _placement = XeniaExtras.Placement(saved);
            _keepOn = XeniaExtras.Keep(saved);
            Dock = DockStyle.Fill;
            AutoScroll = true;
            Padding = new Padding(10);
            _stack = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, Dock = DockStyle.Top };
            Controls.Add(_stack);
            Show();
        }

        /// <summary>Built again: when the tab is shown (the Updates & DLC choice may have changed) and when an option changes.</summary>
        public new void Show()
        {
            _stack.SuspendLayout();
            _stack.Controls.Clear();
            Label Line(string text, bool grey = true, bool bold = false)
                => new Label { Text = text, AutoSize = true, MaximumSize = new Size(560, 0), ForeColor = grey ? SystemColors.GrayText : SystemColors.ControlText,
                               Font = bold ? new Font(SystemFonts.MessageBoxFont, FontStyle.Bold) : null, Margin = new Padding(0, 2, 0, 6) };

            var x = _layout == null ? null : XeniaExtras.For(_rom, _layout);
            if (x == null)
            {
                _stack.Controls.Add(Line("This game's folder has not been looked at yet: open the Updates & DLC tab and click Look at the folder.", false));
                AddChoices(null);
                _stack.ResumeLayout();
                return;
            }
            var choice = Current();
            var plan = XeniaExtras.Plan(x, choice, _layout);

            // ── what the next launch unpacks ──
            var box = XeniaConsolePanel.Group("What the next launch unpacks");
            var table = new TableLayoutPanel { AutoSize = true, ColumnCount = 2, Location = new Point(8, 20) };
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 300));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
            void Row(string what, string size, bool bold = false)
            {
                int r = table.RowCount++;
                table.Controls.Add(new Label { Text = what, AutoSize = true, Margin = new Padding(0, 3, 4, 3), Font = bold ? new Font(SystemFonts.MessageBoxFont, FontStyle.Bold) : null }, 0, r);
                table.Controls.Add(new Label { Text = size, AutoSize = true, Margin = new Padding(0, 3, 0, 3), Font = bold ? new Font(SystemFonts.MessageBoxFont, FontStyle.Bold) : null }, 1, r);
            }
            var (update, dlc) = XeniaExtras.Chosen(x, choice);
            Row("The game", plan.Archived ? Size(plan.Game) + (plan.GameOnDisk ? " (on the disk already)" : "") : "opened where it is");
            Row(update != null ? "Title update: " + update.Entry.Name : "Title update: none", update != null ? Size(plan.Update) + (update.InStore ? " (on the disk already)" : "") : "-");
            Row("DLC: " + dlc.Count + " of " + x.Dlc.Count, dlc.Count > 0 ? Size(plan.Dlc) : "-");
            Row("In all", Size(plan.Total), bold: true);
            box.Controls.Add(table);
            _stack.Controls.Add(box);

            _stack.Controls.Add(Line("Next launch: " + (plan.Total == 0 ? "nothing to unpack" : plan.Ram ? "a RAM disk, for the session" : "the disk, in " + plan.Folder), false, true));
            _stack.Controls.Add(Line("Because " + plan.Why + "."));

            // ── on the disk now ──
            if (plan.OnDisk > 0)
            {
                _stack.Controls.Add(Line("On the disk now: " + Size(plan.OnDisk) + " in " + plan.Folder
                                         + (plan.Unused > 0 ? ", of which " + Size(plan.Unused) + " is no longer chosen." : "."), false));
                if (plan.Unused > 0)
                {
                    var free = new Button { Text = "Free what is no longer chosen (" + Size(plan.Unused) + ")", AutoSize = true, Margin = new Padding(0, 0, 0, 8) };
                    free.Click += (_, _) =>
                    {
                        if (XeniaConsolePanel.IsRunning(Path.Combine(_layout.InstallDir, XeniaPaths.ExecutableNames[0])))
                        { MessageBox.Show(this, "Xenia is running: close it first.", "Nixx-Xenia", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
                        XeniaExtras.FreeUnused(x, Current());
                        Show();
                    };
                    _stack.Controls.Add(free);
                }
            }
            AddChoices(plan);
            _stack.ResumeLayout();
        }

        private void AddChoices(XeniaExtras.XeniaPlan plan)
        {
            var box = XeniaConsolePanel.Group("This game");
            var flow = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, Location = new Point(8, 20) };
            _auto = new RadioButton { Text = "Automatic - the RAM disk under its threshold, unless part of it is on the disk already", AutoSize = true, Checked = _placement == "auto" };
            _ram = new RadioButton { Text = "Always the RAM disk (whatever its size, and even when a copy is on the disk)", AutoSize = true, Checked = _placement == "ram" };
            _disk = new RadioButton { Text = "Always the disk", AutoSize = true, Checked = _placement == "disk" };
            _keep = new CheckBox
            {
                Text = "Keep it on the disk: never purged by the size limit, and not counted in it", AutoSize = true, Checked = _keepOn, Margin = new Padding(3, 10, 0, 0),
            };
            foreach (var r in new[] { _auto, _ram, _disk })
                r.CheckedChanged += (_, _) => { if (!((RadioButton)r).Checked) return; _placement = r == _ram ? "ram" : r == _disk ? "disk" : "auto"; BeginInvoke(new Action(Show)); };
            _keep.CheckedChanged += (_, _) => _keepOn = _keep.Checked;
            flow.Controls.AddRange(new Control[] { _auto, _ram, _disk, _keep });
            box.Controls.Add(flow);
            _stack.Controls.Add(box);
        }

        /// <summary>The choice as the window stands: the Updates & DLC tab's, with this tab's two over it.</summary>
        private Dictionary<string, string> Current()
        {
            var c = _choice?.Invoke() ?? XeniaExtras.ReadChoice(_gameId);
            Apply(c);
            return c;
        }

        /// <summary>This tab's values into a game's choice - only what is not the default is kept.</summary>
        public void Apply(IDictionary<string, string> choice)
        {
            choice.Remove("placement");
            choice.Remove("keep");
            if (_placement != "auto") choice["placement"] = _placement;
            if (_keepOn) choice["keep"] = "on";
        }

        /// <summary>After the choice is saved: the keep box takes effect on the folder at once, not at the next launch.</summary>
        public void Saved()
        {
            try
            {
                var x = _layout == null ? null : XeniaExtras.For(_rom, _layout);
                if (x != null && Directory.Exists(x.Folder)) XeniaExtras.SetKeep(x.Folder, _keepOn);
            }
            catch { }
        }

        private static string Size(long bytes)
            => bytes >= 1L << 30 ? (bytes / (double)(1L << 30)).ToString("0.00") + " GB" : bytes >= 1L << 20 ? (bytes / (double)(1L << 20)).ToString("0.0") + " MB" : Math.Max(bytes > 0 ? 1 : 0, bytes / 1024) + " KB";
    }
}
