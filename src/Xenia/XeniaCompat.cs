// A game's state in Xenia Canary's compatibility list, kept locally (Mehdi, 01/10).
//
// THE LIST IS XENIA'S OWN FILE, published by its compatibility repository: a GitHub Actions job
// (.github/workflows/update_game_compatibility.yml, every 8 hours) reads the open issues of
// github.com/xenia-canary/game-compatibility with the repository's token, keeps one per game - duplicates and
// invalid reports left out by its script (scripts/update_compatibility_data.py) - and attaches the result,
// compatibility_data.json, to the release "game-compatibility". MEASURED 01/10: 425 KB, 1,107 games; downloading it
// does NOT count against GitHub's API limit (60 an hour without an account - 48 left before, 48 after), it carries
// an ETag and a Last-Modified, so asking "only if it changed" costs a 304. Each game:
//   {"issue": 1237, "id": "4F5807D1", "title": "PDC World Championship Darts", "updated": "...",
//    "state": "Gameplay", "labels": {"state": ["state-gameplay"], "others": ["gpu-driver-issues-amd", ...]},
//    "url": "https://github.com/xenia-canary/game-compatibility/issues/1237"}
// - "state" its own five-step reading (Playable, Gameplay, Loads, Unplayable, Unknown), the label the finer one.
//
// WHEN (Mehdi, 01/10):
//   - fetched at the emulator's install and update;
//   - asked again "only if it changed" when the copy is older than 8 hours - the job's own rhythm - at the end of a
//     game (once Xenia has quit) and when a game's options show it. In the background, under a timeout: never on a
//     launch's time.
//
// NEVER A BROKEN FILE (Mehdi, 01/10): what comes back replaces the copy only once it reads - a JSON array, a hundred
// games at least and no fewer than half the copy's, nineteen in twenty of them with an 8-digit hex id, an issue and a
// state. Otherwise the copy stays and the log says why.
//
// <plugin data>\xenia-compat.json (the file as it came) and xenia-compat.meta (its ETag, its Last-Modified, when it
// was last asked for).

using System;
using System.Collections.Generic;
using System.Diagnostics;
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

namespace LbIntegrations.Xenia
{
    internal sealed class XeniaCompatEntry
    {
        public string TitleId;
        public int Issue;
        public string Title;
        public string State;               // its own: Playable, Gameplay, Loads, Unplayable, Unknown
        public string Detail;              // the label: "intro", "menus"... null when none
        public List<string> Labels = new List<string>();
        public string Url;
    }

    internal static class XeniaCompat
    {
        internal const string Source = "https://github.com/xenia-canary/game-compatibility/releases/download/game-compatibility/compatibility_data.json";
        internal static readonly TimeSpan MaxAge = TimeSpan.FromHours(8);
        private static readonly Regex Hex8 = new Regex("^[0-9A-Fa-f]{8}$", RegexOptions.CultureInvariant);
        private static readonly object Gate = new object();
        private static int _fetching;

        /// <summary>For the probe: the copy somewhere else (a folder). Set by reflection.</summary>
#pragma warning disable CS0649
        internal static string DirOverride;
#pragma warning restore CS0649

        private static string Dir
        {
            get
            {
                if (DirOverride != null) return DirOverride;
                var dll = typeof(XeniaCompat).Assembly.Location;
                var root = Path.GetDirectoryName(Path.GetDirectoryName(dll)) ?? Path.GetDirectoryName(dll);
                return Path.Combine(root, ".data", "8d266d9f-aa30-4036-9cf0-cf8398840658");
            }
        }

        private static string JsonPath => Path.Combine(Dir, "xenia-compat.json");
        private static string MetaPath => Path.Combine(Dir, "xenia-compat.meta");

        // ── reading ──────────────────────────────────────────────────────────

        private static List<XeniaCompatEntry> _cache;
        private static DateTime _cacheStamp;

        /// <summary>The games of a title id, best state first; empty when it has none or there is no copy.</summary>
        public static List<XeniaCompatEntry> Lookup(string titleId)
        {
            if (string.IsNullOrWhiteSpace(titleId)) return new List<XeniaCompatEntry>();
            var id = titleId.Trim().ToUpperInvariant();
            return All().Where(e => e.TitleId == id).OrderByDescending(e => Rank(e.State)).ThenBy(e => e.Issue).ToList();
        }

        private static readonly string[] Order = { "Unknown", "Unplayable", "Loads", "Gameplay", "Playable" };
        private static int Rank(string state) => Array.FindIndex(Order, s => string.Equals(s, state, StringComparison.OrdinalIgnoreCase));

        private static List<XeniaCompatEntry> All()
        {
            lock (Gate)
            {
                try
                {
                    if (!File.Exists(JsonPath)) return new List<XeniaCompatEntry>();
                    var stamp = File.GetLastWriteTimeUtc(JsonPath);
                    if (_cache == null || stamp != _cacheStamp)
                    {
                        _cache = Parse(File.ReadAllText(JsonPath, Encoding.UTF8), out _);
                        _cacheStamp = stamp;
                    }
                    return _cache;
                }
                catch (Exception ex) { Log.Warn("compatibility list: the copy could not be read", ex); return new List<XeniaCompatEntry>(); }
            }
        }

        /// <summary>The games of a compatibility_data.json; <paramref name="valid"/> how many read whole.</summary>
        internal static List<XeniaCompatEntry> Parse(string json, out int valid)
        {
            valid = 0;
            var list = new List<XeniaCompatEntry>();
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) throw new FormatException("not a JSON array");
            foreach (var g in doc.RootElement.EnumerateArray())
            {
                if (g.ValueKind != JsonValueKind.Object) continue;
                string Str(string name) => g.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
                var id = Str("id");
                int issue = g.TryGetProperty("issue", out var n) && n.ValueKind == JsonValueKind.Number && n.TryGetInt32(out var i) ? i : 0;
                var state = Str("state");
                var e = new XeniaCompatEntry { TitleId = (id ?? "").ToUpperInvariant(), Issue = issue, Title = Str("title"), State = state, Url = Str("url") };
                if (g.TryGetProperty("labels", out var labels) && labels.ValueKind == JsonValueKind.Object)
                {
                    if (labels.TryGetProperty("state", out var s) && s.ValueKind == JsonValueKind.Array)
                        e.Detail = s.EnumerateArray().Select(x => x.ValueKind == JsonValueKind.String ? x.GetString() : null)
                                    .FirstOrDefault(x => x != null)?.Replace("state-", "");
                    if (labels.TryGetProperty("others", out var o) && o.ValueKind == JsonValueKind.Array)
                        e.Labels = o.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString()).ToList();
                }
                if (id != null && Hex8.IsMatch(id) && issue > 0 && !string.IsNullOrEmpty(state)) valid++;
                if (id != null && Hex8.IsMatch(id)) list.Add(e);
            }
            return list;
        }

        /// <summary>Does this text read as the list - see the header? Null when it does, else why not.</summary>
        internal static string Problem(string json, int previousCount)
        {
            try
            {
                var games = Parse(json, out var valid);
                int total = games.Count;
                if (total < 100) return total + " game(s) in it - a hundred at least were expected";
                if (previousCount > 0 && total < previousCount / 2) return total + " game(s) in it, against " + previousCount + " in the copy";
                if (valid * 20 < total * 19) return "only " + valid + " of its " + total + " games have an id, an issue and a state";
                return null;
            }
            catch (Exception ex) { return "it does not read (" + ex.Message + ")"; }
        }

        // ── fetching ─────────────────────────────────────────────────────────

        private static readonly HttpClient Http = MakeClient();

        private static HttpClient MakeClient()
        {
            var c = new HttpClient(new HttpClientHandler { AllowAutoRedirect = true });
            c.DefaultRequestHeaders.UserAgent.ParseAdd("lb-integrations-plugins");
            return c;
        }

        private sealed class Meta { public string ETag, LastModified; public DateTime? Asked; }

        private static Meta ReadMeta()
        {
            var m = new Meta();
            try
            {
                if (!File.Exists(MetaPath)) return m;
                foreach (var line in File.ReadAllLines(MetaPath))
                {
                    var f = line.Split(new[] { '\t' }, 2);
                    if (f.Length != 2) continue;
                    if (f[0] == "etag") m.ETag = f[1];
                    else if (f[0] == "last-modified") m.LastModified = f[1];
                    else if (f[0] == "asked" && DateTime.TryParse(f[1], CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var a)) m.Asked = a;
                }
            }
            catch { }
            return m;
        }

        private static void WriteMeta(Meta m)
        {
            Directory.CreateDirectory(Dir);
            File.WriteAllText(MetaPath, "etag\t" + (m.ETag ?? "") + "\r\nlast-modified\t" + (m.LastModified ?? "")
                                         + "\r\nasked\t" + (m.Asked ?? DateTime.UtcNow).ToString("o", CultureInfo.InvariantCulture) + "\r\n");
        }

        /// <summary>When the copy was last asked for, or null when there is none.</summary>
        public static DateTime? Asked() => File.Exists(JsonPath) ? ReadMeta().Asked : null;

        /// <summary>Is there a copy asked for less than <see cref="MaxAge"/> ago?</summary>
        public static bool IsFresh()
        {
            var asked = Asked();
            return asked != null && DateTime.UtcNow - asked.Value < MaxAge;
        }

        /// <summary>Ask for the file - "only if it changed" when there is a copy - and keep what comes back once it reads.
        /// True when the copy is good now. Never throws.</summary>
        public static bool Fetch(TimeSpan timeout) => Fetch(timeout, false, CancellationToken.None, out _);

        /// <summary>The whole list downloaded again, from a game's window (LbipListRefresh): nothing asked "only if it changed".
        /// Null when it was kept, else why not.</summary>
        public static string FetchWhole(LbIntegrations.Lbip.LbipListJob job)
        {
            job.Step = "Downloading Xenia's compatibility list...";
            Fetch(TimeSpan.FromMinutes(2), true, job.Token, out var problem);
            return job.Token.IsCancellationRequested ? "cancelled" : problem;
        }

        /// <summary>When the whole list was last downloaded (its file written - an "unchanged" answer writes nothing), or null.</summary>
        public static DateTime? Downloaded() => File.Exists(JsonPath) ? File.GetLastWriteTime(JsonPath) : (DateTime?)null;

        private static bool Fetch(TimeSpan timeout, bool force, CancellationToken token, out string problem)
        {
            problem = null;
            if (Interlocked.Exchange(ref _fetching, 1) == 1) { problem = "already being downloaded"; return false; }
            try
            {
                var meta = ReadMeta();
                bool haveCopy = File.Exists(JsonPath);
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(token);
                cts.CancelAfter(timeout);
                using var req = new HttpRequestMessage(HttpMethod.Get, Source);
                if (!force && haveCopy && !string.IsNullOrEmpty(meta.ETag)) req.Headers.TryAddWithoutValidation("If-None-Match", meta.ETag);
                if (!force && haveCopy && !string.IsNullOrEmpty(meta.LastModified)) req.Headers.TryAddWithoutValidation("If-Modified-Since", meta.LastModified);
                using var resp = Http.SendAsync(req, cts.Token).GetAwaiter().GetResult();
                if (resp.StatusCode == HttpStatusCode.NotModified)
                {
                    meta.Asked = DateTime.UtcNow;
                    WriteMeta(meta);
                    Log.Info("compatibility list: unchanged since the copy");
                    return true;
                }
                if (!resp.IsSuccessStatusCode) { problem = "HTTP " + (int)resp.StatusCode; Log.Info("compatibility list: not fetched (HTTP " + (int)resp.StatusCode + ") - the copy is kept"); return haveCopy; }
                var json = resp.Content.ReadAsStringAsync(cts.Token).GetAwaiter().GetResult();
                var bad = Problem(json, haveCopy ? All().Count : 0);
                if (bad != null) { problem = bad; Log.Warn("compatibility list: what came back was not kept - " + bad); return haveCopy; }
                Directory.CreateDirectory(Dir);
                var tmp = JsonPath + ".tmp";
                File.WriteAllText(tmp, json, new UTF8Encoding(false));
                File.Move(tmp, JsonPath, overwrite: true);
                meta.ETag = resp.Headers.ETag?.Tag;
                meta.LastModified = resp.Content.Headers.LastModified?.ToString("R", CultureInfo.InvariantCulture);
                meta.Asked = DateTime.UtcNow;
                WriteMeta(meta);
                Log.Info("compatibility list fetched: " + All().Count + " games");
                return true;
            }
            catch (Exception ex) { problem = ex.Message; Log.Info("compatibility list: not fetched (" + ex.Message + ") - the copy is kept"); return File.Exists(JsonPath); }
            finally { Interlocked.Exchange(ref _fetching, 0); }
        }

        /// <summary>In the background, when the copy is older than <see cref="MaxAge"/> (or missing): never waited on.</summary>
        public static void RefreshSoon(TimeSpan timeout)
        {
            try
            {
                var asked = Asked();
                if (asked != null && DateTime.UtcNow - asked.Value < MaxAge) return;
                Task.Run(() => Fetch(timeout));
            }
            catch { }
        }

        /// <summary>Once the Xenia of this launch has come and gone, the list is brought up to date if it is old - in
        /// the background, a watcher that holds nothing and waits two minutes at most for the emulator to appear.</summary>
        public static void RefreshAfter(string exePath)
        {
            var name = Path.GetFileNameWithoutExtension(exePath ?? "");
            if (name.Length == 0) return;
            Task.Run(() =>
            {
                try
                {
                    bool Running() { try { return Process.GetProcessesByName(name).Length > 0; } catch { return false; } }
                    var armed = DateTime.UtcNow;
                    while (!Running() && (DateTime.UtcNow - armed).TotalSeconds < 120) Thread.Sleep(1000);
                    while (Running()) Thread.Sleep(2000);
                    RefreshSoon(TimeSpan.FromSeconds(30));
                }
                catch { }
            });
        }

        /// <summary>One line for the log: "Gameplay (gameplay, #370, gpu-readback)", or why nothing is known.</summary>
        public static string Describe(string titleId)
        {
            var found = Lookup(titleId);
            if (found.Count == 0) return File.Exists(JsonPath) ? "not in Xenia's compatibility list" : "no compatibility list yet";
            return string.Join("; ", found.Select(e => (e.State ?? "?") + " (" + (e.Detail != null ? e.Detail + ", " : "") + "#" + e.Issue
                                                         + (e.Labels.Count > 0 ? ", " + string.Join(", ", e.Labels) : "") + ")"));
        }
    }
}
