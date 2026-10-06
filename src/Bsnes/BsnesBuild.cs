// The bsnes build this plugin installs: the project's NIGHTLY, and how to tell which one is on disk.
//
// WHY THE NIGHTLY. bsnes's last numbered release is v115 (2020-03-20); since Near's death the project is kept up by
// the community at bsnes-emu/bsnes, which publishes its builds as one rolling GitHub release tagged "nightly" (read
// 06/10: "bsnes nightly 2026-05-23", assets bsnes-windows.zip and the other systems'). That is the build installed.
//
// THE VERSION is the nightly's date ("2026.05.23"), from the release's publication; an install writes it in a stamp
// beside the executable, and an update is a newer date. The file changes with every nightly, so there is no SHA-256 to
// pin: it is the project's own release, over HTTPS, and a zip without bsnes.exe in it stops the install.
//
// THE ZIP holds one folder, bsnes-nightly\, with bsnes.exe, settings.bml (its portable defaults), Database\, Shaders\,
// Locale\ and the licence - extracted without that first folder.

using System;
using System.IO;
using System.Net.Http;
using System.Text.Json;

namespace LbIntegrations.Bsnes
{
    internal sealed class BsnesNightly
    {
        public string Version, Url, FileName;
        public DateTime Published;
    }

    internal static class BsnesBuild
    {
        public const string Api = "https://api.github.com/repos/bsnes-emu/bsnes/releases/tags/nightly";
        public const string AssetName = "bsnes-windows.zip";
        public const string Home = "https://github.com/bsnes-emu/bsnes";
        public const string StampName = "lbip-bsnes-build.txt";

        /// <summary>The nightly's Windows build, at an address that does not move between nightlies.</summary>
        public const string DirectUrl = "https://github.com/bsnes-emu/bsnes/releases/download/nightly/" + AssetName;

        private static readonly HttpClient Http = MakeClient();

        private static HttpClient MakeClient()
        {
            var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = true }) { Timeout = TimeSpan.FromMinutes(2) };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("lb-integrations-plugins/1.0");
            return client;
        }

        private static BsnesNightly _latest;
        private static DateTime _asked;

        /// <summary>The nightly as GitHub has it now, or null (unreachable, or no Windows build). Asked at most every 10 minutes.</summary>
        public static BsnesNightly Latest()
        {
            if (_latest != null && DateTime.UtcNow - _asked < TimeSpan.FromMinutes(10)) return _latest;
            try
            {
                var json = Http.GetStringAsync(Api).GetAwaiter().GetResult();
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                var published = root.TryGetProperty("published_at", out var p) && p.TryGetDateTime(out var d) ? d.ToUniversalTime() : DateTime.MinValue;
                foreach (var asset in root.GetProperty("assets").EnumerateArray())
                {
                    if (!string.Equals(asset.GetProperty("name").GetString(), AssetName, StringComparison.OrdinalIgnoreCase)) continue;
                    // The asset's own date: a nightly re-published without a new Windows build keeps it.
                    if (asset.TryGetProperty("updated_at", out var u) && u.TryGetDateTime(out var ud)) published = ud.ToUniversalTime();
                    _latest = new BsnesNightly
                    {
                        Url = asset.GetProperty("browser_download_url").GetString(),
                        FileName = AssetName,
                        Published = published,
                        Version = published == DateTime.MinValue ? null : published.ToString("yyyy.MM.dd"),
                    };
                    _asked = DateTime.UtcNow;
                    return _latest;
                }
                Log.Warn("the nightly release has no " + AssetName);
            }
            catch (Exception ex) { Log.Warn("could not read bsnes's nightly release - asking its download instead", ex); }
            return FromDownload();
        }

        /// <summary>WITHOUT GITHUB'S API, which allows 60 requests an hour to an address (measured 06/10: 403 "rate limit
        /// exceeded"): the download itself asked for its headers - its Last-Modified is the asset's own date (06/10:
        /// "Sat, 23 May 2026 06:55:53 GMT", the API's updated_at to the second).</summary>
        private static BsnesNightly FromDownload()
        {
            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Head, DirectUrl);
                using var resp = Http.SendAsync(req).GetAwaiter().GetResult();
                resp.EnsureSuccessStatusCode();
                var when = resp.Content.Headers.LastModified?.UtcDateTime;
                if (when == null) { Log.Warn("the nightly's download gave no date"); return null; }
                _latest = new BsnesNightly { Url = DirectUrl, FileName = AssetName, Published = when.Value, Version = when.Value.ToString("yyyy.MM.dd") };
                _asked = DateTime.UtcNow;
                return _latest;
            }
            catch (Exception ex) { Log.Warn("could not reach bsnes's nightly download", ex); return null; }
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

        public static void Stamp(string installDir, string version)
        {
            try { File.WriteAllText(Path.Combine(installDir, StampName), (version ?? "") + Environment.NewLine + "nightly" + Environment.NewLine); }
            catch (Exception ex) { Log.Info("could not write the build stamp - " + ex.Message); }
        }

        /// <summary>The version an install of ours stamped beside bsnes.exe, or null - an ordinary answer.</summary>
        public static string Stamped(string installDir)
        {
            try
            {
                var path = Path.Combine(installDir ?? "", StampName);
                if (!File.Exists(path)) return null;
                var lines = File.ReadAllLines(path);
                return lines.Length > 0 && !string.IsNullOrWhiteSpace(lines[0]) ? lines[0].Trim() : null;
            }
            catch { return null; }
        }

        public static string InstalledVersion(string exe)
        {
            try { return string.IsNullOrWhiteSpace(exe) || !File.Exists(exe) ? null : Stamped(Path.GetDirectoryName(Path.GetFullPath(exe))); }
            catch { return null; }
        }

        /// <summary>Both "yyyy.MM.dd". Anything that does not parse is not an update.</summary>
        public static bool IsNewer(string candidate, string installed)
        {
            if (string.IsNullOrWhiteSpace(installed)) return true;
            return Version.TryParse(candidate, out var c) && Version.TryParse(installed, out var i) && c > i;
        }
    }
}
