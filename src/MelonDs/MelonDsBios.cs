// The folder the user drops BIOS, firmware and NAND dumps into, and what this plugin finds in it.
//
//     <install>\bios\        theirs - BIOS, firmware, and one NAND per region they own
//     <install>\dsi\         ours - the working image and the per-title state
//
// WHAT IS ACTUALLY REQUIRED, read out of melonDS rather than out of a wiki
// (EmuInstance::verifySetup:633-665 and loadFirmware:1012-1050):
//
//   a DS game, Emu.ExternalBIOSEnable false    nothing at all - FreeBIOS and a generated firmware
//   a DS game, Emu.ExternalBIOSEnable true     bios7.bin, bios9.bin, firmware.bin
//   a DSiWARE title                            dsi_bios7.bin, dsi_bios9.bin, dsi_firmware.bin,
//                                              and a NAND OF THE RIGHT REGION
//
// THE DSi FIRMWARE IS REQUIRED WHATEVER THAT SETTING SAYS, and it is a trap worth naming: verifySetup
// only checks it when ExternalBIOSEnable is on, so turning that off looks like it removes the
// requirement. It does not. loadFirmware's built-in branch for DSi mode is an empty `// TODO` that
// falls straight through to opening DSi.FirmwarePath anyway (:1016-1019). Trusting the verify step
// there would have produced a plugin that says a file is unnecessary and an emulator that fails
// without it.
//
// A NAND IS FOUND BY WHAT IS INSIDE IT, NEVER BY ITS NAME. The dumps in circulation are named after
// their firmware version - DSi_Nand_USA_1.4.5.bin - and there is no reason to make anyone rename
// one, nor to believe a name somebody else typed. Each candidate is opened and asked; see
// DsiRegions for how, and why that offset is known rather than guessed.
//
// Opening a NAND costs about 150 ms, and six of them a second, which is not a price a launch should
// pay to learn something that does not change. So the answers are written down in dsi\nands.txt and
// each line is invalidated by its file's own size and write time.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using LbIntegrations.Dsi;

namespace LbIntegrations.MelonDs
{

    internal static class MelonDsBios
    {
        /// <summary>The folder these files are DECLARED in, made at install, and the ONLY one read: melonDS's own bios\
        /// (Mehdi, 04/10 - it was RetroArch's system folder until then). The user's originals and nothing else: BIOS,
        /// firmware and NAND dumps as dumped, never written by the pack - what is derived from them lives in dsi\ and
        /// lbip-firmware\.
        ///
        /// FILLED AT INSTALL from RetroArch's system folder when it holds them (ImportFromRetroArch), so the BIOS check
        /// finds them at once; an installation from before 04/10 is filled the same way at its first start-up check.
        /// RetroArch's folder is a SOURCE for that copy, never read at launch.</summary>
        public const string DirName = "bios";

        private const string SEP = "\\";

        /// <summary>Where they were asked for until 04/10, relative to this emulator: copied from at install.</summary>
        public const string RetroArchDirName = ".." + SEP + "RetroArch" + SEP + "system";

        /// <summary>The names this plugin asks for. They are the ones melonDS's own community uses,
        /// so somebody who already has these files already has them under these names.
        ///
        /// THEY ARE THE CONTRACT. These are what is declared to LaunchBox, so they are what its
        /// dependency check looks for and what anybody reads before going to find a file. Accepting
        /// a different name would make the declared name and the accepted name two different
        /// things.</summary>
        public const string DsiBios7 = "biosdsi7.bin";
        public const string DsiBios9 = "biosdsi9.bin";
        public const string DsiFirmware = "dsifirmware.bin";
        public const string DsBios7 = "biosnds7.bin";
        public const string DsBios9 = "biosnds9.bin";
        public const string DsFirmware = "dsfirmware.bin";

        /// <summary>What a DSiWare launch needs, beside a NAND.</summary>
        public static readonly string[] DsiFiles = { DsiBios7, DsiBios9, DsiFirmware };

        /// <summary>What a DS launch needs when Emu.ExternalBIOSEnable is on, and nothing when it is
        /// off. This plugin does not touch that setting - it is the user's answer about whether they
        /// want their own console's BIOS or melonDS's replacement.</summary>
        public static readonly string[] DsFiles = { DsBios7, DsBios9, DsFirmware };

        /// <summary>The other name each file is known by: RetroArch's, declared in the melonDS
        /// cores' own .info files - read there rather than remembered, and identical in both
        /// melonds_libretro and melondsds_libretro.
        ///
        /// Two conventions exist for the same seven files and neither is wrong. RetroArch imposes
        /// its own through those .info files; the melonDS standalone community uses the other. So
        /// both are accepted: somebody who has already set up RetroArch has these files, and telling
        /// them to make a second copy under a second name would be inventing work.</summary>
        private static readonly Dictionary<string, string[]> AlsoKnownAs =
            new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
            {
                [DsiBios7] = new[] { "dsi_bios7.bin" },
                [DsiBios9] = new[] { "dsi_bios9.bin" },
                [DsiFirmware] = new[] { "dsi_firmware.bin" },
                [DsBios7] = new[] { "bios7.bin" },
                [DsBios9] = new[] { "bios9.bin" },
                [DsFirmware] = new[] { "firmware.bin" },
            };

        private const string NoteName = "WHICH-FILES-GO-HERE.txt";


        /// <summary>The folder to put things in and to name in a message, absolute.</summary>
        public static string Dir(MelonDsLayout layout)
        {
            try
            {
                if (layout?.InstallDir == null) return null;
                return Path.GetFullPath(Path.Combine(layout.InstallDir, DirName));
            }
            catch { return null; }
        }

        /// <summary>RetroArch's system folder beside this emulator, absolute - a source at install, nothing more.</summary>
        public static string RetroArchDir(MelonDsLayout layout)
        {
            try { return layout?.InstallDir == null ? null : Path.GetFullPath(Path.Combine(layout.InstallDir, RetroArchDirName)); }
            catch { return null; }
        }

        /// <summary>Where a file is looked for: bios\, and only it (Mehdi, 04/10) - the declared folder is the read one.</summary>
        public static IEnumerable<string> SearchFolders(MelonDsLayout layout)
        {
            var dir = Dir(layout);
            if (dir != null) yield return dir;
        }

        /// <summary>What melonDS may want, under the name it is copied as and the others it circulates under.</summary>
        public static IEnumerable<DsiBiosImport.Wanted> Wanted()
        {
            foreach (var name in DsiFiles.Concat(DsFiles))
                yield return new DsiBiosImport.Wanted { Name = name, Aliases = AlsoKnownAs.TryGetValue(name, out var a) ? a : Array.Empty<string>() };
        }

        /// <summary>The user's files copied into bios\ from RetroArch's system folder - the BIOS and firmware melonDS
        /// wants, under any of their names, and every NAND dump - when bios\ does not have them yet. At install, and at the
        /// first start-up check of an installation from before 04/10 (bios\ not there yet). See DsiBiosImport.</summary>
        public static List<string> ImportFromRetroArch(MelonDsLayout layout, Action<string> report = null, Func<bool> cancelled = null)
        {
            var dir = Dir(layout);
            if (dir == null) return new List<string>();
            return DsiBiosImport.Import(dir, new[] { RetroArchDir(layout) }, Wanted(), nands: true, report, cancelled);
        }

        /// <summary>Make the folder and leave a note in it saying what belongs there. Called at the
        /// end of an install, for the same reason PrepareFolder was: a message naming a path that
        /// does not exist is asking somebody to guess at a spelling.</summary>
        public static void Prepare(MelonDsLayout layout)
        {
            try
            {
                var dir = Dir(layout);
                if (dir == null) return;
                Directory.CreateDirectory(dir);

                var note = Path.Combine(dir, NoteName);
                if (File.Exists(note)) return;

                File.WriteAllText(note, string.Join("\r\n", new[]
                {
                    "Files melonDS needs and cannot generate. Drop them here and the plugin points",
                    "melonDS at them; you do not have to configure anything.",
                    "",
                    "YOUR ORIGINALS ONLY: the pack never writes in this folder. It works on copies",
                    "(dsi\\ for the consoles, lbip-firmware\\ for the firmware).",
                    "",
                    "At install, the plugin copies them here from RetroArch's system folder when they",
                    "are there. Both naming conventions are accepted - RetroArch's dsi_bios7.bin and",
                    "melonDS's biosdsi7.bin are the same file to this plugin.",
                    "",
                    "FOR DSiWARE, all four are required:",
                    "",
                    "    " + DsiBios7 + "      DSi ARM7 BIOS",
                    "    " + DsiBios9 + "      DSi ARM9 BIOS",
                    "    " + DsiFirmware + "    DSi firmware",
                    "    a DSi NAND dump of the right REGION",
                    "",
                    "A DSi NAND is region locked: a Japanese game needs a Japanese NAND. Put in as",
                    "many regions as you own, UNDER ANY NAME - the region is read from inside the",
                    "dump, not from the file name. A NAND is a dump of a real console and cannot be",
                    "generated.",
                    "",
                    "FOR DS GAMES, nothing is needed: melonDS has a built-in BIOS and generates a",
                    "firmware. These three give you your own console's boot animation and settings",
                    "instead, and the plugin turns that on by itself once all three are here:",
                    "",
                    "    " + DsBios7 + "      DS ARM7 BIOS",
                    "    " + DsBios9 + "      DS ARM9 BIOS",
                    "    " + DsFirmware + "     DS firmware",
                    "",
                    "This file is only a note. You can delete it.",
                }) + "\r\n");
                Log.Info("made " + dir + " - it holds the BIOS, firmware and NAND dumps melonDS needs");
            }
            catch (Exception ex) { Log.Verbose("could not prepare the bios folder - " + ex.Message); }
        }

        // ── the files melonDS is pointed at ──────────────────────────────────

        /// <summary>THE NAME IS THE CONTRACT for a BIOS or a firmware, and deliberately so.
        ///
        /// A NAND has no canonical name - the dumps in circulation carry a firmware revision - so
        /// there the region is read out of the file itself. A BIOS is the opposite: these six names
        /// are what this plugin DECLARES to LaunchBox, so they are what its BIOS check looks for and
        /// what anybody reads before going to find one. Sniffing content instead would mean the
        /// declared name and the accepted name were different things, which is a worse contract than
        /// a strict one.
        ///
        /// The size melonDS demands is checked, but only to SAY SOMETHING. A file with the right
        /// name is handed over whatever its length: melonDS makes the final decision, it is better
        /// at it, and a plugin that silently refuses a file the user deliberately put there would be
        /// second-guessing them with less information.</summary>
        private static readonly Dictionary<string, long[]> ExpectedSizes =
            new Dictionary<string, long[]>(StringComparer.OrdinalIgnoreCase)
            {
                // EmuInstance.cpp:487-607, melonDS's own checks.
                [DsBios7] = new[] { 0x4000L },
                [DsBios9] = new[] { 0x1000L },
                [DsFirmware] = new[] { 0x20000L, 0x40000L, 0x80000L },
                [DsiBios7] = new[] { 0x10000L },
                [DsiBios9] = new[] { 0x10000L },
                [DsiFirmware] = new[] { 0x20000L },
            };

        /// <summary>The file with this name, and a note when its size is not one melonDS accepts.
        /// The note is for the log; the file is returned either way.</summary>
        public static string FindChecked(MelonDsLayout layout, string fileName, out string doubt)
        {
            doubt = null;
            var path = Find(layout, fileName);
            if (path == null) return null;

            try
            {
                if (!ExpectedSizes.TryGetValue(fileName, out var sizes)) return path;
                long length = new FileInfo(path).Length;
                foreach (var size in sizes) if (length == size) return path;

                var wanted = new List<string>();
                foreach (var size in sizes) wanted.Add(size.ToString(CultureInfo.InvariantCulture));
                doubt = Path.GetFileName(path) + " is " + length.ToString(CultureInfo.InvariantCulture)
                        + " bytes, and melonDS wants " + string.Join(" or ", wanted)
                        + " - it is being used anyway, and melonDS will say if it is wrong";
            }
            catch { }
            return path;
        }

        /// <summary>One file by name, case-insensitively - a dump named DSI_bios7.bin is the file
        /// that was asked for, and refusing it over a capital letter would be theatre.</summary>
        public static string Find(MelonDsLayout layout, string fileName)
        {
            try
            {
                var names = new List<string> { fileName };
                if (AlsoKnownAs.TryGetValue(fileName, out var others)) names.AddRange(others);

                // Folder by folder, and within a folder the declared name before the alias: a user
                // who has both should get the one this plugin told them to make.
                foreach (var dir in SearchFolders(layout))
                {
                    if (!Directory.Exists(dir)) continue;
                    foreach (var name in names)
                        foreach (var path in Directory.EnumerateFiles(dir))
                            if (string.Equals(Path.GetFileName(path), name, StringComparison.OrdinalIgnoreCase))
                                return path;
                }
                return null;
            }
            catch { return null; }
        }

        /// <summary>The name to SHOW for a file: the one actually sitting in bios\ when there is one, otherwise ours
        /// (melonDS's own convention, since the folder is melonDS's - 04/10). Both conventions are accepted either way;
        /// this only decides what the list says when nothing is there yet.</summary>
        public static string PreferredName(MelonDsLayout layout, string ourName)
        {
            var here = Dir(layout);
            if (here != null && Directory.Exists(here))
            {
                var names = new List<string> { ourName };
                if (AlsoKnownAs.TryGetValue(ourName, out var aliases)) names.AddRange(aliases);
                foreach (var name in names)
                    foreach (var path in Directory.EnumerateFiles(here))
                        if (string.Equals(Path.GetFileName(path), name, StringComparison.OrdinalIgnoreCase))
                            return name;
            }
            return ourName;
        }

        /// <summary>Which of the files a DSiWare launch needs are not in the folder.</summary>
        public static List<string> MissingDsiFiles(MelonDsLayout layout)
        {
            var missing = new List<string>();
            foreach (var name in DsiFiles) if (Find(layout, name) == null) missing.Add(name);
            return missing;
        }
    }
}
