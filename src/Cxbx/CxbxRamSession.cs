// A game unpacked onto a RAM disk for the time of its session - XeniaRamSession, copied, without the junctions (a
// Cxbx-Reloaded game is opened from its own folder, nothing points into the disk).
//
// The RAM disk is the one LiteBox and the other plugins mount (Shared.RamDisk): one helper, one scheduled task, the
// options of the NixxMenu's "RamDisk & VHDX" tab. WHERE IT IS: <plugin data>\ramdisk.where. NEVER OUTLIVES ITS
// SESSION: released once Cxbx-Reloaded has come and gone (CxbxSession), at the next launch, and at the host's start.
//
// WHEN THERE IS NO RAM DISK - no driver, no helper, no task, the setting off, the game over the threshold, not
// enough free memory - the game goes to the disk, and the log says why. Never a reason to refuse a launch.

using System;
using System.Collections.Generic;
using System.IO;
using LbIntegrations.RamDisk;

namespace LbIntegrations.Cxbx
{
    internal static class CxbxRamSession
    {
        private const string MountKey = "cxbx-game";
        /// <summary>Physical RAM kept free beside the disk, for Cxbx-Reloaded - a 32-bit program, 4 GB at most.</summary>
        private const int ReserveMb = 2048;
        private static readonly object Gate = new object();

        private static string WherePath => Path.Combine(CxbxSettings.Dir, "ramdisk.where");

        public static (bool On, long Threshold) Setting()
        {
            var s = CxbxSettings.Read();
            bool on = CxbxSettings.On(s, "ramdisk", true);
            var gb = CxbxSettings.Number(s, "ramdisk_below_gb", CxbxSettings.DefaultRamDiskBelowGb);
            return (on, (long)(gb * 1024 * 1024 * 1024));
        }

        private static int SizeMb(long bytes) => (int)(bytes / (1024 * 1024)) + 32 + (int)(bytes / (1024 * 1024) / 20);

        /// <summary>Why <paramref name="bytes"/> of game would NOT go to a RAM disk now - null when it would. Nothing is
        /// mounted. <paramref name="forced"/>: the game's options say "always the RAM disk" - the threshold is not asked.</summary>
        public static string WhyNot(string installDir, long bytes, bool forced, bool cleanMemory = false)
        {
            try
            {
                var (on, threshold) = Setting();
                int sizeMb = SizeMb(bytes);
                UseRoot(installDir);
                if (!on && !forced) return "the RAM disk is off in the settings";
                if (!forced && bytes > threshold) return (bytes >> 20) + " MB to unpack, over the " + (threshold >> 20) + " MB threshold";
                if (!RamDrive.IsReady()) return NotReady();
                int free = RamDrive.GetFreeRamMb();
                if (cleanMemory && free > 0 && free < sizeMb + ReserveMb && RamDrive.CanCleanMemory) { RamDrive.CleanMemory(); free = RamDrive.GetFreeRamMb(); }
                if (free > 0 && free < sizeMb + ReserveMb) return sizeMb + " MB for the disk and " + ReserveMb + " kept free needed, " + free + " MB of RAM free";
                return null;
            }
            catch (Exception ex) { return ex.Message; }
        }

        /// <summary>A RAM disk for <paramref name="bytes"/> of game: the folder to unpack it into, or null - then the disk.</summary>
        public static string Open(string installDir, string name, long bytes, bool forced = false)
        {
            try
            {
                int sizeMb = SizeMb(bytes);
                var why = WhyNot(installDir, bytes, forced, cleanMemory: true);
                if (why != null) { Log.Info("ramdisk: the game goes to the disk - " + why); return null; }
                var root = RamDrive.MountFor(MountKey, sizeMb);
                if (root == null) { Log.Info("ramdisk: did not mount - the game goes to the disk"); return null; }
                var dir = Path.Combine(root, name);
                Directory.CreateDirectory(dir);
                lock (Gate)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(WherePath));
                    File.WriteAllLines(WherePath, new[] { root, installDir ?? "" });
                }
                Log.Info("ramdisk: " + sizeMb + " MB at " + root + " (" + (bytes >> 20) + " MB to unpack)");
                return dir;
            }
            catch (Exception ex) { Log.Warn("ramdisk: the game goes to the disk", ex); return null; }
        }

        // ── an Xbox disc read where it is (03/10) ────────────────────────────
        // A bare ISO / XISO attached as a disk by the helper (view=xbox, AIM): nothing unpacked, the game read from the
        // image itself. WHERE IT IS: <plugin data>\disc.where. Released with the RAM disk, by the same calls.

        private static string DiscWherePath => Path.Combine(CxbxSettings.Dir, "disc.where");

        /// <summary>Why an Xbox disc would NOT be attached where it is - null when it would. <paramref name="game"/>: the
        /// game's options (its own choice wins over every game's).</summary>
        public static string WhyNotDisc(string installDir, IDictionary<string, string> game)
        {
            if (!CxbxSettings.AttachDiscs(game))
                return game != null && game.TryGetValue("attach_discs", out var v) && v == "off" ? "off for this game" : "off in the settings";
            return DiscSupport(installDir);
        }

        /// <summary>Why this machine cannot attach an Xbox disc - null when it can (AIM, the helper 1.7 and its task).</summary>
        public static string DiscSupport(string installDir)
        {
            try
            {
                UseRoot(installDir);
                return RamDrive.CanAttachXboxDisc(out var why) ? null : why;
            }
            catch (Exception ex) { return ex.Message; }
        }

        /// <summary>The disc attached: its root ("K:\"), or null - then it is unpacked.</summary>
        public static string AttachDisc(string installDir, string image)
        {
            try
            {
                UseRoot(installDir);
                var watch = System.Diagnostics.Stopwatch.StartNew();
                var root = RamDrive.AttachXboxDisc(image, out var error);
                if (root == null) { Log.Info("disc: not attached - " + error); return null; }
                lock (Gate)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(DiscWherePath));
                    File.WriteAllLines(DiscWherePath, new[] { root, installDir ?? "" });
                }
                Log.Info("disc: " + Path.GetFileName(image) + " attached at " + root + " in " + watch.Elapsed.TotalSeconds.ToString("0.0") + " s");
                return root;
            }
            catch (Exception ex) { Log.Warn("disc: not attached", ex); return null; }
        }

        /// <summary>The session's RAM disk and attached disc released. Nothing when there is none, or while Cxbx-Reloaded runs.</summary>
        public static void Release(string why)
        {
            ReleaseRam(why);
            ReleaseDisc(why);
        }

        private static void ReleaseDisc(string why)
        {
            lock (Gate)
            {
                try
                {
                    if (!File.Exists(DiscWherePath)) return;
                    var lines = File.ReadAllLines(DiscWherePath);
                    if (lines.Length < 1) { File.Delete(DiscWherePath); return; }
                    string root = lines[0], installDir = lines.Length > 1 && lines[1].Length > 0 ? lines[1] : null;
                    UseRoot(installDir);
                    if (CxbxPaths.LoaderRunning()) { Log.Info("disc: Cxbx-Reloaded is running - its disc stays attached"); return; }
                    string error = null;
                    bool gone = !Directory.Exists(root) || RamDrive.DetachImage(root, out error);
                    Log.Info("disc: detached " + root + " - " + why + (gone ? "" : " (it is still there: " + error + ")"));
                    if (gone) File.Delete(DiscWherePath);
                }
                catch (Exception ex) { Log.Warn("disc: release", ex); }
            }
        }

        private static void ReleaseRam(string why)
        {
            lock (Gate)
            {
                try
                {
                    if (!File.Exists(WherePath)) return;
                    var lines = File.ReadAllLines(WherePath);
                    if (lines.Length < 1) { File.Delete(WherePath); return; }
                    string root = lines[0], installDir = lines.Length > 1 && lines[1].Length > 0 ? lines[1] : null;
                    UseRoot(installDir);
                    if (CxbxPaths.LoaderRunning()) { Log.Info("ramdisk: Cxbx-Reloaded is running - its RAM disk is kept"); return; }
                    bool gone = !RamDrive.IsRamDisk(root) || RamDrive.Unmount(root);
                    Log.Info("ramdisk: released " + root + " - " + why + (gone ? "" : " (it is still there)"));
                    if (gone) File.Delete(WherePath);
                }
                catch (Exception ex) { Log.Warn("ramdisk: release", ex); }
            }
        }

        public static bool Holds(string path)
        {
            try
            {
                if (!File.Exists(WherePath)) return false;
                var root = File.ReadAllLines(WherePath)[0];
                return path != null && path.StartsWith(root, StringComparison.OrdinalIgnoreCase);
            }
            catch { return false; }
        }

        private static string NotReady()
        {
            if (!RamDrive.IsDriverInstalled()) return "no RAM disk driver is installed (ImDisk or the AIM Toolkit)";
            if (!RamDrive.RuntimeReady(out var runtime)) return "the RAM disk helper cannot run (" + runtime + ")";
            if (!RamDrive.IsHelperInstalled()) return "the RAM disk helper is not installed";
            if (RamDrive.InstalledTaskName() == null) return "the RAM disk task is not set up";
            return "no RAM disk is available";
        }

        /// <summary>The LaunchBox root the helper's folder is under - walked up from the emulator, as Xenia does.</summary>
        private static void UseRoot(string installDir)
        {
            var forced = Environment.GetEnvironmentVariable("LBIP_RAMDISK_ROOT");
            if (!string.IsNullOrWhiteSpace(forced)) { RamDiskHost.UseRoot(forced); return; }
            RamDiskHost.LaunchBoxRoot = () =>
            {
                var dir = installDir;
                for (int i = 0; i < 6 && !string.IsNullOrEmpty(dir); i++)
                {
                    if (Directory.Exists(Path.Combine(dir, "Core")) && Directory.Exists(Path.Combine(dir, "Data"))) return dir;
                    dir = Path.GetDirectoryName(dir);
                }
                return null;
            };
        }
    }
}
