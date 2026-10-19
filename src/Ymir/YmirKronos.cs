// A game's backup RAM carried between Ymir and Kronos, from the game's window - beside RetroArch's (YmirRetroArch), the same
// copy (YmirRetroArch.Copy: the file replaced kept, dated; refused while either emulator runs).
//
// THE SAME BYTES (docs\kronos.md §4, measured 19/10): Ymir's internal backup RAM (32 KiB, "BackUpRam Format") was used by Kronos
// and left byte-identical; both plugins' cartridges are Beetle's 512 KiB .bcr. The clock is not carried: Ymir keeps its own
// (state\smpc-<region>.bin, per region, not per game), Kronos reads the PC's time at each start.
//
// WHERE KRONOS KEEPS THE GAME'S (the Kronos plugin's KronosSaves.RamPath / CartPath): <Kronos>\Saves\<platform>\<game>.bkr and
// .bcr - the same folder and the same <game> as Ymir's own (the file without its extension and its "(Disc N)", the .m3u's name
// for a playlist; invalid characters made _), so Ymir's rule finds it, with Kronos's extension. A Kronos that never ran this
// game through the plugin has no file there: "none yet", and an export makes it.
//
// WHICH KRONOS: the one in LaunchBox (kronos.exe) whose row for the game's platform is the default, else one with a row for it,
// else the first - the game's own emulator first if it is a Kronos.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Unbroken.LaunchBox.Plugins;
using Unbroken.LaunchBox.Plugins.Data;

namespace LbIntegrations.Ymir
{
    internal static class YmirKronos
    {
        public const string ExecutableName = "kronos.exe";

        private static bool IsKronos(string path)
        {
            try { return string.Equals(Path.GetFileName((path ?? "").Trim().Trim('"')), ExecutableName, StringComparison.OrdinalIgnoreCase); }
            catch { return false; }
        }

        /// <summary>The Kronos of the game's platform and the files it keeps the game's RAM and cartridge in. Never throws.</summary>
        public static YmirRaLink Find(IGame game)
        {
            var link = new YmirRaLink { Name = "Kronos", CoreName = "Kronos" };
            try
            {
                var emus = (PluginHelper.DataManager?.GetAllEmulators() ?? Array.Empty<IEmulator>()).Where(e => e != null && IsKronos(Safe(() => e.ApplicationPath))).ToList();
                if (emus.Count == 0) { link.Problem = "No Kronos in LaunchBox."; return link; }
                var platform = Safe(() => game?.Platform) ?? YmirPlugin.Platform;
                var own = Safe(() => game?.EmulatorId);
                int Rank(IEmulator e)
                {
                    if (Safe(() => e.Id) == own) return 0;
                    var row = (Safe(() => e.GetAllEmulatorPlatforms()) ?? Array.Empty<IEmulatorPlatform>())
                        .FirstOrDefault(r => string.Equals(Safe(() => r.Platform), platform, StringComparison.OrdinalIgnoreCase));
                    return row == null ? 3 : Safe(() => row.IsDefault) ? 1 : 2;
                }
                var kronos = emus.OrderBy(Rank).First();
                link.RetroArchExe = YmirPlugin.ResolveFullPathForUi(Safe(() => kronos.ApplicationPath));
                if (string.IsNullOrEmpty(link.RetroArchExe) || !File.Exists(link.RetroArchExe)) { link.Problem = "Kronos's exe is not there: " + link.RetroArchExe; return link; }
                var rom = YmirPlugin.ResolveFullPathForUi(Safe(() => game?.ApplicationPath));
                link.SaveFile = Path.ChangeExtension(YmirPlugin.RamPath(link.RetroArchExe, platform, rom), ".bkr");
                link.CartFile = YmirPlugin.CartPath(link.RetroArchExe, platform, rom);
            }
            catch (Exception ex) { link.Problem = "Kronos could not be looked at: " + ex.Message; Log.Warn("kronos link", ex); }
            return link;
        }

        private static T Safe<T>(Func<T> get) { try { return get(); } catch { return default; } }
    }
}
