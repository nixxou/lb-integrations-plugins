// --xemu: the xemu plugin, on discs MADE HERE (CxbxCheck's builder - no real game needed, none shipped): what each kind
// of file is told to be, the XISO handed over as it is, the game partition of a redump and the image in a zip cut to an
// XISO identical to the original and found again in the cache; then a game's console (the qcow2 over the base disk, a base
// forged here), xemu.toml's few lines, and the launch line.
// Writes only in the temp folder.

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Text;

namespace LbIntegrations.Probe
{
    internal static class XemuCheck
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

        private static Type T(string name) => _asm.GetType("LbIntegrations.Xemu." + name, true);
        private static object Call(string type, string method, params object[] args)
        {
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
            return t.GetProperty(member, Any)?.GetValue(o);
        }

        /// <summary>XemuDisc.Present, its two out parameters taken back.</summary>
        private static string Present(string rom, string exe, out string problem, out object info)
        {
            var m = T("XemuDisc").GetMethod("Present", Any);
            var args = new object[] { rom, exe, null, null, null };
            object r;
            try { r = m.Invoke(null, args); } catch (TargetInvocationException ex) { throw ex.InnerException ?? ex; }
            problem = (string)args[2]; info = args[3];
            return (string)r;
        }

        private static bool SameBytes(string a, string b)
        {
            using var fa = File.OpenRead(a); using var fb = File.OpenRead(b);
            if (fa.Length != fb.Length) return false;
            var ba = new byte[1 << 20]; var bb = new byte[1 << 20];
            int n;
            while ((n = fa.Read(ba, 0, ba.Length)) > 0)
            {
                int m = 0;
                while (m < n) { int k = fb.Read(bb, m, n - m); if (k <= 0) return false; m += k; }
                if (!ba.AsSpan(0, n).SequenceEqual(bb.AsSpan(0, n))) return false;
            }
            return true;
        }

        /// <summary>A stream of <paramref name="length"/> bytes that are a function of their position - a big disc without the disk.</summary>
        private sealed class Pattern : Stream
        {
            private readonly long _length; private long _at;
            public Pattern(long length) { _length = length; }
            public static byte At(long p) => (byte)((p * 2654435761L >> 13) ^ (p >> 20));
            public override bool CanRead => true; public override bool CanSeek => true; public override bool CanWrite => false;
            public override long Length => _length;
            public override long Position { get => _at; set => _at = value; }
            public override long Seek(long offset, SeekOrigin origin) => _at = origin == SeekOrigin.Begin ? offset : origin == SeekOrigin.Current ? _at + offset : _length + offset;
            public override int Read(byte[] buffer, int offset, int count)
            {
                int n = (int)Math.Max(0, Math.Min(count, _length - _at));
                for (int i = 0; i < n; i++) buffer[offset + i] = At(_at + i);
                _at += n;
                return n;
            }
            public override void Flush() { }
            public override void SetLength(long value) => throw new NotSupportedException();
            public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        }

        /// <summary>The exFAT view over <paramref name="source"/>, read back the way a file system driver would - every structure
        /// and checksum recomputed here from the specification, not from the view's code - and its one file compared with the
        /// source's bytes (all of them with <paramref name="sameAs"/>, else a few places and the end).</summary>
        private static void ExfatVolume(Stream source, long fileBase, long fileLength, string name, string sameAs)
        {
            var type = T("ExfatOneFileView");
            object view;
            try { view = Activator.CreateInstance(type, Any, null, new object[] { fileBase, fileLength, name, "XBOXDISC" }, null); }
            catch (TargetInvocationException ex) { throw ex.InnerException ?? ex; }
            var read = type.GetMethod("Read", Any);
            long length = (long)Get(view, "Length");
            byte[] At(long offset, int n)
            {
                var b = new byte[n];
                int got = (int)read.Invoke(view, new object[] { source, offset, b, n });
                if (got != n) throw new Exception("short read at " + offset + ": " + got + " of " + n);
                return b;
            }
            ulong U64(byte[] b, int o) => BitConverter.ToUInt64(b, o);
            uint U32(byte[] b, int o) => BitConverter.ToUInt32(b, o);

            var mbr = At(0, 512);
            Check("MBR signed, one exFAT partition at 1 MB", mbr[510] == 0x55 && mbr[511] == 0xAA && mbr[446 + 4] == 0x07 && U32(mbr, 446 + 8) == 2048 && mbr[462 + 4] == 0);
            long lba = U32(mbr, 446 + 8), sectors = U32(mbr, 446 + 12);
            var region = At(lba * 512, 12 * 512);
            Check("boot sector: EXFAT, 55AA, revision 1.00, 512-byte sectors, one FAT",
                  Encoding.ASCII.GetString(region, 3, 8) == "EXFAT   " && region[510] == 0x55 && region[511] == 0xAA
                  && BitConverter.ToUInt16(region, 104) == 0x0100 && region[108] == 9 && region[110] == 1 && region.Skip(11).Take(53).All(b => b == 0));
            ulong partitionOffset = U64(region, 64), volumeLength = U64(region, 72);
            uint fatOffset = U32(region, 80), fatLength = U32(region, 84), heapOffset = U32(region, 88), clusterCount = U32(region, 92), rootCluster = U32(region, 96);
            int spc = 1 << region[109], clusterBytes = 512 * spc;
            Check("partition offset and length = the MBR's", partitionOffset == (ulong)lba && volumeLength == (ulong)sectors, partitionOffset + "/" + volumeLength);
            Check("the disk ends with the volume", length == (long)(lba + (long)volumeLength) * 512, length + "");
            Check("FAT after the boot regions, heap after the FAT", fatOffset >= 24 && heapOffset >= fatOffset + fatLength && fatLength * 512L >= (clusterCount + 2L) * 4);
            Check("volume = heap + clusters", (long)volumeLength == heapOffset + (long)clusterCount * spc);
            Check("heap on a 1 MB boundary of the disk", (lba + heapOffset) * 512 % (1 << 20) == 0);
            Check("extended boot sectors signed", Enumerable.Range(1, 8).All(s => region[s * 512 + 508] == 0 && region[s * 512 + 509] == 0 && region[s * 512 + 510] == 0x55 && region[s * 512 + 511] == 0xAA));
            uint sum = 0;
            for (int i = 0; i < 11 * 512; i++) { if (i == 106 || i == 107 || i == 112) continue; sum = ((sum & 1) != 0 ? 0x80000000 : 0) + (sum >> 1) + region[i]; }
            Check("boot checksum", Enumerable.Range(0, 128).All(k => U32(region, 11 * 512 + k * 4) == sum));
            Check("backup boot region = the main one", At((lba + 12) * 512, 12 * 512).SequenceEqual(region));

            long fatAt = (lba + fatOffset) * 512, heapAt = (lba + heapOffset) * 512;
            uint Fat(uint c) => U32(At(fatAt + c * 4L, 4), 0);
            Check("FAT entries 0 and 1", Fat(0) == 0xFFFFFFF8 && Fat(1) == 0xFFFFFFFF);
            byte[] Chain(uint first, long bytes)
            {
                var list = new List<byte>();
                uint c = first;
                for (int guard = 0; c >= 2 && c < clusterCount + 2 && list.Count < bytes + clusterBytes && guard < 100000; guard++)
                {
                    list.AddRange(At(heapAt + (c - 2L) * clusterBytes, clusterBytes));
                    uint next = Fat(c);
                    if (next == 0xFFFFFFFF) break;
                    c = next;
                }
                return list.Take((int)Math.Min(bytes, list.Count)).ToArray();
            }

            var root = Chain(rootCluster, 1 << 20);
            Check("root directory read through the FAT", root.Length >= clusterBytes, root.Length + "");
            int label = -1, bitmap = -1, upcase = -1, file = -1;
            for (int i = 0; i + 32 <= root.Length && root[i] != 0; i += 32)
            {
                if (root[i] == 0x83) label = i; else if (root[i] == 0x81) bitmap = i; else if (root[i] == 0x82) upcase = i; else if (root[i] == 0x85 && file < 0) file = i;
            }
            Check("label, bitmap, up-case and file entries", label >= 0 && bitmap >= 0 && upcase >= 0 && file >= 0, label + "," + bitmap + "," + upcase + "," + file);
            if (label < 0 || bitmap < 0 || upcase < 0 || file < 0) return;
            Check("label XBOXDISC", Encoding.Unicode.GetString(root, label + 2, root[label + 1] * 2) == "XBOXDISC");

            var table = Chain(U32(root, upcase + 20), (long)U64(root, upcase + 24));
            uint tsum = 0; foreach (var b in table) tsum = ((tsum & 1) != 0 ? 0x80000000 : 0) + (tsum >> 1) + b;
            Check("up-case table checksum", tsum == U32(root, upcase + 4) && table.Length == (int)U64(root, upcase + 24));
            char Up(char c) => (int)c * 2 + 1 < table.Length ? (char)BitConverter.ToUInt16(table, c * 2) : c;
            Check("up-case: a-z to A-Z, the rest itself", Up('a') == 'A' && Up('z') == 'Z' && Up('A') == 'A' && Up('0') == '0' && Up('é') == 'é');

            var bits = Chain(U32(root, bitmap + 20), (long)U64(root, bitmap + 24));
            Check("bitmap covers every cluster", bits.Length == (int)((clusterCount + 7) / 8), bits.Length + " vs " + (clusterCount + 7) / 8);
            Check("every cluster in use", Enumerable.Range(0, (int)clusterCount).All(c => (bits[c / 8] & (1 << (c % 8))) != 0));

            int secondaries = root[file + 1];
            var set = root.Skip(file).Take(32 * (1 + secondaries)).ToArray();
            ushort ssum = 0; for (int i = 0; i < set.Length; i++) { if (i == 2 || i == 3) continue; ssum = (ushort)(((ssum & 1) != 0 ? 0x8000 : 0) + (ssum >> 1) + set[i]); }
            Check("entry set checksum", ssum == BitConverter.ToUInt16(set, 2));
            Check("read-only file", (BitConverter.ToUInt16(set, 4) & 0x11) == 0x01);
            int st = 32;
            Check("stream extension: contiguous, allocation possible", set[st] == 0xC0 && set[st + 1] == 0x03);
            int nameLength = set[st + 3];
            var sb = new StringBuilder();
            for (int e = 2; e <= secondaries && sb.Length < nameLength; e++)
                if (set[e * 32] == 0xC1) sb.Append(Encoding.Unicode.GetString(set, e * 32 + 2, 30));
            var fileName = sb.ToString().Substring(0, Math.Min(nameLength, sb.Length));
            Check("file name " + name, fileName == name, fileName);
            ushort hash = 0;
            foreach (var c in fileName.Select(Up)) { hash = (ushort)(((hash & 1) != 0 ? 0x8000 : 0) + (hash >> 1) + (c & 0xFF)); hash = (ushort)(((hash & 1) != 0 ? 0x8000 : 0) + (hash >> 1) + (c >> 8)); }
            Check("name hash, through the volume's own up-case table", hash == BitConverter.ToUInt16(set, st + 4));
            long dataLength = (long)U64(set, st + 24);
            Check("length = the game partition's", dataLength == fileLength && (long)U64(set, st + 8) == fileLength, dataLength + "");
            uint first = U32(set, st + 20);
            long fileAt = heapAt + (first - 2L) * clusterBytes;
            Check("the file's clusters inside the heap", first >= 2 && first - 2L + (dataLength + clusterBytes - 1) / clusterBytes <= clusterCount);
            Check("the file starts on a 1 MB boundary of the disk", fileAt % (1 << 20) == 0);

            if (sameAs != null)
            {
                var whole = new byte[dataLength];
                for (long p = 0; p < dataLength; p += 1 << 20) { var part = At(fileAt + p, (int)Math.Min(1 << 20, dataLength - p)); part.CopyTo(whole, p); }
                Check("the file = the XISO, byte for byte", whole.SequenceEqual(File.ReadAllBytes(sameAs)));
            }
            else
            {
                bool ok = true;
                foreach (long p in new[] { 0L, 0x10000, 1L << 32, (1L << 32) + 77, dataLength - 1_000_000 })
                {
                    var part = At(fileAt + p, 1_000_000 - (p == dataLength - 1_000_000 ? 0 : 1));
                    for (int i = 0; i < part.Length && ok; i++) if (part[i] != Pattern.At(fileBase + p + i)) ok = false;
                }
                Check("the file = the disc at 0, 64 KB, 4 GB and its end", ok);
                var tail = At(fileAt + dataLength - 3, 8);
                Check("past the file's end: zeros", tail[3] == 0 && tail[7] == 0 && tail[0] == Pattern.At(fileBase + dataLength - 3));
            }
            var again = Activator.CreateInstance(type, Any, null, new object[] { fileBase, fileLength, name, "XBOXDISC" }, null);
            var mbr2 = new byte[512]; read.Invoke(again, new object[] { source, 0L, mbr2, 512 });
            Check("a new disk signature at every view", BitConverter.ToUInt32(mbr2, 440) != BitConverter.ToUInt32(mbr, 440));
        }

        public static bool Run(Assembly asm)
        {
            _asm = asm;
            var work = Path.Combine(Path.GetTempPath(), "lbip-xemu-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            Directory.CreateDirectory(work);
            T("XemuSettings").GetField("DirOverride", Any).SetValue(null, Path.Combine(work, "settings"));
            T("XemuDisc").GetField("AttachOff", Any).SetValue(null, true);     // never a disk attached here: the copies
            Console.WriteLine("  work folder " + work);
            try
            {
                var emu = Path.Combine(work, "emu");
                Directory.CreateDirectory(emu);
                var exe = Path.Combine(emu, "xemu.exe");
                File.WriteAllBytes(exe, new byte[0]);

                // 1. an XISO: as it is
                var xiso = Path.Combine(work, "Probe Game (USA).xiso");
                CxbxCheck.WriteImage(xiso, 0, CxbxCheck.Game(0x4D530004));
                Console.WriteLine("  XISO");
                var d = Call("XemuDisc", "Describe", xiso);
                Check("is an XISO", Get(d, "Kind")?.ToString() == "Xiso", Get(d, "Kind") + " / " + Get(d, "Problem"));
                Check("title id 4d530004", (string)Get(Get(d, "Xbe"), "TitleIdText") == "4d530004", "" + Get(Get(d, "Xbe"), "TitleIdText"));
                var p = Present(xiso, exe, out var problem, out _);
                Check("handed over as it is", p == xiso, p + " / " + problem);
                Check("nothing copied", !Directory.Exists(Path.Combine(emu, "discs")) || !Directory.GetFiles(Path.Combine(emu, "discs")).Any());

                // 2. a redump: the game partition cut out, identical to the XISO
                var redump = Path.Combine(work, "Probe Game (Europe).iso");
                CxbxCheck.WriteImage(redump, 0x18300000, CxbxCheck.Game(0x4D530004));
                Console.WriteLine("  redump");
                d = Call("XemuDisc", "Describe", redump);
                Check("is a redump", Get(d, "Kind")?.ToString() == "Redump", Get(d, "Kind") + " / " + Get(d, "Problem"));
                Check("its partition at 0x18300000", (long)Get(d, "PartitionBase") == 0x18300000, "" + Get(d, "PartitionBase"));
                p = Present(redump, exe, out problem, out _);
                Check("cut into the cache", p != null && p.StartsWith(Path.Combine(emu, "discs")) && File.Exists(p), p + " / " + problem);
                Check("the cut = the XISO, byte for byte", p != null && SameBytes(p, xiso));
                Check("no .part left", !Directory.GetFiles(Path.Combine(emu, "discs"), "*.part").Any());
                var again = Present(redump, exe, out problem, out _);
                Check("the second launch finds it again", again == p, again + " / " + problem);
                var cutTitle = (string)Call("XemuDisc", "TitleIdOf", redump, exe);
                Check("title id of the redump", cutTitle == "4d530004", cutTitle);

                // 2b. the same redump as one file on an exFAT volume, read by a reader written from the specification
                Console.WriteLine("  exFAT view of the redump");
                using (var src = File.OpenRead(redump))
                    ExfatVolume(src, 0x18300000, src.Length - 0x18300000, "Probe Game (Europe).iso", xiso);
                Console.WriteLine("  exFAT view of a 7.5 GB disc (made up)");
                using (var big = new Pattern(0x18300000 + 7_500_000_123L))
                    ExfatVolume(big, 0x18300000, 7_500_000_123L, "game.iso", null);

                // 3. the XISO in a zip: unpacked, then handed over
                var zip = Path.Combine(work, "Probe Game (Japan).zip");
                using (var z = ZipFile.Open(zip, ZipArchiveMode.Create)) z.CreateEntryFromFile(xiso, "Probe Game (Japan).iso", CompressionLevel.Fastest);
                Console.WriteLine("  XISO in a zip");
                d = Call("XemuDisc", "Describe", zip);
                Check("is an image in an archive", Get(d, "Kind")?.ToString() == "ImageInArchive", Get(d, "Kind") + " / " + Get(d, "Problem"));
                p = Present(zip, exe, out problem, out _);
                Check("unpacked into the cache = the XISO", p != null && SameBytes(p, xiso), p + " / " + problem);
                Check("no .unpacked left", !Directory.GetFiles(Path.Combine(emu, "discs"), "*.unpacked").Any());

                // 3b. a redump in a zip: unpacked, then cut
                var zipR = Path.Combine(work, "Probe Game (Europe).zip");
                using (var z = ZipFile.Open(zipR, ZipArchiveMode.Create)) z.CreateEntryFromFile(redump, "Probe Game (Europe).iso", CompressionLevel.Fastest);
                Console.WriteLine("  redump in a zip");
                p = Present(zipR, exe, out problem, out _);
                Check("unpacked and cut = the XISO", p != null && SameBytes(p, xiso), p + " / " + problem);
                Check("no .unpacked left", !Directory.GetFiles(Path.Combine(emu, "discs"), "*.unpacked").Any());

                // 4. refused: a game unpacked in a zip, a 360 game, something else
                Console.WriteLine("  refused");
                var tree = Path.Combine(work, "tree.zip");
                using (var z = ZipFile.Open(tree, ZipArchiveMode.Create))
                {
                    using (var s = z.CreateEntry("Game/default.xbe").Open()) s.Write(CxbxCheck.XbeBytes(0x4D530004, "Tree"));
                    using (var s = z.CreateEntry("Game/media/a.bin").Open()) s.Write(new byte[10]);
                }
                d = Call("XemuDisc", "Describe", tree);
                Check("a game in folders: TreeInArchive, refused", Get(d, "Kind")?.ToString() == "TreeInArchive" && Get(d, "Problem") != null, Get(d, "Kind") + " / " + Get(d, "Problem"));
                var x360 = Path.Combine(work, "x360.zip");
                using (var z = ZipFile.Open(x360, ZipArchiveMode.Create))
                    using (var s = z.CreateEntry("default.xex").Open()) s.Write(new byte[16]);
                d = Call("XemuDisc", "Describe", x360);
                Check("a 360 game: Xbox360, refused", Get(d, "Kind")?.ToString() == "Xbox360" && Get(d, "Problem") != null, Get(d, "Kind") + " / " + Get(d, "Problem"));
                var junk = Path.Combine(work, "junk.iso");
                File.WriteAllBytes(junk, CxbxCheck.Noise(200000, 9));
                d = Call("XemuDisc", "Describe", junk);
                Check("noise: refused", Get(d, "Problem") != null, "" + Get(d, "Kind"));
                p = Present(junk, exe, out problem, out _);
                Check("noise: no path, a reason", p == null && problem != null, p);
                d = Call("XemuDisc", "Describe", Path.Combine(work, "missing.iso"));
                Check("a missing file: refused", Get(d, "Problem") != null);

                // 5. a game's console: the overlay over a base forged here
                Console.WriteLine("  qcow2 overlay");
                var hdd = Path.Combine(emu, "hdd");
                Directory.CreateDirectory(hdd);
                var baseDisk = Path.Combine(hdd, "base.qcow2");
                var head = new byte[112];
                void U32(int at, uint v) { head[at] = (byte)(v >> 24); head[at + 1] = (byte)(v >> 16); head[at + 2] = (byte)(v >> 8); head[at + 3] = (byte)v; }
                U32(0, 0x514649FB); U32(4, 3); U32(20, 16);
                U32(24, 0x00000002); U32(28, 0x00000000);          // 8 GiB, big-endian 64-bit
                File.WriteAllBytes(baseDisk, head);
                Check("the base's size read: 8 GiB", (long)Call("Qcow2Overlay", "VirtualSize", baseDisk) == 8L << 30, "" + Call("Qcow2Overlay", "VirtualSize", baseDisk));
                var game = Path.Combine(hdd, "games", "4d530004.qcow2");
                var why = (string)Call("Qcow2Overlay", "Create", baseDisk, game);
                Check("made", why == null && File.Exists(game), why);
                Check("4 clusters of 64 KB", File.Exists(game) && new FileInfo(game).Length == 4 * 65536, File.Exists(game) ? "" + new FileInfo(game).Length : "none");
                Check("same size as the base", (long)Call("Qcow2Overlay", "VirtualSize", game) == 8L << 30, "" + Call("Qcow2Overlay", "VirtualSize", game));
                Check("backing ../base.qcow2", (string)Call("Qcow2Overlay", "BackingName", game) == "../base.qcow2", (string)Call("Qcow2Overlay", "BackingName", game));
                Check("never over an existing one", Call("Qcow2Overlay", "Create", baseDisk, game) != null);
                Check("not over a file that is not a qcow2", Call("Qcow2Overlay", "Create", junk, Path.Combine(hdd, "games", "x.qcow2")) != null);
                Check("no temporary file left", !Directory.GetFiles(Path.Combine(hdd, "games"), "*.lbip-tmp").Any());
                Check("the base untouched", File.ReadAllBytes(baseDisk).SequenceEqual(head));
                if (File.Exists(game))
                {
                    var g = File.ReadAllBytes(game);
                    Check("L1 table empty", g.Skip(3 * 65536).All(b => b == 0));
                    Check("refcount table -> block 2", g[65536 + 7] == 0 && g[65536 + 5] == 2 && g.Skip(65536).Take(5).All(b => b == 0));
                    Check("clusters 0-3 counted once", Enumerable.Range(0, 4).All(c => g[2 * 65536 + c * 2] == 0 && g[2 * 65536 + c * 2 + 1] == 1) && g[2 * 65536 + 9] == 0);
                }

                // 6. xemu.toml
                Console.WriteLine("  xemu.toml");
                var toml = Path.Combine(emu, "xemu.toml");
                Check("a new table", (bool)Call("XemuToml", "Set", toml, "general", "show_welcome", "false"));
                Call("XemuToml", "Set", toml, "sys.files", "hdd_path", Call("XemuToml", "Literal", @"C:\a b\hdd.qcow2"));
                Check("a second table appended", File.ReadAllText(toml) == "[general]\nshow_welcome = false\n\n[sys.files]\nhdd_path = 'C:\\a b\\hdd.qcow2'\n", File.ReadAllText(toml));
                Check("a key into the first table, before the blank line", (bool)Call("XemuToml", "Set", toml, "general", "skip_boot_anim", "true")
                      && File.ReadAllText(toml).StartsWith("[general]\nshow_welcome = false\nskip_boot_anim = true\n\n[sys.files]"), File.ReadAllText(toml));
                Check("the same value: no write", !(bool)Call("XemuToml", "Set", toml, "general", "skip_boot_anim", "true"));
                Check("only if absent: the user's value stays", !(bool)Call("XemuToml", "Set", toml, "general", "show_welcome", "true", true)
                      && (string)Call("XemuToml", "Get", toml, "general", "show_welcome") == "false");
                Call("XemuToml", "Set", toml, "sys.files", "hdd_path", Call("XemuToml", "Literal", @"D:\x.qcow2"));
                Check("a value replaced, read back", (string)Call("XemuToml", "Text", Call("XemuToml", "Get", toml, "sys.files", "hdd_path")) == @"D:\x.qcow2");
                Check("[general.updates] not mistaken for [general]", Call("XemuToml", "Get", toml, "general.updates", "check") == null);
                bool threw = false;
                try { Call("XemuToml", "Literal", @"C:\it's\x"); } catch (ArgumentException) { threw = true; }
                Check("a path with a ' refused", threw);
                Check("LF only", !File.ReadAllText(toml).Contains("\r"));
                File.WriteAllText(toml, "# xemu's\r\n[general]\r\nshow_welcome = true\r\n[display]\r\nrenderer = 'VULKAN'\r\n");
                Call("XemuToml", "Set", toml, "general", "show_welcome", "false");
                Check("xemu's own file: the rest kept", File.ReadAllText(toml) == "# xemu's\n[general]\nshow_welcome = false\n[display]\nrenderer = 'VULKAN'\n", File.ReadAllText(toml));

                // 7. the launch line
                Console.WriteLine("  launch line");
                string Line(string current, string rom = null) => (string)Call("XemuPlugin", "CommandLineFor", current, @"C:\d\g.iso", rom);
                Check("the default", Line("-full-screen -dvd_path") == "-full-screen -dvd_path \"C:\\d\\g.iso\" -L", Line("-full-screen -dvd_path"));
                Check("empty: full screen added", Line("") == "-full-screen -dvd_path \"C:\\d\\g.iso\" -L", Line(""));
                Check("options kept with their values", Line("-machine xbox,short-animation=on -dvd_path") == "-full-screen -machine xbox,short-animation=on -dvd_path \"C:\\d\\g.iso\" -L", Line("-machine xbox,short-animation=on -dvd_path"));
                Check("an old -dvd_path value dropped", Line("-full-screen -dvd_path \"E:\\old game.iso\"") == "-full-screen -dvd_path \"C:\\d\\g.iso\" -L", Line("-full-screen -dvd_path \"E:\\old game.iso\""));
                Check("the game's own path dropped", Line("-full-screen \"G:\\Xbox\\My Game.iso\"", @"G:\Xbox\My Game.iso") == "-full-screen -dvd_path \"C:\\d\\g.iso\" -L", Line("-full-screen \"G:\\Xbox\\My Game.iso\"", @"G:\Xbox\My Game.iso"));
                Check("an -L already there not doubled", Line("-full-screen -dvd_path -L") == "-full-screen -dvd_path \"C:\\d\\g.iso\" -L", Line("-full-screen -dvd_path -L"));
                Check("a valued option with spaces requoted", Line("-config_path \"C:\\my cfg\\x.toml\"") == "-full-screen -config_path \"C:\\my cfg\\x.toml\" -dvd_path \"C:\\d\\g.iso\" -L", Line("-config_path \"C:\\my cfg\\x.toml\""));

                // 8. whose install it is
                Console.WriteLine("  ours");
                Check("an xemu without the marker is not ours", !(bool)Call("XemuPaths", "IsOurs", exe));
            }
            catch (Exception ex) { Check("no exception", false, ex.ToString()); }
            finally { try { Directory.Delete(work, true); } catch { } }

            Console.WriteLine(_bad == 0 ? "  all good" : "  " + _bad + " FAILED");
            return _bad == 0;
        }
    }
}
