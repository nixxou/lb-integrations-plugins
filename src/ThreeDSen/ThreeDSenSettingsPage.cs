// The 3dSen page of the pack's Nixx window. Nixx-Menus owns the window, this plugin builds what is inside its own page -
// the contract is a name (src\Menus\Settings.cs):
//
//     public static class LbIntegrations.<assembly name>.Settings
//         string Title { get; }  /  Control CreatePage()  /  string Save(Control page)
//         string[] Emulators()  /  string OpenEmulator(string path)      (optional: the window's Open buttons)
//
// WHAT IT HOLDS (simple, Mehdi 06/10): the install - Steam's or the user's copy, its version - what Steam has, where 3dSen
// keeps its files, the keys, the screen every game gets (ThreeDSenSettings.All, passed at each launch), and the pack's
// in-process plugin (ThreeDSenSettings.Plugin, ThreeDSenBepInEx): its state in each 3dSen folder, Install / Remove acting at
// once (like SUPER ZSNES's), and what it does. 3dSen's own settings (speed, shadows, skybox, pads) stay in 3dSen's menu - a
// game's window can give one game its own, through the plugin.

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

            // ── the in-process plugin ──
            var plugin = new List<Control>();
            foreach (var exe in installs)
            {
                var dir = Path.GetDirectoryName(exe);
                var status = new Label { Text = dir + " - " + ThreeDSenBepInEx.Describe(dir), AutoSize = true, MaximumSize = new Size(600, 0), UseMnemonic = false, Margin = new Padding(0, 4, 0, 2) };
                var install = new Button { Text = ThreeDSenBepInEx.IsDeployed(dir) ? "Repair / refresh" : "Install now", AutoSize = true, Margin = new Padding(0, 2, 6, 6) };
                var remove = new Button { Text = "Remove", AutoSize = true, Margin = new Padding(0, 2, 0, 6), Enabled = ThreeDSenBepInEx.HasBepInEx(dir) };
                void Run(Func<string> work, string doing)
                {
                    if (ThreeDSenLaunch.AnyRunning()) { status.Text = dir + " - close 3dSen first."; return; }
                    install.Enabled = remove.Enabled = false;
                    status.Text = dir + " - " + doing;
                    new System.Threading.Thread(() =>
                    {
                        string failed;
                        try { failed = work(); } catch (Exception ex) { failed = ex.Message; }
                        SafeSet(status, dir + " - " + (failed == null ? ThreeDSenBepInEx.Describe(dir) : "failed: " + failed));
                        try
                        {
                            install.BeginInvoke(new Action(() =>
                            {
                                install.Enabled = true; install.Text = ThreeDSenBepInEx.IsDeployed(dir) ? "Repair / refresh" : "Install now";
                                remove.Enabled = ThreeDSenBepInEx.HasBepInEx(dir);
                            }));
                        }
                        catch { }
                    }) { IsBackground = true }.Start();
                }
                install.Click += (_, _) => Run(() => ThreeDSenBepInEx.Deploy(dir, m => SafeSet(status, dir + " - " + m)), "working...");
                remove.Click += (_, _) => Run(() => ThreeDSenBepInEx.Remove(dir), "removing...");
                plugin.Add(status);
                var buttons = new FlowLayoutPanel { FlowDirection = FlowDirection.LeftToRight, WrapContents = false, AutoSize = true, Margin = new Padding(0) };
                buttons.Controls.Add(install); buttons.Controls.Add(remove);
                plugin.Add(buttons);
            }
            foreach (var o in ThreeDSenSettings.Plugin) plugin.Add(ChoiceRow(o, null));
            plugin.Add(LbipHint.Note("BepInEx " + ThreeDSenBepInEx.Build + " (downloaded from its GitHub release, checked by its SHA-256) and the pack's plugin, in 3dSen's folder.",
                "With it: Esc quits (asked twice), F6 / F7 pick a save slot, a game's own 3dSen settings, and 3dSen's files in its folder: "
                + "portable\\ for its games and keys, Saves\\<platform>\\States for the states (what LocalLow had is copied the first time, never moved). "
                + "Remove takes BepInEx's files away; the portable folder stays.", 600, new Padding(0, 4, 0, 2)));
            stack.Controls.Add(Card("In-process plugin", plugin.ToArray()));

            // ── its files and keys ──
            var first = installs.FirstOrDefault();
            var portable = first != null && ThreeDSenLaunch.Portable(first);
            var data = first == null ? ThreeDSenLaunch.DataDir() : ThreeDSenLaunch.DataDir(first);
            var save = ThreeDSenInput.KeyName(data, "QuickSave");
            var load = ThreeDSenInput.KeyName(data, "QuickLoad");
            stack.Controls.Add(Card("Its files and keys",
                Line(portable
                    ? "3dSen keeps its list of games and its keys in " + data + ", its save states in " + Path.Combine(Path.GetDirectoryName(first), ThreeDSenLaunch.SavesFolder) + "\\<platform>\\States."
                    : "3dSen keeps its list of games, its keys and its save states in " + data + " - one folder for every copy of 3dSen of this Windows user."),
                Line(portable && ThreeDSenSettings.On("Slots")
                    ? "F2 saves and F4 loads the slot F6 / F7 picked (Quick, 1 to 36), said on screen. Esc quits, F1 opens 3dSen's menu; its camera keys are on the keypad (4 6 8 2, + -)."
                    : "Quick save: " + (save ?? "(3dSen not run yet)") + ", quick load: " + (load ?? "(3dSen not run yet)")
                      + " - F2 and F4 are bound by the plugin when 3dSen has none. Esc quits, F1 opens 3dSen's menu (its 36 save slots, its settings)."),
                Line("No cartridge saves: 3dSen keeps a game's progress only in its save states (a game that saves on the cartridge forgets it when 3dSen quits).")));

            // ── the screen ──
            var screen = new List<Control>();
            var remembered = ThreeDSenLaunch.Remembered();
            foreach (var o in ThreeDSenSettings.All) screen.Add(ChoiceRow(o, remembered));
            screen.Add(LbipHint.Note("Passed on 3dSen's command line at each launch; a game's window can change them for that game.",
                "3dSen's own settings - speed, shadows, the sky, the TV mode, the pads - stay as you set them in 3dSen's menu.", 600, new Padding(0, 4, 0, 2)));
            stack.Controls.Add(Card("Screen - every game", screen.ToArray()));

            Controls.Add(stack);
        }

        private Control ChoiceRow(ThreeDSenOption o, string remembered)
        {
            var row = new FlowLayoutPanel { FlowDirection = FlowDirection.LeftToRight, WrapContents = false, AutoSize = true, Margin = new Padding(0, 2, 0, 2) };
            row.Controls.Add(new Label { Text = o.Label, Width = 150, Height = 24, TextAlign = ContentAlignment.MiddleLeft, UseMnemonic = false });
            var c = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 300 };
            c.Items.AddRange(o.Values.Select(v => (object)(o.Key == "Screen" && v == "asis" && remembered != null ? o.TextOf(v) + " (" + remembered + ")" : o.TextOf(v))).ToArray());
            c.SelectedIndex = Math.Max(0, Array.IndexOf(o.Values, ThreeDSenSettings.Value(o.Key)));
            LbipHint.Attach(c, o.Hint);
            _choices[o.Key] = c;
            row.Controls.Add(c);
            return row;
        }

        private static void SafeSet(Label label, string text)
        {
            try { if (label.IsHandleCreated) label.BeginInvoke(new Action(() => label.Text = text)); else label.Text = text; } catch { }
        }

        public void Save()
        {
            foreach (var o in ThreeDSenSettings.All.Concat(ThreeDSenSettings.Plugin))
            {
                var value = o.Values[Math.Max(0, _choices[o.Key].SelectedIndex)];
                if (ThreeDSenSettings.Value(o.Key) != value) ThreeDSenSettings.SetValue(o.Key, value);
            }
        }
    }
}
