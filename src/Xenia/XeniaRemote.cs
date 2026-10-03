// Small files fetched from xenia-manager's data repositories (Mehdi, 03/10: its optimized settings and its patch index),
// kept beside the plugin's settings and asked for again once a day - their pattern, read in xenia-manager's
// DatabaseHttpClient (BSD-3): GitHub Pages first, raw GitHub when Pages fails, a copy kept a day.
//
// NEVER A LAUNCH WAITING ON THE NETWORK FOR LONG: a caller says how long it may wait - the game options window a few
// seconds in the background, a launch only a short one and only when there is no copy at all. A copy, however old,
// is what is used when the network does not answer; a file that comes back empty or as an HTML page is not kept.
//
// <plugin data>\remote\<name> (the file as it came) - its last write time is when it was fetched.

using System;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace LbIntegrations.Xenia
{
    internal static class XeniaRemote
    {
        internal static readonly TimeSpan MaxAge = TimeSpan.FromDays(1);

        private static readonly HttpClient Http = MakeClient();

        private static HttpClient MakeClient()
        {
            var c = new HttpClient(new HttpClientHandler { AllowAutoRedirect = true });
            c.DefaultRequestHeaders.UserAgent.ParseAdd("lb-integrations-plugins");
            return c;
        }

        private static string Dir => Path.Combine(XeniaSettings.Dir, "remote");

        public static string PathOf(string name) => Path.Combine(Dir, name);

        /// <summary>The copy of <paramref name="name"/>, or null when there is none.</summary>
        public static string Cached(string name)
        {
            try { var p = PathOf(name); return File.Exists(p) ? File.ReadAllText(p, Encoding.UTF8) : null; }
            catch { return null; }
        }

        public static bool IsFresh(string name)
        {
            try { var p = PathOf(name); return File.Exists(p) && DateTime.UtcNow - File.GetLastWriteTimeUtc(p) < MaxAge; }
            catch { return false; }
        }

        /// <summary>The file: the copy when it is fresh, else asked from each URL in turn within <paramref name="timeout"/> -
        /// the copy, however old, when none answers. Null when there is neither. Never throws.</summary>
        public static string Get(string name, string[] urls, TimeSpan timeout, Func<string, bool> looksRight = null)
        {
            if (IsFresh(name) || timeout <= TimeSpan.Zero) return Cached(name);     // no wait allowed: the copy, whatever its age
            return Fetch(name, urls, timeout, looksRight) ?? Cached(name);
        }

        /// <summary>Asked now, whatever the copy's age - what came back, kept, or null. Never throws.</summary>
        public static string Fetch(string name, string[] urls, TimeSpan timeout, Func<string, bool> looksRight = null)
        {
            using var cts = new CancellationTokenSource(timeout);
            foreach (var url in urls)
            {
                try
                {
                    using var resp = Http.GetAsync(url, cts.Token).GetAwaiter().GetResult();
                    if (!resp.IsSuccessStatusCode) { Log.Info("remote: " + name + " - HTTP " + (int)resp.StatusCode + " from " + url); continue; }
                    var text = resp.Content.ReadAsStringAsync(cts.Token).GetAwaiter().GetResult();
                    var t = text.TrimStart();
                    if (t.Length == 0 || t.StartsWith("<", StringComparison.Ordinal) || (looksRight != null && !looksRight(text)))
                    {
                        Log.Info("remote: " + name + " - what " + url + " sent does not read as the file; not kept");
                        continue;
                    }
                    Directory.CreateDirectory(Dir);
                    var path = PathOf(name);
                    var tmp = path + ".tmp";
                    File.WriteAllText(tmp, text, new UTF8Encoding(false));
                    File.Move(tmp, path, overwrite: true);
                    return text;
                }
                catch (Exception ex)
                {
                    Log.Info("remote: " + name + " - not fetched from " + url + " (" + ex.GetType().Name + ": " + ex.Message + ")");
                    if (cts.IsCancellationRequested) break;
                }
            }
            return null;
        }

        /// <summary>In the background, when the copy is older than a day or missing: never waited on.</summary>
        public static void RefreshSoon(string name, string[] urls, TimeSpan timeout, Func<string, bool> looksRight = null)
        {
            try { if (!IsFresh(name)) Task.Run(() => Fetch(name, urls, timeout, looksRight)); }
            catch { }
        }
    }
}
