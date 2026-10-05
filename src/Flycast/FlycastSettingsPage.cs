// The Flycast tab of the pack's configuration window. Nixx-Menus owns the window (its Tools menu entry
// "Nixx Integration Plugins Configuration..."), this plugin builds what is inside its own tab - the contract
// is a name, as for the game menus (src\Menus\Settings.cs):
//
//     public static class LbIntegrations.<assembly name>.Settings
//         string Title { get; }  /  Control CreatePage()  /  string Save(Control page)
//
// WHAT IT HOLDS (Mehdi, 29/09: what sets the emulator up and does not move - not its graphics): how LaunchBox's
// Import ROM Files wizard is sorted for each platform Flycast runs - see FlycastLbImport; and whether the arcade
// sets LaunchBox leaves out of such an import are put back afterwards - see FlycastImportFinished. And Flycast's own
// system settings for every game (Mehdi, 04/10) - see FlycastSystemPanel.

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using LbIntegrations.Lbip;

namespace LbIntegrations.Flycast
{
    /// <summary>What Nixx-Menus calls, by name. Public, its signatures fixed.</summary>
    public static class Settings
    {
        public static string Title => "Flycast";

        /// <summary>Its emulators LaunchBox has, "<title>\t<path>", for the Nixx window's Open buttons - opened as LaunchBox's "Open
        /// emulator" menu opens them, with what this plugin does around it (Shared.Lbip\LbipOpenEmulator).</summary>
        public static string[] Emulators() => LbIntegrations.Lbip.LbipOpenEmulator.Find(p => FlycastPaths.IsFlycastExecutable(p));
        public static string OpenEmulator(string path) => LbIntegrations.Lbip.LbipOpenEmulator.Open(path);

        public static Control CreatePage() => new FlycastSettingsPage();

        public static string Save(Control page)
        {
            if (!(page is FlycastSettingsPage ours)) return "this is not the Flycast page";
            try
            {
                foreach (var system in FlycastSettings.Systems)
                {
                    var (quick, crc) = ours.Checks(system);
                    if (FlycastSettings.QuickCheck(system) != quick) FlycastSettings.SetQuickCheck(system, quick);
                    if (FlycastSettings.CrcCheck(system) != crc) FlycastSettings.SetCrcCheck(system, crc);
                }
                if (FlycastSettings.RepairImport != ours.Repair) FlycastSettings.SetRepairImport(ours.Repair);
                var notWritten = ours.System?.Save();
                if (notWritten != null) return notWritten;
            }
            catch (Exception ex) { return "the settings could not be written: " + ex.Message; }
            return null;
        }
    }

    internal sealed class FlycastSettingsPage : UserControl
    {
        private readonly Dictionary<string, (CheckBox Quick, CheckBox Crc)> _checks = new Dictionary<string, (CheckBox, CheckBox)>();

        private readonly CheckBox _repair;

        public (bool Quick, bool Crc) Checks(string system) => (_checks[system].Quick.Checked, _checks[system].Crc.Checked);

        public bool Repair => _repair.Checked;

        public FlycastSystemPanel System { get; }

        public FlycastSettingsPage()
        {
            AutoScroll = true;
            Padding = new Padding(12);

            // Each note: one short sentence, its whole text on hover (LbipHint) - and what is below starts where it ends.
            var import = new GroupBox { Text = "LaunchBox's Import ROM Files wizard", Location = new Point(12, 12), Size = new Size(560, 330) };
            const string importFull = "Importing to Nixx-Flycast, for a platform ticked below (or scraped as one): the game list the wizard shows "
                                    + "last keeps only the games of that platform - handy when a whole MAME folder is imported. A System SP "
                                    + "platform is any whose name holds \"System SP\".";
            int y = Note(import, "For a ticked platform, the wizard's last list keeps only its games.", importFull, 14, 22, 530) + 8;
            var quickHead = new Label { Text = "Quick check", AutoSize = true, Location = new Point(200, y), Font = new Font(Font, FontStyle.Bold) };
            var crcHead = new Label { Text = "CRC check", AutoSize = true, Location = new Point(320, y), Font = new Font(Font, FontStyle.Bold) };
            import.Controls.Add(quickHead);
            import.Controls.Add(crcHead);
            const string checksFull = "Quick: by the file's name, as Flycast finds a set (a disc: by its extension) - instant.\n"
                                    + "CRC: by its content - every file of the set there, a set under another name told by its files (the list "
                                    + "says what to rename it to), each game kept loaded as Flycast loads it; a disc's IP.BIN read. Minutes on a whole MAME folder.";
            LbipHint.Attach(quickHead, checksFull, crcHead);
            y += 24;
            foreach (var system in FlycastSettings.Systems)
            {
                var name = new Label { Text = system == "Dreamcast" ? "Sega Dreamcast" : system, AutoSize = true, Location = new Point(18, y + 2) };
                import.Controls.Add(name);
                LbipHint.Attach(name, importFull);
                var quick = new CheckBox { AutoSize = true, Location = new Point(226, y), Checked = FlycastSettings.QuickCheck(system) };
                var crc = new CheckBox { AutoSize = true, Location = new Point(344, y), Checked = FlycastSettings.CrcCheck(system) };
                import.Controls.Add(quick);
                import.Controls.Add(crc);
                LbipHint.Attach(quick, checksFull, crc);
                _checks[system] = (quick, crc);
                y += 28;
            }
            y = Note(import, "Quick: by file name, instant. CRC: by content, minutes on a whole MAME folder.", checksFull, 14, y + 4, 530) + 12;

            // After the import (Mehdi, 30/09) - see FlycastImportFinished.
            _repair = new CheckBox
            {
                AutoSize = true, Location = new Point(18, y), Checked = FlycastSettings.RepairImport,
                Text = "After the import, put back the arcade sets LaunchBox left out",
            };
            import.Controls.Add(_repair);
            const string repairFull = "The problem: LaunchBox files a set as a version of a game only when their MAME titles are the same. A set "
                                    + "whose title differs but that its database takes for a game already imported (vf4b \"Virtua Fighter 4\" beside "
                                    + "vf4 \"Virtua Fighter 4 Version C\") ends up neither a version nor a game - dropped without a word.\n"
                                    + "The fix: once the games are in, each set left out is looked up in LaunchBox's own database, and added as a "
                                    + "version of the one game holding that id, through LaunchBox's API. Nothing it imported is touched.";
            LbipHint.Attach(_repair, repairFull);
            y = Note(import, "Sets LaunchBox drops without a word are added as versions of their game.", repairFull, 36, y + 24, 508);
            import.Height = y + 12;
            Controls.Add(import);
            System = new FlycastSystemPanel(560) { Location = new Point(12, import.Bottom + 10) };
            Controls.Add(System);
            Controls.Add(new Label
            {
                AutoSize = false, Location = new Point(12, System.Bottom + 8), Size = new Size(560, 20), ForeColor = SystemColors.GrayText, AutoEllipsis = true,
                Text = "Settings file: " + FlycastSettings.SettingsPath,
            });
        }

        /// <summary>A grey note at (x, y) in <paramref name="box"/>, as wide as <paramref name="width"/>; where it ends.</summary>
        private static int Note(Control box, string shortText, string full, int x, int y, int width)
        {
            var note = LbipHint.Note(shortText, full, width, Padding.Empty);
            note.Location = new Point(x, y);
            box.Controls.Add(note);
            return y + note.GetPreferredSize(new Size(width, 0)).Height;
        }
    }
}
