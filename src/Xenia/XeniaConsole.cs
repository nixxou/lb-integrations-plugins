// The console's own settings - language, country, time zone, clock, video region and resolution, music volume:
// what Xenia Canary's "Console settings" window edits (File > Console settings, greyed while a game runs).
//
// THEY ARE NOT CVARS. Canary keeps them in <storage root>\xconfig.settings, the raw XConfigData struct of
// src\xenia\kernel\xconfig.h: 6680 bytes, packed (pragma pack 1), every number BIG-ENDIAN, no checksum
// checked. Xenia reads the file whole at start (XConfig::XConfig) and writes it whole when its window saves
// (WriteXConfig) - so a file written here while Xenia is closed is simply what it starts on. The language is the
// one games display: a game with French in it starts in French.
//
// THE LAYOUT, from the struct (01/10, canary 74c4e4a), and checked on a real file Xenia wrote with its defaults:
// the probe builds Defaults() and compares it byte for byte (--xenia-console).
//     0x0000  Static      270 bytes        0x06E6  Secured     512        0x08E6  User        509
//     0x06E6+0x28  av_region  u32          User+0x08  time_zone_bias i32  ... (see the offsets below)
//
// Canary only: master has no such file, and this pack installs canary.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace LbIntegrations.Xenia
{
    internal sealed class XeniaTimeZone
    {
        public int Bias;               // minutes WEST of UTC, Windows' sign: Paris is -60
        public string StdName, DltName;
        public byte[] StdDate, DltDate;
        public int StdBias, DltBias;
        public string Name;
        public bool HasDst => DltName.Length > 0;
        public override string ToString() => Name;
    }

    /// <summary>What the page edits, read off the file or the defaults.</summary>
    internal sealed class XeniaConsoleValues
    {
        public uint Language;
        public byte Country;
        public int TimeZone;           // index in XeniaConsole.TimeZones; -1 when the file holds one not in the list
        public bool Hour24, DstOff;
        public uint AvRegion;
        public uint Resolution;        // width << 16 | height
        public float MusicVolume;

        public XeniaConsoleValues Copy() => (XeniaConsoleValues)MemberwiseClone();

        public bool SameAs(XeniaConsoleValues o)
            => o != null && Language == o.Language && Country == o.Country && TimeZone == o.TimeZone && Hour24 == o.Hour24
               && DstOff == o.DstOff && AvRegion == o.AvRegion && Resolution == o.Resolution && Math.Abs(MusicVolume - o.MusicVolume) < 0.001f;

        /// <summary>One line, for the log and the install notification.</summary>
        public string Describe()
            => XeniaConsole.LanguageName(Language) + ", " + XeniaConsole.CountryName(Country) + ", "
               + (TimeZone >= 0 ? XeniaConsole.TimeZones[TimeZone].Name : "a time zone of its own") + (Hour24 ? ", 24-hour clock" : ", 12-hour clock");
    }

    internal static class XeniaConsole
    {
        public const string FileName = "xconfig.settings";
        public const int Size = 6680;

        private const int Secured = 0x06E6, User = 0x08E6, Iptv = 0x1808;

        private const int AvRegionAt = Secured + 0x28;
        private const int TzBiasAt = User + 0x08, TzStdNameAt = User + 0x0C, TzDltNameAt = User + 0x10, TzStdDateAt = User + 0x14,
                          TzDltDateAt = User + 0x18, TzStdBiasAt = User + 0x1C, TzDltBiasAt = User + 0x20;
        private const int LanguageAt = User + 0x2C, VideoFlagsAt = User + 0x30, AudioFlagsAt = User + 0x34, RetailFlagsAt = User + 0x38,
                          CountryAt = User + 0x40, ParentalFlagsAt = User + 0x41, HdmiAt = User + 0x15C, ComponentAt = User + 0x160,
                          VgaAt = User + 0x164, ParentalGameAt = User + 0x168, MusicVolumeAt = User + 0x1C1;
        private const int ProviderNameAt = Iptv + 0x08;

        private const uint DstOffFlag = 0x00000002, Hour24Flag = 0x00000008, DashboardInitialized = 0x00000040, WidescreenFlag = 0x00010000;

        public const uint Ntsc = 0x00400100;
        public static readonly (uint Value, string Name)[] AvRegions =
        {
            (0x00400100, "NTSC"), (0x00400200, "NTSC-J"), (0x00400400, "PAL"), (0x00800300, "PAL 50Hz"),
        };

        // console_settings_dialog.cc's kLanguageMap. 10 is not offered there either.
        public static readonly (uint Value, string Name)[] Languages =
        {
            (1, "English"), (2, "Japanese"), (3, "German"), (4, "French"), (5, "Spanish"), (6, "Italian"), (7, "Korean"),
            (8, "Traditional Chinese"), (9, "Portuguese"), (11, "Polish"), (12, "Russian"), (13, "Swedish"), (14, "Turkish"),
            (15, "Norwegian"), (16, "Dutch"), (17, "Simplified Chinese"),
        };

        // console_settings_dialog.cc's kCountryMap: the value and its ISO code. 17 and 94 are not there either.
        public static readonly (byte Value, string Code)[] Countries =
        {
            (1, "AE"), (2, "AL"), (3, "AM"), (4, "AR"), (5, "AT"), (6, "AU"), (7, "AZ"), (8, "BE"), (9, "BG"), (10, "BH"),
            (11, "BN"), (12, "BO"), (13, "BR"), (14, "BY"), (15, "BZ"), (16, "CA"), (18, "CH"), (19, "CL"), (20, "CN"), (21, "CO"),
            (22, "CR"), (23, "CZ"), (24, "DE"), (25, "DK"), (26, "DO"), (27, "DZ"), (28, "EC"), (29, "EE"), (30, "EG"), (31, "ES"),
            (32, "FI"), (33, "FO"), (34, "FR"), (35, "GB"), (36, "GE"), (37, "GR"), (38, "GT"), (39, "HK"), (40, "HN"), (41, "HR"),
            (42, "HU"), (43, "ID"), (44, "IE"), (45, "IL"), (46, "IN"), (47, "IQ"), (48, "IR"), (49, "IS"), (50, "IT"), (51, "JM"),
            (52, "JO"), (53, "JP"), (54, "KE"), (55, "KG"), (56, "KR"), (57, "KW"), (58, "KZ"), (59, "LB"), (60, "LI"), (61, "LT"),
            (62, "LU"), (63, "LV"), (64, "LY"), (65, "MA"), (66, "MC"), (67, "MK"), (68, "MN"), (69, "MO"), (70, "MV"), (71, "MX"),
            (72, "MY"), (73, "NI"), (74, "NL"), (75, "NO"), (76, "NZ"), (77, "OM"), (78, "PA"), (79, "PE"), (80, "PH"), (81, "PK"),
            (82, "PL"), (83, "PR"), (84, "PT"), (85, "PY"), (86, "QA"), (87, "RO"), (88, "RU"), (89, "SA"), (90, "SE"), (91, "SG"),
            (92, "SI"), (93, "SK"), (95, "SV"), (96, "SY"), (97, "TH"), (98, "TN"), (99, "TR"), (100, "TT"), (101, "TW"), (102, "UA"),
            (103, "US"), (104, "UY"), (105, "UZ"), (106, "VE"), (107, "VN"), (108, "YE"), (109, "ZA"),
        };
        public const byte UnitedStates = 103;

        // xconfig.h's XVGAResolution: the list the Console settings window offers.
        public static readonly uint[] Resolutions =
        {
            R(640, 480), R(640, 576), R(720, 480), R(720, 576), R(800, 600), R(848, 480), R(1024, 768), R(1152, 864), R(1280, 720),
            R(1280, 768), R(1280, 960), R(1280, 1024), R(1360, 768), R(1440, 768), R(1440, 900), R(1600, 768), R(1680, 720),
            R(1680, 1050), R(1920, 540), R(1920, 1080),
        };
        private static uint R(int w, int h) => (uint)(w << 16 | h);
        public static string ResolutionName(uint r) => (r >> 16) + "x" + (r & 0xFFFF);
        private static bool IsWidescreen(uint r)
        {
            int w = (int)(r >> 16), h = (int)(r & 0xFFFF), g = Gcd(w, h);
            return g == 0 || !(w / g == 4 && h / g == 3);
        }
        private static int Gcd(int a, int b) => b == 0 ? a : Gcd(b, a % b);

        // xconfig.h's kTimezones, generated from the header (01/10) - same order, so an index is Xenia's index.
        public static readonly XeniaTimeZone[] TimeZones =
        {
            Tz(720, "IDLW", "", "0x00,0x00,0x00,0x00", "0x00,0x00,0x00,0x00", 0, "GMT-12 Tokelau"),
            Tz(660, "NT", "", "0x00,0x00,0x00,0x00", "0x00,0x00,0x00,0x00", 0, "GMT-11 Samoa"),
            Tz(600, "HST", "", "0x00,0x00,0x00,0x00", "0x00,0x00,0x00,0x00", 0, "GMT-10 Hawaii"),
            Tz(540, "YST", "YDT", "0x0A,0x05,0x00,0x02", "0x04,0x01,0x00,0x02", -60, "GMT-09 Alaska"),
            Tz(480, "PST", "PDT", "0x0A,0x05,0x00,0x02", "0x04,0x01,0x00,0x02", -60, "GMT-08 Pacific (U.S. & Canada)"),
            Tz(420, "AMST", "", "0x00,0x00,0x00,0x00", "0x00,0x00,0x00,0x00", 0, "GMT-07 Arizona"),
            Tz(420, "MST", "MST", "0x0A,0x05,0x00,0x02", "0x04,0x01,0x00,0x02", -60, "GMT-07 Mountain (U.S. & Canada)"),
            Tz(360, "CAST", "", "0x00,0x00,0x00,0x00", "0x00,0x00,0x00,0x00", 0, "GMT-06 Central America"),
            Tz(360, "CST", "CDT", "0x0A,0x05,0x00,0x02", "0x04,0x01,0x00,0x02", -60, "GMT-06 Central (U.S. & Canada)"),
            Tz(360, "MST", "MDT", "0x0A,0x05,0x00,0x02", "0x04,0x01,0x00,0x02", -60, "GMT-06 Mexico City"),
            Tz(360, "CCST", "", "0x00,0x00,0x00,0x00", "0x00,0x00,0x00,0x00", 0, "GMT-06 Saskatchewan"),
            Tz(300, "EST", "EDT", "0x0A,0x05,0x00,0x02", "0x04,0x01,0x00,0x02", -60, "GMT-05 Eastern (U.S. & Canada)"),
            Tz(300, "EST", "", "0x00,0x00,0x00,0x00", "0x00,0x00,0x00,0x00", 0, "GMT-05 Indiana"),
            Tz(300, "SPST", "", "0x00,0x00,0x00,0x00", "0x00,0x00,0x00,0x00", 0, "GMT-05 Bogota"),
            Tz(240, "AST", "ADT", "0x0A,0x05,0x00,0x02", "0x04,0x01,0x00,0x02", -60, "GMT-04 Atlantic (U.S. & Canada)"),
            Tz(240, "SWST", "", "0x00,0x00,0x00,0x00", "0x00,0x00,0x00,0x00", 0, "GMT-04 Caracas"),
            Tz(240, "PSST", "PSDT", "0x03,0x02,0x06,0x00", "0x0A,0x02,0x06,0x00", -60, "GMT-04 Santiago"),
            Tz(210, "NST", "NDT", "0x0A,0x05,0x00,0x02", "0x04,0x01,0x00,0x02", -60, "GMT-03:30 Newfoundland"),
            Tz(180, "ESST", "ESDT", "0x02,0x02,0x00,0x02", "0x0A,0x03,0x00,0x02", -60, "GMT-03 Brasilia"),
            Tz(180, "SEST", "", "0x00,0x00,0x00,0x00", "0x00,0x00,0x00,0x00", 0, "GMT-03 Buenos Aires"),
            Tz(180, "GST", "GDT", "0x0A,0x05,0x00,0x02", "0x04,0x01,0x00,0x02", -60, "GMT-03 Greenland"),
            Tz(120, "MAST", "MADT", "0x09,0x05,0x00,0x02", "0x03,0x05,0x00,0x02", -60, "GMT-02 Mid-Atlantic"),
            Tz(60, "AST", "ADT", "0x0A,0x05,0x00,0x03", "0x03,0x05,0x00,0x02", -60, "GMT-01 Azores"),
            Tz(60, "WAT", "", "0x00,0x00,0x00,0x00", "0x00,0x00,0x00,0x00", 0, "GMT-01 Cape Verde"),
            Tz(0, "GST", "", "0x00,0x00,0x00,0x00", "0x00,0x00,0x00,0x00", 0, "GMT+00 Casablanca"),
            Tz(0, "GMT", "BST", "0x0A,0x05,0x00,0x02", "0x03,0x05,0x00,0x01", -60, "GMT+00 London"),
            Tz(-60, "WEST", "WEDT", "0x0A,0x05,0x00,0x03", "0x03,0x05,0x00,0x02", -60, "GMT+01 Berlin"),
            Tz(-60, "CEST", "CEDT", "0x0A,0x05,0x00,0x03", "0x03,0x05,0x00,0x02", -60, "GMT+01 Belgrade"),
            Tz(-60, "RST", "RDT", "0x0A,0x05,0x00,0x03", "0x03,0x05,0x00,0x02", -60, "GMT+01 Paris, Madrid"),
            Tz(-60, "SCST", "SCDT", "0x0A,0x05,0x00,0x03", "0x03,0x05,0x00,0x02", -60, "GMT+01 Sarajevo"),
            Tz(-60, "WAST", "", "0x00,0x00,0x00,0x00", "0x00,0x00,0x00,0x00", 0, "GMT+01 W. Central Africa"),
            Tz(-120, "GTST", "GTDT", "0x0A,0x05,0x00,0x03", "0x03,0x05,0x00,0x02", -60, "GMT+02 Athens"),
            Tz(-120, "EEST", "EEDT", "0x09,0x05,0x00,0x01", "0x03,0x05,0x00,0x00", -60, "GMT+02 Bucharest"),
            Tz(-120, "EST", "EDT", "0x09,0x05,0x03,0x02", "0x05,0x01,0x05,0x02", -60, "GMT+02 Cairo"),
            Tz(-120, "SAST", "", "0x00,0x00,0x00,0x00", "0x00,0x00,0x00,0x00", 0, "GMT+02 Pretoria"),
            Tz(-120, "FLST", "FLDT", "0x0A,0x05,0x00,0x04", "0x03,0x05,0x00,0x03", -60, "GMT+02 Helsinki"),
            Tz(-120, "JST", "", "0x00,0x00,0x00,0x00", "0x00,0x00,0x00,0x00", 0, "GMT+02 Jerusalem"),
            Tz(-180, "AST", "ADT", "0x0A,0x01,0x00,0x04", "0x04,0x01,0x00,0x03", -60, "GMT+03 Baghdad"),
            Tz(-180, "AST", "", "0x00,0x00,0x00,0x00", "0x00,0x00,0x00,0x00", 0, "GMT+03 Kuwait"),
            Tz(-180, "RST", "RDT", "0x0A,0x05,0x00,0x03", "0x03,0x05,0x00,0x02", -60, "GMT+03 Moscow"),
            Tz(-180, "EAST", "", "0x00,0x00,0x00,0x00", "0x00,0x00,0x00,0x00", 0, "GMT+03 Nairobi"),
            Tz(-210, "IST", "IDT", "0x09,0x04,0x02,0x02", "0x03,0x01,0x00,0x02", -60, "GMT+03:30 Tehran"),
            Tz(-240, "AST", "", "0x00,0x00,0x00,0x00", "0x00,0x00,0x00,0x00", 0, "GMT+04 Abu Dhabi"),
            Tz(-240, "CST", "CDT", "0x0A,0x05,0x00,0x03", "0x03,0x05,0x00,0x02", -60, "GMT+04 Baku"),
            Tz(-270, "AST", "", "0x00,0x00,0x00,0x00", "0x00,0x00,0x00,0x00", 0, "GMT+04:30 Kabul"),
            Tz(-300, "EST", "EDT", "0x0A,0x05,0x00,0x03", "0x03,0x05,0x00,0x02", -60, "GMT+05 Ekaterinburg"),
            Tz(-300, "WAST", "", "0x00,0x00,0x00,0x00", "0x00,0x00,0x00,0x00", 0, "GMT+05 Islamabad"),
            Tz(-330, "IST", "", "0x00,0x00,0x00,0x00", "0x00,0x00,0x00,0x00", 0, "GMT+05:30 New Delhi"),
            Tz(-345, "NST", "", "0x00,0x00,0x00,0x00", "0x00,0x00,0x00,0x00", 0, "GMT+05:45 Kathmandu"),
            Tz(-360, "NCST", "NCDT", "0x0A,0x05,0x00,0x03", "0x03,0x05,0x00,0x02", -60, "GMT+06 Almaty"),
            Tz(-360, "CAST", "", "0x00,0x00,0x00,0x00", "0x00,0x00,0x00,0x00", 0, "GMT+06 Dhaka"),
            Tz(-360, "SRST", "", "0x00,0x00,0x00,0x00", "0x00,0x00,0x00,0x00", 0, "GMT+06 Sri Lanka"),
            Tz(-390, "MST", "", "0x00,0x00,0x00,0x00", "0x00,0x00,0x00,0x00", 0, "GMT+06:30 Yangon"),
            Tz(-420, "SAST", "", "0x00,0x00,0x00,0x00", "0x00,0x00,0x00,0x00", 0, "GMT+07 Bangkok"),
            Tz(-420, "NAST", "NADT", "0x0A,0x05,0x00,0x03", "0x03,0x05,0x00,0x02", -60, "GMT+07 Krasnoyarsk"),
            Tz(-480, "CST", "", "0x00,0x00,0x00,0x00", "0x00,0x00,0x00,0x00", 0, "GMT+08 Beijing"),
            Tz(-480, "NEST", "NEDT", "0x0A,0x05,0x00,0x03", "0x03,0x05,0x00,0x02", -60, "GMT+08 Irkutsk"),
            Tz(-480, "MPST", "", "0x00,0x00,0x00,0x00", "0x00,0x00,0x00,0x00", 0, "GMT+08 Singapore"),
            Tz(-480, "AWST", "", "0x00,0x00,0x00,0x00", "0x00,0x00,0x00,0x00", 0, "GMT+08 Perth"),
            Tz(-480, "TST", "", "0x00,0x00,0x00,0x00", "0x00,0x00,0x00,0x00", 0, "GMT+08 Taipei"),
            Tz(-540, "TST", "", "0x00,0x00,0x00,0x00", "0x00,0x00,0x00,0x00", 0, "GMT+09 Tokyo"),
            Tz(-540, "KST", "", "0x00,0x00,0x00,0x00", "0x00,0x00,0x00,0x00", 0, "GMT+09 Seoul"),
            Tz(-540, "YST", "YDT", "0x0A,0x05,0x00,0x03", "0x03,0x05,0x00,0x02", -60, "GMT+09 Yakutsk"),
            Tz(-570, "ACST", "ACDT", "0x03,0x05,0x00,0x02", "0x0A,0x05,0x00,0x02", -60, "GMT+09:30 Adelaide"),
            Tz(-570, "ACST", "", "0x00,0x00,0x00,0x00", "0x00,0x00,0x00,0x00", 0, "GMT+09:30 Darwin"),
            Tz(-600, "AEST", "", "0x00,0x00,0x00,0x00", "0x00,0x00,0x00,0x00", 0, "GMT+10 Brisbane"),
            Tz(-600, "AEST", "AEDT", "0x03,0x05,0x00,0x02", "0x0A,0x05,0x00,0x02", -60, "GMT+10 Sydney"),
            Tz(-600, "WPST", "", "0x00,0x00,0x00,0x00", "0x00,0x00,0x00,0x00", 0, "GMT+10 Guam"),
            Tz(-600, "TST", "TDT", "0x03,0x05,0x00,0x02", "0x0A,0x01,0x00,0x02", -60, "GMT+10 Hobart"),
            Tz(-600, "VST", "VDT", "0x0A,0x05,0x00,0x03", "0x03,0x05,0x00,0x02", -60, "GMT+10 Vladivostok"),
            Tz(-660, "CPST", "", "0x00,0x00,0x00,0x00", "0x00,0x00,0x00,0x00", 0, "GMT+11 Solomon Islands"),
            Tz(-720, "NZST", "NZDT", "0x03,0x03,0x00,0x02", "0x0A,0x01,0x00,0x02", -60, "GMT+12 Auckland"),
            Tz(-720, "FST", "", "0x00,0x00,0x00,0x00", "0x00,0x00,0x00,0x00", 0, "GMT+12 Fiji Islands"),
            Tz(-780, "TST", "", "0x00,0x00,0x00,0x00", "0x00,0x00,0x00,0x00", 0, "GMT+13 Nuku'alofa"),
            Tz(-840, "KST", "", "0x00,0x00,0x00,0x00", "0x00,0x00,0x00,0x00", 0, "GMT+14 Kiribati"),
        };
        /// <summary>Xenia's default zone, kTimezones[0x19].</summary>
        public const int London = 0x19;

        private static XeniaTimeZone Tz(int bias, string std, string dlt, string stdDate, string dltDate, int dltBias, string name)
            => new XeniaTimeZone
            {
                Bias = bias, StdName = std, DltName = dlt, StdDate = Bytes(stdDate), DltDate = Bytes(dltDate), StdBias = 0, DltBias = dltBias, Name = name,
            };

        private static byte[] Bytes(string list) => list.Split(',').Select(b => Convert.ToByte(b.Trim(), 16)).ToArray();

        public static string LanguageName(uint v) => Languages.FirstOrDefault(l => l.Value == v).Name ?? "language " + v;

        public static string CountryName(byte v)
        {
            var code = Countries.FirstOrDefault(c => c.Value == v).Code;
            if (code == null) return "country " + v;
            try { return new RegionInfo(code).EnglishName; } catch { return code; }
        }

        public static string PathIn(string storageRoot) => Path.Combine(storageRoot, FileName);

        // ── the file ─────────────────────────────────────────────────────────

        /// <summary>What Xenia writes when the file is missing (XConfig::SetDefaults), byte for byte.</summary>
        public static byte[] Defaults()
        {
            var b = new byte[Size];
            U32(b, AvRegionAt, Ntsc);
            U32(b, LanguageAt, 1);
            b[CountryAt] = UnitedStates;
            U32(b, AudioFlagsAt, 0x00010000 /* DolbyDigital */ | 0x00000001 /* DolbyProLogic */);
            U32(b, HdmiAt, R(1280, 720));
            U32(b, ComponentAt, R(1280, 720));
            U32(b, VgaAt, R(720, 576));
            U32(b, RetailFlagsAt, DashboardInitialized);
            U32(b, VideoFlagsAt, 0);
            b[ParentalFlagsAt] = 0x01 /* XBLAllowed */ | 0x02 /* XBLMembershipCreationAllowed */;
            U32(b, ParentalGameAt, 0xFF /* NoGameRestrictions */);
            F32(b, MusicVolumeAt, 0.7f);
            PutZone(b, TimeZones[London]);
            var name = Encoding.BigEndianUnicode.GetBytes("Xenia TV");
            Array.Copy(name, 0, b, ProviderNameAt, name.Length);
            return b;
        }

        /// <summary>The file's values - the defaults' when it is missing or not the size it should be.</summary>
        public static XeniaConsoleValues Read(string storageRoot)
        {
            var b = Load(storageRoot, out _);
            var v = new XeniaConsoleValues
            {
                Language = U32(b, LanguageAt), Country = b[CountryAt], AvRegion = U32(b, AvRegionAt), Resolution = U32(b, HdmiAt),
                MusicVolume = F32(b, MusicVolumeAt),
                Hour24 = (U32(b, RetailFlagsAt) & Hour24Flag) != 0, DstOff = (U32(b, RetailFlagsAt) & DstOffFlag) != 0,
                TimeZone = -1,
            };
            for (int i = 0; i < TimeZones.Length; i++)
                if (ZoneIs(b, TimeZones[i])) { v.TimeZone = i; break; }
            return v;
        }

        /// <summary>Is there a file yet? Xenia writes it at its first start.</summary>
        public static bool Exists(string storageRoot) => File.Exists(PathIn(storageRoot));

        /// <summary>The values written over the file as it is - every other byte kept - through a temporary file.</summary>
        public static void Write(string storageRoot, XeniaConsoleValues v)
        {
            var b = Load(storageRoot, out var found);
            U32(b, LanguageAt, v.Language);
            b[CountryAt] = v.Country;
            U32(b, AvRegionAt, v.AvRegion);
            // As the Console settings window does: choosing a resolution sets the widescreen flag with it. Only when it is
            // CHOSEN - Xenia's own defaults are 1280x720 with the flag off, and an untouched resolution keeps them.
            if (U32(b, HdmiAt) != v.Resolution)
            {
                U32(b, HdmiAt, v.Resolution);
                var video = U32(b, VideoFlagsAt);
                U32(b, VideoFlagsAt, IsWidescreen(v.Resolution) ? video | WidescreenFlag : video & ~WidescreenFlag);
            }
            F32(b, MusicVolumeAt, v.MusicVolume);
            var retail = U32(b, RetailFlagsAt) & ~(Hour24Flag | DstOffFlag);
            U32(b, RetailFlagsAt, retail | (v.Hour24 ? Hour24Flag : 0) | (v.DstOff ? DstOffFlag : 0));
            if (v.TimeZone >= 0 && v.TimeZone < TimeZones.Length) PutZone(b, TimeZones[v.TimeZone]);

            var path = PathIn(storageRoot);
            Directory.CreateDirectory(storageRoot);
            var tmp = path + ".tmp";
            File.WriteAllBytes(tmp, b);
            File.Move(tmp, path, overwrite: true);
            Log.Info("console settings written (" + (found ? "over Xenia's file" : "a new file, Xenia's defaults under them") + "): " + v.Describe()
                     + ", " + AvRegions.FirstOrDefault(r => r.Value == v.AvRegion).Name + ", " + ResolutionName(v.Resolution) + " -> " + path);
        }

        private static byte[] Load(string storageRoot, out bool found)
        {
            found = false;
            try
            {
                var path = PathIn(storageRoot);
                if (File.Exists(path))
                {
                    var b = File.ReadAllBytes(path);
                    if (b.Length == Size) { found = true; return b; }
                    Log.Warn(path + " is " + b.Length + " bytes, not " + Size + " - read as Xenia's defaults");
                }
            }
            catch (Exception ex) { Log.Warn("could not read the console settings", ex); }
            return Defaults();
        }

        private static void PutZone(byte[] b, XeniaTimeZone z)
        {
            I32(b, TzBiasAt, z.Bias);
            Name4(b, TzStdNameAt, z.StdName);
            Name4(b, TzDltNameAt, z.DltName);
            Array.Copy(z.StdDate, 0, b, TzStdDateAt, 4);
            Array.Copy(z.DltDate, 0, b, TzDltDateAt, 4);
            I32(b, TzStdBiasAt, z.StdBias);
            I32(b, TzDltBiasAt, z.DltBias);
        }

        /// <summary>The comparison Xenia's window makes to find the zone in its list: every field.</summary>
        private static bool ZoneIs(byte[] b, XeniaTimeZone z)
        {
            var probe = new byte[b.Length];
            Array.Copy(b, probe, b.Length);
            PutZone(probe, z);
            for (int i = TzBiasAt; i < TzDltBiasAt + 4; i++) if (probe[i] != b[i]) return false;
            return true;
        }

        private static void Name4(byte[] b, int at, string s)
        {
            for (int i = 0; i < 4; i++) b[at + i] = i < s.Length ? (byte)s[i] : (byte)0;
        }

        private static uint U32(byte[] b, int at) => (uint)(b[at] << 24 | b[at + 1] << 16 | b[at + 2] << 8 | b[at + 3]);
        private static void U32(byte[] b, int at, uint v) { b[at] = (byte)(v >> 24); b[at + 1] = (byte)(v >> 16); b[at + 2] = (byte)(v >> 8); b[at + 3] = (byte)v; }
        private static void I32(byte[] b, int at, int v) => U32(b, at, unchecked((uint)v));
        private static float F32(byte[] b, int at) => BitConverter.Int32BitsToSingle(unchecked((int)U32(b, at)));
        private static void F32(byte[] b, int at, float v) => U32(b, at, unchecked((uint)BitConverter.SingleToInt32Bits(v)));

        // ── what Windows says ────────────────────────────────────────────────

        /// <summary>The console set as this Windows is: its display language, its region, its time zone, its clock.
        /// What Xenia has no way to know does not move: the video region, the resolution, the music volume.</summary>
        public static XeniaConsoleValues FromWindows(XeniaConsoleValues start)
        {
            var v = start.Copy();
            try
            {
                var ui = CultureInfo.CurrentUICulture;
                var lang = LanguageOf(ui);
                if (lang != 0) v.Language = lang;
            }
            catch { }
            try
            {
                var code = RegionInfo.CurrentRegion.TwoLetterISORegionName;
                var country = Countries.FirstOrDefault(c => string.Equals(c.Code, code, StringComparison.OrdinalIgnoreCase));
                if (country.Code != null) v.Country = country.Value;
            }
            catch { }
            try
            {
                var tz = ZoneOf(TimeZoneInfo.Local);
                if (tz >= 0) { v.TimeZone = tz; v.DstOff = false; }
            }
            catch { }
            try { v.Hour24 = CultureInfo.CurrentCulture.DateTimeFormat.ShortTimePattern.Contains("H"); } catch { }
            return v;
        }

        private static uint LanguageOf(CultureInfo c)
        {
            switch (c.TwoLetterISOLanguageName.ToLowerInvariant())
            {
                case "en": return 1;
                case "ja": return 2;
                case "de": return 3;
                case "fr": return 4;
                case "es": return 5;
                case "it": return 6;
                case "ko": return 7;
                case "zh":
                    return c.Name.IndexOf("Hant", StringComparison.OrdinalIgnoreCase) >= 0 || c.Name.EndsWith("TW") || c.Name.EndsWith("HK") || c.Name.EndsWith("MO") ? 8u : 17u;
                case "pt": return 9;
                case "pl": return 11;
                case "ru": return 12;
                case "sv": return 13;
                case "tr": return 14;
                case "nb": case "nn": case "no": return 15;
                case "nl": return 16;
                default: return 0;
            }
        }

        /// <summary>The zone of the list with Windows' offset and its daylight saving - and, of those, the one
        /// whose city Windows names ("(UTC+01:00) Brussels, Copenhagen, Madrid, Paris" picks "Paris, Madrid").
        /// -1 when no zone has the offset.</summary>
        internal static int ZoneOf(TimeZoneInfo local)
        {
            int bias = -(int)local.BaseUtcOffset.TotalMinutes;
            bool dst = local.SupportsDaylightSavingTime;
            var same = Enumerable.Range(0, TimeZones.Length).Where(i => TimeZones[i].Bias == bias).ToList();
            if (same.Count == 0) return -1;
            var withDst = same.Where(i => TimeZones[i].HasDst == dst).ToList();
            if (withDst.Count > 0) same = withDst;
            var said = (local.DisplayName ?? "") + " " + (local.StandardName ?? "") + " " + (local.Id ?? "");
            foreach (var i in same)
            {
                var cities = TimeZones[i].Name.Substring(TimeZones[i].Name.IndexOf(' ') + 1).Split(',', '(', ')', '&').Select(s => s.Trim()).Where(s => s.Length > 2);
                if (cities.Any(city => said.IndexOf(city, StringComparison.OrdinalIgnoreCase) >= 0)) return i;
            }
            return same[0];
        }
    }
}
