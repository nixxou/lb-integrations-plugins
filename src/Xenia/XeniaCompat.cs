// A game's state in Xenia Canary's compatibility list, kept locally (Mehdi, 01/10).
//
// THE LIST IS GITHUB'S, and only there: Xenia carries none (its "Game compatibility..." menu opens
// github.com/xenia-canary/game-compatibility/issues - emulator_window.cc, ShowCompatibility), and the repository
// holds no data file, only its issues. One issue per game, titled "<TITLE ID> - <name>" ("4D5307E6 - Halo 3"), its
// state a label "state-..." (eleven: nothing, crash-guest, crash-host, crash-xna-WONTFIX, hang, intro, load, title,
// menus, gameplay, playable), its known problems the other labels (gpu-readback, vsync-off-speedup...).
//
// MEASURED 01/10: 1,109 open issues, 1,108 titled by a title id, one state label each (4 have none); closed ones are
// duplicates and invalid reports, never with a state - so OPEN issues only. A few title ids have two open issues
// (GTA IV and Episodes from Liberty City share 545407F2; a prototype beside the release): all are kept.
//
// WHEN (Mehdi, 01/10):
//   - built whole at the emulator's install and update, in the install's own time (some twelve requests);
//   - brought up to date in the background when a game is launched (and when an options window shows it), if older
//     than a few hours: only the issues changed since (state=all&since=..., one request as a rule) - closed ones
//     taken out, open ones put in. Never on a launch's time, always under a timeout;
//   - GitHub's limit without an account is 60 requests an hour from one address, and the list API refuses page
//     numbers past 1,000 results: pages are followed by their "next" link. A refusal keeps the list as it is.
//
// <plugin data>\xenia-compat.tsv: a first line "#updated <UTC>", then one line per issue: title id, issue, state,
// other labels (comma-separated), title.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace LbIntegrations.Xenia
{
    internal sealed class XeniaCompatEntry
    {
        public string TitleId;
        public int Issue;
        public string State;               // "playable", "gameplay"... null when the issue has no state label
        public List<string> Labels = new List<string>();
        public string Title;
        public string Url => "https://github.com/xenia-canary/game-compatibility/issues/" + Issue.ToString(CultureInfo.InvariantCulture);
    }

    internal static class XeniaCompat
    {
        private const string Api = "https://api.github.com/repos/xenia-canary/game-compatibility/issues";
        private static readonly Regex TitleIdRx = new Regex(@"^\s*(?:0x)?(?<id>[0-9A-Fa-f]{8})\b", RegexOptions.CultureInvariant);
        private static readonly object Gate = new object();
        private static int _refreshing;

        /// <summary>For the probe: the list somewhere else. Set by reflection.</summary>
#pragma warning disable CS0649
        internal static string PathOverride;
#pragma warning restore CS0649

        internal static string ListPath
        {
            get
            {
                if (PathOverride != null) return PathOverride;
                var dll = typeof(XeniaCompat).Assembly.Location;
                var root = Path.GetDirectoryName(Path.GetDirectoryName(dll)) ?? Path.GetDirectoryName(dll);
                return Path.Combine(root, ".data", "8d266d9f-aa30-4036-9cf0-cf8398840658", "xenia-compat.tsv");
            }
        }

        // ── reading ──────────────────────────────────────────────────────────

        /// <summary>The issues of a title id, best state first; empty when it has none or the list is not there.</summary>
        public static List<XeniaCompatEntry> Lookup(string titleId)
        {
            if (string.IsNullOrWhiteSpace(titleId)) return new List<XeniaCompatEntry>();
            var id = titleId.Trim().ToUpperInvariant();
            return Load(out _).Values.Where(e => e.TitleId == id).OrderByDescending(e => Rank(e.State)).ThenBy(e => e.Issue).ToList();
        }

        /// <summary>When the list was last brought up to date, or null.</summary>
        public static DateTime? Updated() { Load(out var u); return u; }

        private static readonly string[] Order = { "nothing", "crash-xna-WONTFIX", "crash-host", "crash-guest", "hang", "intro", "load", "title", "menus", "gameplay", "playable" };
        private static int Rank(string state) => state == null ? -1 : Array.IndexOf(Order, state);

        internal static Dictionary<int, XeniaCompatEntry> Load(out DateTime? updated)
        {
            updated = null;
            var all = new Dictionary<int, XeniaCompatEntry>();
            lock (Gate)
            {
                try
                {
                    if (!File.Exists(ListPath)) return all;
                    foreach (var line in File.ReadAllLines(ListPath, Encoding.UTF8))
                    {
                        var f = line.Split('\t');
                        if (f[0] == "#updated" && f.Length > 1 && DateTime.TryParse(f[1], CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var u)) { updated = u; continue; }
                        if (f.Length < 5 || !int.TryParse(f[1], out var n)) continue;
                        all[n] = new XeniaCompatEntry
                        {
                            TitleId = f[0], Issue = n, State = f[2].Length == 0 ? null : f[2],
                            Labels = f[3].Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).ToList(), Title = f[4],
                        };
                    }
                }
                catch (Exception ex) { Log.Warn("compatibility list: could not be read", ex); }
            }
            return all;
        }

        private static void Save(Dictionary<int, XeniaCompatEntry> all, DateTime updated)
        {
            lock (Gate)
            {
                var sb = new StringBuilder("#updated\t" + updated.ToString("o", CultureInfo.InvariantCulture) + "\r\n");
                foreach (var e in all.Values.OrderBy(e => e.TitleId).ThenBy(e => e.Issue))
                    sb.Append(e.TitleId).Append('\t').Append(e.Issue.ToString(CultureInfo.InvariantCulture)).Append('\t').Append(e.State ?? "")
                      .Append('\t').Append(string.Join(",", e.Labels)).Append('\t').Append((e.Title ?? "").Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ')).Append("\r\n");
                Directory.CreateDirectory(Path.GetDirectoryName(ListPath));
                var tmp = ListPath + ".tmp";
                File.WriteAllText(tmp, sb.ToString(), new UTF8Encoding(false));
                File.Move(tmp, ListPath, overwrite: true);
            }
        }

        // ── one page of GitHub's answer ──────────────────────────────────────

        /// <summary>The issues of one page: the open ones with a title id, and the numbers of the closed ones.</summary>
        internal static List<XeniaCompatEntry> ParsePage(string json, List<int> closed)
        {
            var open = new List<XeniaCompatEntry>();
            using var doc = JsonDocument.Parse(json);
            foreach (var issue in doc.RootElement.EnumerateArray())
            {
                if (issue.TryGetProperty("pull_request", out _)) continue;
                int number = issue.GetProperty("number").GetInt32();
                if (issue.GetProperty("state").GetString() != "open") { closed?.Add(number); continue; }
                var title = issue.GetProperty("title").GetString() ?? "";
                var m = TitleIdRx.Match(title);
                if (!m.Success) { closed?.Add(number); continue; }
                var labels = issue.TryGetProperty("labels", out var l) ? l.EnumerateArray().Select(x => x.GetProperty("name").GetString()).Where(x => x != null).ToList() : new List<string>();
                var state = labels.FirstOrDefault(x => x.StartsWith("state-", StringComparison.Ordinal));
                open.Add(new XeniaCompatEntry
                {
                    TitleId = m.Groups["id"].Value.ToUpperInvariant(), Issue = number, Title = title,
                    State = state?.Substring("state-".Length), Labels = labels.Where(x => !x.StartsWith("state-", StringComparison.Ordinal)).ToList(),
                });
            }
            return open;
        }

        // ── from GitHub ──────────────────────────────────────────────────────

        private static readonly HttpClient Http = MakeClient();

        private static HttpClient MakeClient()
        {
            var c = new HttpClient();
            c.DefaultRequestHeaders.UserAgent.ParseAdd("lb-integrations-plugins");
            c.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
            return c;
        }

        /// <summary>Every page from <paramref name="first"/>, following GitHub's "next" links. Throws on a refusal.</summary>
        private static List<string> Pages(string first, CancellationToken cancel, Action<int> page)
        {
            var pages = new List<string>();
            var url = first;
            while (url != null && pages.Count < 60)
            {
                using var resp = Http.GetAsync(url, cancel).GetAwaiter().GetResult();
                if (!resp.IsSuccessStatusCode)
                {
                    var left = resp.Headers.TryGetValues("X-RateLimit-Remaining", out var r) ? r.FirstOrDefault() : "?";
                    throw new InvalidOperationException("GitHub answered " + (int)resp.StatusCode + " (requests left this hour: " + left + ")");
                }
                pages.Add(resp.Content.ReadAsStringAsync(cancel).GetAwaiter().GetResult());
                page?.Invoke(pages.Count);
                url = null;
                if (resp.Headers.TryGetValues("Link", out var links))
                {
                    var m = Regex.Match(string.Join(",", links), "<([^>]+)>;\\s*rel=\"next\"");
                    if (m.Success) url = m.Groups[1].Value;
                }
            }
            return pages;
        }

        /// <summary>The whole list, anew - the install's and the update's. False (the list kept as it was) on any failure.</summary>
        public static bool Build(TimeSpan timeout, Action<string> report = null)
        {
            if (Interlocked.Exchange(ref _refreshing, 1) == 1) return false;
            try
            {
                using var cts = new CancellationTokenSource(timeout);
                var started = DateTime.UtcNow;
                var pages = Pages(Api + "?state=open&per_page=100", cts.Token, n => report?.Invoke("Downloading Xenia's compatibility list... (" + n * 100 + ")"));
                var all = new Dictionary<int, XeniaCompatEntry>();
                foreach (var p in pages) foreach (var e in ParsePage(p, null)) all[e.Issue] = e;
                Save(all, started);
                Log.Info("compatibility list built: " + all.Count + " games, from " + pages.Count + " page(s)");
                return true;
            }
            catch (Exception ex) { Log.Info("compatibility list: not built (" + ex.Message + ") - kept as it was"); return false; }
            finally { Interlocked.Exchange(ref _refreshing, 0); }
        }

        /// <summary>Only what changed since the list's date - closed issues out, open ones in. Builds it whole when there
        /// is none yet. False on a failure (the list kept).</summary>
        public static bool Refresh(TimeSpan timeout)
        {
            var all = Load(out var updated);
            if (updated == null || all.Count == 0) return Build(timeout);
            if (Interlocked.Exchange(ref _refreshing, 1) == 1) return false;
            try
            {
                using var cts = new CancellationTokenSource(timeout);
                var started = DateTime.UtcNow;
                // A few minutes back: an issue changed while the last refresh was reading is not missed.
                var since = updated.Value.AddMinutes(-5).ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
                var pages = Pages(Api + "?state=all&per_page=100&since=" + since, cts.Token, null);
                int changed = 0, gone = 0;
                var closed = new List<int>();
                foreach (var p in pages)
                    foreach (var e in ParsePage(p, closed)) { all[e.Issue] = e; changed++; }
                foreach (var n in closed) if (all.Remove(n)) gone++;
                Save(all, started);
                Log.Info("compatibility list up to date: " + changed + " issue(s) changed, " + gone + " closed - " + all.Count + " games");
                return true;
            }
            catch (Exception ex) { Log.Info("compatibility list: not brought up to date (" + ex.Message + ") - kept as it was"); return false; }
            finally { Interlocked.Exchange(ref _refreshing, 0); }
        }

        /// <summary>In the background, when the list is older than <paramref name="maxAge"/> (or missing): never waited on.</summary>
        public static void RefreshSoon(TimeSpan maxAge, TimeSpan timeout)
        {
            try
            {
                var updated = Updated();
                if (updated != null && DateTime.UtcNow - updated.Value < maxAge) return;
                Task.Run(() => Refresh(timeout));
            }
            catch { }
        }

        /// <summary>One line for the log: "playable (#370)", or why nothing is known.</summary>
        public static string Describe(string titleId)
        {
            var found = Lookup(titleId);
            if (found.Count == 0) return Updated() == null ? "no compatibility list yet" : "not in Xenia's compatibility list";
            return string.Join("; ", found.Select(e => (e.State ?? "no state") + " (#" + e.Issue + (e.Labels.Count > 0 ? ", " + string.Join(", ", e.Labels) : "") + ")"));
        }
    }
}
