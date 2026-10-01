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

        /// <summary>--xenia-scan-dir &lt;folder&gt;: a real folder sorted, every field shown, twice (the second from the cache).
        /// The cache in the temp folder; the folder only read.</summary>
        public static bool Dir(Assembly asm, string folder)
        {
            var flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
            var scan = asm.GetType("LbIntegrations.Xenia.XeniaScan", true);
            var cache = Path.Combine(Path.GetTempPath(), "lbip-xenia-scan-dir-" + Guid.NewGuid().ToString("N") + ".tsv");
            scan.GetField("CacheOverride", flags).SetValue(null, cache);
            try
            {
                for (int pass = 1; pass <= 2; pass++)
                {
                    var sw = System.Diagnostics.Stopwatch.StartNew();
                    var all = ((IEnumerable)scan.GetMethod("Scan", flags).Invoke(null, new object[] { folder, null, null })).Cast<object>().ToList();
                    Console.WriteLine("  pass " + pass + ": " + all.Count + " entries in " + sw.ElapsedMilliseconds + " ms");
                    if (pass == 2) break;
                    foreach (var e in all.OrderBy(x => x.GetType().GetField("Path").GetValue(x)))
                    {
                        string G(string n) => Convert.ToString(e.GetType().GetField(n).GetValue(e));
                        uint U(string n) => (uint)e.GetType().GetField(n).GetValue(e);
                        Console.WriteLine("    " + G("Path").Substring(folder.TrimEnd('\\').Length + 1));
                        Console.WriteLine("        " + G("Kind") + "  title " + G("TitleId") + "  type " + U("ContentType").ToString("X8") + "  media " + U("MediaId").ToString("X8")
                                          + "  version " + U("Version").ToString("X8") + " (" + e.GetType().GetProperty("VersionText").GetValue(e) + ")  base " + U("BaseVersion").ToString("X8")
                                          + "  disc " + G("Disc") + "  name \"" + G("Name") + "\"" + (G("Problem").Length > 0 ? "  PROBLEM " + G("Problem") : ""));
                        Console.WriteLine("        digest " + (G("Digest").Length > 0 ? G("Digest") : "-") + "  patch " + U("PatchFrom").ToString("X8") + " -> " + U("PatchTo").ToString("X8") + "  content id " + G("ContentId"));
                    }
                }
            }
            finally
            {
                scan.GetField("CacheOverride", flags).SetValue(null, null);
                try { File.Delete(cache); } catch { }
            }
            return true;
        }

        /// <summary>--xenia-stfs &lt;package or archive&gt;: the files inside each package, and a title update's patch read -
        /// its media id and versions. An archive's entries read without extracting.</summary>
        public static bool Package(Assembly asm, string path)
        {
            var flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
            var filesT = asm.GetType("LbIntegrations.Xenia.StfsFiles", true);
            var readT = asm.GetType("LbIntegrations.Xenia.XeniaPackageRead", true);
            var seqT = asm.GetType("LbIntegrations.Xenia.SequentialRead", true);
            void Show(string label, Func<long, int, byte[]> read, Func<string> extra)
            {
                Console.WriteLine("  " + label);
                var files = filesT.GetMethod("Open", flags).Invoke(null, new object[] { read });
                if (files == null) { Console.WriteLine("    not an STFS volume"); return; }
                foreach (var e in ((IEnumerable)filesT.GetField("Entries").GetValue(files)).Cast<object>()) Console.WriteLine("    " + e);
                var info = readT.GetMethod("UpdatePatch", flags).Invoke(null, new object[] { read });
                if (info == null) Console.WriteLine("    no patch read");
                else
                {
                    uint U(string n) => Convert.ToUInt32(info.GetType().GetField(n).GetValue(info));
                    string V(uint v) => (v >> 28) + "." + ((v >> 24) & 0xF) + "." + ((v >> 8) & 0xFFFF) + "." + (v & 0xFF);
                    Console.WriteLine("    patch: title " + U("TitleId").ToString("X8") + "  media " + U("MediaId").ToString("X8") + "  version " + V(U("Version"))
                                      + "  base " + V(U("BaseVersion")) + "  patch " + V(U("PatchSourceVersion")) + " -> " + V(U("PatchTargetVersion")));
                }
                var game = readT.GetMethod("GameExecutable", flags).Invoke(null, new object[] { read });
                if (game != null) Console.WriteLine("    default.xex: media " + Convert.ToUInt32(game.GetType().GetField("MediaId").GetValue(game)).ToString("X8") + "  signature digest " + game.GetType().GetField("SignatureDigest").GetValue(game));
                if (info != null) Console.WriteLine("    patch digest_source " + info.GetType().GetField("PatchDigestSource").GetValue(info));
                Console.WriteLine("    " + extra());
            }
            if (path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".7z", StringComparison.OrdinalIgnoreCase))
            {
                using var archive = SharpCompress.Archives.ArchiveFactory.Open(path);
                foreach (var entry in archive.Entries.Where(x => !x.IsDirectory))
                {
                    using var s = entry.OpenEntryStream();
                    var seq = Activator.CreateInstance(seqT, s, 256 * 1024 * 1024);
                    var m = seqT.GetMethod("Read");
                    Show(entry.Key + " (" + entry.Size + " bytes)", (o, l) => (byte[])m.Invoke(seq, new object[] { o, l }),
                         () => "decompressed to read it: " + seqT.GetProperty("Decompressed").GetValue(seq) + " bytes");
                }
            }
            else
            {
                using var fs = File.OpenRead(path);
                Show(path, (o, l) => (byte[])asm.GetType("LbIntegrations.Xenia.Xex", true).GetMethod("ReadAt", flags).Invoke(null, new object[] { fs, o, l }), () => "");
            }
            return true;
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
                File.WriteAllBytes(Path.Combine(root, "Arcade", "58410A5D0000000000000000000000000000000000"), Package("LIVE", 0x000D0000, 0x58410A5D, name: "Braid"));
                var god = Path.Combine(root, "GoD", "4D5307E6", "00007000");
                File.WriteAllBytes(Path.Combine(god, "WITHDATA"), Package("LIVE", 0x00007000, 0x4D5307E6, volume: 1, name: "Halo 3"));
                Directory.CreateDirectory(Path.Combine(god, "WITHDATA.data"));
                File.WriteAllBytes(Path.Combine(god, "WITHDATA.data", "Data0000"), new byte[64]);
                File.WriteAllBytes(Path.Combine(god, "NODATA"), Package("LIVE", 0x00007000, 0x4D5307E6, volume: 1, name: "Halo 3 again"));
                File.WriteAllBytes(Path.Combine(root, "Extracted", "Some Game", "default.xex"), Xex(0x41560857));
                File.WriteAllBytes(Path.Combine(root, "Extracted", "Some Game", "media", "ignored"), new byte[16]);
                File.WriteAllBytes(Path.Combine(root, "game.zar"), new byte[128]);
                Directory.CreateDirectory(Path.Combine(root, "4D5307E6", "00030000"));
                File.WriteAllBytes(Path.Combine(root, "4D5307E6", "00030000", "theme"), Package("CON ", 0x00030000, 0x4D5307E6, name: "A theme"));
                File.WriteAllBytes(Path.Combine(root, "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA"), Encoding.ASCII.GetBytes("this is not a package at all"));
                File.WriteAllBytes(Path.Combine(root, "broken.iso"), new byte[70000]);
                File.WriteAllBytes(Path.Combine(root, "BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB"), Package("LIVE", 0x000B0000, 0));
                File.WriteAllText(Path.Combine(root, "notes"), "a file with no extension, named as nothing of ours");
                File.WriteAllBytes(Path.Combine(root, "58410A5D"), Package("LIVE", 0x000D0000, 0x58410A5D));
                // No-Intro's digital sets: each package zipped, the console's tree around it.
                Directory.CreateDirectory(Path.Combine(root, "Digital"));
                void Zip(string name, params (string Entry, byte[] Data)[] items)
                {
                    using var z = System.IO.Compression.ZipFile.Open(Path.Combine(root, "Digital", name), System.IO.Compression.ZipArchiveMode.Create);
                    foreach (var (entry, data) in items)
                    {
                        using var s = z.CreateEntry(entry, System.IO.Compression.CompressionLevel.Optimal).Open();
                        s.Write(data, 0, data.Length);
                    }
                }
                byte[] Big(byte[] head) { var b = new byte[4 * 1024 * 1024]; head.CopyTo(b, 0); new Random(1).NextBytes(b.AsSpan(head.Length)); return b; }
                Zip("Real Steel (World) (XBLA).zip", ("584111E0/000D0000/62939F79719792E4C7A3023F3C63E17428928B4D58", Big(Package("LIVE", 0x000D0000, 0x584111E0, name: "Real Steel"))));
                Zip("Real Steel (World) (v3) (Title Update).zip", ("584111E0/000B0000/34B98210F59364D0E50938EE7D0E5AA3FD92558B58", Package("LIVE", 0x000B0000, 0x584111E0, 0x00000300, name: "Title Update 3")));
                Zip("Real Steel - Add-on 01 (World) (Addon).zip", ("584111E0/00000002/C729FE8E08F1AC6E8A781CA41A016D2028818ED558", Package("PIRS", 0x00000002, 0x584111E0, name: "Add-on 01")));
                Zip("QbTron 3D (World) (XBLIG).zip", ("584E07D2/00000002/13A061B9D5E8222C8E51FD6B11BFC536586D84E458", Package("LIVE", 0x00000002, 0x584E07D2, name: "QbTron 3D")));
                Zip("nothing.zip", ("readme.txt", Encoding.ASCII.GetBytes("hello")));
                Zip("disc.zip", ("Game.iso", new byte[4096]));                File.WriteAllText(Path.Combine(root, "readme.txt"), "not ours");
                File.WriteAllText(Path.Combine(root, "Halo 3", "cover.jpg"), "not ours");

                var all = Scan();
                foreach (var e in all.OrderBy(e => F(e, "Path"))) Console.WriteLine("      " + F(e, "Path").Substring(root.Length + 1) + "  ->  " + e);
                string Kind(string file) { var e = One(all, file); return e == null ? "(missing)" : F(e, "Kind"); }
                Check("a title update named as XboxUnity names them: Update, its version read", Kind(tu) == "Update" && F(One(all, tu), "VersionText") == "0.0.14.1" && F(One(all, tu), "TitleId") == "4D5307E6",
                      One(all, tu)?.ToString());
                Check("a DLC with no extension: Dlc, its name read", Kind(dlc) == "Dlc" && F(One(all, dlc), "Name") == "Heroic Map Pack");
                Check("an Arcade game with no extension: Game", Kind("58410A5D0000000000000000000000000000000000"[..42]) == "Game" && F(One(all, "58410A5D0000000000000000000000000000000000"[..42]), "TitleId") == "58410A5D");
                Check("Games on Demand with its .data: Game, and nothing inside .data listed", Kind("WITHDATA") == "Game" && One(all, "Data0000") == null);
                Check("Games on Demand without its .data: Invalid, said why", Kind("NODATA") == "Invalid" && F(One(all, "NODATA"), "Problem").Contains(".data"));
                Check("an extracted disc: ONE entry, the folder, its id from default.xex", Kind("Some Game") == "Game" && F(One(all, "Some Game"), "TitleId") == "41560857"
                      && One(all, "default.xex") == null && One(all, "ignored") == null);
                Check("a .zar: a game with no id", Kind("game.zar") == "GameNoId");
                Check("a theme: Other", Kind("theme") == "Other");
                Check("a file named as a package that is not one: Invalid", Kind("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA") == "Invalid" && F(One(all, "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA"), "Problem") == "not Xbox 360 content");
                Check("an .iso that is not a disc: Invalid", Kind("broken.iso") == "Invalid" && F(One(all, "broken.iso"), "Problem") == "not an Xbox 360 disc image");
                Check("a package with no title id: Invalid", Kind("BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB") == "Invalid");
                Check("a file with no extension named as nothing of ours, outside a <title id>\\<type> folder: not looked at", One(all, "notes") == null && One(all, "58410A5D") == null);
                Check("readme.txt and cover.jpg not looked at", One(all, "readme.txt") == null && One(all, "cover.jpg") == null);
                object Entry(List<object> list, string zip) => list.FirstOrDefault(e => F(e, "Path").Contains("\\Digital\\" + zip + "|"));
                string KindIn(List<object> list, string zip) { var e = Entry(list, zip); return e == null ? "(missing)" : F(e, "Kind"); }
                Check("a zipped Arcade game: Game, read without extracting", KindIn(all, "Real Steel (World) (XBLA).zip") == "Game" && F(Entry(all, "Real Steel (World) (XBLA).zip"), "TitleId") == "584111E0",
                      Entry(all, "Real Steel (World) (XBLA).zip")?.ToString());
                Check("a zipped title update: Update, its version", KindIn(all, "Real Steel (World) (v3) (Title Update).zip") == "Update" && F(Entry(all, "Real Steel (World) (v3) (Title Update).zip"), "VersionText") == "0.0.3.0");
                Check("a zipped add-on: Dlc of the game's title id", KindIn(all, "Real Steel - Add-on 01 (World) (Addon).zip") == "Dlc" && F(Entry(all, "Real Steel - Add-on 01 (World) (Addon).zip"), "TitleId") == "584111E0");
                Check("a zipped Indie game (00000002 of 584E07D2): Game, not DLC", KindIn(all, "QbTron 3D (World) (XBLIG).zip") == "Game");
                Check("a zip with nothing of ours: one Invalid line for it", KindIn(all, "nothing.zip") == "Invalid" && F(Entry(all, "nothing.zip"), "Problem").Contains("no Xbox 360 content"));
                Check("a disc image in a zip: noted, not read", KindIn(all, "disc.zip") == "GameNoId" && F(Entry(all, "disc.zip"), "Problem").Contains("not read"));

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
                File.Delete(Path.Combine(root, "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA"));
                again = Scan();
                Check("a changed file is read again", F(One(again, dlc), "Name") == "Heroic Map Pack v2", F(One(again, dlc), "Name"));
                Check("a removed file is gone, from the scan and the cache", One(again, "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA") == null && !File.ReadAllText(Path.Combine(dir, "xenia-scan.tsv")).Contains("\\AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA\t"));
                var cached = ((IEnumerable)scan.GetMethod("Cached", flags).Invoke(null, new object[] { Path.Combine(root, "Halo 3") })).Cast<object>().ToList();
                Check("the cache alone, for a game's folder: its two files", cached.Count == 2, cached.Count.ToString());
                var zipPath = Path.Combine(root, "Digital", "Real Steel (World) (v3) (Title Update).zip");
                File.SetLastWriteTimeUtc(zipPath, DateTime.UtcNow.AddMinutes(2));
                again = Scan();
                Check("a changed zip is read again, the others from the cache", KindIn(again, "Real Steel (World) (v3) (Title Update).zip") == "Update" && again.Count == all.Count - 1);

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
