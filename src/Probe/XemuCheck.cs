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

        public static bool Run(Assembly asm)
        {
            _asm = asm;
            var work = Path.Combine(Path.GetTempPath(), "lbip-xemu-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            Directory.CreateDirectory(work);
            T("XemuSettings").GetField("DirOverride", Any).SetValue(null, Path.Combine(work, "settings"));
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
