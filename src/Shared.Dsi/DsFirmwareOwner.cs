// A DS firmware's OWNER - the name, birthday, colour and language a DS game shows - read and written (Mehdi, 03/10), for the
// copies of a firmware dump melonDS and no$gba boot on (MelonDsFirmware, NoGbaFirmware). Never the user's dump itself.
// GBATEK "DS Firmware User Settings": two 0x100-byte copies at [0x20]*8 and +0x100, CRC16 of 0x00-0x6F at 0x72, the newer
// by the counter at 0x70; checked on two dumps (Bill Nye's, a blank DS Lite's).

#nullable disable

using System;
using System.IO;
using System.Linq;
using System.Text;
using LbIntegrations.Identity;

namespace LbIntegrations.Dsi
{
    internal sealed class DsOwner
    {
        public bool Blank;
        public string Name = "";
        public int Language, Month, Day, Colour;
        public bool MacBlank;

        private static readonly string[] Languages = { "Japanese", "English", "French", "German", "Italian", "Spanish" };

        public string Describe() => Blank ? "nobody (blank)"
            : "\"" + Name + "\" (" + (Language >= 0 && Language < Languages.Length ? Languages[Language] : "language " + Language) + ", " + Month + "/" + Day + ", "
              + PackIdentity.DsColours[Math.Min(15, Math.Max(0, Colour))].Name.ToLowerInvariant() + ")";

        public static DsOwner Of(PackIdentity id)
            => new DsOwner { Blank = false, Name = id.DsNickname(), Language = id.DsLanguage(), Month = id.BirthMonth, Day = id.BirthDay, Colour = id.Colour };

        public bool Same(DsOwner w)
            => !Blank && w != null && Name == w.Name && Language == w.Language && Month == w.Month && Day == w.Day && Colour == w.Colour;
    }

    internal static class DsFirmwareOwner
    {
        /// <summary>The identity as the owner of a firmware COPY of ours: name, colour, birthday, language in both user-settings
        /// copies, each with its CRC16 (GBATEK "DS Firmware User Settings": 0x02 colour, 0x03 month, 0x04 day, 0x06 name in
        /// UTF-16 and 0x1A its length, 0x64 bits 0-2 the language, 0x70 counter, 0x72 CRC of 0x00-0x6F). The newer copy keeps
        /// its counter, the older one gets the next and becomes the newer: both are the identity. The extended block
        /// (0x74-0xFF, its own CRC) is not touched. Null, or why not.</summary>
        public static string WriteOwner(string copy, DsOwner id)
        {
            var fw = File.ReadAllBytes(copy);
            if (fw.Length < 0x200) return "too small to be a firmware";
            int at = BitConverter.ToUInt16(fw, 0x20) * 8;
            if (at <= 0 || at + 0x200 > fw.Length) at = fw.Length - 0x200;
            int a = at, b = at + 0x100;
            bool va = CrcOk(fw, a), vb = CrcOk(fw, b);
            int newer = va && vb ? (((BitConverter.ToUInt16(fw, b + 0x70) - BitConverter.ToUInt16(fw, a + 0x70)) & 0x7F) is int d && d > 0 && d < 0x40 ? b : a)
                      : vb ? b : a;
            int older = newer == a ? b : a;
            // A blank block (no valid copy at all) is made out of the first one's bytes, version 5 as melonDS's own.
            if (!va && !vb) { fw[newer] = 5; BitConverter.GetBytes((ushort)0).CopyTo(fw, newer + 0x70); }
            int counter = BitConverter.ToUInt16(fw, newer + 0x70);
            var name = (id.Name ?? "").Length > 10 ? id.Name.Substring(0, 10) : id.Name ?? "";
            foreach (var slot in new[] { newer, older })
            {
                if (slot == older) Buffer.BlockCopy(fw, newer, fw, older, 0x100);      // the whole block, extended part and its CRC too
                fw[slot + 0x02] = (byte)Math.Min(15, Math.Max(0, id.Colour));
                fw[slot + 0x03] = (byte)Math.Min(12, Math.Max(1, id.Month));
                fw[slot + 0x04] = (byte)Math.Min(31, Math.Max(1, id.Day));
                if (name.Length > 0)
                {
                    Array.Clear(fw, slot + 0x06, 20);
                    var utf16 = Encoding.Unicode.GetBytes(name);
                    Buffer.BlockCopy(utf16, 0, fw, slot + 0x06, Math.Min(20, utf16.Length));
                    BitConverter.GetBytes((ushort)Math.Min(10, name.Length)).CopyTo(fw, slot + 0x1A);
                }
                fw[slot + 0x64] = (byte)((fw[slot + 0x64] & ~0x07) | (id.Language & 0x07));
                BitConverter.GetBytes((ushort)(slot == newer ? counter : (counter + 1) & 0x7F)).CopyTo(fw, slot + 0x70);
                BitConverter.GetBytes((ushort)Crc16(fw, slot, 0x70, 0xFFFF)).CopyTo(fw, slot + 0x72);
            }
            var tmp = copy + ".tmp";
            File.WriteAllBytes(tmp, fw);
            File.Copy(tmp, copy, overwrite: true);
            File.Delete(tmp);
            return null;
        }

        /// <summary>A DS firmware's owner: the newer of its two user-settings copies whose CRC holds.</summary>
        public static DsOwner Read(byte[] fw)
        {
            var o = new DsOwner { Blank = true };
            try
            {
                if (fw == null || fw.Length < 0x200) return o;
                int at = BitConverter.ToUInt16(fw, 0x20) * 8;
                if (at <= 0 || at + 0x200 > fw.Length) at = fw.Length - 0x200;
                int a = at, b = at + 0x100;
                bool va = CrcOk(fw, a), vb = CrcOk(fw, b);
                int use;
                if (va && vb) use = ((BitConverter.ToUInt16(fw, b + 0x70) - BitConverter.ToUInt16(fw, a + 0x70)) & 0x7F) is int d && d > 0 && d < 0x40 ? b : a;
                else if (va) use = a;
                else if (vb) use = b;
                else return o;
                int len = Math.Min(10, (int)fw[use + 0x1A]);
                o.Name = Encoding.Unicode.GetString(fw, use + 6, len * 2);
                o.Colour = fw[use + 2] & 0x0F;
                o.Month = fw[use + 3];
                o.Day = fw[use + 4];
                o.Language = fw[use + 0x64] & 0x07;
                o.Blank = len == 0 || o.Month == 0 || o.Day == 0;
                o.MacBlank = fw.Length > 0x3B && fw.Skip(0x36).Take(6).All(x => x == 0xFF) || fw.Skip(0x36).Take(6).All(x => x == 0);
            }
            catch { }
            return o;
        }

        public static bool CrcOk(byte[] fw, int at) => Crc16(fw, at, 0x70, 0xFFFF) == BitConverter.ToUInt16(fw, at + 0x72);

        /// <summary>The firmware's CRC16 (GBATEK "DS Firmware Header", the BIOS's GetCRC16).</summary>
        private static int Crc16(byte[] d, int start, int len, int crc)
        {
            int[] table = { 0xC0C1, 0xC181, 0xC301, 0xC601, 0xCC01, 0xD801, 0xF001, 0xA001 };
            for (int i = 0; i < len; i++)
            {
                crc ^= d[start + i];
                for (int j = 0; j < 8; j++)
                {
                    bool carry = (crc & 1) != 0;
                    crc >>= 1;
                    if (carry) crc ^= table[j] << (7 - j);
                }
            }
            return crc & 0xFFFF;
        }
    }
}