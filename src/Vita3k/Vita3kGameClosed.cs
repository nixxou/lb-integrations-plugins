// Vita3K's own log, read as it is written, for the one line that says a game ended:
//
//     [MainWindow::on_game_closed]: Game closed: <title>
//
// written the moment the game window closes (gui-qt/src/main_window.cpp, on_game_closed), BEFORE the
// session is stopped - so it is there even when the stop hangs. The log is portable\vita3k.log when
// portable\ exists (app_init.cpp, set_log_path), which it always does under this plugin, and Vita3K
// rewrites it at every start: read from the start, and from the start again if it shrinks.
//
// An INFO line: a log level above INFO (config.yml, log-level > 2) hides it, and then nothing is seen -
// the watcher simply waits for the program to end, as before.

using System;
using System.IO;
using System.Text;

namespace LbIntegrations.Vita3k
{
    internal sealed class Vita3kGameClosed
    {
        private const string Marker = "[MainWindow::on_game_closed]: Game closed";
        private readonly string _path;
        private long _offset;
        private string _carry = "";

        public Vita3kGameClosed(Vita3kLayout layout)
        {
            var portable = Vita3kPaths.PortableDirOf(layout?.InstallDir);
            _path = portable == null ? null : Path.Combine(portable, "vita3k.log");
        }

        /// <summary>Has the line appeared since the last look? Never throws.</summary>
        public bool Seen()
        {
            if (_path == null) return false;
            try
            {
                using var f = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                if (f.Length < _offset) { _offset = 0; _carry = ""; }   // rewritten: a new run
                if (f.Length == _offset) return false;
                f.Seek(_offset, SeekOrigin.Begin);
                var buffer = new byte[Math.Min(f.Length - _offset, 4 * 1024 * 1024)];
                int read = f.Read(buffer, 0, buffer.Length);
                _offset += read;
                var text = _carry + Encoding.UTF8.GetString(buffer, 0, read);
                bool seen = text.IndexOf(Marker, StringComparison.Ordinal) >= 0;
                // A line cut in two by the read is kept for the next one.
                int last = text.LastIndexOf('\n');
                _carry = last < 0 ? text : text.Substring(last + 1);
                if (_carry.Length > Marker.Length * 4) _carry = _carry.Substring(_carry.Length - Marker.Length * 4);
                return seen;
            }
            catch { return false; }
        }
    }
}
