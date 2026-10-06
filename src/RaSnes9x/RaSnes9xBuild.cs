// The one RASnes9x build this plugin installs, and how to tell which one is on disk.
//
// ONE BUILD, PINNED: RASnes9x 1.2 (2024-12-09), Snes9x 1.63 with RetroAchievements - its GitHub release carries no file,
// RetroAchievements publishes the build on its own site only (retroachievements.org/bin/RASnes9x-x64.zip).
//
// TWO SOURCES (Mehdi, 06/10: "les deux"): retroachievements.org first, then the copy kept in this repository
// (vendor\rasnes9x\, see SOURCE.md there). The site answers a program's request with 403 on its API, and served the zip
// on 06/10 only to a browser's User-Agent - the one sent here. Either way the SHA-256 is checked before anything is
// extracted: a changed file stops the install, it does not run. A NEW BUILD IS A CHANGE HERE (Version, Sha256, the
// executable's MD5) and in vendor\rasnes9x\.
//
// THE VERSION ON DISK. RASnes9x.exe's version resource says Snes9x's ("1.63"), not RASnes9x's: a known executable is
// recognised by its MD5, and an install of ours also writes a stamp beside it.
//
// RA_Integration.dll IS NOT IN THE ZIP: the emulator downloads it from RetroAchievements at its first start; the plugin
// does it before, the same way (RaSnes9xToolset).

using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;

namespace LbIntegrations.RaSnes9x
{
    internal static class RaSnes9xBuild
    {
        public const string Version = "1.2";

        public const string FileName = "RASnes9x-x64-1.2.zip";

        /// <summary>RetroAchievements' own download, tried first.</summary>
        public const string SiteUrl = "https://retroachievements.org/bin/RASnes9x-x64.zip";

        /// <summary>The copy in this repository, when the site refuses or has moved on to another file.</summary>
        public const string RepoUrl = "https://raw.githubusercontent.com/nixxou/lb-integrations-plugins/main/vendor/rasnes9x/" + FileName;

        public const string Sha256 = "51d59b891224919bdf2a6275ac38aff2506b92b5279f08ba6af5557e78bdfff1";

        public const string Home = "https://github.com/RetroAchievements/RASnes9x";

        /// <summary>Written beside RASnes9x.exe at install: the version, then the archive it came from.</summary>
        public const string StampName = "lbip-rasnes9x-build.txt";

        /// <summary>The executables this plugin knows, by MD5.</summary>
        private static readonly Dictionary<string, string> KnownExecutables = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["e81f98f95eec0fd69166b4ac97a9f2be"] = "1.2",   // RASnes9x-x64.zip of 2024-12-09 (Snes9x 1.63)
        };

        private static readonly HttpClient Http = MakeClient();

        private static HttpClient MakeClient()
        {
            var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = true }) { Timeout = TimeSpan.FromMinutes(2) };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64)");
            return client;
        }

        /// <summary>The archive, from the developer's copy, the site or this repository - the first whose SHA-256 is the
        /// pinned one. Throws when none is.</summary>
        public static void Fetch(string destination, Action<double> progress, Func<bool> shouldCancel)
        {
            if (FromDevCopy(destination)) return;
            string why = null;
            foreach (var url in new[] { SiteUrl, RepoUrl })
            {
                try
                {
                    Download(url, destination, progress, shouldCancel);
                    var actual = Hash(SHA256.Create(), destination);
                    if (string.Equals(actual, Sha256, StringComparison.OrdinalIgnoreCase))
                    {
                        Log.Info("downloaded " + url + ", SHA-256 checked");
                        return;
                    }
                    why = url + " is not the expected file (SHA-256 " + actual + ")";
                    Log.Warn(why);
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex) { why = url + ": " + ex.Message; Log.Warn("could not download " + url, ex); }
            }
            throw new InvalidDataException("RASnes9x " + Version + " could not be downloaded - " + why);
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

        /// <summary>THE DEVELOPER'S COPY: deploy-dev.ps1 -Plugin RaSnes9x puts the repository's archive in
        /// %LOCALAPPDATA%\lb-integrations-plugins\dev\. Taken only when its SHA-256 is the pinned one.</summary>
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

        /// <summary>The version of the RASnes9x.exe at <paramref name="exe"/>: a known executable by its MD5, else what an
        /// install of ours stamped beside it, else null - an ordinary answer.</summary>
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

        /// <summary>By MD5, cached on the path and its write time: the host asks often, the file is 12 MB.</summary>
        private static string KnownVersion(string exe)
        {
            var full = Path.GetFullPath(exe);
            var write = File.GetLastWriteTimeUtc(full);
            lock (KnownExecutables)
            {
                if (string.Equals(full, _hashedPath, StringComparison.OrdinalIgnoreCase) && write == _hashedWrite) return _hashedVersion;
                var md5 = Hash(MD5.Create(), full);
                KnownExecutables.TryGetValue(md5, out var version);
                _hashedPath = full; _hashedWrite = write; _hashedVersion = version;
                if (version == null) Log.Info("RASnes9x.exe " + md5 + " is not a build this plugin knows");
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
            if (string.IsNullOrWhiteSpace(installed)) return true;
            return System.Version.TryParse(candidate, out var c) && System.Version.TryParse(installed, out var i) && c > i;
        }

        private static string Hash(HashAlgorithm algorithm, string path)
        {
            using (algorithm)
            using (var stream = File.OpenRead(path))
                return Convert.ToHexString(algorithm.ComputeHash(stream)).ToLowerInvariant();
        }
    }
}
