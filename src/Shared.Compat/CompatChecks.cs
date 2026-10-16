// What the Compatibility page shows beside each patch: whether what it relies on in LaunchBox is still there (a reflection
// check - a type, a method found by name, nothing called), and, where it can be done without touching anything, whether the
// bug it works around is still there (a bug test on made-up objects: LaunchBox's own function called, nothing written).
// Run from the page, in the LaunchBox process, when the page opens - never at start-up.

#nullable enable

using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using Unbroken.LaunchBox.Plugins;
using Unbroken.LaunchBox.Plugins.Data;

namespace LbIntegrations.Compat
{
    internal enum CompatCheckState { Ok, Missing, NotHere }
    internal enum CompatBugState { Present, Fixed, NotTestable }

    internal static class CompatChecks
    {
        private const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;

        /// <summary>A reflection check: Ok, Missing (with what), or NotHere (what it looks into is not loaded - e.g. under LiteBox).</summary>
        public static (CompatCheckState State, string Detail) Check(string id)
        {
            try
            {
                switch (id)
                {
                    case "igame.cmdline":
                        return typeof(IGame).GetMethod("GetEffectiveCommandLine", Type.EmptyTypes) != null && typeof(IAdditionalApplication).GetMethod("GetEffectiveCommandLine", Type.EmptyTypes) != null
                            ? (CompatCheckState.Ok, "IGame / IAdditionalApplication.GetEffectiveCommandLine()") : (CompatCheckState.Missing, "GetEffectiveCommandLine() is gone from the SDK");
                    case "ra.statics":
                        return Statics("retroarch", NativeStatics.RetroArch);
                    case "ra.loadConfig":
                    {
                        var n = NativeOf("retroarch");
                        var (t, form) = LbHostShape.Native(n.Assembly, n.Type);
                        if (form == LbForm.NotLoaded) return (CompatCheckState.NotHere, "RetroArch's LaunchBox integration is not loaded");
                        if (t == null) return (CompatCheckState.Missing, n.Type + " is gone from " + n.Assembly);
                        var m = t.GetMethod("LoadConfiguration", BindingFlags.NonPublic | BindingFlags.Static, null, new[] { typeof(string) }, null);
                        return m != null && typeof(Dictionary<string, string>).IsAssignableFrom(m.ReturnType)
                            ? (CompatCheckState.Ok, "RetroArch's integration: LoadConfiguration(string)") : (CompatCheckState.Missing, "LoadConfiguration(string) returning its settings");
                    }
                    case "ra.saves": return StockSaves("retroarch");
                    case "dolphin.saves": return StockSaves("dolphin");
                    case "pcsx2.saves": return StockSaves("pcsx2");
                    case "nosave.saves": return NoSaves();
                    case "lb.pluginRegistry":
                    {
                        // LiteBox loads Unbroken.LaunchBox.Windows too (its installs), but builds no registry of LaunchBox's.
                        if (InLiteBox() || !AppDomain.CurrentDomain.GetAssemblies().Any(a => !a.IsDynamic && a.GetType(LbPluginRegistry.TypeName, false) != null))
                            return (CompatCheckState.NotHere, "LaunchBox's plugin registry is not loaded (not LaunchBox)");
                        var what = LbPluginRegistry.Describe(out var read);
                        return read ? (CompatCheckState.Ok, "LaunchBox's PluginRegistry: " + what) : (CompatCheckState.Missing, what);
                    }
                    case "catchall.loaded":
                        return AppDomain.CurrentDomain.GetAssemblies().Any(a => !a.IsDynamic && string.Equals(a.GetName().Name, "NixxSaves", StringComparison.OrdinalIgnoreCase))
                            ? (CompatCheckState.Ok, "Nixx-Saves is loaded") : (CompatCheckState.NotHere, "Nixx-Saves is not installed (the pack's setup installs it)");
                    case "sdk.saves":
                    {
                        var missing = SaveMethods.Where(n => typeof(EmulatorPlugin).GetMethods(BindingFlags.Public | BindingFlags.Instance).Count(m => m.Name == n && m.IsVirtual) != 1).ToList();
                        return missing.Count == 0 ? (CompatCheckState.Ok, "EmulatorPlugin: the " + SaveMethods.Length + " save methods")
                                                  : (CompatCheckState.Missing, "EmulatorPlugin: " + string.Join(", ", missing));
                    }
                    case "lb.saveIds":
                    {
                        var save = LbType("Unbroken.LaunchBox", GameSaveType, out var gone);
                        if (save == null) return gone ?? (CompatCheckState.NotHere, GameSaveType + " not loaded");
                        var missing = new[] { "SaveGroupId", "MatchLineageId" }.Where(n => save.GetProperty(n)?.CanWrite != true).ToList();
                        return missing.Count == 0 ? (CompatCheckState.Ok, "GameSave.SaveGroupId / MatchLineageId can be set")
                                                  : (CompatCheckState.Missing, "GameSave: " + string.Join(", ", missing) + " cannot be set");
                    }
                    case "dolphin.statics":
                        return Statics("dolphin", NativeStatics.Dolphin);
                    case "lb.saves":
                    {
                        var save = LbType("Unbroken.LaunchBox", GameSaveType, out var gone);
                        if (save == null) return gone ?? (CompatCheckState.NotHere, GameSaveType + " not loaded");
                        var missing = new[] { "GameId", "AdditionalApplicationId", "FilePath", "SaveGroupId", "MatchLineageId", "OriginalFileName", "Slot", "EmulatorFileName" }
                                      .Where(n => save.GetProperty(n) == null).ToList();
                        var holder = Types().FirstOrDefault(t => t.GetProperty("Saves", Any)?.PropertyType is { IsGenericType: true } pt && pt.GetGenericArguments()[0] == save);
                        var coll = holder?.GetProperty("Saves", Any)?.PropertyType;
                        if (holder == null || coll == null) missing.Add("a game's Saves collection");
                        else
                        {
                            if (coll.GetMethod("Add", new[] { save }) == null) missing.Add("Saves.Add");
                            if (coll.GetMethod("TryTake", new[] { save.MakeByRefType() }) == null) missing.Add("Saves.TryTake");
                        }
                        return missing.Count == 0 ? (CompatCheckState.Ok, "GameSave and the games' Saves collections") : (CompatCheckState.Missing, string.Join(", ", missing));
                    }
                    case "lb.applyPersisted":
                    {
                        var vm = LbType("LaunchBox", GameSavesPageType, out var gone);
                        if (vm == null) return gone ?? (CompatCheckState.NotHere, "the Game Saves page is not loaded");
                        var m = vm.GetMethod("ApplyPersistedMetadata", Any);
                        var item = vm.GetNestedType("SaveItem", BindingFlags.Public | BindingFlags.NonPublic);
                        var ctor = item?.GetConstructors(Any).FirstOrDefault(c => c.GetParameters().Length == 2);
                        return m != null && ctor != null ? (CompatCheckState.Ok, "ApplyPersistedMetadata, SaveItem") : (CompatCheckState.Missing, m == null ? "ApplyPersistedMetadata" : "SaveItem(GameSave, bool)");
                    }
                    case "sqlite.reader":
                    {
                        var t = LbType("Microsoft.Data.Sqlite", "Microsoft.Data.Sqlite.SqliteCommand", out var gone);
                        if (t == null) return gone ?? (CompatCheckState.NotHere, "Microsoft.Data.Sqlite not loaded");
                        var m = t.GetMethods(BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly).Where(x => x.Name == "ExecuteDbDataReader").ToList();
                        return m.Count > 0 ? (CompatCheckState.Ok, "SqliteCommand.ExecuteDbDataReader") : (CompatCheckState.Missing, "SqliteCommand.ExecuteDbDataReader");
                    }
                    case "lb.openEmulator":
                        if (!InLaunchBox()) return (CompatCheckState.NotHere, "not inside LaunchBox");
                        return TypeNamed("OpenEmulatorMenuAction") != null ? (CompatCheckState.Ok, "OpenEmulatorMenuAction") : (CompatCheckState.Missing, "OpenEmulatorMenuAction (the \"Open emulator\" action)");
                    case "lb.wizard":
                    {
                        if (!InLaunchBox()) return (CompatCheckState.NotHere, "not inside LaunchBox");
                        var names = new[] { "WizardViewModel", "ImportWizardPlatformSelectViewModel", "RomImportGameListViewModel", "RomImportCustomOptionsViewModel", "RomImportMameOptionsViewModel" };
                        var missing = names.Where(n => TypeNamed(n) == null).ToList();
                        return missing.Count == 0 ? (CompatCheckState.Ok, "the import wizard's view models") : (CompatCheckState.Missing, string.Join(", ", missing));
                    }
                    case "lb.launchWithMenu":
                    case "bb.launchWithPopup":
                    {
                        // What the labels lean on (src\LaunchWithLabels\README.md §9): LaunchBox's "Launch With" label in its resx; in
                        // Big Box, the popup's view model and its PopupOption (Value, DisplayString). Each check in its own host.
                        if (!InLaunchBox()) return (CompatCheckState.NotHere, "not inside LaunchBox or Big Box");
                        bool bigBox = false;
                        try { bigBox = Process.GetCurrentProcess().ProcessName.Equals("BigBox", StringComparison.OrdinalIgnoreCase); } catch { }
                        if (bigBox != (id == "bb.launchWithPopup"))
                            return (CompatCheckState.NotHere, bigBox ? "checked in LaunchBox, not in Big Box" : "checked in Big Box, not in LaunchBox");
                        if (!bigBox)
                        {
                            var strings = Types().FirstOrDefault(x => x.FullName == "Unbroken.LaunchBox.Properties.Strings");
                            var rm = strings?.GetProperty("ResourceManager", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)?.GetValue(null) as System.Resources.ResourceManager;
                            var label = rm?.GetString("LabelLaunchWithMenu", System.Globalization.CultureInfo.InvariantCulture);
                            return label != null ? (CompatCheckState.Ok, "the \"Launch With\" label (" + label + ")") : (CompatCheckState.Missing, "Strings.LabelLaunchWithMenu");
                        }
                        var popup = TypeNamed("SelectOptionPopupViewModel`1");
                        var option = TypeNamed("PopupOption`1") ?? TypeNamed("PopupOption");
                        var missing = new List<string>();
                        if (popup == null) missing.Add("SelectOptionPopupViewModel");
                        if (option == null || option.GetProperty("Value") == null || option.GetProperty("DisplayString") == null) missing.Add("PopupOption.Value / DisplayString");
                        return missing.Count == 0 ? (CompatCheckState.Ok, "Big Box's option popup") : (CompatCheckState.Missing, string.Join(", ", missing));
                    }
                    case "lb.version":
                    {
                        var v = LaunchBoxVersion();
                        var fixedIn = FixedIn.OverwriteBackup;
                        return (CompatCheckState.Ok, "LaunchBox " + (v?.ToString() ?? "?") + (fixedIn == null ? "; the bug is not marked fixed in any version" : "; marked fixed in " + fixedIn));
                    }
                }
                return (CompatCheckState.NotHere, "unknown check " + id);
            }
            catch (Exception ex) { return (CompatCheckState.Missing, ex.GetType().Name + ": " + ex.Message); }
        }

        /// <summary>A bug test, on made-up objects.</summary>
        public static (CompatBugState State, string Detail) Bug(string id)
        {
            try
            {
                switch (id)
                {
                    case "lb.mergeByName": return MergeByName();
                    case "ra.folder": return RetroArchFolder();
                }
                return (CompatBugState.NotTestable, "no test");
            }
            catch (Exception ex) { return (CompatBugState.NotTestable, (ex.InnerException ?? ex).GetType().Name + ": " + (ex.InnerException ?? ex).Message); }
        }

        // ── the bug tests ──

        /// <summary>LaunchBox's own matching (ApplyPersistedMetadata) on a made-up save and a made-up backup of the same game
        /// with the same file name and another card: still taken = still there.</summary>
        private static (CompatBugState, string) MergeByName()
        {
            var vm = TypeNamed("AddEditGameSavesPageViewModel");
            var item = vm?.GetNestedType("SaveItem", BindingFlags.Public | BindingFlags.NonPublic);
            var ctor = item?.GetConstructors(Any).FirstOrDefault(c => c.GetParameters().Length == 2);
            var apply = vm?.GetMethod("ApplyPersistedMetadata", Any);
            if (ctor == null || apply == null) return (CompatBugState.NotTestable, "the Game Saves page is not loaded");
            var saveType = ctor.GetParameters()[0].ParameterType;
            object Save(string file, string id)
            {
                var s = Activator.CreateInstance(saveType, true)!;
                foreach (var (n, v) in new[] { ("GameId", "00000000-0000-0000-0000-00000000c0a7"), ("FilePath", file), ("SaveGroupId", id), ("MatchLineageId", id), ("Title", "compat") })
                    try { saveType.GetProperty(n)?.SetValue(s, v); } catch { }
                return s;
            }
            IList List(params object[] xs) { var l = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(item!))!; foreach (var x in xs) l.Add(x); return l; }
            var live = ctor.Invoke(new[] { Save(@"C:\compat\emuA\Game.srm", "compat-mine"), (object)false });
            var backup = ctor.Invoke(new[] { Save(@"Saves\Compat\Game.srm", "compat-card"), (object)true });
            apply.Invoke(null, new object[] { live, List(), List(backup) });
            var after = saveType.GetProperty("SaveGroupId")?.GetValue(item!.GetProperty("GameSave")?.GetValue(live)) as string;
            return after == "compat-card" ? (CompatBugState.Present, "a save was taken into another card by its file name")
                 : after == "compat-mine" ? (CompatBugState.Fixed, "a save keeps its card although another has its file name")
                 : (CompatBugState.NotTestable, "unexpected answer: " + (after ?? "null"));
        }

        /// <summary>RetroArch's GetSaveStateDirectory with a game whose command line has no core, then with one that has:
        /// no folder only without = still there.</summary>
        private static (CompatBugState, string) RetroArchFolder()
        {
            var n = NativeOf("retroarch");
            var (t, form) = LbHostShape.Native(n.Assembly, n.Type);
            if (form == LbForm.NotLoaded) return (CompatBugState.NotTestable, "RetroArch's integration is not loaded");
            var m = NativeStatics.Find(t, NativeStatics.RaSaveStateDirectory);
            if (m == null) return (CompatBugState.NotTestable, "RetroArch's integration has changed (" + FormName(form) + "): no " + NativeStatics.RaSaveStateDirectory);
            var dm = PluginHelper.DataManager;
            var ra = dm?.GetAllEmulators()?.FirstOrDefault(e => string.Equals(Path.GetFileName(e?.ApplicationPath ?? ""), "retroarch.exe", StringComparison.OrdinalIgnoreCase));
            if (ra == null) return (CompatBugState.NotTestable, "no RetroArch in this library");
            var row = ra.GetAllEmulatorPlatforms()?.FirstOrDefault(p => (p?.CommandLine ?? "").Contains("-L"));
            if (row == null) return (CompatBugState.NotTestable, "this RetroArch has no platform line with a core");
            var without = (string?)m.Invoke(null, new object?[] { ra, StubGame.Make(row.Platform, ""), null, false });
            var with = (string?)m.Invoke(null, new object?[] { ra, StubGame.Make(row.Platform, row.CommandLine), null, false });
            if (with == null) return (CompatBugState.NotTestable, "no states folder even with its core (none yet, or its settings)");
            return without == null ? (CompatBugState.Present, "no states folder without a core on the game's line")
                                   : (CompatBugState.Fixed, "its states folder is found without a core on the game's line");
        }

        // ── helpers ──

        private static IEnumerable<Type> Types()
        {
            foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (a.IsDynamic) continue;
                Type?[] ts;
                try { ts = a.GetTypes(); } catch (ReflectionTypeLoadException e) { ts = e.Types; } catch { continue; }
                foreach (var t in ts) if (t != null) yield return t;
            }
        }

        private static Type? TypeNamed(string name) => Types().FirstOrDefault(t => t.Name == name);

        /// <summary>Running inside LaunchBox or BigBox - not LiteBox, not the probe: what the application's own types are looked for in.</summary>
        private static bool InLaunchBox()
        {
            try { var n = Process.GetCurrentProcess().ProcessName; return n.Equals("LaunchBox", StringComparison.OrdinalIgnoreCase) || n.Equals("BigBox", StringComparison.OrdinalIgnoreCase); }
            catch { return true; }
        }

        /// <summary>Running inside LiteBox (its process): the page marks what only LaunchBox uses. The settings file is the same
        /// for both, so nothing is hidden there.</summary>
        internal static bool InLiteBox()
        {
            if (LiteBoxForTests is bool forced) return forced;
            try { return Process.GetCurrentProcess().ProcessName.Equals("LiteBox", StringComparison.OrdinalIgnoreCase); }
            catch { return false; }
        }

        /// <summary>The probe's page as LiteBox shows it (--nixx-shots with LBIP_AS_LITEBOX=1).</summary>
        internal static bool? LiteBoxForTests;

        private const string GameSaveType = "Unbroken.LaunchBox.Data.GameSave";
        private const string GameSavesPageType = "Unbroken.LaunchBox.Windows.Desktop.ViewModels.AddEditGameSavesPageViewModel";

        /// <summary>A type of LaunchBox's by its full name in its assembly. Not found: <paramref name="gone"/> is Missing when the
        /// assembly IS loaded (the type was renamed or removed - LaunchBox changed), null when the assembly is not (NotHere).
        /// 16/10: "not loaded" for a type gone from a loaded assembly hid a change behind an Ok beside it.</summary>
        private static Type? LbType(string assembly, string fullName, out (CompatCheckState, string)? gone)
        {
            gone = null;
            var a = LbHostShape.NativeAssembly(assembly);
            if (a == null) return null;
            Type? t = null;
            try { t = a.GetType(fullName, false); } catch { }
            if (t == null) gone = (CompatCheckState.Missing, fullName + " is gone from " + assembly);
            return t;
        }

        /// <summary>A native integration of LbHostShape's table, by family ("retroarch", "dolphin"...).</summary>
        private static (string Assembly, string Type, string Family) NativeOf(string family)
            => LbHostShape.Natives.First(n => string.Equals(n.Family, family, StringComparison.OrdinalIgnoreCase));

        private static string FormName(LbForm form) => form switch
        {
            LbForm.V1 => "v1, LaunchBox 14.0",
            LbForm.V2 => "v2, LaunchBox 14.1",
            _ => "neither EmulatorPlugin nor IEmulatorIntegration",
        };

        /// <summary>The save methods of the SDK, as lot 4's hooks find them (StockDesk).</summary>
        internal static readonly string[] SaveMethods =
        {
            "SupportsSaveManagement", "GetSaves", "AddSaveFile", "RemoveSave", "IsSaveActive", "IsSaveContainer", "TryBackupSave",
            "UseSaveGroupIdForPersistedMatch", "TryComputeSaveSignature", "TryGetSaveGroupInfo", "IsSecondarySaveFile",
            "GetCompanionSaveFiles", "GetPotentialSaveSlots",
        };

        /// <summary>A LaunchBox integration's save methods, each by the SDK's exact signature: which it overrides, which run the
        /// SDK's body. Missing when the type is gone or does not override GetSaves and AddSaveFile (lot 4 measured all three do).
        /// LaunchBox 14.1's v2 integration (IEmulatorSaveCapability) is Missing too: the pack does not hook it yet
        /// (docs\lb141-plan.md, step 4) - its desk is off, which the page must say.</summary>
        private static (CompatCheckState, string) StockSaves(string family)
        {
            var n = NativeOf(family);
            var (t, form) = LbHostShape.Native(n.Assembly, n.Type);
            if (form == LbForm.NotLoaded) return (CompatCheckState.NotHere, n.Type + ": its LaunchBox integration is not loaded");
            if (t == null) return (CompatCheckState.Missing, n.Type + " is gone from " + n.Assembly);
            if (form == LbForm.V2)
                return (CompatCheckState.Missing, n.Type + " is a v2 integration (LaunchBox 14.1)"
                        + (LbHostShape.Implements(t, LbHostShape.V2SaveCapability) ? " with IEmulatorSaveCapability" : " without save support")
                        + ": the pack does not hook v2 integrations yet");
            if (form != LbForm.V1) return (CompatCheckState.Missing, n.Type + ": " + FormName(form));
            var own = new List<string>(); var sdk = new List<string>(); var gone = new List<string>();
            foreach (var name in SaveMethods)
            {
                var b = typeof(EmulatorPlugin).GetMethods(BindingFlags.Public | BindingFlags.Instance).FirstOrDefault(m => m.Name == name && m.IsVirtual);
                var m = b == null ? null : t.GetMethod(name, BindingFlags.Public | BindingFlags.Instance, null, b.GetParameters().Select(p => p.ParameterType).ToArray(), null);
                if (m == null) gone.Add(name); else if (m.DeclaringType == typeof(EmulatorPlugin)) sdk.Add(name); else own.Add(name);
            }
            if (gone.Count > 0 || !own.Contains("GetSaves") || !own.Contains("AddSaveFile"))
                return (CompatCheckState.Missing, n.Type + ": " + (gone.Count > 0 ? string.Join(", ", gone) + " not found" : "GetSaves / AddSaveFile not its own"));
            return (CompatCheckState.Ok, n.Type + ": its own " + string.Join(", ", own) + (sdk.Count > 0 ? "; the SDK's " + string.Join(", ", sdk) : ""));
        }

        /// <summary>Lot 5: LaunchBox's integrations without save support (LbHostShape's table, but RetroArch, Dolphin and
        /// PCSX2): Ok when each one loaded still overrides none of the save methods; one that now has its own is said - it gets
        /// lot 4's treatment. A v2 one (LaunchBox 14.1) is not hooked yet: Missing.</summary>
        private static readonly string[] SaveFamilies = { "retroarch", "dolphin", "pcsx2" };

        private static (CompatCheckState, string) NoSaves()
        {
            var plain = new List<string>(); var changed = new List<string>(); var v2 = new List<string>();
            foreach (var n in LbHostShape.Natives.Where(x => !SaveFamilies.Contains(x.Family)))
            {
                var (t, form) = LbHostShape.Native(n.Assembly, n.Type);
                if (form == LbForm.NotLoaded) continue;
                if (t == null) { changed.Add(n.Type + " gone from " + n.Assembly); continue; }
                if (form == LbForm.V2) { v2.Add(n.Type); continue; }
                if (form != LbForm.V1) { changed.Add(n.Type + " (" + FormName(form) + ")"); continue; }
                var own = SaveMethods.Where(name =>
                {
                    var b = typeof(EmulatorPlugin).GetMethods(BindingFlags.Public | BindingFlags.Instance).FirstOrDefault(m => m.Name == name && m.IsVirtual);
                    var m = b == null ? null : t.GetMethod(name, BindingFlags.Public | BindingFlags.Instance, null, b.GetParameters().Select(p => p.ParameterType).ToArray(), null);
                    return m != null && m.DeclaringType != typeof(EmulatorPlugin);
                }).ToList();
                if (own.Count == 0) plain.Add(n.Type); else changed.Add(n.Type + " (its own " + string.Join(", ", own) + ")");
            }
            if (plain.Count == 0 && changed.Count == 0 && v2.Count == 0) return (CompatCheckState.NotHere, "none of LaunchBox's integrations without save support is loaded");
            var bad = new List<string>();
            if (v2.Count > 0) bad.Add("v2 integrations (LaunchBox 14.1), not hooked by the pack yet: " + string.Join(", ", v2));
            if (changed.Count > 0) bad.Add("now with save methods of their own: " + string.Join("; ", changed));
            if (bad.Count > 0) return (CompatCheckState.Missing, string.Join("; ", bad));
            return (CompatCheckState.Ok, "no save method of their own: " + string.Join(", ", plain));
        }

        /// <summary>The statics StockRestore calls, by their EXACT signatures (NativeStatics - the same StockRestore uses).
        /// Until 16/10 checked by name only: green on LaunchBox 14.1 b3 while the restore refused them.</summary>
        private static (CompatCheckState, string) Statics(string family, NativeStatic[] wanted)
        {
            var n = NativeOf(family);
            var (t, form) = LbHostShape.Native(n.Assembly, n.Type);
            if (form == LbForm.NotLoaded) return (CompatCheckState.NotHere, n.Type + ": its LaunchBox integration is not loaded");
            if (t == null) return (CompatCheckState.Missing, n.Type + " is gone from " + n.Assembly);
            var missing = NativeStatics.Missing(t, wanted);
            return missing.Count == 0
                ? (CompatCheckState.Ok, n.Type + ": " + string.Join(", ", wanted.Select(w => w.Name)))
                : (CompatCheckState.Missing, n.Type + " (" + FormName(form) + "): " + string.Join(", ", missing) + " not found");
        }

        internal static Version? LaunchBoxVersion() => LbHostShape.LaunchBoxVersion();

        /// <summary>A game with a platform, a path and a command line, nothing else - for RetroArch's folder function.</summary>
        public class StubGame : DispatchProxy
        {
            private string? _platform, _line;
            public static IGame Make(string? platform, string? line)
            {
                var g = Create<IGame, StubGame>();
                var s = (StubGame)(object)g; s._platform = platform; s._line = line;
                return g;
            }
            protected override object? Invoke(MethodInfo? m, object?[]? args)
            {
                if (m == null) return null;
                switch (m.Name)
                {
                    case "GetEffectiveCommandLine": return _line;
                    case "get_Platform": return _platform;
                    case "get_ApplicationPath": return @"C:\compat\Game.sfc";
                    case "get_Id": return "00000000-0000-0000-0000-00000000c0a7";
                    case "get_Title": return "Compat";
                }
                var r = m.ReturnType;
                return r == typeof(void) ? null : r.IsValueType ? Activator.CreateInstance(r) : null;
            }
        }
    }

    /// <summary>The LaunchBox versions that fix a bug a patch works around - null until one is checked by hand.</summary>
    internal static class FixedIn
    {
        /// <summary>The precautionary copy of the overwritten file at a restore (SlotRestore's warning).</summary>
        public static readonly Version? OverwriteBackup = null;
    }
}
