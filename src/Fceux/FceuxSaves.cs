// Save management for FCEUX - the battery save, the disk save and the save states, one FILE each (RaSnes9xSaves is the
// model of this file).
//
//   Battery save    <rom>.sav            "odnonvol" = <FCEUX>\Saves\<platform>             a cartridge's
//   FDS disk save   <rom>.fds            the same folder                                   the WHOLE modified disk (fds.cpp:906)
//   Save states     <rom>.fc0 .. .fc9    "odstates" = <FCEUX>\Saves\<platform>\States      one file per slot - one row each
//                                                                                          (and <rom>.fc0.png beside each:
//                                                                                          its picture, FceuxStateThumbs)
//   not saves       <rom>-bak.fcN (the slot's previous content), <rom>.bak.fc0 (the undo of a load), <rom>-resume.fcs,
//                   <rom>-autosaveN.fcs - FCEUX writes them by itself, they are replaced as it plays
//
// Read in FCEUX's code at the fceux-2.7.0 tag (file.cpp FCEU_MakeFName, state.cpp, fds.cpp) and measured 18/10 in the lab
// (docs\fceux.md section 8). <rom> is FileBase: the file's name without its extension - FOR AN ARCHIVE, THE ROM INSIDE IT
// (FceuxSavePaths.NameOf).
//
// THE FOLDERS: FCEUX's settings are pointed at the game's platform at each launch (FceuxSavePaths) - so a game's files are
// in <FCEUX>\Saves\<its platform>\, whatever platform was launched last. A folder the user chose in FCEUX is the user's,
// for every platform: listed and restored there.
//
// THE IDS are the pack's JSON (Shared.Saves, docs\saves-lb14-routing.md §3): {"emu":"fceux","kind":"save"|"state",
// "sys":"nes"|"fds","by":"name","id":"<rom>"} and, for a state, its "slot" - "sys" because a disk and a cartridge can share
// a base name. A restore goes to the version the row is tied to, else the game, and takes the name of THAT rom today.

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

namespace LbIntegrations.Fceux
{
    public partial class FceuxPlugin
    {
        /// <summary>The front desk (Shared.Saves SaveDesk): this plugin's emu, and where it logs.</summary>
        private string SaveEmu => Emu;
        private void SaveLog(string message) => Log.Info(message);

        /// <summary>This plugin's "emu" in the ids (Shared.Saves) - never reused by another plugin.</summary>
        internal const string Emu = "fceux";
        private const string SaveGroupName = "Cartridge save";
        private const string SaveChipText = "SAV";
        private const string DiskGroupName = "Disk save";
        private const string DiskChipText = "DISK";
        private const string StateGroupName = "Save states";
        private const string StateChipText = "STATE";

        /// <summary>A state of a slot: "&lt;rom&gt;.fc0" .. ".fc9" - never "-bak.fcN" nor ".bak.fc0" (their own base names).</summary>
        private static readonly Regex StateFile = new Regex(@"\.fc(?<slot>\d)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        public override bool SupportsSaveManagement() => true;

        /// <summary>FCEUX's ten slots, .fc0 to .fc9 (state.cpp, one file each).</summary>
        public override IReadOnlyDictionary<int, string> GetPotentialSaveSlots()
        {
            var slots = new Dictionary<int, string>();
            for (int i = 0; i <= 9; i++) slots[i] = i.ToString(CultureInfo.InvariantCulture);
            return slots;
        }

        // ── where FCEUX keeps a game's ───────────────────────────────────────

        internal sealed class Folders { public string SaveDir, StateDir; }

        /// <summary>The folders of a game of <paramref name="platform"/>: the pack's for its platform, or the user's own.</summary>
        internal static Folders FoldersFor(string exe, string platform)
        {
            var exeDir = Path.GetDirectoryName(Path.GetFullPath(exe));
            // A big listing reads fceux.cfg once, not once per game (SaveScan), and Game Saves while it is unchanged.
            var cfg = SaveScan.Memo("fceux.cfg|" + exe, () => StampedCache.Get(FceuxCfg.Of(exe), "fceux.cfg", () => FceuxCfg.Load(FceuxCfg.Of(exe))));
            string Dir(string key, string sub)
            {
                var v = cfg.Get(key);
                return FceuxSavePaths.IsPacks(v, exeDir) ? FceuxSavePaths.PackDir(exe, platform, sub) : FceuxSavePaths.DirOf(cfg, exe, key);
            }
            return new Folders { SaveDir = Dir(FceuxCfg.SaveDir, ""), StateDir = Dir(FceuxCfg.StateDir, "States") };
        }

        /// <summary>FileBase of a game, read once per listing (an archive is opened to find the ROM inside).</summary>
        internal static string NameOf(string rom) => SaveScan.Memo("fceux.name|" + rom, () => FceuxSavePaths.NameOf(rom));

        // ── listing ──────────────────────────────────────────────────────────

        GetSavesResponse IOwnSaves.OwnGetSaves(GetSavesArgs args)
        {
            try
            {
                if (args?.Emulator == null) return new GetSavesResponse("No emulator was supplied.");
                var exe = ResolveFullPath(Safe(() => args.Emulator.ApplicationPath));
                if (!FceuxLaunch.IsFceuxExe(exe)) return new GetSavesResponse("This emulator is not FCEUX.");

                var found = new List<GameSaveBase>();
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                int states = 0;
                foreach (var app in args.AdditionalApplications ?? (IReadOnlyCollection<IAdditionalApplication>)Array.Empty<IAdditionalApplication>())
                {
                    var gameId = Safe(() => app.GameId);
                    var game = args.Games?.FirstOrDefault(g => Safe(() => g.Id) == gameId) ?? PluginHelper.DataManager?.GetGameById(gameId);
                    states += Collect(found, seen, exe, Safe(() => app.ApplicationPath), gameId, Safe(() => game?.Platform),
                                      SaveRows.TieTo(Safe(() => app.Id), Safe(() => game?.ApplicationPath), Safe(() => app.ApplicationPath), Full));
                }
                foreach (var game in args.Games ?? (IReadOnlyCollection<IGame>)Array.Empty<IGame>())
                    states += Collect(found, seen, exe, Safe(() => game.ApplicationPath), Safe(() => game.Id), Safe(() => game.Platform), null);

                Log.Verbose("found " + (found.Count - states) + " save(s) and " + states + " state(s) for " + exe);
                return new GetSavesResponse(found);
            }
            catch (Exception ex)
            {
                Log.Warn("GetSaves failed", ex);
                return new GetSavesResponse("Could not read FCEUX saves: " + ex.Message);
            }
        }

        /// <summary>Everything one game owns. Returns how many of them were states.</summary>
        private static int Collect(List<GameSaveBase> into, HashSet<string> seen, string exe, string romPath, string gameId, string platform, string appId)
        {
            if (string.IsNullOrWhiteSpace(gameId) || string.IsNullOrWhiteSpace(romPath)) return 0;
            var rom = ResolveFullPath(romPath);
            var name = NameOf(rom);
            if (string.IsNullOrEmpty(name)) return 0;
            var sys = FceuxSystems.SystemFor(platform, rom, Archives.IsArchive(rom) ? Archives.RomInside(rom) : null).Key;
            var folders = FoldersFor(exe, platform);
            var context = appId ?? gameId;

            var sav = folders.SaveDir == null ? null : Path.Combine(folders.SaveDir, name + ".sav");
            if (sav != null && SaveScan.Exists(sav) && seen.Add("save|" + sav + "|" + context))
                into.Add(SaveRow(sav, gameId, appId, IdOf("save", sys, name), SaveGroupName, SaveChipText));
            // A disk's save: the whole modified disk, under the game's name with .fds - only a disk writes one.
            var disk = folders.SaveDir == null ? null : Path.Combine(folders.SaveDir, name + ".fds");
            if (disk != null && sys == FceuxSystems.Fds.Key && SaveScan.Exists(disk) && seen.Add("save|" + disk + "|" + context))
                into.Add(SaveRow(disk, gameId, appId, IdOf("save", sys, name), DiskGroupName, DiskChipText));

            int states = 0;
            foreach (var (path, slot) in StateFilesIn(folders.StateDir, name))
            {
                if (!seen.Add("state|" + path + "|" + context)) continue;
                FceuxStateThumbs.Refresh(path, exe);
                into.Add(StateRow(path, gameId, appId, slot, StateIdOf(sys, name, slot), StateGroupName));
                states++;
            }
            return states;
        }

        private static string IdOf(string kind, string sys, string name) => SaveId.Write(new SaveIdData { Emu = Emu, Kind = kind, Sys = sys, By = "name", Id = name });
        private static string StateIdOf(string sys, string name, int slot) => SaveId.Write(new SaveIdData { Emu = Emu, Kind = "state", Sys = sys, By = "name", Id = name, Slot = slot });

        /// <summary>A game's states in a folder: &lt;rom&gt;.fc0 to .fc9 - never the thumbnail beside one ("x.fc0.png" is
        /// "x.fc0" without its extension), nor FCEUX's backups ("x-bak.fc0", "x.bak.fc0" have other base names), nor another
        /// game's ("Zelda II 2.fc0").</summary>
        private static IEnumerable<(string Path, int Slot)> StateFilesIn(string dir, string name)
        {
            if (dir == null || string.IsNullOrEmpty(name) || !SaveScan.DirExists(dir)) yield break;
            foreach (var path in SaveScan.Files(dir, name + ".fc*"))
            {
                if (!string.Equals(Path.GetFileNameWithoutExtension(path), name, StringComparison.OrdinalIgnoreCase)) continue;
                var m = StateFile.Match(path);
                if (!m.Success || !int.TryParse(m.Groups["slot"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var slot)) continue;
                yield return (path, slot);
            }
        }

        /// <summary>For the session's thumbnails: the state files of the game this ROM is, on that platform.</summary>
        internal static IEnumerable<string> StatesOf(string exe, string rom, string platform)
        {
            var name = FceuxSavePaths.NameOf(ResolveFullPath(rom));
            return StateFilesIn(FoldersFor(exe, platform).StateDir, name).Select(s => s.Path).ToList();
        }

        /// <summary>One of our thumbnails, beside a state: "&lt;rom&gt;.fc0.png".</summary>
        private static bool IsStateThumb(string path)
            => LbipStateThumbs.IsThumb(path) && StateFile.IsMatch(path.Substring(0, path.Length - LbipStateThumbs.Suffix.Length));

        private static GameSaveGame SaveRow(string path, string gameId, string appId, string groupId, string groupName, string chip)
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

        /// <summary>None is a container: each is one file, which the host copies itself.</summary>
        bool IOwnSaves.OwnIsSaveContainer(GameSaveBase save) => false;

        bool IOwnSaves.OwnUseSaveGroupIdForPersistedMatch(GameSaveBase save) => IsOurs(save);

        bool IOwnSaves.OwnTryBackupSave(GameSaveBase save, string emulatorApplicationPath, string destinationFolder, out string error)
        {
            error = "An FCEUX save is one file, not a container.";
            return false;
        }

        /// <summary>A plugin must never call its own save secondary - only a state's thumbnail, part of the state's save.</summary>
        bool IOwnSaves.OwnIsSecondarySaveFile(string filePath) => IsStateThumb(filePath);

        /// <summary>A state's thumbnail travels with it; a battery save and a disk save have no companion.</summary>
        IReadOnlyList<string> IOwnSaves.OwnGetCompanionSaveFiles(string primaryFilePath)
        {
            try { return primaryFilePath != null && StateFile.IsMatch(primaryFilePath) ? LbipStateThumbs.Companion(primaryFilePath) : Array.Empty<string>(); }
            catch { return Array.Empty<string>(); }
        }

        /// <summary>Active when it is where FCEUX reads it now for that game's platform, and still there.</summary>
        bool IOwnSaves.OwnIsSaveActive(GameSaveBase save, string emulatorApplicationPath)
        {
            try
            {
                if (!IsOurs(save) || string.IsNullOrWhiteSpace(save.FileLocation)) return true;
                var exe = ResolveFullPath(emulatorApplicationPath);
                if (!FceuxLaunch.IsFceuxExe(exe) || !File.Exists(save.FileLocation)) return File.Exists(save.FileLocation);
                var target = TargetDirFor(save, exe);
                return target == null || string.Equals(Path.GetFullPath(Path.GetDirectoryName(save.FileLocation)).TrimEnd('\\'),
                                                       Path.GetFullPath(target).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);
            }
            catch { return true; }
        }

        // ── restore ──────────────────────────────────────────────────────────

        /// <summary>A vault copy back where FCEUX reads it, under its own file name.</summary>
        AddSaveResponse IOwnSaves.OwnAddSaveFile(AddSaveArgs args)
        {
            try
            {
                var save = args?.SaveToAdd;
                if (save == null) return new AddSaveResponse("No save was supplied.");
                var source = save.FileLocation;
                if (string.IsNullOrWhiteSpace(source) || !File.Exists(source)) return new AddSaveResponse("This FCEUX backup is not a file: " + source);

                var game = PluginHelper.DataManager?.GetGameById(save.GameId);
                var exe = ExecutableOf(game);
                var to = SaveRows.TargetFor(save, PluginHelper.DataManager, Full);
                if (to.Warning != null) Log.Warn("restore of " + save.OriginalFileName + ": " + to.Warning);
                if (to.Refused) return new AddSaveResponse(to.Warning);
                var platform = Safe(() => game?.Platform);
                var targetDir = exe == null ? null : TargetDirFor(save, exe, platform);
                if (targetDir == null) return new AddSaveResponse("Could not work out where FCEUX reads this save from.");
                // The name of the rom it goes to TODAY (the ROM inside an archive), with the backup's own extension (.sav, .fds, .fc3).
                var original = new[] { save.OriginalFileName, Path.GetFileName(source) }.FirstOrDefault(n => !string.IsNullOrWhiteSpace(n));
                var romName = FceuxSavePaths.NameOf(to.RomPath) ?? "";
                var name = romName.Length > 0 ? romName + Path.GetExtension(original) : original;
                if (FceuxLaunch.Running(exe) && !(save is GameSaveState))
                    return new AddSaveResponse("FCEUX is running: it would write the game's own over this one when it quits. Close it first.");
                // A state goes to the slot picked, when FCEUX has it (SlotRestore); the backup's own when none was.
                int slot = 0, own = -1;
                if (save is GameSaveState picked)
                {
                    var m = StateFile.Match(name);
                    own = SlotRestore.SlotOf(save.SaveGroupId) ?? (m.Success && int.TryParse(m.Groups["slot"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var f) ? f : -1);
                    var (s, refused) = SlotRestore.Choose(picked.Slot, own < 0 ? (int?)null : own, Enumerable.Range(0, 10).ToList(), "FCEUX");
                    if (refused != null) { Log.Info("restore of " + original + " refused: " + refused); return new AddSaveResponse(refused); }
                    slot = s;
                    name = Path.GetFileNameWithoutExtension(name) + ".fc" + slot.ToString(CultureInfo.InvariantCulture);
                }

                var target = Path.Combine(targetDir, name);
                // Over another card's save (another slot, or a card of LaunchBox's own - a manual import): LaunchBox
                // keeps no copy of it - asked first (SlotRestore).
                bool otherCard = !IsOurs(save) || (save is GameSaveState && slot != own) || SlotRestore.OverAnotherFile(target, save);
                if (!SlotRestore.MayOverwrite(target, source, otherCard, save is GameSaveState ? "Slot " + slot : "The current save", Log.Info, out var no))
                    return new AddSaveResponse(no);
                if (File.Exists(target))
                {
                    bool overwrite = true;
                    try { overwrite = args.ShouldOverwriteFunc?.Invoke() ?? true; } catch { }
                    if (!overwrite) return new AddSaveResponse("Restore cancelled.");
                }
                var cannot = SaveCompanions.WhyNotWritable(source, target, null);
                if (cannot != null) { Log.Info("restore of " + name + " refused: " + cannot); return new AddSaveResponse(cannot); }
                Directory.CreateDirectory(targetDir);
                File.Copy(source, target, overwrite: true);
                Log.Info("restored " + name + " -> " + targetDir);

                if (save is GameSaveState state)
                {
                    // Its thumbnail with it; without one, the old would be a picture of another moment - made again from the state.
                    var thumb = GetCompanionSaveFiles(source).FirstOrDefault(LbipStateThumbs.IsThumb);
                    if (thumb != null) File.Copy(thumb, LbipStateThumbs.PathOf(target), overwrite: true);
                    else { LbipStateThumbs.Remove(target); FceuxStateThumbs.Refresh(target, exe); }
                    return new AddSaveResponse(StateRow(target, state.GameId, state.AdditionalApplicationId, slot, SlotRestore.WithSlot(state.SaveGroupId, slot),
                                                        string.IsNullOrWhiteSpace(state.SaveGroupName) ? StateGroupName : state.SaveGroupName));
                }
                return new AddSaveResponse(SaveRow(target, save.GameId, save.AdditionalApplicationId, save.SaveGroupId, save.SaveGroupName, save.DisplayChipText));
            }
            catch (Exception ex)
            {
                Log.Warn("AddSaveFile failed", ex);
                return new AddSaveResponse("Could not restore this FCEUX save: " + ex.Message);
            }
        }

        /// <summary>The folder a save of this kind belongs in for its game's platform - where FCEUX reads it at that game's
        /// next launch.</summary>
        private static string TargetDirFor(GameSaveBase save, string exe, string platform = null)
        {
            platform ??= Safe(() => PluginHelper.DataManager?.GetGameById(save?.GameId)?.Platform);
            var folders = FoldersFor(exe, platform);
            return save is GameSaveState ? folders.StateDir : folders.SaveDir;
        }

        LbIntegrations.Saves.ExpectedDir IOwnSaves.OwnExpectedDir(IEmulator emulator, GameSaveBase save)
        {
            var path = Safe(() => emulator?.ApplicationPath);
            if (save == null || !FceuxLaunch.IsFceuxExe(path)) return LbIntegrations.Saves.ExpectedDir.Unknown("Could not find the FCEUX this game runs on.");
            var to = SaveRows.TargetFor(save, PluginHelper.DataManager, Full);
            if (to.Refused) return LbIntegrations.Saves.ExpectedDir.Unknown(to.Warning);
            return LbIntegrations.Saves.ExpectedDir.FolderOr(TargetDirFor(save, ResolveFullPath(path)), "Could not work out where FCEUX reads this save from.");
        }

        /// <summary>A path as LaunchBox stores it, made full; null for nothing.</summary>
        private static string Full(string path) => string.IsNullOrWhiteSpace(path) ? null : ResolveFullPath(path);

        /// <summary>The FCEUX a game runs on: its own emulator when that is one, else the first in the library.</summary>
        private static string ExecutableOf(IGame game)
        {
            try
            {
                var dm = PluginHelper.DataManager;
                var id = Safe(() => game?.EmulatorId);
                // The install a restore was sent to first (SaveForward.WithVia: the choice among two installs, docs\saves-two-installs.md §4).
                var own = LbIntegrations.Saves.SaveForward.ViaEmulator(FceuxLaunch.IsFceuxExe) ?? (string.IsNullOrEmpty(id) ? null : dm?.GetEmulatorById(id));
                var path = Safe(() => own?.ApplicationPath);
                if (!FceuxLaunch.IsFceuxExe(path))
                    path = Safe(() => (LbIntegrations.Saves.SaveForward.ViaEmulator(FceuxLaunch.IsFceuxExe)   // through a desk: the save's install
                                       ?? dm?.GetAllEmulators()?.FirstOrDefault(e => FceuxLaunch.IsFceuxExe(Safe(() => e.ApplicationPath))))?.ApplicationPath);
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
                return new PluginResponse(false, "Could not delete this FCEUX save: " + ex.Message);
            }
        }

        private static bool IsOurs(GameSaveBase save) => SaveRows.IsOurs(save?.SaveGroupId, Emu);
    }
}
