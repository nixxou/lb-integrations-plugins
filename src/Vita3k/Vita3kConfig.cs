// Vita3K's own settings, in portable\config.yml - what an install sets, and what it reads back to say so.
//
// WHAT AN INSTALL SETS (Mehdi's defaults): games boot full screen (boot-apps-full-screen), and the
// emulator does not look for its own updates (check-for-updates) - the pack's Download button is where
// an update comes from. And the four SYSTEM SETTINGS the games are told, FROM WINDOWS (Mehdi, 28/09):
//   - the language, from Windows' display language (GetUserDefaultUILanguage) - English (UK) for an
//     English that is not American, English (US) for a language the Vita does not have;
//   - the date and time formats, from the regional settings (GetUserDefaultLocaleName) - the order of the short
//     date, and 24-hour when the short time says "H": 24-hour in Europe, as there;
//   - the enter button: circle in Japanese, as on a Japanese console; cross everywhere else.
// Then the install ASKS whether that is right (a notification with Yes / No - Vita3kNotify), and No
// opens Vita3kSystemSettingsForm to change them.
//
// ONLY A KEY THE FILE DOES NOT HOLD YET: a fresh install has no config.yml at all (the emulator has
// never run - the firmware is installed by our library), and an update keeps whatever the user set in
// the emulator's own settings. Vita3K reads every key it finds and takes its default for the rest
// (update_members, config.cpp), then writes the whole file back on exit, keeping ours.
//
// THE SYSTEM SETTINGS ARE THE EMULATOR'S, NOT THE FIRMWARE'S. Read in Vita3K's source (28/09): a game
// asks sceAppUtilSystemParamGetInt, and Vita3K answers from its config (SceAppUtil.cpp) - never from the
// console's registry. Its settings screens write config.yml and nothing else; vd0/registry/system.dreg,
// the registry proper, is only written when a game itself writes a key (SceRegMgr). So changing them
// touches no firmware file, and the disposable console has nothing to rebuild for it. The language also
// picks which TITLE_xx of a param.sfo Vita3K shows (sfo.cpp) - its own list, not the library's: the
// library names games in US English whatever this says (Vita3kContent.TitleOf).

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace LbIntegrations.Vita3k
{
    /// <summary>The four settings a game is told, as Vita3K numbers them.</summary>
    internal sealed class VitaSystemSettings
    {
        public int Language = Vita3kConfig.DefaultLanguage;
        public int DateFormat = Vita3kConfig.DefaultDate;
        public int TimeFormat = Vita3kConfig.DefaultTime;
        public int EnterButton = Vita3kConfig.DefaultEnter;

        public override string ToString()
            => "language " + Vita3kConfig.Name(Vita3kConfig.Languages, Language)
               + ", date " + Vita3kConfig.Name(Vita3kConfig.DateFormats, DateFormat)
               + ", time " + Vita3kConfig.Name(Vita3kConfig.TimeFormats, TimeFormat)
               + ", enter button " + Vita3kConfig.Name(Vita3kConfig.EnterButtons, EnterButton);
    }

    internal static class Vita3kConfig
    {
        /// <summary>What a fresh install puts down besides the system settings - see the header.</summary>
        private static readonly (string Key, string Value)[] InstallDefaults =
        {
            ("boot-apps-full-screen", "true"),
            ("check-for-updates", "false"),
        };

        internal const string LanguageKey = "sys-lang", DateKey = "sys-date-format", TimeKey = "sys-time-format", EnterKey = "sys-button";

        // SceSystemParamLang, SceSystemParamDateFormat, SceSystemParamTimeFormat,
        // SceSystemParamEnterButtonAssign - util/system.h, in that order.
        internal static readonly string[] Languages =
        {
            "Japanese", "English (US)", "French", "Spanish", "German", "Italian", "Dutch", "Portuguese (Portugal)",
            "Russian", "Korean", "Chinese (traditional)", "Chinese (simplified)", "Finnish", "Swedish", "Danish",
            "Norwegian", "Polish", "Portuguese (Brazil)", "English (UK)", "Turkish",
        };
        internal static readonly string[] DateFormats = { "YYYY/MM/DD", "DD/MM/YYYY", "MM/DD/YYYY" };
        internal static readonly string[] TimeFormats = { "12-hour", "24-hour" };
        internal static readonly string[] EnterButtons = { "circle", "cross" };

        /// <summary>Vita3K's defaults for those four (config.h): English (US), MM/DD/YYYY, 12-hour, cross.</summary>
        internal const int DefaultLanguage = 1, DefaultDate = 2, DefaultTime = 0, DefaultEnter = 1;

        internal static string Name(string[] names, int value) => value >= 0 && value < names.Length ? names[value] : "#" + value;

        private static string PathOf(Vita3kLayout layout)
        {
            var portable = Vita3kPaths.PortableDirOf(layout?.InstallDir);
            return string.IsNullOrEmpty(portable) || !Directory.Exists(portable) ? null : Path.Combine(portable, "config.yml");
        }

        /// <summary>Put down the install's defaults and Windows' system settings - each only when the file
        /// does not hold its key yet.</summary>
        public static void ApplyInstallDefaults(Vita3kLayout layout)
            => Put(layout, InstallDefaults.Concat(Pairs(FromWindows())).ToArray(), onlyMissing: true);

        /// <summary>The four settings from Windows - see the header.
        ///
        /// ASKED OF WINDOWS, NOT OF THE PROCESS: CurrentUICulture is the host's to change, and LaunchBox
        /// has a language setting of its own - a French Windows running an English LaunchBox would come
        /// out English. GetUserDefaultUILanguage is the user's display language, GetUserDefaultLocaleName
        /// the regional settings, taken WITH the user's own overrides (a 24-hour clock set by hand).
        /// The process's cultures only when Windows will not say.</summary>
        public static VitaSystemSettings FromWindows()
        {
            CultureInfo ui = CultureInfo.CurrentUICulture, regional = CultureInfo.CurrentCulture;
            try { var id = GetUserDefaultUILanguage(); if (id != 0) ui = new CultureInfo(id); } catch { }
            try
            {
                var name = new System.Text.StringBuilder(85);
                if (GetUserDefaultLocaleName(name, name.Capacity) > 0) regional = new CultureInfo(name.ToString(), useUserOverride: true);
            }
            catch { }
            return FromCultures(ui, regional);
        }

        [System.Runtime.InteropServices.DllImport("kernel32.dll")]
        private static extern ushort GetUserDefaultUILanguage();

        [System.Runtime.InteropServices.DllImport("kernel32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        private static extern int GetUserDefaultLocaleName(System.Text.StringBuilder name, int size);

        /// <summary>The same from given cultures: the language from <paramref name="ui"/>, the formats
        /// from <paramref name="regional"/>.</summary>
        internal static VitaSystemSettings FromCultures(CultureInfo ui, CultureInfo regional)
        {
            var s = new VitaSystemSettings { Language = LanguageOf(ui) };
            s.EnterButton = s.Language == 0 ? 0 : 1;   // circle on a Japanese console, cross elsewhere
            try
            {
                var date = regional.DateTimeFormat.ShortDatePattern ?? "";
                int y = date.IndexOf('y'), m = date.IndexOf('M'), d = date.IndexOf('d');
                if (y >= 0 && (m < 0 || y < m) && (d < 0 || y < d)) s.DateFormat = 0;
                else if (d >= 0 && (m < 0 || d < m)) s.DateFormat = 1;
                else s.DateFormat = 2;
                s.TimeFormat = (regional.DateTimeFormat.ShortTimePattern ?? "").Contains('H') ? 1 : 0;
            }
            catch { }
            return s;
        }

        private static int LanguageOf(CultureInfo culture)
        {
            var name = culture?.Name ?? "";
            var lang = culture?.TwoLetterISOLanguageName ?? "";
            switch (lang)
            {
                case "ja": return 0;
                case "en": return name.Equals("en-US", StringComparison.OrdinalIgnoreCase) || name.Equals("en", StringComparison.OrdinalIgnoreCase)
                                  || name.Equals("en-CA", StringComparison.OrdinalIgnoreCase) ? 1 : 18;
                case "fr": return 2;
                case "es": return 3;
                case "de": return 4;
                case "it": return 5;
                case "nl": return 6;
                case "pt": return name.Equals("pt-BR", StringComparison.OrdinalIgnoreCase) ? 17 : 7;
                case "ru": return 8;
                case "ko": return 9;
                case "zh":
                    // Traditional for Taiwan, Hong Kong, Macao and anything written Hant; simplified otherwise.
                    return name.Contains("Hant", StringComparison.OrdinalIgnoreCase) || name.EndsWith("-TW", StringComparison.OrdinalIgnoreCase)
                           || name.EndsWith("-HK", StringComparison.OrdinalIgnoreCase) || name.EndsWith("-MO", StringComparison.OrdinalIgnoreCase) ? 10 : 11;
                case "fi": return 12;
                case "sv": return 13;
                case "da": return 14;
                case "nb": case "nn": case "no": return 15;
                case "pl": return 16;
                case "tr": return 19;
                default: return DefaultLanguage;
            }
        }

        /// <summary>The four settings as config.yml holds them - Vita3K's default for a key it does not.</summary>
        public static VitaSystemSettings Read(Vita3kLayout layout)
        {
            var keys = new Dictionary<string, string>();
            try { var config = PathOf(layout); if (config != null && File.Exists(config)) keys = Keys(File.ReadAllText(config)); }
            catch (Exception ex) { Log.Warn("could not read config.yml", ex); }
            int Of(string key, int fallback)
                => keys.TryGetValue(key, out var v) && int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : fallback;
            return new VitaSystemSettings
            {
                Language = Of(LanguageKey, DefaultLanguage), DateFormat = Of(DateKey, DefaultDate),
                TimeFormat = Of(TimeKey, DefaultTime), EnterButton = Of(EnterKey, DefaultEnter),
            };
        }

        /// <summary>Write the four settings, over whatever config.yml said.</summary>
        public static bool Write(Vita3kLayout layout, VitaSystemSettings settings)
            => Put(layout, Pairs(settings), onlyMissing: false);

        /// <summary>The system settings the emulator will run games with, as a sentence.</summary>
        public static string SystemSettings(Vita3kLayout layout) => Read(layout).ToString();

        private static (string, string)[] Pairs(VitaSystemSettings s) => new[]
        {
            (LanguageKey, s.Language.ToString(CultureInfo.InvariantCulture)),
            (DateKey, s.DateFormat.ToString(CultureInfo.InvariantCulture)),
            (TimeKey, s.TimeFormat.ToString(CultureInfo.InvariantCulture)),
            (EnterKey, s.EnterButton.ToString(CultureInfo.InvariantCulture)),
        };

        /// <summary>Set top-level keys of config.yml: a line that holds the key is replaced (unless
        /// <paramref name="onlyMissing"/>), a key it does not hold is added at the end. Everything else
        /// in the file is kept as it is.</summary>
        private static bool Put(Vita3kLayout layout, (string Key, string Value)[] pairs, bool onlyMissing)
        {
            try
            {
                var config = PathOf(layout);
                if (config == null) return false;
                var text = File.Exists(config) ? File.ReadAllText(config) : "";
                var lines = text.Length == 0 ? new List<string>() : text.Replace("\r\n", "\n").TrimEnd('\n').Split('\n').ToList();
                var done = new List<string>();
                foreach (var (key, value) in pairs)
                {
                    int at = lines.FindIndex(l => KeyOf(l) == key);
                    if (at >= 0)
                    {
                        if (onlyMissing || lines[at] == key + ": " + value) continue;
                        lines[at] = key + ": " + value;
                    }
                    else lines.Add(key + ": " + value);
                    done.Add(key + " " + value);
                }
                if (done.Count == 0) return true;
                File.WriteAllText(config, string.Join("\n", lines) + "\n");
                Log.Info("config.yml: " + string.Join(", ", done) + (text.Length == 0 ? " - a fresh install" : ""));
                return true;
            }
            catch (Exception ex) { Log.Warn("could not write config.yml", ex); return false; }
        }

        /// <summary>The key of a top-level "key: value" line, or null.</summary>
        private static string KeyOf(string line)
        {
            line = (line ?? "").TrimEnd('\r');
            if (line.Length == 0 || char.IsWhiteSpace(line[0]) || line[0] == '#' || line[0] == '-') return null;
            var colon = line.IndexOf(':');
            return colon <= 0 ? null : line.Substring(0, colon).Trim();
        }

        /// <summary>The top-level "key: value" lines of a config.yml - all this needs of YAML.</summary>
        private static Dictionary<string, string> Keys(string text)
        {
            var keys = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var raw in (text ?? "").Split('\n'))
            {
                var line = raw.TrimEnd('\r');
                var key = KeyOf(line);
                if (key != null) keys[key] = line.Substring(line.IndexOf(':') + 1).Trim().Trim('\'', '"');
            }
            return keys;
        }
    }
}
