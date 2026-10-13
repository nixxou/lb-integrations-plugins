// LaunchBox's own list of the emulator plugins it loaded (lot 6, docs\saves-lot6.md): who already drives an emulator,
// before the pack's catch-all (Nixx-Saves) takes one nobody drives.
//
// Unbroken.LaunchBox.Windows.Plugins.PluginRegistry is a public type of LaunchBox's core, its members obfuscated (read
// 13/10, LaunchBox 14.0.1): the property EmulatorPlugins (ConcurrentDictionary<string, EmulatorPlugin>) keeps its name, its
// body read by ilspy is a decoy - called by reflection, the real body runs (as every reflection into the core, lot 4).
//
// THE LIVE REGISTRY (measured 13/10 in LaunchBox: the static field of PluginRegistry's own type stayed null all along the
// start-up - the catch-all took nothing): PluginRuntimeManager.Shared (a static property that keeps its name) holds the
// manager, whose instance field of type PluginRegistry is the one LaunchBox fills. Fields are found by their TYPE, never by
// their obfuscated names. The static field of the registry's own type is kept as a second way. Not found, unreadable or
// empty: null, with the reason - the catch-all then takes nothing.
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
        public const string ManagerName = "Unbroken.LaunchBox.Windows.Plugins.PluginRuntimeManager";

        private static readonly object Gate = new object();
        private static FieldInfo? _holder;            // the registry's own static field (second way)
        private static PropertyInfo? _shared;         // PluginRuntimeManager.Shared
        private static FieldInfo? _managed;           // the manager's PluginRegistry field
        private static string _via = "";
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
                    if ((_holder == null && _managed == null) || _plugins == null) { why = _missing ?? "LaunchBox's plugin registry not found"; return null; }
                }
                object? registry = null;
                if (_shared != null && _managed != null)
                {
                    var manager = _shared.GetValue(null);
                    if (manager != null) { registry = _managed.GetValue(manager); if (registry != null) _via = "PluginRuntimeManager.Shared"; }
                }
                if (registry == null && _holder != null) { registry = _holder.GetValue(null); if (registry != null) _via = _holder.DeclaringType?.Name + "." + _holder.Name; }
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
            return all == null ? why ?? "unreadable" : all.Count + " emulator plugins (" + _via + ")";
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
            const BindingFlags Instances = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
            try
            {
                var m = t.Assembly.GetType(ManagerName, false);
                if (m != null)
                {
                    _shared = m.GetProperty("Shared", Statics) is { } sp && sp.PropertyType == m ? sp : null;
                    _managed = m.GetFields(Instances).FirstOrDefault(f => f.FieldType == t);
                }
            }
            catch { _shared = null; _managed = null; }
            _holder = t.GetFields(Statics).FirstOrDefault(f => f.FieldType == t);
            if (_holder != null || _managed != null) return;
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
