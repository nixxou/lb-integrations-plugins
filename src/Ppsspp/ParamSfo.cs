// PARAM.SFO — the PSP's key/value metadata blob.
//
// It sits in every save directory (where it carries the save's title) and in every game image
// (where it carries the DISC_ID). Format read from PPSSPP's own Core/ELF/ParamSFO.cpp at tag
// v1.20.4; the bounds checks below are the ones PPSSPP applies, kept because these files come from
// a user's disk and a truncated one must yield nothing rather than throw.
//
//   header, 20 bytes at offset 0, all little-endian:
//     u32 magic = 0x46535000 ("\0PSF")
//     u32 version                       (0x00000101; PPSSPP warns and continues on others)
//     u32 key_table_start               absolute
//     u32 data_table_start              absolute
//     u32 index_table_entries
//
//   index table, index_table_entries x 16 bytes, from offset 20:
//     u16 key_table_offset              relative to key_table_start, NUL-terminated ASCII key
//     u16 param_fmt                     0x0004 raw blob | 0x0204 UTF-8 string | 0x0404 u32
//     u32 param_len                     bytes used
//     u32 param_max_len                 bytes reserved
//     u32 data_table_offset             relative to data_table_start
//
// A compact reader of the same format exists in Freegosy (MIT, abduznik/Freegosy,
// lib/core/save/strategies/ppsspp_save_strategy.dart); this one is written against PPSSPP's source
// rather than transposed, because it also has to serve the raw-blob case the Dart one skips.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace LbIntegrations.Ppsspp
{
    internal sealed class ParamSfo
    {
        private readonly Dictionary<string, string> _strings =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, uint> _ints =
            new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Every key present, whatever its format. Used to answer "does this file declare
        /// SAVEDATA_PARAMS?" without caring about the value.</summary>
        public bool HasKey(string key) => _keys.Contains(key);

        public string GetString(string key) => _strings.TryGetValue(key, out var v) ? v : null;

        /// <summary>The first of <paramref name="keys"/> that holds a non-empty string, or null.</summary>
        public string FirstString(params string[] keys)
        {
            foreach (var k in keys)
            {
                var v = GetString(k);
                if (!string.IsNullOrWhiteSpace(v)) return v.Trim();
            }
            return null;
        }

        public static ParamSfo FromFile(string path)
        {
            try
            {
                if (!File.Exists(path)) return null;
                return Parse(File.ReadAllBytes(path));
            }
            catch (Exception ex) { Log.Warn("could not read " + path, ex); return null; }
        }

        /// <summary>Parses, or returns null when the bytes are not a usable SFO. Never throws.</summary>
        public static ParamSfo Parse(byte[] data)
        {
            try
            {
                if (data == null || data.Length < 20) return null;
                if (BitConverter.ToUInt32(data, 0) != 0x46535000u) return null;   // "\0PSF"

                uint keyTableStart = BitConverter.ToUInt32(data, 8);
                uint dataTableStart = BitConverter.ToUInt32(data, 12);
                uint entries = BitConverter.ToUInt32(data, 16);
                if (keyTableStart > data.Length || dataTableStart > data.Length) return null;
                // An entry count big enough to overflow the index table means a corrupt header, not a
                // huge file: clamp to what the bytes could actually hold.
                long maxEntries = (data.Length - 20L) / 16L;
                if (entries > maxEntries) entries = (uint)Math.Max(0, maxEntries);

                var sfo = new ParamSfo();
                for (uint i = 0; i < entries; i++)
                {
                    int idx = 20 + (int)(i * 16);
                    ushort keyOffset = BitConverter.ToUInt16(data, idx);
                    ushort fmt = BitConverter.ToUInt16(data, idx + 2);
                    uint len = BitConverter.ToUInt32(data, idx + 4);
                    uint maxLen = BitConverter.ToUInt32(data, idx + 8);
                    uint dataOffset = BitConverter.ToUInt32(data, idx + 12);

                    long keyAt = keyTableStart + keyOffset;
                    long dataAt = dataTableStart + dataOffset;
                    if (keyAt >= data.Length || dataAt >= data.Length) continue;

                    string key = ReadCString(data, (int)keyAt);
                    if (key.Length == 0) continue;                  // truncated key table
                    sfo._keys.Add(key);

                    switch (fmt)
                    {
                        case 0x0404:                                // u32
                            if (dataAt + 4 <= data.Length)
                                sfo._ints[key] = BitConverter.ToUInt32(data, (int)dataAt);
                            break;

                        case 0x0204:                                // UTF-8, NUL-terminated
                            // PPSSPP reads up to param_max_len, not param_len (see its own TODO); match
                            // it so we read the same bytes it does.
                            int avail = (int)Math.Min(maxLen, data.Length - dataAt);
                            if (avail > 0) sfo._strings[key] = ReadCString(data, (int)dataAt, avail);
                            break;

                        case 0x0004:                                // raw blob, exactly param_len bytes
                            // Kept only as a key presence: SAVEDATA_PARAMS and SAVEDATA_FILE_LIST are
                            // stored this way, and all we ever ask is whether they are there.
                            break;
                    }
                }
                return sfo;
            }
            catch (Exception ex) { Log.Warn("malformed PARAM.SFO", ex); return null; }
        }

        private static string ReadCString(byte[] data, int start, int max = int.MaxValue)
        {
            int end = start;
            int limit = (int)Math.Min((long)start + max, data.Length);
            while (end < limit && data[end] != 0) end++;
            return end > start ? Encoding.UTF8.GetString(data, start, end - start) : "";
        }
    }
}
