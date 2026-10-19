// The Mesen build this plugin installs: MesenCE's latest RELEASE, and how to tell which one is on disk.
//
// WHICH MESEN (read 18/10, docs\mesen.md §1): SourMesen/Mesen2 is ARCHIVED - its README (master b9fa69d, 2026-06-04): "This
// repository is an archive of Mesen as it was around version 2.1.1 in mid-2025. For the latest version of Mesen, see here:
// https://github.com/nesdev-org/MesenCE". nesdev-org/MesenCE (Mesen Community Edition) is the one maintained: 2.2.1 on
// 2026-06-05. The plugin installs MesenCE; it claims an existing SourMesen 2.x as well (same exe, same files - MesenLaunch).
// Its DEVELOPMENT BUILDS are GitHub Actions artifacts (nightly.link ... "Mesen (Windows - net10.0 - AoT).zip"; the README's
// net8.0 link answers 404, measured 18/10): artifacts expire, so only releases are installed.
//
// THE ASSET: Mesen_<tag>_Windows.zip - one file, Mesen.exe, at its root (measured: 2.2.1's 34 259 023 bytes, SHA-256
// c19e10b3...fd6 = the API's "digest"). Not Mesen_<tag>_Windows_7_And_8.zip (a .NET 6 build, for those Windows only).
// THE RUNTIME: that Mesen.exe is a NATIVE AOT build (no CLR header, no hostfxr import; Windows system DLLs and the Universal
// CRT only - PE parsed 18/10): no .NET runtime and no Visual C++ redistributable to install on Windows 10 / 11.
//
// WITHOUT GITHUB'S API (60 requests an hour to an address): /releases/latest redirects to /releases/tag/<tag> (measured
// 18/10: .../releases/tag/2.2.1); the asset is then taken from /releases/download/<tag>/ without a digest to check.
//
// THE VERSION is the tag ("2.2.1"), written in a stamp beside the exe at install (lbip-mesen-build.txt): the version of an
// install of OURS. The exe's own FileVersion says the same (2.2.1), but a Mesen the user installed has one too.

using System;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace LbIntegrations.Mesen
{
    internal sealed class MesenRelease
    {
        public string Version, Url, FileName, Sha256;
    }

    internal static class MesenBuild
    {
        public const string Repo = "nesdev-org/MesenCE";
        public const string Api = "https://api.github.com/repos/" + Repo + "/releases/latest";
        public const string LatestPage = "https://github.com/" + Repo + "/releases/latest";
        public const string Home = "https://github.com/" + Repo;
        public const string StampName = "lbip-mesen-build.txt";

        /// <summary>What the install says about what Windows needs (docs\mesen.md §2).</summary>
        public const string RuntimeNote = "Mesen is a native build: nothing else to install (no .NET runtime, no Visual C++ package) - Windows 10 or 11.";

        public static string AssetOf(string version) => "Mesen_" + version + "_Windows.zip";

        private static readonly HttpClient Http = MakeClient();

        private static HttpClient MakeClient()
        {
            var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = true }) { Timeout = TimeSpan.FromMinutes(5) };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("lb-integrations-plugins/1.0");
            return client;
        }

        private static MesenRelease _latest;
        private static DateTime _asked;

        public static MesenRelease Latest()
        {
            if (_latest != null && DateTime.UtcNow - _asked < TimeSpan.FromMinutes(10)) return _latest;
            try
            {
                using var doc = JsonDocument.Parse(Http.GetStringAsync(Api).GetAwaiter().GetResult());
                var root = doc.RootElement;
                var tag = root.GetProperty("tag_name").GetString();
                var version = VersionOf(tag);
                var want = AssetOf(version);
                foreach (var asset in root.GetProperty("assets").EnumerateArray())
                {
                    if (!string.Equals(asset.GetProperty("name").GetString(), want, StringComparison.OrdinalIgnoreCase)) continue;
                    string sha = null;
                    if (asset.TryGetProperty("digest", out var d) && d.ValueKind == JsonValueKind.String && d.GetString().StartsWith("sha256:"))
                        sha = d.GetString().Substring(7);
                    _latest = new MesenRelease { Version = version, Url = asset.GetProperty("browser_download_url").GetString(), FileName = want, Sha256 = sha };
                    _asked = DateTime.UtcNow;
                    return _latest;
                }
                Log.Warn("MesenCE's latest release (" + tag + ") has no " + want);
            }
            catch (Exception ex) { Log.Warn("could not read MesenCE's latest release - asking its page instead", ex); }
            return FromRedirect();
        }

        /// <summary>The release's tag from where /releases/latest redirects to (/releases/tag/&lt;tag&gt;).</summary>
        private static MesenRelease FromRedirect()
        {
            try
            {
                using var handler = new HttpClientHandler { AllowAutoRedirect = false };
                using var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(30) };
                http.DefaultRequestHeaders.UserAgent.ParseAdd("lb-integrations-plugins/1.0");
                using var resp = http.SendAsync(new HttpRequestMessage(HttpMethod.Head, LatestPage)).GetAwaiter().GetResult();
                var where = resp.Headers.Location?.ToString() ?? "";
                var m = Regex.Match(where, @"/releases/tag/(?<tag>[^/?#]+)");
                if (!m.Success) { Log.Warn("MesenCE's latest release page did not say its tag (" + (int)resp.StatusCode + " " + where + ")"); return null; }
                var version = VersionOf(Uri.UnescapeDataString(m.Groups["tag"].Value));
                _latest = new MesenRelease { Version = version, Url = "https://github.com/" + Repo + "/releases/download/" + m.Groups["tag"].Value + "/" + AssetOf(version), FileName = AssetOf(version) };
                _asked = DateTime.UtcNow;
                return _latest;
            }
            catch (Exception ex) { Log.Warn("could not reach MesenCE's latest release page", ex); return null; }
        }

        /// <summary>"2.2.1" (a "v" in front taken off).</summary>
        internal static string VersionOf(string tag) => (tag ?? "").Trim().TrimStart('v', 'V');

        public static void Fetch(MesenRelease release, string destination, Action<double> progress, Func<bool> shouldCancel)
        {
            using (var req = new HttpRequestMessage(HttpMethod.Get, release.Url))
            using (var resp = Http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead).GetAwaiter().GetResult())
            {
                resp.EnsureSuccessStatusCode();
                long? total = resp.Content.Headers.ContentLength;
                long done = 0;
                var buffer = new byte[81920];
                using var src = resp.Content.ReadAsStreamAsync().GetAwaiter().GetResult();
                using var dst = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None);
                int read;
                while ((read = src.Read(buffer, 0, buffer.Length)) > 0)
                {
                    if (shouldCancel != null && shouldCancel()) throw new OperationCanceledException("Download cancelled.");
                    dst.Write(buffer, 0, read);
                    done += read;
                    if (total.HasValue && total.Value > 0) try { progress?.Invoke((double)done / total.Value); } catch { }
                }
            }
            if (release.Sha256 != null)
            {
                using var sha = SHA256.Create();
                using var s = File.OpenRead(destination);
                var actual = Convert.ToHexString(sha.ComputeHash(s)).ToLowerInvariant();
                if (!string.Equals(actual, release.Sha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("the downloaded " + release.FileName + " is not the file GitHub publishes (SHA-256 " + actual + ")");
                Log.Info("downloaded MesenCE " + release.Version + ", SHA-256 checked");
            }
            else Log.Info("downloaded MesenCE " + release.Version + " (no digest to check - GitHub's API was not reachable)");
        }

        public static void Stamp(string installDir, string version)
        {
            try { File.WriteAllText(Path.Combine(installDir, StampName), (version ?? "") + Environment.NewLine + Repo + Environment.NewLine); }
            catch (Exception ex) { Log.Info("could not write the build stamp - " + ex.Message); }
        }

        /// <summary>The version of an install of OURS (its stamp), else null.</summary>
        public static string InstalledVersion(string exe)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(exe) || !File.Exists(exe)) return null;
                var path = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(exe)), StampName);
                if (!File.Exists(path)) return null;
                var lines = File.ReadAllLines(path);
                return lines.Length > 0 && !string.IsNullOrWhiteSpace(lines[0]) ? lines[0].Trim() : null;
            }
            catch { return null; }
        }

        public static bool IsNewer(string candidate, string installed)
        {
            if (string.IsNullOrWhiteSpace(installed)) return true;
            return Version.TryParse(Pad(candidate), out var c) && Version.TryParse(Pad(installed), out var i) && c > i;
        }

        private static string Pad(string v)
        {
            v = (v ?? "").Trim();
            var m = Regex.Match(v, @"^\d+(\.\d+){0,3}");
            v = m.Success ? m.Value : v;
            return v.Contains('.') ? v : v + ".0";
        }
    }
}
