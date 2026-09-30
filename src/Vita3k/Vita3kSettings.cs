// This plugin's own settings - those that are not a game's: one key=value per line in
//
//     <plugin root>\.data\<PluginId>\settings.ini
//
// LaunchBox 14's place for a plugin's data (Local\Plugins\.data\<PluginId>), and the one the pack's
// uninstaller leaves alone. The plugin root is the folder holding this plugin's folder, so the same
// rule holds under the classic Plugins\ root of an older LaunchBox.
//
// Read at the moment a setting is used, never cached: the configuration window writes the file, and
// the next use sees it - no restart.
//
// The window's tab for them is Vita3kSettingsPage.cs.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace LbIntegrations.Vita3k
{
    internal static class Vita3kSettings
    {
        /// <summary>manifest.json's PluginId.</summary>
        public const string PluginId = "51f3dae1-cba9-417d-a926-59fa731b2028";

        /// <summary>For the probe: a settings file somewhere else than beside the install.</summary>
        internal static string PathOverride;

        public const string BypassVitaImportKey = "BypassLaunchBoxVitaImport";

        /// <summary>In LaunchBox's Import ROM Files wizard, import Vita games as ROM files rather than
        /// through LaunchBox's own Vita import - see Vita3kLbImport. ON unless turned off.</summary>
        public static bool BypassVitaImport
        {
            get => Bool(BypassVitaImportKey, true);
            set => Write(BypassVitaImportKey, value ? "true" : "false");
        }

        public const string CleanImportListKey = "CleanImportList";

        /// <summary>In that same wizard, put the game list right after the scan: titles from the
        /// param.sfo, updates and DLC recorded for their game and out of the list, non-games out - see
        /// Vita3kImportCleanup. ON unless turned off.</summary>
        public static bool CleanImportList
        {
            get => Bool(CleanImportListKey, true);
            set => Write(CleanImportListKey, value ? "true" : "false");
        }

        public const string ImportRegionVersionKey = "ImportRegionVersion";

        /// <summary>After such an import, each game's region from its param.sfo and its version from its file
        /// name's [tags] - see Vita3kImportFinished. ON unless turned off.</summary>
        public static bool ImportRegionVersion
        {
            get => Bool(ImportRegionVersionKey, true);
            set => Write(ImportRegionVersionKey, value ? "true" : "false");
        }

        public static string SettingsPath
        {
            get
            {
                if (PathOverride != null) return PathOverride;
                var dll = typeof(Vita3kSettings).Assembly.Location;
                var pluginDir = Path.GetDirectoryName(dll);
                var root = Path.GetDirectoryName(pluginDir) ?? pluginDir;
                return Path.Combine(root, ".data", PluginId, "settings.ini");
            }
        }

        private static bool Bool(string key, bool fallback)
        {
            var v = Read().TryGetValue(key, out var s) ? s : null;
            if (string.Equals(v, "true", StringComparison.OrdinalIgnoreCase)) return true;
            if (string.Equals(v, "false", StringComparison.OrdinalIgnoreCase)) return false;
            return fallback;
        }

        private static Dictionary<string, string> Read()
        {
            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                var path = SettingsPath;
                if (!File.Exists(path)) return values;
                foreach (var line in File.ReadAllLines(path))
                {
                    var at = line.IndexOf('=');
                    if (at <= 0 || line.TrimStart().StartsWith("#")) continue;
                    values[line.Substring(0, at).Trim()] = line.Substring(at + 1).Trim();
                }
            }
            catch (Exception ex) { Log.Warn("could not read the settings", ex); }
            return values;
        }

        /// <summary>One key written, the others kept - through a temporary file, so a failure halfway
        /// leaves the previous settings.</summary>
        private static void Write(string key, string value)
        {
            var path = SettingsPath;
            var values = Read();
            values[key] = value;
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            var tmp = path + ".tmp";
            File.WriteAllLines(tmp, values.OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase).Select(kv => kv.Key + "=" + kv.Value));
            File.Move(tmp, path, overwrite: true);
            Log.Info("setting " + key + "=" + value + " (" + path + ")");
        }
    }
}
