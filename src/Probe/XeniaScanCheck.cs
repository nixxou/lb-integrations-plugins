// --xenia-scan [--iso <a real Xbox 360 disc image>]: the sorting of a folder's Xbox 360 files (src\Xenia\XeniaScan.cs) on
// a folder made here - a title update named as XboxUnity names them, a DLC and an Arcade game with no extension, a Games
// on Demand package with and without its .data folder, an extracted disc, a .zar, files that only look like ours - then
// the cache: a second look reads nothing, a changed file is read again, a removed one goes. Writes only in the temp folder.

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;

namespace LbIntegrations.Probe
{
    internal static class XeniaScanCheck
    {
        private static int _bad;

        private static bool Check(string what, bool ok, string detail = null)
        {
            Console.WriteLine("    " + (ok ? "ok   " : "FAIL ") + what + (!ok && detail != null ? "\n          got: " + detail : ""));
            if (!ok) _bad++;
            return ok;
        }

        private static void Be(byte[] b, int at, uint v) { b[at] = (byte)(v >> 24); b[at + 1] = (byte)(v >> 16); b[at + 2] = (byte)(v >> 8); b[at + 3] = (byte)v; }

        /// <summary>An STFS header as the console writes one: magic, type, execution info, volume type, display name.</summary>
        private static byte[] Package(string magic, uint type, uint titleId, uint version = 0, uint volume = 0, string name = "")
        {
            var b = new byte[0x2000];
            Encoding.ASCII.GetBytes(magic).CopyTo(b, 0);
            Be(b, 0x344, type);
            Be(b, 0x354, 0x1234ABCD);
            Be(b, 0x358, version);
            Be(b, 0x360, titleId);
            Be(b, 0x3A9, volume);
            Encoding.BigEndianUnicode.GetBytes(name).CopyTo(b, 0x411);
            return b;
        }

        /// <summary>The smallest XEX2 the reader takes: one optional header, execution info, the title id at +0x0C.</summary>
        private static byte[] Xex(uint titleId)
        {
            var b = new byte[0x40];
            Encoding.ASCII.GetBytes("XEX2").CopyTo(b, 0);
            Be(b, 0x14, 1);
            Be(b, 0x18, 0x00040006);
            Be(b, 0x1C, 0x20);
            Be(b, 0x20 + 0x0C, titleId);
            return b;
        }

        public static bool Run(Assembly asm, string iso)
        {
            _bad = 0;
            var flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
            var scan = asm.GetType("LbIntegrations.Xenia.XeniaScan", true);
            var dir = Path.Combine(Path.GetTempPath(), "lbip-xenia-scan-" + Guid.NewGuid().ToString("N"));
            var root = Path.Combine(dir, "Xbox 360");
            scan.GetField("CacheOverride", flags).SetValue(null, Path.Combine(dir, "xenia-scan.tsv"));
            string F(object e, string name) => Convert.ToString(e.GetType().GetField(name)?.GetValue(e) ?? e.GetType().GetProperty(name).GetValue(e));
            List<object> Scan() => ((IEnumerable)scan.GetMethod("Scan", flags).Invoke(null, new object[] { root, null, null })).Cast<object>().ToList();
            object One(List<object> all, string file) => all.FirstOrDefault(e => Path.GetFileName(F(e, "Path")) == file);
            try
            {
                Console.WriteLine();
                Console.WriteLine("-- sorting a folder of Xbox 360 files --");
                Directory.CreateDirectory(Path.Combine(root, "Halo 3", "updates"));
                Directory.CreateDirectory(Path.Combine(root, "Arcade"));
                Directory.CreateDirectory(Path.Combine(root, "GoD", "4D5307E6", "00007000"));
                Directory.CreateDirectory(Path.Combine(root, "Extracted", "Some Game"));
                Directory.CreateDirectory(Path.Combine(root, "Extracted", "Some Game", "media"));
                const string tu = "TU_10ID8B4_0000014000000.00000000000O4";
                File.WriteAllBytes(Path.Combine(root, "Halo 3", "updates", tu), Package("LIVE", 0x000B0000, 0x4D5307E6, 0x00000E01, name: "Title Update 5"));
                const string dlc = "0000000000000000000000000000000000000ABC";
                File.WriteAllBytes(Path.Combine(root, "Halo 3", dlc), Package("PIRS", 0x00000002, 0x4D5307E6, name: "Heroic Map Pack"));
                File.WriteAllBytes(Path.Combine(root, "Arcade", "58410A5D"), Package("LIVE", 0x000D0000, 0x58410A5D, name: "Braid"));
                var god = Path.Combine(root, "GoD", "4D5307E6", "00007000");
                File.WriteAllBytes(Path.Combine(god, "WITHDATA"), Package("LIVE", 0x00007000, 0x4D5307E6, volume: 1, name: "Halo 3"));
                Directory.CreateDirectory(Path.Combine(god, "WITHDATA.data"));
                File.WriteAllBytes(Path.Combine(god, "WITHDATA.data", "Data0000"), new byte[64]);
                File.WriteAllBytes(Path.Combine(god, "NODATA"), Package("LIVE", 0x00007000, 0x4D5307E6, volume: 1, name: "Halo 3 again"));
                File.WriteAllBytes(Path.Combine(root, "Extracted", "Some Game", "default.xex"), Xex(0x41560857));
                File.WriteAllBytes(Path.Combine(root, "Extracted", "Some Game", "media", "ignored"), new byte[16]);
                File.WriteAllBytes(Path.Combine(root, "game.zar"), new byte[128]);
                File.WriteAllBytes(Path.Combine(root, "theme"), Package("CON ", 0x00030000, 0x4D5307E6, name: "A theme"));
                File.WriteAllBytes(Path.Combine(root, "junk"), Encoding.ASCII.GetBytes("this is not a package at all"));
                File.WriteAllBytes(Path.Combine(root, "broken.iso"), new byte[70000]);
                File.WriteAllBytes(Path.Combine(root, "zero"), Package("LIVE", 0x000B0000, 0));
                File.WriteAllText(Path.Combine(root, "readme.txt"), "not ours");
                File.WriteAllText(Path.Combine(root, "Halo 3", "cover.jpg"), "not ours");

                var all = Scan();
                foreach (var e in all.OrderBy(e => F(e, "Path"))) Console.WriteLine("      " + F(e, "Path").Substring(root.Length + 1) + "  ->  " + e);
                string Kind(string file) { var e = One(all, file); return e == null ? "(missing)" : F(e, "Kind"); }
                Check("a title update named as XboxUnity names them: Update, its version read", Kind(tu) == "Update" && F(One(all, tu), "VersionText") == "0.0.14.1" && F(One(all, tu), "TitleId") == "4D5307E6",
                      One(all, tu)?.ToString());
                Check("a DLC with no extension: Dlc, its name read", Kind(dlc) == "Dlc" && F(One(all, dlc), "Name") == "Heroic Map Pack");
                Check("an Arcade game with no extension: Game", Kind("58410A5D") == "Game" && F(One(all, "58410A5D"), "TitleId") == "58410A5D");
                Check("Games on Demand with its .data: Game, and nothing inside .data listed", Kind("WITHDATA") == "Game" && One(all, "Data0000") == null);
                Check("Games on Demand without its .data: Invalid, said why", Kind("NODATA") == "Invalid" && F(One(all, "NODATA"), "Problem").Contains(".data"));
                Check("an extracted disc: ONE entry, the folder, its id from default.xex", Kind("Some Game") == "Game" && F(One(all, "Some Game"), "TitleId") == "41560857"
                      && One(all, "default.xex") == null && One(all, "ignored") == null);
                Check("a .zar: a game with no id", Kind("game.zar") == "GameNoId");
                Check("a theme: Other", Kind("theme") == "Other");
                Check("a file with no extension that is not a package: Invalid", Kind("junk") == "Invalid" && F(One(all, "junk"), "Problem") == "not Xbox 360 content");
                Check("an .iso that is not a disc: Invalid", Kind("broken.iso") == "Invalid" && F(One(all, "broken.iso"), "Problem") == "not an Xbox 360 disc image");
                Check("a package with no title id: Invalid", Kind("zero") == "Invalid");
                Check("readme.txt and cover.jpg not looked at", One(all, "readme.txt") == null && One(all, "cover.jpg") == null);

                Console.WriteLine();
                Console.WriteLine("-- the cache --");
                var cache = File.ReadAllLines(Path.Combine(dir, "xenia-scan.tsv"));
                Check("one line per entry, a header first", cache.Length == all.Count + 1, cache.Length + " lines for " + all.Count);
                var classify = scan.GetMethod("Classify", flags);
                // A second look: the walk, and nothing read - every entry the same object as the cache's.
                var again = Scan();
                Check("a second look finds the same", again.Count == all.Count && again.Select(e => e.ToString()).OrderBy(s => s).SequenceEqual(all.Select(e => e.ToString()).OrderBy(s => s)));
                var log = Path.Combine(dir, "watch");
                File.WriteAllBytes(Path.Combine(root, "Halo 3", dlc), Package("PIRS", 0x00000002, 0x4D5307E6, name: "Heroic Map Pack v2"));
                File.SetLastWriteTimeUtc(Path.Combine(root, "Halo 3", dlc), DateTime.UtcNow.AddMinutes(1));
                File.Delete(Path.Combine(root, "junk"));
                again = Scan();
                Check("a changed file is read again", F(One(again, dlc), "Name") == "Heroic Map Pack v2", F(One(again, dlc), "Name"));
                Check("a removed file is gone, from the scan and the cache", One(again, "junk") == null && !File.ReadAllText(Path.Combine(dir, "xenia-scan.tsv")).Contains("\\junk\t"));
                var cached = ((IEnumerable)scan.GetMethod("Cached", flags).Invoke(null, new object[] { Path.Combine(root, "Halo 3") })).Cast<object>().ToList();
                Check("the cache alone, for a game's folder: its two files", cached.Count == 2, cached.Count.ToString());

                if (iso != null && File.Exists(iso))
                {
                    var fi = new FileInfo(iso);
                    var e = classify.Invoke(null, new object[] { iso, fi.Length, fi.LastWriteTimeUtc.Ticks });
                    Console.WriteLine("      " + iso + "  ->  " + e);
                    Check("a real disc image: Game, with its title id", F(e, "Kind") == "Game" && F(e, "TitleId").Length == 8);
                }
            }
            catch (Exception ex) { Console.WriteLine("  EXCEPTION: " + (ex.InnerException ?? ex)); _bad++; }
            finally
            {
                scan.GetField("CacheOverride", flags).SetValue(null, null);
                try { Directory.Delete(dir, true); } catch { }
            }
            Console.WriteLine(_bad == 0 ? "\n  OK - every Xbox 360 file is sorted, the bad ones said, and a second look reads only what changed" : "\n  " + _bad + " FAILED");
            return _bad == 0;
        }
    }
}
