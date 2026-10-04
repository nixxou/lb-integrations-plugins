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
// 88.1, 88.3, 88.5 across three runs is a fixed cost, not work: imdisk.exe broadcasts the new drive
// to every top-level window and waits on each, and HUNG windows were answering at ~29 s apiece
// (a stuck installer, measured 26/09; without it the whole run is ~5 s). An unmount no longer goes
// through any of this - see "the direct unmount" below. A mount still does.
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

        /// <summary>The helper version that understands action=clean. AN OLDER ONE READS "clean" AS A
        /// MOUNT - it only knows the unmount words and takes anything else for its default - so this is
        /// checked before the word is ever written.</summary>
        public static readonly Version CleanProtocol = new Version(1, 2, 0, 0);

        /// <summary>Can the deployed helper be asked to free memory?</summary>
        public static bool CanCleanMemory
        {
            get { var v = HelperVersion; return v != null && v >= CleanProtocol && InstalledTaskName() != null; }
        }

        /// <summary>Ask the elevated helper to free physical memory - trim the working sets of the
        /// processes holding more than 100 MB and flush the modified list to the page file (see the
        /// helper's header for what it does and deliberately does not). Waits for it to finish, since
        /// the point is to measure afterwards. Returns what it said, or null.</summary>
        public static string CleanMemory(CancellationToken ct = default(CancellationToken))
        {
            try
            {
                if (!CanCleanMemory)
                {
                    RamDiskLog.Info("the helper cannot free memory (it is " + (HelperVersion?.ToString() ?? "absent")
                                    + ", " + CleanProtocol + " is needed)");
                    return null;
                }
                var task = InstalledTaskName();
                string said = null;
                // TWICE AT MOST. A run started by somebody else - LiteBox, another process of ours -
                // may still be going: the task ignores a second instance, and the answer that then
                // comes back is THAT run's. Measured: "OK mount Z exit=0" in reply to a clean, and
                // nothing freed. An answer that is not a clean's is recognised as such and asked again,
                // now that the other run has finished.
                for (int attempt = 1; attempt <= 2; attempt++)
                {
                    if (!StartRun(task, "clean", 'Z', 0, ct)) return null;
                    said = WaitForResult(ct);
                    _runInFlight = said == null;
                    if (said != null && said.StartsWith("OK clean", StringComparison.Ordinal) && IsOurs(said)) break;
                    RamDiskLog.Info("the helper answered \"" + (said ?? "nothing") + "\" - another run's, not this clean"
                                    + (attempt == 1 ? " - asking again" : ""));
                    said = null;
                }
                RamDiskLog.Info("asked the helper to free memory - it said: " + (said ?? "nothing that was a clean"));
                return said;
            }
            catch (Exception ex) { RamDiskLog.Warn("freeing memory threw", ex); return null; }
        }

        // ── VHDX (helper 1.3) ────────────────────────────────────────────────
        //
        // A pristine image kept clean: the base VHDX is never attached writable during a session - a
        // DIFFERENCING child is created over it, attached instead, and thrown away afterwards. Four
        // helper actions, all through the elevated task: vhdx-create, vhdx-child, vhdx-attach,
        // vhdx-detach. See the helper's header for what each does and how its values are guarded.

        /// <summary>The helper version that knows the vhdx-* actions. AN OLDER ONE READS THEM AS A
        /// MOUNT, so nothing is sent below it.</summary>
        public static readonly Version VhdxProtocol = new Version(1, 3, 0, 0);

        /// <summary>The helper version that knows action=dismount - an unmount that locks and dismounts the
        /// volume before removing the device, and so leaves no "removed" device behind (see the helper's
        /// header). AN OLDER ONE READS IT AS A MOUNT, so nothing is sent below it.</summary>
        public static readonly Version DismountProtocol = new Version(1, 4, 0, 0);

        /// <summary>Can the deployed helper unmount cleanly?</summary>
        public static bool CanDismountCleanly
        {
            get { var v = HelperVersion; return v != null && v >= DismountProtocol && InstalledTaskName() != null; }
        }

        /// <summary>How long a clean dismount waits for the helper to be free - the mount's own run,
        /// still announcing the drive to every window when a session is short - before the unelevated
        /// removal is used instead. Bounded, because the end of a session waits on it.</summary>
        private const int DismountWaitSeconds = 20;

        /// <summary>Can the deployed helper create and attach VHDX files?</summary>
        public static bool CanUseVhdx
        {
            get { var v = HelperVersion; return v != null && v >= VhdxProtocol && InstalledTaskName() != null; }
        }

        /// <summary>Create a dynamic VHDX of <paramref name="sizeMb"/>, formatted NTFS, detached.</summary>
        public static bool CreateVhdx(string path, int sizeMb, string label, out string error)
            => Vhdx("vhdx-create", path, out error, new Dictionary<string, string>
               { { "size", sizeMb.ToString(System.Globalization.CultureInfo.InvariantCulture) }, { "label", label ?? "VHDX" } });

        /// <summary>Create a DIFFERENCING VHDX over <paramref name="parent"/>, detached. Everything
        /// written to the child stays in the child; the parent is only ever read.</summary>
        public static bool CreateDifferencingVhdx(string child, string parent, out string error)
            => Vhdx("vhdx-child", child, out error, new Dictionary<string, string> { { "parent", parent ?? "" } });

        /// <summary>Attach a VHDX and give it a free letter. Returns its root ("X:\") or null.</summary>
        public static string AttachVhdx(string path, bool readOnly, out string error)
        {
            // vhdx=aim (and AIM there, and the helper 1.6): through AIM - removable when the options say so. Else
            // Windows' own, as before. Decided now, from what is installed now.
            if (ActiveVhdx() == "aim")
            {
                var viaAim = AttachImage(path, readOnly, out error, backend: "aim");
                if (viaAim != null) return viaAim;
                RamDiskLog.Warn("the VHDX did not attach through AIM (" + error + ") - Windows' own instead");
            }
            char letter = FreeDriveLetter();
            if (letter == '\0') { error = "no free drive letter"; return null; }
            var extra = new Dictionary<string, string>();
            if (readOnly) extra["readonly"] = "1";
            if (!Vhdx("vhdx-attach", path, out error, extra, letter)) return null;
            var root = letter + ":\\";
            if (!WaitFor(() => Directory.Exists(root), MountSeconds, default(CancellationToken)))
            { error = "attached, but " + root + " did not appear"; return null; }
            RamDiskLog.Info("attached " + Path.GetFileName(path) + " as " + root + (readOnly ? " (read-only)" : ""));
            return root;
        }

        /// <summary>Detach a VHDX.</summary>
        public static bool DetachVhdx(string path, out string error)
        {
            // Attached through AIM - by this process or one before it - the helper knows where, from the file alone.
            var v = HelperVersion;
            if (v != null && v >= BackendProtocol && InstalledTaskName() != null)
            {
                var said = RunAndWait("image-detach", 'Z', path, new Dictionary<string, string>());
                if (said != null && said.StartsWith("OK image-detach", StringComparison.Ordinal)) { error = null; return true; }
            }
            return Vhdx("vhdx-detach", path, out error, null);
        }

        /// <summary>How a VHDX is attached now: "aim" or "windows", from the options and what is installed.</summary>
        public static string ActiveVhdx()
        {
            var v = HelperVersion;
            bool modern = v != null && v >= BackendProtocol && InstalledTaskName() != null;
            var e = RamDiskOptions.Load().Effective(modern && IsAimInstalled(), IsImDiskInstalled(), out _);
            return e.Vhdx == "aim" && modern ? "aim" : "windows";
        }

        /// <summary>One vhdx-* run, waited for - every one of them is followed by something that
        /// needs its result. Asked twice at most, for the run somebody else had going.</summary>
        private static bool Vhdx(string action, string path, out string error, IDictionary<string, string> extra, char drive = 'Z')
        {
            error = null;
            try
            {
                if (!CanUseVhdx)
                {
                    error = "the helper cannot do VHDX (it is " + (HelperVersion?.ToString() ?? "absent") + ", " + VhdxProtocol + " is needed)";
                    return false;
                }
                // FULLY QUALIFIED, as given. Resolving a relative one would create it wherever this
                // process happens to be running - measured: in the repository the probe ran from.
                if (string.IsNullOrWhiteSpace(path) || !OneLine(path) || !Path.IsPathFullyQualified(path))
                { error = "a VHDX path must be absolute and on one line: " + (path ?? "(none)").Replace("\n", "\\n"); return false; }
                var task = InstalledTaskName();
                string said = null;
                for (int attempt = 1; attempt <= 2; attempt++)
                {
                    if (!StartRun(task, action, drive, 0, default(CancellationToken), path, extra: extra)) { error = "the task could not be started (or a value was refused)"; return false; }
                    said = WaitForResult(default(CancellationToken));
                    _runInFlight = said == null;
                    if (said != null && IsOurs(said)) break;
                    said = null;
                }
                if (said == null) { error = "the helper never answered"; return false; }
                if (said.StartsWith("OK " + action, StringComparison.Ordinal)) return true;
                error = said;
                RamDiskLog.Warn(action + " " + Path.GetFileName(path) + ": " + said);
                return false;
            }
            catch (Exception ex) { error = ex.GetType().Name + ": " + ex.Message; return false; }
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

        /// <summary>Is there a RAM disk driver - Arsenal Image Mounter or ImDisk? (LiteBox asks for ImDisk
        /// alone, by its CLI; this pack takes either, AIM first - see RamDiskOptions.)</summary>
        public static bool IsDriverInstalled() => IsImDiskInstalled() || IsAimInstalled();

        /// <summary>True when the ImDisk CLI is present, which is how LiteBox decides the driver is installed.</summary>
        public static bool IsImDiskInstalled()
        {
            try { return File.Exists(ImDiskExe); } catch { return false; }
        }

        /// <summary>Arsenal Image Mounter: its driver (service phdskmnt) and the AIM Toolkit's aim_ll.exe.</summary>
        public static bool IsAimInstalled()
        {
            try
            {
                using (var k = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\phdskmnt"))
                    if (k == null) return false;
                return AimLowLevel != null;
            }
            catch { return false; }
        }

        /// <summary>The AIM Toolkit's folder - from its uninstall entry, else Program Files\AIM Toolkit - when it
        /// holds aim_ll.exe; null otherwise.</summary>
        public static string AimFolder
        {
            get
            {
                var dirs = new List<string>();
                try
                {
                    using (var k = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\AIM-tk"))
                        foreach (var name in new[] { "InstallLocation", "DisplayIcon", "UninstallString" })
                        {
                            var v = ((k?.GetValue(name) as string) ?? "").Trim();
                            if (v.StartsWith("\"")) { int end = v.IndexOf('"', 1); v = end > 0 ? v.Substring(1, end - 1) : v.Trim('"'); }
                            int exe = v.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
                            if (exe > 0) v = Path.GetDirectoryName(v.Substring(0, exe + 4));
                            if (!string.IsNullOrEmpty(v)) dirs.Add(v);
                        }
                }
                catch { }
                dirs.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "AIM Toolkit"));
                foreach (var d in dirs) { try { if (File.Exists(Path.Combine(d, "aim_ll.exe"))) return d; } catch { } }
                return null;
            }
        }

        public static string AimLowLevel { get { var d = AimFolder; return d == null ? null : Path.Combine(d, "aim_ll.exe"); } }

        /// <summary>Which driver a mount goes through now: "aim", "imdisk" or null - for messages.</summary>
        public static string ActiveBackend()
        {
            var v = HelperVersion;
            bool modern = v != null && v >= BackendProtocol;
            if (!modern) return IsImDiskInstalled() ? "imdisk" : null;
            return RamDiskOptions.Load().Effective(IsAimInstalled(), IsImDiskInstalled(), out _).Backend;
        }

        /// <summary>The helper version that knows backend=, mount=, compress=, type=awe without an image and the
        /// image-* actions - Arsenal Image Mounter beside ImDisk. AN OLDER ONE IGNORES THE KEYS and
        /// reads the image actions as a mount.</summary>
        public static readonly Version BackendProtocol = new Version(1, 6, 0, 0);

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
        /// Our build rolls forward across majors, so anything from 9 up will do.
        ///
        /// SINCE 1.9.1 THE HELPER CARRIES ITS OWN (self-contained, one file): then there is nothing to look for.</summary>
        public static bool RuntimeReady(out string why)
        {
            why = null;
            if (HelperSelfContained) return true;
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

        /// <summary>Take the elevated task away (the pack's uninstall, when no LiteBox shares it - Mehdi, 04/10). One UAC
        /// prompt: a HIGHEST task can only be deleted elevated. True when it is gone, or was never there.</summary>
        public static bool RemoveTask()
        {
            try
            {
                var name = InstalledTaskName();
                if (name == null) return true;
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
                    RamDiskLog.Info("deleting the task " + name + " exited with " + p.ExitCode);
                }
                return InstalledTaskName() == null;
            }
            catch (Exception ex) { RamDiskLog.Warn("could not delete the task", ex); return false; }
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
        /// cleanup can unmount it without being told where it went. Returns the root - "R:\", or a folder
        /// when the options say so - or null.</summary>
        public static string MountFor(string key, int sizeMb, CancellationToken ct = default(CancellationToken))
        {
            var root = Mount(sizeMb, ct, key);
            if (!string.IsNullOrEmpty(root) && !string.IsNullOrEmpty(key)) _active[key] = root;
            return root;
        }

        /// <summary>Mount an NTFS RAM drive of <paramref name="sizeMb"/> MB, the way RamDiskOptions says:
        /// through Arsenal Image Mounter or ImDisk, on a letter or in a folder, removable, in
        /// virtual or physical memory. Uses the elevated task when one is registered, else a direct call -
        /// which only works if we are already elevated. Returns the root or null on any failure.</summary>
        public static string Mount(int sizeMb, CancellationToken ct = default(CancellationToken), string key = null)
        {
            try
            {
                if (sizeMb <= 0) return null;
                if (!IsDriverInstalled()) { RamDiskLog.Info("no RAM disk driver (neither AIM nor ImDisk) - not mounting"); return null; }
                var v = HelperVersion;
                bool modern = v != null && v >= BackendProtocol;
                // What is installed NOW decides, not what was installed when the options were saved.
                var o = RamDiskOptions.Load().Effective(modern && IsAimInstalled(), IsImDiskInstalled(), out var notes);
                if (notes.Length > 0) RamDiskLog.Warn("RAM disk options: " + notes);
                if (o.Backend == null) { RamDiskLog.Info("no usable RAM disk driver - not mounting"); return null; }

                // REFUSED RATHER THAN DONE OTHERWISE: a helper older than 1.6 reads none of these keys and
                // would mount a fixed letter in virtual memory through ImDisk - not what was asked.
                if (!modern && o.NeedsProtocol16)
                {
                    RamDiskLog.Warn("the RAM disk options ask for " + "AWE "
                                    + "and the helper is " + (v?.ToString() ?? "absent") + " - " + BackendProtocol + " is needed. Run the installer again.");
                    return null;
                }
                if (!modern && !IsImDiskInstalled())
                {
                    RamDiskLog.Warn("only Arsenal Image Mounter is installed, and the helper " + (v?.ToString() ?? "(absent)") + " only knows ImDisk - "
                                    + BackendProtocol + " is needed. Run the installer again.");
                    return null;
                }

                int free = GetFreeRamMb();
                if (free > 0 && sizeMb >= free)
                {
                    RamDiskLog.Info("asked for " + sizeMb + " MB with " + free + " MB free - not mounting");
                    return null;
                }

                // Always a drive letter (Mehdi, 02/10: a folder mount was of anecdotal use - the helper keeps mount=).
                string folder = null;
                char letter = FreeDriveLetter();
                if (letter == '\0') { RamDiskLog.Info("no free drive letter"); return null; }
                string root = letter + ":\\";

                var extra = new Dictionary<string, string>();
                if (modern) extra["backend"] = o.Backend;
                // memory=auto: AWE through AIM, virtual memory through ImDisk - decided by the driver that will be used.
                bool viaAim = o.Backend == "aim";
                bool awe = o.AweFor(viaAim);
                string type = awe ? "awe" : null;
                string what = sizeMb + " MB, " + (awe ? "physical memory" : "virtual memory")
                              + (o.Removable ? ", removable" : "") + ", backend " + o.Backend;

                var task = InstalledTaskName();
                if (task != null)
                {
                    // TWICE AT MOST, for the one way a good request comes to nothing: another run -
                    // LiteBox's, another process of ours - was still going, and the task (IgnoreNew)
                    // dropped ours without a word. Measured between two processes launched back to
                    // back. That run's answer then lands in ramdisk.result WITHOUT our id: watched for
                    // while waiting for the drive, so the second asking comes as soon as the other run
                    // is over rather than after the full ceiling.
                    string said = null;
                    for (int attempt = 1; attempt <= 2; attempt++)
                    {
                        if (!StartRun(task, "mount", letter, sizeMb, ct, type: type, extra: extra, removable: o.Removable)) return null;

                        // The drive, not the helper - measured at a third of a second, against
                        // seconds or minutes for the helper to finish afterwards. See the header for
                        // why the waiting happens before the NEXT run instead.
                        bool foreign = false;
                        WaitFor(() => IsMountedAt(root) || (foreign = ForeignAnswer()), MountSeconds, ct);
                        if (IsMountedAt(root))
                        {
                            RamDiskLog.Info("mounted " + root + " (" + what + ") through the elevated task"
                                            + (attempt > 1 ? ", second asking" : ""));
                            return root;
                        }
                        if (foreign)
                        {
                            RamDiskLog.Info("the task ignored the mount: another run was going (" + ReadResult() + ")"
                                            + (attempt == 1 ? " - asking again" : ""));
                            _runInFlight = false;
                            continue;
                        }

                        // No drive and no other run: now the helper's own words are worth waiting
                        // for, because something went wrong and that file is where it says what.
                        RamDiskLog.Warn("no drive appeared within " + MountSeconds + "s - waiting for the helper to say why");
                        said = WaitForResult(ct);
                        _runInFlight = said == null;
                        if (said != null && !IsOurs(said)) continue;   // the other run, late
                        break;
                    }
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

                // Elevated already and no task: the driver directly - the same arguments the helper uses.
                bool aim = o.Backend == "aim" || (o.Backend == "auto" && IsAimInstalled());
                var exe = aim ? AimLowLevel : ImDiskExe;
                if (exe == null || !File.Exists(exe)) { RamDiskLog.Warn("the chosen driver's tool is not there"); return null; }
                var args = new List<string> { "-a", "-t", awe ? "file" : "vm", "-s", sizeMb + "M" };
                var opts = new List<string>();
                if (awe) opts.Add("awe");
                if (o.Removable) opts.Add("rem");
                if (opts.Count > 0) { args.Add("-o"); args.Add(string.Join(",", opts)); }
                args.Add("-m"); args.Add(folder != null ? folder.TrimEnd('\\') : letter + ":");
                args.Add("-p"); args.Add("/fs:ntfs /q /y");
                int exit = RunQuiet(exe, args.ToArray());
                if (exit == 0 && WaitFor(() => IsMountedAt(root), 10, ct))
                {
                    RamDiskLog.Info("mounted " + root + " (" + what + ") directly");
                    return root;
                }
                RamDiskLog.Warn(Path.GetFileName(exe) + " exited with " + exit + " and " + root + " is not there");
                return null;
            }
            catch (Exception ex) { RamDiskLog.Warn("mount threw", ex); return null; }
        }

        /// <summary>Unmount a RAM disk by its root ("R:\" or a folder). True when it is gone afterwards.</summary>
        public static bool Unmount(string driveRoot, CancellationToken ct = default(CancellationToken))
        {
            try
            {
                if (string.IsNullOrEmpty(driveRoot)) return false;
                bool isLetter = driveRoot.Length <= 3 && driveRoot.Length >= 2 && driveRoot[1] == ':';
                char letter = isLetter ? driveRoot[0] : 'Z';
                Func<bool> gone = () => !IsMountedAt(driveRoot);
                if (gone()) { TidyFolder(driveRoot); return true; }

                // CLEANLY FIRST, when the helper can (1.4, and 1.6 for AIM or a folder): locked, dismounted,
                // removed - nothing left behind. The unelevated removal below leaves an ImDisk device "removed"
                // until Windows restarts (measured 28/09, one per session), so it is what a helper too old, busy
                // or absent gets.
                if (CanDismountCleanly && IsRamDisk(driveRoot) && Dismount(driveRoot, ct)) { TidyFolder(driveRoot); return true; }

                if (isLetter && DropDirect(driveRoot)) return true;

                var task = InstalledTaskName();
                if (task != null)
                {
                    if (!isLetter && (HelperVersion == null || HelperVersion < BackendProtocol)) return false;
                    var extra = isLetter ? null : new Dictionary<string, string> { { "mount", driveRoot.TrimEnd('\\') } };
                    if (!StartRun(task, "umount", letter, 0, ct, extra: extra)) return false;

                    bool ok = WaitFor(gone, UnmountSeconds, ct);
                    RamDiskLog.Info("unmounted " + driveRoot + " through the task (gone=" + ok + ")");
                    if (!ok)
                        RamDiskLog.Warn(driveRoot + " is still there after " + UnmountSeconds
                                        + "s - the helper will say why in ramdisk.result when it ends");
                    else TidyFolder(driveRoot);
                    return ok;
                }

                if (!isLetter || !File.Exists(ImDiskExe)) return false;
                int exit = RunQuiet(ImDiskExe, new[] { "-D", "-m", letter + ":" });
                return exit == 0 && gone();
            }
            catch (Exception ex) { RamDiskLog.Warn("unmount threw", ex); return false; }
        }

        /// <summary>A folder mount point left empty is taken away with its drive.</summary>
        private static void TidyFolder(string root)
        {
            try
            {
                if (root.Length <= 3) return;
                if (Directory.Exists(root) && !IsMountedAt(root) && !Directory.EnumerateFileSystemEntries(root).Any()) Directory.Delete(root);
            }
            catch { }
        }

        /// <summary>Ask the helper for action=dismount, waiting only so long for it to be free. True when
        /// the drive is gone and the helper says the volume was dismounted before the removal.</summary>
        private static bool Dismount(string driveRoot, CancellationToken ct)
        {
            try
            {
                var task = InstalledTaskName();
                if (task == null) return false;
                bool isLetter = driveRoot.Length <= 3 && driveRoot[1] == ':';
                if (!isLetter && (HelperVersion == null || HelperVersion < BackendProtocol)) return false;
                var extra = isLetter ? null : new Dictionary<string, string> { { "mount", driveRoot.TrimEnd('\\') } };
                if (!StartRun(task, "dismount", isLetter ? driveRoot[0] : 'Z', 0, ct, extra: extra, previousSeconds: DismountWaitSeconds)) return false;
                var said = WaitForResult(ct, DismountWaitSeconds);
                _runInFlight = said == null;
                bool gone = WaitFor(() => !IsMountedAt(driveRoot), 5, ct);
                if (said == null || !IsOurs(said))
                {
                    RamDiskLog.Info("the helper did not answer this dismount (" + (said ?? "nothing") + ") - "
                                    + (gone ? "the drive is gone all the same" : "removing it directly"));
                    return gone;
                }
                RamDiskLog.Info("unmounted " + driveRoot + " cleanly through the helper - " + said);
                if (said.Contains("dismounted=0"))
                    RamDiskLog.Warn("the volume could not be dismounted - the device may be left behind as \"removed\" until a restart");
                return gone;
            }
            catch (Exception ex) { RamDiskLog.Warn("the clean dismount threw", ex); return false; }
        }

        // ── the direct unmount ───────────────────────────────────────────────
        //
        // WHAT THE 88 SECONDS WERE. imdisk.exe (and the helper through it) announces a drive coming
        // and going by broadcasting to EVERY top-level window and waiting on each one - and a single
        // hung window anywhere on the machine costs about 29 s per broadcast. Measured with a stuck
        // installer holding seven of them: 117 s from asking to gone; without it, 5.3 s, of which
        // most is the wait for the mount's own helper run. The driver itself sends nothing.
        //
        // So an unmount does not need the helper at all. imdisk.cpl's own calls do it unelevated:
        // open the device with NO data access (GENERIC_READ is refused to a standard user, error 5,
        // measured), force-remove it, drop the letter WITHOUT a broadcast, and tell the shell alone,
        // without waiting. 6 ms, measured with and without hung windows.
        //
        // ONLY FOR A MEMORY-BACKED DRIVE, read from the driver. A forced removal does not flush the
        // volume's cache: on a vm drive that loses nothing, because nothing outlives it anyway; on a
        // drive backed by an image file it would leave the image corrupt. Anything that is not
        // plainly ours and plainly in memory goes to the task, as before.
        //
        // ONLY WHAT IS OURS. The letter must point at \Device\ImDisk<n>, the device the driver
        // reports must be that n, and the letter is dropped only if it still points there.
        //
        // What it does not do: tell applications other than the shell. Anything else that lists
        // drives sees a stale letter until it next looks. Nothing breaks.

        /// <summary>Try the direct unmount. True when the drive is gone; false leaves it for the
        /// task, with nothing done or with only the letter left behind.</summary>
        private static bool DropDirect(string driveRoot)
        {
            try
            {
                if (driveRoot.Length < 2 || driveRoot[1] != ':') return false;
                var letter = char.ToUpperInvariant(driveRoot[0]) + ":";
                var target = DosTarget(letter);
                if (!ImDiskNumber(target, out var number))
                {
                    RamDiskLog.Info(letter + " is " + (target ?? "nothing") + ", not an ImDisk drive - not removing it directly");
                    return false;
                }

                // By number, the same device the letter was checked to point at.
                var device = ImDiskOpenDeviceByNumber(number, 0);
                if (device == IntPtr.Zero || device == new IntPtr(-1))
                {
                    RamDiskLog.Info("could not open " + letter + " directly (error " + Marshal.GetLastWin32Error() + ") - asking the task");
                    return false;
                }
                try
                {
                    var data = new byte[1024];
                    if (!ImDiskQueryDevice(number, data, (uint)data.Length))
                    {
                        RamDiskLog.Info("could not query " + letter + " (error " + Marshal.GetLastWin32Error() + ") - asking the task");
                        return false;
                    }
                    // IMDISK_CREATE_DATA: DeviceNumber at 0, Flags at 40 (after the 8-aligned
                    // DISK_GEOMETRY and ImageOffset). The number is the check that the layout is right.
                    uint reported = BitConverter.ToUInt32(data, 0);
                    uint flags = BitConverter.ToUInt32(data, 40);
                    if (reported != number || (flags & ImDiskTypeMask) != ImDiskTypeVm)
                    {
                        RamDiskLog.Info(letter + " is device " + reported + " with flags 0x" + flags.ToString("x")
                                        + " - not a memory drive of ours, asking the task");
                        return false;
                    }
                    if (!ImDiskForceRemoveDevice(device, 0))
                    {
                        RamDiskLog.Info("direct removal of " + letter + " failed (error " + Marshal.GetLastWin32Error() + ") - asking the task");
                        return false;
                    }
                }
                finally { CloseHandle(device); }

                // The device is gone; now its letter. Removed by number, the driver takes the letter
                // with it (measured: nothing left to query), so usually there is nothing to do. When
                // it is still there and still ours, it goes quietly; exact-match removal answers "not
                // found" even with the right target (measured), so the newest definition is removed,
                // having just checked it is ours. A letter that now points elsewhere is not ours.
                var now = DosTarget(letter);
                bool unmapped = now == null;
                if (!unmapped && string.Equals(now, target, StringComparison.OrdinalIgnoreCase))
                {
                    unmapped = DefineDosDevice(DDD_REMOVE_DEFINITION | DDD_NO_BROADCAST_SYSTEM, letter, null);
                    if (!unmapped)
                    {
                        // The slow way, which broadcasts - but it only runs when the quiet one failed.
                        RamDiskLog.Warn("could not drop " + letter + " quietly (error " + Marshal.GetLastWin32Error() + ") - through imdisk.cpl instead");
                        unmapped = ImDiskRemoveMountPoint(letter + "\\");
                    }
                }
                SHChangeNotify(SHCNE_DRIVEREMOVED, SHCNF_PATHW | SHCNF_FLUSHNOWAIT, letter + "\\", IntPtr.Zero);

                bool gone = !Directory.Exists(driveRoot);
                RamDiskLog.Info("unmounted " + driveRoot + " directly (gone=" + gone + ", letter dropped=" + unmapped + ")");
                return gone;
            }
            catch (Exception ex)
            {
                // imdisk.cpl missing, an export renamed: the task still knows how.
                RamDiskLog.Warn("the direct unmount could not run - asking the task", ex);
                return false;
            }
        }

        /// <summary>Is this root one of our RAM disks' kind - an ImDisk drive or an Arsenal Image Mounter disk,
        /// on a letter or in a folder? What a caller checks before unmounting a root it only knows from a file,
        /// since after a reboot that letter may be anything. (Kept under its old name: every caller asks it.)</summary>
        public static bool IsImDiskDrive(string driveRoot) => IsRamDisk(driveRoot);

        public static bool IsRamDisk(string root)
        {
            try
            {
                if (string.IsNullOrEmpty(root) || root.Length < 2) return false;
                var device = DeviceOf(root);
                if (device == null) return false;
                if (ImDiskNumber(device, out _)) return true;
                return IsAimVolume(root);
            }
            catch { return false; }
        }

        /// <summary>Is something mounted at this root? A letter: it exists. A folder: it is a volume mount
        /// point of its own - the folder itself exists before and after.</summary>
        public static bool IsMountedAt(string root)
        {
            try
            {
                if (string.IsNullOrEmpty(root)) return false;
                if (root.Length <= 3 && root.Length >= 2 && root[1] == ':') return Directory.Exists(root.Substring(0, 2) + "\\");
                if (JunctionDevice(root) != null) return true;          // ImDisk in a folder: a junction to its device
                var here = VolumeOf(root);
                return here != null && !string.Equals(here, VolumeOf(Path.GetPathRoot(root)), StringComparison.OrdinalIgnoreCase);
            }
            catch { return false; }
        }

        /// <summary>The root of the volume holding a path: "R:\" or the folder a RAM disk is mounted in -
        /// what Path.GetPathRoot gives only for a letter.</summary>
        public static string MountRootOf(string path)
        {
            try
            {
                if (string.IsNullOrEmpty(path)) return null;
                // An ImDisk folder mount is a junction, which GetVolumePathName walks THROUGH (to
                // \\?\GLOBALROOT\Device\ImDisk3\): the nearest folder that is one is the root.
                for (var dir = Path.GetFullPath(path).TrimEnd('\\'); !string.IsNullOrEmpty(dir) && dir.Length > 3; dir = Path.GetDirectoryName(dir))
                    if (JunctionDevice(dir) != null) return dir + "\\";
                var buffer = new StringBuilder(1024);
                return GetVolumePathName(Path.GetFullPath(path), buffer, (uint)buffer.Capacity) ? buffer.ToString() : Path.GetPathRoot(path);
            }
            catch { return Path.GetPathRoot(path ?? ""); }
        }

        private static string VolumeOf(string root)
        {
            var name = new StringBuilder(64);
            return GetVolumeNameForVolumeMountPoint(root.TrimEnd('\\') + "\\", name, (uint)name.Capacity) ? name.ToString() : null;
        }

        /// <summary>The device behind a root: \Device\ImDisk3, \Device\HarddiskVolume12...</summary>
        private static string DeviceOf(string root)
        {
            if (root.Length <= 3 && root[1] == ':') return DosTarget(char.ToUpperInvariant(root[0]) + ":");
            var junction = JunctionDevice(root);
            if (junction != null) return junction;
            var volume = VolumeOf(root);
            return volume == null ? null : DosTarget(volume.Substring(4).TrimEnd('\\'));
        }

        /// <summary>IMDISK IN A FOLDER IS NOT A VOLUME MOUNT POINT - measured 02/10: ImDisk is not known to the
        /// mount manager, so "imdisk -m C:\x" makes a mount-point reparse point (a junction) to \Device\ImDisk3\.
        /// Read from the reparse data itself - .NET's LinkTarget answers null for it. Its device, or null.</summary>
        private static string JunctionDevice(string folder)
        {
            IntPtr h = CreateFileW(folder.TrimEnd('\\'), 0x80 /* FILE_READ_ATTRIBUTES */, 7, IntPtr.Zero, 3,
                                  0x02000000 | 0x00200000 /* BACKUP_SEMANTICS | OPEN_REPARSE_POINT */, IntPtr.Zero);
            if (h == new IntPtr(-1)) return null;
            try
            {
                var buffer = new byte[16384];
                if (!DeviceIoControlBytes(h, 0x000900A8 /* FSCTL_GET_REPARSE_POINT */, null, 0, buffer, (uint)buffer.Length, out _, IntPtr.Zero)) return null;
                if (BitConverter.ToUInt32(buffer, 0) != 0xA0000003) return null;          // IO_REPARSE_TAG_MOUNT_POINT
                int offset = BitConverter.ToUInt16(buffer, 8), length = BitConverter.ToUInt16(buffer, 10);
                var target = System.Text.Encoding.Unicode.GetString(buffer, 16 + offset, length);
                return target.StartsWith(@"\Device\ImDisk", StringComparison.OrdinalIgnoreCase) ? target.TrimEnd('\\') : null;
            }
            catch { return null; }
            finally { CloseHandle(h); }
        }

        [DllImport("kernel32.dll", SetLastError = true, EntryPoint = "DeviceIoControl")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool DeviceIoControlBytes(IntPtr device, uint code, byte[] inBuf, uint inSize, byte[] outBuf, uint outSize, out uint returned, IntPtr overlapped);

        /// <summary>Does the disk under this volume say it is Arsenal Image Mounter's? Asked of the storage stack
        /// (IOCTL_STORAGE_QUERY_PROPERTY, vendor or product "Arsenal"), which answers without elevation - aim_ll
        /// does not.</summary>
        private static bool IsAimVolume(string root)
        {
            var volume = VolumeOf(root);
            if (volume == null) return false;
            IntPtr h = CreateFileW(volume.TrimEnd('\\'), 0, 3, IntPtr.Zero, 3, 0, IntPtr.Zero);
            if (h == new IntPtr(-1)) return false;
            try
            {
                var query = new byte[12];                      // StorageDeviceProperty, PropertyStandardQuery
                var output = new byte[1024];
                if (!DeviceIoControl(h, 0x002D1400, query, (uint)query.Length, output, (uint)output.Length, out _, IntPtr.Zero)) return false;
                int vendor = BitConverter.ToInt32(output, 12), product = BitConverter.ToInt32(output, 16);
                string Read(int at)
                {
                    if (at <= 0 || at >= output.Length) return "";
                    int end = at;
                    while (end < output.Length && output[end] != 0) end++;
                    return Encoding.ASCII.GetString(output, at, end - at).Trim();
                }
                return Read(vendor).StartsWith("Arsenal", StringComparison.OrdinalIgnoreCase)
                       || Read(product).StartsWith("Arsenal", StringComparison.OrdinalIgnoreCase);
            }
            finally { CloseHandle(h); }
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetVolumeNameForVolumeMountPoint(string mountPoint, StringBuilder volumeName, uint length);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetVolumePathName(string fileName, StringBuilder volumePath, uint length);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr CreateFileW(string name, uint access, uint share, IntPtr sa, uint disposition, uint flags, IntPtr template);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool DeviceIoControl(IntPtr device, uint code, byte[] inBuf, uint inSize, byte[] outBuf, uint outSize, out uint returned, IntPtr overlapped);

        // ── images (helper 1.6) ──────────────────────────────────────────────

        /// <summary>Attach a disk image - ISO, raw, VHD, VHDX (a differencing one with its chain), VMDK... - on a
        /// free letter or in <paramref name="folder"/>. Through AIM when it is there, else Windows' own support
        /// (VHD, VHDX, ISO) or ImDisk (raw). <paramref name="overlay"/>: writes go to that file instead of the
        /// image (AIM: any format but ISO; Windows: a differencing .vhdx), deleted at the detach when
        /// <paramref name="deleteOverlay"/>. Returns the root, or null with the reason in <paramref name="error"/>.</summary>
        public static string AttachImage(string image, bool readOnly, out string error, string overlay = null, bool deleteOverlay = false, string folder = null, bool? removable = null, string backend = null)
        {
            error = null;
            try
            {
                var v = HelperVersion;
                if (v == null || v < BackendProtocol || InstalledTaskName() == null)
                { error = "the helper cannot attach images (it is " + (v?.ToString() ?? "absent") + ", " + BackendProtocol + " and its task are needed)"; return null; }
                if (string.IsNullOrWhiteSpace(image) || !OneLine(image) || !Path.IsPathFullyQualified(image)) { error = "an image path must be absolute and on one line"; return null; }
                var options = RamDiskOptions.Load().Effective(IsAimInstalled(), IsImDiskInstalled(), out var notes);
                if (notes.Length > 0) RamDiskLog.Warn("image options: " + notes);
                // A VHD/VHDX goes the way vhdx= says (Windows' own or AIM); anything else through AIM when it is
                // there (ISO, raw, VMDK...), Windows/ImDisk otherwise.
                var ext = Path.GetExtension(image).ToLowerInvariant();
                bool virtualDisk = ext == ".vhd" || ext == ".vhdx" || ext == ".avhdx";
                if (backend == "aim" && !IsAimInstalled()) backend = null;
                string how = backend ?? (virtualDisk ? options.Vhdx : options.Backend == "aim" ? "aim" : "windows");
                if (folder != null && how != "aim") { error = "a folder mount is AIM only"; return null; }
                var extra = new Dictionary<string, string> { { "backend", how } };
                // Removable as the RAM disks are, when the options say so: through AIM only (Windows' attach has no
                // such choice; an older or ImDisk path ignores the key).
                if (removable ?? options.Removable) extra["removable"] = "1";
                char letter = 'Z';
                string root;
                if (folder != null)
                {
                    if (!OneLine(folder) || !Path.IsPathFullyQualified(folder)) { error = "the folder must be absolute"; return null; }
                    extra["mount"] = folder.TrimEnd('\\');
                    root = folder.TrimEnd('\\') + "\\";
                }
                else
                {
                    letter = FreeDriveLetter();
                    if (letter == '\0') { error = "no free drive letter"; return null; }
                    root = letter + ":\\";
                }
                if (readOnly) extra["readonly"] = "1";
                if (!string.IsNullOrEmpty(overlay))
                {
                    if (!OneLine(overlay) || !Path.IsPathFullyQualified(overlay)) { error = "the overlay path must be absolute"; return null; }
                    extra["overlay"] = overlay;
                    if (deleteOverlay) extra["autodelete"] = "1";
                }
                var said = RunAndWait("image-attach", letter, image, extra);
                if (said == null) { error = "the helper never answered"; return null; }
                if (!said.StartsWith("OK image-attach", StringComparison.Ordinal)) { error = said; RamDiskLog.Warn("image-attach " + Path.GetFileName(image) + ": " + said); return null; }
                RamDiskLog.Info("attached " + Path.GetFileName(image) + " at " + root + (readOnly ? " (read-only)" : "") + (overlay != null ? " over " + overlay : "") + " - " + said);
                return root;
            }
            catch (Exception ex) { error = ex.GetType().Name + ": " + ex.Message; return null; }
        }

        /// <summary>The helper that serves an Xbox disc as a FAT32 disk (view=xbox).</summary>
        public static readonly Version XboxViewProtocol = new Version(1, 7, 0, 0);

        /// <summary>The helper that serves a ZArchive (.zar) the same way (view=xbox).</summary>
        public static readonly Version ZarViewProtocol = new Version(1, 8, 0, 0);

        /// <summary>The helper that serves a compressed image the same way (view=xbox): CSO, CCI, CHD.</summary>
        public static readonly Version CompressedViewProtocol = new Version(1, 9, 0, 0);

        /// <summary>Can an Xbox disc be attached where it is: AIM there, the helper 1.7 and its task - 1.8 for a ZArchive,
        /// 1.9 for a CSO, a CCI or a CHD. <paramref name="image"/>: the image (null: a plain one).</summary>
        public static bool CanAttachXboxDisc(out string why, string image = null)
        {
            var v = HelperVersion;
            var needed = image == null ? XboxViewProtocol : IsCompressedImage(image) ? CompressedViewProtocol : IsZar(image) ? ZarViewProtocol : XboxViewProtocol;
            why = !IsAimInstalled() ? "the Arsenal Image Mounter is not installed"
                : v == null || v < needed ? "the RAM disk helper is " + (v?.ToString() ?? "absent") + ", " + needed + " is needed"
                : InstalledTaskName() == null ? "the RAM disk helper's task is not installed"
                : null;
            return why == null;
        }

        /// <summary>An Xbox disc image (redump ISO, XISO) attached as a disk on a free letter, its game read where it
        /// is - the helper's view=xbox, through AIM. Returns the root ("K:\"), or null with the reason. Detached by
        /// DetachImage.</summary>
        public static string AttachXboxDisc(string image, out string error)
        {
            error = null;
            try
            {
                if (string.IsNullOrWhiteSpace(image) || !OneLine(image) || !Path.IsPathFullyQualified(image)) { error = "an image path must be absolute and on one line"; return null; }
                if (!CanAttachXboxDisc(out error, image)) return null;
                char letter = FreeDriveLetter();
                if (letter == '\0') { error = "no free drive letter"; return null; }
                var said = RunAndWait("image-attach", letter, image, new Dictionary<string, string> { { "view", "xbox" }, { "backend", "aim" } });
                if (said == null) { error = "the helper never answered"; return null; }
                if (!said.StartsWith("OK image-attach", StringComparison.Ordinal)) { error = said; RamDiskLog.Warn("xbox attach " + Path.GetFileName(image) + ": " + said); return null; }
                RamDiskLog.Info("attached the Xbox disc " + Path.GetFileName(image) + " at " + letter + ":\\ - " + said);
                return letter + ":\\";
            }
            catch (Exception ex) { error = ex.GetType().Name + ": " + ex.Message; return null; }
        }

        /// <summary>A CSO ("CISO"), a CCI ("CCIM") or a CHD ("MComprHD"), by its first bytes.</summary>
        private static bool IsCompressedImage(string path)
        {
            try
            {
                using var f = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                var head = new byte[8];
                if (f.Read(head, 0, 8) != 8) return false;
                var text = System.Text.Encoding.ASCII.GetString(head);
                return text.StartsWith("CISO", StringComparison.Ordinal) || text.StartsWith("CCIM", StringComparison.Ordinal) || text == "MComprHD";
            }
            catch { return false; }
        }

        /// <summary>A ZArchive, by the magic its footer ends with (0x61BF3A01 0x169F52D6, big-endian) - read here and not
        /// through src\Shared.Zar, which only the plugins with zstd carry.</summary>
        private static bool IsZar(string path)
        {
            try
            {
                using var f = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                if (f.Length < 0x90) return false;
                var tail = new byte[8];
                f.Seek(-8, SeekOrigin.End);
                return f.Read(tail, 0, 8) == 8 && tail[0] == 0x61 && tail[1] == 0xBF && tail[2] == 0x3A && tail[3] == 0x01
                       && tail[4] == 0x16 && tail[5] == 0x9F && tail[6] == 0x52 && tail[7] == 0xD6;
            }
            catch { return false; }
        }

        /// <summary>Detach what AttachImage attached, by its root.</summary>
        public static bool DetachImage(string root, out string error)
        {
            error = null;
            try
            {
                if (string.IsNullOrEmpty(root)) { error = "no root"; return false; }
                bool isLetter = root.Length <= 3 && root[1] == ':';
                var extra = new Dictionary<string, string>();
                if (!isLetter) extra["mount"] = root.TrimEnd('\\');
                var said = RunAndWait("image-detach", isLetter ? root[0] : 'Z', null, extra);
                if (said != null && said.StartsWith("OK image-detach", StringComparison.Ordinal)) { TidyFolder(root); return true; }
                error = said ?? "the helper never answered";
                return false;
            }
            catch (Exception ex) { error = ex.GetType().Name + ": " + ex.Message; return false; }
        }

        /// <summary>One run waited for, asked twice at most for the run somebody else had going.</summary>
        private static string RunAndWait(string action, char drive, string image, IDictionary<string, string> extra)
        {
            var task = InstalledTaskName();
            if (task == null) return null;
            for (int attempt = 1; attempt <= 2; attempt++)
            {
                if (!StartRun(task, action, drive, 0, default(CancellationToken), image, extra: extra)) return null;
                var said = WaitForResult(default(CancellationToken));
                _runInFlight = said == null;
                if (said != null && IsOurs(said)) return said;
            }
            return null;
        }

        private static string DosTarget(string letter)
        {
            var buffer = new StringBuilder(1024);
            return QueryDosDevice(letter, buffer, buffer.Capacity) == 0 ? null : buffer.ToString();
        }

        private static bool ImDiskNumber(string target, out uint number)
        {
            number = 0;
            const string prefix = @"\Device\ImDisk";
            return target != null && target.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                   && uint.TryParse(target.Substring(prefix.Length), out number);
        }

        private const uint ImDiskTypeMask = 0xF00, ImDiskTypeVm = 0x200;
        private const uint DDD_REMOVE_DEFINITION = 0x2, DDD_NO_BROADCAST_SYSTEM = 0x8;
        private const int SHCNE_DRIVEREMOVED = 0x80;
        private const uint SHCNF_PATHW = 0x5, SHCNF_FLUSHNOWAIT = 0x3000;

        [DllImport("imdisk.cpl", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr ImDiskOpenDeviceByNumber(uint deviceNumber, uint accessMode);

        [DllImport("imdisk.cpl", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool ImDiskQueryDevice(uint deviceNumber, byte[] createData, uint createDataSize);   // a NUMBER, not a handle (inc\imdisk.h)

        [DllImport("imdisk.cpl", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool ImDiskForceRemoveDevice(IntPtr device, uint deviceNumber);

        [DllImport("imdisk.cpl", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool ImDiskRemoveMountPoint(string mountPoint);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern uint QueryDosDevice(string deviceName, StringBuilder targetPath, int max);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool DefineDosDevice(uint flags, string deviceName, string targetPath);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CloseHandle(IntPtr handle);

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern void SHChangeNotify(int eventId, uint flags, string item1, IntPtr item2);

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
                var hv = HelperVersion;
                var imageExtra = hv != null && hv >= BackendProtocol ? new Dictionary<string, string> { { "backend", RamDiskOptions.Load().Backend } } : null;
                if (!StartRun(task, "mount", letter, sizeMbIfNew, ct, imagePath, TypeOf(mode), sparse, imageExtra))
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

                // ABSENT OR OLDER THAN THE ONE BUNDLED - the rule both products follow. It used to be
                // "older than ImageProtocol", which froze a 1.3 in place once 1.4 existed.
                var installed = HelperVersion;
                var bundled = BundledVersion(fileByName);
                if (installed != null && (bundled == null || installed >= bundled))
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
                // A SELF-CONTAINED ONE (1.9.1) OVER A FRAMEWORK-DEPENDENT ONE: the three files the old one needed beside it go,
                // or RuntimeReady would read the folder as framework-dependent still. Only when this build carries none of them.
                if (fileByName("RamDiskHelper.runtimeconfig.json") == null)
                    foreach (var stale in FrameworkFiles)
                        try { var p = Path.Combine(dir, stale); if (File.Exists(p)) File.Delete(p); } catch { }
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

        /// <summary>The version of the helper this build carries, read off its bytes. Null when it
        /// carries none or it cannot be read - and then an installed helper is left alone.</summary>
        public static Version BundledVersion(Func<string, byte[]> fileByName)
        {
            string temp = null;
            try
            {
                var bytes = fileByName("RamDiskHelper.dll") ?? fileByName("RamDiskHelper.exe");
                if (bytes == null) return null;
                temp = Path.Combine(Path.GetTempPath(), "lbip-helper-" + Guid.NewGuid().ToString("N") + ".dll");
                File.WriteAllBytes(temp, bytes);
                var v = FileVersionInfo.GetVersionInfo(temp);
                return v?.FileVersion == null ? null : new Version(v.FileMajorPart, v.FileMinorPart, v.FileBuildPart, v.FilePrivatePart);
            }
            catch { return null; }
            finally { try { if (temp != null) File.Delete(temp); } catch { } }
        }

        /// <summary>The helper's one file since 1.9.1: self-contained, single file, its .NET inside (Mehdi, 04/10).</summary>
        public static readonly string[] HelperFiles = { "RamDiskHelper.exe" };

        /// <summary>What a framework-dependent helper (up to 1.9.0, and LiteBox's own) needs beside its exe - their presence
        /// is what says the helper in place needs a .NET runtime on the machine.</summary>
        public static readonly string[] FrameworkFiles =
        {
            "RamDiskHelper.dll",
            "RamDiskHelper.deps.json",
            "RamDiskHelper.runtimeconfig.json",
        };

        /// <summary>Is the helper in place self-contained - its exe, and no runtimeconfig.json beside it?</summary>
        public static bool HelperSelfContained
        {
            get
            {
                try
                {
                    var exe = HelperExe;
                    return exe != null && File.Exists(exe) && !File.Exists(Path.Combine(Path.GetDirectoryName(exe), "RamDiskHelper.runtimeconfig.json"));
                }
                catch { return false; }
            }
        }

        // ── helpers ──────────────────────────────────────────────────────────

        /// <summary>The id of the run we started last (helper 1.2 echoes it in its answer).</summary>
        private static string _runId;

        /// <summary>Has an answer landed that is NOT ours - a run somebody else started, finishing? Only
        /// a 1.2 helper can say; with an older one nothing is ever foreign.</summary>
        private static bool ForeignAnswer()
        {
            var said = ReadResult();
            return said != "<nothing>" && said != "<unreadable>" && !string.IsNullOrWhiteSpace(said) && !IsOurs(said);
        }

        /// <summary>Is this answer the one to the run we started last? Only a 1.2 helper can say;
        /// with an older one every answer is taken as ours, which is how it always was.</summary>
        private static bool IsOurs(string said)
        {
            if (said == null) return false;
            var v = HelperVersion;
            if (v == null || v < CleanProtocol || string.IsNullOrEmpty(_runId)) return true;
            return said.EndsWith(" id=" + _runId, StringComparison.Ordinal);
        }

        /// <summary>Is this a value ramdisk.cfg can carry? The file is one key=value per LINE, read by
        /// an elevated helper: a value holding a line break would become a second key of the caller's
        /// choosing. Measured, the way it matters: a VHDX path with "\n..." in it was cut at the break,
        /// and the helper created the file named by the first half.</summary>
        private static bool OneLine(string value)
        {
            if (value == null) return true;
            foreach (var c in value) if (c < ' ') return false;
            return true;
        }

        private static bool WriteCfg(string action, char drive, int sizeMb,
                                    string image = null, string type = null, bool sparse = false,
                                    IDictionary<string, string> extra = null, bool removable = true)
        {
            if (!OneLine(action) || !OneLine(image) || !OneLine(type)
                || (extra != null && extra.Any(kv => !OneLine(kv.Key) || !OneLine(kv.Value) || kv.Key.Contains('='))))
            {
                RamDiskLog.Warn("refused to write ramdisk.cfg: a value holds a line break or a control character");
                return false;
            }
            try
            {
                var dir = HelperDir;
                if (dir == null) return false;
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

                // 1.5, on every mount: a removable drive, which Everything and Windows Search leave
                // alone - a fixed one they hold open, and its device outlives the unmount (measured
                // 30/09). Sent whatever the helper's version: an older one ignores it and mounts a
                // fixed drive, as it always did.
                if (action == "mount" && removable) cfg.Append("removable=1\r\n");

                // Keys of later protocols, written last: the helper keeps the LAST value of a key, so
                // an extra "label" replaces LiteBox's constant one above.
                if (extra != null)
                    foreach (var kv in extra) cfg.Append(kv.Key).Append('=').Append(kv.Value).Append("\r\n");

                // Harmless to an older helper, which reads only the keys it knows.
                _runId = Guid.NewGuid().ToString("N");
                cfg.Append("id=").Append(_runId).Append("\r\n");

                File.WriteAllText(CfgPath, cfg.ToString());
                return true;
            }
            catch (Exception ex) { RamDiskLog.Warn("could not write ramdisk.cfg", ex); return false; }
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
                                    bool sparse = false, IDictionary<string, string> extra = null,
                                    int previousSeconds = HelperSeconds, bool removable = true)
        {
            if (_runInFlight)
            {
                RamDiskLog.Info("waiting for the previous helper run to finish before asking again");
                var previous = WaitForResult(ct, previousSeconds);
                _runInFlight = false;
                if (previous == null)
                {
                    RamDiskLog.Warn("the previous run never reported within " + previousSeconds
                                    + "s - not asking for another, it would be refused");
                    return false;
                }
                RamDiskLog.Info("the previous run ended: " + previous);
            }

            if (!WriteCfg(action, drive, sizeMb, image, type, sparse, extra, removable)) return false;
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
        private static string WaitForResult(CancellationToken ct) => WaitForResult(ct, HelperSeconds);

        private static string WaitForResult(CancellationToken ct, int seconds)
        {
            var path = ResultPath;
            if (path == null) return null;
            for (int i = 0; i < seconds * 4; i++)
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
