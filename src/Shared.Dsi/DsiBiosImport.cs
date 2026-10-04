// The user's BIOS, firmware and NAND dumps, COPIED into an emulator's own bios\ folder from where they already are
// (Mehdi, 04/10: "un repertoire bios dans melonDS directement plutot que d'aller chercher dans le repertoire retroarch").
//
// An emulator's bios\ holds the ORIGINALS and nothing else - what the user dumped, never written by the pack. Everything
// derived from them (a configured console, a working image, a firmware copy with its owner) lives elsewhere: dsi\,
// lbip-firmware\. The emulator then reads bios\ only; the folders below are sources for this copy, never read at launch.
//
// AT INSTALL, so that LaunchBox's BIOS check finds them already: from RetroArch's system folder (the place they were
// asked for until 04/10, and where a RetroArch DS core's are) and, for no$gba, from melonDS's bios\ too. ONLY WHAT IS
// MISSING: a file already in bios\ under any of its names is never replaced, so the user's own choice wins and a second
// install costs nothing. The sources are left as they are - a copy, not a move: RetroArch may still want its own.
//
// A NAND IS TAKEN ON WHAT IT LOOKS LIKE (DsiDumps.LooksLikeNand: size range and the "DSi eMMC CID/CPU" footer), and NAMED
// AFTER WHAT IT IS (Mehdi, 04/10): DSi_Nand_EUR.bin, its region read out of it, when that name is free - its own name
// otherwise. The name ties a configured console (dsi\<the same name>) to its original, so the console is renamed with it
// (DsiBase.RenameConsole). Files already in bios\ are never renamed: any name works there.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace LbIntegrations.Dsi
{
    internal static class DsiBiosImport
    {
        /// <summary>One file wanted: the name it is copied under, and the names it may have in a source.</summary>
        internal sealed class Wanted
        {
            public string Name;
            public string[] Aliases = Array.Empty<string>();
            public IEnumerable<string> Names => new[] { Name }.Concat(Aliases ?? Array.Empty<string>());
        }

        /// <summary>Copy into <paramref name="target"/> what it is missing, from <paramref name="sources"/> in order. What
        /// was copied, one line each. Never throws; a file that cannot be copied is logged and skipped.
        ///
        /// A NAND IS COPIED UNDER A FIXED NAME, DSi_Nand_EUR.bin (Mehdi, 04/10) - its region read out of it with the DSi ARM7
        /// BIOS <paramref name="bios7"/> gives once the BIOS files are in (so they go first) - when that name is free in
        /// target; under its own otherwise, or when its region cannot be read. <paramref name="renamed"/> is told
        /// (old name, new name), for the console built from it to follow (DsiBase.RenameConsole). A dump already in target
        /// - same size and write time, which File.Copy keeps - is not copied twice under another name.</summary>
        public static List<string> Import(string target, IEnumerable<string> sources, IEnumerable<Wanted> files, bool nands,
                                          Action<string> report = null, Func<bool> cancelled = null,
                                          Func<string> bios7 = null, Action<string, string> renamed = null)
        {
            var copied = new List<string>();
            try
            {
                if (string.IsNullOrEmpty(target)) return copied;
                var full = Path.GetFullPath(target);
                var from = (sources ?? Enumerable.Empty<string>()).Where(s => !string.IsNullOrEmpty(s)).Select(SafeFull)
                    .Where(s => s != null && !string.Equals(s, full, StringComparison.OrdinalIgnoreCase) && Directory.Exists(s))
                    .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                if (from.Count == 0) return copied;
                Directory.CreateDirectory(full);
                var wanted = (files ?? Enumerable.Empty<Wanted>()).ToList();

                // EVERY SOURCE IS SEARCHED RECURSIVELY (Mehdi, 04/10): a RetroArch system folder sorted into sub-folders
                // (system\nds\, system\melonDS DS\...) still gives its files. One listing per source, nearest first.
                var listed = from.ToDictionary(s => s, Listing, StringComparer.OrdinalIgnoreCase);

                // The BIOS and firmware first, from every source: the DSi ARM7 BIOS is what reads a NAND's region below.
                foreach (var source in from)
                    foreach (var file in wanted)
                    {
                        if (cancelled?.Invoke() == true) return copied;
                        if (file.Names.Any(n => File.Exists(Path.Combine(full, n)))) continue;
                        // The declared name before an alias, then the nearest folder: the file the user meant.
                        var found = file.Names.Select(n => listed[source].FirstOrDefault(p => string.Equals(Path.GetFileName(p), n, StringComparison.OrdinalIgnoreCase)))
                                              .FirstOrDefault(p => p != null);
                        if (found == null) continue;
                        if (Copy(found, Path.Combine(full, file.Name), report)) copied.Add(Path.GetFileName(found) + " -> " + file.Name);
                    }

                if (nands)
                {
                    string arm7 = null;
                    try { arm7 = bios7?.Invoke(); } catch { }
                    foreach (var source in from)
                        foreach (var path in listed[source])
                        {
                            if (cancelled?.Invoke() == true) return copied;
                            if (!DsiDumps.LooksLikeNand(path) || AlreadyIn(full, path)) continue;
                            var own = Path.GetFileName(path);
                            var region = DsiDumps.RegionOf(path, arm7);
                            var fixedName = region != null ? DsiRegions.SuggestedFileName(region.Value) : null;
                            var name = fixedName != null && !File.Exists(Path.Combine(full, fixedName)) ? fixedName
                                     : !File.Exists(Path.Combine(full, own)) ? own : null;
                            if (name == null) { DsiLog.Info("bios: " + own + " not copied - " + (fixedName ?? own) + " and " + own + " are both taken in " + full); continue; }
                            if (!Copy(path, Path.Combine(full, name), report)) continue;
                            copied.Add(own == name ? own : own + " -> " + name);
                            if (own != name) try { renamed?.Invoke(own, name); } catch { }
                        }
                }
                if (copied.Count > 0) DsiLog.Info("bios: " + copied.Count + " file(s) copied into " + full + " - " + string.Join(", ", copied));
            }
            catch (Exception ex) { DsiLog.Warn("bios: could not copy the user's files into " + target, ex); }
            return copied;
        }

        /// <summary>The same dump already in <paramref name="dir"/>, under any name: same size and same last 64 bytes - the
        /// "DSi eMMC CID/CPU" footer, which carries the console's own CID. NOT the write time: measured 04/10, six dumps of
        /// six regions sharing one size and one date were all taken for the first one copied.</summary>
        private static bool AlreadyIn(string dir, string path)
        {
            try
            {
                var length = new FileInfo(path).Length;
                var tail = Tail(path);
                if (tail == null) return false;
                foreach (var other in Directory.EnumerateFiles(dir))
                {
                    if (new FileInfo(other).Length != length) continue;
                    var t = Tail(other);
                    if (t != null && t.SequenceEqual(tail)) return true;
                }
                return false;
            }
            catch { return false; }
        }

        private static byte[] Tail(string path)
        {
            try
            {
                var tail = new byte[64];
                using (var f = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    f.Seek(-tail.Length, SeekOrigin.End);
                    return f.Read(tail, 0, tail.Length) == tail.Length ? tail : null;
                }
            }
            catch { return null; }
        }

        /// <summary>Every file under <paramref name="dir"/>, sub-folders included, the shallowest first (then by path, so the
        /// order never drifts). A folder that cannot be read is skipped, not fatal.</summary>
        private static List<string> Listing(string dir)
        {
            try
            {
                var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint };
                return Directory.EnumerateFiles(dir, "*", options)
                    .OrderBy(p => p.Count(c => c == Path.DirectorySeparatorChar))
                    .ThenBy(p => p, StringComparer.OrdinalIgnoreCase).ToList();
            }
            catch (Exception ex) { DsiLog.Warn("bios: could not list " + dir, ex); return new List<string>(); }
        }

        private static string SafeFull(string path)
        {
            try { return Path.GetFullPath(path); } catch { return null; }
        }

        /// <summary>Through a temporary file, so a copy cut short (a 240 MB NAND) never leaves a half file under the name.
        /// File.Copy keeps the write time, which DsiDumps' index of regions is keyed on.</summary>
        private static bool Copy(string from, string to, Action<string> report)
        {
            var tmp = to + ".copying";
            try
            {
                report?.Invoke("Copying " + Path.GetFileName(from) + "...");
                File.Copy(from, tmp, overwrite: true);
                File.Move(tmp, to);
                return true;
            }
            catch (Exception ex)
            {
                DsiLog.Warn("bios: could not copy " + from, ex);
                try { if (File.Exists(tmp)) File.Delete(tmp); } catch { }
                return false;
            }
        }
    }
}
