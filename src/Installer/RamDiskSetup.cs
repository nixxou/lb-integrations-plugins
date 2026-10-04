// The optional RAM disk, reported and switched on from this installer.
//
// NOTHING HERE IS OURS ALONE, and that is the design. LiteBox already ships this machinery: the
// ImDisk driver the user installs once, a small elevated helper under
// <LaunchBox>\ThirdParty\RomExtractor\ramdisk\, and ONE scheduled task registered at HIGHEST so the
// helper can run afterwards with no further UAC prompt. Building a second set of those beside it
// would be two elevated tasks doing one job, and two things for somebody to understand.
//
// So this installer puts the SAME helper in the SAME folder and registers the SAME task, and the two
// products share one. Which also means the helper is written only when it is absent or OLDER than the
// one carried here - see RamDrive.DeployHelper, and Payload.SharedFiles.
//
// IT IS OPTIONAL: Vita3K and Xenia use it when it is there, and fall back to the disk when it is not.
// Each part is reported on its own because each one is repaired differently, and a single
// "not ready" would hide which.

using LbIntegrations.RamDisk;

namespace NixxIntegrations;

/// <summary>What is and is not in place, each part on its own. <see cref="Known"/> is false while no
/// LaunchBox is chosen: the helper and the task belong to an install, the driver and the runtime to the
/// machine.</summary>
internal sealed record RamDiskState(bool Known, bool Driver, bool Runtime, string? RuntimeWhy,
                                    bool Helper, Version? HelperVersion, Version? BundledVersion, string? Task,
                                    bool SelfContained = false)
{
    /// <summary>A helper older than the one this exe carries: it works, but misses what came since
    /// (clean, VHDX, clean dismount), and Enable replaces it.</summary>
    public bool HelperOld => HelperVersion != null && BundledVersion != null && HelperVersion < BundledVersion;
    public bool Ready => Known && Driver && Runtime && Helper && !HelperOld && Task != null;
}

internal static class RamDiskSetup
{
    /// <summary>Is a RAM disk driver (AIM or ImDisk) on this machine? Asked without a LaunchBox: a driver is installed
    /// once per machine.</summary>
    public static bool DriverInstalled() => RamDrive.IsDriverInstalled();

    private static Version? _bundled;
    private static bool _bundledRead;

    /// <summary>The helper version this exe carries, read once.</summary>
    public static Version? Bundled()
    {
        if (!_bundledRead) { _bundled = RamDrive.BundledVersion(Resource); _bundledRead = true; }
        return _bundled;
    }

    /// <summary>Does this exe carry a SELF-CONTAINED helper (1.9.1 on) - its exe and no runtimeconfig.json? Then no
    /// .NET runtime is needed on the machine (Mehdi, 04/10).</summary>
    public static bool BundledSelfContained => Resource("RamDiskHelper.exe") != null && Resource("RamDiskHelper.runtimeconfig.json") == null;

    /// <summary>What the machine says, and - when <paramref name="l"/> is given - what that install says.
    /// The task lookup runs schtasks, so this is slow enough to be asked off the window's thread.</summary>
    public static RamDiskState Look(Layout? l)
    {
        if (l != null) RamDiskHost.UseRoot(l.Root);
        // The runtime matters only for a framework-dependent helper: not for the one this exe carries, nor for a
        // self-contained one already in place.
        string? why = null;
        bool self = BundledSelfContained;
        var runtime = self || RamDrive.RuntimeReady(out why);
        if (l == null)
            return new RamDiskState(false, RamDrive.IsDriverInstalled(), runtime, why, false, null, Bundled(), null, self);
        return new RamDiskState(
            Known: true,
            Driver: RamDrive.IsDriverInstalled(),
            Runtime: runtime,
            RuntimeWhy: why,
            Helper: RamDrive.IsHelperInstalled(),
            HelperVersion: RamDrive.HelperVersion,
            BundledVersion: Bundled(),
            Task: RamDrive.InstalledTaskName(),
            SelfContained: self);
    }

    /// <summary>One line, for messages.</summary>
    public static string Describe(RamDiskState s)
    {
        if (s.Ready)
            return "RAM disk ready - shared with LiteBox: same folder, same scheduled task.";

        var lines = new List<string>();
        if (!s.Driver) lines.Add("no RAM disk driver (the AIM Toolkit, or ImDisk)");
        if (!s.Runtime) lines.Add(".NET 9+ runtime missing");
        if (s.Known && !s.Helper) lines.Add("helper not deployed");
        if (s.HelperOld) lines.Add("helper " + s.HelperVersion + " older than " + s.BundledVersion);
        if (s.Known && s.Task == null) lines.Add("elevated task not registered");
        return "RAM disk (optional): " + string.Join(", ", lines) + ".";
    }

    /// <summary>Deploy the helper if it is absent or older, then register the elevated task. One UAC
    /// prompt, and only for the task - writing the helper needs no more rights than the plugins do.
    ///
    /// Same signature as Install and Uninstall, so the window wires it in one line.</summary>
    public static (bool ok, string message) Enable(Layout l)
    {
        RamDiskHost.UseRoot(l.Root);

        if (!RamDrive.IsDriverInstalled())
            return (false, "No RAM disk driver is installed on this machine.\n\n"
                         + "Install Arsenal Image Mounter (the AIM Toolkit) or ImDisk with their buttons above, then come back here.\n\n"
                         + "Nothing else was changed.");

        if (!BundledSelfContained && !RamDrive.RuntimeReady(out var why))
            return (false, "The RAM disk helper cannot run here: " + why + ".\n\n"
                         + "Install the .NET Runtime (9 or newer) and try again. Nothing was changed.");

        var deployed = RamDrive.DeployHelper(Resource, out var wrote);
        if (!wrote) return (false, deployed);

        var already = RamDrive.InstalledTaskName();
        if (already != null)
            return (true, deployed + "\n\nThe elevated task was already registered as " + already
                        + " - nothing to do. That is LiteBox's task and ours at the same time, which "
                        + "is the point.");

        if (!RamDrive.InstallTask())
            return (false, deployed + "\n\nThe elevated task could not be registered. If you "
                        + "dismissed the Windows prompt, try again and accept it.");

        var name = RamDrive.InstalledTaskName();
        if (name == null)
            return (false, deployed + "\n\nWindows reported the task was created, but it cannot be "
                        + "read back. Nothing will use it until it can.");

        return (true, deployed + "\n\nRegistered the elevated task " + name + ".\n\n"
                    + "That was the only prompt: from now on a RAM disk is mounted without one. "
                    + "LiteBox uses this same task, so its ROM extractor will find it too.");
    }

    /// <summary>THE UNINSTALL'S PART (Mehdi, 04/10): the helper and its elevated task taken away. When LiteBox is in this
    /// LaunchBox it shares both (same folder, same task): <paramref name="askShared"/> is asked then - the window's Yes/No,
    /// No by default - and without it (the command line) they stay. LiteBox puts the helper back at its next start
    /// (NativeInstaller.EnsureDeployed), not the task: its ROM options offer that, one prompt. The drivers (AIM, ImDisk) are
    /// left: they are the machine's, with their own entry in Programs and Features. What happened, for the message.</summary>
    public static string Remove(Layout l, Func<bool>? askShared = null)
    {
        try
        {
            RamDiskHost.UseRoot(l.Root);
            var dir = RamDrive.HelperDir;
            bool helper = RamDrive.IsHelperInstalled(), task = RamDrive.InstalledTaskName() != null;
            if (!helper && !task) return "";
            if (LiteBoxIsHere(l))
            {
                bool remove = false;
                try { remove = askShared?.Invoke() == true; } catch { }
                if (!remove)
                    return "\n\nThe RAM disk helper and its scheduled task are LiteBox's too (it is installed here): left in place.";
            }

            var said = new List<string>();
            if (task) said.Add(RamDrive.RemoveTask() ? "its scheduled task removed" : "its scheduled task NOT removed (the Windows prompt was refused?) - schtasks /delete can do it");
            if (dir != null && Directory.Exists(dir))
            {
                foreach (var name in RamDrive.HelperFiles.Concat(RamDrive.FrameworkFiles).Concat(new[] { "ramdisk.cfg", "ramdisk.result", "attached.tsv" }))
                    try { var p = Path.Combine(dir, name); if (File.Exists(p)) File.Delete(p); } catch { }
                try { if (!Directory.EnumerateFileSystemEntries(dir).Any()) Directory.Delete(dir); } catch { }
                said.Add(File.Exists(Path.Combine(dir, "RamDiskHelper.exe")) ? "the helper NOT removed (in use?)" : "the helper removed");
            }
            return "\n\nRAM disk: " + string.Join(", ", said) + ". The AIM / ImDisk drivers stay - they have their own entry in "
                 + "Programs and Features.";
        }
        catch (Exception ex) { return "\n\nThe RAM disk helper could not be removed: " + ex.Message; }
    }

    /// <summary>Is LiteBox installed in this LaunchBox - its exe at the root or in Core\, or its Core\litebox\ folder?</summary>
    public static bool LiteBoxIsHere(Layout l)
    {
        try
        {
            return File.Exists(Path.Combine(l.Root, "LiteBox.exe")) || File.Exists(Path.Combine(l.Root, "Core", "LiteBox.exe"))
                || Directory.Exists(Path.Combine(l.Root, "Core", "litebox"));
        }
        catch { return true; }
    }

    /// <summary>The helper's file, out of this exe. Null when a build was made without its
    /// payload, which DeployHelper turns into a message rather than an exception.</summary>
    private static byte[]? Resource(string fileName)
    {
        try
        {
            var asm = System.Reflection.Assembly.GetExecutingAssembly();
            using var stream = asm.GetManifestResourceStream("payload/ramdisk/" + fileName);
            if (stream == null) return null;
            using var memory = new MemoryStream();
            stream.CopyTo(memory);
            return memory.ToArray();
        }
        catch { return null; }
    }
}
