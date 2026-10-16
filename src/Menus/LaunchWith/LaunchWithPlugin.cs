// Nixx-Menus' third relay: "Launch With" says how well each emulator runs the game (Labels), in LaunchBox's right-click
// submenu (LaunchBoxMenus) and Big Box's popup (BigBoxPopup). Display only - see src\LaunchWithLabels\README.md, the PoC
// this was taken from, for what was measured and why there is no Harmony patch. Switched on the Compatibility page
// ("launchWithLabels"), read once at start.

using System;
using System.Diagnostics;
using Unbroken.LaunchBox.Plugins;
using Unbroken.LaunchBox.Plugins.Data;

namespace LbIntegrations.Menus.LaunchWith
{
    public sealed class NixxLaunchWithLabels : ISystemEventsPlugin
    {
        private static bool _installed;

        public void OnEventRaised(string eventType)
        {
            if (eventType != SystemEventTypes.PluginInitialized || _installed) return;
            _installed = true;
            try
            {
                if (!LbIntegrations.Compat.Compat.On("launchWithLabels")) { Log.Info("turned off on the Compatibility page"); return; }
                var host = Process.GetCurrentProcess().ProcessName;
                if (string.Equals(host, "LaunchBox", StringComparison.OrdinalIgnoreCase)) LaunchBoxMenus.Install();
                else if (string.Equals(host, "BigBox", StringComparison.OrdinalIgnoreCase)) BigBoxPopup.Install();
                else { Log.Info("host " + host + ": nothing to do"); return; }
                Labels.Refresh();
            }
            catch (Exception ex) { Log.Warn("install", ex); }
        }
    }
}
