// What LaunchBox this is, by its SHAPE - the one place the pack looks, so no other code compares version numbers
// (docs\lb141-plan.md, step 0).
//
// LaunchBox 14.1 (beta 3, read 16/10) reworked its plugin model: a "schema version 2" where an emulator integration
// implements IEmulatorIntegration plus optional capability interfaces (IEmulatorSaveCapability...), EmulatorPlugin kept as
// the "schema version 1" base, wrapped by the core in an EmulatorPluginAdapter. Its own integrations (RetroArch, Dolphin,
// PCSX2, MAME, Xemu...) moved to v2, so they no longer derive from EmulatorPlugin; its registry holds
// EmulatorIntegrations instead of EmulatorPlugins. The pack is still built against the 14.0.1.2 SDK and must run on
// both - each zone that differs (stock hooks, registry, restore statics) picks its way ONCE from what is read here.
//
// Read from types, never from a version number: a beta that changes again falls on "unknown", logged and switched off
// cleanly, instead of on the wrong branch. The v2 types are matched by NAME - they do not exist in the SDK we compile
// against. Everything here is reflection on metadata (types, interfaces, properties): nothing in the host is called.

#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using Unbroken.LaunchBox.Plugins;

namespace LbIntegrations.Compat
{
    /// <summary>How a LaunchBox integration (or the registry) is built in this process.</summary>
    internal enum LbForm
    {
        /// <summary>Its assembly is not loaded (not LaunchBox, not installed, or not loaded yet).</summary>
        NotLoaded,
        /// <summary>LaunchBox 14.0: an <see cref="EmulatorPlugin"/> / EmulatorPlugins.</summary>
        V1,
        /// <summary>LaunchBox 14.1: an IEmulatorIntegration with capability interfaces / EmulatorIntegrations.</summary>
        V2,
        /// <summary>Loaded, but neither: a LaunchBox the pack does not know. Whatever relies on it stays off.</summary>
        Unknown,
    }

    internal static class LbHostShape
    {
        public const string V2Integration = "Unbroken.LaunchBox.Plugins.Components.Emulators.IEmulatorIntegration";
        public const string V2SaveCapability = "Unbroken.LaunchBox.Plugins.Components.Emulators.IEmulatorSaveCapability";
        public const string RegistryType = "Unbroken.LaunchBox.Windows.Plugins.PluginRegistry";

        /// <summary>LaunchBox's integrations the pack looks into: assembly, type, family (as the router's emus).</summary>
        public static readonly (string Assembly, string Type, string Family)[] Natives =
        {
            ("Unbroken.LaunchBox.Windows.RetroArch", "RetroArchPlugin", "retroarch"),
            ("Unbroken.LaunchBox.Windows.Dolphin", "DolphinPlugin", "dolphin"),
            ("Unbroken.LaunchBox.Windows.Pcsx2", "Pcsx2Plugin", "pcsx2"),
            ("Unbroken.LaunchBox.Windows.BigPEmu", "BigPEmuPlugin", "bigpemu"),
            ("Unbroken.LaunchBox.Windows.Cemu", "CemuPlugin", "cemu"),
            ("Unbroken.LaunchBox.Windows.DuckStation", "DuckStationPlugin", "duckstation"),
            ("Unbroken.LaunchBox.Windows.Mame", "MamePlugin", "mame"),
            ("Unbroken.LaunchBox.Windows.Rpcs3", "Rpcs3Plugin", "rpcs3"),
            ("Unbroken.LaunchBox.Windows.ScummVm", "ScummVmPlugin", "scummvm"),
            ("Unbroken.LaunchBox.Windows.Xemu", "XemuPlugin", "xemu"),
        };

        /// <summary>True when the host's SDK carries the v2 plugin model (LaunchBox 14.1 and later).</summary>
        public static bool SdkHasV2 => typeof(EmulatorPlugin).Assembly.GetType(V2Integration, false) != null;

        /// <summary>The host SDK's assembly version (14.0.1.2, 14.1.0.3...).</summary>
        public static Version? SdkVersion => typeof(EmulatorPlugin).Assembly.GetName().Version;

        /// <summary>LaunchBox.dll's file version, beside the plugin's host (Core\). Null outside LaunchBox's Core.</summary>
        public static Version? LaunchBoxVersion()
        {
            try
            {
                var path = Path.Combine(AppContext.BaseDirectory, "LaunchBox.dll");
                if (!File.Exists(path)) return null;
                var fv = FileVersionInfo.GetVersionInfo(path);
                return new Version(fv.FileMajorPart, fv.FileMinorPart, fv.FileBuildPart, fv.FilePrivatePart);
            }
            catch { return null; }
        }

        /// <summary>The loaded assembly of a native integration, or null.</summary>
        public static Assembly? NativeAssembly(string assembly)
            => AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => !a.IsDynamic && string.Equals(a.GetName().Name, assembly, StringComparison.OrdinalIgnoreCase));

        /// <summary>A native integration's type and its form. The type is null when its assembly is not loaded, or loaded
        /// without a type of that name (form Unknown) - a missing type in a loaded assembly is a change, never "not here".</summary>
        public static (Type? Type, LbForm Form) Native(string assembly, string typeName)
        {
            var a = NativeAssembly(assembly);
            if (a == null) return (null, LbForm.NotLoaded);
            var t = a.GetType(assembly + "." + typeName.Replace("Plugin", "") + "." + typeName, false)
                    ?? a.GetType(assembly + "." + typeName, false)
                    ?? SafeTypes(a).FirstOrDefault(x => x.Name == typeName && !x.IsAbstract);
            return (t, FormOf(t));
        }

        /// <summary>V1 for an EmulatorPlugin, V2 for an IEmulatorIntegration (matched by name), Unknown otherwise.</summary>
        public static LbForm FormOf(Type? t)
        {
            if (t == null) return LbForm.Unknown;
            if (typeof(EmulatorPlugin).IsAssignableFrom(t)) return LbForm.V1;
            return Implements(t, V2Integration) ? LbForm.V2 : LbForm.Unknown;
        }

        /// <summary>True when <paramref name="t"/> implements the interface of that full name (v2 types are not in our SDK).</summary>
        public static bool Implements(Type t, string interfaceFullName)
        {
            try { return t.GetInterfaces().Any(i => i.FullName == interfaceFullName); }
            catch { return false; }
        }

        /// <summary>The form of LaunchBox's plugin registry: V1 (EmulatorPlugins), V2 (EmulatorIntegrations), NotLoaded
        /// (no registry in this process - LiteBox, or too early), Unknown (a registry with neither).</summary>
        public static LbForm Registry()
        {
            Type? t = null;
            foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (a.IsDynamic) continue;
                try { t = a.GetType(RegistryType, false); } catch { t = null; }
                if (t != null) break;
            }
            if (t == null) return LbForm.NotLoaded;
            const BindingFlags Pub = BindingFlags.Public | BindingFlags.Instance;
            if (t.GetProperty("EmulatorPlugins", Pub) != null) return LbForm.V1;
            if (t.GetProperty("EmulatorIntegrations", Pub) != null) return LbForm.V2;
            return LbForm.Unknown;
        }

        /// <summary>One line for the logs and the Compatibility page: versions, SDK model, registry, each loaded native.</summary>
        public static string Describe()
        {
            var parts = new List<string>
            {
                "LaunchBox " + (LaunchBoxVersion()?.ToString() ?? "?"),
                "SDK " + (SdkVersion?.ToString() ?? "?") + (SdkHasV2 ? " (v2 model)" : " (v1 model)"),
                "registry " + Registry(),
            };
            var natives = Natives.Select(n => (n.Family, Native(n.Assembly, n.Type).Form)).Where(n => n.Form != LbForm.NotLoaded).ToList();
            parts.Add(natives.Count == 0 ? "no LaunchBox integration loaded" : "integrations " + string.Join(", ", natives.Select(n => n.Family + " " + n.Form)));
            return string.Join("; ", parts);
        }

        private static IEnumerable<Type> SafeTypes(Assembly a)
        {
            Type?[] ts;
            try { ts = a.GetTypes(); } catch (ReflectionTypeLoadException e) { ts = e.Types; } catch { yield break; }
            foreach (var t in ts) if (t != null) yield return t;
        }
    }
}
