// THE PACK'S ONE IDENTITY (Mehdi, 03/10): the things nearly every console asks of its owner - a nickname, a language,
// how a date and a time are written, the button that confirms, a birthday, a favourite colour - asked ONCE, in the
// installer's "Your console" and the Nixx window's tab of that name, and used by every plugin where its emulator has
// such a setting. %LOCALAPPDATA%\lb-integrations-plugins\identity.ini, beside ramdisk.ini.
//
// WHEN IT IS READ. When a plugin sets up its emulator for the FIRST time - where each one asked Windows before, and
// still does when there is no identity.ini:
//   Xenia     gamertag of the profile it creates, language and clock of xconfig.settings (XeniaSetup)
//   Vita3K    sys-lang, sys-date-format, sys-time-format, sys-button of config.yml - the keys it does not hold yet
//   PPSSPP    [SystemParam] NickName, GameLanguage, ParamDateFormat, ParamTimeFormat, ButtonPreference of
//             ppsspp.ini - the keys it does not hold yet
//   melonDS   [Instance0.Firmware] Username, Language, BirthdayMonth/Day, FavouriteColour of melonDS.toml, the keys
//             it does not hold yet, and OverrideSettings on with them: the owner holds on melonDS's own firmware,
//             over a firmware dump and in DSi mode (a forced DSi session keeps the save's own settings files -
//             DsiWorkspace.MarkForced)
//   Flycast   [config] Dreamcast.Language of emu.cfg, when there is no emu.cfg yet
// and Cxbx-Reloaded, at EVERY launch: its "console.language" option's default (the pack's language, else Windows').
// An emulator already set up keeps its own: nothing here is written over a value it has.
//
// THE LANGUAGE IS A CULTURE NAME ("fr-FR", "pt-BR", "zh-TW"), so each plugin keeps its own table of what its console
// numbers each language - and a console without it (no Polish on a DS) takes English, said under the choice.

#nullable disable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace LbIntegrations.Identity
{
    internal sealed class PackIdentity
    {
        public string Nickname = "";
        /// <summary>A culture name of <see cref="Languages"/>.</summary>
        public string Language = "en-US";
        /// <summary>"ymd", "dmy" or "mdy".</summary>
        public string DateOrder = "ymd";
        public bool Clock24 = true;
        /// <summary>"cross" or "circle" - the button that says yes.</summary>
        public string Confirm = "cross";
        public int BirthMonth = 1, BirthDay = 1;
        /// <summary>The DS's favourite colour, 0-15 (melonDS's order).</summary>
        public int Colour;

        public const int DsNicknameMax = 10, PspNicknameMax = 32;

        public static string Dir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "lb-integrations-plugins");
        public static string FilePath => Path.Combine(Dir, "identity.ini");

        // ── the languages ────────────────────────────────────────────────────

        /// <summary>The choices, each in its own language.</summary>
        public static readonly (string Culture, string Name)[] Languages =
        {
            ("en-US", "English (United States)"), ("en-GB", "English (United Kingdom)"), ("fr-FR", "Français"),
            ("de-DE", "Deutsch"), ("es-ES", "Español"), ("it-IT", "Italiano"), ("nl-NL", "Nederlands"),
            ("pt-PT", "Português"), ("pt-BR", "Português (Brasil)"), ("ru-RU", "Русский"), ("pl-PL", "Polski"),
            ("sv-SE", "Svenska"), ("da-DK", "Dansk"), ("nb-NO", "Norsk"), ("fi-FI", "Suomi"), ("tr-TR", "Türkçe"),
            ("ja-JP", "日本語"), ("ko-KR", "한국어"), ("zh-CN", "中文 (简体)"), ("zh-TW", "中文 (繁體)"),
        };

        /// <summary>The consoles that have each language, by the two-letter code - for the line under the choice.
        /// What each plugin's own table holds (XeniaConsole.LanguageOf, Vita3kConfig.LanguageOf, CxbxEeprom, and the
        /// three below).</summary>
        public static readonly (string Console, string[] Has)[] ConsoleLanguages =
        {
            ("Xenia", new[] { "en", "ja", "de", "fr", "es", "it", "ko", "zh", "pt", "pl", "ru", "sv", "tr", "nb", "nl" }),
            ("Vita3K", new[] { "en", "ja", "de", "fr", "es", "it", "ko", "zh", "pt", "pl", "ru", "sv", "tr", "nb", "nl", "da", "fi" }),
            ("PPSSPP", new[] { "en", "ja", "de", "fr", "es", "it", "ko", "zh", "pt", "ru", "nl" }),
            ("Cxbx-Reloaded", new[] { "en", "ja", "de", "fr", "es", "it", "ko", "zh", "pt" }),
            ("melonDS", new[] { "en", "ja", "de", "fr", "es", "it" }),
            ("Flycast", new[] { "en", "ja", "de", "fr", "es", "it" }),
        };

        public CultureInfo Culture
        {
            get { try { return new CultureInfo(string.IsNullOrWhiteSpace(Language) ? "en-US" : Language); } catch { return new CultureInfo("en-US"); } }
        }

        public string LanguageCode => Culture.TwoLetterISOLanguageName.ToLowerInvariant();

        private bool Traditional => Language.EndsWith("-TW", StringComparison.OrdinalIgnoreCase) || Language.EndsWith("-HK", StringComparison.OrdinalIgnoreCase)
                                    || Language.EndsWith("-MO", StringComparison.OrdinalIgnoreCase) || Language.IndexOf("Hant", StringComparison.OrdinalIgnoreCase) >= 0;

        /// <summary>PSP_SYSTEMPARAM_LANGUAGE_* (PPSSPP Core/HLE/sceUtility.h); English for one it lacks.</summary>
        public int PspLanguage()
        {
            switch (LanguageCode)
            {
                case "ja": return 0;
                case "fr": return 2;
                case "es": return 3;
                case "de": return 4;
                case "it": return 5;
                case "nl": return 6;
                case "pt": return 7;
                case "ru": return 8;
                case "ko": return 9;
                case "zh": return Traditional ? 10 : 11;
                default: return 1;
            }
        }

        /// <summary>The DS firmware's (melonDS Firmware::Language): Japanese 0, English 1, French 2, German 3, Italian 4,
        /// Spanish 5; English for any other.</summary>
        public int DsLanguage()
        {
            switch (LanguageCode)
            {
                case "ja": return 0;
                case "fr": return 2;
                case "de": return 3;
                case "it": return 4;
                case "es": return 5;
                default: return 1;
            }
        }

        /// <summary>Flycast's Dreamcast.Language (core/cfg/option.cpp, nvmem.cpp): Japanese 0, English 1, German 2,
        /// French 3, Spanish 4, Italian 5; English for any other.</summary>
        public int DreamcastLanguage()
        {
            switch (LanguageCode)
            {
                case "ja": return 0;
                case "de": return 2;
                case "fr": return 3;
                case "es": return 4;
                case "it": return 5;
                default: return 1;
            }
        }

        /// <summary>The nickname as a DS keeps it: its first 10 characters, one line.</summary>
        public string DsNickname()
        {
            var n = (Nickname ?? "").Replace("\r", "").Replace("\n", " ").Trim();
            return n.Length > DsNicknameMax ? n.Substring(0, DsNicknameMax) : n;
        }

        /// <summary>A valid Xbox gamertag made of <paramref name="name"/>, or "Player" - Xenia's ProfileManager::IsGamertagValid:
        /// 1 to 15 characters, a letter first, then letters and digits, single spaces between words. Accents are dropped
        /// ("Mehdi_Élan" gives "Mehdi Elan"). XeniaProfile.GamertagFrom is this.</summary>
        public static string Gamertag(string name)
        {
            var normalized = (name ?? "").Normalize(NormalizationForm.FormD);
            var sb = new StringBuilder();
            foreach (var c in normalized)
            {
                if (c < 128 && char.IsLetterOrDigit(c)) sb.Append(c);
                else if ((c == ' ' || c == '_' || c == '-' || c == '.') && sb.Length > 0 && sb[sb.Length - 1] != ' ') sb.Append(' ');
            }
            var tag = sb.ToString().Trim();
            while (tag.Length > 0 && !char.IsLetter(tag[0])) tag = tag.Substring(1).TrimStart();
            if (tag.Length > 15) tag = tag.Substring(0, 15).TrimEnd();
            return tag.Length > 0 && System.Text.RegularExpressions.Regex.IsMatch(tag, "^[A-Za-z][A-Za-z0-9]*( [A-Za-z0-9]+)*$") ? tag : "Player";
        }

        /// <summary>The DS's sixteen favourite colours, melonDS's names and order (FirmwareSettingsDialog).</summary>
        public static readonly (string Name, int Rgb)[] DsColours =
        {
            ("Greyish blue", 0x61829A), ("Brown", 0xBA4900), ("Red", 0xFB0018), ("Light pink", 0xFB8AFB),
            ("Orange", 0xFB9200), ("Yellow", 0xF3E300), ("Lime", 0xAAFB00), ("Light green", 0x00FB00),
            ("Dark green", 0x00A238), ("Turquoise", 0x49DB8A), ("Light blue", 0x30BAF3), ("Blue", 0x0059F3),
            ("Dark blue", 0x000092), ("Dark purple", 0x8A00D3), ("Light purple", 0xD300EB), ("Dark pink", 0xFB00F6),
        };

        /// <summary>The nickname as PPSSPP's settings take it: 32 characters at most (GameSettingsScreen.cpp).</summary>
        public string PspNickname()
        {
            var n = (Nickname ?? "").Replace("\r", "").Replace("\n", " ").Trim();
            return n.Length > PspNicknameMax ? n.Substring(0, PspNicknameMax) : n;
        }

        // ── on disk ──────────────────────────────────────────────────────────

        public static bool Exists() { try { return File.Exists(FilePath); } catch { return false; } }

        /// <summary>The identity set, or null when there is none yet - then a plugin does as it did, asks Windows.</summary>
        public static PackIdentity Load()
        {
            try
            {
                if (!File.Exists(FilePath)) return null;
                var v = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var raw in File.ReadAllLines(FilePath, Encoding.UTF8))
                {
                    var line = raw.Trim();
                    if (line.Length == 0 || line[0] == '#' || line[0] == ';') continue;
                    int eq = line.IndexOf('=');
                    if (eq > 0) v[line.Substring(0, eq).Trim()] = line.Substring(eq + 1).Trim();
                }
                var d = FromWindows();
                var p = new PackIdentity
                {
                    Nickname = v.TryGetValue("nickname", out var n) ? n : d.Nickname,
                    Language = v.TryGetValue("language", out var l) && Languages.Any(x => x.Culture.Equals(l, StringComparison.OrdinalIgnoreCase)) ? l : d.Language,
                    DateOrder = v.TryGetValue("date", out var dt) && (dt == "ymd" || dt == "dmy" || dt == "mdy") ? dt : d.DateOrder,
                    Clock24 = v.TryGetValue("clock", out var c) ? c != "12" : d.Clock24,
                    Confirm = v.TryGetValue("confirm", out var b) && (b == "cross" || b == "circle") ? b : d.Confirm,
                    BirthMonth = Int(v, "birth_month", 1, 12, 1),
                    BirthDay = Int(v, "birth_day", 1, 31, 1),
                    Colour = Int(v, "colour", 0, 15, 0),
                };
                return p;
            }
            catch { return null; }
        }

        private static int Int(Dictionary<string, string> v, string key, int min, int max, int fallback)
            => v.TryGetValue(key, out var s) && int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) && n >= min && n <= max ? n : fallback;

        public void Save()
        {
            Directory.CreateDirectory(Dir);
            var lines = new[]
            {
                "# The Nixx pack's console identity - read by each plugin when it sets up its emulator for the first time.",
                "nickname=" + (Nickname ?? "").Replace("\r", "").Replace("\n", " ").Trim(),
                "language=" + Language,
                "date=" + DateOrder,
                "clock=" + (Clock24 ? "24" : "12"),
                "confirm=" + Confirm,
                "birth_month=" + BirthMonth.ToString(CultureInfo.InvariantCulture),
                "birth_day=" + BirthDay.ToString(CultureInfo.InvariantCulture),
                "colour=" + Colour.ToString(CultureInfo.InvariantCulture),
            };
            var tmp = FilePath + ".tmp";
            File.WriteAllLines(tmp, lines, new UTF8Encoding(false));
            if (File.Exists(FilePath)) File.Replace(tmp, FilePath, null); else File.Move(tmp, FilePath);
        }

        // ── what Windows says ────────────────────────────────────────────────

        /// <summary>An identity as this Windows is: the account's name, the display language, the regional formats (the
        /// user's, with his own overrides - as Vita3kConfig.FromWindows asks them), circle on a Japanese console.</summary>
        public static PackIdentity FromWindows()
        {
            var p = new PackIdentity();
            try { p.Nickname = Environment.UserName ?? ""; } catch { }
            CultureInfo ui = CultureInfo.CurrentUICulture, regional = CultureInfo.CurrentCulture;
            try { var id = GetUserDefaultUILanguage(); if (id != 0) ui = new CultureInfo(id); } catch { }
            try
            {
                var name = new StringBuilder(85);
                if (GetUserDefaultLocaleName(name, name.Capacity) > 0) regional = new CultureInfo(name.ToString(), useUserOverride: true);
            }
            catch { }
            p.Language = Closest(ui);
            try
            {
                var date = regional.DateTimeFormat.ShortDatePattern ?? "";
                int y = date.IndexOf('y'), m = date.IndexOf('M'), d = date.IndexOf('d');
                p.DateOrder = y >= 0 && (m < 0 || y < m) && (d < 0 || y < d) ? "ymd" : d >= 0 && (m < 0 || d < m) ? "dmy" : "mdy";
                p.Clock24 = (regional.DateTimeFormat.ShortTimePattern ?? "").Contains('H');
            }
            catch { }
            p.Confirm = p.LanguageCode == "ja" ? "circle" : "cross";
            return p;
        }

        /// <summary>The choice nearest a culture: itself, else its language's first, else English (United States).</summary>
        public static string Closest(CultureInfo c)
        {
            if (c == null) return "en-US";
            var exact = Languages.FirstOrDefault(x => x.Culture.Equals(c.Name, StringComparison.OrdinalIgnoreCase));
            if (exact.Culture != null) return exact.Culture;
            var lang = c.TwoLetterISOLanguageName.ToLowerInvariant();
            if (lang == "zh")
                return c.Name.IndexOf("Hant", StringComparison.OrdinalIgnoreCase) >= 0 || c.Name.EndsWith("-HK", StringComparison.OrdinalIgnoreCase)
                       || c.Name.EndsWith("-MO", StringComparison.OrdinalIgnoreCase) ? "zh-TW" : "zh-CN";
            if (lang == "en") return c.Name.Equals("en", StringComparison.OrdinalIgnoreCase) || c.Name.Equals("en-CA", StringComparison.OrdinalIgnoreCase) ? "en-US" : "en-GB";
            if (lang == "nn" || lang == "no") return "nb-NO";
            var same = Languages.FirstOrDefault(x => x.Culture.StartsWith(lang + "-", StringComparison.OrdinalIgnoreCase));
            return same.Culture ?? "en-US";
        }

        [DllImport("kernel32.dll")]
        private static extern ushort GetUserDefaultUILanguage();

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetUserDefaultLocaleName(StringBuilder name, int size);
    }
}
