// ares's verdict in "Launch With" - what Nixx-Menus shows on its line for a game (src\Menus\LaunchWith\Labels.cs has the
// contract, by name: LbIntegrations.<assembly name>.LaunchWith.Level).
//
// From ares's own compatibility list (AresCompat, ares-emu.net), its line for the game as the game's window finds it - the
// line of this very file first, else the first the names find:
//   playable    Completable
//   glitches    In-Game
//   nostart     Starts (never gets in game), Nothing
//   missing     file: the game's file is not there
//   (none)      not in the list, or a system ares has no page for - nothing to say: the list is a list of reports, a game
//               it lacks may well run
// Worked out once per game (names, system, the LaunchBox database entry's names) and kept in memory while the game's file,
// title and platform stay and the list is not read again. The list itself is read in the background the first time.

using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using Unbroken.LaunchBox.Plugins;
using Unbroken.LaunchBox.Plugins.Data;

namespace LbIntegrations.Ares
{
    /// <summary>What Nixx-Menus calls, by name. Public, its signature fixed.</summary>
    public static class LaunchWith
    {
        private static readonly Dictionary<string, (object Generation, string Level)> Known = new Dictionary<string, (object, string)>(StringComparer.OrdinalIgnoreCase);

        public static string Level(IGame game, string emulatorPath)
        {
            try
            {
                if (game == null || !AresLaunch.IsAresExe(emulatorPath)) return null;
                var rom = game.ApplicationPath;
                if (string.IsNullOrWhiteSpace(rom)) return null;
                var full = AresPlugin.ResolveFullPathForUi(rom);
                if (full != null && !full.Contains("://") && !File.Exists(full) && !Directory.Exists(full)) return "missing:file";
                // The list's first reading takes a second: done in the background, no label until then - never on the menu's time.
                if (!AresCompat.Loaded) { System.Threading.Tasks.Task.Run(() => { try { _ = AresCompat.Generation; } catch { } }); return null; }
                var key = game.Id + "|" + rom + "|" + game.Title + "|" + game.Platform + "|" + emulatorPath;
                var generation = AresCompat.Generation;
                lock (Known) if (Known.TryGetValue(key, out var k) && ReferenceEquals(k.Generation, generation)) return k.Level;

                var emu = PluginHelper.DataManager?.GetAllEmulators()?.FirstOrDefault(e => string.Equals(Safe(() => e.ApplicationPath), emulatorPath, StringComparison.OrdinalIgnoreCase));
                var system = AresSettings.GameSystem(game.Id) ?? AresPlugin.SystemOfForUi(emu, null, game.Platform, rom);
                var alts = Safe(() => game.GetAllAlternateNames())?.Select(a => a?.Name).Where(n => !string.IsNullOrWhiteSpace(n)).ToList();
                var matches = AresCompat.FindAll(system, AresPlugin.ResolveFullPathForUi(rom), game.Title, alts, Safe(() => game.LaunchBoxDbId));
                var level = Of(matches.FirstOrDefault()?.Entry?.Status);
                lock (Known) { if (Known.Count > 4096) Known.Clear(); Known[key] = (generation, level); }
                return level;
            }
            catch (Exception ex) { Log.Warn("LaunchWith.Level", ex); return null; }
        }

        private static string Of(string status) => status switch
        {
            "Completable" => "playable",
            "In-Game" => "glitches",
            "Starts" => "nostart",
            "Nothing" => "nostart",
            _ => null,
        };

        private static T Safe<T>(Func<T> f) { try { return f(); } catch { return default; } }
    }
}
