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
    /// <summary>One NAND dump the user has, the region it came from, and whether a console has
    /// been configured from it. Carried here rather than asked for again later: whoever picked the
    /// dump is who has to act on it.</summary>
    internal sealed class NandDump
    {
        public string Path;
        public DsiRegion Region;

        /// <summary>dsi\<this dump's name> exists. One File.Exists, taken during the sweep -
        /// it replaces a three-state machine read off .lock and .bak files beside the user's own
        /// dumps, which existed only because the dump used to be configured in place.</summary>
        public bool HasConsole;
    }

    internal static class MelonDsBios
    {
        /// <summary>The folder these files are DECLARED in, and the one an install creates:
        /// RetroArch's system folder, beside this emulator.
        ///
        /// WHY SOMEBODY ELSE'S FOLDER. Nearly everybody running LaunchBox already has RetroArch, and
        /// anybody who has ever set up a DS core there already has these seven files sitting in it.
        /// Asking for a second copy, in a second folder, under a second set of names, would be
        /// inventing work for the sake of owning a directory.
        ///
        /// It is created at install time when it is not there, so declaring it is safe even for
        /// somebody who has no RetroArch at all - they get an empty folder with a note in it, which
        /// is exactly what the old private one gave them.</summary>
        public const string DirName = ".." + SEP + "RetroArch" + SEP + "system";

        private const string SEP = "\\";

        /// <summary>Where this plugin used to ask for them. Still searched, so an installation set
        /// up before the move keeps working without anybody touching it.</summary>
        public const string LegacyDirName = "bios";

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
        private const string IndexName = "nands.txt";

        /// <summary>A NAND dump is 240 MB - 251,658,304 bytes for the stock 1.4.5 images, a little
        /// more for one dumped with its 64-byte-per-sector spare area or with a footer stuck on the
        /// end. So: a range, not a size. Anything outside it is not a NAND and is never opened -
        /// opening one costs a decrypt and a FAT mount, which is a second spent to learn nothing.</summary>
        private const long NandLeast = 220L * 1024 * 1024;
        private const long NandMost = 260L * 1024 * 1024;

        /// <summary>Suffixes that are never a NAND, whatever their size.
        ///
        /// The first two are leftovers from an arrangement that no longer exists: this plugin used
        /// to configure the user's dump in place and keep a pristine copy as .lock, with a .bak
        /// while it happened. Both were 240 MB and both sat squarely inside the size range above, so
        /// both had to be named here. Nothing writes them any more - and they are kept in the list
        /// precisely because somebody upgrading still has them on disk.</summary>
        private static readonly string[] NotNands =
            { ".lock", ".bak", MelonDsBase.RecipeSuffix, MelonDsBase.RecordSuffix };

        /// <summary>The folder to put things in and to name in a message. Absolute, and normalised
        /// so a message says G:\...\RetroArch\system rather than G:\...\melonDS\..\RetroArch\system.</summary>
        public static string Dir(MelonDsLayout layout)
        {
            try
            {
                if (layout?.InstallDir == null) return null;
                return Path.GetFullPath(Path.Combine(layout.InstallDir, DirName));
            }
            catch { return null; }
        }

        /// <summary>The folder this plugin used to make, if it is still there.</summary>
        public static string LegacyDir(MelonDsLayout layout)
            => layout?.InstallDir == null ? null : Path.Combine(layout.InstallDir, LegacyDirName);

        /// <summary>Everywhere a file may be, best first.
        ///
        /// OURS IS FIRST AND IS THE ONE DECLARED, because it is the one that always exists - this
        /// plugin makes it at install time. RetroArch's system folder is looked in as well when it
        /// is there, which costs nothing and saves somebody a second copy of seven files under seven
        /// other names: RetroArch's melonDS cores declare exactly these, and anybody who set that up
        /// already has them.
        ///
        /// It is looked in, NOT declared, and that distinction is deliberate. The declaration is a
        /// path relative to the emulator, and whether a "..\RetroArch\system" resolves in that field
        /// is something this plugin cannot test without the host's own dependency window - so it is
        /// not bet on. Nor should a melonDS depend on RetroArch being installed at all.</summary>
        public static IEnumerable<string> SearchFolders(MelonDsLayout layout)
        {
            var shared = Dir(layout);
            if (shared != null) yield return shared;

            // The folder this plugin used to ask for. Searched second so the declared one wins, and
            // searched at all so nobody has to move files because we changed our mind.
            var old = LegacyDir(layout);
            if (old != null) yield return old;
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
                    "THIS IS RETROARCH'S SYSTEM FOLDER, on purpose: if you have ever set up a DS core",
                    "there, these files are already here and there is nothing to do. Both naming",
                    "conventions are accepted - RetroArch's dsi_bios7.bin and melonDS's biosdsi7.bin",
                    "are the same file to this plugin.",
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

        /// <summary>One file by name, case-insensitively        /// <summary>One file by name, case-insensitively - a dump named DSI_bios7.bin is the file
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

        /// <summary>The name to SHOW for a file: the one actually sitting in a search folder when
        /// there is one, otherwise RetroArch's.
        ///
        /// RetroArch's is the fallback because the folder is RetroArch's - somebody sent there by a
        /// dependency list should read a name that fits what else is in it. Both conventions are
        /// accepted either way; this only decides what the list says when nothing is there yet.</summary>
        public static string PreferredName(MelonDsLayout layout, string ourName)
        {
            // ONLY THE DECLARED FOLDER IS LOOKED AT. A file sitting in the legacy one still works -
            // it is searched at launch - but naming it here would point the host's own check at a
            // folder that does not hold it, which is the defect this whole arrangement exists to
            // avoid rather than to move around.
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
            return AlsoKnownAs.TryGetValue(ourName, out var others) && others.Length > 0
                ? others[0] : ourName;
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
                // Nothing can be read without it, and the index must not learn anything from a run
                // that could not look inside a single file. Said once, not once per candidate.
                if (string.IsNullOrWhiteSpace(bios7Path) || !File.Exists(bios7Path))
                {
                    Log.Verbose("no DSi ARM7 BIOS, so no NAND dump can be identified");
                    return found;
                }

                var known = ReadIndex(layout);
                var fresh = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                bool changed = false;

                // EVERY file, not just *.bin. A NAND dump has no agreed extension - .bin, .img,
                // .nand and no extension at all are all in circulation - and the size range below
                // rejects the rest of the folder in one stat() apiece.
                var candidates = new List<string>();
                foreach (var dir in SearchFolders(layout))
                {
                    if (!Directory.Exists(dir)) continue;
                    foreach (var path in Directory.EnumerateFiles(dir))
                        if (!IsNotANand(path)) candidates.Add(path);
                }

                foreach (var path in candidates)
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

                    if (length < NandLeast || length > NandMost) continue;   // not a NAND, not opened

                    var stamp = length.ToString(CultureInfo.InvariantCulture) + "\t"
                              + written.Ticks.ToString(CultureInfo.InvariantCulture);

                    if (known.TryGetValue(path, out var remembered)
                        && remembered.StartsWith(stamp + "\t", StringComparison.Ordinal))
                    {
                        var text = remembered.Substring(stamp.Length + 1);
                        if (text == NotOne) { fresh[path] = remembered; continue; }
                        if (Enum.TryParse<DsiRegion>(text, out var cached))
                        {
                            found.Add(Dump(layout, path, cached));
                            fresh[path] = remembered;
                            continue;
                        }
                    }

                    var region = ReadRegion(path, bios7Path, out var opened);
                    changed = true;
                    if (region == null)
                    {
                        Log.Verbose("could not read a region out of " + Path.GetFileName(path));

                        // Remembered as NOT a NAND - but ONLY when the image itself was opened and
                        // read, and it was its contents that said no. A failure to open it says
                        // nothing about the file and everything about this machine: no native
                        // library, a bad BIOS, a lock held by something else. Writing that down
                        // would mark a real NAND as junk for as long as nobody touches it.
                        if (opened) fresh[path] = stamp + "\t" + NotOne;
                        continue;
                    }
                    found.Add(Dump(layout, path, region.Value));
                    fresh[path] = stamp + "\t" + region.Value;
                    Log.Info(Path.GetFileName(path) + " is a " + MelonDsRegion.Name(region.Value) + " NAND");
                }

                if (changed || fresh.Count != known.Count) WriteIndex(layout, fresh);
            }
            catch (Exception ex) { Log.Verbose("could not look at the NAND dumps - " + ex.Message); }
            return found;
        }

        /// <summary>A file the first-use flow put beside a NAND, and which must never be taken for
        /// a NAND itself.</summary>
        private static bool IsNotANand(string path)
        {
            foreach (var suffix in NotNands)
                if (path.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        /// <summary>What the index writes for a file in the size range that turned out not to be a
        /// NAND. Not a region name, so it can never be parsed back as one.</summary>
        private const string NotOne = "-";

        private static NandDump Dump(MelonDsLayout layout, string path, DsiRegion region)
            => new NandDump
            {
                Path = path,
                Region = region,
                HasConsole = MelonDsBase.ConsoleFor(layout, path) != null,
            };

        /// <summary>The NAND to run this title on: the first of the regions it accepts that the user
        /// actually has. Null with a reason when there is none.</summary>
        public static NandDump NandFor(MelonDsLayout layout, IEnumerable<DsiRegion> regions,
                                       string bios7Path, out string why)
        {
            why = null;
            var dumps = Nands(layout, bios7Path);
            foreach (var region in regions ?? new List<DsiRegion>())
            {
                var matching = new List<NandDump>();
                foreach (var dump in dumps)
                    if (dump.Region == region) matching.Add(dump);
                if (matching.Count == 0) continue;

                var chosen = Steadiest(matching);
                if (matching.Count > 1)
                    Log.Info(matching.Count + " " + MelonDsRegion.Name(region) + " NAND dumps; using "
                             + Path.GetFileName(chosen.Path)
                             + ". Every save is tied to the console it was made on, so this choice "
                             + "must not drift between launches - a dump that already has a console "
                             + "wins, and the name breaks a tie.");

                return chosen;
            }

            why = dumps.Count == 0
                ? "there is no DSi NAND dump in " + Dir(layout)
                : "the only NAND dump(s) there are " + Describe(dumps);
            return null;
        }

        /// <summary>Which of several dumps of the same region to run on.
        ///
        /// THE CHOICE MUST NOT DRIFT. Every DSiWare save is the difference against one particular
        /// console; pick a different one next launch and the saves are replayed onto a console that
        /// was never theirs. Directory order is not an ordering - it is whatever the filesystem
        /// happens to hand back, and it changes when files are added, renamed or defragmented.
        ///
        /// So: a dump that ALREADY HAS A CONSOLE wins. That is the one somebody actually set up,
        /// which makes it the one the saves came from. Failing that, the name, ordinally -
        /// arbitrary, but the same arbitrary answer every time.</summary>
        public static NandDump Steadiest(List<NandDump> matching)
        {
            NandDump best = null;
            foreach (var dump in matching)
            {
                if (best == null) { best = dump; continue; }

                bool mine = dump.HasConsole, theirs = best.HasConsole;
                if (mine != theirs) { if (mine) best = dump; continue; }

                if (string.Compare(dump.Path, best.Path, StringComparison.OrdinalIgnoreCase) < 0)
                    best = dump;
            }
            return best;
        }

        private static string Describe(List<NandDump> dumps)
        {
            var names = new List<string>();
            foreach (var dump in dumps) names.Add(MelonDsRegion.Name(dump.Region));
            return string.Join(", ", names);
        }

        /// <summary>Open a candidate and ask it what it is. One open, one file read out of it.
        ///
        /// <paramref name="opened"/> separates "this is not a NAND" from "this could not be looked
        /// at", which look identical from the outside and mean opposite things - see the caller.</summary>
        private static DsiRegion? ReadRegion(string nandPath, string bios7Path, out bool opened)
        {
            opened = false;
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
                opened = true;

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
