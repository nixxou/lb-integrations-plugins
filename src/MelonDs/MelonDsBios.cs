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
// MelonDsRegion for how, and why that offset is known rather than guessed.
//
// Opening a NAND costs about 150 ms, and six of them a second, which is not a price a launch should
// pay to learn something that does not change. So the answers are written down in dsi\nands.txt and
// each line is invalidated by its file's own size and write time.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace LbIntegrations.MelonDs
{
    /// <summary>One NAND dump the user has, and the region it came from.</summary>
    internal sealed class NandDump
    {
        public string Path;
        public DsiRegion Region;
    }

    internal static class MelonDsBios
    {
        public const string DirName = "bios";

        /// <summary>The names this plugin suggests. melonDS itself takes whatever path it is given,
        /// so these matter only for telling somebody what to look for.</summary>
        public const string DsiBios7 = "dsi_bios7.bin";
        public const string DsiBios9 = "dsi_bios9.bin";
        public const string DsiFirmware = "dsi_firmware.bin";
        public const string DsBios7 = "bios7.bin";
        public const string DsBios9 = "bios9.bin";
        public const string DsFirmware = "firmware.bin";

        /// <summary>What a DSiWare launch needs, beside a NAND.</summary>
        public static readonly string[] DsiFiles = { DsiBios7, DsiBios9, DsiFirmware };

        /// <summary>What a DS launch needs when Emu.ExternalBIOSEnable is on, and nothing when it is
        /// off. This plugin does not touch that setting - it is the user's answer about whether they
        /// want their own console's BIOS or melonDS's replacement.</summary>
        public static readonly string[] DsFiles = { DsBios7, DsBios9, DsFirmware };

        private const string NoteName = "WHICH-FILES-GO-HERE.txt";
        private const string IndexName = "nands.txt";

        /// <summary>A NAND dump is 240 MB. Anything far from that is not one, and opening it to find
        /// out would cost a second for nothing.</summary>
        private const long NandBytes = 251658304L;
        private const long NandSlack = 4L * 1024 * 1024;

        public static string Dir(MelonDsLayout layout)
            => layout?.InstallDir == null ? null : Path.Combine(layout.InstallDir, DirName);

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
                    "This folder is for the files melonDS needs and cannot generate.",
                    "",
                    "NOTHING HERE IS NEEDED FOR ORDINARY DS GAMES. melonDS has a built-in BIOS and",
                    "generates a firmware, so a DS cartridge runs with this folder empty. You only",
                    "need the DS files below if you turn on Config > Emu settings > external BIOS,",
                    "which gives you your own console's boot animation and settings instead.",
                    "",
                    "    " + DsBios7 + "        DS ARM7 BIOS",
                    "    " + DsBios9 + "        DS ARM9 BIOS",
                    "    " + DsFirmware + "     DS firmware",
                    "",
                    "FOR DSiWARE, ALL FOUR OF THESE ARE REQUIRED:",
                    "",
                    "    " + DsiBios7 + "    DSi ARM7 BIOS",
                    "    " + DsiBios9 + "    DSi ARM9 BIOS",
                    "    " + DsiFirmware + " DSi firmware",
                    "    a DSi NAND dump of the right REGION",
                    "",
                    "A DSi NAND is region locked: the system menu that launches an installed title is",
                    "built for one region and refuses titles from another. So you need the NAND that",
                    "matches the game - a Japanese title wants a Japanese NAND.",
                    "",
                    "PUT THE NAND DUMPS IN THIS FOLDER UNDER ANY NAME YOU LIKE. Their region is read",
                    "from inside them, not from the file name, so DSi_Nand_USA_1.4.5.bin works exactly",
                    "as well as anything else. Drop in as many regions as you own; the right one is",
                    "picked per game.",
                    "",
                    "A NAND cannot be generated or downloaded from us: it is encrypted with data",
                    "unique to the console it came from.",
                    "",
                    "This file is only a note. You can delete it.",
                }) + "\r\n");
                Log.Info("made " + dir + " - it holds the BIOS, firmware and NAND dumps melonDS needs");
            }
            catch (Exception ex) { Log.Verbose("could not prepare the bios folder - " + ex.Message); }
        }

        /// <summary>One file by name, case-insensitively - a dump named DSI_bios7.bin is the file
        /// that was asked for, and refusing it over a capital letter would be theatre.</summary>
        public static string Find(MelonDsLayout layout, string fileName)
        {
            try
            {
                var dir = Dir(layout);
                if (dir == null || !Directory.Exists(dir)) return null;
                foreach (var path in Directory.EnumerateFiles(dir))
                    if (string.Equals(Path.GetFileName(path), fileName, StringComparison.OrdinalIgnoreCase))
                        return path;
                return null;
            }
            catch { return null; }
        }

        /// <summary>Which of the files a DSiWare launch needs are not in the folder.</summary>
        public static List<string> MissingDsiFiles(MelonDsLayout layout)
        {
            var missing = new List<string>();
            foreach (var name in DsiFiles) if (Find(layout, name) == null) missing.Add(name);
            return missing;
        }

        /// <summary>Every NAND dump in the folder, with the region each came from.
        ///
        /// <paramref name="bios7Path"/> is needed to decrypt them; without it nothing can be read and
        /// the answer is an empty list rather than a guess.</summary>
        public static List<NandDump> Nands(MelonDsLayout layout, string bios7Path)
        {
            var found = new List<NandDump>();
            try
            {
                var dir = Dir(layout);
                if (dir == null || !Directory.Exists(dir)) return found;

                var known = ReadIndex(layout);
                var fresh = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                bool changed = false;

                foreach (var path in Directory.EnumerateFiles(dir, "*.bin"))
                {
                    long length;
                    DateTime written;
                    try
                    {
                        var info = new FileInfo(path);
                        length = info.Length;
                        written = info.LastWriteTimeUtc;
                    }
                    catch { continue; }

                    if (Math.Abs(length - NandBytes) > NandSlack) continue;   // not a NAND, not opened

                    var stamp = length.ToString(CultureInfo.InvariantCulture) + "\t"
                              + written.Ticks.ToString(CultureInfo.InvariantCulture);

                    if (known.TryGetValue(path, out var remembered)
                        && remembered.StartsWith(stamp + "\t", StringComparison.Ordinal))
                    {
                        var text = remembered.Substring(stamp.Length + 1);
                        if (Enum.TryParse<DsiRegion>(text, out var cached))
                        {
                            found.Add(new NandDump { Path = path, Region = cached });
                            fresh[path] = remembered;
                            continue;
                        }
                    }

                    var region = ReadRegion(path, bios7Path);
                    changed = true;
                    if (region == null)
                    {
                        Log.Verbose("could not read a region out of " + Path.GetFileName(path));
                        continue;
                    }
                    found.Add(new NandDump { Path = path, Region = region.Value });
                    fresh[path] = stamp + "\t" + region.Value;
                    Log.Info(Path.GetFileName(path) + " is a " + MelonDsRegion.Name(region.Value) + " NAND");
                }

                if (changed || fresh.Count != known.Count) WriteIndex(layout, fresh);
            }
            catch (Exception ex) { Log.Verbose("could not look at the NAND dumps - " + ex.Message); }
            return found;
        }

        /// <summary>The NAND to run this title on: the first of the regions it accepts that the user
        /// actually has. Null with a reason when there is none.</summary>
        public static NandDump NandFor(MelonDsLayout layout, IEnumerable<DsiRegion> regions,
                                       string bios7Path, out string why)
        {
            why = null;
            var dumps = Nands(layout, bios7Path);
            foreach (var region in regions ?? new List<DsiRegion>())
                foreach (var dump in dumps)
                    if (dump.Region == region) return dump;

            why = dumps.Count == 0
                ? "there is no DSi NAND dump in " + Dir(layout)
                : "the only NAND dump(s) there are " + Describe(dumps);
            return null;
        }

        private static string Describe(List<NandDump> dumps)
        {
            var names = new List<string>();
            foreach (var dump in dumps) names.Add(MelonDsRegion.Name(dump.Region));
            return string.Join(", ", names);
        }

        /// <summary>Open a candidate and ask it what it is. One open, one file read out of it.</summary>
        private static DsiRegion? ReadRegion(string nandPath, string bios7Path)
        {
            if (string.IsNullOrWhiteSpace(bios7Path) || !File.Exists(bios7Path)) return null;

            string scratch = null;
            try
            {
                using var session = MelonDsNand.Open(nandPath, bios7Path, out var error);
                if (session == null)
                {
                    Log.Verbose("could not open " + Path.GetFileName(nandPath) + " - " + error);
                    return null;
                }

                scratch = Path.Combine(Path.GetTempPath(), "lbip-hwinfo-" + Guid.NewGuid().ToString("N"));
                if (!session.ExportFile(MelonDsRegion.HardwareInfoInNand, scratch, out var whyNot))
                {
                    Log.Verbose("no " + MelonDsRegion.HardwareInfoInNand + " in "
                                + Path.GetFileName(nandPath) + " - " + whyNot);
                    return null;
                }
                return MelonDsRegion.RegionIn(File.ReadAllBytes(scratch));
            }
            catch (Exception ex) { Log.Verbose("could not read a NAND's region - " + ex.Message); return null; }
            finally { try { if (scratch != null && File.Exists(scratch)) File.Delete(scratch); } catch { } }
        }

        // ── the remembered answers ───────────────────────────────────────────

        private static string IndexPath(MelonDsLayout layout)
        {
            var dir = MelonDsDsi.DsiDir(layout);      // ours, not the user's folder
            return dir == null ? null : Path.Combine(dir, IndexName);
        }

        private static Dictionary<string, string> ReadIndex(MelonDsLayout layout)
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                var path = IndexPath(layout);
                if (path == null || !File.Exists(path)) return map;
                foreach (var line in File.ReadAllLines(path))
                {
                    var parts = line.Split(new[] { '\t' }, 2);
                    if (parts.Length == 2) map[parts[0]] = parts[1];
                }
            }
            catch { }
            return map;
        }

        private static void WriteIndex(MelonDsLayout layout, Dictionary<string, string> entries)
        {
            try
            {
                var path = IndexPath(layout);
                if (path == null) return;
                Directory.CreateDirectory(Path.GetDirectoryName(path));

                var lines = new List<string>();
                foreach (var pair in entries) lines.Add(pair.Key + "\t" + pair.Value);
                lines.Sort(StringComparer.OrdinalIgnoreCase);
                File.WriteAllLines(path, lines);
            }
            catch (Exception ex) { Log.Verbose("could not write the NAND index - " + ex.Message); }
        }
    }
}
