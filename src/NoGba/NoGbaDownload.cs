// Getting no$gba, which does not come from GitHub and has no version anywhere.
//
// THE OTHER FOUR PLUGINS ASK A RELEASES API. no$gba is a single zip on its author's own site, at a
// URL that has not changed in years:
//
//     https://problemkaputt.de/no$gba-w.zip     the gaming build, ~210 KB
//
// It holds three entries - NO$GBA.EXE, README.TXT, and DSI-SD.ZIP (a blank DSi SD card image) - and
// the README's installation note is "unzip into a new or blank folder". There is no installer, no
// registry, and the emulator creates nothing outside its own folder. Freeware, name your price; the
// README says in so many words that a player already has everything for free.
//
// AND THERE IS NO VERSION TO READ. The executable's version resource says, literally,
// FileVersion = "Windows version". The site's page carries "3.06" in prose and the configuration
// file no$gba writes says "no$gba 3.0" - neither is on disk after an install, and parsing a hand-
// written HTML page for a number would break the first time its author reformats a paragraph.
//
// SO THE SERVER'S OWN ANSWER IS THE VERSION. A HEAD request returns:
//
//     Last-Modified: Mon, 14 Apr 2025 10:59:11 GMT
//     Content-Length: 214818
//     ETag: "34722-632baf12d09b9"
//
// which identifies a build exactly, needs no parsing, and cannot drift out of step with the file it
// describes. The date is what is shown to the user; the ETag is what an update check compares.

using System;
using System.IO;
using System.Net.Http;
using LbIntegrations.Dsi;

namespace LbIntegrations.NoGba
{
    /// <summary>What the server says about the download, without fetching it.</summary>
    internal sealed class NoGbaBuild
    {
        /// <summary>"2025-04-14", from Last-Modified. What a person sees.</summary>
        public string Label;
        /// <summary>The server's ETag, or the Last-Modified string when there is none. What an
        /// update check compares, and what an install writes down.</summary>
        public string Tag;
        public long Bytes;
    }

    internal static class NoGbaDownload
    {
        /// <summary>The Windows gaming build. The DOS one and the debug one are separate downloads
        /// and neither is what a LaunchBox user wants.</summary>
        public const string Url = "https://problemkaputt.de/no$gba-w.zip";

        /// <summary>Written beside the executable at install time, because nothing else records
        /// which build is on disk.</summary>
        public const string StampName = "lbip-nogba-build.txt";

        private static readonly HttpClient Http = MakeClient();

        private static HttpClient MakeClient()
        {
            var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("lb-integrations-plugins");
            return client;
        }

        /// <summary>Ask the server what it is offering. Null when it cannot be reached - which is a
        /// reason to say nothing, never a reason to claim an update.</summary>
        public static NoGbaBuild Latest()
        {
            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Head, Url);
                using var resp = Http.SendAsync(req).GetAwaiter().GetResult();
                if (!resp.IsSuccessStatusCode)
                {
                    Log.Verbose("problemkaputt.de answered " + (int)resp.StatusCode);
                    return null;
                }

                var modified = resp.Content?.Headers?.LastModified;
                var etag = resp.Headers?.ETag?.Tag;
                var length = resp.Content?.Headers?.ContentLength ?? 0;

                return new NoGbaBuild
                {
                    Label = modified.HasValue ? modified.Value.UtcDateTime.ToString("yyyy-MM-dd") : "latest",
                    Tag = !string.IsNullOrEmpty(etag) ? etag
                        : modified.HasValue ? modified.Value.UtcDateTime.ToString("O") : "unknown",
                    Bytes = length,
                };
            }
            catch (Exception ex)
            {
                Log.Verbose("could not ask problemkaputt.de for the current build - " + ex.Message);
                return null;
            }
        }

        /// <summary>Fetch it, reporting progress and honouring a cancel. Same shape as
        /// GitHubReleases.Download in the other plugins, because the caller is the same shape.</summary>
        public static void Fetch(string destination, Action<double> progress, Func<bool> shouldCancel)
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, Url);
            using var resp = Http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead)
                                 .GetAwaiter().GetResult();
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

        /// <summary>Write down what was installed. Two lines, readable, because the only alternative
        /// source of truth would be a hash table of every build ever shipped.</summary>
        public static void Stamp(string installDir, NoGbaBuild build)
        {
            try
            {
                if (installDir == null || build == null) return;
                var text = build.Label + Environment.NewLine + build.Tag + Environment.NewLine;
                File.WriteAllText(Path.Combine(installDir, StampName), text);
            }
            catch (Exception ex) { Log.Verbose("could not write the build stamp - " + ex.Message); }
        }

        /// <summary>What Stamp wrote, or null for an installation this plugin did not make.</summary>
        public static NoGbaBuild Installed(string installDir)
        {
            try
            {
                if (installDir == null) return null;
                var path = Path.Combine(installDir, StampName);
                if (!File.Exists(path)) return null;

                var lines = File.ReadAllLines(path);
                if (lines.Length == 0 || string.IsNullOrWhiteSpace(lines[0])) return null;
                return new NoGbaBuild
                {
                    Label = lines[0].Trim(),
                    Tag = lines.Length > 1 ? lines[1].Trim() : lines[0].Trim(),
                };
            }
            catch { return null; }
        }
    }
}
