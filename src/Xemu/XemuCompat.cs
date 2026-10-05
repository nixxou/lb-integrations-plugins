// xemu's compatibility list, fetched and kept locally (Mehdi, 05/10) - XeniaCompat's role. What the files are and how they
// read: Shared.Xbox\XboxCompat (Nixx-Cxbx reads them too).
//
// WHEN:
//   - at the emulator's install and update, and at start-up when there is no copy;
//   - asked again when the copy is older than a day, at the end of a game and when a game's options show it. In the
//     background, under a timeout: never on a launch's time. The titles "only if they changed" (ETag); the reports carry
//     none, so they come whole - 1.2 MB once a day at most.
// NEVER A BROKEN FILE: what comes back replaces the copy only once it reads - a JSON array, five hundred entries at least and
// no fewer than half the copy's, nineteen in twenty reports with a rating. Otherwise the copy stays and the log says why.
//
// <plugin data>\xemu-compat-reports.json, xemu-compat-titles.json (the files as they came), xemu-compat.meta (the titles'
// ETag and Last-Modified, when the list was last asked for).

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using LbIntegrations.Xbox;

namespace LbIntegrations.Xemu
{
    internal static class XemuCompat
    {
        internal const string ReportsSource = "https://reports.xemu.app/compatibility";
        internal const string TitlesSource = "https://xemu.app/compat.json";
        internal static readonly TimeSpan MaxAge = TimeSpan.FromHours(24);
        private static int _fetching;

        private static string Dir => XemuSettings.Dir;
        private static string ReportsPath => Path.Combine(Dir, XboxCompat.ReportsFile);
        private static string TitlesPath => Path.Combine(Dir, XboxCompat.TitlesFile);
        private static string MetaPath => Path.Combine(Dir, "xemu-compat.meta");

        private static readonly HttpClient Http = MakeClient();

        private static HttpClient MakeClient()
        {
            var c = new HttpClient(new HttpClientHandler { AllowAutoRedirect = true, AutomaticDecompression = DecompressionMethods.All });
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

        /// <summary>When the list was last asked for, or null when there is no copy.</summary>
        public static DateTime? Asked() => File.Exists(ReportsPath) || File.Exists(TitlesPath) ? ReadMeta().Asked : null;

        public static bool IsFresh() => Asked() is DateTime a && DateTime.UtcNow - a < MaxAge;

        /// <summary>Does this text read as the reports / the titles? Null when it does, else why not.</summary>
        internal static string Problem(string json, bool reports, int previousCount)
        {
            try
            {
                int total, valid;
                if (reports) { var r = XboxCompat.ParseReports(json); total = r.Count; valid = r.Count(x => !string.IsNullOrEmpty(x.Rating)); }
                else { var t = XboxCompat.ParseTitles(json); total = t.Count; valid = t.Count(x => !string.IsNullOrEmpty(x.Status)); }
                if (total < 500) return total + " entr" + (total == 1 ? "y" : "ies") + " in it - five hundred at least were expected";
                if (previousCount > 0 && total < previousCount / 2) return total + " entries in it, against " + previousCount + " in the copy";
                if (valid * 20 < total * 19) return "only " + valid + " of its " + total + " entries have a rating";
                return null;
            }
            catch (Exception ex) { return "it does not read (" + ex.Message + ")"; }
        }

        private static int CountOf(string path, bool reports)
        {
            try { return !File.Exists(path) ? 0 : reports ? XboxCompat.ParseReports(File.ReadAllText(path)).Count : XboxCompat.ParseTitles(File.ReadAllText(path)).Count; }
            catch { return 0; }
        }

        private static void Keep(string path, string json)
        {
            Directory.CreateDirectory(Dir);
            var tmp = path + ".tmp";
            File.WriteAllText(tmp, json, new UTF8Encoding(false));
            File.Move(tmp, path, overwrite: true);
        }

        /// <summary>Both files asked for, each kept once it reads. True when there is a good copy now. Never throws.</summary>
        public static bool Fetch(TimeSpan timeout) => Fetch(timeout, false, CancellationToken.None, out _);

        /// <summary>The whole list downloaded again, from a game's window (LbipListRefresh): nothing asked "only if it changed".
        /// Null when both files were kept, else why not.</summary>
        public static string FetchWhole(LbIntegrations.Lbip.LbipListJob job)
        {
            job.Step = "Downloading xemu.app's titles and reports...";
            Fetch(TimeSpan.FromMinutes(3), true, job.Token, out var problem);
            return job.Token.IsCancellationRequested ? "cancelled" : problem;
        }

        /// <summary>When the whole list was last downloaded - its reports, which always come whole - or null.</summary>
        public static DateTime? Downloaded() => File.Exists(ReportsPath) ? File.GetLastWriteTime(ReportsPath) : (DateTime?)null;

        private static bool Fetch(TimeSpan timeout, bool force, CancellationToken token, out string problem)
        {
            problem = null;
            if (Interlocked.Exchange(ref _fetching, 1) == 1) { problem = "already being downloaded"; return false; }
            var failed = new List<string>();
            try
            {
                var meta = ReadMeta();
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(token);
                cts.CancelAfter(timeout);
                var said = new List<string>();

                // The titles: "only if they changed".
                try
                {
                    bool have = File.Exists(TitlesPath);
                    using var req = new HttpRequestMessage(HttpMethod.Get, TitlesSource);
                    if (!force && have && !string.IsNullOrEmpty(meta.ETag)) req.Headers.TryAddWithoutValidation("If-None-Match", meta.ETag);
                    if (!force && have && !string.IsNullOrEmpty(meta.LastModified)) req.Headers.TryAddWithoutValidation("If-Modified-Since", meta.LastModified);
                    using var resp = Http.SendAsync(req, cts.Token).GetAwaiter().GetResult();
                    if (resp.StatusCode == HttpStatusCode.NotModified) said.Add("titles unchanged");
                    else if (!resp.IsSuccessStatusCode) { said.Add("titles not fetched (HTTP " + (int)resp.StatusCode + ")"); failed.Add("the titles: HTTP " + (int)resp.StatusCode); }
                    else
                    {
                        var json = resp.Content.ReadAsStringAsync(cts.Token).GetAwaiter().GetResult();
                        var bad = Problem(json, false, CountOf(TitlesPath, false));
                        if (bad != null) { said.Add("titles not kept - " + bad); failed.Add("the titles: " + bad); }
                        else
                        {
                            Keep(TitlesPath, json);
                            meta.ETag = resp.Headers.ETag?.Tag;
                            meta.LastModified = resp.Content.Headers.LastModified?.ToString("R", CultureInfo.InvariantCulture);
                            said.Add(XboxCompat.ParseTitles(json).Count + " titles");
                        }
                    }
                }
                catch (Exception ex) { said.Add("titles not fetched (" + ex.Message + ")"); failed.Add("the titles: " + ex.Message); }

                // The reports: whole.
                try
                {
                    using var resp = Http.GetAsync(ReportsSource, cts.Token).GetAwaiter().GetResult();
                    if (!resp.IsSuccessStatusCode) { said.Add("reports not fetched (HTTP " + (int)resp.StatusCode + ")"); failed.Add("the reports: HTTP " + (int)resp.StatusCode); }
                    else
                    {
                        var json = resp.Content.ReadAsStringAsync(cts.Token).GetAwaiter().GetResult();
                        var bad = Problem(json, true, CountOf(ReportsPath, true));
                        if (bad != null) { said.Add("reports not kept - " + bad); failed.Add("the reports: " + bad); }
                        else { Keep(ReportsPath, json); said.Add(XboxCompat.ParseReports(json).Count + " reports"); }
                    }
                }
                catch (Exception ex) { said.Add("reports not fetched (" + ex.Message + ")"); failed.Add("the reports: " + ex.Message); }

                if (File.Exists(ReportsPath) || File.Exists(TitlesPath)) { meta.Asked = DateTime.UtcNow; WriteMeta(meta); }
                Log.Info("compatibility list: " + string.Join(", ", said));
                if (failed.Count > 0) problem = string.Join("; ", failed);
                return File.Exists(ReportsPath) || File.Exists(TitlesPath);
            }
            catch (Exception ex) { problem = ex.Message; Log.Info("compatibility list: not fetched (" + ex.Message + ") - the copy is kept"); return File.Exists(ReportsPath); }
            finally { Interlocked.Exchange(ref _fetching, 0); }
        }

        /// <summary>In the background, when the copy is older than <see cref="MaxAge"/> (or missing): never waited on.
        /// <paramref name="done"/> is told when a fetch has come back.</summary>
        public static void RefreshSoon(Action done = null)
        {
            try
            {
                if (IsFresh()) return;
                Task.Run(() => { Fetch(TimeSpan.FromSeconds(60)); try { done?.Invoke(); } catch { } });
            }
            catch { }
        }

        /// <summary>At the emulator's install and update: asked again whatever the copy's age.</summary>
        public static void RefreshNow() { try { Task.Run(() => Fetch(TimeSpan.FromSeconds(60))); } catch { } }

        /// <summary>One line for the log.</summary>
        public static string Describe(string titleId)
        {
            var (r, t) = XboxCompat.Xemu(titleId);
            if (r == null && t == null) return XboxCompat.HasXemuList() ? "not in xemu's compatibility list" : "no compatibility list yet";
            return XboxCompat.XemuState(r, t)
                   + (r != null ? " (report of " + XboxCompat.Day(r.When) + ", xemu " + r.XemuVersion + ")" : "")
                   + (t != null && r != null && t.Status != r.Rating ? "; the site says " + t.Status : "");
        }
    }
}
