// The Saturn BIOS ("IPL ROM") Ymir needs, found where the user already keeps it and put in Ymir's roms\ipl folder.
//
// YMIR READS ONE FOLDER, BY CONTENT (docs\ymir.md §6; app\rom_manager.cpp ScanIPLROMs, db\ipl_db.cpp): every file of 512 KiB
// in roms\ipl\ (the IPLROMImages override, its sub-folders too) is hashed - XXH3-128 seeded with 0x94B487AF51733FBE
// (sys\memory_defs.hpp kIPLHashSeed) - and named by its table: Saturn JP 1.00 / 1.01, US-EU 1.00a / 1.01a, KR 1.02a,
// Hi-Saturn, V-Saturn, the development kit, and region-free patched ones. Names do not matter. MEASURED 20/10: the lab's
// mpr-17933.bin hashes to D49063DD..CECB8, the table's US/EU 1.01a. Per game Ymir picks the BIOS of the disc's area.
//
// SO THE PLUGIN (at install, at a launch when roms\ipl has none, from the Nixx page): looks in RetroArch's system folder and
// the bios / system / firmware folders of the other emulators beside this one, at files of 512 KiB only, hashes them as Ymir
// does, and COPIES each one Ymir knows whose region is not in roms\ipl yet - under its own name, never a move. A launch with
// no BIOS at all in roms\ipl is refused, saying where to put one (Ymir would start on a black screen).

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Hashing;
using System.Linq;
using LbIntegrations.Lbip;

namespace LbIntegrations.Ymir
{
    internal sealed class YmirIpl
    {
        public string Hash, Version, Variant, Region;
        public bool RegionFree;
        public string Label => Variant + " " + Region + " " + Version + (RegionFree ? " (region-free)" : "");
    }

    internal sealed class YmirBiosFile
    {
        public string Path;
        public YmirIpl Known;     // null: a 512 KiB file Ymir does not know (a modified image - Ymir lists it all the same)
    }

    internal static class YmirBios
    {
        public const long Size = 512 * 1024;
        private const ulong Seed = 0x94B487AF51733FBEUL;

        /// <summary>db\ipl_db.cpp at 0.4.1: the canonical XXH128 (seeded) of each IPL ROM Ymir knows.</summary>
        internal static readonly YmirIpl[] Table =
        {
            Ipl("72C081C9AE6C586150A81664AD98E52F", "1.00", "Saturn", "JP"),
            Ipl("D1B69E029449D569929613581A6BB381", "1.01", "Saturn", "JP"),
            Ipl("80275D4ACDCBC8C9C5AB8D19B8761950", "1.00a", "Saturn", "US/EU"),
            Ipl("D49063DD5320C8C7B1989060784CECB8", "1.01a", "Saturn", "US/EU"),
            Ipl("F369341555268D203B9CCAEA6038E4D6", "1.02a", "Saturn", "KR"),
            Ipl("CFB4CCC988C968F02CC94D481D4D1B49", "1.01", "Hi-Saturn", "JP"),
            Ipl("BF93CA69466E5F420A48A6E7D4DCB169", "1.02", "Hi-Saturn", "JP"),
            Ipl("E52867B64F106218A956AED5180A04C2", "1.03", "Hi-Saturn", "JP"),
            Ipl("75FD715C6BBF8E20278F319B057A2D76", "1.01", "V-Saturn", "JP"),
            Ipl("2E8A939F6440DDD2AAD1F52C25774B18", "1.003", "Dev kit", "JP"),
            Ipl("5984F8D1F30C70F6CD52D5797B1320A8", "1.00", "Saturn", "JP", true),
            Ipl("F34BA4EFC8C86DF06B860F985AB98591", "1.01", "Saturn", "JP", true),
            // Twice in ipl_db.cpp (1.00a, then 1.01a): its unordered_map keeps the first.
            Ipl("55C1A9FE4CB62845835F94671276398C", "1.00a", "Saturn", "JP", true),
            Ipl("933551ED20C13E93DBB3400C63538985", "1.02a", "Saturn", "KR", true),
            Ipl("F1AD09AC605D5982823609CF10C80A49", "1.01", "Hi-Saturn", "JP", true),
            Ipl("A15532697D40B0113CCD380CCB9F8E76", "1.02", "Hi-Saturn", "JP", true),
            Ipl("83C36E72A65C85A1537D82A49CA31632", "1.03", "Hi-Saturn", "JP", true),
            Ipl("486A9D65022E412A92B0EF719CC01A39", "1.01", "V-Saturn", "JP", true),
        };

        private static YmirIpl Ipl(string hash, string version, string variant, string region, bool regionFree = false)
            => new YmirIpl { Hash = hash, Version = version, Variant = variant, Region = region, RegionFree = regionFree };

        public static YmirIpl Known(string hash) => Table.FirstOrDefault(t => string.Equals(t.Hash, hash, StringComparison.OrdinalIgnoreCase));

        private static readonly Dictionary<string, (long Length, DateTime When, string Hash)> Hashes = new Dictionary<string, (long, DateTime, string)>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Ymir's hash of an IPL ROM file, kept by size and date; null when it is not 512 KiB or cannot be read.</summary>
        internal static string HashOf(string path)
        {
            try
            {
                var info = new FileInfo(path);
                if (!info.Exists || info.Length != Size) return null;
                lock (Hashes)
                    if (Hashes.TryGetValue(info.FullName, out var h) && h.Length == info.Length && h.When == info.LastWriteTimeUtc) return h.Hash;
                var hash = Convert.ToHexString(XxHash128.Hash(File.ReadAllBytes(info.FullName), unchecked((long)Seed)));
                lock (Hashes) Hashes[info.FullName] = (info.Length, info.LastWriteTimeUtc, hash);
                return hash;
            }
            catch { return null; }
        }

        /// <summary>The IPL ROMs in Ymir's folder, as Ymir lists them (every 512 KiB file, its sub-folders too).</summary>
        public static List<YmirBiosFile> InFolder(string exe)
        {
            var list = new List<YmirBiosFile>();
            try
            {
                var dir = YmirLaunch.IplDirOf(exe);
                if (!Directory.Exists(dir)) return list;
                foreach (var p in LbipBiosImport.Listing(dir, recursive: true))
                {
                    var h = HashOf(p);
                    if (h != null) list.Add(new YmirBiosFile { Path = p, Known = Known(h) });
                }
            }
            catch (Exception ex) { Log.Warn("bios: could not list Ymir's folder", ex); }
            return list;
        }

        /// <summary>The folders looked in: the bios / system / firmware folders of the emulators beside this one (RetroArch's
        /// system\ among them).</summary>
        private static List<string> Sources(string exe)
        {
            var dirs = new List<string>();
            try
            {
                var own = Path.GetDirectoryName(Path.GetFullPath(exe)).TrimEnd('\\');
                var emulators = Path.GetDirectoryName(own);
                foreach (var emu in Directory.EnumerateDirectories(emulators))
                {
                    if (string.Equals(Path.GetFullPath(emu).TrimEnd('\\'), own, StringComparison.OrdinalIgnoreCase)) continue;
                    foreach (var sub in new[] { "system", "bios", "BIOS", "Bios", "firmware", "Firmware" })
                    {
                        var dir = Path.Combine(emu, sub);
                        if (Directory.Exists(dir) && !dirs.Contains(dir, StringComparer.OrdinalIgnoreCase)) dirs.Add(dir);
                    }
                }
            }
            catch (Exception ex) { Log.Warn("bios: could not list the other emulators", ex); }
            return dirs;
        }

        /// <summary>The BIOS files of Ymir's folder, after copying in each one Ymir knows of a region it has none of (header).
        /// <paramref name="readOnly"/>: only say what is there. Never throws.</summary>
        public static List<YmirBiosFile> Ensure(string exe, Action<string> report = null, bool readOnly = false)
        {
            var have = InFolder(exe);
            if (readOnly) return have;
            try
            {
                string Key(YmirIpl k) => k.Variant + "|" + k.Region;
                var regions = new HashSet<string>(have.Where(f => f.Known != null).Select(f => Key(f.Known)));
                var target = YmirLaunch.IplDirOf(exe);
                var copied = new List<string>();
                foreach (var dir in Sources(exe))
                    foreach (var p in LbipBiosImport.Listing(dir, recursive: true))
                    {
                        long len; try { len = new FileInfo(p).Length; } catch { continue; }
                        if (len != Size) continue;
                        var known = Known(HashOf(p));
                        if (known == null || regions.Contains(Key(known))) continue;
                        Directory.CreateDirectory(target);
                        var to = Path.Combine(target, Path.GetFileName(p));
                        if (File.Exists(to)) to = Path.Combine(target, Path.GetFileNameWithoutExtension(p) + "-" + known.Region.Replace("/", "") + Path.GetExtension(p));
                        if (File.Exists(to) || !LbipBiosImport.Copy(p, to, report)) continue;
                        regions.Add(Key(known));
                        copied.Add(known.Label + " from " + p);
                    }
                if (copied.Count > 0) { Log.Info("bios: copied into " + target + " - " + string.Join("; ", copied)); have = InFolder(exe); }
            }
            catch (Exception ex) { Log.Warn("bios", ex); }
            return have;
        }

        /// <summary>A BIOS for a disc of <paramref name="region"/> ("us-eu" / "jp", YmirDisc.BiosRegion): one of that region,
        /// or a region-free one.</summary>
        public static bool HasFor(IEnumerable<YmirBiosFile> files, string region)
        {
            var known = files.Where(f => f.Known != null).Select(f => f.Known).ToList();
            if (known.Any(k => k.RegionFree)) return true;
            return region == "jp" ? known.Any(k => k.Region == "JP" || k.Region == "KR") : known.Any(k => k.Region == "US/EU");
        }

        public static string RegionWord(string region) => region == "jp" ? "Japanese" : "US / European";
    }
}
