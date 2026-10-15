// Save management for ZiNc - the game's high scores and settings, one FILE per ZiNc game:
//
//     <ZiNc EX's folder>\cfg\<ZiNc's set>.cfg
//
// MEASURED 16/10 on G:\LB1326 (Street Fighter EX from sfexu.zip, Street Fighter EX 2 from sfex2j.zip, each played once):
// ZiNc wrote cfg\sfex.cfg and cfg\sfex2j.cfg, 2048 bytes each, when the game ended - the board's NVRAM/EEPROM, the high-score
// table readable in it (initials and scores: NIN, MEG, GRA... RYU, SHA, BLA...). The file is named after ZiNc's SET, not the
// romset's file: sfexu.zip played as ZiNc's sfex writes sfex.cfg. So the save of a LaunchBox game is the one of the ZiNc game
// its romset is (ZincGames), and two LaunchBox games of one ZiNc game share it - as MAME's nvram does.
//
// cfg\ is beside ZiNc.exe (its readme: "Make sure a cfg/ dir exists"; ZiNc EX starts it with its own folder as the current
// directory, settings.c zinc_start): nothing is written beside the games (Mehdi, 07/10). There are no save states.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using LbIntegrations.Lbip;
using LbIntegrations.Saves;
using Unbroken.LaunchBox.Plugins;
using Unbroken.LaunchBox.Plugins.Data;

namespace LbIntegrations.ZincEx
{
    public partial class ZincExPlugin
    {
        private string SaveEmu => Emu;
        private void SaveLog(string message) => Log.Info(message);

        /// <summary>This plugin's "emu" in the ids (Shared.Saves): {"emu":"zincex","kind":"save","by":"set","id":"&lt;ZiNc's set&gt;"}.</summary>
        private const string Emu = "zincex";
        private const string SaveGroupName = "High scores and settings";
        private const string SaveChipText = "CFG";
        internal const string CfgFolder = "cfg";

        public override bool SupportsSaveManagement() => true;

        /// <summary>ZiNc has no states: the list is for the states of the other emulators its desk lists (SaveDesk.ForeignSlots).</summary>
        public override IReadOnlyDictionary<int, string> GetPotentialSaveSlots() => SaveDesk.ForeignSlots();

        // ── which file ───────────────────────────────────────────────────────

        /// <summary>The ZiNc set a romset is - the game's window's choice when it made one - or null. Kept while the romset keeps
        /// its date and size (StampedCache): Game Saves asks hundreds of times. Reads zips, never explains a miss.</summary>
        internal static string SetOf(string exe, string rom, string gameId)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(exe) || string.IsNullOrWhiteSpace(rom) || !rom.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) return null;
                var chosen = ZincGames.BySet(ZincExSettings.SetFor(gameId));
                var exeDir = Path.GetDirectoryName(Path.GetFullPath(exe));
                return StampedCache.Get(rom, "zincex.set|" + (chosen?.Set ?? "") + "|" + exeDir,
                                        () => File.Exists(rom) ? ZincGames.Identify(rom, ZincExLaunch.Folders(exeDir, rom), out _, chosen)?.Game.Set : null, keepNull: false);
            }
            catch (Exception ex) { Log.Warn("could not tell which ZiNc game " + rom + " is", ex); return null; }
        }

        internal static string CfgOf(string exe, string set)
            => string.IsNullOrEmpty(set) ? null : Path.Combine(Path.GetDirectoryName(Path.GetFullPath(exe)), CfgFolder, set + ".cfg");

        // ── listing ──────────────────────────────────────────────────────────

        GetSavesResponse IOwnSaves.OwnGetSaves(GetSavesArgs args)
        {
            try
            {
                if (args?.Emulator == null) return new GetSavesResponse("No emulator was supplied.");
                var exe = ResolveFullPath(Safe(() => args.Emulator.ApplicationPath));
                if (!ZincExLaunch.IsZincEx(exe)) return new GetSavesResponse("This emulator is not ZiNc EX.");

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
                return new GetSavesResponse("Could not read ZiNc's saves: " + ex.Message);
            }
        }

        private static void Collect(List<GameSaveBase> into, HashSet<string> seen, string exe, string romPath, string gameId, string appId)
        {
            if (string.IsNullOrWhiteSpace(gameId) || string.IsNullOrWhiteSpace(romPath)) return;
            var set = SetOf(exe, ResolveFullPath(romPath), gameId);
            var cfg = CfgOf(exe, set);
            if (cfg == null || !SaveScan.Exists(cfg) || !seen.Add(cfg + "|" + (appId ?? gameId))) return;
            into.Add(Row(cfg, gameId, appId, IdOf(set)));
        }

        private static string IdOf(string set) => SaveId.Write(new SaveIdData { Emu = Emu, Kind = "save", By = "set", Id = set });

        private static GameSaveGame Row(string path, string gameId, string appId, string groupId)
        {
            long size = 0; DateTime when = default;
            try { var i = new FileInfo(path); size = i.Length; when = i.LastWriteTimeUtc; } catch { }
            return new GameSaveGame
            {
                GameId = gameId, AdditionalApplicationId = appId, FileLocation = path, IsDirectory = false,
                OriginalFileName = Path.GetFileName(path), SaveGroupId = groupId,
                SaveGroupName = SaveGroupName, DisplayChipText = SaveChipText,
                ReportedFileSizeBytes = size > 0 ? size : (long?)null, ReportedLastModifiedUtc = when == default ? (DateTime?)null : when,
            };
        }

        // ── the contract ─────────────────────────────────────────────────────

        bool IOwnSaves.OwnIsSaveContainer(GameSaveBase save) => false;

        bool IOwnSaves.OwnUseSaveGroupIdForPersistedMatch(GameSaveBase save) => IsOurs(save);

        bool IOwnSaves.OwnTryBackupSave(GameSaveBase save, string emulatorApplicationPath, string destinationFolder, out string error)
        {
            error = "A ZiNc save is one file, not a container.";
            return false;
        }

        bool IOwnSaves.OwnIsSecondarySaveFile(string filePath) => false;

        IReadOnlyList<string> IOwnSaves.OwnGetCompanionSaveFiles(string primaryFilePath) => Array.Empty<string>();

        /// <summary>Called hundreds of times by Game Saves: a path compared with the cfg folder of the install - no zip read.</summary>
        bool IOwnSaves.OwnIsSaveActive(GameSaveBase save, string emulatorApplicationPath)
        {
            try
            {
                if (!IsOurs(save) || string.IsNullOrWhiteSpace(save.FileLocation)) return true;
                var exe = ResolveFullPath(emulatorApplicationPath);
                if (!ZincExLaunch.IsZincEx(exe) || !File.Exists(save.FileLocation)) return File.Exists(save.FileLocation);
                var cfg = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(exe)), CfgFolder);
                return string.Equals(Path.GetFullPath(Path.GetDirectoryName(save.FileLocation)).TrimEnd('\\'), Path.GetFullPath(cfg).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);
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
                if (string.IsNullOrWhiteSpace(source) || !File.Exists(source)) return new AddSaveResponse("This ZiNc backup is not a file: " + source);

                var game = PluginHelper.DataManager?.GetGameById(save.GameId);
                var exe = ExecutableOf(game);
                if (exe == null) return new AddSaveResponse("Could not find the ZiNc EX this game runs on.");
                var to = SaveRows.TargetFor(save, PluginHelper.DataManager, Full);
                if (to.Warning != null) Log.Warn("restore of " + save.OriginalFileName + ": " + to.Warning);
                if (to.Refused) return new AddSaveResponse(to.Warning);
                // Under the name of the ZiNc game the romset is TODAY - ZiNc reads cfg\<set>.cfg.
                var set = SetOf(exe, to.RomPath, save.GameId);
                if (set == null) return new AddSaveResponse("This game's romset is none of ZiNc's games: ZiNc would not read this save.");
                if (ZincExBuild.Running(Path.GetDirectoryName(exe))) return new AddSaveResponse("ZiNc is running: it would write the game's own over this one. Close it first.");

                var target = CfgOf(exe, set);
                // Over another card's save (a card of LaunchBox's own - a manual import -, or a backup of another ZiNc game): LaunchBox
                // keeps no copy of it - asked first (SlotRestore).
                bool otherCard = !IsOurs(save) || SlotRestore.OverAnotherFile(target, save);
                if (!SlotRestore.MayOverwrite(target, source, otherCard, "The current high scores", m => Log.Info(m), out var no))
                    return new AddSaveResponse(no);
                if (File.Exists(target))
                {
                    bool overwrite = true;
                    try { overwrite = args.ShouldOverwriteFunc?.Invoke() ?? true; } catch { }
                    if (!overwrite) return new AddSaveResponse("Restore cancelled.");
                }
                var cannot = SaveCompanions.WhyNotWritable(source, target, null);
                if (cannot != null) { Log.Info("restore of " + set + ".cfg refused: " + cannot); return new AddSaveResponse(cannot); }
                Directory.CreateDirectory(Path.GetDirectoryName(target));
                File.Copy(source, target, overwrite: true);
                Log.Info("restored " + Path.GetFileName(source) + " -> " + target);
                return new AddSaveResponse(Row(target, save.GameId, save.AdditionalApplicationId, IsOurs(save) && SaveId.TryRead(save.SaveGroupId, out var d) && d.Id == set ? save.SaveGroupId : IdOf(set)));
            }
            catch (Exception ex)
            {
                Log.Warn("AddSaveFile failed", ex);
                return new AddSaveResponse("Could not restore this ZiNc save: " + ex.Message);
            }
        }

        LbIntegrations.Saves.ExpectedDir IOwnSaves.OwnExpectedDir(IEmulator emulator, GameSaveBase save)
        {
            var path = Safe(() => emulator?.ApplicationPath);
            if (save == null || !ZincExLaunch.IsZincEx(path)) return LbIntegrations.Saves.ExpectedDir.Unknown("Could not find the ZiNc EX this game runs on.");
            var exe = ResolveFullPath(path);
            return LbIntegrations.Saves.ExpectedDir.FolderOr(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(exe)), CfgFolder), "Could not work out where ZiNc reads this save from.");
        }

        /// <summary>A path as LaunchBox stores it, made full; null for nothing.</summary>
        private static string Full(string path) => string.IsNullOrWhiteSpace(path) ? null : ResolveFullPath(path);

        private static string ExecutableOf(IGame game)
        {
            try
            {
                var dm = PluginHelper.DataManager;
                var id = Safe(() => game?.EmulatorId);
                // The install a restore was sent to first (SaveForward.WithVia: the choice among two installs, docs\saves-two-installs.md §4).
                var own = LbIntegrations.Saves.SaveForward.ViaEmulator(ZincExLaunch.IsZincEx) ?? (string.IsNullOrEmpty(id) ? null : dm?.GetEmulatorById(id));
                var path = Safe(() => own?.ApplicationPath);
                if (!ZincExLaunch.IsZincEx(path))
                    path = Safe(() => dm?.GetAllEmulators()?.FirstOrDefault(e => ZincExLaunch.IsZincEx(Safe(() => e.ApplicationPath)))?.ApplicationPath);
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
                Log.Info("deleted " + Path.GetFileName(loc));
                return new PluginResponse(true);
            }
            catch (Exception ex)
            {
                Log.Warn("RemoveSave failed", ex);
                return new PluginResponse(false, "Could not delete this ZiNc save: " + ex.Message);
            }
        }

        private static bool IsOurs(GameSaveBase save) => SaveRows.IsOurs(save?.SaveGroupId, Emu);
    }
}
