// The no$gba tab of the pack's configuration window (Mehdi, 03/10) - found by its name, like every plugin's:
//     public static class LbIntegrations.<assembly name>.Settings
//         string Title { get; }  /  Control CreatePage()  /  string Save(Control page)
// WHAT IT HOLDS: the name no$gba's DS games show (its DSi cartridges too, which it runs as DS games) - your console, or the
// owner of your firmware dump - see NoGbaFirmware. Its DSiWare show each DSi console's own owner: no$gba has no override.

using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using LbIntegrations.Identity;

namespace LbIntegrations.NoGba
{
    /// <summary>What Nixx-Menus calls, by name. Public, its signatures fixed.</summary>
    public static class Settings
    {
        public static string Title => "no$gba";

        /// <summary>Its emulators LaunchBox has, "<title>\t<path>", for the Nixx window's Open buttons - opened as LaunchBox's "Open
        /// emulator" menu opens them, with what this plugin does around it (Shared.Lbip\LbipOpenEmulator).</summary>
        public static string[] Emulators() => LbIntegrations.Lbip.LbipOpenEmulator.Find(p => NoGbaPaths.IsNoGbaExecutable(p));
        public static string OpenEmulator(string path) => LbIntegrations.Lbip.LbipOpenEmulator.Open(path);

        public static Control CreatePage() => new NoGbaSettingsPage();

        public static string Save(Control page)
        {
            if (!(page is NoGbaSettingsPage ours)) return "this is not the no$gba page";
            try { return ours.Save(); }
            catch (Exception ex) { return "no$gba's settings could not be written: " + ex.Message; }
        }
    }

    internal sealed class NoGbaSettingsPage : UserControl
    {
        private readonly NoGbaLayout _layout;
        private readonly RadioButton _mine, _theirs;
        private readonly bool _mineShown;

        internal static NoGbaLayout Layout()
        {
            var root = PackIdentity.LaunchBoxRoot();
            var exe = root == null ? null : NoGbaPaths.FindExecutable(Path.Combine(root, "Emulators", "Nixx-nogba"));
            return exe == null ? null : NoGbaPaths.Resolve(exe);
        }

        public NoGbaSettingsPage()
        {
            AutoScroll = true;
            Padding = new Padding(12);
            _layout = Layout();
            Label Grey(string text, int x, int y) => new Label { AutoSize = true, MaximumSize = new Size(540 - x, 0), Location = new Point(x, y), ForeColor = SystemColors.GrayText, Text = text };
            if (_layout == null)
            {
                Controls.Add(Grey("Nixx-nogba is not installed: there is nothing to configure yet. Add it from LaunchBox's Tools > Manage > "
                                  + "Emulators, and its settings appear here.", 12, 12));
                return;
            }

            var box = new GroupBox { Text = "The name the games show", Location = new Point(12, 12), Size = new Size(560, 10) };
            int y = 24;
            var active = NoGbaFirmware.Active(_layout);
            if (active != null)
            {
                var a = active.Value;
                box.Controls.Add(new Label { Text = "DS games (and DSi cartridges, run as DS games):", AutoSize = true, Location = new Point(14, y) });
                y += 22;
                _mineShown = a.Identity;
                var mine = PackIdentity.Load() ?? PackIdentity.FromWindows();
                _mine = new RadioButton { Text = "your console: " + LbIntegrations.Dsi.DsOwner.Of(mine).Describe(), AutoSize = true, Location = new Point(30, y), Checked = a.Identity };
                _theirs = new RadioButton { Text = "the one of your dump " + a.Dump + ": " + a.DumpOwner.Describe(), AutoSize = true, Location = new Point(30, y + 22), Checked = !a.Identity };
                box.Controls.Add(_mine);
                box.Controls.Add(_theirs);
                box.Controls.Add(Grey("Written into FIRMWARE.BIN, no$gba's copy of the dump - your dump itself is never written. Your console is "
                                      + "set in the \"Your console\" tab.", 48, y + 46));
                y += 84;
            }
            else
            {
                box.Controls.Add(Grey("DS games: no$gba has no copy of a DS firmware dump of yours (dsfirmware.bin in no$gba's bios folder), "
                                      + "so they show whatever FIRMWARE.BIN holds, or none.", 14, y));
                y += 44;
            }
            box.Controls.Add(new Label { Text = "DSiWare:", AutoSize = true, Location = new Point(14, y) });
            box.Controls.Add(Grey("the owner of each DSi console - no$gba cannot show another over it. A console made from a blank NAND is "
                                  + "set up as your console when it is made.", 30, y + 20));
            y += 60;
            box.Size = new Size(560, y);
            Controls.Add(box);
        }

        /// <summary>Null when saved (or nothing changed), otherwise why not.</summary>
        public string Save()
        {
            if (_layout == null || _mine == null || _mine.Checked == _mineShown) return null;
            return NoGbaFirmware.SetOwner(_layout, _mine.Checked);
        }
    }
}