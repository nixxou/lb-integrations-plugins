// The Xenia tab of the pack's configuration window (Nixx-Menus' Tools entry "Nixx Integration Plugins
// Configuration..."): the profile and the console of the Xenia in the library (XeniaConsolePanel), then the options
// passed to every game (XeniaOptionRows) - a game's own are in its right-click menu, Nixx-Xenia : Options....
//
// THE CONTRACT IS A NAME (src\Menus\Settings.cs has it): a public static class LbIntegrations.<assembly name>.Settings
// with Title, Control CreatePage(), string Save(Control).

using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using Unbroken.LaunchBox.Plugins;

namespace LbIntegrations.Xenia
{
    /// <summary>What Nixx-Menus calls, by name. Public, its signatures fixed.</summary>
    public static class Settings
    {
        public static string Title => "Xenia";

        public static Control CreatePage() => new XeniaSettingsPage();

        public static string Save(Control page)
        {
            if (!(page is XeniaSettingsPage ours)) return "this is not the Xenia page";
            try { return ours.Save(); }
            catch (Exception ex) { Log.Warn("settings page save", ex); return "the settings could not be written: " + ex.Message; }
        }
    }

    internal sealed class XeniaSettingsPage : UserControl
    {
        private readonly XeniaConsolePanel _console;
        private readonly XeniaOptionRows _rows;
        private readonly TextBox _line;
        private readonly TextBox _contentFolder, _contentLimit, _ramBelow;
        private readonly CheckBox _ram, _importClean, _importTitle, _optimized, _importRegion;

        public XeniaSettingsPage()
        {
            var exe = XeniaLibrary.Executables().FirstOrDefault(e => XeniaPaths.ForkOf(e) == XeniaFork.Canary);
            var top = new Label
            {
                Dock = DockStyle.Top, Height = 40, Padding = new Padding(12, 8, 12, 0), ForeColor = SystemColors.GrayText,
                Text = "The profile and the console of Xenia, then the options passed to every game at launch. A game's own options, "
                       + "and its compatibility, are in its right-click menu: Nixx-Xenia : Options...",
            };

            var scroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(8) };
            var stack = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink };
            scroll.Controls.Add(stack);

            if (exe != null)
            {
                stack.Controls.Add(new Label { Text = "Xenia: " + exe, AutoSize = true, MaximumSize = new Size(600, 0), ForeColor = SystemColors.GrayText, Margin = new Padding(6, 0, 0, 4) });
                _console = new XeniaConsolePanel(exe);
                stack.Controls.Add(_console);
            }
            else
                stack.Controls.Add(new Label
                {
                    AutoSize = true, MaximumSize = new Size(480, 0), ForeColor = SystemColors.GrayText, Margin = new Padding(6, 4, 0, 10),
                    Text = "No Xenia Canary in the library yet: its profile and console settings show here once it is installed (Add Emulator).",
                });

            // LaunchBox's Import ROM Files wizard, for Microsoft Xbox 360 (XeniaLbImport) - its own group, first, as the Cxbx and
            // Vita3K tabs have it.
            var import = XeniaConsolePanel.Group("LaunchBox's Import ROM Files wizard (Microsoft Xbox 360)");
            var im = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Location = new Point(8, 20) };
            _importClean = new CheckBox { Text = "Filter out what is not a game", AutoSize = true, Checked = XeniaLbImport.Wanted };
            im.Controls.Add(_importClean);
            im.Controls.Add(new Label
            {
                AutoSize = true, MaximumSize = new Size(500, 0), ForeColor = SystemColors.GrayText, Margin = new Padding(18, 0, 0, 6),
                Text = "Each file is read for what it holds, before you click Finish: a disc, an extracted disc, an Arcade, Indie or Games on Demand "
                       + "package stays, loose or in an archive; a title update or a DLC is noted for its game, found again at launch; a theme or "
                       + "anything else goes. A disc image inside an archive is read too - it takes longer, once.",
            });
            _importTitle = new CheckBox { Text = "Rename games when their name is not in LaunchBox's database", AutoSize = true, Checked = XeniaLbImport.Titles };
            im.Controls.Add(_importTitle);
            im.Controls.Add(new Label
            {
                AutoSize = true, MaximumSize = new Size(500, 0), ForeColor = SystemColors.GrayText, Margin = new Padding(18, 0, 0, 6),
                Text = "The file's name is kept when LaunchBox's database knows it on Microsoft Xbox 360; else Xenia's compatibility list's name for "
                       + "its title id, else the game's own - the first the database knows, written as it writes it. Shown in the list before you "
                       + "click Finish.",
            });
            _importRegion = new CheckBox { Text = "Set each game's region after the import", AutoSize = true, Checked = XeniaLbImport.Regions };
            im.Controls.Add(_importRegion);
            im.Controls.Add(new Label
            {
                AutoSize = true, MaximumSize = new Size(500, 0), ForeColor = SystemColors.GrayText, Margin = new Padding(18, 0, 0, 6),
                Text = "The region the game's executable declares - North America, Japan, Europe, Asia, Australia, or World when it plays on "
                       + "several - on the game, or on the version LaunchBox filed it as.",
            });

            import.Controls.Add(im);
            stack.Controls.Add(import);

            // Where a game's title update and DLC are put down for Xenia, and how much room they may take (XeniaExtras).
            var content = XeniaExtras.ReadSettings();
            var cbox = XeniaConsolePanel.Group("Title updates and DLC");
            var ct = new TableLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 2, Location = new Point(8, 20) };
            ct.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
            ct.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 340));
            var defaultFolder = exe != null ? XeniaExtras.ContentFolder(XeniaPaths.Resolve(exe)) : "<Xenia's folder>\\lbip-content";
            _contentFolder = new TextBox { Width = 330, Text = content.TryGetValue("folder", out var cf) ? cf : "", Margin = new Padding(3, 3, 0, 0) };
            _contentFolder.HandleCreated += (_, _) => SendMessage(_contentFolder.Handle, 0x1501, (IntPtr)1, defaultFolder);
            _contentLimit = new TextBox { Width = 80, Text = content.TryGetValue("limit_gb", out var cl) ? cl : "", Margin = new Padding(3, 3, 0, 0) };
            _contentLimit.HandleCreated += (_, _) => SendMessage(_contentLimit.Handle, 0x1501, (IntPtr)1, "0 = no limit");
            ct.Controls.Add(new Label { Text = "Folder", AutoSize = true, Margin = new Padding(0, 7, 4, 0) }, 0, 0);
            ct.Controls.Add(_contentFolder, 1, 0);
            ct.Controls.Add(new Label { Text = "Size limit (GB)", AutoSize = true, Margin = new Padding(0, 7, 4, 0) }, 0, 1);
            ct.Controls.Add(_contentLimit, 1, 1);
            _ram = new CheckBox { Text = "On a RAM disk below", AutoSize = true, Margin = new Padding(0, 6, 0, 0),
                                  Checked = !(content.TryGetValue("ramdisk", out var rd) && string.Equals(rd, "off", StringComparison.OrdinalIgnoreCase)) };
            _ramBelow = new TextBox { Width = 80, Text = content.TryGetValue("ramdisk_below_gb", out var rb) ? rb : "", Margin = new Padding(3, 3, 0, 0) };
            _ramBelow.HandleCreated += (_, _) => SendMessage(_ramBelow.Handle, 0x1501, (IntPtr)1, "2 GB");
            var ramRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = Padding.Empty };
            ramRow.Controls.Add(_ramBelow);
            ramRow.Controls.Add(new Label { Text = "GB of content for the game", AutoSize = true, Margin = new Padding(4, 7, 0, 0) });
            ct.Controls.Add(_ram, 0, 2);
            ct.Controls.Add(ramRow, 1, 2);
            var help = new Label
            {
                AutoSize = true, MaximumSize = new Size(480, 0), ForeColor = SystemColors.GrayText, Margin = new Padding(0, 4, 0, 4),
                Text = "Xenia cannot read an archive, and reads a game's title update and DLC from its own folders only: at a launch, a zipped "
                       + "game and what its options choose are unpacked by the plugin - to a RAM disk for the session when it all fits under that size and "
                       + "none of it is on the disk yet, else here, once, then linked into place. Over the size limit, the games launched longest ago lose "
                       + "theirs, whole - they come back at their next launch. Your own files are never changed.",
            };
            ct.Controls.Add(help, 0, 3);
            ct.SetColumnSpan(help, 2);
            cbox.Controls.Add(ct);
            stack.Controls.Add(cbox);
            // The optimized settings of each game (XeniaOptimized): between every game's options and the game's own.
            var optimized = XeniaConsolePanel.Group("Optimized settings (xenia-manager)");
            var op = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Location = new Point(8, 20) };
            _optimized = new CheckBox { Text = "Apply each game's optimized settings", AutoSize = true,
                                        Checked = XeniaOptimized.On(null, XeniaSettings.Read()) };
            op.Controls.Add(_optimized);
            op.Controls.Add(new Label
            {
                AutoSize = true, MaximumSize = new Size(500, 0), ForeColor = SystemColors.GrayText, Margin = new Padding(18, 0, 0, 6),
                Text = "The settings the community found a game runs best on (xenia-manager's database, from A1eNaz's wiki) - Halo 3's gamma "
                       + "fix, a game's resolution scale... They win over the options for every game below, and a game's own options win over "
                       + "them; each game's options window shows them, and can turn them off for that game.",
            });
            optimized.Controls.Add(op);
            stack.Controls.Add(optimized);

            var own = exe != null ? XeniaOptions.Own(XeniaPaths.Resolve(exe).ConfigFile) : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            stack.Controls.Add(new Label { Text = "Options for every game", AutoSize = true, Font = new Font("Segoe UI", 10f, FontStyle.Bold), Margin = new Padding(4, 8, 0, 2) });
            _rows = new XeniaOptionRows(XeniaSettings.Read(), o => "Xenia's own: " + o.LabelOf(own.TryGetValue(o.Key, out var v) ? v : o.Default));
            stack.Controls.Add(_rows);

            var bottom = new Panel { Dock = DockStyle.Bottom, Height = 104, Padding = new Padding(12, 4, 12, 8) };
            _line = new TextBox { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Font = new Font("Consolas", 9f), BackColor = SystemColors.Window };
            var file = new TextBox { Dock = DockStyle.Bottom, ReadOnly = true, BorderStyle = BorderStyle.None, BackColor = SystemColors.Control,
                                     ForeColor = SystemColors.GrayText, Text = "Settings file: " + XeniaSettings.SettingsPath, TabStop = false };
            bottom.Controls.Add(_line);
            bottom.Controls.Add(XeniaOptionRows.Legend());
            bottom.Controls.Add(file);

            Controls.Add(scroll);
            Controls.Add(top);
            Controls.Add(bottom);
            _rows.Changed += ShowLine;
            ShowLine();
        }

        [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, string lParam);

        private void ShowLine()
        {
            var flags = XeniaSettings.Flags(_rows.Values());
            _line.Text = flags.Count == 0 ? "(nothing added to the command line for every game)" : string.Join(" ", flags);
        }

        public string Save()
        {
            var problem = _rows.Problem() ?? _console?.Problem();
            if (problem != null) return problem;
            var limit = _contentLimit.Text.Trim().Replace(',', '.');
            if (limit.Length > 0 && (!double.TryParse(limit, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var gb) || gb < 0))
                return "The size limit is a number of GB, 0 for none.";
            var below = _ramBelow.Text.Trim().Replace(',', '.');
            if (below.Length > 0 && (!double.TryParse(below, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var rg) || rg <= 0))
                return "The RAM disk threshold is a number of GB.";
            _console?.Save();
            var every = _rows.Values();
            if (!_optimized.Checked) every[XeniaOptimized.SettingKey] = "off";
            XeniaSettings.WriteAll(every);
            XeniaExtras.WriteSettings(new Dictionary<string, string> { ["folder"] = _contentFolder.Text.Trim(), ["limit_gb"] = limit,
                ["ramdisk"] = _ram.Checked ? "" : "off", ["ramdisk_below_gb"] = below, ["import_clean"] = _importClean.Checked ? "" : "off", ["import_title"] = _importTitle.Checked ? "" : "off", ["import_region"] = _importRegion.Checked ? "" : "off" });
            return null;
        }
    }

    /// <summary>The Xenia executables the library points at, resolved - canary first.</summary>
    internal static class XeniaLibrary
    {
#pragma warning disable CS0649
        /// <summary>For the probe, which has no library: the one executable to show. Set by reflection.</summary>
        internal static string ExeOverride;
#pragma warning restore CS0649

        public static List<string> Executables()
        {
            var found = new List<string>();
            if (ExeOverride != null) { found.Add(ExeOverride); return found; }
            try
            {
                var dm = PluginHelper.DataManager;
                if (dm == null) return found;
                foreach (var emu in dm.GetAllEmulators())
                {
                    string path;
                    try { path = emu.ApplicationPath; } catch { continue; }
                    if (!XeniaPaths.IsXeniaExecutable(path)) continue;
                    var full = XeniaPlugin.ResolveFullPathForUi(path);
                    if (full != null && File.Exists(full) && !found.Contains(full, StringComparer.OrdinalIgnoreCase)) found.Add(full);
                }
            }
            catch (Exception ex) { Log.Warn("listing the emulators", ex); }
            return found.OrderBy(e => XeniaPaths.ForkOf(e) == XeniaFork.Canary ? 0 : 1).ToList();
        }
    }
}
