// A game's own console: an EMPTY qcow2 whose backing file is the pristine one (Mehdi, 04/10: "un disque par jeu").
//
// HOW QCOW2 DOES IT (QEMU's docs/interop/qcow2.txt): an image may name a backing file. A cluster the image does not hold is
// read from it; a write lands in the image, never in the backing file, which QEMU opens read-only. So a game's disk is
// the dashboard's disk plus only what that game wrote: its saves, a few MB. xemu reads it with QEMU's own block layer -
// nothing of ours runs while it plays.
//
// WHAT IS WRITTEN, four clusters of 64 KB, all big-endian:
//   0  the header (version 3, 112 bytes - the shape of xemu-dashboard's own xbox_hdd.qcow2, measured 04/10: cluster_bits
//      16, refcount_order 4), then the backing format extension ("qcow2"), the end of extensions, the backing file name
//   1  the refcount table: one entry, the refcount block at cluster 2
//   2  the refcount block: clusters 0 to 3 in use, once each (16-bit entries)
//   3  the L1 table: l1_size entries, all zero - nothing allocated, every read goes to the backing file
// THE SIZE IS THE BACKING FILE'S (read from its header): a qcow2 smaller than its backing file would cut the disk.
// THE BACKING NAME IS RELATIVE ("../base.qcow2" from hdd\games\), resolved by QEMU against the image's own folder: the
// install can move without a disk losing its base.

using System;
using System.IO;
using System.Text;

namespace LbIntegrations.Xemu
{
    internal static class Qcow2Overlay
    {
        private const int ClusterBits = 16, Cluster = 1 << ClusterBits;
        private const uint Magic = 0x514649FB;                 // "QFI\xfb"
        private const uint BackingFormatExt = 0xE2792ACA;

        /// <summary>The virtual size of a qcow2, or -1 when it is not one.</summary>
        public static long VirtualSize(string path)
        {
            try
            {
                using var f = File.OpenRead(path);
                var h = new byte[32];
                if (f.Read(h, 0, h.Length) != h.Length || U32(h, 0) != Magic) return -1;
                return (long)U64(h, 24);
            }
            catch { return -1; }
        }

        /// <summary>The backing file name an image carries, or null.</summary>
        public static string BackingName(string path)
        {
            try
            {
                using var f = File.OpenRead(path);
                var h = new byte[20];
                if (f.Read(h, 0, h.Length) != h.Length || U32(h, 0) != Magic) return null;
                long at = (long)U64(h, 8); int len = (int)U32(h, 16);
                if (at == 0 || len <= 0 || len > 1023) return null;
                var name = new byte[len];
                f.Seek(at, SeekOrigin.Begin);
                return f.Read(name, 0, len) == len ? Encoding.UTF8.GetString(name) : null;
            }
            catch { return null; }
        }

        /// <summary>An empty image over <paramref name="backing"/> at <paramref name="path"/> - through a temporary file, so a
        /// cut write never leaves a half disk. Null when made; otherwise why not. Never over an existing file.</summary>
        public static string Create(string backing, string path)
        {
            try
            {
                if (File.Exists(path)) return "it already exists";
                long size = VirtualSize(backing);
                if (size <= 0) return "the base disk is not a qcow2: " + backing;
                var dir = Path.GetDirectoryName(Path.GetFullPath(path));
                Directory.CreateDirectory(dir);
                var name = Path.GetRelativePath(dir, Path.GetFullPath(backing)).Replace('\\', '/');
                var nameBytes = Encoding.UTF8.GetBytes(name);
                if (nameBytes.Length > 1023) return "the base disk's path is too long";

                long l2Covers = (long)(Cluster / 8) * Cluster;   // one L2 table: 8192 entries of one cluster each
                uint l1Size = (uint)((size + l2Covers - 1) / l2Covers);
                if (l1Size * 8L > Cluster) return "the base disk is too big for a one-cluster L1 table";

                var img = new byte[4 * Cluster];
                // ── the header ──
                PutU32(img, 0, Magic);
                PutU32(img, 4, 3);                               // version
                // backing_file_offset / size: after the header and its extensions - set below
                PutU32(img, 20, ClusterBits);
                PutU64(img, 24, (ulong)size);
                PutU32(img, 32, 0);                              // no encryption
                PutU32(img, 36, l1Size);
                PutU64(img, 40, 3UL * Cluster);                  // L1 table
                PutU64(img, 48, 1UL * Cluster);                  // refcount table
                PutU32(img, 56, 1);                              // refcount table clusters
                PutU32(img, 60, 0);                              // snapshots
                PutU64(img, 64, 0);
                PutU64(img, 72, 0);                              // incompatible features
                PutU64(img, 80, 0);                              // compatible features
                PutU64(img, 88, 0);                              // autoclear features
                PutU32(img, 96, 4);                              // refcount_order: 16-bit refcounts
                PutU32(img, 100, 112);                           // header_length (104 + compression type + padding)
                img[104] = 0;                                    // compression type: zlib
                // ── header extensions: the backing format, then the end ──
                int at = 112;
                var fmt = Encoding.ASCII.GetBytes("qcow2");
                PutU32(img, at, BackingFormatExt); PutU32(img, at + 4, (uint)fmt.Length);
                Buffer.BlockCopy(fmt, 0, img, at + 8, fmt.Length);
                at += 8 + ((fmt.Length + 7) / 8) * 8;
                PutU32(img, at, 0); PutU32(img, at + 4, 0);    // end of extensions
                at += 8;
                // ── the backing file name ──
                Buffer.BlockCopy(nameBytes, 0, img, at, nameBytes.Length);
                PutU64(img, 8, (ulong)at);
                PutU32(img, 16, (uint)nameBytes.Length);
                // ── refcount table (cluster 1) -> refcount block (cluster 2): clusters 0..3 used once ──
                PutU64(img, Cluster, 2UL * Cluster);
                for (int c = 0; c < 4; c++) PutU16(img, 2 * Cluster + c * 2, 1);
                // ── L1 table (cluster 3): all zero, already ──

                var tmp = path + ".lbip-tmp";
                File.WriteAllBytes(tmp, img);
                File.Move(tmp, path);
                Log.Info("console: " + Path.GetFileName(path) + " made over " + name + " (" + (size >> 30) + " GiB)");
                return null;
            }
            catch (Exception ex) { return ex.GetType().Name + ": " + ex.Message; }
        }

        private static uint U32(byte[] b, int o) => (uint)(b[o] << 24 | b[o + 1] << 16 | b[o + 2] << 8 | b[o + 3]);
        private static ulong U64(byte[] b, int o) => (ulong)U32(b, o) << 32 | U32(b, o + 4);
        private static void PutU16(byte[] b, int o, ushort v) { b[o] = (byte)(v >> 8); b[o + 1] = (byte)v; }
        private static void PutU32(byte[] b, int o, uint v) { b[o] = (byte)(v >> 24); b[o + 1] = (byte)(v >> 16); b[o + 2] = (byte)(v >> 8); b[o + 3] = (byte)v; }
        private static void PutU64(byte[] b, int o, ulong v) { PutU32(b, o, (uint)(v >> 32)); PutU32(b, o + 4, (uint)v); }
    }
}
