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
        public static T Get<T>(string? file, string key, Func<T> make)
        {
            if (string.IsNullOrWhiteSpace(file)) return make();
            long length = -1, ticks = 0;
            try { var fi = new FileInfo(file); if (fi.Exists) { length = fi.Length; ticks = fi.LastWriteTimeUtc.Ticks; } }
            catch { return make(); }
            var k = key + "|" + file;
            if (Values.TryGetValue(k, out var c) && c.Length == length && c.Ticks == ticks && c.Value is T hit) return hit;
            if (Values.TryGetValue(k, out c) && c.Length == length && c.Ticks == ticks && c.Value == null && default(T) == null) return default!;
            var v = make();
            if (Values.Count > 256) Values.Clear();
            Values[k] = (length, ticks, v);
            return v;
        }
    }
}
