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
using LbIntegrations.Xbox;
using Unbroken.LaunchBox.Plugins.Data;

namespace LbIntegrations.Xemu
{
    /// <summary>What Nixx-Menus calls, by name. Public, its signature fixed.</summary>
    public static class LaunchWith
    {
        private sealed class DiscFacts { public string TitleId, Level; public bool Pending; public DateTime At; }

        private static readonly Dictionary<string, DiscFacts> Discs = new Dictionary<string, DiscFacts>(StringComparer.OrdinalIgnoreCase);

        public static string Level(IGame game, string emulatorPath)
        {
            try
            {
                if (game == null || !XemuPaths.IsXemuExe(emulatorPath)) return null;
                var rom = XemuPlugin.ResolveFullPath(game.ApplicationPath);
                var disc = DiscOf(rom, XemuPlugin.ResolveFullPath(emulatorPath));
                if (disc == null || disc.Pending) return null;
                if (disc.Level != null) return disc.Level;
                var (report, title) = XboxCompat.Xemu(disc.TitleId);
                var state = !string.IsNullOrEmpty(report?.Rating) ? report.Rating : title?.Status;
                return state switch
                {
                    "Perfect" => "perfect",
                    "Playable" => "playable",
                    "Starts" => "glitches",
                    "Intro" => "nostart",
                    "Broken" => "nostart",
                    "Unknown" => "untested",
                    _ => null,
                };
            }
            catch (Exception ex) { Log.Warn("LaunchWith.Level", ex); return null; }
        }

        private static DiscFacts DiscOf(string rom, string exe)
        {
            if (string.IsNullOrWhiteSpace(rom)) return null;
            var fi = new FileInfo(rom);
            if (!fi.Exists) return Directory.Exists(rom) ? null : new DiscFacts { Level = "missing:file" };
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
                else if (d.Kind == XemuDiscKind.Xbox360 || d.Kind == XemuDiscKind.TreeInArchive) disc = new DiscFacts { Level = "unsupported" };
                else disc = new DiscFacts { Level = "invalid" };             // a disc, a ZArchive: no default.xbe in it
            }
            lock (Discs) { if (Discs.Count > 4096) Discs.Clear(); Discs[key] = disc; }
            return disc;
        }
    }
}
