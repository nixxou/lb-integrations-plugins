// A value worked out from a settings file, kept in memory while that file keeps its last write time and length (10/10).
//
// The Game Saves page asks a plugin hundreds of times per opening whether a row is active (82, 342, 176 calls measured),
// and several plugins read and parsed their emulator's whole settings file on every one of those calls: SUPER ZSNES's
// NRBF settings (406 us a call), snes9x.conf, bsnes's settings.bml, Flycast's emu.cfg, melonDS.toml, Cxbx's ini. Here one
// stat replaces the read and the parse; a file the emulator rewrites (at its exit, a setting changed) is read again.
//
// FOR READERS ONLY. The value is shared by every caller: what goes in must be read, never changed - a caller that edits
// a settings object (and writes it back) loads its own. Process memory, a few entries per plugin; every plugin has its
// own copy of this class.

#nullable enable

using System;
using System.Collections.Concurrent;
using System.IO;

namespace LbIntegrations.Lbip
{
    internal static class StampedCache
    {
        private static readonly ConcurrentDictionary<string, (long Length, long Ticks, object? Value)> Values
            = new ConcurrentDictionary<string, (long, long, object?)>(StringComparer.OrdinalIgnoreCase);

        /// <summary>The value <paramref name="make"/> works out from <paramref name="file"/>, kept while the file keeps its date and
        /// length (a missing file is a state too: kept until it appears). <paramref name="key"/> tells apart two values of one file.</summary>
        public static T Get<T>(string? file, string key, Func<T> make) => Get(file, key, make, keepNull: true);

        /// <summary>The same; <paramref name="keepNull"/> false: a null (a reader's "could not read") is never kept.</summary>
        public static T Get<T>(string? file, string key, Func<T> make, bool keepNull)
        {
            if (string.IsNullOrWhiteSpace(file)) return make();
            long length = -1, ticks = 0;
            try { var fi = new FileInfo(file); if (fi.Exists) { length = fi.Length; ticks = fi.LastWriteTimeUtc.Ticks; } }
            catch { return make(); }
            var k = key + "|" + file;
            if (Values.TryGetValue(k, out var c) && c.Length == length && c.Ticks == ticks && c.Value is T hit) return hit;
            if (Values.TryGetValue(k, out c) && c.Length == length && c.Ticks == ticks && c.Value == null && default(T) == null) return default!;
            // The readers swallow their failures (a default, a null), so a value worked out while the file could not be opened -
            // an emulator rewriting it at its exit - is no value to keep: it is returned, not kept, and the next call reads
            // again (Codex review 10/10). Looked at before AND after: a lock taken during the read is caught too.
            bool readable = length < 0 || CanRead(file);
            var v = make();
            if (!readable || (length >= 0 && !CanRead(file))) return v;
            if (v == null && !keepNull) return v;
            if (Values.Count > 256) Values.Clear();
            Values[k] = (length, ticks, v);
            return v;
        }

        /// <summary>Opened as the readers open it - File.ReadAllText, ZipFile.OpenRead: sharing READ only. A file another program
        /// holds open for writing (an emulator rewriting its settings, sharing them for reading) fails here as it fails them;
        /// sharing ReadWrite would have passed while they failed (Codex review 10/10). A more lenient reader loses nothing: its
        /// value is just not kept this time.</summary>
        private static bool CanRead(string file)
        {
            try { using (new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read)) return true; }
            catch { return false; }
        }
    }
}
