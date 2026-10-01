// --xenia-compat: Xenia's compatibility list (src\Xenia\XeniaCompat.cs), offline - one page of GitHub's answer as
// measured on 01/10, the local file read back, a title id with two issues. Nothing is fetched; nothing is written
// but in the temp folder.

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

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

        // Shaped as GitHub answers (01/10): an open game, a closed duplicate, a pull request, a title without an id,
        // one with a 0x prefix, one with no state label.
        private const string Page = @"[
 {""number"": 370, ""title"": ""45410806 - Burnout Paradise"", ""state"": ""open"", ""labels"": [{""name"": ""state-playable""}, {""name"": ""gpu-readback""}]},
 {""number"": 556, ""title"": ""45410806 - Burnout Paradise (Jan 30, 2008 prototype)"", ""state"": ""open"", ""labels"": [{""name"": ""state-intro""}]},
 {""number"": 1196, ""title"": ""4D5307DF - Blue Dragon"", ""state"": ""closed"", ""labels"": [{""name"": ""issue-invalid""}]},
 {""number"": 1300, ""title"": ""Some pull request"", ""state"": ""open"", ""labels"": [], ""pull_request"": {}},
 {""number"": 986, ""title"": ""???????? - Minecraft"", ""state"": ""open"", ""labels"": [{""name"": ""state-gameplay""}]},
 {""number"": 1060, ""title"": ""0x415607E1- Call of Duty 3"", ""state"": ""open"", ""labels"": [{""name"": ""state-gameplay""}]},
 {""number"": 1169, ""title"": ""00000000 - Aurora Dashboard"", ""state"": ""open"", ""labels"": []}
]";

        public static bool Run(Assembly asm)
        {
            _bad = 0;
            Console.WriteLine();
            Console.WriteLine("-- Xenia's compatibility list, offline --");
            var t = asm.GetType("LbIntegrations.Xenia.XeniaCompat", true);
            var flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
            var tmp = Path.Combine(Path.GetTempPath(), "lbip-xenia-compat-" + Guid.NewGuid().ToString("N") + ".tsv");
            t.GetField("PathOverride", flags).SetValue(null, tmp);
            try
            {
                var closed = new List<int>();
                var open = ((IEnumerable)t.GetMethod("ParsePage", flags).Invoke(null, new object[] { Page, closed })).Cast<object>().ToList();
                string F(object e, string name) => Convert.ToString(e.GetType().GetField(name).GetValue(e));
                Check("open games with a title id: four (the pull request and the ???????? title left out)", open.Count == 4,
                      string.Join(" | ", open.Select(e => F(e, "TitleId") + " #" + F(e, "Issue"))));
                Check("a closed issue and an id-less title are counted as gone", closed.Contains(1196) && closed.Contains(986));
                var burnout = open.First(e => F(e, "Issue") == "370");
                Check("the state from its label, the others as problems", F(burnout, "State") == "playable"
                      && ((IEnumerable)burnout.GetType().GetField("Labels").GetValue(burnout)).Cast<string>().SequenceEqual(new[] { "gpu-readback" }));
                Check("a 0x prefix read through", open.Any(e => F(e, "TitleId") == "415607E1"));
                Check("no state label: no state", F(open.First(e => F(e, "Issue") == "1169"), "State") == "");

                // The local file, as a build writes it, read back.
                var dictType = typeof(Dictionary<,>).MakeGenericType(typeof(int), asm.GetType("LbIntegrations.Xenia.XeniaCompatEntry", true));
                var all = (IDictionary)Activator.CreateInstance(dictType);
                foreach (var e in open) all[int.Parse(F(e, "Issue"))] = e;
                t.GetMethod("Save", flags).Invoke(null, new object[] { all, DateTime.UtcNow });
                var found = ((IEnumerable)t.GetMethod("Lookup", flags).Invoke(null, new object[] { "45410806" })).Cast<object>().ToList();
                Check("read back: both issues of 45410806, the better state first", found.Count == 2 && F(found[0], "State") == "playable" && F(found[1], "State") == "intro",
                      string.Join(" | ", found.Select(e => F(e, "State") + " #" + F(e, "Issue"))));
                Check("lower case asked, found all the same", ((IEnumerable)t.GetMethod("Lookup", flags).Invoke(null, new object[] { "45410806".ToLowerInvariant() })).Cast<object>().Count() == 2);
                var said = (string)t.GetMethod("Describe", flags).Invoke(null, new object[] { "45410806" });
                Console.WriteLine("      " + said);
                Check("the log line names state, issue and problems", said.StartsWith("playable (#370, gpu-readback)"));
                Check("an unknown game: said so", (string)t.GetMethod("Describe", flags).Invoke(null, new object[] { "FFFFFFFF" }) == "not in Xenia's compatibility list");
                Check("the list's date is kept", t.GetMethod("Updated", flags).Invoke(null, null) != null);
            }
            catch (Exception ex) { Console.WriteLine("  EXCEPTION: " + (ex.InnerException ?? ex)); _bad++; }
            finally
            {
                t.GetField("PathOverride", flags).SetValue(null, null);
                try { File.Delete(tmp); } catch { }
            }
            Console.WriteLine(_bad == 0 ? "\n  OK - the compatibility list reads as measured" : "\n  " + _bad + " FAILED");
            return _bad == 0;
        }
    }
}
