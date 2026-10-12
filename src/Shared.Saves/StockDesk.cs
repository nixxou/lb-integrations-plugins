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
            // Lot 5 (docs\saves-lot5.md): the ones without save support, measured 12/10 - none overrides a save method.
            ("Unbroken.LaunchBox.Windows.BigPEmu", "BigPEmuPlugin", "lb.bigpemu"),
            ("Unbroken.LaunchBox.Windows.Cemu", "CemuPlugin", "lb.cemu"),
            ("Unbroken.LaunchBox.Windows.DuckStation", "DuckStationPlugin", "lb.duckstation"),
            ("Unbroken.LaunchBox.Windows.Mame", "MamePlugin", "lb.mame"),
            ("Unbroken.LaunchBox.Windows.Rpcs3", "Rpcs3Plugin", "lb.rpcs3"),
            ("Unbroken.LaunchBox.Windows.ScummVm", "ScummVmPlugin", "lb.scummvm"),
            ("Unbroken.LaunchBox.Windows.Xemu", "XemuPlugin", "lb.xemu"),
        };

        /// <summary>The save methods hooked: every one of the SDK's but GetPotentialSaveSlots. SupportsSaveManagement only on
        /// an integration without save support (lot 5) - lot 4's three keep their own answer, untouched.</summary>
        private static readonly string[] Hooked =
        {
            "GetSaves", "AddSaveFile", "RemoveSave", "IsSaveActive", "IsSaveContainer", "TryBackupSave",
            "UseSaveGroupIdForPersistedMatch", "TryComputeSaveSignature", "TryGetSaveGroupInfo", "IsSecondarySaveFile",
            "GetCompanionSaveFiles", "SupportsSaveManagement",
        };

        /// <summary>The SDK's 13 save methods: an integration overriding none has no save support (lot 5).</summary>
        private static readonly string[] SaveApi =
        {
            "SupportsSaveManagement", "GetSaves", "AddSaveFile", "RemoveSave", "IsSaveActive", "IsSaveContainer", "TryBackupSave",
            "UseSaveGroupIdForPersistedMatch", "TryComputeSaveSignature", "TryGetSaveGroupInfo", "IsSecondarySaveFile",
            "GetCompanionSaveFiles", "GetPotentialSaveSlots",
        };

        /// <summary>An integration of LaunchBox without save support (none of the SDK's save methods its own), as hooked in
        /// this process: it lists, backs up and restores only the other emulators' saves of its games (lot 5).</summary>
        internal static bool NoSave(string? emu) => emu != null && AppDomain.CurrentDomain.GetData(Marker + "|nosave|" + emu) is true;

        /// <summary>A no-save integration standing as a desk (lot 5): the switches on, its hooks proven, under LaunchBox's desk.
        /// Its rows are all other emulators'; one nobody claims is never handed to an SDK body that acts on files.</summary>
        internal static bool Desk(string? emu) => NoSave(emu) && LbIntegrations.Compat.Compat.On("stockDesk")
                                                  && LbIntegrations.Compat.Compat.On("stockNoSaves") && Complete(emu!) && SaveRouter.Active;

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
                // Under LiteBox, whatever is switched (Mehdi, 13/10): the platform files LaunchBox wrote may hold these integrations'
                // ids in our form - a safe mode turned on in LaunchBox gives them back only for the games LaunchBox saves after.
                // LiteBox hands them to the integrations as they are, and PCSX2 decides by its pcsx2: prefix, RetroArch's Saturn
                // match by its saturn- one: they must be decoded. Our ids are never given out there (Encoding), and the desk's
                // hooks check their own switches.
                if (SaveRouter.LaunchBoxHost && !LbIntegrations.Compat.Compat.On("stockDesk") && !LbIntegrations.Compat.Compat.On("stockIds")) return;
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
            try
            {
                lock (Gate) TryPatch(e.LoadedAssembly);
                // A LaunchBox integration loaded after the router listed them: listed again, so it is checked and routed
                // (review 10/10: the list was made once, a late one never verified).
                string? at = null;
                try { at = e.LoadedAssembly.IsDynamic ? null : e.LoadedAssembly.Location; } catch { }
                if (at != null && at.IndexOf(@"\System\Plugins\", StringComparison.OrdinalIgnoreCase) >= 0) SaveRouter.ForgetStock();
            }
            catch { }
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
                    // A SECOND copy of the integration's dll: its type is not hooked, and LaunchBox may hold that one -
                    // it would see our ids undecoded (review 10/10). Its own ids for the session.
                    else if (!Emus.ContainsKey(type))
                    {
                        Incomplete(entry.Emu, "a second copy of " + name + " loaded (" + Where(a) + ") - only the first is hooked");
                        SaveRouter.Say("[stock] " + entry.Emu + ": a second copy of its dll loaded - its desk is off, its cards get their own ids back");
                    }
                    return;
                }
                // Marked first: whatever happens below, this integration is never hooked twice.
                AppDomain.CurrentDomain.SetData(Marker + "|" + entry.Emu, Format);
                Emus[type] = entry.Emu;
                // Without save support when it overrides none of the SDK's save methods (lot 5, the seven measured 12/10).
                bool noSave = SaveApi.All(m =>
                {
                    var b = typeof(EmulatorPlugin).GetMethods(BindingFlags.Public | BindingFlags.Instance).FirstOrDefault(x => x.Name == m && x.IsVirtual);
                    var own = b == null ? null : type.GetMethod(m, BindingFlags.Public | BindingFlags.Instance, null, b.GetParameters().Select(p => p.ParameterType).ToArray(), null);
                    return own == null || own.DeclaringType == typeof(EmulatorPlugin);
                });
                if (noSave) AppDomain.CurrentDomain.SetData(Marker + "|nosave|" + entry.Emu, true);
                var h = new Harmony(HarmonyId);
                var said = new List<string> { noSave ? "no save support of its own" : "saves of its own" };
                foreach (var m in Hooked)
                {
                    // Lot 4's three keep their own SupportsSaveManagement - only the ones without saves have theirs answered.
                    if (m == "SupportsSaveManagement" && !noSave) continue;
                    try
                    {
                        var sdk = typeof(EmulatorPlugin).GetMethods(BindingFlags.Public | BindingFlags.Instance).FirstOrDefault(x => x.Name == m && x.IsVirtual);
                        var found = sdk == null ? null : type.GetMethod(m, BindingFlags.Public | BindingFlags.Instance, null, sdk.GetParameters().Select(p => p.ParameterType).ToArray(), null);
                        if (found == null) { said.Add(m + " missing"); Incomplete(entry.Emu, m + " missing"); continue; }
                        bool own = found.DeclaringType != typeof(EmulatorPlugin);
                        // The SDK's body is patched where it is declared, once for all three; the hooks filter on the instance.
                        var target = own ? found : sdk!;
                        if (!own && !SdkOnce("sdk." + m))
                        {
                            // Patched (or failed) for another integration: a failure there is this one's too.
                            if (AppDomain.CurrentDomain.GetData(Marker + "|sdkfail|" + m) is string why) { said.Add(m + " (sdk) NOT hooked"); Incomplete(entry.Emu, m + ": " + why); }
                            else said.Add(m + " (sdk)");
                            continue;
                        }
                        // Typed hooks, one pair per method (<m>In, <m>Out): a hook taking object[] __args loses the out values
                        // (measured 10/10: RetroArch's TryGetSaveGroupInfo then gave back an empty id).
                        var pre = AccessTools.Method(typeof(StockDesk), m + "In");
                        var post = AccessTools.Method(typeof(StockDesk), m + "Out");
                        if (pre == null && post == null) { said.Add(m + " (nothing to do yet)"); continue; }
                        try
                        {
                            if (FailForTests?.Invoke(entry.Emu, m) == true) throw new InvalidOperationException("failure injected by the probe");
                            h.Patch(target,
                                prefix: pre == null ? null : new HarmonyMethod(pre) { priority = Priority.First },
                                postfix: post == null ? null : new HarmonyMethod(post) { priority = Priority.Last });
                        }
                        catch (Exception ex)
                        {
                            if (!own) AppDomain.CurrentDomain.SetData(Marker + "|sdkfail|" + m, ex.Message);
                            throw;
                        }
                        said.Add(m + (own ? "" : " (sdk)"));
                    }
                    catch (Exception ex) { said.Add(m + " NOT hooked (" + ex.Message + ")"); Incomplete(entry.Emu, m + ": " + ex.Message); }
                }
                SaveRouter.Say("[stock] " + entry.Emu + " hooked (" + Format + "): " + string.Join(", ", said));
                if (IncompleteWhy(entry.Emu) is string off)
                    SaveRouter.Say("[stock] " + entry.Emu + ": NOT every row method hooked (" + off + ") - its desk is off and its cards keep "
                                   + "or get back their own ids this session");
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


        /// <summary>Are the hooks still in? MEASURED 10/10: another plugin patching the same methods with its OWN copy of Harmony
        /// (the save probe of the sandbox) built its replacement with its hooks only - ours were gone, silently. A sentinel row
        /// is handed to every row method that decodes an id (Codex review 10/10: one method only proved nothing about the
        /// others); each hook notes it and returns at once - the integration's own code never runs on it (its file does not
        /// exist either, should a hook be gone). At the first listing, then before each one-game listing (Game Saves): a
        /// plugin patching later is caught before that game's cards are used, and they get their own ids back.
        /// An integration hooked but not yet checked (loaded after the first listing) is checked at the next listing of any
        /// kind: until then it is not Complete - only a check that passed makes it so (Codex review 10/10).</summary>
        public static void Verify(bool again = false)
        {
            lock (string.Intern(Marker + "|verify"))
            try
            {
                var file = Path.Combine(Path.GetTempPath(), "lbip-stock-desk-sentinel-never-there", "sentinel.sav");
                foreach (var (emu, plugin) in SaveRouter.Stock())
                {
                    // Hooked by this format of the pack, nothing failed: checked once, then again before each Game Saves.
                    if (!Integrations.Any(i => i.Emu == emu) || AppDomain.CurrentDomain.GetData(Marker + "|" + emu) as string != Format
                        || IncompleteWhy(emu) != null) continue;
                    bool was = Verified(emu);
                    if (was && !again) continue;
                    var row = new GameSaveGame { SaveGroupId = Sentinel, FileLocation = file };
                    var calls = new (string Method, Action Call)[]
                    {
                        ("AddSaveFile", () => plugin.AddSaveFile(new AddSaveArgs { SaveToAdd = row })),
                        ("RemoveSave", () => plugin.RemoveSave(row)),
                        ("IsSaveActive", () => plugin.IsSaveActive(row, "")),
                        ("IsSaveContainer", () => plugin.IsSaveContainer(row)),
                        ("TryBackupSave", () => plugin.TryBackupSave(row, "", "", out _)),
                        ("UseSaveGroupIdForPersistedMatch", () => plugin.UseSaveGroupIdForPersistedMatch(row)),
                        ("TryComputeSaveSignature", () => plugin.TryComputeSaveSignature(row, "", out _)),
                        ("TryGetSaveGroupInfo", () => plugin.TryGetSaveGroupInfo(row, file, out _, out _)),
                    };
                    var lost = new List<string>();
                    foreach (var (method, call) in calls)
                    {
                        AppDomain.CurrentDomain.SetData(Marker + "|seen", null);
                        try { call(); } catch { }
                        if (AppDomain.CurrentDomain.GetData(Marker + "|seen") as string != emu + "|" + method) lost.Add(method);
                    }
                    if (lost.Count == 0)
                    {
                        if (!was) { AppDomain.CurrentDomain.SetData(Marker + "|ok|" + emu, Format); SaveRouter.Say("[stock] " + emu + ": hooks in"); }
                        continue;
                    }
                    AppDomain.CurrentDomain.SetData(Marker + "|ok|" + emu, null);
                    Incomplete(emu,"hooks lost on " + string.Join(", ", lost) + " - another Harmony patched it after the pack");
                    SaveRouter.Say("[stock] " + emu + ": HOOKS LOST on " + string.Join(", ", lost) + " - another plugin patched LaunchBox's "
                                   + emu + " with its own Harmony after the pack: its desk is off, the cards get their own ids back");
                }
            }
            catch (Exception ex) { SaveRouter.Say("[stock] self-check failed: " + ex.Message); }
        }

        /// <summary>True (and noted for Verify) when <paramref name="row"/> is the self-check's: the hook returns at once.</summary>
        private static bool IsSentinel(object instance, GameSaveBase? row, [System.Runtime.CompilerServices.CallerMemberName] string hook = "")
        {
            if (row?.SaveGroupId != Sentinel) return false;
            var name = hook.EndsWith("In", StringComparison.Ordinal) ? hook.Substring(0, hook.Length - 2) : hook;
            if (LostForTests?.Invoke(EmuOf(instance) ?? "", name) == true) return false;   // as if this hook were gone
            AppDomain.CurrentDomain.SetData(Marker + "|seen", EmuOf(instance) + "|" + (hook.EndsWith("In", StringComparison.Ordinal) ? hook.Substring(0, hook.Length - 2) : hook));
            return true;
        }

        /// <summary>The probe's stand-in for another Harmony: the hooks of these (emu, method) behave as if they were gone.</summary>
        internal static Func<string, string, bool>? LostForTests;

        /// <summary>The probe's failure injection: true for an (emu, method) whose patch must fail.</summary>
        internal static Func<string, string, bool>? FailForTests;

        /// <summary>An integration whose row methods are not all hooked (a method gone, a patch refused, another Harmony over
        /// ours): its own code would see our ids undecoded (Codex review 10/10: PCSX2 decides by its pcsx2: prefix). Its desk
        /// is off and its cards keep - or get back - their own ids, for the session; the others go on. Process-wide.</summary>
        internal static void Incomplete(string emu, string why)
        {
            lock (string.Intern(Marker + "|off"))
                AppDomain.CurrentDomain.SetData(Marker + "|off|" + emu, AppDomain.CurrentDomain.GetData(Marker + "|off|" + emu) is string was ? was + "; " + why : why);
        }

        internal static string? IncompleteWhy(string? emu) => emu == null ? null : AppDomain.CurrentDomain.GetData(Marker + "|off|" + emu) as string;

        /// <summary>This integration's rows in our form (and its desk on): hooked by this pack, every row method, and the
        /// self-check passed. Anything short of that proof - not found, not loaded yet, not checked yet, hooked by another
        /// format of the pack - is NOT complete: its cards keep, or get back, their own ids (Codex review 10/10: the
        /// migration names pcsx2:/saturn- owners without any hook, an integration never hooked must not get our ids).</summary>
        internal static bool Complete(string? emu) => emu != null && IncompleteWhy(emu) == null && Verified(emu);

        private static bool Verified(string emu) => AppDomain.CurrentDomain.GetData(Marker + "|ok|" + emu) as string == Format;

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
        private static bool Encoding(string? emu) => LbIntegrations.Compat.Compat.On("stockIds") && SaveRouter.LaunchBoxHost && Complete(emu);

        /// <summary>What a decoded row came in as, for the way out.</summary>
        private struct Inbound
        {
            public string? Json;      // our id it carried, null when it came in native
            public string? Native;    // what the integration was handed
            // Answered by the prefix (another emulator's row, the self-check): the integration never ran, its postfix must
            // leave the answer as its owner gave it (Harmony runs postfixes after a skipped original - Codex review 10/10).
            public bool Forwarded;
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
            id ??= StockIds.Encode(emu, row.SaveGroupId, row is GameSaveState, Path.GetFileName(row.FileLocation ?? ""), (row as GameSaveState)?.Slot, DirOf(row.FileLocation));
            if (id == null) return row;
            return SaveRows.WithIds(row, id, StockIds.Lineage(row.MatchLineageId, row.SaveGroupId, id));
        }

        /// <summary>The folder a by-name id holds (StockIds.DirOf), against LaunchBox's root.</summary>
        internal static string? DirOf(string? fileLocation)
            => StockIds.DirOf(fileLocation, Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..")));

        // ── the row methods: another emulator's row to its owner (step 7), the integration's own decoded ──

        /// <summary>The owner of a row that is NOT this integration's (a pack plugin's, another integration's) - LaunchBox asks
        /// the game's emulator about every persisted row of the game (measured 10/10: RetroArch about Zelda's ares and SUPER
        /// ZSNES rows). Null for its own rows, for any other plugin (the SDK's bodies), and with the desk off.</summary>
        private static SaveOwnerRoute? Foreign(object instance, GameSaveBase? row, out string? emu)
        {
            emu = EmuOf(instance);
            if (emu == null || row == null || !LbIntegrations.Compat.Compat.On("stockDesk") || !Complete(emu)) return null;
            try { return SaveRouter.OwnerForStock(row, emu); } catch { return null; }
        }

        /// <summary>The prefix of a row method: true (the original runs, on the row in its own form) or false with
        /// <paramref name="result"/> answered by the row's owner.</summary>
        private static bool RowIn<T>(object instance, ref GameSaveBase row, ref T result, Func<SaveOwnerRoute, GameSaveBase, T> forward,
                                     [System.Runtime.CompilerServices.CallerMemberName] string method = "")
        {
            if (IsSentinel(instance, row, method)) return false;
            try
            {
                if (Foreign(instance, row, out var emu) is { } r)
                {
                    result = forward(r, row);
                    Once(emu!, method + " " + Show(row) + " -> " + r.Emu + " answered " + result);
                    return false;
                }
                if (emu != null)
                {
                    var came = row?.SaveGroupId;
                    Decode(emu, ref row);
                    Once(emu, method + " own, id \"" + came + "\" handed as \"" + row?.SaveGroupId + "\" " + Path.GetFileName(row?.FileLocation ?? ""));
                }
            }
            catch (Exception ex) { SaveRouter.Say("[stock] left to the integration: " + ex.Message); }
            return true;
        }

        // What the integration answered for its own rows (once per method, id and answer) - the backups' trail.
        private static void Answered(object instance, string method, GameSaveBase? row, object? result, string? extra = null)
        {
            try { if (row?.SaveGroupId != Sentinel && EmuOf(instance) is { } emu) Once(emu, method + " -> " + result + (extra == null ? "" : " " + extra) + " for " + Show(row)); } catch { }
        }

        private static void TryBackupSaveOut(object __instance, GameSaveBase __0, bool __result, ref string __3) => Answered(__instance, "TryBackupSave", __0, __result, "error=\"" + __3 + "\"");
        private static void TryComputeSaveSignatureOut(object __instance, GameSaveBase __0, bool __result, ref string __2) => Answered(__instance, "TryComputeSaveSignature", __0, __result, "signature=" + __2);
        private static void IsSaveContainerOut(object __instance, GameSaveBase __0, bool __result) => Answered(__instance, "IsSaveContainer", __0, __result);
        private static void IsSaveActiveOut(object __instance, GameSaveBase __0, bool __result) => Answered(__instance, "IsSaveActive", __0, __result);
        private static void UseSaveGroupIdForPersistedMatchOut(object __instance, GameSaveBase __0, bool __result) => Answered(__instance, "UseSaveGroupIdForPersistedMatch", __0, __result);
        private static void RemoveSaveOut(object __instance, GameSaveBase __0, PluginResponse __result) => Answered(__instance, "RemoveSave", __0, __result?.WasSuccess + " " + __result?.Message);

        private static bool RemoveSaveIn(object __instance, ref GameSaveBase __0, ref PluginResponse __result)
        {
            if (!RowIn(__instance, ref __0, ref __result, SaveForward.RemoveSave)) return false;
            // A no-save integration's row nobody claims: the SDK's body deletes its file - only when the user says yes (lot 5,
            // Mehdi 12/10). Not tied to `stockNoSaves`: with it off, LaunchBox still shows the persisted rows (T3).
            try
            {
                if (EmuOf(__instance) is { } emu && NoSave(emu) && LbIntegrations.Compat.Compat.On("stockDesk") && Complete(emu)
                    && !SlotRestore.MayDeleteUnclaimed(__0?.FileLocation, m => SaveRouter.Say("[stock] " + emu + " " + m), out var no))
                {
                    __result = new PluginResponse(false, no);
                    return false;
                }
            }
            catch { }
            return true;
        }
        private static bool IsSaveActiveIn(object __instance, ref GameSaveBase __0, string __1, ref bool __result)
            => RowIn(__instance, ref __0, ref __result, (r, s) => SaveForward.IsSaveActive(r, s, __1));
        private static bool IsSaveContainerIn(object __instance, ref GameSaveBase __0, ref bool __result)
            => RowIn(__instance, ref __0, ref __result, SaveForward.IsSaveContainer);

        private static bool TryBackupSaveIn(object __instance, ref GameSaveBase __0, string __1, string __2, ref string __3, ref bool __result)
        {
            string? error = null;
            var run = RowIn(__instance, ref __0, ref __result, (r, s) => SaveForward.TryBackupSave(r, s, __1, __2, out error));
            if (!run) __3 = error!;
            return run;
        }

        private static bool TryComputeSaveSignatureIn(object __instance, ref GameSaveBase __0, string __1, ref string __2, ref bool __result)
        {
            string? signature = null;
            var run = RowIn(__instance, ref __0, ref __result, (r, s) => SaveForward.TryComputeSaveSignature(r, s, __1, out signature));
            if (!run) __2 = signature!;
            return run;
        }

        private static bool UseSaveGroupIdForPersistedMatchIn(object __instance, ref GameSaveBase __0, ref bool __result)
        {
            return RowIn(__instance, ref __0, ref __result, SaveForward.UseSaveGroupIdForPersistedMatch);
        }

        /// <summary>Another emulator's row: it names its own group (Plan A - asked in the tile's constructor, left unanswered
        /// LaunchBox ties the row to the card of a backup with the same file name, measured 08/10), else its owner answers.</summary>
        private static bool TryGetSaveGroupInfoIn(object __instance, ref GameSaveBase __0, string __1, ref string __2, ref string __3, ref bool __result, out Inbound __state)
        {
            __state = default;
            if (IsSentinel(__instance, __0)) { __state.Forwarded = true; return false; }
            try
            {
                if (Foreign(__instance, __0, out var emu) is { } r)
                {
                    __state.Forwarded = true;
                    if (!string.IsNullOrEmpty(__0.SaveGroupId) && LbIntegrations.Compat.Compat.On("planA")) { __2 = __0.SaveGroupId; __3 = __0.SaveGroupName; __result = true; return false; }
                    __result = SaveForward.TryGetSaveGroupInfo(r, __0, __1, out var id, out var name);
                    __2 = id!; __3 = name!;
                    return false;
                }
                if (emu != null) __state = Decode(emu, ref __0);
            }
            catch { }
            return true;
        }

        // A file (companions, secondary files): the plugin whose folder holds it, or that listed it here.
        private static bool IsSecondarySaveFileIn(object __instance, string __0, ref bool __result)
        {
            try
            {
                if (EmuOf(__instance) is { } emu && LbIntegrations.Compat.Compat.On("stockDesk") && Complete(emu) && SaveRouter.OwnerOfPath(__0, (EmulatorPlugin)__instance, emu) is { } r)
                { __result = SaveForward.IsSecondarySaveFile(r, __0); return false; }
            }
            catch { }
            return true;
        }

        private static bool GetCompanionSaveFilesIn(object __instance, string __0, ref IReadOnlyList<string> __result)
        {
            try
            {
                if (EmuOf(__instance) is { } emu && LbIntegrations.Compat.Compat.On("stockDesk") && Complete(emu) && SaveRouter.OwnerOfPath(__0, (EmulatorPlugin)__instance, emu) is { } r)
                { __result = SaveForward.GetCompanionSaveFiles(r, __0); return false; }
            }
            catch { }
            return true;
        }

        /// <summary>The group it names, in our form: the very id that came in when it named that one (RetroArch names a vault
        /// copy's group after its file - "saturn-…-01", measured 10/10 - which stays a group of its own, as today).</summary>
        private static void TryGetSaveGroupInfoOut(object __instance, GameSaveBase __0, bool __result, ref string __2, Inbound __state)
        {
            try
            {
                if (__state.Forwarded) return;   // its owner's answer (or the row's own id, Plan A), as given
                var given = __2;
                if (!__result || string.IsNullOrEmpty(__2) || EmuOf(__instance) is not { } emu || !Encoding(emu)) { Answered(__instance, "TryGetSaveGroupInfo", __0, __result, "id=\"" + given + "\""); return; }
                __2 = __state.Json != null && __2 == __state.Native ? __state.Json
                    : StockIds.Encode(emu, __2, __0 is GameSaveState, Path.GetFileName(__0?.FileLocation ?? ""), (__0 as GameSaveState)?.Slot, DirOf(__0?.FileLocation)) ?? __2;
                Answered(__instance, "TryGetSaveGroupInfo", __0, __result, "id=\"" + given + "\" given as \"" + __2 + "\"");
            }
            catch { }
        }

        private static void GetCompanionSaveFilesOut(object __instance, string __0, IReadOnlyList<string> __result)
        {
            try { if (EmuOf(__instance) is { } emu) Once(emu, "GetCompanionSaveFiles " + Path.GetFileName(__0 ?? "") + " -> " + string.Join(", ", (__result ?? Array.Empty<string>()).Select(Path.GetFileName))); } catch { }
        }

        private static void IsSecondarySaveFileOut(object __instance, string __0, bool __result)
        {
            try { if (EmuOf(__instance) is { } emu) Once(emu, "IsSecondarySaveFile " + Path.GetFileName(__0 ?? "") + " -> " + __result); } catch { }
        }

        private static bool AddSaveFileIn(object __instance, ref AddSaveArgs __0, ref AddSaveResponse __result, out Inbound __state)
        {
            __state = default;
            if (IsSentinel(__instance, __0?.SaveToAdd)) { __result = new AddSaveResponse("self-check"); __state.Forwarded = true; return false; }
            try
            {
                // Another emulator's save restored from this game's card: by its owner, into its owner's folders.
                if (Foreign(__instance, __0?.SaveToAdd, out _) is { } r)
                {
                    __state.Forwarded = true;
                    __result = SaveForward.AddSaveFile(r, __0!, m => SaveRouter.Log?.Invoke(m));
                    return false;
                }
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
                // A restore its owner made: the row stays in the owner's form (a native pcsx2: from an incomplete PCSX2
                // must not come back as lb.retroarch's).
                if (__state.Forwarded) return;
                if (EmuOf(__instance) is not { } emu || !Encoding(emu) || __result?.WasSuccess != true || __result.SaveAdded is not { } added) return;
                var id = StockIds.Result(emu, __state.Native, __state.Json, added.SaveGroupId, added is GameSaveState,
                                         Path.GetFileName(added.FileLocation ?? ""), (added as GameSaveState)?.Slot, DirOf(added.FileLocation));
                var ours = Encode(emu, added, id);
                if (!ReferenceEquals(ours, added)) __result = new AddSaveResponse(ours);
                Once(emu, "restore -> " + Path.GetFileName(added.FileLocation ?? "") + " " + ours.SaveGroupId);
            }
            catch (Exception ex) { SaveRouter.Say("[stock] restore's row left as it came: " + ex.Message); }
        }

        /// <summary>Lot 5, decision D6: an integration without save support says it has some as soon as another plugin with
        /// saves - one of ours or LaunchBox's - covers a platform its emulators cover (its own answer takes no platform: over
        /// all of them). Then LaunchBox shows Game Saves on its games and asks it about saves; the desk answers with the other
        /// emulators' (GetSaves). Otherwise, or a switch off, or its hooks not proven: the SDK's false.</summary>
        private static bool SupportsSaveManagementIn(object __instance, ref bool __result)
        {
            try
            {
                if (EmuOf(__instance) is not { } emu || !NoSave(emu)) return true;
                // Every answer said once (asked often): when LaunchBox asks, and why the answer is no (measured in LaunchBox).
                string? no = !LbIntegrations.Compat.Compat.On("stockDesk") || !LbIntegrations.Compat.Compat.On("stockNoSaves") ? "a switch is off"
                           : !Complete(emu) ? "its hooks are not proven" : !SaveRouter.Active ? "not under LaunchBox's desk"
                           : !SaveRouter.OthersCoverPlatformsOf(emu, NoSave) ? "no plugin with saves covers its platforms" : null;
                Once(emu, "SupportsSaveManagement asked: " + (no == null ? "true - another plugin with saves covers one of its platforms (lot 5)" : "false - " + no));
                if (no != null) return true;
                __result = true;
                return false;
            }
            catch { return true; }
        }

        private static void GetSavesIn(object __instance, GetSavesArgs __0, out long __state)
        {
            __state = EmuOf(__instance) == null ? 0 : Stopwatch.GetTimestamp();
            if (__state != 0) EnsureIds(__0);
        }

        /// <summary>The persisted cards of LaunchBox's integrations in the form `stockIds` says (step 8, StockIdMigration),
        /// BEFORE a listing is matched against them: the whole library once per process, at the first listing of a desk or
        /// of an integration (LaunchBox's start-up scan), then the games of each one-game listing (Game Saves) - games added
        /// since. Back to their own ids when the switch is off. Not under LiteBox (lot 8).</summary>
        public static void EnsureIds(GetSavesArgs? args)
        {
            try
            {
                if (!SaveRouter.LaunchBoxHost) return;
                // Every decoding hook still in, at the first listing and before each one-game listing (Game Saves).
                Verify(again: args?.Games?.Count == 1);
                // Our ids only for an integration whose every row method decodes them (Codex review 10/10): one not fully
                // hooked - a method gone, a patch refused, another Harmony over ours - gets its cards' own ids back.
                bool on = LbIntegrations.Compat.Compat.On("stockIds");
                // The ones without saves have no cards of their own: never migrated (lot 5).
                bool Ours(string? emu) => on && Complete(emu) && !NoSave(emu);
                // The whole library again whenever the integrations in our form change: one hooked and checked after the
                // first pass (its assembly loaded late), one whose hooks were lost, the switch turned.
                var ready = string.Join(",", Integrations.Select(i => i.Emu).Where(e => Ours(e)));
                bool whole;
                lock (string.Intern(Marker + "|ids"))
                {
                    whole = AppDomain.CurrentDomain.GetData(Marker + "|ids") as string != ready;
                    if (whole) AppDomain.CurrentDomain.SetData(Marker + "|ids", ready);
                }
                IEnumerable<IGame>? games = whole ? null : args?.Games?.Count == 1 ? args.Games : null;
                if (!whole && games == null) return;
                if (whole) SaveRouter.Say("[stock] ids: whole library, our form for: " + (ready.Length == 0 ? "none" : ready));
                StockIdMigration.Run(games, true, m => SaveRouter.Say("[stock] " + m), e => Ours(e));
                StockIdMigration.Run(games, false, m => SaveRouter.Say("[stock] " + m), e => !Ours(e));
            }
            catch (Exception ex) { SaveRouter.Say("[stock] ids not put in step: " + ex.Message); }
        }

        /// <summary>The rows listed, in our form - each given the id of the card already persisted at its path when there is
        /// one (listed = persisted: a migrated GUID card is never split in two), else its own in our form.</summary>
        private static void GetSavesOut(object __instance, GetSavesArgs __0, ref GetSavesResponse __result, long __state)
        {
            try
            {
                if (EmuOf(__instance) is not { } emu) return;
                var rows = __result?.FoundSaves ?? (IReadOnlyCollection<GameSaveBase>)Array.Empty<GameSaveBase>();
                int games = __0?.Games?.Count ?? 0;
                if (Encoding(emu) && __result?.WasSuccess == true && rows.Count > 0)
                {
                    var cards = PersistedCards(__0, emu);
                    var ours = rows.Select(r => r == null ? r! : Encode(emu, r, PickCard(r.SaveGroupId, cards.TryGetValue(CardKey(r.GameId, r.FileLocation, r.AdditionalApplicationId), out var c) ? c : null))).ToList();
                    // What the lookup found, for a one-game listing (Game Saves; measured 11/10: the second listing hands a
                    // working copy of the game, its Saves empty - the cards are read on the DataManager's).
                    if (games == 1)
                    {
                        var g0 = __0!.Games!.First();
                        int Count(object? o) { try { return (o?.GetType().GetProperty("Saves", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(o) as System.Collections.IEnumerable)?.Cast<object>().Count() ?? -1; } catch { return -1; } }
                        var handed = SaveGameView.Real(g0);
                        object? held = null;
                        try { held = PluginHelper.DataManager?.GetGameById(handed.Id); } catch { }
                        SaveRouter.Say("[stock]    cards: game handed " + Count(handed) + " persisted row(s), the data's " + Count(held)
                                       + (ReferenceEquals(handed, held) ? " (the same object)" : " (another object)") + "; "
                                       + cards.Count + " path(s) in " + emu + "'s form; per row: "
                                       + string.Join(", ", rows.Select(r => Path.GetFileName(r?.FileLocation ?? "") + "=" + (cards.TryGetValue(CardKey(r?.GameId, r?.FileLocation, r?.AdditionalApplicationId), out var cc) ? cc.Count : 0))));
                    }
                    if (!ours.SequenceEqual(rows)) __result = new GetSavesResponse(ours);
                    rows = ours;
                }
                // Lot 5: an integration without saves of its own answers the SDK's failure ("not supported") - when it says it
                // supports saves (SupportsSaveManagementIn), its listing is the other emulators' saves, a success even empty.
                if (Desk(emu) && __result?.WasSuccess != true) { rows = Array.Empty<GameSaveBase>(); __result = new GetSavesResponse(new List<GameSaveBase>()); }
                // The other emulators' saves of these games (step 7), as the pack's desks list them. Nothing under LiteBox
                // (SaveRouter.Active), nothing when a desk asks this integration (the nesting guard of OthersFor).
                if (LbIntegrations.Compat.Compat.On("stockDesk") && Complete(emu) && __result?.WasSuccess != false && __0 != null)
                {
                    List<GameSaveBase> others;
                    using (SaveScan.Begin((__0.Games?.Count ?? 0) + (__0.AdditionalApplications?.Count ?? 0)))
                        others = SaveRouter.OthersFor((EmulatorPlugin)__instance, __0, rows);
                    // Only what this desk can hand to its owner: a row it could not route would reach this integration's
                    // own code at a restore, a removal, a backup (review 10/10).
                    int unrouted = others.RemoveAll(o => SaveRouter.OwnerForStock(o, emu) == null);
                    if (unrouted > 0) Once(emu, "GetSaves: " + unrouted + " other row(s) left out - no owner found for them");
                    if (others.Count > 0)
                    {
                        if (games == 1 && LbIntegrations.Compat.Compat.On("planA"))
                        {
                            var gameId = __0.Games!.First().Id;
                            others = SavePersist.TieLikeLaunchBox(gameId, others, m => SaveRouter.Say("[stock] " + m)).ToList();
                            SavePersist.Ensure(gameId, others, m => SaveRouter.Say("[stock] " + m.Replace("[saves] ", "")));
                        }
                        rows = rows.Concat(others).ToList();
                        __result = new GetSavesResponse(rows);
                    }
                }
                var ms = __state == 0 ? 0 : (Stopwatch.GetTimestamp() - __state) * 1000.0 / Stopwatch.Frequency;
                SaveRouter.Say("[stock] " + emu + " GetSaves: " + games + " game(s), " + rows.Count + " row(s), " + ms.ToString("0") + " ms");
                if (games <= 1) foreach (var r in rows) SaveRouter.Say("[stock]    listed " + Row(r));
            }
            catch (Exception ex) { SaveRouter.Say("[stock] rows left as listed: " + ex.Message); }
        }

        /// <summary>The persisted card a listed row takes: ONLY for a row the integration listed with no id of its own (RetroArch's
        /// saves and states - LaunchBox gave the card its GUID), and only when one card of this game, file and version holds
        /// it. A row with its own id keeps it - one file can hold several games' saves (a PCSX2 memory card: a pcsx2:<serial>
        /// card per game, Codex review 10/10). Two candidates: none taken, the row gets its own by-name id.</summary>
        internal static string? PickCard(string? listedId, IReadOnlyCollection<string>? candidates)
        {
            if (!string.IsNullOrEmpty(listedId) || candidates == null) return null;
            var distinct = candidates.Distinct(StringComparer.Ordinal).ToList();
            return distinct.Count == 1 ? distinct[0] : null;
        }

        /// <summary>The cards of this integration already in our form, persisted on the listed games (and their versions):
        /// game, path and version → their ids.</summary>
        private static Dictionary<string, List<string>> PersistedCards(GetSavesArgs? args, string emu)
        {
            var cards = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            var games = new List<IGame>();
            // The game LaunchBox's data holds, not the object handed in: opening Game Saves, LaunchBox lists a second time with a
            // working copy of the game whose Saves is empty (measured 11/10, Zelda: 18 rows, then 0 - the DataManager's game
            // kept its 18). The view of a desk (SaveGameView) is unwrapped by Holders, a copy is not: by id.
            if (args?.Games != null)
                foreach (var g in args.Games.Where(g => g != null))
                {
                    IGame? held = null;
                    try { held = PluginHelper.DataManager?.GetGameById(SaveGameView.Real(g).Id); } catch { }
                    games.Add(held ?? g);
                }
            foreach (var a in args?.AdditionalApplications ?? (IReadOnlyCollection<IAdditionalApplication>)Array.Empty<IAdditionalApplication>())
                if (a != null && games.All(g => g.Id != a.GameId) && PluginHelper.DataManager?.GetGameById(a.GameId) is { } g) games.Add(g);
            foreach (var g in games)
                foreach (var p in StockIdMigration.Read(g))
                    if (p.SaveGroupId != null && StockIds.TryDecode(p.SaveGroupId, emu, out _))
                    {
                        var key = CardKey(p.GameId, p.FilePath, p.AppId);
                        if (!cards.TryGetValue(key, out var list)) cards[key] = list = new List<string>();
                        list.Add(p.SaveGroupId);
                    }
            return cards;
        }

        private static string CardKey(string? gameId, string? path, string? appId)
        {
            string full;
            try
            {
                var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, ".."));
                full = string.IsNullOrWhiteSpace(path) ? "" : Path.GetFullPath(Path.IsPathRooted(path) ? path : Path.Combine(root, path));
            }
            catch { full = path ?? ""; }
            return (gameId ?? "").ToLowerInvariant() + "|" + full.ToLowerInvariant() + "|" + (appId ?? "").ToLowerInvariant();
        }

        private static string Row(GameSaveBase r)
            => r.GetType().Name + (r is GameSaveState s ? " slot " + s.Slot : "") + " id=\"" + r.SaveGroupId + "\" lineage=\"" + r.MatchLineageId
               + "\" game=" + r.GameId + (string.IsNullOrEmpty(r.AdditionalApplicationId) ? "" : " app=" + r.AdditionalApplicationId)
               + " exe=" + r.EmulatorFileName + " core=" + r.EmulatorCore + " " + r.FileLocation;

        private static string Show(GameSaveBase? r) => r == null ? "null" : "id=\"" + r.SaveGroupId + "\" " + Path.GetFileName(r.FileLocation ?? "");
    }
}
