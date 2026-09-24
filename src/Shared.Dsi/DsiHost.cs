// What the shared DSi engine needs to know about the emulator it is working for, and nothing else.
//
// THE ENGINE TALKS TO THE IMAGE, NOT TO THE EMULATOR. Choosing a dump by region, building a console
// from it, installing a title, taking the difference a session made, packing it as a .dsisave,
// rebuilding a console from a recipe - none of that depends on which emulator will run the result.
// That is why this folder can be compiled into two plugins at once.
//
// FOUR THINGS DIFFER, and they are all here. Everything else the engine needs it already receives as
// a parameter - a bios7 path, a title id, a ROM.
//
// WORKIMAGEPATH IS THE INTERESTING ONE. melonDS is told where its NAND is, through a key in its
// configuration, so the working image lives in our folder: <install>\dsi\work.bin. no$gba has no
// such setting - measured, its whole INI holds fifty-odd keys and not one path - and reads a FIXED
// FILE NAME beside its executable: DSi-1.mmc. So one plugin's working image is in our folder and the
// other's is in the emulator's, and that single property absorbs the difference. Nothing downstream
// notices: the rebuild, the capture, the receipt and the save file are the same either way.

using System;
using System.Collections.Generic;

namespace LbIntegrations.Dsi
{
    internal sealed class DsiHost
    {
        /// <summary>The emulator's installation folder - where our own dsi\ subfolder goes, holding
        /// the configured consoles, the rebuilds, the per-title saves and the caches.</summary>
        public string InstallDir;

        /// <summary>The image a launch builds and the emulator then runs. Scratch: rebuilt from a
        /// console at every launch, and nothing is expected to survive in it past a capture.</summary>
        public string WorkImagePath;

        /// <summary>Where the user keeps NAND dumps, best first. Both plugins happen to read the
        /// same folder today - ..\RetroArch\system - which is what lets two emulators share one set
        /// of dumps while each keeps its own consoles.</summary>
        public Func<IEnumerable<string>> DumpFolders;

        /// <summary>Is the emulator running? The process name differs, and the answer matters twice:
        /// a capture must not walk an image somebody is writing, and a launch must wait for the
        /// previous session to be let go before building over it.</summary>
        public Func<bool> EmulatorRunning;

        public IEnumerable<string> Dumps()
        {
            try { return DumpFolders?.Invoke() ?? Array.Empty<string>(); }
            catch { return Array.Empty<string>(); }
        }

        public bool Running()
        {
            try { return EmulatorRunning != null && EmulatorRunning(); }
            catch { return false; }
        }
    }
}
