// Where a DSiWare title's metadata comes from, in order of preference.
//
// A .tmd is needed to install a title into a NAND, and most DSiWare dumps are the .nds alone. Four
// sources, tried in this order:
//
//   1. beside the ROM        <rom>.nds.tmd - what a complete dump ships, and what the user chose
//   2. the carried index     tmd-library.bin, embedded in this assembly - about 1700 titles
//   3. Nintendo's server     one request, for a title the index does not have
//   4. built from the ROM    a last resort, unsigned, and the log says so
//
// THE REVISION IS CHOSEN BY HASH, NOT BY DATE. A title can have several revisions - about a hundred
// do - and a TMD carries the SHA-1 of the content it describes. So the right one for a given ROM is
// the one whose hash IS that ROM's, which is a question with an exact answer rather than a guess at
// "probably the newest". Only when none matches does the newest win, and the log says that it had to.
//
// NOTHING IS INFLATED TO FIND A TITLE. The index is a sorted table of 36-byte records carrying the
// title id, the revision, the content hash and where the metadata sits; it is stored UNCOMPRESSED, so
// finding a title is a binary search over about eleven records and choosing between its revisions
// needs only the hashes already in those records.
//
// Only the winner's own block is then inflated: sixteen entries, 8 KB, out of which 520 bytes are
// copied. The buffer is a local and nothing is cached in a static, so it is collectable the moment
// this returns - which matters because this runs inside LaunchBox's process, not ours.
//
// The format is written out in tools\pack-tmd-library.ps1.

using System;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;

namespace LbIntegrations.MelonDs
{
    internal static class MelonDsTmd
    {
        /// <summary>The embedded index's resource name, as the csproj declares it.</summary>
        private const string ResourceName = "tmd-library.bin";

        private const int HeaderBytes = 20;      // magic 8, count 4, blocks 4, perBlock 2, reserved 2
        private const int RecordBytes = 36;      // titleId 8, version 2, block 2, offset 2, length 2, sha1 20
        private const int BlockEntryBytes = 8;   // offset 4, length 4
        private const int Sha1Bytes = 20;

        /// <summary>A TMD is 520 bytes; a downloaded one carries certificates after it, which melonDS
        /// never reads and the index does not keep. Anything shorter is not metadata.</summary>
        private const int MinimumBytes = 520;

        /// <summary>A guard on what an inflated block may cost, so a damaged file cannot ask for an
        /// arbitrary allocation. Sixteen entries of 520 bytes is 8 KB; this is far above it.</summary>
        private const int BlockCeiling = 1 << 20;

        /// <summary>Where this title's metadata is, or null when it has to be built. Never throws.
        /// <paramref name="source"/> says which of the four it was, for the log.</summary>
        /// <param name="romPath">The .nds itself - hashed, to pick the revision that matches it. For
        /// a title that came out of an archive this is the unpacked copy.</param>
        /// <param name="besidePath">Where to look for a .tmd the user put there. Normally the same
        /// file; for an archive it is the ARCHIVE, because that is where somebody would have put one
        /// and a temporary copy has nothing beside it.</param>
        public static string Resolve(MelonDsLayout layout, NdsRom rom, string romPath,
                                     string besidePath, out string source)
        {
            source = null;

            var beside = Usable((besidePath ?? romPath) + ".tmd");
            if (beside != null) { source = "the .tmd beside the ROM"; return beside; }

            var carried = FromIndex(layout, rom, romPath, out var which);
            if (carried != null) { source = which; return carried; }

            var kept = KeptPath(layout, rom.TitleId);
            MoveOldCopy(layout, rom.TitleId, kept);
            var already = Usable(kept);
            if (already != null) { source = "a copy fetched earlier"; return already; }

            var fetched = MelonDsNus.TmdFor(rom.TitleId, kept, out var why);
            if (fetched != null) { source = "Nintendo's update server"; return fetched; }

            Log.Info("no metadata for " + rom.AssetName + " - " + why
                     + "; one will be built from the ROM, unsigned");
            return null;
        }

        /// <summary>The revision of this title, from the carried index, whose content hash is the
        /// ROM's. Written out beside the title's NAND, because the library that installs it takes a
        /// path rather than bytes.</summary>
        private static string FromIndex(MelonDsLayout layout, NdsRom rom, string romPath, out string which)
        {
            which = null;
            try
            {
                using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourceName);
                if (stream == null || !stream.CanSeek) return null;

                var header = ReadAt(stream, 0, HeaderBytes);
                if (header == null || header[0] != 'M' || header[1] != 'D' || header[2] != 'S'
                    || header[3] != 'T' || header[4] != 'M' || header[5] != 'D' || header[7] != 2) return null;

                int count = BitConverter.ToInt32(header, 8);
                int blocks = BitConverter.ToInt32(header, 12);
                if (count <= 0 || blocks <= 0) return null;

                long table = HeaderBytes + (long)count * RecordBytes;       // where the block table is
                long payload = table + (long)blocks * BlockEntryBytes;      // where the blocks start

                var wanted = TitleIdBytes(rom.TitleId);
                if (wanted == null) return null;

                int first = FirstRecord(stream, count, wanted);
                if (first < 0) return null;

                var sha = Sha1Of(romPath);
                byte[] chosen = null;
                int chosenVersion = -1;
                bool matched = false;

                // Revisions of one title are adjacent, because the table is sorted by title id. This
                // reads records only - no block is inflated to compare hashes.
                for (int i = first; i < count; i++)
                {
                    var record = ReadAt(stream, HeaderBytes + (long)i * RecordBytes, RecordBytes);
                    if (record == null || Compare(record, wanted) != 0) break;

                    int version = BitConverter.ToUInt16(record, 8);
                    if (sha != null && Same(record, 16, sha)) { chosen = record; chosenVersion = version; matched = true; break; }
                    if (version > chosenVersion) { chosen = record; chosenVersion = version; }
                }
                if (chosen == null) return null;

                var tmd = Extract(stream, table, payload,
                                  BitConverter.ToUInt16(chosen, 10),    // which block
                                  BitConverter.ToUInt16(chosen, 12),    // where in it
                                  BitConverter.ToUInt16(chosen, 14));   // how long
                if (tmd == null || tmd.Length < MinimumBytes) return null;

                var target = KeptPath(layout, rom.TitleId);
                if (target == null) return null;
                Directory.CreateDirectory(Path.GetDirectoryName(target));
                MelonDsToml.WriteAtomicBytes(target, tmd);

                which = matched
                    ? "the carried index, revision " + chosenVersion + " - it matches this ROM"
                    : "the carried index, revision " + chosenVersion + " - no revision matches this ROM";
                if (!matched)
                    Log.Info(rom.AssetName + ": no metadata revision matches this dump's hash; using "
                             + "revision " + chosenVersion + ". If the DSi menu refuses it, the ROM is "
                             + "a revision the index does not have.");
                return target;
            }
            catch (Exception ex) { Log.Verbose("could not read the carried index - " + ex.Message); return null; }
        }

        /// <summary>One entry's 520 bytes, by inflating the one block that holds it. The inflated
        /// block is a local: it goes out of scope with this call, and only the slice survives.</summary>
        private static byte[] Extract(Stream stream, long table, long payload, int block, int offset, int length)
        {
            var entry = ReadAt(stream, table + (long)block * BlockEntryBytes, BlockEntryBytes);
            if (entry == null || length <= 0) return null;

            var compressed = ReadAt(stream, payload + BitConverter.ToUInt32(entry, 0),
                                    (int)BitConverter.ToUInt32(entry, 4));
            if (compressed == null) return null;

            using var input = new MemoryStream(compressed, writable: false);
            using var deflate = new DeflateStream(input, CompressionMode.Decompress);

            // Read only as far as this entry reaches: the tail of the block belongs to other titles,
            // and there is no reason to hold it.
            int end = offset + length;
            if (end > BlockCeiling) return null;
            var inflated = new byte[end];
            int total = 0, read;
            while (total < end && (read = deflate.Read(inflated, total, end - total)) > 0) total += read;
            if (total != end) return null;

            var tmd = new byte[length];
            Buffer.BlockCopy(inflated, offset, tmd, 0, length);
            return tmd;
        }

        /// <summary>The first record for this title id, by binary search, or -1. Records are fixed
        /// width and sorted, so this reads about eleven of them out of nearly two thousand.</summary>
        private static int FirstRecord(Stream stream, int count, byte[] wanted)
        {
            int low = 0, high = count - 1, found = -1;
            while (low <= high)
            {
                int middle = low + (high - low) / 2;
                var record = ReadAt(stream, HeaderBytes + (long)middle * RecordBytes, RecordBytes);
                if (record == null) return -1;

                int order = Compare(record, wanted);
                if (order == 0) { found = middle; high = middle - 1; }   // keep looking left
                else if (order < 0) low = middle + 1;
                else high = middle - 1;
            }
            return found;
        }

        /// <summary>A record's title id against the one wanted. Big-endian bytes compare in order,
        /// which is why they are stored as they are spelled.</summary>
        private static int Compare(byte[] record, byte[] wanted)
        {
            for (int i = 0; i < 8; i++)
                if (record[i] != wanted[i]) return record[i] < wanted[i] ? -1 : 1;
            return 0;
        }

        private static byte[] TitleIdBytes(string titleId)
        {
            try
            {
                if (titleId == null || titleId.Length != 16) return null;
                var bytes = new byte[8];
                for (int i = 0; i < 8; i++)
                    bytes[i] = Convert.ToByte(titleId.Substring(i * 2, 2), 16);
                return bytes;
            }
            catch { return null; }
        }

        private static byte[] ReadAt(Stream stream, long offset, int length)
        {
            try
            {
                if (offset < 0 || length <= 0 || offset + length > stream.Length) return null;
                stream.Seek(offset, SeekOrigin.Begin);
                var buffer = new byte[length];
                int total = 0, read;
                while (total < length && (read = stream.Read(buffer, total, length - total)) > 0) total += read;
                return total == length ? buffer : null;
            }
            catch { return null; }
        }

        /// <summary>Where a resolved TMD is kept: dsi\tmd\, and NOT in the title's own folder.
        ///
        /// IT DESCRIBES THE TITLE, NOT THE SAVE. It used to sit beside the state folder, which was
        /// tidy and wrong: deleting a save takes the whole title folder, so it took the metadata
        /// with it. For the 1686 titles the carried index holds that costs nothing. For one that
        /// came from Nintendo's server it costs a second download - and, on a machine that is
        /// offline when the game is next launched, it costs the metadata altogether: the fallback is
        /// a TMD built from the ROM, unsigned, which the DSi menu may refuse.
        ///
        /// Nothing about a title's metadata is changed by somebody deleting their progress.</summary>
        private static string KeptPath(MelonDsLayout layout, string titleId)
        {
            var dsi = MelonDsDsi.DsiDir(layout);
            if (dsi == null || string.IsNullOrWhiteSpace(titleId)) return null;
            return Path.Combine(dsi, KeptDirName, titleId + ".tmd");
        }

        private const string KeptDirName = "tmd";

        /// <summary>A copy kept under the old arrangement, moved rather than re-fetched. One
        /// File.Exists on a path we were about to look at anyway.</summary>
        private static void MoveOldCopy(MelonDsLayout layout, string titleId, string target)
        {
            try
            {
                if (target == null || File.Exists(target)) return;
                var dir = MelonDsDsi.TitleDir(layout, titleId);
                var old = dir == null ? null : Path.Combine(dir, "title.tmd");
                if (old == null || !File.Exists(old)) return;

                Directory.CreateDirectory(Path.GetDirectoryName(target));
                File.Move(old, target);
                Log.Verbose("moved the kept metadata of " + titleId + " out of its save folder");
            }
            catch (Exception ex) { Log.Verbose("could not move a kept TMD - " + ex.Message); }
        }

        private static byte[] Sha1Of(string path)
        {
            try
            {
                using var stream = File.OpenRead(path);
                using var sha = SHA1.Create();
                return sha.ComputeHash(stream);
            }
            catch { return null; }
        }

        private static bool Same(byte[] haystack, int offset, byte[] needle)
        {
            if (needle.Length != Sha1Bytes || haystack.Length < offset + Sha1Bytes) return false;
            for (int i = 0; i < Sha1Bytes; i++)
                if (haystack[offset + i] != needle[i]) return false;
            return true;
        }

        private static string Usable(string path)
        {
            try
            {
                return !string.IsNullOrWhiteSpace(path) && File.Exists(path)
                       && new FileInfo(path).Length >= MinimumBytes
                    ? path : null;
            }
            catch { return null; }
        }
    }
}
