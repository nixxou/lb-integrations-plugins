// The plugin's two windows, reached through Nixx-Menus (src\Menus) by name - a plugin in LaunchBox 14's Local\Plugins
// gets its emulator role and nothing else, so the relay in Plugins\ asks for these classes:
//   - LbIntegrations.Cxbx.Settings   the "Cxbx-Reloaded" tab of the Tools window (every game)
//   - LbIntegrations.Cxbx.GameMenu   "Nixx-Cxbx : Options..." on a game's right-click (that game)
// The shapes are Xenia's (XeniaSettingsPage, XeniaGameMenu / XeniaSessionTab), cut down to what this plugin has.

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using Unbroken.LaunchBox.Plugins;
using Unbroken.LaunchBox.Plugins.Data;

namespace LbIntegrations.Cxbx
{
    /// <summary>What Nixx-Menus calls, by name. Public, its signatures fixed.</summary>
    public static class Settings
    {
        public static string Title => "Cxbx-Reloaded";

        public static Control CreatePage() => new CxbxSettingsPage();

        public static string Save(Control page)
        {
            if (!(page is CxbxSettingsPage ours)) return "this is not the Cxbx-Reloaded page";
            try { return ours.Save(); }
            catch (Exception ex) { Log.Warn("settings page save", ex); return "the settings could not be written: " + ex.Message; }
        }
    }

    /// <summary>What Nixx-Menus calls, by name - see src\Menus\Menus.cs. Public, its signatures fixed.</summary>
    public static class GameMenu
    {
        public static string[] Entries(IGame[] games)
            => games != null && games.Any(CxbxGameMenu.IsOurs) ? new[] { CxbxGameMenu.Caption } : new string[0];

        public static void Selected(string entry, IGame[] games)
        {
            if (entry == CxbxGameMenu.Caption) CxbxGameMenu.Open(games ?? new IGame[0]);
        }

        public static Image Icon => LbIntegrations.Lbip.LbipMenuIcon.Of(CxbxPaths.IsCxbx, CxbxPlugin.ResolveFullPathForUi);
    }

    internal sealed class CxbxSettingsPage : UserControl
    {
        private readonly TextBox _folder, _limit, _ramBelow;
        private readonly CheckBox _ram, _borderless, _importClean, _importTitle, _importRegion, _attach;
        private readonly CxbxOptionRows _rows;

        public CxbxSettingsPage()
        {
            var exe = CxbxLibrary.Loaders().FirstOrDefault();
            var s = CxbxSettings.Read();
            var scroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(8) };
            var stack = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink };
            scroll.Controls.Add(stack);

            // What is installed, read the way Cxbx-Reloaded reads it.
            var lines = new List<string>();
            if (exe == null) lines.Add("No Cxbx-Reloaded in the library yet - Add Emulator, Nixx-Cxbx, Download.");
            else
            {
                lines.Add("Cxbx-Reloaded: " + exe + (CxbxPaths.InstalledTag(exe) is string tag ? "  (" + tag + ")" : ""));
                var data = CxbxPaths.DataDir(exe);
                lines.Add("Its data (EmuDisk, the saves): " + (data ?? "not set up yet - it is the first start that decides"));
                if (data != null && string.Equals(data, Path.GetDirectoryName(exe), StringComparison.OrdinalIgnoreCase)) lines.Add("Portable: the data is beside the emulator.");
            }
            if (!CxbxPaths.VcRuntimeX86()) lines.Add("The Visual C++ 2015-2022 runtime (x86) is NOT installed: Cxbx-Reloaded cannot start without it (vc_redist.x86.exe, from Microsoft).");
            stack.Controls.Add(new Label { Text = string.Join("\n", lines), AutoSize = true, MaximumSize = new Size(600, 0), ForeColor = SystemColors.GrayText, Margin = new Padding(6, 0, 0, 8) });

            // LaunchBox's Import ROM Files wizard, for Microsoft Xbox (CxbxImport) - as Vita3K's tab has it.
            var import = Group("LaunchBox's Import ROM Files wizard (Microsoft Xbox)");
            var im = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Location = new Point(8, 20) };
            Label Explain(string text) => new Label { AutoSize = true, MaximumSize = new Size(500, 0), ForeColor = SystemColors.GrayText, Margin = new Padding(18, 0, 0, 6), Text = text };
            _importClean = new CheckBox { Text = "Filter out what is not a game", AutoSize = true, Checked = CxbxSettings.On(s, "import_clean", true) };
            im.Controls.Add(_importClean);
            im.Controls.Add(Explain("Each file is read for what it holds, zipped or not, before you click Finish: a disc with a default.xbe stays; an "
                                    + "Xbox 360 disc or anything else goes. The disc is listed on the way, so its first launch only unpacks it - a zipped "
                                    + "game takes a few seconds to read, once, under a window saying how far it is."));
            _importTitle = new CheckBox { Text = "Rename games when their name is not in LaunchBox's database", AutoSize = true, Checked = CxbxSettings.On(s, "import_title", true) };
            im.Controls.Add(_importTitle);
            im.Controls.Add(Explain("The file's name is kept when LaunchBox's database knows it on Microsoft Xbox; else the compatibility list's name for its "
                                    + "serial, else the name its executable carries - the first the database knows, written as it writes it. Shown in the "
                                    + "list before you click Finish."));
            _importRegion = new CheckBox { Text = "Set each game's region after the import", AutoSize = true, Checked = CxbxSettings.On(s, "import_region", true) };
            im.Controls.Add(_importRegion);
            im.Controls.Add(Explain("The compatibility list's entry for that very disc (Europe, Germany, North America...), else the region its executable "
                                    + "declares - North America, Japan, or the rest of the world as Europe, World when it has several."));
            import.Controls.Add(im);
            stack.Controls.Add(import);

            var games = Group("Games");
            // An ISO / XISO / CSO / CCI / CHD / ZAR read where it is (03/10): attached as a disk through AIM by the RAM disk helper, nothing copied.
            var attachStack = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Location = new Point(8, 20) };
            var noSupport = CxbxRamSession.DiscSupport(exe != null ? Path.GetDirectoryName(exe) : null);
            _attach = new CheckBox { Text = "Mount ISO / XISO / CSO / CCI / CHD / ZAR directly through AIM - no copy, no RAM disk", AutoSize = true, Checked = CxbxSettings.On(s, "attach_discs", true), Enabled = noSupport == null };
            attachStack.Controls.Add(_attach);
            attachStack.Controls.Add(Explain(noSupport == null
                ? "The disc image is attached as a read-only disk for the session and the game read from it (a compressed one decompressed as the game reads) - zips and 7z are still unpacked, "
                  + "as below. Needs the Arsenal Image Mounter."
                : "Unavailable: " + noSupport + " (what is installed shows in the RamDisk & VHDX tab). Until then discs are unpacked, as below."));
            var t = new TableLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 2, Location = new Point(8, 20) };
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 380));
            var defaultFolder = exe != null ? CxbxPlace.GamesFolder(Path.GetDirectoryName(exe)) : "<Cxbx-Reloaded's folder>\\lbip-games";
            _folder = Box(s, "folder", 370, defaultFolder);
            _limit = Box(s, "limit_gb", 80, "0 = no limit");
            t.Controls.Add(Caption("Folder"), 0, 0); t.Controls.Add(_folder, 1, 0);
            t.Controls.Add(Caption("Size limit (GB)"), 0, 1); t.Controls.Add(_limit, 1, 1);
            _ram = new CheckBox { Text = "On a RAM disk below", AutoSize = true, Margin = new Padding(0, 6, 0, 0), Checked = CxbxSettings.On(s, "ramdisk", true) };
            _ramBelow = Box(s, "ramdisk_below_gb", 80, CxbxSettings.DefaultRamDiskBelowGb.ToString(CultureInfo.InvariantCulture) + " GB");
            var ramRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = Padding.Empty };
            ramRow.Controls.Add(_ramBelow);
            ramRow.Controls.Add(new Label { Text = "GB of game", AutoSize = true, Margin = new Padding(4, 7, 0, 0) });
            t.Controls.Add(_ram, 0, 2); t.Controls.Add(ramRow, 1, 2);
            var help = new Label
            {
                AutoSize = true, MaximumSize = new Size(520, 0), ForeColor = SystemColors.GrayText, Margin = new Padding(0, 6, 0, 4), UseMnemonic = false,
                Text = "Cxbx-Reloaded runs a game's executable, not its disc image. At a launch, the plugin unpacks the disc - from the image, or "
                       + "straight out of its zip or 7z without writing the image - to a RAM disk for the session when the game fits under that size, "
                       + "else into this folder, once: the next launches open it from there. Over the size limit, the games launched longest ago lose "
                       + "their copy, whole - it comes back at their next launch. Your own files are never changed. The RAM disk itself is set in the "
                       + "RamDisk & VHDX tab.",
            };
            t.Controls.Add(help, 0, 3); t.SetColumnSpan(help, 2);
            t.Location = Point.Empty;
            attachStack.Controls.Add(t);
            games.Controls.Add(attachStack);
            stack.Controls.Add(games);

            var display = Group("At launch");
            var d = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Location = new Point(8, 20) };
            Label Note(string text, int indent) => new Label { AutoSize = true, MaximumSize = new Size(520 - indent, 0), ForeColor = SystemColors.GrayText, Margin = new Padding(indent, 2, 0, 4), Text = text };

            // A game is launched with the loader alone (Mehdi, 03/10: game-by-game options will come, as for the other
            // emulators, and the GUI around a game would be in their way). Cxbx-Reloaded's window is for its settings,
            // before a game: right-click a game, "Open Nixx-Cxbx..." opens it (CxbxOpenRedirect).
            _borderless = new CheckBox { Text = "Full screen in a window once the game shows", AutoSize = true, Checked = CxbxSettings.On(s, "borderless", true) };
            d.Controls.Add(_borderless);
            d.Controls.Add(Note("Cxbx-Reloaded's own Alt+Enter, sent to the game: a borderless window over the whole screen. Its exclusive full "
                                + "screen (Video settings, in Cxbx-Reloaded) can lock up the screen on some machines; when it is on, this is not sent.", 18));
            d.Controls.Add(Note("Cxbx-Reloaded's own settings (video, controllers, EEPROM...): right-click a game, \"Open Nixx-Cxbx...\" opens its "
                                + "window. Set them there before playing.", 0));
            display.Controls.Add(d);
            stack.Controls.Add(display);

            // The options of every game (CxbxOptions) - a game's own are in its right-click menu, Nixx-Cxbx : Options...
            stack.Controls.Add(new Label { Text = "Options for every game", AutoSize = true, Font = new Font("Segoe UI", 10f, FontStyle.Bold), Margin = new Padding(4, 8, 0, 0) });
            stack.Controls.Add(new Label
            {
                AutoSize = true, MaximumSize = new Size(560, 0), ForeColor = SystemColors.GrayText, Margin = new Padding(4, 2, 0, 4),
                Text = "Written into Cxbx-Reloaded's settings for the time of a game, then put back. Unset, Cxbx-Reloaded's own setting applies - "
                       + "the one its window sets (right-click a game, \"Open Nixx-Cxbx...\"). A game's own options win over these. "
                       + "Audio, the hacks and the experimental LLE parts are set game by game, in its right-click menu.",
            });
            _rows = new CxbxOptionRows(s, CxbxOptionRows.EveryGameFallback(CxbxOwn.Read(exe)), everyGame: true);
            stack.Controls.Add(CxbxOptionRows.Legend("Not set: the default, else Cxbx-Reloaded's own (read from its settings)"));
            stack.Controls.Add(_rows);

            var file = new TextBox { Dock = DockStyle.Bottom, ReadOnly = true, BorderStyle = BorderStyle.None, BackColor = SystemColors.Control,
                                     ForeColor = SystemColors.GrayText, Text = "Settings file: " + CxbxSettings.SettingsPath, TabStop = false };
            Controls.Add(scroll);
            Controls.Add(file);
        }

        public string Save()
        {
            var limit = _limit.Text.Trim().Replace(',', '.');
            if (limit.Length > 0 && (!double.TryParse(limit, NumberStyles.Float, CultureInfo.InvariantCulture, out var gb) || gb < 0))
                return "The size limit is a number of GB, 0 for none.";
            var below = _ramBelow.Text.Trim().Replace(',', '.');
            if (below.Length > 0 && (!double.TryParse(below, NumberStyles.Float, CultureInfo.InvariantCulture, out var rg) || rg <= 0))
                return "The RAM disk threshold is a number of GB.";
            // Every key kept that this page does not own; its own replaced.
            var all = CxbxSettings.Read();
            foreach (var kv in new Dictionary<string, string>
            {
                ["folder"] = _folder.Text.Trim(), ["limit_gb"] = limit, ["ramdisk"] = _ram.Checked ? "" : "off", ["attach_discs"] = _attach.Checked ? "" : "off",
                ["ramdisk_below_gb"] = below, ["borderless"] = _borderless.Checked ? "" : "off",
                ["import_clean"] = _importClean.Checked ? "" : "off", ["import_title"] = _importTitle.Checked ? "" : "off", ["import_by_name"] = "", ["import_region"] = _importRegion.Checked ? "" : "off",
            }) all[kv.Key] = kv.Value;
            foreach (var kv in _rows.Values()) all[kv.Key] = kv.Value;
            CxbxSettings.Write(all);
            Log.Info("settings written -> " + CxbxSettings.SettingsPath);
            return null;
        }

        internal static GroupBox Group(string text) => new GroupBox { Text = text, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(8, 4, 8, 8), Margin = new Padding(4, 4, 4, 8), MinimumSize = new Size(560, 0) };
        private static Label Caption(string text) => new Label { Text = text, AutoSize = true, Margin = new Padding(0, 7, 4, 0) };

        private static TextBox Box(IDictionary<string, string> s, string key, int width, string cue)
        {
            var b = new TextBox { Width = width, Text = s.TryGetValue(key, out var v) ? v : "", Margin = new Padding(3, 3, 0, 0) };
            b.HandleCreated += (_, _) => SendMessage(b.Handle, 0x1501, (IntPtr)1, cue);       // EM_SETCUEBANNER
            return b;
        }

        [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, string lParam);
    }

    internal static class CxbxGameMenu
    {
        public const string Caption = "Nixx-Cxbx : Options...";

        /// <summary>A game of ours: a Cxbx-Reloaded loader is among what it can be launched with - its own emulator, or one
        /// "Launch With" offers for its platform (LbipLaunchWith) - so a game set to xemu gets this entry too.</summary>
        internal static bool IsOurs(IGame game) => LbIntegrations.Lbip.LbipLaunchWith.Offers(game, CxbxPaths.IsCxbx);

        internal static void Open(IGame[] games)
        {
            try
            {
                var ours = games.Where(IsOurs).ToList();
                if (ours.Count == 0) return;
                using var form = new CxbxGameForm(ours);
                form.ShowDialog(OwnerWindow.Foreground());
            }
            catch (Exception ex) { Log.Warn("game menu", ex); }
        }
    }

    /// <summary>A game's placement - RAM disk or disk, and why - and its copy on the disk. A selection of several games
    /// takes the same choice; what is shown is the first one's.</summary>
    internal sealed class CxbxGameForm : Form
    {
        private readonly List<IGame> _games;
        private readonly RadioButton _auto, _ram, _disk;
        private readonly CheckBox _keep, _attachGame;
        private readonly Label _plan, _onDisk;
        private readonly Button _delete;
        private readonly string _exe, _rom;
        private readonly CxbxRom _described;      // read once: an archive's directory is not reread at each click
        private readonly CxbxOptionRows _rows;

        public CxbxGameForm(List<IGame> games)
        {
            _games = games;
            var first = games[0];
            _exe = CxbxLibrary.LoaderFor(first);
            _rom = CxbxPlugin.ResolveFullPathForUi(CxbxPlugin.Safe(() => first.ApplicationPath));
            var choice = CxbxSettings.ReadGame(CxbxPlugin.Safe(() => first.Id));

            Text = "Nixx-Cxbx - Options" + (games.Count > 1 ? " (" + games.Count + " games)" : " - " + CxbxPlugin.Safe(() => first.Title));
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.Sizable;
            MinimizeBox = false; MaximizeBox = false; ShowInTaskbar = false;
            Font = new Font("Segoe UI", 9f);
            ClientSize = new Size(640, 760);
            MinimumSize = new Size(600, 420);

            var stack = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink };
            var d = _described = _rom != null ? CxbxGame.Describe(_rom) : null;
            var facts = new List<string> { _rom ?? "(no file)" };
            if (d != null)
            {
                facts.Add(KindText(d.Kind) + (d.Bytes > 0 ? ", " + Mb(d.Bytes) + (d.Kind == CxbxRomKind.ImageInArchive && d.Xbe == null ? " (the image: its files are known once it is unpacked)" : "") : ""));
                if (d.Xbe != null) facts.Add("Title " + d.Xbe.TitleIdText + (d.Xbe.TitleName.Length > 0 ? "  \"" + d.Xbe.TitleName + "\"" : "") + "  - its saves: E:\\UDATA\\" + d.Xbe.TitleIdText);
                if (d.Problem != null && d.Kind != CxbxRomKind.ImageInArchive) facts.Add("Cannot be launched: " + d.Problem);
            }
            if (games.Count > 1) facts.Add("The choice below goes to the " + games.Count + " games selected.");
            stack.Controls.Add(new Label { Text = string.Join("\n", facts), AutoSize = true, MaximumSize = new Size(560, 0), ForeColor = SystemColors.GrayText, Margin = new Padding(2, 0, 0, 8) });

            // Its state in Cxbx-Reloaded's compatibility list - this very version, then the game's others - asked again in
            // the background as the window opens (at most every 6 hours), and redrawn if the site says something new.
            if (d?.Xbe != null && CxbxCompat.SerialOf(d.Xbe.TitleId) != null)
            {
                var compat = CxbxSettingsPage.Group("Compatibility (cxbx-reloaded.co.uk)");
                _compat = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, Location = new Point(8, 20) };
                compat.Controls.Add(_compat);
                stack.Controls.Add(compat);
                ShowCompat(d.Xbe, "");
                CxbxCompat.RefreshGame(d.Xbe, done: changed => OnUi(() => ShowCompat(d.Xbe, changed ? "Just asked the site again: updated." : "")));
            }

            var where = CxbxSettingsPage.Group("Where it is unpacked");
            var w = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, Location = new Point(8, 20) };
            var placement = CxbxSettings.Placement(choice);
            // Read where it is (AIM) - a bare ISO / XISO / CSO / CCI / CHD, or a ZArchive. Three states: on, off, every game's (the grey square).
            var noSupport = CxbxRamSession.DiscSupport(_exe != null ? Path.GetDirectoryName(_exe) : null, d?.Path);
            bool bareImage = d == null || d.Kind == CxbxRomKind.Image || d.Kind == CxbxRomKind.Zar;
            choice.TryGetValue("attach_discs", out var own);
            _attachGame = new CheckBox
            {
                Text = "Mount the ISO / XISO / CSO / CCI / CHD / ZAR directly through AIM - no copy, no RAM disk", AutoSize = true, ThreeState = true,
                CheckState = own == "on" ? CheckState.Checked : own == "off" ? CheckState.Unchecked : CheckState.Indeterminate,
                Enabled = noSupport == null && bareImage,
            };
            new ToolTip().SetToolTip(_attachGame, "Grey: as every game (the Cxbx-Reloaded tab of the Nixx window, now "
                                                  + (CxbxSettings.On(CxbxSettings.Read(), "attach_discs", true) ? "on" : "off") + ").");
            w.Controls.Add(_attachGame);
            w.Controls.Add(new Label
            {
                AutoSize = true, MaximumSize = new Size(520, 0), ForeColor = SystemColors.GrayText, Margin = new Padding(20, 0, 3, 8),
                Text = noSupport != null ? "Unavailable: " + noSupport + "."
                     : !bareImage ? "Only for a bare disc image (ISO, XISO, CSO, CCI, CHD) or a .zar - a zip or a 7z is unpacked."
                     : "Grey: as every game. When it is mounted, the choice below is not used - nothing is unpacked.",
            });
            _auto = new RadioButton { Text = "Automatic - a RAM disk when it fits under the threshold, else the disk", AutoSize = true, Checked = placement == "auto" };
            _ram = new RadioButton { Text = "Always a RAM disk, whatever its size (when one can be had)", AutoSize = true, Checked = placement == "ram" };
            _disk = new RadioButton { Text = "Always the disk", AutoSize = true, Checked = placement == "disk" };
            _keep = new CheckBox { Text = "Keep its copy on the disk - never removed to make room", AutoSize = true, Checked = CxbxSettings.Keep(choice), Margin = new Padding(3, 8, 3, 3) };
            _plan = new Label { AutoSize = true, MaximumSize = new Size(540, 0), Margin = new Padding(3, 8, 3, 3) };
            w.Controls.AddRange(new Control[] { _auto, _ram, _disk, _keep, _plan });
            _attachGame.CheckStateChanged += (_, _) => ShowPlan();
            where.Controls.Add(w);
            stack.Controls.Add(where);

            var disk = CxbxSettingsPage.Group("Its copy on the disk");
            var k = new FlowLayoutPanel { FlowDirection = FlowDirection.LeftToRight, WrapContents = false, AutoSize = true, Location = new Point(8, 20) };
            _onDisk = new Label { AutoSize = true, Margin = new Padding(3, 7, 8, 3) };
            _delete = new Button { Text = "Delete it", AutoSize = true };
            _delete.Click += (_, _) => DeleteCopy();
            k.Controls.Add(_onDisk); k.Controls.Add(_delete);
            disk.Controls.Add(k);
            stack.Controls.Add(disk);

            // Two tabs: the game (what it is, where it goes), and its options - every game's for what it leaves unset.
            var tabs = new TabControl { Dock = DockStyle.Fill };
            var gameTab = new TabPage("Game") { UseVisualStyleBackColor = true };
            var gameScroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(8) };
            gameScroll.Controls.Add(stack);
            gameTab.Controls.Add(gameScroll);
            tabs.TabPages.Add(gameTab);

            var optionsTab = new TabPage("Options") { UseVisualStyleBackColor = true };
            var optionsScroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(8) };
            var optionsStack = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink };
            optionsStack.Controls.Add(new Label
            {
                AutoSize = true, MaximumSize = new Size(560, 0), ForeColor = SystemColors.GrayText, Margin = new Padding(4, 0, 0, 6),
                Text = (games.Count > 1 ? "The options of the " + games.Count + " games selected, shown from the first. " : "This game's own options. ")
                       + "Unset, an option is every game's (the Nixx window's Cxbx-Reloaded tab), else Cxbx-Reloaded's own setting. "
                       + "They are written into Cxbx-Reloaded's settings for the time of the game, then put back.",
            });
            _rows = new CxbxOptionRows(choice, CxbxOptionRows.GameFallback(CxbxSettings.Read(), CxbxOwn.Read(_exe)));
            _rowsAtOpen = _rows.Values();
            optionsStack.Controls.Add(CxbxOptionRows.Legend("Not set: every game's, else the default, else Cxbx-Reloaded's own"));
            var reset = new Button { Text = "Reset to defaults", AutoSize = true, Margin = new Padding(4, 0, 0, 6) };
            new ToolTip().SetToolTip(reset, "Every option of this game back to unset. Nothing is saved until OK.");
            reset.Click += (_, _) => _rows.Reset();
            optionsStack.Controls.Add(reset);
            optionsStack.Controls.Add(_rows);
            optionsScroll.Controls.Add(optionsStack);
            optionsTab.Controls.Add(optionsScroll);
            tabs.TabPages.Add(optionsTab);

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
            Controls.Add(bottom);

            foreach (var r in new[] { _auto, _ram, _disk }) r.CheckedChanged += (_, _) => ShowPlan();
            ShowPlan();
        }

        private string Placement => _ram.Checked ? "ram" : _disk.Checked ? "disk" : "auto";
        private string AttachChoice => _attachGame.CheckState == CheckState.Checked ? "on" : _attachGame.CheckState == CheckState.Unchecked ? "off" : "";

        private FlowLayoutPanel _compat;

        private void OnUi(Action a)
        {
            try { if (!IsDisposed && IsHandleCreated) BeginInvoke(a); } catch { }
        }

        private void ShowCompat(XbeInfo xbe, string note)
        {
            if (_compat == null || _compat.IsDisposed) return;
            _compat.SuspendLayout();
            _compat.Controls.Clear();
            var (exact, others) = CxbxCompat.For(xbe);
            var serial = CxbxCompat.SerialOf(xbe.TitleId) + " " + CxbxCompat.VersionOf(xbe.Version);
            if (exact == null)
                _compat.Controls.Add(new Label { AutoSize = true, Text = serial + ": not in the list.", ForeColor = SystemColors.GrayText });
            else
            {
                _compat.Controls.Add(Line(exact, serial == exact.Serial + " " + exact.Version ? "This disc" : "This disc (as " + exact.Version + ")", true));
                foreach (var o in others.Take(8)) _compat.Controls.Add(Line(o, "Other version", false));
                if (others.Count > 8) _compat.Controls.Add(new Label { AutoSize = true, ForeColor = SystemColors.GrayText, Text = "... and " + (others.Count - 8) + " more." });
            }
            _compat.Controls.Add(new Label
            {
                AutoSize = true, MaximumSize = new Size(540, 0), ForeColor = SystemColors.GrayText, Margin = new Padding(3, 6, 3, 0),
                Text = (note.Length > 0 ? note + " " : "") + "The reports are mostly from 2020 and 2021: Cxbx-Reloaded has moved on since, a game may well do better now.",
            });
            _compat.ResumeLayout();
        }

        private static Control Line(CxbxCompatEntry e, string what, bool bold)
        {
            var row = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 1, 0, 1) };
            row.Controls.Add(new Label { AutoSize = true, Text = what + ":", Margin = new Padding(3, 3, 4, 0), Font = bold ? new Font("Segoe UI", 9f, FontStyle.Bold) : null });
            row.Controls.Add(new Label { AutoSize = true, Text = e.State, ForeColor = ColorOf(e.State), Font = new Font("Segoe UI", 9f, FontStyle.Bold), Margin = new Padding(0, 3, 4, 0) });
            var link = new LinkLabel { AutoSize = true, Text = e.Serial + " " + e.Version + ", " + e.Region + (e.Updated != "N/A" && e.Updated.Length > 0 ? ", " + e.Updated : ""), Margin = new Padding(0, 3, 0, 0) };
            link.LinkClicked += (_, _) => { try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(e.Url) { UseShellExecute = true }); } catch { } };
            row.Controls.Add(link);
            return row;
        }

        private static Color ColorOf(string state)
        {
            switch (state)
            {
                case "Playable": return Color.FromArgb(0x2E, 0xA0, 0x43);
                case "In-Game": return Color.FromArgb(0x6B, 0x9E, 0x2A);
                case "Boots": return Color.FromArgb(0xD0, 0x8A, 0x00);
                case "Nothing": return Color.FromArgb(0xD7, 0x3A, 0x49);
                default: return Color.Gray;
            }
        }

        private void ShowPlan()
        {
            try
            {
                if (_exe == null || _rom == null) { _plan.Text = "No Cxbx-Reloaded for this game in the library."; _onDisk.Text = ""; _delete.Enabled = false; return; }
                var id = CxbxPlugin.Safe(() => _games[0].Id);
                var saved = CxbxSettings.ReadGame(id);
                var now = new Dictionary<string, string>(saved, StringComparer.OrdinalIgnoreCase) { ["placement"] = Placement, ["attach_discs"] = AttachChoice };
                var plan = CxbxPlace.Plan(_rom, now, Path.GetDirectoryName(_exe), _described);
                _plan.Text = !plan.Unpacks || plan.Attach ? "Next launch: " + plan.Why + "."
                             : "Next launch: " + (plan.Ram ? "a RAM disk" : "the disk") + " - " + plan.Why + ".";
                var size = plan.Folder != null ? CxbxPlace.SizeOf(Path.Combine(plan.Folder, "game")) : 0;
                _onDisk.Text = size > 0 ? Mb(size) + " in " + plan.Folder : "None.";
                _delete.Enabled = size > 0;
            }
            catch (Exception ex) { _plan.Text = "The next launch could not be worked out: " + ex.Message; }
        }

        // What the option rows showed at opening: OK changes only what was changed (LbipGameEdit, Mehdi 04/10) - over
        // several games, each keeps what it has of its own and the user did not change.
        private Dictionary<string, string> _rowsAtOpen;

        private void SaveChoice()
        {
            foreach (var g in _games)
            {
                var id = CxbxPlugin.Safe(() => g.Id);
                if (string.IsNullOrEmpty(id)) continue;
                var values = CxbxSettings.ReadGame(id);
                values["placement"] = Placement == "auto" ? "" : Placement;
                values["keep"] = _keep.Checked ? "on" : "";
                values["attach_discs"] = AttachChoice;
                foreach (var kv in _rows.Values())
                    if (_rowsAtOpen == null || !_rowsAtOpen.TryGetValue(kv.Key, out var before) || before != kv.Value) values[kv.Key] = kv.Value;
                CxbxSettings.WriteGame(id, values);
                var rom = CxbxPlugin.ResolveFullPathForUi(CxbxPlugin.Safe(() => g.ApplicationPath));
                if (_exe != null && rom != null) CxbxPlace.SetKeep(CxbxPlace.GameFolder(Path.GetDirectoryName(_exe), rom), _keep.Checked);
            }
            Log.Info("game options of " + _games.Count + " game(s): placement " + Placement + ", keep " + (_keep.Checked ? "on" : "off"));
        }

        private void DeleteCopy()
        {
            try
            {
                if (CxbxPaths.LoaderRunning()) { MessageBox.Show(this, "Cxbx-Reloaded is running - close it first.", Text); return; }
                var folder = CxbxPlace.GameFolder(Path.GetDirectoryName(_exe), _rom);
                if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
                Log.Info("copy on the disk deleted: " + folder);
            }
            catch (Exception ex) { MessageBox.Show(this, "It could not be deleted: " + ex.Message, Text); }
            ShowPlan();
        }

        private static string KindText(CxbxRomKind k)
        {
            switch (k)
            {
                case CxbxRomKind.Xbe: return "An Xbox executable, opened where it is";
                case CxbxRomKind.Image: return "A disc image";      // plain, CSO, CCI or CHD
                case CxbxRomKind.ImageInArchive: return "A disc image in an archive";
                case CxbxRomKind.TreeInArchive: return "An unpacked game in an archive";
                case CxbxRomKind.Xbox360: return "An Xbox 360 game";
                case CxbxRomKind.Zar: return "A ZArchive (.zar) of the game's files";
                default: return "Not an Xbox game this plugin can read";
            }
        }

        private static string Mb(long bytes) => bytes >= 1L << 30 ? (bytes / (double)(1L << 30)).ToString("0.0", CultureInfo.InvariantCulture) + " GB" : (bytes >> 20) + " MB";
    }

    /// <summary>The Cxbx-Reloaded loaders the library points at, resolved.</summary>
    internal static class CxbxLibrary
    {
#pragma warning disable CS0649
        internal static string ExeOverride;
#pragma warning restore CS0649

        public static List<string> Loaders()
        {
            var found = new List<string>();
            if (ExeOverride != null) { found.Add(ExeOverride); return found; }
            try
            {
                var dm = PluginHelper.DataManager;
                if (dm == null) return found;
                foreach (var emu in dm.GetAllEmulators())
                {
                    var path = CxbxPlugin.Safe(() => emu.ApplicationPath);
                    if (!CxbxPaths.IsCxbx(path)) continue;
                    var full = CxbxPlugin.ResolveFullPathForUi(path);
                    if (full != null && File.Exists(full) && !found.Contains(full, StringComparer.OrdinalIgnoreCase)) found.Add(full);
                }
            }
            catch (Exception ex) { Log.Warn("listing the emulators", ex); }
            return found;
        }

        /// <summary>The game's own emulator when it is a loader, else the library's first.</summary>
        public static string LoaderFor(IGame game)
        {
            try
            {
                var dm = PluginHelper.DataManager;
                var id = CxbxPlugin.Safe(() => game.EmulatorId);
                var own = string.IsNullOrWhiteSpace(id) ? null : dm?.GetEmulatorById(id);
                var path = CxbxPlugin.ResolveFullPathForUi(CxbxPlugin.Safe(() => own?.ApplicationPath));
                if (CxbxPaths.IsCxbx(path)) return path;
            }
            catch { }
            return Loaders().FirstOrDefault();
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
}
