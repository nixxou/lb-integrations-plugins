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
// A GAME may match SEVERAL lines (its regions, its versions - Mehdi, 06/10): all are found (FindAll) - by its file's
// No-Intro name, then by LiteBox's CompareName key of its file's name, title, alternate names and database entry.

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

        internal string Key;   // its CompareName (AresCompat.Key)

        /// <summary>"Completable (unverified), v130" - short.</summary>
        public string Summary => Status + (Unverified ? " (unverified)" : "") + (string.IsNullOrEmpty(Version) ? "" : ", " + Version);
    }

    /// <summary>A line of the list for a game, and how it was found; Best: the one that is this very file.</summary>
    internal sealed class AresCompatMatch
    {
        public AresCompatEntry Entry;
        public string How;
        public bool Best;
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
        private static Dictionary<string, List<AresCompatEntry>> _bySlug;
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
            var bySlug = new Dictionary<string, List<AresCompatEntry>>(StringComparer.Ordinal);
            foreach (var e in entries)
            {
                e.Key = Key(e.Title);
                if (!bySlug.TryGetValue(e.Slug, out var l)) bySlug[e.Slug] = l = new List<AresCompatEntry>();
                l.Add(e);
            }
            // _entries last: Load's check outside the lock reads it, and FindAll then reads _bySlug - set before it, never seen null.
            _bySlug = bySlug; _when = when; _entries = entries;
        }

        /// <summary>The list in memory, as an identity: another object once the list is read again (Refresh) - what the
        /// "Launch With" verdicts (AresLaunchWith) keep theirs by.</summary>
        internal static object Generation { get { Load(); return _entries; } }

        /// <summary>Whether the list is in memory already - reading it takes a second (fifteen thousand titles, their keys).</summary>
        internal static bool Loaded => _entries != null;

        /// <summary>How many games the list has, from when, and whether it is the embedded one.</summary>
        public static (int Count, DateTime? When, bool Embedded) Info()
        {
            Load();
            return (_entries.Count, _when, _embedded);
        }

        // ── a game ───────────────────────────────────────────────────────────

        private static string Norm(string s) => Regex.Replace((s ?? "").ToLowerInvariant().Trim(), @"\s+", " ");

        // THE KEY (Mehdi, 06/10): LiteBox's CompareName - a faithful copy of LbApiHost\Host\Web\Db\TitleNormalizer.cs
        // (PerformSanitize, then NormalizeCompareName), so a title gives here the key it gives there and in ExtendDB.
        // "Legend of Zelda, The - A Link to the Past (USA)" and "The Legend of Zelda: A Link to the Past" give one key. A
        // key the normalizer says is not valid (under 5 characters, or more than 20% of the title dropped) falls back
        // to the sanitized title itself, prefixed, so it only ever meets the same short title.
        internal static string Key(string title)
        {
            if (string.IsNullOrWhiteSpace(title)) return null;
            var sanitized = PerformSanitize(title);
            var compact = NormalizeCompareName(sanitized, out var valid);
            return valid ? compact : sanitized.Length > 0 ? "~" + sanitized : null;
        }

        private static string NormalizeCompareName(string input, out bool isValid)
        {
            isValid = false;
            if (string.IsNullOrEmpty(input)) return null;
            if (input.Trim().EndsWith("+") || input.Trim().EndsWith("#")) return null;
            int totalCharsForRatio = input.Count(c => !char.IsWhiteSpace(c));
            string normalized = input.Normalize(NormalizationForm.FormD);
            var sb = new StringBuilder();
            foreach (char c in normalized)
            {
                var uc = CharUnicodeInfo.GetUnicodeCategory(c);
                if (uc != UnicodeCategory.NonSpacingMark && char.IsLetterOrDigit(c)) sb.Append(char.ToUpperInvariant(c));
            }
            string result = Regex.Replace(sb.ToString(), @"[^A-Z0-9]", "");
            int replacements = totalCharsForRatio - result.Length;
            isValid = result.Length >= 5 && ((double)replacements / totalCharsForRatio) <= 0.2;
            return string.IsNullOrEmpty(result) ? null : result;
        }

        private static string PerformSanitize(string name)
        {
            string sanitized = name;
            sanitized = StripBrackets(sanitized, @"(?<=\S)?\([^)]*\)(?=\S)?");
            sanitized = StripBrackets(sanitized, @"(?<=\S)?\[[^\]]*\](?=\S)?");
            sanitized = StripBrackets(sanitized, @"(?<=\S)?\{[^}]*\}(?=\S)?");
            var invalidChars = Path.GetInvalidFileNameChars().Where(c => c != '*' && c != '`' && c != '|' && c != '>' && c != '<' && c != '~').ToArray();
            char[] toSpace = { '-', ':', '&', '!', ',', '/', '\\', '?' };
            char[] toVoid = { '\'', '.', '"' };
            sanitized = new string(sanitized.Select(c =>
            {
                if (invalidChars.Contains(c) || toSpace.Contains(c)) return ' ';
                if (toVoid.Contains(c)) return '\0';
                return c;
            }).Where(c => c != '\0').ToArray());
            sanitized = Regex.Replace(sanitized, " {2,}", " ").Trim();
            sanitized = Regex.Replace(sanitized, @"(?<=^|\s)II(?=\s|$)", "2");
            sanitized = Regex.Replace(sanitized, @"(?<=^|\s)III(?=\s|$)", "3");
            sanitized = Regex.Replace(sanitized, @"(?<=^|\s)IV(?=\s|$)", "4");
            sanitized = Regex.Replace(sanitized, @"(?<=^|\s)VIII(?=\s|$)", "8");
            sanitized = Regex.Replace(sanitized, @"(?<=^|\s)VII(?=\s|$)", "7");
            sanitized = Regex.Replace(sanitized, @"(?<=^|\s)VI(?=\s|$)", "6");
            sanitized = Regex.Replace(sanitized, @"(?<=^|\s)V(?=\s|$)", "5");
            sanitized = Regex.Replace(sanitized, @"(?<=^|\s)a(?=\s|$)", " ", RegexOptions.IgnoreCase);
            sanitized = Regex.Replace(sanitized, @"(?<=^|\s)an(?=\s|$)", " ", RegexOptions.IgnoreCase);
            sanitized = Regex.Replace(sanitized, @"(?<=^|\s)and(?=\s|$)", " ", RegexOptions.IgnoreCase);
            sanitized = Regex.Replace(sanitized, @"(?<=^|\s)the(?=\s|$)", "", RegexOptions.IgnoreCase);
            sanitized = Regex.Replace(sanitized, " {2,}", " ").Trim();
            return sanitized.ToUpper();
        }

        private static string StripBrackets(string input, string pattern)
            => Regex.Replace(input, pattern, match =>
            {
                char before = match.Index > 0 ? input[match.Index - 1] : ' ';
                char after = match.Index + match.Length < input.Length ? input[match.Index + match.Length] : ' ';
                return (!char.IsWhiteSpace(before) && !char.IsWhiteSpace(after)) ? "" : " ";
            });

        private static readonly string[] Regions = { "USA", "Europe", "Japan", "World", "France", "Germany", "Spain", "Italy", "Korea", "Brazil", "Australia" };

        /// <summary>The region a No-Intro name says - "(USA)", "(Europe, Australia)" - or null.</summary>
        private static string RegionOf(string name)
        {
            var region = Regions.FirstOrDefault(r => Regex.IsMatch(name ?? "", @"[(,]\s*" + r + @"\b", RegexOptions.IgnoreCase));
            if (region != null) return region;
            // GoodTools' codes - "Sonic CD (U)": the first letter of the group says the region.
            var g = Regex.Match(name ?? "", @"\((U|E|J|F|G|S|I|K|B|W|A|UE|JU|JUE|UJ|EU)\)");
            if (!g.Success) return null;
            switch (g.Groups[1].Value[0])
            {
                case 'U': return "USA"; case 'E': return "Europe"; case 'J': return "Japan"; case 'F': return "France"; case 'G': return "Germany";
                case 'S': return "Spain"; case 'I': return "Italy"; case 'K': return "Korea"; case 'B': return "Brazil"; case 'W': return "World";
                case 'A': return "Australia"; default: return null;
            }
        }

        /// <summary>EVERY line of the list for a game (Mehdi, 06/10: a game can match several - its regions, its
        /// versions), the best first:
        ///   1. its file's No-Intro name, exactly (a "(Disc N)" left out) - the one;
        ///   2. the key of its file's name, its LaunchBox title, its alternate names in the library, and the name and
        ///      alternate names of its LaunchBox database entry (<paramref name="dbId"/>, read from LaunchBox.Metadata.db);
        /// marked best: the exact one, else the one of its file's region. Empty when the list does not have it.</summary>
        public static List<AresCompatMatch> FindAll(string system, string rom, string title, IEnumerable<string> altNames = null, int? dbId = null)
        {
            var found = new List<AresCompatMatch>();
            Load();
            if (system == null || !SlugOf.TryGetValue(system, out var slug) || !_bySlug.TryGetValue(slug, out var entries)) return found;
            var stem = Path.GetFileNameWithoutExtension(rom ?? "");
            var noDisc = Regex.Replace(stem, @"\s*\(Disc \d+\)", "", RegexOptions.IgnoreCase);
            foreach (var e in entries)
                if (Norm(e.Title) == Norm(stem) || Norm(e.Title) == Norm(noDisc)) found.Add(new AresCompatMatch { Entry = e, How = "its file's name", Best = true });

            var keys = new List<(string Key, string How)>();
            void Add(string name, string how) { var k = Key(name); if (k != null && !keys.Any(x => x.Key == k)) keys.Add((k, how)); }
            Add(stem, "its file's name");
            Add(title, "its title");
            foreach (var a in altNames ?? Enumerable.Empty<string>()) Add(a, "an alternate name");
            if (dbId != null)
            {
                var (dbName, dbAlts) = DatabaseNames(dbId.Value);
                Add(dbName, "its LaunchBox database entry");
                foreach (var a in dbAlts) Add(a, "an alternate name of its database entry");
            }
            foreach (var e in entries)
            {
                if (e.Key == null || found.Any(f => f.Entry == e)) continue;
                var hit = keys.FirstOrDefault(k => k.Key == e.Key);
                if (hit.Key != null) found.Add(new AresCompatMatch { Entry = e, How = hit.How });
            }
            if (!found.Any(f => f.Best))
            {
                var region = RegionOf(stem);
                var best = (region != null ? found.FirstOrDefault(f => RegionOf(f.Entry.Title) == region) : null);
                if (best != null) best.Best = true;
            }
            return found.OrderByDescending(f => f.Best).ThenBy(f => f.Entry.Title, StringComparer.OrdinalIgnoreCase).ToList();
        }

        // ── the game's LaunchBox database entry ──────────────────────────────

        private static readonly Dictionary<int, (string, List<string>)> DbCache = new Dictionary<int, (string, List<string>)>();

        /// <summary>The name and alternate names of a LaunchBox database entry, from LaunchBox.Metadata.db - read only,
        /// through the SQLite LaunchBox itself loaded (the plugin may not carry its own: LbipRowInjection). Nothing when
        /// it cannot be read (the probe, an install without the file).</summary>
        private static (string Name, List<string> Alternates) DatabaseNames(int dbId)
        {
            lock (DbCache)
            {
                if (DbCache.TryGetValue(dbId, out var c)) return c;
                var result = (Name: (string)null, Alternates: new List<string>());
                try
                {
                    var db = MetadataDb();
                    var type = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType("Microsoft.Data.Sqlite.SqliteConnection", false)).FirstOrDefault(t => t != null);
                    if (db != null && type != null)
                    {
                        using var conn = (System.Data.Common.DbConnection)Activator.CreateInstance(type, "Data Source=" + db + ";Mode=ReadOnly");
                        conn.Open();
                        using (var cmd = conn.CreateCommand())
                        {
                            cmd.CommandText = "SELECT Name FROM Games WHERE DatabaseID = " + dbId;
                            result.Name = cmd.ExecuteScalar() as string;
                        }
                        using (var cmd = conn.CreateCommand())
                        {
                            cmd.CommandText = "SELECT AlternateName FROM GameAlternateTitles WHERE DatabaseID = " + dbId;
                            using var r = cmd.ExecuteReader();
                            while (r.Read()) if (!r.IsDBNull(0)) result.Alternates.Add(r.GetString(0));
                        }
                    }
                }
                catch (Exception ex) { Log.Warn("compatibility: the LaunchBox database entry " + dbId + " could not be read", ex); }
                DbCache[dbId] = result;
                return result;
            }
        }

        /// <summary>LaunchBox's Metadata\LaunchBox.Metadata.db, from the plugin's own place in the install.</summary>
        private static string MetadataDb()
        {
            try
            {
                var dir = Path.GetDirectoryName(typeof(AresCompat).Assembly.Location);
                for (int i = 0; i < 6 && !string.IsNullOrEmpty(dir); i++)
                {
                    var db = Path.Combine(dir, "Metadata", "LaunchBox.Metadata.db");
                    if (File.Exists(db)) return db;
                    dir = Path.GetDirectoryName(dir);
                }
            }
            catch { }
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
