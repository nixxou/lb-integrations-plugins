// Nixx-LaunchWithLabels (PoC): "Launch With" says how well each emulator runs the game.
//
//   LaunchBox.exe -> LaunchBoxMenus (the right-click "Launch With" submenu, WPF MenuItem headers)
//   BigBox.exe    -> BigBoxPopup    (the "Launch With" popup, PopupOption.DisplayString)
//
// Display only, in both: what a line launches is never touched, no data object carries a label, nothing is
// patched. The labels come from CompatTable (%LOCALAPPDATA%\lb-integrations-plugins\emu-labels.json).

using System;
using System.Diagnostics;
using Unbroken.LaunchBox.Plugins;
using Unbroken.LaunchBox.Plugins.Data;

namespace LbIntegrations.LaunchWithLabels
{
    public sealed class LaunchWithLabelsPlugin : ISystemEventsPlugin
    {
        internal static readonly CompatTable Table = new CompatTable(Log.Beside("emu-labels.json"));
        private static bool _installed;

        public void OnEventRaised(string eventType)
        {
            if (eventType != SystemEventTypes.PluginInitialized || _installed) return;
            _installed = true;
            try
            {
                var host = Process.GetCurrentProcess().ProcessName;
                if (string.Equals(host, "LaunchBox", StringComparison.OrdinalIgnoreCase)) LaunchBoxMenus.Install();
                else if (string.Equals(host, "BigBox", StringComparison.OrdinalIgnoreCase)) BigBoxPopup.Install();
                else Log.Info("host " + host + ": nothing to do");
                Table.Refresh();
            }
            catch (Exception ex) { Log.Warn("install", ex); }
        }
    }
}
