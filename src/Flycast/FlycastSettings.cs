// This plugin's own settings - those that are not a game's: one key=value per line in
//
//     <plugin root>\.data\<PluginId>\settings.ini
//
// LaunchBox 14's place for a plugin's data (Local\Plugins\.data\<PluginId>), and the one the pack's
// uninstaller leaves alone - the same file and rules as the Vita3K plugin's (Vita3kSettings).
//
// Read at the moment a setting is used, never cached: the configuration window writes the file, and the
// next use sees it - no restart.
//
// The window's tab for them is FlycastSettingsPage.cs.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace LbIntegrations.Flycast
{
    internal static class FlycastSettings
    {
        /// <summary>manifest.json's PluginId.</summary>
        public const string PluginId = "9d2eed8a-4647-46b4-8495-ca776ce47036";

        /// <summary>For the probe: a settings file somewhere else than beside the install.</summary>
        internal static string PathOverride;

        /// <summary>The systems whose LaunchBox import can be filtered, in flycast-id's words (--sets).</summary>
        public static readonly string[] Systems = { "Dreamcast", "Naomi", "Naomi 2", "Atomiswave", "System SP" };

        private static string Key(string check, string system) => "Import" + check + "." + system.Replace(" ", "");

        /// <summary>In LaunchBox's Import ROM Files wizard, to Nixx-Flycast, for this system's platform - see
        /// FlycastLbImport. QUICK: by the file's name (an arcade set of Flycast's for this system, its GD-ROM image
        /// beside it) or extension (a Dreamcast disc) - instant. ON unless turned off.</summary>
        public static bool QuickCheck(string system) => Bool(Key("Quick", system), true);

        /// <summary>CRC: by the file's content - an arcade set's files by their CRCs, a set under another name
        /// told by its content, what is kept loaded by flycast-id as Flycast loads it; a Dreamcast disc's IP.BIN
        /// read. Minutes on a whole MAME folder, so OFF unless turned on.</summary>
        public static bool CrcCheck(string system) => Bool(Key("Crc", system), false);

        public static void SetQuickCheck(string system, bool on) => Write(Key("Quick", system), on ? "true" : "false");
        public static void SetCrcCheck(string system, bool on) => Write(Key("Crc", system), on ? "true" : "false");

        /// <summary>After an import to Nixx-Flycast, the arcade sets LaunchBox left out put back as versions of
        /// their game - see FlycastImportFinished. ON unless turned off.</summary>
        public static bool RepairImport => Bool("ImportRepairLeftOut", true);

        public static void SetRepairImport(bool on) => Write("ImportRepairLeftOut", on ? "true" : "false");

        public static string SettingsPath
        {
            get
            {
                if (PathOverride != null) return PathOverride;
                var dll = typeof(FlycastSettings).Assembly.Location;
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
