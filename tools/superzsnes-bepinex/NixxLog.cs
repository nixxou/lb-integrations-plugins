// Where this plugin's lines go.
//
// BepInEx is deployed SILENT by the pack - no console, no disk log - because a frontend's user
// should see the game and nothing else. That leaves the day something breaks, after an emulator
// update typically, with nothing to read. So every line this plugin writes goes to BepInEx's own
// logger as usual (which shows when BepInEx.cfg turns logging back on) AND, when the emulator was
// started with --nixx-log, to <exe>\portable\nixx.log, truncated at each start. The runbook in
// BepInEx\nixx-docs says to tick "Write the plugin's diagnostic log" in the Nixx window, launch,
// and read that file.
//
// Errors are the exception: a patch that could not be applied is written to the file EVEN WITHOUT
// --nixx-log, as portable\nixx-errors.log, because that is the one line whoever opens the folder
// after an update needs, and asking them to relaunch with a flag to see it would be unkind.

using System;
using System.IO;
using BepInEx.Logging;

namespace LbIntegrations.SuperZsnes.Mod
{
    internal sealed class NixxLog
    {
        private readonly ManualLogSource _bepinex;
        private readonly string _file;
        private readonly string _errors;
        private readonly bool _verbose;
        private readonly object _gate = new object();
        private bool _started, _errorsStarted;

        public NixxLog(ManualLogSource bepinex, string portableRoot, bool verbose)
        {
            _bepinex = bepinex;
            _verbose = verbose;
            _file = Path.Combine(portableRoot, "nixx.log");
            _errors = Path.Combine(portableRoot, "nixx-errors.log");
        }

        public void LogInfo(string message) { _bepinex.LogInfo(message); if (_verbose) Write(_file, "INFO  " + message, ref _started); }
        public void LogWarning(string message) { _bepinex.LogWarning(message); if (_verbose) Write(_file, "WARN  " + message, ref _started); }
        public void LogError(string message)
        {
            _bepinex.LogError(message);
            if (_verbose) Write(_file, "ERROR " + message, ref _started);
            Write(_errors, message, ref _errorsStarted);
        }

        private void Write(string path, string line, ref bool started)
        {
            try
            {
                lock (_gate)
                {
                    var text = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + "  " + line + Environment.NewLine;
                    if (!started)
                    {
                        started = true;
                        Directory.CreateDirectory(Path.GetDirectoryName(path));
                        File.WriteAllText(path, "=== SUPER ZSNES integration plugin, " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + Environment.NewLine + text);
                    }
                    else File.AppendAllText(path, text);
                }
            }
            catch { }
        }
    }
}
