// The installer's entry point: a window by default, a command when asked.
//
// [STAThread] is not decoration. WinForms file and message dialogs require a single-threaded
// apartment and a .NET process main thread is MTA by default, so without it the picker throws
// rather than opening - a confusing way to learn this.
//
// THE COMMAND FORM EXISTS BECAUSE A WINDOW CANNOT BE TESTED. Two buttons is the whole interface a
// user needs, and it is also an interface no script can exercise: every check of "does the sweep
// remove the old folder", "is the tick carried over", "does it refuse while a host is up" would
// otherwise be a person clicking and looking. It is the same shape LiteBox's own installer takes.
//
//     NixxIntegrations.exe --install   <LaunchBox root>
//     NixxIntegrations.exe --uninstall <LaunchBox root>
//     NixxIntegrations.exe --status    <LaunchBox root>
//     NixxIntegrations.exe --ramdisk   <LaunchBox root>
//
// Exit code 0 when it worked, 1 when it did not. This is a WinExe, so it has no console of its own;
// it attaches to whichever one started it, and falls back to silence plus the exit code when there
// is none.

using System.Runtime.InteropServices;

namespace NixxIntegrations;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length >= 1 && args[0].StartsWith("--", StringComparison.Ordinal))
            return Command(args);

        ApplicationConfiguration.Initialize();
        // A LaunchBox root as the one argument opens the window on it - what a shortcut or a drop on the
        // exe gives.
        var start = args.Length == 1 && InstallerCore.LooksLikeRoot(args[0]) ? Path.GetFullPath(args[0]) : null;
        Application.Run(new InstallerForm(start));
        return 0;
    }

    private static int Command(string[] args)
    {
        AttachConsole(unchecked((uint)-1));

        // AND THEN BIND Console TO THE REAL HANDLE. AttachConsole gives this process a console; it
        // does not decide where Console.Out points. Re-opening the standard handle says so
        // explicitly, which is what a caller redirecting our output is entitled to.
        //
        // HONESTLY: this was added while chasing an empty `--status > file`, and it is NOT what
        // fixed it - redirection through CreateProcess works either way, measured, and `cmd /c`
        // with a > redirect still produces nothing for a GUI-subsystem exe. That is a cmd-side
        // quirk and not ours. This stays because binding the handle explicitly is right, not
        // because it was shown to matter.
        try
        {
            var stdout = new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true };
            Console.SetOut(stdout);
        }
        catch { }

        var verb = args[0];

        // THE ELEVATED HALF OF A DRIVER PANEL: --driver-setup <imdisk|aim> <result file> [LaunchBox root].
        // A driver belongs to the machine, so the root is optional; with one, ImDisk comes with the helper
        // and the task. What happened goes to the file the window named - this process has no window.
        if (verb == "--driver-setup" && args.Length >= 3)
        {
            var chosen = args[1] == "aim" ? RamDriver.Aim : RamDriver.ImDisk;
            var at = args.Length >= 4 && InstallerCore.LooksLikeRoot(args[3]) ? InstallerCore.Resolve(Path.GetFullPath(args[3])) : null;
            var (done, said) = ImDiskSetup.SetUpElevated(at, chosen);
            try { File.WriteAllText(args[2], said); } catch { }
            Console.WriteLine(said);
            return done ? 0 : 1;
        }

        var root = args.Length >= 2 ? Path.GetFullPath(args[1]) : "";

        if (root.Length == 0 || !InstallerCore.LooksLikeRoot(root))
        {
            Console.WriteLine();
            Console.WriteLine("usage: NixxIntegrations.exe --install|--uninstall|--status|--ramdisk <LaunchBox root>");
            Console.WriteLine("       the root is the folder that holds Core\\LaunchBox.exe, not Core itself.");
            if (root.Length > 0) Console.WriteLine("       not a LaunchBox root: " + root);
            return 1;
        }

        var layout = InstallerCore.Resolve(root);

        if (verb == "--status")
        {
            Console.WriteLine();
            Console.WriteLine("root      " + layout.Root);
            Console.WriteLine("LaunchBox " + (layout.LbMajor > 0 ? layout.LbMajor.ToString() : "version unreadable"));
            Console.WriteLine("plugins   " + layout.PluginsRoot);
            Console.WriteLine("state     " + (InstallerCore.IsInstalled(layout) ? "installed" : "not installed"));

            // The optional half, one line per thing that can be missing - the same four the window
            // shows, because "not ready" on its own tells nobody what to do about it.
            var ram = RamDiskSetup.Look(layout);
            Console.WriteLine("ramdisk   " + (ram.Ready ? "ready (shared with LiteBox), through " + (LbIntegrations.RamDisk.RamDrive.ActiveBackend() ?? "nothing") : "not ready"));
            Console.WriteLine("  driver    " + (ram.Driver ? "ImDisk installed, " + (ImDiskSetup.InstalledVersion()?.ToString() ?? "version unreadable")
                                                    : "ImDisk NOT installed - this exe carries " + ImDiskSetup.Bundled()));
            Console.WriteLine("  runtime   " + (ram.Runtime ? "present" : "MISSING - " + ram.RuntimeWhy));
            Console.WriteLine("  helper    " + (!ram.Helper ? "not deployed"
                              : "in place, " + (ram.HelperVersion?.ToString() ?? "no version")
                                + (ram.HelperOld ? " - OLDER than the bundled " + ram.BundledVersion : "")));
            Console.WriteLine("  task      " + (ram.Task ?? "not registered"));
            Console.WriteLine("  aim       " + (AimSetup.ToolkitInstalled() ? "AIM Toolkit installed" : "AIM Toolkit not installed - this exe carries build " + AimSetup.Build)
                              + (AimSetup.DriverVersion() is { } aim ? ", driver " + aim : "") + (LbIntegrations.RamDisk.RamDrive.ActiveBackend() == "aim" ? " - the RAM disk uses it" : ""));

            // VHDX: a property of the machine, reported part by part.
            foreach (var line in VhdxSetup.Lines(VhdxSetup.Look())) Console.WriteLine(line);

            var busy = InstallerCore.RunningHost();
            if (busy != null) Console.WriteLine("running   " + busy);
            return 0;
        }

        var (ok, message) = verb switch
        {
            "--install"   => InstallerCore.Install(layout),
            "--uninstall" => InstallerCore.Uninstall(layout),
            // Prompts for elevation exactly once, to register the task. Scriptable for the same
            // reason the other two are: a window cannot be tested.
            "--ramdisk"   => !RamDiskSetup.DriverInstalled()
                                 ? (ImDiskSetup.IsElevated() ? ImDiskSetup.SetUpElevated(layout) : ImDiskSetup.SetUpWithPrompt(layout))
                                 : RamDiskSetup.Enable(layout),
            _             => (false, "unknown command: " + verb),
        };

        Console.WriteLine();
        Console.WriteLine(message);
        return ok ? 0 : 1;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AttachConsole(uint processId);
}
