// Where a plugin line goes. Same arrangement as the other plugins of this pack - see
// src\Xenia\Log.cs for the reasoning: LaunchBox.exe has no console, so every line also goes to
//
//     %LOCALAPPDATA%\lb-integrations-plugins\superzsnes.log
//
// deliberately not beside the assembly, because LaunchBox 14 validates a managed plugin folder
// against its manifest and a log file appearing inside it is the kind of change that fails that check.

using System;
using System.IO;
using System.Text;

namespace LbIntegrations.SuperZsnes
{
    internal static class Log
    {
        private const string Prefix = "[superzsnes] ";

        /// <summary>Past this, the file is truncated once, at the first write of the process.</summary>
        private const long MaxBytes = 512 * 1024;

        private static readonly object Gate = new object();
        private static string _path;
        private static bool _resolved;

        public static void Info(string message) => Write(message);

        /// <summary>For anything the host can ask for many times over. Written only when a marker
        /// file named "trace" exists beside the log.</summary>
        public static void Verbose(string message) { if (Tracing) Write(message); }

        public static bool Tracing => _tracing ??= MarkerExists("trace");
        private static bool? _tracing;

        /// <summary>Is a feature switched off by a marker file beside the log? Read once per name.
        /// The shared row injection reads "no-metadata" through here; see LbipLog.</summary>
        public static bool Disabled(string marker)
        {
            lock (_switches)
            {
                if (_switches.TryGetValue(marker, out var off)) return off;
                off = MarkerExists(marker);
                _switches[marker] = off;
                return off;
            }
        }

        private static readonly System.Collections.Generic.Dictionary<string, bool> _switches =
            new System.Collections.Generic.Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

        private static bool MarkerExists(string marker)
        {
            try
            {
                var log = Path();
                return log != null
                       && File.Exists(System.IO.Path.Combine(
                              System.IO.Path.GetDirectoryName(log) ?? "", marker));
            }
            catch { return false; }
        }

        /// <summary>An exception we swallowed. Type and message, never the stack.</summary>
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
                var path = System.IO.Path.Combine(dir, "superzsnes.log");

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

        private static string HostName()
        {
            try { return System.Diagnostics.Process.GetCurrentProcess().ProcessName; }
            catch { return "?"; }
        }
    }
}
