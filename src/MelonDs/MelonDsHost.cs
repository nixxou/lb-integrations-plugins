// melonDS, described in the four terms the shared DSi engine asks for.
//
// The engine in src\Shared.Dsi is compiled into this plugin and into the no$gba one. It works on the
// NAND image rather than on the emulator, so almost nothing about melonDS reaches it - see
// DsiHost for the whole of what does. This file is that description, and it is short on purpose: if
// it grows, something emulator-specific has leaked into the engine.
//
// THE CONVERSION IS IMPLICIT, and that is not laziness. Every call site in this plugin already holds
// a MelonDsLayout and passes it along; making the layout convert means the engine could be extracted
// without rewriting two hundred call sites, which is exactly the kind of churn that hides a real
// mistake among five hundred harmless ones.

using System;
using System.Collections.Generic;
using System.IO;
using LbIntegrations.Dsi;

namespace LbIntegrations.MelonDs
{
    internal static class MelonDsHost
    {
        /// <summary>Tell the shared engine who it is working for. Called once, from the plugin's
        /// constructor, before anything can reach the engine.</summary>
        public static void Announce()
        {
            DsiLog.Use(Log.Info, Log.Warn, Log.Verbose, Log.Disabled);

            // WHAT "THE EMULATOR IS RUNNING" MEANS HERE. The engine refuses to open a NAND, to walk
            // one, or to build over one while the emulator still has it - and the only thing it
            // cannot work out for itself is which process that is.
            DsiNand.EmulatorProcessPrefix = "melonDS";
        }

        /// <summary>The four things that differ, for this installation.</summary>
        public static DsiHost For(MelonDsLayout layout)
        {
            if (layout?.InstallDir == null) return new DsiHost();

            return new DsiHost
            {
                InstallDir = layout.InstallDir,

                // OURS, because melonDS can be told where its NAND is: DSi.NANDPath in the TOML
                // takes any path, so the working image lives in the folder this plugin owns. no$gba
                // has no such setting and reads a fixed name beside its executable instead - which
                // is the whole of the difference between the two hosts.
                WorkImagePath = Path.Combine(layout.InstallDir, DsiWorkspace.DirName,
                                             DsiWorkspace.WorkName),

                DumpFolders = () => MelonDsBios.SearchFolders(layout),
                EmulatorRunning = DsiNand.EmulatorRunning,
            };
        }
    }
}
