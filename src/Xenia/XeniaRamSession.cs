// A game's unpacked content on a RAM disk for the time of its session - the game's package when it came in an archive,
// its title update and DLC (Mehdi, 01/10: below a threshold, and only when none of it is on the disk already).
//
// The RAM disk is the one LiteBox, melonDS and Vita3K mount (Shared.RamDisk): ImDisk, the elevated helper, one scheduled
// task. WHERE IT IS, in one small file: <plugin data>\ramdisk.where holds its root, the content root whose junctions
// point into it and the title id. NEVER OUTLIVES ITS SESSION: released once Xenia has come and gone (a watcher, as the
// compatibility list's refresh), at the next launch, and at the host's start - its junctions removed first, the links
// only, so that Xenia never sees a folder pointing at a drive that is not there.
//
// WHEN THERE IS NO RAM DISK - ImDisk not installed, no helper, no task, the setting off, the content over the threshold,
// not enough free memory - the content goes to the disk as it would have, and the log says why. Never a reason to
// refuse a launch.

using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LbIntegrations.RamDisk;

namespace LbIntegrations.Xenia
{
    internal static class XeniaRamSession
    {
        private const string MountKey = "xenia-content";
        /// <summary>Physical RAM kept free beside the disk, for Xenia - which wants several GB of its own.</summary>
        private const int ReserveMb = 4096;
        private static readonly object Gate = new object();

        private static string WherePath => Path.Combine(XeniaSettings.Dir, "ramdisk.where");

        /// <summary>Is the RAM disk wanted, and up to how many bytes of content?</summary>
        public static (bool On, long Threshold) Setting()
        {
            var s = XeniaExtras.ReadSettings();
            bool on = !(s.TryGetValue("ramdisk", out var v) && string.Equals(v, "off", StringComparison.OrdinalIgnoreCase));
            double gb = 2;
            if (s.TryGetValue("ramdisk_below_gb", out var t) && double.TryParse(t.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var g) && g > 0) gb = g;
            return (on, (long)(gb * 1024 * 1024 * 1024));
        }

        private static int SizeMb(long bytes) => (int)(bytes / (1024 * 1024)) + 32 + (int)(bytes / (1024 * 1024) / 20);

        /// <summary>Why <paramref name="bytes"/> of content would NOT go to a RAM disk now - null when it would. Nothing is
        /// mounted: the game's Session tab asks this. <paramref name="forced"/>: the game's options say "always the RAM disk" -
        /// the threshold is not asked, the rest is. <paramref name="cleanMemory"/>: a launch may free memory to make room.</summary>
        public static string WhyNot(XeniaLayout layout, long bytes, bool forced, bool cleanMemory = false)
        {
            try
            {
                var (on, threshold) = Setting();
                int sizeMb = SizeMb(bytes);
                UseRoot(layout?.InstallDir);
                if (!on && !forced) return "the RAM disk is off in the settings";
                if (!forced && bytes > threshold) return (bytes >> 20) + " MB of content, over the " + (threshold >> 20) + " MB threshold";
                if (!RamDrive.IsReady()) return NotReady();
                int free = RamDrive.GetFreeRamMb();
                if (cleanMemory && free > 0 && free < sizeMb + ReserveMb && RamDrive.CanCleanMemory) { RamDrive.CleanMemory(); free = RamDrive.GetFreeRamMb(); }
                if (free > 0 && free < sizeMb + ReserveMb) return sizeMb + " MB for the disk and " + ReserveMb + " kept free needed, " + free + " MB of RAM free";
                return null;
            }
            catch (Exception ex) { return ex.Message; }
        }

        /// <summary>A RAM disk for <paramref name="bytes"/> of content: the folder to put it in, or null - then it goes to the disk.</summary>
        public static string Open(XeniaLayout layout, string titleId, string name, long bytes, string exe, bool forced = false)
        {
            try
            {
                int sizeMb = SizeMb(bytes);
                var why = WhyNot(layout, bytes, forced, cleanMemory: true);
                if (why != null) { Log.Info("ramdisk: the content goes to the disk - " + why); return null; }
                var root = RamDrive.MountFor(MountKey, sizeMb);
                if (root == null) { Log.Info("ramdisk: did not mount - the content goes to the disk"); return null; }
                var dir = Path.Combine(root, name);
                Directory.CreateDirectory(dir);
                lock (Gate)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(WherePath));
                    File.WriteAllLines(WherePath, new[] { root, layout.ContentRoot, titleId, layout.InstallDir });
                }
                Log.Info("ramdisk: " + sizeMb + " MB at " + root + " for " + titleId + " (" + (bytes >> 20) + " MB of content)");
                ReleaseAfter(exe);
                return dir;
            }
            catch (Exception ex) { Log.Warn("ramdisk: the content goes to the disk", ex); return null; }
        }

        /// <summary>The session's RAM disk released - its junctions first. Nothing when there is none, or while Xenia runs.</summary>
        public static void Release(string why)
        {
            lock (Gate)
            {
                try
                {
                    if (!File.Exists(WherePath)) return;
                    var lines = File.ReadAllLines(WherePath);
                    if (lines.Length < 3) { File.Delete(WherePath); return; }
                    string root = lines[0], contentRoot = lines[1], titleId = lines[2], installDir = lines.Length > 3 ? lines[3] : null;
                    UseRoot(installDir);
                    if (installDir != null && XeniaRunning(installDir)) { Log.Info("ramdisk: Xenia is running - its RAM disk is kept"); return; }

                    var title = Path.Combine(contentRoot, "0000000000000000", titleId);
                    foreach (var type in new[] { XeniaExtras.UpdateType, XeniaExtras.DlcType })
                    {
                        var d = Path.Combine(title, type);
                        try
                        {
                            if (!Directory.Exists(d) && !File.Exists(d)) continue;
                            if (!File.GetAttributes(d).HasFlag(FileAttributes.ReparsePoint)) continue;
                            var target = new DirectoryInfo(d).LinkTarget ?? "";
                            if (target.StartsWith(root, StringComparison.OrdinalIgnoreCase)) Directory.Delete(d);   // the link, never what it points at
                        }
                        catch (Exception ex) { Log.Warn("ramdisk: junction " + d, ex); }
                    }
                    bool gone = !RamDrive.IsImDiskDrive(root) || RamDrive.Unmount(root);
                    Log.Info("ramdisk: released " + root + " - " + why + (gone ? "" : " (it is still there)"));
                    if (gone) File.Delete(WherePath);
                }
                catch (Exception ex) { Log.Warn("ramdisk: release", ex); }
            }
        }

        /// <summary>Once the Xenia of this launch has come and gone: released. Waits two minutes at most for it to appear.</summary>
        private static void ReleaseAfter(string exe)
        {
            var name = Path.GetFileNameWithoutExtension(exe ?? "");
            if (name.Length == 0) return;
            Task.Run(() =>
            {
                try
                {
                    bool Running() { try { return Process.GetProcessesByName(name).Length > 0; } catch { return false; } }
                    var armed = DateTime.UtcNow;
                    while (!Running() && (DateTime.UtcNow - armed).TotalSeconds < 120) Thread.Sleep(1000);
                    while (Running()) Thread.Sleep(2000);
                    Release("its game is over");
                }
                catch { }
            });
        }

        private static bool XeniaRunning(string installDir)
        {
            foreach (var name in XeniaPaths.ExecutableNames)
            {
                var exe = Path.Combine(installDir, name);
                if (File.Exists(exe) && XeniaConsolePanel.IsRunning(exe)) return true;
            }
            return false;
        }

        private static string NotReady()
        {
            if (!RamDrive.IsDriverInstalled()) return "no RAM disk driver is installed (Arsenal Image Mounter or ImDisk)";
            if (!RamDrive.RuntimeReady(out var runtime)) return "the RAM disk helper cannot run (" + runtime + ")";
            if (!RamDrive.IsHelperInstalled()) return "the RAM disk helper is not installed";
            if (RamDrive.InstalledTaskName() == null) return "the RAM disk task is not set up";
            return "no RAM disk is available";
        }

        /// <summary>The LaunchBox root the helper's folder is under - walked up from the emulator, as melonDS does.</summary>
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
