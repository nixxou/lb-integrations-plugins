// Restoring a RetroArch or Dolphin row from our front desk (lot 3, docs\saves-lb14-routing.md §1, "How the
// stock integrations find the emulator at restore").
//
// WHY NOT FORWARD. Their AddSaveFile takes the emulator of the GAME (game.EmulatorId, or the version's), and
// here the game's emulator is ours: RetroArch finds no core and no folders, Dolphin builds its user roots from
// our exe. So the desk runs their own steps itself, with the emulator the ROW was listed from - the same
// private statics their AddSaveFile calls (read in the decompiled 14.0.1 code, 08/10), reached by reflection.
// A step missing or changed in another LaunchBox (renamed, other parameters) refuses the restore and logs
// which: nothing is ever written by a guess.
//
// RetroArch:  GetGameSaveDirectory / GetSaveStateDirectory(IEmulator, IGame, IAdditionalApplication, bool),
//             IsSaturnSaveContext(string core, IGame), CopySaturnCompanionFiles(string, string),
//             GetSaturnGroupId(string). The names: the rom's stem + the backup's extension for a save,
//             ".state" / ".state<n>" / ".state.auto" for a state - as theirs.
// Dolphin:    GetPlatformType(IGame, string rom), BuildUserRoots(IEmulator), TryGetDiscId(string rom, IEmulator,
//             out string), RestoreGameCubeSave / RestoreWiiSave / RestoreSaveState - which write and answer.
//
// PCSX2 is not here: it finds its card by the row's EmulatorFileName (an exe name) over the whole library,
// which the desk's rows carry - plain forwarding works.

#nullable enable

using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Unbroken.LaunchBox.Plugins;
using Unbroken.LaunchBox.Plugins.Data;

namespace LbIntegrations.Saves
{
    internal static class StockRestore
    {
        public const string RetroArch = "lb.retroarch";
        public const string Dolphin = "lb.dolphin";

        /// <summary>True for the owners whose AddSaveFile takes the game's emulator - those the desk restores itself.</summary>
        public static bool Handles(string emu) => emu == RetroArch || emu == Dolphin;

        public static AddSaveResponse Add(SaveOwnerRoute route, AddSaveArgs args, Action<string> log)
        {
            if (route.Emulator == null)
                return Refuse(log, "no " + route.Emu.Substring(SaveOwner.LaunchBoxPrefix.Length) + " emulator in LaunchBox for this save");
            try
            {
                // Which install: two of the same exe, core and platform in different folders are told apart by the
                // card's live file, or the restore is refused (SaveRouter.RestoreEmulator).
                if (args?.SaveToAdd != null)
                {
                    var install = SaveRouter.RestoreEmulator(route.Plugin, args.SaveToAdd, route.Emulator, out var ambiguous);
                    if (ambiguous != null) { log("[saves] restore refused: " + ambiguous); return new AddSaveResponse(ambiguous); }
                    if (install != null && !ReferenceEquals(install, route.Emulator))
                    {
                        log("[saves] restore goes to " + Safe(() => install.Title) + " (" + Safe(() => install.ApplicationPath) + "), the install its card's live file is in");
                        route = new SaveOwnerRoute(route.Plugin, route.Emu, install);
                    }
                }
                // Lot 4: a row in our form handed on in the integration's own (its steps read it as theirs), and the row it
                // gives back in ours - the card it came from kept (StockIds.Result).
                string? inbound = null, sent = args?.SaveToAdd?.SaveGroupId;
                if (args?.SaveToAdd != null && StockIds.TryDecode(args.SaveToAdd.SaveGroupId, route.Emu, out var native))
                {
                    inbound = args.SaveToAdd.SaveGroupId; sent = native;
                    var row = args.SaveToAdd;
                    args = new AddSaveArgs { SaveToAdd = SaveRows.WithIds(row, native, StockIds.TryDecode(row.MatchLineageId, route.Emu, out var l) ? l : row.MatchLineageId), ShouldOverwriteFunc = args.ShouldOverwriteFunc };
                }
                var done = route.Emu == RetroArch ? AddRetroArch(route, args!, log) : AddDolphin(route, args!, log);
                if (done?.WasSuccess == true && done.SaveAdded is { } added && LbIntegrations.Compat.Compat.On("stockIds") && SaveRouter.LaunchBoxHost && StockDesk.Complete(route.Emu))
                {
                    var id = StockIds.Result(route.Emu, sent, inbound, added.SaveGroupId, added is GameSaveState, Path.GetFileName(added.FileLocation ?? ""), (added as GameSaveState)?.Slot, StockDesk.DirOf(added.FileLocation));
                    if (id != null && id != added.SaveGroupId) done = new AddSaveResponse(SaveRows.WithIds(added, id, StockIds.Lineage(added.MatchLineageId, added.SaveGroupId, id)));
                }
                return done!;
            }
            catch (TargetInvocationException ex) { return Refuse(log, route.Emu + " failed: " + (ex.InnerException?.Message ?? ex.Message)); }
            catch (Exception ex) { return Refuse(log, route.Emu + " failed: " + ex.Message); }
        }

        // ── RetroArch ────────────────────────────────────────────────────────

        private static AddSaveResponse AddRetroArch(SaveOwnerRoute route, AddSaveArgs args, Action<string> log)
        {
            var row = args?.SaveToAdd;
            if (row == null || string.IsNullOrWhiteSpace(row.FileLocation) || !File.Exists(row.FileLocation))
                return new AddSaveResponse("The save file doesn't exist!");
            if (!Context(row, out var game, out var app, out var rom, out var why)) return new AddSaveResponse(why);
            // Their folder functions read the core off the GAME's command line - ours has none (SaveGameView).
            var platformName = Safe(() => game.Platform);
            if (LbIntegrations.Compat.Compat.On("retroarchView"))
            {
                game = SaveGameView.As(game, route.Emulator!);
                if (app != null) app = SaveGameView.As(app, route.Emulator!, platformName);
            }

            var t = route.Plugin.GetType();
            var saveDir = Step(t, "GetGameSaveDirectory", typeof(IEmulator), typeof(IGame), typeof(IAdditionalApplication), typeof(bool));
            var stateDir = Step(t, "GetSaveStateDirectory", typeof(IEmulator), typeof(IGame), typeof(IAdditionalApplication), typeof(bool));
            var isSaturn = Step(t, "IsSaturnSaveContext", typeof(string), typeof(IGame));
            var companions = Step(t, "CopySaturnCompanionFiles", typeof(string), typeof(string));
            var saturnId = Step(t, "GetSaturnGroupId", typeof(string));
            var missing = new[] { ("GetGameSaveDirectory", saveDir), ("GetSaveStateDirectory", stateDir), ("IsSaturnSaveContext", isSaturn),
                                  ("CopySaturnCompanionFiles", companions), ("GetSaturnGroupId", saturnId) }.Where(x => x.Item2 == null).Select(x => x.Item1).ToList();
            if (missing.Count > 0) return Refuse(log, "RetroArch's integration has changed (" + string.Join(", ", missing) + " not found)");

            string target;
            var state = row as GameSaveState;
            if (state != null)
            {
                var ext = state.Slot > 0 ? ".state" + state.Slot : state.Slot < 0 ? ".state.auto" : ".state";
                var dir = (string?)stateDir!.Invoke(null, new object?[] { route.Emulator, game, app, true });
                if (string.IsNullOrWhiteSpace(dir)) return new AddSaveResponse("Couldn't determine where to place this save!");
                target = Path.Combine(dir, Path.GetFileNameWithoutExtension(rom) + ext);
                // Into another slot than the backup's, over another state: LaunchBox keeps no copy of it (SlotRestore).
                int? own = SlotOfName(row.OriginalFileName) ?? SlotOfName(row.FileLocation);
                if (!SlotRestore.MayOverwrite(target, row.FileLocation!, own != state.Slot, state.Slot < 0 ? "The Auto slot" : "Slot " + (state.Slot ?? 0), log, out var no))
                    return new AddSaveResponse(no);
            }
            else
            {
                var dir = (string?)saveDir!.Invoke(null, new object?[] { route.Emulator, game, app, true });
                if (string.IsNullOrWhiteSpace(dir)) return new AddSaveResponse("Couldn't determine where to place this save!");
                target = Path.Combine(dir, Path.GetFileNameWithoutExtension(rom) + Path.GetExtension(row.FileLocation));
            }
            if (File.Exists(target) && (args!.ShouldOverwriteFunc == null || !args.ShouldOverwriteFunc()))
                return new AddSaveResponse("Operation cancelled by user!");

            // The core of the row's emulator for the game's platform (theirs reads the game's own command line,
            // which names our emulator's, not RetroArch's).
            var core = !string.IsNullOrEmpty(row.EmulatorCore) ? row.EmulatorCore! : SaveRouter.CoreOf(route.Emulator, Safe(() => game.Platform)) ?? "";
            var saturn = (bool)isSaturn!.Invoke(null, new object?[] { core, game })!;
            File.Copy(row.FileLocation!, target, overwrite: true);
            if (saturn) companions!.Invoke(null, new object?[] { row.FileLocation, target });
            var groupId = saturn ? (string?)saturnId!.Invoke(null, new object?[] { Path.GetFileNameWithoutExtension(target) }) : row.SaveGroupId;
            var exe = Path.GetFileName(route.EmulatorPath ?? "");
            log("[saves] restored a RetroArch " + (state != null ? "state" : "save") + " to " + target + " (" + Safe(() => route.Emulator!.Title) + ")");
            return new AddSaveResponse(state != null
                ? new GameSaveState
                {
                    GameId = row.GameId, AdditionalApplicationId = row.AdditionalApplicationId, FileLocation = target, Slot = state.Slot,
                    EmulatorFileName = exe, EmulatorCore = core, SaveGroupId = groupId,
                    SaveGroupName = string.IsNullOrWhiteSpace(row.SaveGroupName) ? "My Save State" : row.SaveGroupName, Title = row.Title,
                }
                : new GameSaveGame
                {
                    GameId = row.GameId, AdditionalApplicationId = row.AdditionalApplicationId, FileLocation = target,
                    EmulatorFileName = exe, EmulatorCore = core, SaveGroupId = groupId,
                    SaveGroupName = string.IsNullOrWhiteSpace(row.SaveGroupName) ? "My Save File" : row.SaveGroupName, Title = row.Title,
                });
        }

        // ── Dolphin ──────────────────────────────────────────────────────────

        private static AddSaveResponse AddDolphin(SaveOwnerRoute route, AddSaveArgs args, Action<string> log)
        {
            var row = args?.SaveToAdd;
            if (row == null) return new AddSaveResponse("The save file doesn't exist!");
            if (!Context(row, out var game, out _, out var rom, out var why)) return new AddSaveResponse(why);

            var t = route.Plugin.GetType();
            var platformOf = Step(t, "GetPlatformType", typeof(IGame), typeof(string));
            var roots = Step(t, "BuildUserRoots", typeof(IEmulator));
            var discId = Step(t, "TryGetDiscId", typeof(string), typeof(IEmulator), typeof(string).MakeByRefType());
            var gc = Named(t, "RestoreGameCubeSave", 4);
            var wii = Named(t, "RestoreWiiSave", 4);
            var st = Named(t, "RestoreSaveState", 4);
            var missing = new[] { ("GetPlatformType", platformOf), ("BuildUserRoots", roots), ("TryGetDiscId", discId),
                                  ("RestoreGameCubeSave", gc), ("RestoreWiiSave", wii), ("RestoreSaveState", st) }.Where(x => x.Item2 == null).Select(x => x.Item1).ToList();
            if (missing.Count > 0) return Refuse(log, "Dolphin's integration has changed (" + string.Join(", ", missing) + " not found)");

            var platform = platformOf!.Invoke(null, new object?[] { game, rom })?.ToString();
            if (platform == null || platform == "Unknown") return new AddSaveResponse("Unsupported platform for Dolphin save management!");
            var userRoots = roots!.Invoke(null, new object?[] { route.Emulator });

            string? DiscId()
            {
                var a = new object?[] { rom, route.Emulator, null };
                return (bool)discId!.Invoke(null, a)! ? a[2] as string : null;
            }

            object? answer;
            if (row is GameSaveState state)
            {
                var id = DiscId();
                if (id == null) return new AddSaveResponse("Couldn't determine save identifier for this game!");
                answer = st!.Invoke(null, new object?[] { state, route.Emulator, id, userRoots });
            }
            else if (platform == "Wii") answer = wii!.Invoke(null, new object?[] { row, route.Emulator, rom, userRoots });
            else
            {
                var id = DiscId();
                if (id == null) return new AddSaveResponse("Couldn't determine save identifier for this game!");
                answer = gc!.Invoke(null, new object?[] { row, route.Emulator, id, userRoots });
            }
            if (answer is not AddSaveResponse r) return Refuse(log, "Dolphin's restore answered nothing");
            log("[saves] Dolphin restore through " + Safe(() => route.Emulator!.Title) + ": " + (r.WasSuccess ? "done" : r.Message));
            return r;
        }

        // ── shared ───────────────────────────────────────────────────────────

        /// <summary>The game, the version when the row names one, and the rom - as their AddSaveFile reads them.</summary>
        private static bool Context(GameSaveBase row, out IGame game, out IAdditionalApplication? app, out string rom, out string why)
        {
            game = null!; app = null; rom = ""; why = "";
            var g = Safe(() => PluginHelper.DataManager?.GetGameById(row.GameId));
            if (g == null) { why = "Game doesn't exist!"; return false; }
            game = g;
            if (!string.IsNullOrWhiteSpace(row.AdditionalApplicationId))
            {
                app = Safe(() => g.GetAllAdditionalApplications()?.FirstOrDefault(a => a?.Id == row.AdditionalApplicationId));
                if (app == null) { why = "App doesn't exist!"; return false; }
            }
            var path = !string.IsNullOrWhiteSpace(app?.ApplicationPath) ? app!.ApplicationPath : Safe(() => g.ApplicationPath);
            if (string.IsNullOrWhiteSpace(path)) { why = "No Game/App application path found!"; return false; }
            rom = path!;
            return true;
        }

        private const BindingFlags Statics = BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public;

        /// <summary>A static of theirs by its exact parameters; null when it is not there any more.</summary>
        private static MethodInfo? Step(Type t, string name, params Type[] parameters)
        {
            try { return t.GetMethod(name, Statics, null, parameters, null); } catch { return null; }
        }

        /// <summary>A static of theirs whose parameter types are partly theirs (DolphinUserDataRoots): by name and count.</summary>
        private static MethodInfo? Named(Type t, string name, int count)
        {
            try
            {
                var all = t.GetMethods(Statics).Where(m => m.Name == name && m.GetParameters().Length == count && typeof(AddSaveResponse).IsAssignableFrom(m.ReturnType)).ToList();
                return all.Count == 1 ? all[0] : null;
            }
            catch { return null; }
        }

        private static AddSaveResponse Refuse(Action<string> log, string why)
        {
            log("[saves] restore refused: " + why);
            return new AddSaveResponse("This save could not be restored: " + why + ".");
        }

        private static T? Safe<T>(Func<T> f) { try { return f(); } catch { return default; } }

        /// <summary>A RetroArch state's slot from its name: ".state" 0, ".state3" 3, ".state.auto" -1; null when it is not
        /// one (a vault copy LaunchBox renamed "&lt;game&gt;.state" reads 0 - its row's own name says better).</summary>
        internal static int? SlotOfName(string? name)
        {
            if (string.IsNullOrWhiteSpace(name)) return null;
            var file = Path.GetFileName(name);
            if (file.EndsWith(".state.auto", StringComparison.OrdinalIgnoreCase)) return -1;
            var m = System.Text.RegularExpressions.Regex.Match(file, @"\.state(?<n>\d*)$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (!m.Success) return null;
            return m.Groups["n"].Value.Length == 0 ? 0 : int.Parse(m.Groups["n"].Value, System.Globalization.CultureInfo.InvariantCulture);
        }
    }
}
