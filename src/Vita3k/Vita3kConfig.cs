// Vita3K's own settings, in portable\config.yml - what an install sets, and what it reads back to say so.
//
// WHAT AN INSTALL SETS (Mehdi's defaults): games boot full screen (boot-apps-full-screen), and the
// emulator does not look for its own updates (check-for-updates) - the pack's Download button is where
// an update comes from. ONLY A KEY THE FILE DOES NOT HOLD YET: a fresh install has no config.yml at all
// (the emulator has never run - the firmware is installed by our library), and an update keeps whatever
// the user set in the emulator's own settings. Vita3K reads every key it finds and takes its default for
// the rest (update_members, config.cpp), then writes the whole file back on exit, keeping ours.
//
// THE SYSTEM SETTINGS - language, date and time format, enter button - ARE THE EMULATOR'S, NOT THE
// FIRMWARE'S. Read in Vita3K's source (28/09): a game asks sceAppUtilSystemParamGetInt, and Vita3K answers
// from its config (SceAppUtil.cpp) - never from the console's registry. Its settings screens write
// config.yml and nothing else; vd0/registry/system.dreg, the registry proper, is only written when a game
// itself writes a key (SceRegMgr). So changing them touches no firmware file, and the disposable console
// has nothing to rebuild for it. The language also picks which TITLE_xx of a param.sfo is shown (sfo.cpp)
// and is kept in the apps cache, which is rebuilt when it changes.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace LbIntegrations.Vita3k
{
    internal static class Vita3kConfig
    {
        /// <summary>What a fresh install puts down - see the header.</summary>
        private static readonly (string Key, string Value)[] InstallDefaults =
        {
            ("boot-apps-full-screen", "true"),
            ("check-for-updates", "false"),
        };

        // SceSystemParamLang, SceSystemParamDateFormat, SceSystemParamTimeFormat,
        // SceSystemParamEnterButtonAssign - util/system.h, in that order.
        private static readonly string[] Languages =
        {
            "Japanese", "English (US)", "French", "Spanish", "German", "Italian", "Dutch", "Portuguese (Portugal)",
            "Russian", "Korean", "Chinese (traditional)", "Chinese (simplified)", "Finnish", "Swedish", "Danish",
            "Norwegian", "Polish", "Portuguese (Brazil)", "English (UK)", "Turkish",
        };
        private static readonly string[] DateFormats = { "YYYY/MM/DD", "DD/MM/YYYY", "MM/DD/YYYY" };
        private static readonly string[] TimeFormats = { "12-hour", "24-hour" };
        private static readonly string[] EnterButtons = { "circle", "cross" };

        /// <summary>Vita3K's defaults for those four (config.h): English (US), MM/DD/YYYY, 12-hour, cross.</summary>
        private const int DefaultLanguage = 1, DefaultDate = 2, DefaultTime = 0, DefaultEnter = 1;

        private static string PathOf(Vita3kLayout layout)
        {
            var portable = Vita3kPaths.PortableDirOf(layout?.InstallDir);
            return string.IsNullOrEmpty(portable) || !Directory.Exists(portable) ? null : Path.Combine(portable, "config.yml");
        }

        /// <summary>Put down the install's defaults - each only when the file does not hold its key yet.</summary>
        public static void ApplyInstallDefaults(Vita3kLayout layout)
        {
            try
            {
                var config = PathOf(layout);
                if (config == null) return;
                var text = File.Exists(config) ? File.ReadAllText(config) : "";
                var held = Keys(text);
                var added = InstallDefaults.Where(d => !held.ContainsKey(d.Key)).ToList();
                if (added.Count == 0) return;
                if (text.Length > 0 && !text.EndsWith("\n")) text += "\n";
                text += string.Concat(added.Select(d => d.Key + ": " + d.Value + "\n"));
                File.WriteAllText(config, text);
                Log.Info("config.yml: " + string.Join(", ", added.Select(d => d.Key + " " + d.Value))
                         + (held.Count == 0 ? " - a fresh install" : " - the rest left as the user set it"));
            }
            catch (Exception ex) { Log.Warn("could not put down the install's settings", ex); }
        }

        /// <summary>The system settings the emulator will run games with, as a sentence: what config.yml
        /// says, or Vita3K's default for a key it does not hold.</summary>
        public static string SystemSettings(Vita3kLayout layout)
        {
            var keys = new Dictionary<string, string>();
            try { var config = PathOf(layout); if (config != null && File.Exists(config)) keys = Keys(File.ReadAllText(config)); }
            catch (Exception ex) { Log.Warn("could not read config.yml", ex); }

            string Of(string key, string[] names, int fallback)
            {
                int v = keys.TryGetValue(key, out var s) && int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : fallback;
                return v >= 0 && v < names.Length ? names[v] : "#" + v;
            }
            return "language " + Of("sys-lang", Languages, DefaultLanguage)
                   + ", date " + Of("sys-date-format", DateFormats, DefaultDate)
                   + ", time " + Of("sys-time-format", TimeFormats, DefaultTime)
                   + ", enter button " + Of("sys-button", EnterButtons, DefaultEnter);
        }

        /// <summary>The top-level "key: value" lines of a config.yml - all this needs of YAML.</summary>
        private static Dictionary<string, string> Keys(string text)
        {
            var keys = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var raw in (text ?? "").Split('\n'))
            {
                var line = raw.TrimEnd('\r');
                if (line.Length == 0 || char.IsWhiteSpace(line[0]) || line[0] == '#' || line[0] == '-') continue;
                var colon = line.IndexOf(':');
                if (colon <= 0) continue;
                keys[line.Substring(0, colon).Trim()] = line.Substring(colon + 1).Trim().Trim('\'', '"');
            }
            return keys;
        }
    }
}
