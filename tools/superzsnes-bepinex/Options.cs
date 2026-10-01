// What the command line may tell this plugin, and how it is read.
//
// THE GRAMMAR. Every argument of ours starts with "--nixx-", which the emulator's own parser skips:
// MasterExecutor.Awake ignores any argument that starts with "-" unless it is one of its nine own
// flags, and takes as the ROM the first argument ending in .smc/.sfc/.zip/.swc/.ufo (read off its
// x86, and measured: a ROM behind our flags still loads). So ours can go anywhere.
//
//   --nixx-set:<field>=<value>        a field of MainMenuSettings, by its exact name (case-insensitive)
//   --nixx-game:<field>=<value>       a field of GameSpecificSettings for the game being played
//   --nixx-quit-confirm=on|off        Escape asks twice, then quits (default on)
//   --nixx-menu-key=<KeyCode>         the key that opens the emulator's menu (default F1)
//   --nixx-portable=on|off            persistentDataPath -> <exe>\portable (default on)
//   --nixx-support-popup=on|off       the "check out our Patreon" dialog (default off: hidden)
//   --nixx-version-popup=on|off       the "a new version is out" dialog (default off: hidden - Mehdi, 01/10)
//   --nixx-display=primary            full screen window on the PRIMARY display, whichever index
//                                     Unity gives it - see Display.cs
//   --nixx-log                        write this plugin's lines to <exe>\portable\nixx.log too
//                                     (BepInEx is deployed silent; see NixxLog.cs)
//   --nixx-persist                    write the overrides into the settings file instead of
//                                     restoring the user's values around every save
//   --nixx-data-folders=on|off        saves, states and cheats in <exe>\saves, \states, \cheats rather than
//                                     beside the ROM, written for good (default on) - see DataFolders.cs
//   --nixx-dump-options               write <portable>\options.json, the schema of every field
//                                     the two --nixx-set/--nixx-game families can reach
//
// Values: true/false/on/off/1/0 for booleans, invariant-culture numbers, enum member names or
// numbers, anything for strings. A value with spaces is one argument in quotes, which Windows
// unquotes before the process sees it: "--nixx-set:srmPath=D:\my saves".
//
// Nothing here touches Unity: it runs in Load(), before the game exists.

using System;
using System.Collections.Generic;
using System.Globalization;

namespace LbIntegrations.SuperZsnes.Mod
{
    internal sealed class Options
    {
        public const string Prefix = "--nixx-";

        public readonly Dictionary<string, string> Settings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        public readonly Dictionary<string, string> GameSettings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        public bool QuitConfirm = true;
        public string MenuKey = "F1";
        public bool Portable = true;
        public bool SupportPopup = false;
        public bool VersionPopup = false;
        public bool Persist = false;
        public bool DumpOptions = false;
        public bool DataFolders = true;
        /// <summary>"primary", or null for "leave the display alone".</summary>
        public string Display;
        /// <summary>--nixx-log: this plugin's lines also go to portable\nixx.log. See NixxLog.</summary>
        public bool Log = false;
        /// <summary>--nixx-ra-probe / --nixx-ra-unlock: the RetroAchievements diagnostic - see RaProbe.cs.</summary>
        public bool RaProbe = false, RaUnlock = false, RaDrive = false;
        public readonly List<string> Unknown = new List<string>();

        public static Options Parse(string[] args)
        {
            var o = new Options();
            if (args == null) return o;
            foreach (var raw in args)
            {
                if (raw == null || !raw.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase)) continue;
                var arg = raw.Substring(Prefix.Length);
                string key, value;
                int eq = arg.IndexOf('=');
                if (eq >= 0) { key = arg.Substring(0, eq); value = arg.Substring(eq + 1); }
                else { key = arg; value = null; }

                if (key.StartsWith("set:", StringComparison.OrdinalIgnoreCase) && value != null)
                    o.Settings[key.Substring(4)] = value;
                else if (key.StartsWith("game:", StringComparison.OrdinalIgnoreCase) && value != null)
                    o.GameSettings[key.Substring(5)] = value;
                else
                {
                    switch (key.ToLowerInvariant())
                    {
                        case "quit-confirm":  o.QuitConfirm = Flag(value, true); break;
                        case "menu-key":      if (!string.IsNullOrWhiteSpace(value)) o.MenuKey = value.Trim(); break;
                        case "portable":      o.Portable = Flag(value, true); break;
                        case "support-popup": o.SupportPopup = Flag(value, true); break;
                        case "version-popup": o.VersionPopup = Flag(value, true); break;
                        case "persist":       o.Persist = Flag(value, true); break;
                        case "dump-options":  o.DumpOptions = Flag(value, true); break;
                        case "data-folders":  o.DataFolders = Flag(value, true); break;
                        case "display":       o.Display = string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToLowerInvariant(); break;
                        case "log":           o.Log = Flag(value, true); break;
                        case "ra-probe":      o.RaProbe = Flag(value, true); break;
                        case "ra-unlock":     o.RaUnlock = Flag(value, true); if (o.RaUnlock) o.RaProbe = true; break;
                        case "ra-drive":      o.RaDrive = Flag(value, true); if (o.RaDrive) o.RaProbe = true; break;
                        default: o.Unknown.Add(raw); break;
                    }
                }
            }
            return o;
        }

        /// <summary>A bare flag means true; on/off/true/false/1/0 otherwise.</summary>
        public static bool Flag(string value, bool bare)
        {
            if (value == null) return bare;
            switch (value.Trim().ToLowerInvariant())
            {
                case "1": case "on": case "true": case "yes": return true;
                case "0": case "off": case "false": case "no": return false;
                default: return bare;
            }
        }

        /// <summary>Convert a command-line value to a field's type. Null when it cannot be.</summary>
        public static object Convert(string value, Type type, out string problem)
        {
            problem = null;
            try
            {
                if (type == typeof(string)) return value;
                if (type == typeof(bool))
                {
                    var v = value.Trim().ToLowerInvariant();
                    if (v == "1" || v == "on" || v == "true" || v == "yes") return true;
                    if (v == "0" || v == "off" || v == "false" || v == "no") return false;
                    problem = "not a boolean: " + value; return null;
                }
                if (type == typeof(int)) return int.Parse(value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture);
                if (type == typeof(float)) return float.Parse(value.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture);
                if (type == typeof(double)) return double.Parse(value.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture);
                if (type == typeof(long)) return long.Parse(value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture);
                if (type.IsEnum)
                {
                    if (int.TryParse(value.Trim(), out var n)) return Enum.ToObject(type, n);
                    return Enum.Parse(type, value.Trim(), ignoreCase: true);
                }
                problem = "unsupported type " + type.Name; return null;
            }
            catch (Exception ex) { problem = ex.Message; return null; }
        }
    }
}
