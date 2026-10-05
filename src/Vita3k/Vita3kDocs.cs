// Documentation files fetched at the end of an install or an update of Vita3K, into
//
//     <emulator folder>\doc\
//
// THE LIST IS Urls, BELOW - filled by hand. Empty, nothing happens at all: no request, no folder.
// Not empty, the doc folder is made when it is not there, and every URL is fetched into it under the
// file name its URL ends with, replacing the previous copy - an update brings the current documents.
//
// A document is never a reason to fail an install: one that does not come (timeout, 404, no network)
// is logged and skipped, the others are still fetched, and the install's message says how many came.
// Each one has Timeout to arrive, whole. Written to a temporary name and moved into place, so a
// download cut short never leaves half a file where the previous one was.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;

namespace LbIntegrations.Vita3k
{
    internal static class Vita3kDocs
    {
        /// <summary>The folder, under the emulator's.</summary>
        public const string FolderName = "zrif";

        /// <summary>THE FILES TO FETCH - one URL each, fetched in this order. Leave it empty for none.
        /// (An array cannot be a C# const: static readonly is the nearest, fixed in the code like one.)</summary>
        public static readonly string[] Urls =
        {
            "uggcf://abcnlfgngvba.pbz/gfi/craqvat/CFI_TNZRF.gfi",
            "uggcf://abcnlfgngvba.pbz/gfi/craqvat/CFI_QYPF.gfi",
            "uggcf://abcnlfgngvba.pbz/gfi/craqvat/CFI_QRZBF.gfi"
        };

        /// <summary>How long one file has to arrive, whole, before it is given up.</summary>
        public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(60);

        private static readonly HttpClient Http = CreateClient();

        private static HttpClient CreateClient()
        {
            var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = true }) { Timeout = System.Threading.Timeout.InfiniteTimeSpan };
            client.DefaultRequestHeaders.UserAgent.ParseAdd(
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) " +
                "AppleWebKit/537.36 (KHTML, like Gecko) " +
                "Chrome/153.0.0.0 Safari/537.36");
            return client;
        }

        public static string Rot13(string input)
        {
            if (input == null)
                return null;

            return string.Create(input.Length, input, static (span, source) =>
            {
                for (int i = 0; i < source.Length; i++)
                {
                    char c = source[i];

                    span[i] = c switch
                    {
                        >= 'A' and <= 'M' => (char)(c + 13),
                        >= 'N' and <= 'Z' => (char)(c - 13),
                        >= 'a' and <= 'm' => (char)(c + 13),
                        >= 'n' and <= 'z' => (char)(c - 13),
                        _ => c
                    };
                }
            });
        }

        /// <summary>Fetch every document of <see cref="Urls"/> into &lt;<paramref name="emulatorDir"/>&gt;\doc.
        /// Returns what the install's message says about it - "" when the list is empty.</summary>
        public static string Fetch(string emulatorDir, Action<string, double?> report, Func<bool> cancelled)
            => Fetch(emulatorDir, Urls, Timeout, report, cancelled);

        /// <summary>The same, for a given list and timeout - what the probe tries it with.</summary>
        internal static string Fetch(string emulatorDir, IEnumerable<string> list, TimeSpan timeout,
                                     Action<string, double?> report, Func<bool> cancelled)
        {
            var urls = (list ?? new string[0]).Where(u => !string.IsNullOrWhiteSpace(u)).ToList();
            if (urls.Count == 0) return "";

            string folder;
            try
            {
                folder = Path.Combine(emulatorDir, FolderName);
                Directory.CreateDirectory(folder);
            }
            catch (Exception ex) { Log.Warn("documentation: could not make the doc folder", ex); return " The documentation could not be fetched."; }

            int done = 0;
            for (int i = 0; i < urls.Count; i++)
            {
                if (cancelled?.Invoke() == true) break;
                var url = Rot13(urls[i].Trim());
                //var url = urls[i].Trim();
                var name = FileNameOf(url, i);
                report?.Invoke("Downloading the documentation (" + (i + 1) + "/" + urls.Count + "): " + name + "...", i / (double)urls.Count);
                var target = Path.Combine(folder, name);
                var partial = target + ".part";
                try
                {
                    using var cts = new CancellationTokenSource(timeout);
                    using (var response = Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cts.Token).GetAwaiter().GetResult())
                    {
                        if (!response.IsSuccessStatusCode)
                        { Log.Warn("documentation: " + url + " answered " + (int)response.StatusCode + " " + response.ReasonPhrase + " - skipped"); continue; }
                        using var body = response.Content.ReadAsStreamAsync(cts.Token).GetAwaiter().GetResult();
                        using var file = File.Create(partial);
                        body.CopyToAsync(file, cts.Token).GetAwaiter().GetResult();
                    }
                    File.Move(partial, target, overwrite: true);
                    done++;
                    Log.Info("documentation: " + name + " fetched from " + url);
                }
                catch (OperationCanceledException) { Log.Warn("documentation: " + url + " did not arrive within " + (int)timeout.TotalSeconds + " s - skipped"); }
                catch (Exception ex) { Log.Warn("documentation: " + url + " could not be fetched - skipped", ex); }
                finally { try { if (File.Exists(partial)) File.Delete(partial); } catch { } }
            }
            return done == urls.Count
                ? " The documentation is in " + FolderName + "\\ (" + done + " file(s))."
                : " Documentation: " + done + " of " + urls.Count + " file(s) fetched into " + FolderName + "\\ - see the log for the others.";
        }

        /// <summary>The file name a URL ends with, query and fragment left out, made safe for Windows -
        /// or document-N when it ends with none.</summary>
        internal static string FileNameOf(string url, int index)
        {
            string name = null;
            try { name = Uri.UnescapeDataString(Path.GetFileName(new Uri(url).AbsolutePath)); } catch { }
            if (string.IsNullOrWhiteSpace(name)) name = "document-" + (index + 1);
            foreach (var c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
            return name;
        }
    }
}
