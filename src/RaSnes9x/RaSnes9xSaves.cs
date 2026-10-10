// Save management for RASnes9x - the cartridge save and the save states, one FILE each, as SUPER ZSNES's are
// (SuperZsnesSaves, the model of this file).
//
//   Battery save   <name>.srm     in the SRAM folder         (and <name>.rtc beside it: the clock of the few carts
//                                                            that have one - S-RTC, SPC7110 - its companion)
//   Save states    <name>.000     in the savestates folder   .000 to .009 the ten slots (Shift+F1..F10 save; F2 / F4
//                                                            the current slot, F6 / F7 pick it - RaSnes9xLaunch); a
//                                                            higher number is a slot of another bank (and
//                                                            <name>.000.png beside each: its picture,
//                                                            RaSnes9xStateThumbs - its companion)
//
// THE NAME is the file name Snes9x was given, its last extension off (S9xGetFilename): for a .zip the archive's; a .7z
// is unpacked under its own name (RaSnes9xLaunch.RomFor), so it gives the same.
//
// THE FOLDERS are Snes9x's own settings, [Settings\Win\Files] Dir:SRAM and Dir:Savestates of snes9x.conf - ".\Saves"
// for both as it ships, relative to the executable. Measured 06/10: Zelda's 8 KB .srm written to x64\Saves\ when the
// window was closed (WM_CLOSE) - Snes9x writes the .srm when it quits, or loads another game.
//
// NOT LISTED, on purpose: <name>.oops, the state Snes9x writes by itself before loading one ("undo"), replaced at
// every load - as SUPER ZSNES's resume state is left out.
//
// THE IDS are the pack's JSON (Shared.Saves, docs\saves-lb14-routing.md §3): {"emu":"rasnes9x","kind":"save",
// "by":"name","id":"<name>"} and, for a state, "kind":"state" with its "slot". A restore goes to the version the row is
// tied to, else the game, and takes the name of THAT rom today: a rom renamed (and updated in LaunchBox) gets its save
// back under the new name. A version on its game's own file is not tied to the version (SaveRows.TieTo).

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using LbIntegrations.Lbip;
using LbIntegrations.Saves;
using Unbroken.LaunchBox.Plugins;
using Unbroken.LaunchBox.Plugins.Data;

namespace LbIntegrations.RaSnes9x
{
    public partial class RaSnes9xPlugin
    {
        /// <summary>The front desk (Shared.Saves SaveDesk): this plugin's emu, and where it logs.</summary>
        private string SaveEmu => Emu;
        private void SaveLog(string message) => Log.Info(message);

        /// <summary>This plugin's "emu" in the ids (Shared.Saves).</summary>
        private const string Emu = "rasnes9x";
        private const string SaveGroupName = "Cartridge save";
        private const string SaveChipText = "SRM";
        private const string StateGroupName = "Save states";
        private const string StateChipText = "STATE";
        private const string Files = "Settings\\Win\\Files";

        private static readonly Regex StateFile = new Regex(@"\.(?<slot>\d{3})$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        public override bool SupportsSaveManagement() => true;

        public override IReadOnlyDictionary<int, string> GetPotentialSaveSlots()
        {
            var slots = new Dictionary<int, string>();
            for (int i = 0; i <= 9; i++) slots[i] = i.ToString(CultureInfo.InvariantCulture);
            return slots;
        }

        // ── where Snes9x keeps a game's ──────────────────────────────────────

        internal sealed class Folders { public string SaveDir, StateDir; }

        internal static Folders FoldersFor(string exePath, string romPath)
        {
            var exeDir = Path.GetDirectoryName(Path.GetFullPath(exePath));
            var romDir = string.IsNullOrWhiteSpace(romPath) ? null : Path.GetDirectoryName(Path.GetFullPath(romPath));
            // A big listing reads snes9x.conf once, not once per game (LbIntegrations.Saves.SaveScan).
            var conf = LbIntegrations.Saves.SaveScan.Memo("rasnes9x.conf|" + exePath, () => LbIntegrations.Lbip.StampedCache.Get(RaSnes9xConf.Of(exePath), "rasnes9x.conf", () => RaSnes9xConf.Load(RaSnes9xConf.Of(exePath))));   // and Game Saves: while snes9x.conf is unchanged
            string Dir(string key)
            {
                var v = conf.Get(Files, key);
                if (v == null) v = ".\\Saves";              // the file is not written yet: Snes9x's default
                if (string.IsNullOrWhiteSpace(v)) return romDir;
                try { return Path.GetFullPath(Path.IsPathRooted(v) ? v : Path.Combine(exeDir, v)); }
                catch { return romDir; }
            }
            return new Folders { SaveDir = Dir("Dir:SRAM"), StateDir = Dir("Dir:Savestates") };
        }

        internal static string NameOf(string romPath) => Path.GetFileNameWithoutExtension(romPath ?? "");

        // ── listing ──────────────────────────────────────────────────────────

        GetSavesResponse IOwnSaves.OwnGetSaves(GetSavesArgs args)
        {
            try
            {
                if (args?.Emulator == null) return new GetSavesResponse("No emulator was supplied.");
                var exe = ResolveFullPath(Safe(() => args.Emulator.ApplicationPath));
                if (!RaSnes9xLaunch.IsRaSnes9xExe(exe)) return new GetSavesResponse("This emulator is not RASnes9x.");

                var found = new List<GameSaveBase>();
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                int states = 0;
                foreach (var app in args.AdditionalApplications ?? (IReadOnlyCollection<IAdditionalApplication>)Array.Empty<IAdditionalApplication>())
                {
                    var gameId = Safe(() => app.GameId);
                    var gamePath = Safe(() => (args.Games?.FirstOrDefault(g => Safe(() => g.Id) == gameId) ?? PluginHelper.DataManager?.GetGameById(gameId))?.ApplicationPath);
                    states += Collect(found, seen, exe, Safe(() => app.ApplicationPath), gameId,
                                      SaveRows.TieTo(Safe(() => app.Id), gamePath, Safe(() => app.ApplicationPath), Full));
                }
                foreach (var game in args.Games ?? (IReadOnlyCollection<IGame>)Array.Empty<IGame>())
                    states += Collect(found, seen, exe, Safe(() => game.ApplicationPath), Safe(() => game.Id), null);

                Log.Verbose("found " + (found.Count - states) + " save(s) and " + states + " state(s) for " + exe);
                return new GetSavesResponse(found);
            }
            catch (Exception ex)
            {
                Log.Warn("GetSaves failed", ex);
                return new GetSavesResponse("Could not read RASnes9x saves: " + ex.Message);
            }
        }

        /// <summary>Everything one game owns. Returns how many of them were states.</summary>
        private static int Collect(List<GameSaveBase> into, HashSet<string> seen, string exe, string romPath, string gameId, string appId)
        {
            if (string.IsNullOrWhiteSpace(gameId) || string.IsNullOrWhiteSpace(romPath)) return 0;
            var rom = ResolveFullPath(romPath);
            var name = NameOf(rom);
            if (name.Length == 0) return 0;
            var folders = FoldersFor(exe, rom);
            var context = appId ?? gameId;

            var srm = folders.SaveDir == null ? null : Path.Combine(folders.SaveDir, name + ".srm");
            if (srm != null && LbIntegrations.Saves.SaveScan.Exists(srm) && seen.Add("save|" + srm + "|" + context))
                into.Add(SaveRow(srm, gameId, appId, SaveIdOf(name)));

            int states = 0;
            foreach (var (path, slot) in StateFilesIn(folders.StateDir, name))
            {
                if (!seen.Add("state|" + path + "|" + context)) continue;
                RaSnes9xStateThumbs.Refresh(path);
                into.Add(StateRow(path, gameId, appId, slot, StateIdOf(name, slot), StateGroupName));
                states++;
            }
            return states;
        }

        private static string SaveIdOf(string name) => SaveId.Write(new SaveIdData { Emu = Emu, Kind = "save", By = "name", Id = name });
        private static string StateIdOf(string name, int slot) => SaveId.Write(new SaveIdData { Emu = Emu, Kind = "state", By = "name", Id = name, Slot = slot });

        /// <summary>A game's states in the folder Snes9x keeps them: &lt;name&gt;.000 and up - never its .oops, nor the
        /// thumbnail beside a state ("Zelda.000.png" is "Zelda.000" without its extension).</summary>
        private static IEnumerable<(string Path, int Slot)> StateFilesIn(string dir, string name)
        {
            if (dir == null || string.IsNullOrEmpty(name) || !LbIntegrations.Saves.SaveScan.DirExists(dir)) yield break;
            foreach (var path in LbIntegrations.Saves.SaveScan.Files(dir, name + ".*"))
            {
                if (!string.Equals(Path.GetFileNameWithoutExtension(path), name, StringComparison.OrdinalIgnoreCase)) continue;   // "Zelda 2.000" is not Zelda's
                var m = StateFile.Match(path);
                if (!m.Success || !int.TryParse(m.Groups["slot"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var slot)) continue;
                yield return (path, slot);
            }
        }

        /// <summary>For the session's thumbnails: the state files of the game this ROM is.</summary>
        internal static IEnumerable<string> StatesOf(string exe, string rom)
        {
            var name = NameOf(ResolveFullPath(rom));
            return StateFilesIn(FoldersFor(exe, rom).StateDir, name).Select(s => s.Path).ToList();
        }

        /// <summary>One of our thumbnails, beside a state: "&lt;name&gt;.000.png".</summary>
        private static bool IsStateThumb(string path)
            => LbipStateThumbs.IsThumb(path) && StateFile.IsMatch(path.Substring(0, path.Length - LbipStateThumbs.Suffix.Length));

        private static GameSaveGame SaveRow(string path, string gameId, string appId, string groupId, string groupName = null, string chip = null)
        {
            long size = 0; DateTime when = default;
            try { var i = new FileInfo(path); size = i.Length; when = i.LastWriteTimeUtc; } catch { }
            return new GameSaveGame
            {
                GameId = gameId, AdditionalApplicationId = appId, FileLocation = path, IsDirectory = false,
                OriginalFileName = Path.GetFileName(path), SaveGroupId = groupId,
                SaveGroupName = groupName ?? SaveGroupName, DisplayChipText = chip ?? SaveChipText,
                ReportedFileSizeBytes = size > 0 ? size : (long?)null, ReportedLastModifiedUtc = when == default ? (DateTime?)null : when,
            };
        }

        private static GameSaveState StateRow(string path, string gameId, string appId, int slot, string groupId, string groupName)
        {
            long size = 0; DateTime when = default;
            try { var i = new FileInfo(path); size = i.Length; when = i.LastWriteTimeUtc; } catch { }
            return new GameSaveState
            {
                GameId = gameId, AdditionalApplicationId = appId, FileLocation = path, IsDirectory = false,
                OriginalFileName = Path.GetFileName(path), Slot = slot, SaveGroupId = groupId,
                SaveGroupName = groupName ?? StateGroupName, DisplayChipText = StateChipText,
                ReportedFileSizeBytes = size > 0 ? size : (long?)null, ReportedLastModifiedUtc = when == default ? (DateTime?)null : when,
            };
        }

        // ── the contract ─────────────────────────────────────────────────────

        /// <summary>Neither is a container: each is one file, which the host copies itself.</summary>
        bool IOwnSaves.OwnIsSaveContainer(GameSaveBase save) => false;

        bool IOwnSaves.OwnUseSaveGroupIdForPersistedMatch(GameSaveBase save) => IsOurs(save);

        bool IOwnSaves.OwnTryBackupSave(GameSaveBase save, string emulatorApplicationPath, string destinationFolder, out string error)
        {
            error = "A RASnes9x save is one file, not a container.";
            return false;
        }

        /// <summary>A plugin must never call its own save secondary - only a state's thumbnail, part of the state's save.</summary>
        bool IOwnSaves.OwnIsSecondarySaveFile(string filePath) => IsStateThumb(filePath);

        /// <summary>A cartridge save's clock (.rtc beside the .srm) travels with it; a state its thumbnail, the picture it
        /// carries made a PNG beside it (RaSnes9xStateThumbs).</summary>
        IReadOnlyList<string> IOwnSaves.OwnGetCompanionSaveFiles(string primaryFilePath)
        {
            try
            {
                if (primaryFilePath != null && StateFile.IsMatch(primaryFilePath)) return LbipStateThumbs.Companion(primaryFilePath);
                if (string.IsNullOrWhiteSpace(primaryFilePath) || !primaryFilePath.EndsWith(".srm", StringComparison.OrdinalIgnoreCase)) return Array.Empty<string>();
                var rtc = Path.ChangeExtension(primaryFilePath, ".rtc");
                return File.Exists(rtc) ? new[] { rtc } : Array.Empty<string>();
            }
            catch { return Array.Empty<string>(); }
        }

        /// <summary>Active when it is where Snes9x reads it now - the folder its settings name - and still there.</summary>
        bool IOwnSaves.OwnIsSaveActive(GameSaveBase save, string emulatorApplicationPath)
        {
            try
            {
                if (!IsOurs(save) || string.IsNullOrWhiteSpace(save.FileLocation)) return true;
                var exe = ResolveFullPath(emulatorApplicationPath);
                if (!RaSnes9xLaunch.IsRaSnes9xExe(exe) || !File.Exists(save.FileLocation)) return File.Exists(save.FileLocation);
                var target = TargetDirFor(save, exe);
                return target == null || string.Equals(Path.GetFullPath(Path.GetDirectoryName(save.FileLocation)).TrimEnd('\\'),
                                                       Path.GetFullPath(target).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);
            }
            catch { return true; }
        }

        // ── restore ──────────────────────────────────────────────────────────

        /// <summary>A vault copy back where Snes9x reads it, under its own file name.</summary>
        AddSaveResponse IOwnSaves.OwnAddSaveFile(AddSaveArgs args)
        {
            try
            {
                var save = args?.SaveToAdd;
                if (save == null) return new AddSaveResponse("No save was supplied.");
                var source = save.FileLocation;
                if (string.IsNullOrWhiteSpace(source) || !File.Exists(source)) return new AddSaveResponse("This RASnes9x backup is not a file: " + source);

                var game = PluginHelper.DataManager?.GetGameById(save.GameId);
                var exe = ExecutableOf(game);
                var to = SaveRows.TargetFor(save, PluginHelper.DataManager, Full);
                if (to.Warning != null) Log.Warn("restore of " + save.OriginalFileName + ": " + to.Warning);
                if (to.Refused) return new AddSaveResponse(to.Warning);
                var targetDir = exe == null ? null : TargetDirFor(save, exe, to.RomPath);
                if (targetDir == null) return new AddSaveResponse("Could not work out where RASnes9x reads this save from.");
                // The name of the rom it goes to TODAY, with the backup's own extension (.srm, .rtc, .003...).
                var original = new[] { save.OriginalFileName, Path.GetFileName(source) }.FirstOrDefault(n => !string.IsNullOrWhiteSpace(n));
                var romName = NameOf(to.RomPath);
                var name = romName.Length > 0 ? romName + Path.GetExtension(original) : original;
                if (RaSnes9xLaunch.Running(exe) && !(save is GameSaveState))
                    return new AddSaveResponse("RASnes9x is running: it would write the game's save over this one when it quits. Close it first.");
                // A state goes to the slot picked, when Snes9x has it (SlotRestore); the backup's own when none was.
                int slot = 0, own = -1;
                if (save is GameSaveState picked)
                {
                    var m = StateFile.Match(name);
                    own = SlotRestore.SlotOf(save.SaveGroupId) ?? (m.Success && int.TryParse(m.Groups["slot"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var f) ? f : -1);
                    var (s, refused) = SlotRestore.Choose(picked.Slot, own < 0 ? (int?)null : own, Enumerable.Range(0, 10).ToList(), "RASnes9x");
                    if (refused != null) { Log.Info("restore of " + original + " refused: " + refused); return new AddSaveResponse(refused); }
                    slot = s;
                    name = Path.GetFileNameWithoutExtension(name) + "." + slot.ToString("000", CultureInfo.InvariantCulture);
                }

                var target = Path.Combine(targetDir, name);
                // Over another card's save (another slot, or a card of LaunchBox's own - a manual import): LaunchBox
                // keeps no copy of it - asked first (SlotRestore).
                bool otherCard = !IsOurs(save) || (save is GameSaveState && slot != own);
                if (!SlotRestore.MayOverwrite(target, source, otherCard, save is GameSaveState ? "Slot " + slot : "The current save", Log.Info, out var no))
                    return new AddSaveResponse(no);
                if (File.Exists(target))
                {
                    bool overwrite = true;
                    try { overwrite = args.ShouldOverwriteFunc?.Invoke() ?? true; } catch { }
                    if (!overwrite) return new AddSaveResponse("Restore cancelled.");
                }
                // Everything it will write must be writable first - the clock beside the save too (SaveCompanions).
                var companions = save is GameSaveState ? null : ((IOwnSaves)this).OwnGetCompanionSaveFiles(source);
                var cannot = SaveCompanions.WhyNotWritable(source, target, companions);
                if (cannot != null) { Log.Info("restore of " + name + " refused: " + cannot); return new AddSaveResponse(cannot); }
                Directory.CreateDirectory(targetDir);
                if (save is GameSaveState) File.Copy(source, target, overwrite: true);
                // The cartridge's clock (.rtc) with its save, all or nothing: LaunchBox copies nothing back (SaveCompanions).
                else SaveCompanions.RestoreSet(source, target, companions, m => Log.Info(m));
                Log.Info("restored " + name + " -> " + targetDir);

                if (save is GameSaveState)
                {
                    // Its thumbnail with it; without one, the old would be a picture of another moment - made again from the state.
                    var thumb = GetCompanionSaveFiles(source).FirstOrDefault(LbipStateThumbs.IsThumb);
                    if (thumb != null) File.Copy(thumb, LbipStateThumbs.PathOf(target), overwrite: true);
                    else { LbipStateThumbs.Remove(target); RaSnes9xStateThumbs.Refresh(target); }
                }
                if (save is GameSaveState state)
                    return new AddSaveResponse(StateRow(target, state.GameId, state.AdditionalApplicationId, slot, SlotRestore.WithSlot(state.SaveGroupId, slot),
                                                        string.IsNullOrWhiteSpace(state.SaveGroupName) ? StateGroupName : state.SaveGroupName));
                return new AddSaveResponse(SaveRow(target, save.GameId, save.AdditionalApplicationId, save.SaveGroupId, save.SaveGroupName, save.DisplayChipText));
            }
            catch (Exception ex)
            {
                Log.Warn("AddSaveFile failed", ex);
                return new AddSaveResponse("Could not restore this RASnes9x save: " + ex.Message);
            }
        }

        /// <summary>The folder a save of this kind belongs in for this rom - the row's version's or its game's
        /// (SaveRows.TargetFor) - as Snes9x's settings have it.</summary>
        private static string TargetDirFor(GameSaveBase save, string exe, string rom = null)
        {
            rom ??= SaveRows.TargetFor(save, PluginHelper.DataManager, Full).RomPath;
            var folders = FoldersFor(exe, rom);
            return save is GameSaveState ? folders.StateDir : folders.SaveDir;
        }

        /// <summary>A path as LaunchBox stores it, made full; null for nothing.</summary>
        private static string Full(string path) => string.IsNullOrWhiteSpace(path) ? null : ResolveFullPath(path);

        /// <summary>The RASnes9x a game runs on: its own emulator when that is one, else the first in the library.</summary>
        private static string ExecutableOf(IGame game)
        {
            try
            {
                var dm = PluginHelper.DataManager;
                var id = Safe(() => game?.EmulatorId);
                var own = string.IsNullOrEmpty(id) ? null : dm?.GetEmulatorById(id);
                var path = Safe(() => own?.ApplicationPath);
                if (!RaSnes9xLaunch.IsRaSnes9xExe(path))
                    path = Safe(() => (LbIntegrations.Saves.SaveForward.ViaEmulator(RaSnes9xLaunch.IsRaSnes9xExe)   // through a desk: the save's install
                                       ?? dm?.GetAllEmulators()?.FirstOrDefault(e => RaSnes9xLaunch.IsRaSnes9xExe(Safe(() => e.ApplicationPath))))?.ApplicationPath);
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
                if (save is GameSaveState) LbipStateThumbs.Remove(loc);
                Log.Info("deleted " + Path.GetFileName(loc));
                return new PluginResponse(true);
            }
            catch (Exception ex)
            {
                Log.Warn("RemoveSave failed", ex);
                return new PluginResponse(false, "Could not delete this RASnes9x save: " + ex.Message);
            }
        }

        private static bool IsOurs(GameSaveBase save) => SaveRows.IsOurs(save?.SaveGroupId, Emu);

        private static string Safe(Func<string> read)
        {
            try { return read(); } catch { return null; }
        }
    }
}
