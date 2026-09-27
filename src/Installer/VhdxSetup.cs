// VHDX support, reported by this installer - the ground for keeping the pristine Vita NAND in a
// virtual disk, each session on a differencing disk of its own, so the NAND itself stays clean.
// Nothing uses it yet: this only says whether the machine can.
//
// NOT HYPER-V. IsoEnablerForRPCS3 drives VHDX through the Hyper-V PowerShell cmdlets (New-VHD,
// Mount-VHD), which exist only where the Hyper-V management feature is installed - Pro and Enterprise
// editions, switched on. Windows itself creates and attaches VHDX files, differencing disks included,
// through the virtual disk API (virtdisk.dll over the vhdmp.sys driver) on every edition since
// Windows 8, Home included. That is what is checked, by its entry points rather than by the edition's
// name; the Hyper-V module is reported beside it, for information.
//
// Attaching a virtual disk needs administrator rights (SeManageVolumePrivilege): when this is used,
// it will go through the same elevated task as the RAM disk, never a prompt per session.

using System.Runtime.InteropServices;

namespace NixxIntegrations;

internal sealed record VhdxState(bool Windows8, bool Api, bool Driver, bool HyperVModule)
{
    /// <summary>Can this machine create a VHDX, a differencing disk on top of it, and attach both?</summary>
    public bool Ready => Windows8 && Api && Driver;
}

internal static class VhdxSetup
{
    public static VhdxState Look()
    {
        var system = Environment.SystemDirectory;
        return new VhdxState(
            Windows8: Environment.OSVersion.Version >= new Version(6, 2),
            Api: HasEntryPoints("virtdisk.dll", "CreateVirtualDisk", "OpenVirtualDisk", "AttachVirtualDisk", "DetachVirtualDisk"),
            Driver: File.Exists(Path.Combine(system, "drivers", "vhdmp.sys")),
            HyperVModule: Directory.Exists(Path.Combine(system, "WindowsPowerShell", "v1.0", "Modules", "Hyper-V")));
    }

    /// <summary>One line for the window.</summary>
    public static string Describe(VhdxState s)
    {
        if (s.Ready)
            return "VHDX supported - native virtual disks, differencing disks included (no Hyper-V needed)."
                   + (s.HyperVModule ? " The Hyper-V module is also installed." : "");
        var missing = new List<string>();
        if (!s.Windows8) missing.Add("Windows 8 or later needed");
        if (!s.Api) missing.Add("the virtual disk API (virtdisk.dll) is missing");
        if (!s.Driver) missing.Add("the virtual disk driver (vhdmp.sys) is missing");
        return "VHDX not supported: " + string.Join(", ", missing) + ".";
    }

    /// <summary>The same, one line per part, for --status.</summary>
    public static IEnumerable<string> Lines(VhdxState s)
    {
        yield return "vhdx      " + (s.Ready ? "supported (native, differencing disks included)" : "NOT supported");
        yield return "  windows   " + (s.Windows8 ? "8 or later" : "older than Windows 8");
        yield return "  api       " + (s.Api ? "virtdisk.dll with Create/Open/Attach/DetachVirtualDisk" : "virtdisk.dll or its entry points MISSING");
        yield return "  driver    " + (s.Driver ? "vhdmp.sys present" : "vhdmp.sys MISSING");
        yield return "  hyper-v   " + (s.HyperVModule ? "PowerShell module installed (not needed)" : "PowerShell module absent (not needed)");
    }

    /// <summary>Asked of the DLL itself: a file that is there but lacks what we would call is not
    /// support.</summary>
    private static bool HasEntryPoints(string dll, params string[] names)
    {
        IntPtr lib = IntPtr.Zero;
        try
        {
            if (!NativeLibrary.TryLoad(Path.Combine(Environment.SystemDirectory, dll), out lib)) return false;
            foreach (var n in names)
                if (!NativeLibrary.TryGetExport(lib, n, out _)) return false;
            return true;
        }
        catch { return false; }
        finally { if (lib != IntPtr.Zero) try { NativeLibrary.Free(lib); } catch { } }
    }
}
