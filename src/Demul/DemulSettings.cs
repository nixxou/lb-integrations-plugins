// This plugin's own settings - those that are not a game's: one key=value per line in
//
//     <plugin root>\.data\<PluginId>\settings.ini
//
// LaunchBox 14's place for a plugin's data (Local\Plugins\.data\<PluginId>), and the one the pack's uninstaller
// leaves alone - the same file and rules as the Flycast plugin's (FlycastSettings).
//
// Read at the moment a setting is used, never cached: the Nixx window writes the file, and the next import or
// launch sees it - no restart. The window's page for them is DemulSettingsPage.cs.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace LbIntegrations.Demul
{
    internal static class DemulSettings
    {
        /// <summary>manifest.json's PluginId.</summary>
        public const string PluginId = "a38528af-8941-4a7e-9c03-6666b75e00fa";

        /// <summary>For the probe: a settings file somewhere else than beside the install.</summary>
        internal static string PathOverride;

        /// <summary>The boards whose LaunchBox import is sorted (DemulPlatforms.BoardOf's names), in the page's order.</summary>
        public static readonly string[] Boards = { "Dreamcast", "Naomi", "Naomi 2", "Atomiswave", "Hikaru", "System SP", "CV1000", "Gaelco" };

        private static string BoardKey(string board) => "ImportSort." + board.Replace(" ", "");

        /// <summary>In LaunchBox's Import ROM Files wizard, to Nixx-Demul, for this board's platform: the last list keeps only
        /// what Demul can run of that board - see DemulLbImport. ON unless turned off.</summary>
        public static bool SortImport(string board) => Bool(BoardKey(board), true);
        public static void SetSortImport(string board, bool on) => Write(BoardKey(board), on ? "true" : "false");

        /// <summary>The sets of the right board that are not in Demul's game list: "ask" (the default - once per import, for
        /// all of them), "keep" or "drop".</summary>
        public static string UnknownSets
        {
            get { var v = Text("ImportUnknownSets"); return v == "keep" || v == "drop" ? v : "ask"; }
        }
        public static void SetUnknownSets(string value) => Write("ImportUnknownSets", value == "keep" || value == "drop" ? value : "ask");

        /// <summary>Demul's video plugin: "auto" (the default - gpuDX11old.dll for Gaelco and CV1000, gpuDX11.dll for the rest,
        /// as Batocera does), "new" (gpuDX11.dll always) or "old" (gpuDX11old.dll always).</summary>
        public static string VideoPlugin
        {
            get { var v = Text("VideoPlugin"); return v == "new" || v == "old" ? v : "auto"; }
        }
        public static void SetVideoPlugin(string value) => Write("VideoPlugin", value == "new" || value == "old" ? value : "auto");

        // ── the picture, written into the video plugin's .ini at every launch (DemulLaunch.PrepareGpu) ──
        // As Batocera's launcher sets them (batocera_launch/emulators/demul.py, demul.emulator.yml): its keys of gpuDX11.ini
        // [main] and [resolution], their values and defaults. Demul is no longer developed: the keys will not move.

        /// <summary>UseFullscreen = 1, at the screen's resolution. ON unless turned off (Batocera: always on).</summary>
        public static bool Fullscreen => Bool("Fullscreen", true);
        public static void SetFullscreen(bool on) => Write("Fullscreen", on ? "true" : "false");

        /// <summary>aspect: 0 stretched, 1 4:3 (the default), 2 16:9 - Batocera's "GAME ASPECT RATIO".</summary>
        public static int Aspect { get { var v = Int("Aspect", 1); return v >= 0 && v <= 2 ? v : 1; } }
        public static void SetAspect(int value) => Write("Aspect", Math.Max(0, Math.Min(2, value)).ToString());

        /// <summary>Vsync = 1 against tearing. OFF unless turned on (Batocera's default).</summary>
        public static bool VSync => Bool("VSync", false);
        public static void SetVSync(bool on) => Write("VSync", on ? "true" : "false");

        /// <summary>scaling, 1 to 6: Gaelco's picture scaled from its own resolution - Batocera's "SET GAELCO SCALING AMOUNT".</summary>
        public static int GaelcoScaling { get { var v = Int("GaelcoScaling", 1); return v >= 1 && v <= 6 ? v : 1; } }
        public static void SetGaelcoScaling(int value) => Write("GaelcoScaling", Math.Max(1, Math.Min(6, value)).ToString());

        public static string SettingsPath
        {
            get
            {
                if (PathOverride != null) return PathOverride;
                var dll = typeof(DemulSettings).Assembly.Location;
                var pluginDir = Path.GetDirectoryName(dll);
                var root = Path.GetDirectoryName(pluginDir) ?? pluginDir;
                return Path.Combine(root, ".data", PluginId, "settings.ini");
            }
        }

        private static string Text(string key) => Read().TryGetValue(key, out var s) ? (s ?? "").Trim().ToLowerInvariant() : null;

        private static int Int(string key, int fallback) => int.TryParse(Text(key), out var n) ? n : fallback;

        private static bool Bool(string key, bool fallback)
        {
            var v = Text(key);
            if (v == "true") return true;
            if (v == "false") return false;
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
            catch (Exception ex) { Log.Warn("settings: could not read " + SettingsPath, ex); }
            return values;
        }

        private static void Write(string key, string value)
        {
            var path = SettingsPath;
            var lines = File.Exists(path) ? File.ReadAllLines(path).ToList() : new List<string>();
            lines.RemoveAll(l => l.IndexOf('=') > 0 && string.Equals(l.Substring(0, l.IndexOf('=')).Trim(), key, StringComparison.OrdinalIgnoreCase));
            lines.Add(key + "=" + value);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllLines(path, lines);
        }
    }
}
