// The disc given to xemu by ONE path that never changes (Mehdi, 05/10): <xemu>\lbip-disc\game.iso, a symbolic link to the
// disc of the session - the file itself, the disc served where it is (RamDrive.AttachXiso), or its copy on the RAM disk or in
// the cache - and xemu told the RELATIVE path, lbip-disc\game.iso.
//
// WHY: a snapshot records the disc's path as xemu was given it, and loading one compares it with the disc in the drive,
// character for character (ui/xui/snapshot-manager.cc): another path - another drive letter, the RAM disk one time and the
// cache the next - and xemu asks to load the snapshot's, which fails when that path is gone. Relative (Mehdi: "si on change
// d'ordi, c'est mort"): the same path on any machine, for snapshots that travel with LaunchBox's backups.
//
// MEASURED 05/10 (xemu v0.8.136): xemu reads a disc through a symbolic link, to another volume too; it keeps the relative
// path as it is and resolves it from the folder it was started in - from its own folder, the game starts; from C:\, "Could
// not open". So it holds only while LaunchBox starts it in its folder: to see at the first launch from LaunchBox.
// A SYMBOLIC LINK NEEDS THE RIGHT: Windows' developer mode, or an elevated LaunchBox. Without it, or when anything is not as
// expected, xemu is given the disc's own path as before, and the log says why.
//
// NEVER A FILE OF THE USER'S: only a symbolic link is deleted there - a real file at that place is left alone, and the disc
// then goes by its own path. The link alone is ever deleted, never what it points to.

using System;
using System.IO;

namespace LbIntegrations.Xemu
{
    internal static class XemuDiscLink
    {
        public const string RelativePath = @"lbip-disc\game.iso";

        private static string LinkOf(string exe) => XemuPaths.Dir(exe) is string d ? Path.Combine(d, RelativePath) : null;

        /// <summary>The path to give xemu for <paramref name="disc"/>: the relative one when the link is made and reads as the
        /// disc, else the disc's own.</summary>
        public static string For(string exe, string disc)
        {
            var link = LinkOf(exe);
            if (link == null || string.IsNullOrEmpty(disc)) return disc;
            try
            {
                var target = Path.GetFullPath(disc);
                if (!File.Exists(target)) return disc;
                Directory.CreateDirectory(Path.GetDirectoryName(link));
                if (!RemoveLink(link)) { Log.Warn("disc: " + link + " is a file, not a link of this plugin - left alone, the disc goes by its own path"); return disc; }
                try { File.CreateSymbolicLink(link, target); }
                catch (Exception ex)
                {
                    Log.Info("disc: no fixed path (" + ex.Message.Trim() + " - a symbolic link needs Windows' developer mode, or LaunchBox run as an "
                             + "administrator) - the disc goes by its own path, " + target);
                    return disc;
                }
                // Checked: a link, to this disc, read through as the disc.
                var made = new FileInfo(link);
                long through;
                using (var fs = new FileStream(link, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete)) through = fs.Length;
                if (made.LinkTarget == null || !string.Equals(Path.GetFullPath(made.LinkTarget), target, StringComparison.OrdinalIgnoreCase) || through != new FileInfo(target).Length)
                {
                    RemoveLink(link);
                    Log.Warn("disc: the link " + link + " did not read as " + target + " - the disc goes by its own path");
                    return disc;
                }
                Log.Info("disc: " + RelativePath + " -> " + target + " (one path for every session, as its snapshots recorded it)");
                return RelativePath;
            }
            catch (Exception ex)
            {
                Log.Warn("disc: no fixed path - the disc goes by its own path", ex);
                RemoveLink(link);
                return disc;
            }
        }

        /// <summary>The link taken away - at a session's end, at start-up (a session the host never saw end). Never while xemu runs.</summary>
        public static void Remove(string exe)
        {
            try
            {
                if (XemuPaths.Running(exe)) return;
                var link = LinkOf(exe);
                if (link != null && IsLink(link) && RemoveLink(link)) Log.Info("disc: " + RelativePath + " taken away");
            }
            catch (Exception ex) { Log.Warn("disc: the link could not be taken away", ex); }
        }

        private static bool IsLink(string path)
        {
            try { var fi = new FileInfo(path); return fi.LinkTarget != null || (fi.Exists && (fi.Attributes & FileAttributes.ReparsePoint) != 0); }
            catch { return false; }
        }

        /// <summary>Nothing there, or a link deleted: true. A real file: false, left alone.</summary>
        private static bool RemoveLink(string path)
        {
            try
            {
                var fi = new FileInfo(path);
                if (fi.LinkTarget == null && !fi.Exists) return true;
                if (!IsLink(path)) return false;
                File.Delete(path);        // the link itself - never its target
                return true;
            }
            catch { return false; }
        }
    }
}
