// Flycast's verdict in "Launch With" - what Nixx-Menus shows on its line for a game (src\Menus\LaunchWith\Labels.cs has the
// contract, by name: LbIntegrations.<assembly name>.LaunchWith.Level).
//
// Flycast keeps no compatibility list; its arcade table is a strict one: an arcade zip runs only as one of the sets Flycast
// knows (flycast-id --sets, the table of the very Flycast it ships with), by the zip's name.
//   unsupported a zip or 7z that is none of Flycast's sets, or a BIOS
//   missing     CHD: a Naomi GD-ROM game whose image is not there (the launch refuses it - FlycastPlugin.PrepareForLaunch);
//               file: the game's file is not there
//   (none)      a set Flycast knows, a disc, anything else - nothing to say
// The table is asked in the background the first time (the tool may take seconds); until it is there, no label.

using System;
using System.IO;
using Unbroken.LaunchBox.Plugins.Data;

namespace LbIntegrations.Flycast
{
    /// <summary>What Nixx-Menus calls, by name. Public, its signature fixed.</summary>
    public static class LaunchWith
    {
        public static string Level(IGame game, string emulatorPath) => Ask(game, emulatorPath).Level;

        /// <summary>The tooltip: why the set does not run on Flycast, or where its GD-ROM image goes.</summary>
        public static string Note(IGame game, string emulatorPath) => Ask(game, emulatorPath).Note;

        private static (string Level, string Note) Ask(IGame game, string emulatorPath)
        {
            try
            {
                if (game == null || !FlycastPaths.IsFlycastExecutable(emulatorPath)) return (null, null);
                var rom = game.ApplicationPath;
                if (string.IsNullOrWhiteSpace(rom)) return (null, null);
                var full = FlycastPlugin.ResolveFullPath(rom);
                if (full != null && !File.Exists(full) && !Directory.Exists(full)) return ("missing:file", "The game's file is not there: " + full);
                var ext = Path.GetExtension(rom).ToLowerInvariant();
                if (ext != ".zip" && ext != ".7z") return (null, null);
                var sets = FlycastGameIdentity.SetsIfReady(FlycastPaths.Resolve(FlycastPlugin.ResolveFullPath(emulatorPath)));
                if (sets == null) return (null, null);
                var name = Path.GetFileNameWithoutExtension(rom);
                if (!sets.TryGetValue(name, out var set))
                    return ("unsupported", "\"" + name + "\" is not one of the " + sets.Count + " arcade sets Flycast runs (Naomi, Naomi 2, Atomiswave, System SP).");
                if (set.System == FlycastGameIdentity.BiosSystem) return ("unsupported", name + " is a BIOS set, not a game: Flycast wants it in its data folder.");
                if (set.GdRom != null && FlycastGameIdentity.GdRomImage(full, set) == null)
                    return ("missing:CHD", "This Naomi GD-ROM game needs its disc image: " + set.GdRom + ".chd, in a folder named " + set.Name
                                           + (set.Parent != null ? " (or " + set.Parent + ")" : "") + " beside the zip:\n"
                                           + Path.Combine(Path.GetDirectoryName(full) ?? "", set.Name, set.GdRom + ".chd"));
                return (null, null);
            }
            catch (Exception ex) { Log.Warn("LaunchWith.Level", ex); return (null, null); }
        }
    }
}
