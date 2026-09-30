// Overriding the emulator's settings in memory, and keeping them out of its file.
//
// THE OBJECTS. MainMenuManager.mainMenuSettings is one MainMenuSettings, deserialised by
// LoadMainMenuSave from szsnes_ui.data and serialised back by SaveMainMenuSave - at exit, and
// every time the game returns to the menu. Per-game settings are GameSpecificSettings objects in
// its gameSettings dictionary, handed out by MainMenuManager.GetGameSettings(filename), which
// creates the entry when there is none. Il2CppInterop exposes every IL2CPP field as a C# property
// of the same name, so a field is reached by reflection on the interop type - by NAME, which is
// what survives an emulator update.
//
// NOT WRITTEN, UNLESS ASKED. The emulator serialises whatever is in memory, so an override left in
// place would land in the user's file at the next save. Around every SaveMainMenuSave the prefix
// puts the user's own values back and the postfix re-applies ours, so the file on disk never holds a
// value the user did not choose. --nixx-persist turns that off, for someone who wants the override
// to stick.
//
// Applied twice, on purpose: at LoadMainMenuSave's end (the settings object exists now) and on
// every GetGameSettings answer (the per-game object may be new). Both are idempotent.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text;
using HarmonyLib;

namespace LbIntegrations.SuperZsnes.Mod
{
    internal static class Overrides
    {
        private sealed class Applied
        {
            public object Target;
            public PropertyInfo Property;
            public object Original;
            public object Value;
        }

        private static readonly List<Applied> _applied = new List<Applied>();
        private static bool _settingsDone;

        /// <summary>The reachable members of a type: public instance properties of a scalar type.
        /// Il2CppInterop generates one per IL2CPP field, plus the class's own properties.</summary>
        internal static IEnumerable<PropertyInfo> Scalars(Type type)
        {
            foreach (var p in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (!p.CanRead || !p.CanWrite || p.GetIndexParameters().Length != 0) continue;
                var t = p.PropertyType;
                if (t == typeof(string) || t == typeof(bool) || t == typeof(int) || t == typeof(float)
                    || t == typeof(double) || t == typeof(long) || t.IsEnum)
                    yield return p;
            }
        }

        private static PropertyInfo Find(Type type, string name)
            => Scalars(type).FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));

        /// <summary>Apply a name=value map to one object. Remembers the original the first time a
        /// property of that object is touched.</summary>
        private static void Apply(object target, Dictionary<string, string> wanted, string what)
        {
            if (target == null) return;
            var type = target.GetType();
            foreach (var kv in wanted)
            {
                var prop = Find(type, kv.Key);
                if (prop == null) { Plugin.Logger.LogWarning(what + ": no scalar field named \"" + kv.Key + "\" on " + type.Name); continue; }
                var value = Options.Convert(kv.Value, prop.PropertyType, out var problem);
                if (problem != null) { Plugin.Logger.LogWarning(what + ": " + kv.Key + " = \"" + kv.Value + "\" - " + problem); continue; }
                try
                {
                    var known = _applied.FirstOrDefault(a => ReferenceEquals(a.Target, target) && a.Property == prop);
                    if (known == null)
                    {
                        known = new Applied { Target = target, Property = prop, Original = prop.GetValue(target) };
                        _applied.Add(known);
                        Plugin.Logger.LogInfo(what + ": " + prop.Name + " " + Show(known.Original) + " -> " + Show(value));
                    }
                    known.Value = value;
                    prop.SetValue(target, value);
                }
                catch (Exception ex) { Plugin.Logger.LogWarning(what + ": could not set " + prop.Name + " - " + ex.Message); }
            }
        }

        private static string Show(object v) => v == null ? "null" : v is string s ? "\"" + s + "\"" : System.Convert.ToString(v, CultureInfo.InvariantCulture);

        internal static void RestoreAll()
        {
            foreach (var a in _applied)
                try { a.Property.SetValue(a.Target, a.Original); } catch { }
        }

        internal static void ReapplyAll()
        {
            foreach (var a in _applied)
                try { a.Property.SetValue(a.Target, a.Value); } catch { }
        }

        // ── the patches ──────────────────────────────────────────────────────

        [HarmonyPatch(typeof(MainMenuManager), "LoadMainMenuSave")]
        internal static class AfterLoad
        {
            private static void Postfix(MainMenuManager __instance)
            {
                try
                {
                    if (Plugin.Opt.Settings.Count == 0 && !Plugin.Opt.DumpOptions) return;
                    var settings = __instance.mainMenuSettings;
                    if (settings == null) { Plugin.Logger.LogWarning("no MainMenuSettings after LoadMainMenuSave"); return; }
                    if (!_settingsDone)
                    {
                        _settingsDone = true;
                        if (Plugin.Opt.DumpOptions) Schema.Dump(settings);
                    }
                    Apply(settings, Plugin.Opt.Settings, "set");
                }
                catch (Exception ex) { Plugin.Logger.LogWarning("after LoadMainMenuSave - " + ex.Message); }
            }
        }

        [HarmonyPatch(typeof(MainMenuManager), nameof(MainMenuManager.GetGameSettings))]
        internal static class AfterGameSettings
        {
            private static void Postfix(MainMenuManager.GameSpecificSettings __result)
            {
                try
                {
                    if (Plugin.Opt.GameSettings.Count == 0 || __result == null) return;
                    Apply(__result, Plugin.Opt.GameSettings, "game");
                }
                catch (Exception ex) { Plugin.Logger.LogWarning("after GetGameSettings - " + ex.Message); }
            }
        }

        [HarmonyPatch(typeof(MainMenuManager), nameof(MainMenuManager.SaveMainMenuSave))]
        internal static class AroundSave
        {
            private static void Prefix()
            {
                if (Plugin.Opt.Persist || _applied.Count == 0) return;
                RestoreAll();
            }

            private static void Postfix()
            {
                if (Plugin.Opt.Persist || _applied.Count == 0) return;
                ReapplyAll();
            }
        }
    }

    /// <summary>What the two override families can reach, written once as JSON so the LaunchBox
    /// side can build its window from the emulator's actual fields rather than from a list typed
    /// off a dump. Hand-rolled JSON: five shapes, no library to drag in.</summary>
    internal static class Schema
    {
        public static void Dump(MainMenuManager.MainMenuSettings settings)
        {
            try
            {
                var sb = new StringBuilder();
                sb.Append("{\n  \"emulator\": \"SUPER ZSNES\",\n  \"generated\": \"").Append(DateTime.UtcNow.ToString("O")).Append("\",\n");
                sb.Append("  \"settings\": [\n");
                Type(sb, typeof(MainMenuManager.MainMenuSettings), settings);
                sb.Append("  ],\n  \"game\": [\n");
                Type(sb, typeof(MainMenuManager.GameSpecificSettings), null);
                sb.Append("  ]\n}\n");
                var path = System.IO.Path.Combine(Plugin.PortableRoot, "options.json");
                System.IO.File.WriteAllText(path, sb.ToString());
                Plugin.Logger.LogInfo("options schema -> " + path);
            }
            catch (Exception ex) { Plugin.Logger.LogWarning("options schema - " + ex.Message); }
        }

        private static void Type(StringBuilder sb, Type type, object instance)
        {
            bool first = true;
            foreach (var p in Overrides.Scalars(type))
            {
                if (!first) sb.Append(",\n");
                first = false;
                var t = p.PropertyType;
                string kind = t == typeof(string) ? "string" : t == typeof(bool) ? "bool" : t.IsEnum ? "enum"
                            : (t == typeof(float) || t == typeof(double)) ? "float" : "int";
                sb.Append("    {\"name\": \"").Append(p.Name).Append("\", \"type\": \"").Append(kind).Append('"');
                if (t.IsEnum)
                    sb.Append(", \"values\": [").Append(string.Join(", ", Enum.GetNames(t).Select(n => "\"" + n + "\""))).Append(']');
                if (instance != null)
                {
                    object v = null;
                    try { v = p.GetValue(instance); } catch { }
                    sb.Append(", \"current\": ").Append(Json(v));
                }
                sb.Append('}');
            }
            sb.Append('\n');
        }

        private static string Json(object v)
        {
            if (v == null) return "null";
            if (v is bool b) return b ? "true" : "false";
            if (v is string s) return "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
            if (v is Enum) return "\"" + v + "\"";
            return System.Convert.ToString(v, CultureInfo.InvariantCulture);
        }
    }
}
