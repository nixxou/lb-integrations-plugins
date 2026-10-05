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
            var args = new object[] { rom, exe, null, null, null, null };
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
            try { view = Activator.CreateInstance(type, Any, null, new object[] { fileBase, fileLength, name, "XBOXDISC", null }, null); }
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
            var again = Activator.CreateInstance(type, Any, null, new object[] { fileBase, fileLength, name, "XBOXDISC", null }, null);
            var mbr2 = new byte[512]; read.Invoke(again, new object[] { source, 0L, mbr2, 512 });
            Check("a new disk signature at every view", BitConverter.ToUInt32(mbr2, 440) != BitConverter.ToUInt32(mbr, 440));
        }

        /// <summary>The console's EEPROM: XboxEepromEditor's crypto both ways, a new console, and a session's settings - region and
        /// video following the game, language, time zone - on a copy, the base never written. With <paramref name="real"/> (an
        /// EEPROM xemu made), opening and sealing it unchanged must give back its very bytes.</summary>
        /// <summary>The flash BIOS of a session with the certificate key at zero (Eeprom\XboxBldr), made from the user's
        /// mcpx_1.0.bin and flash BIOS in <paramref name="biosDir"/>: RC4 is a stream, so the 16 bytes of the key are the only ones
        /// that change in each 256 KB image - nothing else. No key is printed.</summary>
        private static void CertKey(string work, string biosDir)
        {
            // a[3]: "retail" - the BIOS as it is (null), "zero" - Cxbx-Reloaded's key.
            object B(params object[] a)
            {
                a[3] = (string)a[3] == "zero" ? new byte[16] : null;
                try { return _asm.GetType("LbIntegrations.Xbox.XboxKeys", true).GetMethod("SessionFlash", Any).Invoke(null, a); }
                catch (TargetInvocationException ex) { throw ex.InnerException ?? ex; }
            }
            object KeyOf(string m, string f) { try { return _asm.GetType("LbIntegrations.Xbox.XboxKeys", true).GetMethod("CertificateKeyOf", Any).Invoke(null, new object[] { m, f }); } catch (TargetInvocationException ex) { throw ex.InnerException ?? ex; } }
            object Flash(params object[] a) { try { return _asm.GetType("LbIntegrations.Xbox.XboxKeys", true).GetMethod("SessionFlash", Any).Invoke(null, a); } catch (TargetInvocationException ex) { throw ex.InnerException ?? ex; } }
            var said = new List<string>();
            var fake = Path.Combine(work, "flash-fake.bin"); File.WriteAllBytes(fake, new byte[0x100000]);
            var mcpxFake = Path.Combine(work, "mcpx-fake.bin"); File.WriteAllBytes(mcpxFake, new byte[512]);
            var outPath = Path.Combine(work, "flash-session.bin");
            Check("retail: the BIOS as it is", (string)B(mcpxFake, fake, outPath, "retail", said) == fake && !File.Exists(outPath));
            Check("zero on a BIOS that does not open: none made, said", B(mcpxFake, fake, outPath, "zero", said) == null && !File.Exists(outPath) && said.Any(s => s.Contains("NOT set")));
            if (string.IsNullOrWhiteSpace(biosDir) || !File.Exists(Path.Combine(biosDir, "mcpx_1.0.bin"))) { Console.WriteLine("    (no LBIP_XEMU_BIOS: the real BIOS not checked)"); return; }
            var mcpx = Path.Combine(biosDir, "mcpx_1.0.bin");
            foreach (var flash in Directory.GetFiles(biosDir, "*.bin").Where(f => !Path.GetFileName(f).Equals("mcpx_1.0.bin", StringComparison.OrdinalIgnoreCase)))
            {
                said.Clear();
                var before = File.ReadAllBytes(flash);
                var made = (string)B(mcpx, flash, outPath, "zero", said);
                Check(Path.GetFileName(flash) + ": a session copy made", made == outPath && File.Exists(outPath), string.Join("; ", said));
                if (made == null) continue;
                var after = File.ReadAllBytes(outPath);
                var changed = Enumerable.Range(0, before.Length).Where(i => before[i] != after[i]).ToList();
                int images = before.Length / 0x40000;
                Check("... the same size, the user's file untouched", after.Length == before.Length && File.ReadAllBytes(flash).SequenceEqual(before));
                Check("... at most 16 bytes changed per image, each inside its boot loader", changed.Count > 0 && changed.Count <= 16 * images
                      && changed.All(i => i % 0x40000 >= 0x40000 - 0x6200 && i % 0x40000 < 0x40000 - 0x200)
                      && changed.GroupBy(i => i / 0x40000).All(g => g.Max() - g.Min() < 16), changed.Count + " byte(s)");
                said.Clear();
                Check("... zero asked of the copy: already its own - the copy itself, nothing made", (string)B(mcpx, outPath, Path.Combine(work, "flash-session2.bin"), "zero", said) == outPath
                      && !File.Exists(Path.Combine(work, "flash-session2.bin")));
                var own = KeyOf(mcpx, flash) as byte[];
                Check("... its own key read, and the copy's is zero", own != null && own.Length == 16 && own.Any(x => x != 0)
                      && KeyOf(mcpx, outPath) is byte[] z && z.All(x => x == 0));
                said.Clear();
                var back = Flash(mcpx, outPath, Path.Combine(work, "flash-session3.bin"), own, said) as string;
                Check("... its own key put back into the copy: the BIOS byte for byte", back != null && File.ReadAllBytes(back).SequenceEqual(before));
                Check("... asked for its own key: the BIOS itself, no copy", (string)Flash(mcpx, flash, Path.Combine(work, "flash-session4.bin"), own, said) == flash
                      && !File.Exists(Path.Combine(work, "flash-session4.bin")));
                foreach (var f in new[] { outPath, Path.Combine(work, "flash-session3.bin") }) { try { File.Delete(f); } catch { } }
            }
        }

        /// <summary>Your console's seed (PackIdentity): the same seed, however typed, gives the same Xbox; another, another one.
        /// And the hard-coded retail certificate key is the user's BIOS's (LBIP_XEMU_BIOS), when one is given.</summary>
        private static void SeedCheck(string biosDir)
        {
            var id = _asm.GetType("LbIntegrations.Identity.PackIdentity", true);
            object Xbox(string seed) { var p = Activator.CreateInstance(id, true); id.GetField("Seed").SetValue(p, seed); return id.GetMethod("Xbox").Invoke(p, null); }
            object F(object x, string f) => x?.GetType().GetField(f, Any)?.GetValue(x);
            string Sig(object x) => x == null ? null : F(x, "Serial") + "|" + Convert.ToHexString((byte[])F(x, "Mac")) + "|" + Convert.ToHexString((byte[])F(x, "HddKey")) + "|" + Convert.ToHexString((byte[])F(x, "OnlineKey"));
            var a = Xbox("Call me Ishmael");
            Check("no seed: no console values", Xbox("") == null && Xbox("   ") == null);
            Check("the same seed, however typed (case, spaces): the same Xbox", Sig(a) == Sig(Xbox("  call   ME ishmael ")));
            Check("another seed: another Xbox", Sig(a) != Sig(Xbox("Call me Ahab")));
            Check("... a serial of 12 digits, a MAC of Microsoft's, an HDD key not null, keys apart",
                  ((string)F(a, "Serial")).Length == 12 && ((string)F(a, "Serial")).All(char.IsDigit)
                  && ((byte[])F(a, "Mac")).Take(3).SequenceEqual(new byte[] { 0x00, 0x50, 0xF2 })
                  && ((byte[])F(a, "HddKey")).Any(x => x != 0) && !((byte[])F(a, "HddKey")).SequenceEqual((byte[])F(a, "OnlineKey")));
            Console.WriteLine("    (\"Call me Ishmael\": serial " + F(a, "Serial") + ")");
            bool RealForm(string sn) => sn.Length == 12 && sn[7] >= '1' && sn[7] <= '5' && int.Parse(sn.Substring(8, 2)) is int w && w >= 1 && w <= 52
                                        && new[] { "02", "03", "05", "06" }.Contains(sn.Substring(10, 2)) && (sn[7] != '1' || w >= 46);
            var many = Enumerable.Range(0, 300).Select(i => (string)F(Xbox("seed " + i), "Serial")).ToList();
            Check("... serials of a real Xbox's form, L NNNNNN Y WW FF (300 seeds)", many.All(RealForm), many.FirstOrDefault(x => !RealForm(x)));
            Check("... every factory and year seen, no two the same", many.Select(x => x.Substring(10, 2)).Distinct().Count() == 4 && many.Select(x => x[7]).Distinct().Count() == 5 && many.Distinct().Count() == many.Count);
            var keys = _asm.GetType("LbIntegrations.Xbox.XboxKeys", true);
            var retail = (byte[])keys.GetProperty("Retail", Any).GetValue(null);
            Check("the retail certificate key: 16 bytes, not zero", retail.Length == 16 && retail.Any(x => x != 0));
            if (!string.IsNullOrWhiteSpace(biosDir) && File.Exists(Path.Combine(biosDir, "mcpx_1.0.bin")))
                foreach (var flash in Directory.GetFiles(biosDir, "*.bin").Where(f => !Path.GetFileName(f).Equals("mcpx_1.0.bin", StringComparison.OrdinalIgnoreCase)))
                    Check("... the one in " + Path.GetFileName(flash), keys.GetMethod("CertificateKeyOf", Any).Invoke(null, new object[] { Path.Combine(biosDir, "mcpx_1.0.bin"), flash }) is byte[] own && own.SequenceEqual(retail));
        }

        /// <summary>--xemu-states-cycle &lt;an xemu folder COPY&gt; --title id: its game's snapshots exported as state files, one removed
        /// as LaunchBox's Remove does, put back as its Restore does - each step checked on the console.</summary>
        public static bool StatesCycle(Assembly asm, string emuDir, string titleId)
        {
            _asm = asm;
            var exe = Path.Combine(emuDir, "x.emu.exe");
            var states = T("Saves.XemuStates");
            object M(string m, params object[] a) { try { return states.GetMethod(m, Any).Invoke(null, a); } catch (TargetInvocationException ex) { throw ex.InnerException ?? ex; } }
            List<string> Mirror(bool rewrite) { var said = ((IEnumerable<string>)M("Mirror", exe, titleId, rewrite, (Func<string>)(() => "test-keys"))).ToList(); foreach (var s in said) Console.WriteLine("    > " + s); return said; }
            var console = Path.Combine(emuDir, "hdd", "games", titleId + ".qcow2");
            var img = T("Saves.Qcow2Image");
            List<string> Snaps()
            {
                using var q = (IDisposable)img.GetMethod("Open", Any).Invoke(null, new object[] { console });
                return ((System.Collections.IEnumerable)q.GetType().GetProperty("Snapshots").GetValue(q)).Cast<object>()
                       .Select(s => (string)s.GetType().GetField("Name").GetValue(s)).ToList();
            }
            var dir = (string)M("Dir", exe, titleId);
            var first = Snaps();
            Console.WriteLine("  console: " + first.Count + " snapshot(s): " + string.Join(", ", first));
            var w = System.Diagnostics.Stopwatch.StartNew();
            Mirror(false);
            var files = Directory.GetFiles(dir, "*.xemustate").OrderBy(f => f).ToList();
            Console.WriteLine("  exported in " + w.Elapsed.TotalSeconds.ToString("0.0") + " s: " + string.Join(", ", files.Select(f => Path.GetFileName(f) + " " + (new FileInfo(f).Length >> 20) + " MB")));
            Check("a listing: one state file per snapshot", files.Count == first.Count);
            Check("... again: nothing to do", Mirror(false).Count == 0);
            if (files.Count < 2) return _bad == 0;
            var keep = Path.Combine(emuDir, "kept-" + Path.GetFileName(files[1]));
            File.Copy(files[1], keep, overwrite: true);
            File.Delete(files[1]);                                       // LaunchBox's Remove
            Check("a file removed, a listing: the console untouched", Mirror(false).Any(s => s.Contains("wait")) && Snaps().Count == first.Count);
            Mirror(true);
            Check("... at a launch: its snapshot taken out", Snaps().Count == first.Count - 1 && !Snaps().Contains(first[1]), string.Join(", ", Snaps()));
            File.Copy(keep, files[1], overwrite: true);                  // LaunchBox's Restore
            Mirror(true);
            var after = Snaps();
            Check("the file restored: its snapshot back in the console", after.Count == first.Count && after.Contains(first[1]), string.Join(", ", after));
            Check("... and nothing more to do", Mirror(false).Count == 0 && Directory.GetFiles(dir, "*.xemustate").Length == first.Count);
            // Savestates turned off, then on (XemuSaveFiles.States forgets the index): a file deleted meanwhile is exported again,
            // nothing is taken out of the console, nothing exported twice.
            {
                var indexPath = (string)M("IndexPath", exe, titleId);
                var gone = Directory.GetFiles(dir, "*.xemustate").OrderBy(f => f).First();
                File.Delete(indexPath);
                File.Delete(gone);
                var again = Mirror(false);
                Check("off then on: the file deleted meanwhile exported again, the others kept", again.Count == 1 && again[0].Contains("exported")
                      && Directory.GetFiles(dir, "*.xemustate").Length == first.Count, string.Join("; ", again));
                Mirror(true);
                Check("... at a launch: the console untouched", Snaps().Count == first.Count && Mirror(false).Count == 0);
            }
            // The console deleted by hand, its index left beside it: a new console gets the files, none removed.
            var savedConsole = console + ".kept";
            File.Copy(console, savedConsole, overwrite: true);
            File.Delete(console);
            M("ForgetWithoutConsole", exe, titleId);
            var baseDisk = Path.Combine(emuDir, "hdd", "base.qcow2");
            _asm.GetType("LbIntegrations.Xemu.Qcow2Overlay", true).GetMethod("Create", Any).Invoke(null, new object[] { baseDisk, console });
            Mirror(true);
            var reborn = Snaps();
            Check("the console deleted: a new one gets every state file back, none removed", reborn.Count == first.Count && Directory.GetFiles(dir, "*.xemustate").Length == first.Count, string.Join(", ", reborn));
            File.Copy(savedConsole, console, overwrite: true); File.Delete(savedConsole);
            SlotsByName(emuDir, titleId, exe, dir, console, Mirror, Snaps, M);
            return _bad == 0;
        }

        /// <summary>--xemu-compat [rom...]: xemu's compatibility list fetched for real into a scratch folder (XemuCompat), what is
        /// kept checked, broken answers refused; then for each disc its title, its state in xemu's list and Cxbx-Reloaded's
        /// entry (the copy embedded in Xemu.dll).</summary>
        public static bool Compat(Assembly asm, IEnumerable<string> roms)
        {
            _asm = asm;
            var data = Path.Combine(Path.GetTempPath(), "lbip-xemu-compat-" + Guid.NewGuid().ToString("N"));
            var xemuId = "c54b75ab-94aa-4a1e-b36c-b6af7115c51c";
            T("XemuSettings").GetField("DirOverride", Any).SetValue(null, Path.Combine(data, xemuId));
            _asm.GetType("LbIntegrations.Xbox.XboxCompat", true).GetField("DataOverride", Any).SetValue(null, data);
            var compat = T("XemuCompat");
            var shared = _asm.GetType("LbIntegrations.Xbox.XboxCompat", true);
            object C(string m, params object[] a) { try { return compat.GetMethod(m, Any).Invoke(null, a); } catch (TargetInvocationException ex) { throw ex.InnerException ?? ex; } }
            try
            {
                Check("broken answers refused", C("Problem", "[]", true, 0) != null && C("Problem", "{\"a\":1}", false, 0) != null && C("Problem", "not json", true, 0) != null);
                var w = System.Diagnostics.Stopwatch.StartNew();
                Check("the list fetched", (bool)C("Fetch", TimeSpan.FromSeconds(90)));
                Console.WriteLine("  in " + w.Elapsed.TotalSeconds.ToString("0.0") + " s: " + string.Join(", ", Directory.GetFiles(Path.Combine(data, xemuId)).Select(f => Path.GetFileName(f) + " " + (new FileInfo(f).Length >> 10) + " KB")));
                Check("... fresh, and asked again only a day later", (bool)C("IsFresh"));
                var reports = File.ReadAllText(Path.Combine(data, xemuId, "xemu-compat-reports.json"));
                Check("a list cut by two thirds refused against the copy", C("Problem", reports.Substring(0, reports.IndexOf("}, {", reports.Length / 3) + 1) + "]", true, 1104) != null);
                foreach (var rom in roms)
                {
                    var d = T("XemuDisc").GetMethod("Describe", Any).Invoke(null, new object[] { rom });
                    var xbe = d.GetType().GetField("Xbe").GetValue(d);
                    if (xbe == null) { Console.WriteLine("  " + Path.GetFileName(rom) + ": no XBE read"); continue; }
                    var id = (string)xbe.GetType().GetProperty("TitleIdText").GetValue(xbe);
                    var pair = shared.GetMethod("Xemu", Any).Invoke(null, new object[] { id });
                    var report = pair.GetType().GetField("Item1").GetValue(pair);
                    Console.WriteLine("  " + Path.GetFileName(rom) + " (" + id + "): " + C("Describe", id));
                    var cx = shared.GetMethod("Cxbx", Any).Invoke(null, new object[] { xbe.GetType().GetField("TitleId").GetValue(xbe), xbe.GetType().GetField("Version").GetValue(xbe), false });
                    Console.WriteLine("    Cxbx-Reloaded: " + (cx == null ? "not in its list" : cx.GetType().GetProperty("State").GetValue(cx) + " (" + cx.GetType().GetProperty("Serial").GetValue(cx) + " " + cx.GetType().GetProperty("Version").GetValue(cx) + ")"));
                }
            }
            finally { try { Directory.Delete(data, true); } catch { } }
            return _bad == 0;
        }

        /// <summary>A snapshot's name gives its slot (05/10): what xemu would do - a name saved over (Shift+F#), a name deleted then
        /// made again, a new name - played without xemu: a state file altered (its date, its name) and put into the console through
        /// a scratch copy of it, as if xemu had made the snapshot.</summary>
        private static void SlotsByName(string emuDir, string titleId, string exe, string dir, string console,
                                        Func<bool, List<string>> Mirror, Func<List<string>> Snaps, Func<string, object[], object> M)
        {
            Console.WriteLine("  slots by name");
            object Field(object o, string n) => o.GetType().GetField(n, Any).GetValue(o);
            object ReadFile(string p) => M("Read", new object[] { p });
            string SlotFile(int n) => (string)M("SlotPath", new object[] { exe, titleId, n });
            uint U32(byte[] b, long at) => (uint)(b[at] << 24 | b[at + 1] << 16 | b[at + 2] << 8 | b[at + 3]);
            void Put32(byte[] b, long at, uint v) { b[at] = (byte)(v >> 24); b[at + 1] = (byte)(v >> 16); b[at + 2] = (byte)(v >> 8); b[at + 3] = (byte)v; }

            // A state file again, its snapshot's date moved on and its name changed (same length) - in its table and its note.
            string Variant(string src, string name, uint later, string target)
            {
                var tmp = Path.Combine(Path.GetTempPath(), "lbip-variant-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(tmp);
                try
                {
                    System.IO.Compression.ZipFile.ExtractToDirectory(src, tmp);
                    var q = File.ReadAllBytes(Path.Combine(tmp, "state.qcow2"));
                    long table = (long)U32(q, 64) << 32 | U32(q, 68);
                    int extra = (int)U32(q, table + 36), idLen = q[table + 12] << 8 | q[table + 13], nameLen = q[table + 14] << 8 | q[table + 15];
                    uint date = U32(q, table + 16) + later;
                    Put32(q, table + 16, date);
                    var oldName = Encoding.UTF8.GetString(q, (int)(table + 40 + extra + idLen), nameLen);
                    var newName = name ?? oldName;
                    if (Encoding.UTF8.GetByteCount(newName) != nameLen) throw new ArgumentException("same length only");
                    Encoding.UTF8.GetBytes(newName).CopyTo(q, table + 40 + extra + idLen);
                    File.WriteAllBytes(Path.Combine(tmp, "state.qcow2"), q);
                    var meta = File.ReadAllLines(Path.Combine(tmp, "lbip-state.txt"))
                                   .Select(l => l.StartsWith("date_sec=") ? "date_sec=" + date : l.StartsWith("name=") ? "name=" + newName : l);
                    File.WriteAllText(Path.Combine(tmp, "lbip-state.txt"), string.Join("\n", meta) + "\n");
                    if (File.Exists(target)) File.Delete(target);
                    System.IO.Compression.ZipFile.CreateFromDirectory(tmp, target);
                    return target;
                }
                finally { try { Directory.Delete(tmp, true); } catch { } }
            }

            // As if xemu had made it: the snapshot into a scratch copy of the console (its same name replaced), the copy put back.
            void AsIfXemuMade(string stateFile)
            {
                var scratch = emuDir + "-scratch";
                if (Directory.Exists(scratch)) Directory.Delete(scratch, true);
                Directory.CreateDirectory(Path.Combine(scratch, "hdd", "games"));
                File.Copy(Path.Combine(emuDir, "hdd", "base.qcow2"), Path.Combine(scratch, "hdd", "base.qcow2"));
                File.SetAttributes(Path.Combine(scratch, "hdd", "base.qcow2"), FileAttributes.Normal);   // the base is kept read-only
                var sc = Path.Combine(scratch, "hdd", "games", Path.GetFileName(console));
                File.Copy(console, sc);
                var sexe = Path.Combine(scratch, "x.emu.exe");
                File.Copy(exe, sexe);
                var sfile = (string)M("SlotPath", new object[] { sexe, titleId, 50 });
                Directory.CreateDirectory(Path.GetDirectoryName(sfile));
                File.Copy(stateFile, sfile);
                M("Mirror", new object[] { sexe, titleId, true, (Func<string>)(() => "test-keys") });
                File.Copy(sc, console, overwrite: true);
                Directory.Delete(scratch, true);
            }

            var one = ReadFile(SlotFile(1)); var two = ReadFile(SlotFile(2));
            if (one == null || two == null) { Check("slots 1 and 2 there to start from", false); return; }
            string a = (string)Field(one, "Name"), b = (string)Field(two, "Name");
            uint aDate = (uint)Field(one, "DateSec");
            var work = Path.Combine(emuDir, "variants"); Directory.CreateDirectory(work);

            // 1. "a" saved over in xemu (Shift+F#): its new version in slot 1, the old file gone.
            AsIfXemuMade(Variant(SlotFile(1), null, 1, Path.Combine(work, "a1.xemustate")));
            var said = Mirror(false);
            var now1 = ReadFile(SlotFile(1));
            Check("a name saved over in xemu: the new version keeps its slot", said.Any(s => s.Contains("deleted in xemu")) && said.Any(s => s.Contains("\"" + a + "\" exported as slot 1"))
                  && now1 != null && (uint)Field(now1, "DateSec") == aDate + 1 && Directory.GetFiles(dir, "*.xemustate").Length == 2, string.Join("; ", said));

            // "a" gone (LaunchBox's Remove, the launch): slot 1 free.
            File.Delete(SlotFile(1));
            Mirror(true);
            Check("(\"" + a + "\" taken out of the console)", !Snaps().Contains(a));

            // 2. A new name: a number no name ever had - not the free slot 1.
            var c = b.Substring(0, b.Length - 1) + (b.EndsWith("X") ? "Y" : "X");
            AsIfXemuMade(Variant(SlotFile(2), c, 3, Path.Combine(work, "c.xemustate")));
            said = Mirror(false);
            Check("a new name: a number never used, not the one freed", said.Any(s => s.Contains("\"" + c + "\" exported as slot 3")) && !File.Exists(SlotFile(1)), string.Join("; ", said));

            // 3. "a" made again in xemu: its slot back.
            AsIfXemuMade(Variant(Path.Combine(work, "a1.xemustate"), null, 1, Path.Combine(work, "a2.xemustate")));
            said = Mirror(false);
            Check("a name deleted then made again: its slot back", said.Any(s => s.Contains("\"" + a + "\" exported as slot 1")), string.Join("; ", said));
            var slots = File.ReadAllLines((string)M("SlotsPath", new object[] { exe, titleId }));
            Check("... the slots: one line a name, never forgotten (" + slots.Length + ")", slots.Length == 3 && slots.Contains("1\t" + a) && slots.Contains("2\t" + b) && slots.Contains("3\t" + c), string.Join(" | ", slots));

            // The index forgotten (savestates off): the slots kept, nothing renumbered.
            File.Delete((string)M("IndexPath", new object[] { exe, titleId }));
            Check("the index forgotten: nothing exported again, nothing renumbered", Mirror(false).Count == 0 && File.ReadAllLines((string)M("SlotsPath", new object[] { exe, titleId })).Length == 3);
            Directory.Delete(work, true);
        }

        /// <summary>--qcow2-snapshots &lt;a COPY of a console&gt; [--title id --zip save]: its snapshots listed; then, with a save,
        /// the save put into it (XemuSaveStore.Insert - the rebuild with snapshots), and every snapshot read again: its table
        /// entry and every cluster of its L1 (disk and VM state) the same as before, byte for byte.</summary>
        public static bool Qcow2SnapshotsCheck(Assembly asm, string console, string titleId, string zip)
        {
            _asm = asm;
            var img = T("Saves.Qcow2Image");
            object Open(string p) { try { return img.GetMethod("Open", Any).Invoke(null, new object[] { p }); } catch (TargetInvocationException ex) { throw ex.InnerException ?? ex; } }
            object G(object o, string n) => o.GetType().GetProperty(n, Any)?.GetValue(o) ?? o.GetType().GetField(n, Any)?.GetValue(o);
            List<(string Entry, Dictionary<long, string> Clusters)> Read(string path)
            {
                var all = new List<(string, Dictionary<long, string>)>();
                var q = (IDisposable)Open(path);
                using (q)
                {
                    var entryIn = q.GetType().GetMethod("EntryIn", Any);
                    var data = q.GetType().GetMethod("ClusterData", Any);
                    long perL2 = 1L << (int)G(q, "L2Bits");
                    using var sha = System.Security.Cryptography.SHA1.Create();
                    foreach (var sn in (System.Collections.IEnumerable)G(q, "Snapshots"))
                    {
                        var l1 = (ulong[])G(sn, "L1");
                        var map = new Dictionary<long, string>();
                        for (long i = 0; i < l1.Length; i++)
                        {
                            if ((l1[i] & 0x00FFFFFFFFFFFE00UL) == 0) continue;
                            for (long j = 0; j < perL2; j++)
                            {
                                var e = (ulong?)entryIn.Invoke(q, new object[] { l1, i * perL2 + j });
                                if (e == null || e.Value == 0) continue;
                                map[i * perL2 + j] = (e.Value & 1) != 0 && (e.Value & (1UL << 62)) == 0 ? "zero" : Convert.ToHexString(sha.ComputeHash((byte[])data.Invoke(q, new object[] { e.Value })));
                            }
                        }
                        var entry = G(sn, "Id") + "|" + G(sn, "Name") + "|" + G(sn, "DateSec") + "|" + G(sn, "VmStateSize") + "|" + Convert.ToHexString((byte[])G(sn, "Extra")) + "|" + l1.Length;
                        all.Add((entry, map));
                        Console.WriteLine("  snapshot " + G(sn, "Id") + " \"" + G(sn, "Name") + "\" " + G(sn, "Date") + ", VM state " + ((ulong)G(sn, "VmStateSize") >> 20) + " MB, L1 " + l1.Length + ", " + map.Count + " cluster(s)");
                    }
                }
                return all;
            }
            Console.WriteLine("before (" + (new FileInfo(console).Length >> 20) + " MB):");
            var before = Read(console);
            if (titleId == null || zip == null) return true;
            var store = T("Saves.XemuSaveStore");
            var baseDisk = Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(console)), "base.qcow2");
            try { store.GetMethod("Insert", Any).Invoke(null, new object[] { console, baseDisk, titleId, zip }); }
            catch (TargetInvocationException ex) { Check("the save put in", false, (ex.InnerException ?? ex).Message); return false; }
            Console.WriteLine("after (" + (new FileInfo(console).Length >> 20) + " MB):");
            var after = Read(console);
            Check("as many snapshots after as before", after.Count == before.Count, after.Count + " vs " + before.Count);
            for (int i = 0; i < Math.Min(before.Count, after.Count); i++)
            {
                Check("snapshot " + (i + 1) + ": its table entry the same", before[i].Entry == after[i].Entry);
                Check("snapshot " + (i + 1) + ": every cluster the same (" + before[i].Clusters.Count + ")", before[i].Clusters.Count == after[i].Clusters.Count
                      && before[i].Clusters.All(kv => after[i].Clusters.TryGetValue(kv.Key, out var h) && h == kv.Value));
            }
            var extracted = ((System.Collections.IEnumerable)store.GetMethod("Extract", Any).Invoke(null, new object[] { console, titleId })).Cast<object>().ToList();
            var wanted = ((System.Collections.IEnumerable)store.GetMethod("Unpack", Any).Invoke(null, new object[] { zip })).Cast<object>().ToList();
            Check("the active disk holds the save put in (" + wanted.Count + " files)", extracted.Count == wanted.Count);
            return _bad == 0;
        }

        /// <summary>--xbox-save-keys &lt;file&gt;...: saves of 04/10, their keys in the zip's comment, moved to the entry - the
        /// plugin's own code. A file without keys is left alone.</summary>
        public static bool MoveSaveKeys(Assembly asm, IEnumerable<string> packs)
        {
            _asm = asm;
            var t = _asm.GetType("LbIntegrations.Xbox.XboxSaveKeys", true);
            object K(string m, params object[] a) { try { return t.GetMethod(m, Any).Invoke(null, a); } catch (TargetInvocationException ex) { throw ex.InnerException ?? ex; } }
            byte[] F(object o, string f) => (byte[])o.GetType().GetField(f, Any).GetValue(o);
            bool ok = true;
            foreach (var p in packs)
            {
                try
                {
                    var k = K("Read", p);
                    if (k == null) { Console.WriteLine("  no keys, left alone: " + p); continue; }
                    var when = File.GetLastWriteTime(p);
                    K("Write", p, F(k, "Hdd"), F(k, "Cert"));
                    File.SetLastWriteTime(p, when);
                    var back = K("Read", p);
                    bool same = back != null && F(back, "Hdd").SequenceEqual(F(k, "Hdd")) && F(back, "Cert").SequenceEqual(F(k, "Cert")) && (string)K("Comment", p) == "";
                    Console.WriteLine("  " + (same ? "moved " : "FAILED ") + p);
                    ok &= same;
                }
                catch (Exception ex) { Console.WriteLine("  FAILED " + p + ": " + ex.Message); ok = false; }
            }
            return ok;
        }

        /// <summary>The keys a save carries (Shared.Xbox\XboxSaveKeys): an entry at the zip's root, left out of the console.</summary>
        private static void SaveKeysCheck(string work)
        {
            var t = _asm.GetType("LbIntegrations.Xbox.XboxSaveKeys", true);
            object K(string m, params object[] a) { try { return t.GetMethod(m, Any).Invoke(null, a); } catch (TargetInvocationException ex) { throw ex.InnerException ?? ex; } }
            byte[] Field(object o, string f) => o?.GetType().GetField(f, Any)?.GetValue(o) as byte[];
            var store = _asm.GetType("LbIntegrations.Xemu.Saves.XemuSaveStore", true);
            var pack = Path.Combine(work, "keys", "4d530004.cxbxsave");
            Directory.CreateDirectory(Path.GetDirectoryName(pack));
            var files = new List<(string, byte[])> { ("AAAA/SaveMeta.xbx", new byte[] { 1, 2, 3 }), ("TitleMeta.xbx", new byte[] { 4 }) };
            void Pack() { try { store.GetMethod("Pack", Any).Invoke(null, new object[] { files, pack }); } catch (TargetInvocationException ex) { throw ex.InnerException ?? ex; } }
            List<string> Names() { using var a = SharpCompress.Archives.Zip.ZipArchive.Open(pack); return a.Entries.Where(e => !e.IsDirectory).Select(e => e.Key).ToList(); }
            Pack();
            var plain = File.ReadAllBytes(pack);
            var legacy = K("ForLaunch", pack, new byte[16]);
            Check("a save without keys: Cxbx-Reloaded's (the certificate key given), its HDD key not imposed", legacy != null && Field(legacy, "Hdd") == null && Field(legacy, "Cert").All(x => x == 0));
            Check("no save: nothing imposed", K("ForLaunch", Path.Combine(work, "keys", "none.cxbxsave"), new byte[16]) == null);
            var hdd = Enumerable.Repeat((byte)0x11, 16).ToArray();
            var cert = Enumerable.Range(1, 16).Select(i => (byte)i).ToArray();
            K("Write", pack, hdd, cert);
            var read = K("Read", pack);
            Check("the keys written into the file read back", read != null && Field(read, "Hdd").SequenceEqual(hdd) && Field(read, "Cert").SequenceEqual(cert));
            Check("... as lbip-xbox-keys.txt at the zip's root, beside the save's files", Names().OrderBy(n => n, StringComparer.Ordinal).SequenceEqual(new[] { "AAAA/SaveMeta.xbx", "TitleMeta.xbx", "lbip-xbox-keys.txt" }.OrderBy(n => n, StringComparer.Ordinal)), string.Join(", ", Names()));
            var un = (System.Collections.IList)store.GetMethod("Unpack", Any).Invoke(null, new object[] { pack });
            Check("... left out when laid into a console (" + un.Count + " files)", un.Count == 2);
            var before = File.ReadAllBytes(pack);
            K("Write", pack, hdd, cert);
            Check("written again with the same keys: not rewritten", File.ReadAllBytes(pack).SequenceEqual(before));
            K("Write", pack, hdd, new byte[16]);
            Check("written with other keys: replaced, not added", Field(K("Read", pack), "Cert").All(x => x == 0) && Names().Count(n => n == "lbip-xbox-keys.txt") == 1);
            var withKeys = K("ForLaunch", pack, cert);
            Check("a save with keys: its own, not the emulator's", Field(withKeys, "Hdd").SequenceEqual(hdd) && Field(withKeys, "Cert").All(x => x == 0));
            // A save of 04/10: the keys in the zip's comment - read, and moved to the entry when written.
            Pack();
            var b = File.ReadAllBytes(pack);
            var comment = System.Text.Encoding.ASCII.GetBytes((string)K("Format", cert, hdd));
            var old = b.Take(b.Length - 2).Concat(new[] { (byte)comment.Length, (byte)(comment.Length >> 8) }).Concat(comment).ToArray();
            File.WriteAllBytes(pack, old);
            var fromComment = K("Read", pack);
            Check("a save of 04/10 (keys in the comment): read", fromComment != null && Field(fromComment, "Hdd").SequenceEqual(cert));
            K("Write", pack, cert, hdd);
            Check("... written: in the entry, the comment gone", (string)K("Comment", pack) == "" && Names().Contains("lbip-xbox-keys.txt"));
        }

        private static void Eeprom(string work, string real)
        {
            Type E() => _asm.GetType("LbIntegrations.Xemu.Eeprom.XemuEeprom", true);
            object Ec(string m, params object[] a)
            {
                var mi = E().GetMethods(Any).First(x => x.Name == m && x.GetParameters().Length == a.Length);
                try { return mi.Invoke(null, a); } catch (TargetInvocationException ex) { throw ex.InnerException ?? ex; }
            }
            uint U(byte[] b, int at) => BitConverter.ToUInt32(b, at);
            bool Sums(byte[] b) => (uint)Ec("Checksum", b, 0x30, 0x30) == 0xFFFFFFFF && (uint)Ec("Checksum", b, 0x60, 0x60) == 0xFFFFFFFF;

            if (real != null && File.Exists(real))
            {
                var bytes = File.ReadAllBytes(real);
                var o = Ec("Open", bytes);
                Check("xemu's own EEPROM opens (" + Path.GetFileName(real) + ")", o != null);
                if (o != null)
                {
                    Check("a 1.0 kernel's, North America, NTSC-M", Get(o, "Version").ToString() == "RetailFirst" && (uint)Get(o, "Region") == 1 && (uint)Get(o, "AvRegion") == 0x00400100,
                          Get(o, "Version") + " / " + Get(o, "Region") + " / " + Get(o, "AvRegion"));
                    Check("sealed unchanged = its very bytes", ((byte[])Ec("Seal", o)).SequenceEqual(bytes));
                }
            }
            else Console.WriteLine("    (no EEPROM made by xemu at hand - the byte-for-byte check skipped)");

            var fresh = (byte[])Ec("Fresh");
            var f = Ec("Open", fresh);
            Check("a new console opens", f != null);
            if (f == null) return;
            Check("new: 1.0 kernel, North America, NTSC-M, English, London",
                  Get(f, "Version").ToString() == "RetailFirst" && (uint)Get(f, "Region") == 1 && (uint)Get(f, "AvRegion") == 0x00400100 && (uint)Get(f, "Language") == 1 && (string)Get(f, "Zone") == "London",
                  Get(f, "Version") + " " + Get(f, "Region") + " " + Get(f, "AvRegion") + " " + Get(f, "Language") + " " + Get(f, "Zone"));
            Check("new: both section checksums right", Sums(fresh));
            // This machine's "Your console": with a seed, a new console is it (PackIdentity.Xbox); without, XboxEepromEditor's.
            var idType = _asm.GetType("LbIntegrations.Identity.PackIdentity", true);
            var loaded = idType.GetMethod("Load", Any).Invoke(null, null);
            var seedX = loaded == null ? null : idType.GetMethod("Xbox", Any).Invoke(loaded, null);
            var seedSerial = seedX == null ? null : (string)Get(seedX, "Serial");
            var seedHdd = seedX == null ? null : (byte[])Get(seedX, "HddKey");
            Console.WriteLine("    (this machine's console: " + (seedX == null ? "no seed" : "a seed, serial " + seedSerial) + ")");
            if (seedX == null) Check("new: serial of 12 digits ending in 9", System.Text.RegularExpressions.Regex.IsMatch((string)Get(f, "Serial"), "^[0-9]{11}9$"), (string)Get(f, "Serial"));
            else Check("new: your console's serial and MAC", (string)Get(f, "Serial") == seedSerial
                       && ((byte[])fresh.Skip(0x40).Take(6).ToArray()).SequenceEqual((byte[])Get(seedX, "Mac")), (string)Get(f, "Serial"));
            Check("two new consoles differ", !((byte[])Ec("Fresh")).SequenceEqual(fresh));

            var baseFile = Path.Combine(work, "eeprom.bin");
            var session = Path.Combine(work, "eeprom-session.bin");
            File.WriteAllBytes(baseFile, fresh);
            var xbeType = _asm.GetType("LbIntegrations.Cxbx.XbeInfo", true);
            object Game(uint regions) { var g = Activator.CreateInstance(xbeType, true); xbeType.GetField("Region").SetValue(g, regions); return g; }
            object Session(object game, Dictionary<string, string> v, out List<string> said)
            {
                said = new List<string>();
                var path = (string)Ec("Prepare", baseFile, session, game, v, said);
                return path == null ? null : Ec("Open", File.ReadAllBytes(path));
            }

            var europe = Session(Game(4), new Dictionary<string, string>(), out var s1);
            Check("a European game: region Europe", europe != null && (uint)Get(europe, "Region") == 4, europe == null ? "none" : "" + Get(europe, "Region"));
            Check("... PAL-I 50 Hz with 60 Hz allowed", europe != null && (uint)Get(europe, "AvRegion") == 0x00800300 && ((uint)Get(europe, "VideoFlags") & 0x00400000) != 0);
            Check("... same console: HDD key and serial kept", europe != null && ((byte[])Get(europe, "HddKey")).SequenceEqual((byte[])Get(f, "HddKey")) && (string)Get(europe, "Serial") == (string)Get(f, "Serial"));
            Check("... checksums right", Sums(File.ReadAllBytes(session)));
            Check("... the base never written", File.ReadAllBytes(baseFile).SequenceEqual(fresh));
            Console.WriteLine("      said: " + string.Join(", ", s1));

            var usa = Session(Game(3), new Dictionary<string, string>(), out _);
            Check("an American + Japanese game on a North American console: region kept, NTSC with HD modes",
                  usa != null && (uint)Get(usa, "Region") == 1 && (uint)Get(usa, "AvRegion") == 0x00400100 && ((uint)Get(usa, "VideoFlags") & 0xE0000) == 0xE0000);
            var japan = Session(Game(2), new Dictionary<string, string>(), out _);
            Check("a Japanese game: Japan, NTSC-J", japan != null && (uint)Get(japan, "Region") == 2 && (uint)Get(japan, "AvRegion") == 0x00400200);
            var forced = Session(Game(4), new Dictionary<string, string> { ["console.region"] = "1", ["console.video"] = "pal50", ["console.language"] = "4", ["console.timezone"] = "xemu" }, out _);
            Check("chosen: North America, PAL 50, French, its own time zone",
                  forced != null && (uint)Get(forced, "Region") == 1 && (uint)Get(forced, "AvRegion") == 0x00800300 && ((uint)Get(forced, "VideoFlags") & 0x00400000) == 0
                  && (uint)Get(forced, "Language") == 4 && (string)Get(forced, "Zone") == "London");
            var own = Session(Game(4), new Dictionary<string, string> { ["console.region"] = "xemu", ["console.video"] = "xemu", ["console.language"] = "xemu", ["console.timezone"] = "xemu" }, out _);
            Check("all its own: nothing but the parental controls changed", own != null && ((byte[])Get(own, "Data")).SequenceEqual((byte[])Get(f, "Data")));

            Check("Windows' Paris (Romance) = the Xbox's Paris", (string)Ec("WindowsZone", TimeZoneInfo.FindSystemTimeZoneById("Romance Standard Time")) == "Paris");
            Check("Windows' Tokyo = the Xbox's Tokyo", (string)Ec("WindowsZone", TimeZoneInfo.FindSystemTimeZoneById("Tokyo Standard Time")) == "Tokyo");
            var odd = TimeZoneInfo.CreateCustomTimeZone("Probe +9", TimeSpan.FromHours(9), "Probe", "Probe");
            Check("an unknown zone at +9 without DST: one at that offset", new[] { "Seoul", "Tokyo" }.Contains((string)Ec("WindowsZone", odd)), "" + Ec("WindowsZone", odd));
            Check("this machine's zone found", Ec("WindowsZone", TimeZoneInfo.Local) != null, TimeZoneInfo.Local.Id);
            uint lang = (uint)Ec("IdentityLanguage");
            Check("the console language is one the Xbox knows", lang >= 1 && lang <= 9, "" + lang);

            var packKey = seedHdd ?? Enumerable.Repeat((byte)0x11, 16).ToArray();
            Check(seedX == null ? "new: the pack's HDD key (16 x 0x11)" : "new: your console's HDD key", ((byte[])Get(f, "HddKey")).SequenceEqual(packKey));
            if (real != null && File.Exists(real))
            {
                // xemu's own console, its key drawn at random: the session gets the pack's - or keeps its own when asked.
                File.Copy(real, baseFile, overwrite: true);
                var theirs = (byte[])Get(Ec("Open", File.ReadAllBytes(real)), "HddKey");
                var withPack = Session(Game(1), new Dictionary<string, string>(), out var sk);
                Check("xemu's console: the session gets " + (seedX == null ? "the pack's HDD key" : "your console's values"), withPack != null && ((byte[])Get(withPack, "HddKey")).SequenceEqual(packKey)
                      && sk.Contains(seedX == null ? "HDD key the pack's" : "your console's serial, MAC and keys"), string.Join(", ", sk));
                var withOwn = Session(Game(1), new Dictionary<string, string> { ["console.hddkey"] = "xemu" }, out _);
                Check("... console.hddkey=xemu keeps its own", withOwn != null && ((byte[])Get(withOwn, "HddKey")).SequenceEqual(theirs)
                      && (string)Get(withOwn, "Serial") == (string)Get(Ec("Open", File.ReadAllBytes(real)), "Serial"));
                Check("... its serial " + (seedX == null ? "kept" : "your console's"), withPack != null
                      && (string)Get(withPack, "Serial") == (seedSerial ?? (string)Get(Ec("Open", File.ReadAllBytes(real)), "Serial")));
                Check("... the base never written", File.ReadAllBytes(baseFile).SequenceEqual(File.ReadAllBytes(real)));
            }

            File.Delete(baseFile);
            Session(Game(4), new Dictionary<string, string>(), out var s2);
            Check("no eeprom.bin: one made first", File.Exists(baseFile) && Ec("Open", File.ReadAllBytes(baseFile)) != null, string.Join(", ", s2));
        }

        /// <summary>--xemu-console --base &lt;eeprom.bin&gt; --out &lt;session file&gt; --rom &lt;game&gt;: the session's console the plugin would
        /// make for that game, with the settings' defaults - for a launch by hand.</summary>
        public static bool Console_(Assembly asm, string basePath, string outPath, string rom)
        {
            _asm = asm;
            var info = Call("XemuDisc", "Describe", rom);
            Console.WriteLine("  " + Path.GetFileName(rom) + ": " + Get(info, "Kind") + ", regions " + Get(Get(info, "Xbe"), "Region"));
            var said = new List<string>();
            var mi = _asm.GetType("LbIntegrations.Xemu.Eeprom.XemuEeprom", true).GetMethod("Prepare", Any);
            var path = (string)mi.Invoke(null, new object[] { basePath, outPath, Get(info, "Xbe"), new Dictionary<string, string>(), said });
            Console.WriteLine("  -> " + (path ?? "none") + ": " + string.Join(", ", said));
            return path != null;
        }

        /// <summary>--xdvdfs-diff &lt;a&gt; &lt;b&gt;: two Xbox discs side by side - partition, size, where the volume's last file ends,
        /// the files only one has, those whose size or bytes differ (SHA-1 of each file both have). Reads only.</summary>
        public static bool Diff(Assembly asm, string a, string b)
        {
            _asm = asm;
            var xd = _asm.GetType("LbIntegrations.Cxbx.Xdvdfs", true).GetMethods(Any).First(m => m.Name == "List" && m.GetParameters().Length == 1);
            var la = xd.Invoke(null, new object[] { a }); var lb = xd.Invoke(null, new object[] { b });
            IEnumerable<object> Files(object l) => ((System.Collections.IEnumerable)Get(l, "Files")).Cast<object>();
            void Show(string p, object l)
            {
                long pb = (long)Get(l, "PartitionBase"), len = new FileInfo(p).Length;
                long end = Files(l).Select(f => (long)Get(f, "Offset") + (long)Get(f, "Length")).DefaultIfEmpty(0).Max();
                Console.WriteLine("  " + Path.GetFileName(p) + ": " + len.ToString("N0") + " bytes, partition at 0x" + pb.ToString("X") + ", "
                                  + Files(l).Count() + " files, the last ends at " + end.ToString("N0") + " - " + (len - end).ToString("N0") + " bytes after it; in the partition: "
                                  + (len - pb).ToString("N0") + ", up to the last file: " + (end - pb).ToString("N0"));
            }
            Show(a, la); Show(b, lb);
            var fa = Files(la).ToDictionary(f => ((string)Get(f, "Path")).ToLowerInvariant());
            var fb = Files(lb).ToDictionary(f => ((string)Get(f, "Path")).ToLowerInvariant());
            foreach (var k in fa.Keys.Except(fb.Keys).OrderBy(x => x)) Console.WriteLine("  only in the first: " + k + " (" + Get(fa[k], "Length") + ")");
            foreach (var k in fb.Keys.Except(fa.Keys).OrderBy(x => x)) Console.WriteLine("  only in the second: " + k + " (" + Get(fb[k], "Length") + ")");
            string Sha(string p, object f)
            {
                using var s = File.OpenRead(p); s.Seek((long)Get(f, "Offset"), SeekOrigin.Begin);
                using var h = System.Security.Cryptography.SHA1.Create();
                var buf = new byte[1 << 20]; long left = (long)Get(f, "Length"); int n;
                while (left > 0 && (n = s.Read(buf, 0, (int)Math.Min(buf.Length, left))) > 0) { h.TransformBlock(buf, 0, n, null, 0); left -= n; }
                h.TransformFinalBlock(buf, 0, 0);
                return BitConverter.ToString(h.Hash).Replace("-", "").ToLowerInvariant();
            }
            int same = 0, differ = 0;
            foreach (var k in fa.Keys.Intersect(fb.Keys).OrderBy(x => x))
            {
                if ((long)Get(fa[k], "Length") != (long)Get(fb[k], "Length")) { Console.WriteLine("  size differs: " + k + " " + Get(fa[k], "Length") + " vs " + Get(fb[k], "Length")); differ++; continue; }
                var ha = Sha(a, fa[k]); var hb = Sha(b, fb[k]);
                if (ha != hb)
                {
                    Console.WriteLine("  bytes differ: " + k + " (" + Get(fa[k], "Length") + ")"); differ++;
                    byte[] Bytes(string p, object f) { using var s = File.OpenRead(p); s.Seek((long)Get(f, "Offset"), SeekOrigin.Begin); var d = new byte[(int)(long)Get(f, "Length")]; int got = 0, n; while (got < d.Length && (n = s.Read(d, got, d.Length - got)) > 0) got += n; return d; }
                    var da = Bytes(a, fa[k]); var db = Bytes(b, fb[k]);
                    if (k.EndsWith(".xbe")) { File.WriteAllBytes(Path.Combine(Path.GetTempPath(), "lbip-diff-a-" + Path.GetFileName(k)), da); File.WriteAllBytes(Path.Combine(Path.GetTempPath(), "lbip-diff-b-" + Path.GetFileName(k)), db); }
                    int runs = 0;
                    for (int i = 0; i < da.Length && runs < 40; i++)
                    {
                        if (da[i] == db[i]) continue;
                        int j = i; while (j < da.Length && j - i < 64 && da[j] != db[j]) j++;
                        Console.WriteLine("    0x" + i.ToString("X6") + " [" + (j - i) + "]: " + BitConverter.ToString(da, i, Math.Min(j - i, 16)) + "  vs  " + BitConverter.ToString(db, i, Math.Min(j - i, 16)));
                        runs++; i = j;
                    }
                }
                else same++;
                if (k == "default.xbe") Console.WriteLine("  default.xbe sha1: " + ha + " / " + hb);
            }
            Console.WriteLine("  files the same: " + same + ", different: " + differ);
            return true;
        }

        private static readonly byte[] MediaPattern = { 0xE8, 0xCA, 0xFD, 0xFF, 0xFF, 0x85, 0xC0, 0x7D };

        /// <summary>An XBE holding the media check's pattern at <paramref name="at"/> - patched already when <paramref name="done"/>.</summary>
        private static byte[] XbeWithCheck(uint titleId, int at, bool done = false)
        {
            var b = CxbxCheck.XbeBytes(titleId, "Media Game");
            MediaPattern.CopyTo(b, at);
            if (done) b[at + 7] = 0xEB;
            return b;
        }

        private static CxbxCheck.Node MediaGame(bool done = false) => CxbxCheck.Dir("",
            CxbxCheck.File_("default.xbe", XbeWithCheck(0x4D530005, 0x2000, done)),
            CxbxCheck.File_("readme.txt", Encoding.ASCII.GetBytes("hello")),
            CxbxCheck.Dir("tools", CxbxCheck.File_("dash.xbe", XbeWithCheck(0x4D530005, 0x1804, done))),
            CxbxCheck.File_("big.bin", Noise(2 * 1024 * 1024 + 5, 4)));

        private static byte[] Noise(int n, int seed) => CxbxCheck.Noise(n, seed);

        private static void MediaPatch(string work, string exe)
        {
            var xiso = Path.Combine(work, "Media Game (USA).xiso");
            CxbxCheck.WriteImage(xiso, 0, MediaGame());
            var patches = (long[])Call("XemuDisc", "PatchesOf", xiso);
            Check("two places found, one per .xbe", patches.Length == 2, string.Join(",", patches));
            var original = File.ReadAllBytes(xiso);
            Check("each one the pattern's 8th byte, 7D", patches.All(p => original[p] == 0x7D && original.Skip((int)p - 7).Take(8).SequenceEqual(MediaPattern)));

            var p1 = Present(xiso, exe, out var problem, out _);
            Check("an XISO to patch is not handed over as it is: a copy", p1 != null && p1 != xiso && File.Exists(p1), p1 + " / " + problem);
            Check("... named apart (-mp)", p1 != null && p1.EndsWith("-mp.iso"));
            if (p1 != null && File.Exists(p1))
            {
                var copy = File.ReadAllBytes(p1);
                Check("... EB at both places", patches.All(p => copy[p] == 0xEB));
                Check("... every other byte the XISO's", copy.Length == original.Length && Enumerable.Range(0, copy.Length).All(i => copy[i] == original[i] || patches.Contains(i)));
            }
            Check("the user's XISO never written", File.ReadAllBytes(xiso).SequenceEqual(original));

            var done = Path.Combine(work, "Media Game Done (USA).xiso");
            CxbxCheck.WriteImage(done, 0, MediaGame(done: true));
            Check("an XISO patched already: nothing to do", ((long[])Call("XemuDisc", "PatchesOf", done)).Length == 0);
            Check("... handed over as it is", Present(done, exe, out _, out _) == done);

            var redump = Path.Combine(work, "Media Game (Europe).iso");
            CxbxCheck.WriteImage(redump, 0x18300000, MediaGame());
            var rp = (long[])Call("XemuDisc", "PatchesOf", redump);
            Check("a redump: the places counted from its game partition", rp.SequenceEqual(patches), string.Join(",", rp));
            var p2 = Present(redump, exe, out problem, out _);
            Check("... its cut patched like the XISO's copy", p2 != null && p1 != null && File.ReadAllBytes(p2).SequenceEqual(File.ReadAllBytes(p1)), p2 + " / " + problem);

            // The view: the patch served over the bytes read, whatever the read's bounds.
            var type = _asm.GetType("LbIntegrations.Xemu.ExfatOneFileView", true);
            var view = Activator.CreateInstance(type, Any, null, new object[] { 0L, (long)original.Length, "game.iso", "XBOXDISC", patches }, null);
            var read = type.GetMethod("Read", Any);
            long fileAt;
            {
                // the file's first cluster: found as the reader does - the first byte of the volume's heap that is the XISO's
                var probeBuf = new byte[0x10000];
                long heap = -1;
                for (long at = 0; at < (long)Get(view, "Length") && heap < 0; at += 0x100000)
                {
                    read.Invoke(view, new object[] { new MemoryStream(original), at, probeBuf, probeBuf.Length });
                    if (probeBuf.Take(0x10000).SequenceEqual(original.Take(0x10000))) heap = at;
                }
                fileAt = heap;
            }
            Check("the view's file found", fileAt >= 0);
            if (fileAt >= 0)
            {
                bool ok = true;
                foreach (var p in patches)
                    foreach (var (from, len) in new[] { (p - 100, 200), (p, 1), (p - 3, 4), (p, 50) })
                    {
                        var buf = new byte[len];
                        read.Invoke(view, new object[] { new MemoryStream(original), fileAt + from, buf, len });
                        for (int i = 0; i < len; i++)
                        {
                            byte want = patches.Contains(from + i) ? (byte)0xEB : original[from + i];
                            if (buf[i] != want) ok = false;
                        }
                    }
                Check("the view serves EB there, whatever the read's bounds, the rest as it is", ok);
            }

            // Turned off: the discs as they are.
            var settings = Path.Combine(work, "settings");
            Directory.CreateDirectory(settings);
            File.WriteAllText(Path.Combine(settings, "settings.ini"), "opt.disc.media_patch=off\r\n");
            Check("media patch off: the XISO handed over as it is", Present(xiso, exe, out _, out _) == xiso);
            var p3 = Present(redump, exe, out _, out _);
            Check("media patch off: the redump's cut not patched, and named apart", p3 != null && !p3.EndsWith("-mp.iso") && File.Exists(p3)
                  && patches.All(p => File.ReadAllBytes(p3)[p] == 0x7D));
            File.Delete(Path.Combine(settings, "settings.ini"));
        }

        /// <summary>--xemu-saves --console &lt;qcow2&gt; [--title &lt;id&gt;] [--out &lt;zip&gt;]: what E: holds - its root, UDATA's titles and,
        /// for one title, its files - and its save packed as Cxbx's plugin packs one. Reads only (but --out).</summary>
        public static bool Saves(Assembly asm, string console, string titleId, string outZip)
        {
            _asm = asm;
            var qt = _asm.GetType("LbIntegrations.Xemu.Saves.Qcow2Image", true);
            var ft = _asm.GetType("LbIntegrations.Xemu.Saves.FatxVolume", true);
            using var disk = (IDisposable)qt.GetMethod("Open", Any).Invoke(null, new object[] { console });
            Console.WriteLine("  " + Path.GetFileName(console) + ": " + Get(disk, "Length") + " bytes virtual, backing " + Get(disk, "BackingPath"));
            foreach (var (letter, off, size) in new[] { ("X", 0x00080000L, 0x2EE00000L), ("Y", 0x2EE80000L, 0x2EE00000L), ("Z", 0x5DC80000L, 0x2EE00000L), ("C", 0x8CA80000L, 0x1F400000L), ("E", 0xABE80000L, 0x1312D6000L) })
            {
                object v;
                try { v = ft.GetMethod("Open", Any).Invoke(null, new object[] { disk, off, size }); } catch (TargetInvocationException ex) { Console.WriteLine("  " + letter + ": " + ex.InnerException?.Message); continue; }
                if (v == null) { Console.WriteLine("  " + letter + ": no FATX"); continue; }
                var list = (System.Collections.IEnumerable)ft.GetMethod("List", Any).Invoke(v, new object[] { (uint)Get(v, "RootCluster") });
                Console.WriteLine("  " + letter + ": FATX" + ((bool)Get(v, "Fat32") ? "32" : "16") + ", cluster " + Get(v, "ClusterSize") + ", " + Get(v, "ClusterCount") + " clusters; root: "
                                  + string.Join(", ", list.Cast<object>().Select(x => x.ToString())));
                var problems = ((System.Collections.IEnumerable)ft.GetMethod("Check", Any).Invoke(v, null)).Cast<string>().ToList();
                Console.WriteLine("     check: " + (problems.Count == 0 ? "clean" : problems.Count + " problem(s): " + string.Join("; ", problems.Take(6))));
                if (letter != "E") continue;
                var udata = ft.GetMethod("Find", Any).Invoke(v, new object[] { "UDATA" });
                if (udata != null)
                    Console.WriteLine("  E:\\UDATA: " + string.Join(", ", ((System.Collections.IEnumerable)ft.GetMethod("List", Any).Invoke(v, new object[] { (uint)Get(udata, "FirstCluster") })).Cast<object>().Select(x => x.ToString())));
            }
            if (titleId == null) return true;
            var st = _asm.GetType("LbIntegrations.Xemu.Saves.XemuSaveStore", true);
            var files = ((System.Collections.IEnumerable)st.GetMethod("Extract", Any).Invoke(null, new object[] { console, titleId })).Cast<object>().ToList();
            Console.WriteLine("  UDATA\\" + titleId + ": " + files.Count + " file(s)");
            foreach (var f in files)
            {
                var name = (string)f.GetType().GetField("Item1").GetValue(f); var data = (byte[])f.GetType().GetField("Item2").GetValue(f);
                using var sha = System.Security.Cryptography.SHA1.Create();
                Console.WriteLine("    " + name + "  " + data.Length + "  " + BitConverter.ToString(sha.ComputeHash(data)).Replace("-", "").Substring(0, 12).ToLowerInvariant());
            }
            if (outZip != null && files.Count > 0)
            {
                st.GetMethod("Capture", Any).Invoke(null, new object[] { console, titleId, outZip });
                Console.WriteLine("  packed -> " + outZip + " (" + new FileInfo(outZip).Length + " bytes)");
            }
            return true;
        }

        /// <summary>--xemu-insert --console &lt;qcow2&gt; --base &lt;base.qcow2&gt; --title &lt;id&gt; --zip &lt;save&gt;: the save laid into the console
        /// (made when absent) - WRITES the console: give it a copy.</summary>
        public static bool Insert(Assembly asm, string console, string baseDisk, string titleId, string zip)
        {
            _asm = asm;
            var st = _asm.GetType("LbIntegrations.Xemu.Saves.XemuSaveStore", true);
            var watch = System.Diagnostics.Stopwatch.StartNew();
            try { st.GetMethod("Insert", Any).Invoke(null, new object[] { console, baseDisk, titleId, zip }); }
            catch (TargetInvocationException ex) { Console.WriteLine("  insert FAILED: " + ex.InnerException); return false; }
            Console.WriteLine("  inserted in " + watch.ElapsedMilliseconds + " ms -> " + console + " (" + new FileInfo(console).Length + " bytes)");
            return true;
        }

        /// <summary>A whole save cycle on consoles over a copy of <paramref name="realBase"/>: a new console made holding a save, the
        /// save read back the same bytes; a richer save over it (folders, a file across clusters, an empty file) read back file
        /// for file; the first one back, the same bytes; removed, nothing left - and the volume clean after each step.</summary>
        private static void SavesCycle(string work, string realBase)
        {
            if (realBase == null || !File.Exists(realBase)) { Console.WriteLine("    (no dashboard disk at hand - LBIP_XEMU_BASE - the cycle skipped)"); return; }
            var hdd = Path.Combine(work, "hdd"); Directory.CreateDirectory(Path.Combine(hdd, "games"));
            var baseDisk = Path.Combine(hdd, "base.qcow2");
            File.Copy(realBase, baseDisk, overwrite: true);
            var console = Path.Combine(hdd, "games", "4d530004.qcow2");
            var st = _asm.GetType("LbIntegrations.Xemu.Saves.XemuSaveStore", true);
            object S(string m, params object[] a) { try { return st.GetMethods(Any).First(x => x.Name == m && x.GetParameters().Length == a.Length).Invoke(null, a); } catch (TargetInvocationException ex) { throw ex.InnerException ?? ex; } }
            List<string> Problems()
            {
                var qt = _asm.GetType("LbIntegrations.Xemu.Saves.Qcow2Image", true); var ft = _asm.GetType("LbIntegrations.Xemu.Saves.FatxVolume", true);
                using var disk = (IDisposable)qt.GetMethod("Open", Any).Invoke(null, new object[] { console });
                var all = new List<string>();
                foreach (var (off, size) in new[] { (0x00080000L, 0x2EE00000L), (0x2EE80000L, 0x2EE00000L), (0x5DC80000L, 0x2EE00000L), (0x8CA80000L, 0x1F400000L), (0xABE80000L, 0x1312D6000L) })
                {
                    var v = ft.GetMethod("Open", Any).Invoke(null, new object[] { disk, off, size });
                    if (v == null) { all.Add("no FATX at 0x" + off.ToString("X")); continue; }
                    all.AddRange(((System.Collections.IEnumerable)ft.GetMethod("Check", Any).Invoke(v, null)).Cast<string>());
                }
                return all;
            }
            Dictionary<string, string> Hashes(IEnumerable<object> files)
            {
                using var sha = System.Security.Cryptography.SHA1.Create();
                return files.ToDictionary(f => (string)f.GetType().GetField("Item1").GetValue(f), f => BitConverter.ToString(sha.ComputeHash((byte[])f.GetType().GetField("Item2").GetValue(f))));
            }
            var r = new Random(11);
            byte[] Bytes(int n) { var b = new byte[n]; r.NextBytes(b); return b; }
            var first = new List<(string, byte[])> { ("TitleMeta.xbx", Encoding.Unicode.GetBytes("﻿TitleName=Probe Game\r\n")), ("TitleImage.xbx", Bytes(10240)), ("ABCDEF012345/SaveMeta.xbx", Bytes(40)), ("ABCDEF012345/game.sav", Bytes(1442)) };
            var firstZip = Path.Combine(work, "first.cxbxsave");
            st.GetMethod("Pack", Any).Invoke(null, new object[] { first, firstZip });

            S("Insert", console, baseDisk, "4d530004", firstZip);
            var backing = File.Exists(console) ? (string)_asm.GetType("LbIntegrations.Xemu.Qcow2Overlay", true).GetMethod("BackingName", Any).Invoke(null, new object[] { console }) : null;
            Check("a new console made, over the base (../base.qcow2)", backing == "../base.qcow2", backing);
            Check("... its five partitions clean", Problems().Count == 0, string.Join("; ", Problems().Take(4)));
            var back = Path.Combine(work, "back.cxbxsave");
            Check("... the save captured", (bool)S("Capture", console, "4d530004", back));
            Check("... the same bytes as the packed save", File.ReadAllBytes(back).SequenceEqual(File.ReadAllBytes(firstZip)));
            Check("... another title has none", ((System.Collections.IEnumerable)S("Extract", console, "41560003")).Cast<object>().Count() == 0);

            var rich = new List<(string, byte[])>(first) { ("ABCDEF012345/big.bin", Bytes(16384 * 3 + 5)), ("ABCDEF012345/deep/x/y.dat", Bytes(300)), ("FEDCBA987654/SaveMeta.xbx", Bytes(90)), ("FEDCBA987654/empty.dat", new byte[0]) };
            var richZip = Path.Combine(work, "rich.cxbxsave");
            st.GetMethod("Pack", Any).Invoke(null, new object[] { rich, richZip });
            var rewrite = new List<(string, byte[])>(first.Take(3)) { ("ABCDEF012345/game.sav", Bytes(1500)) };
            var rewriteZip = Path.Combine(work, "rewrite.cxbxsave");
            st.GetMethod("Pack", Any).Invoke(null, new object[] { rewrite, rewriteZip });
            S("Insert", console, baseDisk, "4d530004", richZip);
            var got = Hashes(((System.Collections.IEnumerable)S("Extract", console, "4d530004")).Cast<object>());
            var want = Hashes(rich.Select(x => (object)x));
            Check("a richer save over it: file for file", got.Count == want.Count && want.All(kv => got.TryGetValue(kv.Key, out var h) && h == kv.Value), got.Count + " vs " + want.Count);
            Check("... the volume clean", Problems().Count == 0, string.Join("; ", Problems().Take(4)));

            S("Insert", console, baseDisk, "4d530004", firstZip);
            S("Capture", console, "4d530004", back);
            Check("the first save back: the same bytes, nothing left of the richer", File.ReadAllBytes(back).SequenceEqual(File.ReadAllBytes(firstZip)));
            Check("... the volume clean (the freed clusters not leaked)", Problems().Count == 0, string.Join("; ", Problems().Take(4)));

            Check("removed", (bool)S("Remove", console, "4d530004"));
            Check("... no save left", !(bool)S("Capture", console, "4d530004", Path.Combine(work, "none.cxbxsave")) && !File.Exists(Path.Combine(work, "none.cxbxsave")));
            Check("... the volume clean", Problems().Count == 0, string.Join("; ", Problems().Take(4)));
            Check("... removing again: nothing to do", !(bool)S("Remove", console, "4d530004"));

            // THE ACTIVE SAVE (lbip-saves\<id>.cxbxsave) kept in step with the console (Shared.Xbox\XboxSaveSync, Mehdi 05/10):
            // whole contents compared, the stamp of the last agreement beside the game's disk, hdd\games\<id>.stamp.
            var exe = Path.Combine(work, "x.emu.exe");
            File.WriteAllBytes(exe, new byte[0]);
            var files = _asm.GetType("LbIntegrations.Xemu.XemuSaveFiles", true);
            object F(string m, params object[] a) { try { return files.GetMethod(m, Any).Invoke(null, a); } catch (TargetInvocationException ex) { throw ex.InnerException ?? ex; } }
            var sync = _asm.GetType("LbIntegrations.Xbox.XboxSaveSync", true);
            var modeType = _asm.GetType("LbIntegrations.Xbox.XboxSyncMode", true);
            const string tid = "4d530004";
            string Y(string mode) => (string)F("Sync", exe, tid, Enum.Parse(modeType, mode));
            string Hash(object list) => (string)sync.GetMethod("ContentHash", Any).Invoke(null, new[] { list });
            string OfZip(string zip) => Hash(sync.GetMethod("FilesOf", Any).Invoke(null, new object[] { zip }));
            string OfConsole() => Hash(S("Extract", console, tid));
            var active = (string)F("PackPath", exe, tid);
            var stampFile = Path.ChangeExtension(console, ".stamp");
            var conflicts = Path.Combine(work, "lbip-conflicts");
            File.Delete(console);
            Check("no file, no console: nothing to do at launch", Y("Launch") == null);
            Directory.CreateDirectory(Path.GetDirectoryName(active));
            File.Copy(firstZip, active, overwrite: true);
            Check("a file put in lbip-saves: laid into the console at launch", Y("Launch") is string s1 && s1.Contains("laid") && OfConsole() == OfZip(firstZip));
            Check("... its stamp beside the game's disk, nothing beside the save", File.Exists(stampFile)
                  && !Directory.GetFiles(Path.GetDirectoryName(active)).Any(f => f.EndsWith(".stamp") || f.EndsWith(".synced")));
            Check("... the next launch: nothing to do", Y("Launch") == null);
            S("Insert", console, baseDisk, tid, richZip);                        // the game saving during a session
            Check("after a session: the console captured into the file", Y("SessionEnd") is string s2 && s2.Contains("captured") && OfZip(active) == OfZip(richZip));
            Check("... the next launch: nothing to do", Y("Launch") == null && OfConsole() == OfZip(richZip));
            File.Copy(firstZip, active, overwrite: true);                        // a Restore: an older save put back
            Check("a file put back: a listing touches nothing", Y("Listing") == null && OfZip(active) == OfZip(firstZip) && OfConsole() == OfZip(richZip));
            Check("... laid into the console at launch", Y("Launch") is string s3 && s3.Contains("laid") && OfConsole() == OfZip(firstZip));
            S("Insert", console, baseDisk, tid, richZip);                        // a session cut short: never captured
            Check("the console changed, the save not: captured at launch", Y("Launch") is string s4 && s4.Contains("captured") && OfZip(active) == OfZip(richZip));
            File.Copy(firstZip, active, overwrite: true);                        // the save put back, and the console changes too
            S("Insert", console, baseDisk, tid, rewriteZip);
            Check("both changed: a listing touches nothing", Y("Listing") == null && OfConsole() == OfZip(rewriteZip));
            var conflict = Y("Launch");
            Check("... at launch the save wins, the console's version kept in lbip-conflicts", conflict != null && conflict.Contains("kept") && OfConsole() == OfZip(firstZip)
                  && Directory.GetFiles(conflicts, tid + "-console-*.cxbxsave").Length == 1
                  && OfZip(Directory.GetFiles(conflicts, tid + "-console-*.cxbxsave")[0]) == OfZip(rewriteZip), conflict);
            // Copied into another xemu's lbip-saves: nothing comes with it - laid in there.
            var other = Path.Combine(work, "other"); Directory.CreateDirectory(Path.Combine(other, "hdd"));
            File.Copy(baseDisk, Path.Combine(other, "hdd", "base.qcow2"), overwrite: true);
            var otherExe = Path.Combine(other, "x.emu.exe"); File.WriteAllBytes(otherExe, new byte[0]);
            var otherPack = (string)F("PackPath", otherExe, tid);
            Directory.CreateDirectory(Path.GetDirectoryName(otherPack));
            File.Copy(active, otherPack, overwrite: true);
            Check("a file copied into another xemu: laid in there", F("Sync", otherExe, tid, Enum.Parse(modeType, "Launch")) is string so && so.Contains("laid"));
            File.Delete(active);
            Check("the file removed: a listing does not make it again", (string)F("Capture", exe, tid) == null && !File.Exists(active));
            Check("the file removed: the console's save taken out at launch, the stamp gone", Y("Launch") is string s5 && s5.Contains("taken out")
                  && ((System.Collections.IEnumerable)S("Extract", console, tid)).Cast<object>().Count() == 0 && !File.Exists(stampFile));
            Check("... the console clean", Problems().Count == 0, string.Join("; ", Problems().Take(4)));
            S("Insert", console, baseDisk, tid, richZip);                        // a console with a save, no file, no stamp
            Check("a console with no file and no stamp: captured - never taken for a removed save", Y("Launch") is string s6 && s6.Contains("captured") && OfZip(active) == OfZip(richZip));
            // The notes of 04/10 beside the save: their agreement read once, then gone.
            File.Delete(stampFile);
            S("Insert", console, baseDisk, tid, rewriteZip);
            string sha1; using (var sh = System.Security.Cryptography.SHA1.Create()) using (var fs = File.OpenRead(active)) sha1 = Convert.ToHexString(sh.ComputeHash(fs));
            File.WriteAllText(Path.ChangeExtension(active, ".synced"), sha1 + "|" + Path.GetFullPath(active).ToLowerInvariant());
            File.WriteAllText(Path.ChangeExtension(active, ".stamp"), "1|2");
            var before = Directory.GetFiles(conflicts).Length;
            Check("a save of 04/10 agreed in its notes, the console newer: captured, not a conflict", Y("Launch") is string s7 && s7.Contains("captured")
                  && OfZip(active) == OfZip(rewriteZip) && Directory.GetFiles(conflicts).Length == before);
            Check("... the old notes gone", !File.Exists(Path.ChangeExtension(active, ".synced")) && !File.Exists(Path.ChangeExtension(active, ".stamp")) && File.Exists(stampFile));
            // The keys in the console's stamp (Codex's review, 05/10): noted at a launch, written into what a capture makes, given to
            // a conflict's copy; a save written whole; a restore always put in.
            {
                var keysType = _asm.GetType("LbIntegrations.Xbox.XboxSaveKeys", true);
                object KeysOf(string zip) => keysType.GetMethod("Read", Any).Invoke(null, new object[] { zip });
                byte[] Kf(object k, string f) => k?.GetType().GetField(f, Any)?.GetValue(k) as byte[];
                var side = F("Side", exe, tid);
                void Note(byte[] h, byte[] c) => sync.GetMethod("NoteSessionKeys", Any).Invoke(null, new[] { side, h, c });
                var k1h = Enumerable.Repeat((byte)0x21, 16).ToArray(); var k1c = Enumerable.Repeat((byte)0x31, 16).ToArray();
                var k2h = Enumerable.Repeat((byte)0x22, 16).ToArray(); var k2c = Enumerable.Repeat((byte)0x32, 16).ToArray();
                File.Copy(firstZip, active, overwrite: true);
                Y("Launch");                                                     // agreed on first
                Note(k1h, k1c);                                                  // the session runs with K1
                S("Insert", console, baseDisk, tid, richZip);                    // and saves
                Check("a capture writes the keys the session was noted with", Y("SessionEnd") is string c1 && c1.Contains("captured")
                      && Kf(KeysOf(active), "Hdd").SequenceEqual(k1h) && Kf(KeysOf(active), "Cert").SequenceEqual(k1c));
                Check("... the stamp holds them, two lines", File.ReadAllText(stampFile).Contains("content=") && File.ReadAllText(stampFile).Contains("keys=lbip-xbox-keys/1 hdd=" + Convert.ToHexString(k1h)));

                // Codex 1: a conflict's copy carries the keys of what the console holds.
                S("Insert", console, baseDisk, tid, rewriteZip);                 // the console changes (a session cut short)
                var other2 = Path.Combine(work, "k2.cxbxsave");
                File.Copy(firstZip, other2, overwrite: true);
                keysType.GetMethod("Write", Any).Invoke(null, new object[] { other2, k2h, k2c });
                File.Copy(other2, active, overwrite: true);                      // and a save with other keys put there
                var seen = Directory.GetFiles(conflicts).ToList();
                Check("both changed: the save wins at launch", Y("Launch") is string c2 && c2.Contains("kept") && OfConsole() == OfZip(firstZip));
                var copy = Directory.GetFiles(conflicts).Except(seen).SingleOrDefault();
                Check("... the conflict's copy: the console's files, with the console's keys (K1), not the save's (K2)",
                      copy != null && OfZip(copy) == OfZip(rewriteZip) && Kf(KeysOf(copy), "Hdd").SequenceEqual(k1h) && Kf(KeysOf(copy), "Cert").SequenceEqual(k1c));
                Check("... the stamp now holds the keys of the save laid in (K2)", File.ReadAllText(stampFile).Contains("hdd=" + Convert.ToHexString(k2h)));

                // Codex 2: a save is written whole - a capture that cannot write leaves the save and the stamp as they were.
                S("Insert", console, baseDisk, tid, richZip);
                var saveBefore = File.ReadAllBytes(active); var stampBefore = File.ReadAllText(stampFile);
                File.SetAttributes(active, FileAttributes.ReadOnly);
                bool threw = false;
                try { Y("SessionEnd"); } catch { threw = true; }
                File.SetAttributes(active, FileAttributes.Normal);
                Check("a capture that cannot write: the save and the stamp as they were", threw && File.ReadAllBytes(active).SequenceEqual(saveBefore) && File.ReadAllText(stampFile) == stampBefore
                      && !File.Exists(active + ".part"));
                Check("... the next one writes it, keys and all", Y("SessionEnd") is string c3 && c3.Contains("captured") && OfZip(active) == OfZip(richZip) && Kf(KeysOf(active), "Hdd").SequenceEqual(k2h));

                // Codex 3: a restore of the agreed save over a console changed since (a session cut short) - put in, not undone.
                // A Restore only places the file and notes it (Mehdi, 05/10): the launch lays it in.
                void Restored(string zip) { File.Copy(zip, active, overwrite: true); sync.GetMethod("MarkRestored", Any).Invoke(null, new[] { side }); }
                var agreed = Path.Combine(work, "agreed.cxbxsave"); File.Copy(active, agreed, overwrite: true);
                S("Insert", console, baseDisk, tid, rewriteZip);                 // played, the host killed: never captured
                Restored(agreed);                                                // LaunchBox restores the save of the last agreement
                Check("a restore: the console untouched, noted in the stamp", OfConsole() == OfZip(rewriteZip) && File.ReadAllText(stampFile).Contains("restore=1"));
                Check("... a listing and a session's end leave the restored file alone", Y("Listing") == null && Y("SessionEnd") == null && OfZip(active) == OfZip(agreed));
                seen = Directory.GetFiles(conflicts).ToList();
                Check("... at launch: laid in, the console kept apart", Y("Launch") is string c4 && c4.Contains("restored")
                      && OfConsole() == OfZip(agreed) && Directory.GetFiles(conflicts).Except(seen).Count() == 1);
                Check("... the note gone", !File.ReadAllText(stampFile).Contains("restore="));
                // ... and over a console that is the last agreement: laid in, no copy (it is in LaunchBox's backups).
                Restored(firstZip);
                seen = Directory.GetFiles(conflicts).ToList();
                Check("a restore over a console as agreed: laid in at launch, no copy", Y("Launch") is string c5 && c5.Contains("restored") && OfConsole() == OfZip(firstZip)
                      && Directory.GetFiles(conflicts).Except(seen).Count() == 0);
                // ... and with no stamp at all: noted, laid in at launch.
                File.Delete(stampFile);
                Restored(agreed);
                Check("a restore with no stamp: noted, laid in at launch", File.ReadAllText(stampFile).Trim() == "restore=1" && Y("Listing") == null
                      && Y("Launch") is string c6 && c6.Contains("restored") && OfConsole() == OfZip(agreed), File.Exists(stampFile) ? File.ReadAllText(stampFile) : "no stamp");
            }
            // The console deleted by hand, its stamp left: the save is kept at a listing, laid into a new console at the launch.
            File.Copy(firstZip, active, overwrite: true);
            Y("Launch");
            Check("(agreed, its stamp there)", File.Exists(stampFile) && File.Exists(console));
            File.Delete(console);
            Check("the console deleted: a listing keeps the save", Y("Listing") == null && File.Exists(active) && OfZip(active) == OfZip(firstZip));
            Check("... the launch lays it into a new console", Y("Launch") is string sd && sd.Contains("laid") && File.Exists(console) && OfConsole() == OfZip(firstZip));
            File.Delete(active); File.Delete(stampFile);

            var escape = Path.Combine(work, "escape.cxbxsave");
            st.GetMethod("Pack", Any).Invoke(null, new object[] { new List<(string, byte[])> { ("../outside.bin", new byte[] { 1 }) }, escape });
            bool refused = false;
            try { S("Insert", console, baseDisk, "4d530004", escape); } catch (InvalidDataException) { refused = true; }
            Check("a save that would leave its folder: refused", refused);
            Check("the base never written", File.ReadAllBytes(baseDisk).SequenceEqual(File.ReadAllBytes(realBase)));
        }

        /// <summary>The session's xemu.toml and its merge back: made from a user's file, rewritten the way xemu rewrites it on exit
        /// (a pad added, a binding changed, the window resized, a table new), merged back - untouched tables whole, touched keys
        /// the user's, the rest the session's.</summary>
        /// <summary>The disc by one path: a symbolic link made and read through as the disc, the relative path given; made again
        /// over a link left behind; a real file at its place left alone (the disc's own path given); taken away, the link alone.
        /// Without the right to make a link (no developer mode), only the fallback is checked.</summary>
        private static void DiscLink(string work)
        {
            Directory.CreateDirectory(work);
            var exe = Path.Combine(work, "x.emu.exe"); File.WriteAllBytes(exe, new byte[0]);
            var discA = Path.Combine(work, "a.iso"); File.WriteAllBytes(discA, new byte[4096]);
            var discB = Path.Combine(work, "b's disc.iso"); File.WriteAllBytes(discB, new byte[8192]);
            var link = Path.Combine(work, "lbip-disc", "game.iso");
            string For(string disc) => (string)Call("XemuDiscLink", "For", exe, disc);
            var got = For(discA);
            if (got == discA) { Check("no link here (no developer mode?): the disc's own path", !File.Exists(link)); return; }
            Check("the relative path given", got == @"lbip-disc\game.iso", got);
            Check("... a link to the disc, read through as it", new FileInfo(link).LinkTarget == discA && new FileStream(link, FileMode.Open, FileAccess.Read, FileShare.ReadWrite).Length == 4096);
            Check("made again over the one left behind", For(discB) == @"lbip-disc\game.iso" && new FileInfo(link).LinkTarget == discB);
            Call("XemuDiscLink", "Remove", exe);
            Check("taken away: the link gone, its disc there", !File.Exists(link) && new FileInfo(link).LinkTarget == null && File.Exists(discB) && new FileInfo(discB).Length == 8192);
            File.WriteAllBytes(link, new byte[16]);                     // a file of the user's at its place
            Check("a real file at its place: left alone, the disc's own path", For(discA) == discA && new FileInfo(link).LinkTarget == null && new FileInfo(link).Length == 16);
            Call("XemuDiscLink", "Remove", exe);
            Check("... and never taken away", File.Exists(link) && new FileInfo(link).Length == 16);
        }

        private static void SessionToml(string work)
        {
            // A string with a ' (a game "Tom Clancy's ..."): a basic string, escaped, read back as it was - measured 05/10, xemu
            // v0.8.136 reads it and writes it back the same way.
            {
                var toml = T("XemuToml");
                string Lit(string s) => (string)toml.GetMethod("Literal", Any).Invoke(null, new object[] { s });
                string Txt(string s) => (string)toml.GetMethod("Text", Any).Invoke(null, new object[] { s });
                var path = "G:\\xboxoriginal\\Tom Clancy's \"Splinter\" Cell.iso";
                Check("a plain path stays a literal string", Lit("G:\\a b\\c.iso") == "'G:\\a b\\c.iso'");
                Check("a path with a ': a basic string, escaped", Lit(path) == "\"G:\\\\xboxoriginal\\\\Tom Clancy's \\\"Splinter\\\" Cell.iso\"", Lit(path));
                Check("... read back as it was", Txt(Lit(path)) == path, Txt(Lit(path)));
                Check("... as xemu writes it back", Txt("\"G:\\\\xboxoriginal\\\\lbip-test's disc.iso\"") == "G:\\xboxoriginal\\lbip-test's disc.iso");
                var quoted = T("XemuTomlDoc").GetMethod("Parse", Any).Invoke(null, new object[] { "[sys.files]\r\ndvd_path = " + Lit(path) + "\r\nhdd_path = 'x'\r\n" });
                Check("... and a file holding it reads whole", Txt((string)quoted.GetType().GetMethod("Get", Any).Invoke(quoted, new object[] { "sys.files", "dvd_path" })) == path
                      && Txt((string)quoted.GetType().GetMethod("Get", Any).Invoke(quoted, new object[] { "sys.files", "hdd_path" })) == "x");
            }
            var user = Path.Combine(work, "xemu.toml");
            var session = Path.Combine(work, "xemu-session.toml");
            var userText = string.Join("\r\n", new[]
            {
                "[general]", "show_welcome = false", "games_dir = 'G:\\xboxoriginal'", "",
                "[input]", "gamepad_mappings = [", "    { gamepad_id = 'AAA'}", "    ]", "",
                "[input.bindings]", "port1_driver = 'usb-xbox-gamepad'", "port1 = 'AAA'", "",
                "[display]", "renderer = 'OPENGL' # the user's", "",
                "[display.window]", "last_width = 1280", "",
                "[sys.files]", "bootrom_path = 'C:\\x\\mcpx.bin'", "hdd_path = 'C:\\x\\hdd\\standalone.qcow2'", "dvd_path = 'G:\\a [weird] path.iso'", ""
            });
            File.WriteAllText(user, userText);
            var userBytes = File.ReadAllBytes(user);
            var cfg = _asm.GetType("LbIntegrations.Xemu.XemuSessionConfig", true);
            var doc = _asm.GetType("LbIntegrations.Xemu.XemuTomlDoc", true);
            object Load(string f) => doc.GetMethod("Load", Any).Invoke(null, new object[] { f });
            string G(string f, string t, string k) => (string)doc.GetMethod("Get", Any).Invoke(Load(f), new object[] { t, k });
            var set = new List<(string, string, string)>
            {
                ("general", "show_welcome", "false"), ("display", "renderer", "'VULKAN'"), ("display.quality", "surface_scale", "2"),
                ("display.ui", "show_menubar", "false"), ("sys.files", "hdd_path", "'C:\\x\\hdd\\games\\4d530004.qcow2'"), ("sys.files", "dvd_path", "'Z:\\game.iso'"),
            };
            cfg.GetMethod("Make", Any).Invoke(null, new object[] { user, session, set, null });
            Check("the session's file: the game's renderer, scale and menu bar", G(session, "display", "renderer") == "'VULKAN'" && G(session, "display.quality", "surface_scale") == "2" && G(session, "display.ui", "show_menubar") == "false");
            Check("... its console and disc", G(session, "sys.files", "hdd_path") == "'C:\\x\\hdd\\games\\4d530004.qcow2'" && G(session, "sys.files", "dvd_path") == "'Z:\\game.iso'");
            Check("... the user's pad kept in it", G(session, "input.bindings", "port1") == "'AAA'" && G(session, "input", "gamepad_mappings").Contains("'AAA'"));
            Check("the user's file not written", File.ReadAllBytes(user).SequenceEqual(userBytes));
            Check("a path with brackets read as a value, not a table", G(user, "sys.files", "dvd_path") == "'G:\\a [weird] path.iso'");

            // xemu's exit: its whole configuration written back into the session's file, things changed meanwhile.
            File.WriteAllText(session, string.Join("\n", new[]
            {
                "[general]", "show_welcome = false", "games_dir = 'G:\\xboxoriginal'", "last_viewed_menu_index = 3", "",
                "[input]", "gamepad_mappings = [", "    { gamepad_id = 'AAA'},", "    { gamepad_id = 'BBB', controller_mapping = { a = 1 } }", "    ]", "",
                "[input.bindings]", "port1_driver = 'usb-xbox-gamepad'", "port1 = 'AAA'", "port2_driver = 'usb-xbox-gamepad'", "port2 = 'BBB'", "",
                "[display]", "renderer = 'VULKAN'", "",
                "[display.quality]", "surface_scale = 2", "",
                "[display.window]", "last_width = 1920", "",
                "[display.ui]", "show_menubar = false", "use_animations = false", "",
                "[audio]", "hrtf = false", "",
                "[sys.files]", "bootrom_path = 'C:\\x\\mcpx.bin'", "hdd_path = 'C:\\x\\hdd\\games\\4d530004.qcow2'", "dvd_path = 'Z:\\game.iso'", ""
            }));
            bool changed = (bool)cfg.GetMethod("MergeBack", Any).Invoke(null, new object[] { user, session });
            Check("merged back: the user's file changed", changed);
            Check("untouched tables whole: the new pad and its binding kept", G(user, "input", "gamepad_mappings").Contains("'BBB'") && G(user, "input.bindings", "port2") == "'BBB'");
            Check("... the window's new width kept ([display.window], untouched)", G(user, "display.window", "last_width") == "1920");
            Check("... a new table kept ([audio])", G(user, "audio", "hrtf") == "false");
            Check("touched tables the user's, whole: the renderer back, comment and all", G(user, "display", "renderer") == "'OPENGL' # the user's", G(user, "display", "renderer"));
            Check("... the user's console and disc back", G(user, "sys.files", "hdd_path") == "'C:\\x\\hdd\\standalone.qcow2'" && G(user, "sys.files", "dvd_path") == "'G:\\a [weird] path.iso'" && G(user, "sys.files", "bootrom_path") == "'C:\\x\\mcpx.bin'");
            Check("... a table the user never had gone ([display.quality], [display.ui])", !File.ReadAllText(user).Contains("[display.quality]") && !File.ReadAllText(user).Contains("[display.ui]"));
            Check("... a change made in xemu in a touched table not kept ([general]'s menu index, [display.ui]'s animations)", G(user, "general", "last_viewed_menu_index") == null && G(user, "display.ui", "use_animations") == null);
            Check("... [general] as the user had it", G(user, "general", "games_dir") == "'G:\\xboxoriginal'" && G(user, "general", "show_welcome") == "false");
            Check("the session's files removed", !File.Exists(session) && !File.Exists(Path.ChangeExtension(session, ".keys")));
            Check("nothing left: a second merge does nothing", !(bool)cfg.GetMethod("MergeBack", Any).Invoke(null, new object[] { user, session }));

            // A session xemu did not change: the merge gives the user's file back as it was.
            var before = File.ReadAllText(user);
            cfg.GetMethod("Make", Any).Invoke(null, new object[] { user, session, set, null });
            Check("a session xemu left as it was: the user's file unchanged", !(bool)cfg.GetMethod("MergeBack", Any).Invoke(null, new object[] { user, session }) && File.ReadAllText(user) == before);

            // A session file without its keys (cut while being made): nothing of it is the user's.
            File.WriteAllText(session, "[display]\nrenderer = 'NULL'\n");
            Check("a half-made session: discarded, the user's file untouched", !(bool)cfg.GetMethod("MergeBack", Any).Invoke(null, new object[] { user, session }) && File.ReadAllText(user) == before && !File.Exists(session));
        }

        /// <summary>The options' levels and their TOML: a game's own over every game's over the default over xemu's own, "xemu" meaning
        /// xemu's own; the xemu settings among them written as TOML values of their kind; xemu's own read from a toml or its default.</summary>
        private static void Options(string work)
        {
            var settings = Path.Combine(work, "settings");
            Directory.CreateDirectory(Path.Combine(settings, "games"));
            File.WriteAllLines(Path.Combine(settings, "settings.ini"), new[] { "opt.display.renderer=VULKAN", "opt.display.surface_scale=xemu", "opt.display.vsync=off", "cache_gb=12" });
            File.WriteAllLines(Path.Combine(settings, "games", "game-a.ini"), new[] { "opt.display.renderer=OPENGL", "opt.console.region=4", "opt.display.surface_scale=3", "opt.disc.media_patch=off" });
            var xo = _asm.GetType("LbIntegrations.Xemu.XemuOptions", true);
            Dictionary<string, string> Eff(string id) => (Dictionary<string, string>)xo.GetMethod("Effective", Any).Invoke(null, new object[] { id });
            var a = Eff("game-a"); var b = Eff("game-b");
            string V(Dictionary<string, string> d, string k) => d.TryGetValue(k, out var v) ? v : null;
            Check("a game's own over every game's (renderer OpenGL over Vulkan)", V(a, "display.renderer") == "OPENGL" && V(b, "display.renderer") == "VULKAN");
            Check("... over the default (region Europe over follow)", V(a, "console.region") == "4" && V(b, "console.region") == "follow");
            Check("\"xemu\" at every game's: xemu's own, absent - unless the game sets it", V(b, "display.surface_scale") == null && V(a, "display.surface_scale") == "3");
            Check("the defaults: menu bar off, media patch on, HDD key the pack's", V(b, "display.menubar") == "off" && V(b, "disc.media_patch") == "on" && V(b, "console.hddkey") == "pack");
            Check("a game's media patch off", V(a, "disc.media_patch") == "off");
            var toml = ((System.Collections.IEnumerable)xo.GetMethod("TomlOf", Any).Invoke(null, new object[] { a })).Cast<object>()
                       .Select(t => ((string)t.GetType().GetField("Item1").GetValue(t), (string)t.GetType().GetField("Item2").GetValue(t), (string)t.GetType().GetField("Item3").GetValue(t))).ToList();
            Check("as TOML: a string quoted, a number bare, a switch true/false", toml.Contains(("display", "renderer", "'OPENGL'")) && toml.Contains(("display.quality", "surface_scale", "3"))
                  && toml.Contains(("display.window", "vsync", "false")) && toml.Contains(("display.ui", "show_menubar", "false")), string.Join(" ", toml));
            Check("... the plugin's own options not in it (console, disc)", toml.All(t => !t.Item1.StartsWith("console") && !t.Item1.StartsWith("disc")));
            var doc = _asm.GetType("LbIntegrations.Xemu.XemuTomlDoc", true);
            var parsed = doc.GetMethod("Parse", Any).Invoke(null, new object[] { "[display]\nrenderer = 'VULKAN'\n" });
            object Opt(string k) => xo.GetMethod("Find", Any).Invoke(null, new object[] { k });
            string Own(string k, object d) => (string)xo.GetMethod("OwnOf", Any).Invoke(null, new object[] { Opt(k), d });
            Check("xemu's own: its toml's value, else its default", Own("display.renderer", parsed) == "VULKAN" && Own("display.renderer", null) == "OPENGL" && Own("display.vsync", null) == "on" && Own("display.surface_scale", null) == "1",
                  Own("display.renderer", parsed) + " " + Own("display.renderer", null) + " " + Own("display.vsync", null) + " " + Own("display.surface_scale", null));
            var gpus = (List<string>)xo.GetMethod("DisplayAdapters", Any).Invoke(null, null);
            Console.WriteLine("      graphics cards: " + string.Join(", ", gpus));
            Check("the graphics cards listed, none virtual", gpus.Count > 0 && gpus.All(g => !g.ToLowerInvariant().Contains("virtual") && !g.ToLowerInvariant().Contains("parsec")), string.Join(", ", gpus));
            File.Delete(Path.Combine(settings, "settings.ini"));
            File.Delete(Path.Combine(settings, "games", "game-a.ini"));
        }

        /// <summary>--xemu-shot &lt;png&gt; [--rom &lt;game&gt;]: the Nixx window's xemu tab and a game's options window, drawn off screen and laid
        /// side by side - every scroll of each.</summary>
        public static bool Shot(Assembly asm, string outPath, string rom)
        {
            _asm = asm;
            var work = Path.Combine(Path.GetTempPath(), "lbip-xemu-shot");
            Directory.CreateDirectory(work);
            T("XemuSettings").GetField("DirOverride", Any).SetValue(null, work);
            var shots = new List<System.Drawing.Bitmap>();
            void Grab(System.Windows.Forms.Form form, System.Windows.Forms.Panel scroll)
            {
                for (int y = 0, last = -1, n = 0; n < 8; y += scroll.ClientSize.Height - 40, n++)
                {
                    scroll.AutoScrollPosition = new System.Drawing.Point(0, y);
                    System.Windows.Forms.Application.DoEvents();
                    if (-scroll.AutoScrollPosition.Y == last) break;
                    last = -scroll.AutoScrollPosition.Y;
                    var bmp = new System.Drawing.Bitmap(form.Width, form.Height);
                    form.DrawToBitmap(bmp, new System.Drawing.Rectangle(0, 0, bmp.Width, bmp.Height));
                    shots.Add(bmp);
                }
            }
            var page = (System.Windows.Forms.Control)_asm.GetType("LbIntegrations.Xemu.Settings", true).GetMethod("CreatePage").Invoke(null, null);
            using (var form = new System.Windows.Forms.Form { ClientSize = new System.Drawing.Size(640, 760), Font = new System.Drawing.Font("Segoe UI", 9f), Text = "xemu tab", StartPosition = System.Windows.Forms.FormStartPosition.Manual, Location = new System.Drawing.Point(-4000, -4000) })
            {
                page.Dock = System.Windows.Forms.DockStyle.Fill;
                form.Controls.Add(page);
                form.Show();
                System.Windows.Forms.Application.DoEvents();
                Grab(form, page.Controls.OfType<System.Windows.Forms.Panel>().First(p => p.AutoScroll));
                form.Close();
            }
            var games = new List<Unbroken.LaunchBox.Plugins.Data.IGame> { StubGame.Create("probe-shot", rom != null ? Path.GetFileNameWithoutExtension(rom) : "No game", rom ?? "C:\\none.iso") };
            var formType = _asm.GetType("LbIntegrations.Xemu.XemuGameForm", true);
            using (var form = (System.Windows.Forms.Form)Activator.CreateInstance(formType, Any, null, new object[] { games }, null))
            {
                form.StartPosition = System.Windows.Forms.FormStartPosition.Manual;
                form.Location = new System.Drawing.Point(-4000, -4000);
                form.Show();
                System.Windows.Forms.Application.DoEvents();
                Grab(form, form.Controls.OfType<System.Windows.Forms.Panel>().First(p => p.AutoScroll));
                form.Close();
            }
            using var all = new System.Drawing.Bitmap(shots.Sum(s => s.Width), shots.Max(s => s.Height));
            using (var g = System.Drawing.Graphics.FromImage(all))
            {
                g.Clear(System.Drawing.Color.DimGray);
                int x = 0;
                foreach (var s in shots) { g.DrawImage(s, x, 0); x += s.Width; s.Dispose(); }
            }
            all.Save(outPath, System.Drawing.Imaging.ImageFormat.Png);
            Console.WriteLine("  " + outPath + " (" + shots.Count + " views)");
            return true;
        }

        /// <summary>--xemu-launch --emu &lt;xemu.exe of a lab&gt; --rom &lt;game&gt; [--seconds n]: the plugin's whole launch, for real - its
        /// PrepareEmulatorForLaunch (disc served, console made, session EEPROM and xemu.toml, the line), xemu started with that line
        /// (the game's path after it, as the host appends it), closed the normal way after n seconds, then the plugin's session end
        /// (merge back, stand-alone console, save capture) waited for. The game's options: Vulkan on the RTX 3060. STARTS XEMU.</summary>
        public static bool Launch(Assembly asm, string exe, string rom, int seconds, string lbRoot = null)
        {
            _asm = asm;
            // The LaunchBox root, for the plugin's own copy of the RAM disk code: its helper, its task - a disc then served by AIM.
            if (lbRoot != null) asm.GetType("LbIntegrations.RamDisk.RamDiskHost", true).GetMethod("UseRoot", Any).Invoke(null, new object[] { lbRoot });
            var pluginType = asm.GetType("LbIntegrations.Xemu.XemuPlugin", true);
            var dir = Path.GetDirectoryName(exe);
            var settings = Path.Combine(dir, "probe-settings");
            Directory.CreateDirectory(Path.Combine(settings, "games"));
            T("XemuSettings").GetField("DirOverride", Any).SetValue(null, settings);
            File.WriteAllLines(Path.Combine(settings, "games", "probe-launch.ini"), new[] { "opt.display.renderer=VULKAN", "opt.display.gpu=NVIDIA GeForce RTX 3060" });
            var userToml = Path.Combine(dir, "xemu.toml");
            var userBefore = File.ReadAllText(userToml);

            var plugin = (Unbroken.LaunchBox.Plugins.EmulatorPlugin)Activator.CreateInstance(pluginType);
            var emu = new StubEmulator { Title = "Nixx-Xemu", ApplicationPath = exe };
            var game = StubGame.Create("probe-launch", Path.GetFileNameWithoutExtension(rom), rom, emu.Id);
            var watch = System.Diagnostics.Stopwatch.StartNew();
            var r = plugin.PrepareEmulatorForLaunch(new Unbroken.LaunchBox.Plugins.PrepareForLaunchArgs(emu, game, "-full-screen -dvd_path"));
            Console.WriteLine("  prepared in " + watch.ElapsedMilliseconds + " ms: success " + r?.WasSuccess + "\n  line: " + r?.NewCommandLine);
            if (r == null || !r.WasSuccess) return false;
            var session = Path.Combine(dir, "xemu-session.toml");
            Console.WriteLine("  session toml: " + File.Exists(session) + "; the user's untouched: " + (File.ReadAllText(userToml) == userBefore));
            if (File.Exists(session))
                foreach (var l in File.ReadAllLines(session).Where(l => l.StartsWith("renderer") || l.StartsWith("preferred_physical") || l.StartsWith("show_menubar") || l.Contains("_path")))
                    Console.WriteLine("    " + l);

            var psi = new System.Diagnostics.ProcessStartInfo(exe, r.NewCommandLine) { UseShellExecute = false, WorkingDirectory = dir };
            using var p = System.Diagnostics.Process.Start(psi);
            Console.WriteLine("  xemu started, pid " + p.Id + "; " + seconds + " s");
            var served = r.NewCommandLine.Contains("game.iso") ? r.NewCommandLine.Split('"').FirstOrDefault(s => s.EndsWith("game.iso")) : null;
            Console.WriteLine("  disc served where it is: " + (served ?? "no (a copy)"));
            System.Threading.Thread.Sleep(seconds * 1000);
            p.CloseMainWindow();
            if (!p.WaitForExit(15000)) { Console.WriteLine("  xemu did not close - killed"); p.Kill(); p.WaitForExit(); }
            Console.WriteLine("  xemu closed (exit " + p.ExitCode + ")");
            // The plugin's session end runs on its own thread: waited for, by its traces - the session's files gone.
            for (int i = 0; i < 60 && File.Exists(session); i++) System.Threading.Thread.Sleep(500);
            System.Threading.Thread.Sleep(3000);
            Console.WriteLine("  session merged back and removed: " + !File.Exists(session));
            var after = File.ReadAllText(userToml);
            Console.WriteLine("  the user's xemu.toml: " + (after == userBefore ? "as it was" : "changed:"));
            if (after != userBefore) foreach (var l in after.Split('\n')) Console.WriteLine("    | " + l.TrimEnd('\r'));
            if (served != null) Console.WriteLine("  the served disc detached: " + !File.Exists(served));
            var pack = Path.Combine(dir, "lbip-saves");
            Console.WriteLine("  saves captured: " + (Directory.Exists(pack) ? string.Join(", ", Directory.GetFiles(pack, "*.cxbxsave").Select(Path.GetFileName)) : "none"));
            return true;
        }

        /// <summary>--xemu-session make|merge --user &lt;xemu.toml&gt; --session &lt;session toml&gt;: the plugin's session file made from
        /// the user's (renderer Vulkan, menu bar hidden, scale 2 - as a game's options would), or merged back - for a real xemu run
        /// between the two.</summary>
        public static bool SessionStep(Assembly asm, string step, string user, string session)
        {
            _asm = asm;
            var cfg = _asm.GetType("LbIntegrations.Xemu.XemuSessionConfig", true);
            if (step == "make")
            {
                var set = new List<(string, string, string)> { ("display", "renderer", "'VULKAN'"), ("display.ui", "show_menubar", "false"), ("display.quality", "surface_scale", "2") };
                cfg.GetMethod("Make", Any).Invoke(null, new object[] { user, session, set, null });
                Console.WriteLine("  made " + session);
                return true;
            }
            bool changed = (bool)cfg.GetMethod("MergeBack", Any).Invoke(null, new object[] { user, session });
            Console.WriteLine("  merged back: " + (changed ? "the user's file changed" : "nothing to change"));
            return true;
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
                var exe = Path.Combine(emu, "x.emu.exe");
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

                // 4b. the media patch (extract-xiso's rule): a game whose default.xbe and a second .xbe hold the pattern
                Console.WriteLine("  media patch");
                MediaPatch(work, exe);

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
                Check("a path with a ': written as a basic string, not refused (05/10)", (string)Call("XemuToml", "Literal", @"C:\it's\x") == "\"C:\\\\it's\\\\x\"");
                Check("LF only", !File.ReadAllText(toml).Contains("\r"));
                File.WriteAllText(toml, "# xemu's\r\n[general]\r\nshow_welcome = true\r\n[display]\r\nrenderer = 'VULKAN'\r\n");
                Call("XemuToml", "Set", toml, "general", "show_welcome", "false");
                Check("xemu's own file: the rest kept", File.ReadAllText(toml) == "# xemu's\n[general]\nshow_welcome = false\n[display]\nrenderer = 'VULKAN'\n", File.ReadAllText(toml));

                Console.WriteLine("  options");
                Options(work);

                Console.WriteLine("  xemu.toml for a session");
                var sessionWork = Path.Combine(work, "session");
                Directory.CreateDirectory(sessionWork);
                SessionToml(sessionWork);

                Console.WriteLine("  one path for the disc (XemuDiscLink)");
                DiscLink(Path.Combine(work, "disclink"));

                // 7. the launch line
                Console.WriteLine("  launch line");
                string Line(string current, string rom = null) => (string)Call("XemuPlugin", "CommandLineFor", current, @"C:\d\g.iso", rom);
                Check("the default", Line("-full-screen -dvd_path") == "-full-screen -dvd_path \"C:\\d\\g.iso\"", Line("-full-screen -dvd_path"));
                Check("empty: full screen added", Line("") == "-full-screen -dvd_path \"C:\\d\\g.iso\"", Line(""));
                Check("options kept with their values", Line("-machine xbox,short-animation=on -dvd_path") == "-full-screen -machine xbox,short-animation=on -dvd_path \"C:\\d\\g.iso\"", Line("-machine xbox,short-animation=on -dvd_path"));
                Check("an old -dvd_path value dropped", Line("-full-screen -dvd_path \"E:\\old game.iso\"") == "-full-screen -dvd_path \"C:\\d\\g.iso\"", Line("-full-screen -dvd_path \"E:\\old game.iso\""));
                Check("the game's own path dropped", Line("-full-screen \"G:\\Xbox\\My Game.iso\"", @"G:\Xbox\My Game.iso") == "-full-screen -dvd_path \"C:\\d\\g.iso\"", Line("-full-screen \"G:\\Xbox\\My Game.iso\"", @"G:\Xbox\My Game.iso"));
                Check("an -L of an older line dropped", Line("-full-screen -dvd_path -L") == "-full-screen -dvd_path \"C:\\d\\g.iso\"", Line("-full-screen -dvd_path -L"));
                Check("a valued option with spaces requoted", Line("-config_path \"C:\\my cfg\\x.toml\"") == "-full-screen -config_path \"C:\\my cfg\\x.toml\" -dvd_path \"C:\\d\\g.iso\"", Line("-config_path \"C:\\my cfg\\x.toml\""));

                // 7b. the console's EEPROM
                Console.WriteLine("  EEPROM");
                var eepromWork = Path.Combine(work, "eeprom");
                Directory.CreateDirectory(eepromWork);
                Eeprom(eepromWork, Environment.GetEnvironmentVariable("LBIP_XEMU_EEPROM"));

                // 7b'. the certificate key of a session, on a copy of the user's flash BIOS (not shipped: LBIP_XEMU_BIOS, the bios\ folder)
                Console.WriteLine("  certificate key");
                CertKey(work, Environment.GetEnvironmentVariable("LBIP_XEMU_BIOS"));
                Console.WriteLine("  the keys a save carries");
                SaveKeysCheck(work);
                Console.WriteLine("  your console's seed");
                SeedCheck(Environment.GetEnvironmentVariable("LBIP_XEMU_BIOS"));

                // 7c. the saves, in Cxbx's format, on a copy of xemu's dashboard disk (not shipped: LBIP_XEMU_BASE)
                Console.WriteLine("  saves (Cxbx's format)");
                SavesCycle(work, Environment.GetEnvironmentVariable("LBIP_XEMU_BASE"));

                // 8. whose install it is
                Console.WriteLine("  ours");
                Check("an xemu without the marker is not ours", !(bool)Call("XemuPaths", "IsOurs", exe));
                var old = Path.Combine(emu, "xemu.exe");
                File.WriteAllBytes(old, new byte[] { 1 });
                Check("an old-named xemu without the marker: left alone", Call("XemuPaths", "MigrateOldName", old) == null && File.Exists(old));
                File.WriteAllText(Path.Combine(emu, "lbip-xemu-build.txt"), "v0");
                Check("x.emu.exe with the marker is ours", (bool)Call("XemuPaths", "IsOurs", exe));
                Check("xemu.exe with the marker is not ours (Unbroken's plugin claims it)", !(bool)Call("XemuPaths", "IsOurs", old));
                Check("an old-named install of ours: renamed x.emu.exe", Call("XemuPaths", "MigrateOldName", old) is string moved
                      && string.Equals(moved, exe, StringComparison.OrdinalIgnoreCase) && !File.Exists(old) && File.ReadAllBytes(exe).SequenceEqual(new byte[] { 1 }));
                Check("... x.emu.exe itself: nothing to do", Call("XemuPaths", "MigrateOldName", exe) == null);

                // xemu opened on its own (XemuConsoleBoot): xemu.toml the session's for that time, yours put back after.
                Console.WriteLine("  opened on its own");
                var ownToml = Path.Combine(emu, "xemu.toml");
                File.WriteAllText(ownToml, "[general]\nshow_welcome = false\n\n[input]\nport1 = 'pad'\n");
                Call("XemuConsoleBoot", "BeforeOwn", exe);
                var during = File.ReadAllText(ownToml);
                Check("before: yours kept aside, xemu.toml the session's (every game's options, the session's EEPROM)", File.Exists(Path.Combine(emu, "xemu-user.toml"))
                      && during.Contains("show_menubar = false") && during.Contains("eeprom-session.bin"), during);
                File.WriteAllText(ownToml, during.Replace("port1 = 'pad'", "port1 = 'pad'\nport2 = 'other'") + "\n[net]\nenable = true\n");   // xemu writing on exit
                Call("XemuConsoleBoot", "RestoreOwn", exe, "the probe");
                var after = File.ReadAllText(ownToml);
                Check("after: yours back - what xemu wrote in an untouched table kept, the session's settings gone", !File.Exists(Path.Combine(emu, "xemu-user.toml"))
                      && after.Contains("port2 = 'other'") && after.Contains("[net]") && !after.Contains("show_menubar") && !after.Contains("eeprom-session.bin"), after);
                Call("XemuConsoleBoot", "RestoreOwn", exe, "the probe");
                Check("... again: nothing to do", File.ReadAllText(ownToml) == after);            }
            catch (Exception ex) { Check("no exception", false, ex.ToString()); }
            finally { try { Directory.Delete(work, true); } catch { } }

            Console.WriteLine(_bad == 0 ? "  all good" : "  " + _bad + " FAILED");
            return _bad == 0;
        }
    }
}
