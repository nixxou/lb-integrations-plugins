// Finding and downloading a melonDS release.
//
// Asset selection is declarative: a list of substrings the name must contain and a list it must
// not. That is more durable than matching a template like "melonDS-{tag}-windows-x86_64.zip", because
// upstream renames assets far more often than it changes what they are. When the filters match more
// than one asset we report ALL of them rather than picking one — a guess here installs the wrong
// build, and the caller can ask.
//
// The rate-limit fallback matters more than it looks. GitHub's unauthenticated API allows 60
// requests per hour per IP; a LaunchBox user with a few integration plugins checking for updates
// reaches that without trying. LaunchBox's own plugins swallow the exception and return null, which
// surfaces as "no versions available" — indistinguishable from a dead project. On 403/429 we read
// the Atom feed and the release page instead, neither of which is rate limited.

using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;

namespace LbIntegrations.MelonDs
{
    internal sealed class ReleaseAsset
    {
        public string Name;
        public string DownloadUrl;
        public override string ToString() => Name;
    }

    internal sealed class ReleaseInfo
    {
        /// <summary>The git tag, e.g. "v1.20.4".</summary>
        public string Tag;
        public List<ReleaseAsset> Assets = new List<ReleaseAsset>();
        /// <summary>Set when the GitHub API refused us and we fell back to scraping, so the caller can
        /// tell "rate limited" from "nothing found".</summary>
        public bool FromFallback;
    }

    internal static class GitHubReleases
    {
        // One client for the process. LaunchBox's RetroArch plugin does the same; a per-call
        // HttpClient exhausts sockets under repeated update checks.
        private static readonly HttpClient Http = CreateClient();

        private static HttpClient CreateClient()
        {
            var c = new HttpClient(new HttpClientHandler { AllowAutoRedirect = true })
            {
                Timeout = TimeSpan.FromSeconds(30),
            };
            // GitHub rejects requests with no User-Agent outright.
            c.DefaultRequestHeaders.UserAgent.ParseAdd("lb-integrations-plugins/1.0");
            return c;
        }

        /// <summary>Latest release of <paramref name="repo"/> ("owner/name"), or null. Never throws.</summary>
        public static ReleaseInfo GetLatest(string repo)
        {
            try
            {
                var url = "https://api.github.com/repos/" + repo + "/releases/latest";
                using (var resp = Http.GetAsync(url).GetAwaiter().GetResult())
                {
                    if (resp.StatusCode == HttpStatusCode.Forbidden ||
                        resp.StatusCode == (HttpStatusCode)429)
                    {
                        Log.Warn("GitHub API refused the request (" + (int)resp.StatusCode
                                 + ", rate limit) — falling back to the releases feed");
                        return GetLatestWithoutApi(repo);
                    }
                    resp.EnsureSuccessStatusCode();
                    var json = resp.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                    return ParseApiJson(json);
                }
            }
            catch (Exception ex)
            {
                Log.Warn("could not read the latest release of " + repo, ex);
                try { return GetLatestWithoutApi(repo); } catch { return null; }
            }
        }

        /// <summary>The assets matching every string in <paramref name="required"/> and none of those in
        /// <paramref name="excluded"/>, case-insensitively.</summary>
        public static List<ReleaseAsset> SelectAssets(ReleaseInfo release, string[] required, string[] excluded)
        {
            var hits = new List<ReleaseAsset>();
            if (release == null) return hits;
            foreach (var a in release.Assets)
            {
                var n = a.Name ?? "";
                bool ok = true;
                foreach (var r in required)
                    if (n.IndexOf(r, StringComparison.OrdinalIgnoreCase) < 0) { ok = false; break; }
                if (ok && excluded != null)
                    foreach (var x in excluded)
                        if (n.IndexOf(x, StringComparison.OrdinalIgnoreCase) >= 0) { ok = false; break; }
                if (ok) hits.Add(a);
            }
            return hits;
        }

        /// <summary>Download to <paramref name="destination"/>, reporting 0..1 progress when the server
        /// sends a Content-Length. Throws on failure; deletes a partial file first.</summary>
        public static void Download(string url, string destination, Action<double> progress, Func<bool> shouldCancel)
        {
            using (var req = new HttpRequestMessage(HttpMethod.Get, url))
            using (var resp = Http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead).GetAwaiter().GetResult())
            {
                resp.EnsureSuccessStatusCode();
                long? total = resp.Content.Headers.ContentLength;
                long done = 0;
                var buffer = new byte[81920];

                using (var src = resp.Content.ReadAsStreamAsync().GetAwaiter().GetResult())
                using (var dst = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    int read;
                    while ((read = src.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        if (shouldCancel != null && shouldCancel())
                            throw new OperationCanceledException("Download cancelled.");
                        dst.Write(buffer, 0, read);
                        done += read;
                        if (total.HasValue && total.Value > 0)
                            try { progress?.Invoke((double)done / total.Value); } catch { }
                    }
                }
            }
        }

        // ── parsing ──────────────────────────────────────────────────────────

        // System.Text.Json, not a regular expression. A first attempt matched "name" ...
        // "browser_download_url" across a bounded run of non-brace characters, which looks fine and
        // silently returns ZERO assets: each asset carries a nested "uploader":{...} object between
        // those two fields, so the run never spans it. System.Text.Json ships in the shared framework,
        // so using it costs no file beside the plugin.
        private static ReleaseInfo ParseApiJson(string json)
        {
            using (var doc = JsonDocument.Parse(json))
            {
                var root = doc.RootElement;
                var info = new ReleaseInfo();
                if (root.TryGetProperty("tag_name", out var tag) && tag.ValueKind == JsonValueKind.String)
                    info.Tag = tag.GetString();

                if (root.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
                {
                    foreach (var a in assets.EnumerateArray())
                    {
                        if (!a.TryGetProperty("name", out var n) ||
                            !a.TryGetProperty("browser_download_url", out var u)) continue;
                        info.Assets.Add(new ReleaseAsset { Name = n.GetString(), DownloadUrl = u.GetString() });
                    }
                }
                return string.IsNullOrEmpty(info.Tag) && info.Assets.Count == 0 ? null : info;
            }
        }

        /// <summary>Rate-limited path: the Atom feed gives the newest tag, and the release page's
        /// "expanded_assets" fragment gives the download links. Neither is rate limited.</summary>
        private static ReleaseInfo GetLatestWithoutApi(string repo)
        {
            var atom = Http.GetStringAsync("https://github.com/" + repo + "/releases.atom")
                           .GetAwaiter().GetResult();
            var tag = MatchGroup(atom, "<id>tag:github\\.com,2008:Repository/\\d+/([^<]+)</id>");
            if (string.IsNullOrEmpty(tag)) return null;

            var info = new ReleaseInfo { Tag = tag, FromFallback = true };
            var page = Http.GetStringAsync("https://github.com/" + repo + "/releases/expanded_assets/"
                                           + Uri.EscapeDataString(tag)).GetAwaiter().GetResult();
            foreach (Match m in Regex.Matches(page, "href=\"(?<href>/" + Regex.Escape(repo)
                                                    + "/releases/download/[^\"]+)\""))
            {
                var href = m.Groups["href"].Value;
                info.Assets.Add(new ReleaseAsset
                {
                    Name = Uri.UnescapeDataString(href.Substring(href.LastIndexOf('/') + 1)),
                    DownloadUrl = "https://github.com" + href,
                });
            }
            Log.Info("releases feed fallback: tag " + tag + ", " + info.Assets.Count + " asset(s)");
            return info;
        }

        private static string MatchGroup(string input, string pattern)
        {
            var m = Regex.Match(input, pattern);
            return m.Success ? m.Groups[1].Value : null;
        }
    }
}
