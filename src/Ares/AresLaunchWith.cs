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
// Note (the tooltip): the line's status as the site writes it - "not verified yet" for its Unverified badge -, the ares version
// it was tried with, the title it was found as when it is not the file's, and its notes ("Some vertical jiddering").
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
        private static readonly Dictionary<string, (object Generation, string Level, string Note, string Url)> Known = new Dictionary<string, (object, string, string, string)>(StringComparer.OrdinalIgnoreCase);

        public static string Level(IGame game, string emulatorPath) => Ask(game, emulatorPath).Level;

        public static string Note(IGame game, string emulatorPath) => Ask(game, emulatorPath).Note;

        /// <summary>A page about the verdict, for a click (LiteBox's detail card): the list's own page of the game. Null when none.
        /// The last Ask on this thread leaves it - asked right after Level, as the hosts do.</summary>
        public static string Url(IGame game, string emulatorPath) { _url = null; Ask(game, emulatorPath); return _url; }

        [ThreadStatic] private static string _url;

        private static (string Level, string Note) Ask(IGame game, string emulatorPath)
        {
            try
            {
                if (game == null || !AresLaunch.IsAresExe(emulatorPath)) return (null, null);
                var rom = game.ApplicationPath;
                if (string.IsNullOrWhiteSpace(rom)) return (null, null);
                var full = AresPlugin.ResolveFullPathForUi(rom);
                bool gone = full != null && !full.Contains("://") && !File.Exists(full) && !Directory.Exists(full);
                // The list's first reading takes a second: done in the background, no label until then - never on the menu's time.
                if (!AresCompat.Loaded) { System.Threading.Tasks.Task.Run(() => { try { _ = AresCompat.Generation; } catch { } }); return (null, null); }
                var key = game.Id + "|" + rom + "|" + game.Title + "|" + game.Platform + "|" + emulatorPath;
                var generation = AresCompat.Generation;
                lock (Known) if (Known.TryGetValue(key, out var k) && ReferenceEquals(k.Generation, generation)) { _url = k.Url; return Gone(gone, full, k.Level, k.Note); }

                var emu = PluginHelper.DataManager?.GetAllEmulators()?.FirstOrDefault(e => string.Equals(Safe(() => e.ApplicationPath), emulatorPath, StringComparison.OrdinalIgnoreCase));
                var system = AresSettings.GameSystem(game.Id) ?? AresPlugin.SystemOfForUi(emu, null, game.Platform, rom);
                var alts = Safe(() => game.GetAllAlternateNames())?.Select(a => a?.Name).Where(n => !string.IsNullOrWhiteSpace(n)).ToList();
                var matches = AresCompat.FindAll(system, AresPlugin.ResolveFullPathForUi(rom), game.Title, alts, Safe(() => game.LaunchBoxDbId));
                var best = matches.FirstOrDefault();
                var level = Of(best?.Entry?.Status);
                var note = level == null ? null : NoteOf(best);
                var url = level == null ? null : AresCompat.PageOf(best.Entry);
                _url = url;
                lock (Known) { if (Known.Count > 4096) Known.Clear(); Known[key] = (generation, level, note, url); }
                return Gone(gone, full, level, note);
            }
            catch (Exception ex) { Log.Warn("LaunchWith.Level", ex); return (null, null); }
        }

        /// <summary>A file not there: "missing:file" first, the list's verdict (found by the game's names) after it.</summary>
        private static (string Level, string Note) Gone(bool gone, string full, string level, string note)
            => !gone ? (level, note) : ("missing:file" + (level == null ? "" : ";" + level), "The game's file is not there: " + full + (note == null ? "" : "\n" + note));

        private static string NoteOf(AresCompatMatch m)
        {
            var e = m.Entry;
            var line = "ares's list: " + e.Status + (e.Unverified ? " (not verified yet)" : "") + (string.IsNullOrEmpty(e.Version) ? "" : ", tried with " + e.Version);
            if (!m.Best) line += "\nFound as " + e.Title;
            if (!string.IsNullOrWhiteSpace(e.Notes)) line += "\n" + e.Notes.Trim();
            if (e.IssueList.Count > 0) line += "\nGitHub: " + string.Join("\n        ", e.IssueList.Take(5).Select(i => "#" + i.Number + " " + i.Title))
                                               + (e.IssueList.Count > 5 ? "\n        and " + (e.IssueList.Count - 5) + " more" : "");
            else if (e.Issues > 0) line += "\nGitHub issues: " + e.Issues;
            return line;
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
