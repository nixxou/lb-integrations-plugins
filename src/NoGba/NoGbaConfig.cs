// What this plugin sets in NO$GBA.INI, and why each one earns its place.
//
// THE WHOLE PLUGIN EXISTS FOR ONE OF THESE LINES. no$gba writes cartridge saves COMPRESSED, in a
// format of its own, and it does so by default. Measured on Mario Kart DS:
//
//     default          25,487 bytes   "NocashGbaBackupMediaSavDataFile\x1A"
//     Raw             262,144 bytes   "MKDSSV10…"  - 256 KB exactly, the game's own signature
//
// The first is readable by no$gba and by nothing else: not RetroArch, not melonDS, not DeSmuME, not
// a save editor, not RomM, not Argosy. The second is the plain battery image everything speaks.
// The setting has existed for years and is three menus deep. Anybody who has played a DS game in
// no$gba and then tried to move the save somewhere else has met this and had no idea why.
//
// SET AT INSTALL AND CHECKED AT EVERY LAUNCH, and that is not belt-and-braces. no$gba never rewrites
// its INI on exit - measured - so a value written once would normally stay. But Options > Save
// Options rewrites the WHOLE file from the running configuration, so a user who opens that dialog
// for any unrelated reason silently reverts everything this plugin set. Checking costs one file
// read per launch and writes nothing when nothing differs.
//
// WHAT IS DELIBERATELY NOT SET. The key mapping: unlike melonDS, which ships with nothing bound,
// no$gba has working defaults out of the box (they are in the INI as `KEYB_1 == 101E1819…`, PC scan
// codes). And the boot entry point, which defaults to "Start Cartridge directly" - exactly what a
// frontend wants. Neither needs an opinion from us.

using System;
using System.Collections.Generic;
using LbIntegrations.Dsi;

namespace LbIntegrations.NoGba
{
    internal static class NoGbaConfig
    {
        /// <summary>Beside the log, like every other switch in this repository. Creating it stops
        /// this plugin touching NO$GBA.INI at all.</summary>
        private const string KillSwitch = "no-nogba-config";

        // ── the keys, spelled as no$gba spells them ─────────────────────────

        /// <summary>The one that matters. Its accepted values are the labels of the drop-down in
        /// Options > Emulation Setup, and anything else is ignored without a word.</summary>
        public const string SaveFormatKey = "SAV/SNA File Format";

        /// <summary>"Raw" - a plain battery image. The other two values no$gba offers are
        /// "Compressed" (the default) and "Uncompressed", both of which wrap the data in the
        /// NocashGbaBackup container.</summary>
        public const string SaveFormatRaw = "Raw";

        /// <summary>Point the emulator at a configuration that lets its saves travel.
        ///
        /// Answers what it changed, for the log, or null when nothing needed changing.</summary>
        public static string Apply(NoGbaLayout layout)
        {
            try
            {
                if (layout?.IniFile == null) return null;
                if (Log.Disabled(KillSwitch))
                {
                    Log.Verbose("the " + KillSwitch + " marker is there; leaving NO$GBA.INI alone");
                    return null;
                }

                var wanted = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    [SaveFormatKey] = SaveFormatRaw,
                };

                var current = NoGbaIni.Read(layout.IniFile, SaveFormatKey);
                if (current.TryGetValue(SaveFormatKey, out var have)
                    && string.Equals(have, SaveFormatRaw, StringComparison.OrdinalIgnoreCase))
                    return null;                       // already right; write nothing

                var error = NoGbaIni.Write(layout.IniFile, wanted);
                if (error != null)
                {
                    Log.Warn("could not set the save format: " + error);
                    return null;
                }

                var was = have == null ? "unset (so: compressed)" : have;
                Log.Info("set " + SaveFormatKey + " to " + SaveFormatRaw + " - it was " + was
                         + ". no$gba writes compressed saves by default, in a format only it reads; "
                         + "raw is the plain battery image every other emulator and save tool speaks.");
                return SaveFormatKey;
            }
            catch (Exception ex) { Log.Warn("could not configure no$gba", ex); return null; }
        }
    }
}
