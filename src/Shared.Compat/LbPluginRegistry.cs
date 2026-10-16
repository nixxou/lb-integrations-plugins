// LaunchBox's own list of the emulator plugins it loaded (lot 6, docs\saves-lot6.md): who already drives an emulator,
// before the pack's catch-all (Nixx-Saves) takes one nobody drives.
//
// Unbroken.LaunchBox.Windows.Plugins.PluginRegistry is a public type of LaunchBox's core, its members obfuscated (read
// 13/10, LaunchBox 14.0.1): the property EmulatorPlugins (ConcurrentDictionary<string, EmulatorPlugin>) keeps its name, its
// body read by ilspy is a decoy - called by reflection, the real body runs (as every reflection into the core, lot 4).
//
// LAUNCHBOX 14.1 (beta 3, read 16/10, docs\lb141-plan.md step 2): the registry holds EmulatorIntegrations instead
// (ConcurrentDictionary<string, IEmulatorIntegration>) - LaunchBox's own integrations are v2 objects, a v1 plugin (ours)
// is wrapped in an EmulatorPluginAdapter whose field of type EmulatorPlugin is the plugin itself. Which property is read
// is LbHostShape's answer for the registry; each entry becomes a Driver: the v1 plugin when there is one (unwrapped), the
// v2 integration asked by reflection otherwise (IEmulatorIntegration is not in the 14.0 SDK the pack is built against).
//
// THE LIVE REGISTRY (measured 13/10 in LaunchBox: the static field of PluginRegistry's own type stayed null all along the
// start-up - the catch-all took nothing): PluginRuntimeManager.Shared (a static property that keeps its name) holds the
// manager, whose instance field of type PluginRegistry is the one LaunchBox fills (14.1 b3: still there, read 16/10).
// Fields are found by their TYPE, never by their obfuscated names. The static field of the registry's own type is kept as
// a second way. Not found, unreadable or empty: null, with the reason - the catch-all then takes nothing.
//
// Under LiteBox there is no such type: LiteBox has a registry of its own and asks every plugin itself.

#nullable enable

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Unbroken.LaunchBox.Plugins;
using Unbroken.LaunchBox.Plugins.Data;

namespace LbIntegrations.Compat
{
    /// <summary>One emulator plugin LaunchBox loaded: a v1 <see cref="EmulatorPlugin"/> (14.0, or one of ours wrapped by
    /// 14.1), or a v2 integration (14.1's own) reached by reflection.</summary>
    internal sealed class LbDriver
    {
        /// <summary>The v1 plugin, when it is one - ours included.</summary>
        public EmulatorPlugin? Plugin { get; }
        /// <summary>The v2 integration, when it is not a v1 plugin.</summary>
        public object? Integration { get; }
        private readonly MethodInfo? _applicable;

        public LbDriver(EmulatorPlugin plugin) { Plugin = plugin; }

        private LbDriver(object integration, MethodInfo applicable) { Integration = integration; _applicable = applicable; }

        /// <summary>A v2 integration's driver, or null when it has no GetApplicableEmulators where IEmulatorIntegration puts it.</summary>
        public static LbDriver? OfIntegration(object integration)
        {
            var i = integration.GetType().GetInterfaces().FirstOrDefault(x => x.FullName == LbHostShape.V2Integration);
            var m = i?.GetMethod("GetApplicableEmulators", new[] { typeof(IEnumerable<IEmulator>) });
            return m == null ? null : new LbDriver(integration, m);
        }

        public Type Type => (Plugin ?? Integration)!.GetType();

        /// <summary>The emulators it takes among <paramref name="emulators"/> - its own exception passes through.</summary>
        public IEnumerable<IEmulator>? GetApplicableEmulators(IEnumerable<IEmulator> emulators)
        {
            if (Plugin != null) return Plugin.GetApplicableEmulators(emulators);
            try { return _applicable!.Invoke(Integration, new object[] { emulators }) as IEnumerable<IEmulator>; }
            catch (TargetInvocationException ex) when (ex.InnerException != null) { throw ex.InnerException; }
        }

        /// <summary>For the logs: its emulator name (v1) or component name (v2), and its type.</summary>
        public string Describe()
        {
            string n = "";
            try { n = Plugin != null ? Plugin.EmulatorName ?? "" : Integration!.GetType().GetProperty("Name")?.GetValue(Integration) as string ?? ""; } catch { }
            return (n.Length > 0 ? n : Type.Name) + " [" + Type.Name + "]";
        }
    }

    internal static class LbPluginRegistry
    {
        public const string TypeName = LbHostShape.RegistryType;
        public const string ManagerName = "Unbroken.LaunchBox.Windows.Plugins.PluginRuntimeManager";

        /// <summary>The names, in the registry, of the method LaunchBox picks a plugin with: 14.0, 14.1.</summary>
        public static readonly string[] PickerNames = { "GetBestEmulatorPlugin", "GetBestEmulatorIntegration" };

        private static readonly object Gate = new object();
        private static FieldInfo? _holder;            // the registry's own static field (second way)
        private static PropertyInfo? _shared;         // PluginRuntimeManager.Shared
        private static FieldInfo? _managed;           // the manager's PluginRegistry field
        private static string _via = "";
        private static PropertyInfo? _plugins;        // EmulatorPlugins (14.0) or EmulatorIntegrations (14.1)
        private static LbForm _form;
        private static string? _missing;
        private static bool _searched;

        /// <summary>For the probe: the registry's emulator plugins as a test sets them (null: LaunchBox's own).</summary>
        internal static Func<IReadOnlyList<EmulatorPlugin>?>? ForTests;

        /// <summary>Every emulator plugin LaunchBox loaded, or null with <paramref name="why"/> when they cannot be read.</summary>
        public static IReadOnlyList<LbDriver>? Drivers(out string? why)
        {
            why = null;
            if (ForTests != null)
            {
                var given = ForTests();
                if (given == null) why = "the test gives no registry";
                return given?.Select(p => new LbDriver(p)).ToList();
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
                var entries = new List<object>();
                if (value is IDictionary d) { foreach (var v in d.Values) if (v != null) entries.Add(v); }
                else if (value is IEnumerable e)
                {
                    foreach (var item in e)
                        if (item?.GetType().GetProperty("Value")?.GetValue(item) is { } v) entries.Add(v);
                }
                else { why = "LaunchBox's " + _plugins.Name + " is " + (value == null ? "null" : value.GetType().Name); return null; }
                var list = new List<LbDriver>();
                var odd = new List<string>();
                foreach (var entry in entries)
                {
                    var driver = Driver(entry);
                    if (driver != null) list.Add(driver); else odd.Add(entry.GetType().Name);
                }
                // An entry neither a plugin nor an integration we can ask: it may drive an emulator - nothing is decided.
                if (odd.Count > 0) { why = "LaunchBox's registry holds entries the pack cannot ask: " + string.Join(", ", odd.Distinct()); return null; }
                if (list.Count == 0) { why = "LaunchBox's plugin registry holds no emulator plugin yet"; return null; }
                return list;
            }
            catch (Exception ex) { why = "LaunchBox's plugin registry unreadable: " + (ex.InnerException ?? ex).GetType().Name + ": " + (ex.InnerException ?? ex).Message; return null; }
        }

        /// <summary>An entry of the registry as a driver: a v1 plugin as it is; a 14.1 adapter unwrapped to the v1 plugin it
        /// holds (a field of type EmulatorPlugin, found by type); a v2 integration as it is; else null.</summary>
        private static LbDriver? Driver(object entry)
        {
            if (entry is EmulatorPlugin p) return new LbDriver(p);
            for (var t = entry.GetType(); t != null && t != typeof(object); t = t.BaseType)
                foreach (var f in t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                    if (typeof(EmulatorPlugin).IsAssignableFrom(f.FieldType) && f.GetValue(entry) is EmulatorPlugin inner) return new LbDriver(inner);
            return LbDriver.OfIntegration(entry);
        }

        /// <summary>What the Compatibility page's check says: the registry found and its emulator plugins counted.</summary>
        public static string Describe(out bool ok)
        {
            var all = Drivers(out var why);
            ok = all != null;
            if (all == null) return why ?? "unreadable";
            int v1 = all.Count(d => d.Plugin != null);
            return all.Count + " emulator plugins (" + _via + ", " + (_form == LbForm.V2 ? "EmulatorIntegrations: " + v1 + " v1, " + (all.Count - v1) + " v2" : "EmulatorPlugins") + ")";
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
            _form = LbHostShape.Registry();
            _plugins = _form switch
            {
                LbForm.V1 => t.GetProperty("EmulatorPlugins", BindingFlags.Public | BindingFlags.Instance),
                LbForm.V2 => t.GetProperty("EmulatorIntegrations", BindingFlags.Public | BindingFlags.Instance),
                _ => null,
            };
            if (_plugins == null) { _missing = TypeName + " has neither EmulatorPlugins (14.0) nor EmulatorIntegrations (14.1)"; return; }
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
