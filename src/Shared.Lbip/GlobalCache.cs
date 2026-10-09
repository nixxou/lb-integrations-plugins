// What the pack works out from a game's file and can always work out again - a disc id, a title id, a ROM's header -
// kept across sessions, installs and reinstalls, in the pack's global folder (Mehdi, 09/10):
// %LOCALAPPDATA%\lb-integrations-plugins\cache\<kind>.tsv, beside identity.ini and ramdisk.ini.
//
// ONLY DERIVED VALUES. A line is a file's FULL path, its size, its last write time (UTC ticks) and the value; a value
// is trusted only while the file keeps that size and that time, so a ROM replaced or rewritten is read again. Never
// a state of one install (xemu's and Cxbx's sync stamps stay with their console): every LaunchBox of this user reads
// these files, and a ROM analysed by one is known to the others.
//
// SEVERAL PROCESSES. LaunchBox and BigBox, or two installs, may read and write at once. A value is APPENDED under a
// named mutex; only records ended by a new line are read, and whatever follows the last one (a record a crash cut
// short - even inside its value - or the zeros a killed write leaves) is cut off before the next append; a
// process notices another's appends by the file's size and date and reads them in. When the file holds more than
// twice its live entries it is written again whole - to a temporary file, then moved over (never written in place:
// a LaunchBox killed mid-write left Data\InputBindings.xml full of zeros, 09/10). A file whose drive is there but which
// is gone is dropped then; an entry of a drive not plugged in is kept.
//
// Every plugin has its own copy of this class (merged into its dll); they share the files, not the memory.

#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;

namespace LbIntegrations.Lbip
{
    internal static class GlobalCache
    {
        private sealed class Table
        {
            public readonly Dictionary<string, (long Size, long Ticks, string Value)> Entries = new Dictionary<string, (long, long, string)>(StringComparer.OrdinalIgnoreCase);
            public long FileLength = -1;
            public DateTime FileWritten;
            public int Lines;
            /// <summary>When the file was last looked at (Environment.TickCount64).</summary>
            public long Checked;
        }

        private static readonly object Gate = new object();
        private static readonly Dictionary<string, Table> Tables = new Dictionary<string, Table>(StringComparer.OrdinalIgnoreCase);

        /// <summary>For the probe: another folder than the user's.</summary>
        internal static string? DirForTests;

        public static string Dir => DirForTests ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "lb-integrations-plugins", "cache");

        public static string PathOf(string kind) => Path.Combine(Dir, kind + ".tsv");

        /// <summary>How often a hit looks at the file for another process's newer value (Codex review 10/10: a value replaced
        /// elsewhere - "unknown" made known once an archive is unpacked - must not stay stale until a restart). A kind's
        /// name carries its version ("disc-ids-v1"): a plugin that works a value out differently moves to the next one.</summary>
        internal static int RecheckMs = 1000;

        /// <summary>The value kept for <paramref name="file"/> under <paramref name="kind"/>, when the file still has the size
        /// and date it had then. An empty value is a value ("read, nothing found"), for the caller to decide.</summary>
        public static bool TryGet(string kind, string? file, out string value)
        {
            value = "";
            try
            {
                if (!Stamp(file, out var full, out var size, out var ticks)) return false;
                lock (Gate)
                {
                    var t = TableOf(kind);
                    // Another process's newer lines, looked for at most once per RecheckMs (one stat of the file).
                    var now = Environment.TickCount64;
                    if (now - t.Checked >= RecheckMs) { Refresh(kind, t); t.Checked = now; }
                    if (t.Entries.TryGetValue(full, out var e) && e.Size == size && e.Ticks == ticks) { value = e.Value; return true; }
                    // Not known here: another process may have learnt it since.
                    if (Refresh(kind, t) && t.Entries.TryGetValue(full, out e) && e.Size == size && e.Ticks == ticks) { value = e.Value; return true; }
                }
            }
            catch { }
            return false;
        }

        /// <summary>Keep <paramref name="value"/> for <paramref name="file"/> as it is now (its size and date read here).</summary>
        public static void Set(string kind, string? file, string? value)
        {
            try
            {
                if (!Stamp(file, out var full, out var size, out var ticks)) return;
                value ??= "";
                lock (Gate)
                {
                    var t = TableOf(kind);
                    if (t.Entries.TryGetValue(full, out var e) && e.Size == size && e.Ticks == ticks && e.Value == value) return;
                    bool written = false;
                    Locked(kind, () =>
                    {
                        // What the others appended is read in first: the stamp noted below must not skip their lines.
                        Refresh(kind, t);
                        t.Entries[full] = (size, ticks, value);
                        written = true;
                        var path = PathOf(kind);
                        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                        var line = Escape(full) + "\t" + size + "\t" + ticks + "\t" + Escape(value) + "\n";
                        using (var fs = new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.Read))
                        {
                            // What follows the last new line is a record a crash cut short (or the zeros a killed write
                            // leaves): it is cut off, never completed - completed, a truncated value would become a hit
                            // (Codex review 10/10).
                            CutIncompleteTail(fs);
                            fs.Seek(0, SeekOrigin.End);
                            var bytes = Encoding.UTF8.GetBytes(line);
                            fs.Write(bytes, 0, bytes.Length);
                        }
                        t.Lines++;
                        Note(t, path);
                        if (t.Lines > 64 && t.Lines > 2 * t.Entries.Count) Compact(kind, t);
                    });
                    if (!written) t.Entries[full] = (size, ticks, value);   // the file busy: known to this process at least
                }
            }
            catch { }
        }

        // ── the file ─────────────────────────────────────────────────────────

        private static bool Stamp(string? file, out string full, out long size, out long ticks)
        {
            full = ""; size = 0; ticks = 0;
            if (string.IsNullOrWhiteSpace(file)) return false;
            var fi = new FileInfo(file);
            if (!fi.Exists) return false;
            full = fi.FullName; size = fi.Length; ticks = fi.LastWriteTimeUtc.Ticks;
            return true;
        }

        private static Table TableOf(string kind)
        {
            if (Tables.TryGetValue(kind, out var t)) return t;
            t = new Table();
            Tables[kind] = t;
            Refresh(kind, t);
            return t;
        }

        /// <summary>Read the file again when another process changed it. True when it was read.</summary>
        private static bool Refresh(string kind, Table t)
        {
            var path = PathOf(kind);
            var fi = new FileInfo(path);
            if (!fi.Exists) return false;
            if (fi.Length == t.FileLength && fi.LastWriteTimeUtc == t.FileWritten) return false;
            string[] lines;
            try
            {
                using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using var r = new StreamReader(fs, Encoding.UTF8);
                lines = r.ReadToEnd().Split('\n');
            }
            catch { return false; }
            t.Entries.Clear();
            t.Lines = 0;
            // Only records ended by a new line: the last piece (after the last '\n') is either empty or a record cut short.
            for (int i = 0; i < lines.Length - 1; i++)
            {
                var c = lines[i].TrimEnd('\r').Split('\t');
                if (c.Length != 4 || c[0].Length == 0 || !long.TryParse(c[1], out var size) || !long.TryParse(c[2], out var ticks)) continue;
                t.Entries[Unescape(c[0])] = (size, ticks, Unescape(c[3]));   // the last line of a file wins
                t.Lines++;
            }
            t.FileLength = fi.Length;
            t.FileWritten = fi.LastWriteTimeUtc;
            return true;
        }

        /// <summary>The file cut back to just after its last new line (empty when it has none). Read backwards in blocks: a
        /// long run of zeros left by a killed write is cut quickly.</summary>
        private static void CutIncompleteTail(FileStream fs)
        {
            long end = fs.Length;
            if (end == 0) return;
            var buf = new byte[64 * 1024];
            long pos = end;
            while (pos > 0)
            {
                int n = (int)Math.Min(buf.Length, pos);
                pos -= n;
                fs.Seek(pos, SeekOrigin.Begin);
                int read = 0;
                while (read < n) { int r = fs.Read(buf, read, n - read); if (r <= 0) break; read += r; }
                for (int i = read - 1; i >= 0; i--)
                    if (buf[i] == (byte)'\n')
                    {
                        long keep = pos + i + 1;
                        if (keep < end) fs.SetLength(keep);
                        return;
                    }
            }
            fs.SetLength(0);
        }

        private static void Note(Table t, string path)
        {
            try { var fi = new FileInfo(path); t.FileLength = fi.Length; t.FileWritten = fi.LastWriteTimeUtc; } catch { }
        }

        /// <summary>The file written again with one line per live entry - under the mutex, to a temporary file then moved over.</summary>
        private static void Compact(string kind, Table t)
        {
            var path = PathOf(kind);
            Refresh(kind, t);   // what the others appended is kept
            var keep = t.Entries.Where(kv => Keep(kv.Key)).ToList();
            var tmp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            File.WriteAllText(tmp, string.Concat(keep.Select(kv => Escape(kv.Key) + "\t" + kv.Value.Size + "\t" + kv.Value.Ticks + "\t" + Escape(kv.Value.Value) + "\n")), new UTF8Encoding(false));
            File.Move(tmp, path, overwrite: true);
            t.Entries.Clear();
            foreach (var kv in keep) t.Entries[kv.Key] = kv.Value;
            t.Lines = keep.Count;
            Note(t, path);
        }

        /// <summary>Dropped only when its file is gone from a drive that is there: a drive not plugged in keeps its entries.</summary>
        private static bool Keep(string full)
        {
            try
            {
                if (File.Exists(full)) return true;
                var root = Path.GetPathRoot(full);
                return string.IsNullOrEmpty(root) || !Directory.Exists(root);
            }
            catch { return true; }
        }

        private static void Locked(string kind, Action work)
        {
            using var m = new Mutex(false, @"Local\LbIntegrations.GlobalCache." + kind);
            bool held = false;
            try
            {
                try { held = m.WaitOne(TimeSpan.FromSeconds(5)); } catch (AbandonedMutexException) { held = true; }
                if (!held) return;   // another process is stuck in there: this value is simply not kept
                work();
            }
            finally { if (held) m.ReleaseMutex(); }
        }

        /// <summary>Only what would break a line: '%', tab, CR, LF as %25 %09 %0D %0A - a path stays readable as it is.</summary>
        private static string Escape(string s) => s.Replace("%", "%25").Replace("\t", "%09").Replace("\r", "%0D").Replace("\n", "%0A");

        private static string Unescape(string s)
            => s.IndexOf('%') < 0 ? s : s.Replace("%09", "\t").Replace("%0D", "\r").Replace("%0A", "\n").Replace("%25", "%");

        /// <summary>For the probe: forget what this process read, as another one starting would.</summary>
        internal static void ForgetForTests() { lock (Gate) Tables.Clear(); }
    }
}
