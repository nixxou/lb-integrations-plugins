// The FCEUX build this plugin installs: the project's latest numbered RELEASE, its 64-bit Qt / SDL build, and how
// to tell which one is on disk.
//
// THE RELEASES (GitHub API, repos/TASEmulators/fceux/releases, read 18/10/2026 - docs\fceux.md §1): fceux-2.7.0 (latest,
// 2026-10-14), v2.6.6, v2.6.5, fceux-2.6.4 - THE TAG SCHEME IS NOT STABLE, so the version is the tag's \d+.\d+.\d+ and the
// asset's name is made from it: fceux-<version>-win64-QtSDL.zip (2.7.0 names it so). 2.7.0's assets carry a SHA-256
// in the API ("digest", checked before anything is extracted); 2.6.x's do not. The release note of 2.7.0 says its tag was
// bumped once after a bad build: a release's assets can be replaced, so the digest of the moment is the one checked.
//
// NOT THE INTERIM BUILD: "interim-build" is a rolling pre-release refreshed at every build of master (appveyor.yml), its
// assets without a version in their name (fceux-win64.zip). On 18/10 it was the same commit as 2.7.0 but another binary
// (fceux64.exe MD5 617c9f26... against the release's 1c5bba0d...). A known version is installed, never it.
//
// THE QT BUILD, not the classic one (fceux-<v>-win64.zip, fceux64.exe, drivers\win - Mehdi's choice, 18/10): its full
// screen is a borderless window (the classic build's is exclusive DirectDraw, which changes the display's mode), its
// renderer is OpenGL (ReShade-friendly), its hotkeys are text ("Shift+F10") and its config "SDL.Key = value" (FceuxCfg).
//
// WITHOUT GITHUB'S API (60 requests an hour to an address): HEAD on releases/latest answers 302 to .../releases/tag/<tag>
// (measured 18/10 with curl) - the tag, and the asset's URL made from it, taken without a digest to check.
//
// THE VERSION ON DISK: qfceux.exe has NO version resource; its bytes carry "FCEUX 2.7.0" (measured 18/10)
// (FCEU_NAME_AND_VERSION, src\version.h) - enough to DESCRIBE a build, never to say it is ours. Ours: the stamp
// lbip-fceux-build.txt the install writes beside the executable.
//
// THE ZIP (211 entries, measured 18/10): bin\ (qfceux.exe, Qt6, SDL2, ffmpeg, lua), doc\, luaScripts\, palettes\, tools\ -
// extracted as it is into the install, the executable at bin\qfceux.exe. No fceux.cfg: FCEUX makes it at its first exit,
// beside the executable (bin\fceux.cfg).

using System;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace LbIntegrations.Fceux
{
    internal sealed class FceuxRelease
    {
        public string Version, Tag, Url, FileName, Sha256;
    }

    internal static class FceuxBuild
    {
        public const string Api = "https://api.github.com/repos/TASEmulators/fceux/releases/latest";
        public const string LatestPage = "https://github.com/TASEmulators/fceux/releases/latest";
        public const string Home = "https://fceux.com/";
        public const string Project = "https://github.com/TASEmulators/fceux";
        public const string StampName = "lbip-fceux-build.txt";

        public static string AssetName(string version) => "fceux-" + version + "-win64-QtSDL.zip";
        private static string DownloadUrl(string tag, string version) => Project + "/releases/download/" + tag + "/" + AssetName(version);

        private static readonly HttpClient Http = MakeClient();

        private static HttpClient MakeClient()
        {
            var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = true }) { Timeout = TimeSpan.FromMinutes(3) };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("lb-integrations-plugins/1.0");
            return client;
        }

        private static FceuxRelease _latest;
        private static DateTime _asked;

        /// <summary>"fceux-2.7.0", "v2.6.6" -> "2.7.0", "2.6.6"; null when the tag holds no version.</summary>
        internal static string VersionOf(string tag)
        {
            var m = Regex.Match(tag ?? "", @"\d+\.\d+\.\d+");
            return m.Success ? m.Value : null;
        }

        /// <summary>The latest release's win64 build, or null (unreachable). Asked at most every 10 minutes.</summary>
        public static FceuxRelease Latest()
        {
            if (_latest != null && DateTime.UtcNow - _asked < TimeSpan.FromMinutes(10)) return _latest;
            try
            {
                using var doc = JsonDocument.Parse(Http.GetStringAsync(Api).GetAwaiter().GetResult());
                var root = doc.RootElement;
                var tag = root.GetProperty("tag_name").GetString();
                var version = VersionOf(tag);
                if (version == null) Log.Warn("the latest release's tag (" + tag + ") holds no version");
                else
                {
                    foreach (var asset in root.GetProperty("assets").EnumerateArray())
                    {
                        if (!string.Equals(asset.GetProperty("name").GetString(), AssetName(version), StringComparison.OrdinalIgnoreCase)) continue;
                        string sha = null;
                        if (asset.TryGetProperty("digest", out var d) && d.ValueKind == JsonValueKind.String && d.GetString().StartsWith("sha256:"))
                            sha = d.GetString().Substring(7);
                        _latest = new FceuxRelease { Version = version, Tag = tag, Url = asset.GetProperty("browser_download_url").GetString(), FileName = AssetName(version), Sha256 = sha };
                        _asked = DateTime.UtcNow;
                        return _latest;
                    }
                    Log.Warn("the latest release (" + tag + ") has no " + AssetName(version));
                }
            }
            catch (Exception ex) { Log.Warn("could not read FCEUX's latest release - asking its page instead", ex); }
            return FromRedirect();
        }

        /// <summary>The release's tag from where releases/latest redirects to.</summary>
        private static FceuxRelease FromRedirect()
        {
            try
            {
                using var handler = new HttpClientHandler { AllowAutoRedirect = false };
                using var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(30) };
                http.DefaultRequestHeaders.UserAgent.ParseAdd("lb-integrations-plugins/1.0");
                using var resp = http.SendAsync(new HttpRequestMessage(HttpMethod.Head, LatestPage)).GetAwaiter().GetResult();
                var where = resp.Headers.Location?.ToString() ?? "";
                var m = Regex.Match(where, @"/releases/tag/(?<tag>[^/?#]+)");
                var version = m.Success ? VersionOf(Uri.UnescapeDataString(m.Groups["tag"].Value)) : null;
                if (version == null) { Log.Warn("FCEUX's latest release page did not say its tag (" + (int)resp.StatusCode + " " + where + ")"); return null; }
                var tag = Uri.UnescapeDataString(m.Groups["tag"].Value);
                _latest = new FceuxRelease { Version = version, Tag = tag, Url = DownloadUrl(tag, version), FileName = AssetName(version) };
                _asked = DateTime.UtcNow;
                return _latest;
            }
            catch (Exception ex) { Log.Warn("could not reach FCEUX's latest release page", ex); return null; }
        }

        public static void Fetch(FceuxRelease release, string destination, Action<double> progress, Func<bool> shouldCancel)
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
                var actual = Sha256Of(destination);
                if (!string.Equals(actual, release.Sha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("the downloaded " + release.FileName + " is not the file GitHub publishes (SHA-256 " + actual + ")");
                Log.Info("downloaded FCEUX " + release.Version + ", SHA-256 checked");
            }
            else Log.Info("downloaded FCEUX " + release.Version + " (no digest to check - GitHub's API was not reachable, or the release has none)");
        }

        internal static string Sha256Of(string path)
        {
            using var sha = SHA256.Create();
            using var s = File.OpenRead(path);
            return Convert.ToHexString(sha.ComputeHash(s)).ToLowerInvariant();
        }

        public static void Stamp(string installDir, FceuxRelease release)
        {
            try { File.WriteAllText(Path.Combine(installDir, StampName), (release?.Version ?? "") + Environment.NewLine + (release?.FileName ?? "") + Environment.NewLine); }
            catch (Exception ex) { Log.Info("could not write the build stamp - " + ex.Message); }
        }

        /// <summary>The version an install of ours stamped beside the executable, or null - an ordinary answer.</summary>
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

        /// <summary>What the executable says it is ("FCEUX 2.7.0" in its bytes), for the log and the Nixx page - never a
        /// proof that the install is ours. Null when not found.</summary>
        public static string DescribedVersion(string exe)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(exe) || !File.Exists(exe) || new FileInfo(exe).Length > 64L << 20) return null;
                var text = Encoding.Latin1.GetString(File.ReadAllBytes(exe));
                var m = Regex.Match(text, @"FCEUX (\d+\.\d+\.\d+)");
                return m.Success ? m.Groups[1].Value : null;
            }
            catch { return null; }
        }

        /// <summary>Both "x.y.z". Anything that does not parse is not an update.</summary>
        public static bool IsNewer(string candidate, string installed)
        {
            if (string.IsNullOrWhiteSpace(installed)) return true;
            return Version.TryParse(candidate, out var c) && Version.TryParse(installed, out var i) && c > i;
        }
    }
}
