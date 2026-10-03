// A DSi console's owner, written straight into a BLANK NAND - so a console built from a dump never set up skips the DSi
// menu's welcome sequence (Mehdi, 03/10: "on set direct les valeurs et on boot le jeu").
//
// THE FILES: 0:/shared1/TWLCFG0.dat and TWLCFG1.dat, two identical copies, 16 KB of which 0x1B0 bytes are used
// (melonDS DSi_NAND.h, DSiFirmwareSystemSettings; GBATEK "DSi SD/MMC Firmware System Settings Data Files"):
//   0x000  SHA-1 of 0x088..0x1AF            checked against four files, a blank and a set-up one of USA and EUR
//   0x081  update counter, 7 bits
//   0x088  config flags                      a blank dump has 0x08 (Wi-Fi on), the welcome sequence sets bits 0-2: 0x0F
//   0x08D  country, Wii codes                limited by region - GBATEK "DSi Regions", below
//   0x08E  language                          limited by the console's language mask, HWINFO_S.dat 0x88
//   0x094  4 bytes                           FF after the welcome sequence, measured twice
//   0x098  EULA version                      0 after it (GBATEK: 0 = none / country changed)
//   0x0AC  unknown                           3 after it, measured twice
//   0x0CC  favourite colour, 0x0CE birthday month, 0x0CF day, 0x0D0 nickname (UTF-16, 10 + a zero)
// What the sequence sets and the owner is not - the touch calibration (redone or not, measured both ways), the clock
// (RTC year and offset: 0 is melonDS's own clock), the last title used - is left as the dump has it.
//
// BLANK IS THE FLAGS: bits 0-2 of 0x088 clear. A NAND whose owner was set - a real console's, or one already built - is
// never written here: the caller only asks for a console being made from a dump, and this refuses a set-up one.

#nullable disable

using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using LbIntegrations.Identity;

namespace LbIntegrations.Dsi
{
    internal static class DsiUserSettings
    {
        public const string Settings0 = "0:/shared1/TWLCFG0.dat", Settings1 = "0:/shared1/TWLCFG1.dat";
        private const int Used = 0x1B0, HashFrom = 0x88, HashLength = 0x128;
        private const int LanguageMaskOffset = 0x88;           // in HWINFO_S.dat

        /// <summary>Has nobody set this console up - the welcome sequence still to come?</summary>
        public static bool IsBlank(byte[] settings) => settings != null && settings.Length >= Used && (settings[0x88] & 0x07) == 0;

        /// <summary>Is the block's SHA-1 its own.</summary>
        public static bool HashOk(byte[] settings)
        {
            if (settings == null || settings.Length < Used) return false;
            using var sha = SHA1.Create();
            var h = sha.ComputeHash(settings, HashFrom, HashLength);
            for (int i = 0; i < 20; i++) if (settings[i] != h[i]) return false;
            return true;
        }

        /// <summary>The block set up as <paramref name="id"/>'s console of <paramref name="region"/>, the languages in
        /// <paramref name="languageMask"/> allowed. <paramref name="said"/>: what it holds now, for the log.</summary>
        public static byte[] SetUp(byte[] blank, PackIdentity id, DsiRegion region, uint languageMask, out string said)
        {
            var b = (byte[])blank.Clone();
            int language = LanguageFor(id, languageMask);
            byte country = CountryFor(id, region);
            var name = id.DsNickname();

            bool wasBlank = IsBlank(b);
            b[0x81] = (byte)((b[0x81] + 1) & 0x7F);
            b[0x88] |= 0x07;
            b[0x8D] = country;
            b[0x8E] = (byte)language;
            if (wasBlank)
            {
                // What the welcome sequence leaves besides the owner - measured twice; a set-up block has them already.
                for (int i = 0x94; i < 0x98; i++) b[i] = 0xFF;
                b[0x98] = 0;
                b[0xAC] = 3;
            }
            b[0xCC] = (byte)Math.Min(15, Math.Max(0, id.Colour));
            b[0xCE] = (byte)Math.Min(12, Math.Max(1, id.BirthMonth));
            b[0xCF] = (byte)Math.Min(31, Math.Max(1, id.BirthDay));
            Array.Clear(b, 0xD0, 22);
            var utf16 = Encoding.Unicode.GetBytes(name.Length > 0 ? name : "DSi");
            Buffer.BlockCopy(utf16, 0, b, 0xD0, Math.Min(20, utf16.Length));

            using (var sha = SHA1.Create())
                Buffer.BlockCopy(sha.ComputeHash(b, HashFrom, HashLength), 0, b, 0, 20);
            said = "\"" + (name.Length > 0 ? name : "DSi") + "\", language " + language + ", country 0x" + country.ToString("X2")
                 + ", birthday " + b[0xCE] + "/" + b[0xCF] + ", colour " + b[0xCC];
            return b;
        }

        /// <summary>The console's languages, as a mask of melonDS's Firmware::Language - HWINFO_S.dat 0x88.</summary>
        public static uint LanguageMask(byte[] hardwareInfo)
            => hardwareInfo != null && hardwareInfo.Length >= LanguageMaskOffset + 4 ? BitConverter.ToUInt32(hardwareInfo, LanguageMaskOffset) : 0x3E;

        /// <summary>Firmware::Language - Japanese 0, English 1, French 2, German 3, Italian 4, Spanish 5, Chinese 6, Korean 7 -
        /// the identity's when the console has it, else English, else the console's first.</summary>
        public static int LanguageFor(PackIdentity id, uint mask)
        {
            int wanted;
            switch (id.LanguageCode)
            {
                case "ja": wanted = 0; break;
                case "fr": wanted = 2; break;
                case "de": wanted = 3; break;
                case "it": wanted = 4; break;
                case "es": wanted = 5; break;
                case "zh": wanted = 6; break;
                case "ko": wanted = 7; break;
                default: wanted = 1; break;
            }
            if ((mask & (1u << wanted)) != 0) return wanted;
            if ((mask & 2u) != 0) return 1;
            for (int i = 0; i < 8; i++) if ((mask & (1u << i)) != 0) return i;
            return 1;
        }

        // ── the countries (GBATEK "DSi Regions") ─────────────────────────────

        private static readonly (string Iso, byte Code)[] Americas =
        {
            ("AI", 0x08), ("AG", 0x09), ("AR", 0x0A), ("AW", 0x0B), ("BS", 0x0C), ("BB", 0x0D), ("BZ", 0x0E), ("BO", 0x0F),
            ("BR", 0x10), ("VG", 0x11), ("CA", 0x12), ("KY", 0x13), ("CL", 0x14), ("CO", 0x15), ("CR", 0x16), ("DM", 0x17),
            ("DO", 0x18), ("EC", 0x19), ("SV", 0x1A), ("GF", 0x1B), ("GD", 0x1C), ("GP", 0x1D), ("GT", 0x1E), ("GY", 0x1F),
            ("HT", 0x20), ("HN", 0x21), ("JM", 0x22), ("MQ", 0x23), ("MX", 0x24), ("MS", 0x25), ("AN", 0x26), ("CW", 0x26),
            ("SX", 0x26), ("BQ", 0x26), ("NI", 0x27), ("PA", 0x28), ("PY", 0x29), ("PE", 0x2A), ("KN", 0x2B), ("LC", 0x2C),
            ("VC", 0x2D), ("SR", 0x2E), ("TT", 0x2F), ("TC", 0x30), ("US", 0x31), ("UY", 0x32), ("VI", 0x33), ("VE", 0x34),
            ("SG", 0x99), ("AE", 0xA8),
        };

        private static readonly (string Iso, byte Code)[] Europe =
        {
            ("AL", 0x40), ("AT", 0x42), ("BE", 0x43), ("BA", 0x44), ("BW", 0x45), ("BG", 0x46), ("HR", 0x47), ("CY", 0x48),
            ("CZ", 0x49), ("DK", 0x4A), ("EE", 0x4B), ("FI", 0x4C), ("FR", 0x4D), ("DE", 0x4E), ("GR", 0x4F), ("HU", 0x50),
            ("IS", 0x51), ("IE", 0x52), ("IT", 0x53), ("LV", 0x54), ("LS", 0x55), ("LI", 0x56), ("LT", 0x57), ("LU", 0x58),
            ("MK", 0x59), ("MT", 0x5A), ("ME", 0x5B), ("MZ", 0x5C), ("NA", 0x5D), ("NL", 0x5E), ("NO", 0x60), ("PL", 0x61),
            ("PT", 0x62), ("RO", 0x63), ("RU", 0x64), ("RS", 0x65), ("SK", 0x66), ("SI", 0x67), ("ZA", 0x68), ("ES", 0x69),
            ("SZ", 0x6A), ("SE", 0x6B), ("CH", 0x6C), ("TR", 0x6D), ("GB", 0x6E), ("ZM", 0x6F), ("ZW", 0x70),
        };

        /// <summary>This Windows' country when the console's region lists it, else the region's for the identity's language.</summary>
        public static byte CountryFor(PackIdentity id, DsiRegion region)
        {
            string iso = null;
            try { iso = System.Globalization.RegionInfo.CurrentRegion.TwoLetterISORegionName?.ToUpperInvariant(); } catch { }
            switch (region)
            {
                case DsiRegion.Japan: return 0x01;
                case DsiRegion.China: return 0xA0;
                case DsiRegion.Korea: return 0x88;
                case DsiRegion.Australia: return iso == "NZ" ? (byte)0x5F : (byte)0x41;
                case DsiRegion.Usa:
                {
                    var m = Americas.FirstOrDefault(c => c.Iso == iso);
                    if (m.Iso != null) return m.Code;
                    switch (id.LanguageCode) { case "fr": return 0x12; case "es": return 0x24; default: return 0x31; }
                }
                default:
                {
                    var m = Europe.FirstOrDefault(c => c.Iso == iso);
                    if (m.Iso != null) return m.Code;
                    switch (id.LanguageCode)
                    {
                        case "fr": return 0x4D; case "de": return 0x4E; case "it": return 0x53; case "es": return 0x69;
                        case "nl": return 0x5E; case "pt": return 0x62; case "pl": return 0x61; case "sv": return 0x6B;
                        case "da": return 0x4A; case "fi": return 0x4C; case "nb": return 0x60; case "ru": return 0x64; case "tr": return 0x6D;
                        default: return 0x6E;
                    }
                }
            }
        }

        // ── on a NAND ────────────────────────────────────────────────────────

        /// <summary>Set up <paramref name="consolePath"/> - a console just copied out of a dump - as the pack's identity, when
        /// its owner is blank. True when it was written; false with <paramref name="said"/> saying why not (not blank is not a
        /// failure: <paramref name="blank"/> false).</summary>
        public static bool SetUpBlank(string consolePath, string bios7Path, DsiRegion region, PackIdentity id, out bool blank, out string said)
            => Write(consolePath, bios7Path, region, id, onlyBlank: true, out blank, out said);

        /// <summary>The identity as the owner of <paramref name="scratchPath"/> WHATEVER IT HELD - for a scratch image alone
        /// (dsi\work.bin, a DSi cartridge's: Mehdi, 03/10), never a console: a console's owner is in every save made on it.
        /// The same fields as a blank's, its flags set if they were not.</summary>
        public static bool SetUpScratch(string scratchPath, string bios7Path, DsiRegion region, PackIdentity id, out string said)
            => Write(scratchPath, bios7Path, region, id, onlyBlank: false, out _, out said);

        private static bool Write(string consolePath, string bios7Path, DsiRegion region, PackIdentity id, bool onlyBlank, out bool blank, out string said)
        {
            blank = false;
            said = null;
            string dir = Path.Combine(Path.GetTempPath(), "lbip-twlcfg-" + Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(dir);
                using var session = DsiNand.Open(consolePath, bios7Path, out var error);
                if (session == null) { said = "the console could not be opened - " + error; return false; }
                string s0 = Path.Combine(dir, "0"), s1 = Path.Combine(dir, "1"), hw = Path.Combine(dir, "hw");
                if (!session.ExportFile(Settings0, s0, out error)) { said = "no " + Settings0 + " - " + error; return false; }
                var settings = File.ReadAllBytes(s0);
                blank = IsBlank(settings);
                if (onlyBlank && !blank) { said = "its owner is set already - left as it is"; return true; }
                if (!HashOk(settings)) { said = Settings0 + " does not match its own hash - not written"; return false; }
                uint mask = 0x3E;
                if (session.ExportFile(DsiRegions.HardwareInfoInNand, hw, out _))
                {
                    var info = File.ReadAllBytes(hw);
                    mask = LanguageMask(info);
                    region = DsiRegions.RegionIn(info) ?? region;     // the NAND's own region before the caller's guess
                }
                var set = SetUp(settings, id, region, mask, out said);
                File.WriteAllBytes(s0, set);
                File.WriteAllBytes(s1, set);
                if (!session.ImportFile(Settings0, s0, out error) || !session.ImportFile(Settings1, s1, out error))
                { said = "the settings could not be written - " + error; return false; }
                return true;
            }
            catch (Exception ex) { said = ex.GetType().Name + ": " + ex.Message; return false; }
            finally { try { Directory.Delete(dir, recursive: true); } catch { } }
        }
    }
}
