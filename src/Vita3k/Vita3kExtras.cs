// The updates and DLC of the game being launched: where they are looked for, how they are recognised,
// which are kept. Installed right after the game and BEFORE the reference walk - anything installed
// after it would come out of the session as a change, into the save.
//
// WHERE, four places, all around the game's own file (Mehdi's rules):
//   1. beside the game, a file whose NAME carries the title id
//   2. the same, in a DLC or UPDATE sub-folder (plural and PATCH too)
//   3. a folder NAMED the title id, anywhere under the game's folder: every archive in it
//   4. a folder named the game, anywhere under it: every archive in it. "Named the game" is by
//      TitleKey - ExtendDB's add-time normalisation - against the param.sfo's title, its short
//      title, and the title the host shows. The game's own folder counts too.
// None of that decides anything: it only nominates. A candidate is KEPT only when its own
// param.sfo says so - same TITLE_ID as the game, CATEGORY gp (an update) or ac (a DLC). File and
// folder names are somebody's naming; the param.sfo is Sony's.
//
// And the game's OWN ARCHIVE, first: a zip that holds the game with its update and DLC (see
// Vita3kContent). Any archive nominated may hold several contents too - each is read and weighed.
//
// And a fifth: what the import found in the same batch of files, recorded in the emulator's folder
// (Vita3kExtrasIndex) - for extras kept nowhere the four rules look.
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
        private static readonly string[] ExtraFolders = { "DLC", "DLCS", "UPDATE", "UPDATES", "PATCH", "PATCHES" };

        /// <summary>How deep folders are looked into from the game's own. A ROM folder can be a whole
        /// library; listing folders is cheap, but not without end.</summary>
        private const int MaxDepth = 4;

        /// <summary>A ceiling on archives evaluated, for a folder rule that turns out to match a folder
        /// full of unrelated games.</summary>
        private const int MaxCandidates = 400;

        /// <summary>Find, evaluate and choose the updates and DLC of <paramref name="game"/>: every one
        /// found, then the choice - <paramref name="choice"/>'s, or the automatic one.</summary>
        public static VitaExtras For(string romPath, VitaContent game, string hostTitle, string installDir = null,
                                     Vita3kExtrasChoice choice = null)
            => Choose(Evaluate(romPath, game, hostTitle, installDir), choice);

        /// <summary>Everything that belongs to the game: every valid update, every DLC (one per
        /// CONTENT_ID) - nothing chosen yet.</summary>
        public static FoundExtras Evaluate(string romPath, VitaContent game, string hostTitle, string installDir = null)
        {
            var found = new FoundExtras();
            try
            {
                if (game?.TitleId == null || string.IsNullOrWhiteSpace(romPath)) return found;
                var candidates = Candidates(romPath, game, hostTitle);

                // THE IMPORT'S FINDINGS, after the four rules: a file they already nominated keeps its rule.
                var nominated = new HashSet<string>(candidates.Select(c => Full(c.path)), StringComparer.OrdinalIgnoreCase) { Full(romPath) };
                // Hints: each is read below and kept only when its param.sfo says it is the game's.
                foreach (var path in Vita3kExtrasIndex.For(installDir, game.TitleId, romPath))
                    if (nominated.Add(Full(path))) candidates.Add((path, "found with it at import"));

                Log.Info("updates and DLC of " + game.TitleId + ": " + candidates.Count + " candidate archive(s) around "
                         + System.IO.Path.GetDirectoryName(romPath));

                // Every content of every archive: the game's own first (what it holds beside the game),
                // then each one nominated.
                var contents = new List<(string path, string rule, VitaContent content)>();
                foreach (var c in Vita3kContent.DescribeAll(romPath, out _) ?? new List<VitaContent>())
                    if (!c.IsGame) contents.Add((romPath, "in the game's own archive", c));
                foreach (var (path, rule) in candidates)
                {
                    var all = Vita3kContent.DescribeAll(path, out var error);
                    if (all == null) { Log.Info("  set aside " + System.IO.Path.GetFileName(path) + " [" + rule + "] - " + error); continue; }
                    foreach (var c in all) contents.Add((path, rule, c));
                }

                var addons = new Dictionary<string, VitaExtra>(StringComparer.OrdinalIgnoreCase);
                foreach (var (path, rule, content) in contents)
                {
                    var name = System.IO.Path.GetFileName(path) + (content.Root.Length > 0 ? " > " + content.Root : "");
                    if (!string.Equals(content.TitleId, game.TitleId, StringComparison.OrdinalIgnoreCase))
                    { Log.Info("  set aside " + name + " [" + rule + "] - it is " + content.TitleId + "'s, not " + game.TitleId + "'s"); continue; }

                    var extra = new VitaExtra
                    {
                        Path = path, Content = content, FoundBy = rule,
                        Bytes = Math.Max(0, content.Bytes),
                    };
                    if (content.IsPatch)
                    {
                        found.Updates.Add(extra);
                        Log.Info("  update " + (content.AppVer ?? "?") + ": " + name + " [" + rule + "], " + Mb(extra.Bytes));
                    }
                    else if (content.IsAddon)
                    {
                        var id = content.ContentId ?? name;
                        if (addons.TryGetValue(id, out var already))
                        { Log.Info("  set aside " + name + " [" + rule + "] - the same DLC (" + id + ") as " + already.Name); continue; }
                        addons[id] = extra;
                        Log.Info("  DLC " + (content.Title ?? id) + " (" + id + "): " + name + " [" + rule + "], " + Mb(extra.Bytes));
                    }
                    else Log.Info("  set aside " + name + " [" + rule + "] - category " + (content.Category ?? "?") + " is neither an update nor a DLC");
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

        /// <summary>The archives the four rules nominate, each with the rule that did, in the order
        /// found. The game itself is never one of them.</summary>
        public static List<(string path, string rule)> Candidates(string romPath, VitaContent game, string hostTitle)
        {
            var found = new List<(string, string)>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { Full(romPath) };
            var dir = System.IO.Path.GetDirectoryName(Full(romPath));
            if (dir == null || !Directory.Exists(dir)) return found;

            var id = game.TitleId;
            var keys = new HashSet<string>(StringComparer.Ordinal);
            foreach (var t in new[] { game.FullTitle, game.Title, hostTitle })
            {
                var k = TitleKey.Of(t);
                if (k != null) keys.Add(k);
            }
            Log.Info("  looking by title id " + id + " and by title key" + (keys.Count == 1 ? " " : "s ")
                     + (keys.Count > 0 ? string.Join(", ", keys) : "(none valid)"));

            void Add(string path, string rule)
            {
                if (found.Count >= MaxCandidates) return;
                if (!Vita3kContent.Installable(path)) return;
                if (seen.Add(Full(path))) found.Add((path, rule));
            }
            bool NamesTheId(string path) => System.IO.Path.GetFileName(path).IndexOf(id, StringComparison.OrdinalIgnoreCase) >= 0;
            bool NamedTheGame(string folder)
            {
                var name = System.IO.Path.GetFileName(folder.TrimEnd('\\', '/'));
                if (string.Equals(name, id, StringComparison.OrdinalIgnoreCase)) return true;
                var k = TitleKey.Of(name);
                return k != null && keys.Contains(k);
            }

            // 1. beside the game, the title id in the name
            foreach (var f in Files(dir, recursive: false))
                if (NamesTheId(f)) Add(f, "beside the game, " + id + " in its name");

            // 2. the same, in DLC / UPDATE folders
            foreach (var sub in Folders(dir, 1))
                if (ExtraFolders.Any(x => string.Equals(System.IO.Path.GetFileName(sub), x, StringComparison.OrdinalIgnoreCase)))
                    foreach (var f in Files(sub, recursive: true))
                        if (NamesTheId(f)) Add(f, System.IO.Path.GetFileName(sub) + "\\, " + id + " in its name");

            // 3 and 4. folders named the title id or the game - the game's own folder included
            var named = new List<string>();
            if (NamedTheGame(dir)) named.Add(dir);
            named.AddRange(Folders(dir, MaxDepth).Where(NamedTheGame));
            foreach (var folder in named)
            {
                var rule = string.Equals(System.IO.Path.GetFileName(folder), id, StringComparison.OrdinalIgnoreCase)
                    ? "in a folder named " + id : "in a folder named the game (" + System.IO.Path.GetFileName(folder) + ")";
                foreach (var f in Files(folder, recursive: true)) Add(f, rule);
            }
            return found;
        }

        // ── helpers ──────────────────────────────────────────────────────────

        private static IEnumerable<string> Files(string dir, bool recursive)
        {
            IEnumerable<string> list;
            try
            {
                list = Directory.EnumerateFiles(dir, "*", new EnumerationOptions
                {
                    RecurseSubdirectories = recursive, MaxRecursionDepth = MaxDepth,
                    IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint | FileAttributes.System,
                }).ToList();
            }
            catch { return new string[0]; }
            return list.OrderBy(p => p, StringComparer.OrdinalIgnoreCase);
        }

        private static IEnumerable<string> Folders(string dir, int depth)
        {
            try
            {
                return Directory.EnumerateDirectories(dir, "*", new EnumerationOptions
                {
                    RecurseSubdirectories = depth > 1, MaxRecursionDepth = Math.Max(0, depth - 1),
                    IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint | FileAttributes.System,
                }).OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToList();
            }
            catch { return new string[0]; }
        }

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
