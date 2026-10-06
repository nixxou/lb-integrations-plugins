// The RASnes9x page of the pack's Nixx window. Nixx-Menus owns the window, this plugin builds what is inside its own
// page - the contract is a name (src\Menus\Settings.cs):
//
//     public static class LbIntegrations.<assembly name>.Settings
//         string Title { get; }  /  Control CreatePage()  /  string Save(Control page)
//         string[] Emulators()  /  string OpenEmulator(string path)      (optional: the window's Open buttons)
//
// WHAT IT HOLDS: the install as it is (the build, RetroAchievements' toolset, the login the toolset has) and the picture
// every game gets - RaSnes9xSettings.All, written into snes9x.conf at each launch (RaSnes9xLaunch).

using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using LbIntegrations.Lbip;

namespace LbIntegrations.RaSnes9x
{
    /// <summary>What Nixx-Menus calls, by name. Public, its signatures fixed.</summary>
    public static class Settings
    {
        public static string Title => "RASnes9x";

        /// <summary>Its emulators LaunchBox has, "&lt;title&gt;\t&lt;path&gt;", for the Nixx window's Open buttons.</summary>
        public static string[] Emulators() => LbipOpenEmulator.Find(RaSnes9xLaunch.IsRaSnes9xExe);
        public static string OpenEmulator(string path) => LbipOpenEmulator.Open(path);

        public static Control CreatePage() => new RaSnes9xSettingsPage();

        public static string Save(Control page)
        {
            if (!(page is RaSnes9xSettingsPage ours)) return "this is not the RASnes9x page";
            try { ours.Save(); return null; }
            catch (Exception ex) { return "the settings could not be written: " + ex.Message; }
        }

        /// <summary>The first RASnes9x of the library that is on disk, or null.</summary>
        internal static string FirstExe() => Emulators().Select(e => e.Split('\t').Last()).FirstOrDefault(File.Exists);
    }

    internal sealed class RaSnes9xSettingsPage : UserControl
    {
        private readonly Dictionary<string, ComboBox> _choices = new Dictionary<string, ComboBox>();

        public RaSnes9xSettingsPage()
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
            if (exe == null) rows.Add(Line("RASnes9x is not set up in LaunchBox yet: add the emulator Nixx-RASnes9x, it installs itself."));
            else
            {
                rows.Add(Line("RASnes9x " + (RaSnes9xBuild.InstalledVersion(exe) ?? "(a build this plugin does not know)") + " - " + exe));
                rows.Add(Line(RaSnes9xCheevos.HasToolset(exe)
                    ? "RetroAchievements' toolset " + (RaSnes9xToolset.Installed(exe) ?? "") + " is installed - brought to the latest before each game."
                    : "RetroAchievements' toolset is not installed yet: it is downloaded before the next game, from retroachievements.org."));
                var (user, hasToken, hardcore) = RaSnes9xCheevos.Read(exe);
                // LaunchBox hands its login to a plugin only when a game starts: before the first one, RASnes9x has none yet.
                rows.Add(Line(user == null ? "RetroAchievements: no login in RASnes9x yet. LaunchBox's (Tools > Options > RetroAchievements) is written in when a game starts."
                                           : "RetroAchievements: " + user + (hasToken ? "" : " (no token - log in again)") + ", hardcore " + (hardcore ? "on" : "off")
                                             + " - LaunchBox's login and its hardcore switch on the emulator, written in at each launch."));
            }
            stack.Controls.Add(Card("RASnes9x", rows.ToArray()));

            // ── the picture ──
            var picture = new List<Control>();
            foreach (var o in RaSnes9xSettings.All)
            {
                var row = new FlowLayoutPanel { FlowDirection = FlowDirection.LeftToRight, WrapContents = false, AutoSize = true, Margin = new Padding(0, 2, 0, 2) };
                row.Controls.Add(new Label { Text = o.Label, Width = 150, Height = 24, TextAlign = ContentAlignment.MiddleLeft, UseMnemonic = false });
                var c = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 280 };
                c.Items.AddRange(o.Texts.Select(t => (object)t).ToArray());
                c.SelectedIndex = Math.Max(0, Array.IndexOf(o.Values, RaSnes9xSettings.Value(o.Key)));
                LbipHint.Attach(c, o.Hint);
                _choices[o.Key] = c;
                row.Controls.Add(c);
                picture.Add(row);
            }
            picture.Add(LbipHint.Note("Written into snes9x.conf at each launch; a game's window can change them for that game.",
                "Snes9x writes its snes9x.conf back when it quits, so these keys are the plugin's: changed in Snes9x's own menus, they come back to "
                + "these values at the next launch. Everything else - the pads, the hotkeys, the sound, the folders - stays as you set it in Snes9x.",
                600, new Padding(0, 4, 0, 2)));
            stack.Controls.Add(Card("Picture - every game", picture.ToArray()));

            Controls.Add(stack);
        }

        public void Save()
        {
            foreach (var o in RaSnes9xSettings.All)
            {
                var c = _choices[o.Key];
                var value = o.Values[Math.Max(0, c.SelectedIndex)];
                if (RaSnes9xSettings.Value(o.Key) != value) RaSnes9xSettings.SetValue(o.Key, value);
            }
        }
    }
}
