// The Kronos build this plugin installs, and how to tell which one is on disk.
//
// WHERE (docs\kronos.md §1, read 19/10): Kronos's Windows builds are Tradu-France's - Benjamin Siskoo's site, Kronos's
// tester then maintainer; FCare's GitHub publishes an Ubuntu binary only. Its archives page lists every build, the newest
// first, as links "/include/pages/download.php?file=holodeck_admin/...Kronos_2.7.5_-_64_Bits.zip" and its size in MiB
// ("(49,13 Mo)"); older names read "Kronos 2.6.1 64 Bits.zip". The site answers 403 without a browser's User-Agent and the
// page as Referer.
//
// NO HASH IS PUBLISHED: the file downloaded is checked against the size the page gives (to the hundredth of a MiB) and must
// be a zip. 2.7.5: 51 516 897 bytes (49,13 Mo), sha256 672536ac...debbca - measured 19/10.
//
// THE VERSION is the one in the file's name ("2.7.5"), written in a stamp beside the exe at install (lbip-kronos-build.txt):
// the version of an install of OURS - a Kronos the user installed is his to update. 64-bit only.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;

namespace LbIntegrations.Kronos
{
    internal sealed class KronosRelease
    {
        public string Version, Url, FileName;
        /// <summary>The size the page gives, in MiB (two decimals); null when it gives none.</summary>
        public double? MiB;
    }

    internal static class KronosBuild
    {
        public const string Site = "https://tradu-france.com";
        public const string Archives = Site + "/archives/Kronos?arch=64-bits";
        public const string Home = "https://www.tradu-france.com/emulateur/Saturn/Kronos";
        public const string Source = "https://github.com/BenjaminSiskoo/Kronos_test";
        public const string StampName = "lbip-kronos-build.txt";
        private const string Browser = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/130.0 Safari/537.36";

        /// <summary>What the install says about what Windows needs.</summary>
        public const string RuntimeNote = "Kronos needs the Microsoft Visual C++ runtime (x64) - its installer, vc_redist.x64.exe, comes with it.";

        private static readonly HttpClient Http = MakeClient();

        private static HttpClient MakeClient()
        {
            var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = true, AutomaticDecompression = DecompressionMethods.All }) { Timeout = TimeSpan.FromMinutes(10) };
            client.DefaultRequestHeaders.UserAgent.ParseAdd(Browser);
            client.DefaultRequestHeaders.Referrer = new Uri(Archives);
            return client;
        }

        private static KronosRelease _latest;
        private static DateTime _asked;

        public static KronosRelease Latest()
        {
            if (_latest != null && DateTime.UtcNow - _asked < TimeSpan.FromMinutes(10)) return _latest;
            try
            {
                var page = Http.GetStringAsync(Archives).GetAwaiter().GetResult();
                var best = Parse(page).OrderByDescending(r => Pad(r.Version)).FirstOrDefault();
                if (best == null) { Log.Warn("Tradu-France's Kronos page lists no 64-bit build"); return null; }
                _latest = best;
                _asked = DateTime.UtcNow;
                return best;
            }
            catch (Exception ex) { Log.Warn("could not read Tradu-France's Kronos page", ex); return null; }
        }

        private static readonly Regex Link = new Regex(@"href=""(?<href>/include/pages/download\.php\?file=[^""]+)""", RegexOptions.IgnoreCase);
        private static readonly Regex NameVersion = new Regex(@"Kronos[ _](?<v>\d+(?:\.\d+){1,3})[ _]+(?:-[ _]+)?64[ _]Bits\.zip$", RegexOptions.IgnoreCase);
        private static readonly Regex Size = new Regex(@"\((?<n>\d+(?:[,.]\d+)?)\s*Mo\)", RegexOptions.IgnoreCase);

        /// <summary>Every 64-bit build the archives page lists, with its size when the page gives it.</summary>
        internal static List<KronosRelease> Parse(string page)
        {
            var list = new List<KronosRelease>();
            var text = WebUtility.HtmlDecode(page ?? "");
            foreach (Match m in Link.Matches(text))
            {
                var href = m.Groups["href"].Value;
                var file = Uri.UnescapeDataString(href.Substring(href.IndexOf("file=", StringComparison.OrdinalIgnoreCase) + 5));
                var name = file.Substring(file.LastIndexOfAny(new[] { '/', '\\' }) + 1);
                var v = NameVersion.Match(name);
                if (!v.Success || list.Any(r => r.FileName == name)) continue;
                // Its size: the first "(n Mo)" after its name, past the link.
                double? mib = null;
                var at = text.IndexOf(name, m.Index + m.Length, StringComparison.OrdinalIgnoreCase);
                if (at >= 0)
                {
                    var s = Size.Match(text.Substring(at, Math.Min(200, text.Length - at)));
                    if (s.Success && double.TryParse(s.Groups["n"].Value.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var n)) mib = n;
                }
                list.Add(new KronosRelease { Version = v.Groups["v"].Value, Url = Site + href, FileName = name, MiB = mib });
            }
            return list;
        }

        public static void Fetch(KronosRelease release, string destination, Action<double> progress, Func<bool> shouldCancel)
        {
            using (var req = new HttpRequestMessage(HttpMethod.Get, release.Url))
            using (var resp = Http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead).GetAwaiter().GetResult())
            {
                resp.EnsureSuccessStatusCode();
                long? total = resp.Content.Headers.ContentLength;
                if (total == null && release.MiB != null) total = (long)(release.MiB.Value * 1048576);
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
                    if (total.HasValue && total.Value > 0) try { progress?.Invoke(Math.Min(1.0, (double)done / total.Value)); } catch { }
                }
            }
            var length = new FileInfo(destination).Length;
            if (release.MiB != null && Math.Abs(length / 1048576.0 - release.MiB.Value) > 0.006)
                throw new InvalidDataException("the downloaded " + release.FileName + " is " + length + " bytes, not the "
                                               + release.MiB.Value.ToString("0.00", CultureInfo.InvariantCulture) + " MiB Tradu-France gives");
            using (var probe = File.OpenRead(destination))
            {
                var head = new byte[2];
                if (probe.Read(head, 0, 2) != 2 || head[0] != (byte)'P' || head[1] != (byte)'K')
                    throw new InvalidDataException("the downloaded " + release.FileName + " is not a zip (the site sent a page?)");
            }
            Log.Info("downloaded Kronos " + release.Version + " (" + release.FileName + ", " + length + " bytes" + (release.MiB != null ? ", the size Tradu-France gives" : "") + ")");
        }

        public static void Stamp(string installDir, string version)
        {
            try { File.WriteAllText(Path.Combine(installDir, StampName), (version ?? "") + Environment.NewLine + "Tradu-France 64-bit" + Environment.NewLine); }
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
            return Pad(candidate) > Pad(installed);
        }

        private static Version Pad(string v)
        {
            v = (v ?? "").Trim();
            var m = Regex.Match(v, @"^\d+(\.\d+){0,3}");
            v = m.Success ? m.Value : "0";
            if (!v.Contains('.')) v += ".0";
            return Version.TryParse(v, out var r) ? r : new Version(0, 0);
        }
    }
}
