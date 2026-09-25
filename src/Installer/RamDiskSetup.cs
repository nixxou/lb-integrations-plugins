// The optional RAM disk, reported and switched on from this installer.
//
// NOTHING HERE IS OURS ALONE, and that is the design. LiteBox already ships this machinery: the
// ImDisk driver the user installs once, a small elevated helper under
// <LaunchBox>\ThirdParty\RomExtractor\ramdisk\, and ONE scheduled task registered at HIGHEST so the
// helper can run afterwards with no further UAC prompt. Building a second set of those beside it
// would be two elevated tasks doing one job, and two things for somebody to understand.
//
// So this installer puts the SAME helper in the SAME folder and registers the SAME task, and the two
// products share one. Which also means this is the case where the installer does NOT overwrite:
// whoever got there first owns the file. See Payload.SharedFiles and src\Shared.RamDisk\RamDrive.cs,
// whose sources are compiled in here.
//
// IT IS ENTIRELY OPTIONAL. Nothing in the pack needs it today - Vita3K will, for the temporary image
// a session is played on - and every part of it is missing on a clean machine. The four states below
// are reported separately because each one is repaired differently, and a single "not ready" would
// hide which.

using LbIntegrations.RamDisk;

namespace NixxIntegrations;

/// <summary>What is and is not in place, each part on its own.</summary>
internal sealed record RamDiskState(bool Driver, bool Runtime, bool Helper, string? Task, string? RuntimeWhy)
{
    public bool Ready => Driver && Runtime && Helper && Task != null;
}

internal static class RamDiskSetup
{
    /// <summary>Point the shared sources at this install, then ask them what they see.</summary>
    public static RamDiskState Look(Layout l)
    {
        RamDiskHost.UseRoot(l.Root);
        var ready = RamDrive.RuntimeReady(out var why);
        return new RamDiskState(
            Driver: RamDrive.IsDriverInstalled(),
            Runtime: ready,
            Helper: RamDrive.IsHelperInstalled(),
            Task: RamDrive.InstalledTaskName(),
            RuntimeWhy: why);
    }

    /// <summary>Four lines for the window and for --status: one per thing that can be missing.</summary>
    public static string Describe(RamDiskState s)
    {
        if (s.Ready)
            return "RAM disk ready - shared with LiteBox: same folder, same scheduled task.";

        var lines = new List<string>();
        if (!s.Driver) lines.Add("ImDisk not installed");
        if (!s.Runtime) lines.Add(".NET 9+ runtime missing");
        if (!s.Helper) lines.Add("helper not deployed");
        if (s.Task == null) lines.Add("elevated task not registered");
        return "RAM disk (optional): " + string.Join(", ", lines) + ".";
    }

    /// <summary>Deploy the helper if it is absent, then register the elevated task. One UAC prompt,
    /// and only for the task - writing the helper needs no more rights than the plugins do.
    ///
    /// Same signature as Install and Uninstall, so the window wires it in one line.</summary>
    public static (bool ok, string message) Enable(Layout l)
    {
        RamDiskHost.UseRoot(l.Root);

        if (!RamDrive.IsDriverInstalled())
            return (false, "ImDisk is not installed on this machine.\n\n"
                         + "The RAM disk needs its driver, which is a separate free download - use "
                         + "the \"Get ImDisk\" button, install it, then come back here.\n\n"
                         + "Nothing else was changed.");

        if (!RamDrive.RuntimeReady(out var why))
            return (false, "The RAM disk helper cannot run here: " + why + ".\n\n"
                         + "Install the .NET Desktop Runtime and try again. Nothing was changed.");

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

    /// <summary>The helper's four files, out of this exe. Null when a build was made without its
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
