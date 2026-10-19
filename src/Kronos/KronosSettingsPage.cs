// The Kronos page of the pack's Nixx window. Nixx-Menus owns the window, this plugin builds what is inside its own page -
// the contract is a name (src\Menus\Settings.cs):
//
//     public static class LbIntegrations.<assembly name>.Settings
//         string Title { get; }  /  Control CreatePage()  /  string Save(Control page)
//         string[] Emulators()  /  string OpenEmulator(string path)      (optional: the window's Open buttons)
//
// WHAT IT HOLDS, kept simple (memory "nixx-page-simple-per-game-rich"): the install (its version, where its settings are), the
// Saturn BIOS found in its bios folder with a button that looks again, and what every game gets: the screen. Every other
// setting is Kronos's own (its Settings window).

using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using LbIntegrations.Lbip;

namespace LbIntegrations.Kronos
{
    /// <summary>What Nixx-Menus calls, by name. Public, its signatures fixed.</summary>
    public static class Settings
    {
        public static string Title => "Kronos";

        public static string[] Emulators() => LbipOpenEmulator.Find(KronosLaunch.IsKronosExe);
        public static string OpenEmulator(string path) => LbipOpenEmulator.Open(path);

        public static Control CreatePage() => new KronosSettingsPage();

        public static string Save(Control page)
        {
            if (!(page is KronosSettingsPage ours)) return "this is not the Kronos page";
            try { ours.Save(); return null; }
            catch (Exception ex) { return "the settings could not be written: " + ex.Message; }
        }

        internal static string FirstExe() => Emulators().Select(e => e.Split('\t').Last()).FirstOrDefault(File.Exists);
    }

    internal sealed class KronosSettingsPage : UserControl
    {
        private readonly Dictionary<string, ComboBox> _choices = new Dictionary<string, ComboBox>();
        private readonly FlowLayoutPanel _bios;
        private readonly Label _compat;
        private readonly string _exe;

        public KronosSettingsPage()
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
                stack.Controls.Add(Card("Kronos", Line("Kronos is not set up in LaunchBox yet: add the emulator Nixx-Kronos, it installs Tradu-France's latest 64-bit build."),
                                        Line(KronosBuild.RuntimeNote)));
            else
            {
                var ours = KronosBuild.InstalledVersion(_exe);
                stack.Controls.Add(Card("Kronos",
                    Line("Kronos " + (ours ?? "") + (ours == null ? "(an install this plugin did not make: updates are yours)" : " (" + (KronosBuild.InstalledFrom(_exe) ?? "Tradu-France 64-bit") + ")") + " - " + _exe),
                    Line("Its settings: " + KronosLaunch.SettingsOf(_exe) + "; its saves and states: " + Path.Combine(KronosLaunch.ProfileOf(_exe), "Saves")),
                    Line(KronosBuild.RuntimeNote),
                    LbIntegrations.Lbip.LbipHint.Note("Keys: F2 save state, F4 load, F6 / F7 the slot, F11 full screen, PageDown / PageUp the disc, Esc quit.",
                        "Kronos has no \"current slot\": the plugin binds each slot's save to Ctrl+Shift+F1..F10 and its load to Shift+F1..F10 in kronos.ini "
                        + "(Kronos's own Ctrl / Alt + digit do nothing), and the AutoHotkey script of the emulator entry, in Kronos's window only, sends them for "
                        + "the slot it counts. F11 sends Kronos's Alt+Enter, PageDown / PageUp open Kronos's Eject/Load ISO with the game's next disc (its .m3u, "
                        + "or the files named \"(Disc 2)\"... beside it), Esc closes Kronos. Each game keeps its own backup RAM and states, in Saves\\<platform>\\ "
                        + "- Kronos's settings are put back after the game.", 600, new Padding(0, 2, 0, 4))));
            }

            // ── the BIOS ──
            _bios = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, Margin = new Padding(0) };
            var again = new Button { Text = "Look for the BIOS again", AutoSize = true, Enabled = _exe != null, Margin = new Padding(0, 6, 0, 2) };
            LbipHint.Attach(again, "Looks at the 512 KB files of RetroArch's system folder and of the bios / system / firmware folders of the other emulators, "
                                 + "recognises the Saturn BIOS among them by content, and copies each region missing here into Kronos's bios folder - a copy, never a move.");
            again.Click += (_, _) =>
            {
                try { Cursor = Cursors.WaitCursor; ShowBios(KronosBios.Ensure(_exe)); }
                catch (Exception ex) { MessageBox.Show(this, "The BIOS could not be looked for: " + ex.Message, "Nixx-Kronos", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
                finally { Cursor = Cursors.Default; }
            };
            if (_exe != null) ShowBios(KronosBios.InFolder(_exe));
            stack.Controls.Add(Card("BIOS (Saturn, ST-V)", LbipHint.Note("Saturn: in Kronos's bios folder, known by their content - the name does not matter.",
                "mpr-17933.bin (US / Europe 1.01a) runs American and European discs, sega_101.bin (Japan 1.01) Japanese ones; the plugin gives Kronos the one of "
                + "the disc's region at each launch. A game is not started without a BIOS: the plugin says where to put one.", 600, new Padding(0, 2, 0, 4)), _bios, again));

            // ── every game ──
            var rows = new List<Control>();
            foreach (var o in KronosSettings.All)
            {
                var row = new FlowLayoutPanel { FlowDirection = FlowDirection.LeftToRight, WrapContents = false, AutoSize = true, Margin = new Padding(0, 2, 0, 2) };
                row.Controls.Add(new Label { Text = o.Label, Width = 170, Height = 24, TextAlign = ContentAlignment.MiddleLeft, UseMnemonic = false });
                var c = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 280 };
                c.Items.AddRange(o.Texts.Select(t => (object)t).ToArray());
                c.SelectedIndex = Math.Max(0, Array.IndexOf(o.Values, KronosSettings.Value(o.Key)));
                LbipHint.Attach(c, o.Hint);
                _choices[o.Key] = c;
                row.Controls.Add(c);
                rows.Add(row);
            }
            rows.Add(LbipHint.Note("Every other setting is Kronos's own: its Settings (Ctrl+S in Kronos).",
                "Picture, sound, pads: Kronos keeps them in its kronos.ini and the plugin leaves them as you set them.", 600, new Padding(0, 4, 0, 2)));
            stack.Controls.Add(Card("Every game", rows.ToArray()));

            Controls.Add(stack);
        }

        private void ShowBios(List<KronosBiosFile> files)
        {
            _bios.Controls.Clear();
            // The ST-V BIOS: the plugin's stvbios.zip, made at the first ST-V launch when absent.
            var stv = KronosStv.BiosZipOf(_exe);
            var n = KronosStv.BiosFilesIn(stv);
            _bios.Controls.Add(new Label
            {
                Text = n > 0 ? "ST-V: " + stv + " (" + n + " BIOS file" + (n == 1 ? "" : "s") + ")"
                             : "ST-V: no stvbios.zip yet - put MAME's in " + KronosLaunch.BiosDirOf(_exe) + ", or the first ST-V game makes it (the stvbios.zip beside it, or the BIOS files of a MAME non-merged set).",
                AutoSize = true, MaximumSize = new Size(600, 0), UseMnemonic = false, Margin = new Padding(0, 1, 0, 4),
            });
            if (files.Count == 0)
            {
                _bios.Controls.Add(new Label { Text = "None in " + KronosLaunch.BiosDirOf(_exe) + ": no Saturn game starts.", AutoSize = true, ForeColor = Color.FromArgb(0xC0, 0x60, 0x30), UseMnemonic = false });
                return;
            }
            foreach (var f in files)
                _bios.Controls.Add(new Label
                {
                    Text = Path.GetFileName(f.Path) + " - " + (f.Known?.Label ?? "not a Saturn BIOS the plugin knows (a modified image?)"),
                    AutoSize = true, MaximumSize = new Size(600, 0), UseMnemonic = false, Margin = new Padding(0, 1, 0, 1),
                    ForeColor = f.Known != null ? ForeColor : Color.FromArgb(0xC0, 0x60, 0x30),
                });
            bool us = KronosBios.HasFor(files, "us-eu"), jp = KronosBios.HasFor(files, "jp");
            if (!us || !jp)
                _bios.Controls.Add(new Label
                {
                    Text = "No " + (!us ? "US / European" : "Japanese") + " BIOS: " + (!us ? "American and European" : "Japanese") + " discs may not start.",
                    AutoSize = true, MaximumSize = new Size(600, 0), UseMnemonic = false, ForeColor = Color.FromArgb(0xC0, 0x60, 0x30), Margin = new Padding(0, 3, 0, 1),
                });
        }

        public void Save()
        {
            foreach (var o in KronosSettings.All)
            {
                var value = o.Values[Math.Max(0, _choices[o.Key].SelectedIndex)];
                if (KronosSettings.Value(o.Key) != value) KronosSettings.SetValue(o.Key, value);
            }
        }
    }
}
