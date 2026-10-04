// --xiso-attach / --xiso-detach: an Xbox disc's XISO served where it is by the RAM disk helper (1.10, view=xiso) - the one file of
// an exFAT volume on an AIM disk - then opened and read by THIS process, which is not elevated, as xemu would be. MOUNTS A
// DISK: run only with Mehdi's go-ahead. Leaves it attached for an emulator to be tried on it; --xiso-detach takes it back.

using System;
using System.IO;
using System.Linq;
using LbIntegrations.RamDisk;

namespace LbIntegrations.Probe
{
    internal static class XisoAttachCheck
    {
        public static bool Attach(string lb, string rom, string baseHex)
        {
            RamDiskHost.UseRoot(lb);
            Console.WriteLine("  helper " + (RamDrive.HelperVersion?.ToString() ?? "absent") + ", task " + (RamDrive.InstalledTaskName() ?? "none"));
            if (!RamDrive.CanAttachXiso(out var why, rom)) { Console.WriteLine("  cannot: " + why); return false; }
            var watch = System.Diagnostics.Stopwatch.StartNew();
            var file = RamDrive.AttachXiso(rom, out var root, out var error);
            Console.WriteLine("  attach: " + (file ?? "FAILED - " + error) + " in " + watch.ElapsedMilliseconds + " ms (root " + root + ")");
            if (file == null) return false;
            bool elevated = new System.Security.Principal.WindowsPrincipal(System.Security.Principal.WindowsIdentity.GetCurrent()).IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
            Console.WriteLine("  this process elevated: " + elevated);
            try
            {
                var drive = new DriveInfo(root);
                Console.WriteLine("  volume: " + drive.DriveFormat + ", label " + drive.VolumeLabel + ", " + drive.TotalSize.ToString("N0") + " bytes");
                Console.WriteLine("  files: " + string.Join(", ", Directory.GetFileSystemEntries(root).Select(Path.GetFileName)));
                using var f = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read);
                Console.WriteLine("  " + Path.GetFileName(file) + ": " + f.Length.ToString("N0") + " bytes, opened");
                var head = new byte[20];
                f.Seek(0x10000, SeekOrigin.Begin); f.Read(head, 0, 20);
                Console.WriteLine("  at 0x10000: " + System.Text.Encoding.ASCII.GetString(head));
                if (baseHex != null)
                {
                    long b = Convert.ToInt64(baseHex, 16);
                    using var src = File.OpenRead(rom);
                    Console.WriteLine("  length = the image's past 0x" + b.ToString("X") + ": " + (f.Length == src.Length - b));
                    bool same = true;
                    var x = new byte[1 << 20]; var y = new byte[1 << 20];
                    foreach (long p in new[] { 0L, 0x10000L, f.Length / 2, f.Length - (1 << 20) })
                    {
                        f.Seek(p, SeekOrigin.Begin); src.Seek(b + p, SeekOrigin.Begin);
                        int n = f.Read(x, 0, x.Length), m = src.Read(y, 0, y.Length);
                        if (n != m || !x.AsSpan(0, n).SequenceEqual(y.AsSpan(0, m))) { same = false; Console.WriteLine("  differs at " + p); }
                    }
                    Console.WriteLine("  bytes = the image's at the start, middle and end: " + same);
                }
                watch.Restart();
                long total = 0; var buf = new byte[4 << 20]; int r;
                f.Seek(0, SeekOrigin.Begin);
                while (total < (512L << 20) && (r = f.Read(buf, 0, buf.Length)) > 0) total += r;
                Console.WriteLine("  read " + (total >> 20) + " MB in " + watch.ElapsedMilliseconds + " ms (" + (total / 1048576.0 / Math.Max(0.001, watch.Elapsed.TotalSeconds)).ToString("0") + " MB/s)");
            }
            catch (Exception ex) { Console.WriteLine("  READ FAILED: " + ex.GetType().Name + ": " + ex.Message); return false; }
            Console.WriteLine("  left attached - --xiso-detach --root " + root);
            return true;
        }

        public static bool Detach(string lb, string root)
        {
            RamDiskHost.UseRoot(lb);
            var watch = System.Diagnostics.Stopwatch.StartNew();
            bool ok = RamDrive.DetachImage(root, out var error);
            Console.WriteLine("  detach " + root + ": " + (ok ? "ok" : "FAILED - " + error) + " in " + watch.ElapsedMilliseconds + " ms; still there: " + Directory.Exists(root));
            return ok;
        }
    }
}
