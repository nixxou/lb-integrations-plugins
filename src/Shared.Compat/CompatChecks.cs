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
                        return Statics("RetroArch", "GetGameSaveDirectory", "GetSaveStateDirectory", "IsSaturnSaveContext", "CopySaturnCompanionFiles", "GetSaturnGroupId");
                    case "ra.loadConfig":
                    {
                        var t = StaticsOwner("RetroArch", "LoadConfiguration");
                        if (t == null) return (CompatCheckState.NotHere, "RetroArch's LaunchBox integration is not loaded");
                        var m = t.GetMethod("LoadConfiguration", BindingFlags.NonPublic | BindingFlags.Static, null, new[] { typeof(string) }, null);
                        return m != null && typeof(Dictionary<string, string>).IsAssignableFrom(m.ReturnType)
                            ? (CompatCheckState.Ok, "RetroArch's integration: LoadConfiguration(string)") : (CompatCheckState.Missing, "LoadConfiguration(string) returning its settings");
                    }
                    case "ra.saves": return StockSaves("RetroArch", "RetroArchPlugin");
                    case "dolphin.saves": return StockSaves("Dolphin", "DolphinPlugin");
                    case "pcsx2.saves": return StockSaves("Pcsx2", "Pcsx2Plugin");
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
                        var save = Types().FirstOrDefault(t => t.FullName == "Unbroken.LaunchBox.Data.GameSave");
                        if (save == null) return (CompatCheckState.NotHere, "Unbroken.LaunchBox.Data.GameSave not loaded");
                        var missing = new[] { "SaveGroupId", "MatchLineageId" }.Where(n => save.GetProperty(n)?.CanWrite != true).ToList();
                        return missing.Count == 0 ? (CompatCheckState.Ok, "GameSave.SaveGroupId / MatchLineageId can be set")
                                                  : (CompatCheckState.Missing, "GameSave: " + string.Join(", ", missing) + " cannot be set");
                    }
                    case "dolphin.statics":
                        return Statics("Dolphin", "GetPlatformType", "BuildUserRoots", "TryGetDiscId", "RestoreGameCubeSave", "RestoreWiiSave", "RestoreSaveState");
                    case "lb.saves":
                    {
                        var save = Types().FirstOrDefault(t => t.FullName == "Unbroken.LaunchBox.Data.GameSave");
                        if (save == null) return (CompatCheckState.NotHere, "Unbroken.LaunchBox.Data.GameSave not loaded");
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
                        var vm = TypeNamed("AddEditGameSavesPageViewModel");
                        if (vm == null) return (CompatCheckState.NotHere, "the Game Saves page is not loaded");
                        var m = vm.GetMethod("ApplyPersistedMetadata", Any);
                        var item = vm.GetNestedType("SaveItem", BindingFlags.Public | BindingFlags.NonPublic);
                        var ctor = item?.GetConstructors(Any).FirstOrDefault(c => c.GetParameters().Length == 2);
                        return m != null && ctor != null ? (CompatCheckState.Ok, "ApplyPersistedMetadata, SaveItem") : (CompatCheckState.Missing, m == null ? "ApplyPersistedMetadata" : "SaveItem(GameSave, bool)");
                    }
                    case "sqlite.reader":
                    {
                        var t = Types().FirstOrDefault(x => x.FullName == "Microsoft.Data.Sqlite.SqliteCommand");
                        if (t == null) return (CompatCheckState.NotHere, "Microsoft.Data.Sqlite not loaded");
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
                    {
                        // What the labels lean on (src\LaunchWithLabels\README.md §9): LaunchBox's "Launch With" label in its resx; in
                        // Big Box, the popup's view model and its PopupOption (Value, DisplayString).
                        if (!InLaunchBox()) return (CompatCheckState.NotHere, "not inside LaunchBox or Big Box");
                        bool bigBox = false;
                        try { bigBox = Process.GetCurrentProcess().ProcessName.Equals("BigBox", StringComparison.OrdinalIgnoreCase); } catch { }
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
            var t = StaticsOwner("RetroArch", "GetSaveStateDirectory");
            var m = t?.GetMethod("GetSaveStateDirectory", Any, null, new[] { typeof(IEmulator), typeof(IGame), typeof(IAdditionalApplication), typeof(bool) }, null);
            if (m == null) return (CompatBugState.NotTestable, "RetroArch's integration is not loaded");
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

        /// <summary>The type, in an assembly whose name holds <paramref name="family"/>, that has the static <paramref name="method"/>.</summary>
        private static Type? StaticsOwner(string family, string method)
        {
            foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (a.IsDynamic || (a.GetName().Name ?? "").IndexOf(family, StringComparison.OrdinalIgnoreCase) < 0) continue;
                Type?[] ts;
                try { ts = a.GetTypes(); } catch (ReflectionTypeLoadException e) { ts = e.Types; } catch { continue; }
                var t = ts.FirstOrDefault(x => x != null && x.GetMethods(Any).Any(mm => mm.IsStatic && mm.Name == method));
                if (t != null) return t;
            }
            return null;
        }

        /// <summary>The save methods of the SDK, as lot 4's hooks find them (StockDesk).</summary>
        internal static readonly string[] SaveMethods =
        {
            "SupportsSaveManagement", "GetSaves", "AddSaveFile", "RemoveSave", "IsSaveActive", "IsSaveContainer", "TryBackupSave",
            "UseSaveGroupIdForPersistedMatch", "TryComputeSaveSignature", "TryGetSaveGroupInfo", "IsSecondarySaveFile",
            "GetCompanionSaveFiles", "GetPotentialSaveSlots",
        };

        /// <summary>A LaunchBox integration's save methods, each by the SDK's exact signature: which it overrides, which run the
        /// SDK's body. Missing when the type is gone or does not override GetSaves and AddSaveFile (lot 4 measured all three do).</summary>
        private static (CompatCheckState, string) StockSaves(string family, string typeName)
        {
            Type? t = null;
            foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (a.IsDynamic || !(a.GetName().Name ?? "").StartsWith("Unbroken.LaunchBox.Windows." + family, StringComparison.OrdinalIgnoreCase)) continue;
                Type?[] ts;
                try { ts = a.GetTypes(); } catch (ReflectionTypeLoadException e) { ts = e.Types; } catch { continue; }
                t = ts.FirstOrDefault(x => x != null && x.Name == typeName && typeof(EmulatorPlugin).IsAssignableFrom(x));
                if (t != null) break;
            }
            if (t == null) return (CompatCheckState.NotHere, family + "'s LaunchBox integration is not loaded");
            var own = new List<string>(); var sdk = new List<string>(); var gone = new List<string>();
            foreach (var n in SaveMethods)
            {
                var b = typeof(EmulatorPlugin).GetMethods(BindingFlags.Public | BindingFlags.Instance).FirstOrDefault(m => m.Name == n && m.IsVirtual);
                var m = b == null ? null : t.GetMethod(n, BindingFlags.Public | BindingFlags.Instance, null, b.GetParameters().Select(p => p.ParameterType).ToArray(), null);
                if (m == null) gone.Add(n); else if (m.DeclaringType == typeof(EmulatorPlugin)) sdk.Add(n); else own.Add(n);
            }
            if (gone.Count > 0 || !own.Contains("GetSaves") || !own.Contains("AddSaveFile"))
                return (CompatCheckState.Missing, typeName + ": " + (gone.Count > 0 ? string.Join(", ", gone) + " not found" : "GetSaves / AddSaveFile not its own"));
            return (CompatCheckState.Ok, typeName + ": its own " + string.Join(", ", own) + (sdk.Count > 0 ? "; the SDK's " + string.Join(", ", sdk) : ""));
        }

        /// <summary>Lot 5: LaunchBox's integrations without save support (the same table as StockDesk's): Ok when each one
        /// loaded still overrides none of the save methods; one that now has its own is said - it gets lot 4's treatment.</summary>
        internal static readonly (string Family, string Type)[] NoSaveIntegrations =
        {
            ("BigPEmu", "BigPEmuPlugin"), ("Cemu", "CemuPlugin"), ("DuckStation", "DuckStationPlugin"), ("Mame", "MamePlugin"),
            ("Rpcs3", "Rpcs3Plugin"), ("ScummVm", "ScummVmPlugin"), ("Xemu", "XemuPlugin"),
        };

        private static (CompatCheckState, string) NoSaves()
        {
            var plain = new List<string>(); var changed = new List<string>();
            foreach (var (family, typeName) in NoSaveIntegrations)
            {
                Type? t = null;
                foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
                {
                    if (a.IsDynamic || !string.Equals(a.GetName().Name, "Unbroken.LaunchBox.Windows." + family, StringComparison.OrdinalIgnoreCase)) continue;
                    Type?[] ts;
                    try { ts = a.GetTypes(); } catch (ReflectionTypeLoadException e) { ts = e.Types; } catch { continue; }
                    t = ts.FirstOrDefault(x => x != null && x.Name == typeName && typeof(EmulatorPlugin).IsAssignableFrom(x));
                    if (t != null) break;
                }
                if (t == null) continue;
                var own = SaveMethods.Where(n =>
                {
                    var b = typeof(EmulatorPlugin).GetMethods(BindingFlags.Public | BindingFlags.Instance).FirstOrDefault(m => m.Name == n && m.IsVirtual);
                    var m = b == null ? null : t.GetMethod(n, BindingFlags.Public | BindingFlags.Instance, null, b.GetParameters().Select(p => p.ParameterType).ToArray(), null);
                    return m != null && m.DeclaringType != typeof(EmulatorPlugin);
                }).ToList();
                if (own.Count == 0) plain.Add(family); else changed.Add(typeName + " (its own " + string.Join(", ", own) + ")");
            }
            if (plain.Count == 0 && changed.Count == 0) return (CompatCheckState.NotHere, "none of LaunchBox's integrations without save support is loaded");
            if (changed.Count > 0) return (CompatCheckState.Missing, "now with save methods of their own: " + string.Join("; ", changed));
            return (CompatCheckState.Ok, "no save method of their own: " + string.Join(", ", plain));
        }

        private static (CompatCheckState, string) Statics(string family, params string[] names)
        {
            var t = StaticsOwner(family, names[0]);
            if (t == null) return (CompatCheckState.NotHere, family + "'s LaunchBox integration is not loaded");
            var have = new HashSet<string>(t.GetMethods(Any).Where(m => m.IsStatic).Select(m => m.Name));
            var missing = names.Where(n => !have.Contains(n)).ToList();
            return missing.Count == 0 ? (CompatCheckState.Ok, family + "'s integration: " + string.Join(", ", names)) : (CompatCheckState.Missing, string.Join(", ", missing));
        }

        internal static Version? LaunchBoxVersion()
        {
            try
            {
                var fv = FileVersionInfo.GetVersionInfo(Path.Combine(AppContext.BaseDirectory, "LaunchBox.dll"));
                return new Version(fv.FileMajorPart, fv.FileMinorPart, fv.FileBuildPart, fv.FilePrivatePart);
            }
            catch { return null; }
        }

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
