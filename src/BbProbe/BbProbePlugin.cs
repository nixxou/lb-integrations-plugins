// Nixx-BbProbe: what Big Box does around its emulator lists. Active in BigBox.exe only; elsewhere it logs
// one line and stays out of the way. See Hooks for what is listened to, and ProbeLog for where it goes.

using System;
using System.Diagnostics;
using Unbroken.LaunchBox.Plugins;
using Unbroken.LaunchBox.Plugins.Data;

namespace LbIntegrations.BbProbe
{
    public sealed class BbProbePlugin : ISystemEventsPlugin, IGameLaunchingPlugin
    {
        private static bool _on;

        public void OnEventRaised(string eventType)
        {
            try
            {
                if (eventType == SystemEventTypes.PluginInitialized)
                {
                    if (_on) return;
                    var host = Process.GetCurrentProcess().ProcessName;
                    ProbeLog.Start(host);
                    if (!string.Equals(host, "BigBox", StringComparison.OrdinalIgnoreCase))
                    {
                        ProbeLog.Info("not Big Box: inactive");
                        return;
                    }
                    _on = true;
                    Hooks.Install();
                    return;
                }
                if (!_on) return;
                if (eventType == SystemEventTypes.SelectionChanged)
                {
                    var g = PluginHelper.StateManager?.GetAllSelectedGames();
                    ProbeLog.Info("event SelectionChanged -> " + (g != null && g.Length == 1 ? Describe.Safe(() => g[0].Title) : (g?.Length ?? 0) + " game(s)"));
                }
                else ProbeLog.Info("event " + eventType);
            }
            catch (Exception ex) { ProbeLog.Warn(eventType, ex); }
        }

        public void OnBeforeGameLaunching(IGame game, IAdditionalApplication app, IEmulator emulator)
        {
            if (!_on) return;
            ProbeLog.Info("BEFORE LAUNCH game=\"" + Describe.Safe(() => game?.Title) + "\" emulator=\""
                          + (emulator == null ? "null" : Describe.Title(emulator)) + "\"");
            Describe.Stack("before launch");
        }

        public void OnAfterGameLaunched(IGame game, IAdditionalApplication app, IEmulator emulator)
        {
            if (_on) ProbeLog.Info("AFTER LAUNCH game=\"" + Describe.Safe(() => game?.Title) + "\"");
        }

        public void OnGameExited()
        {
            if (_on) ProbeLog.Info("GAME EXITED");
        }
    }
}
