// A game's backup RAM carried between Kronos and Ymir, from the game's window - beside RetroArch's (KronosRetroArch), the same
// copy (KronosRetroArch.Copy: the file replaced kept, dated; refused while either emulator runs).
//
// THE SAME BYTES (docs\kronos.md §4, measured 19/10): Ymir's internal backup RAM (32 KiB, "BackUpRam Format") was used by Kronos
// and left byte-identical; both plugins' cartridges are Beetle's 512 KiB .bcr. The clock is not carried: Kronos reads the PC's
// time at each start, Ymir keeps its own (state\smpc-<region>.bin, per region, not per game).
//
// WHERE YMIR KEEPS THE GAME'S (the Ymir plugin's YmirSaves.RamPath / CartPath, Mehdi 20/10): <Ymir>\Saves\<platform>\<game>.bin
// and .bcr - the same folder and the same <game> as Kronos's own (the file without its extension and its "(Disc N)", the .m3u's
// name for a playlist; invalid characters made _), so Kronos's rule finds it, with Ymir's extension. A Ymir that never ran this
// game through the plugin has no file there: "none yet", and an export makes it.
//
// WHICH YMIR: the one in LaunchBox (ymir-sdl3.exe) whose row for the game's platform is the default, else one with a row for it,
// else the first - the game's own emulator first if it is a Ymir.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Unbroken.LaunchBox.Plugins;
using Unbroken.LaunchBox.Plugins.Data;

namespace LbIntegrations.Kronos
{
    internal static class KronosYmir
    {
        public const string ExecutableName = "ymir-sdl3.exe";

        private static bool IsYmir(string path)
        {
            try { return string.Equals(Path.GetFileName((path ?? "").Trim().Trim('"')), ExecutableName, StringComparison.OrdinalIgnoreCase); }
            catch { return false; }
        }

        /// <summary>The Ymir of the game's platform and the files it keeps the game's RAM and cartridge in. Never throws.</summary>
        public static KronosRaLink Find(IGame game)
        {
            var link = new KronosRaLink { Name = "Ymir", CoreName = "Ymir" };
            try
            {
                var emus = (PluginHelper.DataManager?.GetAllEmulators() ?? Array.Empty<IEmulator>()).Where(e => e != null && IsYmir(Safe(() => e.ApplicationPath))).ToList();
                if (emus.Count == 0) { link.Problem = "No Ymir in LaunchBox."; return link; }
                var platform = Safe(() => game?.Platform) ?? KronosPlugin.Platform;
                var own = Safe(() => game?.EmulatorId);
                int Rank(IEmulator e)
                {
                    if (Safe(() => e.Id) == own) return 0;
                    var row = (Safe(() => e.GetAllEmulatorPlatforms()) ?? Array.Empty<IEmulatorPlatform>())
                        .FirstOrDefault(r => string.Equals(Safe(() => r.Platform), platform, StringComparison.OrdinalIgnoreCase));
                    return row == null ? 3 : Safe(() => row.IsDefault) ? 1 : 2;
                }
                var ymir = emus.OrderBy(Rank).First();
                link.RetroArchExe = KronosPlugin.ResolveFullPathForUi(Safe(() => ymir.ApplicationPath));
                if (string.IsNullOrEmpty(link.RetroArchExe) || !File.Exists(link.RetroArchExe)) { link.Problem = "Ymir's exe is not there: " + link.RetroArchExe; return link; }
                var rom = KronosPlugin.ResolveFullPathForUi(Safe(() => game?.ApplicationPath));
                link.SaveFile = Path.ChangeExtension(KronosPlugin.RamPath(link.RetroArchExe, platform, rom), ".bin");
                link.CartFile = KronosPlugin.CartPath(link.RetroArchExe, platform, rom);
            }
            catch (Exception ex) { link.Problem = "Ymir could not be looked at: " + ex.Message; Log.Warn("ymir link", ex); }
            return link;
        }

        private static T Safe<T>(Func<T> get) { try { return get(); } catch { return default; } }
    }
}
