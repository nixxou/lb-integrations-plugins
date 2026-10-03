// The plugin's own settings, beside the other plugins' (<Plugins>\.data\<PluginId>\), in the shape Xenia's are:
//
//   settings.ini          every game
//     folder=             where games are unpacked on the disk - default <Cxbx-Reloaded's folder>\lbip-games
//     limit_gb=           how much room they may take there, 0 = no limit (the games launched longest ago go first)
//     ramdisk=on|off      unpack to a RAM disk when the game fits (default on)
//     ramdisk_below_gb=   up to how big a game goes to the RAM disk (default 4)
//     borderless=on|off   full screen in a window (Alt+Enter, Cxbx-Reloaded's own) once the game shows (default on)
//   games\<game id>.ini   one game
//     placement=auto|ram|disk
//     keep=on             its copy on the disk is never purged
//   titles.tsv            what each game file is (path, size, date -> title id, name, unpacked size) - so the saves of a
//                         game in a zip are found without opening the zip again

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace LbIntegrations.Cxbx
{
    internal static class CxbxSettings
    {
        /// <summary>manifest.json's PluginId.</summary>
        public const string PluginId = "9b0138fb-62bf-4f60-9e2b-578b9fe89720";

#pragma warning disable CS0649
        /// <summary>For the probe: the settings somewhere else. Set by reflection.</summary>
        internal static string DirOverride;
#pragma warning restore CS0649

        public static string Dir
        {
            get
            {
                if (DirOverride != null) return DirOverride;
                var dll = typeof(CxbxSettings).Assembly.Location;
                var root = Path.GetDirectoryName(Path.GetDirectoryName(dll)) ?? Path.GetDirectoryName(dll);
                return Path.Combine(root, ".data", PluginId);
            }
        }

        public static string SettingsPath => Path.Combine(Dir, "settings.ini");

        public static Dictionary<string, string> Read() => ReadFile(SettingsPath);

        public static void Write(IDictionary<string, string> values)
            => WriteFile(SettingsPath, "# Nixx-Cxbx: every game. Edited by the Nixx window.", values);

        public static string GamePath(string gameId)
        {
            if (string.IsNullOrWhiteSpace(gameId)) return null;
            var safe = new string(gameId.Trim().Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c).ToArray());
            return Path.Combine(Dir, "games", safe + ".ini");
        }

        public static Dictionary<string, string> ReadGame(string gameId)
        {
            var p = GamePath(gameId);
            return p == null ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) : ReadFile(p);
        }

        public static void WriteGame(string gameId, IDictionary<string, string> values)
        {
            var p = GamePath(gameId);
            if (p == null) return;
            if (values == null || values.All(kv => string.IsNullOrEmpty(kv.Value))) { try { if (File.Exists(p)) File.Delete(p); } catch { } return; }
            WriteFile(p, "# Nixx-Cxbx: this game. Edited by its options window.", values);
        }

        // ── the values ───────────────────────────────────────────────────────

        public static bool On(IDictionary<string, string> s, string key, bool byDefault)
        {
            if (!s.TryGetValue(key, out var v) || string.IsNullOrWhiteSpace(v)) return byDefault;
            return v == "on" || v == "1" || v.Equals("true", StringComparison.OrdinalIgnoreCase);
        }

        public static double Number(IDictionary<string, string> s, string key, double byDefault)
            => s.TryGetValue(key, out var v) && double.TryParse(v.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var d) && d >= 0 ? d : byDefault;

        public const double DefaultRamDiskBelowGb = 4;

        /// <summary>"auto" (the rule), "ram" (the RAM disk whenever one can be had), "disk".</summary>
        public static string Placement(IDictionary<string, string> game)
            => game.TryGetValue("placement", out var p) && (p == "ram" || p == "disk") ? p : "auto";

        public static bool Keep(IDictionary<string, string> game) => On(game, "keep", false);

        /// <summary>Is a bare ISO / XISO attached where it is (AIM) rather than unpacked: the game's own "attach_discs"
        /// (on / off) when it has one, else every game's (on by default).</summary>
        public static bool AttachDiscs(IDictionary<string, string> game)
            => game != null && game.TryGetValue("attach_discs", out var v) && (v == "on" || v == "off") ? v == "on" : On(Read(), "attach_discs", true);


        // ── files ────────────────────────────────────────────────────────────

        private static Dictionary<string, string> ReadFile(string path)
        {
            var v = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                if (File.Exists(path))
                    foreach (var line in File.ReadAllLines(path))
                    {
                        var at = line.IndexOf('=');
                        if (at > 0 && !line.TrimStart().StartsWith("#")) v[line.Substring(0, at).Trim()] = line.Substring(at + 1).Trim();
                    }
            }
            catch (Exception ex) { Log.Warn("could not read " + path, ex); }
            return v;
        }

        private static void WriteFile(string path, string header, IDictionary<string, string> values)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            var lines = new List<string> { header };
            lines.AddRange(values.Where(kv => !string.IsNullOrWhiteSpace(kv.Value)).OrderBy(kv => kv.Key, StringComparer.Ordinal).Select(kv => kv.Key + "=" + kv.Value));
            File.WriteAllLines(path, lines);
        }
    }
}
