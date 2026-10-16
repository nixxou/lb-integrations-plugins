// Nixx-EmuLabels: in every emulator list, how well each emulator runs the selected game.
//
//   PluginInitialized          the save guard goes in (SaveGuard) - and without it, nothing else runs
//   *StartupCompleted,
//   SelectionChanged,
//   GameExited                 the emulators are labelled for the one selected game (Labeler)
//   GameStarting,
//   *ShutdownBeginning         every label comes off: a launch reads the emulator, and so does a save
//
// More than one game selected, or none: no labels. The labels come from CompatTable.

using System;
using Unbroken.LaunchBox.Plugins;
using Unbroken.LaunchBox.Plugins.Data;

namespace LbIntegrations.EmuLabels
{
    public sealed class EmuLabelsPlugin : ISystemEventsPlugin
    {
        private static readonly CompatTable Table = new CompatTable(Log.Beside("emu-labels.json"));

        public void OnEventRaised(string eventType)
        {
            try
            {
                switch (eventType)
                {
                    case SystemEventTypes.PluginInitialized:
                        SaveGuard.Install();
                        break;

                    case SystemEventTypes.LaunchBoxStartupCompleted:
                    case SystemEventTypes.BigBoxStartupCompleted:
                    case SystemEventTypes.SelectionChanged:
                    case SystemEventTypes.GameExited:
                        Relabel(eventType);
                        break;

                    case SystemEventTypes.GameStarting:
                    case SystemEventTypes.LaunchBoxShutdownBeginning:
                    case SystemEventTypes.BigBoxShutdownBeginning:
                        var n = Labeler.Clear(PluginHelper.DataManager?.GetAllEmulators());
                        Log.Verbose(eventType + ": " + n + " label(s) removed");
                        break;
                }
            }
            catch (Exception ex) { Log.Warn(eventType, ex); }
        }

        private static void Relabel(string eventType)
        {
            if (!SaveGuard.Installed && !SaveGuard.Install()) return;
            Table.Refresh();

            var selected = PluginHelper.StateManager?.GetAllSelectedGames();
            var game = selected != null && selected.Length == 1 ? selected[0] : null;
            var n = Labeler.Apply(PluginHelper.DataManager?.GetAllEmulators(), game, Table);
            Log.Verbose(eventType + ": " + (game == null ? "no single game" : game.Title + " (" + game.Platform + ")")
                        + ", " + n + " title(s) changed");
        }
    }
}
