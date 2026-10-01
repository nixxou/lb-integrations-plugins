// The updates and DLC of the game being launched: where they are looked for, how they are recognised,
// which are kept. Installed right after the game and BEFORE the reference walk - anything installed
// after it would come out of the session as a change, into the save.
//
// WHERE: the scan of the game's folder (Vita3kScan - every Vita file there read once by its param.sfo, and cached; the
// folder above when it is named with "vita"), and whatever else the scans have seen - an import's folders. The CONTENT
// decides (Mehdi, 01/10, as for the Xbox 360): same TITLE_ID as the game, CATEGORY gp (an update) or ac (a DLC). No file or
// folder name nominates anything any more; the four naming rules and the import's index are gone.
//
// And the game's OWN ARCHIVE: a zip that holds the game with its update and DLC - its other contents are in the scan too.
//
// WHICH: every DLC, one per CONTENT_ID; and ONE update, the highest APP_VER. Vita updates are
// cumulative - Sony's server only ever serves the latest, and a patch replaces ux0/patch/<id> rather
// than adding to it - so 1.22 alone is the game at 1.22. UNLESS the game's options say otherwise
// (Vita3kExtrasChoice): another update, none, some DLC left out.
//
// Everything found, kept and set aside is written to the log, with the reason: the one place
// somebody can see why a DLC did not show up.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace LbIntegrations.Vita3k
{
    internal sealed class VitaExtra
    {
        public string Path;
        public VitaContent Content;
        public long Bytes;          // uncompressed, for sizing the RAM disk
        public string FoundBy;      // which rule nominated it

        /// <summary>Which extra, as the game's options record it: the archive, and where in it when it
        /// holds several - "path|root". A path alone for a content at the top of its archive.</summary>
        public string Ref => Content?.Root is { Length: > 0 } root ? Path + "|" + root : Path;

        /// <summary>Its name in the log: the file, and where in it.</summary>
        public string Name => System.IO.Path.GetFileName(Path) + (Content?.Root is { Length: > 0 } root ? " > " + root : "");

        /// <summary>Two Refs naming the same extra - the path by its full form, the root as written.</summary>
        public static bool SameRef(string a, string b)
        {
            if (a == null || b == null) return false;
            static (string, string) Split(string r) { var i = r.IndexOf('|'); return i < 0 ? (r, "") : (r.Substring(0, i), r.Substring(i + 1)); }
            var (pa, ra) = Split(a); var (pb, rb) = Split(b);
            string Full(string p) { try { return System.IO.Path.GetFullPath(p); } catch { return p; } }
            return string.Equals(Full(pa), Full(pb), StringComparison.OrdinalIgnoreCase) && string.Equals(ra, rb, StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>Everything found for a game, nothing chosen: updates highest first, DLC one per CONTENT_ID.</summary>
    internal sealed class FoundExtras
    {
        public List<VitaExtra> Updates = new List<VitaExtra>();
        public List<VitaExtra> Addons = new List<VitaExtra>();
    }

    internal sealed class VitaExtras
    {
        public VitaExtra Update;
        public List<VitaExtra> Addons = new List<VitaExtra>();

        public IEnumerable<VitaExtra> All => (Update != null ? new[] { Update } : new VitaExtra[0]).Concat(Addons);
        public long Bytes => All.Sum(e => Math.Max(0, e.Bytes));

        /// <summary>What was chosen, as one string - part of the reuse marker, so that adding a DLC or
        /// a newer update between two launches rebuilds the console instead of reusing one without it.</summary>
        public string Key()
        {
            var parts = All.Select(e =>
            {
                long size = 0, ticks = 0;
                try { var i = new FileInfo(e.Path); size = i.Length; ticks = i.LastWriteTimeUtc.Ticks; } catch { }
                return System.IO.Path.GetFullPath(e.Path).ToLowerInvariant() + "|" + (e.Content?.Root ?? "").ToLowerInvariant() + "|" + size + "|" + ticks;
            }).OrderBy(s => s, StringComparer.Ordinal);
            var text = string.Join("\n", parts);
            if (text.Length == 0) return "";
            return Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(text))).Substring(0, 16);
        }
    }

    internal static class Vita3kExtras
    {
        /// <summary>Find, evaluate and choose the updates and DLC of <paramref name="game"/>: every one
        /// found, then the choice - <paramref name="choice"/>'s, or the automatic one.</summary>
        public static VitaExtras For(string romPath, VitaContent game, string hostTitle, string installDir = null,
                                     Vita3kExtrasChoice choice = null, Action<string, double?> progress = null)
            => Choose(Evaluate(romPath, game, hostTitle, installDir, progress), choice);

        /// <summary>Everything that belongs to the game: every valid update, every DLC (one per
        /// CONTENT_ID) - nothing chosen yet. The game's folder is scanned first: quick but for what is new.</summary>
        public static FoundExtras Evaluate(string romPath, VitaContent game, string hostTitle, string installDir = null,
                                           Action<string, double?> progress = null)
        {
            var found = new FoundExtras();
            try
            {
                if (game?.TitleId == null || string.IsNullOrWhiteSpace(romPath)) return found;
                var folder = Vita3kScan.FolderFor(romPath);
                Vita3kScan.Scan(folder, progress);
                var rom = Full(romPath);
                var mine = Vita3kScan.CachedAll()
                    .Where(e => !e.Invalid && string.Equals(e.TitleId, game.TitleId, StringComparison.OrdinalIgnoreCase))
                    .OrderBy(e => string.Equals(Full(e.Archive), rom, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                    .ThenBy(e => e.Path, StringComparer.OrdinalIgnoreCase).ToList();
                Log.Info("updates and DLC of " + game.TitleId + ": " + mine.Count + " content(s) of that title id known, " + folder + " scanned");

                var addons = new Dictionary<string, VitaExtra>(StringComparer.OrdinalIgnoreCase);
                foreach (var e in mine)
                {
                    var content = e.ToContent();
                    bool own = string.Equals(Full(e.Archive), rom, StringComparison.OrdinalIgnoreCase);
                    var rule = own ? "in the game's own archive" : "scanned in " + System.IO.Path.GetDirectoryName(e.Archive);
                    var name = System.IO.Path.GetFileName(e.Archive) + (content.Root.Length > 0 ? " > " + content.Root : "");
                    var extra = new VitaExtra { Path = e.Archive, Content = content, FoundBy = rule, Bytes = Math.Max(0, content.Bytes) };
                    if (content.IsPatch)
                    {
                        found.Updates.Add(extra);
                        Log.Info("  update " + (content.AppVer ?? "?") + ": " + name + " [" + rule + "], " + Mb(extra.Bytes));
                    }
                    else if (content.IsAddon)
                    {
                        var id = content.ContentId ?? name;
                        if (addons.TryGetValue(id, out var already))
                        { Log.Info("  set aside " + name + " - the same DLC (" + id + ") as " + already.Name); continue; }
                        addons[id] = extra;
                        Log.Info("  DLC " + (content.Title ?? id) + " (" + id + "): " + name + " [" + rule + "], " + Mb(extra.Bytes));
                    }
                }
                found.Updates = found.Updates.OrderByDescending(u => VersionOf(u.Content.AppVer)).ToList();
                found.Addons = addons.Values.OrderBy(a => a.Content.ContentId, StringComparer.Ordinal).ToList();
            }
            catch (Exception ex) { Log.Warn("could not look for updates and DLC", ex); }
            return found;
        }
        /// <summary>What is installed with the game: <paramref name="choice"/>'s update and DLC when
        /// there is one - an update it names that is not found falls back to the automatic choice, and
        /// says so - otherwise the highest update and every DLC.</summary>
        public static VitaExtras Choose(FoundExtras found, Vita3kExtrasChoice choice)
        {
            var chosen = new VitaExtras();
            if (choice != null && choice.NoUpdate) chosen.Update = null;
            else if (choice?.UpdatePath != null)
            {
                chosen.Update = found.Updates.FirstOrDefault(u => VitaExtra.SameRef(u.Ref, choice.UpdatePath));
                if (chosen.Update == null)
                {
                    Log.Warn("  the update chosen in the game's options (" + choice.UpdatePath + ") is not found - the highest one instead");
                    chosen.Update = found.Updates.FirstOrDefault();
                }
            }
            else chosen.Update = found.Updates.FirstOrDefault();   // the highest - they are cumulative

            foreach (var u in found.Updates)
                if (u != chosen.Update)
                    Log.Info("  set aside update " + (u.Content.AppVer ?? "?") + " (" + u.Name + ") - "
                             + (choice != null && (choice.NoUpdate || choice.UpdatePath != null)
                                ? "not the one chosen in the game's options"
                                : (chosen.Update?.Content.AppVer ?? "?") + " is higher, and updates are cumulative"));

            chosen.Addons = found.Addons.Where(a => choice == null || !choice.LeftOut.Contains(a.Content.ContentId ?? "")).ToList();
            foreach (var a in found.Addons.Except(chosen.Addons))
                Log.Info("  set aside DLC " + (a.Content.Title ?? a.Content.ContentId) + " - left out in the game's options");

            Log.Info("  kept: " + (chosen.Update != null ? "update " + chosen.Update.Content.AppVer : "no update")
                     + ", " + chosen.Addons.Count + " DLC" + (chosen.Addons.Count > 0 ? " - " + Mb(chosen.Bytes) + " in all" : "")
                     + (choice != null ? " (the game's options)" : ""));
            return chosen;
        }

        // ── helpers ──────────────────────────────────────────────────────────

        private static string Full(string p) { try { return System.IO.Path.GetFullPath(p); } catch { return p; } }

        /// <summary>"01.22" as a version; anything unreadable sorts lowest.</summary>
        internal static Version VersionOf(string appVer)
        {
            if (string.IsNullOrWhiteSpace(appVer)) return new Version(0, 0);
            var parts = appVer.Trim().Split('.');
            int.TryParse(parts[0], out var major);
            int minor = 0;
            if (parts.Length > 1) int.TryParse(parts[1], out minor);
            return new Version(Math.Max(0, major), Math.Max(0, minor));
        }

        internal static string Mb(long bytes) => (bytes / (1024.0 * 1024.0)).ToString("0.0") + " MB";
    }
}
