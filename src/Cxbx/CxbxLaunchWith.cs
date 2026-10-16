// Cxbx-Reloaded's verdict in "Launch With" - what Nixx-Menus shows on its line for a game (src\Menus\LaunchWith\Labels.cs has
// the contract, by name: LbIntegrations.<assembly name>.LaunchWith.Level).
//
// From Cxbx-Reloaded's own compatibility list (CxbxCompat, cxbx-reloaded.co.uk), by the disc's serial and version as the
// game's window finds it (CxbxCompat.For: this very version, else the serial's first line):
//   playable    Playable
//   glitches    In-Game
//   nostart     Boots (never gets in game), Nothing
//   untested    Untested: the list's own word - the game is in it, nobody has said yet
//   unsupported an Xbox 360 disc (Xenia's), a file that is neither an Xbox executable, disc image nor archive
//   invalid     a disc or ZArchive without a default.xbe, an .xbe that is not an Xbox executable
//   missing     file: the game's file is not there
//   (none)      not in the list, or an archive never unpacked (its default.xbe unknown until its first launch)
// The disc's executable is read once per file (StampedCache: while the file keeps its date and size); an archive is answered
// from titles.tsv alone, as the Game Saves page does - never opened here.

using System;
using System.IO;
using Unbroken.LaunchBox.Plugins.Data;
using LbIntegrations.Lbip;

namespace LbIntegrations.Cxbx
{
    /// <summary>What Nixx-Menus calls, by name. Public, its signature fixed.</summary>
    public static class LaunchWith
    {
        private sealed class DiscFacts { public XbeInfo Xbe; public string Level; }

        public static string Level(IGame game, string emulatorPath)
        {
            try
            {
                if (game == null || !CxbxPaths.IsCxbx(emulatorPath)) return null;
                var rom = CxbxPlugin.ResolveFullPathForUi(game.ApplicationPath);
                if (string.IsNullOrWhiteSpace(rom)) return null;
                if (!File.Exists(rom)) return Directory.Exists(rom) ? null : "missing:file";
                var disc = StampedCache.Get(rom, "launch-with.xbe", () =>
                {
                    var d = CxbxGame.Describe(rom, openArchives: false);
                    if (d.Xbe?.TitleId > 0) return new DiscFacts { Xbe = d.Xbe };
                    if (d.Kind == CxbxRomKind.Xbox360) return new DiscFacts { Level = "unsupported" };
                    if (d.Kind == CxbxRomKind.Xbe || d.Kind == CxbxRomKind.Image || d.Kind == CxbxRomKind.Zar) return new DiscFacts { Level = "invalid" };
                    // Unknown: an archive not unpacked yet (known at its first launch) - nothing said, asked again; else a file
                    // that is no Xbox game at all.
                    if (d.Kind == CxbxRomKind.Unknown && !CxbxGame.IsArchive(rom) && !Disc.SectorImage.IsLaterPart(rom)) return new DiscFacts { Level = "unsupported" };
                    return null;
                }, keepNull: false);
                if (disc == null) return null;
                if (disc.Level != null) return disc.Level;
                return CxbxCompat.For(disc.Xbe).Exact?.State switch
                {
                    "Playable" => "playable",
                    "In-Game" => "glitches",
                    "Boots" => "nostart",
                    "Nothing" => "nostart",
                    "Untested" => "untested",
                    _ => null,
                };
            }
            catch (Exception ex) { Log.Warn("LaunchWith.Level", ex); return null; }
        }
    }
}
