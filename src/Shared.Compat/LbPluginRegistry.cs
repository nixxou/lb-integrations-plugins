// LaunchBox's own list of the emulator plugins it loaded (lot 6, docs\saves-lot6.md): who already drives an emulator,
// before the pack's catch-all (Nixx-Saves) takes one nobody drives.
//
// Unbroken.LaunchBox.Windows.Plugins.PluginRegistry is a public type of LaunchBox's core, its members obfuscated (read
// 13/10, LaunchBox 14.0.1): the property EmulatorPlugins (ConcurrentDictionary<string, EmulatorPlugin>) keeps its name, its
// body read by ilspy is a decoy - called by reflection, the real body runs (as every reflection into the core, lot 4). The
// instance is held in a static field of the type itself, under an obfuscated name: found by its TYPE, never by its name,
// and the whole assembly is searched once if that field is gone in a later build. Not found, unreadable or empty: null,
// with the reason - the catch-all then takes nothing.
//
// Under LiteBox there is no such type: LiteBox has a registry of its own and asks every plugin itself.

#nullable enable

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Unbroken.LaunchBox.Plugins;

namespace LbIntegrations.Compat
{
    internal static class LbPluginRegistry
    {
        public const string TypeName = "Unbroken.LaunchBox.Windows.Plugins.PluginRegistry";

        private static readonly object Gate = new object();
        private static FieldInfo? _holder;
        private static PropertyInfo? _plugins;
        private static string? _missing;
        private static bool _searched;

        /// <summary>For the probe: the registry's emulator plugins as a test sets them (null: LaunchBox's own).</summary>
        internal static Func<IReadOnlyList<EmulatorPlugin>?>? ForTests;

        /// <summary>Every emulator plugin LaunchBox loaded, or null with <paramref name="why"/> when they cannot be read.</summary>
        public static IReadOnlyList<EmulatorPlugin>? EmulatorPlugins(out string? why)
        {
            why = null;
            if (ForTests != null)
            {
                var given = ForTests();
                if (given == null) why = "the test gives no registry";
                return given;
            }
            try
            {
                lock (Gate)
                {
                    if (!_searched) Find();
                    if (_holder == null || _plugins == null) { why = _missing ?? "LaunchBox's plugin registry not found"; return null; }
                }
                var registry = _holder.GetValue(null);
                if (registry == null) { why = "LaunchBox's plugin registry is not built yet"; return null; }
                var value = _plugins.GetValue(registry);
                var list = new List<EmulatorPlugin>();
                if (value is IDictionary d) { foreach (var v in d.Values) if (v is EmulatorPlugin p) list.Add(p); }
                else if (value is IEnumerable e)
                {
                    foreach (var item in e)
                    {
                        var v = item?.GetType().GetProperty("Value")?.GetValue(item);
                        if (v is EmulatorPlugin p) list.Add(p);
                    }
                }
                else { why = "LaunchBox's EmulatorPlugins is " + (value == null ? "null" : value.GetType().Name); return null; }
                if (list.Count == 0) { why = "LaunchBox's plugin registry holds no emulator plugin yet"; return null; }
                return list;
            }
            catch (Exception ex) { why = "LaunchBox's plugin registry unreadable: " + (ex.InnerException ?? ex).GetType().Name + ": " + (ex.InnerException ?? ex).Message; return null; }
        }

        /// <summary>What the Compatibility page's check says: the registry found and its emulator plugins counted.</summary>
        public static string Describe(out bool ok)
        {
            var all = EmulatorPlugins(out var why);
            ok = all != null;
            return all == null ? why ?? "unreadable" : all.Count + " emulator plugins (" + _holder?.DeclaringType?.Name + "." + _holder?.Name + ")";
        }

        private static void Find()
        {
            Type? t = null;
            foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (a.IsDynamic) continue;
                try { t = a.GetType(TypeName, false); } catch { t = null; }
                if (t != null) break;
            }
            // Not loaded (yet): looked for again at the next question. Found: never again.
            if (t == null) { _missing = "no " + TypeName + " in this process (not LaunchBox, or another version)"; return; }
            _searched = true;
            _plugins = t.GetProperty("EmulatorPlugins", BindingFlags.Public | BindingFlags.Instance);
            if (_plugins == null) { _missing = TypeName + " has no EmulatorPlugins any more"; return; }
            const BindingFlags Statics = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
            _holder = t.GetFields(Statics).FirstOrDefault(f => f.FieldType == t);
            if (_holder != null) return;
            // Moved in a later build: any static field of that type in the same assembly.
            Type?[] types;
            try { types = t.Assembly.GetTypes(); } catch (ReflectionTypeLoadException ex) { types = ex.Types; } catch { types = Array.Empty<Type>(); }
            foreach (var x in types)
            {
                if (x == null || x.ContainsGenericParameters) continue;
                try { _holder = x.GetFields(Statics).FirstOrDefault(f => f.FieldType == t); } catch { _holder = null; }
                if (_holder != null) return;
            }
            _missing = "no static field holds LaunchBox's " + TypeName;
        }
    }
}
