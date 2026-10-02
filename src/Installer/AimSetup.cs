// The AIM Toolkit, offered beside ImDisk: Arsenal Image Mounter's driver and the tools around it, by the
// author of the ImDisk Toolkit (Mehdi, 02/10: "qu'on comprenne qu'on peut installer l'un ou l'autre" -
// and for AIM, everything: "je veut pouvoir monter des images, des vhd, etc").
//
// THE WHOLE TOOLKIT, the way its own install.bat installs it: files.cab unpacked by extrac32, then
// config.exe run from there. Silent (/fullsilent), with every component - the driver, MountImg (images,
// VHD, VHDX, VMDK... from Explorer's menu), RamDiskUI - and the context menu entries; only the desktop
// shortcuts are left out. Files in vendor\aim\ as shipped (SOURCE.md there). config.exe registers the
// Toolkit in Programs and Features ("AIM-tk") and removes it from there.
//
// THE RAM DISK USES IT FIRST since the helper 1.6 (aim_ll for RAM disks, aim_cli for images), ImDisk behind it; the window
// says so.

using System.Diagnostics;
using Microsoft.Win32;

namespace NixxIntegrations;

internal static class AimSetup
{
    private const string UninstallKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\AIM-tk";

    /// <summary>The Toolkit's build, as the zip names it (AIMtk20260420).</summary>
    public const string Build = "20260420";

    private static string DriverSys => Path.Combine(Environment.SystemDirectory, "drivers", "phdskmnt.sys");

    /// <summary>The AIM driver on this machine - from the Toolkit or from anything else - or null.</summary>
    public static Version? DriverVersion()
    {
        try
        {
            using var service = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\phdskmnt");
            if (service == null || !File.Exists(DriverSys)) return null;
            var v = FileVersionInfo.GetVersionInfo(DriverSys);
            return new Version(v.FileMajorPart, v.FileMinorPart, v.FileBuildPart, v.FilePrivatePart);
        }
        catch { return null; }
    }

    /// <summary>Is the AIM Toolkit installed (its Programs and Features entry)?</summary>
    public static bool ToolkitInstalled()
    {
        try { using var k = Registry.LocalMachine.OpenSubKey(UninstallKey); return k != null; }
        catch { return false; }
    }

    /// <summary>The Toolkit's RamDiskUI.exe, or null: in the folder its uninstall entry names, else the
    /// default Program Files\AIM Toolkit.</summary>
    public static string? RamDiskUI()
    {
        try
        {
            var dirs = new List<string>();
            using (var k = Registry.LocalMachine.OpenSubKey(UninstallKey))
            {
                foreach (var name in new[] { "InstallLocation", "DisplayIcon", "UninstallString" })
                {
                    var v = (k?.GetValue(name) as string)?.Trim().Trim('"');
                    if (string.IsNullOrEmpty(v)) continue;
                    int exe = v.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
                    dirs.Add(exe > 0 ? Path.GetDirectoryName(v.Substring(0, exe + 4).Trim('"'))! : v);
                }
            }
            dirs.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "AIM Toolkit"));
            foreach (var d in dirs)
            {
                var ui = Path.Combine(d, "RamDiskUI.exe");
                if (File.Exists(ui)) return ui;
            }
        }
        catch { }
        return null;
    }

    /// <summary>MountImg's VHD/VMDK support (DiscUtils) needs .NET Framework 4.8 - built into Windows 10
    /// 1903 and later. Release 528040 is 4.8.</summary>
    public static bool NetFramework48()
    {
        try
        {
            using var k = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full");
            return k?.GetValue("Release") is int release && release >= 528040;
        }
        catch { return false; }
    }

    /// <summary>In the elevated process. Nothing is done when the Toolkit is already there.</summary>
    public static (bool ok, string message) Install()
    {
        if (!ImDiskSetup.IsElevated()) return (false, "Installing the AIM Toolkit needs administrator rights.");
        if (ToolkitInstalled()) return (true, "The AIM Toolkit is already installed - left alone.");

        var dir = Path.Combine(Path.GetTempPath(), "lbip-aimtk-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(dir);
            var cab = Path.Combine(dir, "files.cab");
            var bytes = Resource("files.cab");
            if (bytes == null) return (false, "The AIM Toolkit is missing from this build.");
            File.WriteAllBytes(cab, bytes);

            // install.bat: extrac32.exe /e /l "%F%" "%~dp0files.cab", then "%F%\config.exe" %2 %3 %4.
            var files = Path.Combine(dir, "files");
            Directory.CreateDirectory(files);
            Run(Path.Combine(Environment.SystemDirectory, "extrac32.exe"), "/e /l \"" + files + "\" \"" + cab + "\"", dir);
            var config = Path.Combine(files, "config.exe");
            if (!File.Exists(config)) return (false, "The AIM Toolkit package could not be unpacked.");

            int code = Run(config, "/fullsilent /mountimg:1 /ramdiskui:1 /menu_entries:1 /shortcuts_desktop:0", files);
            var driver = DriverVersion();
            if (!ToolkitInstalled() || driver == null)
                return (false, "The AIM Toolkit could not be installed (setup exit " + code + "). Restart Windows and try again.");

            QuietRamDiskUIDefaults();
            return (true, "Installed the AIM Toolkit (build " + Build + "): the Arsenal Image Mounter driver " + driver.ToString(3)
                          + ", MountImg and RamDiskUI. Images, VHD and VHDX mount from Explorer's right-click menu."
                          + (NetFramework48() ? "" : "\n\n.NET Framework 4.8 is missing: VHD, VMDK and the other virtual disk formats need it in MountImg.")
                          + "\n\nIt is removed from Programs and Features like any program."
                          + "\n\nThe plugins' RAM disk uses it first from now on (ImDisk, if it is there too, is the fallback).");
        }
        catch (Exception ex) { return (false, "The AIM Toolkit could not be installed: " + ex.Message); }
        finally { try { Directory.Delete(dir, recursive: true); } catch { } }
    }

    /// <summary>RamDiskUI's own RAM disk, OFF until the user turns it on (Mehdi, 02/10). Its settings live in
    /// HKLM\SOFTWARE\AIM-tk, which config.exe does not fill: with WinBoot ("Launch at Windows Startup") and
    /// TempFolder ("Create TEMP Folder") absent, RamDiskUI shows both ticked - one Apply and a RAM disk is
    /// mounted at every boot, holding a TEMP folder. Written as 0 ONLY WHEN ABSENT: a choice already made, here
    /// or in a previous install, is left alone. The plugins' RAM disks do not go through RamDiskUI.</summary>
    private static void QuietRamDiskUIDefaults()
    {
        try
        {
            using var key = Registry.LocalMachine.CreateSubKey(@"SOFTWARE\AIM-tk");
            foreach (var name in new[] { "WinBoot", "TempFolder" })
                if (key.GetValue(name) == null) key.SetValue(name, 0, RegistryValueKind.DWord);
        }
        catch { }
    }

    private static int Run(string exe, string args, string dir)
    {
        using var p = Process.Start(new ProcessStartInfo(exe, args) { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = dir })!;
        p.WaitForExit();
        return p.ExitCode;
    }

    private static byte[]? Resource(string name)
    {
        try
        {
            using var s = typeof(AimSetup).Assembly.GetManifestResourceStream("payload/aim/" + name);
            if (s == null) return null;
            using var m = new MemoryStream();
            s.CopyTo(m);
            return m.ToArray();
        }
        catch { return null; }
    }
}
