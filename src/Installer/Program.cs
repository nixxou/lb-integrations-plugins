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
        Application.Run(new InstallerForm());
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
            Console.WriteLine("ramdisk   " + (ram.Ready ? "ready (shared with LiteBox)" : "not ready"));
            Console.WriteLine("  driver    " + (ram.Driver ? "ImDisk installed" : "ImDisk NOT installed"));
            Console.WriteLine("  runtime   " + (ram.Runtime ? "present" : "MISSING - " + ram.RuntimeWhy));
            Console.WriteLine("  helper    " + (ram.Helper ? "in place" : "not deployed"));
            Console.WriteLine("  task      " + (ram.Task ?? "not registered"));

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
            "--ramdisk"   => RamDiskSetup.Enable(layout),
            _             => (false, "unknown command: " + verb),
        };

        Console.WriteLine();
        Console.WriteLine(message);
        return ok ? 0 : 1;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AttachConsole(uint processId);
}
