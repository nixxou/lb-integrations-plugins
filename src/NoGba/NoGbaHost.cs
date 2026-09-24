// no$gba, described in the four terms the shared DSi engine asks for.
//
// The engine in src\Shared.Dsi is compiled into this plugin and into the melonDS one. It works on
// the NAND image rather than on the emulator, which is why the same four thousand lines serve both -
// and measured on 2026-09-24, they really do: a console built by the melonDS side booted under
// no$gba, our own tool installed a title into it, the DSi menu launched it, and the walk afterwards
// found the SAME set of files melonDS produces, to the menu's own log.
//
// ONE DIFFERENCE MATTERS, AND IT IS HERE. melonDS is told where its NAND is - DSi.NANDPath takes any
// path - so its working image lives in our folder. no$gba has no such setting: measured, its INI
// holds fifty-odd keys and NOT ONE PATH, and it reads a fixed file name beside its own executable,
// DSi-1.mmc (gbatek, DSi SD/MMC Images: "DSi-#.mmc ;eMMC for machine 1..12"). So the working image
// IS that file, and WorkImagePath says so. Nothing downstream notices.
//
// A FIXED NAME CAN ALREADY BE TAKEN, which melonDS never has to worry about: its folder is ours, so
// a path answers "is this mine". Here the path says nothing, and somebody who set a DSi up by hand
// has their own DSi-1.mmc sitting there. The marker beside the working image answers it instead -
// see DsiWorkspace's work.title and work.sum, and NoGbaDsi.ProtectTheirImage.

using System;
using System.IO;
using LbIntegrations.Dsi;

namespace LbIntegrations.NoGba
{
    internal static class NoGbaHost
    {
        /// <summary>The name of the eMMC image no$gba reads, beside its executable. Slot 1 of the
        /// twelve gbatek documents; nothing here needs a second machine.</summary>
        public const string MmcName = "DSi-1.mmc";

        /// <summary>Where a DSi-1.mmc that is NOT ours is put, once, rather than overwritten.</summary>
        public const string TheirsSuffix = ".yours";

        /// <summary>Tell the shared engine who it is working for. Called once, from the plugin's
        /// constructor, before anything can reach the engine.</summary>
        public static void Announce()
        {
            DsiLog.Use(Log.Info, Log.Warn, Log.Verbose, Log.Disabled);

            // The process name, which is the one thing the engine cannot work out for itself. It
            // refuses to open, walk or build over a NAND while the emulator still has it.
            DsiNand.EmulatorProcessPrefix = "NO$GBA";
        }

        /// <summary>The four things that differ, for this installation.</summary>
        public static DsiHost For(NoGbaLayout layout)
        {
            if (layout?.InstallDir == null) return new DsiHost();

            return new DsiHost
            {
                InstallDir = layout.InstallDir,
                WorkImagePath = Path.Combine(layout.InstallDir, MmcName),
                DumpFolders = () => NoGbaBios.SearchFolders(layout),
                EmulatorRunning = DsiNand.EmulatorRunning,
            };
        }
    }
}
