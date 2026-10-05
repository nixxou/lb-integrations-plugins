// The one Demul build this plugin installs, and how to tell which one is on disk.
//
// ONE BUILD, PINNED. Demul has no feed and no release page a program can read: its site lists
// archives by hand and stops at demul07_280418.7z (2018-04-28). The build the pack installs is
// 0.7 build 251220, which only Batocera publishes - so it is kept in this repository
// (vendor\demul\, see SOURCE.md there for why and what was measured) and downloaded from it. Its
// SHA-256 is checked before anything is extracted: the file is fetched over the network from a
// branch, and a changed file must stop the install, not run.
//
// A NEW BUILD IS A CHANGE HERE: the archive goes into vendor\demul\, and Url, Sha256, Version and
// the executable's MD5 below move with it.
//
// THE VERSION ON DISK. demul.exe carries no version resource. What it does carry is "0.7" and its
// build date, and the build date is what tells two builds apart - so a known executable is
// recognised by its MD5, and an install of ours also writes a stamp beside it.

using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;

namespace LbIntegrations.Demul
{
    internal static class DemulBuild
    {
        /// <summary>"0.7.20251220": the version, then the build date in full. NOT the date as the archive
        /// names write it: demul07_280418.7z is 28/04/2018 (ddmmyy, the executable says Apr 28 2018) while
        /// demul_251220.7z is 2025-12-20 (yymmdd, the executable says Dec 20 2025) - compared as written,
        /// the 2018 build would look the newer.</summary>
        public const string Version = "0.7.20251220";

        public const string FileName = "demul_251220.7z";

        public const string Url = "https://raw.githubusercontent.com/nixxou/lb-integrations-plugins/main/vendor/demul/" + FileName;

        public const string Sha256 = "d27023b24c0764bab88dab6c11902893d9061c2cb79d513b120509151144f659";

        public const string Home = "http://demul.emulation64.com/";

        /// <summary>Written beside demul.exe at install: the version, then the archive it came from.</summary>
        public const string StampName = "lbip-demul-build.txt";

        /// <summary>The executables this plugin knows, by MD5, measured on the two archives.</summary>
        private static readonly Dictionary<string, string> KnownExecutables = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["aff2f5eafcb1c32c28de1a98b6ae5526"] = "0.7.20251220",   // demul_251220.7z (Batocera), 2025-12-20
            ["ce0a6fd5552903311a8935b6f60e26ad"] = "0.7.20180428",   // demul07_280418.7z (the site), 2018-04-28
        };

        private static readonly HttpClient Http = MakeClient();

        private static HttpClient MakeClient()
        {
            var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = true })
            {
                Timeout = TimeSpan.FromMinutes(2),
            };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("lb-integrations-plugins/1.0");
            return client;
        }

        /// <summary>Fetch the archive, reporting progress and honouring a cancel, then check its SHA-256.
        /// Throws on a mismatch.</summary>
        public static void Fetch(string destination, Action<double> progress, Func<bool> shouldCancel)
        {
            if (FromDevCopy(destination)) return;
            using (var req = new HttpRequestMessage(HttpMethod.Get, Url))
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
                    if (shouldCancel != null && shouldCancel())
                        throw new OperationCanceledException("Download cancelled.");
                    dst.Write(buffer, 0, read);
                    done += read;
                    if (total.HasValue && total.Value > 0)
                        try { progress?.Invoke((double)done / total.Value); } catch { }
                }
            }

            var actual = Hash(SHA256.Create(), destination);
            if (!string.Equals(actual, Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("the downloaded " + FileName + " is not the expected file (SHA-256 " + actual + ")");
            Log.Info("downloaded " + FileName + ", SHA-256 checked");
        }

        /// <summary>THE DEVELOPER'S COPY: deploy-dev.ps1 -Plugin Demul puts the repository's archive in
        /// %LOCALAPPDATA%\lb-integrations-plugins\dev\, so a branch can be tried before its archive is online.
        /// Taken only when its SHA-256 is the pinned one - the very file the download would bring - and
        /// otherwise ignored, with a line in the log.</summary>
        private static bool FromDevCopy(string destination)
        {
            try
            {
                var dev = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                                       "lb-integrations-plugins", "dev", FileName);
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

        /// <summary>The version of the demul.exe at <paramref name="exe"/>: a known executable by its MD5,
        /// else what an install of ours stamped beside it, else null - an ordinary answer.</summary>
        public static string InstalledVersion(string exe)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(exe) || !File.Exists(exe)) return null;
                var known = KnownVersion(exe);
                if (known != null) return known;
                return Stamped(Path.GetDirectoryName(Path.GetFullPath(exe)));
            }
            catch (Exception ex) { Log.Warn("could not read the version of " + exe, ex); return null; }
        }

        private static string _hashedPath, _hashedVersion;
        private static DateTime _hashedWrite;

        /// <summary>By MD5, cached on the path and its write time: the host asks often, the file is 5 MB.</summary>
        private static string KnownVersion(string exe)
        {
            var full = Path.GetFullPath(exe);
            var write = File.GetLastWriteTimeUtc(full);
            lock (KnownExecutables)
            {
                if (string.Equals(full, _hashedPath, StringComparison.OrdinalIgnoreCase) && write == _hashedWrite)
                    return _hashedVersion;
                var md5 = Hash(MD5.Create(), full);
                KnownExecutables.TryGetValue(md5, out var version);
                _hashedPath = full; _hashedWrite = write; _hashedVersion = version;
                if (version == null) Log.Info("demul.exe " + md5 + " is not a build this plugin knows");
                return version;
            }
        }

        /// <summary>Did an install of ours put this Demul here?</summary>
        public static bool IsOurs(string exe)
        {
            try { return !string.IsNullOrWhiteSpace(exe) && Stamped(Path.GetDirectoryName(Path.GetFullPath(exe))) != null; }
            catch { return false; }
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

        /// <summary>Is <paramref name="candidate"/> later than <paramref name="installed"/>? Both are
        /// "0.7.yyyymmdd". Anything that does not parse is not an update.</summary>
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
