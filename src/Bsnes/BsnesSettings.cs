// This plugin's settings: every game's in one key=value per line in
//
//     <plugin root>\.data\<PluginId>\settings.ini
//
// and a game's own beside it in games\<game id>.ini, the same keys - a key absent there follows every game's. The same
// files and rules as the Demul plugin's (DemulSettings). Read at the moment a setting is used, never cached: the Nixx
// window and a game's window write the files, the next launch sees them.
//
// THE OPTIONS are a table (All): each is a choice among a few values, with its default, written into settings.bml at
// every launch by BsnesLaunch (full screen: bsnes's --fullscreen). The Nixx page and a game's window are built from the same table.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace LbIntegrations.Bsnes
{
    internal sealed class BsnesOption
    {
        public string Key, Label, Hint;
        public string[] Values, Texts;    // the stored values, and what the lists show for them
        public string Default;

        public string TextOf(string value) { int i = Array.IndexOf(Values, value); return i < 0 ? value : Texts[i]; }
    }

    internal static class BsnesSettings
    {
        /// <summary>manifest.json's PluginId.</summary>
        public const string PluginId = "652457a1-9a00-4701-9801-14e1c01144c2";

        /// <summary>For the probe: a settings file somewhere else than beside the install.</summary>
        internal static string PathOverride;

        public static readonly BsnesOption[] All =
        {
            new BsnesOption { Key = "Fullscreen", Label = "Screen", Default = "true",
                Values = new[] { "true", "false" }, Texts = new[] { "Full screen", "Windowed" },
                Hint = "Full screen from the start (bsnes's --fullscreen). Alt+Enter is not bound: assign Toggle Fullscreen in bsnes's hotkeys to switch." },
            new BsnesOption { Key = "Aspect", Label = "Aspect", Default = "4:3",
                Values = new[] { "4:3", "8:7" }, Texts = new[] { "4:3, as on a TV", "8:7, square pixels" },
                Hint = "bsnes's Aspect Correction: on, the picture a Super Nintendo gave on a television; off, the console's pixels square (256 x 224)." },
            new BsnesOption { Key = "Output", Label = "Scaling", Default = "scale",
                Values = new[] { "scale", "center", "stretch" }, Texts = new[] { "Fill the screen, keep the shape", "Integer only (Center)", "Stretch to the screen" },
                Hint = "bsnes's Output: Scale fills the screen and keeps the aspect; Center scales by whole numbers only, every pixel the same size, "
                     + "with borders; Stretch fills it all." },
            new BsnesOption { Key = "Smooth", Label = "Smoothing", Default = "false",
                Values = new[] { "true", "false" }, Texts = new[] { "On (blur)", "Off - sharp pixels" },
                Hint = "bsnes's Blur: bilinear filtering of the scaled picture. Off is bsnes's default." },
            new BsnesOption { Key = "VSync", Label = "VSync", Default = "false",
                Values = new[] { "true", "false" }, Texts = new[] { "On - against tearing", "Off" },
                Hint = "bsnes's video Blocking: waits for the screen's refresh. Off is bsnes's default - its audio paces the game." },
            new BsnesOption { Key = "Mode7", Label = "HD Mode 7", Default = "1",
                Values = new[] { "1", "2", "3", "4" }, Texts = new[] { "Off (native)", "2x", "3x", "4x" },
                Hint = "bsnes's HD Mode 7: the rotated and zoomed planes (F-Zero, Mario Kart, Pilotwings) drawn at a higher resolution. Needs bsnes's fast PPU, "
                     + "its default; costs speed at 4x." },
        };
        public static BsnesOption Option(string key) => All.First(o => o.Key == key);

        // ── every game's ──

        /// <summary>Every game's value of <paramref name="key"/>: what the Nixx window set, else the option's default.</summary>
        public static string Value(string key)
        {
            var o = Option(key);
            var v = Read(SettingsPath).TryGetValue(key, out var s) ? (s ?? "").Trim().ToLowerInvariant() : null;
            return v != null && o.Values.Contains(v) ? v : o.Default;
        }

        public static void SetValue(string key, string value) => Write(SettingsPath, key, value);

        // ── a game's own ──

        public static string GamePath(string gameId)
        {
            var safe = string.Concat((gameId ?? "").Where(c => char.IsLetterOrDigit(c) || c == '-'));
            return safe.Length == 0 ? null : Path.Combine(Path.GetDirectoryName(SettingsPath), "games", safe + ".ini");
        }

        /// <summary>The game's own value of <paramref name="key"/>, or null: it follows every game's.</summary>
        public static string GameValue(string gameId, string key)
        {
            var path = GamePath(gameId);
            if (path == null) return null;
            var v = Read(path).TryGetValue(key, out var s) && !string.IsNullOrWhiteSpace(s) ? s.Trim().ToLowerInvariant() : null;
            return v != null && Option(key).Values.Contains(v) ? v : null;
        }

        /// <summary>The game's own value set, or taken away with null.</summary>
        public static void SetGameValue(string gameId, string key, string value)
        {
            var path = GamePath(gameId);
            if (path != null) Write(path, key, value);
        }

        /// <summary>What a game starts with: its own value, else every game's.</summary>
        public static string For(string gameId, string key) => GameValue(gameId, key) ?? Value(key);

        public static bool BoolFor(string gameId, string key) => For(gameId, key) == "true";

        // ── the files ──

        public static string SettingsPath
        {
            get
            {
                if (PathOverride != null) return PathOverride;
                var dll = typeof(BsnesSettings).Assembly.Location;
                var pluginDir = Path.GetDirectoryName(dll);
                var root = Path.GetDirectoryName(pluginDir) ?? pluginDir;
                return Path.Combine(root, ".data", PluginId, "settings.ini");
            }
        }

        private static Dictionary<string, string> Read(string path)
        {
            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                if (!File.Exists(path)) return values;
                foreach (var line in File.ReadAllLines(path))
                {
                    var at = line.IndexOf('=');
                    if (at <= 0 || line.TrimStart().StartsWith("#")) continue;
                    values[line.Substring(0, at).Trim()] = line.Substring(at + 1).Trim();
                }
            }
            catch (Exception ex) { Log.Warn("settings: could not read " + path, ex); }
            return values;
        }

        /// <summary><paramref name="key"/> set in <paramref name="path"/> - taken out with a null value.</summary>
        private static void Write(string path, string key, string value)
        {
            var lines = File.Exists(path) ? File.ReadAllLines(path).ToList() : new List<string>();
            lines.RemoveAll(l => l.IndexOf('=') > 0 && string.Equals(l.Substring(0, l.IndexOf('=')).Trim(), key, StringComparison.OrdinalIgnoreCase));
            if (value != null) lines.Add(key + "=" + value);
            if (value == null && lines.Count == 0 && !File.Exists(path)) return;
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllLines(path, lines);
        }
    }
}
