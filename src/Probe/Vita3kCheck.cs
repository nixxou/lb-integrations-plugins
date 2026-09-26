// What the disposable Vita has to get right, on a forged install and a forged game.
//
// It drives the SHIPPED assembly rather than a recompilation of its sources: the probe already loads
// the merged DLL, and reaching an internal static class inside it by name costs a few lines of
// reflection and buys a test of the artifact that actually goes out. Internal is a compiler rule, not
// a runtime one.
//
// What it proves, in order: a .vpk is understood without unpacking it; a session builds a console
// from the pristine firmware and installs the game onto it; the reference walk is taken BETWEEN those
// two and the session after; the difference comes out as one file; a rebuilt console gets the save
// back; relaunching the same game reuses everything; and a DIFFERENT game is what clears the tree -
// never the end of a session.
//
// EVERYTHING IS FORGED, under the temp folder, and deleted afterwards. No emulator is downloaded,
// none is run, and no RAM disk is mounted - the fallback path is the one under test, because it is
// the one every machine without ImDisk takes.

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Text;

namespace LbIntegrations.Probe
{
    internal static class Vita3kCheck
    {
        private const string TitleId = "PCSE00965";
        private static int _bad;
        private static Assembly _asm;

        public static bool Run(Assembly pluginAssembly)
        {
            Console.WriteLine();
            Console.WriteLine("-- Vita3K, on a FORGED install  [WRITES, in the temp folder] " + new string('-', 3));

            _asm = pluginAssembly;
            _bad = 0;
            var root = Path.Combine(Path.GetTempPath(), "lbip-vita3k-" + Guid.NewGuid().ToString("N"));
            try
            {
                var install = Path.Combine(root, "Emulators", "Nixx-Vita3K");
                var portable = Path.Combine(install, "portable");
                Directory.CreateDirectory(portable);
                File.WriteAllText(Path.Combine(install, "Vita3K.exe"), "not really an executable");

                var vpk = ForgeGame(root, "game.vpk");
                Console.WriteLine("  install   " + install);
                Console.WriteLine("  game      " + Path.GetFileName(vpk));

                TheArchive(vpk);
                var layout = Resolve(Path.Combine(install, "Vita3K.exe"));
                TheFirmware(layout, portable);
                var sessionRoot = TheSession(layout, portable, vpk);
                TheCapture(layout, portable, sessionRoot);
                TheSecondLaunch(layout, portable, vpk);
                AnotherGame(layout, portable, root);

                Console.WriteLine();
                Console.WriteLine(_bad == 0 ? "  OK - a console is built, played, captured and rebuilt around its save"
                                            : "  " + _bad + " FAILURE(S) - see above");
                return _bad == 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine("  EXCEPTION: " + ex);
                return false;
            }
            finally { Scrub(root); }
        }

        /// <summary>Install a REAL archive into a throwaway filesystem and print exactly what landed.
        ///
        /// FOR COMPARING AGAINST AN ORACLE: the same emulator before and after installing the same
        /// game through its own installer. That pair is what settled the NoNpDRM rules, and this arm
        /// is how our installer is held to them - by listing what it produces, not by trusting it.
        ///
        /// Writes only under %TEMP%.</summary>
        public static bool Installed(Assembly pluginAssembly, string romPath)
        {
            Console.WriteLine();
            Console.WriteLine("-- Vita3K, installing a real archive  [writes to %TEMP%] " + new string('-', 6));

            _asm = pluginAssembly;
            _bad = 0;
            try
            {
                if (string.IsNullOrWhiteSpace(romPath) || !File.Exists(romPath))
                {
                    Console.WriteLine("  pass --rom <archive.vpk|.zip>");
                    return false;
                }

                // The plugin runs from its build folder here, with no native\ beside it: point it at the
                // tool this checkout built, unless the caller already chose one.
                const string pfsVariable = "LBIP_VITA3K_TOOL";
                if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable(pfsVariable)))
                {
                    var repo = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(pluginAssembly.Location),
                                                             "..", "..", "..", "..", ".."));
                    var built = Path.Combine(repo, "build", "vita3k", "vita3k-install.exe");
                    if (File.Exists(built)) Environment.SetEnvironmentVariable(pfsVariable, built);
                }
                Console.WriteLine("  decryptor " + (Environment.GetEnvironmentVariable(pfsVariable) ?? "(none - PFS dumps will be refused)"));

                var fs = Path.Combine(Path.GetTempPath(), "lbip-vita3k-install-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(fs);
                Console.WriteLine("  archive   " + Path.GetFileName(romPath));
                Console.WriteLine("  into      " + fs);

                var args = new object[] { romPath, fs, null };
                var content = Call("Vita3kContent", "Install", args);
                if (!Check("the archive installs", content != null, args[2] as string)) return false;

                var landed = Directory.GetFiles(fs, "*", SearchOption.AllDirectories)
                                      .Select(p => p.Substring(fs.Length + 1).Replace('\\', '/'))
                                      .OrderBy(p => p, StringComparer.Ordinal)
                                      .ToList();

                Console.WriteLine("  files     " + landed.Count);
                foreach (var p in landed) Console.WriteLine("      " + p);

                // THE THREE RULES THE ORACLE ESTABLISHED.
                Check("the PFS layer is NOT installed - it is what keeps eboot.bin unreadable",
                      !landed.Any(p => p.IndexOf("/sce_pfs/", StringComparison.OrdinalIgnoreCase) >= 0));
                Check("the NoNpDRM package folder is NOT installed",
                      !landed.Any(p => p.IndexOf("/sce_sys/package/", StringComparison.OrdinalIgnoreCase) >= 0));
                Check("and the licence is placed under ux0/license",
                      landed.Any(p => p.StartsWith("ux0/license/", StringComparison.OrdinalIgnoreCase)
                                      && p.EndsWith(".rif", StringComparison.OrdinalIgnoreCase)));

                // THE CHECK THAT NEEDS NO ORACLE. An executable the emulator can load is a SELF, and a
                // SELF starts "SCE\0". Still encrypted, this game's eboot.bin started 92 99 77 50 - and
                // the emulator said exactly that: "file is either not a SELF or is still encrypted".
                var eboot = landed.FirstOrDefault(p => p.EndsWith("/eboot.bin", StringComparison.OrdinalIgnoreCase));
                if (eboot != null)
                {
                    var head = new byte[4];
                    using (var f = File.OpenRead(Path.Combine(fs, eboot.Replace('/', Path.DirectorySeparatorChar))))
                        f.Read(head, 0, 4);
                    Console.WriteLine("  eboot     " + BitConverter.ToString(head));
                    Check("eboot.bin is a SELF the emulator can load (starts SCE\\0)",
                          head[0] == (byte)'S' && head[1] == (byte)'C' && head[2] == (byte)'E' && head[3] == 0);
                }

                Check("and no staging copy is left behind",
                      !Directory.GetDirectories(fs, "*.pfs", SearchOption.AllDirectories).Any());

                Console.WriteLine();
                Console.WriteLine(_bad == 0 ? "  OK - what landed matches what a real install produces"
                                            : "  " + _bad + " FAILURE(S) - see above");
                return _bad == 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine("  EXCEPTION: " + ex.Message);
                return false;
            }
        }

        /// <summary>Against a REAL installation: put its firmware aside if that has not happened yet,
        /// and say what it found. This is the one operation of the model that runs once on a real
        /// console and can then never be observed again, so it gets its own arm rather than a forged
        /// stand-in.
        ///
        /// It WRITES: the filesystem is renamed and walked. That is exactly what the next launch
        /// would do, and doing it here means watching it happen instead of hoping.</summary>
        public static bool Real(Assembly pluginAssembly, string emuPath)
        {
            Console.WriteLine();
            Console.WriteLine("-- Vita3K, on a REAL install  [WRITES to it] " + new string('-', 19));

            _asm = pluginAssembly;
            _bad = 0;
            try
            {
                if (string.IsNullOrWhiteSpace(emuPath) || !File.Exists(emuPath))
                {
                    Console.WriteLine("  pass --emu <Vita3K.exe>");
                    return false;
                }

                var layout = Resolve(emuPath);
                var portable = Path.Combine(Path.GetDirectoryName(emuPath), "portable");
                Console.WriteLine("  install   " + Path.GetDirectoryName(emuPath));

                foreach (var part in new[] { "vs0", "sa0", "pd0", "os0" })
                {
                    var dir = Path.Combine(portable, "fs", part);
                    var n = Directory.Exists(dir)
                        ? Directory.GetFiles(dir, "*", SearchOption.AllDirectories).Length : 0;
                    Console.WriteLine("  fs\\" + part.PadRight(6) + (n > 0 ? n + " files" : "absent"));
                }

                bool had = (bool)Call("Vita3kWorkspace", "HasBase", new object[] { layout });
                Console.WriteLine("  base      " + (had ? "already put aside" : "not yet"));

                var args = new object[] { layout, null };
                bool ok = (bool)Call("Vita3kWorkspace", "EnsureBase", args);
                Check("the firmware is put aside", ok, args[1] as string);
                Check("nand-initiale exists", Directory.Exists(Path.Combine(portable, "nand-initiale")));

                var manifest = Path.Combine(portable, "nand-initiale.manifest");
                if (Check("its manifest exists", File.Exists(manifest)))
                {
                    var lines = File.ReadAllLines(manifest);
                    Console.WriteLine("  manifest  " + lines.Length + " entries");
                    Check("it holds the main firmware", Array.Exists(lines, l => l.Contains("vs0/")));
                    Check("and the font package", Array.Exists(lines, l => l.Contains("sa0/")));
                    Check("and the preinstalled package", Array.Exists(lines, l => l.Contains("pd0/")));
                }

                Console.WriteLine();
                Console.WriteLine(_bad == 0 ? "  OK - this install now has a console to build sessions from"
                                            : "  " + _bad + " FAILURE(S) - see above");
                return _bad == 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine("  EXCEPTION: " + ex.Message);
                return false;
            }
        }

        // ── the archive ──────────────────────────────────────────────────────

        private static void TheArchive(string vpk)
        {
            Console.WriteLine();
            Console.WriteLine("  the archive");

            var content = Call("Vita3kContent", "Describe", new object[] { vpk, null });
            if (!Check("it is described without unpacking", content != null)) return;

            Check("the title id is read", Field(content, "TitleId") as string == TitleId);
            Check("the category is read", Field(content, "Category") as string == "gd");
            Check("it counts as a game", Equals(Field(content, "IsGame"), true));

            var size = (long)Call("Vita3kContent", "UncompressedSize", new object[] { vpk });
            Check("its uncompressed size is known", size > 0);
            Console.WriteLine("            " + size + " bytes uncompressed");

            Check("a .vpk is installable", Equals(Call("Vita3kContent", "Installable", new object[] { vpk }), true));
            Check("a .iso is not", Equals(Call("Vita3kContent", "Installable", new object[] { "x.iso" }), false));
        }

        // ── the pristine firmware ────────────────────────────────────────────

        private static void TheFirmware(object layout, string portable)
        {
            Console.WriteLine();
            Console.WriteLine("  the pristine firmware");

            // What a firmware install leaves behind: the filesystem, with the four populated trees
            // and a couple of the empty ones.
            var fs = Path.Combine(portable, "fs");
            Write(fs, "vs0/data/font.pvf", "a font");
            Write(fs, "vs0/app/NPXS10015/eboot.bin", "settings");
            Write(fs, "sa0/data/cert.bin", "a certificate");
            Write(fs, "pd0/data/preinst.txt", "preinstalled");
            Directory.CreateDirectory(Path.Combine(fs, "ux0"));
            Directory.CreateDirectory(Path.Combine(fs, "ur0"));

            var args = new object[] { layout, null };
            var ok = (bool)Call("Vita3kWorkspace", "AdoptFirmware", args);
            Check("the firmware is put aside", ok, args[1] as string);
            Check("as nand-initiale", Directory.Exists(Path.Combine(portable, "nand-initiale")));
            Check("and fs is gone", !Directory.Exists(Path.Combine(portable, "fs")));
            Check("with a manifest", File.Exists(Path.Combine(portable, "nand-initiale.manifest")));
            Check("which the plugin can see", Equals(Call("Vita3kWorkspace", "HasBase", new object[] { layout }), true));

            // Asked twice on purpose: a base rebuilt under an existing save would make every save
            // describe a console that no longer exists.
            var again = new object[] { layout, null };
            Check("a second call leaves it alone", (bool)Call("Vita3kWorkspace", "AdoptFirmware", again));
            Check("and the manifest still matches the tree",
                  File.ReadAllText(Path.Combine(portable, "nand-initiale.manifest")).Contains("vs0/data/font.pvf"));
        }

        // ── a session ────────────────────────────────────────────────────────

        private static string TheSession(object layout, string portable, string vpk)
        {
            Console.WriteLine();
            Console.WriteLine("  a session is set up");

            var args = new object[] { layout, vpk, null };
            var titleId = Call("Vita3kWorkspace", "Prepare", args) as string;
            if (!Check("the console is built", titleId == TitleId, args[2] as string)) return null;

            var fs = Path.Combine(portable, "fs");
            Check("portable\\fs points somewhere", Directory.Exists(fs));
            Check("and it is a junction, not a folder",
                  new DirectoryInfo(fs).Attributes.HasFlag(FileAttributes.ReparsePoint));

            // NO RAM DISK HERE, deliberately: this is the path a machine without ImDisk takes, and it
            // is the one that has to work everywhere.
            var work = Path.Combine(portable, "work");
            Check("the fallback folder is the working tree", Directory.Exists(work));

            Check("the firmware was copied onto it", File.Exists(Path.Combine(work, "vs0", "data", "font.pvf")));
            Check("the game was installed onto it",
                  File.Exists(Path.Combine(work, "ux0", "app", TitleId, "eboot.bin")));
            Check("and reached through the junction too",
                  File.Exists(Path.Combine(fs, "ux0", "app", TitleId, "eboot.bin")));

            Check("the reference walk was taken", File.Exists(Path.Combine(portable, "work.reference")));
            var marker = Path.Combine(portable, "work.title");
            Check("the marker was written last", File.Exists(marker));
            Check("and it names this game", File.ReadAllText(marker).StartsWith(TitleId, StringComparison.Ordinal));

            // The reference has to describe the console BEFORE the session - install included, save
            // excluded. Nothing of a save can be in it yet, because nothing has played.
            var reference = File.ReadAllText(Path.Combine(portable, "work.reference"));
            Check("the reference holds the installed game", reference.Contains("ux0/app/" + TitleId + "/eboot.bin"));
            Check("and the firmware", reference.Contains("vs0/data/font.pvf"));

            return work;
        }

        // ── what comes out of it ─────────────────────────────────────────────

        private static void TheCapture(object layout, string portable, string work)
        {
            Console.WriteLine();
            Console.WriteLine("  the session comes out");
            if (work == null) { Console.WriteLine("    skipped - no console was built"); _bad++; return; }

            // A session: a save written, a setting changed, a directory left empty.
            Write(work, "ux0/user/00/savedata/" + TitleId + "/data.bin", "sixteen hours of progress");
            Write(work, "vs0/data/font.pvf", "a font, patched by the console");
            Directory.CreateDirectory(Path.Combine(work, "ux0", "user", "00", "trophy"));

            var taken = (bool)Call("Vita3kWorkspace", "Capture", new object[] { layout, TitleId });
            Check("the difference is taken", taken);

            var save = Path.Combine(portable, "saves", TitleId, "state.vitasav");
            Check("a .vitasav is written", File.Exists(save));
            if (File.Exists(save))
                Console.WriteLine("            " + new FileInfo(save).Length + " bytes");
        }

        // ── the console is rebuilt around the save ───────────────────────────

        private static void TheSecondLaunch(object layout, string portable, string vpk)
        {
            Console.WriteLine();
            Console.WriteLine("  relaunching the same game");

            // SAME game: nothing is rebuilt. That is the rule - the tree is cleared when a DIFFERENT
            // game starts, never when one ends.
            var before = File.GetLastWriteTimeUtc(Path.Combine(portable, "work.reference"));
            var args = new object[] { layout, vpk, null };
            var titleId = Call("Vita3kWorkspace", "Prepare", args) as string;
            Check("it launches", titleId == TitleId, args[2] as string);
            Check("and nothing was rebuilt",
                  File.GetLastWriteTimeUtc(Path.Combine(portable, "work.reference")) == before);

            // Now force a rebuild, which is what a different game then this one again would do, and
            // check the save comes back into the fresh console.
            Call("Vita3kWorkspace", "Teardown", new object[] { layout });
            Check("tearing down clears the working tree", !Directory.Exists(Path.Combine(portable, "work")));

            var again = new object[] { layout, vpk, null };
            Check("it builds again", Call("Vita3kWorkspace", "Prepare", again) as string == TitleId, again[2] as string);

            var work = Path.Combine(portable, "work");
            Check("the save came back",
                  File.Exists(Path.Combine(work, "ux0", "user", "00", "savedata", TitleId, "data.bin")));
            Check("with its content",
                  Read(work, "ux0/user/00/savedata/" + TitleId + "/data.bin") == "sixteen hours of progress");
            Check("the altered firmware file came back too",
                  Read(work, "vs0/data/font.pvf") == "a font, patched by the console");
            Check("and the empty directory",
                  Directory.Exists(Path.Combine(work, "ux0", "user", "00", "trophy")));

            // AND THE NEW REFERENCE MUST NOT HOLD THE SAVE. It is taken before the restore, so the
            // next capture finds the save again as a difference. Getting this backwards is how a
            // model like this silently stops saving.
            var reference = File.ReadAllText(Path.Combine(portable, "work.reference"));
            Check("the fresh reference does NOT contain the restored save",
                  !reference.Contains("savedata/" + TitleId));
        }

        // ── a different game is what clears it ───────────────────────────────

        private static void AnotherGame(object layout, string portable, string root)
        {
            Console.WriteLine();
            Console.WriteLine("  a different game");

            var other = ForgeGame(root, "other.vpk", "PCSE99999");
            var args = new object[] { layout, other, null };
            var titleId = Call("Vita3kWorkspace", "Prepare", args) as string;
            Check("it builds a console for the other game", titleId == "PCSE99999", args[2] as string);

            var work = Path.Combine(portable, "work");
            Check("the first game is gone from the tree",
                  !Directory.Exists(Path.Combine(work, "ux0", "app", TitleId)));
            Check("the other game is there",
                  File.Exists(Path.Combine(work, "ux0", "app", "PCSE99999", "eboot.bin")));

            // THE SAVE SURVIVES THE TREE. Clearing the working console must never touch what came out
            // of it.
            Check("the first game's save is untouched",
                  File.Exists(Path.Combine(portable, "saves", TitleId, "state.vitasav")));
        }

        // ── forging ──────────────────────────────────────────────────────────

        /// <summary>A .vpk is a zip with sce_sys/param.sfo in it. This builds a real one, PSF header
        /// and all, so the plugin reads the same shape it will read from a dump.</summary>
        private static string ForgeGame(string root, string name, string titleId = TitleId)
        {
            var staging = Path.Combine(root, "staging-" + Path.GetFileNameWithoutExtension(name));
            Write(staging, "eboot.bin", "the game itself, for " + titleId);
            Write(staging, "sce_sys/icon0.png", "an icon");
            File.WriteAllBytes(Path.Combine(staging, "sce_sys", "param.sfo"),
                               Psf(("CATEGORY", "gd"), ("STITLE", "A Forged Game"), ("TITLE_ID", titleId)));

            var vpk = Path.Combine(root, name);
            ZipFile.CreateFromDirectory(staging, vpk);
            return vpk;
        }

        /// <summary>A PARAM.SFO carrying UTF-8 strings. Keys have to be sorted, which is how Sony
        /// writes them and what the reader expects to walk.</summary>
        private static byte[] Psf(params (string Key, string Value)[] pairs)
        {
            Array.Sort(pairs, (a, b) => string.CompareOrdinal(a.Key, b.Key));

            var keys = new MemoryStream();
            var data = new MemoryStream();
            var index = new MemoryStream();

            foreach (var (key, value) in pairs)
            {
                int keyOffset = (int)keys.Length;
                var keyBytes = Encoding.ASCII.GetBytes(key);
                keys.Write(keyBytes, 0, keyBytes.Length);
                keys.WriteByte(0);

                int dataOffset = (int)data.Length;
                var valueBytes = Encoding.UTF8.GetBytes(value);
                data.Write(valueBytes, 0, valueBytes.Length);
                data.WriteByte(0);
                int used = valueBytes.Length + 1;

                index.Write(BitConverter.GetBytes((ushort)keyOffset), 0, 2);
                index.Write(BitConverter.GetBytes((ushort)0x0204), 0, 2);   // UTF-8 string
                index.Write(BitConverter.GetBytes(used), 0, 4);
                index.Write(BitConverter.GetBytes(used), 0, 4);
                index.Write(BitConverter.GetBytes(dataOffset), 0, 4);
            }

            // The key table has to start on a four-byte boundary, as Sony's own files do.
            while (keys.Length % 4 != 0) keys.WriteByte(0);

            int keyStart = 20 + (int)index.Length;
            int dataStart = keyStart + (int)keys.Length;

            var sfo = new MemoryStream();
            sfo.Write(BitConverter.GetBytes(0x46535000), 0, 4);   // "\0PSF"
            sfo.Write(BitConverter.GetBytes(0x00000101), 0, 4);
            sfo.Write(BitConverter.GetBytes(keyStart), 0, 4);
            sfo.Write(BitConverter.GetBytes(dataStart), 0, 4);
            sfo.Write(BitConverter.GetBytes(pairs.Length), 0, 4);
            index.WriteTo(sfo);
            keys.WriteTo(sfo);
            data.WriteTo(sfo);
            return sfo.ToArray();
        }

        // ── reaching into the assembly ───────────────────────────────────────

        private static object Resolve(string exePath)
            => Call("Vita3kPaths", "Resolve", new object[] { exePath });

        private static object Call(string type, string method, object[] args)
        {
            var t = _asm.GetType("LbIntegrations.Vita3k." + type, throwOnError: true);
            var m = FindMethod(t, method, args.Length);
            if (m == null) throw new MissingMethodException(type + "." + method);
            return m.Invoke(null, args);
        }

        private static MethodInfo FindMethod(Type t, string name, int count)
        {
            foreach (var m in t.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
                if (m.Name == name && m.GetParameters().Length == count) return m;
            return null;
        }

        private static object Field(object instance, string name)
        {
            var t = instance.GetType();
            var f = t.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (f != null) return f.GetValue(instance);
            var p = t.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            return p?.GetValue(instance);
        }

        // ── helpers ──────────────────────────────────────────────────────────

        private static void Write(string root, string relative, string content)
        {
            var path = Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, content);
        }

        private static string Read(string root, string relative)
        {
            try { return File.ReadAllText(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar))); }
            catch { return null; }
        }

        private static bool Check(string what, bool ok, string error = null)
        {
            Console.WriteLine("    " + (ok ? "ok  " : "BAD ") + what
                              + (!ok && !string.IsNullOrEmpty(error) ? "  (" + error + ")" : ""));
            if (!ok) _bad++;
            return ok;
        }

        private static void Scrub(string dir)
        {
            // The junction goes first: deleting a tree through one would follow it.
            try
            {
                var fs = Directory.GetDirectories(dir, "fs", SearchOption.AllDirectories);
                foreach (var link in fs)
                    if (new DirectoryInfo(link).Attributes.HasFlag(FileAttributes.ReparsePoint))
                        Directory.Delete(link);
            }
            catch { }
            try { if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true); } catch { }
        }
    }
}
