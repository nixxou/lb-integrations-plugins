// Every Vita file of a folder and its subfolders, sorted by what it is - games, updates, DLC - with the files that look
// like ours and are not, and why. Rebuilt on the Xbox 360's (src\Xenia\XeniaScan.cs, Mehdi 01/10): the content decides,
// never a name, and what was read is kept.
//
// WHICH FILES: the extensions Vita3kContent installs (.vpk, .zip, .pkg). Each one is read by its param.sfo
// (Vita3kContent.DescribeAll - an archive may hold several contents: the game, its update, its DLC), and each content is
// one entry, "<archive>|<root>". A file the rule takes that holds no Vita content is ONE entry, Invalid, with the reason -
// so it is not read again.
//
// WHICH FOLDER (FolderFor): the game's own folder - or, when the folder above it is named with "vita" (any case, Mehdi
// 01/10), that one, all of it. Never a drive's root.
//
// THE CACHE: <plugin data>\vita-scan.tsv, one line per entry, kept while its archive's size and date are the same. Looking
// again walks the folder - cheap - and reads only what is new or changed; what is gone is dropped. Plain text, readable.
// A file MOVED (Mehdi, 03/10) - in the cache under another folder with the same name, size and date - is taken from its
// old lines, not read again.
//
// It replaces the four naming rules and the import's index (lbip-vita-extras.tsv, gone): a game's updates and DLC are
// whatever the cache knows of its title id, wherever it was scanned - its folder, or an import's.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace LbIntegrations.Vita3k
{
    internal sealed class VitaScanEntry
    {
        public string Path;             // "<archive>|<root>" - "<archive>|" for an invalid file
        public long Size, Ticks;        // the archive's
        public string TitleId = "", Category = "", ContentId = "", Title = "", FullTitle = "", AppVer = "", Root = "";
        public List<string> Nested = new List<string>();
        public long Bytes;
        public string Problem = "";     // why it holds no Vita content

        public string Archive => Path.Substring(0, Path.IndexOf('|'));
        public bool Invalid => Problem.Length > 0;

        /// <summary>The same entry, for the same archive at another path - a file moved.</summary>
        public VitaScanEntry MovedTo(string archive)
        {
            var c = (VitaScanEntry)MemberwiseClone();
            c.Path = archive + Path.Substring(Path.IndexOf('|'));
            c.Nested = new List<string>(Nested);
            return c;
        }

        /// <summary>The content as Vita3kContent described it, for installing and choosing.</summary>
        public VitaContent ToContent() => new VitaContent
        {
            TitleId = TitleId, Category = Category, ContentId = Nul(ContentId), Title = Nul(Title), FullTitle = Nul(FullTitle),
            AppVer = Nul(AppVer), Root = Root, Nested = new List<string>(Nested), Bytes = Bytes,
        };

        private static string Nul(string s) => string.IsNullOrEmpty(s) ? null : s;

        public override string ToString()
            => System.IO.Path.GetFileName(Archive) + (Root.Length > 0 ? " > " + Root : "") + " -> "
               + (Invalid ? "Invalid - " + Problem : TitleId + " [" + Category + "]" + (AppVer.Length > 0 ? " " + AppVer : "") + (Title.Length > 0 ? " " + Title : ""));
    }

    internal static class Vita3kScan
    {
        private static readonly object Gate = new object();

#pragma warning disable CS0649
        /// <summary>For the probe: the cache somewhere else. Set by reflection.</summary>
        internal static string CacheOverride;
#pragma warning restore CS0649

        public static string CachePath
            => CacheOverride ?? System.IO.Path.Combine(System.IO.Path.GetDirectoryName(Vita3kSettings.SettingsPath), "vita-scan.tsv");

        /// <summary>The folder a game's scan covers - see the header.</summary>
        public static string FolderFor(string rom)
        {
            if (string.IsNullOrWhiteSpace(rom)) return null;
            try
            {
                var full = System.IO.Path.GetFullPath(rom);
                var own = Directory.Exists(full) ? System.IO.Path.GetDirectoryName(full.TrimEnd('\\')) : System.IO.Path.GetDirectoryName(full);
                var above = own == null ? null : System.IO.Path.GetDirectoryName(own);
                if (above != null && System.IO.Path.GetDirectoryName(above) != null
                    && System.IO.Path.GetFileName(above).IndexOf("vita", StringComparison.OrdinalIgnoreCase) >= 0) return above;
                return own;
            }
            catch { return null; }
        }

        /// <summary>Everything under <paramref name="root"/>, read where the cache does not already know it. Never throws.</summary>
        public static List<VitaScanEntry> Scan(string root, Action<string, double?> progress = null, Func<bool> cancel = null)
        {
            var result = new List<VitaScanEntry>();
            if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root)) return result;
            root = System.IO.Path.GetFullPath(root);
            var started = DateTime.UtcNow;
            progress?.Invoke("Looking for Vita files...", null);

            bool driveRoot = string.Equals(root.TrimEnd('\\'), (System.IO.Path.GetPathRoot(root) ?? "").TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);
            var files = new List<string>();
            Walk(root, files, deep: !driveRoot);

            var cache = Load();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int done = 0, read = 0;
            bool stopped = false;
            foreach (var path in files)
            {
                if (cancel?.Invoke() == true) { stopped = true; break; }
                done++;
                long size, ticks;
                try { var fi = new FileInfo(path); size = fi.Length; ticks = fi.LastWriteTimeUtc.Ticks; } catch { continue; }
                var prefix = path + "|";
                var known = cache.Values.Where(e => e.Path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).ToList();
                if (known.Count == 0)
                {
                    // Moved: the same name, size and date under another folder.
                    var name = System.IO.Path.GetFileName(path);
                    var old = cache.Values.Where(e => e.Size == size && e.Ticks == ticks
                                                      && string.Equals(System.IO.Path.GetFileName(e.Archive), name, StringComparison.OrdinalIgnoreCase))
                                          .GroupBy(e => e.Archive, StringComparer.OrdinalIgnoreCase).FirstOrDefault();
                    if (old != null)
                    {
                        Log.Info("scan: " + name + " known from its old place, " + old.Key + " (same name, size and date)");
                        known = old.Select(e => e.MovedTo(path)).ToList();
                        foreach (var e in known) cache[e.Path] = e;
                    }
                }
                if (known.Count > 0 && known.All(e => e.Size == size && e.Ticks == ticks))
                {
                    result.AddRange(known);
                    foreach (var e in known) seen.Add(e.Path);
                    continue;
                }
                progress?.Invoke("Reading " + System.IO.Path.GetFileName(path), (double)done / Math.Max(1, files.Count));
                foreach (var stale in known) cache.Remove(stale.Path);
                foreach (var e in Classify(path, size, ticks))
                {
                    cache[e.Path] = e;
                    seen.Add(e.Path);
                    result.Add(e);
                }
                read++;
            }

            var under = root.TrimEnd('\\') + "\\";
            if (!stopped)
                foreach (var gone in cache.Keys.Where(k => k.StartsWith(under, StringComparison.OrdinalIgnoreCase) && !seen.Contains(k)
                                                            && (driveRoot ? !k.Substring(under.Length).Contains('\\') : true)).ToList())
                    cache.Remove(gone);
            Save(cache);
            Log.Info("scan of " + root + ": " + files.Count + " file(s), " + read + " read, " + (files.Count - read) + " from the cache - "
                     + result.Count(e => !e.Invalid && Vita3kKind(e) == "game") + " game(s), " + result.Count(e => Vita3kKind(e) == "update") + " update(s), "
                     + result.Count(e => Vita3kKind(e) == "dlc") + " DLC, " + result.Count(e => e.Invalid) + " invalid - "
                     + (int)(DateTime.UtcNow - started).TotalMilliseconds + " ms");
            return result;
        }

        private static string Vita3kKind(VitaScanEntry e)
        {
            if (e.Invalid) return "invalid";
            var c = e.ToContent();
            return c.IsPatch ? "update" : c.IsAddon ? "dlc" : "game";
        }

        /// <summary>Everything the cache knows whose archive is still there, wherever it was scanned.</summary>
        public static List<VitaScanEntry> CachedAll()
            => Load().Values.Where(e => { try { return File.Exists(e.Archive); } catch { return false; } }).ToList();

        /// <summary>One archive's contents, read - one Invalid entry when it holds none.</summary>
        internal static List<VitaScanEntry> Classify(string path, long size, long ticks)
        {
            var list = new List<VitaScanEntry>();
            string error = null;
            List<VitaContent> all = null;
            try { all = Vita3kContent.DescribeAll(path, out error); }
            catch (Exception ex) { error = ex.Message; }
            if (all == null || all.Count == 0)
            {
                list.Add(new VitaScanEntry { Path = path + "|", Size = size, Ticks = ticks, Problem = string.IsNullOrWhiteSpace(error) ? "no Vita content in it" : error.Replace('\t', ' ').Replace('\n', ' ') });
                return list;
            }
            foreach (var c in all)
                list.Add(new VitaScanEntry
                {
                    Path = path + "|" + (c.Root ?? ""), Size = size, Ticks = ticks,
                    TitleId = c.TitleId ?? "", Category = c.Category ?? "", ContentId = c.ContentId ?? "", Title = c.Title ?? "",
                    FullTitle = c.FullTitle ?? "", AppVer = c.AppVer ?? "", Root = c.Root ?? "", Nested = new List<string>(c.Nested ?? new List<string>()), Bytes = c.Bytes,
                });
            return list;
        }

        private static void Walk(string folder, List<string> files, bool deep)
        {
            string[] subs, names;
            try { subs = Directory.GetDirectories(folder); names = Directory.GetFiles(folder); }
            catch (Exception ex) { Log.Info("scan: " + folder + " skipped (" + ex.Message + ")"); return; }
            foreach (var f in names) if (Vita3kContent.Installable(f)) files.Add(f);
            if (!deep) return;
            foreach (var d in subs)
            {
                try { if ((File.GetAttributes(d) & (FileAttributes.ReparsePoint | FileAttributes.System)) != 0) continue; } catch { continue; }
                Walk(d, files, true);
            }
        }

        // ── the cache file ───────────────────────────────────────────────────

        /// <summary>The first line - and the format's version: a cache written by another one is read again from the files.</summary>
        private const string Header = "v1\tpath\tsize\tticks\ttitle_id\tcategory\tcontent_id\ttitle\tfull_title\tapp_ver\troot\tnested\tbytes\tproblem";

        private static Dictionary<string, VitaScanEntry> Load()
        {
            var map = new Dictionary<string, VitaScanEntry>(StringComparer.OrdinalIgnoreCase);
            lock (Gate)
            {
                try
                {
                    if (!File.Exists(CachePath)) return map;
                    var lines = File.ReadAllLines(CachePath, Encoding.UTF8);
                    if (lines.Length == 0 || lines[0] != Header) return map;
                    foreach (var line in lines.Skip(1))
                    {
                        var c = line.Split('\t');
                        if (c.Length < 13 || c[0].IndexOf('|') < 0) continue;
                        map[c[0]] = new VitaScanEntry
                        {
                            Path = c[0], Size = L(c[1]), Ticks = L(c[2]), TitleId = c[3], Category = c[4], ContentId = c[5], Title = c[6], FullTitle = c[7],
                            AppVer = c[8], Root = c[9], Nested = c[10].Length == 0 ? new List<string>() : c[10].Split(';').ToList(), Bytes = L(c[11]), Problem = c[12],
                        };
                    }
                }
                catch (Exception ex) { Log.Warn("scan cache: could not be read, starting over", ex); }
            }
            return map;
        }

        private static void Save(Dictionary<string, VitaScanEntry> map)
        {
            lock (Gate)
            {
                try
                {
                    Directory.CreateDirectory(System.IO.Path.GetDirectoryName(CachePath));
                    var lines = new List<string> { Header };
                    lines.AddRange(map.Values.OrderBy(e => e.Path, StringComparer.OrdinalIgnoreCase).Select(e => string.Join("\t",
                        e.Path, e.Size.ToString(CultureInfo.InvariantCulture), e.Ticks.ToString(CultureInfo.InvariantCulture), Clean(e.TitleId), Clean(e.Category),
                        Clean(e.ContentId), Clean(e.Title), Clean(e.FullTitle), Clean(e.AppVer), Clean(e.Root), string.Join(";", e.Nested.Select(Clean)),
                        e.Bytes.ToString(CultureInfo.InvariantCulture), Clean(e.Problem))));
                    var tmp = CachePath + ".tmp";
                    File.WriteAllLines(tmp, lines, new UTF8Encoding(false));
                    File.Move(tmp, CachePath, overwrite: true);
                }
                catch (Exception ex) { Log.Warn("scan cache: could not be written", ex); }
            }
        }

        private static string Clean(string s) => (s ?? "").Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ');
        private static long L(string s) => long.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : 0;
    }
}
