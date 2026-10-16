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
        public static string Level(IGame game, string emulatorPath)
        {
            try
            {
                if (game == null || !Vita3kPaths.IsVita3kExecutable(emulatorPath)) return null;
                var rom = Vita3kPlugin.ResolveFullPath(game.ApplicationPath);
                if (string.IsNullOrWhiteSpace(rom)) return null;
                if (!File.Exists(rom) && !Directory.Exists(rom)) return "missing:file";
                var id = Vita3kPlugin.TitleIdOf(game.ApplicationPath);
                if (id == null) return null;
                var layout = Vita3kPaths.Resolve(Vita3kPlugin.ResolveFullPath(emulatorPath));
                return Vita3kCompat.LookupKept(layout, id)?.State switch
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
            }
            catch (Exception ex) { Log.Warn("LaunchWith.Level", ex); return null; }
        }
    }
}
