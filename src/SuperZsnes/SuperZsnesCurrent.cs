// What SUPER ZSNES runs on when this pack passes nothing - read off its own files, for the options window
// to show "SUPER ZSNES's own: <value>" rather than a blank (Mehdi, 01/10). Read only, never written.
//
//   the settings file   <exe>\portable\szsnes_ui.data (else LocalLow - SuperZsnesPaths.SettingsFile): one
//                       MainMenuManager+MainMenuSettings in .NET's binary format (NRBF). Decoded with
//                       System.Formats.Nrbf, which reads the records and LOADS NO TYPE - nothing of the game
//                       is instantiated. Members by NAME, the same names --nixx-set reaches; an enum is a
//                       record whose value__ is its index (the catalogue's choices are in the enum's order).
//                       Measured 01/10 on 0.310: the root record, every member of the catalogue there.
//   the registry        HKCU\Software\ZEMU Software Inc.\SUPERZSNES, Unity's own "Screenmanager ..." values,
//                       each name ending in a hash (_h182942802) - matched by the name before it. SHARED by
//                       every SUPER ZSNES of the Windows user: the last one that ran wrote them.
//
// Anything missing or unreadable is simply not known: the window then says "SUPER ZSNES's own" alone.

using System;
using System.Collections.Generic;
using System.Formats.Nrbf;
using System.Globalization;
using System.IO;
using System.Linq;
using Microsoft.Win32;

namespace LbIntegrations.SuperZsnes
{
    internal static class SuperZsnesCurrent
    {
        private const string RegistryKey = @"Software\ZEMU Software Inc.\SUPERZSNES";

        /// <summary>IniKey -> the value as the window shows it, for the options it can read.</summary>
        public static Dictionary<string, string> Read(string exePath)
        {
            var known = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try { FromSettingsFile(exePath, known); } catch (Exception ex) { Log.Info("current settings: the settings file could not be read (" + ex.Message + ")"); }
            try { FromRegistry(known); } catch (Exception ex) { Log.Info("current settings: the registry could not be read (" + ex.Message + ")"); }
            return known;
        }

#pragma warning disable SYSLIB5005 // System.Formats.Nrbf: stable since 10.0, the attribute is kept on some members
        /// <summary>Some members of the settings file as they are - a string, a bool, a number, or null for a null or a
        /// record. Empty when the file is not there or cannot be read. For the save management's folders.</summary>
        public static Dictionary<string, object> Raw(string exePath, params string[] members)
        {
            var raw = new Dictionary<string, object>(StringComparer.Ordinal);
            try
            {
                var file = SuperZsnesPaths.SettingsFile(exePath);
                if (file == null || !File.Exists(file)) return raw;
                using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                if (!(NrbfDecoder.Decode(stream, leaveOpen: false) is ClassRecord root)) return raw;
                foreach (var m in members)
                    if (root.HasMember(m))
                    {
                        var v = root.GetRawValue(m);
                        raw[m] = v is SerializationRecord ? null : v;
                    }
            }
            catch (Exception ex) { Log.Info("settings file: could not be read (" + ex.Message + ")"); }
            return raw;
        }

        private static void FromSettingsFile(string exePath, Dictionary<string, string> known)
        {
            var file = SuperZsnesPaths.SettingsFile(exePath);
            if (file == null || !File.Exists(file)) return;
            using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            if (!(NrbfDecoder.Decode(stream, leaveOpen: false) is ClassRecord root)) return;
            foreach (var o in SuperZsnesOptions.All.Where(x => x.Family == OptionFamily.Setting))
            {
                if (!root.HasMember(o.Key)) continue;
                var raw = root.GetRawValue(o.Key);
                string shown = null;
                switch (raw)
                {
                    case bool b: shown = b ? "on" : "off"; break;
                    case int or long or short or byte: shown = Convert.ToString(raw, CultureInfo.InvariantCulture); break;
                    case float f: shown = f.ToString("0.###", CultureInfo.InvariantCulture); break;
                    case double d: shown = d.ToString("0.###", CultureInfo.InvariantCulture); break;
                    case string s: shown = s; break;
                    case ClassRecord e when e.HasMember("value__"):
                        var i = Convert.ToInt32(e.GetRawValue("value__"), CultureInfo.InvariantCulture);
                        shown = o.Choices != null && i >= 0 && i < o.Choices.Length ? o.Choices[i] : i.ToString(CultureInfo.InvariantCulture);
                        break;
                }
                if (raw == null && o.Kind == OptionKind.Text) shown = o.Default ?? "empty";
                if (shown != null) known[o.IniKey] = shown;
            }
        }
#pragma warning restore SYSLIB5005

        private static void FromRegistry(Dictionary<string, string> known)
        {
            using var key = Registry.CurrentUser.OpenSubKey(RegistryKey);
            if (key == null) return;
            int? Value(string name)
            {
                var full = key.GetValueNames().FirstOrDefault(n => n.StartsWith(name + "_h", StringComparison.Ordinal));
                return full != null && key.GetValue(full) is int v ? v : (int?)null;
            }
            // Unity's FullScreenMode: 0 exclusive, 1 full screen window, 2 maximized window, 3 windowed.
            var mode = Value("Screenmanager Fullscreen mode");
            if (mode != null) known["unity.screen-fullscreen"] = mode == 3 ? "windowed" : "fullscreen";
            // "Use Native" 1 (the first run's, measured 01/10 with the key deleted): the display's own resolution, whatever
            // the width and height beside it say (1920 x 1080 then).
            var native = Value("Screenmanager Resolution Use Native");
            var w = Value("Screenmanager Resolution Width");
            var h = Value("Screenmanager Resolution Height");
            if (native == 1) { known["unity.screen-width"] = "the display's"; known["unity.screen-height"] = "the display's"; }
            else
            {
                if (w != null) known["unity.screen-width"] = w.Value.ToString(CultureInfo.InvariantCulture);
                if (h != null) known["unity.screen-height"] = h.Value.ToString(CultureInfo.InvariantCulture);
            }
            // 0-based in the registry, 1-based on -monitor.
            var monitor = Value("UnitySelectMonitor");
            if (monitor != null) known["unity.monitor"] = (monitor.Value + 1).ToString(CultureInfo.InvariantCulture);
        }
    }
}
