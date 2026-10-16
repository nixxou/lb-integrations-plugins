// xemu's verdict in "Launch With" - what Nixx-Menus shows on its line for a game (src\Menus\LaunchWith\Labels.cs has the
// contract, by name: LbIntegrations.<assembly name>.LaunchWith.Level).
//
// From xemu's own compatibility list (XboxCompat.Xemu: the newest report of the disc's title id, else the site's status), as
// the game's window shows it:
//   perfect     Perfect
//   playable    Playable
//   glitches    Starts (gets in game, with serious faults)
//   nostart     Intro, Broken
//   unsupported what xemu does not take: an Xbox 360 disc (Xenia's), an archive of the game's files rather than of a disc image
//   invalid     a disc without a default.xbe
//   missing     file: the game's file is not there
//   untested    the list has the title, with no state ("Unknown") - its own word
//   (none)      not in the list, an archive never launched (its disc unknown until then)
// The disc is read once per file (its date and size); an archive whose image is not unpacked yet is looked at again every
// two minutes at most, not at each opening of the menu.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using LbIntegrations.Xbox;
using Unbroken.LaunchBox.Plugins.Data;

namespace LbIntegrations.Xemu
{
    /// <summary>What Nixx-Menus calls, by name. Public, its signature fixed.</summary>
    public static class LaunchWith
    {
        private sealed class DiscFacts { public string TitleId, Level, Note; public bool Pending; public DateTime At; }

        private static readonly Dictionary<string, DiscFacts> Discs = new Dictionary<string, DiscFacts>(StringComparer.OrdinalIgnoreCase);

        public static string Level(IGame game, string emulatorPath) => Ask(game, emulatorPath).Level;

        /// <summary>The tooltip: the newest report's comment, with its xemu version and date - or why xemu does not take the file.</summary>
        public static string Note(IGame game, string emulatorPath) => Ask(game, emulatorPath).Note;

        private static (string Level, string Note) Ask(IGame game, string emulatorPath)
        {
            try
            {
                if (game == null || !XemuPaths.IsXemuExe(emulatorPath)) return (null, null);
                var rom = XemuPlugin.ResolveFullPath(game.ApplicationPath);
                var disc = DiscOf(rom, XemuPlugin.ResolveFullPath(emulatorPath));
                if (disc == null || disc.Pending) return (null, null);
                if (disc.Level != null) return (disc.Level, disc.Note);
                var (report, title) = XboxCompat.Xemu(disc.TitleId);
                var state = !string.IsNullOrEmpty(report?.Rating) ? report.Rating : title?.Status;
                var level = state switch
                {
                    "Perfect" => "perfect",
                    "Playable" => "playable",
                    "Starts" => "glitches",
                    "Intro" => "nostart",
                    "Broken" => "nostart",
                    "Unknown" => "untested",
                    _ => null,
                };
                if (level == null) return (null, null);
                string note;
                if (!string.IsNullOrEmpty(report?.Rating))
                {
                    note = "xemu's newest report: " + report.Rating
                           + (string.IsNullOrWhiteSpace(report.XemuVersion) ? "" : ", xemu " + report.XemuVersion.Trim())
                           + (report.When > DateTime.MinValue ? ", " + report.When.ToLocalTime().ToString("d", System.Globalization.CultureInfo.CurrentCulture) : "");
                    if (!string.IsNullOrWhiteSpace(report.Comment)) note += "\n" + Short(report.Comment.Trim());
                }
                else note = "xemu's list: " + state + " (no report yet)";
                return (level, note);
            }
            catch (Exception ex) { Log.Warn("LaunchWith.Level", ex); return (null, null); }
        }

        /// <summary>A report's comment for a tooltip: its lines without the blank ones and the "-----" rulers, 400 characters at most.</summary>
        private static string Short(string s)
        {
            var lines = s.Replace("\r", "").Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0 && l.Trim('-', '=', '_').Length > 0);
            var text = string.Join("\n", lines);
            return text.Length <= 400 ? text : text.Substring(0, 400).TrimEnd() + "...";
        }

        private static DiscFacts DiscOf(string rom, string exe)
        {
            if (string.IsNullOrWhiteSpace(rom)) return null;
            var fi = new FileInfo(rom);
            if (!fi.Exists) return Directory.Exists(rom) ? null : new DiscFacts { Level = "missing:file", Note = "The game's file is not there: " + rom };
            var key = fi.FullName + "|" + fi.Length + "|" + fi.LastWriteTimeUtc.Ticks;
            lock (Discs) if (Discs.TryGetValue(key, out var known) && (!known.Pending || DateTime.UtcNow - known.At < TimeSpan.FromMinutes(2))) return known;
            DiscFacts disc;
            var id = XemuDisc.TitleIdOf(rom, exe);
            if (id != null) disc = new DiscFacts { TitleId = id };
            else
            {
                var d = XemuDisc.Describe(rom);
                if (d.Kind == XemuDiscKind.ImageInArchive) disc = new DiscFacts { Pending = true, At = DateTime.UtcNow };
                else if (d.Kind == XemuDiscKind.Unknown) return null;   // not read (a lock, an error): asked again next time
                else if (d.Kind == XemuDiscKind.Xbox360 || d.Kind == XemuDiscKind.TreeInArchive) disc = new DiscFacts { Level = "unsupported", Note = Sentence(d.Problem) };
                else disc = new DiscFacts { Level = "invalid", Note = Sentence(d.Problem) };             // a disc, a ZArchive: no default.xbe in it
            }
            lock (Discs) { if (Discs.Count > 4096) Discs.Clear(); Discs[key] = disc; }
            return disc;
        }

        private static string Sentence(string s) => string.IsNullOrEmpty(s) ? null : char.ToUpperInvariant(s[0]) + s.Substring(1) + ".";
    }
}
