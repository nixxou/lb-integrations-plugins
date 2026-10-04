// The ImDisk driver, installed from this exe - exactly what LTR Data's own imdiskinst.exe 2.1.2 puts on
// a machine, and nothing else (Mehdi, 02/10: "uniquement ce qui est necessaire pour mimic l'install").
//
// NOT THE IMDISK TOOLKIT. The Toolkit's installer (config.exe) adds RamDiskUI, MountImg, a service of
// its own, menu entries and DiscUtils - which wants .NET Framework 4.8 and stops on a dialog without
// it - and its driver script refuses to go on while an ImDisk disk is mounted. None of it is needed:
// the RAM disk helper only calls System32\imdisk.exe and imdisk.cpl.
//
// SO THE DRIVER PACKAGE ITSELF, run the way its install.cmd runs it: imdisk.inf through setupapi's
// InstallHinfSection, then its two services started. The files are vendor\imdisk\, byte-identical to
// what imdiskinst.exe left on the machine they were measured on (SOURCE.md there). What lands:
//   System32\drivers\imdisk.sys, System32\imdisk.exe, imdisk.cpl, imdsksvc.exe, uninstall_imdisk.cmd,
//   SysWOW64\imdisk.exe and imdisk.cpl, inf\imdisk.inf, the ImDisk and ImDskSvc services, the
//   "ImDisk Virtual Disk Driver" entry in Programs and Features, and the INF's Explorer entries
//   (Mount as ImDisk Virtual Disk...). The control panel applet is imdisk.cpl.
//
// ONLY WHEN THERE IS NO DRIVER. An ImDisk already there - the Toolkit's, LiteBox's, a newer one - is
// left alone and reported: it may be in use, and it is not ours to replace.
//
// ELEVATED IN ONE PROMPT. The window starts this exe again, elevated, with --driver-setup; that one
// process installs the driver, deploys the helper and registers the task, and writes what it did to a
// file the window then shows.

using System.Diagnostics;
using System.Security.Principal;

namespace NixxIntegrations;

/// <summary>Which driver the window was asked to install: ImDisk, which the RAM disk uses, or Arsenal
/// Image Mounter, its successor, installed only for now (AimSetup).</summary>
internal enum RamDriver { ImDisk, Aim }

internal static class ImDiskSetup
{
    /// <summary>The INF's files, as resource name -> path relative to the folder the INF is run from.</summary>
    private static readonly string[] Files =
    {
        "imdisk.inf", "uninstall_imdisk.cmd",
        "sys/amd64/imdisk.sys", "cli/amd64/imdisk.exe", "cli/i386/imdisk.exe",
        "cpl/amd64/imdisk.cpl", "cpl/i386/imdisk.cpl", "svc/amd64/imdsksvc.exe",
    };

    private static string System32 => Environment.SystemDirectory;
    private static string DriverSys => Path.Combine(System32, "drivers", "imdisk.sys");
    public static string ControlPanel => Path.Combine(System32, "imdisk.cpl");

    /// <summary>The version of the driver on this machine, or null.</summary>
    public static Version? InstalledVersion()
    {
        try
        {
            if (!File.Exists(DriverSys)) return null;
            var v = FileVersionInfo.GetVersionInfo(DriverSys);
            return new Version(v.FileMajorPart, v.FileMinorPart, v.FileBuildPart, v.FilePrivatePart);
        }
        catch { return null; }
    }

    private static Version? _bundled;

    /// <summary>The driver version this exe carries (2.1.2.66), read off its bytes.</summary>
    public static Version? Bundled()
    {
        if (_bundled != null) return _bundled;
        string? temp = null;
        try
        {
            var bytes = Resource("sys/amd64/imdisk.sys");
            if (bytes == null) return null;
            temp = Path.Combine(Path.GetTempPath(), "lbip-imdisk-" + Guid.NewGuid().ToString("N") + ".sys");
            File.WriteAllBytes(temp, bytes);
            var v = FileVersionInfo.GetVersionInfo(temp);
            return _bundled = new Version(v.FileMajorPart, v.FileMinorPart, v.FileBuildPart, v.FilePrivatePart);
        }
        catch { return null; }
        finally { try { if (temp != null) File.Delete(temp); } catch { } }
    }

    public static bool CanOpenControlPanel => File.Exists(ControlPanel);

    public static void OpenControlPanel()
        => Process.Start(new ProcessStartInfo(Path.Combine(System32, "control.exe"), "\"" + ControlPanel + "\"") { UseShellExecute = false });

    public static bool IsElevated()
    {
        try
        {
            using var id = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch { return false; }
    }

    /// <summary>From the window: everything the RAM disk needs, behind ONE prompt - this exe again,
    /// elevated, running <see cref="SetUpElevated"/>. The answer comes back through a file.</summary>
    public static (bool ok, string message) SetUpWithPrompt(Layout l) => SetUpWithPrompt(l, RamDriver.ImDisk);

    public static (bool ok, string message) SetUpWithPrompt(Layout? l, RamDriver driver)
    {
        var result = Path.Combine(Path.GetTempPath(), "lbip-ramdisk-setup-" + Guid.NewGuid().ToString("N") + ".txt");
        try
        {
            var psi = new ProcessStartInfo(Environment.ProcessPath!)
            {
                UseShellExecute = true,
                Verb = "runas",
                WindowStyle = ProcessWindowStyle.Hidden,
            };
            // --driver-setup <imdisk|aim> <result file> [LaunchBox root]: with a root, ImDisk comes with the
            // helper and the task in the same prompt.
            psi.ArgumentList.Add("--driver-setup");
            psi.ArgumentList.Add(driver == RamDriver.Aim ? "aim" : "imdisk");
            psi.ArgumentList.Add(result);
            if (l != null) psi.ArgumentList.Add(l.Root);
            using (var p = Process.Start(psi))
            {
                if (p == null) return (false, "The setup could not be started.");
                p.WaitForExit();
                var text = File.Exists(result) ? File.ReadAllText(result).Trim() : "";
                if (text.Length == 0) return (false, "The elevated setup ended without saying what it did (exit " + p.ExitCode + ").");
                return (p.ExitCode == 0, text);
            }
        }
        catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            return (false, "The Windows prompt was dismissed - nothing was changed.");
        }
        catch (Exception ex) { return (false, "The setup could not be started: " + ex.Message); }
        finally { try { File.Delete(result); } catch { } }
    }

    /// <summary>In the elevated process: the driver when there is none (the AIM Toolkit or ImDisk), then - with a LaunchBox -
    /// the helper and the task, in this same prompt.</summary>
    public static (bool ok, string message) SetUpElevated(Layout? l, RamDriver chosen = RamDriver.ImDisk)
    {
        if (chosen == RamDriver.Aim)
        {
            // The AIM Toolkit, then - with a LaunchBox - the helper and the task, in this same prompt: AIM is the
            // RAM disk's first choice since the helper 1.6.
            var (aimOk, aimSaid) = AimSetup.Install();
            if (!aimOk || l == null) return (aimOk, aimSaid);
            var (helperOk, helperSaid) = RamDiskSetup.Enable(l);
            return (helperOk, aimSaid + "\n\n" + helperSaid);
        }
        if (l == null)
            return InstalledVersion() != null && LbIntegrations.RamDisk.RamDrive.IsImDiskInstalled()
                ? (true, "The ImDisk driver " + InstalledVersion()!.ToString(3) + " is already installed - left alone.")
                : InstallDriver();
        string driver = "";
        var installed = InstalledVersion();
        if (installed == null || !LbIntegrations.RamDisk.RamDrive.IsImDiskInstalled())
        {
            var (ok, message) = InstallDriver();
            if (!ok) return (false, message);
            driver = message + "\n\n";
        }
        var (ramOk, ramMessage) = RamDiskSetup.Enable(l);
        return (ramOk, driver + ramMessage);
    }

    /// <summary>imdisk.inf, run as its own install.cmd runs it, from a temporary copy of the package.
    /// Needs administrator rights.</summary>
    public static (bool ok, string message) InstallDriver()
    {
        if (!IsElevated()) return (false, "Installing the ImDisk driver needs administrator rights.");
        var dir = Path.Combine(Path.GetTempPath(), "lbip-imdisk-" + Guid.NewGuid().ToString("N"));
        try
        {
            foreach (var name in Files)
            {
                var bytes = Resource(name);
                if (bytes == null) return (false, "The ImDisk driver is missing from this build (" + name + ").");
                var path = Path.Combine(dir, name.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllBytes(path, bytes);
            }

            // install.cmd: rundll32 setupapi.dll,InstallHinfSection DefaultInstall 132 .\imdisk.inf
            // (132 = 128, the INF's folder is the source, + 4, ask before a reboot if one is needed).
            int code = Run(Path.Combine(System32, "rundll32.exe"),
                           "setupapi.dll,InstallHinfSection DefaultInstall 132 " + Path.Combine(dir, "imdisk.inf"), dir);
            var version = InstalledVersion();
            if (code != 0 || version == null || !LbIntegrations.RamDisk.RamDrive.IsImDiskInstalled())
                return (false, "The ImDisk driver could not be installed (setup exit " + code + "). Restart Windows and try again.");

            // Then its services, as install.cmd starts them. Already running is not a failure.
            Run(Path.Combine(System32, "net.exe"), "start imdsksvc", dir);
            Run(Path.Combine(System32, "net.exe"), "start imdisk", dir);
            bool running = ServiceRunning("ImDisk");

            return (true, "Installed the ImDisk Virtual Disk Driver " + version.ToString(3) + " (LTR Data)."
                          + (running ? "" : "\nThe driver did not start: restart Windows before using the RAM disk.")
                          + "\nIts control panel is imdisk.cpl; it is removed from Programs and Features like any program.");
        }
        catch (Exception ex) { return (false, "The ImDisk driver could not be installed: " + ex.Message); }
        finally { try { Directory.Delete(dir, recursive: true); } catch { } }
    }

    private static bool ServiceRunning(string name)
    {
        try
        {
            var outp = Capture(Path.Combine(System32, "sc.exe"), "query " + name);
            return outp.Contains("RUNNING", StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

    private static int Run(string exe, string args, string dir)
    {
        using var p = Process.Start(new ProcessStartInfo(exe, args) { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = dir })!;
        p.WaitForExit();
        return p.ExitCode;
    }

    private static string Capture(string exe, string args)
    {
        using var p = Process.Start(new ProcessStartInfo(exe, args) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true })!;
        var text = p.StandardOutput.ReadToEnd();
        p.WaitForExit();
        return text;
    }

    private static byte[]? Resource(string name)
    {
        try
        {
            using var s = typeof(ImDiskSetup).Assembly.GetManifestResourceStream("payload/imdisk/" + name);
            if (s == null) return null;
            using var m = new MemoryStream();
            s.CopyTo(m);
            return m.ToArray();
        }
        catch { return null; }
    }
}
