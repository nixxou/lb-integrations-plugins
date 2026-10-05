// The plugin's two windows, reached through Nixx-Menus (src\Menus) by name - a plugin in LaunchBox 14's Local\Plugins gets
// its emulator role and nothing else, so the relay in Plugins\ asks for these classes:
//   - LbIntegrations.Xemu.Settings   the "xemu" tab of the Nixx window (every game)
//   - LbIntegrations.Xemu.GameMenu   "Nixx-Xemu : Options..." on a game's right-click (that game)
// CxbxMenus' shapes, cut down to what this plugin has.

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using Unbroken.LaunchBox.Plugins;
using Unbroken.LaunchBox.Plugins.Data;

namespace LbIntegrations.Xemu
{
    /// <summary>What Nixx-Menus calls, by name. Public, its signatures fixed.</summary>
    public static class Settings
    {
        public static string Title => "xemu";

        /// <summary>Its emulators LaunchBox has, "<title>\t<path>", for the Nixx window's Open buttons - opened as LaunchBox's "Open
        /// emulator" menu opens them, with what this plugin does around it (Shared.Lbip\LbipOpenEmulator).</summary>
        public static string[] Emulators() => LbIntegrations.Lbip.LbipOpenEmulator.Find(p => XemuPaths.IsOurs(p));
        public static string OpenEmulator(string path) => LbIntegrations.Lbip.LbipOpenEmulator.Open(path);

        public static Control CreatePage() => new XemuSettingsPage();

        public static string Save(Control page)
        {
            if (!(page is XemuSettingsPage ours)) return "this is not the xemu page";
            try { return ours.Save(); }
            catch (Exception ex) { Log.Warn("settings page save", ex); return "the settings could not be written: " + ex.Message; }
        }
    }

    /// <summary>What Nixx-Menus calls, by name - see src\Menus\Menus.cs. Public, its signatures fixed.</summary>
    public static class GameMenu
    {
        public static string[] Entries(IGame[] games)
            => games != null && games.Any(XemuGameMenu.IsOurs) ? new[] { XemuGameMenu.Caption } : new string[0];

        public static void Selected(string entry, IGame[] games)
        {
            if (entry == XemuGameMenu.Caption) XemuGameMenu.Open(games ?? new IGame[0]);
        }

        public static Image Icon => LbIntegrations.Lbip.LbipMenuIcon.Of(XemuPaths.IsOurs, XemuPlugin.ResolveFullPath);
    }

    internal sealed class XemuSettingsPage : UserControl
    {
        private readonly TextBox _cache;
        private readonly CheckBox _states;
        private readonly XemuOptionRows _rows;

        public XemuSettingsPage()
        {
            var exe = XemuLibrary.All().FirstOrDefault();
            var s = XemuSettings.Read();
            var scroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(8) };
            var stack = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink };
            scroll.Controls.Add(stack);

            var lines = new List<string>();
            if (exe == null) lines.Add("No xemu of this plugin in the library yet - Add Emulator, Nixx-Xemu, Download.");
            else
            {
                lines.Add("xemu: " + exe + (XemuPaths.InstalledTag(exe) is string tag ? "  (" + tag + ")" : ""));
                lines.Add("Its BIOS: " + XemuPaths.BiosDir(exe) + " - " + (XemuPaths.McpxPath(exe) != null ? "MCPX boot ROM there" : "NO MCPX boot ROM (mcpx_1.0.bin)") + ", "
                          + (XemuPaths.FlashPath(exe) is string flash ? "flash " + Path.GetFileName(flash) : "NO flash BIOS"));
                lines.Add("Each game runs on a console of its own: " + Path.Combine(XemuPaths.HddDir(exe) ?? "", "games"));
            }
            stack.Controls.Add(new Label { Text = string.Join("\n", lines), AutoSize = true, MaximumSize = new Size(600, 0), ForeColor = SystemColors.GrayText, Margin = new Padding(6, 0, 0, 8) });

            var discs = XemuGameForm.Group("Discs xemu cannot open as they are");
            var t = new TableLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 2, Location = new Point(8, 20) };
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 380));
            _cache = new TextBox { Width = 80, Text = s.TryGetValue("cache_gb", out var c) ? c : "", Margin = new Padding(3, 3, 0, 0) };
            t.Controls.Add(new Label { Text = "Copies kept (GB)", AutoSize = true, Margin = new Padding(0, 7, 4, 0) }, 0, 0);
            t.Controls.Add(_cache, 1, 0);
            var help = new Label
            {
                AutoSize = true, MaximumSize = new Size(520, 0), ForeColor = SystemColors.GrayText, Margin = new Padding(0, 6, 0, 4), UseMnemonic = false,
                Text = "xemu opens an XISO only. A redump, a CSO, a CCI or a CHD is served as one where it is, through the Arsenal Image Mounter - "
                       + "nothing copied. Without it (or for an image in a zip / 7z) the XISO is made once into <xemu>\\discs and kept, up to this "
                       + "size (40 GB by default): past it, the copies used longest ago go. Your own files are never changed.",
            };
            t.Controls.Add(help, 0, 1); t.SetColumnSpan(help, 2);
            discs.Controls.Add(t);
            stack.Controls.Add(discs);

            var states = XemuGameForm.Group("Savestates");
            var sp = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Location = new Point(8, 20) };
            _states = new CheckBox { Text = "Show xemu's snapshots in LaunchBox, as the game's savestates", AutoSize = true, Checked = XemuSettings.Savestates() };
            sp.Controls.Add(_states);
            sp.Controls.Add(new Label
            {
                AutoSize = true, MaximumSize = new Size(520, 0), ForeColor = SystemColors.GrayText, Margin = new Padding(18, 2, 0, 4), UseMnemonic = false,
                Text = "xemu makes its snapshots (Snapshots menu) inside the game's console whatever this says. On, each one is also copied "
                       + "out as a file LaunchBox lists, backs up and restores - some 35 MB each, written when the game closes. Off, nothing "
                       + "is copied and no file is put back into a console; turned on again, the snapshots made meanwhile are copied out at "
                       + "the next listing or close, and none is lost.",
            });
            states.Controls.Add(sp);
            stack.Controls.Add(states);

            stack.Controls.Add(new Label { Text = "Options for every game", AutoSize = true, Font = new Font("Segoe UI", 10f, FontStyle.Bold), Margin = new Padding(4, 8, 0, 0) });
            stack.Controls.Add(new Label
            {
                AutoSize = true, MaximumSize = new Size(560, 0), ForeColor = SystemColors.GrayText, Margin = new Padding(4, 2, 0, 4),
                Text = "Given to xemu for the time of a game, in a copy of its settings: your xemu.toml is not written while you play, and what "
                       + "you change in xemu's own windows meanwhile (a pad, a key) comes back into it - but in a part these options set. Unset, "
                       + "xemu's own setting applies. A game's own options (right-click it, Nixx-Xemu : Options...) win over these.",
            });
            var toml = exe != null ? XemuTomlDoc.Load(XemuPaths.TomlOf(exe)) : null;
            _rows = new XemuOptionRows(s, XemuOptionRows.EveryGameFallback(toml));
            stack.Controls.Add(XemuOptionRows.Legend("Not set: the default, else xemu's own (read from its xemu.toml)"));
            stack.Controls.Add(_rows);

            var file = new TextBox { Dock = DockStyle.Bottom, ReadOnly = true, BorderStyle = BorderStyle.None, BackColor = SystemColors.Control,
                                     ForeColor = SystemColors.GrayText, Text = "Settings file: " + XemuSettings.SettingsPath, TabStop = false };
            Controls.Add(scroll);
            Controls.Add(file);
        }

        public string Save()
        {
            var cache = _cache.Text.Trim().Replace(',', '.');
            if (cache.Length > 0 && (!double.TryParse(cache, NumberStyles.Float, CultureInfo.InvariantCulture, out var gb) || gb <= 0))
                return "The copies kept is a number of GB.";
            // Every key kept that this page does not own; its own replaced.
            var all = XemuSettings.Read();
            all["cache_gb"] = cache;
            all["savestates"] = _states.Checked ? "on" : "";
            foreach (var kv in _rows.Values()) all[kv.Key] = kv.Value;
            XemuSettings.Write(all);
            Log.Info("settings written -> " + XemuSettings.SettingsPath);
            return null;
        }
    }

    internal static class XemuGameMenu
    {
        public const string Caption = "Nixx-Xemu : Options...";

        /// <summary>A game of ours: an xemu of this plugin is among what it can be launched with - its own emulator, or one
        /// "Launch With" offers for its platform (LbipLaunchWith) - so a game set to Cxbx-Reloaded gets this entry too.</summary>
        internal static bool IsOurs(IGame game) => LbIntegrations.Lbip.LbipLaunchWith.Offers(game, p => XemuPaths.IsOurs(XemuPlugin.ResolveFullPath(p)));

        internal static void Open(IGame[] games)
        {
            try
            {
                var ours = games.Where(IsOurs).ToList();
                if (ours.Count == 0) return;
                using var form = new XemuGameForm(ours);
                form.ShowDialog(XemuOwnerWindow.Foreground());
            }
            catch (Exception ex) { Log.Warn("game menu", ex); }
        }
    }

    /// <summary>A game's options - a selection of several games takes the same choice; what is shown is the first one's.</summary>
    internal sealed class XemuGameForm : Form
    {
        private readonly List<IGame> _games;
        private readonly XemuOptionRows _rows;
        private readonly Dictionary<string, string> _rowsAtOpen;

        public XemuGameForm(List<IGame> games)
        {
            _games = games;
            var first = games[0];
            var exe = XemuLibrary.For(first);
            var rom = XemuPlugin.ResolveFullPath(XemuPlugin.Safe(() => first.ApplicationPath));
            var choice = XemuSettings.ReadGame(XemuPlugin.Safe(() => first.Id));

            Text = "Nixx-Xemu - " + (games.Count > 1 ? games.Count + " games" : XemuPlugin.Safe(() => first.Title));
            StartPosition = FormStartPosition.CenterParent;
            Font = new Font("Segoe UI", 9f);
            ClientSize = new Size(640, 720);
            MinimizeBox = false; MaximizeBox = false; ShowInTaskbar = false;

            var scroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(8) };
            var stack = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink };
            scroll.Controls.Add(stack);

            // What the game is, read once: its disc's kind, its title, its regions - and its console.
            var about = new List<string>();
            string titleId = null;
            LbIntegrations.Cxbx.XbeInfo xbe = null;
            try
            {
                if (rom != null)
                {
                    var d = XemuDisc.Describe(rom);
                    about.Add(Path.GetFileName(rom) + ": " + KindText(d.Kind) + (d.Problem != null ? " - " + d.Problem : ""));
                    if (d.Xbe != null)
                    {
                        about.Add("\"" + d.Xbe.TitleName + "\", title id " + d.Xbe.TitleIdText + ", regions " + Eeprom.XemuEeprom.Name(d.Xbe.Region & 7));
                        titleId = d.Xbe.TitleIdText;
                        xbe = d.Xbe;
                    }
                }
            }
            catch (Exception ex) { about.Add("The game could not be read: " + ex.Message); }
            if (exe == null) about.Add("No xemu of this plugin in the library.");
            stack.Controls.Add(new Label { Text = string.Join("\n", about), AutoSize = true, MaximumSize = new Size(600, 0), ForeColor = SystemColors.GrayText, Margin = new Padding(4, 0, 0, 8) });
            if (xbe != null) stack.Controls.Add(CompatGroup(exe, xbe));
            if (exe != null) stack.Controls.Add(ConsoleGroup(exe, titleId ?? (rom != null ? XemuDisc.XbeForConsole(rom, exe, out _)?.TitleIdText : null), first, rom));

            stack.Controls.Add(new Label
            {
                AutoSize = true, MaximumSize = new Size(600, 0), ForeColor = SystemColors.GrayText, Margin = new Padding(4, 0, 0, 6),
                Text = (games.Count > 1 ? "The options of the " + games.Count + " games selected, shown from the first. " : "This game's own options. ")
                       + "Unset, an option is every game's (the Nixx window's xemu tab), else the default, else xemu's own setting. They are "
                       + "given to xemu for the time of the game - your xemu.toml is left as it is.",
            });
            var toml = exe != null ? XemuTomlDoc.Load(XemuPaths.TomlOf(exe)) : null;
            _rows = new XemuOptionRows(choice, XemuOptionRows.GameFallback(XemuSettings.Read(), toml));
            _rowsAtOpen = _rows.Values();
            stack.Controls.Add(XemuOptionRows.Legend("Not set: every game's, else the default, else xemu's own"));
            var reset = new Button { Text = "Reset to defaults", AutoSize = true, Margin = new Padding(4, 0, 0, 6) };
            new ToolTip().SetToolTip(reset, "Every option of this game back to unset. Nothing is saved until OK.");
            reset.Click += (_, _) => _rows.Reset();
            stack.Controls.Add(reset);
            stack.Controls.Add(_rows);

            var bottom = new Panel { Dock = DockStyle.Bottom, Height = 46 };
            var ok = new Button { Text = "OK", Width = 90, Anchor = AnchorStyles.Right | AnchorStyles.Bottom };
            var cancel = new Button { Text = "Cancel", Width = 90, DialogResult = DialogResult.Cancel, Anchor = AnchorStyles.Right | AnchorStyles.Bottom };
            ok.Click += (_, _) => { SaveChoice(); DialogResult = DialogResult.OK; Close(); };
            bottom.Controls.AddRange(new Control[] { ok, cancel });
            bottom.Layout += (_, _) =>
            {
                cancel.Location = new Point(bottom.ClientSize.Width - 12 - cancel.Width, 10);
                ok.Location = new Point(cancel.Left - 8 - ok.Width, 10);
            };
            AcceptButton = ok; CancelButton = cancel;
            Controls.Add(scroll);
            Controls.Add(bottom);
        }

        /// <summary>OK changes only what was changed (LbipGameEdit's rule): over several games, each keeps what it has of its own
        /// and the user did not change.</summary>
        private void SaveChoice()
        {
            foreach (var g in _games)
            {
                var id = XemuPlugin.Safe(() => g.Id);
                if (string.IsNullOrEmpty(id)) continue;
                var values = XemuSettings.ReadGame(id);
                foreach (var kv in _rows.Values())
                    if (!_rowsAtOpen.TryGetValue(kv.Key, out var before) || before != kv.Value) values[kv.Key] = kv.Value;
                XemuSettings.WriteGame(id, values);
            }
            Log.Info("game options of " + _games.Count + " game(s) saved");
        }

        /// <summary>The game's own console (Mehdi, 05/10): its disk, where its save stands, and a button to boot it without a disc -
        /// its dashboard, to clear a cache or look at its saves (XemuConsoleBoot).</summary>
        private GroupBox ConsoleGroup(string exe, string titleId, IGame game, string rom)
        {
            var box = Group("This game's console");
            var inner = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, Dock = DockStyle.Fill };
            if (titleId == null)
            {
                // Never hidden (Mehdi, 05/10): why it cannot be shown, and the button there, greyed.
                string why = null;
                if (rom == null) why = "the game has no file";
                else XemuDisc.XbeForConsole(rom, exe, out why);
                inner.Controls.Add(new Label { Text = "Its console cannot be known: " + (why ?? "its title id could not be read") + ".", AutoSize = true, MaximumSize = new Size(540, 0), ForeColor = Color.Firebrick, Margin = new Padding(0, 0, 0, 6) });
                inner.Controls.Add(new Button { Text = "Boot this game's console, without its disc", AutoSize = true, Enabled = false });
                box.Controls.Add(inner);
                return box;
            }
            var hdd = XemuPaths.GameHdd(exe, titleId);
            var lines = new List<string>();
            if (hdd != null && File.Exists(hdd))
            {
                var fi = new FileInfo(hdd);
                lines.Add("Its disk: " + hdd + " - " + (fi.Length >> 20) + " MB, last written " + fi.LastWriteTime.ToString("g"));
            }
            else lines.Add("Its disk: not made yet - made at its first launch, or by booting it below.");
            if (XemuSaveFiles.Side(exe, titleId) is LbIntegrations.Xbox.XboxSaveSide side) lines.AddRange(LbIntegrations.Xbox.XboxSaveSync.Describe(side));
            inner.Controls.Add(new Label { Text = string.Join("\n", lines), AutoSize = true, MaximumSize = new Size(540, 0), Margin = new Padding(0, 0, 0, 6) });
            var boot = new Button { Text = "Boot this game's console, without its disc", AutoSize = true };
            new ToolTip().SetToolTip(boot, "xemu starts on this game's console with its options, its region and the keys of its save, but no disc: its "
                                         + "dashboard - to clear a cache, look at or delete its saves. When it is closed, the save is captured as after a game.");
            boot.Click += (_, _) =>
            {
                var problem = XemuConsoleBoot.Boot(game);
                if (problem != null) MessageBox.Show(this, "The console could not be booted: " + problem, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            };
            inner.Controls.Add(boot);
            box.Controls.Add(inner);
            return box;
        }

        // ── compatibility (XemuCompat, Shared.Xbox\XboxCompat) ───────────────

        private FlowLayoutPanel _compat;

        private void OnUi(Action a)
        {
            try { if (!IsDisposed && IsHandleCreated) BeginInvoke(a); else if (!IsDisposed) HandleCreated += (_, _) => BeginInvoke(a); } catch { }
        }

        /// <summary>What xemu's list says of the game - its newest report - and Cxbx-Reloaded's (Mehdi, 05/10: each emulator's
        /// window shows the other's too). The list asked again (when older than a day) in the background, then redrawn. Which
        /// version of the game a report was made on is not said: its xbe_headers_sha256 is of the headers in the console's memory,
        /// which the kernel rewrites as the game runs (measured on Batman, 05/10) - never the disc's.</summary>
        private GroupBox CompatGroup(string exe, LbIntegrations.Cxbx.XbeInfo xbe)
        {
            var box = Group("Compatibility");
            _compat = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, Dock = DockStyle.Fill };
            box.Controls.Add(_compat);
            ShowCompat(exe, xbe, "");
            System.Threading.Tasks.Task.Run(() =>
            {
                bool asked = false;
                if (!XemuCompat.IsFresh()) { XemuCompat.Fetch(TimeSpan.FromSeconds(30)); asked = true; }
                OnUi(() => ShowCompat(exe, xbe, asked ? "List asked again just now." : ""));
            });
            return box;
        }

        private void ShowCompat(string exe, LbIntegrations.Cxbx.XbeInfo xbe, string note)
        {
            if (_compat == null || _compat.IsDisposed) return;
            _compat.SuspendLayout();
            _compat.Controls.Clear();
            var id = xbe.TitleIdText;
            var (report, title) = LbIntegrations.Xbox.XboxCompat.Xemu(id);
            var state = LbIntegrations.Xbox.XboxCompat.XemuState(report, title);
            var page = LbIntegrations.Xbox.XboxCompat.XemuPage(id, title);
            if (report == null && title == null)
                _compat.Controls.Add(StateRow("xemu", LbIntegrations.Xbox.XboxCompat.HasXemuList() ? "not in its list" : "no list yet", Color.Gray, "xemu.app", page));
            else
            {
                _compat.Controls.Add(StateRow("xemu", state, LbIntegrations.Xbox.XboxCompat.XemuColor(state), title?.Name ?? "xemu.app", page));
                var facts = new List<string>();
                if (report != null)
                {
                    var mine = exe != null ? XemuPaths.InstalledTag(exe) : null;
                    facts.Add("Newest report: " + LbIntegrations.Xbox.XboxCompat.Day(report.When) + ", xemu " + report.XemuVersion + (mine != null ? " (yours: " + mine + ")" : "")
                              + (string.IsNullOrEmpty(report.Platform) ? "" : ", " + report.Platform) + (string.IsNullOrEmpty(report.Gpu) ? "" : ", " + report.Gpu));
                    if (title != null && !string.IsNullOrEmpty(title.Status) && title.Status != report.Rating) facts.Add("The site shows " + title.Status + " for the game (all its versions).");
                }
                _compat.Controls.Add(new Label { AutoSize = true, MaximumSize = new Size(540, 0), ForeColor = SystemColors.GrayText, Margin = new Padding(18, 0, 3, 2), Text = string.Join("\n", facts) });
                if (!string.IsNullOrWhiteSpace(report?.Comment))
                {
                    var c = report.Comment.Replace("\r", "").Trim();
                    if (c.Length > 600) c = c.Substring(0, 600).TrimEnd() + "...";
                    _compat.Controls.Add(new Label { AutoSize = true, MaximumSize = new Size(520, 0), UseMnemonic = false, Margin = new Padding(18, 2, 3, 6), Text = "\"" + c + "\"" });
                }
            }
            var cx = LbIntegrations.Xbox.XboxCompat.Cxbx(xbe.TitleId, xbe.Version, out var hasCxbx);
            if (cx == null) _compat.Controls.Add(StateRow("Cxbx-Reloaded", hasCxbx ? "not in its list" : "no list", Color.Gray, null, null));
            else _compat.Controls.Add(StateRow("Cxbx-Reloaded", cx.State, LbIntegrations.Xbox.XboxCompat.CxbxColor(cx.State),
                                               cx.Serial + " " + cx.Version + (cx.Region.Length > 0 ? ", " + cx.Region : "") + (cx.Updated.Length > 0 && cx.Updated != "N/A" ? ", " + cx.Updated : ""), cx.Url));
            var asked = XemuCompat.Asked();
            _compat.Controls.Add(new Label
            {
                AutoSize = true, MaximumSize = new Size(540, 0), ForeColor = SystemColors.GrayText, Margin = new Padding(3, 6, 3, 0),
                Text = (note.Length > 0 ? note + " " : "") + (asked != null ? "xemu's list as of " + asked.Value.ToLocalTime().ToString("g") + ". " : "")
                       + "Cxbx-Reloaded's reports are mostly from 2020 and 2021.",
            });
            _compat.ResumeLayout();
        }

        private static Control StateRow(string who, string state, Color color, string linkText, string url)
        {
            var row = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 1, 0, 1) };
            row.Controls.Add(new Label { AutoSize = true, Text = "●", ForeColor = color, Margin = new Padding(3, 3, 2, 0) });
            row.Controls.Add(new Label { AutoSize = true, Text = who + ":", Margin = new Padding(0, 3, 4, 0) });
            row.Controls.Add(new Label { AutoSize = true, Text = state, ForeColor = color, Font = new Font("Segoe UI", 9f, FontStyle.Bold), Margin = new Padding(0, 3, 6, 0) });
            if (url != null)
            {
                var link = new LinkLabel { AutoSize = true, Text = linkText ?? url, UseMnemonic = false, Margin = new Padding(0, 3, 0, 0) };
                link.LinkClicked += (_, _) => { try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true }); } catch { } };
                row.Controls.Add(link);
            }
            return row;
        }

        internal static GroupBox Group(string text) => new GroupBox { Text = text, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(8, 4, 8, 8), Margin = new Padding(4, 4, 4, 8), MinimumSize = new Size(560, 0) };

        private static string KindText(XemuDiscKind k)
        {
            switch (k)
            {
                case XemuDiscKind.Xiso: return "an XISO, opened as it is";
                case XemuDiscKind.Redump: return "a redump image, served as an XISO";
                case XemuDiscKind.Compressed: return "a compressed image (CSO, CCI, CHD), served as an XISO";
                case XemuDiscKind.ImageInArchive: return "a disc image in an archive, copied once as an XISO";
                case XemuDiscKind.TreeInArchive: return "an unpacked game in an archive";
                case XemuDiscKind.Zar: return "a ZArchive (.zar)";
                case XemuDiscKind.Xbox360: return "an Xbox 360 game";
                default: return "not an Xbox disc this plugin can read";
            }
        }
    }

    /// <summary>The xemu installs of this plugin the library points at, resolved.</summary>
    internal static class XemuLibrary
    {
        public static List<string> All()
        {
            var found = new List<string>();
            try
            {
                var dm = PluginHelper.DataManager;
                if (dm == null) return found;
                foreach (var emu in dm.GetAllEmulators())
                {
                    var full = XemuPlugin.ResolveFullPath(XemuPlugin.Safe(() => emu.ApplicationPath));
                    if (full != null && XemuPaths.IsOurs(full) && !found.Contains(full, StringComparer.OrdinalIgnoreCase)) found.Add(full);
                }
            }
            catch (Exception ex) { Log.Warn("listing the emulators", ex); }
            return found;
        }

        /// <summary>The game's own emulator when it is ours, else the library's first.</summary>
        public static string For(IGame game)
        {
            try
            {
                var dm = PluginHelper.DataManager;
                var id = XemuPlugin.Safe(() => game.EmulatorId);
                var own = string.IsNullOrWhiteSpace(id) ? null : dm?.GetEmulatorById(id);
                var path = XemuPlugin.ResolveFullPath(XemuPlugin.Safe(() => own?.ApplicationPath));
                if (XemuPaths.IsOurs(path)) return path;
            }
            catch { }
            return All().FirstOrDefault();
        }
    }

    internal sealed class XemuOwnerWindow : IWin32Window
    {
        private XemuOwnerWindow(IntPtr handle) { Handle = handle; }
        public IntPtr Handle { get; }

        public static IWin32Window Foreground()
        {
            var handle = GetForegroundWindow();
            return handle == IntPtr.Zero ? null : new XemuOwnerWindow(handle);
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();
    }
}
