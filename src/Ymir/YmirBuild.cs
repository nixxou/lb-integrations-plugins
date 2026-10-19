// The Ymir build this plugin installs: the latest STABLE release, and how to tell which one is on disk.
//
// WHERE (measured 20/10, docs\ymir.md §1): github.com/ymir-emu/Ymir - the project moved there from StrikerX3/Ymir, the old
// address redirecting. Releases "v0.4.1"...; a rolling "latest-nightly" pre-release is rebuilt at every push and is never
// taken (Mehdi 20/10: stable releases only - save states are guaranteed between stable releases alone, the README).
// /releases/latest answers the latest stable one.
//
// THE ASSET: ymir-windows-x86_64-AVX2-<tag>.zip on a processor with AVX2, ymir-windows-x86_64-SSE2-<tag>.zip otherwise (the
// same emulator, built for older processors), ymir-windows-ARM64-<tag>.zip on ARM. The API's "digest" is the asset's SHA-256
// (0.4.1 AVX2: 7a62443b...0e8, measured equal to the file's).
//
// WITHOUT GITHUB'S API (60 requests an hour to an address): /releases/latest redirects to /releases/tag/<tag>; the asset is
// then taken from /releases/download/<tag>/ without a digest to check.
//
// THE VERSION is the tag without its "v" ("0.4.1"), written in a stamp beside the exe at install (lbip-ymir-build.txt): the
// version of an install of OURS - a Ymir the user installed is his to update.

using System;
using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace LbIntegrations.Ymir
{
    internal sealed class YmirRelease
    {
        public string Version, Tag, Url, FileName, Sha256;
    }

    internal static class YmirBuild
    {
        public const string Repo = "ymir-emu/Ymir";
        public const string Api = "https://api.github.com/repos/" + Repo + "/releases/latest";
        public const string LatestPage = "https://github.com/" + Repo + "/releases/latest";
        public const string Home = "https://github.com/" + Repo;
        public const string StampName = "lbip-ymir-build.txt";

        /// <summary>What the install says about what Windows needs.</summary>
        public const string RuntimeNote = "Ymir needs nothing else installed (no runtime package) - Windows 10 or 11, 64-bit.";

        /// <summary>The build for this processor: "AVX2", "SSE2" or "ARM64" (the asset's middle part).</summary>
        internal static string Flavour()
        {
            if (RuntimeInformation.OSArchitecture == Architecture.Arm64) return "ARM64";
            try { return System.Runtime.Intrinsics.X86.Avx2.IsSupported ? "AVX2" : "SSE2"; } catch { return "SSE2"; }
        }

        public static string AssetOf(string tag, string flavour = null)
        {
            flavour ??= Flavour();
            return flavour == "ARM64" ? "ymir-windows-ARM64-" + tag + ".zip" : "ymir-windows-x86_64-" + flavour + "-" + tag + ".zip";
        }

        private static readonly HttpClient Http = MakeClient();

        private static HttpClient MakeClient()
        {
            var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = true }) { Timeout = TimeSpan.FromMinutes(5) };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("lb-integrations-plugins/1.0");
            return client;
        }

        private static YmirRelease _latest;
        private static DateTime _asked;

        public static YmirRelease Latest()
        {
            if (_latest != null && DateTime.UtcNow - _asked < TimeSpan.FromMinutes(10)) return _latest;
            try
            {
                using var doc = JsonDocument.Parse(Http.GetStringAsync(Api).GetAwaiter().GetResult());
                var root = doc.RootElement;
                var tag = root.GetProperty("tag_name").GetString();
                if (root.TryGetProperty("prerelease", out var pre) && pre.ValueKind == JsonValueKind.True) { Log.Warn("Ymir's latest release is a pre-release (" + tag + ") - not taken"); return null; }
                var want = AssetOf(tag);
                foreach (var asset in root.GetProperty("assets").EnumerateArray())
                {
                    if (!string.Equals(asset.GetProperty("name").GetString(), want, StringComparison.OrdinalIgnoreCase)) continue;
                    string sha = null;
                    if (asset.TryGetProperty("digest", out var d) && d.ValueKind == JsonValueKind.String && d.GetString().StartsWith("sha256:"))
                        sha = d.GetString().Substring(7);
                    _latest = new YmirRelease { Version = VersionOf(tag), Tag = tag, Url = asset.GetProperty("browser_download_url").GetString(), FileName = want, Sha256 = sha };
                    _asked = DateTime.UtcNow;
                    return _latest;
                }
                Log.Warn("Ymir's latest release (" + tag + ") has no " + want);
            }
            catch (Exception ex) { Log.Warn("could not read Ymir's latest release - asking its page instead", ex); }
            return FromRedirect();
        }

        /// <summary>The release's tag from where /releases/latest redirects to (/releases/tag/&lt;tag&gt;).</summary>
        private static YmirRelease FromRedirect()
        {
            try
            {
                using var handler = new HttpClientHandler { AllowAutoRedirect = false };
                using var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(30) };
                http.DefaultRequestHeaders.UserAgent.ParseAdd("lb-integrations-plugins/1.0");
                using var resp = http.SendAsync(new HttpRequestMessage(HttpMethod.Head, LatestPage)).GetAwaiter().GetResult();
                var where = resp.Headers.Location?.ToString() ?? "";
                var m = Regex.Match(where, @"/releases/tag/(?<tag>[^/?#]+)");
                if (!m.Success) { Log.Warn("Ymir's latest release page did not say its tag (" + (int)resp.StatusCode + " " + where + ")"); return null; }
                var tag = Uri.UnescapeDataString(m.Groups["tag"].Value);
                _latest = new YmirRelease { Version = VersionOf(tag), Tag = tag, Url = "https://github.com/" + Repo + "/releases/download/" + m.Groups["tag"].Value + "/" + AssetOf(tag), FileName = AssetOf(tag) };
                _asked = DateTime.UtcNow;
                return _latest;
            }
            catch (Exception ex) { Log.Warn("could not reach Ymir's latest release page", ex); return null; }
        }

        /// <summary>"0.4.1" (the "v" in front taken off).</summary>
        internal static string VersionOf(string tag) => (tag ?? "").Trim().TrimStart('v', 'V');

        public static void Fetch(YmirRelease release, string destination, Action<double> progress, Func<bool> shouldCancel)
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
                Log.Info("downloaded Ymir " + release.Version + " (" + release.FileName + "), SHA-256 checked");
            }
            else Log.Info("downloaded Ymir " + release.Version + " (no digest to check - GitHub's API was not reachable)");
        }

        public static void Stamp(string installDir, string version)
        {
            try { File.WriteAllText(Path.Combine(installDir, StampName), (version ?? "") + Environment.NewLine + Repo + Environment.NewLine + Flavour() + Environment.NewLine); }
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
