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
using System.Linq;
using System.Runtime.InteropServices;
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
            Console.WriteLine("  helper            " + (helper ? "in place, version " + RamDrive.HelperVersion
                                                                   : "NOT in place"));
            Console.WriteLine("  image protocol    " + (RamDrive.CanMountImages
                ? "yes (>= " + RamDrive.ImageProtocol + ")"
                : "NO - needs " + RamDrive.ImageProtocol + ", so images would be refused"));
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

            if (!Mount()) return false;
            return RamDrive.CanMountImages ? Image() : true;
        }

        /// <summary>The 1.1 protocol, end to end: build a base image, put a file in it, unmount, then
        /// mount it back INTO MEMORY and check the file is there and the image on disk is untouched.
        ///
        /// That last check is the one that matters. A memory-backed drive exists so a session can be
        /// played on a copy while the pristine base stays exactly as it was - if the file changes,
        /// the whole model is wrong.</summary>
        private static bool Image()
        {
            Console.WriteLine();
            Console.WriteLine("-- the image protocol  [WRITES AN IMAGE FILE] " + new string('-', 18));

            int bad = 0;
            var image = Path.Combine(Path.GetTempPath(), "lbip-ramdisk-" + Guid.NewGuid().ToString("N") + ".img");
            const string key = "probe-image";
            const string payload = "written into the base image";
            string root = null;
            try
            {
                Console.WriteLine("  image     " + image);
                Console.WriteLine("  creating a " + SizeMb + " MB base image...");
                root = RamDrive.CreateImage(key, image, SizeMb);
                if (!Check("the image mounted as a drive", root != null)) return false;

                File.WriteAllText(Path.Combine(root, "base.txt"), payload);
                if (!Check("a file can be written into it", File.Exists(Path.Combine(root, "base.txt")))) bad++;

                if (!Check("unmounted", RamDrive.UnmountFor(key))) { bad++; return false; }
                root = null;

                var onDisk = new FileInfo(image);
                Console.WriteLine("  on disk   " + onDisk.Length + " bytes declared, "
                                  + Allocated(image) + " actually allocated"
                                  + (Allocated(image) < onDisk.Length ? "   (sparse - the point)" : ""));
                var before = File.ReadAllBytes(image).Length;
                var stamp = onDisk.LastWriteTimeUtc;

                Console.WriteLine("  mounting it back INTO MEMORY...");
                root = RamDrive.MountImage(key, image, RamDrive.RamImage.Memory);
                if (!Check("the image mounted into memory", root != null)) { bad++; return false; }

                var read = Path.Combine(root, "base.txt");
                if (!Check("the file written earlier is there", File.Exists(read))) bad++;
                else if (!Check("and reads back unchanged", File.ReadAllText(read) == payload)) bad++;

                File.WriteAllText(Path.Combine(root, "session.txt"), "this must NOT reach the image");
                if (!Check("a session file can be written to the copy",
                           File.Exists(Path.Combine(root, "session.txt")))) bad++;

                RamDrive.UnmountFor(key);
                root = null;

                // THE POINT OF THE WHOLE THING.
                if (!Check("the image on disk is byte-for-byte unchanged",
                           File.ReadAllBytes(image).Length == before
                           && new FileInfo(image).LastWriteTimeUtc == stamp)) bad++;
                using (var fs = File.OpenRead(image))
                    if (!Check("and does not contain the session file", !Contains(fs, "this must NOT reach"))) bad++;
            }
            catch (Exception ex)
            {
                Console.WriteLine("  EXCEPTION: " + ex.Message);
                bad++;
            }
            finally
            {
                if (root != null) RamDrive.UnmountFor(key);
                try { if (File.Exists(image)) File.Delete(image); } catch { }
            }

            Console.WriteLine();
            Console.WriteLine(bad == 0
                ? "  OK - a base image, a memory copy, and an original nothing wrote to."
                : "  " + bad + " FAILURE(S) - see above");
            return bad == 0;
        }

        /// <summary>Bytes the file really occupies, which is not its length when it is sparse - and
        /// that difference is the answer to "does a 10 GB image reserve 10 GB".
        ///
        /// GetCompressedFileSize rather than fsutil: parsing a localised console message for a
        /// number is how the first version of this printed 400000067108864 bytes, by scraping every
        /// digit in the output including the ones that were not the size.</summary>
        private static long Allocated(string path)
        {
            try
            {
                uint high;
                uint low = GetCompressedFileSize(path, out high);
                if (low == 0xFFFFFFFF && Marshal.GetLastWin32Error() != 0) return -1;
                return ((long)high << 32) | low;
            }
            catch { return -1; }
        }

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern uint GetCompressedFileSize(string fileName, out uint fileSizeHigh);

        private static bool Contains(Stream s, string needle)
        {
            var want = System.Text.Encoding.ASCII.GetBytes(needle);
            var buffer = new byte[1 << 20];
            int carry = 0;
            int read;
            while ((read = s.Read(buffer, carry, buffer.Length - carry)) > 0)
            {
                int end = carry + read;
                for (int i = 0; i + want.Length <= end; i++)
                {
                    int j = 0;
                    while (j < want.Length && buffer[i + j] == want[j]) j++;
                    if (j == want.Length) return true;
                }
                carry = Math.Min(want.Length - 1, end);
                Array.Copy(buffer, end - carry, buffer, 0, carry);
            }
            return false;
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
