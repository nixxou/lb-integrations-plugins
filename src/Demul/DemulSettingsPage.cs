// The Demul page of the pack's Nixx window. Nixx-Menus owns the window (its Tools menu entry), this plugin builds what
// is inside its own page - the contract is a name (src\Menus\Settings.cs):
//
//     public static class LbIntegrations.<assembly name>.Settings
//         string Title { get; }  /  Control CreatePage()  /  string Save(Control page)
//         string[] Emulators()  /  string OpenEmulator(string path)      (optional: the window's Open buttons)
//
// WHAT IT HOLDS (06/10): the install as it is (the build, the BIOS sets in roms\); the picture every game gets - what
// Batocera's launcher sets (batocera_launch/emulators/demul.py: full screen at the screen's resolution, aspect, vsync,
// Gaelco's scaling) and the video plugin by board; and how LaunchBox's Import ROM Files wizard is sorted (DemulLbImport).
// Demul is no longer developed, so these are written straight into its .ini at each launch (DemulLaunch.PrepareGpu).

using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using LbIntegrations.Lbip;

namespace LbIntegrations.Demul
{
    /// <summary>What Nixx-Menus calls, by name. Public, its signatures fixed.</summary>
    public static class Settings
    {
        public static string Title => "Demul";

        /// <summary>Its emulators LaunchBox has, "&lt;title&gt;\t&lt;path&gt;", for the Nixx window's Open buttons.</summary>
        public static string[] Emulators() => LbipOpenEmulator.Find(IsDemulExe);
        public static string OpenEmulator(string path) => LbipOpenEmulator.Open(path);

        internal static bool IsDemulExe(string path)
        {
            try { return string.Equals(Path.GetFileName((path ?? "").Trim().Trim('"')), "demul.exe", StringComparison.OrdinalIgnoreCase); }
            catch { return false; }
        }

        public static Control CreatePage() => new DemulSettingsPage();

        public static string Save(Control page)
        {
            if (!(page is DemulSettingsPage ours)) return "this is not the Demul page";
            try { ours.Save(); return null; }
            catch (Exception ex) { return "the settings could not be written: " + ex.Message; }
        }
    }

    internal sealed class DemulSettingsPage : UserControl
    {
        private readonly CheckBox _fullscreen, _vsync;
        private readonly ComboBox _aspect, _scaling, _video, _unknown;
        private readonly Dictionary<string, CheckBox> _sort = new Dictionary<string, CheckBox>();
        private readonly Label _biosLine;
        private readonly string _exe;

        public DemulSettingsPage()
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
            Control Row(string label, Control field, int labelWidth = 170)
            {
                var row = new FlowLayoutPanel { FlowDirection = FlowDirection.LeftToRight, WrapContents = false, AutoSize = true, Margin = new Padding(0, 2, 0, 2) };
                row.Controls.Add(new Label { Text = label, Width = labelWidth, AutoSize = false, Height = 24, TextAlign = ContentAlignment.MiddleLeft, UseMnemonic = false });
                row.Controls.Add(field);
                return row;
            }
            ComboBox List(int width, params string[] items)
            {
                var c = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = width };
                c.Items.AddRange(items);
                return c;
            }

            // ── the install ──
            _exe = Settings.Emulators().Select(e => e.Split('\t').Last()).FirstOrDefault(File.Exists);
            var version = _exe == null ? null : DemulBuild.InstalledVersion(_exe);
            var installLine = new Label
            {
                AutoSize = true, UseMnemonic = false, Margin = new Padding(0, 2, 0, 4),
                Text = _exe == null ? "Demul is not set up in LaunchBox yet: add the emulator Nixx-Demul, it installs itself."
                                    : "Demul " + (version ?? "(a build this plugin does not know)") + " - " + _exe,
            };
            _biosLine = new Label { AutoSize = true, UseMnemonic = false, Margin = new Padding(0, 0, 0, 4), Visible = _exe != null };
            ShowBios();
            var importBios = new Button { Text = "Look for the BIOS sets again", AutoSize = true, Enabled = _exe != null, Margin = new Padding(0, 2, 0, 2) };
            LbipHint.Attach(importBios, "Copies the BIOS sets roms\\ is missing (dc, naomi, naomi2, naomigd, awbios, hikaru, segasp) from Nixx-Flycast's data\\, "
                                      + "RetroArch's system folder and the folders of the library's MAME games. A copy, never a move.");
            importBios.Click += (_, _) =>
            {
                try
                {
                    Cursor = Cursors.WaitCursor;
                    var copied = DemulBios.Import(Path.GetDirectoryName(_exe));
                    ShowBios();
                    MessageBox.Show(this, copied.Count == 0 ? "No BIOS set was missing, or none was found to copy." : "Copied: " + string.Join(", ", copied.Select(Path.GetFileName)),
                                    "Nixx-Demul", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                catch (Exception ex) { MessageBox.Show(this, "The BIOS sets could not be copied: " + ex.Message, "Nixx-Demul", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
                finally { Cursor = Cursors.Default; }
            };
            stack.Controls.Add(Card("Demul", installLine, _biosLine, importBios));

            // ── the picture ──
            _fullscreen = new CheckBox { Text = "Full screen, at the screen's resolution", AutoSize = true, Checked = DemulSettings.Fullscreen };
            LbipHint.Attach(_fullscreen, "UseFullscreen = 1 in the video plugin's .ini, its Width and Height those of the main screen - as Batocera does.");
            _aspect = List(200, "Stretched", "4:3 (default)", "16:9");
            _aspect.SelectedIndex = DemulSettings.Aspect;
            LbipHint.Attach(_aspect, "The game's picture: stretched to the screen, 4:3 with bars at the sides (the default), or 16:9.");
            _vsync = new CheckBox { Text = "VSync - against tearing", AutoSize = true, Checked = DemulSettings.VSync };
            _scaling = List(200, "1x", "2x", "3x", "4x", "5x", "6x");
            _scaling.SelectedIndex = DemulSettings.GaelcoScaling - 1;
            LbipHint.Attach(_scaling, "Gaelco games only: the picture scaled from the board's own resolution (its video plugin's \"scaling\").");
            _video = List(320, "By board (recommended)", "Always the new DX11 plugin", "Always the old DX11 plugin");
            _video.SelectedIndex = DemulSettings.VideoPlugin == "new" ? 1 : DemulSettings.VideoPlugin == "old" ? 2 : 0;
            LbipHint.Attach(_video, "By board: the old DX11 plugin for Gaelco and Cave CV1000, the new one for the rest - Batocera's choice: "
                                  + "\"Gaelco won't work with the new DX11 plugin, and for cave3rd it crashes at start-up\".");
            stack.Controls.Add(Card("Picture - every game",
                _fullscreen, Row("Aspect", _aspect), _vsync, Row("Gaelco scaling", _scaling), Row("Video plugin", _video),
                LbipHint.Note("Written into Demul's video plugin settings at each launch.", "Demul is no longer developed: its keys will not move. "
                              + "What is not here stays as you set it in Demul.", 600, new Padding(0, 4, 0, 2))));

            // ── the import wizard ──
            var boards = new FlowLayoutPanel { FlowDirection = FlowDirection.LeftToRight, WrapContents = true, AutoSize = true, MaximumSize = new Size(600, 0), Margin = new Padding(0, 2, 0, 4) };
            foreach (var b in DemulSettings.Boards)
            {
                var c = new CheckBox { Text = b, AutoSize = true, Checked = DemulSettings.SortImport(b), Margin = new Padding(0, 2, 14, 2) };
                _sort[b] = c;
                boards.Controls.Add(c);
            }
            _unknown = List(360, "Ask, once per import (default)", "Keep them", "Leave them out");
            _unknown.SelectedIndex = DemulSettings.UnknownSets == "keep" ? 1 : DemulSettings.UnknownSets == "drop" ? 2 : 0;
            LbipHint.Attach(_unknown, "Sets of the right board that are not in Demul's own game list - Demul will most likely not run them; keep them "
                                    + "if another emulator of the platform does.");
            stack.Controls.Add(Card("LaunchBox's Import ROM Files wizard",
                LbipHint.Note("For a ticked board, the wizard's last list keeps only its games.", "Importing to Nixx-Demul for that board's platform (or scraped as it): "
                              + "the other boards' sets, the BIOS sets and what is no romset leave the list - handy for a whole MAME folder. By the file's name, "
                              + "as Demul finds a set. A Dreamcast import keeps the discs Demul reads (.gdi, .cdi, .chd).", 600, new Padding(0, 2, 0, 4)),
                boards, Row("Sets Demul does not know", _unknown)));

            Controls.Add(stack);
        }

        private void ShowBios()
        {
            if (_exe == null) { _biosLine.Text = ""; return; }
            var roms = Path.Combine(Path.GetDirectoryName(_exe), DemulLaunch.BiosFolder);
            var have = DemulBios.Sets.Where(s => File.Exists(Path.Combine(roms, s))).Select(s => Path.GetFileNameWithoutExtension(s)).ToList();
            var missing = DemulBios.Sets.Select(s => Path.GetFileNameWithoutExtension(s)).Except(have).ToList();
            _biosLine.Text = "BIOS sets in roms\\: " + (have.Count == 0 ? "none" : string.Join(", ", have)) + (missing.Count > 0 ? "  -  missing: " + string.Join(", ", missing) : "");
        }

        public void Save()
        {
            DemulSettings.SetFullscreen(_fullscreen.Checked);
            DemulSettings.SetAspect(Math.Max(0, _aspect.SelectedIndex));
            DemulSettings.SetVSync(_vsync.Checked);
            DemulSettings.SetGaelcoScaling(Math.Max(0, _scaling.SelectedIndex) + 1);
            DemulSettings.SetVideoPlugin(_video.SelectedIndex == 1 ? "new" : _video.SelectedIndex == 2 ? "old" : "auto");
            foreach (var kv in _sort) if (DemulSettings.SortImport(kv.Key) != kv.Value.Checked) DemulSettings.SetSortImport(kv.Key, kv.Value.Checked);
            DemulSettings.SetUnknownSets(_unknown.SelectedIndex == 1 ? "keep" : _unknown.SelectedIndex == 2 ? "drop" : "ask");
        }
    }
}
