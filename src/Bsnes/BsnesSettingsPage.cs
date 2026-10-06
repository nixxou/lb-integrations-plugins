// The bsnes page of the pack's Nixx window. Nixx-Menus owns the window, this plugin builds what is inside its own
// page - the contract is a name (src\Menus\Settings.cs):
//
//     public static class LbIntegrations.<assembly name>.Settings
//         string Title { get; }  /  Control CreatePage()  /  string Save(Control page)
//         string[] Emulators()  /  string OpenEmulator(string path)      (optional: the window's Open buttons)
//
// WHAT IT HOLDS: the install as it is (the nightly, its date) and the picture every game gets - BsnesSettings.All,
// written into settings.bml at each launch (BsnesLaunch).

using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using LbIntegrations.Lbip;

namespace LbIntegrations.Bsnes
{
    /// <summary>What Nixx-Menus calls, by name. Public, its signatures fixed.</summary>
    public static class Settings
    {
        public static string Title => "bsnes";

        /// <summary>Its emulators LaunchBox has, "&lt;title&gt;\t&lt;path&gt;", for the Nixx window's Open buttons.</summary>
        public static string[] Emulators() => LbipOpenEmulator.Find(BsnesLaunch.IsBsnesExe);
        public static string OpenEmulator(string path) => LbipOpenEmulator.Open(path);

        public static Control CreatePage() => new BsnesSettingsPage();

        public static string Save(Control page)
        {
            if (!(page is BsnesSettingsPage ours)) return "this is not the bsnes page";
            try { ours.Save(); return null; }
            catch (Exception ex) { return "the settings could not be written: " + ex.Message; }
        }

        /// <summary>The first bsnes of the library that is on disk, or null.</summary>
        internal static string FirstExe() => Emulators().Select(e => e.Split('\t').Last()).FirstOrDefault(File.Exists);
    }

    internal sealed class BsnesSettingsPage : UserControl
    {
        private readonly Dictionary<string, ComboBox> _choices = new Dictionary<string, ComboBox>();

        public BsnesSettingsPage()
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
            if (exe == null) rows.Add(Line("bsnes is not set up in LaunchBox yet: add the emulator Nixx-bsnes, it installs its nightly build."));
            else
            {
                rows.Add(Line("bsnes, nightly of " + (BsnesBuild.InstalledVersion(exe) ?? "(a build this plugin did not install)") + " - " + exe));
                rows.Add(Line("Updates: the Download button of LaunchBox's emulator window offers a newer nightly when there is one."));
                rows.Add(Line("Save states: F5 saves, F7 loads the quick slot - bound by the plugin when bsnes had none. No RetroAchievements in bsnes."));
            }
            stack.Controls.Add(Card("bsnes", rows.ToArray()));            // ── the picture ──
            var picture = new List<Control>();
            foreach (var o in BsnesSettings.All)
            {
                var row = new FlowLayoutPanel { FlowDirection = FlowDirection.LeftToRight, WrapContents = false, AutoSize = true, Margin = new Padding(0, 2, 0, 2) };
                row.Controls.Add(new Label { Text = o.Label, Width = 150, Height = 24, TextAlign = ContentAlignment.MiddleLeft, UseMnemonic = false });
                var c = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 280 };
                c.Items.AddRange(o.Texts.Select(t => (object)t).ToArray());
                c.SelectedIndex = Math.Max(0, Array.IndexOf(o.Values, BsnesSettings.Value(o.Key)));
                LbipHint.Attach(c, o.Hint);
                _choices[o.Key] = c;
                row.Controls.Add(c);
                picture.Add(row);
            }
            picture.Add(LbipHint.Note("Written into settings.bml at each launch; a game's window can change them for that game.",
                "bsnes writes its settings.bml back when it quits, so these settings are the plugin's: changed in bsnes's own menus, they come back to "
                + "these values at the next launch. Everything else - the pads, the hotkeys, the sound, the folders - stays as you set it in bsnes.",
                600, new Padding(0, 4, 0, 2)));
            stack.Controls.Add(Card("Picture - every game", picture.ToArray()));

            Controls.Add(stack);
        }

        public void Save()
        {
            foreach (var o in BsnesSettings.All)
            {
                var c = _choices[o.Key];
                var value = o.Values[Math.Max(0, c.SelectedIndex)];
                if (BsnesSettings.Value(o.Key) != value) BsnesSettings.SetValue(o.Key, value);
            }
        }
    }
}
