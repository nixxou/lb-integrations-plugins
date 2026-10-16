// Xenia's verdict in "Launch With" - what Nixx-Menus shows on its line for a game (src\Menus\LaunchWith\Labels.cs has the
// contract, by name: LbIntegrations.<assembly name>.LaunchWith.Level).
//
// From Xenia's own compatibility list (XeniaCompat, xenia-canary/game-compatibility), the best report of the game's title id
// - the game's window's first line:
//   playable    Playable
//   glitches    Gameplay (gets in game, with faults)
//   nostart     Loads (never gets in game), Unplayable
//   untested    Unknown: the list has the game, with no state - its own word
//   missing     file: the game's file is not there
//   (none)      not in the list, no title id on the file
// The title id is read once per file (XeniaTitleId's cache, then GlobalCache across sessions).

using System;
using System.IO;
using System.Linq;
using Unbroken.LaunchBox.Plugins.Data;

namespace LbIntegrations.Xenia
{
    /// <summary>What Nixx-Menus calls, by name. Public, its signature fixed.</summary>
    public static class LaunchWith
    {
        public static string Level(IGame game, string emulatorPath) => Ask(game, emulatorPath).Level;

        /// <summary>The tooltip: the report's title and state label, its other labels (crashes, graphics...), its issue number.</summary>
        public static string Note(IGame game, string emulatorPath) => Ask(game, emulatorPath).Note;

        private static (string Level, string Note) Ask(IGame game, string emulatorPath)
        {
            try
            {
                if (game == null || !XeniaPaths.IsXeniaExecutable(emulatorPath)) return (null, null);
                var rom = XeniaPlugin.ResolveFullPathForUi(game.ApplicationPath);
                if (string.IsNullOrEmpty(rom)) return (null, null);
                if (!File.Exists(rom) && !Directory.Exists(rom)) return ("missing:file", "The game's file is not there: " + rom);
                var id = XeniaTitleId.Of(rom);
                if (id == null) return (null, null);
                var e = XeniaCompat.Lookup(id).FirstOrDefault();
                var level = e?.State switch
                {
                    "Playable" => "playable",
                    "Gameplay" => "glitches",
                    "Loads" => "nostart",
                    "Unplayable" => "nostart",
                    "Unknown" => "untested",
                    _ => null,
                };
                if (level == null) return (null, null);
                var others = e.Labels.Where(l => !l.StartsWith("state-", StringComparison.OrdinalIgnoreCase)).ToList();
                var note = "Xenia's list: " + e.State + (string.IsNullOrEmpty(e.Detail) || e.Detail.Equals(e.State, StringComparison.OrdinalIgnoreCase) ? "" : " (" + e.Detail + ")")
                           + (string.IsNullOrEmpty(e.Title) ? "" : " - " + e.Title) + (e.Issue > 0 ? ", report #" + e.Issue : "")
                           + (others.Count > 0 ? "\n" + string.Join(", ", others) : "");
                return (level, note);
            }
            catch (Exception ex) { Log.Warn("LaunchWith.Level", ex); return (null, null); }
        }
    }
}
