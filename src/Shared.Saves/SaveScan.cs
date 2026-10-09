// A big listing touches the disk once per folder instead of once per game (09/10, docs\saves-lot3.md §11c).
//
// At LaunchBox's start every plugin is handed ALL its games in one GetSaves (2,003 measured), and a listing asks, game
// after game, the same questions of the same folders and the same settings files: ~25 File.Exists per game for ares,
// its settings.bml parsed 3 times per game, a folder walked per game... SaveScan answers them from memory DURING ONE
// LISTING ONLY, and only a big one (more than Threshold games and versions): Game Saves, a close, a restore - one game -
// keep the real calls, and nothing is kept once the listing returns (Mehdi: no memory held outside the scan).
//
// THE SAME ANSWERS, NOT A NEW ALGORITHM. Exists / DirExists / Files answer exactly what File.Exists, Directory.Exists
// and Directory.GetFiles(dir, pattern) answer: a folder is listed once, the first time it is asked about, with .NET's
// own options (EnumerationOptions.Compatible: hidden and system files included) and its own pattern rules
// (FileSystemEnumerableFactory.NormalizeInputs: "*.*" and "." are "*", the rest TranslateWin32Expression +
// MatchesWin32Expression, case-insensitive). Anything unusual - a pattern holding a folder, a name ending in '.' or
// ' ', a ':' - goes to the real call. Text / Lines / Bytes read a file once per listing; Memo keeps a value derived
// from them. The probe compares both ways on tricky folders (SavesCheck.Scan).
//
// WRITES. A listing may write (state thumbnails, a capture, a .cue): whoever writes during a listing calls
// Touched(path) - the folder is listed again and the file read again the next time it is asked about. Nothing checks
// that a write was declared: code added to a listing that writes into a folder SaveScan answers for must call Touched,
// or that folder reads stale until the listing ends (the captures stay on the real calls for that reason).
//
// A folder that cannot be listed (permissions) answers through the real calls: FolderOf returns null.
//
// Per thread and per plugin: every plugin has its own copy of this class, and a listing is one call on one thread; a
// thread a plugin starts inside its listing simply makes the real calls.

#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Enumeration;
using System.Linq;
using System.Text;

namespace LbIntegrations.Saves
{
    internal sealed class SaveScan : IDisposable
    {
        /// <summary>Games and versions above which a listing is a big one.</summary>
        public const int Threshold = 50;

        [ThreadStatic] private static SaveScan? _current;
        private readonly SaveScan? _outer;
        private bool _closed;

        private sealed class Folder
        {
            public readonly List<(string Name, bool IsDir)> Entries = new List<(string, bool)>();
            public readonly Dictionary<string, bool> ByName = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            public bool Missing;
        }

        private readonly Dictionary<string, Folder> _folders = new Dictionary<string, Folder>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, object> _reads = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, object?> _memo = new Dictionary<string, object?>(StringComparer.Ordinal);

        private static readonly EnumerationOptions Compatible = new EnumerationOptions
        {
            // EnumerationOptions.Compatible, what Directory.GetFiles uses.
            AttributesToSkip = 0, IgnoreInaccessible = false, MatchType = MatchType.Win32,
            MatchCasing = MatchCasing.PlatformDefault, RecurseSubdirectories = false, ReturnSpecialDirectories = false,
        };

        private SaveScan(SaveScan? outer) { _outer = outer; }

        /// <summary>True while a big listing runs on this thread.</summary>
        public static bool Active => _current != null;

        /// <summary>A big listing starts (when <paramref name="items"/> is above Threshold); dispose it when it returns.
        /// A small one gets a scope that does nothing.</summary>
        public static IDisposable Begin(int items)
        {
            if (items <= Threshold || !LbIntegrations.Compat.Compat.On("saveScan")) return Nothing.Instance;   // off from the Compatibility page
            var s = new SaveScan(_current);
            _current = s;
            return s;
        }

        public void Dispose()
        {
            if (_closed) return;
            _closed = true;
            if (ReferenceEquals(_current, this)) _current = _outer;
            _folders.Clear(); _reads.Clear(); _memo.Clear();
        }

        private sealed class Nothing : IDisposable
        {
            public static readonly Nothing Instance = new Nothing();
            public void Dispose() { }
        }

        // ── the folders ──────────────────────────────────────────────────────

        /// <summary>File.Exists.</summary>
        public static bool Exists(string? path)
        {
            var s = _current;
            if (s == null || !s.Split(path, out var dir, out var name)) return File.Exists(path);
            var f = s.FolderOf(dir);
            if (f == null) return File.Exists(path);
            return f.ByName.TryGetValue(name, out var isDir) && !isDir;
        }

        /// <summary>Directory.Exists.</summary>
        public static bool DirExists(string? path)
        {
            var s = _current;
            if (s == null || !s.Split(path, out var dir, out var name)) return Directory.Exists(path);
            var f = s.FolderOf(dir);
            if (f == null) return Directory.Exists(path);
            return f.ByName.TryGetValue(name, out var isDir) && isDir;
        }

        /// <summary>Directory.GetFiles(<paramref name="dir"/>, <paramref name="pattern"/>), the folder alone.</summary>
        public static string[] Files(string dir, string pattern)
        {
            var s = _current;
            if (s == null || dir == null || pattern == null || pattern.IndexOfAny(new[] { '\\', '/', ':', '\0' }) >= 0 || dir.IndexOf('\0') >= 0)
                return Directory.GetFiles(dir!, pattern!);
            string full;
            try { full = Path.GetFullPath(dir); } catch { return Directory.GetFiles(dir, pattern); }
            var f = s.FolderOf(full);
            if (f == null || f.Missing) return Directory.GetFiles(dir, pattern);   // a missing folder: the real exception
            string expr = pattern;
            if (expr.Length == 0 || expr == "." || expr == "*.*") expr = "*";
            else if (expr != "*") expr = FileSystemName.TranslateWin32Expression(expr);
            var hits = new List<string>();
            foreach (var (n, isDir) in f.Entries)
                if (!isDir && FileSystemName.MatchesWin32Expression(expr.AsSpan(), n.AsSpan(), ignoreCase: true))
                    hits.Add(Path.Join(dir, n));
            return hits.ToArray();
        }

        /// <summary>The folder <paramref name="path"/> is in (or the folder itself) changed: asked again next time.</summary>
        public static void Touched(string? path)
        {
            var s = _current;
            if (s == null || string.IsNullOrWhiteSpace(path)) return;
            try
            {
                var full = Path.GetFullPath(path!).TrimEnd(Path.DirectorySeparatorChar);
                s._folders.Remove(full);
                var parent = Path.GetDirectoryName(full);
                if (parent != null) s._folders.Remove(parent);
                foreach (var k in s._reads.Keys.Where(k => k.StartsWith(full + "|", StringComparison.OrdinalIgnoreCase)).ToList()) s._reads.Remove(k);
                s._memo.Clear();   // values worked out from what changed
            }
            catch { s._folders.Clear(); s._reads.Clear(); s._memo.Clear(); }
        }

        /// <summary>The folder and the last name of a path an index can answer for; false for anything unusual.</summary>
        private bool Split(string? path, out string dir, out string name)
        {
            dir = name = "";
            if (string.IsNullOrEmpty(path) || path!.IndexOf('\0') >= 0) return false;
            var last = path[path.Length - 1];
            if (last == '\\' || last == '/' || last == '.' || last == ' ') return false;
            try
            {
                var full = Path.GetFullPath(path);
                var d = Path.GetDirectoryName(full);
                var n = Path.GetFileName(full);
                if (string.IsNullOrEmpty(d) || string.IsNullOrEmpty(n) || n.IndexOfAny(new[] { ':', '*', '?', '<', '>', '"', '|' }) >= 0) return false;
                dir = d!; name = n;
                return true;
            }
            catch { return false; }
        }

        /// <summary>The folder's entries, listed once; null when it cannot be listed (the real call then answers).</summary>
        private Folder? FolderOf(string dir)
        {
            if (_folders.TryGetValue(dir, out var f)) return f;
            f = new Folder();
            try
            {
                if (!Directory.Exists(dir)) f.Missing = true;
                else
                {
                    var e = new FileSystemEnumerable<(string, bool)>(dir, (ref FileSystemEntry x) => (x.FileName.ToString(), x.IsDirectory), Compatible);
                    foreach (var (n, isDir) in e)
                    {
                        f.Entries.Add((n, isDir));
                        f.ByName[n] = isDir;
                    }
                }
            }
            catch { return null; }
            _folders[dir] = f;
            return f;
        }

        // ── the files read ───────────────────────────────────────────────────

        /// <summary>File.ReadAllText.</summary>
        public static string Text(string path) => Read(path, "text", () => File.ReadAllText(path));

        /// <summary>File.ReadAllText with an encoding.</summary>
        public static string Text(string path, Encoding encoding) => Read(path, "text:" + encoding.WebName, () => File.ReadAllText(path, encoding));

        /// <summary>File.ReadAllLines (a copy: the caller may change it).</summary>
        public static string[] Lines(string path) => (string[])Read(path, "lines", () => File.ReadAllLines(path)).Clone();

        /// <summary>File.ReadAllLines with an encoding (a copy).</summary>
        public static string[] Lines(string path, Encoding encoding) => (string[])Read(path, "lines:" + encoding.WebName, () => File.ReadAllLines(path, encoding)).Clone();

        /// <summary>File.ReadAllBytes (a copy).</summary>
        public static byte[] Bytes(string path) => (byte[])Read(path, "bytes", () => File.ReadAllBytes(path)).Clone();

        private static T Read<T>(string path, string how, Func<T> read) where T : class
        {
            var s = _current;
            if (s == null || string.IsNullOrEmpty(path)) return read();
            string key;
            try { key = Path.GetFullPath(path) + "|" + how; } catch { return read(); }
            if (s._reads.TryGetValue(key, out var v)) return (T)v;
            var r = read();   // a failure is not kept: the next call fails the same way
            s._reads[key] = r;
            return r;
        }

        /// <summary>A value worked out once per big listing (outside one: worked out every time).</summary>
        public static T Memo<T>(string key, Func<T> make)
        {
            var s = _current;
            if (s == null) return make();
            if (s._memo.TryGetValue(key, out var v)) return (T)v!;
            var r = make();
            s._memo[key] = r;
            return r;
        }
    }
}
