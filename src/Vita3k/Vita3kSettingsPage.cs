// The Vita3K tab of the pack's configuration window. Nixx-Menus owns the window (its Tools menu entry
// "Nixx Integration Plugins Configuration..."), this plugin builds what is inside its own tab.
//
// THE CONTRACT IS A NAME, as for the game menus (src\Menus\Settings.cs has it): a public static class
// LbIntegrations.<assembly name>.Settings with
//
//     public static string Title { get; }                // the tab's name
//     public static Control CreatePage();                // the tab's content, built here
//     public static string Save(Control page);           // on OK / Apply: null, or why not
//
// Control is WinForms' own, the same type on both sides - nothing else is shared.

using System;
using System.Drawing;
using System.Windows.Forms;

namespace LbIntegrations.Vita3k
{
    /// <summary>What Nixx-Menus calls, by name. Public, its signatures fixed.</summary>
    public static class Settings
    {
        public static string Title => "Vita3K";

        public static Control CreatePage() => new Vita3kSettingsPage();

        public static string Save(Control page)
        {
            if (!(page is Vita3kSettingsPage ours)) return "this is not the Vita3K page";
            try
            {
                if (Vita3kSettings.BypassVitaImport != ours.Bypass) Vita3kSettings.BypassVitaImport = ours.Bypass;
                return null;
            }
            catch (Exception ex) { return "the settings could not be written: " + ex.Message; }
        }
    }

    internal sealed class Vita3kSettingsPage : UserControl
    {
        private readonly CheckBox _bypass;

        public bool Bypass => _bypass.Checked;

        public Vita3kSettingsPage()
        {
            AutoScroll = true;
            Padding = new Padding(12);

            var import = new GroupBox { Text = "LaunchBox's Import ROM Files wizard", Dock = DockStyle.Top, Height = 150, Padding = new Padding(10) };
            _bypass = new CheckBox
            {
                Text = "Import Vita games as ROM files (bypass LaunchBox's own PS Vita import)",
                AutoSize = true, Location = new Point(14, 26), Checked = Vita3kSettings.BypassVitaImport,
            };
            var explain = new Label
            {
                AutoSize = false, Location = new Point(32, 52), Size = new Size(540, 86), ForeColor = SystemColors.GrayText,
                Text = "For \"Sony Playstation Vita\" (or a platform scraped as it), LaunchBox ignores the files you pick "
                     + "and lists the games installed in Vita3K instead - none, with this plugin's console, which is "
                     + "rebuilt for every session. When this is on, the wizard scans your .zip / .vpk files like any "
                     + "other ROMs; the platform, its metadata and its images stay \"Sony Playstation Vita\".",
            };
            import.Controls.Add(_bypass);
            import.Controls.Add(explain);

            var where = new Label
            {
                Dock = DockStyle.Top, Height = 40, Padding = new Padding(0, 12, 0, 0), ForeColor = SystemColors.GrayText,
                Text = "Settings file: " + Vita3kSettings.SettingsPath,
            };

            Controls.Add(where);
            Controls.Add(import);
        }
    }
}
