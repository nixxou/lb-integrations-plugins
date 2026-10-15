// The ZiNc EX build this plugin installs: the project's latest GitHub release, and how to tell which one is on disk.
//
// THE RELEASE (read 15/10): hazem-abdelghani/ZiNc-EX publishes numbered releases ("ZiNc EX v1.0.0", tag v1.0.0) with one
// asset, ZiNc-EX-v<version>.zip. It holds one folder, "ZiNc EX\", with everything: ZiNc.exe (ZiNc 1.1 itself, 32-bit),
// ZiNc-EX.exe (64-bit) and ZiNc-EX-32bit.exe, the renderers\ and the plugins ZiNc loads (controller.znc, sound.znc,
// s11player.dll), bezels\, trainer\, cfg\ and roms\ (each with a dummy.txt) and the two readmes - extracted without that
// first folder (Archives).
//
// THE SAME TAG CAN BE UPLOADED AGAIN: v1.0.0's asset was created on 07/10 and replaced on 13/10 (its updated_at), under
// the same name. So an install stamps the tag AND the asset's id - a new upload is a new id - and an update is a newer
// version, or the same version with another asset.
//
// THE VERSION ON DISK: the stamp of an install of ours, else ZiNc-EX.exe's own version resource (app.rc: VERSIONINFO
// 1,0,0,0, "ProductVersion" "1.0.0") - a ZiNc EX unpacked by hand.

using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace LbIntegrations.ZincEx
{
    internal sealed class ZincExRelease
    {
        public string Version, Url, FileName;
        public long AssetId;
    }

    internal static class ZincExBuild
    {
        public const string Repo = "hazem-abdelghani/ZiNc-EX";
        public const string Api = "https://api.github.com/repos/" + Repo + "/releases/latest";
        public const string Home = "https://github.com/" + Repo;
        public const string StampName = "lbip-zincex-build.txt";

        private static readonly Regex AssetName = new Regex(@"^ZiNc-EX-v?[0-9][0-9.]*\.zip$", RegexOptions.IgnoreCase);

        private static readonly HttpClient Http = MakeClient(true);
        private static readonly HttpClient NoRedirect = MakeClient(false);

        private static HttpClient MakeClient(bool redirects)
        {
            var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = redirects }) { Timeout = TimeSpan.FromMinutes(2) };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("lb-integrations-plugins/1.0");
            return client;
        }

        private static ZincExRelease _latest;
        private static DateTime _asked;

        /// <summary>The latest release as GitHub has it now, or null (unreachable, or no zip in it). Asked at most every 10 minutes.</summary>
        public static ZincExRelease Latest()
        {
            if (_latest != null && DateTime.UtcNow - _asked < TimeSpan.FromMinutes(10)) return _latest;
            try
            {
                var json = Http.GetStringAsync(Api).GetAwaiter().GetResult();
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                var tag = root.TryGetProperty("tag_name", out var t) ? t.GetString() : null;
                foreach (var asset in root.GetProperty("assets").EnumerateArray())
                {
                    var name = asset.GetProperty("name").GetString() ?? "";
                    if (!AssetName.IsMatch(name)) continue;
                    _latest = new ZincExRelease
                    {
                        Version = VersionOf(tag) ?? VersionOf(name),
                        Url = asset.GetProperty("browser_download_url").GetString(),
                        FileName = name,
                        AssetId = asset.TryGetProperty("id", out var id) && id.TryGetInt64(out var n) ? n : 0,
                    };
                    _asked = DateTime.UtcNow;
                    return _latest;
                }
                Log.Warn("the latest release (" + tag + ") has no ZiNc-EX-v*.zip");
            }
            catch (Exception ex) { Log.Warn("could not read ZiNc EX's latest release - asking its page instead", ex); }
            return FromRedirect();
        }

        /// <summary>WITHOUT GITHUB'S API, which allows 60 requests an hour to an address: /releases/latest answers with a
        /// redirect to /releases/tag/&lt;tag&gt;, and the asset is named after the tag (ZiNc-EX-v1.0.0.zip). No asset id that way:
        /// a re-upload under the same tag is not seen until the API answers again.</summary>
        private static ZincExRelease FromRedirect()
        {
            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Head, Home + "/releases/latest");
                using var resp = NoRedirect.SendAsync(req).GetAwaiter().GetResult();
                var where = resp.Headers.Location?.ToString() ?? "";
                var tag = where.Contains("/tag/") ? Uri.UnescapeDataString(where.Substring(where.LastIndexOf("/tag/", StringComparison.Ordinal) + 5)) : null;
                var version = VersionOf(tag);
                if (version == null) { Log.Warn("the releases page gave no tag (" + (int)resp.StatusCode + " " + where + ")"); return null; }
                var file = "ZiNc-EX-v" + version + ".zip";
                _latest = new ZincExRelease { Version = version, FileName = file, Url = Home + "/releases/download/" + tag + "/" + file };
                _asked = DateTime.UtcNow;
                return _latest;
            }
            catch (Exception ex) { Log.Warn("could not reach ZiNc EX's releases page", ex); return null; }
        }

        /// <summary>"v1.0.0" or "ZiNc-EX-v1.0.0.zip" -> "1.0.0"; null when there is no version in it.</summary>
        internal static string VersionOf(string text)
        {
            var m = Regex.Match(text ?? "", @"([0-9]+(?:\.[0-9]+){1,3})");
            return m.Success ? m.Groups[1].Value : null;
        }

        public static void Fetch(string url, string destination, Action<double> progress, Func<bool> shouldCancel)
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            using var resp = Http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead).GetAwaiter().GetResult();
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
            Log.Info("downloaded " + url + " (" + done + " bytes)");
        }

        // ── the stamp ────────────────────────────────────────────────────────

        /// <summary>Written beside ZiNc-EX.exe: the version, the asset's id (0 when unknown), its file name.</summary>
        public static void Stamp(string installDir, ZincExRelease release)
        {
            try { File.WriteAllLines(Path.Combine(installDir, StampName), new[] { release.Version ?? "", release.AssetId.ToString(), release.FileName ?? "" }); }
            catch (Exception ex) { Log.Info("could not write the build stamp - " + ex.Message); }
        }

        private static string[] StampLines(string installDir)
        {
            try
            {
                var path = Path.Combine(installDir ?? "", StampName);
                return File.Exists(path) ? File.ReadAllLines(path) : null;
            }
            catch { return null; }
        }

        /// <summary>Did an install of ours put this ZiNc EX here?</summary>
        public static bool IsOurs(string exe)
        {
            try { return !string.IsNullOrWhiteSpace(exe) && StampLines(Path.GetDirectoryName(Path.GetFullPath(exe))) is string[] l && l.Length > 0 && l[0].Trim().Length > 0; }
            catch { return false; }
        }

        /// <summary>The version of the ZiNc EX at <paramref name="exe"/>: our stamp, else its version resource, else null.</summary>
        public static string InstalledVersion(string exe)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(exe) || !File.Exists(exe)) return null;
                var lines = StampLines(Path.GetDirectoryName(Path.GetFullPath(exe)));
                if (lines != null && lines.Length > 0 && lines[0].Trim().Length > 0) return lines[0].Trim();
                var info = FileVersionInfo.GetVersionInfo(exe);
                return VersionOf(info.ProductVersion) ?? VersionOf(info.FileVersion);
            }
            catch (Exception ex) { Log.Warn("could not read the version of " + exe, ex); return null; }
        }

        /// <summary>Is <paramref name="release"/> an update of the install of ours at <paramref name="exe"/>: a newer version,
        /// or the same one uploaded again (another asset id - both known).</summary>
        public static bool IsUpdate(ZincExRelease release, string exe)
        {
            if (release == null) return false;
            var lines = StampLines(Path.GetDirectoryName(Path.GetFullPath(exe)));
            var installed = lines != null && lines.Length > 0 ? lines[0].Trim() : null;
            if (string.IsNullOrWhiteSpace(installed)) return true;
            if (Version.TryParse(release.Version, out var c) && Version.TryParse(installed, out var i))
            {
                if (c != i) return c > i;
                long.TryParse(lines.Length > 1 ? lines[1].Trim() : "", out var asset);
                return release.AssetId != 0 && asset != 0 && release.AssetId != asset;
            }
            return false;
        }

        /// <summary>A ZiNc-EX.exe, ZiNc-EX-32bit.exe or ZiNc.exe of this folder is running - an update would fail half way.</summary>
        public static bool Running(string installDir)
        {
            try
            {
                var dir = Path.GetFullPath(installDir).TrimEnd('\\');
                foreach (var name in new[] { "ZiNc-EX", "ZiNc-EX-32bit", "ZiNc" })
                    foreach (var p in Process.GetProcessesByName(name))
                        using (p)
                        {
                            try { if (string.Equals(Path.GetDirectoryName(p.MainModule?.FileName ?? "")?.TrimEnd('\\'), dir, StringComparison.OrdinalIgnoreCase)) return true; }
                            catch { return true; }   // not ours to read: taken as running, the safe way
                        }
            }
            catch { }
            return false;
        }
    }
}
