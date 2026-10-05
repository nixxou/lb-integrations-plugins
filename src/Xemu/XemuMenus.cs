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
            // A short sentence, the whole of it on hover (Mehdi, 05/10) - over the box too.
            const string discsFull = "xemu opens an XISO only. A redump, a CSO, a CCI or a CHD is served as one where it is, through the Arsenal Image Mounter - "
                       + "nothing copied. Without it (or for an image in a zip / 7z) the XISO is made once into <xemu>\\discs and kept, up to this "
                       + "size (40 GB by default): past it, the copies used longest ago go. Your own files are never changed.";
            var help = LbIntegrations.Lbip.LbipHint.Note("Mounted as an XISO through AIM, else converted once and kept up to this size.",
                                                         discsFull, 520, new Padding(0, 6, 0, 4));
            LbIntegrations.Lbip.LbipHint.Attach(_cache, discsFull);
            t.Controls.Add(help, 0, 1); t.SetColumnSpan(help, 2);
            discs.Controls.Add(t);
            stack.Controls.Add(discs);

            var states = XemuGameForm.Group("Savestates");
            var sp = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Location = new Point(8, 20) };
            _states = new CheckBox { Text = "Show xemu's snapshots in LaunchBox, as the game's savestates", AutoSize = true, Checked = XemuSettings.Savestates() };
            sp.Controls.Add(_states);
            const string statesFull = "xemu makes its snapshots (Snapshots menu) inside the game's console whatever this says. On, each one is also copied "
                       + "out as a file LaunchBox lists, backs up and restores - some 35 MB each, written when the game closes. Off, nothing "
                       + "is copied and no file is put back into a console; turned on again, the snapshots made meanwhile are copied out at "
                       + "the next listing or close, and none is lost.";
            sp.Controls.Add(LbIntegrations.Lbip.LbipHint.Note("Each is copied out (about 35 MB) when the game closes; off, xemu still keeps them.",
                                                              statesFull, 520, new Padding(18, 2, 0, 4)));
            LbIntegrations.Lbip.LbipHint.Attach(_states, statesFull);
            states.Controls.Add(sp);
            stack.Controls.Add(states);

            // The compatibility list, every game's (Mehdi, 05/10): downloaded whole again here too, the date of the last one.
            var compat = XemuGameForm.Group("xemu's compatibility list (xemu.app)");
            var cl = LbIntegrations.Lbip.LbipListRefresh.Row("xemu's compatibility list",
                () => XemuCompat.Downloaded() is DateTime d ? "downloaded " + d.ToString("g") + " - each game's state in its Options window" : "never downloaded yet",
                XemuCompat.FetchWhole, null, 540);
            cl.Location = new Point(8, 20);
            compat.Controls.Add(cl);
            stack.Controls.Add(compat);

            // The console and the disc only (Mehdi, 05/10): xemu's own settings - picture, system, performance, sound - are set for
            // every game in xemu's own window, and game by game in the game's window.
            stack.Controls.Add(new Label { Text = "Console and disc, for every game", AutoSize = true, Font = new Font("Segoe UI", 10f, FontStyle.Bold), Margin = new Padding(4, 8, 0, 0) });
            stack.Controls.Add(LbIntegrations.Lbip.LbipHint.Note("Made for the time of a game, your files untouched; a game's own choice wins.",
                "The console a game runs on and its disc, made for the time of the game - your files are never written. A game's own "
                + "choice (right-click it, Nixx-Xemu : Options..., Console & disc) wins over these. Picture, sound and performance: "
                + "for every game in xemu's own window, for one game in its Options window.",
                560, new Padding(4, 2, 0, 4)));
            var toml = exe != null ? XemuTomlDoc.Load(XemuPaths.TomlOf(exe)) : null;
            _rows = new XemuOptionRows(s, XemuOptionRows.EveryGameFallback(toml), o => !o.IsXemuSetting);
            stack.Controls.Add(XemuOptionRows.Legend("Not set: the default"));
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
        private readonly XemuOptionRows _xemuRows, _consoleRows;
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

            // THREE TABS (Mehdi, 05/10: "le pergame menu est un gros bordel"): the game - its compatibility, its console; xemu's
            // settings for it; its console and disc. What it is, above them all.
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
            if (games.Count > 1) about.Add("The " + games.Count + " games selected take the same choice; what is shown is the first one's.");
            // Above the tabs: what it is, and its compatibility at a glance (Mehdi, 05/10) - drawn again with the Game tab's.
            var header = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new Padding(8, 6, 8, 4) };
            header.Controls.Add(new Label { Text = string.Join("\n", about), AutoSize = true, MaximumSize = new Size(620, 0), ForeColor = SystemColors.GrayText, Margin = new Padding(2, 0, 0, 2) });
            _pills = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = Padding.Empty };
            header.Controls.Add(_pills);

            // A tab: a scrolling page, its controls stacked.
            FlowLayoutPanel Tab(TabControl tabs, string title)
            {
                var page = new TabPage(title) { UseVisualStyleBackColor = true };
                var scroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(8) };
                var stack = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink };
                scroll.Controls.Add(stack);
                page.Controls.Add(scroll);
                tabs.TabPages.Add(page);
                return stack;
            }
            Label Explain(string text) => new Label { AutoSize = true, MaximumSize = new Size(580, 0), ForeColor = SystemColors.GrayText, Margin = new Padding(4, 0, 0, 6), Text = text };
            Button Reset(XemuOptionRows rows, string what)
            {
                var b = new Button { Text = "Reset to defaults", AutoSize = true, Margin = new Padding(4, 0, 0, 6) };
                new ToolTip().SetToolTip(b, what + " back to unset. Nothing is saved until OK.");
                b.Click += (_, _) => rows.Reset();
                return b;
            }
            var tabs = new TabControl { Dock = DockStyle.Fill };

            // ── Game: its compatibility, its console ──
            var game = Tab(tabs, "Game");
            if (xbe != null) game.Controls.Add(CompatGroup(exe, xbe));
            if (exe != null) game.Controls.Add(ConsoleGroup(exe, titleId ?? (rom != null ? XemuDisc.XbeForConsole(rom, exe, out _)?.TitleIdText : null), first, rom));

            // ── xemu's settings for this game ──
            var toml = exe != null ? XemuTomlDoc.Load(XemuPaths.TomlOf(exe)) : null;
            var every = XemuSettings.Read();
            var settings = Tab(tabs, "xemu settings");
            settings.Controls.Add(LbIntegrations.Lbip.LbipHint.Note("For this game only; unset, xemu's own setting applies.",
                "xemu's own settings, for this game only. Unset: xemu's own - its own window sets them for every game. Given "
                + "to xemu for the time of the game: your xemu.toml is left as it is.", 580, new Padding(4, 0, 0, 6)));
            _xemuRows = new XemuOptionRows(choice, XemuOptionRows.GameFallback(every, toml), o => o.IsXemuSetting);
            settings.Controls.Add(XemuOptionRows.Legend("Not set: xemu's own (its xemu.toml), else the default"));
            settings.Controls.Add(Reset(_xemuRows, "Every xemu setting of this game"));
            settings.Controls.Add(_xemuRows);

            // ── its console and disc ──
            var console = Tab(tabs, "Console & disc");
            console.Controls.Add(LbIntegrations.Lbip.LbipHint.Note("For this game only; unset, the Nixx window's choice applies.",
                "The console this game runs on and its disc, made for the time of the game - your files are never written. "
                + "Unset: every game's (the Nixx window's xemu tab), else the default.", 580, new Padding(4, 0, 0, 6)));
            _consoleRows = new XemuOptionRows(choice, XemuOptionRows.GameFallback(every, toml), o => !o.IsXemuSetting);
            console.Controls.Add(XemuOptionRows.Legend("Not set: every game's, else the default"));
            console.Controls.Add(Reset(_consoleRows, "The console and disc options of this game"));
            console.Controls.Add(_consoleRows);

            _rowsAtOpen = Values();

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
            Controls.Add(tabs);
            Controls.Add(header);
            Controls.Add(bottom);
            // Dressed as the Nixx window (Mehdi, 05/10): LiteBox's look, its tabs a page bar at the left.
            LbIntegrations.Ui.NixxShell.Dress(this);
        }

        private Dictionary<string, string> Values()
        {
            var all = new Dictionary<string, string>(_xemuRows.Values(), StringComparer.OrdinalIgnoreCase);
            foreach (var kv in _consoleRows.Values()) all[kv.Key] = kv.Value;
            return all;
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
                foreach (var kv in Values())
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
            // Its files (Mehdi, 05/10: "toutes les infos fichiers qu'on a ... chargé en arrière plan"): read off the window's
            // thread, then drawn - the snapshots' pictures one by one after.
            _files = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, Margin = new Padding(0, 0, 0, 6) };
            inner.Controls.Add(_files);
            var buttons = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = Padding.Empty };
            var boot = new Button { Text = "Boot this game's console, without its disc", AutoSize = true };
            new ToolTip().SetToolTip(boot, "xemu starts on this game's console with its options, its region and the keys of its save, but no disc: its "
                                         + "dashboard - to clear a cache, look at or delete its saves. When it is closed, the save is captured as after a game.");
            boot.Click += (_, _) =>
            {
                var problem = XemuConsoleBoot.Boot(game);
                if (problem != null) MessageBox.Show(this, "The console could not be booted: " + problem, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            };
            var delete = new Button { Text = "Delete this console...", AutoSize = true, Margin = new Padding(8, 3, 3, 3) };
            new ToolTip().SetToolTip(delete, "Made again from scratch at the game's next launch, its save laid in - nothing lost: its snapshots without a "
                                           + "file are exported first, and its save kept apart when it is not the active one.");
            delete.Click += (_, _) =>
            {
                if (MessageBox.Show(this, "Delete this game's console (" + titleId + ".qcow2)?\n\nIt is made again at the next launch, its save laid into it. "
                                    + "Before that, its snapshots that have no state file are exported as one, and its save is kept in lbip-conflicts if it "
                                    + "is not the active one.", Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
                delete.Enabled = false;
                System.Threading.Tasks.Task.Run(() => XemuSaveFiles.DeleteConsole(exe, titleId, out var ok) is string said ? (said, ok) : ("", ok)).ContinueWith(t => OnUi(() =>
                {
                    delete.Enabled = true;
                    var (said, ok) = t.Status == System.Threading.Tasks.TaskStatus.RanToCompletion ? t.Result : (t.Exception?.GetBaseException().Message ?? "failed", false);
                    MessageBox.Show(this, (ok ? "" : "Not deleted: ") + said, Text, MessageBoxButtons.OK, ok ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
                    ShowFiles(exe, titleId);
                }));
            };
            buttons.Controls.Add(boot);
            buttons.Controls.Add(delete);
            inner.Controls.Add(buttons);
            box.Controls.Add(inner);
            ShowFiles(exe, titleId);
            return box;
        }

        // ── its files ────────────────────────────────────────────────────────

        private FlowLayoutPanel _files;

        private sealed class FilesModel
        {
            public string Console, Pack, CxbxPack, Stamp, Problem;
            public bool CxbxThere;
            public List<string> SaveState = new List<string>();
            public string ConsoleHash, PackHash, CxbxHash;
            public List<Saves.XemuStates.SnapshotView> Snapshots;
            public List<Saves.XemuStateFile> Waiting = new List<Saves.XemuStateFile>();
            public List<string> Conflicts = new List<string>();
        }

        private void ShowFiles(string exe, string titleId)
        {
            if (_files == null) return;
            _files.Controls.Clear();
            _files.Controls.Add(new Label { AutoSize = true, ForeColor = SystemColors.GrayText, Text = "Reading its files..." });
            System.Threading.Tasks.Task.Run(() => ReadFiles(exe, titleId)).ContinueWith(t => OnUi(() =>
            {
                if (t.Status == System.Threading.Tasks.TaskStatus.RanToCompletion) DrawFiles(exe, titleId, t.Result);
                else { _files.Controls.Clear(); _files.Controls.Add(new Label { AutoSize = true, ForeColor = Color.Firebrick, Text = "Its files could not be read: " + t.Exception?.GetBaseException().Message }); }
            }));
        }

        private static FilesModel ReadFiles(string exe, string titleId)
        {
            var cx = XemuSaveFiles.FindCxbx(titleId);
            var m = new FilesModel { Console = XemuPaths.GameHdd(exe, titleId), Pack = XemuSaveFiles.PackPath(exe, titleId), CxbxPack = cx != null && File.Exists(cx.Pack) ? cx.Pack : null, CxbxThere = cx != null };
            m.Stamp = m.Console == null ? null : Path.ChangeExtension(m.Console, ".stamp");
            string HashOf(List<(string Name, byte[] Data)> files) => files == null ? null : LbIntegrations.Xbox.XboxSaveSync.ContentHash(files);
            if (XemuSaveFiles.Side(exe, titleId) is LbIntegrations.Xbox.XboxSaveSide side)
            {
                m.SaveState = LbIntegrations.Xbox.XboxSaveSync.Describe(side);
                try { m.ConsoleHash = File.Exists(m.Console) ? HashOf(side.ReadConsole()) : null; } catch { }
            }
            try { m.PackHash = HashOf(LbIntegrations.Xbox.XboxSaveSync.FilesOf(m.Pack)); } catch { }
            try { m.CxbxHash = HashOf(LbIntegrations.Xbox.XboxSaveSync.FilesOf(m.CxbxPack)); } catch { }
            m.Snapshots = Saves.XemuStates.View(exe, titleId, out m.Waiting, out m.Problem);
            var conflicts = XemuPaths.Dir(exe) is string d ? Path.Combine(d, "lbip-conflicts") : null;
            if (conflicts != null && Directory.Exists(conflicts)) m.Conflicts = Directory.GetFiles(conflicts, titleId + "-*.cxbxsave").OrderByDescending(File.GetLastWriteTime).ToList();
            return m;
        }

        private static string SizeOf(long bytes) => bytes >= 10L << 20 ? (bytes >> 20) + " MB" : bytes >= 1 << 20 ? (bytes / 1048576.0).ToString("0.0") + " MB" : Math.Max(1, (bytes + 1023) >> 10) + " KB";

        private static string Facts(string path)
        {
            try { var fi = new FileInfo(path); return SizeOf(fi.Length) + ", " + fi.LastWriteTime.ToString("g"); } catch { return ""; }
        }

        /// <summary>"What: path - 38 MB, 05/10 20:47": the path a link that shows the file in Explorer.</summary>
        private Control FileRow(string what, string exe, string path, string facts = null)
        {
            var row = new FlowLayoutPanel { AutoSize = true, WrapContents = true, MaximumSize = new Size(540, 0), Margin = new Padding(0, 4, 0, 0) };
            row.Controls.Add(new Label { AutoSize = true, Text = what + ":", Font = new Font("Segoe UI", 9f, FontStyle.Bold), Margin = new Padding(0, 0, 4, 0) });
            if (path == null || !File.Exists(path)) { row.Controls.Add(new Label { AutoSize = true, ForeColor = SystemColors.GrayText, Text = facts ?? "none", Margin = Padding.Empty }); return row; }
            var dir = XemuPaths.Dir(exe);
            var shown = dir != null && path.StartsWith(dir + "\\", StringComparison.OrdinalIgnoreCase) ? path.Substring(dir.Length + 1) : path;
            var link = new LinkLabel { AutoSize = true, Text = shown, UseMnemonic = false, Margin = Padding.Empty };
            new ToolTip().SetToolTip(link, path + "\nClick: show it in Explorer.");
            link.LinkClicked += (_, _) => { try { System.Diagnostics.Process.Start("explorer.exe", "/select,\"" + path + "\""); } catch { } };
            row.Controls.Add(link);
            row.Controls.Add(new Label { AutoSize = true, ForeColor = SystemColors.GrayText, Text = "- " + (facts ?? Facts(path)), Margin = new Padding(4, 0, 0, 0) });
            return row;
        }

        /// <summary>A save across, after a word on what it replaces: warned when it is another save, which is kept apart.</summary>
        private void Transfer(string exe, string titleId, bool fromCxbx, FilesModel m)
        {
            var from = fromCxbx ? m.CxbxPack : m.Pack;
            var to = fromCxbx ? m.Pack : m.CxbxPack;
            var replaces = to != null && File.Exists(to) && XemuSaveFiles.WouldReplace(from, to) == true;
            var text = (fromCxbx ? "Make Cxbx-Reloaded's save (" + Facts(from) + ") this game's active save on xemu?"
                                 : "Give this game's active save (" + Facts(from) + ") to Cxbx-Reloaded?")
                       + "\n\nIt is laid into " + (fromCxbx ? "this console" : "Cxbx-Reloaded's console") + " at the next launch there."
                       + (replaces ? "\n\nWARNING: it replaces " + (fromCxbx ? "the active save here" : "Cxbx-Reloaded's save") + " (" + Facts(to) + "), another save - "
                                     + "that one is kept in " + (fromCxbx ? "this xemu's" : "Cxbx-Reloaded's") + " lbip-conflicts folder first." : "");
            if (MessageBox.Show(this, text, Text, MessageBoxButtons.YesNo, replaces ? MessageBoxIcon.Warning : MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
            System.Threading.Tasks.Task.Run(() =>
            {
                bool ok;
                var said = fromCxbx ? XemuSaveFiles.TransferFromCxbx(exe, titleId, out ok) : XemuSaveFiles.TransferToCxbx(exe, titleId, out ok);
                return (said, ok);
            }).ContinueWith(t => OnUi(() =>
            {
                var (said, ok) = t.Status == System.Threading.Tasks.TaskStatus.RanToCompletion ? t.Result : (t.Exception?.GetBaseException().Message ?? "failed", false);
                MessageBox.Show(this, (ok ? "" : "Not done: ") + said, Text, MessageBoxButtons.OK, ok ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
                ShowFiles(exe, titleId);
            }));
        }

        private Label Note(string text, Color? color = null)
            => new Label { AutoSize = true, MaximumSize = new Size(530, 0), ForeColor = color ?? SystemColors.GrayText, Margin = new Padding(14, 1, 0, 2), Text = text, UseMnemonic = false };

        private void DrawFiles(string exe, string titleId, FilesModel m)
        {
            _files.SuspendLayout();
            _files.Controls.Clear();

            // The console.
            _files.Controls.Add(FileRow("Console", exe, m.Console, File.Exists(m.Console ?? "") ? null : "not made yet - made at its first launch, or by booting it below"));
            if (File.Exists(m.Console ?? ""))
            {
                var lines = m.SaveState.Where(l => !l.StartsWith("Active save")).ToList();
                foreach (var l in lines) _files.Controls.Add(Note(l));
                if (m.Stamp != null && File.Exists(m.Stamp)) _files.Controls.Add(Note("Last agreement of the save and the console: " + File.GetLastWriteTime(m.Stamp).ToString("g")));
            }

            // Its snapshots, and their files.
            if (m.Problem != null) _files.Controls.Add(Note("Its snapshots could not be read: " + m.Problem, Color.Firebrick));
            else if (m.Snapshots != null && m.Snapshots.Count > 0)
            {
                _files.Controls.Add(Note(m.Snapshots.Count + " snapshot(s) in it" + (XemuSettings.Savestates() ? "" : " - savestates are off (the Nixx window's xemu tab): not shown in LaunchBox")));
                var pending = new List<(PictureBox Box, string Identity, string File)>();
                foreach (var s in m.Snapshots)
                {
                    var row = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(14, 4, 0, 2) };
                    var pic = new PictureBox { Size = new Size(128, 96), SizeMode = PictureBoxSizeMode.Zoom, BorderStyle = BorderStyle.FixedSingle, BackColor = Color.Black, Margin = new Padding(0, 0, 8, 0) };
                    row.Controls.Add(pic);
                    var text = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, Margin = Padding.Empty };
                    text.Controls.Add(new Label { AutoSize = true, Text = "\"" + s.Name + "\"", Font = new Font("Segoe UI", 9f, FontStyle.Bold), UseMnemonic = false, Margin = Padding.Empty });
                    text.Controls.Add(new Label { AutoSize = true, ForeColor = SystemColors.GrayText, Text = s.Date.ToString("g") + ", its state " + SizeOf((long)s.VmStateSize), Margin = Padding.Empty });
                    if (s.File != null) text.Controls.Add(FileRow("Slot " + s.File.Slot, exe, s.File.Path));
                    else text.Controls.Add(new Label { AutoSize = true, ForeColor = SystemColors.GrayText, Text = XemuSettings.Savestates() ? "no file yet: exported at the next listing or close" : "no file: only in the console", Margin = new Padding(0, 4, 0, 0) });
                    row.Controls.Add(text);
                    _files.Controls.Add(row);
                    pending.Add((pic, s.Identity, s.File?.Path));
                }
                // The pictures, one by one, off the window's thread.
                System.Threading.Tasks.Task.Run(() =>
                {
                    foreach (var (box, identity, file) in pending)
                    {
                        var png = Saves.XemuStates.Thumbnail(exe, titleId, identity, file);
                        if (png == null) continue;
                        try
                        {
                            var image = Image.FromStream(new MemoryStream(png));
                            OnUi(() => { if (!box.IsDisposed) box.Image = image; });
                        }
                        catch { }
                    }
                });
            }
            else if (File.Exists(m.Console ?? "")) _files.Controls.Add(Note("No snapshot in it."));
            foreach (var f in m.Waiting)
            {
                _files.Controls.Add(FileRow("Slot " + f.Slot + " (\"" + f.Name + "\")", exe, f.Path));
                if (Saves.XemuStates.FileThumbnail(f.Path) is byte[] own)
                    try { _files.Controls.Add(new PictureBox { Size = new Size(128, 96), SizeMode = PictureBoxSizeMode.Zoom, BorderStyle = BorderStyle.FixedSingle, BackColor = Color.Black, Margin = new Padding(14, 2, 0, 2), Image = Image.FromStream(new MemoryStream(own)) }); } catch { }
                _files.Controls.Add(Note("Not in the console: put into it at the next launch" + (XemuSettings.Savestates() ? "" : ", once savestates are on")));
            }

            // The active save, and Cxbx-Reloaded's.
            _files.Controls.Add(FileRow("Active save", exe, m.Pack));
            if (m.PackHash != null && m.ConsoleHash != null)
                _files.Controls.Add(Note(m.PackHash == m.ConsoleHash ? "The same as the save in the console." : "Not the save in the console - see its state above.", m.PackHash == m.ConsoleHash ? (Color?)null : Color.DarkGoldenrod));
            if (LbIntegrations.Xbox.XboxCompat.PluginLoaded(LbIntegrations.Xbox.XboxCompat.CxbxPluginType) || m.CxbxPack != null)
            {
                _files.Controls.Add(FileRow("Cxbx-Reloaded's save", exe, m.CxbxPack));
                if (m.CxbxHash != null)
                    _files.Controls.Add(Note(m.CxbxHash == m.PackHash ? "The same save as this one."
                                             : m.CxbxHash == m.ConsoleHash ? "The same as the save in this console, not as the active one."
                                             : "Another save than this one: the game was played on each emulator since they last had the same.",
                                             m.CxbxHash == m.PackHash ? (Color?)null : Color.DarkGoldenrod));
            }

            // The save from one emulator to the other (Mehdi, 05/10), each a Restore on the side it goes to.
            if (m.CxbxThere)
            {
                var swap = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(14, 4, 0, 2) };
                var take = new Button { Text = "Take Cxbx-Reloaded's save", AutoSize = true, Enabled = m.CxbxPack != null && m.CxbxHash != m.PackHash };
                var give = new Button { Text = "Give this save to Cxbx-Reloaded", AutoSize = true, Enabled = File.Exists(m.Pack ?? "") && m.CxbxHash != m.PackHash };
                new ToolTip().SetToolTip(take, "Cxbx-Reloaded's save becomes this game's active save here, laid into its console at the next launch.");
                new ToolTip().SetToolTip(give, "This game's active save becomes Cxbx-Reloaded's, laid into its console at its next launch.");
                take.Click += (_, _) => Transfer(exe, titleId, true, m);
                give.Click += (_, _) => Transfer(exe, titleId, false, m);
                swap.Controls.Add(take);
                swap.Controls.Add(give);
                _files.Controls.Add(swap);
            }

            // The console's versions kept apart.

            if (m.Conflicts.Count > 0)
            {
                _files.Controls.Add(FileRow("Kept apart", exe, m.Conflicts[0]));
                _files.Controls.Add(Note(m.Conflicts.Count == 1 ? "A save kept when another took its place." : m.Conflicts.Count + " saves kept when others took their place (lbip-conflicts) - the newest shown."));
            }
            _files.ResumeLayout();
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
            var outer = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, Dock = DockStyle.Fill };
            _compat = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, Margin = Padding.Empty };
            outer.Controls.Add(_compat);
            // Its whole list downloaded again (Mehdi, 05/10), the date of the last one.
            outer.Controls.Add(LbIntegrations.Lbip.LbipListRefresh.Row("xemu's compatibility list",
                () => XemuCompat.Downloaded() is DateTime d ? "downloaded " + d.ToString("g") + " (xemu.app)" : "never downloaded yet",
                XemuCompat.FetchWhole, () => ShowCompat(exe, xbe, "")));
            box.Controls.Add(outer);
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
                    // The tester's words, whole, in a box of its own (Mehdi, 05/10): read-only, scrolled when long - rules of
                    // dashes and blank lines left out.
                    var lines = report.Comment.Replace("\r", "").Split('\n').Select(l => l.Trim())
                                      .Where(l => l.Length > 0 && !l.All(ch => ch == '-' || ch == '=' || ch == '_' || ch == '*')).ToList();
                    var box = new TextBox
                    {
                        Multiline = true, ReadOnly = true, WordWrap = true, ScrollBars = ScrollBars.Vertical, BorderStyle = BorderStyle.FixedSingle,
                        BackColor = SystemColors.Window, Width = 520, Margin = new Padding(18, 2, 3, 6), TabStop = false,
                        Text = string.Join("\r\n", lines),
                    };
                    box.Height = Math.Min(110, Math.Max(36, TextRenderer.MeasureText(box.Text, box.Font, new Size(box.Width - 22, 0), TextFormatFlags.WordBreak).Height + 10));
                    _compat.Controls.Add(box);
                }
            }
            // Cxbx-Reloaded's line only when Nixx-Cxbx is loaded in this LaunchBox (Mehdi, 05/10): not there or turned off, not shown.
            bool cxbxOn = LbIntegrations.Xbox.XboxCompat.PluginLoaded(LbIntegrations.Xbox.XboxCompat.CxbxPluginType);
            var cx = cxbxOn ? LbIntegrations.Xbox.XboxCompat.Cxbx(xbe.TitleId, xbe.Version, out _) : null;
            if (cxbxOn && cx == null) _compat.Controls.Add(StateRow("Cxbx-Reloaded", "not in its list", Color.Gray, null, null));
            else if (cx != null) _compat.Controls.Add(StateRow("Cxbx-Reloaded", cx.State, LbIntegrations.Xbox.XboxCompat.CxbxColor(cx.State),
                                               cx.Serial + " " + cx.Version + (cx.Region.Length > 0 ? ", " + cx.Region : "") + (cx.Updated.Length > 0 && cx.Updated != "N/A" ? ", " + cx.Updated : ""), cx.Url));
            var asked = XemuCompat.Asked();
            _compat.Controls.Add(new Label
            {
                AutoSize = true, MaximumSize = new Size(540, 0), ForeColor = SystemColors.GrayText, Margin = new Padding(3, 6, 3, 0),
                Text = (note.Length > 0 ? note + " " : "") + (asked != null ? "xemu's list as of " + asked.Value.ToLocalTime().ToString("g") + ". " : "")
                       + (cxbxOn ? "Cxbx-Reloaded's reports are mostly from 2020 and 2021." : ""),
            });
            _compat.ResumeLayout();

            // The same at a glance, above the tabs.
            if (_pills != null && !_pills.IsDisposed)
            {
                _pills.SuspendLayout();
                _pills.Controls.Clear();
                void Pill(string who, string what, Color color)
                {
                    _pills.Controls.Add(new Label { AutoSize = true, Text = "●", ForeColor = color, Margin = new Padding(2, 2, 2, 0) });
                    _pills.Controls.Add(new Label { AutoSize = true, Text = who + ":", Margin = new Padding(0, 2, 3, 0) });
                    _pills.Controls.Add(new Label { AutoSize = true, Text = what, ForeColor = color, Font = new Font("Segoe UI", 9f, FontStyle.Bold), Margin = new Padding(0, 2, 14, 0) });
                }
                bool known = report != null || title != null;
                Pill("xemu", known ? state : "not known", known ? LbIntegrations.Xbox.XboxCompat.XemuColor(state) : Color.Gray);
                if (cxbxOn) Pill("Cxbx-Reloaded", cx?.State ?? "not known", cx != null ? LbIntegrations.Xbox.XboxCompat.CxbxColor(cx.State) : Color.Gray);
                _pills.ResumeLayout();
            }
        }

        private FlowLayoutPanel _pills;

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
#pragma warning disable CS0649
        /// <summary>For the probe: the xemu every game has. Set by reflection.</summary>
        internal static string Override;
#pragma warning restore CS0649

        public static List<string> All()
        {
            if (Override != null) return new List<string> { Override };
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
            if (Override != null) return Override;
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
