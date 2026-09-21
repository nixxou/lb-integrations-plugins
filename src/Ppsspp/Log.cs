// Where a plugin line goes.
//
// Two sinks, because the two hosts are not the same kind of program. LiteBox runs with a console,
// so Console.WriteLine is read there directly, prefixed the way the hosts do it ([loader],
// [emuplugin], ...) so a plugin line is recognisable in a mixed log. LaunchBox.exe is a windowed
// application with NO console attached: every Console.WriteLine from a plugin is written to a
// handle that goes nowhere. Assuming otherwise cost a diagnosis - when the Download button did not
// appear under LaunchBox there was simply no trace to read.
//
// So every line is ALSO appended to a file. It lives under %LOCALAPPDATA%, deliberately not next to
// the assembly: LaunchBox 14 validates a managed plugin folder against its manifest, and a log file
// appearing inside it is exactly the kind of change that makes that check fail.
//
// LaunchBox's own plugins call Root.Logging, which lives in the obfuscated core; this repo
// references nothing but the public SDK, so the file is ours to write.

using System;
using System.IO;
using System.Text;

namespace LbIntegrations.Ppsspp
{
    internal static class Log
    {
        private const string Prefix = "[ppsspp] ";

        /// <summary>Past this, the file is truncated once, at the first write of the process. A
        /// plugin that logs on every call must not be able to fill a disk.</summary>
        private const long MaxBytes = 512 * 1024;

        private static readonly object Gate = new object();
        private static string _path;
        private static bool _resolved;

        public static void Info(string message) => Write(message);

        /// <summary>An exception we swallowed. Prints the type and message, never the stack: these
        /// lines land in a user's log, and every call site here is already a non-fatal path.</summary>
        public static void Warn(string message, Exception ex = null)
            => Write(ex == null ? message : message + " - " + ex.GetType().Name + ": " + ex.Message);

        private static void Write(string message)
        {
            var line = Prefix + message;
            try { Console.WriteLine(line); } catch { }
            try
            {
                lock (Gate)
                {
                    var path = Path();
                    if (path == null) return;
                    File.AppendAllText(path,
                        DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + "  " + line + Environment.NewLine,
                        Encoding.UTF8);
                }
            }
            catch { }
        }

        /// <summary>The log file, resolved once. Null means we could not get one, and every call
        /// site then degrades to the console alone rather than throwing into the host.</summary>
        private static string Path()
        {
            if (_resolved) return _path;
            _resolved = true;
            try
            {
                var dir = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "lb-integrations-plugins");
                Directory.CreateDirectory(dir);
                var path = System.IO.Path.Combine(dir, "ppsspp.log");

                // Truncate rather than roll: the interesting content is always the current session,
                // and a second file is one more thing to explain to someone reading a bug report.
                try
                {
                    var info = new FileInfo(path);
                    if (info.Exists && info.Length > MaxBytes) info.Delete();
                }
                catch { }

                File.AppendAllText(path,
                    Environment.NewLine + "=== " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
                    + "  host: " + HostName() + " ===" + Environment.NewLine,
                    Encoding.UTF8);
                _path = path;
            }
            catch { _path = null; }
            return _path;
        }

        /// <summary>The process we were loaded into, so a log opened cold says whether it came from
        /// LaunchBox, BigBox or LiteBox without anyone having to guess.</summary>
        private static string HostName()
        {
            try { return System.Diagnostics.Process.GetCurrentProcess().ProcessName; }
            catch { return "?"; }
        }
    }
}
