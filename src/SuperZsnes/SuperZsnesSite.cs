// Getting SUPER ZSNES, which does not come from GitHub.
//
// THE DOWNLOAD IS A LINK ON THE HOME PAGE. https://www.zsnes.com/ carries a "Downloads" section
// with one card per platform, and the Windows one reads, verbatim from the page as served on
// 2026-09-30:
//
//     <div class="download-card">
//       <h3>Windows</h3>
//       <a href="files/SuperZSNES_v0.310.zip">Download</a>
//     </div>
//
// followed by the Mac (.dmg) and Linux (.tar.gz) cards in the same shape, then app-store links for
// Android and iOS. The version is in the file name and nowhere else on that card. So the plugin
// reads the page, takes the anchor under the Windows heading, and resolves it against the page's
// own address - a relative href today, and an absolute one tomorrow would resolve the same.
//
// THE EMULATOR HAS A VERSION FEED OF ITS OWN, and it is the cross-check. Its build carries the
// literal https://zsnes.com/version.txt (read out of global-metadata.dat, beside the
// retroachievements.org address it also talks to), and the emulator's "A new version of SUPER ZSNES
// is out! You'll need to download it over at www.zsnes.com!" dialog is what that feed drives. It
// answers three lines of text:
//
//     Windows,0.310
//     Linux,0.310
//     Mac,0.310
//
// It is what a machine is meant to read, so when the page cannot be parsed - a redesign, a card
// renamed - the version from here plus the file-name pattern still gives a download. And when both
// answer and disagree, the log says so: that is the day the pattern changed.
//
// AND THERE IS NO VERSION IN THE EXECUTABLE. SUPERZSNES.exe's version resource is Unity's own
// (6000.3.6, the engine), the product and company fields are empty, and the build is IL2CPP so
// there is no managed assembly to ask. What exists is the About box's text, "v0.310b", serialised
// into SUPERZSNES_Data\level0 - SuperZsnesPaths reads it from there. A stamp is written at install
// as well, for the day that text moves to another scene file.

using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;

namespace LbIntegrations.SuperZsnes
{
    /// <summary>One downloadable build, as the site describes it.</summary>
    internal sealed class SuperZsnesBuild
    {
        /// <summary>"0.310" - digits only, what the file name carries.</summary>
        public string Version;
        /// <summary>Absolute, ready to fetch.</summary>
        public string Url;
        /// <summary>"SuperZSNES_v0.310.zip"</summary>
        public string FileName;
        /// <summary>Where the answer came from, for the log.</summary>
        public string Source;
    }

    internal static class SuperZsnesSite
    {
        public const string Home = "https://www.zsnes.com/";
        public const string VersionFeed = "https://www.zsnes.com/version.txt";

        /// <summary>The Windows download, when the page has to be reconstructed from the version
        /// feed alone. Measured against two releases: SuperZSNES_v0.230.zip and SuperZSNES_v0.310.zip
        /// both live under files\ with this exact shape.</summary>
        private const string FileTemplate = "files/SuperZSNES_v{0}.zip";

        /// <summary>Written beside the executable at install time. The About text in level0 is the
        /// first source of the installed version; this is the second, for a build whose scene layout
        /// changes.</summary>
        public const string StampName = "lbip-superzsnes-build.txt";

        private static readonly HttpClient Http = MakeClient();

        private static HttpClient MakeClient()
        {
            var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = true })
            {
                Timeout = TimeSpan.FromSeconds(30),
            };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("lb-integrations-plugins/1.0");
            return client;
        }

        /// <summary>What the site offers for Windows right now, or null when it cannot be reached or
        /// read. Never throws. Null is a reason to say nothing, never a reason to claim an update.</summary>
        public static SuperZsnesBuild Latest()
        {
            SuperZsnesBuild fromPage = null;
            string feedVersion = null;

            var page = TryGet(Home);
            if (page != null)
            {
                fromPage = ParsePage(page, Home);
                if (fromPage == null) Log.Warn("the home page no longer carries a Windows download the way this plugin reads it");
            }

            var feed = TryGet(VersionFeed);
            if (feed != null)
            {
                feedVersion = ParseVersionFeed(feed);
                if (feedVersion == null) Log.Warn("version.txt has no Windows line: " + Squash(feed));
            }

            if (fromPage != null)
            {
                if (fromPage.Version == null && feedVersion != null)
                {
                    // The link is there but its name no longer carries a number: the feed's is the
                    // only version we have, and a download without one would nag forever.
                    fromPage.Version = feedVersion;
                    fromPage.Source += ", version from version.txt";
                }
                else if (feedVersion != null && fromPage.Version != null
                    && !string.Equals(feedVersion, fromPage.Version, StringComparison.Ordinal))
                    Log.Warn("the page offers " + fromPage.Version + " and version.txt says " + feedVersion
                             + " - taking the page, since that is the file a person would get");
                Log.Verbose("latest: " + fromPage.Version + " from " + fromPage.Source);
                return fromPage;
            }

            if (feedVersion != null)
            {
                var url = Home + string.Format(FileTemplate, feedVersion);
                Log.Info("home page unreadable, reconstructing the download from version.txt: " + url);
                return new SuperZsnesBuild
                {
                    Version = feedVersion,
                    Url = url,
                    FileName = Path.GetFileName(new Uri(url).AbsolutePath),
                    Source = "version.txt and the file-name pattern",
                };
            }

            return null;
        }

        /// <summary>The Windows card's anchor, resolved against <paramref name="baseUrl"/>. Null when
        /// the page carries nothing recognisable.</summary>
        internal static SuperZsnesBuild ParsePage(string html, string baseUrl)
        {
            if (string.IsNullOrEmpty(html)) return null;

            // The heading and the anchor are siblings inside one card. Whitespace between them is
            // whatever the author's editor left, so it is not assumed.
            var m = Regex.Match(html,
                @"<h3[^>]*>\s*Windows\s*</h3>\s*<a\s[^>]*href\s*=\s*""(?<href>[^""]+)""",
                RegexOptions.IgnoreCase | RegexOptions.Singleline);
            string source = "the Windows card on zsnes.com";

            if (!m.Success)
            {
                // No heading, or a renamed one: any anchor to a SuperZSNES zip is still the Windows
                // build, since the Mac and Linux ones are a .dmg and a .tar.gz.
                m = Regex.Match(html, @"href\s*=\s*""(?<href>[^""]*SuperZSNES_v[0-9][^""]*\.zip)""",
                                RegexOptions.IgnoreCase);
                source = "a SuperZSNES zip link on zsnes.com (no Windows heading found)";
                if (!m.Success) return null;
            }

            var href = WebUtility.HtmlDecode(m.Groups["href"].Value.Trim());
            Uri absolute;
            try { absolute = new Uri(new Uri(baseUrl), href); }
            catch { return null; }

            var file = Path.GetFileName(absolute.AbsolutePath);
            var v = Regex.Match(file, @"_v(?<v>\d+(?:\.\d+)+)", RegexOptions.IgnoreCase);
            return new SuperZsnesBuild
            {
                Version = v.Success ? v.Groups["v"].Value : null,
                Url = absolute.ToString(),
                FileName = Uri.UnescapeDataString(file),
                Source = source,
            };
        }

        /// <summary>"0.310" from the "Windows,0.310" line, or null.</summary>
        internal static string ParseVersionFeed(string text)
        {
            if (string.IsNullOrEmpty(text)) return null;
            var m = Regex.Match(text, @"^\s*Windows\s*,\s*(?<v>\d+(?:\.\d+)+)\s*$",
                                RegexOptions.IgnoreCase | RegexOptions.Multiline);
            return m.Success ? m.Groups["v"].Value : null;
        }

        /// <summary>Fetch it, reporting progress and honouring a cancel. Same shape as
        /// GitHubReleases.Download in the other plugins, because the caller is the same shape.</summary>
        public static void Fetch(string url, string destination, Action<double> progress, Func<bool> shouldCancel)
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
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

        /// <summary>Write down what was installed: the version, then the file it came from.</summary>
        public static void Stamp(string installDir, SuperZsnesBuild build)
        {
            try
            {
                if (installDir == null || build == null || build.Version == null) return;
                File.WriteAllText(Path.Combine(installDir, StampName),
                    build.Version + Environment.NewLine + (build.FileName ?? "") + Environment.NewLine);
            }
            catch (Exception ex) { Log.Verbose("could not write the build stamp - " + ex.Message); }
        }

        /// <summary>What Stamp wrote, or null for an installation this plugin did not make.</summary>
        public static string Stamped(string installDir)
        {
            try
            {
                if (installDir == null) return null;
                var path = Path.Combine(installDir, StampName);
                if (!File.Exists(path)) return null;
                var lines = File.ReadAllLines(path);
                return lines.Length > 0 && !string.IsNullOrWhiteSpace(lines[0]) ? lines[0].Trim() : null;
            }
            catch { return null; }
        }

        private static string TryGet(string url)
        {
            try
            {
                using var resp = Http.GetAsync(url).GetAwaiter().GetResult();
                if (!resp.IsSuccessStatusCode)
                {
                    Log.Verbose(url + " answered " + (int)resp.StatusCode);
                    return null;
                }
                return resp.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                Log.Verbose("could not read " + url + " - " + ex.Message);
                return null;
            }
        }

        private static string Squash(string text)
        {
            var s = (text ?? "").Replace("\r", " ").Replace("\n", " ").Trim();
            return s.Length > 80 ? s.Substring(0, 80) + "..." : s;
        }
    }
}
