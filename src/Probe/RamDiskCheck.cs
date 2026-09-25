// What the shared RAM disk has to get right, against a real LaunchBox install.
//
// It reports four capabilities first, because each one is broken and repaired differently and a
// single "ready / not ready" hides which. Then, if they are all there, it does the ONLY thing that
// proves the chain: mounts a small drive, writes a file, reads it back, unmounts, and checks the
// drive is gone. Everything between here and the kernel takes part in that - the scheduled task,
// the helper, ramdisk.cfg, imdisk, the drive letter - and nothing short of it proves any of them.
//
// THIS ONE MOUNTS A REAL DRIVE. It is 64 MB of real RAM for a few seconds, and it is unmounted
// afterwards including when an assertion fails. It writes ramdisk.cfg and ramdisk.result into the
// install's ThirdParty folder, which is what those files are for - LiteBox overwrites them at every
// mount.

using System;
using System.IO;
using LbIntegrations.RamDisk;

namespace LbIntegrations.Probe
{
    internal static class RamDiskCheck
    {
        private const int SizeMb = 64;

        public static bool Run(string launchBoxRoot)
        {
            Console.WriteLine();
            Console.WriteLine("-- RAM disk, shared with LiteBox  [MOUNTS A REAL DRIVE] " + new string('-', 8));

            if (string.IsNullOrWhiteSpace(launchBoxRoot))
            {
                Console.WriteLine("  skipped (pass --lb <LaunchBox root>)");
                return true;
            }
            if (!Directory.Exists(launchBoxRoot))
            {
                Console.WriteLine("  no such folder: " + launchBoxRoot);
                return false;
            }

            RamDiskLog.Use(m => Console.WriteLine("  [log] " + m),
                           (m, ex) => Console.WriteLine("  [log] " + m + (ex != null ? " - " + ex.Message : "")));
            RamDiskHost.UseRoot(launchBoxRoot);

            Console.WriteLine("  root      " + launchBoxRoot);
            Console.WriteLine("  helper    " + (RamDrive.HelperExe ?? "<unknown>"));

            string why;
            bool driver = RamDrive.IsDriverInstalled();
            bool helper = RamDrive.IsHelperInstalled();
            bool runtime = RamDrive.RuntimeReady(out why);
            string task = RamDrive.InstalledTaskName();

            Console.WriteLine();
            Console.WriteLine("  ImDisk driver     " + (driver ? "installed" : "NOT installed  (" + RamDrive.ImDiskExe + ")"));
            Console.WriteLine("  .NET runtime      " + (runtime ? "present" : "MISSING  (" + why + ")"));
            Console.WriteLine("  helper            " + (helper ? "in place" : "NOT in place"));
            Console.WriteLine("  elevated task     " + (task ?? "NOT registered"));
            Console.WriteLine("  free RAM          " + RamDrive.GetFreeRamMb() + " MB");
            Console.WriteLine("  IsReady()         " + RamDrive.IsReady());

            if (!RamDrive.IsReady())
            {
                Console.WriteLine();
                Console.WriteLine("  not ready - nothing was mounted. The four lines above say what is missing.");
                // NOT a failure: a machine without ImDisk is a supported state, and the whole point
                // of this library is that it degrades rather than breaks.
                return true;
            }

            return Mount();
        }

        private static bool Mount()
        {
            int bad = 0;
            string root = null;
            const string key = "probe";
            try
            {
                Console.WriteLine();
                Console.WriteLine("  mounting " + SizeMb + " MB...");
                root = RamDrive.MountFor(key, SizeMb);

                if (!Check("a drive came back", root != null)) return false;
                Console.WriteLine("  drive     " + root);
                if (!Check("the drive is there", Directory.Exists(root))) { bad++; return false; }

                // The drive is NTFS and empty - that is what the helper asked imdisk for.
                var probe = Path.Combine(root, "probe.txt");
                var payload = "mounted by the probe at " + DateTime.Now.ToString("HH:mm:ss");
                File.WriteAllText(probe, payload);
                if (!Check("a file can be written", File.Exists(probe))) bad++;
                if (!Check("and read back unchanged", File.ReadAllText(probe) == payload)) bad++;

                var drive = new DriveInfo(root.Substring(0, 1));
                Console.WriteLine("  format    " + Safe(() => drive.DriveFormat) + ", "
                                  + Safe(() => (drive.TotalSize / (1024 * 1024)) + " MB total"));
                if (!Check("it is formatted NTFS", string.Equals(Safe(() => drive.DriveFormat), "NTFS",
                                                                 StringComparison.OrdinalIgnoreCase))) bad++;

                // Usually "<nothing>" at this point, and that is RIGHT: the drive is usable in a
                // third of a second while the helper runs on for another minute and a half. Its
                // verdict lands later, and the unmount below is what waits for it.
                Console.WriteLine("  helper    " + RamDrive.ReadResult()
                                  + "   (empty here is expected - it finishes long after the drive works)");
            }
            catch (Exception ex)
            {
                Console.WriteLine("  EXCEPTION: " + ex.Message);
                bad++;
            }
            finally
            {
                if (root != null)
                {
                    Console.WriteLine("  unmounting...");
                    bool gone = RamDrive.UnmountFor(key);
                    if (!Check("the drive is unmounted", gone)) bad++;
                    if (!Check("and its letter is free again", !Directory.Exists(root))) bad++;
                }
            }

            Console.WriteLine();
            Console.WriteLine(bad == 0
                ? "  OK - mounted, written, read, unmounted. The whole chain works."
                : "  " + bad + " FAILURE(S) - see above");
            return bad == 0;
        }

        private static bool Check(string what, bool ok)
        {
            Console.WriteLine("  " + (ok ? "ok  " : "BAD ") + what);
            return ok;
        }

        private static string Safe(Func<string> f)
        {
            try { return f(); } catch { return "?"; }
        }
    }
}
