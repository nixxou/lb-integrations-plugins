// The plugin's own settings, beside the other plugins' (<Plugins>\.data\<PluginId>\), in Cxbx's shape:
//
//   settings.ini     every game - the import (Mehdi, 03/10), each on by default, the PPSSPP tab of the Nixx window:
//     import_clean=on|off     the import wizard's list put right: firmware updates and what is not a PSP game out, game
//                             updates out of it and NOTED for their game (PpssppUpdates)
//     import_title=on|off     the pack's rule for a game's name (LbipImportTitle): the original, else the PARAM.SFO's TITLE
//     import_region=on|off    the region its DISC_ID says, set once it is in the library
//   updates.tsv      the game updates found - see PpssppUpdates

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace LbIntegrations.Ppsspp
{
    internal static class PpssppSettings
    {
        /// <summary>manifest.json's PluginId.</summary>
        public const string PluginId = "75b34659-92fc-4f10-9765-b9e05fac8f6f";

        public static string Dir
        {
            get
            {
                var dll = typeof(PpssppSettings).Assembly.Location;
                var root = Path.GetDirectoryName(Path.GetDirectoryName(dll)) ?? Path.GetDirectoryName(dll);
                return Path.Combine(root, ".data", PluginId);
            }
        }

        public static string SettingsPath => Path.Combine(Dir, "settings.ini");

        public static Dictionary<string, string> Read()
        {
            var v = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                if (!File.Exists(SettingsPath)) return v;
                foreach (var raw in File.ReadAllLines(SettingsPath))
                {
                    var line = raw.Trim();
                    if (line.Length == 0 || line[0] == '#') continue;
                    int eq = line.IndexOf('=');
                    if (eq > 0) v[line.Substring(0, eq).Trim()] = line.Substring(eq + 1).Trim();
                }
            }
            catch (Exception ex) { Log.Warn("could not read " + SettingsPath, ex); }
            return v;
        }

        public static void Write(IDictionary<string, string> values)
        {
            Directory.CreateDirectory(Dir);
            var lines = new List<string> { "# Nixx-PPSSPP: every game. Edited by the Nixx window." };
            lines.AddRange(values.Where(kv => kv.Value != null).Select(kv => kv.Key + "=" + kv.Value));
            File.WriteAllLines(SettingsPath, lines, new UTF8Encoding(false));
        }

        public static bool On(IDictionary<string, string> s, string key, bool byDefault)
            => s.TryGetValue(key, out var v) ? !(v.Equals("off", StringComparison.OrdinalIgnoreCase) || v.Equals("false", StringComparison.OrdinalIgnoreCase)) : byDefault;

        public static bool CleanImport => On(Read(), "import_clean", true);
        public static bool TitleImport => On(Read(), "import_title", true);
        public static bool RegionImport => On(Read(), "import_region", true);
    }
}
