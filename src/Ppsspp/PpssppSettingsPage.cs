// The PPSSPP tab of the pack's configuration window (Mehdi, 03/10) - found by its name, like every plugin's:
//     public static class LbIntegrations.<assembly name>.Settings
//         string Title { get; }  /  Control CreatePage()  /  string Save(Control page)
// WHAT IT HOLDS: the import of PSP games into LaunchBox (PpssppLbImport) - each step on by default.

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace LbIntegrations.Ppsspp
{
    /// <summary>What Nixx-Menus calls, by name. Public, its signatures fixed.</summary>
    public static class Settings
    {
        public static string Title => "PPSSPP";

        public static Control CreatePage() => new PpssppSettingsPage();

        public static string Save(Control page)
        {
            if (!(page is PpssppSettingsPage ours)) return "this is not the PPSSPP page";
            try { return ours.Save(); }
            catch (Exception ex) { return "PPSSPP's settings could not be written: " + ex.Message; }
        }
    }

    internal sealed class PpssppSettingsPage : UserControl
    {
        private readonly CheckBox _clean, _title, _region;

        public PpssppSettingsPage()
        {
            AutoScroll = true;
            Padding = new Padding(12);
            var s = PpssppSettings.Read();
            var box = new GroupBox { Text = "LaunchBox's Import ROM Files wizard (Sony PSP)", Location = new Point(12, 12), Size = new Size(560, 250) };
            int y = 24;
            CheckBox Box(string text, bool on, string help)
            {
                var c = new CheckBox { Text = text, AutoSize = true, Location = new Point(14, y), Checked = on };
                box.Controls.Add(c);
                box.Controls.Add(new Label { Text = help, AutoSize = true, MaximumSize = new Size(510, 0), Location = new Point(32, y + 22), ForeColor = SystemColors.GrayText });
                y += 70;
                return c;
            }
            _clean = Box("Filter out what is not a game", PpssppSettings.On(s, "import_clean", true),
                "Each file's PARAM.SFO is read, zipped or not, before you click Finish: a game stays; a game update is noted for its "
                + "game (its options window, tab Updates - never installed unasked); a firmware update and anything else goes. A .elf "
                + "or .prx cannot be read: it stays.");
            _title = Box("Rename games when their name is not in LaunchBox's database", PpssppSettings.On(s, "import_title", true),
                "The file's name is kept when LaunchBox's database knows it on Sony PSP; else the PARAM.SFO's title, written as the "
                + "database writes it - else the file's name, unless it is a bare serial. Shown in the list before you click Finish.");
            _region = Box("Set each game's region after the import", PpssppSettings.On(s, "import_region", true),
                "The region its serial says: ULUS / NPUH North America, ULES / NPEH Europe, ULJM / NPJH Japan, Asia, Korea.");
            Controls.Add(box);
        }

        public string Save()
        {
            var s = PpssppSettings.Read();
            s["import_clean"] = _clean.Checked ? "on" : "off";
            s["import_title"] = _title.Checked ? "on" : "off";
            s["import_region"] = _region.Checked ? "on" : "off";
            PpssppSettings.Write(s);
            return null;
        }
    }
}