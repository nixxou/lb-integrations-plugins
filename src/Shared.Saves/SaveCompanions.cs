// A save's companions put back with it at a restore (lot 3, docs\saves-lot3.md §7, measured 09/10).
//
// LaunchBox copies a save's companions INTO the vault at a backup ("-10.srm" + "-10.rtc"), but at a restore it hands the
// plugin the primary only and copies nothing back: RASnes9x's Zelda .srm came back, its .rtc stayed the one it replaced
// (measured). ares put its 64DD clock back itself; RASnes9x and bsnes did not.
//
// Each companion of the backup (the plugin's own GetCompanionSaveFiles of the vault copy) goes beside the restored file,
// under the restored file's stem and its own tail: "-10.rtc" beside "-10.srm" becomes "<rom>.rtc" beside "<rom>.srm".
// A backup without a companion leaves the live one as it is (as ares). Thumbnails are the callers' (states only).

#nullable enable

using System;
using System.Collections.Generic;
using System.IO;

namespace LbIntegrations.Saves
{
    internal static class SaveCompanions
    {
        /// <summary>Copies each of <paramref name="companions"/> of <paramref name="source"/> beside <paramref name="target"/>;
        /// returns the files written.</summary>
        public static List<string> CopyBack(string source, string target, IEnumerable<string>? companions, Action<string>? log)
        {
            var written = new List<string>();
            if (companions == null || !LbIntegrations.Compat.Compat.On("companionRestore")) return written;
            var sourceStem = Path.GetFileNameWithoutExtension(source);
            var targetStem = Path.Combine(Path.GetDirectoryName(target) ?? "", Path.GetFileNameWithoutExtension(target));
            foreach (var c in companions)
            {
                if (string.IsNullOrWhiteSpace(c) || !File.Exists(c)) continue;
                var dest = targetStem + TailOf(sourceStem, c);
                if (string.Equals(Path.GetFullPath(dest), Path.GetFullPath(c), StringComparison.OrdinalIgnoreCase)) continue;
                File.Copy(c, dest, overwrite: true);
                written.Add(dest);
                log?.Invoke("restored with it " + Path.GetFileName(dest));
            }
            return written;
        }

        /// <summary>Null when <paramref name="target"/> and every companion's destination can be written, else why not -
        /// asked BEFORE the primary is copied (Mehdi, 09/10): a companion that cannot be written (read-only, in use, a
        /// folder without write access - a save beside the roms on a share) failed after the primary was replaced, a
        /// restore half done and reported as failed. Opening a file for writing changes nothing; a folder is tried with
        /// a file created and deleted at once.</summary>
        public static string? WhyNotWritable(string source, string target, IEnumerable<string>? companions)
        {
            var sourceStem = Path.GetFileNameWithoutExtension(source);
            var targetStem = Path.Combine(Path.GetDirectoryName(target) ?? "", Path.GetFileNameWithoutExtension(target));
            var dests = new List<string> { target };
            if (companions != null)
                foreach (var c in companions)
                    if (!string.IsNullOrWhiteSpace(c) && File.Exists(c)) dests.Add(targetStem + TailOf(sourceStem, c));
            foreach (var d in dests)
            {
                var why = WhyNot(d);
                if (why != null) return Path.GetFileName(d) + " " + why + ": nothing was restored.";
            }
            return null;
        }

        private static string? WhyNot(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    if ((File.GetAttributes(path) & FileAttributes.ReadOnly) != 0) return "is read-only";
                    using (new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.ReadWrite)) { }
                    return null;
                }
                var dir = Path.GetDirectoryName(path);
                if (string.IsNullOrEmpty(dir)) return null;
                if (!Directory.Exists(dir)) return null;   // created by the copy, as before
                var probe = Path.Combine(dir, ".lbip-write-" + Guid.NewGuid().ToString("N"));
                using (new FileStream(probe, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1, FileOptions.DeleteOnClose)) { }
                return null;
            }
            catch (UnauthorizedAccessException) { return "cannot be written (no write access)"; }
            catch (IOException ex) { return "cannot be written (" + ex.Message.Trim() + ")"; }
            catch (Exception ex) { return "cannot be written (" + ex.GetType().Name + ")"; }
        }

        /// <summary>What a companion adds to its primary's stem (".rtc"), else its own extension.</summary>
        internal static string TailOf(string primaryStem, string companion)
        {
            var name = Path.GetFileName(companion);
            return name.StartsWith(primaryStem, StringComparison.OrdinalIgnoreCase) && name.Length > primaryStem.Length
                ? name.Substring(primaryStem.Length) : Path.GetExtension(name);
        }
    }
}
