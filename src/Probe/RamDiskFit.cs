// --ramdisk-fit --lb <LaunchBox root> --nand <image> [--extra 4,8,16,32,50]
//
// How big a RAM disk has to be for a DSi working NAND. The image is a fixed-size file - a copy of the
// console it was built from, never grown by melonDS - so the only unknown is what NTFS keeps for itself
// on a volume of that size. For each margin: mount image + margin, note the space NTFS leaves free, copy
// the real image in, and say whether it fitted and what was left.

using System;
using System.Globalization;
using System.IO;
using System.Linq;
using LbIntegrations.RamDisk;

namespace LbIntegrations.Probe
{
    internal static class RamDiskFit
    {
        public static bool Run(string launchBoxRoot, string nand, string extras)
        {
            Console.WriteLine();
            Console.WriteLine("-- RAM disk: the size a DSi NAND needs  [MOUNTS REAL DRIVES] --");
            if (string.IsNullOrWhiteSpace(launchBoxRoot) || !Directory.Exists(launchBoxRoot) || !File.Exists(nand ?? ""))
            { Console.WriteLine("  pass --lb <LaunchBox root> --nand <image>"); return false; }
            RamDiskLog.Use(_ => { }, (m, ex) => Console.WriteLine("  [log] " + m + (ex != null ? " - " + ex.Message : "")));
            RamDiskHost.UseRoot(launchBoxRoot);

            long image = new FileInfo(nand).Length;
            int imageMb = (int)((image + 1024L * 1024 - 1) / (1024 * 1024));
            Console.WriteLine("  image     " + image.ToString("N0", CultureInfo.InvariantCulture) + " bytes = " + imageMb + " MB, rounded up");
            var margins = (string.IsNullOrWhiteSpace(extras) ? "2,4,8,16,32,50" : extras)
                          .Split(',').Select(x => int.Parse(x.Trim(), CultureInfo.InvariantCulture)).ToList();

            Console.WriteLine();
            Console.WriteLine("  margin   disk MB   NTFS keeps   free before   fits   free after");
            foreach (var margin in margins)
            {
                string root = null;
                try
                {
                    root = RamDrive.MountFor("fit-probe", imageMb + margin);
                    if (root == null) { Console.WriteLine("  " + margin + ": could not mount"); continue; }
                    var drive = new DriveInfo(root.Substring(0, 1));
                    long total = drive.TotalSize, freeBefore = drive.AvailableFreeSpace;
                    long declared = (long)(imageMb + margin) * 1024 * 1024;
                    bool fits;
                    long freeAfter = -1;
                    try
                    {
                        var dir = Path.Combine(root, "dsi");
                        Directory.CreateDirectory(dir);
                        File.Copy(nand, Path.Combine(dir, "work.bin"));
                        fits = new FileInfo(Path.Combine(dir, "work.bin")).Length == image;
                        freeAfter = new DriveInfo(root.Substring(0, 1)).AvailableFreeSpace;
                    }
                    catch (IOException) { fits = false; }
                    Console.WriteLine(string.Format(CultureInfo.InvariantCulture, "  {0,6}   {1,7}   {2,7:0.0} MB   {3,8:0.0} MB   {4,4}   {5}",
                        margin, imageMb + margin, (declared - freeBefore) / 1048576.0, freeBefore / 1048576.0,
                        fits ? "yes" : "NO", freeAfter < 0 ? "-" : (freeAfter / 1048576.0).ToString("0.0", CultureInfo.InvariantCulture) + " MB"));
                }
                finally { if (root != null) RamDrive.UnmountFor("fit-probe"); }
            }
            return true;
        }
    }
}
