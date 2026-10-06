// The ares build this plugin installs: the project's latest RELEASE, and how to tell which one is on disk.
//
// THE RELEASES (read 06/10): ares-emulator/ares publishes a numbered release every few months - v148 on 2026-05-30 -
// with ares-windows-x64.zip (65 MB) among its assets; its "nightly" pre-release stopped in 12/2024. The latest release
// is installed, its SHA-256 checked against the one GitHub publishes for the asset (the API's "digest",
// "sha256:fa97958e..." for v148's zip).
//
// WITHOUT GITHUB'S API (60 requests an hour to an address - the bsnes plugin met it): releases/latest/download/<asset>
// redirects to the release's own download, whose path names the tag (/download/v148/); the file is then taken
// without a digest to check.
//
// THE VERSION is the tag's number ("148"): ares.exe carries no version resource, so an install writes it in a stamp.
// THE ZIP holds one folder, ares-v148\: ares.exe, librashader.dll, SDL3.dll, Database\, Shaders\ - extracted without it.

using System;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace LbIntegrations.Ares
{
    internal sealed class AresRelease
    {
        public string Version, Url, FileName, Sha256;
    }

    internal static class AresBuild
    {
        public const string Api = "https://api.github.com/repos/ares-emulator/ares/releases/latest";
        public const string AssetName = "ares-windows-x64.zip";
        public const string DirectUrl = "https://github.com/ares-emulator/ares/releases/latest/download/" + AssetName;
        public const string Home = "https://ares-emu.net/";
        public const string StampName = "lbip-ares-build.txt";

        private static readonly HttpClient Http = MakeClient();

        private static HttpClient MakeClient()
        {
            var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = true }) { Timeout = TimeSpan.FromMinutes(5) };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("lb-integrations-plugins/1.0");
            return client;
        }

        private static AresRelease _latest;
        private static DateTime _asked;

        public static AresRelease Latest()
        {
            if (_latest != null && DateTime.UtcNow - _asked < TimeSpan.FromMinutes(10)) return _latest;
            try
            {
                using var doc = JsonDocument.Parse(Http.GetStringAsync(Api).GetAwaiter().GetResult());
                var root = doc.RootElement;
                var tag = root.GetProperty("tag_name").GetString();
                foreach (var asset in root.GetProperty("assets").EnumerateArray())
                {
                    if (!string.Equals(asset.GetProperty("name").GetString(), AssetName, StringComparison.OrdinalIgnoreCase)) continue;
                    string sha = null;
                    if (asset.TryGetProperty("digest", out var d) && d.ValueKind == JsonValueKind.String && d.GetString().StartsWith("sha256:"))
                        sha = d.GetString().Substring(7);
                    _latest = new AresRelease { Version = VersionOf(tag), Url = asset.GetProperty("browser_download_url").GetString(), FileName = AssetName, Sha256 = sha };
                    _asked = DateTime.UtcNow;
                    return _latest;
                }
                Log.Warn("the latest release (" + tag + ") has no " + AssetName);
            }
            catch (Exception ex) { Log.Warn("could not read ares's latest release - asking its download instead", ex); }
            return FromDownload();
        }

        /// <summary>The release's tag from where releases/latest/download/ redirects to.</summary>
        private static AresRelease FromDownload()
        {
            try
            {
                using var handler = new HttpClientHandler { AllowAutoRedirect = false };
                using var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(30) };
                http.DefaultRequestHeaders.UserAgent.ParseAdd("lb-integrations-plugins/1.0");
                using var resp = http.SendAsync(new HttpRequestMessage(HttpMethod.Head, DirectUrl)).GetAwaiter().GetResult();
                var where = resp.Headers.Location?.ToString() ?? "";
                var m = Regex.Match(where, @"/download/(?<tag>[^/]+)/");
                if (!m.Success) { Log.Warn("ares's latest download did not say its release (" + (int)resp.StatusCode + " " + where + ")"); return null; }
                _latest = new AresRelease { Version = VersionOf(m.Groups["tag"].Value), Url = where, FileName = AssetName };
                _asked = DateTime.UtcNow;
                return _latest;
            }
            catch (Exception ex) { Log.Warn("could not reach ares's latest download", ex); return null; }
        }

        /// <summary>"v148" -> "148".</summary>
        internal static string VersionOf(string tag) => (tag ?? "").Trim().TrimStart('v', 'V');

        public static void Fetch(AresRelease release, string destination, Action<double> progress, Func<bool> shouldCancel)
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
                    throw new InvalidDataException("the downloaded " + AssetName + " is not the file GitHub publishes (SHA-256 " + actual + ")");
                Log.Info("downloaded ares " + release.Version + ", SHA-256 checked");
            }
            else Log.Info("downloaded ares " + release.Version + " (no digest to check - GitHub's API was not reachable)");
        }

        public static void Stamp(string installDir, string version)
        {
            try { File.WriteAllText(Path.Combine(installDir, StampName), (version ?? "") + Environment.NewLine + AssetName + Environment.NewLine); }
            catch (Exception ex) { Log.Info("could not write the build stamp - " + ex.Message); }
        }

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
            return double.TryParse(candidate, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var c)
                && double.TryParse(installed, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var i) && c > i;
        }
    }
}
