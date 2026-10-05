// --list-refresh <plugin.dll>: a plugin's whole compatibility list downloaded again as its game window's button does
// (Shared.Lbip\LbipListRefresh) - for real, into a scratch folder: cancelled after a moment, the copy left as it was; then
// to its end, the list kept and its date known. Cxbx-Reloaded, PPSSPP (their pages counted), Xenia, xemu.

using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace LbIntegrations.Probe
{
    internal static class ListRefreshCheck
    {
        private const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
        private static int _bad;

        private static void Check(string what, bool ok, string detail = null)
        {
            Console.WriteLine("    " + (ok ? "ok  " : "FAIL") + " " + what + (ok || detail == null ? "" : " - " + detail));
            if (!ok) _bad++;
        }

        public static bool Run(Assembly asm)
        {
            var jobType = asm.GetType("LbIntegrations.Lbip.LbipListJob", true);
            var scratch = Path.Combine(Path.GetTempPath(), "lbip-list-refresh-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(scratch);
            void Override(string type, string field, object value) => asm.GetType(type, true).GetField(field, Any).SetValue(null, value);
            object Call(string type, string method, params object[] a)
            {
                try { return asm.GetType(type, true).GetMethod(method, Any).Invoke(null, a); }
                catch (TargetInvocationException ex) { throw ex.InnerException ?? ex; }
            }

            // What the button runs, and where the copy is: by plugin.
            Func<object, string> run; Func<DateTime?> when; string copy; bool pages;
            if (asm.GetType("LbIntegrations.Cxbx.CxbxCompat") != null && asm.GetType("LbIntegrations.Cxbx.CxbxPlugin") != null)
            {
                Override("LbIntegrations.Cxbx.CxbxSettings", "DirOverride", scratch);
                run = j => (string)Call("LbIntegrations.Cxbx.CxbxCompat", "RefreshWhole", j);
                when = () => (DateTime?)Call("LbIntegrations.Cxbx.CxbxCompat", "Downloaded");
                copy = Path.Combine(scratch, "cxbx-compat.json"); pages = true;
            }
            else if (asm.GetType("LbIntegrations.Ppsspp.PpssppCompat") != null)
            {
                Override("LbIntegrations.Ppsspp.PpssppSettings", "DirOverride", scratch);
                var exe = Environment.ProcessPath;                    // a file that is there: the list is written for it
                run = j => (string)Call("LbIntegrations.Ppsspp.PpssppCompat", "RebuildWhole", exe, j);
                when = () => (DateTime?)Call("LbIntegrations.Ppsspp.PpssppCompat", "Downloaded");
                copy = Path.Combine(scratch, "ppsspp-compat.tsv"); pages = true;
            }
            else if (asm.GetType("LbIntegrations.Xenia.XeniaCompat") != null)
            {
                Override("LbIntegrations.Xenia.XeniaCompat", "DirOverride", scratch);
                run = j => (string)Call("LbIntegrations.Xenia.XeniaCompat", "FetchWhole", j);
                when = () => (DateTime?)Call("LbIntegrations.Xenia.XeniaCompat", "Downloaded");
                copy = Path.Combine(scratch, "xenia-compat.json"); pages = false;
            }
            else if (asm.GetType("LbIntegrations.Xemu.XemuCompat") != null)
            {
                Override("LbIntegrations.Xemu.XemuSettings", "DirOverride", scratch);
                run = j => (string)Call("LbIntegrations.Xemu.XemuCompat", "FetchWhole", j);
                when = () => (DateTime?)Call("LbIntegrations.Xemu.XemuCompat", "Downloaded");
                copy = Path.Combine(scratch, "xemu-compat-reports.json"); pages = false;
            }
            else { Console.WriteLine("  no whole list in this plugin"); return false; }

            object Job(CancellationToken t)
            {
                var j = Activator.CreateInstance(jobType);
                jobType.GetField("Token").SetValue(j, t);
                return j;
            }
            int Done(object j) => (int)jobType.GetField("Done").GetValue(j);
            string Step(object j) => (string)jobType.GetField("Step").GetValue(j);

            try
            {
                Check("no copy yet, no date", when() == null && !File.Exists(copy));

                // Cancelled: after two pages for a list of pages, else at once.
                using (var cts = new CancellationTokenSource())
                {
                    var job = Job(cts.Token);
                    var task = Task.Run(() => run(job));
                    var start = DateTime.UtcNow;
                    while (!task.IsCompleted && (DateTime.UtcNow - start).TotalSeconds < 60)
                    {
                        if (!pages || Done(job) >= 2) { cts.Cancel(); break; }
                        Thread.Sleep(100);
                    }
                    var said = task.GetAwaiter().GetResult();
                    Console.WriteLine("  cancelled at: " + Step(job) + " -> " + (said ?? "kept"));
                    Check("cancelled: said so, nothing written, no date", said == "cancelled" && !File.Exists(copy) && when() == null, said);
                }

                // To its end.
                using (var cts = new CancellationTokenSource())
                {
                    var job = Job(cts.Token);
                    var w = System.Diagnostics.Stopwatch.StartNew();
                    var task = Task.Run(() => run(job));
                    string last = null;
                    while (!task.IsCompleted) { var s = Step(job); if (s != last && pages && Done(job) % 10 == 0) Console.WriteLine("    " + s); last = s; Thread.Sleep(200); }
                    var said = task.GetAwaiter().GetResult();
                    Console.WriteLine("  whole: " + (said ?? "kept") + " in " + w.Elapsed.TotalSeconds.ToString("0") + " s, " + (File.Exists(copy) ? (new FileInfo(copy).Length >> 10) + " KB" : "no file"));
                    Check("downloaded whole: kept, and its date known", said == null && File.Exists(copy) && when() is DateTime d && (DateTime.Now - d).TotalMinutes < 5, said);
                }
            }
            finally { try { Directory.Delete(scratch, true); } catch { } }
            Console.WriteLine(_bad == 0 ? "  all good" : "  " + _bad + " FAILED");
            return _bad == 0;
        }
    }
}
