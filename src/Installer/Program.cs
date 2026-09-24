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

        var verb = args[0];
        var root = args.Length >= 2 ? Path.GetFullPath(args[1]) : "";

        if (root.Length == 0 || !InstallerCore.LooksLikeRoot(root))
        {
            Console.WriteLine();
            Console.WriteLine("usage: NixxIntegrations.exe --install|--uninstall|--status <LaunchBox root>");
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
            var busy = InstallerCore.RunningHost();
            if (busy != null) Console.WriteLine("running   " + busy);
            return 0;
        }

        var (ok, message) = verb switch
        {
            "--install"   => InstallerCore.Install(layout),
            "--uninstall" => InstallerCore.Uninstall(layout),
            _             => (false, "unknown command: " + verb),
        };

        Console.WriteLine();
        Console.WriteLine(message);
        return ok ? 0 : 1;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AttachConsole(uint processId);
}
