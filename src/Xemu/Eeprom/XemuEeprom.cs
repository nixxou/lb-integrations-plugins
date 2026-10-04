// xemu's console, set for a game before it starts (Mehdi, 04/10): its region and video standard following the game, its
// language the pack's ("Your console", else Windows'), its time zone Windows', its HDD key the pack's (PackIdentity.XboxHddKey,
// the same on every console: a save a game signs with the console's key moves between them). Measured on GTA San Andreas (Europe), 04/10:
// on the console xemu makes (North America, NTSC) the game stays on a black screen; on a copy turned Europe / PAL it plays.
//
// THE FILE IS A REAL XBOX'S, unlike Cxbx-Reloaded's (which keeps its EEPROM.bin in clear - CxbxEeprom): 256 bytes,
//   0x00 [20]  HMAC-SHA1 of the security section, with the Xbox's own key for the kernel the EEPROM was made for
//   0x14 [28]  the security section, RC4-encrypted (key: the HMAC of that hash): confounder [8], HDD key [16], 0x2C game
//              region u32 (1 North America, 2 Japan, 4 rest of the world)
//   0x30       factory section checksum, over 0x34..0x5F: serial, MAC, online key, 0x58 AV region (the video standard)
//   0x60       user section checksum, over 0x64..0xBF: 0x64 time zone [0x2C], 0x90 language, 0x94 video flags, 0x98
//              audio flags, 0x9C / 0xA4 parental controls
// Decrypted and encrypted again with XboxEepromEditor's code (github.com/Ernegien/XboxEepromEditor, nixxou's fork):
// HmacSha1.cs and RC4.cs beside this file, and the time zone table below - which is why src\Xemu is GPL-2.0-or-later.
//
// THE USER'S FILE IS NEVER WRITTEN. The session's console is <xemu>\eeprom-session.bin, made from eeprom.bin at every
// launch, and xemu.toml's eeprom_path points at it for the game - back at eeprom.bin when the game is over
// (XemuSession.Standalone). Whatever a game changes in it goes with the session. No eeprom.bin yet: one is made first, as
// XboxEepromEditor makes a new one (a 1.0 kernel's, North America, NTSC-M, English, random serial, MAC and keys) - the
// console every later session starts from, its HDD key with it.
//
// Copyright (C) 2003 [TEAM ASSEMBLY] www.team-assembly.com, (C) 2020 Mike Davis - the time zone table.
// This program is free software; you can redistribute it and/or modify it under the terms of the GNU General Public
// License as published by the Free Software Foundation; either version 2 of the License, or (at your option) any later
// version. It is distributed WITHOUT ANY WARRANTY; see src\Xemu\LICENSE.md.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using LbIntegrations.Cxbx;

namespace LbIntegrations.Xemu.Eeprom
{
    internal static class XemuEeprom
    {
        public const int Size = 256;
        private const int SecurityAt = 0x14, SecurityLength = 0x1C, RegionAt = 0x2C;
        private const int AvRegionAt = 0x58, ZoneAt = 0x64, ZoneLength = 0x2C, LanguageAt = 0x90, VideoFlagsAt = 0x94, ParentalGamesAt = 0x9C, ParentalMoviesAt = 0xA4;
        private const uint NtscM = 0x100, NtscJ = 0x200, PalI = 0x300, Hz60 = 0x00400000, Hz50 = 0x00800000;
        private const uint Hd720 = 0x20000, Hd1080 = 0x40000, Hd480 = 0x80000;

        /// <summary>An EEPROM read: its kernel version and its bytes with the security section in clear. Null when it is not
        /// one any Xbox key opens.</summary>
        public sealed class Opened
        {
            public EepromVersion Version;
            public byte[] Data;                                 // 256 bytes, 0x14..0x2F decrypted

            public uint Region { get => BitConverter.ToUInt32(Data, RegionAt); set => BitConverter.GetBytes(value).CopyTo(Data, RegionAt); }
            public uint AvRegion { get => BitConverter.ToUInt32(Data, AvRegionAt); set => BitConverter.GetBytes(value).CopyTo(Data, AvRegionAt); }
            public uint Language { get => BitConverter.ToUInt32(Data, LanguageAt); set => BitConverter.GetBytes(value).CopyTo(Data, LanguageAt); }
            public uint VideoFlags { get => BitConverter.ToUInt32(Data, VideoFlagsAt); set => BitConverter.GetBytes(value).CopyTo(Data, VideoFlagsAt); }
            public byte[] HddKey { get => Data.Skip(0x1C).Take(16).ToArray(); set { if (value?.Length != 16) throw new ArgumentException("an HDD key is 16 bytes"); value.CopyTo(Data, 0x1C); } }
            public string Serial => Encoding.ASCII.GetString(Data, 0x34, 12);
            public string Zone { get => ZoneName(Data); set => SetZone(Data, value); }
        }

        public static Opened Open(byte[] file)
        {
            if (file == null || file.Length != Size) return null;
            var sha = new HmacSha1();
            for (var v = EepromVersion.Debug; v <= EepromVersion.RetailLast; v++)
            {
                var hash = file.Take(20).ToArray();
                var rc4 = new RC4();
                rc4.Init(sha.Compute(v, hash));
                var security = file.Skip(SecurityAt).Take(SecurityLength).ToArray();
                rc4.Crypt(security, 0, security.Length);
                if (!hash.SequenceEqual(sha.Compute(v, security))) continue;
                var data = (byte[])file.Clone();
                security.CopyTo(data, SecurityAt);
                return new Opened { Version = v, Data = data };
            }
            return null;
        }

        /// <summary>The file again: both checksums recomputed, the security section hashed and encrypted with its version's
        /// key.</summary>
        public static byte[] Seal(Opened e)
        {
            var b = (byte[])e.Data.Clone();
            BitConverter.GetBytes(~Checksum(b, 0x34, 0x2C)).CopyTo(b, 0x30);
            BitConverter.GetBytes(~Checksum(b, 0x64, 0x5C)).CopyTo(b, 0x60);
            var sha = new HmacSha1();
            var security = b.Skip(SecurityAt).Take(SecurityLength).ToArray();
            var hash = sha.Compute(e.Version, security);
            var rc4 = new RC4();
            rc4.Init(sha.Compute(e.Version, hash));
            rc4.Crypt(security, 0, security.Length);
            hash.CopyTo(b, 0);
            security.CopyTo(b, SecurityAt);
            return b;
        }

        /// <summary>A section's checksum, as the kernel computes it: 32-bit words added with their carries folded in.</summary>
        internal static uint Checksum(byte[] data, int offset, int size)
        {
            uint high = 0, low = 0;
            for (int i = 0; i < size / 4; i++)
            {
                uint val = BitConverter.ToUInt32(data, offset + i * 4);
                ulong sum = ((ulong)high << 32) | low;
                high = (uint)((sum + val) >> 32);
                low += val;
            }
            return high + low;
        }

        /// <summary>A new console, as XboxEepromEditor makes one: a 1.0 kernel's (the version of the EEPROM xemu makes itself),
        /// North America, NTSC-M, English, London; a random serial (ending in 9, as it marks its own), MAC with a Microsoft
        /// prefix, online key and confounder - but the pack's HDD key.</summary>
        public static byte[] Fresh()
        {
            var e = new Opened { Version = EepromVersion.RetailFirst, Data = new byte[Size] };
            var r = RandomNumberGenerator.Create();
            byte[] Random(int n) { var b = new byte[n]; r.GetBytes(b); return b; }
            Random(8).CopyTo(e.Data, 0x14);                     // confounder
            LbIntegrations.Identity.PackIdentity.XboxHddKey().CopyTo(e.Data, 0x1C);   // HDD key: the pack's, the same on every console
            e.Region = 1;
            var digits = Random(11);
            Encoding.ASCII.GetBytes(new string(digits.Select(d => (char)('0' + d % 10)).ToArray()) + "9").CopyTo(e.Data, 0x34);
            var mac = Random(6); mac[0] = 0x00; mac[1] = 0x50; mac[2] = 0xF2;
            mac.CopyTo(e.Data, 0x40);
            Random(16).CopyTo(e.Data, 0x48);                    // online key
            e.AvRegion = NtscM | Hz60;
            e.Zone = "London";
            e.Language = 1;
            return Seal(e);
        }

        // ── the session's console ────────────────────────────────────────────

        /// <summary>The console a launch of <paramref name="game"/> runs on, written to <paramref name="sessionPath"/> from
        /// <paramref name="basePath"/> (made when absent): the options of <paramref name="v"/> - console.region (follow, 1,
        /// 2, 4, xemu), console.video (follow, pal50, pal60, ntsc, ntsc-hd, xemu), console.language (windows, 1..9, xemu),
        /// console.timezone (windows, xemu), console.hddkey (pack, xemu). Null when the base cannot be read - the launch then uses it as it is.</summary>
        public static string Prepare(string basePath, string sessionPath, XbeInfo game, IDictionary<string, string> v, List<string> said)
        {
            if (!File.Exists(basePath))
            {
                WriteAtomically(basePath, Fresh());
                said.Add("a new console made (" + Path.GetFileName(basePath) + ")");
            }
            var e = Open(File.ReadAllBytes(basePath));
            if (e == null) { said.Add(Path.GetFileName(basePath) + " is not an EEPROM any Xbox key opens - used as it is"); return null; }
            string O(string key, string fallback) => v != null && v.TryGetValue(key, out var s) && !string.IsNullOrWhiteSpace(s) ? s.Trim() : fallback;

            // The region: the game's (the first it accepts, when the console's is not one of them), or the one chosen.
            uint wants = (game?.Region ?? 0) & 7;
            var region = O("console.region", "follow");
            if (region == "follow")
            {
                if (wants != 0 && (e.Region & wants) == 0)
                {
                    uint first = wants & (uint)-(int)wants;
                    said.Add("region " + Name(e.Region) + " -> " + Name(first) + ", the game's (" + Name(wants) + ")");
                    e.Region = first;
                }
            }
            else if (uint.TryParse(region, out var r) && (r == 1 || r == 2 || r == 4)) { e.Region = r; said.Add("region " + Name(r)); }

            // The video standard: CxbxEeprom's rule - a European console PAL with 60 Hz allowed, a Japanese one NTSC-J, else
            // NTSC; both NTSC with the HD modes.
            var video = O("console.video", "follow");
            if (video != "xemu")
            {
                var mode = video;
                if (mode == "follow")
                    mode = (e.Region & 4) != 0 && (e.Region & 3) == 0 ? "pal60" : (e.Region & 2) != 0 && (e.Region & 1) == 0 ? "ntsc-j-hd" : "ntsc-hd";
                uint flags = e.VideoFlags & ~(Hz60 | Hd480 | Hd720 | Hd1080);
                uint av;
                switch (mode)
                {
                    case "pal50": av = PalI | Hz50; break;
                    case "pal60": av = PalI | Hz50; flags |= Hz60; break;
                    case "ntsc": av = NtscM | Hz60; break;
                    case "ntsc-j-hd": av = NtscJ | Hz60; flags |= Hd480 | Hd720 | Hd1080; break;
                    default: av = NtscM | Hz60; flags |= Hd480 | Hd720 | Hd1080; break;   // ntsc-hd
                }
                e.AvRegion = av; e.VideoFlags = flags;
                said.Add("video " + mode + (video == "follow" ? " (the console's region)" : ""));
            }

            var language = O("console.language", "windows");
            if (language != "xemu")
            {
                uint code = language == "windows" ? IdentityLanguage() : uint.TryParse(language, out var l) ? l : 0;
                if (code >= 1 && code <= 9) { e.Language = code; said.Add("language " + code); }
            }

            if (O("console.timezone", "windows") == "windows" && WindowsZone() is string zone) { e.Zone = zone; said.Add("time zone " + zone); }

            // The HDD key: the pack's (PackIdentity.XboxHddKey), unless console.hddkey=xemu keeps the console's own.
            if (O("console.hddkey", "pack") != "xemu")
            {
                var key = LbIntegrations.Identity.PackIdentity.XboxHddKey();
                if (!e.HddKey.SequenceEqual(key)) { e.HddKey = key; said.Add("HDD key the pack's"); }
            }

            // Never a game refused by parental controls (CxbxEeprom's rule): no restriction, whatever was set.
            BitConverter.GetBytes(0u).CopyTo(e.Data, ParentalGamesAt);
            BitConverter.GetBytes(0u).CopyTo(e.Data, ParentalMoviesAt);

            WriteAtomically(sessionPath, Seal(e));
            return sessionPath;
        }

        /// <summary>The pack's console language ("Your console", 03/10), else Windows', as the Xbox numbers its own: English
        /// 1, Japanese 2, German 3, French 4, Spanish 5, Italian 6, Korean 7, Chinese 8, Portuguese 9 - English for any
        /// other. CxbxEeprom.WindowsLanguage's table.</summary>
        public static uint IdentityLanguage()
        {
            System.Globalization.CultureInfo culture = null;
            try { culture = LbIntegrations.Identity.PackIdentity.Load()?.Culture; } catch { }
            switch ((culture ?? System.Globalization.CultureInfo.CurrentUICulture).TwoLetterISOLanguageName)
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

        internal static string Name(uint r)
        {
            var s = new List<string>();
            if ((r & 1) != 0) s.Add("North America");
            if ((r & 2) != 0) s.Add("Japan");
            if ((r & 4) != 0) s.Add("Europe");
            return s.Count == 0 ? "0x" + r.ToString("X") : string.Join(" + ", s);
        }

        private static void WriteAtomically(string path, byte[] b)
        {
            var tmp = path + ".lbip-tmp";
            File.WriteAllBytes(tmp, b);
            File.Move(tmp, path, overwrite: true);
        }

        // ── time zones ───────────────────────────────────────────────────────

        /// <summary>The 0x2C bytes at 0x64 for each zone the Xbox dashboard offers - XboxEepromEditor's table (Eeprom.cs).</summary>
        internal static readonly (string Zone, string Data)[] Zones =
        {
            ("Samoa", "940200004E5400004E5400000000000000000000000000000000000000000000000000000000000000000000"),
            ("Hawaii", "5802000048535400485354000000000000000000000000000000000000000000000000000000000000000000"),
            ("Alaska", "1C020000595354005944540000000000000000000A05000204010002000000000000000000000000C4FFFFFF"),
            ("Pacific", "E0010000505354005044540000000000000000000A05000204010002000000000000000000000000C4FFFFFF"),
            ("Mountain", "A40100004D5354004D53540000000000000000000A05000204010002000000000000000000000000C4FFFFFF"),
            ("Arizona", "A40100004D5354004D5354000000000000000000000000000000000000000000000000000000000000000000"),
            ("Saskatchewan", "6801000043435354434353540000000000000000000000000000000000000000000000000000000000000000"),
            ("MexicoCity", "680100004D5354004D44540000000000000000000A05000204010002000000000000000000000000C4FFFFFF"),
            ("Central", "68010000435354004344540000000000000000000A05000204010002000000000000000000000000C4FFFFFF"),
            ("CentralAmerica", "6801000043415354434153540000000000000000000000000000000000000000000000000000000000000000"),
            ("Indiana", "2C01000045535400455354000000000000000000000000000000000000000000000000000000000000000000"),
            ("Eastern", "2C010000455354004544540000000000000000000A05000204010002000000000000000000000000C4FFFFFF"),
            ("Bogota", "2C01000053505354535053540000000000000000000000000000000000000000000000000000000000000000"),
            ("Santiago", "F000000050535354505344540000000000000000030206000A020600000000000000000000000000C4FFFFFF"),
            ("Caracas", "F000000053575354535753540000000000000000000000000000000000000000000000000000000000000000"),
            ("Atlantic", "F0000000415354004144540000000000000000000A05000204010002000000000000000000000000C4FFFFFF"),
            ("Greenland", "B4000000475354004744540000000000000000000A05000204010002000000000000000000000000C4FFFFFF"),
            ("BuenosAires", "B400000053455354534553540000000000000000000000000000000000000000000000000000000000000000"),
            ("Brasilia", "B400000045535354455344540000000000000000020200020A030002000000000000000000000000C4FFFFFF"),
            ("MidAtlantic", "780000004D4153544D41445400000000000000000905000203050002000000000000000000000000C4FFFFFF"),
            ("CapeVerdeIslands", "3C00000057415400574154000000000000000000000000000000000000000000000000000000000000000000"),
            ("Azores", "3C000000415354004144540000000000000000000A05000303050002000000000000000000000000C4FFFFFF"),
            ("Casablanca", "0000000047535400475354000000000000000000000000000000000000000000000000000000000000000000"),
            ("London", "00000000474D54004253540000000000000000000A05000203050001000000000000000000000000C4FFFFFF"),
            ("Belgrade", "C4FFFFFF434553544345445400000000000000000A05000303050002000000000000000000000000C4FFFFFF"),
            ("Berlin", "C4FFFFFF574553545745445400000000000000000A05000303050002000000000000000000000000C4FFFFFF"),
            ("Paris", "C4FFFFFF525354005244540000000000000000000A05000303050002000000000000000000000000C4FFFFFF"),
            ("Sarajevo", "C4FFFFFF534353545343445400000000000000000A05000303050002000000000000000000000000C4FFFFFF"),
            ("WestCentralAfrica", "C4FFFFFF57415354574153540000000000000000000000000000000000000000000000000000000000000000"),
            ("Athens", "88FFFFFF475453544754445400000000000000000A05000303050002000000000000000000000000C4FFFFFF"),
            ("Bucharest", "88FFFFFF454553544545445400000000000000000905000103050000000000000000000000000000C4FFFFFF"),
            ("Cairo", "88FFFFFF455354004544540000000000000000000905030205010502000000000000000000000000C4FFFFFF"),
            ("Helsinki", "88FFFFFF464C5354464C445400000000000000000A05000403050003000000000000000000000000C4FFFFFF"),
            ("Jerusalem", "88FFFFFF4A5354004A5354000000000000000000000000000000000000000000000000000000000000000000"),
            ("Pretoria", "88FFFFFF53415354534153540000000000000000000000000000000000000000000000000000000000000000"),
            ("Baghdad", "4CFFFFFF415354004144540000000000000000000A01000404010003000000000000000000000000C4FFFFFF"),
            ("Kuwait", "4CFFFFFF41535400415354000000000000000000000000000000000000000000000000000000000000000000"),
            ("Moscow", "4CFFFFFF525354005244540000000000000000000A05000303050002000000000000000000000000C4FFFFFF"),
            ("Nairobi", "4CFFFFFF45415354454153540000000000000000000000000000000000000000000000000000000000000000"),
            ("AbuDhabi", "10FFFFFF41535400415354000000000000000000000000000000000000000000000000000000000000000000"),
            ("Baku", "10FFFFFF435354004344540000000000000000000A05000303050002000000000000000000000000C4FFFFFF"),
            ("Ekaterinburg", "D4FEFFFF455354004544540000000000000000000A05000303050002000000000000000000000000C4FFFFFF"),
            ("Islamabad", "D4FEFFFF57415354574153540000000000000000000000000000000000000000000000000000000000000000"),
            ("Almaty", "98FEFFFF4E4353544E43445400000000000000000A05000303050002000000000000000000000000C4FFFFFF"),
            ("Dhaka", "98FEFFFF43415354434153540000000000000000000000000000000000000000000000000000000000000000"),
            ("Srilanka", "98FEFFFF53525354535253540000000000000000000000000000000000000000000000000000000000000000"),
            ("Bangkok", "5CFEFFFF53415354534153540000000000000000000000000000000000000000000000000000000000000000"),
            ("Krasnoyarsk", "5CFEFFFF4E4153544E41445400000000000000000A05000303050002000000000000000000000000C4FFFFFF"),
            ("Beijing", "20FEFFFF43535400435354000000000000000000000000000000000000000000000000000000000000000000"),
            ("Irkutsk", "20FEFFFF4E4553544E45445400000000000000000A05000303050002000000000000000000000000C4FFFFFF"),
            ("Perth", "20FEFFFF41575354415753540000000000000000000000000000000000000000000000000000000000000000"),
            ("Singapore", "20FEFFFF4D5053544D5053540000000000000000000000000000000000000000000000000000000000000000"),
            ("Taipei", "20FEFFFF54535400545354000000000000000000000000000000000000000000000000000000000000000000"),
            ("Seoul", "E4FDFFFF4B5354004B5354000000000000000000000000000000000000000000000000000000000000000000"),
            ("Tokyo", "E4FDFFFF54535400545354000000000000000000000000000000000000000000000000000000000000000000"),
            ("Yakutsk", "E4FDFFFF595354005944540000000000000000000A05000303050002000000000000000000000000C4FFFFFF"),
            ("Brisbane", "A8FDFFFF41455354414553540000000000000000000000000000000000000000000000000000000000000000"),
            ("Guam", "A8FDFFFF57505354575053540000000000000000000000000000000000000000000000000000000000000000"),
            ("Hobart", "A8FDFFFF54535400544454000000000000000000030500020A010002000000000000000000000000C4FFFFFF"),
            ("Sydney", "A8FDFFFF41455354414544540000000000000000030500020A050002000000000000000000000000C4FFFFFF"),
            ("Vladivostok", "A8FDFFFF565354005644540000000000000000000A05000303050002000000000000000000000000C4FFFFFF"),
            ("SolomonIslands", "6CFDFFFF43505354435053540000000000000000000000000000000000000000000000000000000000000000"),
            ("Auckland", "30FDFFFF4E5A53544E5A44540000000000000000030300020A010002000000000000000000000000C4FFFFFF"),
            ("FijiIslands", "30FDFFFF46535400465354000000000000000000000000000000000000000000000000000000000000000000"),
            ("Nukualofa", "F4FCFFFF54535400545354000000000000000000000000000000000000000000000000000000000000000000"),
            ("Kiribati", "B8FCFFFF4B5354004B5354000000000000000000000000000000000000000000000000000000000000000000"),
            ("Tehran", "2EFFFFFF495354004944540000000000000000000904020203010002000000000000000000000000C4FFFFFF"),
            ("Kabul", "F2FEFFFF41535400415354000000000000000000000000000000000000000000000000000000000000000000"),
            ("NewDelhi", "B6FEFFFF49535400495354000000000000000000000000000000000000000000000000000000000000000000"),
            ("Kathmandu", "A7FEFFFF4E5354004E5354000000000000000000000000000000000000000000000000000000000000000000"),
            ("Yangon", "7AFEFFFF4D5354004D5354000000000000000000000000000000000000000000000000000000000000000000"),
            ("Adelaide", "C6FDFFFF41435354414344540000000000000000030500020A050002000000000000000000000000C4FFFFFF"),
            ("Darwin", "C6FDFFFF41435354414353540000000000000000000000000000000000000000000000000000000000000000"),
        };

        /// <summary>Windows' zones (TimeZoneInfo ids) and the Xbox's that matches each.</summary>
        private static readonly Dictionary<string, string> FromWindows = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["UTC-11"] = "Samoa", ["Samoa Standard Time"] = "Samoa", ["Hawaiian Standard Time"] = "Hawaii", ["Alaskan Standard Time"] = "Alaska",
            ["Pacific Standard Time"] = "Pacific", ["Mountain Standard Time"] = "Mountain", ["US Mountain Standard Time"] = "Arizona",
            ["Canada Central Standard Time"] = "Saskatchewan", ["Central Standard Time (Mexico)"] = "MexicoCity", ["Central Standard Time"] = "Central",
            ["Central America Standard Time"] = "CentralAmerica", ["US Eastern Standard Time"] = "Indiana", ["Eastern Standard Time"] = "Eastern",
            ["SA Pacific Standard Time"] = "Bogota", ["Pacific SA Standard Time"] = "Santiago", ["Venezuela Standard Time"] = "Caracas",
            ["Atlantic Standard Time"] = "Atlantic", ["Greenland Standard Time"] = "Greenland", ["Argentina Standard Time"] = "BuenosAires",
            ["E. South America Standard Time"] = "Brasilia", ["Mid-Atlantic Standard Time"] = "MidAtlantic", ["UTC-02"] = "MidAtlantic",
            ["Cape Verde Standard Time"] = "CapeVerdeIslands", ["Azores Standard Time"] = "Azores", ["Morocco Standard Time"] = "Casablanca",
            ["GMT Standard Time"] = "London", ["Greenwich Standard Time"] = "Casablanca", ["Central Europe Standard Time"] = "Belgrade",
            ["W. Europe Standard Time"] = "Berlin", ["Romance Standard Time"] = "Paris", ["Central European Standard Time"] = "Sarajevo",
            ["W. Central Africa Standard Time"] = "WestCentralAfrica", ["GTB Standard Time"] = "Athens", ["E. Europe Standard Time"] = "Bucharest",
            ["Egypt Standard Time"] = "Cairo", ["FLE Standard Time"] = "Helsinki", ["Israel Standard Time"] = "Jerusalem",
            ["South Africa Standard Time"] = "Pretoria", ["Arabic Standard Time"] = "Baghdad", ["Arab Standard Time"] = "Kuwait",
            ["Russian Standard Time"] = "Moscow", ["E. Africa Standard Time"] = "Nairobi", ["Arabian Standard Time"] = "AbuDhabi",
            ["Azerbaijan Standard Time"] = "Baku", ["Ekaterinburg Standard Time"] = "Ekaterinburg", ["Pakistan Standard Time"] = "Islamabad",
            ["Central Asia Standard Time"] = "Almaty", ["Bangladesh Standard Time"] = "Dhaka", ["Sri Lanka Standard Time"] = "Srilanka",
            ["SE Asia Standard Time"] = "Bangkok", ["North Asia Standard Time"] = "Krasnoyarsk", ["China Standard Time"] = "Beijing",
            ["North Asia East Standard Time"] = "Irkutsk", ["W. Australia Standard Time"] = "Perth", ["Singapore Standard Time"] = "Singapore",
            ["Taipei Standard Time"] = "Taipei", ["Korea Standard Time"] = "Seoul", ["Tokyo Standard Time"] = "Tokyo", ["Yakutsk Standard Time"] = "Yakutsk",
            ["E. Australia Standard Time"] = "Brisbane", ["West Pacific Standard Time"] = "Guam", ["Tasmania Standard Time"] = "Hobart",
            ["AUS Eastern Standard Time"] = "Sydney", ["Vladivostok Standard Time"] = "Vladivostok", ["Central Pacific Standard Time"] = "SolomonIslands",
            ["New Zealand Standard Time"] = "Auckland", ["Fiji Standard Time"] = "FijiIslands", ["Tonga Standard Time"] = "Nukualofa",
            ["Line Islands Standard Time"] = "Kiribati", ["Iran Standard Time"] = "Tehran", ["Afghanistan Standard Time"] = "Kabul",
            ["India Standard Time"] = "NewDelhi", ["Nepal Standard Time"] = "Kathmandu", ["Myanmar Standard Time"] = "Yangon",
            ["Cen. Australia Standard Time"] = "Adelaide", ["AUS Central Standard Time"] = "Darwin",
        };

        /// <summary>The Xbox zone for Windows' own: by name, else the first with the same offset and the same daylight saving.
        /// Null when none has that offset.</summary>
        public static string WindowsZone(TimeZoneInfo tz = null)
        {
            try
            {
                tz ??= TimeZoneInfo.Local;
                if (FromWindows.TryGetValue(tz.Id, out var known)) return known;
                int bias = -(int)tz.BaseUtcOffset.TotalMinutes;
                bool dst = tz.SupportsDaylightSavingTime;
                foreach (var (zone, data) in Zones)
                {
                    var b = Hex(data);
                    bool zoneDst = b.Skip(20).Any(x => x != 0);
                    if (BitConverter.ToInt32(b, 0) == bias && zoneDst == dst) return zone;
                }
                return null;
            }
            catch { return null; }
        }

        private static string ZoneName(byte[] data)
        {
            var at = data.Skip(ZoneAt).Take(ZoneLength).ToArray();
            foreach (var (zone, hex) in Zones) if (Hex(hex).SequenceEqual(at)) return zone;
            return null;
        }

        private static void SetZone(byte[] data, string zone)
        {
            var hit = Zones.FirstOrDefault(z => string.Equals(z.Zone, zone, StringComparison.OrdinalIgnoreCase));
            if (hit.Data == null) throw new ArgumentException("no such Xbox time zone: " + zone);
            Hex(hit.Data).CopyTo(data, ZoneAt);
        }

        private static byte[] Hex(string s) => Enumerable.Range(0, s.Length / 2).Select(i => Convert.ToByte(s.Substring(i * 2, 2), 16)).ToArray();
    }
}
