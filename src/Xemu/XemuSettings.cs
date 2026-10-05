// The plugin's own settings, beside the other plugins' (<Plugins>\.data\<PluginId>\settings.ini), Cxbx's shape:
//
//   cache_gb=      how much room the XISO copies may take in <xemu>\discs (default 40; the copies used longest ago go first)
//   savestates=on  xemu's snapshots shown in LaunchBox as savestates (off by default)
//   opt.<key>=     an option of every game (XemuOptions): xemu's settings, the console's, the media patch
//   games\<game id>.ini   one game's options, the same keys

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

        /// <summary>Is the media patch made for every game (XboxMediaPatch, the option disc.media_patch)? On unless set off.</summary>
        public static bool MediaPatch() => XemuOptions.Get(Read(), "disc.media_patch") != "off";

        /// <summary>Are xemu's snapshots shown in LaunchBox as savestates (Saves\XemuStates)? Off unless set on (Mehdi, 05/10):
        /// each one is a file of some 35 MB, exported at a game's close.</summary>
        public static bool Savestates() => Read().TryGetValue("savestates", out var v) && v == "on";

        public static string GamePath(string gameId)
        {
            if (string.IsNullOrWhiteSpace(gameId)) return null;
            var safe = new string(gameId.Trim().Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c).ToArray());
            return Path.Combine(Dir, "games", safe + ".ini");
        }

        public static Dictionary<string, string> ReadGame(string gameId)
        {
            var p = GamePath(gameId);
            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (p == null || !File.Exists(p)) return values;
            try
            {
                foreach (var line in File.ReadAllLines(p))
                {
                    var t = line.Trim();
                    int eq = t.IndexOf('=');
                    if (t.Length > 0 && t[0] != '#' && eq > 0) values[t.Substring(0, eq).Trim()] = t.Substring(eq + 1).Trim();
                }
            }
            catch (Exception ex) { Log.Warn("could not read " + p, ex); }
            return values;
        }

        public static void WriteGame(string gameId, IDictionary<string, string> values)
        {
            var p = GamePath(gameId);
            if (p == null) return;
            if (values == null || values.All(kv => string.IsNullOrEmpty(kv.Value))) { try { if (File.Exists(p)) File.Delete(p); } catch { } return; }
            Directory.CreateDirectory(Path.GetDirectoryName(p));
            var lines = new List<string> { "# Nixx-Xemu: this game. Edited by its options window." };
            lines.AddRange(values.Where(kv => !string.IsNullOrEmpty(kv.Value)).OrderBy(kv => kv.Key, StringComparer.Ordinal).Select(kv => kv.Key + "=" + kv.Value));
            var tmp = p + ".tmp";
            File.WriteAllLines(tmp, lines);
            File.Move(tmp, p, overwrite: true);
        }

        public static long CacheBytes()
        {
            return Read().TryGetValue("cache_gb", out var v) && double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out var gb) && gb > 0
                ? (long)(gb * (1L << 30)) : XemuDisc.DefaultCacheBytes;
        }
    }
}
