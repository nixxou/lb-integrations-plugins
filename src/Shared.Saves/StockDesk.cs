// The front desk on LaunchBox's own RetroArch, Dolphin and PCSX2 integrations (lot 4, docs\saves-lot4.md), by Harmony.
//
// LaunchBox asks only the plugin of a game's emulator about its saves; our plugins hold a desk (SaveDesk) that also lists
// and routes the other emulators' saves. A game set to one of LaunchBox's integrations had none. This puts one there.
//
// STEP 5 OF THE PLAN: the hooks are in and only WATCH - every call reaches the original unchanged, what it was handed and
// what it gave back is logged once per kind (the listed rows, Dolphin's ids, which row LaunchBox hands the per-row
// methods). Translation and routing come in the next steps.
//
// Patched ONCE per process (every plugin of the pack carries this class and its own Harmony: a mark in the AppDomain's data
// says it is in), when the integrations are loaded - now, or when they load later. Each of the SDK's save methods is found
// by its exact signature: an integration's override is patched; one it does not override runs the SDK's body
// (EmulatorPlugin), patched once and filtered on the instance's type - every other plugin passes through untouched.
// Measured 10/10 (probe --stock-saves): which each overrides, docs\saves-lot4.md §1.1.

#nullable enable

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Unbroken.LaunchBox.Plugins;
using Unbroken.LaunchBox.Plugins.Data;

namespace LbIntegrations.Saves
{
    internal static class StockDesk
    {
        public const string HarmonyId = "com.nixxou.lbip.stock-desk";
        /// <summary>The once-per-process mark; its value says which format of the hooks is in.</summary>
        private const string Marker = "LbIntegrations.StockDesk.Patched";
        private const string Format = "watch/1";
        private static readonly object Gate = new object();
        private static bool _watching;

        /// <summary>The integrations' types and their emus - the router's rule ("lb." + EmulatorName), measured 10/10.</summary>
        private static readonly (string Assembly, string Type, string Emu)[] Integrations =
        {
            ("Unbroken.LaunchBox.Windows.RetroArch", "RetroArchPlugin", "lb.retroarch"),
            ("Unbroken.LaunchBox.Windows.Dolphin", "DolphinPlugin", "lb.dolphin"),
            ("Unbroken.LaunchBox.Windows.Pcsx2", "Pcsx2Plugin", "lb.pcsx2"),
        };

        /// <summary>The save methods hooked: every one of the SDK's but the two that take nothing naming a row.</summary>
        private static readonly string[] Hooked =
        {
            "GetSaves", "AddSaveFile", "RemoveSave", "IsSaveActive", "IsSaveContainer", "TryBackupSave",
            "UseSaveGroupIdForPersistedMatch", "TryComputeSaveSignature", "TryGetSaveGroupInfo", "IsSecondarySaveFile",
            "GetCompanionSaveFiles",
        };

        /// <summary>The hooked integrations' types → their emu, ONE dictionary for the whole process (in the AppDomain's data):
        /// the SDK's bodies are patched by whichever copy of the pack comes first, and must know the types another copy hooked.</summary>
        private static ConcurrentDictionary<Type, string> Emus
        {
            get
            {
                if (_emus != null) return _emus;
                lock (string.Intern(Marker + "|emus"))
                {
                    if (AppDomain.CurrentDomain.GetData(Marker + "|emus") is not ConcurrentDictionary<Type, string> d)
                        AppDomain.CurrentDomain.SetData(Marker + "|emus", d = new ConcurrentDictionary<Type, string>());
                    return _emus = d;
                }
            }
        }
        private static ConcurrentDictionary<Type, string>? _emus;

        /// <summary>Put the hooks in when the integrations are there, or as soon as they load. From every desk's constructor.</summary>
        public static void Install()
        {
            try
            {
                if (!LbIntegrations.Compat.Compat.On("stockDesk") && !LbIntegrations.Compat.Compat.On("stockIds")) return;
                lock (Gate)
                {
                    foreach (var a in AppDomain.CurrentDomain.GetAssemblies()) TryPatch(a);
                    if (_watching) return;
                    _watching = true;
                    AppDomain.CurrentDomain.AssemblyLoad += OnLoad;
                }
            }
            catch (Exception ex) { SaveRouter.Say("[stock] hooks not installed: " + ex.Message); }
        }

        private static void OnLoad(object? sender, AssemblyLoadEventArgs e)
        {
            try { lock (Gate) TryPatch(e.LoadedAssembly); } catch { }
        }

        private static void TryPatch(Assembly a)
        {
            if (a.IsDynamic) return;
            var name = a.GetName().Name ?? "";
            var entry = Integrations.FirstOrDefault(i => string.Equals(i.Assembly, name, StringComparison.OrdinalIgnoreCase));
            if (entry.Type == null) return;
            var type = a.GetType(entry.Assembly + "." + entry.Type.Replace("Plugin", "") + "." + entry.Type, false)
                       ?? SafeTypes(a).FirstOrDefault(t => t.Name == entry.Type && typeof(EmulatorPlugin).IsAssignableFrom(t));
            if (type == null) { SaveRouter.Say("[stock] " + entry.Type + " not found in " + name + " - not hooked"); return; }

            // Every copy of the pack sees the others through the AppDomain's data and an interned string, never a type.
            lock (string.Intern(Marker + "|" + entry.Emu))
            {
                if (AppDomain.CurrentDomain.GetData(Marker + "|" + entry.Emu) is string other)
                {
                    if (other != Format) SaveRouter.Say("[stock] " + entry.Emu + " already hooked by another copy of the pack (" + other + ")");
                    return;
                }
                // Marked first: whatever happens below, this integration is never hooked twice.
                AppDomain.CurrentDomain.SetData(Marker + "|" + entry.Emu, Format);
                Emus[type] = entry.Emu;
                var h = new Harmony(HarmonyId);
                var said = new List<string>();
                foreach (var m in Hooked)
                {
                    try
                    {
                        var sdk = typeof(EmulatorPlugin).GetMethods(BindingFlags.Public | BindingFlags.Instance).FirstOrDefault(x => x.Name == m && x.IsVirtual);
                        var found = sdk == null ? null : type.GetMethod(m, BindingFlags.Public | BindingFlags.Instance, null, sdk.GetParameters().Select(p => p.ParameterType).ToArray(), null);
                        if (found == null) { said.Add(m + " missing"); continue; }
                        bool own = found.DeclaringType != typeof(EmulatorPlugin);
                        // The SDK's body is patched where it is declared, once for all three; the hooks filter on the instance.
                        var target = own ? found : sdk!;
                        if (!own && !SdkOnce("sdk." + m)) { said.Add(m + " (sdk)"); continue; }
                        // Typed hooks, one pair per method (<m>In, <m>Out): a hook taking object[] __args loses the out values
                        // (measured 10/10: RetroArch's TryGetSaveGroupInfo then gave back an empty id).
                        var pre = AccessTools.Method(typeof(StockDesk), m + "In");
                        var post = AccessTools.Method(typeof(StockDesk), m + "Out");
                        h.Patch(target,
                            prefix: pre == null ? null : new HarmonyMethod(pre) { priority = Priority.First },
                            postfix: post == null ? null : new HarmonyMethod(post) { priority = Priority.Last });
                        said.Add(m + (own ? "" : " (sdk)"));
                    }
                    catch (Exception ex) { said.Add(m + " NOT hooked (" + ex.Message + ")"); }
                }
                SaveRouter.Say("[stock] " + entry.Emu + " watched (" + Format + "): " + string.Join(", ", said));
            }
        }

        /// <summary>True the first time a SDK body is asked for, in the whole process.</summary>
        private static bool SdkOnce(string key)
        {
            lock (string.Intern(Marker + "|" + key))
            {
                if (AppDomain.CurrentDomain.GetData(Marker + "|" + key) != null) return false;
                AppDomain.CurrentDomain.SetData(Marker + "|" + key, Format);
                return true;
            }
        }

        private static IEnumerable<Type> SafeTypes(Assembly a)
        {
            try { return a.GetTypes(); } catch (ReflectionTypeLoadException e) { return e.Types.Where(t => t != null)!; } catch { return Array.Empty<Type>(); }
        }

        /// <summary>The emu of the integration <paramref name="instance"/> is, or null for any other plugin (the SDK's bodies).</summary>
        private static string? EmuOf(object? instance) => instance != null && Emus.TryGetValue(instance.GetType(), out var e) ? e : null;

        // ── step 5: watch only ──────────────────────────────────────────────

        private static readonly ConcurrentDictionary<string, byte> Seen = new(StringComparer.Ordinal);

        /// <summary>Said once per integration, method and row (IsSaveActive is asked hundreds of times per opening).</summary>
        private static void Once(string emu, string method, string what)
        {
            if (Seen.Count < 4000 && Seen.TryAdd(emu + "|" + method + "|" + what, 0)) SaveRouter.Say("[stock] " + emu + " " + method + " " + what);
        }

        private static void Watch(object instance, string method, GameSaveBase? row)
        {
            try { if (EmuOf(instance) is { } emu) Once(emu, method, "<- " + (row == null ? "null" : Row(row))); } catch { }
        }

        private static void WatchFile(object instance, string method, string? file)
        {
            try { if (EmuOf(instance) is { } emu) Once(emu, method, "<- file " + file); } catch { }
        }

        private static void WatchBool(object instance, string method, GameSaveBase? row, bool result)
        {
            try { if (EmuOf(instance) is { } emu) Once(emu, method, "-> " + result + " for " + Show(row)); } catch { }
        }

        private static void GetSavesIn(object __instance, out long __state) => __state = EmuOf(__instance) == null ? 0 : Stopwatch.GetTimestamp();

        private static void GetSavesOut(object __instance, GetSavesArgs __0, GetSavesResponse __result, long __state)
        {
            try
            {
                if (EmuOf(__instance) is not { } emu) return;
                var ms = __state == 0 ? 0 : (Stopwatch.GetTimestamp() - __state) * 1000.0 / Stopwatch.Frequency;
                var rows = __result?.FoundSaves ?? Array.Empty<GameSaveBase>();
                int games = __0?.Games?.Count ?? 0;
                SaveRouter.Say("[stock] " + emu + " GetSaves: " + games + " game(s), " + rows.Count + " row(s), " + ms.ToString("0") + " ms");
                if (games <= 1) foreach (var r in rows) SaveRouter.Say("[stock]    listed " + Row(r));
            }
            catch { }
        }

        private static void AddSaveFileIn(object __instance, AddSaveArgs __0) => Watch(__instance, "AddSaveFile", __0?.SaveToAdd);

        private static void AddSaveFileOut(object __instance, AddSaveResponse __result)
        {
            try { if (EmuOf(__instance) is { } emu) SaveRouter.Say("[stock] " + emu + " AddSaveFile -> " + Show(__result?.SaveAdded) + " success=" + __result?.WasSuccess + " " + __result?.Message); } catch { }
        }

        private static void RemoveSaveIn(object __instance, GameSaveBase __0) => Watch(__instance, "RemoveSave", __0);
        private static void IsSaveActiveIn(object __instance, GameSaveBase __0) => Watch(__instance, "IsSaveActive", __0);
        private static void IsSaveActiveOut(object __instance, GameSaveBase __0, bool __result) => WatchBool(__instance, "IsSaveActive", __0, __result);
        private static void IsSaveContainerIn(object __instance, GameSaveBase __0) => Watch(__instance, "IsSaveContainer", __0);
        private static void TryBackupSaveIn(object __instance, GameSaveBase __0) => Watch(__instance, "TryBackupSave", __0);
        private static void UseSaveGroupIdForPersistedMatchIn(object __instance, GameSaveBase __0) => Watch(__instance, "UseSaveGroupIdForPersistedMatch", __0);
        private static void UseSaveGroupIdForPersistedMatchOut(object __instance, GameSaveBase __0, bool __result) => WatchBool(__instance, "UseSaveGroupIdForPersistedMatch", __0, __result);
        private static void TryComputeSaveSignatureIn(object __instance, GameSaveBase __0) => Watch(__instance, "TryComputeSaveSignature", __0);
        private static void TryGetSaveGroupInfoIn(object __instance, GameSaveBase __0) => Watch(__instance, "TryGetSaveGroupInfo", __0);

        private static void TryGetSaveGroupInfoOut(object __instance, GameSaveBase __0, bool __result, ref string __2, ref string __3)
        {
            try { if (EmuOf(__instance) is { } emu) Once(emu, "TryGetSaveGroupInfo", "-> " + __result + " id=\"" + __2 + "\" name=\"" + __3 + "\" for " + Show(__0)); } catch { }
        }

        private static void IsSecondarySaveFileIn(object __instance, string __0) => WatchFile(__instance, "IsSecondarySaveFile", __0);
        private static void GetCompanionSaveFilesIn(object __instance, string __0) => WatchFile(__instance, "GetCompanionSaveFiles", __0);

        private static string Row(GameSaveBase r)
            => r.GetType().Name + (r is GameSaveState s ? " slot " + s.Slot : "") + " id=\"" + r.SaveGroupId + "\" lineage=\"" + r.MatchLineageId
               + "\" game=" + r.GameId + (string.IsNullOrEmpty(r.AdditionalApplicationId) ? "" : " app=" + r.AdditionalApplicationId)
               + " exe=" + r.EmulatorFileName + " core=" + r.EmulatorCore + " " + r.FileLocation;

        private static string Show(GameSaveBase? r) => r == null ? "null" : "id=\"" + r.SaveGroupId + "\" " + Path.GetFileName(r.FileLocation ?? "");
    }
}
