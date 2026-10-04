// The plugin's own settings, beside the other plugins' (<Plugins>\.data\<PluginId>\settings.ini), Cxbx's shape:
//
//   cache_gb=      how much room the XISO copies may take in <xemu>\discs (default 40; the copies used longest ago go first)

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace LbIntegrations.Xemu
{
    internal static class XemuSettings
    {
        /// <summary>manifest.json's PluginId.</summary>
        public const string PluginId = "c54b75ab-94aa-4a1e-b36c-b6af7115c51c";

#pragma warning disable CS0649
        /// <summary>For the probe: the settings somewhere else. Set by reflection.</summary>
        internal static string DirOverride;
#pragma warning restore CS0649

        public static string Dir
        {
            get
            {
                if (DirOverride != null) return DirOverride;
                var dll = typeof(XemuSettings).Assembly.Location;
                var root = Path.GetDirectoryName(Path.GetDirectoryName(dll)) ?? Path.GetDirectoryName(dll);
                return Path.Combine(root, ".data", PluginId);
            }
        }

        public static string SettingsPath => Path.Combine(Dir, "settings.ini");

        public static Dictionary<string, string> Read()
        {
            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                if (!File.Exists(SettingsPath)) return values;
                foreach (var line in File.ReadAllLines(SettingsPath))
                {
                    var t = line.Trim();
                    if (t.Length == 0 || t[0] == '#') continue;
                    int eq = t.IndexOf('=');
                    if (eq > 0) values[t.Substring(0, eq).Trim()] = t.Substring(eq + 1).Trim();
                }
            }
            catch (Exception ex) { Log.Warn("could not read " + SettingsPath, ex); }
            return values;
        }

        public static void Write(IDictionary<string, string> values)
        {
            Directory.CreateDirectory(Dir);
            var lines = new List<string> { "# Nixx-Xemu: every game. Edited by the Nixx window." };
            lines.AddRange(values.Where(kv => !string.IsNullOrEmpty(kv.Value)).OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase).Select(kv => kv.Key + "=" + kv.Value));
            var tmp = SettingsPath + ".tmp";
            File.WriteAllLines(tmp, lines);
            File.Move(tmp, SettingsPath, overwrite: true);
        }

        public static long CacheBytes()
        {
            return Read().TryGetValue("cache_gb", out var v) && double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out var gb) && gb > 0
                ? (long)(gb * (1L << 30)) : XemuDisc.DefaultCacheBytes;
        }
    }
}
