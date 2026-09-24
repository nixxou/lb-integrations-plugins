// The user's NAND dumps: finding them, knowing what region each one is, and choosing between two
// of the same region without ever changing our mind.
//
// THIS IS THE SAME QUESTION FOR EVERY EMULATOR that runs a DSi, which is why it lives here rather
// than beside one plugin's BIOS table. A dump is a 240 MB file with a "DSi eMMC CID/CPU" footer -
// no$gba's convention, which melonDS adopted - and its region is a byte inside it. Neither fact
// belongs to an emulator.
//
// OPENING ONE COSTS ABOUT 150 ms: a decryption plus a FAT mount. So the answers are remembered in
// dsi\nands.txt, keyed on size and write time, and forgotten when either changes. "This is not a
// NAND" is cached too - but ONLY when the image was genuinely opened and read, because a failure to
// open says something about the machine and nothing about the file.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace LbIntegrations.Dsi
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

    internal static class DsiDumps
    {
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
            { ".lock", ".bak", DsiBase.RecipeSuffix, DsiBase.RecordSuffix };


        /// <summary>Every NAND dump in the folder, with the region each came from.
        ///
        /// <paramref name="bios7Path"/> is needed to decrypt them; without it nothing can be read and
        /// the answer is an empty list rather than a guess.</summary>
        public static List<NandDump> Nands(DsiHost layout, string bios7Path)
        {
            var found = new List<NandDump>();
            try
            {
                // Nothing can be read without it, and the index must not learn anything from a run
                // that could not look inside a single file. Said once, not once per candidate.
                if (string.IsNullOrWhiteSpace(bios7Path) || !File.Exists(bios7Path))
                {
                    DsiLog.Verbose("no DSi ARM7 BIOS, so no NAND dump can be identified");
                    return found;
                }

                var known = ReadIndex(layout);
                var fresh = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                bool changed = false;

                // EVERY file, not just *.bin. A NAND dump has no agreed extension - .bin, .img,
                // .nand and no extension at all are all in circulation - and the size range below
                // rejects the rest of the folder in one stat() apiece.
                var candidates = new List<string>();
                foreach (var dir in layout.Dumps())
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
                        DsiLog.Verbose("could not read a region out of " + Path.GetFileName(path));

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
                    DsiLog.Info(Path.GetFileName(path) + " is a " + DsiRegions.Name(region.Value) + " NAND");
                }

                if (changed || fresh.Count != known.Count) WriteIndex(layout, fresh);
            }
            catch (Exception ex) { DsiLog.Verbose("could not look at the NAND dumps - " + ex.Message); }
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

        private static NandDump Dump(DsiHost layout, string path, DsiRegion region)
            => new NandDump
            {
                Path = path,
                Region = region,
                HasConsole = DsiBase.ConsoleFor(layout, path) != null,
            };

        /// <summary>The NAND to run this title on: the first of the regions it accepts that the user
        /// actually has. Null with a reason when there is none.</summary>
        public static NandDump NandFor(DsiHost layout, IEnumerable<DsiRegion> regions,
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
                    DsiLog.Info(matching.Count + " " + DsiRegions.Name(region) + " NAND dumps; using "
                             + Path.GetFileName(chosen.Path)
                             + ". Every save is tied to the console it was made on, so this choice "
                             + "must not drift between launches - a dump that already has a console "
                             + "wins, and the name breaks a tie.");

                return chosen;
            }

            why = dumps.Count == 0
                ? "there is no DSi NAND dump in " + DsiWorkspace.DsiDir(layout)
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
            foreach (var dump in dumps) names.Add(DsiRegions.Name(dump.Region));
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
                using var session = DsiNand.Open(nandPath, bios7Path, out var error);
                if (session == null)
                {
                    DsiLog.Verbose("could not open " + Path.GetFileName(nandPath) + " - " + error);
                    return null;
                }
                opened = true;

                scratch = Path.Combine(Path.GetTempPath(), "lbip-hwinfo-" + Guid.NewGuid().ToString("N"));
                if (!session.ExportFile(DsiRegions.HardwareInfoInNand, scratch, out var whyNot))
                {
                    DsiLog.Verbose("no " + DsiRegions.HardwareInfoInNand + " in "
                                + Path.GetFileName(nandPath) + " - " + whyNot);
                    return null;
                }
                return DsiRegions.RegionIn(File.ReadAllBytes(scratch));
            }
            catch (Exception ex) { DsiLog.Verbose("could not read a NAND's region - " + ex.Message); return null; }
            finally { try { if (scratch != null && File.Exists(scratch)) File.Delete(scratch); } catch { } }
        }

        // ── the remembered answers ───────────────────────────────────────────

        private static string IndexPath(DsiHost layout)
        {
            var dir = DsiWorkspace.DsiDir(layout);      // ours, not the user's folder
            return dir == null ? null : Path.Combine(dir, IndexName);
        }

        private static Dictionary<string, string> ReadIndex(DsiHost layout)
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

        private static void WriteIndex(DsiHost layout, Dictionary<string, string> entries)
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
            catch (Exception ex) { DsiLog.Verbose("could not write the NAND index - " + ex.Message); }
        }
    }
}
