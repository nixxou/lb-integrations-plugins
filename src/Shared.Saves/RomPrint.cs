// The fingerprint of the file LaunchBox launches, written into a save's id when its row is created.
//
// WHAT FILE. The additional version's ApplicationPath when the save belongs to one, else the game's -
// as it is on disk. An archive is fingerprinted as the archive: nothing is opened or unpacked (Mehdi,
// 08/10: "faut se focus sur l'appPath").
//
// HOW MUCH IS READ (Mehdi, 08/10). Up to 10 MB the whole file, one CRC32. Above, the first MB and,
// after a seek to the end, the last one: 2 MB read whatever the size, a few milliseconds on an SSD.
// No creation date: a copy changes it while the content stays the same.
//
// CACHED IN MEMORY, per session, on path + size + last write time: an unchanged file is read once.
// The last write time and not the creation time, because a file patched in place keeps its creation
// time and changes its content. Nothing is written to disk (Mehdi: no files beside anything).
//
// The CRC32 is System.IO.Hashing's, merged into each plugin like the other packages.

#nullable enable

using System;
using System.Collections.Concurrent;
using System.Globalization;
using System.IO;
using System.IO.Hashing;
using System.Threading;

namespace LbIntegrations.Saves
{
    internal static class RomPrint
    {
        /// <summary>Up to this size the whole file is read; above it, its first and last <see cref="Chunk"/>.</summary>
        public const long WholeLimit = 10L * 1024 * 1024;
        public const int Chunk = 1024 * 1024;

        private static readonly ConcurrentDictionary<string, (long Size, long WriteTicks, RomPrintData Print)> _cache
            = new(StringComparer.OrdinalIgnoreCase);

        private static int _reads;
        /// <summary>How many times a file was actually read (not answered from the cache) - for the self-test.</summary>
        public static int Reads => Volatile.Read(ref _reads);

        /// <summary>The fingerprint of <paramref name="fullPath"/>, or null when it is missing or unreadable.
        /// Never throws.</summary>
        public static RomPrintData? Of(string? fullPath)
        {
            if (string.IsNullOrEmpty(fullPath)) return null;
            try
            {
                var info = new FileInfo(fullPath);
                if (!info.Exists) return null;
                long size = info.Length, ticks = info.LastWriteTimeUtc.Ticks;
                if (_cache.TryGetValue(info.FullName, out var hit) && hit.Size == size && hit.WriteTicks == ticks)
                    return hit.Print;

                var print = Compute(info.FullName, size);
                if (print != null) _cache[info.FullName] = (size, ticks, print);
                return print;
            }
            catch { return null; }
        }

        private static RomPrintData? Compute(string path, long size)
        {
            Interlocked.Increment(ref _reads);
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete,
                                          Chunk, FileOptions.SequentialScan);
            if (size <= WholeLimit)
            {
                var crc = new Crc32();
                crc.Append(fs);
                return new RomPrintData { Size = size, Crc = Hex(crc.GetCurrentHashAsUInt32()) };
            }
            var buffer = new byte[Chunk];
            string head = Hex(Crc32.HashToUInt32(ReadExactly(fs, buffer)));
            fs.Seek(size - Chunk, SeekOrigin.Begin);
            string tail = Hex(Crc32.HashToUInt32(ReadExactly(fs, buffer)));
            return new RomPrintData { Size = size, Head = head, Tail = tail };
        }

        private static ReadOnlySpan<byte> ReadExactly(Stream s, byte[] buffer)
        {
            int got = 0;
            while (got < buffer.Length)
            {
                int n = s.Read(buffer, got, buffer.Length - got);
                if (n <= 0) break;
                got += n;
            }
            return new ReadOnlySpan<byte>(buffer, 0, got);
        }

        private static string Hex(uint crc) => crc.ToString("X8", CultureInfo.InvariantCulture);

        /// <summary>Forget everything - the self-test only.</summary>
        internal static void ClearCache() => _cache.Clear();
    }
}
