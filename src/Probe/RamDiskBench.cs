// How long does a RAM disk take to go away, by each way there is of asking?
//
//   --ramdisk-bench --lb <LaunchBox root> [--only-api | --shipped]
//
// Every trial mounts THROUGH THE TASK, the way a game launch does - so the helper's mount run is still
// going when the unmount is asked for, which is the situation a session end is really in. Then one
// method is timed from the request until the drive letter is gone:
//
//   RamDrive.Unmount     what the plugin does: the direct unmount, the task only as a fallback -
//                        asked at once, and again once the mount's helper run has finished
//   imdisk -d            direct, not forced, not elevated
//   imdisk -D            direct, forced, not elevated
//   API, no broadcast    imdisk.cpl's removal calls without its broadcast, then the shell alone told
//
// A few megabytes are written first, so the volume is not trivially empty. MOUNTS REAL DRIVES, and
// takes minutes: every mount after a direct unmount waits for the previous helper run to end.

using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using LbIntegrations.RamDisk;

namespace LbIntegrations.Probe
{
    internal static class RamDiskBench
    {
        private const int SizeMb = 256;

        public static bool Run(string launchBoxRoot, bool onlyApi = false, bool shipped = false)
        {
            Console.WriteLine();
            Console.WriteLine("-- RAM disk unmount, by method  [MOUNTS REAL DRIVES] " + new string('-', 10));
            if (string.IsNullOrWhiteSpace(launchBoxRoot) || !Directory.Exists(launchBoxRoot))
            { Console.WriteLine("  pass --lb <LaunchBox root>"); return false; }

            RamDiskLog.Use(m => Console.WriteLine("    [log] " + m), (m, ex) => Console.WriteLine("    [log] " + m + (ex != null ? " - " + ex.Message : "")));
            RamDiskHost.UseRoot(launchBoxRoot);
            if (!RamDrive.IsReady()) { Console.WriteLine("  the RAM disk is not set up on this machine"); return false; }

            var results = new System.Collections.Generic.List<string>();
            if (!onlyApi)
            {
                results.Add(Trial("RamDrive.Unmount, at once", root => RamDrive.Unmount(root), waitForHelperFirst: false));
                results.Add(Trial("RamDrive.Unmount, idle", root => RamDrive.Unmount(root), waitForHelperFirst: true));
            }
            if (!onlyApi && !shipped)
            {
                results.Add(Trial("imdisk -d (direct)", root => ImDisk("-d", root) == 0, waitForHelperFirst: false));
                results.Add(Trial("imdisk -D (direct, forced)", root => ImDisk("-D", root) == 0, waitForHelperFirst: false));
            }
            if (!shipped) results.Add(Trial("API, no broadcast + shell", ApiRemove, waitForHelperFirst: false));

            Console.WriteLine();
            Console.WriteLine("  method                         request -> gone");
            foreach (var r in results) Console.WriteLine("  " + r);
            return true;
        }

        /// <summary>--ramdisk-clean --lb &lt;root&gt;: ask the helper to free memory, and measure what it
        /// gave back. Trims other programs' working sets - they page back in when next used.</summary>
        public static bool Clean(string launchBoxRoot)
        {
            Console.WriteLine();
            Console.WriteLine("-- freeing memory through the helper  [TRIMS OTHER PROGRAMS] " + new string('-', 4));
            if (string.IsNullOrWhiteSpace(launchBoxRoot) || !Directory.Exists(launchBoxRoot))
            { Console.WriteLine("  pass --lb <LaunchBox root>"); return false; }
            RamDiskLog.Use(m => Console.WriteLine("    [log] " + m), (m, ex) => Console.WriteLine("    [log] " + m + (ex != null ? " - " + ex.Message : "")));
            RamDiskHost.UseRoot(launchBoxRoot);

            Console.WriteLine("  helper    " + (RamDrive.HelperVersion?.ToString() ?? "absent") + " - can clean: " + RamDrive.CanCleanMemory);
            if (!RamDrive.CanCleanMemory) return false;

            int before = RamDrive.GetFreeRamMb();
            var watch = Stopwatch.StartNew();
            var said = RamDrive.CleanMemory();
            int after = RamDrive.GetFreeRamMb();
            Console.WriteLine("  answer    " + (said ?? "(none)") + "  in " + watch.ElapsedMilliseconds + " ms");
            Console.WriteLine("  free RAM  " + before + " MB -> " + after + " MB  (" + (after - before >= 0 ? "+" : "") + (after - before) + " MB)");
            return said != null && said.StartsWith("OK clean", StringComparison.Ordinal);
        }

        private static string Trial(string name, Func<string, bool> unmount, bool waitForHelperFirst)
        {
            Console.WriteLine();
            Console.WriteLine("  " + name);
            var watch = Stopwatch.StartNew();
            var root = RamDrive.Mount(SizeMb);
            if (root == null) return string.Format("{0,-30} (did not mount)", name);
            Console.WriteLine("    mounted " + root + " in " + watch.ElapsedMilliseconds + " ms");

            try
            {
                var data = new byte[4 * 1024 * 1024];
                new Random(1).NextBytes(data);
                for (int i = 0; i < 4; i++) File.WriteAllBytes(Path.Combine(root, "bench-" + i + ".bin"), data);
            }
            catch (Exception ex) { Console.WriteLine("    could not write: " + ex.Message); }

            if (waitForHelperFirst)
            {
                var idle = Stopwatch.StartNew();
                while (idle.Elapsed.TotalSeconds < 240 && !(RamDrive.ReadResult() ?? "").StartsWith("OK mount", StringComparison.Ordinal))
                    Thread.Sleep(500);
                Console.WriteLine("    helper mount run over after " + (idle.ElapsedMilliseconds / 1000.0).ToString("0.0") + " s more");
            }

            watch.Restart();
            bool asked = false;
            try { asked = unmount(root); } catch (Exception ex) { Console.WriteLine("    threw: " + ex.Message); }
            var returned = watch.ElapsedMilliseconds;
            while (Directory.Exists(root) && watch.Elapsed.TotalSeconds < 180) Thread.Sleep(100);
            var gone = !Directory.Exists(root);
            var line = string.Format("{0,-30} {1,7:0.0} s   {2}", name, watch.ElapsedMilliseconds / 1000.0,
                                     gone ? (asked ? "ok" : "gone, but the call said it failed") : "STILL MOUNTED after 180 s");
            Console.WriteLine("    call returned after " + (returned / 1000.0).ToString("0.0") + " s - " + line.Trim());

            // A trial that left it mounted must not leave it behind for the next one.
            if (!gone) ImDisk("-D", root);
            return line;
        }

        // ── the removal imdisk.exe does, minus its broadcast ─────────────────
        //
        // imdisk.exe -D is, from its imports: ImDiskNotifyRemovePending (a broadcast to EVERY top-level
        // window, which waits on any hung one), then ImDiskForceRemoveDevice, then
        // ImDiskRemoveMountPoint. The same two calls without the first, and the one notification
        // that matters - the shell's, so Explorer drops the letter - sent without waiting for anyone.

        [DllImport("imdisk.cpl", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr ImDiskOpenDeviceByMountPoint(string mountPoint, uint accessMode);

        [DllImport("imdisk.cpl", SetLastError = true)]
        private static extern bool ImDiskForceRemoveDevice(IntPtr device, uint deviceNumber);

        [DllImport("imdisk.cpl", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool ImDiskRemoveMountPoint(string mountPoint);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr handle);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern uint QueryDosDevice(string deviceName, System.Text.StringBuilder targetPath, int max);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool DefineDosDevice(uint flags, string deviceName, string targetPath);

        private const uint DDD_RAW_TARGET_PATH = 0x1, DDD_REMOVE_DEFINITION = 0x2,
                           DDD_EXACT_MATCH_ON_REMOVE = 0x4, DDD_NO_BROADCAST_SYSTEM = 0x8;

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern void SHChangeNotify(int eventId, uint flags, string item1, IntPtr item2);

        private const int SHCNE_DRIVEREMOVED = 0x00000080;
        private const uint SHCNF_PATHW = 0x0005;
        private const uint SHCNF_FLUSHNOWAIT = 0x3000;
        private const uint GENERIC_READ = 0x80000000;

        private static bool ApiRemove(string root)
        {
            var mountPoint = root.Substring(0, 2);              // "Z:"
            var sw = Stopwatch.StartNew();
            // GENERIC_READ is refused to a standard user (error 5, measured) - yet imdisk -D works
            // unelevated. Two ways it could: a handle opened with no data access, or no handle at all,
            // the device named by its number. Both tried, and the one that works is printed.
            bool removed = false;
            int removeError = 0;
            var target = new System.Text.StringBuilder(512);
            QueryDosDevice(mountPoint, target, target.Capacity);
            var deviceName = target.ToString();                     // \Device\ImDisk<n>, read while it is still there
            Console.WriteLine("    " + mountPoint + " -> '" + deviceName + "'");
            var device = ImDiskOpenDeviceByMountPoint(mountPoint, 0);
            if (device != IntPtr.Zero && device != new IntPtr(-1))
            {
                removed = ImDiskForceRemoveDevice(device, 0);
                removeError = Marshal.GetLastWin32Error();
                CloseHandle(device);
                Console.WriteLine("    by handle (no access)  ForceRemove=" + removed + (removed ? "" : " error " + removeError) + " after " + sw.ElapsedMilliseconds + " ms");
            }
            else Console.WriteLine("    open with no access failed, error " + Marshal.GetLastWin32Error());

            if (!removed)
            {
                var name = deviceName;
                Console.WriteLine("    " + mountPoint + " is " + name);
                const string prefix = @"\Device\ImDisk";
                if (name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                    && uint.TryParse(name.Substring(prefix.Length), out var number))
                {
                    removed = ImDiskForceRemoveDevice(IntPtr.Zero, number);
                    removeError = Marshal.GetLastWin32Error();
                    Console.WriteLine("    by number " + number + "            ForceRemove=" + removed + (removed ? "" : " error " + removeError) + " after " + sw.ElapsedMilliseconds + " ms");
                }
                else Console.WriteLine("    not an ImDisk device - not touching it");
            }
            if (!removed) return false;

            Console.WriteLine("    letter still answers after the device went: " + Directory.Exists(root));

            // ImDiskRemoveMountPoint took 58.5 s (measured): it goes through DefineDosDevice WITHOUT
            // DDD_NO_BROADCAST_SYSTEM, so kernel32 broadcasts WM_DEVICECHANGE and waits on every hung
            // window. The same removal, told not to.
            bool unmapped = DefineDosDevice(DDD_REMOVE_DEFINITION | DDD_EXACT_MATCH_ON_REMOVE | DDD_RAW_TARGET_PATH | DDD_NO_BROADCAST_SYSTEM,
                                            mountPoint, deviceName);
            int unmapError = Marshal.GetLastWin32Error();
            Console.WriteLine("    DefineDosDevice(remove, no broadcast): " + unmapped + (unmapped ? "" : " (error " + unmapError + ")") + " after " + sw.ElapsedMilliseconds + " ms");
            if (!unmapped)
            {
                // Not matched on the target: remove the newest definition of the letter, which is ours.
                unmapped = DefineDosDevice(DDD_REMOVE_DEFINITION | DDD_NO_BROADCAST_SYSTEM, mountPoint, null);
                unmapError = Marshal.GetLastWin32Error();
                Console.WriteLine("    DefineDosDevice(remove newest, no broadcast): " + unmapped + (unmapped ? "" : " (error " + unmapError + ")") + " after " + sw.ElapsedMilliseconds + " ms");
            }
            if (!unmapped)
            {
                unmapped = ImDiskRemoveMountPoint(mountPoint + "\\");
                Console.WriteLine("    fell back to ImDiskRemoveMountPoint: " + unmapped + " after " + sw.ElapsedMilliseconds + " ms");
            }

            SHChangeNotify(SHCNE_DRIVEREMOVED, SHCNF_PATHW | SHCNF_FLUSHNOWAIT, mountPoint + "\\", IntPtr.Zero);
            Console.WriteLine("    SHChangeNotify (Explorer only, no wait) after " + sw.ElapsedMilliseconds + " ms");
            return removed;
        }

        private static int ImDisk(string flag, string root)
        {
            var psi = new ProcessStartInfo(RamDrive.ImDiskExe)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            psi.ArgumentList.Add(flag);
            psi.ArgumentList.Add("-m");
            psi.ArgumentList.Add(root.Substring(0, 2));
            using var p = Process.Start(psi);
            var said = (p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd()).Trim().Replace(Environment.NewLine, " | ");
            p.WaitForExit(180000);
            Console.WriteLine("    imdisk " + flag + ": exit " + p.ExitCode + " - " + said);
            return p.ExitCode;
        }
    }
}
