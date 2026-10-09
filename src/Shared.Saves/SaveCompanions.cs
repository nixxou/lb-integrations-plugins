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
            if (companions == null) return written;
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

        /// <summary>What a companion adds to its primary's stem (".rtc"), else its own extension.</summary>
        internal static string TailOf(string primaryStem, string companion)
        {
            var name = Path.GetFileName(companion);
            return name.StartsWith(primaryStem, StringComparison.OrdinalIgnoreCase) && name.Length > primaryStem.Length
                ? name.Substring(primaryStem.Length) : Path.GetExtension(name);
        }
    }
}
