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
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using LbIntegrations.RamDisk;
using LbIntegrations.Snapshot;

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

        // ── the files of the folder ──────────────────────────────────────────

        public const string FirmwareName = "firmware";
        public const string LockName = "lbip-vhdx.lock";

        /// <summary>WHAT GOES INTO A BASE, as a number: bumped whenever the way a base is built
        /// changes (the install, the merge of an update, the layout inside the disk), so that bases
        /// built the old way are rebuilt rather than trusted.</summary>
        public const int Recipe = 1;

        /// <summary>The virtual size of the firmware disk - and so of every disk over it: a
        /// differencing disk has its parent's size. Expandable, so it costs only what is written;
        /// 64 GB is the largest Vita memory card.</summary>
        public const int VirtualSizeMb = 65536;

        /// <summary>The tree Vita3K sees, one folder down from the disk's root: the junction cannot
        /// point at a volume root (see Vita3kWorkspace.OpenWorkingTree).</summary>
        public const string FsName = "fs";

        public static string FirmwareVhdx(string dir) => Path.Combine(dir, FirmwareName + ".vhdx");
        public static string FirmwareIdentity(string dir) => Path.Combine(dir, FirmwareName + ".identity");
        public static string GameVhdx(string dir, string titleId) => Path.Combine(dir, titleId + ".vhdx");
        public static string GameIdentity(string dir, string titleId) => Path.Combine(dir, titleId + ".identity");
        public static string GameReference(string dir, string titleId) => Path.Combine(dir, titleId + ".reference");
        public static string SessionVhdx(string dir, string titleId) => Path.Combine(dir, titleId + ".session.vhdx");

        // ── identities ───────────────────────────────────────────────────────
        //
        // One key=value per line. Every line but "built=" is compared: the same lines, in the same
        // order, or the base is rebuilt. The order is fixed by construction (DLC sorted), so two
        // launches finding the same files write the same text.

        /// <summary>A source file as the identity knows it: NAME, size and write time - not the full
        /// path (Mehdi's choice): ROMs moved to another folder are the same ROMs, and a base is not
        /// rebuilt for it. What could be confused - two different files with the same name, size and
        /// time - is then checked by the title id read from inside the archive.</summary>
        public static string SourceLine(string path)
        {
            try
            {
                var info = new FileInfo(path);
                return info.Name + "|" + info.Length + "|" + info.LastWriteTimeUtc.Ticks;
            }
            catch { return Path.GetFileName(path) + "|0|0"; }
        }

        /// <summary>The firmware disk's lines: which pristine firmware it holds, by its manifest.</summary>
        public static List<string> FirmwareLines(Vita3kLayout layout)
        {
            string nand;
            try { nand = Convert.ToHexString(SHA1.HashData(File.ReadAllBytes(Vita3kWorkspace.BaseManifestPath(layout)))); }
            catch { nand = "unknown"; }
            return new List<string> { "format=1", "kind=firmware", "recipe=" + Recipe, "nand=" + nand };
        }

        /// <summary>A game disk's lines: the firmware BUILD it sits on (a firmware disk made again is a
        /// different parent, and a differencing disk will not attach to it), the game, its update and
        /// each DLC - and what a save made on it was made with.</summary>
        public static List<string> GameLines(string firmwareBuild, string romPath, VitaContent game, VitaExtras extras)
        {
            var lines = new List<string>
            {
                "format=1", "kind=game", "recipe=" + Recipe,
                "title_id=" + game.TitleId,
                "firmware=" + firmwareBuild,
                "game=" + SourceLine(romPath) + "|" + (game.AppVer ?? ""),
            };
            if (extras?.Update != null)
                lines.Add("update=" + SourceLine(extras.Update.Path) + "|" + (extras.Update.Content.AppVer ?? ""));
            foreach (var a in (extras?.Addons ?? new List<VitaExtra>())
                              .OrderBy(a => a.Content.ContentId ?? Path.GetFileName(a.Path), StringComparer.Ordinal))
                lines.Add("dlc=" + SourceLine(a.Path) + "|" + (a.Content.ContentId ?? ""));
            lines.Add("app_ver=" + (extras?.Update?.Content.AppVer ?? game.AppVer ?? ""));
            return lines;
        }

        /// <summary>The lines of an identity file, "built=" left out. Null when there is none.</summary>
        public static List<string> ReadIdentity(string path)
        {
            try
            {
                if (!File.Exists(path)) return null;
                return File.ReadAllLines(path).Where(l => l.Length > 0 && !l.StartsWith("built=", StringComparison.Ordinal)).ToList();
            }
            catch { return null; }
        }

        /// <summary>The value of one key in identity lines, or null.</summary>
        public static string Value(List<string> lines, string key)
            => lines?.FirstOrDefault(l => l.StartsWith(key + "=", StringComparison.Ordinal))?.Substring(key.Length + 1);

        /// <summary>WRITTEN LAST, and through a temporary file: an identity that exists is a base that
        /// was finished. A half-written one is not an identity at all.</summary>
        private static void WriteIdentity(string path, IEnumerable<string> lines)
        {
            var tmp = path + ".tmp";
            File.WriteAllLines(tmp, lines.Concat(new[] { "built=" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") }));
            File.Move(tmp, path, overwrite: true);
        }

        /// <summary>What changed between two identities, for the log: "- dlc=..." for what went,
        /// "+ dlc=..." for what came.</summary>
        public static string Difference(List<string> had, List<string> now)
        {
            if (had == null) return "no identity (never built, or its build did not finish)";
            var gone = had.Except(now).Select(l => "- " + l);
            var came = now.Except(had).Select(l => "+ " + l);
            var all = gone.Concat(came).ToList();
            return all.Count == 0 ? "the same lines in another order" : string.Join("; ", all);
        }

        // ── building ─────────────────────────────────────────────────────────

        /// <summary>The disk of this game, built if it is not there or not what the launch finds now -
        /// its path, or null with <paramref name="error"/> (the session then takes the usual path).
        ///
        /// firmware.vhdx first, once: the pristine firmware copied in, never attached writable again.
        /// Then &lt;TITLE_ID&gt;.vhdx, a differencing disk over it holding the game, its update and
        /// its DLC, and &lt;TITLE_ID&gt;.reference, its walk - the same reference a RAM disk session
        /// takes, taken once. Each identity is written last.</summary>
        public static string EnsureGameBase(Vita3kLayout layout, string dir, string romPath, VitaContent game,
                                            VitaExtras extras, Action<string, double?> report, out string error)
        {
            error = null;
            FileStream held = null;
            try
            {
                // ONE BUILDER AT A TIME ON THIS FOLDER - two LaunchBox installs can share it (an
                // external drive). The other one waits a little, then takes the usual path.
                held = Hold(Path.Combine(dir, LockName), TimeSpan.FromSeconds(10));
                if (held == null) { error = "another install is building in " + dir; return null; }

                var sessionDisk = SessionVhdx(dir, game.TitleId);
                if (File.Exists(sessionDisk))
                { error = "a session disk of " + game.TitleId + " is still there (" + sessionDisk + ") - its base is left alone"; return null; }

                var build = EnsureFirmware(layout, dir, report, out error);
                if (build == null) return null;

                var path = GameVhdx(dir, game.TitleId);
                var identity = GameIdentity(dir, game.TitleId);
                var reference = GameReference(dir, game.TitleId);
                var want = GameLines(build, romPath, game, extras);
                var had = ReadIdentity(identity);
                if (had != null && had.SequenceEqual(want) && File.Exists(path) && File.Exists(reference))
                {
                    Log.Info("the disk of " + game.TitleId + " is the one this launch finds - reusing " + path);
                    return path;
                }
                Log.Info("building the disk of " + game.TitleId + ": " + Difference(had, want));
                Discard(path, identity, reference);

                // Room for it on the drive holding the folder: the game, its update and DLC as they
                // unpack, and a margin for the file system.
                long need = Vita3kContent.WorkingSizeBytes(romPath) + (extras?.Bytes ?? 0) + 256L * 1024 * 1024;
                long free = new DriveInfo(Path.GetPathRoot(dir)).AvailableFreeSpace;
                if (free < need)
                { error = "not enough room in " + dir + ": " + need / (1024 * 1024) + " MB needed, " + free / (1024 * 1024) + " free"; return null; }

                var watch = System.Diagnostics.Stopwatch.StartNew();
                report?.Invoke("Building the game's disk (first launch only)...", null);
                if (!RamDrive.CreateDifferencingVhdx(path, FirmwareVhdx(dir), out error)) { Discard(path, identity, reference); return null; }

                bool done = false;
                string drive = null;
                try
                {
                    drive = RamDrive.AttachVhdx(path, false, out error);
                    if (drive == null) return null;
                    var root = Path.Combine(drive, FsName);

                    var installed = Vita3kContent.Install(romPath, root, out error, report);
                    if (installed == null) return null;
                    Vita3kWorkspace.InstallExtras(extras ?? new VitaExtras(), root, installed, report);

                    report?.Invoke("Taking the console's fingerprint...", 0);
                    int walked = SnapWalk.WriteFrom(root, Vita3kWorkspace.BaseManifestPath(layout), installed.Written, installed.Hashed,
                                                    reference, out error, f => report?.Invoke(null, f), out _, out _);
                    if (walked < 0) return null;

                    if (!RamDrive.DetachVhdx(path, out error)) return null;
                    drive = null;
                    WriteIdentity(identity, want);
                    done = true;
                    Log.Info("built the disk of " + game.TitleId + " in " + watch.ElapsedMilliseconds + " ms, "
                             + walked + " entries in its reference, " + new FileInfo(path).Length / (1024 * 1024) + " MB on disk");
                    return path;
                }
                finally
                {
                    if (drive != null) RamDrive.DetachVhdx(path, out _);
                    if (!done) Discard(path, identity, reference);
                }
            }
            catch (Exception ex)
            {
                error = ex.GetType().Name + ": " + ex.Message;
                Log.Warn("could not build the disk of " + game?.TitleId, ex);
                return null;
            }
            finally { held?.Dispose(); }
        }

        /// <summary>firmware.vhdx, built if missing or holding another firmware - the id of its build,
        /// or null. A firmware disk made again makes every game disk over it an orphan: they are
        /// discarded here, the ones with a session left alone.</summary>
        private static string EnsureFirmware(Vita3kLayout layout, string dir, Action<string, double?> report, out string error)
        {
            error = null;
            var path = FirmwareVhdx(dir);
            var identity = FirmwareIdentity(dir);
            var want = FirmwareLines(layout);
            var had = ReadIdentity(identity);
            if (had != null && File.Exists(path) && had.Take(want.Count).SequenceEqual(want) && Value(had, "build") != null)
                return Value(had, "build");

            Log.Info("building the firmware disk: " + Difference(had?.Where(l => !l.StartsWith("build=", StringComparison.Ordinal)).ToList(), want));
            Discard(path, identity, null);
            foreach (var orphan in Directory.EnumerateFiles(dir, "*.identity"))
            {
                var id = Path.GetFileNameWithoutExtension(orphan);
                if (string.Equals(id, FirmwareName, StringComparison.OrdinalIgnoreCase)) continue;
                if (File.Exists(SessionVhdx(dir, id))) continue;
                Log.Info("  its game disk " + id + " goes with it");
                Discard(GameVhdx(dir, id), orphan, GameReference(dir, id));
            }

            var watch = System.Diagnostics.Stopwatch.StartNew();
            report?.Invoke("Building the firmware disk (once)...", null);
            if (!RamDrive.CreateVhdx(path, VirtualSizeMb, "VITA", out error)) { Discard(path, identity, null); return null; }
            string drive = null;
            bool done = false;
            try
            {
                drive = RamDrive.AttachVhdx(path, false, out error);
                if (drive == null) return null;
                var root = Path.Combine(drive, FsName);
                Directory.CreateDirectory(root);
                report?.Invoke("Copying the console firmware...", 0);
                Vita3kWorkspace.CopyTree(Vita3kWorkspace.BaseDir(layout), root, f => report?.Invoke(null, f));
                if (!RamDrive.DetachVhdx(path, out error)) return null;
                drive = null;

                var build = Guid.NewGuid().ToString("N");
                WriteIdentity(identity, want.Concat(new[] { "build=" + build }));
                done = true;
                Log.Info("built the firmware disk in " + watch.ElapsedMilliseconds + " ms, "
                         + new FileInfo(path).Length / (1024 * 1024) + " MB on disk");
                return build;
            }
            finally
            {
                if (drive != null) RamDrive.DetachVhdx(path, out _);
                if (!done) Discard(path, identity, null);
            }
        }

        /// <summary>A base and what goes with it, gone - the identity FIRST, so that anything
        /// interrupted leaves a disk with no identity, which is a disk nobody trusts. A disk still
        /// attached (a build cut short) is detached to be deleted.</summary>
        private static void Discard(string vhdx, string identity, string reference)
        {
            foreach (var p in new[] { identity, vhdx, reference })
            {
                if (p == null || !File.Exists(p)) continue;
                try { File.Delete(p); }
                catch (IOException) when (p == vhdx)
                {
                    RamDrive.DetachVhdx(p, out _);
                    try { File.Delete(p); } catch (Exception ex) { Log.Warn("could not delete " + p, ex); }
                }
                catch (Exception ex) { Log.Warn("could not delete " + p, ex); }
            }
        }

        /// <summary>The folder's lock, held open with no sharing - or null after <paramref name="wait"/>.</summary>
        private static FileStream Hold(string path, TimeSpan wait)
        {
            var until = DateTime.UtcNow + wait;
            while (true)
            {
                try { return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
                catch (IOException) when (DateTime.UtcNow < until) { System.Threading.Thread.Sleep(250); }
                catch (IOException) { return null; }
            }
        }
    }
}
