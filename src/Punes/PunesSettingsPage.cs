// The puNES page of the pack's Nixx window. Nixx-Menus owns the window, this plugin builds what is inside its own page -
// the contract is a name (src\Menus\Settings.cs):
//
//     public static class LbIntegrations.<assembly name>.Settings
//         string Title { get; }  /  Control CreatePage()  /  string Save(Control page)
//         string[] Emulators()  /  string OpenEmulator(string path)      (optional: the window's Open buttons)
//
// KEPT SIMPLE (Mehdi, 06/10 - memory "nixx-page-simple-per-game-rich"): the install and its version, the screen every game
// starts on, the Famicom Disk System's BIOS. puNES's general settings stay its own, set in its menus; a game's window
// (right-click, "Nixx-puNES : Options...") chooses that game's.

using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using LbIntegrations.Lbip;

namespace LbIntegrations.Punes
{
    /// <summary>What Nixx-Menus calls, by name. Public, its signatures fixed.</summary>
    public static class Settings
    {
        public static string Title => "puNES";

        /// <summary>Its emulators LaunchBox has, "&lt;title&gt;\t&lt;path&gt;", for the Nixx window's Open buttons.</summary>
        public static string[] Emulators() => LbipOpenEmulator.Find(PunesLaunch.IsPunesExe);
        public static string OpenEmulator(string path) => LbipOpenEmulator.Open(path);

        public static Control CreatePage() => new PunesSettingsPage();

        public static string Save(Control page)
        {
            if (!(page is PunesSettingsPage ours)) return "this is not the puNES page";
            try { ours.Save(); return null; }
            catch (Exception ex) { return "the settings could not be written: " + ex.Message; }
        }

        /// <summary>The first puNES of the library that is on disk, or null.</summary>
        internal static string FirstExe() => Emulators().Select(e => PunesPlugin.ResolveFullPathForUi(e.Split('\t').Last())).FirstOrDefault(File.Exists);
    }

    internal sealed class PunesSettingsPage : UserControl
    {
        private readonly Dictionary<string, ComboBox> _choices = new Dictionary<string, ComboBox>();

        public PunesSettingsPage()
        {
            AutoScroll = true;
            Padding = new Padding(12);
            var stack = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, Dock = DockStyle.Top };

            GroupBox Card(string title, params Control[] rows)
            {
                var box = new GroupBox { Text = title, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(10, 6, 10, 8), Margin = new Padding(0, 0, 0, 10), MinimumSize = new Size(620, 0) };
                var flow = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, Dock = DockStyle.Fill };
                flow.Controls.AddRange(rows);
                box.Controls.Add(flow);
                return box;
            }
            Label Line(string text) => new Label { Text = text, AutoSize = true, MaximumSize = new Size(600, 0), UseMnemonic = false, Margin = new Padding(0, 2, 0, 4) };

            // ── the install ──
            var exe = Settings.FirstExe();
            var rows = new List<Control>();
            if (exe == null) rows.Add(Line("puNES is not set up in LaunchBox yet: add the emulator Nixx-puNES, it installs puNES " + PunesBuild.Version + "."));
            else
            {
                var version = PunesBuild.InstalledVersion(exe);
                rows.Add(Line("puNES " + (version ?? "(a build this plugin did not install)") + " - " + exe));
                var latest = PunesBuild.LatestTag;
                if (latest != null && !string.Equals(latest, PunesBuild.Tag, StringComparison.OrdinalIgnoreCase))
                    rows.Add(Line("puNES " + latest + " is out: this plugin installs " + PunesBuild.Tag + ", a newer one comes with an update of the plugin."));
                rows.Add(Line("Keys (set in puNES's input.cfg at each launch): F2 saves the state, F4 loads it, F6 / F7 change the slot, F11 full screen, Esc quits, P pause, Space fast forward (L held), F8 screenshot, F9 mute, H reset (Shift+Esc hard), F10 / Shift+F10 PAL / NTSC."));
                rows.Add(Line(PunesSaveDirs.NoJunctions(exe)
                    ? "Saves, states and screenshots stay in puNES's own folders (" + PunesCfg.DataDir(exe) + "): links to Saves\\<platform> cannot be made there."
                    : "Saves, states and screenshots: " + Path.Combine(PunesSaveDirs.InstallDir(exe), PunesSaveDirs.Root) + "\\<platform>\\ (prb, save, diff, screenshot), set at each launch."));
            }
            rows.Add(LbipHint.Note("Systems: NES and Famicom cartridges, Famicom Disk System disks. No RetroAchievements in puNES.",
                "Platforms: Nintendo Entertainment System, Nintendo Famicom Disk System, and LiteBox's Nes - Super Mario Bros. Hacks and Nintendo Famicom. "
                + "Vs. System dumps (NES 2.0 .nes files) run as NES games. puNES picks the system from the file itself.", 600, new Padding(0, 2, 0, 2)));
            stack.Controls.Add(Card("puNES", rows.ToArray()));

            // ── the screen ──
            var screen = new List<Control>();
            foreach (var o in PunesSettings.All.Where(x => x.OnPage))
            {
                var row = new FlowLayoutPanel { FlowDirection = FlowDirection.LeftToRight, WrapContents = false, AutoSize = true, Margin = new Padding(0, 2, 0, 2) };
                row.Controls.Add(new Label { Text = o.Label, Width = 150, Height = 24, TextAlign = ContentAlignment.MiddleLeft, UseMnemonic = false });
                var c = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 280 };
                c.Items.AddRange(o.Texts.Select(t => (object)t).ToArray());
                c.SelectedIndex = Math.Max(0, Array.IndexOf(o.Values, PunesSettings.Value(o.Key)));
                LbipHint.Attach(c, o.Hint);
                _choices[o.Key] = c;
                row.Controls.Add(c);
                screen.Add(row);
            }
            screen.Add(LbipHint.Note("Given to puNES at each launch, not kept by it; a game's window can choose otherwise for that game.",
                "On puNES's command line (--fullscreen=yes / =no), before the game: puNES's own setting comes back when it quits. A game's window "
                + "(right-click, Nixx-puNES : Options...) also has puNES's region, picture and emulation options for that game.", 600, new Padding(0, 4, 0, 2)));
            stack.Controls.Add(Card("Screen - every game", screen.ToArray()));

            // ── the BIOS ──
            var fds = PunesSystems.System(PunesSystems.Fds);
            var biosRows = new List<Control>();
            var status = Line("");
            void ShowBios(IEnumerable<(PunesBios Bios, string Path)> found)
                => status.Text = string.Join("\n", found.Select(f => f.Bios.Name + ": " + (f.Path ?? "not found - the disks will not start")));
            if (exe == null) status.Text = "disksys.rom: puNES is not installed yet.";
            else ShowBios(fds.Bios.Select(b => (b, PunesFirmware.Found(exe, b))));
            biosRows.Add(status);
            if (exe != null)
            {
                var look = new Button { Text = "Look for it now", AutoSize = true, Margin = new Padding(0, 2, 0, 4) };
                look.Click += (_, _) =>
                {
                    try { Cursor = Cursors.WaitCursor; ShowBios(PunesFirmware.Ensure(exe, new[] { fds })); }
                    catch (Exception ex) { Log.Warn("the BIOS search", ex); }
                    finally { Cursor = Cursors.Default; }
                };
                LbipHint.Attach(look, "Copies disksys.rom into " + PunesFirmware.FolderOf(exe) + " from RetroArch's system folder and the bios / system / firmware "
                                      + "folders of your other emulators, when it is there. Done at the install and before each disk game too.");
                biosRows.Add(look);
            }
            biosRows.Add(LbipHint.Note("The Famicom Disk System needs disksys.rom (8 KB); the NES needs no BIOS.",
                "puNES reads it from its bios folder (or the path set in its own settings, or beside the game). Without it a disk shows an empty screen, "
                + "so the plugin refuses to start a disk game and says where to put it.", 600, new Padding(0, 4, 0, 2)));
            stack.Controls.Add(Card("Famicom Disk System BIOS", biosRows.ToArray()));

            Controls.Add(stack);
        }

        public void Save()
        {
            foreach (var o in PunesSettings.All.Where(x => x.OnPage))
            {
                var c = _choices[o.Key];
                var value = o.Values[Math.Max(0, c.SelectedIndex)];
                if (PunesSettings.Value(o.Key) != value) PunesSettings.SetValue(o.Key, value);
            }
        }
    }
}
