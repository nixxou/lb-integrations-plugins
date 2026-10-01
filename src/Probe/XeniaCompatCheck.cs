// --xenia-compat [--online]: Xenia's compatibility list (src\Xenia\XeniaCompat.cs) - its file read, the checks a
// fetched file must pass before it replaces the copy, and with --online the real file fetched twice (the second time
// must be "unchanged", a 304). Writes only in the temp folder. GitHub's API limit is not touched: the file is a
// release asset.

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;

namespace LbIntegrations.Probe
{
    internal static class XeniaCompatCheck
    {
        private static int _bad;

        private static bool Check(string what, bool ok, string detail = null)
        {
            Console.WriteLine("    " + (ok ? "ok   " : "FAIL ") + what + (!ok && detail != null ? "\n          got: " + detail : ""));
            if (!ok) _bad++;
            return ok;
        }

        /// <summary>One game as compatibility_data.json writes it (01/10).</summary>
        private static string Game(int issue, string id, string state, string label, params string[] others)
            => "{\"issue\": " + issue + ", \"id\": \"" + id + "\", \"title\": \"Game " + issue + "\", \"updated\": \"2026-09-30T00:00:00Z\", \"state\": \"" + state
               + "\", \"labels\": {\"state\": [\"state-" + label + "\"], \"others\": [" + string.Join(", ", others.Select(o => "\"" + o + "\"")) + "]}, "
               + "\"url\": \"https://github.com/xenia-canary/game-compatibility/issues/" + issue + "\"}";

        private static string List(IEnumerable<string> games) => "[\n" + string.Join(",\n", games) + "\n]";

        public static bool Run(Assembly asm, bool online)
        {
            _bad = 0;
            Console.WriteLine();
            Console.WriteLine("-- Xenia's compatibility list --");
            var t = asm.GetType("LbIntegrations.Xenia.XeniaCompat", true);
            var flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
            var dir = Path.Combine(Path.GetTempPath(), "lbip-xenia-compat-" + Guid.NewGuid().ToString("N"));
            t.GetField("DirOverride", flags).SetValue(null, dir);
            string Problem(string json, int previous) => (string)t.GetMethod("Problem", flags).Invoke(null, new object[] { json, previous });
            try
            {
                // 120 games: two of one title id, the rest filler.
                var games = new List<string>
                {
                    Game(370, "45410806", "Playable", "playable", "gpu-readback"),
                    Game(556, "45410806", "Loads", "intro"),
                };
                for (int i = 0; i < 118; i++) games.Add(Game(2000 + i, (0x41000000 + i).ToString("X8"), "Gameplay", "gameplay"));
                var good = List(games);

                Check("a list as published reads", Problem(good, 0) == null, Problem(good, 0));
                Check("broken JSON: refused", Problem(good.Substring(0, good.Length / 2), 0)?.StartsWith("it does not read") == true);
                Check("an object rather than a list: refused", Problem("{\"message\": \"Not Found\"}", 0) != null);
                Check("an HTML page: refused", Problem("<html><body>502 Bad Gateway</body></html>", 0) != null);
                Check("fewer than a hundred games: refused", Problem(List(games.Take(50)), 0)?.Contains("a hundred") == true);
                Check("under half the copy's count: refused", Problem(good, 1000)?.Contains("against 1000") == true);
                var holes = List(games.Take(60).Concat(Enumerable.Range(0, 60).Select(i => "{\"issue\": 0, \"id\": \"" + (0x42000000 + i).ToString("X8") + "\"}")));
                Check("games without an issue or a state, more than one in twenty: refused", Problem(holes, 0)?.Contains("have an id, an issue and a state") == true);

                // The copy, read: the two games of 45410806, the better state first.
                Directory.CreateDirectory(dir);
                File.WriteAllText(Path.Combine(dir, "xenia-compat.json"), good, new UTF8Encoding(false));
                var found = ((IEnumerable)t.GetMethod("Lookup", flags).Invoke(null, new object[] { "45410806" })).Cast<object>().ToList();
                string F(object e, string name) => Convert.ToString(e.GetType().GetField(name).GetValue(e));
                Check("both games of one title id, Playable before Loads", found.Count == 2 && F(found[0], "State") == "Playable" && F(found[1], "Detail") == "intro",
                      string.Join(" | ", found.Select(e => F(e, "State") + " #" + F(e, "Issue"))));
                var said = (string)t.GetMethod("Describe", flags).Invoke(null, new object[] { "45410806" });
                Console.WriteLine("      " + said);
                Check("the log line: state, detail, issue, problems", said.StartsWith("Playable (playable, #370, gpu-readback)"));
                Check("an unknown game: said so", (string)t.GetMethod("Describe", flags).Invoke(null, new object[] { "FFFFFFFF" }) == "not in Xenia's compatibility list");

                if (online)
                {
                    Console.WriteLine("  online: " + (string)t.GetField("Source", flags).GetValue(null));
                    Directory.Delete(dir, true);
                    var fetch = t.GetMethod("Fetch", flags);
                    Check("fetched for real, and it passed the checks", (bool)fetch.Invoke(null, new object[] { TimeSpan.FromSeconds(30) })
                          && File.Exists(Path.Combine(dir, "xenia-compat.json")));
                    var stamp = File.GetLastWriteTimeUtc(Path.Combine(dir, "xenia-compat.json"));
                    Check("asked again: unchanged, the copy not rewritten", (bool)fetch.Invoke(null, new object[] { TimeSpan.FromSeconds(30) })
                          && File.GetLastWriteTimeUtc(Path.Combine(dir, "xenia-compat.json")) == stamp);
                    var halo = (string)t.GetMethod("Describe", flags).Invoke(null, new object[] { "4D5307E6" });
                    Console.WriteLine("      Halo 3: " + halo);
                    Check("a known game found in the real list", !halo.StartsWith("not in") && !halo.StartsWith("no compat"));
                }
            }
            catch (Exception ex) { Console.WriteLine("  EXCEPTION: " + (ex.InnerException ?? ex)); _bad++; }
            finally
            {
                t.GetField("DirOverride", flags).SetValue(null, null);
                try { Directory.Delete(dir, true); } catch { }
            }
            Console.WriteLine(_bad == 0 ? "\n  OK - the compatibility list reads as measured, and a bad file is never kept" : "\n  " + _bad + " FAILED");
            return _bad == 0;
        }
    }
}
