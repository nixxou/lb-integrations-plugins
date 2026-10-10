// A save's companions put back with it at a restore (lot 3, docs\saves-lot3.md §7, measured 09/10).
//
// LaunchBox copies a save's companions INTO the vault at a backup ("-10.srm" + "-10.rtc"), but at a restore it hands the
// plugin the primary only and copies nothing back: RASnes9x's Zelda .srm came back, its .rtc stayed the one it replaced
// (measured). ares put its 64DD clock back itself; RASnes9x and bsnes did not.
//
// Each companion of the backup (the plugin's own GetCompanionSaveFiles of the vault copy) goes beside the restored file,
// under the restored file's stem and its own tail: "-10.rtc" beside "-10.srm" becomes "<rom>.rtc" beside "<rom>.srm".
// A backup without a companion leaves the live one as it is (as ares). Thumbnails are the callers' (states only).
// The save and its companions go in together or not at all (RestoreSet: staged, swapped, put back on a failure).

#nullable enable

using System;
using System.Collections.Generic;
using System.IO;

namespace LbIntegrations.Saves
{
    internal static class SaveCompanions
    {
        /// <summary>Restores <paramref name="source"/> to <paramref name="target"/> and each of its <paramref name="companions"/>
        /// beside it - ALL OR NOTHING (Codex review 10/10: a backup's .rtc that could not be read failed after the live .srm
        /// was replaced, the restored save left with the old clock). Every source is first copied next to its destination
        /// (a read that fails touches nothing live); then each destination is swapped for its copy, the file it held set
        /// aside; a swap that fails puts back every one already made. Returns the files written (the target first); throws
        /// with the live files as they were.</summary>
        public static List<string> RestoreSet(string source, string target, IEnumerable<string>? companions, Action<string>? log)
        {
            var pairs = new List<(string From, string To)> { (source, target) };
            if (companions != null && LbIntegrations.Compat.Compat.On("companionRestore"))
            {
                var sourceStem = Path.GetFileNameWithoutExtension(source);
                var targetStem = Path.Combine(Path.GetDirectoryName(target) ?? "", Path.GetFileNameWithoutExtension(target));
                foreach (var c in companions)
                {
                    if (string.IsNullOrWhiteSpace(c) || !File.Exists(c)) continue;
                    var dest = targetStem + TailOf(sourceStem, c);
                    if (string.Equals(Path.GetFullPath(dest), Path.GetFullPath(c), StringComparison.OrdinalIgnoreCase)) continue;
                    pairs.Add((c, dest));
                }
            }
            var tag = Guid.NewGuid().ToString("N").Substring(0, 8);
            var staged = new List<string>();
            var swaps = new List<Swap>();
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(target) ?? ".");
                // 1. Every source read, beside its destination (same folder: the swap is a rename).
                foreach (var (from, to) in pairs)
                {
                    var tmp = to + ".lbip-new-" + tag;
                    staged.Add(tmp);
                    File.Copy(from, tmp, overwrite: true);
                }
                // 2. Each destination swapped for its copy, what it held kept aside until all are in.
                for (int i = 0; i < pairs.Count; i++)
                {
                    var s = new Swap { To = pairs[i].To };
                    swaps.Add(s);
                    if (File.Exists(s.To))
                    {
                        s.Old = s.To + ".lbip-old-" + tag;
                        File.Move(s.To, s.Old);
                    }
                    File.Move(staged[i], s.To);
                    s.In = true;
                }
            }
            catch
            {
                // Back as it was, last swap first: the copy moved in removed, the file it replaced put back.
                for (int i = swaps.Count - 1; i >= 0; i--)
                {
                    var s = swaps[i];
                    try
                    {
                        if (s.In) File.Delete(s.To);
                        if (s.Old != null && File.Exists(s.Old)) File.Move(s.Old, s.To);
                    }
                    catch (Exception ex) { log?.Invoke("could not put back " + Path.GetFileName(s.To) + ": " + ex.Message + (s.Old == null ? "" : " (kept as " + Path.GetFileName(s.Old) + ")")); }
                }
                foreach (var tmp in staged) try { if (File.Exists(tmp)) File.Delete(tmp); } catch { }
                throw;
            }
            foreach (var s in swaps) try { if (s.Old != null) File.Delete(s.Old); } catch { }
            var written = new List<string>();
            foreach (var (_, to) in pairs)
            {
                written.Add(to);
                if (written.Count > 1) log?.Invoke("restored with it " + Path.GetFileName(to));
            }
            return written;
        }

        private sealed class Swap
        {
            public string To = "";
            public string? Old;   // what it held, set aside
            public bool In;       // the restored copy is in its place
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
