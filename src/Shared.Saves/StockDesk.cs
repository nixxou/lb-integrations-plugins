// The front desk on LaunchBox's own RetroArch, Dolphin and PCSX2 integrations (lot 4, docs\saves-lot4.md), by Harmony.
//
// LaunchBox asks only the plugin of a game's emulator about its saves; our plugins hold a desk (SaveDesk) that also lists
// and routes the other emulators' saves. A game set to one of LaunchBox's integrations had none. This puts one there.
//
// THEIR IDS IN OUR FORM (lot 4, step 6; StockIds): every row the integration lists, restores or names a group for leaves it
// in our form; every row it is handed in our form reaches its own code in its own form - a copy, LaunchBox's row is never
// touched. The routing of other emulators' rows comes next (step 7).
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
        private const string Format = "ids/1";
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
                        if (pre == null && post == null) { said.Add(m + " (nothing to do yet)"); continue; }
                        h.Patch(target,
                            prefix: pre == null ? null : new HarmonyMethod(pre) { priority = Priority.First },
                            postfix: post == null ? null : new HarmonyMethod(post) { priority = Priority.Last });
                        said.Add(m + (own ? "" : " (sdk)"));
                    }
                    catch (Exception ex) { said.Add(m + " NOT hooked (" + ex.Message + ")"); }
                }
                SaveRouter.Say("[stock] " + entry.Emu + " hooked (" + Format + "): " + string.Join(", ", said));
                SaveRouter.Say("[stock]    " + Where(a) + ", " + Copies(name) + " loaded");
            }
        }

        // ── where the hooks are, and are they still in ───────────────────────────

        private static string Where(Assembly a)
        {
            string ctx;
            try { ctx = System.Runtime.Loader.AssemblyLoadContext.GetLoadContext(a)?.Name ?? "?"; } catch { ctx = "?"; }
            string loc;
            try { loc = a.Location; } catch { loc = "?"; }
            return a.GetName().Name + " [context " + ctx + "] " + loc;
        }

        private static int Copies(string name)
            => AppDomain.CurrentDomain.GetAssemblies().Count(x => !x.IsDynamic && string.Equals(x.GetName().Name, name, StringComparison.OrdinalIgnoreCase));

        /// <summary>The id of the self-check's row - never a real save.</summary>
        private const string Sentinel = "lbip-stock-desk-sentinel";

        /// <summary>Once per process, from the first listing of a desk: are the hooks still in? MEASURED 10/10: another plugin
        /// patching the same methods with its OWN copy of Harmony (the save probe of the sandbox) built its replacement with
        /// its hooks only - ours were gone, silently. A row with the sentinel id handed to RetroArch's (and Dolphin's, PCSX2's)
        /// UseSaveGroupIdForPersistedMatch must come through our hook; said out loud when it does not.</summary>
        public static void Verify()
        {
            lock (string.Intern(Marker + "|verify"))
            {
                if (AppDomain.CurrentDomain.GetData(Marker + "|verified") != null) return;
                AppDomain.CurrentDomain.SetData(Marker + "|verified", true);
            }
            try
            {
                foreach (var (emu, plugin) in SaveRouter.Stock())
                {
                    if (!Integrations.Any(i => i.Emu == emu) || AppDomain.CurrentDomain.GetData(Marker + "|" + emu) == null) continue;
                    AppDomain.CurrentDomain.SetData(Marker + "|seen", null);
                    try { plugin.UseSaveGroupIdForPersistedMatch(new GameSaveGame { SaveGroupId = Sentinel, FileLocation = Sentinel }); } catch { }
                    if (AppDomain.CurrentDomain.GetData(Marker + "|seen") as string == emu) SaveRouter.Say("[stock] " + emu + ": hooks in");
                    else SaveRouter.Say("[stock] " + emu + ": HOOKS LOST - another plugin patched LaunchBox's " + emu + " with its own Harmony "
                                        + "after the pack: its desk and its ids are off for this session");
                }
            }
            catch (Exception ex) { SaveRouter.Say("[stock] self-check failed: " + ex.Message); }
        }

        /// <summary>True (and noted for Verify) when <paramref name="row"/> is the self-check's.</summary>
        private static bool IsSentinel(object instance, GameSaveBase? row)
        {
            if (row?.SaveGroupId != Sentinel) return false;
            AppDomain.CurrentDomain.SetData(Marker + "|seen", EmuOf(instance));
            return true;
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

        // ── the ids: theirs on the way in, ours on the way out ───────────────

        private static readonly ConcurrentDictionary<string, byte> Seen = new(StringComparer.Ordinal);

        /// <summary>Said once per integration and line (IsSaveActive is asked hundreds of times per opening).</summary>
        private static void Once(string emu, string what)
        {
            if (Seen.Count < 4000 && Seen.TryAdd(emu + "|" + what, 0)) SaveRouter.Say("[stock] " + emu + " " + what);
        }

        /// <summary>Our ids given out: the `stockIds` switch, and only to LaunchBox (LiteBox reads their own until lot 8).
        /// Decoding never stops: a card already in our form must keep working whatever is switched.</summary>
        private static bool Encoding => LbIntegrations.Compat.Compat.On("stockIds") && SaveRouter.LaunchBoxHost;

        /// <summary>What a decoded row came in as, for the way out.</summary>
        private struct Inbound
        {
            public string? Json;      // our id it carried, null when it came in native
            public string? Native;    // what the integration was handed
        }

        /// <summary>A row of this integration in our form swapped for a copy in its own (the integrations decide on their own
        /// forms: RetroArch's Saturn match, PCSX2's pcsx2: prefix, measured 10/10). Anything else is left as it came.</summary>
        private static Inbound Decode(string emu, ref GameSaveBase row)
        {
            var seen = new Inbound { Native = row?.SaveGroupId };
            if (row == null || !StockIds.TryDecode(row.SaveGroupId, emu, out var native)) return seen;
            var lineage = StockIds.TryDecode(row.MatchLineageId, emu, out var l) ? l : row.MatchLineageId;
            seen.Json = row.SaveGroupId;
            seen.Native = native;
            row = SaveRows.WithIds(row, native, lineage);
            return seen;
        }

        /// <summary>A row the integration gave out, in our form.</summary>
        private static GameSaveBase Encode(string emu, GameSaveBase row, string? id)
        {
            id ??= StockIds.Encode(emu, row.SaveGroupId, row is GameSaveState, Path.GetFileName(row.FileLocation ?? ""), (row as GameSaveState)?.Slot);
            if (id == null) return row;
            return SaveRows.WithIds(row, id, StockIds.Lineage(row.MatchLineageId, row.SaveGroupId, id));
        }

        // The row methods: decoded on the way in.
        private static bool RowIn(object instance, ref GameSaveBase row)
        {
            try { if (EmuOf(instance) is { } emu) Decode(emu, ref row); } catch { }
            return true;
        }

        private static bool RemoveSaveIn(object __instance, ref GameSaveBase __0) => RowIn(__instance, ref __0);
        private static bool IsSaveActiveIn(object __instance, ref GameSaveBase __0) => RowIn(__instance, ref __0);
        private static bool IsSaveContainerIn(object __instance, ref GameSaveBase __0) => RowIn(__instance, ref __0);
        private static bool TryBackupSaveIn(object __instance, ref GameSaveBase __0) => RowIn(__instance, ref __0);
        private static bool TryComputeSaveSignatureIn(object __instance, ref GameSaveBase __0) => RowIn(__instance, ref __0);

        private static bool UseSaveGroupIdForPersistedMatchIn(object __instance, ref GameSaveBase __0)
        {
            if (IsSentinel(__instance, __0)) return true;
            return RowIn(__instance, ref __0);
        }

        private static bool TryGetSaveGroupInfoIn(object __instance, ref GameSaveBase __0, out Inbound __state)
        {
            __state = default;
            try { if (EmuOf(__instance) is { } emu) __state = Decode(emu, ref __0); } catch { }
            return true;
        }

        /// <summary>The group it names, in our form: the very id that came in when it named that one (RetroArch names a vault
        /// copy's group after its file - "saturn-…-01", measured 10/10 - which stays a group of its own, as today).</summary>
        private static void TryGetSaveGroupInfoOut(object __instance, GameSaveBase __0, bool __result, ref string __2, Inbound __state)
        {
            try
            {
                if (!__result || string.IsNullOrEmpty(__2) || !Encoding || EmuOf(__instance) is not { } emu) return;
                __2 = __state.Json != null && __2 == __state.Native ? __state.Json
                    : StockIds.Encode(emu, __2, __0 is GameSaveState, Path.GetFileName(__0?.FileLocation ?? ""), (__0 as GameSaveState)?.Slot) ?? __2;
            }
            catch { }
        }

        private static bool AddSaveFileIn(object __instance, ref AddSaveArgs __0, out Inbound __state)
        {
            __state = default;
            try
            {
                if (EmuOf(__instance) is not { } emu || __0?.SaveToAdd == null) return true;
                var row = __0.SaveToAdd;
                __state = Decode(emu, ref row);
                if (!ReferenceEquals(row, __0.SaveToAdd)) __0 = new AddSaveArgs { SaveToAdd = row, ShouldOverwriteFunc = __0.ShouldOverwriteFunc };
            }
            catch { }
            return true;
        }

        /// <summary>The row a restore gave back, in our form (StockIds.Result: its card kept, or the slot it was put in).</summary>
        private static void AddSaveFileOut(object __instance, ref AddSaveResponse __result, Inbound __state)
        {
            try
            {
                if (!Encoding || EmuOf(__instance) is not { } emu || __result?.WasSuccess != true || __result.SaveAdded is not { } added) return;
                var id = StockIds.Result(emu, __state.Native, __state.Json, added.SaveGroupId, added is GameSaveState,
                                         Path.GetFileName(added.FileLocation ?? ""), (added as GameSaveState)?.Slot);
                var ours = Encode(emu, added, id);
                if (!ReferenceEquals(ours, added)) __result = new AddSaveResponse(ours);
                Once(emu, "restore -> " + Path.GetFileName(added.FileLocation ?? "") + " " + ours.SaveGroupId);
            }
            catch (Exception ex) { SaveRouter.Say("[stock] restore's row left as it came: " + ex.Message); }
        }

        private static void GetSavesIn(object __instance, out long __state) => __state = EmuOf(__instance) == null ? 0 : Stopwatch.GetTimestamp();

        /// <summary>The rows listed, in our form - each given the id of the card already persisted at its path when there is
        /// one (listed = persisted: a migrated GUID card is never split in two), else its own in our form.</summary>
        private static void GetSavesOut(object __instance, GetSavesArgs __0, ref GetSavesResponse __result, long __state)
        {
            try
            {
                if (EmuOf(__instance) is not { } emu) return;
                var rows = __result?.FoundSaves ?? (IReadOnlyCollection<GameSaveBase>)Array.Empty<GameSaveBase>();
                int games = __0?.Games?.Count ?? 0;
                if (Encoding && __result?.WasSuccess == true && rows.Count > 0)
                {
                    var cards = PersistedCards(__0, emu);
                    var ours = rows.Select(r => r == null ? r! : Encode(emu, r, cards.TryGetValue(CardKey(r.FileLocation, r.AdditionalApplicationId), out var c) ? c : null)).ToList();
                    if (!ours.SequenceEqual(rows)) __result = new GetSavesResponse(ours);
                    rows = ours;
                }
                var ms = __state == 0 ? 0 : (Stopwatch.GetTimestamp() - __state) * 1000.0 / Stopwatch.Frequency;
                SaveRouter.Say("[stock] " + emu + " GetSaves: " + games + " game(s), " + rows.Count + " row(s), " + ms.ToString("0") + " ms");
                if (games <= 1) foreach (var r in rows) SaveRouter.Say("[stock]    listed " + Row(r));
            }
            catch (Exception ex) { SaveRouter.Say("[stock] rows left as listed: " + ex.Message); }
        }

        /// <summary>The cards of this integration already in our form, persisted on the listed games (and their versions):
        /// path and version → id.</summary>
        private static Dictionary<string, string> PersistedCards(GetSavesArgs? args, string emu)
        {
            var cards = new Dictionary<string, string>(StringComparer.Ordinal);
            var games = new List<IGame>();
            if (args?.Games != null) games.AddRange(args.Games.Where(g => g != null));
            foreach (var a in args?.AdditionalApplications ?? (IReadOnlyCollection<IAdditionalApplication>)Array.Empty<IAdditionalApplication>())
                if (a != null && games.All(g => g.Id != a.GameId) && PluginHelper.DataManager?.GetGameById(a.GameId) is { } g) games.Add(g);
            foreach (var g in games)
                foreach (var p in StockIdMigration.Read(g))
                    if (p.SaveGroupId != null && StockIds.TryDecode(p.SaveGroupId, emu, out _))
                        cards[CardKey(p.FilePath, p.AppId)] = p.SaveGroupId;
            return cards;
        }

        private static string CardKey(string? path, string? appId)
        {
            string full;
            try
            {
                var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, ".."));
                full = string.IsNullOrWhiteSpace(path) ? "" : Path.GetFullPath(Path.IsPathRooted(path) ? path : Path.Combine(root, path));
            }
            catch { full = path ?? ""; }
            return full.ToLowerInvariant() + "|" + (appId ?? "").ToLowerInvariant();
        }

        private static string Row(GameSaveBase r)
            => r.GetType().Name + (r is GameSaveState s ? " slot " + s.Slot : "") + " id=\"" + r.SaveGroupId + "\" lineage=\"" + r.MatchLineageId
               + "\" game=" + r.GameId + (string.IsNullOrEmpty(r.AdditionalApplicationId) ? "" : " app=" + r.AdditionalApplicationId)
               + " exe=" + r.EmulatorFileName + " core=" + r.EmulatorCore + " " + r.FileLocation;

        private static string Show(GameSaveBase? r) => r == null ? "null" : "id=\"" + r.SaveGroupId + "\" " + Path.GetFileName(r.FileLocation ?? "");
    }
}
