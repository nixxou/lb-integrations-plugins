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
                TheGameMenu();
                TheOptions();
                TheImportRecord();
                TheSettings();
                TheDocs(root);
                var layout = Resolve(Path.Combine(install, "Vita3K.exe"));
                TheFirmware(layout, portable);
                var sessionRoot = TheSession(layout, portable, vpk);
                TheCapture(layout, portable, sessionRoot);
                TheSecondLaunch(layout, portable, vpk);
                AnotherGame(layout, portable, root);
                NeverEnded(layout, portable, root, vpk);
                TheEmulatorsShare(layout);
                UpdatesAndDlc(layout, portable, root);
                OrphanedJunction(layout, portable, root);

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

        /// <summary>The native library the plugin loads, called the way the plugin calls it.
        ///
        /// Three claims are measured here rather than trusted. That the library answers its own
        /// known-answer test. That a firmware installed THROUGH IT lands complete, with progress
        /// reported to the end - the output folders are printed so they can be compared, byte for
        /// byte, against an install made by Vita3K. And that it is UNLOADED after every call: pup.cpp
        /// keeps a static counter that only a fresh load resets, so a library still mapped after a call
        /// would be a corrupt partition waiting to happen. Two rounds, so the second one runs on a
        /// library that has been loaded and freed before.
        ///
        /// Writes only under %TEMP%.</summary>
        public static bool Native(Assembly pluginAssembly, string pupDir)
        {
            Console.WriteLine();
            Console.WriteLine("-- Vita3K, the native library  [writes to %TEMP%] " + new string('-', 13));

            _asm = pluginAssembly;
            _bad = 0;
            try
            {
                const string variable = "LBIP_VITA3K_NATIVE";
                if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable(variable)))
                {
                    var repo = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(pluginAssembly.Location),
                                                             "..", "..", "..", "..", ".."));
                    var built = Path.Combine(repo, "build", "vita3k", "vita3k-install.dll");
                    if (File.Exists(built)) Environment.SetEnvironmentVariable(variable, built);
                }
                // Asked of the plugin, not guessed: from its build folder that is the override just set,
                // from a deployed folder it is native\ beside it - the path a host would load.
                var native = _asm.GetType("LbIntegrations.Vita3k.Vita3kNative", throwOnError: true);
                var library = native.GetProperty("LibraryPath", BindingFlags.Public | BindingFlags.Static)?.GetValue(null) as string;
                Console.WriteLine("  library   " + (library ?? "(none)"));
                if (!Check("the library is there", library != null && File.Exists(library))) return false;

                var selftest = new object[] { null };
                bool st = (bool)native.GetMethod("Selftest", BindingFlags.Public | BindingFlags.Static).Invoke(null, selftest);
                Check("it passes its own known-answer test", st, selftest[0] as string);
                Check("and is unloaded afterwards", !Mapped(library));

                var pups = string.IsNullOrWhiteSpace(pupDir) || !Directory.Exists(pupDir)
                    ? new string[0] : Directory.GetFiles(pupDir, "*.PUP");
                Array.Sort(pups, StringComparer.OrdinalIgnoreCase);
                if (pups.Length == 0)
                {
                    Console.WriteLine("  (no --pup-dir with .PUP files: firmware not exercised)");
                }
                else
                {
                    var progressType = native.GetNestedType("Progress");
                    var install = native.GetMethod("InstallFirmware", BindingFlags.Public | BindingFlags.Static);

                    for (int round = 1; round <= 2; round++)
                    {
                        var fs = Path.Combine(Path.GetTempPath(), "lbip-vita3k-fw-" + round + "-" + Guid.NewGuid().ToString("N"), "fs");
                        Console.WriteLine("  round " + round + "  " + fs);
                        foreach (var pup in pups)
                        {
                            int calls = 0;
                            double last = -1;
                            Action<double> seen = f => { calls++; last = f; };
                            var progress = Delegate.CreateDelegate(progressType, seen.Target, seen.Method);

                            var a = new object[] { pup, fs, progress, null };
                            bool ok = (bool)install.Invoke(null, a);
                            Console.WriteLine("            " + Path.GetFileName(pup) + "  " + calls + " progress call(s), last " + last.ToString("0.00"));
                            Check(Path.GetFileName(pup) + " installs", ok, a[3] as string);
                            Check("  progress reaches the end", calls > 0 && last >= 0.999);
                            Check("  and the library is unloaded afterwards", !Mapped(library));
                        }
                        Console.WriteLine("  OUTPUT " + fs);
                    }
                }

                Console.WriteLine();
                Console.WriteLine(_bad == 0 ? "  OK - loaded, called, freed, and it did what it said"
                                            : "  " + _bad + " FAILURE(S) - see above");
                return _bad == 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine("  EXCEPTION: " + (ex.InnerException ?? ex).Message);
                return false;
            }
        }

        /// <summary>A REAL launch preparation, without a host: the current session is put away, then a
        /// console is built for the game exactly as PrepareEmulatorForLaunch would - on the RAM disk
        /// when there is one - and every step timed. It WRITES to the real install and leaves a
        /// prepared console behind, which the next launch of the same game reuses.</summary>
        public static bool Prepare(Assembly pluginAssembly, string emuPath, string romPath)
        {
            Console.WriteLine();
            Console.WriteLine("-- Vita3K, a real launch preparation  [WRITES to the install] " + new string('-', 2));

            _asm = pluginAssembly;
            _bad = 0;
            try
            {
                if (!File.Exists(emuPath ?? "") || !File.Exists(romPath ?? ""))
                { Console.WriteLine("  pass --emu <Vita3K.exe> --rom <archive>"); return false; }

                // From the build folder there is no native\ beside the plugin: point it at the library this
                // checkout built, as the other arms do - measured, without it the install is refused.
                const string variable = "LBIP_VITA3K_NATIVE";
                if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable(variable)))
                {
                    var repo = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(pluginAssembly.Location),
                                                             "..", "..", "..", "..", ".."));
                    var built = Path.Combine(repo, "build", "vita3k", "vita3k-install.dll");
                    if (File.Exists(built)) Environment.SetEnvironmentVariable(variable, built);
                }

                var layout = Resolve(emuPath);
                var watch = System.Diagnostics.Stopwatch.StartNew();
                Call("Vita3kWorkspace", "Teardown", new object[] { layout });
                Console.WriteLine("  previous session put away in " + watch.ElapsedMilliseconds + " ms");

                string lastStep = null;
                var steps = new System.Collections.Generic.List<string>();
                var stepWatch = System.Diagnostics.Stopwatch.StartNew();
                Action<string, double?> report = (step, fraction) =>
                {
                    if (step == null || step == lastStep) return;
                    if (lastStep != null) steps.Add(string.Format("  {0,6} ms  {1}", stepWatch.ElapsedMilliseconds, lastStep));
                    lastStep = step;
                    stepWatch.Restart();
                };

                var t = _asm.GetType("LbIntegrations.Vita3k.Vita3kWorkspace", throwOnError: true);
                var prepare = t.GetMethod("Prepare", BindingFlags.Public | BindingFlags.Static, null,
                    new[] { layout.GetType(), typeof(string), typeof(string).MakeByRefType(), typeof(Action<string, double?>) }, null);
                var a = new object[] { layout, romPath, null, report };
                watch.Restart();
                var titleId = prepare.Invoke(null, a) as string;
                long total = watch.ElapsedMilliseconds;
                if (lastStep != null) steps.Add(string.Format("  {0,6} ms  {1}", stepWatch.ElapsedMilliseconds, lastStep));

                foreach (var line in steps) Console.WriteLine(line);
                Console.WriteLine("  ------");
                Console.WriteLine(string.Format("  {0,6} ms  in all", total));
                Check("a console is prepared", titleId != null, a[2] as string);

                Console.WriteLine();
                Console.WriteLine(_bad == 0 ? "  OK - prepared; the next launch of this game reuses it" : "  " + _bad + " FAILURE(S)");
                return _bad == 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine("  EXCEPTION: " + (ex.InnerException ?? ex).Message);
                return false;
            }
        }

        /// <summary>The launch command line, case by case. A pure function, so it is checked here
        /// rather than discovered at the next launch - the first case is the line LaunchBox really
        /// handed over, which an earlier version turned into
        ///     -F [PCSE00965] [USA] [NoNpDRM].zip" -r PCSE00965 -Z
        /// and the emulator opened on its own window instead of the game.</summary>
        public static bool CommandLine(Assembly pluginAssembly)
        {
            Console.WriteLine();
            Console.WriteLine("-- Vita3K, the launch command line " + new string('-', 30));

            _asm = pluginAssembly;
            _bad = 0;
            try
            {
                var type = _asm.GetType("LbIntegrations.Vita3k.Vita3kPlugin", throwOnError: true);
                var build = type.GetMethod("CommandLineFor", BindingFlags.NonPublic | BindingFlags.Static);
                if (!Check("CommandLineFor exists", build != null)) return false;

                const string rom = @"C:\Users\mehdi\Downloads\KILLALLZOMBIES [PCSE00965] [USA] [NoNpDRM].zip";
                string Line(string current, string titleId) => (string)build.Invoke(null, new object[] { current, titleId, rom });

                void Case(string what, string current, string titleId, string want)
                {
                    var got = Line(current, titleId);
                    Console.WriteLine("  " + what);
                    Console.WriteLine("      in   " + current);
                    Console.WriteLine("      out  " + got);
                    Check("    gives " + want, got == want);
                }

                Case("the line LaunchBox really passed", "-F -r \"" + rom + "\"", "PCSE00965", "-F -r PCSE00965");
                Case("the new default line", "-F \"" + rom + "\"", "PCSE00965", "-F -r PCSE00965");
                Case("a line an earlier build rewrote", "-F -r PCSE00965 -Z \"" + rom + "\"", "PCSE00965", "-F -r PCSE00965");
                Case("no console to build on: the game stays, -r goes", "-F -r \"" + rom + "\"", null, "-F \"" + rom + "\"");
                Case("a user's own switch is kept", "-F --log-level 2 \"" + rom + "\"", "PCSE00965", "-F --log-level 2 -r PCSE00965");
                // OUR flag is read and never passed on: Vita3K's CLI11 refuses an option it does not know.
                Case("--no-ramdisk never reaches the emulator", "-F --no-ramdisk \"" + rom + "\"", "PCSE00965", "-F -r PCSE00965");
                Case("in any case, anywhere on the line", "--NO-RAMDISK -F \"" + rom + "\"", null, "-F \"" + rom + "\"");
                var carries = type.GetMethod("Carries", BindingFlags.NonPublic | BindingFlags.Static);
                Check("and it is seen on the line", carries != null
                      && (bool)carries.Invoke(null, new object[] { "-F --no-ramdisk \"" + rom + "\"", "--no-ramdisk" })
                      && !(bool)carries.Invoke(null, new object[] { "-F \"" + rom + "\"", "--no-ramdisk" }));

                // --ramdisk-margin: its value goes with it, in either spelling.
                Case("--ramdisk-margin and its value never reach the emulator",
                     "-F --ramdisk-margin 1024 \"" + rom + "\"", "PCSE00965", "-F -r PCSE00965");
                Case("nor the = spelling", "-F --ramdisk-margin=2048 \"" + rom + "\"", null, "-F \"" + rom + "\"");
                Case("nor a refused negative value", "-F --ramdisk-margin -5 \"" + rom + "\"", "PCSE00965", "-F -r PCSE00965");
                var margin = type.GetMethod("MarginFrom", BindingFlags.NonPublic | BindingFlags.Static);
                int? Margin(string line, out string problem)
                {
                    var a = new object[] { line, null };
                    var got = (int?)margin.Invoke(null, a);
                    problem = a[1] as string;
                    return got;
                }
                Check("the margin is read: --ramdisk-margin 1024", margin != null && Margin("-F --ramdisk-margin 1024", out _) == 1024);
                Check("the margin is read: --ramdisk-margin=2048", Margin("-F --ramdisk-margin=2048", out _) == 2048);
                Check("0 is a margin too", Margin("--ramdisk-margin 0", out var p0) == 0 && p0 == null);
                Check("none asked: no margin, no complaint", Margin("-F", out var p1) == null && p1 == null);
                Check("not a number: no margin, and it says why", Margin("--ramdisk-margin big", out var p2) == null && p2 != null);
                Check("negative: refused", Margin("--ramdisk-margin -5", out var p3) == null && p3 != null);
                Check("absurd: refused", Margin("--ramdisk-margin 999999", out var p4) == null && p4 != null);

                // --vita3k-ram: the same rules, its own value.
                Case("--vita3k-ram and its value never reach the emulator",
                     "-F --vita3k-ram 3072 --ramdisk-margin=256 \"" + rom + "\"", "PCSE00965", "-F -r PCSE00965");
                var ram = type.GetMethod("Vita3kRamFrom", BindingFlags.NonPublic | BindingFlags.Static);
                int? Ram(string line)
                {
                    var a = new object[] { line, null };
                    return (int?)ram.Invoke(null, a);
                }
                Check("the RAM for Vita3K is read: --vita3k-ram 3072", ram != null && Ram("-F --vita3k-ram 3072 --ramdisk-margin 256") == 3072);
                Check("the RAM for Vita3K is read: --vita3k-ram=1536", Ram("--vita3k-ram=1536") == 1536);
                Check("and the two flags do not read each other's value",
                      Ram("--ramdisk-margin 256") == null && Margin("--vita3k-ram 3072", out _) == null);

                // --use-vhdx: alone, or with its folder - and neither ever reaches the emulator.
                Case("--use-vhdx alone never reaches the emulator", "-F --use-vhdx \"" + rom + "\"", "PCSE00965", "-F -r PCSE00965");
                Case("nor with its folder", "-F --use-vhdx=E:\\VitaVhdx \"" + rom + "\"", "PCSE00965", "-F -r PCSE00965");
                Case("nor a quoted folder with a space", "-F \"--use-vhdx=E:\\Mes VHDX\" \"" + rom + "\"", null, "-F \"" + rom + "\"");
                Case("nor its folder in the space spelling", "-F --use-vhdx E:\\VitaVhdx \"" + rom + "\"", "PCSE00965", "-F -r PCSE00965");
                Case("alone before the game, the game is not taken for its folder", "-F -r --use-vhdx \"" + rom + "\"", null, "-F \"" + rom + "\"");
                var vhdx = type.GetMethod("VhdxFrom", BindingFlags.NonPublic | BindingFlags.Static);
                string Vhdx(string line, out bool asked)
                {
                    var a = new object[] { line, rom, null };
                    var got = (string)vhdx.Invoke(null, a);
                    asked = (bool)a[2];
                    return got;
                }
                Check("not asked: no folder, not asked", vhdx != null && Vhdx("-F --no-ramdisk", out var a0) == null && !a0);
                Check("alone: asked, the default folder", Vhdx("-F --use-vhdx", out var a1) == null && a1);
                Check("--use-vhdx=E:\\VitaVhdx", Vhdx("--use-vhdx=E:\\VitaVhdx", out var a2) == @"E:\VitaVhdx" && a2);
                Check("\"--use-vhdx=E:\\Mes VHDX\"", Vhdx("\"--use-vhdx=E:\\Mes VHDX\" -F", out _) == @"E:\Mes VHDX");
                Check("--use-vhdx E:\\VitaVhdx", Vhdx("--use-vhdx E:\\VitaVhdx -F", out _) == @"E:\VitaVhdx");
                Check("--use-vhdx followed by the game: the default folder", Vhdx("--use-vhdx \"" + rom + "\"", out var a3) == null && a3);
                Check("--use-vhdx followed by a switch: the default folder", Vhdx("--use-vhdx -F", out _) == null);
                Check("--use-vhdx= with nothing: the default folder", Vhdx("--use-vhdx= -F", out var a4) == null && a4);

                Console.WriteLine();
                Console.WriteLine(_bad == 0 ? "  OK - the game path never reaches the emulator beside -r"
                                            : "  " + _bad + " FAILURE(S) - see above");
                return _bad == 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine("  EXCEPTION: " + (ex.InnerException ?? ex).Message);
                return false;
            }
        }

        /// <summary>The progress window a game install shows at launch, looked for by its title among
        /// the desktop's windows: absent during its delay, present after it, gone once disposed. It
        /// does open on screen for about three seconds - that is the point of it.</summary>
        public static bool Window(Assembly pluginAssembly)
        {
            Console.WriteLine();
            Console.WriteLine("-- Vita3K, the progress window  [opens a window] " + new string('-', 15));

            _asm = pluginAssembly;
            _bad = 0;
            try
            {
                var type = _asm.GetType("LbIntegrations.Vita3k.Vita3kProgressWindow", throwOnError: true);
                var title = "Vita3K - probe " + Guid.NewGuid().ToString("N").Substring(0, 8);
                var window = type.GetMethod("Open", BindingFlags.Public | BindingFlags.Static).Invoke(null, new object[] { title });
                if (!Check("it opens", window != null)) return false;
                var report = type.GetMethod("Report");

                report.Invoke(window, new object[] { "Unpacking the game...", 0.0 });
                System.Threading.Thread.Sleep(300);
                Check("nothing is drawn during the first moments", FindWindow(null, title) == IntPtr.Zero);

                for (int i = 0; i <= 25; i++)
                {
                    report.Invoke(window, new object[] { i < 12 ? "Unpacking the game..." : "Decrypting the game...", i / 25.0 });
                    System.Threading.Thread.Sleep(100);
                }
                Check("it is on screen for a longer wait", FindWindow(null, title) != IntPtr.Zero);

                report.Invoke(window, new object[] { "Taking the console's fingerprint...", null });
                System.Threading.Thread.Sleep(400);

                ((IDisposable)window).Dispose();
                System.Threading.Thread.Sleep(500);
                Check("and gone once disposed", FindWindow(null, title) == IntPtr.Zero);

                Console.WriteLine();
                Console.WriteLine(_bad == 0 ? "  OK - quiet for a quick wait, visible for a long one, gone after"
                                            : "  " + _bad + " FAILURE(S) - see above");
                return _bad == 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine("  EXCEPTION: " + (ex.InnerException ?? ex).Message);
                return false;
            }
        }

        [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        private static extern IntPtr FindWindow(string className, string windowName);

        private static void CopyAll(string from, string to)
        {
            foreach (var d in Directory.GetDirectories(from, "*", SearchOption.AllDirectories))
                Directory.CreateDirectory(Path.Combine(to, d.Substring(from.Length).TrimStart('\\')));
            foreach (var f in Directory.GetFiles(from, "*", SearchOption.AllDirectories))
            {
                var t = Path.Combine(to, f.Substring(from.Length).TrimStart('\\'));
                Directory.CreateDirectory(Path.GetDirectoryName(t));
                File.Copy(f, t, true);
            }
        }

        /// <summary>Is this file mapped into the current process? Asked of the process itself, which is
        /// the only honest answer to "was it freed".</summary>
        private static bool Mapped(string path)
        {
            var full = Path.GetFullPath(path);
            foreach (System.Diagnostics.ProcessModule m in System.Diagnostics.Process.GetCurrentProcess().Modules)
                if (string.Equals(Path.GetFullPath(m.FileName), full, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        /// <summary>Install a REAL archive into a throwaway filesystem and print exactly what landed.
        ///
        /// FOR COMPARING AGAINST AN ORACLE: the same emulator before and after installing the same
        /// game through its own installer. That pair is what settled the NoNpDRM rules, and this arm
        /// is how our installer is held to them - by listing what it produces, not by trusting it.
        ///
        /// Writes only under %TEMP%.</summary>
        public static bool Installed(Assembly pluginAssembly, string romPath) => Installed(pluginAssembly, romPath, null);

        /// <summary>The same, onto a REAL RAM DISK when <paramref name="ramdiskRoot"/> names a LaunchBox
        /// install whose RAM disk task is set up. The host installs onto one; a probe that only ever
        /// installed onto a plain folder passed while the first real launch failed - ImDisk volumes
        /// cannot resolve a final path, and psvpfsparser asks for one.</summary>
        public static bool Installed(Assembly pluginAssembly, string romPath, string ramdiskRoot)
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
                const string pfsVariable = "LBIP_VITA3K_NATIVE";
                if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable(pfsVariable)))
                {
                    var repo = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(pluginAssembly.Location),
                                                             "..", "..", "..", "..", ".."));
                    var built = Path.Combine(repo, "build", "vita3k", "vita3k-install.dll");
                    if (File.Exists(built)) Environment.SetEnvironmentVariable(pfsVariable, built);
                }
                var nativeType = _asm.GetType("LbIntegrations.Vita3k.Vita3kNative", throwOnError: true);
                var nativePath = nativeType.GetProperty("LibraryPath", BindingFlags.Public | BindingFlags.Static)?.GetValue(null) as string;
                Console.WriteLine("  native    " + (nativePath != null && File.Exists(nativePath)
                                                    ? nativePath : "(none - PFS dumps will be refused)"));

                string fs, drive = null;
                if (!string.IsNullOrWhiteSpace(ramdiskRoot))
                {
                    LbIntegrations.RamDisk.RamDiskLog.Use(m => Console.WriteLine("  [ramdisk] " + m),
                                                          (m, ex) => Console.WriteLine("  [ramdisk] " + m + (ex != null ? " - " + ex.Message : "")));
                    LbIntegrations.RamDisk.RamDiskHost.UseRoot(ramdiskRoot);
                    long need = new FileInfo(romPath).Length * 3 / (1024 * 1024) + 256;
                    drive = LbIntegrations.RamDisk.RamDrive.MountFor("probe-vita3k", (int)need);
                    if (!Check("a RAM disk mounts", drive != null)) return false;
                    fs = Path.Combine(drive, "fs");
                }
                else fs = Path.Combine(Path.GetTempPath(), "lbip-vita3k-install-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(fs);
                try
                {
                Console.WriteLine("  archive   " + Path.GetFileName(romPath));
                Console.WriteLine("  into      " + fs);

                var tempBefore = new HashSet<string>(Directory.GetDirectories(Path.GetTempPath(), "lbip-*"), StringComparer.OrdinalIgnoreCase);

                // THE PEAK, measured rather than argued: what the RAM disk held at its fullest during
                // the install. An ImDisk drive keeps its declared size in memory, so this is what the
                // disk has to be sized for.
                long usedBefore = 0, peak = 0;
                bool sampling = drive != null;
                System.Threading.Thread sampler = null;
                if (sampling)
                {
                    var info = new DriveInfo(drive);
                    usedBefore = info.TotalSize - info.AvailableFreeSpace;
                    sampler = new System.Threading.Thread(() =>
                    {
                        while (sampling)
                        {
                            try { var i = new DriveInfo(drive); peak = Math.Max(peak, i.TotalSize - i.AvailableFreeSpace - usedBefore); } catch { }
                            System.Threading.Thread.Sleep(5);
                        }
                    }) { IsBackground = true };
                    sampler.Start();
                }

                var args = new object[] { romPath, fs, null };
                var content = Call("Vita3kContent", "Install", args);
                sampling = false;
                sampler?.Join();
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

                // HASHED AS WRITTEN: every file that landed must have its fingerprint from the install,
                // and it must be the one reading the file back gives. This is what lets the reference walk
                // skip reading a fresh game - so it is checked against the slow way, file by file.
                var hashedField = content.GetType().GetField("Hashed");
                var hashedMap = hashedField?.GetValue(content) as System.Collections.IDictionary;
                int missing = 0, wrong = 0;
                var firstBad = new List<string>();
                foreach (var rel in landed)
                {
                    var entry = hashedMap != null && hashedMap.Contains(rel) ? hashedMap[rel] : null;
                    if (entry == null) { missing++; if (firstBad.Count < 5) firstBad.Add("missing " + rel); continue; }
                    var size = entry.GetType().GetField("Size").GetValue(entry) as string;
                    var sha1 = entry.GetType().GetField("Sha1").GetValue(entry) as string;
                    var path = Path.Combine(fs, rel.Replace('/', Path.DirectorySeparatorChar));
                    string actual;
                    using (var f = File.OpenRead(path))
                    using (var sha = System.Security.Cryptography.SHA1.Create())
                        actual = Convert.ToHexString(sha.ComputeHash(f));
                    if (size != new FileInfo(path).Length.ToString() || sha1 != actual)
                    { wrong++; if (firstBad.Count < 5) firstBad.Add("differs " + rel); }
                }
                Console.WriteLine("  hashed    " + (hashedMap?.Count ?? 0) + " as written, " + missing + " missing, " + wrong + " wrong");
                foreach (var b in firstBad) Console.WriteLine("      " + b);
                Check("every installed file was hashed as it was written", hashedMap != null && missing == 0);
                Check("and every one of those equals the file read back", hashedMap != null && wrong == 0);

                var besideTree = Path.GetDirectoryName(Path.GetFullPath(fs).TrimEnd(Path.DirectorySeparatorChar)) ?? fs;
                Check("and no staging copy is left behind",
                      !Directory.GetDirectories(fs, "*.pfs", SearchOption.AllDirectories).Any()
                      && !Directory.GetDirectories(besideTree, "lbip-staging-*").Any()
                      && !Directory.GetDirectories(Path.GetTempPath(), "lbip-vita3k-staging-*").Any());
                var tempNew = Directory.GetDirectories(Path.GetTempPath(), "lbip-*")
                                       .Where(d => !tempBefore.Contains(d) && !d.StartsWith(fs, StringComparison.OrdinalIgnoreCase)
                                                   && !fs.StartsWith(d, StringComparison.OrdinalIgnoreCase)).ToList();
                if (drive != null)
                {
                    Check("and the install wrote nothing to %TEMP% - the staging copy stayed on the RAM disk",
                          tempNew.Count == 0, string.Join(", ", tempNew));
                    long installed = landed.Sum(p => new FileInfo(Path.Combine(fs, p.Replace('/', Path.DirectorySeparatorChar))).Length);
                    Console.WriteLine("  RAM disk  peak " + (peak / 1024 / 1024.0).ToString("0.0") + " MB used during the install, for "
                                      + (installed / 1024 / 1024.0).ToString("0.0") + " MB installed");
                    // The game once, and the NTFS overhead of its files - not the game plus a copy. The
                    // unpacking fallback (LBIP_VITA3K_NO_ZIP=1) holds a file twice at worst: not checked.
                    if (Environment.GetEnvironmentVariable("LBIP_VITA3K_NO_ZIP") != "1")
                        Check("the RAM disk never held more than the installed game (+10% and 8 MB of filesystem overhead)",
                              peak <= installed + installed / 10 + 8L * 1024 * 1024,
                              (peak / 1024 / 1024) + " MB peak for " + (installed / 1024 / 1024) + " MB");
                    else
                        Console.WriteLine("            (the unpacking fallback: the peak is shown, not held to the game's size)");
                }

                if (drive != null)
                {
                    // The oracle comparison needs the files after the drive is gone.
                    var keep = Path.Combine(Path.GetTempPath(), "lbip-vita3k-install-ramdisk-" + Guid.NewGuid().ToString("N"));
                    CopyAll(fs, keep);
                    Console.WriteLine("  kept      " + keep + "  (for the oracle comparison)");
                }
                else Console.WriteLine("  kept      " + fs + "  (for the oracle comparison - it is not deleted)");
                }
                finally
                {
                    if (drive != null)
                    {
                        Console.WriteLine("  unmounting " + drive + " ...");
                        Check("the RAM disk unmounts", LbIntegrations.RamDisk.RamDrive.UnmountFor("probe-vita3k"));
                    }
                }

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

            // THE FAST REFERENCE MUST BE THE SLOW ONE, BYTE FOR BYTE. It is built from the base's own
            // manifest plus a hash of what the install wrote; a full walk of the same tree, taken now -
            // nothing has touched it since, there is no save to restore on a first session - is the
            // answer it has to give.
            var slow = Path.Combine(Path.GetTempPath(), "lbip-probe-full-" + Guid.NewGuid().ToString("N") + ".manifest");
            LbIntegrations.Snapshot.SnapWalk.Write(work, slow, out _);
            Check("the reference from the base is the full walk, byte for byte",
                  File.ReadAllText(Path.Combine(portable, "work.reference")) == File.ReadAllText(slow));
            File.Delete(slow);

            FallsBackWhenTheTreeIsNotTheBase(portable);
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

        /// <summary>A tree that is NOT the base plus the fresh folders must be walked in full - a file the
        /// fast path skipped would come out of the next session as a "change", into a save. A leftover
        /// file, a file of another size, a missing one: each must fall back, and each must still give
        /// the full walk's bytes.</summary>
        private static void FallsBackWhenTheTreeIsNotTheBase(string portable)
        {
            var baseDir = Path.Combine(portable, "nand-initiale");
            var baseManifest = Path.Combine(portable, "nand-initiale.manifest");
            if (!Directory.Exists(baseDir) || !File.Exists(baseManifest)) { Check("a base to test against", false); return; }

            void Trial(string what, Action<string> spoil, bool expectFallback)
            {
                var tree = Path.Combine(Path.GetTempPath(), "lbip-probe-tree-" + Guid.NewGuid().ToString("N"));
                try
                {
                    foreach (var d in Directory.GetDirectories(baseDir, "*", SearchOption.AllDirectories))
                        Directory.CreateDirectory(Path.Combine(tree, d.Substring(baseDir.Length).TrimStart('\\')));
                    Directory.CreateDirectory(tree);
                    foreach (var f in Directory.GetFiles(baseDir, "*", SearchOption.AllDirectories))
                        File.Copy(f, Path.Combine(tree, f.Substring(baseDir.Length).TrimStart('\\')));
                    spoil?.Invoke(tree);

                    var fast = tree + ".fast";
                    var full = tree + ".full";
                    LbIntegrations.Snapshot.SnapWalk.WriteFrom(tree, baseManifest, new string[0], fast, out _, null, out int hashed);
                    LbIntegrations.Snapshot.SnapWalk.Write(tree, full, out _);
                    Check("  " + what + (expectFallback ? " - falls back to a full walk" : " - reads nothing"),
                          expectFallback ? hashed < 0 : hashed == 0);
                    Check("  " + what + " - same bytes as the full walk", File.ReadAllText(fast) == File.ReadAllText(full));
                    File.Delete(fast);
                    File.Delete(full);
                }
                finally { try { Directory.Delete(tree, true); } catch { } }
            }

            Console.WriteLine("  the fast reference against trees that are not the base");
            Trial("the base as it is", null, expectFallback: false);
            Trial("a leftover file", t => File.WriteAllText(Path.Combine(t, "leftover.txt"), "not the base"), expectFallback: true);
            Trial("a file of another size", t =>
            {
                var any = Directory.GetFiles(t, "*", SearchOption.AllDirectories)[0];
                File.AppendAllText(any, "grown");
            }, expectFallback: true);
            Trial("a missing file", t => File.Delete(Directory.GetFiles(t, "*", SearchOption.AllDirectories)[0]), expectFallback: true);
        }

        /// <summary>A junction whose target is GONE - what a reboot leaves: the RAM disk vanished,
        /// portable\fs still points at it. It has to be recognised as a link and removed, or the next
        /// mklink fails because the name is taken. An Exists check asks about the target and can
        /// miss it; the plugin reads the entry's own attributes.</summary>
        private static void OrphanedJunction(object layout, string portable, string root)
        {
            Console.WriteLine();
            Console.WriteLine("  an orphaned junction");

            Call("Vita3kWorkspace", "Teardown", new object[] { layout });
            var link = Path.Combine(portable, "fs");
            var target = Path.Combine(root, "vanished-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(target);

            var psi = new System.Diagnostics.ProcessStartInfo("cmd.exe", "/c mklink /J \"" + link + "\" \"" + target + "\"")
            { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true };
            using (var p = System.Diagnostics.Process.Start(psi)) p.WaitForExit(30000);
            Directory.Delete(target);

            bool listed() => Directory.EnumerateFileSystemEntries(portable).Any(e => string.Equals(e, link, StringComparison.OrdinalIgnoreCase));
            if (!Check("a junction pointing nowhere is in place", listed())) return;

            Call("Vita3kWorkspace", "Teardown", new object[] { layout });
            Check("tearing down removes it all the same", !listed());
        }

        /// <summary>The proof of ownership, on REAL RAM disks: a drive under the marker's letter that
        /// is not this console's - LiteBox's, say - must be left mounted; one that is, released.
        /// MOUNTS REAL DRIVES through the elevated task of <paramref name="lbRoot"/>.</summary>
        public static bool Ownership(Assembly pluginAssembly, string lbRoot)
        {
            Console.WriteLine();
            Console.WriteLine("-- Vita3K, whose RAM disk is it  [MOUNTS REAL DRIVES] " + new string('-', 9));
            _asm = pluginAssembly;
            _bad = 0;
            var root = Path.Combine(Path.GetTempPath(), "lbip-vita3k-owner-" + Guid.NewGuid().ToString("N"));
            string drive = null;
            try
            {
                if (string.IsNullOrWhiteSpace(lbRoot) || !Directory.Exists(lbRoot)) { Console.WriteLine("  pass --lb <LaunchBox root>"); return false; }
                var install = Path.Combine(root, "Emulators", "Nixx-Vita3K");
                var portable = Path.Combine(install, "portable");
                Directory.CreateDirectory(portable);
                File.WriteAllText(Path.Combine(install, "Vita3K.exe"), "not really an executable");
                var layout = Resolve(Path.Combine(install, "Vita3K.exe"));

                LbIntegrations.RamDisk.RamDiskHost.UseRoot(lbRoot);
                drive = LbIntegrations.RamDisk.RamDrive.MountFor("probe-owner", 64);
                if (!Check("a RAM disk mounts", drive != null)) return false;
                var fs = Path.Combine(drive, "fs");
                Directory.CreateDirectory(fs);
                File.WriteAllText(Path.Combine(portable, "work.title"), TitleId + "\t0\t0\t0\t" + fs);

                // Somebody else's drive under our old letter: no proof on it.
                Call("Vita3kWorkspace", "CleanUpAtStart", new object[] { layout });
                Check("a drive without our proof is left mounted by the start-up check", Directory.Exists(drive));
                Call("Vita3kWorkspace", "Teardown", new object[] { layout });
                Check("and by a teardown", Directory.Exists(drive));
                Check("(the teardown still forgot our session)", !File.Exists(Path.Combine(portable, "work.title")));

                // Ours: the proof names this console's portable folder.
                File.WriteAllText(Path.Combine(portable, "work.title"), TitleId + "\t0\t0\t0\t" + fs);
                File.WriteAllText(Path.Combine(drive, "lbip-vita3k.owner"), Path.GetFullPath(portable));
                Call("Vita3kWorkspace", "CleanUpAtStart", new object[] { layout });
                bool gone = !Directory.Exists(drive);
                Check("a drive with our proof, outliving its session, is released at start-up", gone);
                if (gone) drive = null;

                Console.WriteLine();
                Console.WriteLine(_bad == 0 ? "  OK - only this console's drive is ever released" : "  " + _bad + " FAILURE(S) - see above");
                return _bad == 0;
            }
            catch (Exception ex) { Console.WriteLine("  EXCEPTION: " + ex); return false; }
            finally
            {
                if (drive != null)
                {
                    LbIntegrations.RamDisk.RamDiskHost.UseRoot(lbRoot);
                    LbIntegrations.RamDisk.RamDrive.Unmount(drive);
                }
                Scrub(root);
            }
        }

        /// <summary>Where --use-vhdx may keep its disks, asked of the REAL volumes of this machine:
        /// a missing drive, a CD, a relative path, a network share - each refused with its reason - and
        /// a folder under %TEMP% accepted. Needs a real install for its helper: --emu &lt;Vita3K.exe&gt;.
        /// Creates nothing but that %TEMP% folder, removed afterwards.</summary>
        public static bool VhdxFolder(Assembly pluginAssembly, string emuPath)
        {
            Console.WriteLine();
            Console.WriteLine("-- Vita3K, where the VHDX may live " + new string('-', 29));
            _asm = pluginAssembly;
            _bad = 0;
            var temp = Path.Combine(Path.GetTempPath(), "lbip-vhdx-folder-" + Guid.NewGuid().ToString("N"));
            try
            {
                if (string.IsNullOrWhiteSpace(emuPath) || !File.Exists(emuPath)) { Console.WriteLine("  pass --emu <Vita3K.exe of a real install>"); return false; }
                var layout = Resolve(emuPath);
                string Why(string dir)
                {
                    var why = (string)Call("Vita3kVhdx", "WhyNot", new object[] { layout, dir });
                    Console.WriteLine("      " + dir + "  ->  " + (why ?? "usable"));
                    return why;
                }

                var dflt = (string)Call("Vita3kVhdx", "DirFor", new object[] { layout, null });
                Check("the flag alone: the emulator's own vhdx folder",
                      string.Equals(dflt, Path.Combine((string)Field(layout, "InstallDir"), "vhdx"), StringComparison.OrdinalIgnoreCase));
                Check("a folder given is taken as it is", (string)Call("Vita3kVhdx", "DirFor", new object[] { layout, @"E:\VitaVhdx" }) == @"E:\VitaVhdx");

                Check("a folder under %TEMP% is usable, and created", Why(temp) == null && Directory.Exists(temp));
                Check("a relative folder is refused", Why(@"vhdx") != null);
                Check("a network share is refused", Why(@"\\server\share\vhdx") != null);

                char missing = '\0';
                for (char c = 'Z'; c >= 'I'; c--) if (!Directory.Exists(c + @":\")) { missing = c; break; }
                if (missing != '\0')
                {
                    var why = Why(missing + @":\VitaVhdx");
                    Check("a drive that is not there is refused, and says so", why != null && why.Contains("not there"));
                }
                foreach (var d in DriveInfo.GetDrives())
                {
                    if (d.DriveType != DriveType.CDRom || !d.IsReady) continue;
                    var why = Why(d.RootDirectory.FullName + "VitaVhdx");
                    Check("a " + d.DriveFormat + " drive (" + d.Name + ") is refused, by its file system", why != null && why.Contains(d.DriveFormat));
                    break;
                }

                Console.WriteLine();
                Console.WriteLine(_bad == 0 ? "  OK - a folder the VHDX cannot live in is refused with its reason" : "  " + _bad + " FAILURE(S) - see above");
                return _bad == 0;
            }
            catch (Exception ex) { Console.WriteLine("  EXCEPTION: " + (ex.InnerException ?? ex)); return false; }
            finally { Scrub(temp); }
        }

        /// <summary>The bases --use-vhdx builds, on REAL virtual disks under %TEMP%: firmware.vhdx and
        /// the game's differencing disk over it, built once, reused, and rebuilt for each reason an
        /// identity can give - the game, the firmware, an identity missing - and not built while the
        /// folder's lock is held. Needs a real install (its helper and pristine firmware, only READ):
        /// --vita3k-vhdx-base --emu &lt;Vita3K.exe&gt; --rom &lt;game archive&gt;. LBIP_PROBE_KEEP=1 leaves the folder.</summary>
        public static bool VhdxBase(Assembly pluginAssembly, string emuPath, string romPath)
        {
            Console.WriteLine();
            Console.WriteLine("-- Vita3K, the VHDX bases  [ATTACHES DISKS] " + new string('-', 19));
            _asm = pluginAssembly;
            _bad = 0;
            var dir = Path.Combine(Path.GetTempPath(), "lbip-vhdx-base-" + Guid.NewGuid().ToString("N"));
            try
            {
                if (string.IsNullOrWhiteSpace(emuPath) || !File.Exists(emuPath)) { Console.WriteLine("  pass --emu <Vita3K.exe of a real install>"); return false; }
                if (string.IsNullOrWhiteSpace(romPath) || !File.Exists(romPath)) { Console.WriteLine("  pass --rom <game archive>"); return false; }
                var layout = Resolve(emuPath);
                var lbRoot = (string)Call("Vita3kPaths", "LaunchBoxRootOf", new object[] { layout });
                LbIntegrations.RamDisk.RamDiskHost.UseRoot(lbRoot);
                if (!Check("the folder is usable", Call("Vita3kVhdx", "WhyNot", new object[] { layout, dir }) == null)) return false;

                var described = new object[] { romPath, null };
                var content = Call("Vita3kContent", "Describe", described);
                if (!Check("the game is described", content != null, described[1] as string)) return false;
                var extras = Call("Vita3kExtras", "For", new object[] { romPath, content, null, null, null });
                var id = (string)Field(content, "TitleId");

                var watch = System.Diagnostics.Stopwatch.StartNew();
                string Build(out string error)
                {
                    watch.Restart();
                    var a = new object[] { layout, dir, romPath, content, extras, null, null };
                    var got = (string)Call("Vita3kVhdx", "EnsureGameBase", a);
                    error = a[6] as string;
                    Console.WriteLine("            " + watch.ElapsedMilliseconds + " ms" + (error != null ? " - " + error : ""));
                    return got;
                }
                string fw = Path.Combine(dir, "firmware.vhdx"), fwId = Path.Combine(dir, "firmware.identity");
                string game = Path.Combine(dir, id + ".vhdx"), gameId = Path.Combine(dir, id + ".identity"), gameRef = Path.Combine(dir, id + ".reference");
                string Build_() => (File.ReadAllLines(fwId).FirstOrDefault(l => l.StartsWith("build=")) ?? "");

                Console.WriteLine("  first launch: both disks built");
                var disk = Build(out var err);
                if (!Check("the game's disk is built", disk != null && File.Exists(disk), err)) return false;
                Check("the firmware disk and its identity are there", File.Exists(fw) && File.Exists(fwId));
                Check("the game's identity and reference are there", File.Exists(gameId) && File.Exists(gameRef));
                foreach (var l in File.ReadAllLines(gameId)) Console.WriteLine("            | " + l);
                Check("the identity names the game by name, size and time, not by path",
                      File.ReadAllLines(gameId).Any(l => l == "game=" + Path.GetFileName(romPath) + "|" + new FileInfo(romPath).Length + "|"
                                                           + new FileInfo(romPath).LastWriteTimeUtc.Ticks + "|" + (Field(content, "AppVer") ?? "")));
                Check("and the firmware build it sits on", File.ReadAllLines(gameId).Contains("firmware=" + Build_().Substring(6)));
                Console.WriteLine("            firmware " + new FileInfo(fw).Length / (1024 * 1024) + " MB, game " + new FileInfo(disk).Length / (1024 * 1024) + " MB on disk");

                var root = LbIntegrations.RamDisk.RamDrive.AttachVhdx(game, true, out err);
                if (Check("the game's disk attaches read-only", root != null, err))
                {
                    Check("it holds the game", File.Exists(Path.Combine(root, "fs", "ux0", "app", id, "sce_sys", "param.sfo")));
                    Check("over the firmware", Directory.Exists(Path.Combine(root, "fs", "vs0")) && Directory.Exists(Path.Combine(root, "fs", "os0")));
                    Check("and no update left in ux0/patch", !Directory.Exists(Path.Combine(root, "fs", "ux0", "patch", id)));
                    LbIntegrations.RamDisk.RamDrive.DetachVhdx(game, out _);
                }

                Console.WriteLine("  second launch: nothing built");
                var stamp = File.GetLastWriteTimeUtc(game);
                var build = Build_();
                disk = Build(out err);
                Check("the same disk comes back, untouched", disk != null && File.GetLastWriteTimeUtc(game) == stamp && Build_() == build, err);
                Check("in under two seconds", watch.ElapsedMilliseconds < 2000);

                Console.WriteLine("  the game is not the one it was built from: the game's disk is rebuilt");
                File.WriteAllLines(gameId, File.ReadAllLines(gameId).Select(l => l.StartsWith("game=") ? "game=other.zip|1|1|" : l));
                disk = Build(out err);
                Check("rebuilt, on the same firmware", disk != null && Build_() == build
                      && File.ReadAllLines(gameId).Any(l => l.StartsWith("game=" + Path.GetFileName(romPath))), err);

                Console.WriteLine("  its identity is missing (a build cut short): rebuilt");
                File.Delete(gameId);
                disk = Build(out err);
                Check("rebuilt", disk != null && File.Exists(gameId), err);

                Console.WriteLine("  another firmware: both rebuilt");
                File.WriteAllLines(fwId, File.ReadAllLines(fwId).Select(l => l.StartsWith("nand=") ? "nand=0000" : l));
                disk = Build(out err);
                Check("the firmware disk is a new build", disk != null && Build_() != build && Build_().Length > 6, err);
                Check("and the game's disk sits on it", File.ReadAllLines(gameId).Contains("firmware=" + Build_().Substring(6)));
                root = LbIntegrations.RamDisk.RamDrive.AttachVhdx(game, true, out err);
                if (Check("and attaches", root != null, err))
                    LbIntegrations.RamDisk.RamDrive.DetachVhdx(game, out _);

                Console.WriteLine("  the folder's lock is held by somebody else: nothing is built");
                File.Delete(gameId);
                using (new FileStream(Path.Combine(dir, "lbip-vhdx.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
                {
                    disk = Build(out err);
                    Check("refused, and says why", disk == null && err != null && err.Contains("another install"));
                }
                disk = Build(out err);
                Check("and built once it is released", disk != null && File.Exists(gameId), err);

                // THE FOLDER MOVED - an external drive back under another letter looks the same to a
                // differencing disk: its parent's absolute path is gone, only the relative one is left.
                Console.WriteLine("  the whole folder moved elsewhere: the chain still resolves");
                var moved = dir + "-moved";
                Directory.Move(dir, moved);
                dir = moved;
                var session = Path.Combine(moved, id + ".session.vhdx");
                Check("a session disk is created over the moved game's disk",
                      LbIntegrations.RamDisk.RamDrive.CreateDifferencingVhdx(session, Path.Combine(moved, id + ".vhdx"), out err), err);
                root = LbIntegrations.RamDisk.RamDrive.AttachVhdx(session, true, out err);
                if (Check("it attaches, three levels down a moved folder", root != null, err))
                {
                    Check("and sees the game and the firmware", File.Exists(Path.Combine(root, "fs", "ux0", "app", id, "sce_sys", "param.sfo"))
                          && Directory.Exists(Path.Combine(root, "fs", "vs0")));
                    LbIntegrations.RamDisk.RamDrive.DetachVhdx(session, out _);
                }
                root = LbIntegrations.RamDisk.RamDrive.AttachVhdx(Path.Combine(moved, id + ".vhdx"), true, out err);
                if (Check("the game's disk attaches from the moved folder too", root != null, err))
                    LbIntegrations.RamDisk.RamDrive.DetachVhdx(Path.Combine(moved, id + ".vhdx"), out _);

                Console.WriteLine();
                Console.WriteLine(_bad == 0 ? "  OK - a base is built once, reused, and rebuilt exactly when its identity says so" : "  " + _bad + " FAILURE(S) - see above");
                return _bad == 0;
            }
            catch (Exception ex) { Console.WriteLine("  EXCEPTION: " + (ex.InnerException ?? ex)); return false; }
            finally
            {
                if (Environment.GetEnvironmentVariable("LBIP_PROBE_KEEP") == "1") Console.WriteLine("  kept: " + dir);
                else Scrub(dir);
            }
        }

        /// <summary>Whole sessions with --use-vhdx, on REAL disks: a session on a differencing disk over
        /// the game's, captured and thrown away; a relaunch that installs nothing and finds its save; a
        /// reboot (the session disk detached under a live marker) saved at start-up; an orphaned session
        /// disk (its marker gone) saved by the next launch. A forged emulator under
        /// &lt;LaunchBox root&gt;\Emulators\lbip-probe-* (the helper is found from there), removed
        /// afterwards; the disks under %TEMP%. The real install is only READ, for its firmware.
        /// --vita3k-vhdx-session --lb &lt;LaunchBox root&gt; --rom &lt;game archive&gt;</summary>
        public static bool VhdxSession(Assembly pluginAssembly, string lbRoot, string romPath)
        {
            Console.WriteLine();
            Console.WriteLine("-- Vita3K, sessions on a VHDX  [ATTACHES DISKS] " + new string('-', 15));
            _asm = pluginAssembly;
            _bad = 0;
            if (string.IsNullOrWhiteSpace(lbRoot) || !Directory.Exists(lbRoot)) { Console.WriteLine("  pass --lb <LaunchBox root>"); return false; }
            if (string.IsNullOrWhiteSpace(romPath) || !File.Exists(romPath)) { Console.WriteLine("  pass --rom <game archive>"); return false; }
            var real = Path.Combine(lbRoot, "Emulators", "Nixx-Vita3K", "portable");
            var install = Path.Combine(lbRoot, "Emulators", "lbip-probe-" + Guid.NewGuid().ToString("N"));
            var portable = Path.Combine(install, "portable");
            var dir = Path.Combine(Path.GetTempPath(), "lbip-vhdx-session-" + Guid.NewGuid().ToString("N"));
            object layout = null;
            string id = null;
            try
            {
                Directory.CreateDirectory(portable);
                File.WriteAllText(Path.Combine(install, "Vita3K.exe"), "not really an executable");
                Console.WriteLine("  copying the real pristine firmware into the forged emulator...");
                CopyDir(Path.Combine(real, "nand-initiale"), Path.Combine(portable, "nand-initiale"));
                File.Copy(Path.Combine(real, "nand-initiale.manifest"), Path.Combine(portable, "nand-initiale.manifest"));
                layout = Resolve(Path.Combine(install, "Vita3K.exe"));
                LbIntegrations.RamDisk.RamDiskHost.UseRoot(lbRoot);

                var ws = _asm.GetType("LbIntegrations.Vita3k.Vita3kWorkspace", throwOnError: true);
                ws.GetField("Ask", BindingFlags.NonPublic | BindingFlags.Static)
                  .SetValue(null, new Func<string, string, bool>((t, x) => { Console.WriteLine("    [asked] " + t); return true; }));

                var watch = System.Diagnostics.Stopwatch.StartNew();
                string Launch(out string error)
                {
                    var launch = Activator.CreateInstance(_asm.GetType("LbIntegrations.Vita3k.Vita3kLaunch", throwOnError: true));
                    launch.GetType().GetField("UseVhdx").SetValue(launch, true);
                    launch.GetType().GetField("VhdxDir").SetValue(launch, dir);
                    var a = new object[] { layout, romPath, null, null, launch };
                    watch.Restart();
                    var got = (string)Call("Vita3kWorkspace", "Prepare", a);
                    error = a[2] as string;
                    Console.WriteLine("            prepared in " + watch.ElapsedMilliseconds + " ms" + (error != null ? " - " + error : ""));
                    return got;
                }
                string Marker() { var m = Path.Combine(portable, "work.title"); return File.Exists(m) ? File.ReadAllText(m) : null; }
                string Root() => Marker()?.Split('\t')[4];
                string Sav(string root) => Path.Combine(root, "ux0", "user", "00", "savedata", id, "probe.sav");
                string Read(string root) { var p = Sav(root); return File.Exists(p) ? File.ReadAllText(p) : null; }
                void Play(string root, string text) { Directory.CreateDirectory(Path.GetDirectoryName(Sav(root))); File.WriteAllText(Sav(root), text); }
                bool End() { watch.Restart(); var ok = (bool)Call("Vita3kWorkspace", "CaptureOnExit", new object[] { layout, id }); Console.WriteLine("            ended in " + watch.ElapsedMilliseconds + " ms"); return ok; }

                Console.WriteLine("  1. the first launch: disks built, a session disk over them");
                id = Launch(out var err);
                if (!Check("the session is ready", id != null, err)) return false;
                var session = Path.Combine(dir, id + ".session.vhdx");
                var game = Path.Combine(dir, id + ".vhdx");
                var marker = Marker();
                Check("the marker names the session disk", marker != null && marker.Split('\t').Length == 7 && marker.Split('\t')[6] == session);
                Check("the session disk is there", File.Exists(session));
                var root = Root();
                Check("the console sees the game", File.Exists(Path.Combine(root, "ux0", "app", id, "sce_sys", "param.sfo")));
                Check("through portable\\fs", File.Exists(Path.Combine(portable, "fs", "ux0", "app", id, "sce_sys", "param.sfo")));
                var gameStamp = File.GetLastWriteTimeUtc(game);
                Play(root, "session 1");

                if (!Check("2. the end of the session is captured", End())) return false;
                Check("the save is there", File.Exists(Path.Combine(portable, "saves", id, "state.vitasav")));
                Check("the session disk is gone", !File.Exists(session));
                Check("and the junction", !Directory.Exists(Path.Combine(portable, "fs")));
                Check("and the marker", Marker() == null);
                Check("the game's disk is untouched", File.GetLastWriteTimeUtc(game) == gameStamp);

                Console.WriteLine("  3. the next launch: nothing installed");
                id = Launch(out err);
                if (!Check("the session is ready", id != null, err)) return false;
                Check("in under ten seconds", watch.ElapsedMilliseconds < 10000);
                root = Root();
                Check("with the save put back", Read(root) == "session 1", Read(root));
                Play(root, "session 2");

                Console.WriteLine("  4. a reboot: the session disk detached under its marker, then the start-up check");
                Check("detached", LbIntegrations.RamDisk.RamDrive.DetachVhdx(session, out err) && !Directory.Exists(root), err);
                Call("Vita3kWorkspace", "CleanUpAtStart", new object[] { layout });
                Check("the session is gone, disk and marker", !File.Exists(session) && Marker() == null);
                id = Launch(out err);
                root = Root();
                Check("and its save came out of it", id != null && Read(root) == "session 2", Read(root ?? "") ?? err);
                Play(root, "session 3");

                Console.WriteLine("  5. an orphan: the session disk detached AND its marker lost, then a launch");
                Check("detached", LbIntegrations.RamDisk.RamDrive.DetachVhdx(session, out err), err);
                File.Delete(Path.Combine(portable, "work.title"));
                System.Threading.Thread.Sleep(1100);
                id = Launch(out err);
                root = Root();
                Check("the orphan's save came out of it", id != null && Read(root) == "session 3", Read(root ?? "") ?? err);
                Check("and a fresh session disk replaced it", File.Exists(session) && Marker()?.Split('\t')[6] == session);

                Check("6. the last session ends cleanly", End() && !File.Exists(session));
                Check("the folder holds the bases and nothing else", Directory.GetFiles(dir, "*.vhdx").Select(Path.GetFileName)
                      .OrderBy(n => n).SequenceEqual(new[] { id + ".vhdx", "firmware.vhdx" }.OrderBy(n => n)));

                Console.WriteLine();
                Console.WriteLine(_bad == 0 ? "  OK - every session on a VHDX comes out as a save, and nothing is left attached" : "  " + _bad + " FAILURE(S) - see above");
                return _bad == 0;
            }
            catch (Exception ex) { Console.WriteLine("  EXCEPTION: " + (ex.InnerException ?? ex)); return false; }
            finally
            {
                try
                {
                    var link = Path.Combine(portable, "fs");
                    if (Directory.Exists(link) && new DirectoryInfo(link).Attributes.HasFlag(FileAttributes.ReparsePoint)) Directory.Delete(link);
                }
                catch { }
                if (id != null) LbIntegrations.RamDisk.RamDrive.DetachVhdx(Path.Combine(dir, id + ".session.vhdx"), out _);
                if (Environment.GetEnvironmentVariable("LBIP_PROBE_KEEP") == "1") Console.WriteLine("  kept: " + dir + " and " + install);
                else { Scrub(dir); Scrub(install); }
                Console.WriteLine("  removed: " + (!Directory.Exists(dir) && !Directory.Exists(install)));
            }
        }

        private static void CopyDir(string from, string to)
        {
            foreach (var d in Directory.GetDirectories(from, "*", SearchOption.AllDirectories))
                Directory.CreateDirectory(Path.Combine(to, Path.GetRelativePath(from, d)));
            Directory.CreateDirectory(to);
            foreach (var f in Directory.GetFiles(from, "*", SearchOption.AllDirectories))
            {
                var target = Path.Combine(to, Path.GetRelativePath(from, f));
                File.Copy(f, target);
                File.SetLastWriteTimeUtc(target, File.GetLastWriteTimeUtc(f));
            }
        }

        /// <summary>The right-click entry, as the relay finds it. NO game menu type in this assembly -
        /// a plugin in Local\Plugins never has one asked on LaunchBox 14, and LiteBox, which loads both
        /// roots, would show it twice beside Nixx-Menus - and a GameMenu class with exactly the
        /// signatures the relay binds. Then the REAL relay (src\Menus, built in this checkout) is
        /// loaded and must find it.</summary>
        private static void TheGameMenu()
        {
            Console.WriteLine();
            Console.WriteLine("  the right-click entry on games");
            Check("no type of this plugin is a game menu (the relay shows it)",
                  !_asm.GetExportedTypes().Any(x => typeof(Unbroken.LaunchBox.Plugins.IGameMenuItemPlugin).IsAssignableFrom(x)
                                                 || typeof(Unbroken.LaunchBox.Plugins.IGameMultiMenuItemPlugin).IsAssignableFrom(x)));
            var t = _asm.GetType("LbIntegrations.Vita3k.GameMenu");
            if (!Check("LbIntegrations.Vita3k.GameMenu is there, public", t != null && t.IsPublic && t.IsAbstract && t.IsSealed)) return;
            var flags = BindingFlags.Public | BindingFlags.Static;
            var entries = t.GetMethod("Entries", flags, null, new[] { typeof(Unbroken.LaunchBox.Plugins.Data.IGame[]) }, null);
            Check("Entries(IGame[]) -> string[]", entries != null && entries.ReturnType == typeof(string[]));
            Check("Selected(string, IGame[])", t.GetMethod("Selected", flags, null, new[] { typeof(string), typeof(Unbroken.LaunchBox.Plugins.Data.IGame[]) }, null) != null);
            Check("an icon", t.GetProperty("Icon", flags)?.GetValue(null) is System.Drawing.Image);
            Check("nothing offered for no game", entries != null
                  && ((string[])entries.Invoke(null, new object[] { null })).Length == 0
                  && ((string[])entries.Invoke(null, new object[] { new Unbroken.LaunchBox.Plugins.Data.IGame[0] })).Length == 0);

            // THE RELAY ITSELF, as built in this checkout.
            var dir = Path.GetDirectoryName(_asm.Location);
            string relay = null;
            for (int up = 0; up < 6 && dir != null && relay == null; up++, dir = Path.GetDirectoryName(dir))
            {
                var candidate = Path.Combine(dir, "Menus", "bin", "Release", "NixxMenus.dll");
                if (File.Exists(candidate)) relay = candidate;
            }
            if (!Check("the relay is built (dotnet build src\\Menus -c Release)", relay != null)) return;
            var menus = Assembly.LoadFrom(relay);
            var plugin = menus.GetType("LbIntegrations.Menus.NixxGameMenus");
            Check("the relay is a multi-entry game menu", plugin != null && plugin.IsPublic
                  && typeof(Unbroken.LaunchBox.Plugins.IGameMultiMenuItemPlugin).IsAssignableFrom(plugin));
            var all = (System.Collections.IList)menus.GetType("LbIntegrations.Menus.Providers")
                          .GetMethod("All", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, null);
            var names = all.Cast<object>().Select(o => (string)o.GetType().GetField("Name").GetValue(o)).ToList();
            Console.WriteLine("            relaying: " + string.Join(", ", names));
            Check("and it finds this plugin by name", names.Contains(_asm.GetName().Name));
            var instance = (Unbroken.LaunchBox.Plugins.IGameMultiMenuItemPlugin)Activator.CreateInstance(plugin);
            Check("and offers nothing for no game", !instance.GetMenuItems(new Unbroken.LaunchBox.Plugins.Data.IGame[0]).Any());
        }

        /// <summary>The documentation fetched after an install, from a local server: nothing at all for an
        /// empty list; the doc folder made; a file that comes, one that answers 404, one too slow for the
        /// timeout - the last two skipped, the install's message saying so.</summary>
        private static void TheDocs(string root)
        {
            Console.WriteLine();
            Console.WriteLine("  the documentation fetched after an install  [a local HTTP server]");
            var emu = Path.Combine(root, "docs-emulator");
            Directory.CreateDirectory(emu);
            var fetch = _asm.GetType("LbIntegrations.Vita3k.Vita3kDocs", throwOnError: true).GetMethods(BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static)
                            .First(m => m.Name == "Fetch" && m.GetParameters().Length == 5);
            string Fetch(string[] urls, int seconds) => (string)fetch.Invoke(null, new object[] { emu, urls, TimeSpan.FromSeconds(seconds), null, null });
            var folderName = (string)_asm.GetType("LbIntegrations.Vita3k.Vita3kDocs").GetField("FolderName").GetValue(null);

            Check("an empty list: nothing said, no " + folderName + " folder", Fetch(new string[0], 5) == "" && !Directory.Exists(Path.Combine(emu, folderName)));

            int port = 0;
            var probe = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
            probe.Start(); port = ((System.Net.IPEndPoint)probe.LocalEndpoint).Port; probe.Stop();
            var prefix = "http://localhost:" + port + "/";
            using var server = new System.Net.HttpListener();
            server.Prefixes.Add(prefix);
            server.Start();
            var serving = new System.Threading.Thread(() =>
            {
                while (server.IsListening)
                {
                    System.Net.HttpListenerContext ctx;
                    try { ctx = server.GetContext(); } catch { return; }
                    try
                    {
                        var path = ctx.Request.Url.AbsolutePath;
                        if (path.EndsWith("/guide%20one.pdf") || path.EndsWith("/guide one.pdf"))
                        {
                            var bytes = System.Text.Encoding.ASCII.GetBytes("the first document");
                            ctx.Response.OutputStream.Write(bytes, 0, bytes.Length);
                        }
                        else if (path.EndsWith("/slow.txt")) { System.Threading.Thread.Sleep(3000); }
                        else ctx.Response.StatusCode = 404;
                        ctx.Response.Close();
                    }
                    catch { }
                }
            }) { IsBackground = true };
            serving.Start();
            try
            {
                var said = Fetch(new[] { prefix + "docs/guide%20one.pdf?x=1", prefix + "missing.pdf", prefix + "slow.txt" }, 1);
                Console.WriteLine("            " + said.Trim());
                var doc = Path.Combine(emu, folderName);
                Check("the " + folderName + " folder is made", Directory.Exists(doc));
                Check("the document that came is there, under its URL's name", File.Exists(Path.Combine(doc, "guide one.pdf"))
                      && File.ReadAllText(Path.Combine(doc, "guide one.pdf")) == "the first document");
                Check("the 404 and the one too slow are skipped, no partial file left",
                      Directory.GetFiles(doc).Length == 1);
                Check("and the install's message says 1 of 3", said.Contains("1 of 3"));
                File.WriteAllText(Path.Combine(doc, "guide one.pdf"), "an older copy");
                Fetch(new[] { prefix + "docs/guide%20one.pdf" }, 5);
                Check("fetched again: the previous copy replaced", File.ReadAllText(Path.Combine(doc, "guide one.pdf")) == "the first document");
            }
            finally { server.Stop(); }
        }

        /// <summary>The Vita3K tab of the pack's configuration window, and the relay hosting it: the
        /// Settings contract by name, the bypass of LaunchBox's Vita import ON by default, a tab that
        /// saves to its file and reads back - on a settings file under %TEMP%, never the install's.
        /// LBIP_PROBE_SHOT_SETTINGS=&lt;png&gt; draws the window for a look at its layout.</summary>
        private static void TheSettings()
        {
            Console.WriteLine();
            Console.WriteLine("  the configuration window: the Vita3K tab");
            var settingsType = _asm.GetType("LbIntegrations.Vita3k.Vita3kSettings", throwOnError: true);
            var overrideField = settingsType.GetField("PathOverride", BindingFlags.NonPublic | BindingFlags.Static);
            var file = Path.Combine(Path.GetTempPath(), "lbip-settings-" + Guid.NewGuid().ToString("N"), "settings.ini");
            overrideField.SetValue(null, file);
            try
            {
                bool Bypass() => (bool)settingsType.GetProperty("BypassVitaImport", BindingFlags.Public | BindingFlags.Static).GetValue(null);
                Check("with no settings file, the bypass of LaunchBox's Vita import is ON", Bypass());

                var contract = _asm.GetType("LbIntegrations.Vita3k.Settings");
                if (!Check("LbIntegrations.Vita3k.Settings is there, public", contract != null && contract.IsPublic)) return;
                var flags = BindingFlags.Public | BindingFlags.Static;
                Check("its tab is called Vita3K", (string)contract.GetProperty("Title", flags).GetValue(null) == "Vita3K");
                var page = (System.Windows.Forms.Control)contract.GetMethod("CreatePage", flags).Invoke(null, null);
                var box = page.Controls.Cast<System.Windows.Forms.Control>().SelectMany(c => c.Controls.Cast<System.Windows.Forms.Control>())
                              .OfType<System.Windows.Forms.CheckBox>().FirstOrDefault();
                Check("its page builds, the bypass ticked", box != null && box.Checked);
                box.Checked = false;
                var save = contract.GetMethod("Save", flags);
                Check("saved unticked: no complaint", save.Invoke(null, new object[] { page }) == null);
                Check("the file says so", File.Exists(file) && File.ReadAllText(file).Contains("BypassLaunchBoxVitaImport=false"));
                Check("and the setting reads it back", !Bypass());
                Check("a page that is not its own is refused", save.Invoke(null, new object[] { new System.Windows.Forms.Panel() }) != null);

                // THE RELAY, from this checkout's build.
                var dir = Path.GetDirectoryName(_asm.Location);
                string relay = null;
                for (int up = 0; up < 6 && dir != null && relay == null; up++, dir = Path.GetDirectoryName(dir))
                {
                    var candidate = Path.Combine(dir, "Menus", "bin", "Release", "NixxMenus.dll");
                    if (File.Exists(candidate)) relay = candidate;
                }
                if (!Check("the relay is built", relay != null)) return;
                var menus = Assembly.LoadFrom(relay);
                var menuType = menus.GetType("LbIntegrations.Menus.NixxSettingsMenu");
                Check("the relay has a Tools menu entry", menuType != null && menuType.IsPublic
                      && typeof(Unbroken.LaunchBox.Plugins.ISystemMenuItemPlugin).IsAssignableFrom(menuType));
                var entry = (Unbroken.LaunchBox.Plugins.ISystemMenuItemPlugin)Activator.CreateInstance(menuType);
                Console.WriteLine("            \"" + entry.Caption + "\"");
                Check("in LaunchBox, not in Big Box", entry.ShowInLaunchBox && !entry.ShowInBigBox && entry.IconImage != null);
                var providers = menus.GetType("LbIntegrations.Menus.SettingsProviders")
                                     .GetMethod("All", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, null);
                var names = ((System.Collections.IEnumerable)providers).Cast<object>().Select(o => (string)o.GetType().GetField("Name").GetValue(o)).ToList();
                Console.WriteLine("            settings relayed: " + string.Join(", ", names));
                Check("it finds this plugin's settings", names.Contains(_asm.GetName().Name));

                var formType = menus.GetType("LbIntegrations.Menus.NixxSettingsForm");
                using (var form = (System.Windows.Forms.Form)Activator.CreateInstance(formType, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance, null, new[] { providers }, null))
                {
                    var tabs = form.Controls.OfType<System.Windows.Forms.TabControl>().First();
                    var titles = tabs.TabPages.Cast<System.Windows.Forms.TabPage>().Select(t => t.Text).ToList();
                    Console.WriteLine("            tabs: " + string.Join(" | ", titles));
                    Check("the window has General, then the Vita3K tab", titles.Count >= 2 && titles[0] == "General" && titles.Contains("Vita3K"));

                    var shot = Environment.GetEnvironmentVariable("LBIP_PROBE_SHOT_SETTINGS");
                    if (!string.IsNullOrEmpty(shot))
                    {
                        form.StartPosition = System.Windows.Forms.FormStartPosition.Manual;
                        form.Location = new System.Drawing.Point(-4000, -4000);
                        form.Show();
                        foreach (var i in new[] { 0, titles.IndexOf("Vita3K") })
                        {
                            tabs.SelectedIndex = i;
                            System.Windows.Forms.Application.DoEvents();
                            using var bmp = new System.Drawing.Bitmap(form.Width, form.Height);
                            form.DrawToBitmap(bmp, new System.Drawing.Rectangle(0, 0, form.Width, form.Height));
                            var png = Path.Combine(Path.GetDirectoryName(shot), Path.GetFileNameWithoutExtension(shot) + "-" + i + ".png");
                            bmp.Save(png);
                            Console.WriteLine("            drawn to " + png);
                        }
                        form.Hide();
                    }
                }
            }
            finally
            {
                overrideField.SetValue(null, null);
                Scrub(Path.GetDirectoryName(file));
            }
        }

        /// <summary>What the import wizard's restore does to a game record: LaunchBox's GameRecord keeps its
        /// platform in a READONLY field under an obfuscated name, so every string field holding exactly
        /// the stand-in is written. Tried on a record of the same shape.</summary>
        private static void TheImportRecord()
        {
            Console.WriteLine();
            Console.WriteLine("  the import wizard: a game record given its platform back");
            var standIn = (string)_asm.GetType("LbIntegrations.Vita3k.Vita3kLbImport").GetField("StandIn", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null);
            var record = new FakeGameRecord(standIn, standIn, @"C:\games\a.zip");
            bool any = (bool)Call("Vita3kLbImport", "SetFields", new object[] { record, standIn, "Sony Playstation Vita" });
            Check("a readonly platform field is written", any && record.Platform == "Sony Playstation Vita");
            Check("and the scrape-as beside it", record.ScrapeAs == "Sony Playstation Vita");
            Check("and nothing else", record.ApplicationPath == @"C:\games\a.zip");
        }

        /// <summary>The shape of LaunchBox's GameRecord: get-only properties over readonly fields.</summary>
        private sealed class FakeGameRecord
        {
            private readonly string m_p, m_s, m_a;
            public FakeGameRecord(string platform, string scrapeAs, string path) { m_p = platform; m_s = scrapeAs; m_a = path; }
            public string Platform => m_p;
            public string ScrapeAs => m_s;
            public string ApplicationPath => m_a;
        }

        /// <summary>The options window's editing of a command line: our flags cut out and put back,
        /// everything else left as it was written; a game given back to the emulator's line when
        /// nothing of ours is left; and the window itself, built off screen on a selection that does
        /// not agree.</summary>
        private static void TheOptions()
        {
            Console.WriteLine();
            Console.WriteLine("  the options window: our flags on a game's command line");
            const string rom = @"C:\Users\mehdi\Downloads\KILLALLZOMBIES [PCSE00965] [USA] [NoNpDRM].zip";
            var optType = _asm.GetType("LbIntegrations.Vita3k.Vita3kOptions", throwOnError: true);
            object Opt(bool vhdx = false, string dir = null, bool noRam = false, int? margin = null, int? ram = null)
            {
                var o = Activator.CreateInstance(optType);
                optType.GetField("UseVhdx").SetValue(o, vhdx);
                optType.GetField("VhdxDir").SetValue(o, dir);
                optType.GetField("NoRamDisk").SetValue(o, noRam);
                optType.GetField("MarginMb").SetValue(o, margin);
                optType.GetField("Vita3kRamMb").SetValue(o, ram);
                return o;
            }
            string Strip(string line) => (string)Call("Vita3kCommandLines", "Strip", new object[] { line, rom });
            string With(string line, object o) => (string)Call("Vita3kCommandLines", "With", new object[] { line, o, rom });
            string Own(string own, string inherited, object o) => (string)Call("Vita3kCommandLines", "NewOwnLine", new object[] { own, inherited, o, rom });
            string Key(object o) => (string)optType.GetProperty("Key").GetValue(o);
            object From(string line) => optType.GetMethod("From", BindingFlags.Public | BindingFlags.Static).Invoke(null, new object[] { line, rom });
            void Is(string what, string got, string want)
            {
                if (!Check(what, got == want)) Console.WriteLine("        got  [" + got + "]\n        want [" + want + "]");
            }

            Is("a flag in the middle goes, the rest stays", Strip("-F --no-ramdisk --fullscreen"), "-F --fullscreen");
            Is("a flag opening the line goes with the space after it", Strip("--no-ramdisk -F"), "-F");
            Is("a quoted flag goes; odd spacing elsewhere is left as written",
               Strip("-F  \"--use-vhdx=E:\\Mes VHDX\"   --log-level 2"), "-F   --log-level 2");
            Is("a value in the space spelling goes with its flag, a quoted word of somebody else's stays",
               Strip("-F --ramdisk-margin 1024 \"C:\\a b\\x.txt\""), "-F \"C:\\a b\\x.txt\"");
            Is("--use-vhdx and its folder in the space spelling", Strip("-F --use-vhdx E:\\VitaVhdx --fullscreen"), "-F --fullscreen");
            Is("a line with nothing of ours is returned as it is", Strip("-F   --fullscreen "), "-F   --fullscreen ");
            Is("all of them at once", Strip("--vita3k-ram=3072 -F --use-vhdx --ramdisk-margin=256 --no-ramdisk"), "-F");

            Is("the new options go at the end, a folder with a space quoted",
               With("-F --fullscreen", Opt(vhdx: true, dir: @"E:\Mes VHDX", margin: 256)),
               "-F --fullscreen \"--use-vhdx=E:\\Mes VHDX\" --ramdisk-margin=256");
            Is("and they REPLACE the old ones", With("-F --no-ramdisk --vita3k-ram=1000", Opt(ram: 2000)), "-F --vita3k-ram=2000");

            Is("a game that inherits and gets nothing keeps inheriting", Own("", "-F", Opt()), "");
            Is("a game that inherits and gets a flag: the emulator's line plus the flag", Own("", "-F", Opt(noRam: true)), "-F --no-ramdisk");
            Is("a game left with nothing of ours and the emulator's line inherits again", Own("-F  --no-ramdisk", "-F", Opt()), "");
            Is("a game left with nothing of ours and a line of its own keeps its line", Own("-F --fullscreen --no-ramdisk", "-F", Opt()), "-F --fullscreen");
            Is("a game with its own line keeps it, the flags change", Own("-F --fullscreen --no-ramdisk", "-F", Opt(vhdx: true)), "-F --fullscreen --use-vhdx");

            // THE SAME READING AS A LAUNCH: what the window writes is what PrepareEmulatorForLaunch reads,
            // and none of it reaches Vita3K.
            var all = Opt(vhdx: true, dir: @"E:\Mes VHDX", noRam: true, margin: 128, ram: 4096);
            var written = With("-F --fullscreen", all);
            Is("read back, the same options", Key(From(written)), Key(all));
            var build = _asm.GetType("LbIntegrations.Vita3k.Vita3kPlugin").GetMethod("CommandLineFor", BindingFlags.NonPublic | BindingFlags.Static);
            Is("and Vita3K is given none of them", (string)build.Invoke(null, new object[] { written + " \"" + rom + "\"", "PCSE00965", rom }),
               "-F --fullscreen -r PCSE00965");

            // THE WINDOW, built and never shown: two groups, so a combo box, and a preview.
            var formType = _asm.GetType("LbIntegrations.Vita3k.Vita3kOptionsForm", throwOnError: true);
            var entryType = formType.GetNestedType("Entry", BindingFlags.NonPublic | BindingFlags.Public);
            var list = (System.Collections.IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(entryType));
            foreach (var (title, own) in new[] { ("Game A", ""), ("Game B", ""), ("Game C", "-F --use-vhdx") })
            {
                var e = Activator.CreateInstance(entryType);
                entryType.GetField("Title").SetValue(e, title);
                entryType.GetField("Rom").SetValue(e, rom);
                entryType.GetField("Own").SetValue(e, own);
                entryType.GetField("Inherited").SetValue(e, "-F");
                entryType.GetField("Options").SetValue(e, From(own.Length > 0 ? own : "-F"));
                list.Add(e);
            }
            using (var form = (System.Windows.Forms.Form)Activator.CreateInstance(formType, list))
            {
                var source = (System.Windows.Forms.ComboBox)formType.GetField("_source", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(form);
                var preview = (System.Windows.Forms.TextBox)formType.GetField("_preview", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(form);
                var vhdx = (System.Windows.Forms.RadioButton)formType.GetField("_useVhdx", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(form);
                var disk = (System.Windows.Forms.RadioButton)formType.GetField("_diskOnly", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(form);
                var ram = (System.Windows.Forms.RadioButton)formType.GetField("_ramDisk", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(form);
                Check("the window builds on three games in two groups, with a combo box of two", source != null && source.Items.Count == 2);
                Console.WriteLine("            " + string.Join(" | ", source.Items.Cast<object>()));
                Check("the first group names its games", source.Items[0].ToString().StartsWith("Game A <and 1 other>"));
                Check("it starts from the first group: inherited, nothing of ours", !vhdx.Checked && preview.Text.Contains("inherited"));
                source.SelectedIndex = 1;
                Check("choosing the other group loads its options", vhdx.Checked && preview.Text == "-F --use-vhdx");
                Check("three exclusive choices: VHDX alone is checked", vhdx.Checked && !disk.Checked && !ram.Checked);
                disk.Checked = true;
                Check("choosing disk only takes VHDX off the line", !vhdx.Checked && preview.Text == "-F --no-ramdisk");
                vhdx.Checked = true;
                Console.WriteLine("            preview: " + preview.Text);

                // LBIP_PROBE_SHOT=<png>: the window as drawn, off screen, for a look at its layout.
                var shot = Environment.GetEnvironmentVariable("LBIP_PROBE_SHOT");
                if (!string.IsNullOrEmpty(shot))
                {
                    form.StartPosition = System.Windows.Forms.FormStartPosition.Manual;
                    form.Location = new System.Drawing.Point(-4000, -4000);
                    form.Show();
                    System.Windows.Forms.Application.DoEvents();
                    using var bmp = new System.Drawing.Bitmap(form.Width, form.Height);
                    form.DrawToBitmap(bmp, new System.Drawing.Rectangle(0, 0, form.Width, form.Height));
                    bmp.Save(shot);
                    Console.WriteLine("            drawn to " + shot);
                    form.Hide();
                }
            }
        }

        /// <summary>A .pkg installed our way against the same .pkg installed by Vita3K itself - the oracle.
        /// The zRIF is given on the command line and NEVER kept anywhere: this repository ships no licence.
        /// --vita3k-pkg --pkg &lt;file.pkg&gt; --zrif &lt;zRIF&gt; --oracle &lt;a Vita3K fs folder it was installed into&gt;</summary>
        public static bool Pkg(Assembly pluginAssembly, string pkgPath, string zrif, string oracle)
        {
            Console.WriteLine();
            Console.WriteLine("-- Vita3K, a .pkg against Vita3K's own install of it " + new string('-', 11));
            _asm = pluginAssembly;
            _bad = 0;
            if (!File.Exists(pkgPath ?? "") || string.IsNullOrWhiteSpace(zrif) || !Directory.Exists(oracle ?? ""))
            { Console.WriteLine("  pass --pkg <file.pkg> --zrif <zRIF> --oracle <fs folder>"); return false; }
            var temp = Path.Combine(Path.GetTempPath(), "lbip-pkg-" + Guid.NewGuid().ToString("N"));
            try
            {
                var pkgType = _asm.GetType("LbIntegrations.Vita3k.VitaPkg", throwOnError: true);
                var open = new object[] { pkgPath, null };
                var pkg = pkgType.GetMethod("Open", BindingFlags.Public | BindingFlags.Static).Invoke(null, open);
                if (!Check("the .pkg opens", pkg != null, open[1] as string)) return false;
                var contentId = (string)pkgType.GetField("ContentId").GetValue(pkg);
                Console.WriteLine("            " + contentId + ", type 0x" + ((int)pkgType.GetField("ContentType").GetValue(pkg)).ToString("X")
                                  + ", " + pkgType.GetField("FileCount").GetValue(pkg) + " item(s)");
                var describe = new object[] { null };
                var content = pkgType.GetMethod("Describe").Invoke(pkg, describe);
                if (!Check("its param.sfo is read without any licence", content != null, describe[0] as string)) return false;
                var titleId = (string)Field(content, "TitleId");
                Console.WriteLine("            " + titleId + " [" + Field(content, "Category") + "] " + Field(content, "FullTitle") + " " + Field(content, "AppVer"));

                // THE SIZE A CONSOLE IS GIVEN FOR IT: the package, and - only while it installs - its
                // largest item, read from the item table alone.
                var contentType = _asm.GetType("LbIntegrations.Vita3k.Vita3kContent", throwOnError: true);
                var sizeWatch = System.Diagnostics.Stopwatch.StartNew();
                long lasting = (long)contentType.GetMethod("WorkingSizeBytes").Invoke(null, new object[] { pkgPath });
                long transient = (long)contentType.GetMethod("TransientBytes").Invoke(null, new object[] { pkgPath });
                Console.WriteLine("            sized in " + sizeWatch.ElapsedMilliseconds + " ms: " + lasting / 1024 + " KB lasting, "
                                  + transient / 1024 + " KB more while it installs (its largest item)");
                Check("its lasting size is the package's", lasting == new FileInfo(pkgPath).Length);
                Check("and its largest item is found, smaller than it", transient > 0 && transient < lasting);

                // THE LICENCE: the zRIF turned into the RIF Vita3K wrote.
                var toRif = new object[] { zrif, null };
                var rif = (byte[])_asm.GetType("LbIntegrations.Vita3k.VitaZrif", throwOnError: true).GetMethod("ToRif").Invoke(null, toRif);
                if (!Check("the zRIF gives a licence", rif != null && rif.Length == 512, toRif[1] as string)) return false;
                var oracleRif = Path.Combine(oracle, "ux0", "license", titleId, contentId + ".rif");
                Check("byte for byte the one Vita3K wrote (" + Path.GetFileName(oracleRif) + ")",
                      File.Exists(oracleRif) && File.ReadAllBytes(oracleRif).AsSpan().SequenceEqual(rif));

                // THE OUTER LAYER, then the PFS - into %TEMP%, the oracle only read.
                var stage = Path.Combine(temp, "stage");
                var extract = new object[] { stage, null, null };
                var watch = System.Diagnostics.Stopwatch.StartNew();
                if (!Check("the outer layer comes off", (bool)pkgType.GetMethod("Extract").Invoke(pkg, extract), extract[2] as string)) return false;
                Console.WriteLine("            unpacked in " + watch.ElapsedMilliseconds + " ms: " + Directory.GetFiles(stage, "*", SearchOption.AllDirectories).Length + " file(s)");
                Check("and what comes out is still PFS-encrypted (sce_pfs/ there)", Directory.Exists(Path.Combine(stage, "sce_pfs")));

                var licence = Path.Combine(temp, "licence.rif");
                File.WriteAllBytes(licence, rif);
                var app = Path.Combine(temp, "app");
                var native = _asm.GetType("LbIntegrations.Vita3k.Vita3kNative", throwOnError: true);
                var decrypt = native.GetMethods(BindingFlags.Public | BindingFlags.Static).First(m => m.Name == "DecryptApp" && m.GetParameters().Length == 5);
                var dargs = new object[] { stage, licence, app, null, null };
                watch.Restart();
                if (!Check("the PFS is decrypted with that licence", (bool)decrypt.Invoke(null, dargs), dargs[4] as string)) return false;
                Console.WriteLine("            decrypted in " + watch.ElapsedMilliseconds + " ms");

                // THE ORACLE: every file, by path and SHA-1.
                var theirs = Path.Combine(oracle, "ux0", "app", titleId);
                Dictionary<string, string> Tree(string dir) => Directory.GetFiles(dir, "*", SearchOption.AllDirectories)
                    .ToDictionary(f => Path.GetRelativePath(dir, f).Replace('\\', '/'), f => Convert.ToHexString(System.Security.Cryptography.SHA1.HashData(File.ReadAllBytes(f))), StringComparer.OrdinalIgnoreCase);
                var ours = Tree(app);
                var oracleTree = Tree(theirs);
                var missing = oracleTree.Keys.Except(ours.Keys, StringComparer.OrdinalIgnoreCase).ToList();
                var extra = ours.Keys.Except(oracleTree.Keys, StringComparer.OrdinalIgnoreCase).ToList();
                var differ = ours.Keys.Intersect(oracleTree.Keys, StringComparer.OrdinalIgnoreCase).Where(k => ours[k] != oracleTree[k]).ToList();
                Console.WriteLine("            ours " + ours.Count + " file(s), the oracle's " + oracleTree.Count);
                foreach (var m in missing.Take(10)) Console.WriteLine("              missing: " + m);
                foreach (var m in extra.Take(10)) Console.WriteLine("              extra:   " + m);
                foreach (var m in differ.Take(10)) Console.WriteLine("              differs: " + m);
                Check("the same files as Vita3K's own install", missing.Count == 0 && extra.Count == 0);
                Check("byte for byte", differ.Count == 0);

                // THE LAUNCH'S OWN PATH: Vita3kContent.Install, the licence from a zrif table.
                Console.WriteLine();
                Console.WriteLine("  through the launch's install, the licence in a zrif table");
                var licences = _asm.GetType("LbIntegrations.Vita3k.Vita3kLicences", throwOnError: true);
                var installDirField = licences.GetField("InstallDir", BindingFlags.NonPublic | BindingFlags.Static);
                var emu = Path.Combine(temp, "emulator");
                Directory.CreateDirectory(emu);
                installDirField.SetValue(null, emu);
                try
                {
                    var fs = Path.Combine(temp, "fs");
                    Directory.CreateDirectory(fs);
                    var install = _asm.GetType("LbIntegrations.Vita3k.Vita3kContent", throwOnError: true).GetMethods(BindingFlags.Public | BindingFlags.Static)
                                      .First(m => m.Name == "Install" && m.GetParameters().Length == 3);
                    var none = new object[] { pkgPath, fs, null };
                    Check("with no licence anywhere: refused, and it says where to put one",
                          install.Invoke(null, none) == null && (none[2] as string ?? "").Contains("my-licences.tsv"), none[2] as string);
                    var template = Path.Combine(emu, "zrif", "my-licences.tsv");
                    Check("and the zrif folder was made, with its template - no licence in it",
                          File.Exists(template) && !File.ReadAllText(template).Contains(zrif));
                    Check("nothing left of the refused install", !Directory.EnumerateDirectories(temp, "lbip-staging-*", SearchOption.AllDirectories).Any());

                    File.Delete(template);
                    File.WriteAllLines(Path.Combine(emu, "zrif", "mine.tsv"), new[] { "Title ID\tRegion\tName\tContent ID\tzRIF", titleId + "\tEU\tprobe\t" + contentId + "\t" + zrif });
                    var ok = new object[] { pkgPath, fs, null };
                    watch.Restart();
                    var installed = install.Invoke(null, ok);
                    if (!Check("with the licence in a table: installed", installed != null, ok[2] as string)) return false;
                    Console.WriteLine("            in " + watch.ElapsedMilliseconds + " ms");
                    var mine = Tree(Path.Combine(fs, "ux0", "app", titleId));
                    Check("the app is Vita3K's, file for file and byte for byte",
                          mine.Count == oracleTree.Count && mine.All(kv => oracleTree.TryGetValue(kv.Key, out var h) && h == kv.Value));
                    var placed = Path.Combine(fs, "ux0", "license", titleId, contentId + ".rif");
                    Check("the licence is where Vita3K put its own, the same bytes",
                          File.Exists(placed) && File.ReadAllBytes(placed).AsSpan().SequenceEqual(File.ReadAllBytes(oracleRif)));
                    var hashed = (System.Collections.IDictionary)Field(installed, "Hashed");
                    Check("every file was hashed as it was written", hashed.Count >= mine.Count);
                    Check("and no staging copy is left", !Directory.EnumerateDirectories(temp, "lbip-staging-*", SearchOption.AllDirectories).Any());

                    // BESIDE THE PKG: <name>.bin, then a licence of another name in its folder. On a copy
                    // of the .pkg under %TEMP%, the zrif table out of the way.
                    File.Delete(Path.Combine(emu, "zrif", "mine.tsv"));
                    var near = Path.Combine(temp, "near");
                    Directory.CreateDirectory(near);
                    var copy = Path.Combine(near, "a game.pkg");
                    File.Copy(pkgPath, copy);
                    var find = licences.GetMethod("Find", BindingFlags.Public | BindingFlags.Static);
                    File.WriteAllBytes(Path.Combine(near, "a game.bin"), rif);
                    var byName = new object[] { copy, contentId, null, null };
                    Check("a licence beside it as <name>.bin is found", find.Invoke(null, byName) is byte[] b1 && b1.AsSpan().SequenceEqual(rif), byName[3] as string);
                    Console.WriteLine("            " + byName[2]);
                    File.Delete(Path.Combine(near, "a game.bin"));
                    File.WriteAllBytes(Path.Combine(near, "whatever I called it.rif"), rif);
                    File.WriteAllBytes(Path.Combine(near, "another licence.rif"), new byte[512]);
                    File.WriteAllBytes(Path.Combine(near, "a game.bin"), new byte[1024 * 1024]);   // same name, not licence-sized
                    var byContent = new object[] { copy, contentId, null, null };
                    Check("and one under another name in its folder, by the content it names", find.Invoke(null, byContent) is byte[] b2 && b2.AsSpan().SequenceEqual(rif), byContent[3] as string);
                    Console.WriteLine("            " + byContent[2]);
                    Check("a 1 MB <name>.bin beside it is never read (its size says it is no licence)", (byContent[2] as string ?? "").StartsWith("whatever I called it.rif"));

                    // THE IMPORT WIZARD'S LIST: a .pkg game named from its param.sfo - and taken out
                    // when its licence is nowhere.
                    var run = _asm.GetType("LbIntegrations.Vita3k.Vita3kImportCleanup", throwOnError: true).GetMethod("Run", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                    var alone = Path.Combine(temp, "alone");
                    Directory.CreateDirectory(alone);
                    var lonely = Path.Combine(alone, "some pkg.pkg");
                    File.Copy(pkgPath, lonely);
                    var withoutList = new FakeGameList();
                    withoutList.Games.Add(new FakeListRecord("some pkg", lonely));
                    run.Invoke(null, new object[] { null, withoutList });
                    Check("in the import list, a .pkg game with no licence anywhere is taken out", withoutList.Games.Count == 0);
                    File.WriteAllBytes(Path.Combine(alone, "some pkg.rif"), rif);
                    var withList = new FakeGameList();
                    withList.Games.Add(new FakeListRecord("some pkg", lonely));
                    run.Invoke(null, new object[] { null, withList });
                    Check("with its licence beside it, kept - and named from its param.sfo",
                          withList.Games.Count == 1 && withList.Games[0].Title == (string)Call("Vita3kImportCleanup", "CleanTitle", new object[] { (string)Field(content, "FullTitle") }), withList.Games.Count == 1 ? withList.Games[0].Title : null);
                }
                finally { installDirField.SetValue(null, null); }

                Console.WriteLine();
                Console.WriteLine(_bad == 0 ? "  OK - the .pkg installs as Vita3K installs it" : "  " + _bad + " FAILURE(S) - see above");
                return _bad == 0;
            }
            catch (Exception ex) { Console.WriteLine("  EXCEPTION: " + (ex.InnerException ?? ex)); return false; }
            finally { Scrub(temp); }
        }

        // ── what Vita3K itself is given ──────────────────────────────────────

        private static void TheEmulatorsShare(object layout)
        {
            Console.WriteLine();
            Console.WriteLine("  the memory kept for Vita3K itself");
            int Reserve(string id) => (int)Call("Vita3kWorkspace", "EmulatorReserveMb", new object[] { layout, id });

            Check("a game never measured, on an install that measured nothing, gets the default (2048 MB)", Reserve(TitleId) == 2048);
            Call("Vita3kWorkspace", "RememberEmulatorPeak", new object[] { layout, TitleId, 2000 });
            Check("a measured game gets its peak plus 15% (2300 MB)", Reserve(TitleId) == 2300);
            Call("Vita3kWorkspace", "RememberEmulatorPeak", new object[] { layout, "PCSE99999", 3000 });
            Check("each game keeps its own", Reserve(TitleId) == 2300 && Reserve("PCSE99999") == 3450);
            Check("a game never measured gets the largest peak seen (3450 MB)", Reserve("PCSG00001") == 3450);
            Call("Vita3kWorkspace", "RememberEmulatorPeak", new object[] { layout, TitleId, 2200 });
            Check("the latest session of a game is the one kept (2530 MB)", Reserve(TitleId) == 2530);
            Call("Vita3kWorkspace", "RememberEmulatorPeak", new object[] { layout, TitleId, 967 });
            Check("never under 1536 MB: LittleBigPlanet's 967 MB peak keeps 1536, not 1112", Reserve(TitleId) == 1536);
        }

        /// <summary>A REAL game with its real update and DLC, found beside it, installed and decrypted
        /// onto a forged console under %TEMP% - no RAM disk (a forged install has no helper), nothing of
        /// the real install touched. What it proves: the update decrypts under the GAME's licence (it
        /// carries none), the DLC under its own, and each lands where Vita3K looks.</summary>
        public static bool ExtrasReal(Assembly pluginAssembly, string romPath)
        {
            Console.WriteLine();
            Console.WriteLine("-- Vita3K, a real game with its update and DLC  [writes to %TEMP%] " + new string('-', 1));
            _asm = pluginAssembly;
            _bad = 0;
            var root = Path.Combine(Path.GetTempPath(), "lbip-vita3k-extras-" + Guid.NewGuid().ToString("N"));
            bool keep = Environment.GetEnvironmentVariable("LBIP_PROBE_KEEP") == "1";
            try
            {
                if (!File.Exists(romPath ?? "")) { Console.WriteLine("  pass --rom <game archive>"); return false; }
                const string variable = "LBIP_VITA3K_NATIVE";
                if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable(variable)))
                {
                    var repo = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(pluginAssembly.Location), "..", "..", "..", "..", ".."));
                    var built = Path.Combine(repo, "build", "vita3k", "vita3k-install.dll");
                    if (File.Exists(built)) Environment.SetEnvironmentVariable(variable, built);
                }

                var install = Path.Combine(root, "Emulators", "Nixx-Vita3K");
                var portable = Path.Combine(install, "portable");
                Directory.CreateDirectory(portable);
                File.WriteAllText(Path.Combine(install, "Vita3K.exe"), "not really an executable");
                var layout = Resolve(Path.Combine(install, "Vita3K.exe"));
                TheFirmware(layout, portable);

                string lastStep = null;
                var steps = new List<string>();
                var stepWatch = System.Diagnostics.Stopwatch.StartNew();
                Action<string, double?> report = (step, fraction) =>
                {
                    if (step == null || step == lastStep) return;
                    if (lastStep != null) steps.Add(string.Format("  {0,7} ms  {1}", stepWatch.ElapsedMilliseconds, lastStep));
                    lastStep = step;
                    stepWatch.Restart();
                };
                var t = _asm.GetType("LbIntegrations.Vita3k.Vita3kWorkspace", throwOnError: true);
                var prepare = t.GetMethod("Prepare", BindingFlags.Public | BindingFlags.Static, null,
                    new[] { layout.GetType(), typeof(string), typeof(string).MakeByRefType(), typeof(Action<string, double?>) }, null);
                var a = new object[] { layout, romPath, null, report };
                var watch = System.Diagnostics.Stopwatch.StartNew();
                var titleId = prepare.Invoke(null, a) as string;
                if (lastStep != null) steps.Add(string.Format("  {0,7} ms  {1}", stepWatch.ElapsedMilliseconds, lastStep));
                Console.WriteLine();
                Console.WriteLine("  what the progress window said:");
                foreach (var line in steps) Console.WriteLine(line);
                Console.WriteLine(string.Format("  {0,7} ms  in all", watch.ElapsedMilliseconds));
                if (!Check("a console is prepared", titleId != null, a[2] as string)) return false;

                var work = Path.Combine(portable, "work");
                string Head(string file)
                {
                    var h = new byte[4];
                    using (var f = File.OpenRead(file)) f.Read(h, 0, 4);
                    return BitConverter.ToString(h);
                }
                var appDir = Path.Combine(work, "ux0", "app", titleId);
                var eboot = Path.Combine(appDir, "eboot.bin");
                Check("the update is merged into the app, and ux0/patch gone", !Directory.Exists(Path.Combine(work, "ux0", "patch", titleId)));
                Console.WriteLine("  app eboot " + Head(eboot));
                Check("the app's eboot.bin is a SELF (the update's, decrypted under the game's licence)", Head(eboot) == "53-43-45-00");
                var sfo = File.ReadAllBytes(Path.Combine(appDir, "sce_sys", "param.sfo"));
                var ver = System.Text.Encoding.ASCII.GetString(sfo);
                Console.WriteLine("  app param.sfo carries APP_VER " + (ver.Contains("01.22") ? "01.22" : ver.Contains("01.00") ? "01.00" : "?"));
                Check("and its param.sfo is the update's: Vita3K will show 01.22", ver.Contains("01.22"));
                Check("with no sce_pfs left in it", !Directory.Exists(Path.Combine(appDir, "sce_pfs")));
                var addcont = Path.Combine(work, "ux0", "addcont", titleId);
                var dlcs = Directory.Exists(addcont) ? Directory.GetDirectories(addcont) : new string[0];
                foreach (var d in dlcs) Console.WriteLine("  DLC folder " + Path.GetFileName(d) + " - " + Directory.GetFiles(d, "*", SearchOption.AllDirectories).Length + " file(s)");
                Check("the DLC is installed in ux0/addcont", dlcs.Length > 0);
                Check("with its own licence in ux0/license", dlcs.All(d => File.Exists(Path.Combine(work, "ux0", "license", titleId,
                      "UP9000-" + titleId + "_00-" + Path.GetFileName(d) + ".rif"))));
                Check("and no sce_pfs left in it", dlcs.All(d => !Directory.Exists(Path.Combine(d, "sce_pfs"))));
                var reference = File.ReadAllText(Path.Combine(portable, "work.reference"));
                Check("all of it in the reference", reference.Contains("ux0/app/" + titleId + "/eboot.bin") && reference.Contains("ux0/addcont/" + titleId + "/"));

                // LBIP_PROBE_KEEP=1 keeps the tree, for a comparison with an install made by Vita3K itself.
                if (keep) Console.WriteLine("  kept      " + work + "  (LBIP_PROBE_KEEP=1 - delete it after)");
                else Call("Vita3kWorkspace", "Teardown", new object[] { layout });
                Console.WriteLine();
                Console.WriteLine(_bad == 0 ? "  OK - the game, its update and its DLC, decrypted and in place" : "  " + _bad + " FAILURE(S)");
                return _bad == 0;
            }
            catch (Exception ex) { Console.WriteLine("  EXCEPTION: " + (ex.InnerException ?? ex)); return false; }
            finally { if (!keep) Scrub(root); }
        }

        // ── updates and DLC ──────────────────────────────────────────────────

        private static void UpdatesAndDlc(object layout, string portable, string root)
        {
            Console.WriteLine();
            Console.WriteLine("  updates and DLC");

            // A library laid out the four ways, with a decoy for each rule.
            var roms = Path.Combine(root, "roms");
            Directory.CreateDirectory(roms);
            var game = ForgeContent(roms, "A Forged Game [PCSE00965].vpk", TitleId, "gd", "UP0000-PCSE00965_00-FORGEDGAME000000", "01.00", "A Forged Game");
            ForgeContent(roms, "A Forged Game [PCSE00965] [PATCH] [v1.10].vpk", TitleId, "gp", "UP0000-PCSE00965_00-FORGEDGAME000000", "01.10", "A Forged Game");
            ForgeContent(Path.Combine(roms, "UPDATE"), "update PCSE00965 1.20.vpk", TitleId, "gp", "UP0000-PCSE00965_00-FORGEDGAME000000", "01.20", "A Forged Game");
            ForgeContent(Path.Combine(roms, "PCSE00965"), "costume.vpk", TitleId, "ac", "UP0000-PCSE00965_00-DLCCOSTUME000001", null, "A Costume");
            ForgeContent(Path.Combine(roms, "PCSE00965"), "costume again.vpk", TitleId, "ac", "UP0000-PCSE00965_00-DLCCOSTUME000001", null, "A Costume");
            ForgeContent(Path.Combine(roms, "Forged Game, The - Extras"), "level pack.vpk", TitleId, "ac", "UP0000-PCSE00965_00-DLCLEVELPACK0002", null, "A Level Pack");
            ForgeContent(roms, "not really PCSE00965.vpk", "PCSE99999", "gp", "UP0000-PCSE99999_00-SOMETHINGELSE000", "09.99", "Another Game");
            ForgeContent(Path.Combine(roms, "Unrelated"), "unrelated.vpk", TitleId, "ac", "UP0000-PCSE00965_00-NEVERFOUND000003", null, "Never Found");

            var described = Call("Vita3kContent", "Describe", new object[] { game, null });
            var candidates = (System.Collections.IList)Call("Vita3kExtras", "Candidates", new object[] { game, described, "A Forged Game" });
            var names = new List<string>();
            foreach (var c in candidates) names.Add(Path.GetFileName((string)c.GetType().GetField("Item1").GetValue(c)));
            Console.WriteLine("            candidates: " + string.Join(", ", names));
            Check("the update beside the game, by its name, is a candidate", names.Contains("A Forged Game [PCSE00965] [PATCH] [v1.10].vpk"));
            Check("the one in UPDATE is", names.Contains("update PCSE00965 1.20.vpk"));
            Check("everything in the folder named the title id is", names.Contains("costume.vpk") && names.Contains("costume again.vpk"));
            Check("everything in a folder named the game is (\"Forged Game, The - Extras\" has another key: not)", !names.Contains("level pack.vpk"));
            Check("a zip naming the id in a folder with no reason to be looked at is not", !names.Contains("unrelated.vpk"));
            Check("the game itself is never a candidate", !names.Contains("A Forged Game [PCSE00965].vpk"));

            // The folder named the game: its key has to be the game's.
            var named = Path.Combine(roms, "A Forged Game - Extras");
            Directory.Move(Path.Combine(roms, "Forged Game, The - Extras"), Path.Combine(roms, "Forged Game"));
            candidates = (System.Collections.IList)Call("Vita3kExtras", "Candidates", new object[] { game, described, "A Forged Game" });
            names.Clear();
            foreach (var c in candidates) names.Add(Path.GetFileName((string)c.GetType().GetField("Item1").GetValue(c)));
            Check("a folder named the game by its key (\"Forged Game\" = FORGEDGAME) is looked into", names.Contains("level pack.vpk"));

            var extras = Call("Vita3kExtras", "For", new object[] { game, described, "A Forged Game", null, null });
            var update = extras.GetType().GetField("Update").GetValue(extras);
            var addons = (System.Collections.IList)extras.GetType().GetField("Addons").GetValue(extras);
            string AppVer(object e) => e == null ? null : (string)Field(Field(e, "Content"), "AppVer");
            Check("of the two updates, the highest is kept (01.20)", AppVer(update) == "01.20");
            Check("two DLC kept - one per CONTENT_ID, the decoy of another game set aside", addons.Count == 2);

            ImportIndexAndChoice(layout, root, game, described);
            ImportCleanup(root);
            MultiContent(root);
            TheConfig(layout);

            // A launch installs them, before the reference.
            var args = new object[] { layout, game, null };
            Check("the game launches", Call("Vita3kWorkspace", "Prepare", args) as string == TitleId, args[2] as string);
            var work = Path.Combine(portable, "work");
            Check("the update is merged into the app, as Vita3K's own install does - its eboot.bin is 1.20's",
                  File.ReadAllText(Path.Combine(work, "ux0", "app", TitleId, "eboot.bin")).Contains("01.20"));
            Check("and ux0/patch is gone - Vita3K reads app0: from ux0/app alone", !Directory.Exists(Path.Combine(work, "ux0", "patch", TitleId)));
            Check("the app's param.sfo is the update's now (what Vita3K shows as the version)",
                  File.ReadAllBytes(Path.Combine(work, "ux0", "app", TitleId, "sce_sys", "param.sfo")).AsSpan().IndexOf(System.Text.Encoding.ASCII.GetBytes("01.20")) >= 0);
            Check("the first DLC in ux0/addcont", Directory.Exists(Path.Combine(work, "ux0", "addcont", TitleId, "DLCCOSTUME000001")));
            Check("the second too", Directory.Exists(Path.Combine(work, "ux0", "addcont", TitleId, "DLCLEVELPACK0002")));
            var reference = File.ReadAllText(Path.Combine(portable, "work.reference"));
            Check("and all of it is in the reference - none of it will come out as a save",
                  reference.Contains("ux0/app/" + TitleId + "/eboot.bin") && !reference.Contains("ux0/patch/")
                  && reference.Contains("ux0/addcont/" + TitleId + "/DLCLEVELPACK0002/"));
            var marker = File.ReadAllText(Path.Combine(portable, "work.title")).Split('\t');
            Check("the marker remembers which were installed", marker.Length >= 6 && marker[5].Length > 0);

            // Same game, same extras: reused. A new DLC: rebuilt, with it.
            var before = File.GetLastWriteTimeUtc(Path.Combine(portable, "work.reference"));
            args = new object[] { layout, game, null };
            Call("Vita3kWorkspace", "Prepare", args);
            Check("relaunched with the same extras: nothing rebuilt", File.GetLastWriteTimeUtc(Path.Combine(portable, "work.reference")) == before);
            ForgeContent(Path.Combine(roms, "DLC"), "bonus PCSE00965.vpk", TitleId, "ac", "UP0000-PCSE00965_00-DLCBONUS00000003", null, "A Bonus");
            args = new object[] { layout, game, null };
            Check("a DLC added since: it launches", Call("Vita3kWorkspace", "Prepare", args) as string == TitleId, args[2] as string);
            Check("and the console was rebuilt with it", Directory.Exists(Path.Combine(work, "ux0", "addcont", TitleId, "DLCBONUS00000003")));

            SavesAcrossExtras(layout, portable, roms, game);
        }

        /// <summary>The import wizard's game list put right: titles from the param.sfo, cleaned; updates,
        /// DLC and what is not a Vita game out. On a list of the wizard's shape. OPENS the progress
        /// window for a moment.</summary>
        private static void ImportCleanup(string root)
        {
            Console.WriteLine();
            Console.WriteLine("  the import wizard's list, put right  [opens a window]");
            string Clean(string t) => (string)Call("Vita3kImportCleanup", "CleanTitle", new object[] { t });
            Check("LittleBigPlanet™ PlayStation®Vita -> LittleBigPlanet PlayStation Vita (a sign is a space)",
                  Clean("LittleBigPlanet™ PlayStation®Vita") == "LittleBigPlanet PlayStation Vita", Clean("LittleBigPlanet™ PlayStation®Vita"));
            Check("Game™: The Sequel -> Game: The Sequel", Clean("Game™: The Sequel") == "Game: The Sequel");
            Check("a title on two lines is one", Clean("Two\nLines") == "Two Lines");
            Check("a plain title is left as it is", Clean("#KILLALLZOMBIES") == "#KILLALLZOMBIES");

            var dir = Path.Combine(root, "import");
            var game = ForgeContent(dir, "a forged game [PCSE00965].vpk", TitleId, "gd", "UP0000-PCSE00965_00-FORGEDGAME000000", "01.00", "A Forged Game™ Deluxe");
            var patch = ForgeContent(dir, "a forged game [PCSE00965] [PATCH].vpk", TitleId, "gp", "UP0000-PCSE00965_00-FORGEDGAME000000", "01.40", "A Forged Game");
            var dlc = ForgeContent(dir, "a forged game [PCSE00965] [DLC].vpk", TitleId, "ac", "UP0000-PCSE00965_00-DLCCLEANUP000001", null, "A Costume");
            var notVita = Path.Combine(dir, "not a vita game.zip");
            using (var z = System.IO.Compression.ZipFile.Open(notVita, System.IO.Compression.ZipArchiveMode.Create))
                using (var w = new StreamWriter(z.CreateEntry("readme.txt").Open())) w.Write("hello");
            var text = Path.Combine(dir, "notes.txt");
            File.WriteAllText(text, "not an archive");

            var list = new FakeGameList();
            foreach (var f in new[] { game, patch, dlc, notVita, text })
                list.Games.Add(new FakeListRecord(Path.GetFileNameWithoutExtension(f), f));
            Call("Vita3kImportCleanup", "Run", new object[] { null, list });
            Console.WriteLine("            left: " + string.Join(" | ", list.Games.Select(r => r.Title + " (" + Path.GetFileName(r.ApplicationPath) + ")")));
            Check("one line left: the game", list.Games.Count == 1 && list.Games[0].ApplicationPath == game);
            Check("titled from its param.sfo, cleaned", list.Games.Count == 1 && list.Games[0].Title == "A Forged Game Deluxe");
        }

        /// <summary>A zip of SEVERAL contents, stored the wrong way round: the update first, a second and
        /// smaller game, then the game with a DLC nested inside its own folder - and no folder named
        /// app/, patch/ or addcont/. The biggest game is the one, the update and DLC of the zip are
        /// found with it, and each content gets what is its own, in the order that works.</summary>
        private static void MultiContent(string root)
        {
            Console.WriteLine();
            Console.WriteLine("  a zip holding several contents");
            var dir = Path.Combine(root, "multi");
            Directory.CreateDirectory(dir);
            var zip = Path.Combine(dir, "a bundle.zip");
            const string DlcId = "UP0000-PCSE00965_00-MULTICOSTUME0001";
            using (var z = ZipFile.Open(zip, ZipArchiveMode.Create))
            {
                void Put(string key, byte[] bytes) { using var s = z.CreateEntry(key).Open(); s.Write(bytes, 0, bytes.Length); }
                void Text(string key, string text) => Put(key, System.Text.Encoding.ASCII.GetBytes(text));
                Put("the update/sce_sys/param.sfo", Psf(("CATEGORY", "gp"), ("TITLE_ID", TitleId), ("CONTENT_ID", "UP0000-PCSE00965_00-FORGEDGAME000000"), ("TITLE", "A Forged Game"), ("APP_VER", "01.40")));
                Text("the update/eboot.bin", "patched to 01.40");
                Put("a demo/sce_sys/param.sfo", Psf(("CATEGORY", "gd"), ("TITLE_ID", "PCSE11111"), ("CONTENT_ID", "UP0000-PCSE11111_00-FORGEDDEMO000000"), ("TITLE", "A Small Demo")));
                Text("a demo/eboot.bin", "small");
                Put("stuff/game/sce_sys/param.sfo", Psf(("CATEGORY", "gd"), ("TITLE_ID", TitleId), ("CONTENT_ID", "UP0000-PCSE00965_00-FORGEDGAME000000"), ("TITLE", "A Forged Game"), ("APP_VER", "01.00")));
                Text("stuff/game/eboot.bin", new string('g', 4000));
                Put("stuff/game/dlc/costume/sce_sys/param.sfo", Psf(("CATEGORY", "ac"), ("TITLE_ID", TitleId), ("CONTENT_ID", DlcId), ("TITLE", "A Nested Costume")));
                Text("stuff/game/dlc/costume/costume.bin", new string('c', 3000));
            }

            var allArgs = new object[] { zip, null };
            var all = ((System.Collections.IList)Call("Vita3kContent", "DescribeAll", allArgs)).Cast<object>().ToList();
            Console.WriteLine("            contents: " + string.Join(" | ", all.Select(c => Field(c, "Root") + " " + c)));
            Check("four contents read, whatever their folders are called", all.Count == 4);
            var game = Call("Vita3kContent", "Describe", new object[] { zip, null });
            Check("the game is the biggest one, not the first stored", (string)Field(game, "TitleId") == TitleId && (string)Field(game, "Root") == "stuff/game/");
            Check("its size leaves out the DLC nested in its folder", (long)Field(game, "Bytes") < 5000 && (long)Field(game, "Bytes") > 4000, Field(game, "Bytes").ToString());

            var found = Call("Vita3kExtras", "Evaluate", new object[] { zip, game, "A Forged Game", null });
            var updates = ((System.Collections.IList)Field(found, "Updates")).Cast<object>().ToList();
            var addons = ((System.Collections.IList)Field(found, "Addons")).Cast<object>().ToList();
            Check("the zip's own update is found with it", updates.Count == 1 && (string)Field(Field(updates[0], "Content"), "AppVer") == "01.40"
                  && ((string)Field(updates[0], "FoundBy")).Contains("own archive"));
            Check("and its nested DLC", addons.Count == 1 && (string)Field(Field(addons[0], "Content"), "ContentId") == DlcId);
            var updateRef = (string)updates[0].GetType().GetProperty("Ref").GetValue(updates[0]);
            Check("the update is named by its archive and its place in it", updateRef == zip + "|the update/", updateRef);

            // CHOSEN BY THAT NAME in the game's options - and nothing else.
            var choiceType = _asm.GetType("LbIntegrations.Vita3k.Vita3kExtrasChoice", throwOnError: true);
            var choice = Activator.CreateInstance(choiceType);
            choiceType.GetField("UpdatePath").SetValue(choice, updateRef);
            var chosen = Call("Vita3kExtras", "Choose", new object[] { found, choice });
            Check("the game's options name it by that, and find it", Field(chosen, "Update") != null);

            // INSTALLED: the game alone, then what was chosen - the update first stored, installed second.
            var fs = Path.Combine(dir, "fs");
            Directory.CreateDirectory(fs);
            var installArgs = new object[] { zip, game, fs, null, null };
            var installed = Call("Vita3kContent", "Install", installArgs);
            Check("the game installs", installed != null, installArgs[3] as string);
            var app = Path.Combine(fs, "ux0", "app", TitleId);
            Check("with its own files only - not the nested DLC's, not the demo's, not the update's",
                  File.Exists(Path.Combine(app, "eboot.bin")) && !Directory.Exists(Path.Combine(app, "dlc"))
                  && !Directory.Exists(Path.Combine(fs, "ux0", "app", "PCSE11111")) && File.ReadAllText(Path.Combine(app, "eboot.bin")).StartsWith("gggg"));
            Call("Vita3kWorkspace", "InstallExtras", new object[] { Call("Vita3kExtras", "Choose", new object[] { found, null }), fs, installed, null });
            Check("then the update, over it", File.ReadAllText(Path.Combine(app, "eboot.bin")) == "patched to 01.40");
            Check("and the DLC where Vita3K puts it", File.Exists(Path.Combine(fs, "ux0", "addcont", TitleId, "MULTICOSTUME0001", "costume.bin"))
                  && !File.Exists(Path.Combine(fs, "ux0", "addcont", TitleId, "MULTICOSTUME0001", "eboot.bin")));

            // THE IMPORT: a bundle holding a game is a game - kept, not taken for its update.
            var list = new FakeGameList();
            list.Games.Add(new FakeListRecord("a bundle", zip));
            Call("Vita3kImportCleanup", "Run", new object[] { null, list });
            Check("at import, the bundle stays - as its game", list.Games.Count == 1 && list.Games[0].Title == "A Forged Game", list.Games.Count == 1 ? list.Games[0].Title : null);

            // THE US ENGLISH TITLE, as the library names it: TITLE_01 before the region's own TITLE.
            var jp = Path.Combine(dir, "a japanese game.zip");
            using (var z = ZipFile.Open(jp, ZipArchiveMode.Create))
            {
                var sfo = Psf(("CATEGORY", "gd"), ("TITLE_ID", "PCSG00001"), ("CONTENT_ID", "JP0000-PCSG00001_00-FORGEDJAPAN00000"),
                              ("TITLE", "ゲーム"), ("STITLE", "ゲ"), ("TITLE_01", "A Japanese Game"), ("STITLE_01", "Japanese Game"));
                using var s = z.CreateEntry("sce_sys/param.sfo").Open(); s.Write(sfo, 0, sfo.Length);
            }
            var jpContent = Call("Vita3kContent", "Describe", new object[] { jp, null });
            Check("a title in several languages: the US English one", (string)Field(jpContent, "FullTitle") == "A Japanese Game"
                  && (string)Field(jpContent, "Title") == "Japanese Game", (string)Field(jpContent, "FullTitle"));
        }

        /// <summary>What an install puts in config.yml - full screen on, the update check off - only where
        /// the file does not say yet; and the system settings read back for the notification.</summary>
        private static void TheConfig(object layout)
        {
            Console.WriteLine();
            Console.WriteLine("  the emulator's own settings, at install");
            var portable = Path.Combine((string)Field(layout, "InstallDir"), "portable");
            var config = Path.Combine(portable, "config.yml");
            var before = File.Exists(config) ? File.ReadAllText(config) : null;
            try
            {
                if (File.Exists(config)) File.Delete(config);
                Call("Vita3kConfig", "ApplyInstallDefaults", new object[] { layout });
                var fresh = File.ReadAllText(config);
                Check("a fresh install: full screen on, the update check off",
                      fresh.Contains("boot-apps-full-screen: true") && fresh.Contains("check-for-updates: false"), fresh.Trim());
                var windows = Call("Vita3kConfig", "FromWindows", new object[0]).ToString();
                Console.WriteLine("            from this Windows: " + windows);
                Check("and the system settings are Windows' - read back as they were written",
                      fresh.Contains("sys-lang: ") && fresh.Contains("sys-button: ")
                      && (string)Call("Vita3kConfig", "SystemSettings", new object[] { layout }) == windows);

                // THE CULTURES, one by one: the language from the display language, the formats from the
                // regional settings, circle for Japanese only.
                string From(string ui, string regional) => Call("Vita3kConfig", "FromCultures", new object[]
                    { new System.Globalization.CultureInfo(ui), new System.Globalization.CultureInfo(regional) }).ToString();
                Check("fr-FR: French, DD/MM/YYYY, 24-hour, cross", From("fr-FR", "fr-FR") == "language French, date DD/MM/YYYY, time 24-hour, enter button cross", From("fr-FR", "fr-FR"));
                Check("ja-JP: Japanese, YYYY/MM/DD, 24-hour, circle", From("ja-JP", "ja-JP") == "language Japanese, date YYYY/MM/DD, time 24-hour, enter button circle", From("ja-JP", "ja-JP"));
                Check("en-US: English (US), MM/DD/YYYY, 12-hour", From("en-US", "en-US") == "language English (US), date MM/DD/YYYY, time 12-hour, enter button cross", From("en-US", "en-US"));
                Check("en-GB: English (UK), DD/MM/YYYY, 24-hour", From("en-GB", "en-GB") == "language English (UK), date DD/MM/YYYY, time 24-hour, enter button cross", From("en-GB", "en-GB"));
                Check("an English Windows with French regional settings: English (US), and the French formats",
                      From("en-US", "fr-FR") == "language English (US), date DD/MM/YYYY, time 24-hour, enter button cross", From("en-US", "fr-FR"));
                Check("pt-BR and zh-TW: their own variant", From("pt-BR", "pt-BR").StartsWith("language Portuguese (Brazil)") && From("zh-TW", "zh-TW").StartsWith("language Chinese (traditional)"));

                File.WriteAllText(config, "show-welcome: false\ncheck-for-updates: true\nsys-lang: 2\nsys-date-format: 1\nsys-time-format: 1\n");
                Call("Vita3kConfig", "ApplyInstallDefaults", new object[] { layout });
                var updated = File.ReadAllText(config);
                Check("an update keeps what the user set - the update check they turned on stays on",
                      updated.Contains("check-for-updates: true") && !updated.Contains("check-for-updates: false"));
                Check("and adds only the key the file did not hold", updated.Contains("boot-apps-full-screen: true"));
                var said = (string)Call("Vita3kConfig", "SystemSettings", new object[] { layout });
                Console.WriteLine("            " + said);
                Check("the settings are read back from it", said == "language French, date DD/MM/YYYY, time 24-hour, enter button cross");

                // WHAT THE WINDOW SAVES: the four keys replaced where they are, everything else kept.
                var settingsType = _asm.GetType("LbIntegrations.Vita3k.VitaSystemSettings", throwOnError: true);
                var chosen = Activator.CreateInstance(settingsType);
                settingsType.GetField("Language").SetValue(chosen, 0);
                settingsType.GetField("EnterButton").SetValue(chosen, 0);
                settingsType.GetField("DateFormat").SetValue(chosen, 0);
                settingsType.GetField("TimeFormat").SetValue(chosen, 1);
                Check("the window's choice is saved", (bool)Call("Vita3kConfig", "Write", new object[] { layout, chosen }));
                var saved = File.ReadAllText(config);
                Check("over the keys that were there, once each", saved.Split('\n').Count(l => l.StartsWith("sys-lang:")) == 1 && saved.Contains("sys-lang: 0") && saved.Contains("sys-button: 0"));
                Check("and the rest of the file kept", saved.Contains("show-welcome: false") && saved.Contains("check-for-updates: true") && saved.Contains("boot-apps-full-screen: true"));
                Check("read back: Japanese, circle", (string)Call("Vita3kConfig", "SystemSettings", new object[] { layout }) == "language Japanese, date YYYY/MM/DD, time 24-hour, enter button circle");
            }
            finally
            {
                if (before != null) File.WriteAllText(config, before);
                else if (File.Exists(config)) File.Delete(config);
            }
        }

        /// <summary>The shape of the wizard's game list: Games, a collection the grid shows.</summary>
        public sealed class FakeGameList
        {
            public System.Collections.ObjectModel.ObservableCollection<FakeListRecord> Games { get; } = new System.Collections.ObjectModel.ObservableCollection<FakeListRecord>();
        }

        /// <summary>The shape of a record: Title with a setter, ApplicationPath without.</summary>
        public sealed class FakeListRecord
        {
            public FakeListRecord(string title, string path) { Title = title; ApplicationPath = path; }
            public string Title { get; set; }
            public string ApplicationPath { get; }
        }

        /// <summary>The import's index (an extra the four rules never look at, found through it) and the
        /// game's choice of update and DLC (none, another, a DLC left out). Leaves the forged library as
        /// it found it: the launch checks after this expect 01.20 and two DLC.</summary>
        private static void ImportIndexAndChoice(object layout, string root, string game, object described)
        {
            Console.WriteLine();
            Console.WriteLine("  the import's index, and the game's choice");
            var install = (string)Field(layout, "InstallDir");
            string AppVer(object e) => e == null ? null : (string)Field(Field(e, "Content"), "AppVer");

            // AN UPDATE NOWHERE THE RULES LOOK: in another folder, under a name without the title id.
            var elsewhere = Path.Combine(root, "elsewhere");
            var hidden = ForgeContent(elsewhere, "the big patch.vpk", TitleId, "gp", "UP0000-PCSE00965_00-FORGEDGAME000000", "01.30", "A Forged Game");
            var hiddenContent = Call("Vita3kContent", "Describe", new object[] { hidden, null });
            var extras = Call("Vita3kExtras", "For", new object[] { game, described, "A Forged Game", install, null });
            Check("without the index, an update elsewhere is not found", AppVer(extras.GetType().GetField("Update").GetValue(extras)) == "01.20");

            var row = Call("Vita3kExtrasIndex", "From", new object[] { game, hidden, hiddenContent });
            var rows = (System.Collections.IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(row.GetType()));
            rows.Add(row);
            Call("Vita3kExtrasIndex", "Record", new object[] { install, new[] { game }, rows });
            var index = Path.Combine(install, "lbip-vita-extras.tsv");
            Check("the import writes the index in the emulator's folder", File.Exists(index) && File.ReadAllText(index).Contains("the big patch.vpk"));
            extras = Call("Vita3kExtras", "For", new object[] { game, described, "A Forged Game", install, null });
            Check("with it, that update is found - and, the highest, kept (01.30)", AppVer(extras.GetType().GetField("Update").GetValue(extras)) == "01.30");

            // Recorded again for the same game: replaced, not added.
            Call("Vita3kExtrasIndex", "Record", new object[] { install, new[] { game }, rows });
            Check("the same import twice: one line, not two",
                  File.ReadAllLines(index).Count(l => l.Contains("the big patch.vpk")) == 1);
            Check("the game is kept by its name and size, not its path",
                  File.ReadAllLines(index).Any(l => l.StartsWith(Path.GetFileName(game) + "\t" + new FileInfo(game).Length + "\t")));
            Check("an extra outside the game's folder keeps its full path", File.ReadAllText(index).Contains(hidden));

            // BESIDE THE GAME, RELATIVE - and a library moved whole keeps its index.
            var lib = Path.Combine(root, "library");
            var libGame = ForgeContent(lib, "moved game [PCSE00965].vpk", TitleId, "gd", "UP0000-PCSE00965_00-FORGEDGAME000000", "01.00", "A Forged Game");
            var libDlc = ForgeContent(Path.Combine(lib, "odd folder"), "extra thing.vpk", TitleId, "ac", "UP0000-PCSE00965_00-DLCMOVED00000001", null, "A Moved Costume");
            var libRows = (System.Collections.IList)Activator.CreateInstance(rows.GetType());
            libRows.Add(Call("Vita3kExtrasIndex", "From", new object[] { libGame, libDlc, Call("Vita3kContent", "Describe", new object[] { libDlc, null }) }));
            Call("Vita3kExtrasIndex", "Record", new object[] { install, new[] { libGame }, libRows });
            Check("an extra under the game's folder is kept relative to it",
                  File.ReadAllLines(index).Any(l => l.Contains("\todd folder\\extra thing.vpk\t")));
            var moved = Path.Combine(root, "library moved");
            Directory.Move(lib, moved);
            var proposed = (List<string>)Call("Vita3kExtrasIndex", "For", new object[] { install, TitleId, Path.Combine(moved, "moved game [PCSE00965].vpk") });
            Check("the library moved whole: the extra is found where it is now",
                  proposed.Contains(Path.Combine(moved, "odd folder", "extra thing.vpk")));

            // THE SAME GAME SCANNED AGAIN FROM ITS NEW PLACE: its lines replaced, none added.
            var movedGame = Path.Combine(moved, "moved game [PCSE00965].vpk");
            var movedRows = (System.Collections.IList)Activator.CreateInstance(rows.GetType());
            movedRows.Add(Call("Vita3kExtrasIndex", "From", new object[] { movedGame, Path.Combine(moved, "odd folder", "extra thing.vpk"),
                                                                            Call("Vita3kContent", "Describe", new object[] { Path.Combine(moved, "odd folder", "extra thing.vpk"), null }) }));
            movedRows.Add(movedRows[0]);   // and found twice in the one batch
            Call("Vita3kExtrasIndex", "Record", new object[] { install, new[] { movedGame }, movedRows });
            Check("scanned again from elsewhere, and twice in one batch: still one line",
                  File.ReadAllLines(index).Count(l => l.Contains("extra thing.vpk")) == 1);
            Scrub(moved);

            // THE CHOICE: none, another update, a DLC left out.
            var found = Call("Vita3kExtras", "Evaluate", new object[] { game, described, "A Forged Game", install });
            var choiceType = _asm.GetType("LbIntegrations.Vita3k.Vita3kExtrasChoice", throwOnError: true);
            object Choose(Action<object> set)
            {
                var c = Activator.CreateInstance(choiceType);
                set(c);
                return Call("Vita3kExtras", "Choose", new object[] { found, c });
            }
            var none = Choose(c => choiceType.GetField("NoUpdate").SetValue(c, true));
            Check("choosing no update: none installed", none.GetType().GetField("Update").GetValue(none) == null);
            var older = Choose(c => choiceType.GetField("UpdatePath").SetValue(c, Path.Combine(Path.GetDirectoryName(game), "A Forged Game [PCSE00965] [PATCH] [v1.10].vpk")));
            Check("choosing 1.10: 1.10, not the highest", AppVer(older.GetType().GetField("Update").GetValue(older)) == "01.10");
            var fewer = Choose(c => ((HashSet<string>)choiceType.GetField("LeftOut").GetValue(c)).Add("UP0000-PCSE00965_00-DLCCOSTUME000001"));
            Check("leaving a DLC out: the other one only",
                  ((System.Collections.IList)fewer.GetType().GetField("Addons").GetValue(fewer)).Count == 1);
            var gone = Choose(c => choiceType.GetField("UpdatePath").SetValue(c, Path.Combine(root, "no such update.vpk")));
            Check("an update chosen and gone since: the highest instead", AppVer(gone.GetType().GetField("Update").GetValue(gone)) == "01.30");

            // Saved, read back; the automatic choice removes the line.
            var saved = Activator.CreateInstance(choiceType);
            choiceType.GetField("NoUpdate").SetValue(saved, true);
            ((HashSet<string>)choiceType.GetField("LeftOut").GetValue(saved)).Add("UP0000-PCSE00965_00-DLCCOSTUME000001");
            Call("Vita3kExtrasChoice", "Save", new object[] { install, "probe-game-id", saved });
            var back = Call("Vita3kExtrasChoice", "Load", new object[] { install, "probe-game-id" });
            Check("a choice is saved and read back", back != null && (bool)choiceType.GetField("NoUpdate").GetValue(back)
                  && ((HashSet<string>)choiceType.GetField("LeftOut").GetValue(back)).Contains("UP0000-PCSE00965_00-DLCCOSTUME000001"));
            Call("Vita3kExtrasChoice", "Save", new object[] { install, "probe-game-id", Activator.CreateInstance(choiceType) });
            Check("choosing the automatic values again removes it", Call("Vita3kExtrasChoice", "Load", new object[] { install, "probe-game-id" }) == null);

            // A line whose file is gone is not proposed - and the library is as it was.
            File.Delete(hidden);
            extras = Call("Vita3kExtras", "For", new object[] { game, described, "A Forged Game", install, null });
            Check("its file gone, the indexed update is no longer proposed", AppVer(extras.GetType().GetField("Update").GetValue(extras)) == "01.20");

            ExtrasTab(install, game);
        }

        /// <summary>The options window's Updates & DLC tab, built off screen on the forged library: what
        /// it lists, what it starts from, the choice it gives. LBIP_PROBE_SHOT_EXTRAS=&lt;png&gt; draws it.</summary>
        private static void ExtrasTab(string install, string game)
        {
            Console.WriteLine();
            Console.WriteLine("  the options window: Updates & DLC");
            var formType = _asm.GetType("LbIntegrations.Vita3k.Vita3kOptionsForm", throwOnError: true);
            var entryType = formType.GetNestedType("Entry", BindingFlags.NonPublic | BindingFlags.Public);
            var optType = _asm.GetType("LbIntegrations.Vita3k.Vita3kOptions", throwOnError: true);
            object Entry(string title)
            {
                var e = Activator.CreateInstance(entryType);
                foreach (var (f, v) in new[] { ("Title", title), ("Rom", game), ("RomFull", game), ("Own", ""), ("Inherited", "-F"),
                                               ("GameId", "probe-tab-game"), ("InstallDir", install) })
                    entryType.GetField(f).SetValue(e, v);
                entryType.GetField("Options").SetValue(e, optType.GetMethod("From", BindingFlags.Public | BindingFlags.Static).Invoke(null, new object[] { "-F", game }));
                return e;
            }
            System.Collections.IList Entries(int count)
            {
                var list = (System.Collections.IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(entryType));
                for (int i = 0; i < count; i++) list.Add(Entry("A Forged Game " + i));
                return list;
            }
            T F<T>(object form, string name) => (T)formType.GetField(name, BindingFlags.NonPublic | BindingFlags.Instance).GetValue(form);

            using (var several = (System.Windows.Forms.Form)Activator.CreateInstance(formType, Entries(2)))
            {
                var tab = F<System.Windows.Forms.TabPage>(several, "_extrasTab");
                Check("for several games, the tab says to pick one", tab.Controls.Count == 1 && tab.Controls[0].Text.Contains("single game"));
            }

            using var form = (System.Windows.Forms.Form)Activator.CreateInstance(formType, Entries(1));
            formType.GetMethod("LoadExtras", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(form, null);
            var updates = F<System.Collections.IList>(form, "_updates");
            var dlc = F<System.Collections.IList>(form, "_dlc");
            var auto = F<System.Windows.Forms.RadioButton>(form, "_updateAuto");
            var none = F<System.Windows.Forms.RadioButton>(form, "_updateNone");
            Console.WriteLine("            " + auto?.Text + " | " + updates.Count + " update(s) | " + dlc.Count + " DLC");
            Check("it lists the two updates and the two DLC found", updates.Count == 2 && dlc.Count == 2);
            Check("with no choice made: Automatic, every DLC ticked", auto != null && auto.Checked
                  && dlc.Cast<object>().All(d => ((System.Windows.Forms.CheckBox)d.GetType().GetField("Item1").GetValue(d)).Checked));

            none.Checked = true;
            ((System.Windows.Forms.CheckBox)dlc[0].GetType().GetField("Item1").GetValue(dlc[0])).Checked = false;
            var choice = formType.GetMethod("ExtrasChoice", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(form, null);
            var ct = choice.GetType();
            Check("None and a DLC unticked give: no update, that DLC left out", (bool)ct.GetField("NoUpdate").GetValue(choice)
                  && ((HashSet<string>)ct.GetField("LeftOut").GetValue(choice)).Count == 1);

            var shot = Environment.GetEnvironmentVariable("LBIP_PROBE_SHOT_EXTRAS");
            if (!string.IsNullOrEmpty(shot))
            {
                auto.Checked = true;
                var tabs = form.Controls.OfType<System.Windows.Forms.TabControl>().First();
                form.StartPosition = System.Windows.Forms.FormStartPosition.Manual;
                form.Location = new System.Drawing.Point(-4000, -4000);
                form.Show();
                tabs.SelectedTab = F<System.Windows.Forms.TabPage>(form, "_extrasTab");
                System.Windows.Forms.Application.DoEvents();
                using var bmp = new System.Drawing.Bitmap(form.Width, form.Height);
                form.DrawToBitmap(bmp, new System.Drawing.Rectangle(0, 0, form.Width, form.Height));
                bmp.Save(shot);
                Console.WriteLine("            drawn to " + shot);
                form.Hide();
            }
        }

        /// <summary>A save stays valid whatever the update and DLC; only a lower version or a missing
        /// DLC asks first, and "no" keeps the save aside rather than losing it.</summary>
        private static void SavesAcrossExtras(object layout, string portable, string roms, string game)
        {
            Console.WriteLine();
            Console.WriteLine("  a save across updates and DLC");
            var work = Path.Combine(portable, "work");
            var saveDir = Path.Combine(portable, "saves", TitleId);
            var save = Path.Combine(saveDir, "state.vitasav");
            var ws = _asm.GetType("LbIntegrations.Vita3k.Vita3kWorkspace", throwOnError: true);
            var askField = ws.GetField("Ask", BindingFlags.NonPublic | BindingFlags.Static);
            var original = askField.GetValue(null);
            string asked = null;
            bool answer = true;
            askField.SetValue(null, new Func<string, string, bool>((title, text) => { asked = text; return answer; }));
            try
            {
                // A session at 1.20 with three DLC: progress, and a file dropped into the update's folder.
                Write(work, "ux0/user/00/savedata/" + TitleId + "/progress.bin", "made at 1.20");
                Write(work, "ux0/app/" + TitleId + "/written-by-the-session.bin", "should not survive a change of update");
                Check("the session is captured", (bool)Call("Vita3kWorkspace", "Capture", new object[] { layout, TitleId }));
                string inside = null;
                using (var z = ZipFile.OpenRead(save))
                {
                    var e = z.GetEntry("-lbip-context.txt");
                    if (e != null) using (var r = new StreamReader(e.Open())) inside = r.ReadToEnd();
                }
                Console.WriteLine("            the save says: " + (inside ?? "(nothing)").Replace("\n", " | "));
                Check("the save knows what it was made with (app_ver=01.20, 3 DLC)",
                      inside != null && inside.Contains("app_ver=01.20") && inside.Split('\n').Count(l => l.StartsWith("dlc=")) == 3);

                // The 1.20 update goes away: the game will run at 1.10. Answered NO.
                var gone = Path.Combine(roms, "UPDATE", "update PCSE00965 1.20.vpk");
                var parked = Path.Combine(roms, "..", "parked-1.20.vpk");
                File.Move(gone, parked);
                answer = false; asked = null;
                var args = new object[] { layout, game, null };
                Check("launching at 1.10 still works", Call("Vita3kWorkspace", "Prepare", args) as string == TitleId, args[2] as string);
                Check("but it asked first, naming 1.20", asked != null && asked.Contains("01.20"));
                Check("answered no: the save is not restored", !File.Exists(Path.Combine(work, "ux0", "user", "00", "savedata", TitleId, "progress.bin")));
                Check("and it is kept aside as state.v01.20.vitasav", File.Exists(Path.Combine(saveDir, "state.v01.20.vitasav")) && !File.Exists(save));

                // The same, answered YES.
                File.Move(Path.Combine(saveDir, "state.v01.20.vitasav"), save);
                Call("Vita3kWorkspace", "Teardown", new object[] { layout });
                answer = true; asked = null;
                args = new object[] { layout, game, null };
                Call("Vita3kWorkspace", "Prepare", args);
                Check("answered yes: asked, and the save is restored", asked != null
                      && File.Exists(Path.Combine(work, "ux0", "user", "00", "savedata", TitleId, "progress.bin")));
                Check("except what it held in the game's folder, which changed", !File.Exists(Path.Combine(work, "ux0", "app", TitleId, "written-by-the-session.bin")));

                // Going UP asks nothing: a save made at 1.10, the game back at 1.20.
                Check("a save made at 1.10 is captured", (bool)Call("Vita3kWorkspace", "Capture", new object[] { layout, TitleId }));
                File.Move(parked, gone);
                Call("Vita3kWorkspace", "Teardown", new object[] { layout });
                asked = null;
                args = new object[] { layout, game, null };
                Call("Vita3kWorkspace", "Prepare", args);
                Check("going up to 1.20 asks nothing", asked == null);
                Check("and restores it", File.Exists(Path.Combine(work, "ux0", "user", "00", "savedata", TitleId, "progress.bin")));

                // A DLC that is gone asks, even at the same version.
                var bonus = Path.Combine(roms, "DLC", "bonus PCSE00965.vpk");
                Check("a save made with the bonus DLC is captured", (bool)Call("Vita3kWorkspace", "Capture", new object[] { layout, TitleId }));
                File.Delete(bonus);
                Call("Vita3kWorkspace", "Teardown", new object[] { layout });
                asked = null; answer = true;
                args = new object[] { layout, game, null };
                Call("Vita3kWorkspace", "Prepare", args);
                Check("a missing DLC asks, naming it", asked != null && asked.Contains("DLCBONUS00000003"));
            }
            finally { askField.SetValue(null, original); }
        }

        private static string ForgeContent(string dir, string name, string titleId, string category, string contentId,
                                           string appVer, string title)
        {
            Directory.CreateDirectory(dir);
            var staging = Path.Combine(dir, ".staging-" + Guid.NewGuid().ToString("N"));
            Write(staging, "eboot.bin", category + " " + titleId + " " + (appVer ?? contentId));
            var pairs = new List<(string, string)> { ("CATEGORY", category), ("TITLE_ID", titleId), ("CONTENT_ID", contentId), ("TITLE", title), ("STITLE", title) };
            if (appVer != null) pairs.Add(("APP_VER", appVer));
            Directory.CreateDirectory(Path.Combine(staging, "sce_sys"));
            File.WriteAllBytes(Path.Combine(staging, "sce_sys", "param.sfo"), Psf(pairs.ToArray()));
            var file = Path.Combine(dir, name);
            ZipFile.CreateFromDirectory(staging, file);
            Directory.Delete(staging, recursive: true);
            return file;
        }

        // ── a session that stopped without ending ────────────────────────────

        private static void NeverEnded(object layout, string portable, string root, string vpk)
        {
            Console.WriteLine();
            Console.WriteLine("  a session that never ended");

            var work = Path.Combine(portable, "work");
            var saves = Path.Combine(portable, "saves");

            // 1. The host died during PCSE99999's session: progress in the tree, never captured. The
            //    next launch is ANOTHER game - the one moment the tree is cleared.
            Write(work, "ux0/user/00/savedata/PCSE99999/data.bin", "progress nobody saved");
            var otherSave = Path.Combine(saves, "PCSE99999", "state.vitasav");
            Check("(the other game has no save yet)", !File.Exists(otherSave));
            var args = new object[] { layout, vpk, null };
            Check("launching another game still works", Call("Vita3kWorkspace", "Prepare", args) as string == TitleId, args[2] as string);
            Check("and the unsaved session was captured BEFORE its tree was cleared", File.Exists(otherSave));

            // 2. The same, found by the start-up check on the disk fallback: saved, and work\ KEPT.
            var save = Path.Combine(saves, TitleId, "state.vitasav");
            var savedAt = File.GetLastWriteTimeUtc(save);
            System.Threading.Thread.Sleep(50);
            Write(work, "ux0/user/00/savedata/" + TitleId + "/data.bin", "progress after a crash");
            Call("Vita3kWorkspace", "CleanUpAtStart", new object[] { layout });
            Check("the start-up check saves a tree newer than its save", File.GetLastWriteTimeUtc(save) > savedAt);
            Check("and keeps work - only another game clears it", Directory.Exists(Path.Combine(work, "ux0", "app", TitleId)));
            Check("with its marker", File.Exists(Path.Combine(portable, "work.title")));

            // 3. The machine restarted: the marker names a RAM disk that no longer exists, and the
            //    junction points into it.
            Call("Vita3kWorkspace", "Teardown", new object[] { layout });
            var vanished = Path.Combine(root, "a-drive-that-is-gone", "fs");
            Directory.CreateDirectory(vanished);
            var link = Path.Combine(portable, "fs");
            var psi = new System.Diagnostics.ProcessStartInfo("cmd.exe", "/c mklink /J \"" + link + "\" \"" + vanished + "\"")
            { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true };
            using (var p = System.Diagnostics.Process.Start(psi)) p.WaitForExit(30000);
            Directory.Delete(Path.GetDirectoryName(vanished), recursive: true);
            File.WriteAllText(Path.Combine(portable, "work.title"), TitleId + "\t0\t0\t0\t" + vanished);
            bool linkListed() => Directory.EnumerateFileSystemEntries(portable).Any(e => string.Equals(e, link, StringComparison.OrdinalIgnoreCase));
            Check("(a junction into the gone drive is in place)", linkListed());
            Call("Vita3kWorkspace", "CleanUpAtStart", new object[] { layout });
            Check("the start-up check forgets the session of a drive that is gone", !File.Exists(Path.Combine(portable, "work.title")));
            Check("and removes the junction into nothing", !linkListed());
            Check("and the save is untouched", File.Exists(save));

            // 4. Leftovers: old ones go, fresh ones - an operation in progress - stay.
            var oldStaging = Directory.CreateDirectory(Path.Combine(portable, "lbip-staging-old"));
            var newStaging = Directory.CreateDirectory(Path.Combine(portable, "lbip-staging-new"));
            var oldState = Directory.CreateDirectory(Path.Combine(saves, TitleId, "state.vitasav.abc.state"));
            var oldPart = Path.Combine(portable, "work.reference.abc.part");
            File.WriteAllText(oldPart, "half a manifest");
            var twoHoursAgo = DateTime.UtcNow.AddHours(-2);
            Directory.SetLastWriteTimeUtc(oldStaging.FullName, twoHoursAgo);
            Directory.SetLastWriteTimeUtc(oldState.FullName, twoHoursAgo);
            File.SetLastWriteTimeUtc(oldPart, twoHoursAgo);
            Call("Vita3kWorkspace", "CleanUpAtStart", new object[] { layout });
            Check("an old staging copy is removed", !Directory.Exists(oldStaging.FullName));
            Check("a fresh one is left alone", Directory.Exists(newStaging.FullName));
            Check("a capture that never finished is removed", !Directory.Exists(oldState.FullName));
            Check("so is half a manifest", !File.Exists(oldPart));
            Check("and the save itself is not a leftover", File.Exists(save));
            Directory.Delete(newStaging.FullName);
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
