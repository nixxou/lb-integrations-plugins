// PPSSPP's verdict in "Launch With" - what Nixx-Menus shows on its line for a game (src\Menus\LaunchWith\Labels.cs has the
// contract, by name: LbIntegrations.<assembly name>.LaunchWith.Level).
//
// From PPSSPP's own report site (PpssppCompat, report.ppsspp.org), the best report of the disc's DISC_ID and DISC_VERSION -
// what the game's window shows:
//   perfect     Perfect
//   playable    Playable
//   glitches    Ingame
//   nostart     Menu/Intro, Doesn't Boot
//   missing     file: the game's file is not there
//   (none)      not in the list, or in it with no report yet - nothing to say
// The PARAM.SFO is read once per file (PspDiscId's cache, psp-sfo.tsv across sessions).

using System;
using System.IO;
using Unbroken.LaunchBox.Plugins.Data;

namespace LbIntegrations.Ppsspp
{
    /// <summary>What Nixx-Menus calls, by name. Public, its signature fixed.</summary>
    public static class LaunchWith
    {
        public static string Level(IGame game, string emulatorPath) => Ask(game, emulatorPath).Level;

        private static (string Level, string Note) Ask(IGame game, string emulatorPath)
        {
            try
            {
                if (game == null || !PpssppPaths.IsPpssppExecutable(emulatorPath)) return (null, null);
                var rom = PpssppPlugin.ResolveFullPathOf(game.ApplicationPath);
                if (string.IsNullOrWhiteSpace(rom)) return (null, null);
                if (!File.Exists(rom) && !Directory.Exists(rom)) return ("missing:file", "The game's file is not there: " + rom);
                var discId = PspDiscId.Of(rom);
                var version = discId == null ? null : PspDiscId.SfoOf(rom)?.GetString("DISC_VERSION");
                var c = PpssppCompat.Of(discId, version);
                var level = c?.Rating switch
                {
                    "Perfect" => "perfect",
                    "Playable" => "playable",
                    "Ingame" => "glitches",
                    "Menu/Intro" => "nostart",
                    "Doesn't Boot" => "nostart",
                    _ => null,
                };
                if (level == null) return (null, null);
                return (level, null);   // the site keeps a rating, no comment: no tooltip
            }
            catch (Exception ex) { Log.Warn("LaunchWith.Level", ex); return (null, null); }
        }
    }
}
