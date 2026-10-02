// A DSiWare title's working NAND on a RAM disk - by default, when one can be had (Mehdi, 29/09).
//
// melonDS ONLY. The working image is a 240 MB file melonDS is TOLD the path of (DSi.NANDPath), so it can
// live anywhere; no$gba reads a fixed name beside its own executable and cannot, so it stays on disk.
// The RAM disk is the one LiteBox and the Vita3K plugin mount (Shared.RamDisk): ImDisk, the elevated
// helper, one scheduled task.
//
// WHERE THE IMAGE IS, in one small file: dsi\work.where holds the RAM disk's root while a session's
// image is on it. MelonDsHost.For reads it, so every part of the shared engine - the capture, the
// rebuild, the lazy save refresh - works on the right image without knowing a RAM disk exists. The
// marker (work.title) and the receipt (work.sum) stay in dsi\, as they always were.
//
// NEVER TWO IMAGES OF ONE TITLE (Mehdi's worry, and his remedy): a session built on the RAM disk
// DELETES the image on the disk, once whatever it held has been captured - the launch captures before
// it rebuilds, always. And the RAM disk never outlives its session: released right after the capture
// on exit, marker forgotten. So the next launch, on the disk or on RAM, finds no old image to reuse and
// rebuilds from base.bin and the title's saved state. Relaunching the same game therefore always
// rebuilds on RAM - a copy of 240 MB into memory, a fraction of a second.
//
// WHEN THERE IS NO RAM DISK - ImDisk not installed, no helper, no task, no .NET runtime for it, not
// enough free memory, or --no-ramdisk on the game's line - the session is on the disk exactly as it was
// before, and the log says why. Never a reason to refuse a launch.
//
// A HOST OR MACHINE THAT DIES MID-SESSION. The host only: the RAM disk is still there and holds the
// session, and the next thing to look - a launch, or the start-up check - captures it, then releases
// it. The machine: the RAM disk is gone and the unsaved session with it (the known limit of playing in
// memory; --no-ramdisk avoids it); work.where then names a drive that is not there, and is forgotten.

using System;
using System.IO;
using System.Linq;
using LbIntegrations.Dsi;
using LbIntegrations.RamDisk;

namespace LbIntegrations.MelonDs
{
    internal static class MelonDsRamDisk
    {
        private const string WhereName = "work.where";
        private const string RamDirName = "dsi";
        private const string MountKey = "melonds-dsi";

        /// <summary>The RAM disk's size - MEASURED (29/09, probe --ramdisk-fit): the image is a fixed-size
        /// file, 251,658,304 bytes on every dump of every region seen (240 MB and a 64-byte footer), never
        /// grown by melonDS; NTFS keeps 14.5-15 MB of a volume that size for itself, and the image fits from
        /// 257 MB with 2.3 MB to spare. 300 MB (Mehdi's figure) leaves about 44 MB free. The engine's 256 MB
        /// disk rule is lifted on this volume - MelonDsHost sets RebuildFreeMargin - since the room is in the
        /// size already.</summary>
        private const int DiskMb = 300;

        /// <summary>What NTFS keeps, and a little: an image larger than the measured dumps still gets its
        /// room rather than a disk it cannot fit on.</summary>
        private const int NtfsMb = 16 + 16;

        /// <summary>Physical RAM kept free beside the disk, for melonDS and the rest of the machine.</summary>
        private const int ReserveMb = 1024;

        private static string WherePath(DsiHost host)
        {
            var dir = DsiWorkspace.DsiDir(host);
            return dir == null ? null : Path.Combine(dir, WhereName);
        }

        /// <summary>The folder on the RAM disk holding this installation's working image, or null when the
        /// image is on the disk - no work.where, or a work.where naming a drive that is not an ImDisk
        /// drive any more.</summary>
        public static string RamDir(DsiHost host)
        {
            try
            {
                var where = WherePath(host);
                if (where == null || !File.Exists(where)) return null;
                var root = File.ReadAllText(where).Trim();
                if (root.Length < 2 || !RamDrive.IsImDiskDrive(root)) return null;
                var dir = Path.Combine(root, RamDirName);
                return Directory.Exists(dir) ? dir : null;
            }
            catch { return null; }
        }

        /// <summary>Will this launch try a RAM disk? Asked before deciding whether the image on the disk
        /// may be reused - a session headed for RAM never reuses it. Only whether one CAN be had: the
        /// memory is looked at when it is placed, and a disk image rebuilt for nothing costs a quarter
        /// of a second.</summary>
        public static bool WillTry(MelonDsLayout layout, bool noRamDisk)
        {
            if (noRamDisk) return false;
            DsiHost host = layout;
            UseRoot(host);
            return RamDrive.IsReady();
        }

        /// <summary>Put the image of the session about to be built where it goes: on a RAM disk when one
        /// can be had and the line does not say --no-ramdisk, else on the disk. Called once the previous
        /// image has been captured and before the rebuild. Never throws, never refuses.</summary>
        public static void Place(MelonDsLayout layout, string sourceNand, bool noRamDisk)
        {
            DsiHost host = layout;
            try
            {
                UseRoot(host);
                // Whatever session was on a RAM disk has just been captured: its drive goes first.
                Release(host, "a new session is being built");

                long image = 0;
                try { image = new FileInfo(sourceNand).Length; } catch { }
                int sizeMb = Math.Max(DiskMb, (int)((image + 1024L * 1024 - 1) / (1024 * 1024)) + NtfsMb);

                string why = null;
                if (noRamDisk) why = MelonDsCommandLine.NoRamDiskFlag + " is on the command line";
                else if (image <= 0) why = "the console to build on cannot be sized";
                else if (!RamDrive.IsReady()) why = NotReadyReason();
                else
                {
                    int free = RamDrive.GetFreeRamMb();
                    if (free > 0 && free < sizeMb + ReserveMb && RamDrive.CanCleanMemory)
                    {
                        var said = RamDrive.CleanMemory();
                        Log.Info("ramdisk: freeing memory - " + free + " MB free before, " + RamDrive.GetFreeRamMb() + " after (" + (said ?? "no answer") + ")");
                        free = RamDrive.GetFreeRamMb();
                    }
                    if (free > 0 && free < sizeMb + ReserveMb)
                        why = sizeMb + " MB for the disk and " + ReserveMb + " kept free needed, " + free + " MB of physical RAM free";
                }

                if (why == null)
                {
                    var root = RamDrive.MountFor(MountKey, sizeMb);
                    if (root == null) why = "the RAM disk did not mount";
                    else
                    {
                        Directory.CreateDirectory(Path.Combine(root, RamDirName));
                        Atomic.WriteBytes(WherePath(host), System.Text.Encoding.UTF8.GetBytes(root));

                        // THE IMAGE ON THE DISK GOES: captured already, and never to be reused by mistake.
                        var disk = DiskImagePath(host);
                        if (disk != null && File.Exists(disk))
                        {
                            try { File.Delete(disk); Log.Info("ramdisk: the working NAND on the disk is removed - this session's is on " + root); }
                            catch (Exception ex) { Log.Warn("ramdisk: could not remove the working NAND on the disk", ex); }
                        }
                        Log.Info("ramdisk: the working NAND of this session is on a RAM disk at " + root + " (" + sizeMb + " MB)");
                        return;
                    }
                }
                Log.Info("ramdisk: the working NAND stays on the disk - " + why);
            }
            catch (Exception ex) { Log.Warn("ramdisk: could not place the working NAND - it stays on the disk", ex); }
        }

        /// <summary>The session has been captured: its RAM disk, if it had one, is released and the marker
        /// forgotten - the image it described is gone with the drive.</summary>
        public static void AfterCapture(DsiHost host)
        {
            if (host == null || RamDir(host) == null) return;
            UseRoot(host);
            if (Release(host, "its session is saved")) DsiWorkspace.ForgetWork(host);
        }

        /// <summary>At the plugin's start: a RAM disk left behind by a host that went mid-session is
        /// captured, then released; a work.where naming a drive that is gone (the machine restarted) is
        /// forgotten.</summary>
        public static void StartUp(MelonDsLayout layout, string bios7)
        {
            DsiHost host = layout;
            try
            {
                var where = WherePath(host);
                if (where == null || !File.Exists(where)) return;
                if (DsiNand.EmulatorRunning()) return;
                UseRoot(host);
                if (RamDir(host) != null)
                {
                    Log.Info("ramdisk: a session's RAM disk outlived its host - saving it, then releasing it");
                    DsiWorkspace.CaptureWork(host, bios7);
                    if (Release(host, "the start-up check saved it")) DsiWorkspace.ForgetWork(host);
                }
                else
                {
                    Log.Info("ramdisk: the RAM disk of the last session is gone (the machine restarted?) - forgetting it");
                    TryDelete(where);
                    DsiWorkspace.ForgetWork(host);
                }
            }
            catch (Exception ex) { Log.Warn("ramdisk: start-up check", ex); }
        }

        /// <summary>Unmount the RAM disk work.where names, and forget work.where. True when there is none
        /// left - also when there was none.</summary>
        private static bool Release(DsiHost host, string why)
        {
            var where = WherePath(host);
            if (where == null || !File.Exists(where)) return true;
            string root = null;
            try { root = File.ReadAllText(where).Trim(); } catch { }
            bool gone = true;
            if (!string.IsNullOrEmpty(root) && RamDrive.IsImDiskDrive(root))
            {
                gone = RamDrive.Unmount(root);
                Log.Info("ramdisk: released " + root + " - " + why + (gone ? "" : " (it is still there)"));
            }
            if (gone) TryDelete(where);
            return gone;
        }

        /// <summary>Where the image lives when it is on the disk - the engine's default.</summary>
        internal static string DiskImagePath(DsiHost host)
        {
            var dir = DsiWorkspace.DsiDir(host);
            return dir == null ? null : Path.Combine(dir, DsiWorkspace.WorkName);
        }

        /// <summary>What IsReady found missing, in words.</summary>
        internal static string NotReadyReason()
        {
            if (!RamDrive.IsDriverInstalled()) return "no RAM disk driver is installed (Arsenal Image Mounter or ImDisk)";
            if (!RamDrive.RuntimeReady(out var runtime)) return "the RAM disk helper cannot run (" + runtime + ")";
            if (!RamDrive.IsHelperInstalled()) return "the RAM disk helper is not installed";
            if (RamDrive.InstalledTaskName() == null) return "the RAM disk task is not set up";
            return "no RAM disk is available";
        }

        /// <summary>The LaunchBox root the helper's folder is under - walked up from the emulator.</summary>
        private static void UseRoot(DsiHost host)
        {
            // The probe's way in: a forged install in the temp folder has no LaunchBox root above it,
            // and this names the real one whose helper and task it may use.
            var forced = Environment.GetEnvironmentVariable("LBIP_RAMDISK_ROOT");
            if (!string.IsNullOrWhiteSpace(forced)) { RamDiskHost.UseRoot(forced); return; }
            RamDiskHost.LaunchBoxRoot = () =>
            {
                var dir = host?.InstallDir;
                for (int i = 0; i < 6 && !string.IsNullOrEmpty(dir); i++)
                {
                    if (Directory.Exists(Path.Combine(dir, "Core")) && Directory.Exists(Path.Combine(dir, "Data"))) return dir;
                    dir = Path.GetDirectoryName(dir);
                }
                return null;
            };
        }

        private static void TryDelete(string path)
        {
            try { if (path != null && File.Exists(path)) File.Delete(path); } catch { }
        }
    }
}
