// An ImDisk RAM drive, mounted the way LiteBox mounts one - because it is the same one.
//
// THIS DELIBERATELY BUILDS NOTHING OF ITS OWN. LiteBox already ships this machinery
// (Host\Rom\ArchiveRamDisk.cs): the ImDisk driver the user installs, a small elevated helper under
// <LaunchBox>\ThirdParty\RomExtractor\ramdisk\, and ONE scheduled task registered at HIGHEST so the
// helper can be run afterwards without a UAC prompt each time. A second set of all that, sitting
// beside the first and doing the same job, would be two elevated tasks, two helpers and two things
// for a user to understand. So this folder speaks LiteBox's protocol exactly, writes to the same
// folder, and looks for the same task.
//
// THE PROTOCOL, read out of the helper's source and confirmed by running it:
//
//   ramdisk.cfg      action = mount | umount        (also "unmount", "remove")
//                    drive  = R                     letter, no colon
//                    size   = 2048                  MB, mount only
//                    label  = RomExtractorRAM       read by the helper and then IGNORED
//
//   ramdisk.result   OK|FAIL <action> <drive> exit=<n>   the imdisk run, whatever it did
//                    ERROR <message>                     it never got as far as imdisk
//
// THAT RESULT FILE IS THE COMPLETION SIGNAL, and it is not the same question as "is the drive
// usable". Both are needed, and asking the wrong one at the wrong moment is a race with teeth.
//
// Phase timings, measured on this machine, twice each and within a fifth of a second:
//
//   mount     schtasks returns 0.0s | helper starts 0.1s | DRIVE USABLE 0.3s | helper done 88.3s
//   unmount                                              | drive gone  29.5s | helper done 88.1s
//
// So the drive is ready almost at once, and then imdisk sits there for another minute and a half.
// 88.1, 88.3, 88.5 across three runs is a fixed cost, not work: something inside imdisk waits on a
// timeout. What it waits for has not been established here, and the number is quoted as measured
// rather than explained.
//
// WAITING FOR THE HELPER AFTER A MOUNT WOULD THEREFORE COST 88 SECONDS PER LAUNCH, which is not a
// price for anything. But NOT waiting at all is how the first version of this broke: the drive
// appeared, the mount was called done, the unmount fired straight after, and the scheduled task -
// whose policy is IgnoreNew - REFUSED it with 0x800710E0 because the previous run was still going.
// The drive stayed mounted and nothing said so.
//
// So the wait moved to where it belongs: BEFORE STARTING A RUN, never after finishing one. A mount
// returns as soon as its drive works; the next operation waits for the previous helper first, and
// in real use - a mount, a game, an unmount - that wait has already elapsed and costs nothing.
// LiteBox waits on the drive alone and has the same race; it never shows there for exactly that
// reason.
//
// EVERYTHING DEGRADES. No driver, no helper, no task, no runtime, not enough RAM, no free letter:
// each of those logs and returns null, and the caller carries on without a RAM disk. Nothing here
// ever throws to a caller, because the callers are a plugin inside somebody's frontend and an
// installer with three buttons.

#nullable disable

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace LbIntegrations.RamDisk
{
    internal static class RamDrive
    {
        /// <summary>key → mounted drive root ("R:\"), so a later cleanup can unmount what it did not
        /// mount itself.</summary>
        private static readonly ConcurrentDictionary<string, string> _active =
            new ConcurrentDictionary<string, string>();

        private static string System32
        {
            get { try { return Environment.SystemDirectory ?? @"C:\Windows\System32"; } catch { return @"C:\Windows\System32"; } }
        }

        public static string ImDiskExe => Path.Combine(System32, "imdisk.exe");
        private static string SchTasks => Path.Combine(System32, "schtasks.exe");

        /// <summary>The prefix every one of these tasks carries. LiteBox's name, kept literally -
        /// this is the string that makes the two find each other.</summary>
        public const string TaskPrefix = "LiteBox_RomExtractor_RamDisk_";

        /// <summary>How long to wait for a previous helper run to finish, in seconds. Measured at 88;
        /// this leaves room and still has a ceiling, because a run that was refused would otherwise
        /// be waited on for ever.</summary>
        private const int HelperSeconds = 150;

        /// <summary>How long to wait for a mounted drive to become usable. Measured at 0.3s - this is
        /// a ceiling for a machine under load, not an expectation.</summary>
        private const int MountSeconds = 30;

        /// <summary>How long to wait for an unmounted drive to go. Measured at 29.5s, which is the
        /// one phase here that really is work.</summary>
        private const int UnmountSeconds = 90;

        /// <summary>True when we have started a run and not yet seen it finish. The next run has to
        /// wait for it: this task ignores a second instance rather than queueing it.</summary>
        private static bool _runInFlight;

        /// <summary>The helper version that understands image, type, sparse and format in
        /// ramdisk.cfg. 1.0 is the protocol as LiteBox first shipped it.
        ///
        /// 1.1.1 AND NOT 1.1.0: the first build of the image protocol defaulted the format
        /// parameters off whenever an image was named, so CREATING one attached a disk with no
        /// filesystem - exit 0, a device, and a drive letter that is not a directory. Asking for
        /// 1.1.1 is how a helper with that in it is refused rather than trusted.
        ///
        /// IT IS CHECKED, NOT ASSUMED, because a 1.0 helper does not fail on a key it has never
        /// heard of - it ignores it and mounts a blank 1024 MB disk instead, which is a wrong answer
        /// wearing a success. And 1.0 is what is on disk wherever LiteBox got there first.</summary>
        public static readonly Version ImageProtocol = new Version(1, 1, 1, 0);

        /// <summary>The version of the helper actually deployed here, or null when there is none or
        /// it carries no version resource.</summary>
        public static Version HelperVersion
        {
            get
            {
                try
                {
                    var exe = HelperExe;
                    if (exe == null || !File.Exists(exe)) return null;
                    var v = FileVersionInfo.GetVersionInfo(exe);
                    if (v == null || v.FileVersion == null) return null;
                    return new Version(v.FileMajorPart, v.FileMinorPart, v.FileBuildPart, v.FilePrivatePart);
                }
                catch (Exception ex) { RamDiskLog.Warn("could not read the helper version", ex); return null; }
            }
        }

        /// <summary>Can the deployed helper be asked to back a drive with an image?</summary>
        public static bool CanMountImages
        {
            get { var v = HelperVersion; return v != null && v >= ImageProtocol; }
        }

        // ── where things live ────────────────────────────────────────────────

        /// <summary>The helper's folder: LiteBox's, exactly. Null when the root is unknown.</summary>
        public static string HelperDir
        {
            get
            {
                var root = RamDiskHost.Root();
                return root == null ? null : Path.Combine(root, "ThirdParty", "RomExtractor", "ramdisk");
            }
        }

        public static string HelperExe
        {
            get { var dir = HelperDir; return dir == null ? null : Path.Combine(dir, "RamDiskHelper.exe"); }
        }

        private static string CfgPath
        {
            get { var dir = HelperDir; return dir == null ? null : Path.Combine(dir, "ramdisk.cfg"); }
        }

        private static string ResultPath
        {
            get { var dir = HelperDir; return dir == null ? null : Path.Combine(dir, "ramdisk.result"); }
        }

        /// <summary>The task name LiteBox's light build computes for this install.
        ///
        /// It is an FNV-1a of what AppContext.BaseDirectory gives that build, which is
        /// &lt;root&gt;\Core - lower-cased, trailing separators removed. Reproduced here rather than
        /// invented, because a task under a different name would be a second task.
        ///
        /// It is what we REGISTER under. It is not the only name we ACCEPT - see IsTaskInstalled,
        /// and the note there about the two LiteBox builds.</summary>
        public static string TaskName
        {
            get
            {
                var root = RamDiskHost.Root();
                if (root == null) return null;
                return TaskPrefix + Fnv1a(Path.Combine(root, "Core"));
            }
        }

        /// <summary>FNV-1a of a path, in LiteBox's exact terms: lower-cased invariantly, trailing
        /// separators trimmed, eight lower-case hex digits.</summary>
        private static string Fnv1a(string path)
        {
            string s = (path ?? "").TrimEnd('\\', '/').ToLowerInvariant();
            uint h = 2166136261;
            foreach (char c in s) { h = (h ^ c) * 16777619; }
            return h.ToString("x8");
        }

        // ── what is available ────────────────────────────────────────────────

        /// <summary>True when the ImDisk CLI is present, which is how LiteBox decides the driver is
        /// installed. The user installs it themselves - neither side ships it.</summary>
        public static bool IsDriverInstalled()
        {
            try { return File.Exists(ImDiskExe); } catch { return false; }
        }

        /// <summary>True when the helper is sitting where it belongs.</summary>
        public static bool IsHelperInstalled()
        {
            try { var exe = HelperExe; return exe != null && File.Exists(exe); } catch { return false; }
        }

        /// <summary>Free physical RAM in MB, 0 on failure.</summary>
        public static int GetFreeRamMb()
        {
            try
            {
                var s = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX)) };
                if (GlobalMemoryStatusEx(ref s)) return (int)(s.ullAvailPhys / (1024UL * 1024UL));
            }
            catch { }
            return 0;
        }

        /// <summary>Is there a .NET runtime the helper can start on?
        ///
        /// IT IS A FRAMEWORK-DEPENDENT net9.0 EXE, so it needs a shared runtime on the machine - it
        /// does not carry one. Asked because the alternative is telling somebody everything is ready
        /// and then having a mount fail with nothing to read: the helper would not start, would write
        /// no result file, and the only trace would be a Windows dialog nobody sees because a
        /// scheduled task runs hidden.
        ///
        /// Our build rolls forward across majors, so anything from 9 up will do.</summary>
        public static bool RuntimeReady(out string why)
        {
            why = null;
            try
            {
                var dirs = new List<string>();
                foreach (var baseDir in new[]
                         {
                             Environment.GetEnvironmentVariable("ProgramFiles"),
                             Environment.GetEnvironmentVariable("ProgramW6432"),
                             @"C:\Program Files",
                         })
                {
                    if (string.IsNullOrWhiteSpace(baseDir)) continue;
                    var shared = Path.Combine(baseDir, "dotnet", "shared", "Microsoft.NETCore.App");
                    if (Directory.Exists(shared)) dirs.Add(shared);
                }

                foreach (var shared in dirs.Distinct(StringComparer.OrdinalIgnoreCase))
                    foreach (var version in Directory.GetDirectories(shared))
                    {
                        var name = Path.GetFileName(version);
                        int dot = name.IndexOf('.');
                        int major;
                        if (dot > 0 && int.TryParse(name.Substring(0, dot), out major) && major >= 9)
                            return true;
                    }

                why = "no .NET 9 or newer runtime is installed, and the RAM disk helper needs one";
                return false;
            }
            catch (Exception ex)
            {
                // Could not look - say yes rather than block on our own failure to read a folder.
                RamDiskLog.Warn("could not look for a .NET runtime", ex);
                return true;
            }
        }

        /// <summary>The registered task for THIS install, or null.
        ///
        /// TWO LOOKUPS, AND THE SECOND IS THE POINT. The first asks for the name LiteBox's light
        /// build computes. The second enumerates every task carrying the prefix and keeps one whose
        /// action names this install's helper - which is LiteBox's own acceptance test, just reached
        /// by enumeration instead of by name.
        ///
        /// It is needed because the tag is an FNV-1a of AppContext.BaseDirectory, and a LaunchBox
        /// install can carry two LiteBox builds whose base directories differ: the light one under
        /// Core\ and a single-file one at the root. Measured: both are present on this machine. Asking
        /// only for the name we compute would miss a task the other build registered, and we would
        /// helpfully register a second one beside it.</summary>
        public static string InstalledTaskName()
        {
            var helper = HelperExe;
            if (helper == null) return null;

            var expected = TaskName;
            if (expected != null && TaskPointsAtOurHelper(expected, helper)) return expected;

            try
            {
                string all = RunCaptured(SchTasks, "/query /fo LIST /v");
                if (all == null) return null;

                // "TaskName:  \LiteBox_RomExtractor_RamDisk_1a2b3c4d"
                foreach (var line in all.Split('\n'))
                {
                    int at = line.IndexOf(TaskPrefix, StringComparison.OrdinalIgnoreCase);
                    if (at < 0) continue;
                    var name = line.Substring(at).Trim();
                    // The prefix also turns up on the action line; keep only a bare task name.
                    int space = name.IndexOfAny(new[] { ' ', '\t', '"' });
                    if (space > 0) name = name.Substring(0, space);
                    if (name.Length <= TaskPrefix.Length) continue;
                    if (string.Equals(name, expected, StringComparison.OrdinalIgnoreCase)) continue;
                    if (TaskPointsAtOurHelper(name, helper))
                    {
                        RamDiskLog.Info("found LiteBox's task under another name: " + name);
                        return name;
                    }
                }
            }
            catch (Exception ex) { RamDiskLog.Warn("could not enumerate scheduled tasks", ex); }
            return null;
        }

        private static bool TaskPointsAtOurHelper(string taskName, string helperExe)
        {
            try
            {
                string outp = RunCaptured(SchTasks, "/query /tn \"" + taskName + "\" /v /fo LIST");
                return outp != null && outp.IndexOf(helperExe, StringComparison.OrdinalIgnoreCase) >= 0;
            }
            catch { return false; }
        }

        public static bool IsTaskInstalled() => InstalledTaskName() != null;

        /// <summary>True when a RAM disk can actually be mounted right now.</summary>
        public static bool IsReady()
        {
            string why;
            return IsDriverInstalled() && IsHelperInstalled() && RuntimeReady(out why)
                   && (IsTaskInstalled() || IsElevated());
        }

        private static bool IsElevated()
        {
            try
            {
                using (var id = System.Security.Principal.WindowsIdentity.GetCurrent())
                    return new System.Security.Principal.WindowsPrincipal(id)
                        .IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
            }
            catch { return false; }
        }

        // ── putting it in place ──────────────────────────────────────────────

        /// <summary>Register the elevated task - ONE UAC prompt, and then never again.
        ///
        /// Registered under the name LiteBox's light build looks for, so that build finds it without
        /// being told. Does nothing when a usable task is already there, whichever name it carries:
        /// the whole point is that there is one.</summary>
        public static bool InstallTask()
        {
            try
            {
                var helper = HelperExe;
                if (helper == null || !File.Exists(helper))
                {
                    RamDiskLog.Warn("cannot register the task: the helper is not at " + (helper ?? "<unknown>"));
                    return false;
                }
                var already = InstalledTaskName();
                if (already != null) { RamDiskLog.Info("the task is already registered as " + already); return true; }

                var name = TaskName;
                if (name == null) return false;

                var psi = new ProcessStartInfo(SchTasks)
                {
                    UseShellExecute = true,
                    Verb = "runas",
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden,
                    Arguments = "/create /tn \"" + name + "\" /tr \"\\\"" + helper + "\\\"\""
                                + " /sc ONCE /st 00:00 /rl HIGHEST /f",
                };
                using (var p = Process.Start(psi))
                {
                    if (p == null) return false;
                    p.WaitForExit();
                    RamDiskLog.Info("registering " + name + " exited with " + p.ExitCode);
                    return p.ExitCode == 0;
                }
            }
            catch (Exception ex) { RamDiskLog.Warn("could not register the elevated task", ex); return false; }
        }

        /// <summary>Remove the task. NOT called by this pack's uninstaller - the task may be
        /// LiteBox's, and removing it would take its RAM disk away too.</summary>
        public static bool UninstallTask()
        {
            try
            {
                var name = InstalledTaskName() ?? TaskName;
                if (name == null) return false;
                var psi = new ProcessStartInfo(SchTasks)
                {
                    UseShellExecute = true,
                    Verb = "runas",
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden,
                    Arguments = "/delete /tn \"" + name + "\" /f",
                };
                using (var p = Process.Start(psi))
                {
                    if (p == null) return false;
                    p.WaitForExit();
                    return p.ExitCode == 0;
                }
            }
            catch (Exception ex) { RamDiskLog.Warn("could not remove the elevated task", ex); return false; }
        }

        // ── mount / unmount ──────────────────────────────────────────────────

        /// <summary>Mount an NTFS RAM drive and remember it under <paramref name="key"/>, so a later
        /// cleanup can unmount it without being told where it went. Returns the drive root ("R:\")
        /// or null.</summary>
        public static string MountFor(string key, int sizeMb, CancellationToken ct = default(CancellationToken))
        {
            var root = Mount(sizeMb, ct);
            if (!string.IsNullOrEmpty(root) && !string.IsNullOrEmpty(key)) _active[key] = root;
            return root;
        }

        /// <summary>Mount an NTFS RAM drive of <paramref name="sizeMb"/> MB. Uses the elevated task
        /// when one is registered, else a direct imdisk call - which only works if we are already
        /// elevated. Returns the drive root ("R:\") or null on any failure.</summary>
        public static string Mount(int sizeMb, CancellationToken ct = default(CancellationToken))
        {
            try
            {
                if (sizeMb <= 0) return null;
                if (!IsDriverInstalled()) { RamDiskLog.Info("no ImDisk driver - not mounting"); return null; }

                int free = GetFreeRamMb();
                if (free > 0 && sizeMb >= free)
                {
                    RamDiskLog.Info("asked for " + sizeMb + " MB with " + free + " MB free - not mounting");
                    return null;
                }

                char letter = FreeDriveLetter();
                if (letter == '\0') { RamDiskLog.Info("no free drive letter"); return null; }
                string root = letter + ":\\";

                var task = InstalledTaskName();
                if (task != null)
                {
                    if (!StartRun(task, "mount", letter, sizeMb, ct)) return null;

                    // The drive, not the helper - measured at a third of a second, against a minute
                    // and a half for the helper to finish afterwards. See the header for why the
                    // waiting happens before the NEXT run instead.
                    if (WaitFor(() => Directory.Exists(root), MountSeconds, ct))
                    {
                        RamDiskLog.Info("mounted " + root + " (" + sizeMb + " MB) through the elevated task");
                        return root;
                    }

                    // No drive: now the helper's own words are worth waiting for, because something
                    // went wrong and that file is the only place it says what.
                    RamDiskLog.Warn("no drive appeared within " + MountSeconds + "s - waiting for the"
                                    + " helper to say why");
                    var said = WaitForResult(ct);
                    _runInFlight = said == null;
                    RamDiskLog.Warn("the task produced no drive - the helper said: "
                                    + (said ?? "nothing at all, which means it is still running or was"
                                               + " refused because another run is in flight"));
                    return null;
                }

                if (!IsElevated())
                {
                    RamDiskLog.Warn("no elevated task is registered and a direct mount needs admin");
                    return null;
                }

                int exit = RunQuiet(ImDiskExe, new[] { "-a", "-s", sizeMb + "M", "-m", letter + ":", "-p", "/fs:ntfs /q /y" });
                if (exit == 0 && Directory.Exists(root))
                {
                    RamDiskLog.Info("mounted " + root + " (" + sizeMb + " MB) directly");
                    return root;
                }
                RamDiskLog.Warn("imdisk exited with " + exit + " and " + root + " is not there");
                return null;
            }
            catch (Exception ex) { RamDiskLog.Warn("mount threw", ex); return null; }
        }

        /// <summary>Unmount a drive by its root ("R:\"). True when it is gone afterwards.</summary>
        public static bool Unmount(string driveRoot, CancellationToken ct = default(CancellationToken))
        {
            try
            {
                if (string.IsNullOrEmpty(driveRoot)) return false;
                char letter = driveRoot[0];

                var task = InstalledTaskName();
                if (task != null)
                {
                    if (!StartRun(task, "umount", letter, 0, ct)) return false;

                    bool gone = WaitFor(() => !Directory.Exists(driveRoot), UnmountSeconds, ct);
                    RamDiskLog.Info("unmounted " + driveRoot + " through the task (gone=" + gone + ")");
                    if (!gone)
                        RamDiskLog.Warn(driveRoot + " is still there after " + UnmountSeconds
                                        + "s - the helper will say why in ramdisk.result when it ends");
                    return gone;
                }

                int exit = RunQuiet(ImDiskExe, new[] { "-D", "-m", letter + ":" });
                return exit == 0 && !Directory.Exists(driveRoot);
            }
            catch (Exception ex) { RamDiskLog.Warn("unmount threw", ex); return false; }
        }

        /// <summary>Unmount whatever was mounted under this key.</summary>
        public static bool UnmountFor(string key)
        {
            if (string.IsNullOrEmpty(key)) return false;
            string root;
            return _active.TryRemove(key, out root) && Unmount(root);
        }

        /// <summary>Unmount everything this process mounted. What an exit cleanup calls, since it
        /// carries no key.</summary>
        public static void UnmountAll()
        {
            foreach (var kv in _active.ToArray())
            {
                string root;
                if (_active.TryRemove(kv.Key, out root)) Unmount(root);
            }
        }

        public static bool HasActiveMounts { get { return !_active.IsEmpty; } }

        // ── images ───────────────────────────────────────────────────────────

        /// <summary>Where the bytes of an image-backed drive live.</summary>
        internal enum RamImage
        {
            /// <summary>Virtual memory, preloaded from the image. The image on disk is never
            /// written to. The whole declared size is committed up front.</summary>
            Memory,

            /// <summary>Physical memory, preloaded from the image, through awealloc. Same untouched
            /// original as Memory, and the size comes from the image rather than being declared -
            /// which is the difference that matters when nobody knows the right number.</summary>
            PhysicalMemory,

            /// <summary>The image file IS the disk. Changes land in it. This is the one that
            /// persists, and the one to use when BUILDING a base image rather than playing on
            /// one.</summary>
            File,
        }

        private static string TypeOf(RamImage mode)
        {
            switch (mode)
            {
                case RamImage.PhysicalMemory: return "awe";
                case RamImage.File: return "file";
                default: return "vm";
            }
        }

        /// <summary>Mount a drive backed by an image, and remember it under <paramref name="key"/>.
        /// Returns the drive root ("X:\") or null.
        ///
        /// REFUSES RATHER THAN DEGRADES when the deployed helper is older than the image protocol,
        /// and that is deliberate. A 1.0 helper reads the image key, does not recognise it, and
        /// mounts a BLANK 1024 MB disk - a wrong answer that looks exactly like a right one, and
        /// which a caller would then fill with a session and hand back as a save. The caller is told
        /// no and can fall back to copying a pristine folder onto a plain RAM disk.</summary>
        public static string MountImage(string key, string imagePath, RamImage mode,
                                        int sizeMbIfNew = 0,
                                        CancellationToken ct = default(CancellationToken))
        {
            try
            {
                if (string.IsNullOrWhiteSpace(imagePath)) return null;
                if (!IsDriverInstalled()) { RamDiskLog.Info("no ImDisk driver - not mounting"); return null; }

                if (!CanMountImages)
                {
                    RamDiskLog.Warn("the deployed helper is " + (HelperVersion == null ? "unknown" : HelperVersion.ToString())
                                    + " and images need " + ImageProtocol + " - refusing rather than"
                                    + " letting it mount a blank disk instead. Run the pack's installer"
                                    + " to update it.");
                    return null;
                }

                bool exists = File.Exists(imagePath);
                if (!exists && sizeMbIfNew <= 0)
                {
                    RamDiskLog.Warn(imagePath + " does not exist and no size was given to create it");
                    return null;
                }
                if (mode != RamImage.File && !exists)
                {
                    RamDiskLog.Warn("a memory-backed drive has to be preloaded from an image that"
                                    + " exists; " + imagePath + " does not");
                    return null;
                }

                char letter = FreeDriveLetter();
                if (letter == '\0') { RamDiskLog.Info("no free drive letter"); return null; }
                string root = letter + ":\\";

                var task = InstalledTaskName();
                if (task == null)
                {
                    RamDiskLog.Warn("no elevated task is registered - an image mount needs one");
                    return null;
                }

                // Sparse only means anything for a file that IS the disk, and only when creating it.
                bool sparse = mode == RamImage.File && !exists;
                if (!StartRun(task, "mount", letter, sizeMbIfNew, ct, imagePath, TypeOf(mode), sparse))
                    return null;

                if (WaitFor(() => Directory.Exists(root), MountSeconds, ct))
                {
                    RamDiskLog.Info("mounted " + root + " from " + Path.GetFileName(imagePath)
                                    + " as " + mode);
                    if (!string.IsNullOrEmpty(key)) _active[key] = root;
                    return root;
                }

                RamDiskLog.Warn("no drive appeared within " + MountSeconds + "s - waiting for the"
                                + " helper to say why");
                var said = WaitForResult(ct);
                _runInFlight = said == null;
                RamDiskLog.Warn("the image did not mount - the helper said: " + (said ?? "nothing at all"));
                return null;
            }
            catch (Exception ex) { RamDiskLog.Warn("mounting " + imagePath + " threw", ex); return null; }
        }

        /// <summary>Create an empty image file with a filesystem on it, by mounting it as a
        /// file-backed disk and letting imdisk format it. Returns the drive root so the caller can
        /// fill it; UNMOUNT IT when done, and the file is then a usable base image.</summary>
        public static string CreateImage(string key, string imagePath, int sizeMb, bool sparse = true,
                                         CancellationToken ct = default(CancellationToken))
        {
            try
            {
                if (File.Exists(imagePath))
                {
                    RamDiskLog.Warn(imagePath + " already exists - not creating over it");
                    return null;
                }
                var dir = Path.GetDirectoryName(imagePath);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                // File mode with no existing file: the helper passes -s, and -o sparse when asked,
                // so imdisk creates the file and formats it.
                return MountImage(key, imagePath, RamImage.File, sizeMb, ct);
            }
            catch (Exception ex) { RamDiskLog.Warn("could not create " + imagePath, ex); return null; }
        }

        // ── the helper's file ────────────────────────────────────────────────

        /// <summary>Put the helper where both this pack and LiteBox look for it.
        ///
        /// ABSENT OR OLDER, NEVER NEWER - and LiteBox now follows the same rule for the same file.
        /// Two products write here from one source, neither can see the other's release schedule,
        /// and the version resource is what lets them agree without talking: whoever was updated
        /// last owns the file, and nobody steps back over anybody. Only-if-absent was the first
        /// rule and it was not enough - it froze a 1.0 helper in place for ever, and the pack would
        /// then have been sending it cfg keys it silently ignores.
        ///
        /// Returns what happened, for a message.</summary>
        public static string DeployHelper(Func<string, byte[]> fileByName, out bool ok)
        {
            ok = false;
            try
            {
                var dir = HelperDir;
                if (dir == null) return "The LaunchBox root is not known.";

                var installed = HelperVersion;
                if (installed != null && installed >= ImageProtocol)
                {
                    ok = true;
                    return "The RAM disk helper " + installed + " was already in place - left alone."
                           + " It may be LiteBox's, and it is new enough either way.";
                }

                Directory.CreateDirectory(dir);
                var replacing = installed != null;
                foreach (var name in HelperFiles)
                {
                    var bytes = fileByName(name);
                    if (bytes == null) return "The RAM disk helper is missing from this build (" + name + ").";
                    File.WriteAllBytes(Path.Combine(dir, name), bytes);
                }
                ok = true;
                var now = HelperVersion;
                RamDiskLog.Info((replacing ? "replaced the helper (" + installed + " -> " + now + ") in "
                                           : "deployed the helper to ") + dir);
                return replacing
                    ? "The RAM disk helper was updated from " + installed + " to " + now + " in " + dir
                      + ".\n\nLiteBox uses this same file and will not step back over it."
                    : "The RAM disk helper was installed into " + dir + ".";
            }
            catch (Exception ex)
            {
                RamDiskLog.Warn("could not deploy the helper", ex);
                return "The RAM disk helper could not be installed: " + ex.Message;
            }
        }

        /// <summary>The four files a framework-dependent .NET executable needs beside it.</summary>
        public static readonly string[] HelperFiles =
        {
            "RamDiskHelper.exe",
            "RamDiskHelper.dll",
            "RamDiskHelper.deps.json",
            "RamDiskHelper.runtimeconfig.json",
        };

        // ── helpers ──────────────────────────────────────────────────────────

        private static void WriteCfg(string action, char drive, int sizeMb,
                                    string image = null, string type = null, bool sparse = false)
        {
            try
            {
                var dir = HelperDir;
                if (dir == null) return;
                Directory.CreateDirectory(dir);

                // The four 1.0 keys, byte for byte what LiteBox writes - label included, which the
                // helper reads and then ignores. Writing it anyway keeps a plain mount's cfg
                // identical to theirs.
                var cfg = new StringBuilder();
                cfg.Append("action=").Append(action).Append("\r\n");
                cfg.Append("drive=").Append(drive).Append("\r\n");
                cfg.Append("size=").Append(sizeMb).Append("\r\n");
                cfg.Append("label=RomExtractorRAM\r\n");

                // And the 1.1 keys, written ONLY when asked for. A cfg with none of them is a 1.0
                // cfg, which is what every helper ever deployed understands.
                if (!string.IsNullOrEmpty(image)) cfg.Append("image=").Append(image).Append("\r\n");
                if (!string.IsNullOrEmpty(type)) cfg.Append("type=").Append(type).Append("\r\n");
                if (sparse) cfg.Append("sparse=1\r\n");

                File.WriteAllText(CfgPath, cfg.ToString());
            }
            catch (Exception ex) { RamDiskLog.Warn("could not write ramdisk.cfg", ex); }
        }

        private static void RunTask(string taskName, CancellationToken ct)
            => RunQuiet(SchTasks, new[] { "/run", "/tn", taskName });

        /// <summary>Ask the task to do one thing, having first made sure it is free to be asked.
        ///
        /// THE WAIT IS HERE AND NOWHERE ELSE. This task ignores a second instance instead of queueing
        /// it, so asking while the previous run is still going gets 0x800710E0 and silence. In real
        /// use the previous run finished long ago and this returns at once; back to back, it pays the
        /// minute and a half that the caller would otherwise have paid on every single mount.</summary>
        private static bool StartRun(string task, string action, char drive, int sizeMb,
                                    CancellationToken ct, string image = null, string type = null,
                                    bool sparse = false)
        {
            if (_runInFlight)
            {
                RamDiskLog.Info("waiting for the previous helper run to finish before asking again");
                var previous = WaitForResult(ct);
                _runInFlight = false;
                if (previous == null)
                {
                    RamDiskLog.Warn("the previous run never reported within " + HelperSeconds
                                    + "s - not asking for another, it would be refused");
                    return false;
                }
                RamDiskLog.Info("the previous run ended: " + previous);
            }

            WriteCfg(action, drive, sizeMb, image, type, sparse);
            ClearResult();
            RunTask(task, ct);
            _runInFlight = true;
            return true;
        }

        /// <summary>Poll a condition, in quarter seconds, up to a ceiling.</summary>
        private static bool WaitFor(Func<bool> done, int seconds, CancellationToken ct)
        {
            for (int i = 0; i < seconds * 4; i++)
            {
                try { if (done()) return true; } catch { }
                if (ct.IsCancellationRequested) return false;
                Thread.Sleep(250);
            }
            try { return done(); } catch { return false; }
        }

        /// <summary>Remove the previous run's answer, so the next one cannot be mistaken for it.</summary>
        private static void ClearResult()
        {
            try { var r = ResultPath; if (r != null && File.Exists(r)) File.Delete(r); }
            catch (Exception ex) { RamDiskLog.Warn("could not clear ramdisk.result", ex); }
        }

        /// <summary>Wait for the helper to say it is done, and return what it said - or null when it
        /// never did.
        ///
        /// THIS IS THE COMPLETION SIGNAL. The helper writes this file last, after imdisk has
        /// returned, so it is the only thing that means the run is over. Waiting on the drive
        /// instead returns while the volume is still being formatted and leaves the task busy,
        /// which is how a perfectly good unmount gets refused.
        ///
        /// Polled rather than watched: a FileSystemWatcher on a folder an elevated task writes to is
        /// more machinery than a quarter-second poll deserves.</summary>
        private static string WaitForResult(CancellationToken ct)
        {
            var path = ResultPath;
            if (path == null) return null;
            for (int i = 0; i < HelperSeconds * 4; i++)
            {
                if (ct.IsCancellationRequested) return null;
                if (File.Exists(path))
                {
                    // Written in one call, but read a beat later anyway: an empty read here would be
                    // a torn write, and waiting one more tick costs nothing.
                    var said = ReadResult();
                    if (!string.IsNullOrWhiteSpace(said) && said != "<nothing>") return said;
                }
                Thread.Sleep(250);
            }
            return null;
        }

        /// <summary>The helper's last word, verbatim.</summary>
        public static string ReadResult()
        {
            try
            {
                var p = ResultPath;
                return p != null && File.Exists(p) ? File.ReadAllText(p).Trim() : "<nothing>";
            }
            catch { return "<unreadable>"; }
        }

        private static char FreeDriveLetter()
        {
            try
            {
                var used = new HashSet<char>(DriveInfo.GetDrives().Select(d => char.ToUpperInvariant(d.Name[0])));
                // Z down to D, the way LiteBox and the plugin before it picked one.
                for (char c = 'Z'; c >= 'D'; c--) if (!used.Contains(c)) return c;
            }
            catch { }
            return '\0';
        }

        private static int RunQuiet(string exe, string[] args)
        {
            try
            {
                var psi = new ProcessStartInfo(exe) { UseShellExecute = false, CreateNoWindow = true };
                foreach (var a in args) psi.ArgumentList.Add(a);
                using (var p = Process.Start(psi))
                {
                    if (p == null) return -1;
                    p.WaitForExit(60000);
                    return p.HasExited ? p.ExitCode : -1;
                }
            }
            catch (Exception ex) { RamDiskLog.Warn("could not run " + Path.GetFileName(exe), ex); return -1; }
        }

        private static string RunCaptured(string exe, string arguments)
        {
            try
            {
                var psi = new ProcessStartInfo(exe, arguments)
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                };
                using (var p = Process.Start(psi))
                {
                    if (p == null) return null;
                    string outp = p.StandardOutput.ReadToEnd();
                    p.StandardError.ReadToEnd();
                    p.WaitForExit(15000);
                    return p.ExitCode == 0 ? outp : null;
                }
            }
            catch { return null; }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MEMORYSTATUSEX
        {
            public uint dwLength;
            public uint dwMemoryLoad;
            public ulong ullTotalPhys;
            public ulong ullAvailPhys;
            public ulong ullTotalPageFile;
            public ulong ullAvailPageFile;
            public ulong ullTotalVirtual;
            public ulong ullAvailVirtual;
            public ulong ullAvailExtendedVirtual;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);
    }
}
