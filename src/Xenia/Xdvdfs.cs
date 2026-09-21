// SPDX-License-Identifier: MPL-2.0
//
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of
// the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
//
// Derived from sigil's src/xdvdfs.c (MPL-2.0), the save-identification library bundled with Argosy
// (github.com/abduznik/Freegosy ecosystem / argosy-launcher). The structure, the two-magic
// validation and the partition-base probing order are its work; the C# is ours. Cross-checked
// against Xenia's own src/xenia/vfs/devices/disc_image_device.cc, which probes a different set of
// bases - this file takes the union of the two, which is why there are six.
//
// ---
//
// XDVDFS is the filesystem on Xbox and Xbox 360 discs. It is not ISO9660 and shares nothing with it
// beyond the 2048-byte sector.
//
//   volume descriptor, at <partition base> + 0x10000:
//     +0x000  char[20]  "MICROSOFT*XBOX*MEDIA"
//     +0x014  u32 LE    root directory sector, counted from the partition base
//     +0x018  u32 LE    root directory size, in bytes
//     +0x7EC  char[20]  the same magic again, filling the sector to 2048
//
//   directory entry, at <table> + ordinal * 4:
//     +0x00  u16 LE  left child ordinal   (0 = none)
//     +0x02  u16 LE  right child ordinal  (0 = none)
//     +0x04  u32 LE  first sector
//     +0x08  u32 LE  length in bytes
//     +0x0C  u8      attributes           (0x10 = directory)
//     +0x0D  u8      name length
//     +0x0E  char[]  name, not NUL-terminated
//
// The magic is required TWICE. That is not belt and braces: a disc image is mostly game data, and a
// stray copy of the string inside it would otherwise be read as a partition header.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace LbIntegrations.Xenia
{
    internal sealed class XdvdfsEntry
    {
        public string Name;
        public long Offset;        // absolute, already rebased on the partition
        public uint Length;
        public bool IsDirectory;
    }

    internal static class Xdvdfs
    {
        private const int SectorSize = 2048;
        private const long VolumeDescriptorOffset = 0x10000;
        private const int MagicTrailingOffset = 0x7EC;
        private const int MagicLength = 20;
        private const string Magic = "MICROSOFT*XBOX*MEDIA";
        private const byte AttributeDirectory = 0x10;

        /// <summary>The union of Xenia's five probe offsets and sigil's four, ascending. Ascending
        /// matters for a reader that can only seek forward cheaply.</summary>
        private static readonly long[] PartitionBases =
        {
            0,            // already trimmed
            0x0000FB20,   // 64288
            0x00020600,   // 132096
            0x02080000,   // 34078720   XGD3
            0x0FD90000,   // 265879552  XGD2
            0x18300000,   // 405798912  XGD1
        };

        /// <summary>A file at the root of the disc, or null. <paramref name="read"/> answers in
        /// absolute image offsets.</summary>
        public static XdvdfsEntry FindRootFile(Func<long, int, byte[]> read, string name)
        {
            foreach (var entry in RootEntries(read))
                if (string.Equals(entry.Name, name, StringComparison.OrdinalIgnoreCase) && !entry.IsDirectory)
                    return entry;
            return null;
        }

        /// <summary>Every entry in the root directory. Empty when this is not an XDVDFS image.</summary>
        public static List<XdvdfsEntry> RootEntries(Func<long, int, byte[]> read)
        {
            var found = new List<XdvdfsEntry>();
            if (!TryFindPartition(read, out long partitionBase, out uint rootSector, out uint rootSize))
                return found;

            long tableOffset = partitionBase + (long)rootSector * SectorSize;
            var table = read(tableOffset, (int)rootSize);
            if (table == null) return found;

            // The tree is sorted, but its collation is not ours, so every node is walked rather than
            // descended: a root directory is a sector or two and the whole thing costs one read.
            var pending = new Stack<uint>();
            var seen = new HashSet<uint>();
            pending.Push(0);
            while (pending.Count > 0)
            {
                uint ordinal = pending.Pop();
                if (!seen.Add(ordinal)) continue;                 // a corrupt table can loop
                if (seen.Count > 4096) break;

                long at = (long)ordinal * 4;
                if (at < 0 || at + 0x0E > table.Length) continue;

                ushort left = (ushort)(table[at] | (table[at + 1] << 8));
                ushort right = (ushort)(table[at + 2] | (table[at + 3] << 8));
                uint sector = LeUInt32(table, at + 4);
                uint length = LeUInt32(table, at + 8);
                byte attributes = table[at + 0x0C];
                int nameLength = table[at + 0x0D];

                if (nameLength > 0 && at + 0x0E + nameLength <= table.Length)
                {
                    found.Add(new XdvdfsEntry
                    {
                        Name = Encoding.ASCII.GetString(table, (int)at + 0x0E, nameLength),
                        Offset = partitionBase + (long)sector * SectorSize,
                        Length = length,
                        IsDirectory = (attributes & AttributeDirectory) != 0,
                    });
                }

                if (left != 0 && left != 0xFFFF) pending.Push(left);
                if (right != 0 && right != 0xFFFF) pending.Push(right);
            }
            return found;
        }

        /// <summary>Probes each known partition base for a volume descriptor.</summary>
        private static bool TryFindPartition(Func<long, int, byte[]> read, out long partitionBase,
                                             out uint rootSector, out uint rootSize)
        {
            partitionBase = 0; rootSector = 0; rootSize = 0;
            foreach (var candidate in PartitionBases)
            {
                var descriptor = read(candidate + VolumeDescriptorOffset, SectorSize);
                if (descriptor == null || descriptor.Length < SectorSize) continue;
                if (!MagicAt(descriptor, 0) || !MagicAt(descriptor, MagicTrailingOffset)) continue;

                uint sector = LeUInt32(descriptor, 0x14);
                uint size = LeUInt32(descriptor, 0x18);
                // A root table is a sector or two. An absurd size means a descriptor we misread.
                if (size < 0x0E || size > 32 * 1024 * 1024) continue;

                partitionBase = candidate; rootSector = sector; rootSize = size;
                Log.Info("XDVDFS partition at 0x" + candidate.ToString("X") + ", root " + size + " bytes");
                return true;
            }
            return false;
        }

        private static bool MagicAt(byte[] buffer, int offset)
        {
            if (offset + MagicLength > buffer.Length) return false;
            for (int i = 0; i < MagicLength; i++)
                if (buffer[offset + i] != (byte)Magic[i]) return false;
            return true;
        }

        private static uint LeUInt32(byte[] b, long offset)
            => (uint)(b[offset] | (b[offset + 1] << 8) | (b[offset + 2] << 16) | (b[offset + 3] << 24));

        /// <summary>The title id of a disc image: find default.xex at the root, then read its XEX header.</summary>
        public static uint? TitleIdOfImage(string path)
        {
            try
            {
                using var fs = File.OpenRead(path);
                Func<long, int, byte[]> read = (offset, length) => Xex.ReadAt(fs, offset, length);

                var xex = FindRootFile(read, "default.xex");
                if (xex == null) return null;
                // Re-base the reader on the executable so the XEX code sees offsets from its own start.
                return Xex.TitleId((offset, length) => read(xex.Offset + offset, length));
            }
            catch (Exception ex) { Log.Warn("could not read the disc image " + path, ex); return null; }
        }
    }
}
