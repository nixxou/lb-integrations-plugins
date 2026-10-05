// The Vita3K tab of the pack's configuration window. Nixx-Menus owns the window (its Tools menu entry
// "Nixx Integration Plugins Configuration..."), this plugin builds what is inside its own tab.
//
// THE CONTRACT IS A NAME, as for the game menus (src\Menus\Settings.cs has it): a public static class
// LbIntegrations.<assembly name>.Settings with
//
//     public static string Title { get; }                // the tab's name
//     public static Control CreatePage();                // the tab's content, built here
//     public static string Save(Control page);           // on OK / Apply: null, or why not
//
// Control is WinForms' own, the same type on both sides - nothing else is shared.

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace LbIntegrations.Vita3k
{
    /// <summary>What Nixx-Menus calls, by name. Public, its signatures fixed.</summary>
    public static class Settings
    {
        public static string Title => "Vita3K";

        /// <summary>Its emulators LaunchBox has, "<title>\t<path>", for the Nixx window's Open buttons - opened as LaunchBox's "Open
        /// emulator" menu opens them, with what this plugin does around it (Shared.Lbip\LbipOpenEmulator).</summary>
        public static string[] Emulators() => LbIntegrations.Lbip.LbipOpenEmulator.Find(p => Vita3kPaths.IsVita3kExecutable(p));
        public static string OpenEmulator(string path) => LbIntegrations.Lbip.LbipOpenEmulator.Open(path);

        public static Control CreatePage() => new Vita3kSettingsPage();

        public static string Save(Control page)
        {
            if (!(page is Vita3kSettingsPage ours)) return "this is not the Vita3K page";
            try
            {
                if (Vita3kSettings.BypassVitaImport != ours.Bypass) Vita3kSettings.BypassVitaImport = ours.Bypass;
                if (Vita3kSettings.CleanImportList != ours.Clean) Vita3kSettings.CleanImportList = ours.Clean;
                if (Vita3kSettings.ImportTitle != ours.Title) Vita3kSettings.ImportTitle = ours.Title;
                if (Vita3kSettings.ImportRegionVersion != ours.RegionVersion) Vita3kSettings.ImportRegionVersion = ours.RegionVersion;
            }
            catch (Exception ex) { return "the settings could not be written: " + ex.Message; }
            return ours.SaveSystem();
        }
    }

    internal sealed class Vita3kSettingsPage : UserControl
    {
        private readonly CheckBox _bypass, _clean, _title, _regionVersion;
        private readonly Vita3kLayout _layout;
        private readonly VitaSystemSettings _shown;
        private readonly Vita3kSystemFields _system;

        public bool Bypass => _bypass.Checked;
        public bool Clean => _clean.Checked;
        public bool Title => _title.Checked;
        public bool RegionVersion => _regionVersion.Checked;

        public Vita3kSettingsPage()
        {
            AutoScroll = true;
            Padding = new Padding(12);

            var import = new GroupBox { Text = "LaunchBox's Import ROM Files wizard", Dock = DockStyle.Top, Height = 250, Padding = new Padding(10) };
            _bypass = new CheckBox
            {
                Text = "Import Vita games as ROM files (bypass LaunchBox's own PS Vita import)",
                AutoSize = true, Location = new Point(14, 26), Checked = Vita3kSettings.BypassVitaImport,
            };
            var explain = new Label
            {
                AutoSize = false, Location = new Point(32, 52), Size = new Size(540, 86), ForeColor = SystemColors.GrayText,
                Text = "For \"Sony Playstation Vita\" (or a platform scraped as it), LaunchBox ignores the files you pick "
                     + "and lists the games installed in Vita3K instead - none, with this plugin's console, which is "
                     + "rebuilt for every session. When this is on, the wizard scans your .zip / .vpk files like any "
                     + "other ROMs; the platform, its metadata and its images stay \"Sony Playstation Vita\".",
            };
            _clean = new CheckBox
            {
                Text = "Filter out what is not a game",
                AutoSize = true, Location = new Point(32, 146), Checked = Vita3kSettings.CleanImportList,
            };
            var explainClean = new Label
            {
                AutoSize = false, Location = new Point(50, 170), Size = new Size(522, 52), ForeColor = SystemColors.GrayText,
                Text = "Each file's param.sfo is read before you click Finish: a game stays; an update or a DLC is noted for "
                     + "its game, found again at launch; a .pkg with no licence and anything else goes.",
            };
            _title = new CheckBox
            {
                Text = "Rename games when their name is not in LaunchBox's database",
                AutoSize = true, Location = new Point(32, 226), Checked = Vita3kSettings.ImportTitle,
            };
            var explainTitle = new Label
            {
                AutoSize = false, Location = new Point(50, 250), Size = new Size(522, 52), ForeColor = SystemColors.GrayText,
                Text = "The file's name is kept when LaunchBox's database knows it on Sony Playstation Vita; else the param.sfo's "
                     + "title, written as the database writes it. Shown in the list before you click Finish.",
            };
            // After the import (Mehdi, 30/09) - see Vita3kImportFinished. Read while the list is read.
            _regionVersion = new CheckBox
            {
                Text = "Set each game's region and version after the import",
                AutoSize = true, Location = new Point(32, 306), Checked = Vita3kSettings.ImportRegionVersion,
            };
            var explainRegion = new Label
            {
                AutoSize = false, Location = new Point(50, 330), Size = new Size(522, 56), ForeColor = SystemColors.GrayText,
                Text = "Region: from the game's param.sfo - the store its CONTENT_ID names (U North America, E Europe, "
                     + "J Japan, H Asia, K Korea), else its title id. Version: only the [tags] and (tags) of the file's "
                     + "name - \"[PCSA00017] [USA] [NoNpDRM]\" - and nothing when it has none.",
            };
            void Enable() { _clean.Enabled = _title.Enabled = _regionVersion.Enabled = _bypass.Checked; }
            Enable();
            _bypass.CheckedChanged += (_, _) => Enable();
            import.Height = 396;
            import.Controls.Add(_bypass);
            import.Controls.Add(explain);
            import.Controls.Add(_clean);
            import.Controls.Add(explainClean);
            import.Controls.Add(_title);
            import.Controls.Add(explainTitle);
            import.Controls.Add(_regionVersion);
            import.Controls.Add(explainRegion);

            var where = new Label
            {
                Dock = DockStyle.Top, Height = 40, Padding = new Padding(0, 12, 0, 0), ForeColor = SystemColors.GrayText,
                Text = "Settings file: " + Vita3kSettings.SettingsPath,
            };

            // THE SYSTEM SETTINGS of the Vita3K this pack installed (Emulators\Nixx-Vita3K): config.yml's,
            // those every game without its own runs on - the same four the install asked about.
            var exe = Vita3kPlugin.KnownExecutables().FirstOrDefault();
            _layout = exe == null ? null : Vita3kPaths.Resolve(exe);
            var system = new GroupBox { Text = "System settings - what Vita3K tells every game", Dock = DockStyle.Top, Height = 224, Padding = new Padding(10) };
            if (_layout == null)
                system.Controls.Add(new Label
                {
                    AutoSize = false, Location = new Point(14, 26), Size = new Size(540, 40), ForeColor = SystemColors.GrayText,
                    Text = "Nixx-Vita3K is not installed: there is nothing to configure yet.",
                });
            else
            {
                _shown = Vita3kConfig.Read(_layout);
                _system = new Vita3kSystemFields { Location = new Point(14, 26) };
                _system.ShowValues(_shown);
                system.Controls.Add(_system);
                system.Controls.Add(new Label
                {
                    AutoSize = false, Location = new Point(14, 182), Size = new Size(540, 36), ForeColor = SystemColors.GrayText,
                    Text = "Written into portable\\config.yml. A game's own system settings are in its Options window. "
                         + "The library keeps naming games in US English.",
                });
            }

            // NO GRAPHICS HERE (Mehdi, 29/09): writing Vita3K's own graphics keys from a list of ours is asking
            // for trouble the day Vita3K changes them - a game's own are safer, set for a session over the
            // user's own and put back after (its Options). For anything else, Vita3K's own file, opened as it
            // is: config.yml IS its settings, and Vita3K's own window already edits it.
            if (_layout != null)
            {
                system.Height = 262;
                var open = new Button { Text = "Open config.yml...", AutoSize = true, Location = new Point(14, 222) };
                open.Click += (_, _) => OpenConfigYml();
                system.Controls.Add(open);
                system.Controls.Add(new Label
                {
                    AutoSize = true, Location = new Point(150, 227), ForeColor = SystemColors.GrayText,
                    Text = "every other setting, in Vita3K's own file - Vita3K must be closed, it rewrites it as it quits",
                });
            }

            Controls.Add(where);
            Controls.Add(system);
            Controls.Add(import);
        }

        /// <summary>The system settings, written when they changed. Null, or why not.</summary>
        public string SaveSystem()
        {
            if (_layout == null || _system == null) return null;
            var chosen = _system.Read();
            if (Vita3kSystemFields.Key(chosen) == Vita3kSystemFields.Key(_shown)) return null;
            // Vita3K writes its whole config.yml as it quits: a change now would be lost.
            if (Vita3kPaths.EmulatorRunning()) return "Vita3K is running - close it first to change its system settings.";
            if (!Vita3kConfig.Write(_layout, chosen)) return "Vita3K's system settings could not be written (see the log).";
            _shown.Language = chosen.Language; _shown.DateFormat = chosen.DateFormat;
            _shown.TimeFormat = chosen.TimeFormat; _shown.EnterButton = chosen.EnterButton; _shown.Pstv = chosen.Pstv;
            return null;
        }

        private void OpenConfigYml()
        {
            try
            {
                if (Vita3kPaths.EmulatorRunning())
                {
                    MessageBox.Show(this, "Vita3K is running: close it first - it rewrites config.yml as it quits, over any change made meanwhile.",
                                    "Nixx-Vita3K", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                var portable = Vita3kPaths.PortableDirOf(_layout?.InstallDir);
                var yml = portable == null ? null : System.IO.Path.Combine(portable, "config.yml");
                if (yml == null || !System.IO.File.Exists(yml)) { MessageBox.Show(this, "There is no config.yml yet: Vita3K writes it the first time it runs.", "Nixx-Vita3K"); return; }
                // Notepad, not the file's association: .yml often has none, or opens something that is not an editor.
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("notepad.exe", "\"" + yml + "\"") { UseShellExecute = true });
                Log.Info("configuration window: config.yml opened in Notepad");
            }
            catch (Exception ex) { Log.Warn("could not open config.yml", ex); }
        }
    }
}
