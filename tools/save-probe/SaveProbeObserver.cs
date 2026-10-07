// What LaunchBox calls on the OTHER plugins' save methods - logged, nothing changed. Harmony prefixes on:
//   - the save methods LaunchBox's own integrations override (System\Plugins: RetroArch, Dolphin, PCSX2 - not
//     obfuscated, real bodies, measured 07/10);
//   - the SDK's default bodies (EmulatorPlugin's virtuals), which run for every plugin that does not override them:
//     RPCS3, DuckStation, MAME... and the pack's plugins for the methods their older SDK does not know.
// Patched once, at the first host event, and again for an integration loaded later (AssemblyLoad).

#nullable disable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Unbroken.LaunchBox.Plugins;
using Unbroken.LaunchBox.Plugins.Data;

namespace LbIntegrations.SaveProbeMain
{
    internal static class SaveProbeObserver
    {
        private static readonly string[] SaveMethods =
        {
            "SupportsSaveManagement", "GetSaves", "IsSecondarySaveFile", "GetCompanionSaveFiles", "IsSaveActive", "IsSaveContainer",
            "UseSaveGroupIdForPersistedMatch", "TryBackupSave", "TryComputeSaveSignature", "TryGetSaveGroupInfo", "GetPotentialSaveSlots",
            "AddSaveFile", "RemoveSave",
        };
        private static readonly string[] Integrations = { "RetroArchPlugin", "DolphinPlugin", "Pcsx2Plugin" };

        private static Harmony _harmony;
        private static readonly HashSet<MethodBase> Done = new HashSet<MethodBase>();
        private static readonly object Gate = new object();

        public static void Install()
        {
            lock (Gate)
            {
                if (_harmony != null) return;
                _harmony = new Harmony("com.nixxou.lbip.saveprobe");
                AppDomain.CurrentDomain.AssemblyLoad += (_, e) => { try { lock (Gate) PatchIn(e.LoadedAssembly); } catch { } };
                PatchType(typeof(EmulatorPlugin), "SDK default");
                foreach (var a in AppDomain.CurrentDomain.GetAssemblies()) PatchIn(a);
            }
        }

        private static void PatchIn(Assembly a)
        {
            if (a == null || a.IsDynamic || !(a.GetName().Name ?? "").StartsWith("Unbroken.LaunchBox.Windows.", StringComparison.Ordinal)) return;
            Type[] types;
            try { types = a.GetTypes(); } catch (ReflectionTypeLoadException e) { types = e.Types.Where(t => t != null).ToArray(); } catch { return; }
            foreach (var t in types.Where(t => Integrations.Contains(t.Name) && typeof(EmulatorPlugin).IsAssignableFrom(t))) PatchType(t, t.Name);
        }

        private static void PatchType(Type t, string label)
        {
            var prefix = new HarmonyMethod(typeof(SaveProbeObserver).GetMethod(nameof(Prefix), BindingFlags.NonPublic | BindingFlags.Static));
            var postfix = new HarmonyMethod(typeof(SaveProbeObserver).GetMethod(nameof(PostfixGetSaves), BindingFlags.NonPublic | BindingFlags.Static));
            int n = 0;
            foreach (var m in t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                               .Where(m => SaveMethods.Contains(m.Name) && !m.IsAbstract && m.GetMethodBody() != null))
            {
                if (!Done.Add(m)) continue;
                try
                {
                    _harmony.Patch(m, prefix: prefix, postfix: m.Name == "GetSaves" ? postfix : null);
                    n++;
                }
                catch (Exception ex) { Probe.Log("observer: could not patch " + label + "." + m.Name + " - " + ex.Message); }
            }
            Probe.Log("observer: " + n + " save method(s) of " + label + " watched");
            if (t.Name == "RetroArchPlugin") SaveProbeTranslator.Patch(_harmony, t);
        }

        private static bool Skip(object instance) => instance?.GetType().Namespace?.StartsWith("LbIntegrations.SaveProbe", StringComparison.Ordinal) == true;

        private static void Prefix(object __instance, MethodBase __originalMethod, object[] __args)
        {
            try
            {
                if (Skip(__instance)) return;
                var who = __instance?.GetType().Name ?? "?";
                var where = __originalMethod.DeclaringType == typeof(EmulatorPlugin) ? " (SDK default)" : "";
                Probe.Log("OBSERVED " + who + "." + __originalMethod.Name + where + "(" + string.Join(", ", (__args ?? Array.Empty<object>()).Select(Describe)) + ")   by " + Probe.Caller());
            }
            catch { }
        }

        private static void PostfixGetSaves(object __instance, GetSavesResponse __result)
        {
            try
            {
                if (Skip(__instance)) return;
                var rows = __result?.FoundSaves ?? (IReadOnlyCollection<GameSaveBase>)Array.Empty<GameSaveBase>();
                Probe.Log("OBSERVED " + (__instance?.GetType().Name ?? "?") + ".GetSaves -> " + rows.Count + " row(s)" + (rows.Count == 0 ? "" : ": " + string.Join(" | ", rows.Take(6).Select(r => Probe.Safe(() => System.IO.Path.GetFileName(r.FileLocation)) + " [" + r.SaveGroupId + "]"))));
            }
            catch { }
        }

        private static string Describe(object a)
        {
            switch (a)
            {
                case null: return "null";
                case string s: return "\"" + s + "\"";
                case GameSaveBase s: return Probe.Save(s);
                case GetSavesArgs g:
                    return "emu=" + Probe.Emu(g.Emulator) + " games=[" + string.Join("; ", (g.Games ?? (IReadOnlyCollection<IGame>)Array.Empty<IGame>()).Take(5).Select(x => Probe.Game(Probe.Safe(() => x.Id))))
                         + ((g.Games?.Count ?? 0) > 5 ? "; +" + (g.Games.Count - 5) : "") + "] apps=" + (g.AdditionalApplications?.Count ?? 0);
                case AddSaveArgs s: return Probe.Save(s.SaveToAdd);
                default: return a.GetType().Name;
            }
        }
    }
}
