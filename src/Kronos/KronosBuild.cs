// The Kronos build this plugin installs, and how to tell which one is on disk.
//
// WHERE (docs\kronos.md §1, read 19/10): Kronos's Windows builds are Tradu-France's - Benjamin Siskoo's site, Kronos's
// tester then maintainer; FCare's GitHub publishes an Ubuntu binary only. Its archives page lists every build, the newest
// first, as links "/include/pages/download.php?file=holodeck_admin/...Kronos_2.7.5_-_64_Bits.zip" and its size in MiB
// ("(49,13 Mo)"); older names read "Kronos 2.6.1 64 Bits.zip". The site answers 403 without a browser's User-Agent and the
// page as Referer.
//
// AND THE PACK'S OWN 2.7.5b (Mehdi 19/10): Tradu-France's 2.7.5 cannot start an ST-V game from its command line (§8), so
// nixxou/Kronos_test publishes 2.7.5b - the same source (66bd0836d) with the fork's later fix - as a GitHub release. The
// newer of the two is installed: 2.7.5b over 2.7.5, Tradu-France's next build (2.7.6...) over 2.7.5b.
//
// NO HASH ON TRADU-FRANCE: the file downloaded is checked against the size the page gives (to the hundredth of a MiB) and
// must be a zip. 2.7.5: 51 516 897 bytes (49,13 Mo), sha256 672536ac...debbca - measured 19/10. GitHub's API gives the
// asset's exact size and its SHA-256 ("digest"), both checked.
//
// THE VERSION is the one in the file's name ("2.7.5", "2.7.5b"), written in a stamp beside the exe at install
// (lbip-kronos-build.txt) with where it came from: the version of an install of OURS - a Kronos the user installed is his
// to update. 64-bit only.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace LbIntegrations.Kronos
{
    internal sealed class KronosRelease
    {
        public string Version, Url, FileName;
        /// <summary>The size the page gives, in MiB (two decimals); null when it gives none.</summary>
        public double? MiB;
        /// <summary>GitHub's: the exact size and SHA-256; null for Tradu-France's.</summary>
        public long? Bytes;
        public string Sha256;
        /// <summary>"Tradu-France" or "GitHub".</summary>
        public string From = KronosBuild.FromTraduFrance;
    }

    internal static class KronosBuild
    {
        public const string Site = "https://tradu-france.com";
        public const string Archives = Site + "/archives/Kronos?arch=64-bits";
        public const string Home = "https://www.tradu-france.com/emulateur/Saturn/Kronos";
        public const string Source = "https://github.com/BenjaminSiskoo/Kronos_test";
        public const string Repo = "nixxou/Kronos_test";
        public const string Api = "https://api.github.com/repos/" + Repo + "/releases/latest";
        public const string FromTraduFrance = "Tradu-France", FromGitHub = "GitHub";
        public const string StampName = "lbip-kronos-build.txt";
        /// <summary>The first build whose command line starts an ST-V game (-a) by itself.</summary>
        public const string StvStartsItselfFrom = "2.7.5b";
        private const string Browser = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/130.0 Safari/537.36";

        /// <summary>What the install says about what Windows needs.</summary>
        public const string RuntimeNote = "Kronos needs the Microsoft Visual C++ runtime (x64) - its installer, vc_redist.x64.exe, comes with it.";

        private static readonly HttpClient Http = MakeClient();

        private static HttpClient MakeClient()
        {
            var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = true, AutomaticDecompression = DecompressionMethods.All }) { Timeout = TimeSpan.FromMinutes(10) };
            client.DefaultRequestHeaders.UserAgent.ParseAdd(Browser);
            return client;
        }

        private static KronosRelease _latest;
        private static DateTime _asked;

        /// <summary>The newer of Tradu-France's latest and the pack's GitHub release; either alone when the other is unreachable.</summary>
        public static KronosRelease Latest()
        {
            if (_latest != null && DateTime.UtcNow - _asked < TimeSpan.FromMinutes(10)) return _latest;
            var both = new[] { FromTradu(), FromGitHubRelease() }.Where(r => r != null).ToList();
            var best = both.OrderByDescending(r => r.Version, Comparer<string>.Create(Compare)).FirstOrDefault();
            if (best == null) return null;
            _latest = best;
            _asked = DateTime.UtcNow;
            return best;
        }

        private static KronosRelease FromTradu()
        {
            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Get, Archives);
                req.Headers.Referrer = new Uri(Archives);
                using var resp = Http.SendAsync(req).GetAwaiter().GetResult();
                resp.EnsureSuccessStatusCode();
                var page = resp.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                var best = Parse(page).OrderByDescending(r => r.Version, Comparer<string>.Create(Compare)).FirstOrDefault();
                if (best == null) Log.Warn("Tradu-France's Kronos page lists no 64-bit build");
                return best;
            }
            catch (Exception ex) { Log.Warn("could not read Tradu-France's Kronos page", ex); return null; }
        }

        private static KronosRelease FromGitHubRelease()
        {
            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Get, Api);
                req.Headers.Accept.ParseAdd("application/vnd.github+json");
                using var resp = Http.SendAsync(req).GetAwaiter().GetResult();
                if (resp.StatusCode == HttpStatusCode.NotFound) return null;  // no release published
                resp.EnsureSuccessStatusCode();
                using var doc = JsonDocument.Parse(resp.Content.ReadAsStringAsync().GetAwaiter().GetResult());
                var root = doc.RootElement;
                if (root.TryGetProperty("prerelease", out var pre) && pre.ValueKind == JsonValueKind.True) return null;
                foreach (var asset in root.GetProperty("assets").EnumerateArray())
                {
                    var name = asset.GetProperty("name").GetString() ?? "";
                    var v = NameVersion.Match(name);
                    if (!v.Success) continue;
                    string sha = null;
                    if (asset.TryGetProperty("digest", out var d) && d.ValueKind == JsonValueKind.String && d.GetString().StartsWith("sha256:"))
                        sha = d.GetString().Substring(7);
                    return new KronosRelease
                    {
                        Version = v.Groups["v"].Value, Url = asset.GetProperty("browser_download_url").GetString(), FileName = name,
                        Bytes = asset.TryGetProperty("size", out var s) && s.ValueKind == JsonValueKind.Number ? s.GetInt64() : (long?)null,
                        Sha256 = sha, From = FromGitHub,
                    };
                }
                Log.Warn(Repo + "'s latest release has no 64-bit Kronos zip");
            }
            catch (Exception ex) { Log.Warn("could not read " + Repo + "'s latest release", ex); }
            return null;
        }

        private static readonly Regex Link = new Regex(@"href=""(?<href>/include/pages/download\.php\?file=[^""]+)""", RegexOptions.IgnoreCase);
        private static readonly Regex NameVersion = new Regex(@"Kronos[ _](?<v>\d+(?:\.\d+){1,3}[a-z]?)[ _]+(?:-[ _]+)?64[ _]Bits\.zip$", RegexOptions.IgnoreCase);
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
            {
                if (release.From == FromTraduFrance) req.Headers.Referrer = new Uri(Archives);
                using var resp = Http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead).GetAwaiter().GetResult();
                resp.EnsureSuccessStatusCode();
                long? total = resp.Content.Headers.ContentLength ?? release.Bytes;
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
            if (release.Bytes != null && length != release.Bytes.Value)
                throw new InvalidDataException("the downloaded " + release.FileName + " is " + length + " bytes, not the " + release.Bytes.Value + " GitHub gives");
            if (release.MiB != null && Math.Abs(length / 1048576.0 - release.MiB.Value) > 0.006)
                throw new InvalidDataException("the downloaded " + release.FileName + " is " + length + " bytes, not the "
                                               + release.MiB.Value.ToString("0.00", CultureInfo.InvariantCulture) + " MiB Tradu-France gives");
            if (release.Sha256 != null)
            {
                using var sha = SHA256.Create();
                using var s = File.OpenRead(destination);
                var actual = Convert.ToHexString(sha.ComputeHash(s)).ToLowerInvariant();
                if (!string.Equals(actual, release.Sha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("the downloaded " + release.FileName + " is not the file GitHub publishes (SHA-256 " + actual + ")");
            }
            using (var probe = File.OpenRead(destination))
            {
                var head = new byte[2];
                if (probe.Read(head, 0, 2) != 2 || head[0] != (byte)'P' || head[1] != (byte)'K')
                    throw new InvalidDataException("the downloaded " + release.FileName + " is not a zip (the site sent a page?)");
            }
            Log.Info("downloaded Kronos " + release.Version + " from " + release.From + " (" + release.FileName + ", " + length + " bytes"
                     + (release.Sha256 != null ? ", SHA-256 checked" : release.MiB != null ? ", the size Tradu-France gives" : "") + ")");
        }

        public static void Stamp(string installDir, KronosRelease release)
        {
            var from = release?.From == FromGitHub ? "GitHub " + Repo : "Tradu-France";
            try { File.WriteAllText(Path.Combine(installDir, StampName), (release?.Version ?? "") + Environment.NewLine + from + " 64-bit" + Environment.NewLine); }
            catch (Exception ex) { Log.Info("could not write the build stamp - " + ex.Message); }
        }

        /// <summary>The version of an install of OURS (its stamp), else null.</summary>
        public static string InstalledVersion(string exe) => StampOf(exe)?[0];

        /// <summary>Where an install of ours came from ("Tradu-France 64-bit", "GitHub nixxou/Kronos_test 64-bit"), else null.</summary>
        public static string InstalledFrom(string exe)
        {
            var lines = StampOf(exe);
            return lines != null && lines.Length > 1 && !string.IsNullOrWhiteSpace(lines[1]) ? lines[1].Trim() : null;
        }

        private static string[] StampOf(string exe)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(exe) || !File.Exists(exe)) return null;
                var path = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(exe)), StampName);
                if (!File.Exists(path)) return null;
                var lines = File.ReadAllLines(path);
                if (lines.Length == 0 || string.IsNullOrWhiteSpace(lines[0])) return null;
                lines[0] = lines[0].Trim();
                return lines;
            }
            catch { return null; }
        }

        /// <summary>Whether this Kronos starts an ST-V game from its command line: an install of ours from 2.7.5b on. Any
        /// other (Tradu-France's 2.7.5, one the user installed) is started through its settings window (KronosStvStart).</summary>
        public static bool StvStartsItself(string exe)
        {
            var v = InstalledVersion(exe);
            return v != null && Compare(v, StvStartsItselfFrom) >= 0;
        }

        public static bool IsNewer(string candidate, string installed)
        {
            if (string.IsNullOrWhiteSpace(installed)) return true;
            return Compare(candidate, installed) > 0;
        }

        /// <summary>"2.7.5" &lt; "2.7.5b" &lt; "2.7.6": the numbers, then the letters after them (none before "a").</summary>
        internal static int Compare(string a, string b)
        {
            var (na, sa) = Split(a);
            var (nb, sb) = Split(b);
            var c = na.CompareTo(nb);
            return c != 0 ? c : string.CompareOrdinal(sa, sb);
        }

        private static (Version, string) Split(string v)
        {
            v = (v ?? "").Trim();
            var m = Regex.Match(v, @"^(?<n>\d+(\.\d+){0,3})(?<s>[a-z]*)", RegexOptions.IgnoreCase);
            if (!m.Success) return (new Version(0, 0, 0, 0), "");
            var parts = m.Groups["n"].Value.Split('.').Select(p => int.TryParse(p, out var i) ? i : 0).ToList();
            while (parts.Count < 4) parts.Add(0);
            return (new Version(parts[0], parts[1], parts[2], parts[3]), m.Groups["s"].Value.ToLowerInvariant());
        }
    }
}
