// A game's state in Cxbx-Reloaded's compatibility list, kept locally (Mehdi, 03/10) - XeniaCompat's role, from another
// kind of source.
//
// THE SOURCE IS THE PROJECT'S SITE, cxbx-reloaded.co.uk/compatibility: one entry per disc version - serial ("TT-164",
// the certificate's title id as Cxbx-Reloaded writes it: two letters, a dash, the number), version ("v1.0", certificate
// version & 0xFF "." version >> 8), region and edition, a status (Playable, In-Game, Boots, Nothing, Untested), the date
// of its last report and its page. Measured 03/10: 1,182 games, 2,163 entries, reports from 2020 (817), 2021 (281), 2022
// (9) and 2023 (1) - a floor more than a verdict, the emulator has moved on since. The GitHub issues of
// Cxbx-Reloaded/game-compatibility are older still (frozen July 2021) and add nothing.
//
// NO FILE, NO API (all measured 03/10): the page is a Livewire component paginated by 25 on the server, whatever is
// asked - no page size it accepts, no search that unpaginates, no export. So:
//   - THE WHOLE LIST: its 42 pages read one after the other (?page=N), at the emulator's install and update, in the
//     background. And a copy of it is embedded in the plugin (compat.json, taken when it was built), so a game has an
//     answer before any of that, and offline.
//   - ONE GAME (Mehdi, 03/10): the component's search, which matches the serial - "TT-164" finds GTA San Andreas with
//     its five versions, in ~100 ms - when the game's options window opens and at its launch, in the background, at most
//     every 6 hours per game. Two requests: the page (its session and the component's signed state), then the search.
// NEVER A BROKEN COPY: a whole list replaces the copy only when it reads - a thousand entries at least and no fewer than
// nine tenths of the copy's; one game's search only replaces that game's entries, and only with entries it parsed.
//
// <plugin data>\cxbx-compat.json (what is known) and cxbx-compat.meta (when each serial was last asked).

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace LbIntegrations.Cxbx
{
    internal sealed class CxbxCompatEntry
    {
        public string Title { get; set; } = "";
        public string Serial { get; set; } = "";      // "TT-164"
        public string Version { get; set; } = "";     // "v1.0"
        public string Region { get; set; } = "";      // "Europe: Classics"
        public string State { get; set; } = "";       // Playable, In-Game, Boots, Nothing, Untested
        public string Updated { get; set; } = "";     // "15th Dec 2020 05:12", or "N/A"
        public string Url { get; set; } = "";
    }

    internal static class CxbxCompat
    {
        private const string Site = "https://cxbx-reloaded.co.uk";
        private static readonly TimeSpan GameMaxAge = TimeSpan.FromHours(6);
        private static readonly object Gate = new object();
        private static List<CxbxCompatEntry> _entries;
        private static int _fetchingAll;

        private static string JsonPath => Path.Combine(CxbxSettings.Dir, "cxbx-compat.json");
        private static string MetaPath => Path.Combine(CxbxSettings.Dir, "cxbx-compat.meta");

        // ── what a game is called there ──────────────────────────────────────

        /// <summary>"TT-164": the certificate's title id as Cxbx-Reloaded shows it (WndMain FormatTitleId).</summary>
        public static string SerialOf(uint titleId)
        {
            char a = (char)((titleId >> 24) & 0xFF), b = (char)((titleId >> 16) & 0xFF);
            if (a < 0x20 || a > 0x7E || b < 0x20 || b > 0x7E) return null;
            return "" + a + b + "-" + (titleId & 0xFFFF).ToString("000", CultureInfo.InvariantCulture);
        }

        /// <summary>"v1.0": disc version "." patch version, as Cxbx-Reloaded shows it.</summary>
        public static string VersionOf(uint version)
            => "v" + (version & 0xFF).ToString(CultureInfo.InvariantCulture) + "." + (version >> 8).ToString(CultureInfo.InvariantCulture);

        // ── reading ──────────────────────────────────────────────────────────

        public static List<CxbxCompatEntry> All()
        {
            lock (Gate)
            {
                if (_entries != null) return _entries;
                _entries = Read(JsonPath) ?? Embedded() ?? new List<CxbxCompatEntry>();
                return _entries;
            }
        }

        /// <summary>The entry of this very disc, and the game's other versions (same title), best first.</summary>
        public static (CxbxCompatEntry Exact, List<CxbxCompatEntry> Others) For(XbeInfo xbe)
        {
            var serial = xbe == null ? null : SerialOf(xbe.TitleId);
            if (serial == null) return (null, new List<CxbxCompatEntry>());
            var version = VersionOf(xbe.Version);
            var all = All();
            var exact = all.FirstOrDefault(e => e.Serial == serial && e.Version == version)
                        ?? all.FirstOrDefault(e => e.Serial == serial);
            var titles = new HashSet<string>(all.Where(e => e.Serial == serial).Select(e => e.Title), StringComparer.OrdinalIgnoreCase);
            var others = all.Where(e => titles.Contains(e.Title) && e != exact).OrderBy(e => Rank(e.State)).ThenBy(e => e.Serial).ToList();
            return (exact, others);
        }

        /// <summary>One line for the log.</summary>
        public static string Describe(XbeInfo xbe)
        {
            var serial = xbe == null ? null : SerialOf(xbe.TitleId);
            if (serial == null) return "no serial";
            var (exact, others) = For(xbe);
            var s = serial + " " + VersionOf(xbe.Version) + ": " + (exact == null ? "not in the list" : exact.State + (exact.Version != VersionOf(xbe.Version) ? " (as " + exact.Version + ")" : "") + Date(exact));
            var tested = others.Where(o => o.State != "Untested").Take(3).ToList();
            if (tested.Count > 0) s += "; other versions: " + string.Join(", ", tested.Select(o => o.Serial + " " + o.Version + " " + o.State + Date(o)));
            return s;
        }

        private static string Date(CxbxCompatEntry e) => string.IsNullOrEmpty(e.Updated) || e.Updated == "N/A" ? "" : " (" + e.Updated + ")";

        /// <summary>Best first: Playable, In-Game, Boots, Nothing, then not tested.</summary>
        public static int Rank(string state)
        {
            switch (state)
            {
                case "Playable": return 0;
                case "In-Game": return 1;
                case "Boots": return 2;
                case "Nothing": return 3;
                default: return 4;
            }
        }

        // ── one game ─────────────────────────────────────────────────────────

        /// <summary>This game's entries asked again, in the background, unless that was done in the last 6 hours (or
        /// <paramref name="force"/>). <paramref name="done"/> is told whether anything changed.</summary>
        public static void RefreshGame(XbeInfo xbe, bool force = false, Action<bool> done = null)
        {
            var serial = xbe == null ? null : SerialOf(xbe.TitleId);
            if (serial == null) { done?.Invoke(false); return; }
            if (!force && Asked(serial) is DateTime last && DateTime.UtcNow - last < GameMaxAge) { done?.Invoke(false); return; }
            Task.Run(() =>
            {
                bool changed = false;
                try
                {
                    var found = Search(serial, TimeSpan.FromSeconds(20));
                    MarkAsked(serial);
                    if (found == null) return;
                    var mine = found.Where(e => e.Serial == serial).Select(e => e.Title).ToHashSet(StringComparer.OrdinalIgnoreCase);
                    var fresh = found.Where(e => mine.Contains(e.Title)).ToList();
                    if (fresh.Count == 0) { Log.Info("compatibility: " + serial + " is not on the site's list"); return; }
                    lock (Gate)
                    {
                        var all = All().ToList();
                        var before = string.Join("|", all.Where(e => mine.Contains(e.Title)).OrderBy(e => e.Serial + e.Version).Select(Key));
                        all.RemoveAll(e => mine.Contains(e.Title));
                        all.AddRange(fresh);
                        changed = before != string.Join("|", fresh.OrderBy(e => e.Serial + e.Version).Select(Key));
                        _entries = all;
                        if (changed) Write(JsonPath, all);
                    }
                    Log.Info("compatibility of " + serial + " asked again: " + fresh.Count + " entr" + (fresh.Count == 1 ? "y" : "ies") + (changed ? ", changed" : ", as it was"));
                }
                catch (Exception ex) { Log.Info("compatibility of " + serial + ": the site did not answer (" + ex.GetType().Name + ": " + ex.Message + ")"); }
                finally { try { done?.Invoke(changed); } catch { } }
            });
        }

        private static string Key(CxbxCompatEntry e) => e.Serial + " " + e.Version + " " + e.Region + " " + e.State + " " + e.Updated;

        /// <summary>The site's search for <paramref name="text"/>: the page first (its session cookie, its CSRF token, the
        /// component's signed state), then the component told the search changed. Null when it could not be read.</summary>
        internal static List<CxbxCompatEntry> Search(string text, TimeSpan timeout)
        {
            using var handler = new HttpClientHandler { CookieContainer = new CookieContainer(), AutomaticDecompression = DecompressionMethods.All };
            using var http = new HttpClient(handler) { Timeout = timeout };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("lb-integrations-plugins/1.0");
            var page = http.GetStringAsync(Site + "/compatibility").GetAwaiter().GetResult();
            var token = Regex.Match(page, "livewire_token = '([^']+)'").Groups[1].Value;
            var raw = Regex.Match(page, "wire:initial-data=\"([^\"]+)\"[^>]*class=\"mt-4\"").Groups[1].Value;
            if (token.Length == 0 || raw.Length == 0) throw new InvalidDataException("the page no longer carries the list's component");
            using var init = JsonDocument.Parse(WebUtility.HtmlDecode(raw));
            var body = "{\"fingerprint\":" + init.RootElement.GetProperty("fingerprint").GetRawText()
                     + ",\"serverMemo\":" + init.RootElement.GetProperty("serverMemo").GetRawText()
                     + ",\"updates\":[{\"type\":\"syncInput\",\"payload\":{\"id\":\"lbip\",\"name\":\"search\",\"value\":" + JsonSerializer.Serialize(text) + "}}]}";
            using var req = new HttpRequestMessage(HttpMethod.Post, Site + "/livewire/message/games-list") { Content = new StringContent(body, Encoding.UTF8, "application/json") };
            req.Headers.Add("X-CSRF-TOKEN", token);
            req.Headers.Add("X-Livewire", "true");
            using var resp = http.SendAsync(req).GetAwaiter().GetResult();
            resp.EnsureSuccessStatusCode();
            using var answer = JsonDocument.Parse(resp.Content.ReadAsStringAsync().GetAwaiter().GetResult());
            if (!answer.RootElement.TryGetProperty("effects", out var effects) || !effects.TryGetProperty("html", out var html) || html.ValueKind != JsonValueKind.String) return null;
            return Parse(html.GetString());
        }

        // ── the whole list ───────────────────────────────────────────────────

        /// <summary>Every page, one after the other, in the background - at the emulator's install and update. Once at a time.</summary>
        public static void RefreshAll() => Task.Run(() => Whole(null));

        /// <summary>The whole list read again, from a game's window (LbipListRefresh): its pages counted, Cancel heard. Null when
        /// it was kept, else why not.</summary>
        public static string RefreshWhole(LbIntegrations.Lbip.LbipListJob job) => Whole(job);

        /// <summary>When the whole list was last read - not a game's search, which writes the copy too - or null: the copy then
        /// is the one embedded when the plugin was built.</summary>
        public static DateTime? Downloaded() => Asked(WholeKey) is DateTime t ? t.ToLocalTime() : (DateTime?)null;

        private const string WholeKey = "*whole*";

        private static string Whole(LbIntegrations.Lbip.LbipListJob job)
        {
            if (Interlocked.Exchange(ref _fetchingAll, 1) == 1) return "already being read";
            try
            {
                var watch = System.Diagnostics.Stopwatch.StartNew();
                var list = FetchAll(TimeSpan.FromSeconds(30), job);
                if (job != null && job.Token.IsCancellationRequested) { Log.Info("compatibility list: cancelled - the copy stays"); return "cancelled"; }
                lock (Gate)
                {
                    int had = All().Count;
                    if (list.Count < 1000 || list.Count < had * 9 / 10)
                    {
                        Log.Info("compatibility: the site gave " + list.Count + " entries against " + had + " kept - the copy stays");
                        return "the site gave " + list.Count + " entries against " + had + " kept";
                    }
                    _entries = list;
                    Write(JsonPath, list);
                }
                MarkAsked(WholeKey);
                Log.Info("compatibility list read again: " + list.Count + " entries, " + list.Select(e => e.Title).Distinct().Count() + " games, " + watch.Elapsed.TotalSeconds.ToString("0") + " s");
                return null;
            }
            catch (Exception ex)
            {
                if (job != null && job.Token.IsCancellationRequested) return "cancelled";
                Log.Info("compatibility list: the site did not answer (" + ex.GetType().Name + ": " + ex.Message + ") - the copy stays");
                return "the site did not answer (" + ex.Message + ")";
            }
            finally { Interlocked.Exchange(ref _fetchingAll, 0); }
        }

        /// <summary>The 42-odd pages, ?page=N until one holds no game. Gently: one at a time, a pause between. A cancelled
        /// read stops at once - what it read is thrown away by its caller.</summary>
        internal static List<CxbxCompatEntry> FetchAll(TimeSpan timeout, LbIntegrations.Lbip.LbipListJob job = null)
        {
            using var http = new HttpClient(new HttpClientHandler { AutomaticDecompression = DecompressionMethods.All }) { Timeout = timeout };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("lb-integrations-plugins/1.0");
            var token = job?.Token ?? CancellationToken.None;
            var all = new List<CxbxCompatEntry>();
            for (int page = 1; page <= 500; page++)
            {
                if (token.IsCancellationRequested) break;
                if (job != null) { job.Done = page - 1; job.Step = "Page " + page + " of cxbx-reloaded.co.uk's list (25 games each) - " + all.Count + " entries so far"; }
                var found = Parse(http.GetStringAsync(Site + "/compatibility?page=" + page, token).GetAwaiter().GetResult());
                if (found.Count == 0) break;
                all.AddRange(found);
                if (token.WaitHandle.WaitOne(250)) break;
            }
            return all;
        }

        // ── parsing ──────────────────────────────────────────────────────────

        // One entry of the list, as the page draws it (measured 03/10):
        //   <a href="https://cxbx-reloaded.co.uk/compatibility/title/1" ...> ... <h5 class="mb-1">007: Agent Under Fire</h5>
        //   <small>EA-013 v1.0</small> ... <small>USA: Original</small> ... <span class="text-warning">Boots</span> ...
        //   <i class="fas fa-clock"></i> 22nd Dec 2021 05:19</small>
        private static readonly Regex EntryRx = new Regex(
            "<a href=\"(?<url>[^\"]*/compatibility/title/\\d+)\"[^>]*>.*?<h5 class=\"mb-1\">(?<title>[^<]+)</h5>\\s*<small>(?<serial>[^<]*)</small>"
            + ".*?<small>(?<region>[^<]*)</small>.*?<span class=\"text-[a-z]+\">(?<state>[^<]+)</span>.*?fa-clock\"></i>\\s*(?<when>[^<]+)<",
            RegexOptions.Singleline | RegexOptions.CultureInvariant);

        internal static List<CxbxCompatEntry> Parse(string html)
        {
            var found = new List<CxbxCompatEntry>();
            if (string.IsNullOrEmpty(html)) return found;
            foreach (Match m in EntryRx.Matches(html))
            {
                var sv = WebUtility.HtmlDecode(m.Groups["serial"].Value).Trim();
                int space = sv.LastIndexOf(' ');
                found.Add(new CxbxCompatEntry
                {
                    Title = WebUtility.HtmlDecode(m.Groups["title"].Value).Trim(),
                    Serial = space > 0 ? sv.Substring(0, space) : sv,
                    Version = space > 0 ? sv.Substring(space + 1) : "",
                    Region = WebUtility.HtmlDecode(m.Groups["region"].Value).Trim(),
                    State = WebUtility.HtmlDecode(m.Groups["state"].Value).Trim(),
                    Updated = WebUtility.HtmlDecode(m.Groups["when"].Value).Trim(),
                    Url = m.Groups["url"].Value,
                });
            }
            return found;
        }

        // ── files ────────────────────────────────────────────────────────────

        private static List<CxbxCompatEntry> Read(string path)
        {
            try { return File.Exists(path) ? JsonSerializer.Deserialize<List<CxbxCompatEntry>>(File.ReadAllText(path)) : null; }
            catch (Exception ex) { Log.Warn("could not read " + path, ex); return null; }
        }

        private static List<CxbxCompatEntry> Embedded()
        {
            try
            {
                using var s = typeof(CxbxCompat).Assembly.GetManifestResourceStream("compat.json");
                if (s == null) return null;
                using var r = new StreamReader(s);
                return JsonSerializer.Deserialize<List<CxbxCompatEntry>>(r.ReadToEnd());
            }
            catch (Exception ex) { Log.Warn("could not read the embedded compatibility list", ex); return null; }
        }

        internal static void Write(string path, List<CxbxCompatEntry> list)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path + ".part", JsonSerializer.Serialize(list));
            File.Move(path + ".part", path, overwrite: true);
        }

        private static DateTime? Asked(string serial)
        {
            try
            {
                if (!File.Exists(MetaPath)) return null;
                foreach (var line in File.ReadAllLines(MetaPath))
                {
                    var c = line.Split('\t');
                    if (c.Length == 2 && c[0] == serial && DateTime.TryParse(c[1], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var t)) return t;
                }
            }
            catch { }
            return null;
        }

        private static void MarkAsked(string serial)
        {
            lock (Gate)
            {
                try
                {
                    var lines = File.Exists(MetaPath) ? File.ReadAllLines(MetaPath).Where(l => !l.StartsWith(serial + "\t")).ToList() : new List<string>();
                    lines.Add(serial + "\t" + DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture));
                    Directory.CreateDirectory(Path.GetDirectoryName(MetaPath));
                    File.WriteAllLines(MetaPath, lines);
                }
                catch { }
            }
        }
    }
}
