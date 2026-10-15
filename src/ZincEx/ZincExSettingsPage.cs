// The ZiNc EX page of the pack's Nixx window. Nixx-Menus owns the window (its Tools menu entry), this plugin builds what
// is inside its own page - the contract is a name (src\Menus\Settings.cs):
//
//     public static class LbIntegrations.<assembly name>.Settings
//         string Title { get; }  /  Control CreatePage()  /  string Save(Control page)
//         string[] Emulators()  /  string OpenEmulator(string path)      (optional: the window's Open buttons)
//
// WHAT IT HOLDS: the install as it is; where the games' parent and BIOS zips are looked for, and which BIOS sets are found
// there; how the game's window opens. Everything else - renderer, resolution, sound, controls, bezels - is ZiNc EX's own,
// set in its window (the Open button) for every game or per game, and taken as it is at each launch (Mehdi, 06/10: the
// Nixx page simple, the emulator's settings the emulator's).

using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using LbIntegrations.Lbip;

namespace LbIntegrations.ZincEx
{
    /// <summary>What Nixx-Menus calls, by name. Public, its signatures fixed.</summary>
    public static class Settings
    {
        public static string Title => "ZiNc EX";

        /// <summary>Its emulators LaunchBox has, "&lt;title&gt;\t&lt;path&gt;", for the Nixx window's Open buttons.</summary>
        public static string[] Emulators() => LbipOpenEmulator.Find(ZincExLaunch.IsZincEx);
        public static string OpenEmulator(string path) => LbipOpenEmulator.Open(path);

        public static Control CreatePage() => new ZincExSettingsPage();

        public static string Save(Control page)
        {
            if (!(page is ZincExSettingsPage ours)) return "this is not the ZiNc EX page";
            try { ours.Save(); return null; }
            catch (Exception ex) { return "the settings could not be written: " + ex.Message; }
        }
    }

    internal sealed class ZincExSettingsPage : UserControl
    {
        private readonly ComboBox _screen;

        public ZincExSettingsPage()
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
            var exe = Settings.Emulators().Select(e => ZincExPlugin.ResolveFullPathForUi(e.Split('\t').Last())).FirstOrDefault(File.Exists);
            var version = exe == null ? null : ZincExBuild.InstalledVersion(exe);
            var rows = new System.Collections.Generic.List<Control>
            {
                Line(exe == null ? "ZiNc EX is not set up in LaunchBox yet: add the emulator Nixx-ZiNc-EX, it installs itself."
                                 : "ZiNc EX " + (version ?? "(version unknown)") + " - " + exe),
            };
            if (exe != null)
            {
                var dir = Path.GetDirectoryName(exe);
                if (!File.Exists(Path.Combine(dir, ZincExLaunch.ZincName))) rows.Add(Line("ZiNc.exe is NOT beside it: no game can start. Update the emulator from LaunchBox."));
                var own = Path.Combine(dir, ZincExConfig.DefaultFolder);
                var user = ZincExConfig.UserFolder(dir);
                rows.Add(Line("A game's parent and BIOS zips are looked for beside it, then in " + own
                              + (user != null && !ZincExConfig.SameFolder(user, own) ? ", then in ZiNc EX's ROMs folder " + user : "") + "."));
                rows.Add(Line("BIOS sets found there: " + BiosFound(new[] { own, user })));
            }
            stack.Controls.Add(Card("ZiNc EX", rows.ToArray()));

            // ── the window ──
            _screen = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 320 };
            _screen.Items.AddRange(new object[] { "Full screen (default)", "Windowed", "As set in ZiNc EX" });
            _screen.SelectedIndex = ZincExSettings.Screen == "windowed" ? 1 : ZincExSettings.Screen == "zincex" ? 2 : 0;
            LbipHint.Attach(_screen, "ZiNc EX's --fullscreen or --windowed, for the launch only - nothing is written into ZiNc EX's settings. "
                                   + "\"As set in ZiNc EX\": neither, its Video > Full Screen and the game's own Game Settings decide.");
            var screenRow = new FlowLayoutPanel { FlowDirection = FlowDirection.LeftToRight, WrapContents = false, AutoSize = true, Margin = new Padding(0, 2, 0, 2) };
            screenRow.Controls.Add(new Label { Text = "Screen", Width = 170, Height = 24, TextAlign = ContentAlignment.MiddleLeft, UseMnemonic = false });
            screenRow.Controls.Add(_screen);
            stack.Controls.Add(Card("Every game", screenRow,
                LbipHint.Note("Renderer, resolution, sound, controls and bezels are ZiNc EX's: set them in its own window.", "Open ZiNc EX from here (Open), "
                              + "for every game, or per game in its Game > Game Settings - the launch from LaunchBox takes them as they are.", 600, new Padding(0, 4, 0, 2))));

            Controls.Add(stack);
        }

        /// <summary>Each BIOS set ZiNc knows, found under its name or MAME's in one of <paramref name="folders"/>.</summary>
        private static string BiosFound(string[] folders)
        {
            var found = ZincGames.BiosAliases.Keys.OrderBy(b => b).Select(b =>
            {
                foreach (var name in new[] { b }.Concat(ZincGames.BiosAliases[b]))
                    foreach (var f in folders.Where(f => !string.IsNullOrEmpty(f)))
                        if (File.Exists(Path.Combine(f, name + ".zip"))) return name == b ? b : b + " (" + name + ")";
                return null;
            }).Where(s => s != null).ToList();
            return found.Count == 0 ? "none - a MAME non-merged set carries its own, or put them beside the games" : string.Join(", ", found);
        }

        public void Save() => ZincExSettings.SetScreen(_screen.SelectedIndex == 1 ? "windowed" : _screen.SelectedIndex == 2 ? "zincex" : "fullscreen");
    }
}
