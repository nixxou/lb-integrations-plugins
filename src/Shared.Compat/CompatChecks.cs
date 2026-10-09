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
                        return TypeNamed("OpenEmulatorMenuAction") != null ? (CompatCheckState.Ok, "OpenEmulatorMenuAction") : (CompatCheckState.Missing, "OpenEmulatorMenuAction (the \"Open emulator\" action)");
                    case "lb.wizard":
                    {
                        var names = new[] { "WizardViewModel", "ImportWizardPlatformSelectViewModel", "RomImportGameListViewModel", "RomImportCustomOptionsViewModel", "RomImportMameOptionsViewModel" };
                        var missing = names.Where(n => TypeNamed(n) == null).ToList();
                        return missing.Count == 0 ? (CompatCheckState.Ok, "the import wizard's view models") : (CompatCheckState.Missing, string.Join(", ", missing));
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
