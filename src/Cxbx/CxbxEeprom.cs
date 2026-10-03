// The console's region, set to the game's before it starts - so Cxbx-Reloaded does not stop on "The loaded title is
// designed for region: E (PAL) / However Cxbx-Reloaded is configured as: A (NTSC) ... Would you like to attempt
// emulation anyway?" (CxbxKrnl.cpp: shown when certificate.dwGameRegion & EEPROM.GameRegion is 0). Measured on GTA
// San Andreas (Europe), 02/10: a fresh install asks it of every PAL game.
//
// <data>\EEPROM.bin, 256 bytes, written in clear by Cxbx-Reloaded (EmuEEPROM.cpp). Measured on a real one:
//   0x00  [20]  checksum = HMAC-SHA1(key, Confounder[8] + HDKey[16] + GameRegion[4]) - so the region IS covered,
//               and is recomputed here as Cxbx-Reloaded's own EEPROM window does (DlgEepromConfig.cpp
//               WriteEepromInMemory): the key is the first 16 bytes of <data>\keys.bin, zeros without one. A wrong
//               checksum would not stop a game - Cxbx-Reloaded logs it and blinks the LED - but it is kept right.
//   0x14  [8]   confounder        0x1C [16] HDD key        0x2C  u32  game region (1 NA, 2 Japan, 4 rest of world)
//   0x30..      factory settings (checksum, serial, MAC, online key, 0x58 AV region), 0x60.. user settings
//               (0x90 language). Their two checksums are recomputed by Cxbx-Reloaded at every load (gen_section_CRCs).
// Only the region is changed - not the AV region (PAL / NTSC video), which is a choice of its own in Cxbx-Reloaded's
// EEPROM window and does not trigger the question.
//
// NO EEPROM.bin YET (the first game of a fresh install): Cxbx-Reloaded would make one, NTSC. One is made here instead,
// as EmuEEPROMReset makes it - random serial, MAC (00:50:F2 + 3 random), online key and HDD key, NTSC-M 60 Hz video,
// English - with the game's region. Cxbx-Reloaded then loads it as its own.

using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace LbIntegrations.Cxbx
{
    internal static class CxbxEeprom
    {
        private const int Size = 256, RegionAt = 0x2C;
        private const uint RegionMask = 0x7;           // NA | Japan | rest of world - not the manufacturing bit

        /// <summary>The console's region made one the game accepts. Never throws; what it did is logged.</summary>
        /// <param name="createOnly">only make an EEPROM.bin when there is none (with the game's region) - CxbxOptions,
        /// before it copies the file for the session.</param>
        public static void MatchRegion(string exe, XbeInfo game, bool createOnly = false)
        {
            try
            {
                uint wants = (game?.Region ?? 0) & RegionMask;
                if (wants == 0 && !createOnly) return;                   // region-free, or a certificate we could not read
                var data = CxbxPaths.DataDir(exe);
                if (data == null) return;
                var path = Path.Combine(data, "EEPROM.bin");
                uint region = wants == 0 ? 1u : wants & (uint)-(int)wants;   // the first region it accepts

                if (createOnly && File.Exists(path)) return;
                if (!File.Exists(path))
                {
                    var fresh = Fresh(region);
                    Sign(fresh, data);
                    WriteAtomically(path, fresh);
                    Log.Info("EEPROM: made with the region " + Name(region) + " (none was there yet) -> " + path);
                    return;
                }
                var b = File.ReadAllBytes(path);
                if (b.Length != Size) { Log.Info("EEPROM: " + path + " is " + b.Length + " bytes, not " + Size + " - left alone"); return; }
                uint now = BitConverter.ToUInt32(b, RegionAt);
                if ((now & wants) != 0) return;
                BitConverter.GetBytes(region).CopyTo(b, RegionAt);
                Sign(b, data);
                WriteAtomically(path, b);
                Log.Info("EEPROM: region " + Name(now) + " -> " + Name(region) + ", the game's (" + Name(wants) + ")");
            }
            catch (Exception ex) { Log.Warn("EEPROM: the region could not be set - Cxbx-Reloaded may ask about it", ex); }
        }

        private const int AvRegionAt = 0x58, LanguageAt = 0x90, VideoFlagsAt = 0x94, AudioFlagsAt = 0x98, ParentalGamesAt = 0x9C, ParentalMoviesAt = 0xA4;
        private const uint NtscM = 0x100, NtscJ = 0x200, PalI = 0x300, Hz60 = 0x00400000, Hz50 = 0x00800000;
        private const uint Widescreen = 0x10000, Letterbox = 0x100000, Hd720 = 0x20000, Hd1080 = 0x40000, Hd480 = 0x80000;

        /// <summary>The console options of a launch (CxbxOptions) written into EEPROM.bin, and its checksum kept right.
        /// Only the AV region (0x58) and the user settings change - the factory and user checksums are Cxbx-Reloaded's
        /// to recompute at every load (gen_section_CRCs); the header's HMAC covers neither.</summary>
        public static void ApplyOptions(string path, string dataDir, Dictionary<string, string> v, List<string> said)
        {
            var b = File.ReadAllBytes(path);
            if (b.Length != Size) { said.Add("EEPROM left alone (" + b.Length + " bytes)"); return; }
            uint U(int at) => BitConverter.ToUInt32(b, at);
            void W(int at, uint value) => BitConverter.GetBytes(value).CopyTo(b, at);

            // A region chosen (Mehdi, 03/10) - "follow" was done before, by MatchRegion.
            if (v.TryGetValue("console.region", out var chosen) && uint.TryParse(chosen, out var r) && (r == 1 || r == 2 || r == 4))
            {
                W(RegionAt, r);
                said.Add("region " + Name(r));
            }

            if (v.TryGetValue("console.language", out var lang))
            {
                uint code = lang == "windows" ? WindowsLanguage() : uint.TryParse(lang, out var l) ? l : 0;
                if (code >= 1 && code <= 9) { W(LanguageAt, code); said.Add("language " + code); }
            }

            uint flags = U(VideoFlagsAt);
            if (v.TryGetValue("console.video", out var video))
            {
                var mode = video;
                if (mode == "follow")
                {
                    uint region = U(RegionAt);
                    mode = (region & 4) != 0 && (region & 3) == 0 ? "pal60" : (region & 2) != 0 && (region & 1) == 0 ? "ntsc-j-hd" : "ntsc-hd";
                }
                flags &= ~(Hz60 | Hd480 | Hd720 | Hd1080);
                uint av;
                switch (mode)
                {
                    case "pal50": av = PalI | Hz50; break;
                    case "pal60": av = PalI | Hz50; flags |= Hz60; break;
                    case "ntsc": av = NtscM | Hz60; break;
                    case "ntsc-j-hd": av = NtscJ | Hz60; flags |= Hd480 | Hd720 | Hd1080; break;
                    default: av = NtscM | Hz60; flags |= Hd480 | Hd720 | Hd1080; break;   // ntsc-hd
                }
                W(AvRegionAt, av);
                said.Add("video " + mode + (video == "follow" ? " (the game's region)" : ""));
            }
            if (v.TryGetValue("console.screen", out var screen))
            {
                flags &= ~(Widescreen | Letterbox);
                if (screen == "widescreen") flags |= Widescreen; else if (screen == "letterbox") flags |= Letterbox;
                said.Add("picture " + screen);
            }
            W(VideoFlagsAt, flags);

            if (v.TryGetValue("console.audio", out var audio))
            {
                uint a = U(AudioFlagsAt) & ~3u;
                if (audio == "mono") a |= 1; else if (audio == "surround") a |= 2;
                W(AudioFlagsAt, a);
                said.Add("sound " + audio);
            }

            // Never a game refused by parental controls: no restriction, whatever was set.
            W(ParentalGamesAt, 0);
            W(ParentalMoviesAt, 0);

            Sign(b, dataDir);
            WriteAtomically(path, b);
        }

        /// <summary>Windows' language, as the Xbox numbers its own: English 1, Japanese 2, German 3, French 4, Spanish 5,
        /// Italian 6, Korean 7, Chinese 8, Portuguese 9 - English for any other.</summary>
        public static uint WindowsLanguage()
        {
            switch (System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName)
            {
                case "ja": return 2;
                case "de": return 3;
                case "fr": return 4;
                case "es": return 5;
                case "it": return 6;
                case "ko": return 7;
                case "zh": return 8;
                case "pt": return 9;
                default: return 1;
            }
        }

        private static byte[] Fresh(uint region)
        {
            var b = new byte[Size];
            var rnd = RandomNumberGenerator.Create();
            byte Next() { var one = new byte[1]; rnd.GetBytes(one); return one[0]; }
            // Factory: serial (12 digits), MAC with Microsoft's prefix, online key, AV region NTSC-M | 60 Hz.
            for (int i = 0; i < 12; i++) b[0x34 + i] = (byte)('0' + Next() % 10);
            b[0x40] = 0x00; b[0x41] = 0x50; b[0x42] = 0xF2;
            for (int i = 3; i < 6; i++) b[0x40 + i] = Next();
            for (int i = 0; i < 16; i++) b[0x48 + i] = Next();
            BitConverter.GetBytes(0x00400100u).CopyTo(b, 0x58);
            // Encrypted: the region, an HDD key.
            BitConverter.GetBytes(region).CopyTo(b, RegionAt);
            for (int i = 0; i < 16; i++) b[0x1C + i] = Next();
            // User: English; everything else zero, as Cxbx-Reloaded leaves it.
            BitConverter.GetBytes(1u).CopyTo(b, 0x90);
            return b;
        }

        /// <summary>The header's HMAC, with Cxbx-Reloaded's key: keys.bin's first 16 bytes, else zeros.</summary>
        private static void Sign(byte[] b, string dataDir)
        {
            var key = new byte[16];
            try
            {
                var keys = Path.Combine(dataDir, "keys.bin");
                if (File.Exists(keys)) { var k = File.ReadAllBytes(keys); Array.Copy(k, key, Math.Min(16, k.Length)); }
            }
            catch { }
            using var h = new HMACSHA1(key);
            var mac = h.ComputeHash(b, 0x14, 8 + 16 + 4);
            Array.Copy(mac, 0, b, 0, 20);
        }

        private static void WriteAtomically(string path, byte[] b)
        {
            var tmp = path + ".lbip";
            File.WriteAllBytes(tmp, b);
            File.Move(tmp, path, overwrite: true);
        }

        private static string Name(uint r)
        {
            var s = new StringBuilder();
            if ((r & 1) != 0) s.Append("A (NTSC) ");
            if ((r & 2) != 0) s.Append("J (Japan) ");
            if ((r & 4) != 0) s.Append("E (PAL) ");
            return s.Length == 0 ? "0x" + r.ToString("X") : s.ToString().Trim();
        }
    }
}
