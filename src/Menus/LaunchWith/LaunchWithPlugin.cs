// Nixx-Menus' third relay: "Launch With" says how well each emulator runs the game (Labels), in LaunchBox's right-click
// submenu (LaunchBoxMenus) and Big Box's popup (BigBoxPopup). Display only - see src\LaunchWithLabels\README.md, the PoC
// this was taken from, for what was measured and why there is no Harmony patch. Two switches on the Compatibility page, read
// once at start: "launchWithLabels" (LaunchBox's menu) and "launchWithLabelsBigBox" (Big Box's popup).

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
                var host = Process.GetCurrentProcess().ProcessName;
                bool bigBox = string.Equals(host, "BigBox", StringComparison.OrdinalIgnoreCase);
                if (!bigBox && !string.Equals(host, "LaunchBox", StringComparison.OrdinalIgnoreCase)) { Log.Info("host " + host + ": nothing to do"); return; }
                var id = bigBox ? "launchWithLabelsBigBox" : "launchWithLabels";
                if (!LbIntegrations.Compat.Compat.On(id)) { Log.Info(id + " turned off on the Compatibility page"); return; }
                if (bigBox) BigBoxPopup.Install(); else LaunchBoxMenus.Install();
                Labels.Refresh();
            }
            catch (Exception ex) { Log.Warn("install", ex); }
        }
    }
}
