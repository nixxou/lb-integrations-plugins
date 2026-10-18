// The FCEUX page of the pack's Nixx window. Nixx-Menus owns the window, this plugin builds what is inside its own page -
// the contract is a name (src\Menus\Settings.cs):
//
//     public static class LbIntegrations.<assembly name>.Settings
//         string Title { get; }  /  Control CreatePage()  /  string Save(Control page)
//         string[] Emulators()  /  string OpenEmulator(string path)      (optional: the window's Open buttons)
//
// KEPT SIMPLE (Mehdi, 06/10): the install as it is, the Famicom Disk System's BIOS, the screen. Everything else - the pads,
// the hotkeys, the sound, the palette - stays FCEUX's own, set in FCEUX.

using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using LbIntegrations.Lbip;

namespace LbIntegrations.Fceux
{
    /// <summary>What Nixx-Menus calls, by name. Public, its signatures fixed.</summary>
    public static class Settings
    {
        public static string Title => "FCEUX";

        /// <summary>Its emulators LaunchBox has, "&lt;title&gt;\t&lt;path&gt;", for the Nixx window's Open buttons.</summary>
        public static string[] Emulators() => LbipOpenEmulator.Find(FceuxLaunch.IsFceuxExe);
        public static string OpenEmulator(string path) => LbipOpenEmulator.Open(path);

        public static Control CreatePage() => new FceuxSettingsPage();

        public static string Save(Control page)
        {
            if (!(page is FceuxSettingsPage ours)) return "this is not the FCEUX page";
            try { ours.Save(); return null; }
            catch (Exception ex) { return "the settings could not be written: " + ex.Message; }
        }

        /// <summary>The first FCEUX of the library that is on disk, or null.</summary>
        internal static string FirstExe() => Emulators().Select(e => e.Split('\t').Last()).FirstOrDefault(File.Exists);
    }

    internal sealed class FceuxSettingsPage : UserControl
    {
        private readonly Dictionary<string, ComboBox> _choices = new Dictionary<string, ComboBox>();
        private Label _biosLine;

        public FceuxSettingsPage()
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
            if (exe == null) rows.Add(Line("FCEUX is not set up in LaunchBox yet: add the emulator Nixx-FCEUX, it installs FCEUX's latest release."));
            else
            {
                var ours = FceuxBuild.InstalledVersion(exe);
                var said = FceuxBuild.DescribedVersion(exe);
                rows.Add(Line("FCEUX " + (ours ?? (said != null ? said + " (a build this plugin did not install)" : "(a build this plugin did not install)")) + " - " + exe));
                rows.Add(Line("Updates: the Download button of LaunchBox's emulator window offers a newer release when there is one."));
                rows.Add(Line("Keys in a game: F2 saves a state in the current slot, F4 loads it, F6 / F7 change the slot, Esc quits, Alt+Enter "
                              + "switches the full screen - bound by the plugin where FCEUX had nothing else on them. No RetroAchievements in FCEUX."));
                rows.Add(Line("Saves, states, screenshots and cheats: " + Path.Combine(FceuxSavePaths.InstallDir(exe), FceuxSavePaths.Root) + "\\<platform>."));
            }
            stack.Controls.Add(Card("FCEUX", rows.ToArray()));

            // ── the disk system's BIOS ──
            if (exe != null)
            {
                _biosLine = Line("");
                var find = new Button { Text = "Look for it", AutoSize = true, Margin = new Padding(0, 2, 0, 2) };
                find.Click += (s, e) =>
                {
                    try { Cursor = Cursors.WaitCursor; ShowBios(exe, FceuxFirmware.Ensure(exe)); }
                    catch (Exception ex) { Log.Warn("BIOS: the page's search", ex); }
                    finally { Cursor = Cursors.Default; }
                };
                LbipHint.Attach(find, "Copies disksys.rom (or fds_bios.bin) from RetroArch's system folder and the bios, system and firmware folders of the "
                                      + "other emulators beside this one - a copy, the original stays. Also done at each disk game's launch.");
                ShowBios(exe, FceuxFirmware.State(exe));
                stack.Controls.Add(Card("Famicom Disk System BIOS", _biosLine, find,
                    LbipHint.Note("Only the disk games (.fds) need it; the cartridges run without.", null, 600, new Padding(0, 4, 0, 2))));
            }

            // ── the screen ──
            var picture = new List<Control>();
            foreach (var o in FceuxSettings.All)
            {
                var row = new FlowLayoutPanel { FlowDirection = FlowDirection.LeftToRight, WrapContents = false, AutoSize = true, Margin = new Padding(0, 2, 0, 2) };
                row.Controls.Add(new Label { Text = o.Label, Width = 150, Height = 24, TextAlign = ContentAlignment.MiddleLeft, UseMnemonic = false });
                var c = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 280 };
                c.Items.AddRange(o.Texts.Select(t => (object)t).ToArray());
                c.SelectedIndex = Math.Max(0, Array.IndexOf(o.Values, FceuxSettings.Value(o.Key)));
                LbipHint.Attach(c, o.Hint);
                _choices[o.Key] = c;
                row.Controls.Add(c);
                picture.Add(row);
            }
            picture.Add(LbipHint.Note("Written into fceux.cfg at each launch.",
                "FCEUX writes its fceux.cfg back when it quits, so this setting is the plugin's: changed in FCEUX's own menus, it comes back to "
                + "this value at the next launch. Everything else - the pads, the hotkeys, the sound, the palette - stays as you set it in FCEUX.",
                600, new Padding(0, 4, 0, 2)));
            stack.Controls.Add(Card("Screen - every game", picture.ToArray()));

            Controls.Add(stack);
        }

        private void ShowBios(string exe, FceuxBiosState s)
        {
            if (_biosLine == null) return;
            _biosLine.Text = s.RightSize ? "disksys.rom is there - " + s.Path + (s.KnownDump ? "" : " (not the usual dump; FCEUX takes it as it is)")
                           : s.Present ? s.Path + " is there but is not 8 192 bytes: FCEUX will refuse it."
                           : "Missing: put disksys.rom in " + s.Folder + ", or press \"Look for it\".";
        }

        public void Save()
        {
            foreach (var o in FceuxSettings.All)
            {
                var c = _choices[o.Key];
                var value = o.Values[Math.Max(0, c.SelectedIndex)];
                if (FceuxSettings.Value(o.Key) != value) FceuxSettings.SetValue(o.Key, value);
            }
        }
    }
}
