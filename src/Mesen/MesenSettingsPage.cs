// The Mesen page of the pack's Nixx window. Nixx-Menus owns the window, this plugin builds what is inside its own page -
// the contract is a name (src\Menus\Settings.cs):
//
//     public static class LbIntegrations.<assembly name>.Settings
//         string Title { get; }  /  Control CreatePage()  /  string Save(Control page)
//         string[] Emulators()  /  string OpenEmulator(string path)      (optional: the window's Open buttons)
//
// WHAT IT HOLDS, kept simple (Mehdi 06/10): the install (MesenCE or SourMesen, its version, its home folder), what Windows
// needs (nothing: a native build), the BIOS of every system that needs one - found, missing, a size Mesen does not take - with
// a button that looks again, and the screen every game gets. Every other setting is Mesen's own, set in its menus.

using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using LbIntegrations.Lbip;

namespace LbIntegrations.Mesen
{
    /// <summary>What Nixx-Menus calls, by name. Public, its signatures fixed.</summary>
    public static class Settings
    {
        public static string Title => "Mesen";

        public static string[] Emulators() => LbipOpenEmulator.Find(MesenLaunch.IsMesenExe);
        public static string OpenEmulator(string path) => LbipOpenEmulator.Open(path);

        public static Control CreatePage() => new MesenSettingsPage();

        public static string Save(Control page)
        {
            if (!(page is MesenSettingsPage ours)) return "this is not the Mesen page";
            try { ours.Save(); return null; }
            catch (Exception ex) { return "the settings could not be written: " + ex.Message; }
        }

        internal static string FirstExe() => Emulators().Select(e => e.Split('\t').Last()).FirstOrDefault(File.Exists);
    }

    internal sealed class MesenSettingsPage : UserControl
    {
        private readonly Dictionary<string, ComboBox> _choices = new Dictionary<string, ComboBox>();
        private readonly FlowLayoutPanel _bios;
        private readonly string _exe;

        public MesenSettingsPage()
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
                stack.Controls.Add(Card("Mesen", Line("Mesen is not set up in LaunchBox yet: add the emulator Nixx-Mesen, it installs MesenCE's latest release."),
                                        Line(MesenBuild.RuntimeNote)));
            else
            {
                var v = MesenLaunch.VersionOf(_exe);
                var ours = MesenBuild.InstalledVersion(_exe);
                var which = (MesenLaunch.IsCe(_exe) ? "MesenCE " : "Mesen ") + (v == null ? "" : v.ToString(3));
                stack.Controls.Add(Card("Mesen",
                    Line(which + (ours == null ? " (an install this plugin did not make: updates are yours)" : "") + " - " + _exe),
                    Line("Its settings, BIOS and saves: " + MesenLaunch.HomeOf(_exe) + (MesenLaunch.IsPortable(_exe) ? " (portable, beside the exe)" : "")),
                    Line(MesenBuild.RuntimeNote),
                    LbipHint.Note("Each platform runs as its own console; the game's saves and states in Saves\\<platform>.",
                        "Mesen picks the console from the file (.nes, .fds, .sfc, .gb, .gba, .pce, .cue, .sms, .gg, .col, .ws...). Some platform rows carry the model "
                        + "on their line in LaunchBox (Edit Emulator > Associated Platforms) - Game Boy: --gameboy.model=AutoFavorGb, WonderSwan: --ws.model=Monochrome... "
                        + "Those switches are put back in Mesen's settings after the game, as are the folders. Keys bound by the plugin where Mesen had none: F2 save "
                        + "state, F4 load, F6 / F7 the slot, Esc quit; F11 is Mesen's full screen.", 600, new Padding(0, 2, 0, 4))));
            }

            // ── the BIOS ──
            _bios = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, Margin = new Padding(0) };
            var again = new Button { Text = "Look for the BIOS files again", AutoSize = true, Enabled = _exe != null, Margin = new Padding(0, 6, 0, 2) };
            LbipHint.Attach(again, "Looks in Mesen's Firmware folder (by name, then by content), then copies what is missing from RetroArch's system folder and the "
                                 + "bios / system / firmware folders of the other emulators - a copy, never a move - under the names Mesen reads.");
            again.Click += (_, _) =>
            {
                try { Cursor = Cursors.WaitCursor; ShowBios(MesenBios.Ensure(_exe, MesenSystems.All)); }
                catch (Exception ex) { MessageBox.Show(this, "The BIOS files could not be looked for: " + ex.Message, "Nixx-Mesen", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
                finally { Cursor = Cursors.Default; }
            };
            if (_exe != null) ShowBios(MesenBios.Ensure(_exe, MesenSystems.All, readOnly: true));
            stack.Controls.Add(Card("BIOS", LbipHint.Note("Mesen reads them in its Firmware folder, by name and size.",
                "disksys.rom (Famicom Disk System), gba_bios.bin (Game Boy Advance), syscard3.pce (PC Engine CD), bios.col (ColecoVision), BS-X.bin (Satellaview), "
                + "SufamiTurbo.sfc, SGB2.sfc (Super Game Boy)... A game whose console needs one that is missing is not launched: the plugin says which file to put there.",
                600, new Padding(0, 2, 0, 4)), _bios, again));

            // ── every game ──
            var rows = new List<Control>();
            foreach (var o in MesenSettings.All.Where(x => x.OnPage))
            {
                var row = new FlowLayoutPanel { FlowDirection = FlowDirection.LeftToRight, WrapContents = false, AutoSize = true, Margin = new Padding(0, 2, 0, 2) };
                row.Controls.Add(new Label { Text = o.Label, Width = 150, Height = 24, TextAlign = ContentAlignment.MiddleLeft, UseMnemonic = false });
                var c = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 280 };
                c.Items.AddRange(o.Texts.Select(t => (object)t).ToArray());
                c.SelectedIndex = Math.Max(0, Array.IndexOf(o.Values, MesenSettings.Value(o.Key)));
                LbipHint.Attach(c, o.Hint);
                _choices[o.Key] = c;
                row.Controls.Add(c);
                rows.Add(row);
            }
            rows.Add(LbipHint.Note("Every other setting is Mesen's own: set it in Mesen's menus.",
                "Picture, sound, pads, each console's options: Mesen keeps them in its settings.json and the plugin leaves them as you set them.", 600, new Padding(0, 4, 0, 2)));
            stack.Controls.Add(Card("Every game", rows.ToArray()));

            Controls.Add(stack);
        }

        private void ShowBios(List<MesenBiosState> states)
        {
            _bios.Controls.Clear();
            foreach (var group in states.GroupBy(s => s.System).Where(g => g.Any(s => !s.Spec.Optional)))
            {
                var parts = group.Where(s => !s.Spec.Optional).Select(s => s.Spec.File + ": "
                    + (s.Path == null ? (s.Required ? "missing" : "-") : !s.SizeOk ? "not a size Mesen reads" : "found" + (s.HashOk ? "" : " (another dump)")));
                bool ok = MesenBios.Enough(group);
                _bios.Controls.Add(new Label
                {
                    Text = group.Key.Name + " - " + string.Join(";  ", parts), AutoSize = true, MaximumSize = new Size(600, 0), UseMnemonic = false, Margin = new Padding(0, 1, 0, 1),
                    ForeColor = ok ? ForeColor : Color.FromArgb(0xC0, 0x60, 0x30),
                });
            }
        }

        public void Save()
        {
            foreach (var o in MesenSettings.All.Where(x => x.OnPage))
            {
                var value = o.Values[Math.Max(0, _choices[o.Key].SelectedIndex)];
                if (MesenSettings.Value(o.Key) != value) MesenSettings.SetValue(o.Key, value);
            }
        }
    }
}
