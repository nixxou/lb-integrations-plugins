// --use-vhdx: the console kept in virtual disks instead of rebuilt in memory at every launch.
//
//     <dir>\firmware.vhdx             the pristine firmware, never attached writable
//     <dir>\<TITLE_ID>.vhdx           a DIFFERENCING disk over it: the game, its update and DLC installed
//     <dir>\<TITLE_ID>.identity       what that base was built from - written LAST, so a base without
//                                     one is a half-built one and is thrown away
//     <dir>\<TITLE_ID>.session.vhdx   a differencing disk over the game's, for one session, deleted
//                                     once the save is taken out of it
//
// <dir> is the value of --use-vhdx=<dir>, or <emulator folder>\vhdx when the flag has none. The three
// levels stay IN THE SAME FOLDER: a differencing disk records where its parent is, and side by side
// the relative locator still resolves when an external drive comes back under another letter.
//
// This file decides whether the folder can be used at all. Anything it refuses sends the session back
// to the usual path (RAM disk, then disk) with the reason in the log - a launch never fails for it.

using System;
using System.IO;
using LbIntegrations.RamDisk;

namespace LbIntegrations.Vita3k
{
    internal static class Vita3kVhdx
    {
        public const string DefaultDirName = "vhdx";

        /// <summary>Where the VHDX live for this launch: the folder asked for, or the emulator's own
        /// vhdx folder when --use-vhdx came without one.</summary>
        public static string DirFor(Vita3kLayout layout, string asked)
            => string.IsNullOrWhiteSpace(asked) ? Path.Combine(layout.InstallDir, DefaultDirName) : asked;

        /// <summary>Can the VHDX of this launch live in <paramref name="dir"/>? Null when they can
        /// (the folder then exists); otherwise why not, in words fit for the log.</summary>
        public static string WhyNot(Vita3kLayout layout, string dir)
        {
            RamDiskHost.LaunchBoxRoot = () => Vita3kPaths.LaunchBoxRootOf(layout);

            // THE HELPER FIRST: attaching needs the elevated task, and a helper older than 1.3 reads
            // a vhdx-* action as a MOUNT.
            if (!RamDrive.CanUseVhdx)
                return "the RAM disk helper cannot do VHDX (it is "
                       + (RamDrive.HelperVersion?.ToString() ?? "not installed") + ", "
                       + RamDrive.VhdxProtocol + " is needed - run the installer again)";

            // As the helper will take it: absolute, one line, no quote to close diskpart's own.
            if (string.IsNullOrWhiteSpace(dir) || !Path.IsPathFullyQualified(dir))
                return "the VHDX folder must be a full path, not \"" + dir + "\"";
            foreach (var c in dir)
                if (c < ' ' || c == '"') return "the VHDX folder holds a character the helper refuses: " + dir;

            string root;
            try { root = Path.GetPathRoot(Path.GetFullPath(dir)); }
            catch (Exception ex) { return "the VHDX folder is not a usable path (" + ex.Message + "): " + dir; }
            if (root == null || root.StartsWith(@"\\", StringComparison.Ordinal))
                return "the VHDX folder must be on a local drive, not a network one: " + dir;

            DriveInfo drive;
            try { drive = new DriveInfo(root); }
            catch (Exception ex) { return "the drive of the VHDX folder cannot be read (" + ex.Message + "): " + root; }
            if (!drive.IsReady)
                return root + " is not there - an external drive unplugged? (" + dir + ")";

            // A RAM disk is gone at the end of the session, the base with it: that is not a base.
            if (RamDrive.IsImDiskDrive(root))
                return root + " is a RAM disk - a VHDX kept there would not outlive the session";

            // NTFS or ReFS only. FAT32 cannot hold a file over 4 GB, and a game base easily is one;
            // exFAT is not refused on principle, it is not MEASURED yet - to be allowed once a VHDX
            // has been seen to attach from it.
            string format;
            try { format = drive.DriveFormat; }
            catch (Exception ex) { return "the file system of " + root + " cannot be read (" + ex.Message + ")"; }
            if (!string.Equals(format, "NTFS", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(format, "ReFS", StringComparison.OrdinalIgnoreCase))
                return root + " is " + format + " - VHDX are kept on NTFS or ReFS only";

            try { Directory.CreateDirectory(dir); }
            catch (Exception ex) { return "the VHDX folder cannot be created (" + ex.Message + "): " + dir; }
            return null;
        }
    }
}
