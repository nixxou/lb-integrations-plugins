// 3dSen's save states: the index sav.cfg and the files sav\<slot>.sav of its user data folder (docs\3dsen.md section 6,
// read in 3dSen PC 1.1.2's Nes3D/SaveStateDB.cs, SaveConfig.cs, Texture2DSur.cs; measured on the lab's files).
//
//   sav.cfg   protobuf-net List<SaveConfig>: each item a field 1 (length-delimited), holding
//               1 SlotId    string   "1".."36", "quick"
//               2 gameId    string   the PROFILE the state belongs to ("" = a free slot)
//               3 texture   Texture2DSur {1 width, 2 height, 3 TextureFormat (3 = RGB24), 4 raw bytes, rows bottom-up}
//               4 lastTime  varint   Unix seconds
//             written in that order, zeros too (SaveStateDB: UseImplicitZeroDefaults = false - [file] "20 00" in every
//             free slot); no texture on a slot never saved into.
//   sav\N.sav the state: a BinaryWriter string "__Default" (the core's name), then a protobuf SaveStateWrapper whose field 1
//             is the core's data (DefaultCore/Core.SaveState): i64 format, i32 frame, the memory (bool, 8 KB scratch,
//             8 KB battery RAM), the cartridge (16-byte header, mirroring, 3 bools, mapper, 2 bytes, 3 i32, romPath and
//             romName as BinaryWriter strings, then the WHOLE ROM) ... A state is self-contained: loading it reads no ROM.
//
// SLOTS: 36 numbered ones and "quick", SHARED BY EVERY GAME - a slot is a place, the game is its gameId. 3dSen's index
// assumes the numbered slots in order ("1" first: SaveStateDB[id] = SaveList[int.Parse(id) - 1]) and a missing file makes it
// start again with 36 free slots (SaveStateDB.Awake): this writer therefore only ever replaces ONE item of a file it fully
// understood, keeps every other item's bytes as they were, and writes a whole fresh list when there is no file at all.
// Never while 3dSen runs: it keeps the list in memory and writes it whole at each state saved and when it quits.
//
// THE PICTURE (field 3): a state saved from 3dSen's menu carries the frame taken when the game was paused (VRUIController
// .Pause -> ScreenRecorder.Capture, 256 x 192); one saved by the QuickSave key or the pad's Select + RotDown carries none -
// SaveStateProfile(QuickSave) passes no texture and AssignSS keeps a blank one: [run] 147 456 bytes of 0xCD. A picture of
// one colour is therefore no picture (ThreeDSenStateThumbs then uses a capture of the window).

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace LbIntegrations.ThreeDSen
{
    internal sealed class ThreeDSenSlot
    {
        public string SlotId, GameId = "";
        public long LastTime;
        public int Width, Height, Format;
        public byte[] Pixels;          // null: no texture field
        public byte[] Raw;             // the item's bytes as read; null once changed (written again from the fields)

        public bool Empty => string.IsNullOrEmpty(GameId);
        public bool IsQuick => SlotId == ThreeDSenSavCfg.QuickId;
        /// <summary>LaunchBox's number for it: 0 the quick slot, 1..36 the others; -1 for anything else.</summary>
        public int Number => ThreeDSenSavCfg.NumberOf(SlotId);
    }

    internal static class ThreeDSenSavCfg
    {
        public const string FileName = "sav.cfg";
        public const string Folder = "sav";
        public const string QuickId = "quick";
        public const int Numbered = 36;
        public const int ThumbWidth = 256, ThumbHeight = 192, Rgb24 = 3;
        /// <summary>What a texture 3dSen never drew into holds ([run] the quick slot after a hotkey save).</summary>
        public const byte Blank = 0xCD;

        public static string PathIn(string data) => Path.Combine(data, FileName);
        public static string StateDir(string data) => Path.Combine(data, Folder);

        /// <summary>sav\&lt;slot&gt;.sav (SaveConfig.SlotId's setter).</summary>
        public static string StatePath(string data, string slotId) => Path.Combine(StateDir(data), slotId + ".sav");

        public static string SlotIdOf(int number) => number == 0 ? QuickId : number.ToString(System.Globalization.CultureInfo.InvariantCulture);

        public static int NumberOf(string slotId)
        {
            if (slotId == QuickId) return 0;
            return int.TryParse(slotId, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var n) && n >= 1 && n <= Numbered ? n : -1;
        }

        /// <summary>The slot a state file is: "quick.sav" 0, "5.sav" 5; -1 for anything else (a thumbnail, a stray file).</summary>
        public static int NumberOfFile(string path)
        {
            var name = Path.GetFileName(path ?? "");
            return name.EndsWith(".sav", StringComparison.OrdinalIgnoreCase) ? NumberOf(name.Substring(0, name.Length - 4).ToLowerInvariant()) : -1;
        }

        // ── reading ──────────────────────────────────────────────────────────

        /// <summary>The slots of a sav.cfg, in its order; null when it is missing or not understood.</summary>
        public static List<ThreeDSenSlot> Read(string path)
        {
            try { return File.Exists(path) ? Parse(File.ReadAllBytes(path)) : null; }
            catch (Exception ex) { Log.Warn("sav.cfg: could not read " + path, ex); return null; }
        }

        /// <summary>Every item, or null at the first byte not understood (any other field, any other wire type).</summary>
        public static List<ThreeDSenSlot> Parse(byte[] b)
        {
            var list = new List<ThreeDSenSlot>();
            int i = 0;
            while (i < b.Length)
            {
                if (!Tag(b, ref i, out var field, out var wire) || field != 1 || wire != 2) return null;
                if (!Bytes(b, ref i, out var at, out var len)) return null;
                var slot = Item(b, at, len);
                if (slot == null) return null;
                slot.Raw = b.Skip(at).Take(len).ToArray();
                list.Add(slot);
            }
            return list;
        }

        private static ThreeDSenSlot Item(byte[] b, int at, int len)
        {
            var s = new ThreeDSenSlot();
            int i = at, end = at + len;
            while (i < end)
            {
                if (!Tag(b, ref i, out var field, out var wire)) return null;
                switch (field)
                {
                    case 1 when wire == 2: if (!Text(b, ref i, end, out s.SlotId)) return null; break;
                    case 2 when wire == 2: if (!Text(b, ref i, end, out s.GameId)) return null; break;
                    case 4 when wire == 0: if (!Varint(b, ref i, out var t)) return null; s.LastTime = (long)t; break;
                    case 3 when wire == 2:
                        if (!Bytes(b, ref i, out var tat, out var tlen) || tat + tlen > end || !Texture(b, tat, tlen, s)) return null;
                        break;
                    default: return null;
                }
                if (i > end) return null;
            }
            return s.SlotId == null ? null : s;
        }

        private static bool Texture(byte[] b, int at, int len, ThreeDSenSlot s)
        {
            int i = at, end = at + len;
            s.Pixels = Array.Empty<byte>();
            while (i < end)
            {
                if (!Tag(b, ref i, out var field, out var wire)) return false;
                if (field >= 1 && field <= 3 && wire == 0)
                {
                    if (!Varint(b, ref i, out var v)) return false;
                    if (field == 1) s.Width = (int)v; else if (field == 2) s.Height = (int)v; else s.Format = (int)v;
                }
                else if (field == 4 && wire == 2)
                {
                    if (!Bytes(b, ref i, out var pat, out var plen) || pat + plen > end) return false;
                    s.Pixels = new byte[plen];
                    Buffer.BlockCopy(b, pat, s.Pixels, 0, plen);
                }
                else return false;
            }
            return i == end;
        }

        /// <summary>The 36 numbered slots in order ("1" to "36" - SaveStateDB indexes them so) and at most one quick slot:
        /// a file 3dSen itself would read back as it is. Anything else is not written into.</summary>
        public static bool Writable(List<ThreeDSenSlot> slots)
        {
            if (slots == null) return false;
            var numbered = slots.Where(s => !s.IsQuick).ToList();
            if (numbered.Count != Numbered || slots.Count(s => s.IsQuick) > 1) return false;
            for (int k = 0; k < Numbered; k++) if (numbered[k].SlotId != (k + 1).ToString(System.Globalization.CultureInfo.InvariantCulture)) return false;
            return true;
        }

        /// <summary>What 3dSen makes when it finds no sav.cfg (SaveStateDB.Awake): 36 free slots, then the quick one.</summary>
        public static List<ThreeDSenSlot> Fresh()
        {
            var list = Enumerable.Range(1, Numbered).Select(n => new ThreeDSenSlot { SlotId = SlotIdOf(n) }).ToList();
            list.Add(new ThreeDSenSlot { SlotId = QuickId });
            return list;
        }

        // ── writing ──────────────────────────────────────────────────────────

        /// <summary>The file: every item's own bytes when it was not changed, written again from its fields when it was.</summary>
        public static byte[] Write(List<ThreeDSenSlot> slots)
        {
            using var ms = new MemoryStream();
            foreach (var s in slots)
            {
                var item = s.Raw ?? Encode(s);
                ms.WriteByte(0x0A);
                WriteVarint(ms, (ulong)item.Length);
                ms.Write(item, 0, item.Length);
            }
            return ms.ToArray();
        }

        /// <summary>One item as protobuf-net writes it: 1, 2, 3 (when there is a texture), 4 - zeros included.</summary>
        public static byte[] Encode(ThreeDSenSlot s)
        {
            using var ms = new MemoryStream();
            WriteText(ms, 1, s.SlotId ?? "");
            WriteText(ms, 2, s.GameId ?? "");
            if (s.Pixels != null)
            {
                using var t = new MemoryStream();
                t.WriteByte(0x08); WriteVarint(t, (ulong)s.Width);
                t.WriteByte(0x10); WriteVarint(t, (ulong)s.Height);
                t.WriteByte(0x18); WriteVarint(t, (ulong)s.Format);
                t.WriteByte(0x22); WriteVarint(t, (ulong)s.Pixels.Length); t.Write(s.Pixels, 0, s.Pixels.Length);
                var tex = t.ToArray();
                ms.WriteByte(0x1A); WriteVarint(ms, (ulong)tex.Length); ms.Write(tex, 0, tex.Length);
            }
            ms.WriteByte(0x20); WriteVarint(ms, (ulong)s.LastTime);
            return ms.ToArray();
        }

        /// <summary>Writes the file through a temporary one beside it, moved over. Never while 3dSen runs (the caller checks).</summary>
        public static void Save(string path, List<ThreeDSenSlot> slots)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            var tmp = path + ".lbip-part";
            File.WriteAllBytes(tmp, Write(slots));
            File.Move(tmp, path, overwrite: true);
        }

        // ── the picture ──────────────────────────────────────────────────────

        /// <summary>True when the slot carries a picture 3dSen drew: RGB24, its size right, not one colour.</summary>
        public static bool HasPicture(ThreeDSenSlot s)
        {
            if (s?.Pixels == null || s.Format != Rgb24 || s.Width < 16 || s.Height < 16 || s.Pixels.Length != s.Width * s.Height * 3) return false;
            byte first = s.Pixels[0];
            for (int k = 1; k < s.Pixels.Length; k++) if (s.Pixels[k] != first) return true;
            return false;
        }

        /// <summary>The slot's picture, top-down, or null.</summary>
        public static Bitmap Picture(ThreeDSenSlot s)
        {
            if (!HasPicture(s)) return null;
            int w = s.Width, h = s.Height;
            var bmp = new Bitmap(w, h, PixelFormat.Format24bppRgb);
            var bits = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.WriteOnly, PixelFormat.Format24bppRgb);
            try
            {
                var row = new byte[w * 3];
                for (int y = 0; y < h; y++)
                {
                    int src = (h - 1 - y) * w * 3;   // Unity's rows go bottom-up
                    for (int x = 0; x < w; x++)
                    {
                        row[x * 3 + 0] = s.Pixels[src + x * 3 + 2];   // GDI+ is BGR
                        row[x * 3 + 1] = s.Pixels[src + x * 3 + 1];
                        row[x * 3 + 2] = s.Pixels[src + x * 3 + 0];
                    }
                    Marshal.Copy(row, 0, bits.Scan0 + y * bits.Stride, row.Length);
                }
            }
            finally { bmp.UnlockBits(bits); }
            return bmp;
        }

        /// <summary>A picture made 3dSen's texture: 256 x 192 RGB24, rows bottom-up; a blank one (0xCD, what 3dSen writes for a
        /// hotkey save) when there is none.</summary>
        public static byte[] PixelsOf(Image picture)
        {
            var px = new byte[ThumbWidth * ThumbHeight * 3];
            if (picture == null) { for (int k = 0; k < px.Length; k++) px[k] = Blank; return px; }
            using var bmp = new Bitmap(ThumbWidth, ThumbHeight, PixelFormat.Format24bppRgb);
            using (var g = Graphics.FromImage(bmp))
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.DrawImage(picture, 0, 0, ThumbWidth, ThumbHeight);
            }
            var bits = bmp.LockBits(new Rectangle(0, 0, ThumbWidth, ThumbHeight), ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
            try
            {
                var row = new byte[ThumbWidth * 3];
                for (int y = 0; y < ThumbHeight; y++)
                {
                    Marshal.Copy(bits.Scan0 + y * bits.Stride, row, 0, row.Length);
                    int dst = (ThumbHeight - 1 - y) * ThumbWidth * 3;
                    for (int x = 0; x < ThumbWidth; x++)
                    {
                        px[dst + x * 3 + 0] = row[x * 3 + 2];
                        px[dst + x * 3 + 1] = row[x * 3 + 1];
                        px[dst + x * 3 + 2] = row[x * 3 + 0];
                    }
                }
            }
            finally { bmp.UnlockBits(bits); }
            return px;
        }

        // ── the state file ───────────────────────────────────────────────────

        /// <summary>The ROM file name a state was made from (Cartridge.romName: "Zelda II zipped.nes" for a .zip), or null.
        /// Reads the first 32 KB only.</summary>
        public static string RomNameOf(string state)
        {
            try
            {
                byte[] b;
                using (var f = new FileStream(state, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                {
                    b = new byte[Math.Min(f.Length, 32768)];
                    int n = 0, r;
                    while (n < b.Length && (r = f.Read(b, n, b.Length - n)) > 0) n += r;
                }
                int i = 0;
                if (!Dotnet(b, ref i, out var core) || !core.StartsWith("__", StringComparison.Ordinal)) return null;
                if (!Tag(b, ref i, out var field, out var wire) || field != 1 || wire != 2 || !Varint(b, ref i, out _)) return null;
                i += 8 + 4 + 1 + 8192 + 8192 + 16 + 1 + 3 + 1 + 1 + 1 + 4 + 4 + 4;   // format, frame, memory, header ... mirroringBase
                if (!Dotnet(b, ref i, out _)) return null;   // romPath
                return Dotnet(b, ref i, out var romName) ? romName : null;
            }
            catch { return null; }
        }

        // ── protobuf and BinaryWriter primitives ─────────────────────────────

        private static bool Varint(byte[] b, ref int i, out ulong v)
        {
            v = 0;
            for (int shift = 0; shift < 64; shift += 7)
            {
                if (i >= b.Length) return false;
                var c = b[i++];
                v |= (ulong)(c & 0x7F) << shift;
                if (c < 0x80) return true;
            }
            return false;
        }

        private static bool Tag(byte[] b, ref int i, out int field, out int wire)
        {
            field = wire = 0;
            if (!Varint(b, ref i, out var t) || t > int.MaxValue) return false;
            field = (int)(t >> 3); wire = (int)(t & 7);
            return field > 0;
        }

        private static bool Bytes(byte[] b, ref int i, out int at, out int len)
        {
            at = len = 0;
            if (!Varint(b, ref i, out var n) || n > (ulong)(b.Length - i)) return false;
            at = i; len = (int)n; i += len;
            return true;
        }

        private static bool Text(byte[] b, ref int i, int end, out string s)
        {
            s = null;
            if (!Bytes(b, ref i, out var at, out var len) || at + len > end) return false;
            s = Encoding.UTF8.GetString(b, at, len);
            return true;
        }

        /// <summary>A .NET BinaryWriter string: 7-bit length, UTF-8.</summary>
        private static bool Dotnet(byte[] b, ref int i, out string s)
        {
            s = null;
            if (!Varint(b, ref i, out var n) || n > (ulong)(b.Length - i)) return false;
            s = Encoding.UTF8.GetString(b, i, (int)n);
            i += (int)n;
            return true;
        }

        private static void WriteVarint(Stream s, ulong v)
        {
            while (v >= 0x80) { s.WriteByte((byte)(v | 0x80)); v >>= 7; }
            s.WriteByte((byte)v);
        }

        private static void WriteText(Stream s, int field, string text)
        {
            var b = Encoding.UTF8.GetBytes(text);
            s.WriteByte((byte)(field << 3 | 2));
            WriteVarint(s, (ulong)b.Length);
            s.Write(b, 0, b.Length);
        }
    }
}
