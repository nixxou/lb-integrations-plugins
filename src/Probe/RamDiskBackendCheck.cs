// The RAM disk through each driver and each option, against a real install (helper 1.6):
//
//   --ramdisk-backends --lb <LaunchBox root> [--images <folder with base.vhdx, child.vhdx, test.iso, raw.img>]
//
// For AIM and for ImDisk (whichever are installed): a letter mount, removable,
// in virtual memory and in AWE - each written to, read back, found by IsRamDisk, unmounted, and checked gone,
// with the driver's own device list compared before and after (no device left behind). Then, with --images,
// AttachImage / DetachImage on an ISO, a raw image, a VHDX read-only, a differencing VHDX, and a VHDX under a
// write overlay deleted at the detach - the base checked untouched afterwards.
//
// MOUNTS REAL DRIVES, 64 MB each, for a few seconds. The options file it uses is a temporary one: the user's
// ramdisk.ini is put back at the end, whatever happens.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using LbIntegrations.RamDisk;

namespace LbIntegrations.Probe
{
    internal static class RamDiskBackendCheck
    {
        private const int SizeMb = 64;
        private static int _fail;

        public static bool Run(string launchBoxRoot, string images)
        {
            Console.WriteLine();
            Console.WriteLine("-- RAM disk backends (ImDisk and AIM)  [MOUNTS REAL DRIVES] --------");
            if (string.IsNullOrWhiteSpace(launchBoxRoot) || !Directory.Exists(launchBoxRoot)) { Console.WriteLine("  pass --lb <LaunchBox root>"); return false; }
            RamDiskLog.Use(m => Console.WriteLine("    [log] " + m), (m, ex) => Console.WriteLine("    [log] " + m + (ex != null ? " - " + ex.Message : "")));
            RamDiskHost.UseRoot(launchBoxRoot);
            Console.WriteLine("  helper " + RamDrive.HelperVersion + ", task " + (RamDrive.InstalledTaskName() ?? "none")
                              + ", AIM " + (RamDrive.IsAimInstalled() ? "yes (" + RamDrive.AimFolder + ")" : "no") + ", ImDisk " + (RamDrive.IsImDiskInstalled() ? "yes" : "no"));
            if (RamDrive.HelperVersion == null || RamDrive.HelperVersion < RamDrive.BackendProtocol || RamDrive.InstalledTaskName() == null)
            { Console.WriteLine("  the helper 1.6 and its task are needed"); return false; }

            var ini = RamDiskOptions.FilePath;
            string saved = File.Exists(ini) ? File.ReadAllText(ini) : null;
            try
            {
                // A driver gone between two launches (Mehdi, 02/10): the saved wish stays, what is used follows the machine.
                Console.WriteLine();
                Console.WriteLine("  options against what is installed (pure)");
                var wish = new RamDiskOptions { Backend = "aim", Vhdx = "aim" };
                var e1 = wish.Effective(false, true, out var n1);
                Check("AIM chosen, AIM gone: ImDisk, VHDX by Windows", e1.Backend == "imdisk" && e1.Vhdx == "windows", n1);
                var e2 = new RamDiskOptions { Backend = "imdisk" }.Effective(true, false, out var n2);
                Check("ImDisk chosen, ImDisk gone: AIM", e2.Backend == "aim", n2);
                var e3 = new RamDiskOptions { Backend = "auto" }.Effective(false, false, out _);
                Check("nothing installed: no RAM disk", e3.Backend == null, null);
                var e4 = wish.Effective(true, true, out var n4);
                Check("AIM chosen and there: all of it kept", e4.Backend == "aim" && e4.Vhdx == "aim" && n4.Length == 0, n4);
                var backends = new List<string>();
                if (RamDrive.IsAimInstalled()) backends.Add("aim");
                if (RamDrive.IsImDiskInstalled()) backends.Add("imdisk");
                foreach (var b in backends)
                {
                    Case(b, folder: false, removable: true, awe: false);
                    Case(b, folder: false, removable: false, awe: true);
                }
                Options("auto", false, true, false);
                Check("auto picks " + (RamDrive.IsImDiskInstalled() ? "imdisk" : "aim"), RamDrive.ActiveBackend() == (RamDrive.IsImDiskInstalled() ? "imdisk" : "aim"), RamDrive.ActiveBackend());

                if (!string.IsNullOrEmpty(images)) Images(images);
            }
            finally
            {
                try { if (saved != null) File.WriteAllText(ini, saved); else if (File.Exists(ini)) File.Delete(ini); } catch { }
            }
            Console.WriteLine();
            Console.WriteLine(_fail == 0 ? "  all passed" : "  " + _fail + " FAILED");
            return _fail == 0;
        }

        private static void Options(string backend, bool folder, bool removable, bool awe, string vhdx = "aim")
            => new RamDiskOptions { Vhdx = vhdx, Backend = backend, Removable = removable, Awe = awe, AutoMemory = false }.Save();

        private static void Case(string backend, bool folder, bool removable, bool awe)
        {
            Console.WriteLine();
            Console.WriteLine("  " + backend + ": " + (folder ? "folder" : "letter") + (removable ? ", removable" : "") + (awe ? ", AWE" : ", vm"));
            Options(backend, folder, removable, awe);
            var devicesBefore = Devices();
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var root = RamDrive.MountFor("probe-" + backend, SizeMb);
            if (!Check("mounted", root != null, root + " in " + sw.ElapsedMilliseconds + " ms")) return;
            try
            {
                bool inFolder = false;     // the plugins always mount on a letter (Mehdi, 02/10)
                Check(inFolder ? "in a folder, no letter" : "on a letter", inFolder ? root.Length > 3 : root.Length == 3, root);
                Check("IsRamDisk", RamDrive.IsRamDisk(root), null);
                Check("MountRootOf a file in it", string.Equals(RamDrive.MountRootOf(Path.Combine(root, "x", "y.txt")).TrimEnd('\\'), root.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase),
                      RamDrive.MountRootOf(Path.Combine(root, "x", "y.txt")));
                var file = Path.Combine(root, "probe.txt");
                File.WriteAllText(file, new string('x', 100000));
                Check("written and read back", File.ReadAllText(file).Length == 100000, null);
                var attrs = File.GetAttributes(file);
                bool expect = false;     // the plugins never compress (Mehdi, 02/10)
                Check(expect ? "compressed" : "not compressed", attrs.HasFlag(FileAttributes.Compressed) == expect, attrs.ToString());
                if (!folder)
                {
                    var type = new DriveInfo(root).DriveType;
                    Check(removable ? "removable" : "fixed", (type == DriveType.Removable) == removable, type.ToString());
                }
            }
            finally
            {
                sw.Restart();
                bool gone = RamDrive.UnmountFor("probe-" + backend);
                Check("unmounted", gone && !RamDrive.IsMountedAt(root), sw.ElapsedMilliseconds + " ms");
                
                // A removed ImDisk device can linger a few seconds while a scanner lets go of it - measured 02/10:
                // \Device\ImDisk3 there just after the dismount, gone on its own a moment later. Judged after 10 s.
                var left = Devices().Except(devicesBefore).ToList();
                for (int i = 0; i < 20 && left.Count > 0; i++) { System.Threading.Thread.Sleep(500); left = Devices().Except(devicesBefore).ToList(); }
                if (left.Count > 0 && backend == "imdisk" && !removable)
                    // KNOWN, measured 30/09: an indexer (Everything) holds every FIXED NTFS volume, the lock is refused,
                    // and an ImDisk device - not Plug and Play - stays "removed" until a restart. Why removable is the
                    // default; AIM, a PnP disk, does not have it.
                    Console.WriteLine("    note ImDisk left " + string.Join(", ", left) + " behind - the known fixed-drive case, why removable is the default");
                else Check("no device left behind", left.Count == 0, string.Join(", ", left));
            }
        }

        private static void Images(string dir)
        {
            Options("auto", false, true, false);
            Attach("ISO", Path.Combine(dir, "test.iso"), true, null, "iso.txt", "from-iso");
            Attach("raw image, read-only", Path.Combine(dir, "raw.img"), true, null, "raw.txt", "from-raw");
            Attach("VHDX, read-only, in a folder", Path.Combine(dir, "base.vhdx"), true, null, "base.txt", "from-base", folder: Path.Combine(Path.GetTempPath(), "lbip-img"));
            Attach("differencing VHDX", Path.Combine(dir, "child.vhdx"), true, null, "base.txt", "from-base");
            var overlay = Path.Combine(dir, "probe-overlay.diff");
            Attach("VHDX under a write overlay, deleted after", Path.Combine(dir, "base.vhdx"), false, overlay, "base.txt", "from-base", write: true);
            Check("the overlay is gone", !File.Exists(overlay), null);
            Attach("the base after the overlay session", Path.Combine(dir, "base.vhdx"), true, null, "base.txt", "from-base", absent: "session.txt");
        }

        private static void Attach(string name, string image, bool readOnly, string overlay, string file, string expect,
                                   string folder = null, bool write = false, string absent = null)
        {
            Console.WriteLine();
            Console.WriteLine("  image: " + name);
            if (!File.Exists(image)) { Console.WriteLine("    (no " + image + " - skipped)"); return; }
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var root = RamDrive.AttachImage(image, readOnly, out var error, overlay, overlay != null, folder);
            if (!Check("attached", root != null, root != null ? root + " in " + sw.ElapsedMilliseconds + " ms" : error)) return;
            try
            {
                string read = null;
                try { read = File.ReadAllText(Path.Combine(root, file)).Trim(); } catch (Exception ex) { read = ex.Message; }
                Check("reads " + file, read == expect, read);
                if (write)
                {
                    try { File.WriteAllText(Path.Combine(root, "session.txt"), "session"); Check("writable", true, null); }
                    catch (Exception ex) { Check("writable", false, ex.Message); }
                }
                if (absent != null) Check(absent + " is not there", !File.Exists(Path.Combine(root, absent)), null);
            }
            finally
            {
                Check("detached", RamDrive.DetachImage(root, out var derr) && !RamDrive.IsMountedAt(root), derr);
            }
        }

        /// <summary>The disk devices Windows has right now - an AIM disk is one, an ImDisk drive another.</summary>
        private static HashSet<string> Devices()
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                if (File.Exists(RamDrive.ImDiskExe))
                {
                    var i = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(RamDrive.ImDiskExe, "-l") { UseShellExecute = false, RedirectStandardOutput = true, CreateNoWindow = true });
                    foreach (var line in i.StandardOutput.ReadToEnd().Split('\n').Select(l => l.Trim()).Where(l => l.StartsWith(@"\Device\"))) set.Add(line);
                    i.WaitForExit();
                }
            }
            catch { }
            try
            {
                var p = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("cmd.exe", "/c wmic diskdrive get PNPDeviceID")
                { UseShellExecute = false, RedirectStandardOutput = true, CreateNoWindow = true });
                foreach (var line in p.StandardOutput.ReadToEnd().Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0 && l != "PNPDeviceID")) set.Add(line);
                p.WaitForExit();
            }
            catch { }
            return set;
        }

        private static bool Check(string what, bool ok, string detail)
        {
            Console.WriteLine("    " + (ok ? "ok  " : "FAIL") + " " + what + (string.IsNullOrEmpty(detail) ? "" : "  (" + detail + ")"));
            if (!ok) _fail++;
            return ok;
        }
    }
}
