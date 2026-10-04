// --cxbx-selftest: the Cxbx plugin's reading and unpacking of original Xbox games, on discs MADE HERE (no real one is
// needed, none is shipped): an XDVDFS image laid out as a trimmed XISO and as a redump (game partition at 0x18300000),
// its tables written AFTER the files they list so that the stream reader has to make more than one pass; the same in a
// zip; a game already unpacked in a zip; an Xbox 360 disc. Then the launch line, the save file's determinism and its
// restore, and a whole Prepare (RAM disk off) twice - the second opened from the disk.
// --cxbx-describe <file>: a real game file described. --cxbx-releases: the newest CI build, online.
// Writes only in the temp folder.

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Text;

namespace LbIntegrations.Probe
{
    internal static class CxbxCheck
    {
        private const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
        private static int _bad;
        private static Assembly _asm;

        private static bool Check(string what, bool ok, string detail = null)
        {
            Console.WriteLine("    " + (ok ? "ok   " : "FAIL ") + what + (!ok && detail != null ? "\n          got: " + detail : ""));
            if (!ok) _bad++;
            return ok;
        }

        private static Type T(string name) => _asm.GetType("LbIntegrations.Cxbx." + name, true);
        private static object Call(string type, string method, params object[] args)
        {
            // The exact count first; else one whose remaining parameters are optional - filled with their defaults.
            var m = T(type).GetMethods(Any).FirstOrDefault(x => x.Name == method && x.GetParameters().Length == args.Length)
                    ?? T(type).GetMethods(Any).First(x => x.Name == method && x.GetParameters().Length > args.Length
                                                         && x.GetParameters().Skip(args.Length).All(p => p.IsOptional));
            var full = args.Concat(m.GetParameters().Skip(args.Length).Select(p => p.DefaultValue)).ToArray();
            try { return m.Invoke(null, full); } catch (TargetInvocationException ex) { throw ex.InnerException ?? ex; }
        }
        private static object Get(object o, string member)
        {
            if (o == null) return null;
            var t = o.GetType();
            var f = t.GetField(member, Any);
            if (f != null) return f.GetValue(o);
            var p = t.GetProperty(member, Any);
            return p?.GetValue(o);
        }

        // ── a disc, made here ────────────────────────────────────────────────

        internal sealed class Node
        {
            public string Name;
            public byte[] Data;                       // a file
            public List<Node> Children;               // a directory
            public uint Sector, Size;
        }

        internal static Node File_(string name, byte[] data) => new Node { Name = name, Data = data };
        internal static Node Dir(string name, params Node[] children) => new Node { Name = name, Children = children.ToList() };

        private static byte[] Table(List<Node> entries)
        {
            var b = new List<byte>();
            for (int i = 0; i < entries.Count; i++)
            {
                var e = entries[i];
                var name = Encoding.ASCII.GetBytes(e.Name);
                int len = (14 + name.Length + 3) & ~3;
                int next = i + 1 < entries.Count ? (b.Count + len) / 4 : 0;
                var entry = new byte[len];
                for (int k = 0; k < len; k++) entry[k] = 0xFF;
                BitConverter.GetBytes((ushort)0).CopyTo(entry, 0);
                BitConverter.GetBytes((ushort)next).CopyTo(entry, 2);
                BitConverter.GetBytes(e.Sector).CopyTo(entry, 4);
                BitConverter.GetBytes(e.Size).CopyTo(entry, 8);
                entry[12] = (byte)(e.Children != null ? 0x10 : 0x20);
                entry[13] = (byte)name.Length;
                name.CopyTo(entry, 14);
                b.AddRange(entry);
            }
            while (b.Count % 2048 != 0) b.Add(0xFF);
            return b.ToArray();
        }

        /// <summary>An XDVDFS image at <paramref name="partitionBase"/>: files from sector 0x40, then the tables, deepest
        /// FIRST and the root LAST - so a reader front to back meets each table after the files it names.</summary>
        internal static void WriteImage(string path, long partitionBase, Node root)
        {
            uint sector = 0x40;
            var files = new List<Node>(); var dirs = new List<Node>();
            void Walk(Node d) { foreach (var c in d.Children) { if (c.Children != null) { dirs.Add(c); Walk(c); } else files.Add(c); } }
            Walk(root);
            foreach (var f in files) { f.Sector = sector; f.Size = (uint)f.Data.Length; sector += (uint)Math.Max(1, (f.Data.Length + 2047) / 2048); }
            // Tables: the root's after the others, the others deepest first (dirs is in walk order: parent before child).
            var order = dirs.AsEnumerable().Reverse().Concat(new[] { root }).ToList();
            var tableBytes = new Dictionary<Node, byte[]>();
            // Sizes first: a parent's entry holds its child's table size.
            foreach (var d in order) d.Size = (uint)Table(d.Children).Length;
            foreach (var d in order) { d.Sector = sector; sector += d.Size / 2048; }
            foreach (var d in order) tableBytes[d] = Table(d.Children);

            using var fs = new FileStream(path, FileMode.Create);
            fs.SetLength(partitionBase + (long)sector * 2048 + 4096);
            var desc = new byte[2048];
            Encoding.ASCII.GetBytes("MICROSOFT*XBOX*MEDIA").CopyTo(desc, 0);
            BitConverter.GetBytes(root.Sector).CopyTo(desc, 0x14);
            BitConverter.GetBytes(root.Size).CopyTo(desc, 0x18);
            Encoding.ASCII.GetBytes("MICROSOFT*XBOX*MEDIA").CopyTo(desc, 0x7EC);
            fs.Seek(partitionBase + 0x10000, SeekOrigin.Begin); fs.Write(desc);
            foreach (var f in files) { fs.Seek(partitionBase + (long)f.Sector * 2048, SeekOrigin.Begin); fs.Write(f.Data); }
            foreach (var d in order) { fs.Seek(partitionBase + (long)d.Sector * 2048, SeekOrigin.Begin); fs.Write(tableBytes[d]); }
        }

        internal static byte[] XbeBytes(uint titleId, string name)
        {
            var b = new byte[0x3000];
            Encoding.ASCII.GetBytes("XBEH").CopyTo(b, 0);
            BitConverter.GetBytes(0x00010000u).CopyTo(b, 0x104);
            BitConverter.GetBytes(0x00010000u + 0x400).CopyTo(b, 0x118);
            BitConverter.GetBytes(titleId).CopyTo(b, 0x400 + 0x08);
            Encoding.Unicode.GetBytes(name).CopyTo(b, 0x400 + 0x0C);
            BitConverter.GetBytes(1u).CopyTo(b, 0x400 + 0xA0);
            BitConverter.GetBytes(1u).CopyTo(b, 0x400 + 0xAC);        // version: v1.0
            for (int i = 0x1000; i < b.Length; i++) b[i] = (byte)(i * 7);
            return b;
        }

        internal static byte[] Noise(int n, int seed) { var r = new Random(seed); var b = new byte[n]; r.NextBytes(b); return b; }

        internal static Node Game(uint titleId) => Dir("",
            File_("default.xbe", XbeBytes(titleId, "Probe Game")),
            File_("readme.txt", Encoding.ASCII.GetBytes("hello")),
            File_("empty.dat", new byte[0]),
            Dir("media", File_("a.bin", Noise(300000, 1)), Dir("sub", File_("deep.bin", Noise(5000, 2)))),
            File_("big.bin", Noise(3 * 1024 * 1024 + 17, 3)));

        private static bool SameTree(string a, string b, out string why)
        {
            why = null;
            var fa = Directory.GetFiles(a, "*", SearchOption.AllDirectories).Select(f => f.Substring(a.Length)).OrderBy(x => x).ToList();
            var fb = Directory.GetFiles(b, "*", SearchOption.AllDirectories).Select(f => f.Substring(b.Length)).OrderBy(x => x).ToList();
            if (!fa.SequenceEqual(fb, StringComparer.OrdinalIgnoreCase)) { why = string.Join(",", fa) + " vs " + string.Join(",", fb); return false; }
            foreach (var f in fa) if (!File.ReadAllBytes(a + f).SequenceEqual(File.ReadAllBytes(b + f))) { why = f + " differs"; return false; }
            return true;
        }

        private static void WriteTree(string dir, Node d)
        {
            Directory.CreateDirectory(dir);
            foreach (var c in d.Children)
                if (c.Children != null) WriteTree(Path.Combine(dir, c.Name), c);
                else File.WriteAllBytes(Path.Combine(dir, c.Name), c.Data);
        }

        public static bool Run(Assembly asm)
        {
            _asm = asm;
            var work = Path.Combine(Path.GetTempPath(), "lbip-cxbx-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            Directory.CreateDirectory(work);
            T("CxbxSettings").GetField("DirOverride", Any).SetValue(null, Path.Combine(work, "settings"));
            Console.WriteLine("  work folder " + work);
            try
            {
                var expected = Path.Combine(work, "expected");
                WriteTree(expected, Game(0x4D530004));

                // 1. a trimmed image, loose
                var xiso = Path.Combine(work, "Probe Game (USA).xiso");
                WriteImage(xiso, 0, Game(0x4D530004));
                var d = Call("CxbxGame", "Describe", xiso, true);
                Console.WriteLine("  XISO, loose");
                Check("is an image", Get(d, "Kind")?.ToString() == "Image", Get(d, "Kind") + " / " + Get(d, "Problem"));
                Check("title id 4d530004", (string)Get(Get(d, "Xbe"), "TitleIdText") == "4d530004", "" + Get(Get(d, "Xbe"), "TitleIdText"));
                Check("title name read", (string)Get(Get(d, "Xbe"), "TitleName") == "Probe Game", "" + Get(Get(d, "Xbe"), "TitleName"));
                long expectedBytes = Directory.GetFiles(expected, "*", SearchOption.AllDirectories).Sum(f => new FileInfo(f).Length);
                Check("size = its files", (long)Get(d, "Bytes") == expectedBytes, Get(d, "Bytes") + " vs " + expectedBytes);
                var outA = Path.Combine(work, "out-a");
                var r = Call("Xdvdfs", "Extract", (Func<Stream>)(() => File.OpenRead(xiso)), true, new FileInfo(xiso).Length, outA, null);
                Check("extracted, seeking: no error", Get(r, "Error") == null, "" + Get(r, "Error"));
                Check("extracted, seeking: one walk to list, one to unpack", (int)Get(r, "Passes") == 2, "" + Get(r, "Passes"));
                Check("extracted tree = the disc", SameTree(expected, outA, out var why), why);

                // 2. the same in a zip: a stream, front to back
                var zip = Path.Combine(work, "Probe Game (USA).zip");
                using (var z = ZipFile.Open(zip, ZipArchiveMode.Create)) z.CreateEntryFromFile(xiso, "Probe Game (USA).iso", CompressionLevel.Optimal);
                d = Call("CxbxGame", "Describe", zip, true);
                Console.WriteLine("  XISO in a zip");
                Check("is an image in an archive", Get(d, "Kind")?.ToString() == "ImageInArchive", Get(d, "Kind") + " / " + Get(d, "Problem"));
                Check("title id unknown before unpacking", Call("CxbxGame", "TitleIdOf", zip) == null);
                var outB = Path.Combine(work, "out-b");
                long len = new FileInfo(xiso).Length;
                using (var za = SharpCompress.Archives.ArchiveFactory.Open(zip))
                {
                    var entry = za.Entries.First();
                    r = Call("Xdvdfs", "Extract", (Func<Stream>)(() => entry.OpenEntryStream()), false, len, outB, null);
                }
                Check("extracted, streaming: no error", Get(r, "Error") == null, "" + Get(r, "Error"));
                Check("tables written after their files: taken from the blocks kept, not from another pass", (int)Get(r, "TablesFromMemory") >= 2, "" + Get(r, "TablesFromMemory"));
                Check("two passes in all: one to list, one to unpack", (int)Get(r, "Passes") == 2, "" + Get(r, "Passes"));
                Check("extracted tree = the disc", SameTree(expected, outB, out why), why);

                // 3. a redump layout: the game partition at 0x18300000
                var redump = Path.Combine(work, "Probe Game (Redump).iso");
                WriteImage(redump, 0x18300000, Game(0x4D530004));
                d = Call("CxbxGame", "Describe", redump, true);
                Console.WriteLine("  redump image");
                Check("found at 0x18300000", Get(d, "Kind")?.ToString() == "Image" && (string)Get(Get(d, "Xbe"), "TitleIdText") == "4d530004", Get(d, "Kind") + " / " + Get(d, "Problem"));
                var redumpZip = Path.Combine(work, "Probe Game (Redump).zip");
                using (var z = ZipFile.Open(redumpZip, ZipArchiveMode.Create)) z.CreateEntryFromFile(redump, "Probe Game (Redump).iso", CompressionLevel.Fastest);
                File.Delete(redump);

                // 4. a game already unpacked, in a zip
                var tree = Path.Combine(work, "Probe Tree.zip");
                using (var z = ZipFile.Open(tree, ZipArchiveMode.Create))
                    foreach (var f in Directory.GetFiles(expected, "*", SearchOption.AllDirectories))
                        z.CreateEntryFromFile(f, "Probe Tree/" + f.Substring(expected.Length + 1).Replace('\\', '/'));
                d = Call("CxbxGame", "Describe", tree, true);
                Console.WriteLine("  unpacked game in a zip");
                Check("is a tree in an archive, title read", Get(d, "Kind")?.ToString() == "TreeInArchive" && (string)Get(Get(d, "Xbe"), "TitleIdText") == "4d530004", Get(d, "Kind") + " / " + Get(d, "Problem"));

                // 5. an Xbox 360 disc
                var x360 = Path.Combine(work, "Other.iso");
                WriteImage(x360, 0, Dir("", File_("default.xex", Encoding.ASCII.GetBytes("XEX2...."))));
                d = Call("CxbxGame", "Describe", x360, true);
                Console.WriteLine("  Xbox 360 disc");
                Check("refused as Xbox 360", Get(d, "Kind")?.ToString() == "Xbox360" && Get(d, "Problem") != null, Get(d, "Kind") + " / " + Get(d, "Problem"));

                // 6. the launch line
                Console.WriteLine("  launch line");
                var xbe = @"R:\Probe Game\game\default.xbe";
                var rom = @"G:\Games\Probe Game (USA).zip";
                string Line(string current) => (string)Call("CxbxPlugin", "CommandLineFor", current, xbe, rom, false);
                string GuiLine(string current) => (string)Call("CxbxPlugin", "CommandLineFor", current, xbe, rom, true);
                Check("the GUI: the XBE as its very first word", GuiLine("/df \"" + rom + "\"") == "\"" + xbe + "\" /df", GuiLine("/df \"" + rom + "\""));
                Check("the GUI: a key of the user's stays after it", GuiLine("/chihiro \"" + rom + "\"") == "\"" + xbe + "\" /chihiro /df", GuiLine("/chihiro \"" + rom + "\""));
                Check("/df \"<rom>\" -> /load \"<xbe>\" /df", Line("/df \"" + rom + "\"") == "/load \"" + xbe + "\" /df", Line("/df \"" + rom + "\""));
                Check("the rom alone -> same", Line("\"" + rom + "\"") == "/load \"" + xbe + "\" /df", Line("\"" + rom + "\""));
                Check("a key of the user's stays", Line("/chihiro /df \"" + rom + "\"") == "/load \"" + xbe + "\" /chihiro /df", Line("/chihiro /df \"" + rom + "\""));
                Check("an old /load goes", Line("/load \"C:\\x.xbe\" \"" + rom + "\"") == "/load \"" + xbe + "\" /df", Line("/load \"C:\\x.xbe\" \"" + rom + "\""));

                // 7. the save file
                Console.WriteLine("  save file");
                var live = Path.Combine(work, "udata", "4d530004");
                WriteTree(live, Dir("", File_("TitleMeta.xbx", Encoding.Unicode.GetBytes("TitleName=Probe")), Dir("ABCDEF012345", File_("SaveMeta.xbx", Noise(200, 4)), File_("save.dat", Noise(9000, 5)))));
                var p1 = Path.Combine(work, "p1.cxbxsave"); var p2 = Path.Combine(work, "p2.cxbxsave");
                var args1 = new object[] { live, p1, null }; var args2 = new object[] { live, p2, null };
                bool ok1 = (bool)T("CxbxSaves").GetMethod("Pack", Any).Invoke(null, args1);
                System.Threading.Thread.Sleep(50);
                foreach (var f in Directory.GetFiles(live, "*", SearchOption.AllDirectories)) File.SetLastWriteTimeUtc(f, DateTime.UtcNow);
                bool ok2 = (bool)T("CxbxSaves").GetMethod("Pack", Any).Invoke(null, args2);
                Check("packed twice", ok1 && ok2, args1[2] + " / " + args2[2]);
                Check("same bytes for the same content, whatever the dates", File.ReadAllBytes(p1).SequenceEqual(File.ReadAllBytes(p2)));
                File.WriteAllText(Path.Combine(live, "stray.txt"), "not in the backup");
                var ua = new object[] { p1, live, null };
                bool un = (bool)T("CxbxSaves").GetMethod("Unpack", Any).Invoke(null, ua);
                Check("restored", un, "" + ua[2]);
                Check("restore replaces: a file not in the backup is gone", !File.Exists(Path.Combine(live, "stray.txt")));
                Check("restore holds the slot folder", File.Exists(Path.Combine(live, "ABCDEF012345", "save.dat")));

                // 8. a whole Prepare, twice, onto the disk
                Console.WriteLine("  prepare (RAM disk off)");
                var emuDir = Path.Combine(work, "Nixx-Cxbx");
                Directory.CreateDirectory(emuDir);
                var exe = Path.Combine(emuDir, "cxbxr-ldr.exe");
                File.WriteAllBytes(exe, new byte[0]);
                File.WriteAllText(Path.Combine(emuDir, "settings.ini"), "");
                Call("CxbxSettings", "Write", new Dictionary<string, string> { ["ramdisk"] = "off" });
                var pa = new object[] { zip, "game-1", exe, null, null };
                var m = T("CxbxPlace").GetMethod("Prepare", Any);
                var got = (string)m.Invoke(null, pa);
                Check("first launch: unpacked, an XBE handed over", got != null && File.Exists(got), "" + pa[3]);
                if (got != null) Check("unpacked tree = the disc", SameTree(expected, Path.GetDirectoryName(got), out why), why);
                Check("title id known now, from titles.tsv", (string)Call("CxbxGame", "TitleIdOf", zip) == "4d530004", "" + Call("CxbxGame", "TitleIdOf", zip));
                var stamp = got == null ? DateTime.MinValue : File.GetLastWriteTimeUtc(got);
                System.Threading.Thread.Sleep(50);
                var got2 = (string)m.Invoke(null, pa);
                Check("second launch: opened from the disk, not unpacked again", got2 == got && File.GetLastWriteTimeUtc(got2) == stamp, got2);
                // The listing kept: a launch whose copy is gone unpacks without listing again - and a kept listing that
                // does not describe the disc is caught by the descriptor check and read again.
                if (got != null)
                {
                    var listing = Call("CxbxListing", "Load", zip);
                    Check("the listing is kept", listing != null && (long)Get(listing, "Bytes") == expectedBytes, listing == null ? "none" : "" + Get(listing, "Bytes"));
                    Directory.Delete(Path.GetDirectoryName(Path.GetDirectoryName(got)), true);
                    var got4 = (string)m.Invoke(null, pa);
                    Check("copy gone: unpacked again from the kept listing", got4 != null && SameTree(expected, Path.GetDirectoryName(got4), out why), "" + (pa[3] ?? why));
                    var dir = Path.Combine(work, "settings", "listings");
                    var file = Directory.GetFiles(dir).First();
                    var lines = File.ReadAllLines(file); lines[1] = "base=18300000"; File.WriteAllLines(file, lines);
                    Directory.Delete(Path.GetDirectoryName(Path.GetDirectoryName(got4)), true);
                    var got5 = (string)m.Invoke(null, pa);
                    Check("a wrong kept listing: caught, read again, unpacked", got5 != null && SameTree(expected, Path.GetDirectoryName(got5), out why), "" + (pa[3] ?? why));
                }
                var pr = new object[] { redumpZip, "game-2", exe, null, null };
                var got3 = (string)m.Invoke(null, pr);
                Check("redump in a zip: unpacked", got3 != null && SameTree(expected, Path.GetDirectoryName(got3), out why), (pr[3] ?? why) + "");
                var px = new object[] { x360, "game-3", exe, null, null };
                Check("Xbox 360 disc: refused with a reason", m.Invoke(null, px) == null && px[3] != null, "" + px[3]);
                Console.WriteLine("      (" + px[3] + ")");

                // 8b. the launch mode is the entry's path: only its file name changes, and only to a file that is there
                Console.WriteLine("  launch mode");
                File.WriteAllBytes(Path.Combine(emuDir, "cxbx.exe"), new byte[0]);
                string Mode(string entry, bool gui) => (string)Call("CxbxPaths", "PathForMode", entry, Path.Combine(emuDir, Path.GetFileName(entry)), gui);
                Check("loader entry, window wanted: cxbx.exe, same folder, still relative", Mode(@"Emulators\Nixx-Cxbx\cxbxr-ldr.exe", true) == @"Emulators\Nixx-Cxbx\cxbx.exe", Mode(@"Emulators\Nixx-Cxbx\cxbxr-ldr.exe", true));
                Check("window entry, window wanted: nothing to change", Mode(@"Emulators\Nixx-Cxbx\cxbx.exe", true) == null);
                Check("window entry, loader wanted: back to cxbxr-ldr.exe", Mode(@"Emulators\Nixx-Cxbx\cxbx.exe", false) == @"Emulators\Nixx-Cxbx\cxbxr-ldr.exe");
                File.Delete(Path.Combine(emuDir, "cxbx.exe"));
                Check("the GUI is not there: left on the loader", Mode(@"Emulators\Nixx-Cxbx\cxbxr-ldr.exe", true) == null);
                Check("another emulator: never touched", Mode(@"Emulators\Xenia\xenia_canary.exe", true) == null);

                // 8b. the active save (lbip-saves\<id>.cxbxsave, Mehdi 04/10): a file put there is laid out at launch; the folder after
                // a session is packed into it; the plugin's own file is never laid back over a newer folder.
                Console.WriteLine("  active save");
                var udataDir = (string)Call("CxbxPaths", "UdataDir", exe);
                if (udataDir == null) Check("Cxbx-Reloaded's UDATA folder known", false, "none for " + exe);
                else
                {
                    var activeLive = Path.Combine(udataDir, "4d530005");
                    var activePack = (string)Call("CxbxSaves", "PackPath", exe, "4d530005");
                    Directory.CreateDirectory(Path.GetDirectoryName(activePack));
                    File.Copy(p1, activePack, overwrite: true);
                    Check("a file put in lbip-saves: laid out at launch", Call("CxbxSaves", "SyncIn", exe, "4d530005") is string a1 && a1.Contains("laid"));
                    Check("... the folder holds it", File.Exists(Path.Combine(activeLive, "ABCDEF012345", "save.dat")));
                    Check("... the next launch: nothing to do", Call("CxbxSaves", "SyncIn", exe, "4d530005") == null);
                    System.Threading.Thread.Sleep(50);
                    File.WriteAllBytes(Path.Combine(activeLive, "ABCDEF012345", "save.dat"), Noise(9100, 6));     // the game saving
                    var before = File.ReadAllBytes(activePack);
                    Check("after a session: the folder packed into the file", (string)Call("CxbxSaves", "Capture", exe, "4d530005") == activePack && !File.ReadAllBytes(activePack).SequenceEqual(before));
                    Check("... the file the plugin wrote not laid back at launch", Call("CxbxSaves", "SyncIn", exe, "4d530005") == null && new FileInfo(Path.Combine(activeLive, "ABCDEF012345", "save.dat")).Length == 9100);
                    File.Copy(p1, activePack, overwrite: true);                                                        // a Restore
                    Check("a file put back: not packed over before the launch", (string)Call("CxbxSaves", "Capture", exe, "4d530005") == activePack && File.ReadAllBytes(activePack).SequenceEqual(File.ReadAllBytes(p1)));
                    Check("... laid out at launch", Call("CxbxSaves", "SyncIn", exe, "4d530005") is string a2 && a2.Contains("laid") && new FileInfo(Path.Combine(activeLive, "ABCDEF012345", "save.dat")).Length == 9000);
                    File.Delete(activePack);
                    Check("the file removed: the folder taken out at launch", Call("CxbxSaves", "SyncIn", exe, "4d530005") is string a3 && a3.Contains("taken out") && !Directory.Exists(activeLive));
                }

                // 9. the compatibility list, embedded: GTA San Andreas Europe (Classics), title id 545400a4, version 1
                Console.WriteLine("  compatibility (embedded list)");
                Check("serial as Cxbx-Reloaded writes it: TT-164", (string)Call("CxbxCompat", "SerialOf", 0x545400A4u) == "TT-164");
                Check("version as it writes it: v1.0", (string)Call("CxbxCompat", "VersionOf", 1u) == "v1.0");
                var gta = Activator.CreateInstance(T("XbeInfo"));
                T("XbeInfo").GetField("TitleId").SetValue(gta, 0x545400A4u);
                T("XbeInfo").GetField("Version").SetValue(gta, 1u);
                var line = (string)Call("CxbxCompat", "Describe", gta);
                Console.WriteLine("      " + line);
                Check("this very disc found, the other versions listed", line.StartsWith("TT-164 v1.0: Untested") && line.Contains("TT-130 v2.0 In-Game"), line);

                // 10. the console's region
                Console.WriteLine("  EEPROM region");
                var eeprom = Path.Combine(emuDir, "EEPROM.bin");
                object Info(uint region) { var i = Activator.CreateInstance(T("XbeInfo")); T("XbeInfo").GetField("Region").SetValue(i, region); return i; }
                bool Signed(byte[] e) { using var h = new System.Security.Cryptography.HMACSHA1(new byte[16]); return h.ComputeHash(e, 0x14, 28).SequenceEqual(e.Take(20)); }
                Call("CxbxEeprom", "MatchRegion", exe, Info(4));
                var e1 = File.Exists(eeprom) ? File.ReadAllBytes(eeprom) : new byte[0];
                Check("none there: one made, 256 bytes", e1.Length == 256, "" + e1.Length);
                if (e1.Length == 256)
                {
                    Check("made with the game's region (PAL)", BitConverter.ToUInt32(e1, 0x2C) == 4, "" + BitConverter.ToUInt32(e1, 0x2C));
                    Check("made as Cxbx-Reloaded makes one: English, NTSC-M 60 Hz, a Microsoft MAC", BitConverter.ToUInt32(e1, 0x90) == 1 && BitConverter.ToUInt32(e1, 0x58) == 0x00400100 && e1[0x41] == 0x50 && e1[0x42] == 0xF2);
                    Check("its checksum right", Signed(e1));
                    Check("made with the pack's HDD key (16 x 0x11)", e1.Skip(0x1C).Take(16).All(x => x == 0x11));
                    var na = (byte[])e1.Clone(); BitConverter.GetBytes(1u).CopyTo(na, 0x2C); File.WriteAllBytes(eeprom, na);
                    Call("CxbxEeprom", "MatchRegion", exe, Info(4));
                    var e2 = File.ReadAllBytes(eeprom);
                    Check("NTSC console, PAL game: region set to PAL", BitConverter.ToUInt32(e2, 0x2C) == 4);
                    Check("checksum recomputed", Signed(e2));
                    Check("nothing else touched", e2.Skip(20).SequenceEqual(e1.Skip(20)));
                    Call("CxbxEeprom", "MatchRegion", exe, Info(5));
                    Check("a game it already accepts: file left as it is", File.ReadAllBytes(eeprom).SequenceEqual(e2));
                    Call("CxbxEeprom", "MatchRegion", exe, Info(0x80000000));
                    Check("no region in the certificate: left as it is", File.ReadAllBytes(eeprom).SequenceEqual(e2));
                }

                // 11. the options of a session: written, then put back as they were
                Console.WriteLine("  options for a session");
                var ini = Path.Combine(emuDir, "settings.ini");
                File.WriteAllText(ini, "[gui]\r\nDataStorageToggle = 1\r\n\r\n[video]\r\nadapter = 1\r\nVSync = true\r\n\r\n[core]\r\nFlagsLLE = 8\r\n");
                if (!File.Exists(eeprom)) Call("CxbxEeprom", "MatchRegion", exe, Info(1), false);
                {
                    // A console with a key of its own (Cxbx-Reloaded draws one): the session must get the pack's.
                    var ownKey = File.ReadAllBytes(eeprom);
                    for (int i = 0; i < 16; i++) ownKey[0x1C + i] = (byte)(0xA0 + i);
                    using var h = new System.Security.Cryptography.HMACSHA1(new byte[16]);
                    h.ComputeHash(ownKey, 0x14, 28).CopyTo(ownKey, 0);
                    File.WriteAllBytes(eeprom, ownKey);
                }
                var iniBefore = File.ReadAllBytes(ini); var eepBefore = File.ReadAllBytes(eeprom);
                Call("CxbxSettings", "Write", new Dictionary<string, string> { ["ramdisk"] = "off", ["opt.video.render"] = "2", ["opt.lle.gpu"] = "on" });
                Call("CxbxSettings", "WriteGame", "game-opt", new Dictionary<string, string> { ["opt.video.render"] = "3", ["opt.console.video"] = "ntsc-hd", ["opt.console.language"] = "4", ["opt.console.screen"] = "widescreen",
                                                                                    ["opt.video.adapter"] = "main", ["opt.video.resolution"] = "screen", ["opt.audio.device"] = "windows" });
                Call("CxbxOptions", "Apply", exe, "game-opt", Info(4));
                var during = File.ReadAllText(ini);
                var mode = (string)Call("CxbxOptions", "MainScreenMode");
                Console.WriteLine("      main screen: " + mode);
                Check("main screen: adapter 0", during.Contains("adapter = 0"), during);
                Check("the screen's own resolution", mode != null && during.Contains("VideoResolution = " + mode), during);
                Check("the game's render factor over every game's", during.Contains("RenderResolution = 3"), during);
                Check("LLE: every game's GPU added to Cxbx-Reloaded's USB (8 + 2)", during.Contains("FlagsLLE = 10"), during);
                Check("Windows' audio device", during.Contains("adapter = 00000000 0000 0000 0000 000000000000"), during);
                Check("what nobody sets kept as it was: VSync, [gui]", during.Contains("VSync = true") && during.Contains("DataStorageToggle = 1"), during);
                var e = File.ReadAllBytes(eeprom);
                Check("console: NTSC + HD modes", BitConverter.ToUInt32(e, 0x58) == 0x00400100 && (BitConverter.ToUInt32(e, 0x94) & 0xE0000) == 0xE0000, BitConverter.ToUInt32(e, 0x58).ToString("X8") + " " + BitConverter.ToUInt32(e, 0x94).ToString("X8"));
                Check("console: French, widescreen", BitConverter.ToUInt32(e, 0x90) == 4 && (BitConverter.ToUInt32(e, 0x94) & 0x10000) != 0);
                Check("console: the game's region (PAL), checksum right", BitConverter.ToUInt32(e, 0x2C) == 4 && Signed(e));
                Check("console: the pack's HDD key over its own, checksum right", e.Skip(0x1C).Take(16).All(x => x == 0x11) && Signed(e));
                Call("CxbxOptions", "Restore", exe, "the probe's session is over");
                Check("after the session: settings.ini as it was, byte for byte", File.ReadAllBytes(ini).SequenceEqual(iniBefore));
                Check("after the session: EEPROM.bin as it was, byte for byte", File.ReadAllBytes(eeprom).SequenceEqual(eepBefore));
                Call("CxbxSettings", "WriteGame", "game-own", new Dictionary<string, string> { ["opt.video.resolution"] = "cxbx", ["opt.console.video"] = "cxbx", ["opt.video.adapter"] = "cxbx" });
                Call("CxbxSettings", "Write", new Dictionary<string, string> { ["ramdisk"] = "off" });
                Call("CxbxOptions", "Apply", exe, "game-own", Info(4));
                var own = File.ReadAllText(ini);
                e = File.ReadAllBytes(eeprom);
                Check("\"Cxbx-Reloaded's own\": nothing written - its resolution, screen and video standard kept",
                      !own.Contains("VideoResolution") && own.Contains("adapter = 1") && BitConverter.ToUInt32(e, 0x58) == BitConverter.ToUInt32(eepBefore, 0x58), own);
                Call("CxbxOptions", "Restore", exe, "the probe's session is over");
                Call("CxbxSettings", "WriteGame", "game-japan", new Dictionary<string, string> { ["opt.console.region"] = "2" });
                Call("CxbxOptions", "Apply", exe, "game-japan", Info(4));
                e = File.ReadAllBytes(eeprom);
                Check("a region chosen for the game: Japan, even for a PAL game; checksum right", BitConverter.ToUInt32(e, 0x2C) == 2 && Signed(e), "" + BitConverter.ToUInt32(e, 0x2C));
                Call("CxbxOptions", "Restore", exe, "the probe's session is over");
                Call("CxbxSettings", "WriteGame", "game-ownregion", new Dictionary<string, string> { ["opt.console.region"] = "cxbx" });
                var regionBefore = BitConverter.ToUInt32(File.ReadAllBytes(eeprom), 0x2C);
                Call("CxbxOptions", "Apply", exe, "game-ownregion", Info(regionBefore == 4 ? 1u : 4u));
                Check("region \"Cxbx-Reloaded's own\": left as it was, whatever the game's", BitConverter.ToUInt32(File.ReadAllBytes(eeprom), 0x2C) == regionBefore);
                Call("CxbxOptions", "Restore", exe, "the probe's session is over");
                Call("CxbxSettings", "WriteGame", "game-follow", new Dictionary<string, string>());
                Call("CxbxSettings", "Write", new Dictionary<string, string> { ["ramdisk"] = "off" });
                Call("CxbxOptions", "Apply", exe, "game-follow", Info(4));
                e = File.ReadAllBytes(eeprom);
                Check("follow the game: a PAL game gets PAL with 60 Hz allowed", BitConverter.ToUInt32(e, 0x58) == 0x00800300 && (BitConverter.ToUInt32(e, 0x94) & 0x00400000) != 0 && (BitConverter.ToUInt32(e, 0x94) & 0xE0000) == 0);
                Call("CxbxOptions", "Restore", exe, "the probe's session is over");

                // 12. the import: each file identified by its content, its region and title worked out
                Console.WriteLine("  import");
                var id1 = Call("CxbxPlace", "Identify", zip, null);
                Check("a zipped disc: an Xbox game, its certificate read", Get(id1, "Problem") == null && (string)Get(Get(id1, "Xbe"), "TitleIdText") == "4d530004", "" + Get(id1, "Problem"));
                Check("and its listing kept for its first launch", Call("CxbxListing", "Load", zip) != null);
                var id2 = Call("CxbxPlace", "Identify", x360, null);
                Check("an Xbox 360 disc: not an Xbox game, and why", Get(id2, "Problem") != null && ((string)Get(id2, "Problem")).Contains("360"), "" + Get(id2, "Problem"));
                var junk = Path.Combine(work, "notes.zip");
                using (var z = ZipFile.Open(junk, ZipArchiveMode.Create)) { var en = z.CreateEntry("readme.txt"); using var w = new StreamWriter(en.Open()); w.Write("hello"); }
                Check("an archive with no Xbox game in it: out, and why", Get(Call("CxbxPlace", "Identify", junk, null), "Problem") != null);
                Check("site region \"Europe: Classics\" -> Europe", (string)Call("CxbxImportFinished", "RegionOfSite", "Europe: Classics") == "Europe");
                Check("site region \"USA: Platinum Hits, Grand Theft Auto: The Trilogy\" -> North America", (string)Call("CxbxImportFinished", "RegionOfSite", "USA: Platinum Hits, Grand Theft Auto: The Trilogy") == "North America");
                Check("certificate: rest of the world -> Europe, several -> World", (string)Call("CxbxImportFinished", "RegionOf", 4u) == "Europe" && (string)Call("CxbxImportFinished", "RegionOf", 7u) == "World");

                Console.WriteLine(_bad == 0 ? "  all good" : "  " + _bad + " FAILED");
                return _bad == 0;
            }
            finally
            {
                try { Directory.Delete(work, recursive: true); } catch (Exception ex) { Console.WriteLine("  could not remove " + work + ": " + ex.Message); }
            }
        }

        public static bool Describe(Assembly asm, string file)
        {
            _asm = asm;
            var watch = System.Diagnostics.Stopwatch.StartNew();
            var d = Call("CxbxGame", "Describe", file, true);
            Console.WriteLine("  " + file + "  (" + watch.ElapsedMilliseconds + " ms)");
            foreach (var n in new[] { "Kind", "EntryKey", "EntrySize", "Bytes", "Problem" }) Console.WriteLine("    " + n + ": " + Get(d, n));
            var x = Get(d, "Xbe");
            if (x != null) Console.WriteLine("    title: " + Get(x, "TitleIdText") + " \"" + Get(x, "TitleName") + "\" region " + Get(x, "Region"));
            return Get(d, "Problem") == null;
        }

        /// <summary>--cxbx-shot &lt;out dir&gt; --emu &lt;cxbxr-ldr.exe&gt;: the settings tab and a game's options window, as
        /// PNGs - the game a zipped disc made here. Settings in the temp folder.</summary>
        public static bool Shot(Assembly asm, string outDir, string exe)
        {
            _asm = asm;
            var work = Path.Combine(Path.GetTempPath(), "lbip-cxbx-shot-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            Directory.CreateDirectory(work);
            Directory.CreateDirectory(outDir);
            try
            {
                T("CxbxSettings").GetField("DirOverride", Any).SetValue(null, Path.Combine(work, "settings"));
                T("CxbxLibrary").GetField("ExeOverride", Any).SetValue(null, exe);
                var xiso = Path.Combine(work, "Probe Game.iso");
                WriteImage(xiso, 0, Game(0x545400A4));      // GTA San Andreas' title id: the compatibility section has something to show
                var zip = Path.Combine(work, "Probe Game (USA).zip");
                using (var z = ZipFile.Open(zip, ZipArchiveMode.Create)) z.CreateEntryFromFile(xiso, "Probe Game (USA).iso");

                var page = (System.Windows.Forms.Control)T("Settings").GetMethod("CreatePage").Invoke(null, null);
                var form = new System.Windows.Forms.Form { ClientSize = new System.Drawing.Size(700, 560), Font = new System.Drawing.Font("Segoe UI", 9f), Text = "Cxbx-Reloaded" };
                page.Dock = System.Windows.Forms.DockStyle.Fill;
                form.Controls.Add(page);
                Snap(form, Path.Combine(outDir, "cxbx-settings.png"));

                var game = StubGame.With(StubGame.Create("game-1", "Probe Game", xiso), "Platform", "Microsoft Xbox");
                var gameForm = (System.Windows.Forms.Form)Activator.CreateInstance(T("CxbxGameForm"), Any, null, new object[] { new List<Unbroken.LaunchBox.Plugins.Data.IGame> { game } }, null);
                Snap(gameForm, Path.Combine(outDir, "cxbx-game.png"));
                var second = (System.Windows.Forms.Form)Activator.CreateInstance(T("CxbxGameForm"), Any, null, new object[] { new List<Unbroken.LaunchBox.Plugins.Data.IGame> { game } }, null);
                second.Shown += (_, _) => { foreach (var tc in second.Controls.OfType<System.Windows.Forms.TabControl>()) tc.SelectedIndex = 1; };
                Snap(second, Path.Combine(outDir, "cxbx-game-options.png"));
                Console.WriteLine("  shots in " + outDir);
                return true;
            }
            finally { try { Directory.Delete(work, recursive: true); } catch { } }
        }

        private static void Snap(System.Windows.Forms.Form form, string path)
        {
            form.StartPosition = System.Windows.Forms.FormStartPosition.Manual;
            form.Location = new System.Drawing.Point(-3000, 0);
            form.Show();
            for (int i = 0; i < 5; i++) { System.Windows.Forms.Application.DoEvents(); System.Threading.Thread.Sleep(80); }
            using var bmp = new System.Drawing.Bitmap(form.Width, form.Height);
            form.DrawToBitmap(bmp, new System.Drawing.Rectangle(0, 0, form.Width, form.Height));
            bmp.Save(path);
            form.Close();
        }

        /// <summary>--cxbx-prepare --emu &lt;cxbxr-ldr.exe&gt; --rom &lt;file&gt; --folder &lt;games folder&gt; [--ram]: a real launch's
        /// preparation, timed, then the XBE it hands over read back. Settings in the temp folder.</summary>
        public static bool Prepare(Assembly asm, string exe, string rom, string folder, bool ram)
        {
            _asm = asm;
            var work = Path.Combine(Path.GetTempPath(), "lbip-cxbx-prep-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            T("CxbxSettings").GetField("DirOverride", Any).SetValue(null, work);
            Call("CxbxSettings", "Write", new Dictionary<string, string> { ["folder"] = folder, ["ramdisk"] = ram ? "" : "off" });
            var m = T("CxbxPlace").GetMethod("Prepare", Any);
            for (int round = 1; round <= 2; round++)
            {
                var watch = System.Diagnostics.Stopwatch.StartNew();
                var a = new object[] { rom, "probe-game", exe, null, null };
                var xbe = (string)m.Invoke(null, a);
                Console.WriteLine("  round " + round + ": " + (xbe ?? "REFUSED: " + a[3]) + "  (" + watch.Elapsed.TotalSeconds.ToString("0.0") + " s)");
                if (xbe == null) return false;
                var x = Call("Xbe", "Read", xbe);
                Console.WriteLine("    title " + Get(x, "TitleIdText") + " \"" + Get(x, "TitleName") + "\" region " + Get(x, "Region")
                                  + ", " + Directory.GetFiles(Path.GetDirectoryName(xbe), "*", SearchOption.AllDirectories).Length + " files");
                Console.WriteLine("    title id from the file now: " + Call("CxbxGame", "TitleIdOf", rom));
                var line = (string)Call("CxbxPlugin", "CommandLineFor", "/df \"" + rom + "\"", xbe, rom);
                Console.WriteLine("    line: " + line);
            }
            try { Directory.Delete(work, true); } catch { }
            return true;
        }

        /// <summary>--cxbx-compat-snapshot &lt;out.json&gt;: the site's whole list, read with the plugin's own parser and
        /// written where the plugin embeds it from; then one game's search (TT-164, GTA San Andreas). Online.</summary>
        public static bool CompatSnapshot(Assembly asm, string output)
        {
            _asm = asm;
            var watch = System.Diagnostics.Stopwatch.StartNew();
            var list = (IList)Call("CxbxCompat", "FetchAll", TimeSpan.FromSeconds(30));
            var titles = new HashSet<string>(list.Cast<object>().Select(e => (string)Get(e, "Title")));
            Console.WriteLine("  " + list.Count + " entries, " + titles.Count + " games, " + watch.Elapsed.TotalSeconds.ToString("0") + " s");
            var bad = list.Cast<object>().Count(e => !System.Text.RegularExpressions.Regex.IsMatch((string)Get(e, "Serial") ?? "", "^[A-Z0-9]{2}-\\d{3}$"));
            Console.WriteLine("  entries whose serial is not XX-NNN: " + bad);
            foreach (var g in list.Cast<object>().GroupBy(e => (string)Get(e, "State")).OrderByDescending(g => g.Count())) Console.WriteLine("    " + g.Key + " " + g.Count());
            var write = T("CxbxCompat").GetMethod("Write", Any);
            var typed = Activator.CreateInstance(typeof(List<>).MakeGenericType(T("CxbxCompatEntry")));
            foreach (var e in list) ((IList)typed).Add(e);
            write.Invoke(null, new object[] { output, typed });
            Console.WriteLine("  written: " + output + " (" + new FileInfo(output).Length / 1024 + " KB)");
            var found = (IList)Call("CxbxCompat", "Search", "TT-164", TimeSpan.FromSeconds(20));
            Console.WriteLine("  search TT-164: " + (found == null ? "nothing" : string.Join(", ", found.Cast<object>().Select(e => Get(e, "Serial") + " " + Get(e, "Version") + " " + Get(e, "State")))));
            // A few entries are mistyped on the site itself (measured 03/10: Mortal Kombat "MW-0-1"): they never match a disc.
            return list.Count > 1000 && bad <= 5 && found != null && found.Count > 0;
        }

        /// <summary>--cxbx-gui-session --emu &lt;cxbx.exe&gt; --rom &lt;default.xbe&gt;: the GUI started on the game as a launch would
        /// start it, the plugin's session watcher on it - full screen expected - then Alt+F4 in the game: the GUI must close.</summary>
        public static bool GuiSession(Assembly asm, string exe, string xbe)
        {
            _asm = asm;
            T("CxbxSettings").GetField("DirOverride", Any).SetValue(null, Path.Combine(Path.GetTempPath(), "lbip-cxbx-gui-" + Guid.NewGuid().ToString("N").Substring(0, 8)));
            var line = (string)Call("CxbxPlugin", "CommandLineFor", "/df \"C:\\Games\\Some Game.zip\"", xbe, "C:\\Games\\Some Game.zip", true) + " \"C:\\Games\\Some Game.zip\"";
            Console.WriteLine("  cxbx.exe " + line);
            var gui = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(exe, line) { WorkingDirectory = Path.GetDirectoryName(exe), UseShellExecute = false });
            T("CxbxSession").GetMethod("Watch", Any).Invoke(null, new object[] { exe, null, true, true });
            System.Threading.Thread.Sleep(15000);
            // Alt+F4 in the game, as a player would.
            var keys = System.Diagnostics.Process.GetProcessesByName("cxbxr-ldr");
            Console.WriteLine("  loaders running: " + keys.Length + " - Alt+F4 sent to the game");
            foreach (var w in FindWindows("CxbxRender")) PostMessage(w, 0x104, (IntPtr)0x73, (IntPtr)0x20000001);
            bool closed = gui.WaitForExit(40000);
            Console.WriteLine(closed ? "  the GUI closed by itself" : "  FAIL the GUI is still open");
            if (!closed) { try { gui.CloseMainWindow(); } catch { } }
            return closed;
        }

        private static List<IntPtr> FindWindows(string cls)
        {
            var found = new List<IntPtr>();
            EnumWindows((top, _) =>
            {
                if (ClassOf(top) == cls) found.Add(top);
                EnumChildWindows(top, (c, __) => { if (ClassOf(c) == cls) found.Add(c); return true; }, IntPtr.Zero);
                return true;
            }, IntPtr.Zero);
            return found;
        }

        private static string ClassOf(IntPtr h) { var s = new StringBuilder(64); GetClassName(h, s, 64); return s.ToString(); }
        private delegate bool EnumProc(IntPtr h, IntPtr l);
        [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool EnumWindows(EnumProc p, IntPtr l);
        [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool EnumChildWindows(IntPtr w, EnumProc p, IntPtr l);
        [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)] private static extern int GetClassName(IntPtr h, StringBuilder s, int n);
        [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool PostMessage(IntPtr h, uint m, IntPtr w, IntPtr l);

        /// <summary>--cxbx-open-redirect: LaunchBox's "Open emulator" on an entry naming the loader opens the GUI beside
        /// it. Two stand-ins: cxbxr-ldr.exe is a copy of hostname.exe, cxbx.exe of whoami.exe - what they print says
        /// which one ran. The plugin constructed here installs the pack's Process.Start patch.</summary>
        public static bool OpenRedirect(Assembly asm)
        {
            _asm = asm;
            Activator.CreateInstance(T("CxbxPlugin"));
            var dir = Path.Combine(Path.GetTempPath(), "lbip-cxbx-open-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            Directory.CreateDirectory(dir);
            try
            {
                File.Copy(Path.Combine(Environment.SystemDirectory, "hostname.exe"), Path.Combine(dir, "cxbxr-ldr.exe"));
                File.Copy(Path.Combine(Environment.SystemDirectory, "whoami.exe"), Path.Combine(dir, "cxbx.exe"));
                Check("the pack's patch is in", LbIntegrations.Catalog.LbEmulatorOpened.Patched);
                var fromMenu = FakeOpenEmulatorMenuAction.OnSelect(Path.Combine(dir, "cxbxr-ldr.exe"), "");
                Check("from the menu, the loader: the GUI ran instead", fromMenu.IndexOf(Environment.UserName, StringComparison.OrdinalIgnoreCase) >= 0, fromMenu);
                var withArgs = FakeOpenEmulatorMenuAction.OnSelect(Path.Combine(dir, "cxbxr-ldr.exe"), "/?");
                Check("with arguments (a game's launch): the loader, untouched", withArgs.IndexOf(Environment.UserName, StringComparison.OrdinalIgnoreCase) < 0, withArgs);
                var gui = FakeOpenEmulatorMenuAction.OnSelect(Path.Combine(dir, "cxbx.exe"), "");
                Check("from the menu, the GUI: itself", gui.IndexOf(Environment.UserName, StringComparison.OrdinalIgnoreCase) >= 0, gui);
                return _bad == 0;
            }
            finally { try { Directory.Delete(dir, true); } catch { } }
        }

        /// <summary>Named like LaunchBox's own, which is what the patch recognises on the stack.</summary>
        private static class FakeOpenEmulatorMenuAction
        {
            [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
            public static string OnSelect(string exe, string args)
            {
                using var p = new System.Diagnostics.Process { StartInfo = new System.Diagnostics.ProcessStartInfo(exe, args) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, WorkingDirectory = Path.GetDirectoryName(exe) } };
                p.Start();
                var output = p.StandardOutput.ReadToEnd();
                p.WaitForExit();
                return output.Trim();
            }
        }

        /// <summary>--cxbx-identify &lt;file&gt;: what the import would make of a real game file - timed, the settings in the temp folder
        /// (so nothing kept is reused) - then the title and region it would get.</summary>
        public static bool Identify(Assembly asm, string file)
        {
            _asm = asm;
            T("CxbxSettings").GetField("DirOverride", Any).SetValue(null, Path.Combine(Path.GetTempPath(), "lbip-cxbx-id-" + Guid.NewGuid().ToString("N").Substring(0, 8)));
            var watch = System.Diagnostics.Stopwatch.StartNew();
            var d = Call("CxbxPlace", "Identify", file, null);
            Console.WriteLine("  " + Path.GetFileName(file) + " (" + watch.Elapsed.TotalSeconds.ToString("0.0") + " s): " + Get(d, "Kind") + (Get(d, "Problem") is string p ? " - " + p : ""));
            var x = Get(d, "Xbe");
            if (x == null) return false;
            Console.WriteLine("    " + Call("CxbxCompat", "SerialOf", Get(x, "TitleId")) + " " + Call("CxbxCompat", "VersionOf", Get(x, "Version")) + ", \"" + Get(x, "TitleName") + "\", regions " + Get(x, "Region"));
            Call("CxbxImportFinished", "Prepare", file, x);
            return true;
        }

        /// <summary>--fat: the FAT32 disk view (XisoFatView, for AIM) instead of the CD view.</summary>
        private static string ViewKind => Environment.GetCommandLineArgs().Contains("--fat") ? "XisoFatView" : "XisoCdView";

        /// <summary>--cxbx-cdview-write &lt;out.iso&gt; [--rom &lt;xbox image&gt;]: an Xbox disc's CD view (XisoCdView) written whole to a
        /// file - the disc made here unless one is given - for Windows' own ISO mounting to read: no driver of ours involved.</summary>
        public static bool CdViewWrite(Assembly asm, string output, string image)
        {
            _asm = asm;
            string work = null;
            if (image == null)
            {
                work = Path.Combine(Path.GetTempPath(), "lbip-cdview-" + Guid.NewGuid().ToString("N").Substring(0, 8));
                Directory.CreateDirectory(work);
                image = Path.Combine(work, "game.xiso");
                WriteImage(image, 0, Game(0x4D530004));
                WriteTree(Path.Combine(Path.GetDirectoryName(output), "cdview-expected"), Game(0x4D530004));
            }
            var listing = Call("Xdvdfs", "List", image);
            Console.WriteLine("  listing: " + ((IList)Get(listing, "Files")).Count + " files, partition 0x" + ((long)Get(listing, "PartitionBase")).ToString("X"));
            var view = Activator.CreateInstance(T(ViewKind), Any, null, new object[] { listing, new FileInfo(image).Length, "XBOXGAME" }, null);
            long length = (long)Get(view, "Length");
            Console.WriteLine("  view: " + length + " bytes (image " + new FileInfo(image).Length + ")");
            var read = T(ViewKind).GetMethod("Read");
            using (var img = File.OpenRead(image))
            using (var o = File.Create(output))
            {
                var buffer = new byte[1 << 20];
                for (long at = 0; at < length;)
                {
                    int n = (int)Math.Min(buffer.Length, length - at);
                    n = (int)read.Invoke(view, new object[] { img, at, buffer, n });
                    o.Write(buffer, 0, n);
                    at += n;
                }
            }
            Console.WriteLine("  written: " + output);
            if (work != null) try { Directory.Delete(work, true); } catch { }
            return true;
        }

        /// <summary>--cxbx-cdview-serve --rom &lt;xbox image&gt; --port &lt;n&gt;: the image's CD view served to ImDisk's proxy (TCP,
        /// imdisk -a -t proxy -o ip,ro,cd -f 127.0.0.1:&lt;n&gt;) until it disconnects - with what it was asked, every 10 s.</summary>
        public static bool CdViewServe(Assembly asm, string image, int port)
        {
            _asm = asm;
            var listing = Call("Xdvdfs", "List", image);
            if (Get(listing, "Error") is string err) { Console.WriteLine("  listing failed: " + err); return false; }
            if (Environment.GetCommandLineArgs().Contains("--subfolder"))
            {
                // The whole disc under \game: the XBE then never sits at a drive's root (XeImageFileName, measured 03/10).
                var dirs = (IList)Get(listing, "Dirs");
                for (int i = 0; i < dirs.Count; i++) dirs[i] = "game\\" + dirs[i];
                dirs.Insert(0, "game");
                foreach (var f in (IList)Get(listing, "Files")) f.GetType().GetField("Path").SetValue(f, "game\\" + (string)Get(f, "Path"));
                Console.WriteLine("  everything under \\game");
            }
            var view = Activator.CreateInstance(T(ViewKind), Any, null, new object[] { listing, new FileInfo(image).Length, "XBOXGAME" }, null);
            long length = (long)Get(view, "Length");
            var read = T(ViewKind).GetMethod("Read");
            Console.WriteLine("  view of " + Path.GetFileName(image) + ": " + ((IList)Get(listing, "Files")).Count + " files, " + length + " bytes - listening on 127.0.0.1:" + port);
            var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, port);
            listener.Start();
            using var socket = listener.AcceptSocket();
            listener.Stop();
            socket.NoDelay = true;
            Console.WriteLine("  connected");
            using var net = new System.Net.Sockets.NetworkStream(socket, true);
            using var reader = new BinaryReader(net);
            using var img = new FileStream(image, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16, FileOptions.RandomAccess);
            var buffer = new byte[4 << 20];
            long reads = 0, bytes = 0, ticks = 0, biggest = 0;
            var since = System.Diagnostics.Stopwatch.StartNew();
            var total = System.Diagnostics.Stopwatch.StartNew();
            while (true)
            {
                ulong code;
                try { code = reader.ReadUInt64(); } catch (EndOfStreamException) { break; } catch (IOException) { break; }
                if (code == 1)          // INFO: size, alignment, read-only
                {
                    var o = new byte[24];
                    BitConverter.GetBytes((ulong)length).CopyTo(o, 0);
                    BitConverter.GetBytes(1UL).CopyTo(o, 8);
                    BitConverter.GetBytes(1UL).CopyTo(o, 16);
                    net.Write(o, 0, o.Length);
                }
                else if (code == 2)     // READ: offset, length -> errno, length, data
                {
                    long offset = reader.ReadInt64();
                    int n = (int)reader.ReadUInt64();
                    if (n > buffer.Length) buffer = new byte[n];
                    var t = System.Diagnostics.Stopwatch.StartNew();
                    // Always the length asked for - zeros past the end: a short answer is a disk error for ImDisk.
                    int got = offset >= length ? 0 : (int)read.Invoke(view, new object[] { img, offset, buffer, (int)Math.Min(n, length - offset) });
                    if (got < n) { Array.Clear(buffer, got, n - got); got = n; }
                    ticks += t.ElapsedTicks;
                    var head = new byte[16];
                    BitConverter.GetBytes(0UL).CopyTo(head, 0);
                    BitConverter.GetBytes((ulong)got).CopyTo(head, 8);
                    net.Write(head, 0, 16);
                    if (got > 0) net.Write(buffer, 0, got);
                    reads++; bytes += got; biggest = Math.Max(biggest, n);
                }
                else if (code == 5) break;   // CLOSE
                else { Console.WriteLine("  unsupported request " + code); break; }
                if (since.Elapsed.TotalSeconds >= 10)
                {
                    Console.WriteLine("  " + total.Elapsed.TotalSeconds.ToString("0") + " s: " + reads + " reads, " + (bytes >> 20) + " MB, biggest " + (biggest >> 10) + " KB, "
                                      + (reads == 0 ? 0 : ticks * 1000.0 / System.Diagnostics.Stopwatch.Frequency / reads).ToString("0.00") + " ms/read in the view");
                    since.Restart();
                }
            }
            Console.WriteLine("  disconnected after " + total.Elapsed.TotalSeconds.ToString("0") + " s: " + reads + " reads, " + (bytes >> 20) + " MB");
            return true;
        }

        /// <summary>--cxbx-cdview-verify --rom &lt;xbox image&gt; --drive X: every file the listing names, read through the drive -
        /// its first and last 256 KB, all of it when smaller - against the image's own bytes.</summary>
        public static bool CdViewVerify(Assembly asm, string image, string drive)
        {
            _asm = asm;
            var listing = Call("Xdvdfs", "List", image);
            int ok = 0, bad = 0;
            long read = 0;
            var watch = System.Diagnostics.Stopwatch.StartNew();
            using var img = File.OpenRead(image);
            foreach (var f in (IList)Get(listing, "Files"))
            {
                var rel = (string)Get(f, "Path"); long off = (long)Get(f, "Offset"), len = (long)Get(f, "Length");
                var path = Path.Combine(drive + "\\", rel);
                try
                {
                    if (new FileInfo(path).Length != len) { bad++; Console.WriteLine("  SIZE " + rel + ": " + new FileInfo(path).Length + " vs " + len); continue; }
                    bool same = true;
                    foreach (var (at, n) in len <= 512 * 1024 ? new[] { (0L, len) } : new[] { (0L, 256 * 1024L), (len - 256 * 1024, 256 * 1024L) })
                    {
                        var a = new byte[n]; var b = new byte[n];
                        using (var s = File.OpenRead(path)) { s.Seek(at, SeekOrigin.Begin); s.ReadExactly(a); }
                        img.Seek(off + at, SeekOrigin.Begin); img.ReadExactly(b);
                        read += n;
                        if (!a.SequenceEqual(b)) same = false;
                    }
                    if (same) ok++; else { bad++; Console.WriteLine("  DIFF " + rel); }
                }
                catch (Exception ex) { bad++; Console.WriteLine("  FAIL " + rel + ": " + ex.Message); }
            }
            Console.WriteLine("  " + ok + " file(s) identical, " + bad + " different or missing - " + (read >> 20) + " MB compared in " + watch.Elapsed.TotalSeconds.ToString("0.0") + " s");
            return bad == 0;
        }

        /// <summary>--cxbx-cdview-dump --rom &lt;xbox image&gt;: the CD view's Joliet tree, read back from the view.</summary>
        public static bool CdViewDump(Assembly asm, string image)
        {
            _asm = asm;
            var listing = Call("Xdvdfs", "List", image);
            var view = Activator.CreateInstance(T(ViewKind), Any, null, new object[] { listing, new FileInfo(image).Length, "XBOXGAME" }, null);
            using var img = File.OpenRead(image);
            Console.WriteLine((string)T(ViewKind).GetMethod("Dump").Invoke(view, new object[] { img }));
            return true;
        }

        /// <summary>--cxbx-apply --emu &lt;loader&gt; --rom &lt;game&gt; / --cxbx-restore --emu &lt;loader&gt;: a launch's options written into
        /// Cxbx-Reloaded's files (every game's, the region following the game), then put back - for a launch made by hand.</summary>
        public static bool ApplyOptions(Assembly asm, string exe, string rom)
        {
            _asm = asm;
            var d = Call("CxbxGame", "Describe", rom, true);
            Call("CxbxOptions", "Apply", exe, null, Get(d, "Xbe"));
            return true;
        }

        public static bool RestoreOptions(Assembly asm, string exe)
        {
            _asm = asm;
            Call("CxbxOptions", "Restore", exe, "the probe's session is over");
            return true;
        }

        public static bool Releases(Assembly asm)
        {
            _asm = asm;
            var plugin = Activator.CreateInstance(T("CxbxPlugin"));
            var versions = (IEnumerable)T("CxbxPlugin").GetMethod("GetInstallableVersions").Invoke(plugin, null);
            if (versions == null) { Console.WriteLine("  FAIL no versions"); return false; }
            foreach (var v in versions) Console.WriteLine("  " + Get(v, "Label") + "  " + Get(v, "Identifier"));
            return true;
        }
    }
}
