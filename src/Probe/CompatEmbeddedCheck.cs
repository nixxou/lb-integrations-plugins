// The compatibility lists embedded in the plugins (06/10), read when nothing was downloaded yet: --compat-embedded, for
// Nixx-Xemu, Nixx-Cxbx (with --also <Xemu.dll>: the copy of xemu's list is in Xemu.dll), Nixx-Xenia, Nixx-PPSSPP, Nixx-Vita3K.
// Every data folder points to an empty temporary one; nothing is downloaded.

using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;

namespace LbIntegrations.Probe
{
    internal static class CompatEmbeddedCheck
    {
        private const BindingFlags Flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
        private static int _bad;

        private static void Check(string what, bool ok, string detail = null)
        {
            if (!ok) _bad++;
            Console.WriteLine("  [" + (ok ? "ok" : "FAIL") + "] " + what + (detail != null ? " - " + detail : ""));
        }

        private static object Field(object o, string name)
            => o?.GetType().GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(o)
               ?? o?.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(o);

        public static bool Run(Assembly asm, string also)
        {
            _bad = 0;
            Console.WriteLine();
            Console.WriteLine("-- the compatibility lists embedded in " + asm.GetName().Name + " --");
            var empty = Path.Combine(Path.GetTempPath(), "lbip-compat-embedded-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(empty);
            try
            {
                if (also != null) Assembly.LoadFrom(also);

                if (asm.GetType("LbIntegrations.Xbox.XboxCompat") is Type xbox)
                {
                    xbox.GetField("DataOverride", Flags).SetValue(null, empty);
                    var r = xbox.GetMethod("Xemu", Flags).Invoke(null, new object[] { "5443000d" });
                    var title = r.GetType().GetField("Item2").GetValue(r);
                    var report = r.GetType().GetField("Item1").GetValue(r);
                    bool expected = asm.GetName().Name == "Xemu" || also != null;
                    Check("xemu's list, no download: Ninja Gaiden Black " + (expected ? "found" : "not found (no Xemu.dll loaded)"),
                          (title != null) == expected, title != null ? Field(title, "Name") + ", " + Field(title, "Status") + (report != null ? ", a report" : "") : null);
                    Check("HasXemuList agrees", (bool)xbox.GetMethod("HasXemuList", Flags).Invoke(null, null) == expected);
                }
                if (asm.GetType("LbIntegrations.Xenia.XeniaCompat") is Type xenia)
                {
                    xenia.GetField("DirOverride", Flags).SetValue(null, empty);
                    var list = (IList)xenia.GetMethod("Lookup", Flags).Invoke(null, new object[] { "45410898" });
                    Check("Xenia's list, no download: 45410898 found", list.Count > 0, list.Count > 0 ? Field(list[0], "Title") + ", " + Field(list[0], "State") : null);
                    Check("Downloaded() stays empty", xenia.GetMethod("Downloaded", Flags).Invoke(null, null) == null);
                }
                if (asm.GetType("LbIntegrations.Ppsspp.PpssppCompat") is Type psp)
                {
                    asm.GetType("LbIntegrations.Ppsspp.PpssppSettings", true).GetField("DirOverride", Flags).SetValue(null, empty);
                    var c = psp.GetMethod("Of", Flags).Invoke(null, new object[] { "ULUS10041", "1.05" });
                    Check("PPSSPP's list, no build: GTA Liberty City Stories found", c != null, c != null ? Field(c, "Title") + ", " + Field(c, "Rating") : null);
                    Check("nothing written in the data folder", !Directory.EnumerateFileSystemEntries(empty).Any());
                }
                if (asm.GetType("LbIntegrations.Vita3k.Vita3kCompat") is Type vita)
                {
                    var install = Path.Combine(empty, "Nixx-Vita3K");
                    Directory.CreateDirectory(Path.Combine(install, "portable"));
                    File.WriteAllText(Path.Combine(install, "Vita3K.exe"), "");
                    vita.GetField("NoNetwork", Flags)?.SetValue(null, true);
                    var layout = asm.GetType("LbIntegrations.Vita3k.Vita3kPaths", true).GetMethod("Resolve", Flags, null, new[] { typeof(string) }, null)
                                    .Invoke(null, new object[] { Path.Combine(install, "Vita3K.exe") });
                    int found = 0;
                    foreach (var id in new[] { "PCSE00965", "PCSA00017", "PCSF00021" })
                    {
                        var c = vita.GetMethod("Lookup", Flags).Invoke(null, new object[] { layout, id });
                        if (c != null) found++;
                        Console.WriteLine("    " + id + ": " + (c == null ? "not in the list" : Field(c, "State") + " - " + string.Join(", ", ((IEnumerable)Field(c, "Problems") ?? new string[0]).Cast<object>())));
                    }
                    Check("Vita3K's list, none in its cache: games found", found > 0, found + " of 3");
                    Check("the cache left empty", !File.Exists(Path.Combine(install, "portable", "cache", "app_compat_db.xml")));
                }
            }
            catch (Exception ex) { Check("ran", false, (ex.InnerException ?? ex).ToString()); }
            finally { try { Directory.Delete(empty, true); } catch { } }
            Console.WriteLine(_bad == 0 ? "  all good" : "  " + _bad + " FAILED");
            return _bad == 0;
        }
    }
}
