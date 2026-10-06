// The ares page of the pack's Nixx window. Nixx-Menus owns the window, this plugin builds what is inside its own page -
// the contract is a name (src\Menus\Settings.cs):
//
//     public static class LbIntegrations.<assembly name>.Settings
//         string Title { get; }  /  Control CreatePage()  /  string Save(Control page)
//         string[] Emulators()  /  string OpenEmulator(string path)      (optional: the window's Open buttons)
//
// WHAT IT HOLDS: the install (the release), the BIOS of every system that needs one - found, missing, not the dump ares
// knows - with a button that looks again, and the screen every game gets. Kept simple (Mehdi, 06/10): every game's
// settings are ares's own, set in its menus; a game's window chooses the rest for that game (AresSettings.OnPage).

using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using LbIntegrations.Lbip;

namespace LbIntegrations.Ares
{
    /// <summary>What Nixx-Menus calls, by name. Public, its signatures fixed.</summary>
    public static class Settings
    {
        public static string Title => "ares";

        public static string[] Emulators() => LbipOpenEmulator.Find(AresLaunch.IsAresExe);
        public static string OpenEmulator(string path) => LbipOpenEmulator.Open(path);

        public static Control CreatePage() => new AresSettingsPage();

        public static string Save(Control page)
        {
            if (!(page is AresSettingsPage ours)) return "this is not the ares page";
            try { ours.Save(); return null; }
            catch (Exception ex) { return "the settings could not be written: " + ex.Message; }
        }

        internal static string FirstExe() => Emulators().Select(e => e.Split('\t').Last()).FirstOrDefault(File.Exists);
    }

    internal sealed class AresSettingsPage : UserControl
    {
        private readonly Dictionary<string, ComboBox> _choices = new Dictionary<string, ComboBox>();
        private readonly Dictionary<string, string[]> _values = new Dictionary<string, string[]>();   // what each list's items stand for
        private readonly FlowLayoutPanel _bios;
        private readonly string _exe;

        public AresSettingsPage()
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
            stack.Controls.Add(Card("ares", _exe == null
                ? new Control[] { Line("ares is not set up in LaunchBox yet: add the emulator Nixx-ares, it installs the latest release.") }
                : new Control[] {
                    Line("ares " + (AresBuild.InstalledVersion(_exe) ?? "(a build this plugin did not install)") + " - " + _exe),
                    Line("Each platform runs as the system on its line in LaunchBox (Edit Emulator > Associated Platforms: --system \"...\"); a game's window can "
                         + "pick another - a Sega CD game as Mega CD 32X. Save states: F5 saves, F7 loads, bound by the plugin when ares had none."),
                }));

            // ── the BIOS ──
            _bios = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, Margin = new Padding(0) };
            var again = new Button { Text = "Look for the BIOS files again", AutoSize = true, Enabled = _exe != null, Margin = new Padding(0, 6, 0, 2) };
            LbipHint.Attach(again, "Looks in ares's Firmware folder (by name, then by content), then copies what is missing from RetroArch's system folder and the "
                                 + "bios / system / firmware folders of the other emulators - a copy, never a move - and gives each to ares in its settings.");
            again.Click += (_, _) =>
            {
                try { Cursor = Cursors.WaitCursor; ShowBios(AresFirmware.Ensure(_exe, AresSystems.All)); }
                catch (Exception ex) { MessageBox.Show(this, "The BIOS files could not be looked for: " + ex.Message, "Nixx-ares", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
                finally { Cursor = Cursors.Default; }
            };
            if (_exe != null) ShowBios(AresFirmware.Ensure(_exe, AresSystems.All));
            stack.Controls.Add(Card("BIOS", LbipHint.Note("ares gives no name to a BIOS: each is a path in its settings, set here by the plugin.",
                "The usual names (scph5501.bin, bios_CD_U.bin, syscard3.pce, gba_bios.bin, neogeo.zip...) are looked for, and in ares's Firmware folder any file "
                + "whose content is the dump ares knows. A file found by name whose content is another dump is still given, and marked.", 600, new Padding(0, 2, 0, 4)),
                _bios, again));

            // ── ares's compatibility list (AresCompat): what the game's window shows, and its age ──
            var compatLine = Line("");
            void ShowCompat()
            {
                var (count, when, embedded) = AresCompat.Info();
                compatLine.Text = count + " games tested, " + (when == null ? "no date" : "from " + when.Value.ToLocalTime().ToString("d")) + (embedded ? " (built into the plugin)" : "")
                                  + (AresCompat.Fetching ? " - updating..." : "");
            }
            ShowCompat();
            var update = new Button { Text = "Update the list now", AutoSize = true, Margin = new Padding(0, 4, 0, 2) };
            LbipHint.Attach(update, "Reads ares-emu.net/compatibility again (every system, a page at a time - a few minutes). Done by itself once a month.");
            update.Click += (_, _) =>
            {
                update.Enabled = false;
                AresCompat.Refresh(now: true, done: said => BeginInvoke(new Action(() => { update.Enabled = true; ShowCompat(); compatLine.Text += " - " + said; })));
                ShowCompat();
            };
            AresCompat.Refresh();
            stack.Controls.Add(Card("Compatibility list", LbipHint.Note("ares's own list, shown in a game's window.", "From ares-emu.net/compatibility: "
                + "completable, in-game, starts or nothing, the ares version it was tried with and the team's notes. A game is found by its file's name "
                + "(No-Intro), else by its title.", 600, new Padding(0, 2, 0, 4)), compatLine, update));

            // ── every game's options, a card per group ──
            // Every game's settings are ares's own, set in its menus (Mehdi, 06/10): here the screen alone; a game's window
            // (right-click > Nixx-ares : Options...) sets the rest for that game.
            {
                var rows = new List<Control>();
                rows.Add(LbipHint.Note("Every other setting is ares's own: set it in ares's menus. A game's window sets its own.",
                    "Right-click a game > Nixx-ares : Options... - its picture, colour, sound, emulation and system options, given to ares on its command line "
                    + "(--setting) for that game alone; ares's own settings come back when it quits.", 600, new Padding(0, 0, 0, 6)));
                foreach (var o in AresSettings.All.Where(x => x.OnPage))
                {
                    var row = new FlowLayoutPanel { FlowDirection = FlowDirection.LeftToRight, WrapContents = false, AutoSize = true, Margin = new Padding(0, 2, 0, 2) };
                    row.Controls.Add(new Label { Text = o.Label, Width = 170, Height = 24, TextAlign = ContentAlignment.MiddleLeft, UseMnemonic = false });
                    var current = AresSettings.Value(o.Key);
                    var (values, texts) = AresSettings.Choices(o, _exe, current);
                    var c = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 300, MaxDropDownItems = 20 };
                    c.Items.AddRange(texts.Select(t => (object)t).ToArray());
                    c.SelectedIndex = Math.Max(0, Array.FindIndex(values, v => string.Equals(v, current, StringComparison.OrdinalIgnoreCase)));
                    LbipHint.Attach(c, o.Hint);
                    _choices[o.Key] = c;
                    _values[o.Key] = values;
                    row.Controls.Add(c);
                    rows.Add(row);
                }
                stack.Controls.Add(Card("Every game", rows.ToArray()));
            }

            Controls.Add(stack);
        }

        private void ShowBios(List<AresFirmwareState> states)
        {
            _bios.Controls.Clear();
            var bySystem = AresSystems.All.Where(s => s.Firmware.Length > 0).ToList();
            foreach (var s in bySystem)
            {
                var mine = states.Where(st => s.Firmware.Contains(st.Spec)).ToList();
                var parts = mine.Select(st => st.Spec.Region + (st.Spec.Type != "BIOS" ? " " + st.Spec.Type : "") + ": "
                                              + (st.Path == null ? (st.Spec.Optional ? "-" : "missing") : Path.GetFileName(st.Path) + (st.HashOk ? "" : " (another dump)")));
                bool ok = AresFirmware.Enough(mine);
                _bios.Controls.Add(new Label
                {
                    Text = s.Name + " - " + string.Join(";  ", parts), AutoSize = true, MaximumSize = new Size(600, 0), UseMnemonic = false, Margin = new Padding(0, 1, 0, 1),
                    ForeColor = ok ? ForeColor : Color.FromArgb(0xC0, 0x60, 0x30),
                });
            }
        }

        public void Save()
        {
            foreach (var o in AresSettings.All.Where(x => x.OnPage))
            {
                var value = _values[o.Key][Math.Max(0, _choices[o.Key].SelectedIndex)];
                if (AresSettings.Value(o.Key) != value) AresSettings.SetValue(o.Key, value);
            }
        }
    }
}
