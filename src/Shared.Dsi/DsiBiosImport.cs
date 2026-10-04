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
// A NAND IS TAKEN ON WHAT IT LOOKS LIKE (DsiDumps.LooksLikeNand: size range and the "DSi eMMC CID/CPU" footer), under its
// own name: that name is what ties a configured console (dsi\<the same name>) to its original - see DsiBase.

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
        /// was copied, one line each. Never throws; a file that cannot be copied is logged and skipped.</summary>
        public static List<string> Import(string target, IEnumerable<string> sources, IEnumerable<Wanted> files, bool nands,
                                          Action<string> report = null, Func<bool> cancelled = null)
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

                foreach (var source in from)
                {
                    foreach (var file in wanted)
                    {
                        if (cancelled?.Invoke() == true) return copied;
                        if (file.Names.Any(n => File.Exists(Path.Combine(full, n)))) continue;
                        var found = file.Names.Select(n => Path.Combine(source, n)).FirstOrDefault(File.Exists);
                        if (found == null) continue;
                        if (Copy(found, Path.Combine(full, file.Name), report)) copied.Add(Path.GetFileName(found) + " -> " + file.Name);
                    }
                    if (!nands) continue;
                    foreach (var path in Directory.EnumerateFiles(source))
                    {
                        if (cancelled?.Invoke() == true) return copied;
                        var name = Path.GetFileName(path);
                        if (File.Exists(Path.Combine(full, name)) || !DsiDumps.LooksLikeNand(path)) continue;
                        if (Copy(path, Path.Combine(full, name), report)) copied.Add(name);
                    }
                }
                if (copied.Count > 0) DsiLog.Info("bios: " + copied.Count + " file(s) copied into " + full + " - " + string.Join(", ", copied));
            }
            catch (Exception ex) { DsiLog.Warn("bios: could not copy the user's files into " + target, ex); }
            return copied;
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
