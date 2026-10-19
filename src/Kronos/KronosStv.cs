// Sega ST-V (Titan), the Saturn's arcade board - Kronos's second batch (Mehdi 19/10: the platform "Sega ST-V", and "Arcade",
// filtered as the Flycast plugin filters Naomi).
//
// WHAT KRONOS RUNS (docs\kronos.md §8): the MAME sets of its own table (ctrl/src/stv.c GameList) - 106 in 2.7.5, all of MAME's
// stv.cpp but wwshin, plus tsuribor. kronos-stv.tsv (tools\kronos-stv\make-sets.py) lists every ST-V set, MAME's and Kronos's,
// with whether 2.7.5 knows it (its name in kronos.exe), MAME's title and parent, and the files Kronos looks for (name and CRC).
//
// HOW KRONOS LOADS ONE - no command-line switch: kronos.ini says Cartridge\Type=11 (the ST-V ROM cartridge), Cartridge\Path a
// FOLDER (and Cartridge\Path\STVRomgame, the same, for its settings window), Cartridge\STVGame the set's name. Kronos reads
// EVERY zip of that folder, matching files by name and CRC against its whole table (stv.c updateGameList), writes
// gamelistv2.save there, and loads the set named (STVInit -> loadGame) - in 2.7.5 only once its settings window has made that
// list: the plugin has it do so at each start (KronosStvStart). Its EEPROM, <set>.nv, and coin counters, <set>.coins, go to that
// folder too (eepromdir = cartpath, YabauseThread::reloadSettings), written when Kronos quits.
//
// SO EACH LAUNCH GETS A FOLDER OF ITS OWN, <kronos>\lbip-stv: the game's zip, its parent's when it has one beside it, and
// stvbios.zip - hard links where they can be (the same volume), copies otherwise - never a whole MAME folder (thousands of
// zips read at each start). The game's .nv and .coins are brought in from <kronos>\Saves\<platform>\ before the launch and
// taken back after it (KronosSession's after); a session cut short (LaunchBox gone) is taken back at the next launch, from
// the folder's lbip-stv.txt. Kronos reads the folder with the ANSI file API (FindFirstFileA): the plugin's folder name is ASCII.
//
// CHECKED BEFORE THE LAUNCH, as Kronos would: every file of the set (its zip, then its parent's), and the BIOS. A set Kronos
// does not know, or a file missing, is refused with what is wrong - on "Arcade" a set of another board too: Kronos would only
// show an empty window.
//
// THE BIOS IS READ FROM A FILE NAMED stvbios.zip ONLY [M 19/10]: stv.c's updateGameList counts a BIOS when processBios has read
// one, and only a zip of that name goes to processBios - the BIOS files a MAME non-merged set carries in its own zip are never
// taken (shienryu.zip alone: an empty game list, "Cannot initialize STV emulation", in 2.7.5 as in 2.7.5b).
//
// SO THE BIOS IS THE PLUGIN'S, KEPT IN <kronos>\bios\stvbios.zip (Mehdi 19/10: a copy, never a link - a ROM manager rewriting
// the MAME folder's file in place would change Kronos's with it). It fills itself at the first ST-V launch: the stvbios.zip
// beside the game copied there, else one made of the BIOS files a MAME non-merged set carries (the game's zip, or its
// parent's) - refused only when none of them has a BIOS file Kronos knows. Each launch's folder then links it.

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;

namespace LbIntegrations.Kronos
{
    internal sealed class KronosStvSet
    {
        public string Name, Title, Parent;
        /// <summary>In Kronos 2.7.5's table (its name in kronos.exe).</summary>
        public bool Known;
        /// <summary>A set of MAME's stv.cpp (LaunchBox's MAME.xml).</summary>
        public bool InMame;
        /// <summary>The files Kronos looks for: name and CRC (lower-case hex) - empty for a set it does not know.</summary>
        public (string Name, string Crc)[] Files = Array.Empty<(string, string)>();
    }

    internal static class KronosStv
    {
        public const string Platform = "Sega ST-V";
        /// <summary>LaunchBox's catch-all arcade platform (Mehdi 19/10): accepted, never recommended - a whole MAME folder filed
        /// there holds Kronos's sets among thousands of others.</summary>
        public const string Arcade = "Arcade";
        public const string BiosSet = "stvbios";
        public const string RunFolder = "lbip-stv";
        private const string Marker = "lbip-stv.txt";
        /// <summary>Cartridge\Type of the ST-V ROM cartridge (CART_ROMSTV).</summary>
        public const string CartType = "11";

        public static bool IsStv(string platform) => string.Equals((platform ?? "").Trim(), Platform, StringComparison.OrdinalIgnoreCase);
        public static bool IsArcade(string platform) => string.Equals((platform ?? "").Trim(), Arcade, StringComparison.OrdinalIgnoreCase);
        public static bool IsOurs(string platform) => IsStv(platform) || IsArcade(platform);

        private static Dictionary<string, KronosStvSet> _sets;
        private static (string Name, string Crc)[] _bios = Array.Empty<(string, string)>();

        /// <summary>Every ST-V set by name, case-insensitive. Empty, never null, if the resource cannot be read.</summary>
        public static Dictionary<string, KronosStvSet> Sets
        {
            get
            {
                if (_sets != null) return _sets;
                var sets = new Dictionary<string, KronosStvSet>(StringComparer.OrdinalIgnoreCase);
                try
                {
                    using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("kronos-stv.tsv");
                    using var reader = new StreamReader(stream);
                    string line;
                    while ((line = reader.ReadLine()) != null)
                    {
                        if (line.Length == 0 || line[0] == '#') continue;
                        var f = line.Split('\t');
                        if (f.Length < 5) continue;
                        var files = (f.Length > 5 ? f[5] : "").Split(',', StringSplitOptions.RemoveEmptyEntries)
                            .Select(x => { int i = x.LastIndexOf(':'); return (x.Substring(0, i), x.Substring(i + 1).ToLowerInvariant()); }).ToArray();
                        if (f[1] == "bios") { _bios = files; continue; }
                        sets[f[0]] = new KronosStvSet
                        {
                            Name = f[0], Known = f[1] == "1", InMame = f[2] == "1", Title = f[3],
                            Parent = f[4].Trim().Length > 0 ? f[4].Trim() : null, Files = files,
                        };
                    }
                    Log.Info("st-v: set table, " + sets.Count + " sets, " + sets.Values.Count(s => s.Known) + " known to Kronos");
                }
                catch (Exception ex) { Log.Warn("st-v: the set table could not be read", ex); }
                return _sets = sets;
            }
        }

        public static (string Name, string Crc)[] BiosFiles { get { _ = Sets; return _bios; } }

        /// <summary>The set a file is, by its name (the one thing Kronos goes by: Cartridge\STVGame) - null when it is none.</summary>
        public static KronosStvSet SetOf(string path)
        {
            var name = Path.GetFileNameWithoutExtension(path ?? "");
            return name.Length > 0 && Sets.TryGetValue(name, out var set) ? set : null;
        }

        /// <summary>Why Kronos cannot run this file from that platform, or null when it can (the files not looked at).</summary>
        public static string WhyNotByName(string path, string platform)
        {
            var set = SetOf(path);
            var file = Path.GetFileName(path ?? "");
            if (!string.Equals(Path.GetExtension(path ?? ""), ".zip", StringComparison.OrdinalIgnoreCase))
                return file + " is no zip: Kronos reads ST-V sets as MAME zips only.";
            if (string.Equals(Path.GetFileNameWithoutExtension(path), BiosSet, StringComparison.OrdinalIgnoreCase))
                return file + " is the ST-V BIOS, not a game.";
            if (set == null)
                return IsArcade(platform) ? file + " is not an ST-V game: from the Arcade platform, Kronos runs the Sega ST-V sets of its own table only."
                                          : file + " is not an ST-V set Kronos or MAME knows (the file's name is the set's, as in MAME).";
            if (!set.Known) return set.Title + " (" + set.Name + ") is an ST-V set Kronos 2.7.5 does not run.";
            return null;
        }

        // ── the files ────────────────────────────────────────────────────────

        /// <summary>The files of a zip: name (without its folder, lower-case) and CRC, as Kronos reads them. Null if unreadable.</summary>
        private static HashSet<(string, string)> ZipFiles(string zip)
        {
            try
            {
                using var a = ZipFile.OpenRead(zip);
                return new HashSet<(string, string)>(a.Entries.Select(e => (Path.GetFileName(e.FullName).ToLowerInvariant(), e.Crc32.ToString("x8"))));
            }
            catch (Exception ex) { Log.Warn("st-v: could not read " + zip, ex); return null; }
        }

        /// <summary>The plugin's stvbios.zip: &lt;kronos&gt;\bios\stvbios.zip.</summary>
        public static string BiosZipOf(string exe) => Path.Combine(KronosLaunch.BiosDirOf(exe), BiosSet + ".zip");

        /// <summary>How many of Kronos's BIOS files a zip holds (0 when it is absent or unreadable).</summary>
        public static int BiosFilesIn(string zip)
        {
            if (string.IsNullOrEmpty(zip) || !File.Exists(zip)) return 0;
            var files = ZipFiles(zip);
            return files == null ? 0 : BiosFiles.Count(b => files.Contains((b.Name.ToLowerInvariant(), b.Crc)));
        }

        /// <summary>The plugin's stvbios.zip, made when it has none: the one beside the game copied, else one made of the BIOS
        /// files of <paramref name="carriers"/> (the game's zip, its parent's). Null with why when none has a BIOS.</summary>
        public static string EnsureBios(string exe, string game, IEnumerable<string> carriers, out string problem)
        {
            problem = null;
            var ours = BiosZipOf(exe);
            if (BiosFilesIn(ours) > 0) return ours;
            var beside = Path.Combine(Path.GetDirectoryName(game) ?? "", BiosSet + ".zip");
            var tmp = ours + ".lbip-tmp";
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(ours));
                if (File.Exists(tmp)) File.Delete(tmp);
                string from = null;
                if (BiosFilesIn(beside) > 0) { File.Copy(beside, tmp); from = beside; }
                else
                    foreach (var c in carriers)
                        if (BiosFilesIn(c) > 0) { MakeBiosZip(c, tmp); from = Path.GetFileName(c) + "'s BIOS files"; break; }
                if (from == null)
                {
                    problem = "Kronos needs the ST-V BIOS, " + BiosSet + ".zip (MAME's), and none was found: not in " + KronosLaunch.BiosDirOf(exe)
                              + ", not beside the game, and the game's zip carries no BIOS file (a MAME non-merged set would)."
                              + "\n\nPut " + BiosSet + ".zip beside the game or in " + KronosLaunch.BiosDirOf(exe) + ".";
                    return null;
                }
                if (File.Exists(ours)) File.Delete(ours);   // one without a BIOS file Kronos knows
                File.Move(tmp, ours);
                Log.Info("st-v: the ST-V BIOS kept in " + ours + " (" + BiosFilesIn(ours) + " BIOS files), from " + from);
                return ours;
            }
            catch (Exception ex)
            {
                try { if (File.Exists(tmp)) File.Delete(tmp); } catch { }
                problem = "The ST-V BIOS could not be put in " + KronosLaunch.BiosDirOf(exe) + " (" + ex.Message + ").";
                Log.Warn("st-v: " + ours, ex);
                return null;
            }
        }

        /// <summary>What Kronos needs for this set, found: the zips to give it (the game's, its parent's when a file is there, the
        /// BIOS) - or null with what is missing.</summary>
        public static List<string> ZipsFor(string game, KronosStvSet set, string exe, out string problem)
        {
            problem = null;
            var zips = new List<string> { game };
            var have = ZipFiles(game);
            if (have == null) { problem = Path.GetFileName(game) + " could not be read as a zip."; return null; }
            var missing = set.Files.Where(f => !have.Contains((f.Name.ToLowerInvariant(), f.Crc))).ToList();
            if (missing.Count > 0 && set.Parent != null)
            {
                var parent = Path.Combine(Path.GetDirectoryName(game), set.Parent + ".zip");
                var inParent = File.Exists(parent) ? ZipFiles(parent) : null;
                if (inParent != null && missing.Any(f => inParent.Contains((f.Name.ToLowerInvariant(), f.Crc))))
                {
                    zips.Add(parent);
                    missing = missing.Where(f => !inParent.Contains((f.Name.ToLowerInvariant(), f.Crc))).ToList();
                }
            }
            if (missing.Count > 0)
            {
                problem = set.Title + " (" + set.Name + "): " + missing.Count + " of its files " + (missing.Count == 1 ? "is" : "are") + " not in "
                          + Path.GetFileName(game) + (set.Parent != null ? " nor in " + set.Parent + ".zip beside it" : "") + " - "
                          + string.Join(", ", missing.Take(6).Select(f => f.Name + " (CRC " + f.Crc + ")")) + (missing.Count > 6 ? "..." : "")
                          + ".\n\nKronos matches each file by its name and CRC, as MAME's sets have them.";
                return null;
            }
            // The BIOS: the plugin's stvbios.zip - the only file Kronos takes it from - made at the first launch if need be.
            var carriers = new List<string>(zips);
            if (set.Parent != null && carriers.Count == 1)
            {
                var parent = Path.Combine(Path.GetDirectoryName(game), set.Parent + ".zip");
                if (File.Exists(parent)) carriers.Add(parent);
            }
            var bios = EnsureBios(exe, game, carriers, out problem);
            if (bios == null) return null;
            zips.Add(bios);
            return zips;
        }

        // ── the launch's folder ──────────────────────────────────────────────

        public static string RunDirOf(string exe) => Path.Combine(KronosLaunch.ProfileOf(exe), RunFolder);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool CreateHardLink(string newFile, string existingFile, IntPtr security);

        /// <summary>The launch's folder made: emptied of the last one (its saves taken back first), the zips linked or copied, the
        /// game's EEPROM and coins brought in from <paramref name="saves"/>. Returns the folder, or null with why.</summary>
        public static string Prepare(string exe, KronosStvSet set, List<string> zips, string saves, out string problem)
        {
            problem = null;
            var run = RunDirOf(exe);
            try
            {
                TakeBack(exe);
                Directory.CreateDirectory(run);
                foreach (var f in Directory.EnumerateFiles(run)) File.Delete(f);
                int linked = 0, copied = 0;
                foreach (var z in zips)
                {
                    var to = Path.Combine(run, Path.GetFileName(z));
                    if (CreateHardLink(to, z, IntPtr.Zero)) linked++;
                    else { File.Copy(z, to, true); copied++; }
                }
                Directory.CreateDirectory(saves);
                foreach (var ext in new[] { ".nv", ".coins" })
                {
                    var from = Path.Combine(saves, set.Name + ext);
                    if (File.Exists(from)) File.Copy(from, Path.Combine(run, set.Name + ext), true);
                }
                File.WriteAllLines(Path.Combine(run, Marker), new[] { set.Name, saves });
                Log.Info("st-v: " + run + " - " + string.Join(", ", zips.Select(Path.GetFileName)) + " (" + linked + " linked, " + copied + " copied)");
                return run;
            }
            catch (Exception ex) { problem = "The ST-V game's folder could not be made (" + ex.Message + ")."; Log.Warn("st-v: " + run, ex); return null; }
        }

        /// <summary>stvbios.zip made of the BIOS files (name and CRC of Kronos's table) of a set's zip - their contents unchanged,
        /// so their CRCs too.</summary>
        internal static int MakeBiosZip(string from, string to)
        {
            var wanted = new HashSet<(string, string)>(BiosFiles.Select(b => (b.Name.ToLowerInvariant(), b.Crc)));
            int n = 0;
            using var src = ZipFile.OpenRead(from);
            using var dst = ZipFile.Open(to, ZipArchiveMode.Create);
            foreach (var e in src.Entries)
            {
                var name = Path.GetFileName(e.FullName);
                if (!wanted.Contains((name.ToLowerInvariant(), e.Crc32.ToString("x8")))) continue;
                var entry = dst.CreateEntry(name, CompressionLevel.Optimal);
                using (var i = e.Open()) using (var o = entry.Open()) i.CopyTo(o);
                n++;
            }
            return n;
        }

        /// <summary>The last launch's EEPROM and coins back to its game's saves (the folder's lbip-stv.txt says which), once
        /// Kronos has gone. Never throws.</summary>
        public static void TakeBack(string exe)
        {
            var run = RunDirOf(exe);
            var marker = Path.Combine(run, Marker);
            try
            {
                if (!File.Exists(marker)) return;
                var lines = File.ReadAllLines(marker);
                if (lines.Length < 2) return;
                var (set, saves) = (lines[0].Trim(), lines[1].Trim());
                Directory.CreateDirectory(saves);
                foreach (var ext in new[] { ".nv", ".coins" })
                {
                    var from = Path.Combine(run, set + ext);
                    if (File.Exists(from)) File.Copy(from, Path.Combine(saves, set + ext), true);
                }
                File.Delete(marker);
                Log.Info("st-v: " + set + "'s EEPROM and coins back in " + saves);
            }
            catch (Exception ex) { Log.Warn("st-v: could not take the game's EEPROM back from " + run, ex); }
        }
    }
}
