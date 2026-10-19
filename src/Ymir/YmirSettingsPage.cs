// The Ymir page of the pack's Nixx window. Nixx-Menus owns the window, this plugin builds what is inside its own page -
// the contract is a name (src\Menus\Settings.cs):
//
//     public static class LbIntegrations.<assembly name>.Settings
//         string Title { get; }  /  Control CreatePage()  /  string Save(Control page)
//         string[] Emulators()  /  string OpenEmulator(string path)      (optional: the window's Open buttons)
//
// WHAT IT HOLDS, kept simple (memory "nixx-page-simple-per-game-rich"): the install (its version, the build for this
// processor), the Saturn BIOS found in roms\ipl with a button that looks again, Ymir's compatibility list (when it was
// downloaded, a button to download it now), and what every game gets: the screen, and when the list's state is shown at
// launch. Every other setting is Ymir's own (F10 in Ymir).

using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using LbIntegrations.Lbip;

namespace LbIntegrations.Ymir
{
    /// <summary>What Nixx-Menus calls, by name. Public, its signatures fixed.</summary>
    public static class Settings
    {
        public static string Title => "Ymir";

        public static string[] Emulators() => LbipOpenEmulator.Find(YmirLaunch.IsYmirExe);
        public static string OpenEmulator(string path) => LbipOpenEmulator.Open(path);

        public static Control CreatePage() => new YmirSettingsPage();

        public static string Save(Control page)
        {
            if (!(page is YmirSettingsPage ours)) return "this is not the Ymir page";
            try { ours.Save(); return null; }
            catch (Exception ex) { return "the settings could not be written: " + ex.Message; }
        }

        internal static string FirstExe() => Emulators().Select(e => e.Split('\t').Last()).FirstOrDefault(File.Exists);
    }

    internal sealed class YmirSettingsPage : UserControl
    {
        private readonly Dictionary<string, ComboBox> _choices = new Dictionary<string, ComboBox>();
        private readonly FlowLayoutPanel _bios;
        private readonly Label _compat;
        private readonly string _exe;

        public YmirSettingsPage()
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
            _exe = Settings.FirstExe();
            if (_exe == null)
                stack.Controls.Add(Card("Ymir", Line("Ymir is not set up in LaunchBox yet: add the emulator Nixx-Ymir, it installs Ymir's latest stable release."),
                                        Line(YmirBuild.RuntimeNote)));
            else
            {
                var ours = YmirBuild.InstalledVersion(_exe);
                stack.Controls.Add(Card("Ymir",
                    Line("Ymir " + (ours ?? "") + (ours == null ? "(an install this plugin did not make: updates are yours)" : " (" + YmirBuild.Flavour() + " build)") + " - " + _exe),
                    Line("Its settings, BIOS, saves and states: " + YmirLaunch.ProfileOf(_exe) + " (portable, beside the exe)"),
                    Line(YmirBuild.RuntimeNote),
                    LbipHint.Note("Keys: F2 save state, F4 load, F6 / F7 the slot, F11 full screen, PageDown / PageUp the disc, Esc quit.",
                        "F2, F4 and F11 are bound in Ymir.toml where Ymir still had its own default (F3 loads in Ymir, F11 is its debug trace). F6 / F7 select "
                        + "the slot through Ymir's keys 1 to 0, PageDown / PageUp open Ymir's Load Disc with the game's next disc (its .m3u, or the files "
                        + "named \"(Disc 2)\"... beside it), Esc closes Ymir - the AutoHotkey script of the emulator entry, in Ymir's window only. "
                        + "Each game keeps its own backup RAM, in Saves\\<platform>\\ - Ymir's setting is put back after the game.", 600, new Padding(0, 2, 0, 4))));
            }

            // ── the BIOS ──
            _bios = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, Margin = new Padding(0) };
            var again = new Button { Text = "Look for the BIOS again", AutoSize = true, Enabled = _exe != null, Margin = new Padding(0, 6, 0, 2) };
            LbipHint.Attach(again, "Looks at the 512 KB files of RetroArch's system folder and of the bios / system / firmware folders of the other emulators, "
                                 + "recognises the Saturn BIOS among them by content as Ymir does, and copies each region missing here into roms\\ipl - a copy, never a move.");
            again.Click += (_, _) =>
            {
                try { Cursor = Cursors.WaitCursor; ShowBios(YmirBios.Ensure(_exe)); }
                catch (Exception ex) { MessageBox.Show(this, "The BIOS could not be looked for: " + ex.Message, "Nixx-Ymir", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
                finally { Cursor = Cursors.Default; }
            };
            if (_exe != null) ShowBios(YmirBios.InFolder(_exe));
            stack.Controls.Add(Card("Saturn BIOS", LbipHint.Note("Ymir reads them in roms\\ipl, by their content - the name does not matter.",
                "mpr-17933.bin (US / Europe 1.01a) runs American and European discs, sega_101.bin (Japan 1.01) Japanese ones; Ymir picks the one of the disc's "
                + "region. A game is not started without a BIOS: the plugin says where to put one.", 600, new Padding(0, 2, 0, 4)), _bios, again));

            // ── the compatibility list ──
            _compat = Line(CompatText());
            var download = new Button { Text = "Download the list now", AutoSize = true, Margin = new Padding(0, 6, 0, 2) };
            LbipHint.Attach(download, "Ymir's own compatibility list (a Google sheet, one tab per region): each game's state by its disc's hash. Downloaded at install, "
                                    + "then again when it is a week old; a download that fails keeps the copy there.");
            download.Click += async (_, _) =>
            {
                download.Enabled = false;
                _compat.Text = "Downloading...";
                var said = await Task.Run(() => YmirCompat.Download());
                _compat.Text = CompatText() + (said.Contains("kept the copy") ? "\n" + said : "");
                download.Enabled = true;
            };
            stack.Controls.Add(Card("Compatibility list", _compat, download));

            // ── every game ──
            var rows = new List<Control>();
            foreach (var o in YmirSettings.All)
            {
                var row = new FlowLayoutPanel { FlowDirection = FlowDirection.LeftToRight, WrapContents = false, AutoSize = true, Margin = new Padding(0, 2, 0, 2) };
                row.Controls.Add(new Label { Text = o.Label, Width = 170, Height = 24, TextAlign = ContentAlignment.MiddleLeft, UseMnemonic = false });
                var c = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 280 };
                c.Items.AddRange(o.Texts.Select(t => (object)t).ToArray());
                c.SelectedIndex = Math.Max(0, Array.IndexOf(o.Values, YmirSettings.Value(o.Key)));
                LbipHint.Attach(c, o.Hint);
                _choices[o.Key] = c;
                row.Controls.Add(c);
                rows.Add(row);
            }
            rows.Add(LbipHint.Note("Every other setting is Ymir's own: F10 in Ymir.",
                "Picture, sound, pads, region: Ymir keeps them in its Ymir.toml and the plugin leaves them as you set them.", 600, new Padding(0, 4, 0, 2)));
            stack.Controls.Add(Card("Every game", rows.ToArray()));

            Controls.Add(stack);
        }

        private static string CompatText()
        {
            var when = YmirCompat.Downloaded();
            int count = YmirCompat.Count();
            return count == 0 ? "Not downloaded yet." : count + " games, downloaded " + (when?.ToString("g") ?? "partly") + " - each game's state in its Options window and as it starts.";
        }

        private void ShowBios(List<YmirBiosFile> files)
        {
            _bios.Controls.Clear();
            if (files.Count == 0)
            {
                _bios.Controls.Add(new Label { Text = "None in " + YmirLaunch.IplDirOf(_exe) + ": no game starts.", AutoSize = true, ForeColor = Color.FromArgb(0xC0, 0x60, 0x30), UseMnemonic = false });
                return;
            }
            foreach (var f in files)
                _bios.Controls.Add(new Label
                {
                    Text = Path.GetFileName(f.Path) + " - " + (f.Known?.Label ?? "not one Ymir knows (a modified image?)"),
                    AutoSize = true, MaximumSize = new Size(600, 0), UseMnemonic = false, Margin = new Padding(0, 1, 0, 1),
                    ForeColor = f.Known != null ? ForeColor : Color.FromArgb(0xC0, 0x60, 0x30),
                });
            bool us = YmirBios.HasFor(files, "us-eu"), jp = YmirBios.HasFor(files, "jp");
            if (!us || !jp)
                _bios.Controls.Add(new Label
                {
                    Text = "No " + (!us ? "US / European" : "Japanese") + " BIOS: " + (!us ? "American and European" : "Japanese") + " discs may not start.",
                    AutoSize = true, MaximumSize = new Size(600, 0), UseMnemonic = false, ForeColor = Color.FromArgb(0xC0, 0x60, 0x30), Margin = new Padding(0, 3, 0, 1),
                });
        }

        public void Save()
        {
            foreach (var o in YmirSettings.All)
            {
                var value = o.Values[Math.Max(0, _choices[o.Key].SelectedIndex)];
                if (YmirSettings.Value(o.Key) != value) YmirSettings.SetValue(o.Key, value);
            }
        }
    }
}
