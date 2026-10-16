// Flycast's verdict in "Launch With" - what Nixx-Menus shows on its line for a game (src\Menus\LaunchWith\Labels.cs has the
// contract, by name: LbIntegrations.<assembly name>.LaunchWith.Level).
//
// Flycast keeps no compatibility list; its arcade table is a strict one: an arcade zip runs only as one of the sets Flycast
// knows (flycast-id --sets, the table of the very Flycast it ships with), by the zip's name.
//   nostart     a zip or 7z that is none of Flycast's sets, a BIOS, or a Naomi GD-ROM game whose image is not there (the
//               launch refuses it - FlycastPlugin.PrepareForLaunch)
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
        public static string Level(IGame game, string emulatorPath)
        {
            try
            {
                if (game == null || !FlycastPaths.IsFlycastExecutable(emulatorPath)) return null;
                var rom = game.ApplicationPath;
                if (string.IsNullOrWhiteSpace(rom)) return null;
                var ext = Path.GetExtension(rom).ToLowerInvariant();
                if (ext != ".zip" && ext != ".7z") return null;
                var sets = FlycastGameIdentity.SetsIfReady(FlycastPaths.Resolve(FlycastPlugin.ResolveFullPath(emulatorPath)));
                if (sets == null) return null;
                if (!sets.TryGetValue(Path.GetFileNameWithoutExtension(rom), out var set) || set.System == FlycastGameIdentity.BiosSystem) return "nostart";
                if (set.GdRom != null && FlycastGameIdentity.GdRomImage(FlycastPlugin.ResolveFullPath(rom), set) == null) return "nostart";
                return null;
            }
            catch (Exception ex) { Log.Warn("LaunchWith.Level", ex); return null; }
        }
    }
}
