// The 3dSen page of the pack's Nixx window. Nixx-Menus owns the window, this plugin builds what is inside its own page -
// the contract is a name (src\Menus\Settings.cs):
//
//     public static class LbIntegrations.<assembly name>.Settings
//         string Title { get; }  /  Control CreatePage()  /  string Save(Control page)
//         string[] Emulators()  /  string OpenEmulator(string path)      (optional: the window's Open buttons)
//
// WHAT IT HOLDS (simple, Mehdi 06/10): the install - Steam's or the user's copy, its version - what Steam has, where 3dSen
// keeps its files, the keys, and the screen every game gets (ThreeDSenSettings.All, passed at each launch). 3dSen's own
// settings (speed, shadows, skybox, pads) stay in 3dSen's menu.

using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using LbIntegrations.Lbip;

namespace LbIntegrations.ThreeDSen
{
    /// <summary>What Nixx-Menus calls, by name. Public, its signatures fixed.</summary>
    public static class Settings
    {
        public static string Title => "3dSen";

        public static string[] Emulators() => LbipOpenEmulator.Find(ThreeDSenLaunch.IsExe);
        public static string OpenEmulator(string path) => LbipOpenEmulator.Open(path);

        public static Control CreatePage() => new ThreeDSenSettingsPage();

        public static string Save(Control page)
        {
            if (!(page is ThreeDSenSettingsPage ours)) return "this is not the 3dSen page";
            try { ours.Save(); return null; }
            catch (Exception ex) { return "the settings could not be written: " + ex.Message; }
        }

        /// <summary>The first 3dSen of the library that is on disk, or null.</summary>
        internal static string FirstExe() => Emulators().Select(e => e.Split('\t').Last()).FirstOrDefault(File.Exists);
    }

    internal sealed class ThreeDSenSettingsPage : UserControl
    {
        private readonly Dictionary<string, ComboBox> _choices = new Dictionary<string, ComboBox>();

        public ThreeDSenSettingsPage()
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
            var rows = new List<Control>();
            var installs = Settings.Emulators().Select(e => e.Split('\t').Last()).Where(File.Exists).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (installs.Count == 0)
                rows.Add(Line("3dSen is not set up in LaunchBox yet: add the emulator Nixx-3DSen and click Download - it asks whether to use "
                              + "your Steam install or the folder of your copy (nothing is downloaded: 3dSen is sold on Steam)."));
            foreach (var exe in installs)
                rows.Add(Line("3dSen " + (ThreeDSenInstall.VersionOf(exe) ?? "(version unreadable)") + (ThreeDSenSteam.IsSteamPath(exe) ? ", Steam's - " : ", your copy - ") + exe));
            try { foreach (var l in ThreeDSenInstall.Describe(ThreeDSenSteam.Read())) rows.Add(Line(l)); } catch { }
            rows.Add(LbipHint.Note("Updates: Steam updates its install; a copy of yours is as you keep it.",
                "The plugin never installs nor updates 3dSen. To point LaunchBox at another 3dSen, click Download again in its emulator window.", 600, new Padding(0, 4, 0, 2)));
            stack.Controls.Add(Card("3dSen", rows.ToArray()));

            // ── its files and keys ──
            var data = ThreeDSenLaunch.DataDir();
            var save = ThreeDSenInput.KeyName(data, "QuickSave");
            var load = ThreeDSenInput.KeyName(data, "QuickLoad");
            stack.Controls.Add(Card("Its files and keys",
                Line("3dSen keeps its list of games, its keys and its save states in " + data + " - one folder for every copy of 3dSen of this Windows user."),
                Line("Quick save: " + (save ?? "(3dSen not run yet)") + ", quick load: " + (load ?? "(3dSen not run yet)")
                     + " - F2 and F4 are bound by the plugin when 3dSen has none. Esc opens 3dSen's menu (its 36 save slots, its settings)."),
                Line("No cartridge saves: 3dSen keeps a game's progress only in its save states (a game that saves on the cartridge forgets it when 3dSen quits).")));

            // ── the screen ──
            var screen = new List<Control>();
            var remembered = ThreeDSenLaunch.Remembered();
            foreach (var o in ThreeDSenSettings.All)
            {
                var row = new FlowLayoutPanel { FlowDirection = FlowDirection.LeftToRight, WrapContents = false, AutoSize = true, Margin = new Padding(0, 2, 0, 2) };
                row.Controls.Add(new Label { Text = o.Label, Width = 150, Height = 24, TextAlign = ContentAlignment.MiddleLeft, UseMnemonic = false });
                var c = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 300 };
                c.Items.AddRange(o.Values.Select(v => (object)(o.Key == "Screen" && v == "asis" && remembered != null ? o.TextOf(v) + " (" + remembered + ")" : o.TextOf(v))).ToArray());
                c.SelectedIndex = Math.Max(0, Array.IndexOf(o.Values, ThreeDSenSettings.Value(o.Key)));
                LbipHint.Attach(c, o.Hint);
                _choices[o.Key] = c;
                row.Controls.Add(c);
                screen.Add(row);
            }
            screen.Add(LbipHint.Note("Passed on 3dSen's command line at each launch; a game's window can change them for that game.",
                "3dSen's own settings - speed, shadows, the sky, the TV mode, the pads - stay as you set them in 3dSen's menu.", 600, new Padding(0, 4, 0, 2)));
            stack.Controls.Add(Card("Screen - every game", screen.ToArray()));

            Controls.Add(stack);
        }

        public void Save()
        {
            foreach (var o in ThreeDSenSettings.All)
            {
                var value = o.Values[Math.Max(0, _choices[o.Key].SelectedIndex)];
                if (ThreeDSenSettings.Value(o.Key) != value) ThreeDSenSettings.SetValue(o.Key, value);
            }
        }
    }
}
