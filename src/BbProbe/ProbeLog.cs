// %LOCALAPPDATA%\lb-integrations-plugins\bb-probe.log - one line per observation, never beside the DLL.

using System;
using System.IO;
using System.Text;

namespace LbIntegrations.BbProbe
{
    internal static class ProbeLog
    {
        private static readonly object Gate = new object();
        private static readonly string PathName = Beside("bb-probe.log");

        public static string Beside(string name)
            => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                            "lb-integrations-plugins", name);

        public static void Start(string host)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(PathName));
                if (File.Exists(PathName) && new FileInfo(PathName).Length > 4 * 1024 * 1024) File.Delete(PathName);
            }
            catch { }
            Write(Environment.NewLine + "=== " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  host: " + host + " ===", raw: true);
        }

        public static void Info(string line) => Write(line);

        public static void Warn(string what, Exception ex) => Write("! " + what + " - " + ex.GetType().Name + ": " + ex.Message);

        private static void Write(string line, bool raw = false)
        {
            try
            {
                lock (Gate)
                    File.AppendAllText(PathName,
                        (raw ? line : DateTime.Now.ToString("HH:mm:ss.fff") + "  " + line) + Environment.NewLine,
                        Encoding.UTF8);
            }
            catch { }
        }
    }
}
