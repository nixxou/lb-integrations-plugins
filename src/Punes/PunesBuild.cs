// The one puNES build this plugin installs, and how to tell which one is on disk.
//
// ONE BUILD, PINNED: puNES 0.111 (released 2024-02-25, the latest on 18/10 - GitHub API, and /releases/latest redirects to
// /releases/tag/v0.111), the x86_64 Direct3D 9 build - the Windows default of the README's build table ("ENABLE_OPENGL ...
// instead of Direct3D 9 (only for Windows)"). Its asset carries no "digest" (it predates GitHub's), so the SHA-256 below
// was computed on the download on 18/10, and is checked before anything is extracted: a changed file stops the install.
// NO WIP BUILD CAN BE HAD (18/10, docs\punes.md section 1): AppVeyor's last build is of 2022, the GitHub Actions
// artifacts the README links through nightly.link have expired (404), and master's builds fail since 2026-07.
// A NEW BUILD IS A CHANGE HERE: Version, FileName, Sha256 and the executables' MD5s.
//
// A NEWER RELEASE is only noticed, never installed on its own: /releases/latest is asked (a HEAD, its redirect read - no
// GitHub API, which allows 60 requests an hour), at most once per process and off the host's thread, and a tag other than
// the pinned one is said in the log and on the Nixx page.
//
// THE VERSION ON DISK: the exe's version resource says "0.111" (measured), but so would a copy the user unpacked himself -
// an install of ours is told by the exe's MD5 (the two Windows x86_64 builds of 0.111, measured) or by the stamp written
// beside it (lbip-punes-build.txt). Anything else is the user's: GetCurrentVersion null, no update offered over it.

using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace LbIntegrations.Punes
{
    internal static class PunesBuild
    {
        public const string Version = "0.111";
        public const string Tag = "v0.111";
        public const string FileName = "punes-0.111-x86_64-win-d3d9.zip";
        public const string Url = Home + "/releases/download/" + Tag + "/" + FileName;
        public const string Sha256 = "eb973f8dfde20f831b3ccd39a182fffd39b80c9070eadf58d3964062cc470e2b";
        public const string Home = "https://github.com/punesemu/puNES";

        /// <summary>Written beside punes64.exe at install: the version, then the archive it came from.</summary>
        public const string StampName = "lbip-punes-build.txt";

        /// <summary>The executables this plugin knows, by MD5 (measured 18/10 on the two zips of v0.111).</summary>
        private static readonly Dictionary<string, string> KnownExecutables = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["fbedc6c1ec63f13a3e5b7c832941cc4e"] = "0.111",   // punes-0.111-x86_64-win-d3d9.zip, punes64.exe
            ["2be2fc2df34a7cf8cd3a4b18a8027254"] = "0.111",   // punes-0.111-x86_64-win-opengl.zip, punes64.exe
        };

        private static readonly HttpClient Http = MakeClient(true);
        private static readonly HttpClient NoRedirect = MakeClient(false);

        private static HttpClient MakeClient(bool redirects)
        {
            var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = redirects }) { Timeout = redirects ? TimeSpan.FromMinutes(5) : TimeSpan.FromSeconds(10) };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("lb-integrations-plugins/1.0");
            return client;
        }

        // ── the download ─────────────────────────────────────────────────────

        /// <summary>The archive, from the developer's copy or GitHub, its SHA-256 the pinned one. Throws when it is not.</summary>
        public static void Fetch(string destination, Action<double> progress, Func<bool> shouldCancel)
        {
            if (FromDevCopy(destination)) return;
            Download(Url, destination, progress, shouldCancel);
            var actual = Hash(SHA256.Create(), destination);
            if (!string.Equals(actual, Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("the file downloaded from " + Url + " is not puNES " + Version + " as this plugin knows it (SHA-256 " + actual + ")");
            Log.Info("downloaded " + Url + ", SHA-256 checked");
        }

        private static void Download(string url, string destination, Action<double> progress, Func<bool> shouldCancel)
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
        }

        /// <summary>THE DEVELOPER'S COPY: an archive put in %LOCALAPPDATA%\lb-integrations-plugins\dev\ (deploy-dev.ps1's
        /// vendored-archive block, as for RASnes9x and Demul). Taken only when its SHA-256 is the pinned one.</summary>
        private static bool FromDevCopy(string destination)
        {
            try
            {
                var dev = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "lb-integrations-plugins", "dev", FileName);
                if (!File.Exists(dev)) return false;
                var actual = Hash(SHA256.Create(), dev);
                if (!string.Equals(actual, Sha256, StringComparison.OrdinalIgnoreCase))
                {
                    Log.Warn("the developer's copy " + dev + " is not the pinned file (SHA-256 " + actual + ") - downloading instead");
                    return false;
                }
                File.Copy(dev, destination, overwrite: true);
                Log.Info("installing from the developer's copy " + dev + ", SHA-256 checked - not downloaded");
                return true;
            }
            catch (Exception ex) { Log.Warn("the developer's copy could not be used - downloading instead", ex); return false; }
        }

        // ── a newer release, noticed ─────────────────────────────────────────

        private static string _latestTag;
        private static int _asked;

        /// <summary>The tag /releases/latest redirected to, once asked (null before, or when it could not be read).</summary>
        public static string LatestTag => _latestTag;

        /// <summary>Asks once per process, on a pool thread: never holds the host. A tag other than the pinned one is logged.</summary>
        public static void NoticeLatest()
        {
            if (System.Threading.Interlocked.Exchange(ref _asked, 1) == 1) return;
            Task.Run(() =>
            {
                try
                {
                    using var req = new HttpRequestMessage(HttpMethod.Head, Home + "/releases/latest");
                    using var resp = NoRedirect.SendAsync(req).GetAwaiter().GetResult();
                    var where = resp.Headers.Location?.ToString() ?? "";
                    var tag = where.Contains("/tag/") ? Uri.UnescapeDataString(where.Substring(where.LastIndexOf("/tag/", StringComparison.Ordinal) + 5)) : null;
                    if (tag == null) { Log.Info("puNES's releases page gave no tag (" + (int)resp.StatusCode + " " + where + ")"); return; }
                    _latestTag = tag;
                    if (!string.Equals(tag, Tag, StringComparison.OrdinalIgnoreCase))
                        Log.Info("puNES " + tag + " is out; this plugin installs " + Tag + " - a newer one comes with an update of the plugin");
                }
                catch (Exception ex) { Log.Info("could not reach puNES's releases page - " + ex.Message); }
            });
        }

        /// <summary>"v0.112" -> "0.112"; null when there is no version in it.</summary>
        internal static string VersionOf(string text)
        {
            var m = Regex.Match(text ?? "", @"([0-9]+(?:\.[0-9]+){1,3})");
            return m.Success ? m.Groups[1].Value : null;
        }

        // ── the version on disk ──────────────────────────────────────────────

        /// <summary>The version of the puNES at <paramref name="exe"/> when it is one of ours - a known executable by its
        /// MD5, else what an install of ours stamped beside it - else null, an ordinary answer.</summary>
        public static string InstalledVersion(string exe)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(exe) || !File.Exists(exe)) return null;
                return KnownVersion(exe) ?? Stamped(Path.GetDirectoryName(Path.GetFullPath(exe)));
            }
            catch (Exception ex) { Log.Warn("could not read the version of " + exe, ex); return null; }
        }

        private static string _hashedPath, _hashedVersion;
        private static DateTime _hashedWrite;
        private static long _hashedLength;

        /// <summary>By MD5, cached on the path, its size and its write time: the host asks often, the file is 27 MB.</summary>
        private static string KnownVersion(string exe)
        {
            var info = new FileInfo(Path.GetFullPath(exe));
            lock (KnownExecutables)
            {
                if (string.Equals(info.FullName, _hashedPath, StringComparison.OrdinalIgnoreCase) && info.LastWriteTimeUtc == _hashedWrite && info.Length == _hashedLength)
                    return _hashedVersion;
                var md5 = Hash(MD5.Create(), info.FullName);
                KnownExecutables.TryGetValue(md5, out var version);
                _hashedPath = info.FullName; _hashedWrite = info.LastWriteTimeUtc; _hashedLength = info.Length; _hashedVersion = version;
                if (version == null) Log.Info(info.Name + " " + md5 + " is not a build this plugin knows");
                return version;
            }
        }

        public static void Stamp(string installDir)
        {
            try { File.WriteAllText(Path.Combine(installDir, StampName), Version + Environment.NewLine + FileName + Environment.NewLine); }
            catch (Exception ex) { Log.Info("could not write the build stamp - " + ex.Message); }
        }

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

        /// <summary>Is <paramref name="candidate"/> later than <paramref name="installed"/>? Anything that does not parse is
        /// not an update.</summary>
        public static bool IsNewer(string candidate, string installed)
        {
            if (string.IsNullOrWhiteSpace(installed)) return false;
            return System.Version.TryParse(candidate, out var c) && System.Version.TryParse(installed, out var i) && c > i;
        }

        internal static string Hash(HashAlgorithm algorithm, string path)
        {
            using (algorithm)
            using (var stream = File.OpenRead(path))
                return Convert.ToHexString(algorithm.ComputeHash(stream)).ToLowerInvariant();
        }
    }
}
