// The right-click entry on the games our Xenia can run - one game or a selection: "Nixx-Xenia : Options...", with two
// tabs:
//   - Options: the game's own (XeniaOptionRows), kept in games\<game id>.ini and passed on its command line at
//     launch over every game's - never written into Xenia's TOML;
//   - Compatibility: where the game stands in Xenia's compatibility list (XeniaCompat), by its title id - every
//     report of that id, the best state first. The list is brought up to date when the window opens if it is older
//     than 8 hours (Mehdi, 01/10), in the background: the tab says so, and shows the new one when it comes.
//
// NOT AN IGameMenuItemPlugin: a plugin in LaunchBox 14's Local\Plugins gets its emulator role and nothing else;
// Nixx-Menus relays to the GameMenu class below by its name - src\Menus\Menus.cs has the contract.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using Unbroken.LaunchBox.Plugins;
using Unbroken.LaunchBox.Plugins.Data;

namespace LbIntegrations.Xenia
{
    internal static class XeniaGameMenu
    {
        public const string Caption = "Nixx-Xenia : Options...";
        private const string Platform = "Microsoft Xbox 360";

        public static Image Icon => LbIntegrations.Lbip.LbipMenuIcon.Of(XeniaPaths.IsXeniaExecutable, XeniaPlugin.ResolveFullPathForUi);

        /// <summary>A game of ours: its own emulator is a Xenia, or it is an Xbox 360 game and a Xenia is in the library.
        /// Asked at every right-click: lookups in memory only.</summary>
        internal static bool IsOurs(IGame game)
        {
            try
            {
                var dm = PluginHelper.DataManager;
                if (dm == null || game == null) return false;
                var own = string.IsNullOrWhiteSpace(game.EmulatorId) ? null : dm.GetEmulatorById(game.EmulatorId);
                if (own != null && XeniaPaths.IsXeniaExecutable(own.ApplicationPath)) return true;
                if (!string.Equals(game.Platform, Platform, StringComparison.OrdinalIgnoreCase)) return false;
                return (dm.GetAllEmulators() ?? new IEmulator[0]).Any(e => e != null && XeniaPaths.IsXeniaExecutable(e.ApplicationPath));
            }
            catch { return false; }
        }

        internal static void Open(IGame[] games)
        {
            try
            {
                var ours = games.Where(IsOurs).ToList();
                Log.Info("game menu: " + ours.Count + " game(s) of ours selected" + (games.Length > ours.Count ? ", " + (games.Length - ours.Count) + " other(s) left out" : ""));
                if (ours.Count == 0) return;
                using var form = new XeniaGameOptionsForm(ours);
                form.ShowDialog(OwnerWindow.Foreground());
            }
            catch (Exception ex) { Log.Warn("game menu", ex); }
        }
    }

    internal sealed class XeniaGameOptionsForm : Form
    {
        private readonly List<(string Id, string Title)> _games;
        private readonly XeniaOptionRows _rows;
        private readonly TextBox _line;
        private readonly Panel _compat;
        private readonly string _titleId;
        private readonly XeniaExtrasTab _extrasTab;
        private readonly XeniaSessionTab _sessionTab;

        public XeniaGameOptionsForm(List<IGame> games)
        {
            _games = games.Select(g => (Safe(() => g.Id), Safe(() => g.Title))).Where(g => g.Item1.Length > 0).ToList();
            var first = _games.FirstOrDefault();
            var saved = XeniaSettings.ReadGame(first.Id);
            bool differ = _games.Skip(1).Any(g => !Same(XeniaSettings.ReadGame(g.Id), saved));

            Text = "Nixx-Xenia - Options" + (_games.Count > 1 ? " (" + _games.Count + " games)" : " - " + first.Title);
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.Sizable;
            MinimizeBox = false;
            ShowInTaskbar = false;
            Font = new Font("Segoe UI", 9f);
            ClientSize = new Size(620, 720);
            MinimumSize = new Size(560, 400);

            // What an unset row falls back to: every game's value when the Xenia tab sets one, else Xenia's own.
            var every = XeniaSettings.Read();
            var exe = ExecutableFor(games.FirstOrDefault());
            var own = exe != null ? XeniaOptions.Own(XeniaPaths.Resolve(exe).ConfigFile) : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            string Fallback(XeniaOption o)
                => every.TryGetValue(o.Key, out var e) ? "every game's: " + o.LabelOf(e) : "Xenia's own: " + o.LabelOf(own.TryGetValue(o.Key, out var v) ? v : o.Default);

            var tabs = new TabControl { Dock = DockStyle.Fill };

            // ── Options ──
            var optionsTab = new TabPage("Options") { UseVisualStyleBackColor = true };
            var intro = new Label
            {
                Dock = DockStyle.Top, Height = 52, Padding = new Padding(10, 8, 10, 0), ForeColor = SystemColors.GrayText,
                Text = (_games.Count == 1 ? first.Title + "'s own options" : _games.Count + " games" + (differ ? " - their options differ: shown from " + first.Title : ", all with the same options"))
                       + ". Unset, an option is every game's (the Nixx window's Xenia tab) or Xenia's own; set, it goes on the game's command line "
                       + "at launch, never into Xenia's config.",
            };
            var scroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(6) };
            _rows = new XeniaOptionRows(saved, Fallback);
            scroll.Controls.Add(_rows);
            var bottom = new Panel { Dock = DockStyle.Bottom, Height = 84, Padding = new Padding(10, 2, 10, 4) };
            _line = new TextBox { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Font = new Font("Consolas", 9f), BackColor = SystemColors.Window };
            bottom.Controls.Add(_line);
            bottom.Controls.Add(XeniaOptionRows.Legend());
            optionsTab.Controls.Add(scroll);
            optionsTab.Controls.Add(intro);
            optionsTab.Controls.Add(bottom);
            _rows.Changed += ShowLine;
            ShowLine();
            tabs.TabPages.Add(optionsTab);

            // ── Updates & DLC: one game at a time - what it can take depends on its own executable ──
            var extrasTab = new TabPage("Updates & DLC") { UseVisualStyleBackColor = true };
            if (_games.Count == 1)
            {
                _extrasTab = new XeniaExtrasTab(XeniaPlugin.ResolveFullPathForUi(Safe(() => games[0].ApplicationPath)), first.Id, exe);
                extrasTab.Controls.Add(_extrasTab);
            }
            else extrasTab.Controls.Add(new Label { Dock = DockStyle.Fill, Padding = new Padding(12), ForeColor = SystemColors.GrayText,
                                                    Text = "A game's title update and DLC are chosen one game at a time: open this window on one game." });
            tabs.TabPages.Add(extrasTab);

            // ── Session: what the next launch unpacks, where, and this game's say in it (XeniaSessionTab) ──
            if (_games.Count == 1)
            {
                var sessionTab = new TabPage("Session") { UseVisualStyleBackColor = true };
                _sessionTab = new XeniaSessionTab(XeniaPlugin.ResolveFullPathForUi(Safe(() => games[0].ApplicationPath)), first.Id, exe, () => _extrasTab?.Values());
                sessionTab.Controls.Add(_sessionTab);
                tabs.TabPages.Add(sessionTab);
                tabs.SelectedIndexChanged += (_, _) => { if (tabs.SelectedTab == sessionTab) _sessionTab.Show(); };
            }

            // ── Compatibility ──
            var compatTab = new TabPage("Compatibility") { UseVisualStyleBackColor = true, Padding = new Padding(10) };
            _compat = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
            compatTab.Controls.Add(_compat);
            tabs.TabPages.Add(compatTab);
            _titleId = TitleIdOf(games.FirstOrDefault());
            ShowCompat(XeniaCompat.IsFresh() ? null : "Bringing the list up to date...");

            var ok = new Button { Text = "OK", Width = 90 };
            var cancel = new Button { Text = "Cancel", Width = 90, DialogResult = DialogResult.Cancel };
            ok.Click += (_, _) => Apply();
            var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 42, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(8, 8, 8, 4) };
            buttons.Controls.Add(cancel);
            buttons.Controls.Add(ok);
            AcceptButton = ok;
            CancelButton = cancel;

            Controls.Add(tabs);
            Controls.Add(buttons);

            Shown += (_, _) =>
            {
                if (XeniaCompat.IsFresh()) return;
                Task.Run(() => XeniaCompat.Fetch(TimeSpan.FromSeconds(20))).ContinueWith(t =>
                {
                    try
                    {
                        if (IsDisposed || !IsHandleCreated) return;
                        BeginInvoke(new Action(() => ShowCompat(t.Status == TaskStatus.RanToCompletion && t.Result ? null : "The list could not be brought up to date: the copy is shown.")));
                    }
                    catch { }
                });
            };
        }

        private void ShowLine()
        {
            var values = XeniaSettings.Read();
            foreach (var kv in _rows.Values()) values[kv.Key] = kv.Value;
            var flags = XeniaSettings.Flags(values);
            _line.Text = flags.Count == 0 ? "(nothing added to the command line)" : "At launch: " + string.Join(" ", flags);
        }

        private void ShowCompat(string note)
        {
            _compat.SuspendLayout();
            _compat.Controls.Clear();
            var stack = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, Dock = DockStyle.Top };
            Label Line(string text, bool grey = true)
                => new Label { Text = text, AutoSize = true, MaximumSize = new Size(560, 0), ForeColor = grey ? SystemColors.GrayText : SystemColors.ControlText, Margin = new Padding(0, 2, 0, 6) };

            if (_games.Count > 1) stack.Controls.Add(Line("Shown for " + _games[0].Title + ", the first of the selection."));
            if (_titleId == null)
                stack.Controls.Add(Line("This game's title id could not be read off its file, so it cannot be looked up in Xenia's compatibility list.", false));
            else
            {
                stack.Controls.Add(Line("Title id " + _titleId));
                var found = XeniaCompat.Lookup(_titleId);
                if (found.Count == 0)
                    stack.Controls.Add(Line(XeniaCompat.Asked() == null ? "No compatibility list yet." : "Not in Xenia's compatibility list: nobody has reported this game yet.", false));
                foreach (var e in found) stack.Controls.Add(XeniaCompatRow.Build(e, 560));
            }
            var asked = XeniaCompat.Asked();
            stack.Controls.Add(Line(note ?? (asked != null ? "List checked " + asked.Value.ToLocalTime().ToString("g", CultureInfo.CurrentCulture) + " (xenia-canary/game-compatibility)." : "")));
            var link = new LinkLabel { Text = "All of Xenia's compatibility reports", AutoSize = true, Margin = new Padding(0, 2, 0, 0) };
            link.LinkClicked += (_, _) => Open("https://github.com/xenia-canary/game-compatibility/issues");
            stack.Controls.Add(link);
            _compat.Controls.Add(stack);
            _compat.ResumeLayout();
        }

        internal static void Open(string url)
        {
            try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
            catch (Exception ex) { Log.Warn("could not open " + url, ex); }
        }

        private static string TitleIdOf(IGame game)
        {
            try
            {
                var rom = XeniaPlugin.ResolveFullPathForUi(game?.ApplicationPath);
                return string.IsNullOrEmpty(rom) ? null : XeniaTitleId.Of(rom);
            }
            catch (Exception ex) { Log.Info("title id: " + ex.Message); return null; }
        }

        private static string ExecutableFor(IGame game)
        {
            try
            {
                var dm = PluginHelper.DataManager;
                if (dm == null) return XeniaLibrary.Executables().FirstOrDefault();
                IEmulator emu = null;
                var id = game == null ? null : Safe(() => game.EmulatorId);
                if (!string.IsNullOrWhiteSpace(id)) emu = dm.GetEmulatorById(id);
                if (emu != null && XeniaPaths.IsXeniaExecutable(emu.ApplicationPath)) return XeniaPlugin.ResolveFullPathForUi(emu.ApplicationPath);
                return XeniaLibrary.Executables().FirstOrDefault();
            }
            catch { return null; }
        }

        private void Apply()
        {
            var problem = _rows.Problem();
            if (problem != null) { MessageBox.Show(this, problem, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
            if (_games.Count > 1
                && MessageBox.Show(this, "These options will be applied to all " + _games.Count + " selected games.", Text,
                                   MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) != DialogResult.OK)
                return;
            _extrasTab?.Save(c => _sessionTab?.Apply(c));
            _sessionTab?.Saved();
            var values = _rows.Values();
            int changed = 0;
            foreach (var g in _games)
            {
                if (Same(XeniaSettings.ReadGame(g.Id), values)) continue;
                XeniaSettings.WriteGame(g.Id, values);
                changed++;
            }
            Log.Info("game options window: " + changed + " change(s), of " + _games.Count + " game(s) - " + (values.Count == 0 ? "none set" : string.Join(", ", values.Select(kv => kv.Key + "=" + kv.Value))));
            DialogResult = DialogResult.OK;
            Close();
        }

        private static bool Same(IDictionary<string, string> a, IDictionary<string, string> b)
            => a.Count == b.Count && a.All(kv => b.TryGetValue(kv.Key, out var v) && string.Equals(v, kv.Value, StringComparison.Ordinal));

        private static string Safe(Func<string> read)
        {
            try { return read() ?? ""; } catch { return ""; }
        }
    }

    /// <summary>One report of Xenia's compatibility list on one line: a dot of the state's colour, the state and its
    /// detail, the known problems, and a link to the report - as Vita3K's tab shows its own.</summary>
    internal static class XeniaCompatRow
    {
        public static Control Build(XeniaCompatEntry e, int width)
        {
            var row = new FlowLayoutPanel { Width = width, AutoSize = true, WrapContents = true, FlowDirection = FlowDirection.LeftToRight, Margin = new Padding(0, 0, 0, 8) };
            var color = ColorOf(e.State);
            var dot = new Panel { Size = new Size(14, 14), Margin = new Padding(0, 3, 6, 0) };
            dot.Paint += (_, p) =>
            {
                p.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                using var b = new SolidBrush(color);
                using var pen = new Pen(ControlPaint.Dark(color), 1f);
                p.Graphics.FillEllipse(b, 1, 1, 11, 11);
                p.Graphics.DrawEllipse(pen, 1, 1, 11, 11);
            };
            row.Controls.Add(dot);
            row.Controls.Add(new Label { AutoSize = true, Margin = new Padding(0, 3, 0, 0), Text = "Xenia compatibility: " });
            row.Controls.Add(new Label { AutoSize = true, Margin = new Padding(0, 3, 0, 0), Text = e.State ?? "?", Font = new Font(SystemFonts.MessageBoxFont, FontStyle.Bold) });
            if (!string.IsNullOrEmpty(e.Detail))
                row.Controls.Add(new Label { AutoSize = true, Margin = new Padding(4, 3, 0, 0), Text = "(" + e.Detail + ")" });
            var link = new LinkLabel { AutoSize = true, Margin = new Padding(8, 3, 0, 0), Text = "report #" + e.Issue.ToString(CultureInfo.InvariantCulture) };
            link.LinkClicked += (_, _) => XeniaGameOptionsForm.Open(e.Url);
            row.Controls.Add(link);
            var lines = new List<string>();
            if (!string.IsNullOrWhiteSpace(e.Title)) lines.Add(e.Title);
            if (e.Labels.Count > 0) lines.Add("Known problems: " + string.Join(", ", e.Labels));
            if (lines.Count > 0)
            {
                var more = new Label { AutoSize = true, MaximumSize = new Size(width - 20, 0), ForeColor = SystemColors.GrayText, Margin = new Padding(20, 2, 0, 0), Text = string.Join("\n", lines) };
                row.SetFlowBreak(link, true);
                row.Controls.Add(more);
            }
            return row;
        }

        /// <summary>The state's colour, as the list's own labels have them.</summary>
        private static Color ColorOf(string state)
        {
            switch ((state ?? "").ToLowerInvariant())
            {
                case "playable": return Color.FromArgb(0x2E, 0xA0, 0x43);
                case "gameplay": return Color.FromArgb(0x8B, 0xC3, 0x4A);
                case "loads": return Color.FromArgb(0xF0, 0xB4, 0x00);
                case "unplayable": return Color.FromArgb(0xD7, 0x3A, 0x49);
                default: return Color.Gray;
            }
        }
    }

    internal sealed class OwnerWindow : IWin32Window
    {
        private OwnerWindow(IntPtr handle) { Handle = handle; }
        public IntPtr Handle { get; }

        public static IWin32Window Foreground()
        {
            var handle = GetForegroundWindow();
            return handle == IntPtr.Zero ? null : new OwnerWindow(handle);
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();
    }

    /// <summary>What Nixx-Menus calls, by name - see src\Menus\Menus.cs. Public, its signatures fixed.</summary>
    public static class GameMenu
    {
        public static string[] Entries(IGame[] games)
            => games != null && games.Any(XeniaGameMenu.IsOurs) ? new[] { XeniaGameMenu.Caption } : new string[0];

        public static void Selected(string entry, IGame[] games)
        {
            if (entry == XeniaGameMenu.Caption) XeniaGameMenu.Open(games ?? new IGame[0]);
        }

        public static Image Icon => XeniaGameMenu.Icon;
    }
}
