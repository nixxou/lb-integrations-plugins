// Where this plugin's lines go - as the SUPER ZSNES plugin's (tools\superzsnes-bepinex\NixxLog.cs).
//
// BepInEx is deployed SILENT by the pack (no console, no disk log). So every line goes to BepInEx's own logger AND, when 3dSen
// was started with --nixx-log, to <data>\nixx.log, truncated at each start. Errors (a patch that could not be applied after an
// update of 3dSen, typically) go to <data>\nixx-errors.log EVEN WITHOUT --nixx-log: the one line whoever opens the folder needs.

using System;
using System.IO;
using BepInEx.Logging;

namespace LbIntegrations.ThreeDSen.Mod
{
    internal sealed class NixxLog
    {
        private readonly ManualLogSource _bepinex;
        private readonly object _gate = new object();
        private string _file, _errors;
        private bool _verbose, _started, _errorsStarted;

        public NixxLog(ManualLogSource bepinex) { _bepinex = bepinex; }

        /// <summary>The folder the files go to, once known (3dSen's data folder), and whether the full log is wanted.</summary>
        public void To(string folder, bool verbose)
        {
            _verbose = verbose;
            _file = Path.Combine(folder, "nixx.log");
            _errors = Path.Combine(folder, "nixx-errors.log");
        }

        public void Info(string message) { _bepinex.LogInfo(message); if (_verbose) Write(_file, "INFO  " + message, ref _started); }
        public void Warn(string message) { _bepinex.LogWarning(message); if (_verbose) Write(_file, "WARN  " + message, ref _started); }
        public void Error(string message)
        {
            _bepinex.LogError(message);
            if (_verbose) Write(_file, "ERROR " + message, ref _started);
            Write(_errors, message, ref _errorsStarted);
        }

        private void Write(string path, string line, ref bool started)
        {
            if (path == null) return;
            try
            {
                lock (_gate)
                {
                    var text = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + "  " + line + Environment.NewLine;
                    if (!started)
                    {
                        started = true;
                        Directory.CreateDirectory(Path.GetDirectoryName(path));
                        File.WriteAllText(path, "=== 3dSen integration plugin, " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + Environment.NewLine + text);
                    }
                    else File.AppendAllText(path, text);
                }
            }
            catch { }
        }
    }
}
