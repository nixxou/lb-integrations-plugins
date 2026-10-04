// A PSP game's state in PPSSPP's compatibility reports, report.ppsspp.org (Mehdi, 04/10) - shown under its title in the
// game's window (PpssppUpdatesForm), with the link to its page, always.
//
// THE SITE'S OWN RATING, NOT OURS: the best a report ever gave - measured 04/10, Aliens vs. Predator: Requiem (ULES00972
// 1.00): 50 reports, 15 "Doesn't Boot", 8 "Perfect" - "Perfect"; Xyanide (ULJM05295 1.01): Menu/Intro x2, Ingame x1 -
// "Ingame". Mehdi: the same as the site, not a result of our own that differs from it.
//
// ONE PAGE PER GAME AND DISC VERSION: /game/<DISC_ID>_<DISC_VERSION> (ULES00972_1.00), both read off the PARAM.SFO. No API:
// the HTML is read, as little of it as can be - so a page laid out otherwise gives no rating, and the link stays.
//
//   THE WHOLE LIST   /games?page=1..N (100 a page, 89 pages and 8740 games and versions on 04/10, the last page named by the pager): <a href="/game/ID_VER" class="title">,
//                    then <span class="label ...">Rating</span>, within its row. Built again when PPSSPP is installed or updated
//                    (InstallEmulator), and by the PPSSPP tab's button, in the background, a page every 400 ms. LaunchBox
//                    closed meanwhile: nothing written - a game's window reads its own page, that is all (Mehdi, 04/10).
//   ONE GAME         its own page, when its window opens and its line is older than a week: the best label of its
//                    reports table (a <tbody class="reports"> per report) - the "related game versions" above it carry labels too.
//
// <plugin data>\ppsspp-compat.tsv: ID_VER, rating, title, when it was read (UTC ticks). Never throws, never waits for the
// network on the window's thread.
//
// WRITTEN ONLY WHEN IT IS ALL THERE, AND STILL WANTED (Mehdi, 04/10): ONE build at a time (a second asked meanwhile is
// that one); nothing is written before every page is read - a page missing keeps the list as it was; and at the
// moment of writing, the PPSSPP it was built for must still be there (its executable) and this plugin too (its own
// folder) - an emulator removed meanwhile, or the pack uninstalled, writes nothing and creates no folder. Added again
// meanwhile, it is there again: the list is written. The same checks for a game's line.
//
// THE SITE DOWN OR LAID OUT OTHERWISE NEVER STOPS ANYTHING: every read is caught, every search has a time limit; the list
// is then kept as it was, a game keeps what was known, and its link stays. The PPSSPP tab says how the list stands.

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

namespace LbIntegrations.Ppsspp
{
    internal sealed class PspCompat
    {
        public string Key, Rating, Title;
        public DateTime Read;
    }

    internal static class PpssppCompat
    {
        private const string Site = "https://report.ppsspp.org";
        private static readonly TimeSpan GameInterval = TimeSpan.FromDays(7);
        private static readonly object Gate = new object();
        private static int _building;
        private static volatile int _page, _last;
        private static volatile string _problem;
        private static readonly TimeSpan SearchLimit = TimeSpan.FromSeconds(3);

        /// <summary>From the best down - the site's own words.</summary>
        internal static readonly string[] Ratings = { "Perfect", "Playable", "Ingame", "Menu/Intro", "Doesn't Boot" };

        private static readonly HttpClient Http = CreateClient();

        private static HttpClient CreateClient()
        {
            var c = new HttpClient(new HttpClientHandler { AllowAutoRedirect = true, AutomaticDecompression = DecompressionMethods.All }) { Timeout = TimeSpan.FromSeconds(20) };
            c.DefaultRequestHeaders.UserAgent.ParseAdd("Nixx-PPSSPP (LaunchBox plugin)");
            return c;
        }

        private static string DbPath => Path.Combine(PpssppSettings.Dir, "ppsspp-compat.tsv");

        public static string Key(string discId, string discVersion)
            => string.IsNullOrWhiteSpace(discId) || string.IsNullOrWhiteSpace(discVersion) ? null : discId.Trim().ToUpperInvariant() + "_" + discVersion.Trim();

        /// <summary>The game's page on the site - always shown, rated or not.</summary>
        public static string PageOf(string discId, string discVersion)
            => Key(discId, discVersion) is string k ? Site + "/game/" + k : Site + "/games";

        /// <summary>What the database says now - null when it does not know the game.</summary>
        public static PspCompat Of(string discId, string discVersion)
        {
            var k = Key(discId, discVersion);
            if (k == null) return null;
            lock (Gate) return Load().TryGetValue(k, out var c) ? c : null;
        }

        /// <summary>The game's line read again from its own page when it is older than a week (or missing). Blocking: call it
        /// off the window's thread. The fresh line, else what was known. <paramref name="exe"/>: the PPSSPP it is for - see
        /// the header.</summary>
        public static PspCompat Refresh(string discId, string discVersion, string exe)
        {
            var k = Key(discId, discVersion);
            if (k == null) return null;
            var known = Of(discId, discVersion);
            if (known != null && DateTime.UtcNow - known.Read < GameInterval) return known;
            try
            {
                using var response = Http.GetAsync(Site + "/game/" + k).GetAwaiter().GetResult();
                if (response.StatusCode == HttpStatusCode.NotFound)
                {
                    var none = new PspCompat { Key = k, Rating = "", Title = known?.Title ?? "", Read = DateTime.UtcNow };
                    Save(new[] { none }, exe);
                    return none;
                }
                if (!response.IsSuccessStatusCode) { Log.Info("compatibility: " + k + " - " + (int)response.StatusCode + ", kept as known"); return known; }
                var html = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                var rating = BestOfReports(html);
                if (rating == null) { Log.Info("compatibility: " + k + " - its page holds no report this plugin can read, kept as known"); return known; }
                var fresh = new PspCompat { Key = k, Rating = rating, Title = known?.Title ?? "", Read = DateTime.UtcNow };
                Save(new[] { fresh }, exe);
                Log.Info("compatibility: " + k + " - " + rating + " (its page)");
                return fresh;
            }
            catch (Exception ex) { Log.Info("compatibility: " + k + " not read - " + ex.Message); return known; }
        }

        /// <summary>The best label of a game page's reports table - the site's own rating. Null when there is none to read.</summary>
        internal static string BestOfReports(string html)
        {
            // One <tbody class="reports"> PER REPORT (measured 04/10: 50 of them on ULES00972's page) - the table read whole,
            // from the first one to its </table>.
            int start = html.IndexOf("<tbody class=\"reports\"", StringComparison.Ordinal);
            if (start < 0) return null;
            int end = html.IndexOf("</table>", start, StringComparison.Ordinal);
            var table = end < 0 ? html.Substring(start) : html.Substring(start, end - start);
            int best = int.MaxValue;
            foreach (Match m in Regex.Matches(table, "<span class=\"label[^\"]*\">([^<]+)</span>", RegexOptions.None, SearchLimit))
            {
                int at = Array.IndexOf(Ratings, WebUtility.HtmlDecode(m.Groups[1].Value).Trim());
                if (at >= 0 && at < best) best = at;
            }
            return best == int.MaxValue ? null : Ratings[best];
        }

        // ── the whole list ───────────────────────────────────────────────────

        /// <summary>The whole list read again, in the background - at PPSSPP's install or update, and once when there is none.
        /// <paramref name="exe"/>: the PPSSPP it is for, still there when it is written - see the header.</summary>
        public static void RebuildSoon(string why, string exe)
        {
            if (Interlocked.Exchange(ref _building, 1) == 1) { Log.Info("compatibility list: already being read - this request is that one (" + why + ")"); return; }
            new Thread(() =>
            {
                try { Rebuild(why, exe); }
                catch (Exception ex) { _problem = ex.Message; Log.Warn("compatibility list: not read", ex); }
                finally { Interlocked.Exchange(ref _building, 0); }
            }) { IsBackground = true, Name = "PPSSPP compatibility list" }.Start();
        }

        /// <summary>Is the list being read now.</summary>
        public static bool Building => Volatile.Read(ref _building) == 1;

        /// <summary>How the list stands, in words, for the PPSSPP tab.</summary>
        public static string StatusText()
        {
            try
            {
                if (Building) return "Being read: page " + Math.Max(1, _page) + (_last > 0 ? " of " + _last : "") + "...";
                int count;
                lock (Gate) count = Load().Count(kv => kv.Value.Read > DateTime.MinValue);
                var said = !File.Exists(DbPath) || count == 0
                    ? "Not built yet: a game's rating is read from its own page when its window opens."
                    : count + " games and versions, read " + File.GetLastWriteTime(DbPath).ToString("g", CultureInfo.CurrentCulture) + ".";
                var problem = _problem;
                return said + (problem != null ? "  Last attempt: " + problem : "");
            }
            catch (Exception ex) { return "Not known (" + ex.Message + ")"; }
        }

        // One row each: the label looked for before its </tr> only - a row without one never takes the next row's.
        private static readonly Regex Row = new Regex("<a href=\"/game/([^\"]+)\" class=\"title\">([^<]*)</a>(?:(?!</tr>).)*?<span class=\"label[^\"]*\">([^<]+)</span>", RegexOptions.Singleline, SearchLimit);
        private static readonly Regex Last = new Regex("<li class=\"[^\"]*last[^\"]*\"><a href=\"/games\\?page=(\\d+)\">", RegexOptions.None, SearchLimit);

        private static void Rebuild(string why, string exe)
        {
            var started = DateTime.UtcNow;
            var found = new List<PspCompat>();
            int last = 1;
            _page = 0; _last = 0;
            string Fail(string why) { _problem = why + " - the list kept as it was"; Log.Info("compatibility list: " + _problem); return null; }
            for (int page = 1; page <= last && page <= 500; page++)
            {
                _page = page;
                string html;
                try { html = Http.GetStringAsync(Site + "/games" + (page > 1 ? "?page=" + page : "")).GetAwaiter().GetResult(); }
                catch (Exception ex) { Fail("page " + page + " not read (" + ex.Message + ")"); return; }
                try
                {
                    if (page == 1)
                    {
                        var l = Last.Match(html);
                        if (l.Success && int.TryParse(l.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) && n > 0) last = Math.Min(n, 500);
                        _last = last;
                    }
                    int before = found.Count;
                    foreach (Match m in Row.Matches(html))
                    {
                        var key = WebUtility.HtmlDecode(m.Groups[1].Value).Trim();
                        var rating = WebUtility.HtmlDecode(m.Groups[3].Value).Trim();
                        found.Add(new PspCompat { Key = key, Title = WebUtility.HtmlDecode(m.Groups[2].Value).Trim(), Rating = Array.IndexOf(Ratings, rating) >= 0 ? rating : "", Read = started });
                    }
                    if (found.Count == before) { Fail("page " + page + " holds no game this plugin can read (the site laid out otherwise?)"); return; }
                }
                catch (Exception ex) { Fail("page " + page + " could not be read through (" + ex.Message + ")"); return; }
                Thread.Sleep(400);
            }
            if (!Save(found, exe, replace: true)) { Fail("not written"); return; }
            _problem = null;
            Log.Info("compatibility list: " + found.Count + " games and versions from " + last + " page(s), " + (int)(DateTime.UtcNow - started).TotalSeconds + " s (" + why + ")");
        }

        // ── ppsspp-compat.tsv ────────────────────────────────────────────────

        private static Dictionary<string, PspCompat> _db;
        private static DateTime _dbStamp;

        private static Dictionary<string, PspCompat> Load()
        {
            try
            {
                var stamp = File.Exists(DbPath) ? File.GetLastWriteTimeUtc(DbPath) : DateTime.MinValue;
                if (_db != null && stamp == _dbStamp) return _db;
                var db = new Dictionary<string, PspCompat>(StringComparer.OrdinalIgnoreCase);
                if (File.Exists(DbPath))
                    foreach (var line in File.ReadAllLines(DbPath, Encoding.UTF8))
                    {
                        var c = line.Split('\t');
                        if (c.Length < 4) continue;
                        long.TryParse(c[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out var ticks);
                        db[c[0]] = new PspCompat { Key = c[0], Rating = c[1], Title = c[2], Read = new DateTime(Math.Max(0, ticks), DateTimeKind.Utc) };
                    }
                _db = db; _dbStamp = stamp;
                return db;
            }
            catch (Exception ex) { Log.Warn("could not read " + DbPath, ex); return _db ?? new Dictionary<string, PspCompat>(); }
        }

        /// <summary>Why it may not be written now - null when it may: the PPSSPP it was read for still there, and this plugin.</summary>
        private static string NotNow(string exe)
        {
            try
            {
                if (string.IsNullOrEmpty(exe) || !File.Exists(exe)) return "the PPSSPP it was read for is gone (" + (exe ?? "none") + ")";
                var own = Path.GetDirectoryName(typeof(PpssppCompat).Assembly.Location);
                if (string.IsNullOrEmpty(own) || !Directory.Exists(own)) return "this plugin is gone";
                return null;
            }
            catch (Exception ex) { return ex.Message; }
        }

        /// <summary>The lines written - false, and nothing written nor created, when they may not be (NotNow).</summary>
        private static bool Save(IEnumerable<PspCompat> lines, string exe, bool replace = false)
        {
            lock (Gate)
            {
                try
                {
                    var notNow = NotNow(exe);
                    if (notNow != null) { Log.Info("compatibility: not written - " + notNow); return false; }
                    var db = replace ? new Dictionary<string, PspCompat>(StringComparer.OrdinalIgnoreCase) : new Dictionary<string, PspCompat>(Load(), StringComparer.OrdinalIgnoreCase);
                    foreach (var l in lines) db[l.Key] = l;
                    Directory.CreateDirectory(PpssppSettings.Dir);
                    var tmp = DbPath + ".tmp";
                    File.WriteAllLines(tmp, db.Values.OrderBy(c => c.Key, StringComparer.OrdinalIgnoreCase)
                        .Select(c => string.Join("\t", c.Key, c.Rating ?? "", (c.Title ?? "").Replace('\t', ' '), c.Read.Ticks.ToString(CultureInfo.InvariantCulture))), new UTF8Encoding(false));
                    File.Move(tmp, DbPath, overwrite: true);
                    _db = null;
                    return true;
                }
                catch (Exception ex) { Log.Warn("could not write " + DbPath, ex); return false; }
            }
        }
    }
}
