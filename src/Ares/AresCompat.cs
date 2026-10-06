// ares's own compatibility list (ares-emu.net/compatibility - Mehdi, 06/10: "il manque toute la gestion de liste de
// compatibilité de ares"), as the pack's other plugins have theirs (Xemu, Xenia, PPSSPP, Vita3K, Demul).
//
// THE SITE has no data file: one page per system (its slug: "nintendo-nintendo-64"), filtered by status
// (?status=4000 Completable, 3000 In-Game, 2000 Starts, 1000 Nothing, 0 Unknown) and paged by 20 (&page=N) - read
// 06/10. A row: <td>title</td><td><span>status</span>[<span>Unverified</span>]</td><td>version</td><td>notes</td>
// <td>issues</td>. Titles are No-Intro names ("007 - The World Is Not Enough (USA)"). Unknown is never fetched: most of
// the list (ZX Spectrum, PlayStation, Atari 2600...) is untested, and a game not in the list says the same.
//
// THE DATA: a snapshot is embedded (ares-compat.tsv, made with the probe's --ares-compat-fetch); a list fetched since
// (ares-compat.tsv beside the plugin's settings) wins. It is fetched again ONLY on the ares page's "Update the list now" (Mehdi,
// 06/10: reading the whole site is slow - never by itself),
// one page at a time with a pause between - the site is ares's own, not a CDN to hammer.
//
// A GAME is found by its file's name (No-Intro), else by its LaunchBox title with the region of its file, if any.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace LbIntegrations.Ares
{
    internal sealed class AresCompatEntry
    {
        public string Slug, Title, Status, Version, Notes;
        public bool Unverified;
        public int Issues;

        /// <summary>"Completable (unverified), v130" - short.</summary>
        public string Summary => Status + (Unverified ? " (unverified)" : "") + (string.IsNullOrEmpty(Version) ? "" : ", " + Version);
    }

    internal static class AresCompat
    {
        public const string Site = "https://ares-emu.net/compatibility/";
        private const string FileName = "ares-compat.tsv";
        private static readonly (string Status, int Code)[] Statuses = { ("Completable", 4000), ("In-Game", 3000), ("Starts", 2000), ("Nothing", 1000) };

        /// <summary>The site's page of each system ares runs (its slug). Mega CD 32X and SuperGrafx CD games are in the
        /// Mega CD and PC Engine CD pages; SC-3000 has none.</summary>
        public static readonly Dictionary<string, string> SlugOf = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Atari 2600"] = "atari-atari-2600", ["ColecoVision"] = "coleco-colecovision",
            ["Famicom"] = "nintendo-nes-famicom", ["Dendy"] = "nintendo-nes-famicom", ["Famicom Disk System"] = "nintendo-famicom-disk-system",
            ["Super Famicom"] = "nintendo-snes-super-famicom", ["Nintendo 64"] = "nintendo-nintendo-64", ["Nintendo 64DD"] = "nintendo-nintendo-64dd",
            ["Game Boy"] = "nintendo-game-boy", ["Game Boy Color"] = "nintendo-game-boy-color", ["Game Boy Advance"] = "nintendo-game-boy-advance",
            ["SG-1000"] = "sega-sg-1000", ["Master System"] = "sega-master-system-mark-iii", ["Game Gear"] = "sega-game-gear",
            ["Mega Drive"] = "sega-mega-drive-genesis", ["Mega 32X"] = "sega-32x", ["Mega CD"] = "sega-mega-cd", ["Mega CD 32X"] = "sega-mega-cd",
            ["LaserActive (SEGA PAC)"] = "pioneer-laseractive", ["LaserActive (NEC PAC)"] = "pioneer-laseractive",
            ["PC Engine"] = "nec-pc-engine-turbografx", ["SuperGrafx"] = "nec-pc-engine-supergrafx",
            ["PC Engine CD"] = "nec-pc-engine-cd-turbografx-cd", ["SuperGrafx CD"] = "nec-pc-engine-cd-turbografx-cd",
            ["Neo Geo AES"] = "snk-neo-geo-aesmvs", ["Neo Geo MVS"] = "snk-neo-geo-aesmvs",
            ["Neo Geo Pocket"] = "snk-neo-geo-pocket", ["Neo Geo Pocket Color"] = "snk-neo-geo-pocket-color",
            ["WonderSwan"] = "bandai-wonderswan", ["WonderSwan Color"] = "bandai-wonderswan-color", ["Pocket Challenge V2"] = "benesse-pocket-challenge-v2",
            ["PlayStation"] = "sony-playstation", ["MSX"] = "microsoft-msx", ["MSX2"] = "microsoft-msx2",
            ["ZX Spectrum"] = "sinclair-zx-spectrum", ["ZX Spectrum 128"] = "sinclair-zx-spectrum",
        };

        private static readonly object Gate = new object();
        private static List<AresCompatEntry> _entries;
        private static Dictionary<string, Dictionary<string, AresCompatEntry>> _byName;
        private static DateTime? _when;
        private static bool _embedded;
        private static int _fetching;

        /// <summary>For the probe: the cache somewhere else than beside the settings.</summary>
        internal static string PathOverride;

        public static string CachePath => PathOverride ?? Path.Combine(Path.GetDirectoryName(AresSettings.SettingsPath), FileName);

        // ── the list ─────────────────────────────────────────────────────────

        private static void Load()
        {
            if (_entries != null) return;
            lock (Gate)
            {
                if (_entries != null) return;
                string text = null;
                try { if (File.Exists(CachePath)) { text = File.ReadAllText(CachePath, Encoding.UTF8); _embedded = false; } }
                catch (Exception ex) { Log.Warn("compatibility: the list fetched could not be read", ex); }
                if (text == null)
                {
                    try
                    {
                        using var s = typeof(AresCompat).Assembly.GetManifestResourceStream(FileName);
                        if (s != null) using (var r = new StreamReader(s, Encoding.UTF8)) text = r.ReadToEnd();
                        _embedded = true;
                    }
                    catch (Exception ex) { Log.Warn("compatibility: the embedded list could not be read", ex); }
                }
                Parse(text ?? "");
            }
        }

        private static void Parse(string text)
        {
            var entries = new List<AresCompatEntry>();
            DateTime? when = null;
            foreach (var line in text.Replace("\r", "").Split('\n'))
            {
                if (line.StartsWith("# fetched "))
                {
                    if (DateTime.TryParse(line.Substring(10).Trim(), CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var d)) when = d;
                    continue;
                }
                if (line.Length == 0 || line.StartsWith("#")) continue;
                var f = line.Split('\t');
                if (f.Length < 7) continue;
                entries.Add(new AresCompatEntry { Slug = f[0], Title = f[1], Status = f[2], Unverified = f[3] == "1", Version = f[4], Notes = f[5], Issues = int.TryParse(f[6], out var n) ? n : 0 });
            }
            var byName = new Dictionary<string, Dictionary<string, AresCompatEntry>>(StringComparer.Ordinal);
            foreach (var e in entries)
            {
                if (!byName.TryGetValue(e.Slug, out var d)) byName[e.Slug] = d = new Dictionary<string, AresCompatEntry>(StringComparer.Ordinal);
                d[Norm(e.Title)] = e;
            }
            _entries = entries; _byName = byName; _when = when;
        }

        /// <summary>How many games the list has, from when, and whether it is the embedded one.</summary>
        public static (int Count, DateTime? When, bool Embedded) Info()
        {
            Load();
            return (_entries.Count, _when, _embedded);
        }

        // ── a game ───────────────────────────────────────────────────────────

        private static string Norm(string s) => Regex.Replace((s ?? "").ToLowerInvariant().Trim(), @"\s+", " ");

        /// <summary>A title as words only: "Legend of Zelda, The - A Link to the Past" and "The Legend of Zelda: A Link to
        /// the Past" alike.</summary>
        private static string Words(string s)
        {
            s = (s ?? "").ToLowerInvariant();
            s = Regex.Replace(s, @"\([^)]*\)|\[[^\]]*\]", " ");
            s = Regex.Replace(s, @",\s*the\b", " ");
            s = Regex.Replace(s, @"^\s*the\s+", " ");
            s = Regex.Replace(s, @"[^a-z0-9]+", " ");
            s = Regex.Replace(s, @"^\s*the\s+", " ");
            return Regex.Replace(s, @"\s+", " ").Trim();
        }

        private static readonly string[] Regions = { "USA", "Europe", "Japan", "World", "France", "Germany", "Spain", "Italy", "Korea", "Brazil", "Australia" };

        /// <summary>The game's entry: by its file's name, else by its title (and the region its file says). Null when the
        /// list does not have it. <paramref name="how"/> says which.</summary>
        public static AresCompatEntry Find(string system, string rom, string title, out string how)
        {
            how = null;
            Load();
            if (system == null || !SlugOf.TryGetValue(system, out var slug) || !_byName.TryGetValue(slug, out var names)) return null;
            var stem = Path.GetFileNameWithoutExtension(rom ?? "");
            if (names.TryGetValue(Norm(stem), out var hit)) { how = "by its file's name"; return hit; }
            var noDisc = Regex.Replace(stem, @"\s*\(Disc \d+\)", "", RegexOptions.IgnoreCase);
            if (noDisc != stem && names.TryGetValue(Norm(noDisc), out hit)) { how = "by its file's name"; return hit; }
            foreach (var key in new[] { Words(stem), Words(title) }.Where(k => k.Length > 0).Distinct())
            {
                var candidates = names.Values.Where(e => Words(e.Title) == key).ToList();
                if (candidates.Count == 0) continue;
                var region = Regions.FirstOrDefault(r => stem.IndexOf("(" + r, StringComparison.OrdinalIgnoreCase) >= 0 || stem.IndexOf(", " + r, StringComparison.OrdinalIgnoreCase) >= 0);
                hit = (region != null ? candidates.FirstOrDefault(e => e.Title.IndexOf(region, StringComparison.OrdinalIgnoreCase) >= 0) : null)
                      ?? candidates.FirstOrDefault(e => e.Title.Contains("(USA") || e.Title.Contains("(World")) ?? candidates[0];
                how = "by its title";
                return hit;
            }
            return null;
        }

        // ── fetching ─────────────────────────────────────────────────────────

        /// <summary>The whole list read from the site into <paramref name="path"/> - every system, every status but
        /// Unknown, page after page. Returns how many games; throws when the site cannot be read at all.</summary>
        public static int Fetch(string path, Action<string> progress = null, CancellationToken cancel = default)
        {
            var entries = new List<AresCompatEntry>();
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("Nixx-ares/1.0 (LaunchBox plugin; compatibility list, on the user's request)");
            int pages = 0;
            foreach (var slug in SlugOf.Values.Distinct())
            {
                foreach (var (status, code) in Statuses)
                {
                    string firstOfPrevious = null;
                    for (int page = 1; page < 2000; page++)
                    {
                        cancel.ThrowIfCancellationRequested();
                        var url = Site + slug + "?status=" + code + (page > 1 ? "&page=" + page : "");
                        string html;
                        try { html = http.GetStringAsync(url).GetAwaiter().GetResult(); }
                        catch (Exception ex) { throw new IOException("could not read " + url + ": " + ex.Message, ex); }
                        pages++;
                        var rows = Rows(html, slug).ToList();
                        if (rows.Count == 0 || rows[0].Title == firstOfPrevious) break;
                        firstOfPrevious = rows[0].Title;
                        entries.AddRange(rows);
                        progress?.Invoke(slug + " " + status + " page " + page + " (" + entries.Count + " games)");
                        Thread.Sleep(250);
                        if (rows.Count < 20) break;
                    }
                }
            }
            var sb = new StringBuilder();
            sb.Append("# ares compatibility list, from ").Append(Site).Append(" - slug, title, status, unverified, version, notes, issues\n");
            sb.Append("# fetched ").Append(DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture)).Append('\n');
            foreach (var e in entries.GroupBy(e => e.Slug + "\t" + e.Title).Select(g => g.First()))
                sb.Append(string.Join("\t", e.Slug, e.Title, e.Status, e.Unverified ? "1" : "0", e.Version, e.Notes, e.Issues.ToString(CultureInfo.InvariantCulture))).Append('\n');
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
            var tmp = path + ".part";
            File.WriteAllText(tmp, sb.ToString(), new UTF8Encoding(false));
            if (File.Exists(path)) File.Delete(path);
            File.Move(tmp, path);
            Log.Info("compatibility: " + entries.Count + " games read from " + pages + " page(s) of " + Site);
            return entries.Count;
        }

        private static string Clean(string html)
        {
            var s = Regex.Replace(html ?? "", "<[^>]+>", " ");
            s = WebUtility.HtmlDecode(s);
            return Regex.Replace(s, @"\s+", " ").Trim();
        }

        private static IEnumerable<AresCompatEntry> Rows(string html, string slug)
        {
            foreach (Match tr in Regex.Matches(html, @"<tr[\s>].*?</tr>", RegexOptions.Singleline))
            {
                var cells = Regex.Matches(tr.Value, @"<td[^>]*>(.*?)</td>", RegexOptions.Singleline).Cast<Match>().Select(m => m.Groups[1].Value).ToList();
                if (cells.Count < 4) continue;
                var title = Clean(cells[0]);
                if (title.Length == 0 || title.StartsWith("No results", StringComparison.OrdinalIgnoreCase)) continue;
                var spans = Regex.Matches(cells[1], @"<span[^>]*>(.*?)</span>", RegexOptions.Singleline).Cast<Match>().Select(m => Clean(m.Groups[1].Value)).ToList();
                var status = spans.FirstOrDefault(s => !s.Equals("Unverified", StringComparison.OrdinalIgnoreCase)) ?? Clean(cells[1]);
                yield return new AresCompatEntry
                {
                    Slug = slug, Title = title, Status = status,
                    Unverified = spans.Any(s => s.Equals("Unverified", StringComparison.OrdinalIgnoreCase)) || cells[1].IndexOf("Unverified", StringComparison.OrdinalIgnoreCase) >= 0,
                    Version = Clean(cells[2]), Notes = Clean(cells[3]).Replace('\t', ' '),
                    Issues = cells.Count > 4 && int.TryParse(Clean(cells[4]), out var n) ? n : 0,
                };
            }
        }

        /// <summary>The list fetched again in the background - the page's button only - then used. One at a time; never throws. <paramref name="done"/> is told the result.</summary>
        public static void Refresh(Action<string> done = null)
        {
            try
            {
                if (Interlocked.Exchange(ref _fetching, 1) == 1) return;
                Task.Run(() =>
                {
                    try
                    {
                        var n = Fetch(CachePath);
                        lock (Gate) { _entries = null; }
                        Load();
                        done?.Invoke(n + " games, from today");
                    }
                    catch (Exception ex) { Log.Warn("compatibility: the list could not be fetched", ex); done?.Invoke("not fetched: " + ex.Message); }
                    finally { Interlocked.Exchange(ref _fetching, 0); }
                });
            }
            catch (Exception ex) { Log.Warn("compatibility refresh", ex); }
        }

        public static bool Fetching => Volatile.Read(ref _fetching) == 1;
    }
}
