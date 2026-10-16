// Vita3K's verdict in "Launch With" - what Nixx-Menus shows on its line for a game (src\Menus\LaunchWith\Labels.cs has the
// contract, by name: LbIntegrations.<assembly name>.LaunchWith.Level).
//
// From Vita3K's own compatibility list (Vita3kCompat: app_compat_db.xml, Vita3K/compatibility), the state of the game's
// title id - what the game's window shows:
//   playable    Playable
//   glitches    Ingame+, Ingame (gets in game, with faults)
//   nostart     Menu, Intro, Boots, Nothing (never gets in game)
//   missing     file: the game's file is not there
//   (none)      not in the list, a state label the plugin does not know, no title id on the file
// The title id is read once per file (Vita3kPlugin.TitleIdOf, GlobalCache across sessions); the list's answer is kept while
// the list keeps its date and size.

using System;
using System.IO;
using Unbroken.LaunchBox.Plugins.Data;

namespace LbIntegrations.Vita3k
{
    /// <summary>What Nixx-Menus calls, by name. Public, its signature fixed.</summary>
    public static class LaunchWith
    {
        public static string Level(IGame game, string emulatorPath) => Ask(game, emulatorPath).Level;

        /// <summary>The tooltip: the state's description and the report's other labels (its problems), its issue number.</summary>
        public static string Note(IGame game, string emulatorPath) => Ask(game, emulatorPath).Note;

        /// <summary>A page about the verdict, for a click (LiteBox's detail card): the list's own page of the game. Null when none.
        /// The last Ask on this thread leaves it - asked right after Level, as the hosts do.</summary>
        public static string Url(IGame game, string emulatorPath) { _url = null; Ask(game, emulatorPath); return _url; }

        [ThreadStatic] private static string _url;

        private static (string Level, string Note) Ask(IGame game, string emulatorPath)
        {
            try
            {
                if (game == null || !Vita3kPaths.IsVita3kExecutable(emulatorPath)) return (null, null);
                var rom = Vita3kPlugin.ResolveFullPath(game.ApplicationPath);
                if (string.IsNullOrWhiteSpace(rom)) return (null, null);
                if (!File.Exists(rom) && !Directory.Exists(rom)) return ("missing:file", "The game's file is not there: " + rom);
                var id = Vita3kPlugin.TitleIdOf(game.ApplicationPath);
                if (id == null) return (null, null);
                var layout = Vita3kPaths.Resolve(Vita3kPlugin.ResolveFullPath(emulatorPath));
                var c = Vita3kCompat.LookupKept(layout, id);
                var level = c?.State switch
                {
                    "Playable" => "playable",
                    "Ingame+" => "glitches",
                    "Ingame" => "glitches",
                    "Menu" => "nostart",
                    "Intro" => "nostart",
                    "Boots" => "nostart",
                    "Nothing" => "nostart",
                    _ => null,
                };
                if (level == null) return (null, null);
                _url = c.IssueId > 0 ? c.Url : null;
                var note = "Vita3K's list: " + c.State + (string.IsNullOrWhiteSpace(c.StateDescription) ? "" : " - " + c.StateDescription.Trim())
                           + (c.IssueId > 0 ? " (report #" + c.IssueId + ")" : "")
                           + (c.Problems.Count > 0 ? "\n" + string.Join(", ", c.Problems) : "");
                return (level, note);
            }
            catch (Exception ex) { Log.Warn("LaunchWith.Level", ex); return (null, null); }
        }
    }
}
