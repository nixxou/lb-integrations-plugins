// --ramdisk-leak --lb <LaunchBox root> [--size <MB>] [--dismount]
//
// Measures what an unmount leaves behind: the \Device\ImDisk<n> objects the driver still lists, and the
// memory the system still has committed. Mounts a real drive, fills most of it with non-zero bytes,
// unmounts it the way the plugins do (RamDrive.UnmountFor) - or, with --dismount, locks and dismounts
// the NTFS volume first - then counts again after a pause.

using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using LbIntegrations.RamDisk;
using Microsoft.Win32.SafeHandles;

namespace LbIntegrations.Probe
{
    internal static class RamDiskLeak
    {
        public static bool Run(string launchBoxRoot, int sizeMb, bool dismount)
        {
            Console.WriteLine();
            Console.WriteLine("-- RAM disk: what an unmount leaves behind  [MOUNTS A REAL DRIVE] --");
            if (string.IsNullOrWhiteSpace(launchBoxRoot) || !Directory.Exists(launchBoxRoot)) { Console.WriteLine("  pass --lb <LaunchBox root>"); return false; }
            RamDiskLog.Use(m => Console.WriteLine("  [log] " + m), (m, ex) => Console.WriteLine("  [log] " + m + (ex != null ? " - " + ex.Message : "")));
            RamDiskHost.UseRoot(launchBoxRoot);

            var before = State();
            Console.WriteLine("  before      " + before);
            const string key = "leak-probe";
            var root = RamDrive.MountFor(key, sizeMb);
            if (root == null) { Console.WriteLine("  could not mount"); return false; }
            var mounted = State();
            Console.WriteLine("  mounted     " + mounted + "   at " + root);

            long fill = (long)(sizeMb * 0.7) * 1024 * 1024;
            var buf = new byte[1 << 20];
            new Random(1).NextBytes(buf);
            using (var f = new FileStream(Path.Combine(root, "fill.bin"), FileMode.Create, FileAccess.Write, FileShare.None, 1 << 20, FileOptions.WriteThrough))
                for (long w = 0; w < fill; w += buf.Length) f.Write(buf, 0, buf.Length);
            var filled = State();
            Console.WriteLine("  filled      " + filled + "   (" + fill / (1024 * 1024) + " MB written)");

            if (dismount)
            {
                var letter = root.Substring(0, 2);
                Console.WriteLine("  dismount    " + Dismount(letter));
            }
            bool gone = RamDrive.UnmountFor(key);
            Console.WriteLine("  unmounted   gone=" + gone);
            Thread.Sleep(5000);
            var after = State();
            Console.WriteLine("  after 5 s   " + after);
            Console.WriteLine();
            Console.WriteLine("  devices left behind by this unmount: " + (after.Devices - before.Devices));
            Console.WriteLine("  commit still held:   " + (after.CommitMb - before.CommitMb) + " MB   (the drive held " + (filled.CommitMb - before.CommitMb) + " MB at its fullest)");
            return true;
        }

        private sealed class Snapshot
        {
            public int Devices, CommitMb, AvailableMb, NonPagedMb;
            public override string ToString() => $"devices={Devices} commit={CommitMb} MB available={AvailableMb} MB nonpaged={NonPagedMb} MB";
        }

        private static Snapshot State()
        {
            var s = new Snapshot();
            try
            {
                var psi = new ProcessStartInfo(RamDrive.ImDiskExe, "-l") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
                using var p = Process.Start(psi);
                var o = p.StandardOutput.ReadToEnd(); p.WaitForExit(10000);
                s.Devices = o.Split('\n').Count(l => l.Trim().StartsWith(@"\Device\ImDisk", StringComparison.OrdinalIgnoreCase));
            }
            catch { s.Devices = -1; }
            var perf = new PERFORMANCE_INFORMATION { cb = (uint)Marshal.SizeOf<PERFORMANCE_INFORMATION>() };
            if (GetPerformanceInfo(ref perf, perf.cb))
            {
                long page = (long)perf.PageSize;
                s.CommitMb = (int)((long)perf.CommitTotal * page / (1024 * 1024));
                s.AvailableMb = (int)((long)perf.PhysicalAvailable * page / (1024 * 1024));
                s.NonPagedMb = (int)((long)perf.KernelNonpaged * page / (1024 * 1024));
            }
            return s;
        }

        /// <summary>Lock then dismount the volume - what "imdisk -d" does before removing, and what a forced
        /// removal skips. Says what each step answered.</summary>
        private static string Dismount(string letter)
        {
            var tried = "";
            SafeFileHandle h = null;
            foreach (var access in new uint[] { GENERIC_READ | GENERIC_WRITE, GENERIC_READ, 0x80 /* FILE_READ_ATTRIBUTES */, 0 })
            {
                h = CreateFile(@"\\.\" + letter, access, FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
                if (!h.IsInvalid) { tried += "opened with 0x" + access.ToString("x") + "; "; break; }
                tried += "0x" + access.ToString("x") + " refused (" + Marshal.GetLastWin32Error() + "); ";
                h.Dispose();
            }
            if (h == null || h.IsInvalid) return tried;
            using var owned = h;
            bool flushed = FlushFileBuffers(h);
            bool locked = DeviceIoControl(h, FSCTL_LOCK_VOLUME, IntPtr.Zero, 0, IntPtr.Zero, 0, out _, IntPtr.Zero);
            int lockErr = locked ? 0 : Marshal.GetLastWin32Error();
            bool dismounted = DeviceIoControl(h, FSCTL_DISMOUNT_VOLUME, IntPtr.Zero, 0, IntPtr.Zero, 0, out _, IntPtr.Zero);
            int disErr = dismounted ? 0 : Marshal.GetLastWin32Error();
            return tried + $"flush={flushed} lock={locked}{(locked ? "" : " (error " + lockErr + ")")} dismount={dismounted}{(dismounted ? "" : " (error " + disErr + ")")}";
        }

        private const uint GENERIC_READ = 0x80000000, GENERIC_WRITE = 0x40000000, FILE_SHARE_READ = 1, FILE_SHARE_WRITE = 2, OPEN_EXISTING = 3;
        private const uint FSCTL_LOCK_VOLUME = 0x00090018, FSCTL_DISMOUNT_VOLUME = 0x00090020;

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern SafeFileHandle CreateFile(string name, uint access, uint share, IntPtr sa, uint disposition, uint flags, IntPtr template);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool DeviceIoControl(SafeFileHandle h, uint code, IntPtr inBuf, uint inSize, IntPtr outBuf, uint outSize, out uint returned, IntPtr overlapped);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool FlushFileBuffers(SafeFileHandle h);

        [StructLayout(LayoutKind.Sequential)]
        private struct PERFORMANCE_INFORMATION
        {
            public uint cb; public UIntPtr CommitTotal, CommitLimit, CommitPeak, PhysicalTotal, PhysicalAvailable, SystemCache, KernelTotal, KernelPaged, KernelNonpaged, PageSize;
            public uint HandleCount, ProcessCount, ThreadCount;
        }

        [DllImport("psapi.dll", SetLastError = true)]
        private static extern bool GetPerformanceInfo(ref PERFORMANCE_INFORMATION info, uint size);
    }
}
