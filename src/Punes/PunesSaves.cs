// Save management for puNES - the cartridge save, the disk writes and the save states, one FILE each (RaSnes9xSaves is
// the model of this file).
//
//   Cartridge save   <name>.prb            in prb\     the cartridge's battery RAM, written when puNES quits
//   Disk writes      <name>.ipf (.ipq)     in diff\    a Famicom Disk System disk's changes ("write mode=diff file", the
//                                                      default: puNES never writes into the disk image itself)
//   Save states      <name>.p00 ... .p0B   in save\    the twelve slots, 0-9, A, B (F1 saves, F4 loads, F2 / F3 the slot);
//                                                      an NSF's are .n00 ... .n0B. And <name>.p00.png beside each: the
//                                                      picture the state carries (PunesStateThumbs) - its companion
//
// Read in puNES's code at v0.111 (common.h:35-48, memmap.c:1898-1910, save_slot.c:759-795, fds.c) and measured 18/10 in
// the lab (docs\punes.md sections 3, 8, 9): <name> is the base name of the file puNES was given - for an archive the file
// inside, which the plugin makes the archive's own name (PunesLaunch.RomFor: given as it is only when the one file in it
// is named after the archive, else unpacked under the archive's name).
//
// THE FOLDERS are <puNES>\Saves\<the game's platform>\prb (save, diff): puNES's own four folders are junctions pointed
// there before each launch (PunesSaveDirs). A listing looks in the platform's folder and in puNES's own when that is not
// one of ours - a folder of the user's, or a real one not moved yet (PunesSaveDirs.Dirs); a restore goes where puNES
// reads it at that game's next launch (PunesSaveDirs.Target).
//
// THE IDS are the pack's JSON (Shared.Saves, docs\saves-lb14-routing.md §3): {"emu":"punes","kind":"save"|"disk"|"state",
// "sys":"nes"|"fds","by":"name","id":"<name>"} and, for a state, its "slot". A restore goes to the version the row is
// tied to, else the game, and takes the name of THAT rom today.

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

namespace LbIntegrations.Punes
{
    public partial class PunesPlugin
    {
        /// <summary>The front desk (Shared.Saves SaveDesk): this plugin's emu, and where it logs.</summary>
        private string SaveEmu => Emu;
        private void SaveLog(string message) => Log.Info(message);

        /// <summary>This plugin's "emu" in the ids (Shared.Saves).</summary>
        private const string Emu = "punes";
        private const string SaveGroupName = "Cartridge save";
        private const string SaveChipText = "PRB";
        private const string DiskGroupName = "Disk writes";
        private const string DiskChipText = "DISK";
        private const string StateGroupName = "Save states";
        private const string StateChipText = "STATE";

        /// <summary>puNES's twelve slots (SAVE_SLOTS, save_slot.h:27).</summary>
        private const int Slots = 12;

        /// <summary>A state's extension: ".p" (".n" for an NSF) and the slot in two hex digits (save_slot.c: "%02X").</summary>
        private static readonly Regex StateFile = new Regex(@"\.(?<kind>[pn])(?<slot>[0-9A-Fa-f]{2})$", RegexOptions.CultureInvariant);

        public override bool SupportsSaveManagement() => true;

        /// <summary>The slots as puNES shows them: 0 to 9, then A and B.</summary>
        public override IReadOnlyDictionary<int, string> GetPotentialSaveSlots()
        {
            var slots = new Dictionary<int, string>();
            for (int i = 0; i < Slots; i++) slots[i] = i.ToString("X", CultureInfo.InvariantCulture);
            return slots;
        }

        internal static string NameOf(string romPath) => Path.GetFileNameWithoutExtension(romPath ?? "");

        /// <summary>The system a game's files are of: what the launch would say, from its file then its platform.</summary>
        private static string SysOf(string platform, string rom) => PunesSystems.SystemFor(platform, rom).System?.Id ?? PunesSystems.Platforms[0].Default;

        // ── listing ──────────────────────────────────────────────────────────

        GetSavesResponse IOwnSaves.OwnGetSaves(GetSavesArgs args)
        {
            try
            {
                if (args?.Emulator == null) return new GetSavesResponse("No emulator was supplied.");
                var exe = ResolveFullPath(Safe(() => args.Emulator.ApplicationPath));
                if (!PunesLaunch.IsPunesExe(exe)) return new GetSavesResponse("This emulator is not puNES.");

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
                return new GetSavesResponse("Could not read puNES saves: " + ex.Message);
            }
        }

        /// <summary>Everything one game owns. Returns how many of them were states.</summary>
        private static int Collect(List<GameSaveBase> into, HashSet<string> seen, string exe, string romPath, string gameId, string platform, string appId)
        {
            if (string.IsNullOrWhiteSpace(gameId) || string.IsNullOrWhiteSpace(romPath)) return 0;
            var rom = ResolveFullPath(romPath);
            var name = NameOf(rom);
            if (name.Length == 0) return 0;
            var sys = SysOf(platform, rom);
            var context = appId ?? gameId;

            foreach (var dir in PunesSaveDirs.Dirs(exe, platform, PunesSaveDirs.Prb))
            {
                var prb = Path.Combine(dir, name + ".prb");
                if (SaveScan.Exists(prb) && seen.Add("save|" + prb + "|" + context))
                    into.Add(SaveRow(prb, gameId, appId, IdOf("save", sys, name), SaveGroupName, SaveChipText));
            }
            foreach (var dir in PunesSaveDirs.Dirs(exe, platform, PunesSaveDirs.Diff))
                foreach (var ext in new[] { ".ipf", ".ipq" })
                {
                    var disk = Path.Combine(dir, name + ext);
                    if (SaveScan.Exists(disk) && seen.Add("disk|" + disk + "|" + context))
                        into.Add(SaveRow(disk, gameId, appId, IdOf("disk", sys, name), DiskGroupName, DiskChipText));
                }

            int states = 0;
            foreach (var dir in PunesSaveDirs.Dirs(exe, platform, PunesSaveDirs.States))
                foreach (var (path, slot) in StateFilesIn(dir, name))
                {
                    if (!seen.Add("state|" + path + "|" + context)) continue;
                    PunesStateThumbs.Refresh(path);
                    into.Add(StateRow(path, gameId, appId, slot, StateIdOf(sys, name, slot), StateGroupName));
                    states++;
                }
            return states;
        }

        private static string IdOf(string kind, string sys, string name) => SaveId.Write(new SaveIdData { Emu = Emu, Kind = kind, Sys = sys, By = "name", Id = name });
        private static string StateIdOf(string sys, string name, int slot) => SaveId.Write(new SaveIdData { Emu = Emu, Kind = "state", Sys = sys, By = "name", Id = name, Slot = slot });

        /// <summary>A game's states in one folder: &lt;name&gt;.p00 to .p0B (.n.. for an NSF) - never the thumbnail beside a
        /// state ("Zelda.p00.png" is "Zelda.p00" without its extension), nor another game's ("Zelda 2.p00").</summary>
        private static IEnumerable<(string Path, int Slot)> StateFilesIn(string dir, string name)
        {
            if (dir == null || string.IsNullOrEmpty(name) || !SaveScan.DirExists(dir)) yield break;
            foreach (var path in SaveScan.Files(dir, name + ".*"))
            {
                if (!string.Equals(Path.GetFileNameWithoutExtension(path), name, StringComparison.OrdinalIgnoreCase)) continue;
                var m = StateFile.Match(path);
                if (!m.Success || !int.TryParse(m.Groups["slot"].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var slot) || slot >= Slots) continue;
                yield return (path, slot);
            }
        }

        /// <summary>For the session's thumbnails: the state files of the game this ROM is, on that platform.</summary>
        internal static IEnumerable<string> StatesOf(string exe, string rom, string platform)
        {
            var name = NameOf(ResolveFullPath(rom));
            return PunesSaveDirs.Dirs(exe, platform, PunesSaveDirs.States).SelectMany(d => StateFilesIn(d, name)).Select(s => s.Path).ToList();
        }

        /// <summary>One of our thumbnails, beside a state: "&lt;name&gt;.p00.png".</summary>
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

        /// <summary>The kind of folder a row of ours lives in: its id's kind, else its extension.</summary>
        private static string KindOf(GameSaveBase save)
        {
            if (save is GameSaveState) return PunesSaveDirs.States;
            if (SaveId.TryRead(save?.SaveGroupId, out var d) && d.Kind == "disk") return PunesSaveDirs.Diff;
            var ext = Path.GetExtension(save?.OriginalFileName ?? save?.FileLocation ?? "");
            return string.Equals(ext, ".ipf", StringComparison.OrdinalIgnoreCase) || string.Equals(ext, ".ipq", StringComparison.OrdinalIgnoreCase)
                ? PunesSaveDirs.Diff : PunesSaveDirs.Prb;
        }

        // ── the contract ─────────────────────────────────────────────────────

        /// <summary>None is a container: each is one file, which the host copies itself.</summary>
        bool IOwnSaves.OwnIsSaveContainer(GameSaveBase save) => false;

        bool IOwnSaves.OwnUseSaveGroupIdForPersistedMatch(GameSaveBase save) => IsOurs(save);

        bool IOwnSaves.OwnTryBackupSave(GameSaveBase save, string emulatorApplicationPath, string destinationFolder, out string error)
        {
            error = "A puNES save is one file, not a container.";
            return false;
        }

        /// <summary>A plugin must never call its own save secondary - only a state's thumbnail, part of the state's save.</summary>
        bool IOwnSaves.OwnIsSecondarySaveFile(string filePath) => IsStateThumb(filePath);

        /// <summary>A state's thumbnail travels with it; a cartridge save and a disk's writes have no companion.</summary>
        IReadOnlyList<string> IOwnSaves.OwnGetCompanionSaveFiles(string primaryFilePath)
        {
            try
            {
                if (primaryFilePath != null && StateFile.IsMatch(primaryFilePath)) return LbipStateThumbs.Companion(primaryFilePath);
                return Array.Empty<string>();
            }
            catch { return Array.Empty<string>(); }
        }

        /// <summary>Active when it is where puNES reads it now for that game's platform, and still there.</summary>
        bool IOwnSaves.OwnIsSaveActive(GameSaveBase save, string emulatorApplicationPath)
        {
            try
            {
                if (!IsOurs(save) || string.IsNullOrWhiteSpace(save.FileLocation)) return true;
                var exe = ResolveFullPath(emulatorApplicationPath);
                if (!PunesLaunch.IsPunesExe(exe) || !File.Exists(save.FileLocation)) return File.Exists(save.FileLocation);
                var target = TargetDirFor(save, exe);
                return target == null || string.Equals(Path.GetFullPath(Path.GetDirectoryName(save.FileLocation)).TrimEnd('\\'),
                                                       Path.GetFullPath(target).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);
            }
            catch { return true; }
        }

        // ── restore ──────────────────────────────────────────────────────────

        /// <summary>A vault copy back where puNES reads it, under its own file name.</summary>
        AddSaveResponse IOwnSaves.OwnAddSaveFile(AddSaveArgs args)
        {
            try
            {
                var save = args?.SaveToAdd;
                if (save == null) return new AddSaveResponse("No save was supplied.");
                var source = save.FileLocation;
                if (string.IsNullOrWhiteSpace(source) || !File.Exists(source)) return new AddSaveResponse("This puNES backup is not a file: " + source);

                var game = PluginHelper.DataManager?.GetGameById(save.GameId);
                var exe = ExecutableOf(game);
                var to = SaveRows.TargetFor(save, PluginHelper.DataManager, Full);
                if (to.Warning != null) Log.Warn("restore of " + save.OriginalFileName + ": " + to.Warning);
                if (to.Refused) return new AddSaveResponse(to.Warning);
                var platform = Safe(() => game?.Platform);
                var targetDir = exe == null ? null : TargetDirFor(save, exe, to.RomPath, platform);
                if (targetDir == null) return new AddSaveResponse("Could not work out where puNES reads this save from.");
                // The name of the rom it goes to TODAY, with the backup's own extension (.prb, .ipf, .p03...).
                var original = new[] { save.OriginalFileName, Path.GetFileName(source) }.FirstOrDefault(n => !string.IsNullOrWhiteSpace(n));
                var romName = NameOf(to.RomPath);
                var name = romName.Length > 0 ? romName + Path.GetExtension(original) : original;
                if (PunesLaunch.Running(exe) && !(save is GameSaveState))
                    return new AddSaveResponse("puNES is running: it would write the game's own over this one when it quits. Close it first.");
                // A state goes to the slot picked, when puNES has it (SlotRestore); the backup's own when none was.
                int slot = 0, own = -1;
                if (save is GameSaveState picked)
                {
                    var m = StateFile.Match(name);
                    own = SlotRestore.SlotOf(save.SaveGroupId) ?? (m.Success && int.TryParse(m.Groups["slot"].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var f) ? f : -1);
                    var (s, refused) = SlotRestore.Choose(picked.Slot, own < 0 ? (int?)null : own, Enumerable.Range(0, Slots).ToList(), "puNES");
                    if (refused != null) { Log.Info("restore of " + original + " refused: " + refused); return new AddSaveResponse(refused); }
                    slot = s;
                    var letter = m.Success ? m.Groups["kind"].Value : "p";
                    name = Path.GetFileNameWithoutExtension(name) + "." + letter + slot.ToString("X2", CultureInfo.InvariantCulture);
                }

                var target = Path.Combine(targetDir, name);
                // Over another card's save (another slot, or a card of LaunchBox's own - a manual import): LaunchBox
                // keeps no copy of it - asked first (SlotRestore).
                bool otherCard = !IsOurs(save) || (save is GameSaveState && slot != own) || SlotRestore.OverAnotherFile(target, save);
                if (!SlotRestore.MayOverwrite(target, source, otherCard, save is GameSaveState ? "Slot " + slot.ToString("X", CultureInfo.InvariantCulture) : "The current save", Log.Info, out var no))
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
                    else { LbipStateThumbs.Remove(target); PunesStateThumbs.Refresh(target); }
                    return new AddSaveResponse(StateRow(target, state.GameId, state.AdditionalApplicationId, slot, SlotRestore.WithSlot(state.SaveGroupId, slot),
                                                        string.IsNullOrWhiteSpace(state.SaveGroupName) ? StateGroupName : state.SaveGroupName));
                }
                return new AddSaveResponse(SaveRow(target, save.GameId, save.AdditionalApplicationId, save.SaveGroupId, save.SaveGroupName, save.DisplayChipText));
            }
            catch (Exception ex)
            {
                Log.Warn("AddSaveFile failed", ex);
                return new AddSaveResponse("Could not restore this puNES save: " + ex.Message);
            }
        }

        /// <summary>The folder a save of this kind belongs in for this rom and its game's platform - where puNES reads it at
        /// that game's next launch (PunesSaveDirs.Target).</summary>
        private static string TargetDirFor(GameSaveBase save, string exe, string rom = null, string platform = null)
        {
            platform ??= Safe(() => PluginHelper.DataManager?.GetGameById(save?.GameId)?.Platform);
            return PunesSaveDirs.Target(exe, platform, KindOf(save));
        }

        LbIntegrations.Saves.ExpectedDir IOwnSaves.OwnExpectedDir(IEmulator emulator, GameSaveBase save)
        {
            var path = Safe(() => emulator?.ApplicationPath);
            if (save == null || !PunesLaunch.IsPunesExe(path)) return LbIntegrations.Saves.ExpectedDir.Unknown("Could not find the puNES this game runs on.");
            var to = SaveRows.TargetFor(save, PluginHelper.DataManager, Full);
            if (to.Refused) return LbIntegrations.Saves.ExpectedDir.Unknown(to.Warning);
            return LbIntegrations.Saves.ExpectedDir.FolderOr(TargetDirFor(save, ResolveFullPath(path), to.RomPath), "Could not work out where puNES reads this save from.");
        }

        /// <summary>A path as LaunchBox stores it, made full; null for nothing.</summary>
        private static string Full(string path) => string.IsNullOrWhiteSpace(path) ? null : ResolveFullPath(path);

        /// <summary>The puNES a game runs on: its own emulator when that is one, else the first in the library.</summary>
        private static string ExecutableOf(IGame game)
        {
            try
            {
                var dm = PluginHelper.DataManager;
                var id = Safe(() => game?.EmulatorId);
                // The install a restore was sent to first (SaveForward.WithVia: the choice among two installs, docs\saves-two-installs.md §4).
                var own = LbIntegrations.Saves.SaveForward.ViaEmulator(PunesLaunch.IsPunesExe) ?? (string.IsNullOrEmpty(id) ? null : dm?.GetEmulatorById(id));
                var path = Safe(() => own?.ApplicationPath);
                if (!PunesLaunch.IsPunesExe(path))
                    path = Safe(() => (LbIntegrations.Saves.SaveForward.ViaEmulator(PunesLaunch.IsPunesExe)   // through a desk: the save's install
                                       ?? dm?.GetAllEmulators()?.FirstOrDefault(e => PunesLaunch.IsPunesExe(Safe(() => e.ApplicationPath))))?.ApplicationPath);
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
                return new PluginResponse(false, "Could not delete this puNES save: " + ex.Message);
            }
        }

        private static bool IsOurs(GameSaveBase save) => SaveRows.IsOurs(save?.SaveGroupId, Emu);

        private static string Safe(Func<string> read)
        {
            try { return read(); } catch { return null; }
        }
    }
}
