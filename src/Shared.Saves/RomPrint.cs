// The fingerprint of the file LaunchBox launches, written into a save's id when its row is created.
//
// WHAT FILE. The additional version's ApplicationPath when the save belongs to one, else the game's -
// as it is on disk. An archive is fingerprinted as the archive: nothing is opened or unpacked (Mehdi,
// 08/10: "faut se focus sur l'appPath").
//
// HOW MUCH IS READ (Mehdi, 08/10). Up to 10 MB the whole file, one CRC32 - a proof. Above, the CRC32
// of the first MB, the middle MB and the last MB, reached by seeks: 3 MB read whatever the size. That
// one is only a SAMPLE - two variants of one disc with the same size and a patch outside the three
// samples print the same - which is why SaveRows only acts on it when exactly one file matches
// (Codex review, 08/10). No creation date: a copy changes it while the content stays the same.
//
// A FILE THAT CHANGES UNDER US GETS NO PRINT (Codex review, 08/10). A print is frozen into the id for
// ever, so a wrong one is worse than none. The file is opened with no other writer allowed, its size
// taken from that handle, a short read refused, and its last write time compared before and after:
// any doubt answers null and the id simply carries no "rom". A disk image the emulator writes to (a
// VHDX, a qcow2...) changes by design and is never printed (see Writable).
//
// CACHED IN MEMORY, per session, on path + size + last write time: an unchanged file is read once.
// The last write time and not the creation time, because a file patched in place keeps its creation
// time and changes its content. Nothing is written to disk.
//
// The CRC32 is System.IO.Hashing's, merged into each plugin like the other packages.

#nullable enable

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Hashing;
using System.Threading;

namespace LbIntegrations.Saves
{
    internal static class RomPrint
    {
        /// <summary>Up to this size the whole file is read; above it, three <see cref="Chunk"/> samples.</summary>
        public const long WholeLimit = 10L * 1024 * 1024;
        public const int Chunk = 1024 * 1024;

        /// <summary>Disk images an emulator writes to while it runs: their content changes by design, so a
        /// print taken once would never match again. A plugin can also refuse a print for its own reasons.</summary>
        private static readonly HashSet<string> Writable = new(StringComparer.OrdinalIgnoreCase)
        {
            ".vhdx", ".vhd", ".avhdx", ".qcow2", ".qcow", ".vmdk", ".vdi",
        };

        private static readonly ConcurrentDictionary<string, (long Size, long WriteTicks, RomPrintData Print)> _cache
            = new(StringComparer.OrdinalIgnoreCase);

        private static int _reads;
        /// <summary>How many times a file was actually read (not answered from the cache) - for the self-test.</summary>
        public static int Reads => Volatile.Read(ref _reads);

        /// <summary>A hook for the self-test: runs between the two halves of a read, to change the file under it.</summary>
        internal static Action<string>? DuringRead;

        /// <summary>True when <paramref name="path"/> is an image its emulator writes to - never printed.</summary>
        public static bool IsWritableImage(string? path)
            => !string.IsNullOrEmpty(path) && Writable.Contains(Path.GetExtension(path));

        /// <summary>The fingerprint of <paramref name="fullPath"/>, or null when it is missing, unreadable, being
        /// written to, changed while read, or a writable image. Never throws.</summary>
        public static RomPrintData? Of(string? fullPath)
        {
            if (string.IsNullOrEmpty(fullPath) || IsWritableImage(fullPath)) return null;
            try
            {
                var info = new FileInfo(fullPath);
                if (!info.Exists) return null;
                if (_cache.TryGetValue(info.FullName, out var hit) && hit.Size == info.Length && hit.WriteTicks == info.LastWriteTimeUtc.Ticks)
                    return hit.Print;

                var (print, size, ticks) = Compute(info.FullName);
                if (print != null) _cache[info.FullName] = (size, ticks, print);
                return print;
            }
            catch { return null; }
        }

        private static (RomPrintData? Print, long Size, long Ticks) Compute(string path)
        {
            Interlocked.Increment(ref _reads);
            long before = File.GetLastWriteTimeUtc(path).Ticks;
            // FileShare.Read: nobody may write or replace the file while it is read. A file somebody is
            // already writing to fails to open here - and gets no print, which is the safe answer.
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, Chunk, FileOptions.None);
            long size = fs.Length;
            RomPrintData? print;
            if (size <= WholeLimit)
            {
                var all = new byte[size];
                if (!ReadFully(fs, all, 0)) return (null, size, before);
                DuringRead?.Invoke(path);
                print = new RomPrintData { Size = size, Crc = Hex(Crc32.HashToUInt32(all)) };
            }
            else
            {
                var buffer = new byte[Chunk];
                if (!ReadFully(fs, buffer, 0)) return (null, size, before);
                string head = Hex(Crc32.HashToUInt32(buffer));
                DuringRead?.Invoke(path);
                if (!ReadFully(fs, buffer, size / 2 - Chunk / 2)) return (null, size, before);
                string mid = Hex(Crc32.HashToUInt32(buffer));
                if (!ReadFully(fs, buffer, size - Chunk)) return (null, size, before);
                string tail = Hex(Crc32.HashToUInt32(buffer));
                print = new RomPrintData { Size = size, Head = head, Mid = mid, Tail = tail };
            }
            long after = File.GetLastWriteTimeUtc(path).Ticks;
            if (after != before || fs.Length != size) return (null, size, before);
            return (print, size, before);
        }

        /// <summary>Fills <paramref name="buffer"/> from <paramref name="offset"/>; false on a short read.</summary>
        private static bool ReadFully(FileStream fs, byte[] buffer, long offset)
        {
            fs.Seek(offset, SeekOrigin.Begin);
            int got = 0;
            while (got < buffer.Length)
            {
                int n = fs.Read(buffer, got, buffer.Length - got);
                if (n <= 0) return false;
                got += n;
            }
            return true;
        }

        private static string Hex(uint crc) => crc.ToString("X8", CultureInfo.InvariantCulture);

        /// <summary>Forget everything - the self-test only.</summary>
        internal static void ClearCache() => _cache.Clear();
    }
}
