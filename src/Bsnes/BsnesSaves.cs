// Save management for bsnes - the cartridge save and the save states, one FILE each (as RaSnes9xSaves, after
// SuperZsnesSaves).
//
//   Cartridge save   <name>.srm    in Path/Saves, else beside the game   (and <name>.rtc - the clock of S-RTC/SPC7110
//                                                                        carts - and <name>.psr - Satellaview's -
//                                                                        beside it: its companions)
//   Save states      <name>.bsz    in Path/States, else beside the game  ONE zip holding them all: Quick/Slot 1..N.bst,
//                                                                        Quick/Undo, Quick/Redo, Managed/<name>.bst
//                                                                        (and <name>.bsz.png beside it: the picture of
//                                                                        the newest, BsnesStateThumbs - its companion)
//
// Read off bsnes's code (target-bsnes/program/paths.cpp, game-rom.cpp, states.cpp, 06/10): path("Saves", location,
// ".srm") is the folder of Path/Saves when it is set, else the game's, then the game's file name without its last
// extension - for a .zip or a .7z the archive's own. A state is saved by rewriting the whole .bsz with the new one in
// it, so the file is the unit LaunchBox backs up and restores.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using LbIntegrations.Lbip;
using LbIntegrations.Saves;
using Unbroken.LaunchBox.Plugins;
using Unbroken.LaunchBox.Plugins.Data;

namespace LbIntegrations.Bsnes
{
    public partial class BsnesPlugin
    {
        /// <summary>The front desk (Shared.Saves SaveDesk): this plugin's emu, and where it logs.</summary>
        private string SaveEmu => Emu;
        private void SaveLog(string message) => Log.Info(message);

        /// <summary>This plugin's "emu" in the ids (Shared.Saves): {"emu":"bsnes","kind":"save","by":"name","id":"<name>"},
        /// and "kind":"state" for the .bsz - one file for every slot, so no "slot".</summary>
        private const string Emu = "bsnes";
        private const string SaveGroupName = "Cartridge save";
        private const string SaveChipText = "SRM";
        private const string StateGroupName = "Save states (all slots, one file)";
        private const string StateChipText = "BSZ";

        public override bool SupportsSaveManagement() => true;

        /// <summary>bsnes's own states are one .bsz, restored as a file: no slot of its own. The list is for the states of
        /// the other emulators its desk lists (SaveDesk.ForeignSlots).</summary>
        public override IReadOnlyDictionary<int, string> GetPotentialSaveSlots() => SaveDesk.ForeignSlots();

        // ── where bsnes keeps a game's ───────────────────────────────────────

        internal sealed class Folders { public string SaveDir, StateDir; }

        internal static Folders FoldersFor(string exePath, string romPath)
        {
            var romDir = string.IsNullOrWhiteSpace(romPath) ? null : Path.GetDirectoryName(Path.GetFullPath(romPath));
            var bml = BsnesBml.Load(BsnesBml.Of(exePath));
            string Dir(string node)
            {
                var v = bml.Get(node);
                if (string.IsNullOrWhiteSpace(v)) return romDir;
                try { return Path.GetFullPath(v.Replace('/', Path.DirectorySeparatorChar)); }
                catch { return romDir; }
            }
            return new Folders { SaveDir = Dir("Path/Saves"), StateDir = Dir("Path/States") };
        }

        internal static string NameOf(string romPath) => Path.GetFileNameWithoutExtension(romPath ?? "");

        // ── listing ──────────────────────────────────────────────────────────

        GetSavesResponse IOwnSaves.OwnGetSaves(GetSavesArgs args)
        {
            try
            {
                if (args?.Emulator == null) return new GetSavesResponse("No emulator was supplied.");
                var exe = ResolveFullPath(Safe(() => args.Emulator.ApplicationPath));
                if (!BsnesLaunch.IsBsnesExe(exe)) return new GetSavesResponse("This emulator is not bsnes.");

                var found = new List<GameSaveBase>();
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var app in args.AdditionalApplications ?? (IReadOnlyCollection<IAdditionalApplication>)Array.Empty<IAdditionalApplication>())
                {
                    // A version on its game's own file is not tied to the version (SaveRows.TieTo).
                    var gameId = Safe(() => app.GameId);
                    var gamePath = Safe(() => (args.Games?.FirstOrDefault(g => Safe(() => g.Id) == gameId) ?? PluginHelper.DataManager?.GetGameById(gameId))?.ApplicationPath);
                    Collect(found, seen, exe, Safe(() => app.ApplicationPath), gameId,
                            SaveRows.TieTo(Safe(() => app.Id), gamePath, Safe(() => app.ApplicationPath), Full));
                }
                foreach (var game in args.Games ?? (IReadOnlyCollection<IGame>)Array.Empty<IGame>())
                    Collect(found, seen, exe, Safe(() => game.ApplicationPath), Safe(() => game.Id), null);

                Log.Verbose("found " + found.Count + " file(s) for " + exe);
                return new GetSavesResponse(found);
            }
            catch (Exception ex)
            {
                Log.Warn("GetSaves failed", ex);
                return new GetSavesResponse("Could not read bsnes saves: " + ex.Message);
            }
        }

        private static void Collect(List<GameSaveBase> into, HashSet<string> seen, string exe, string romPath, string gameId, string appId)
        {
            if (string.IsNullOrWhiteSpace(gameId) || string.IsNullOrWhiteSpace(romPath)) return;
            var rom = ResolveFullPath(romPath);
            var name = NameOf(rom);
            if (name.Length == 0) return;
            var folders = FoldersFor(exe, rom);
            var context = appId ?? gameId;

            var srm = folders.SaveDir == null ? null : Path.Combine(folders.SaveDir, name + ".srm");
            if (srm != null && File.Exists(srm) && seen.Add("save|" + srm + "|" + context))
                into.Add(Row(srm, gameId, appId, IdOf("save", name), SaveGroupName, SaveChipText));

            var bsz = folders.StateDir == null ? null : Path.Combine(folders.StateDir, name + ".bsz");
            if (bsz != null && File.Exists(bsz) && seen.Add("states|" + bsz + "|" + context))
            {
                BsnesStateThumbs.Refresh(bsz);
                into.Add(Row(bsz, gameId, appId, IdOf("state", name), StateGroupName, StateChipText));
            }
        }

        private static string IdOf(string kind, string name) => SaveId.Write(new SaveIdData { Emu = Emu, Kind = kind, By = "name", Id = name });

        /// <summary>The .bsz row: our id saying "state".</summary>
        private static bool IsStates(GameSaveBase save) => SaveId.TryRead(save?.SaveGroupId, out var d) && d.Kind == "state";

        private static GameSaveGame Row(string path, string gameId, string appId, string groupId, string groupName, string chip)
        {
            long size = 0; DateTime when = default;
            try { var i = new FileInfo(path); size = i.Length; when = i.LastWriteTimeUtc; } catch { }
            return new GameSaveGame
            {
                GameId = gameId, AdditionalApplicationId = appId, FileLocation = path, IsDirectory = false,
                OriginalFileName = Path.GetFileName(path), SaveGroupId = groupId,
                SaveGroupName = groupName, DisplayChipText = chip,
                ReportedFileSizeBytes = size > 0 ? size : (long?)null, ReportedLastModifiedUtc = when == default ? (DateTime?)null : when,
            };
        }

        // ── the contract ─────────────────────────────────────────────────────

        bool IOwnSaves.OwnIsSaveContainer(GameSaveBase save) => false;

        bool IOwnSaves.OwnUseSaveGroupIdForPersistedMatch(GameSaveBase save) => IsOurs(save);

        bool IOwnSaves.OwnTryBackupSave(GameSaveBase save, string emulatorApplicationPath, string destinationFolder, out string error)
        {
            error = "A bsnes save is one file, not a container.";
            return false;
        }

        /// <summary>The states' thumbnail is part of the .bsz's save, not a save of its own.</summary>
        bool IOwnSaves.OwnIsSecondarySaveFile(string filePath) => BsnesStateThumbs.IsThumbOfStates(filePath);

        /// <summary>A cartridge save's clock (.rtc) and Satellaview data (.psr) travel with it; the states' thumbnail with the .bsz.</summary>
        IReadOnlyList<string> IOwnSaves.OwnGetCompanionSaveFiles(string primaryFilePath)
        {
            try
            {
                if (primaryFilePath != null && primaryFilePath.EndsWith(".bsz", StringComparison.OrdinalIgnoreCase)) return LbipStateThumbs.Companion(primaryFilePath);
                if (string.IsNullOrWhiteSpace(primaryFilePath) || !primaryFilePath.EndsWith(".srm", StringComparison.OrdinalIgnoreCase)) return Array.Empty<string>();
                return new[] { ".rtc", ".psr" }.Select(e => Path.ChangeExtension(primaryFilePath, e)).Where(File.Exists).ToArray();
            }
            catch { return Array.Empty<string>(); }
        }

        bool IOwnSaves.OwnIsSaveActive(GameSaveBase save, string emulatorApplicationPath)
        {
            try
            {
                if (!IsOurs(save) || string.IsNullOrWhiteSpace(save.FileLocation)) return true;
                var exe = ResolveFullPath(emulatorApplicationPath);
                if (!BsnesLaunch.IsBsnesExe(exe) || !File.Exists(save.FileLocation)) return File.Exists(save.FileLocation);
                var target = TargetDirFor(save, exe);
                return target == null || string.Equals(Path.GetFullPath(Path.GetDirectoryName(save.FileLocation)).TrimEnd('\\'),
                                                       Path.GetFullPath(target).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);
            }
            catch { return true; }
        }

        // ── restore ──────────────────────────────────────────────────────────

        AddSaveResponse IOwnSaves.OwnAddSaveFile(AddSaveArgs args)
        {
            try
            {
                var save = args?.SaveToAdd;
                if (save == null) return new AddSaveResponse("No save was supplied.");
                var source = save.FileLocation;
                if (string.IsNullOrWhiteSpace(source) || !File.Exists(source)) return new AddSaveResponse("This bsnes backup is not a file: " + source);

                var game = PluginHelper.DataManager?.GetGameById(save.GameId);
                var exe = ExecutableOf(game);
                var to = SaveRows.TargetFor(save, PluginHelper.DataManager, Full);
                if (to.Warning != null) Log.Warn("restore of " + save.OriginalFileName + ": " + to.Warning);
                if (to.Refused) return new AddSaveResponse(to.Warning);
                var targetDir = exe == null ? null : TargetDirFor(save, exe, to.RomPath);
                if (targetDir == null) return new AddSaveResponse("Could not work out where bsnes reads this save from.");
                // The name of the rom it goes to TODAY, with the backup's own extension (.srm, .rtc, .psr, .bsz).
                var original = new[] { save.OriginalFileName, Path.GetFileName(source) }.FirstOrDefault(n => !string.IsNullOrWhiteSpace(n));
                var romName = NameOf(to.RomPath);
                var name = romName.Length > 0 ? romName + Path.GetExtension(original) : original;
                if (BsnesLaunch.Running(exe)) return new AddSaveResponse("bsnes is running: it would write the game's own over this one. Close it first.");

                var target = Path.Combine(targetDir, name);
                if (File.Exists(target))
                {
                    bool overwrite = true;
                    try { overwrite = args.ShouldOverwriteFunc?.Invoke() ?? true; } catch { }
                    if (!overwrite) return new AddSaveResponse("Restore cancelled.");
                }
                // Everything it will write must be writable first - the clock beside the save too (SaveCompanions).
                bool states = IsStates(save);
                var companions = states ? null : ((IOwnSaves)this).OwnGetCompanionSaveFiles(source);
                var cannot = SaveCompanions.WhyNotWritable(source, target, companions);
                if (cannot != null) { Log.Info("restore of " + name + " refused: " + cannot); return new AddSaveResponse(cannot); }
                Directory.CreateDirectory(targetDir);
                File.Copy(source, target, overwrite: true);
                Log.Info("restored " + name + " -> " + targetDir);
                if (states)
                {
                    // Its thumbnail with it; without one, the old would be a picture of another moment - made again from the .bsz.
                    var thumb = GetCompanionSaveFiles(source).FirstOrDefault(LbipStateThumbs.IsThumb);
                    if (thumb != null) File.Copy(thumb, LbipStateThumbs.PathOf(target), overwrite: true);
                    else { LbipStateThumbs.Remove(target); BsnesStateThumbs.Refresh(target); }
                }
                // The cartridge's clock (.rtc) and Satellaview data (.psr) with its save: LaunchBox copies nothing back
                // (SaveCompanions).
                else SaveCompanions.CopyBack(source, target, companions, m => Log.Info(m));
                return new AddSaveResponse(Row(target, save.GameId, save.AdditionalApplicationId, save.SaveGroupId,
                                               states ? StateGroupName : SaveGroupName, states ? StateChipText : SaveChipText));
            }
            catch (Exception ex)
            {
                Log.Warn("AddSaveFile failed", ex);
                return new AddSaveResponse("Could not restore this bsnes save: " + ex.Message);
            }
        }

        /// <summary>The folder for this rom - the row's version's or its game's (SaveRows.TargetFor).</summary>
        private static string TargetDirFor(GameSaveBase save, string exe, string rom = null)
        {
            rom ??= SaveRows.TargetFor(save, PluginHelper.DataManager, Full).RomPath;
            var folders = FoldersFor(exe, rom);
            return IsStates(save) ? folders.StateDir : folders.SaveDir;
        }

        /// <summary>A path as LaunchBox stores it, made full; null for nothing.</summary>
        private static string Full(string path) => string.IsNullOrWhiteSpace(path) ? null : ResolveFullPath(path);

        private static string ExecutableOf(IGame game)
        {
            try
            {
                var dm = PluginHelper.DataManager;
                var id = Safe(() => game?.EmulatorId);
                var own = string.IsNullOrEmpty(id) ? null : dm?.GetEmulatorById(id);
                var path = Safe(() => own?.ApplicationPath);
                if (!BsnesLaunch.IsBsnesExe(path))
                    path = Safe(() => dm?.GetAllEmulators()?.FirstOrDefault(e => BsnesLaunch.IsBsnesExe(Safe(() => e.ApplicationPath)))?.ApplicationPath);
                return string.IsNullOrEmpty(path) ? null : ResolveFullPath(path);
            }
            catch { return null; }
        }

        // ── delete ───────────────────────────────────────────────────────────

        PluginResponse IOwnSaves.OwnRemoveSave(GameSaveBase save)
        {
            try
            {
                if (!IsOurs(save)) return base.RemoveSave(save);
                var loc = save?.FileLocation;
                if (string.IsNullOrWhiteSpace(loc)) return new PluginResponse(false, "This save has no location.");
                try { if (File.Exists(loc)) File.Delete(loc); }
                catch (Exception ex) { Log.Warn("could not delete " + loc, ex); return new PluginResponse(false, "Could not delete " + Path.GetFileName(loc) + "."); }
                if (loc.EndsWith(".bsz", StringComparison.OrdinalIgnoreCase)) LbipStateThumbs.Remove(loc);
                Log.Info("deleted " + Path.GetFileName(loc));
                return new PluginResponse(true);
            }
            catch (Exception ex)
            {
                Log.Warn("RemoveSave failed", ex);
                return new PluginResponse(false, "Could not delete this bsnes save: " + ex.Message);
            }
        }

        private static bool IsOurs(GameSaveBase save) => SaveRows.IsOurs(save?.SaveGroupId, Emu);

        private static string Safe(Func<string> read)
        {
            try { return read(); } catch { return null; }
        }
    }
}
