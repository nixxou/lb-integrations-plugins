// Save management for Mesen - the battery save and the save states (FceuxSaves is the model of this file).
//
//   Battery save    <rom><ext>          Preferences.SaveDataFolder  = <home>\Saves\<platform>          ONE ROW: the first of the
//                                                                                                    game's files is the save,
//                                                                                                    the others travel with it
//   Save states     <rom>_1.mss ..      Preferences.SaveStateFolder = <home>\Saves\<platform>\States   one file per slot - one row
//                   <rom>_10.mss                                                                     each (and <rom>_1.mss.png
//                   <rom>_11.mss        the automatic state (every 5 min by default): slot 11, "Auto"     beside each, its picture -
//                                                                                                    MesenStateThumbs)
//   not saves       RecentGames\<rom>.rgd (Mesen's resume of the last session, a fixed folder - Mehdi 19/10: not listed)
//
// <rom> is the name of the file Mesen loads without its extension - FOR AN ARCHIVE, THE GAME INSIDE (MesenLaunch.Inner).
// The battery files of a game, in the order the first found is the save (docs\mesen.md §9, every SaveBattery of both tags):
// .sav .srm .ips (a disk's changes, Famicom Disk System) .eeprom128 .eeprom256 .bs, then what goes with one: .chr.sav
// .chr.ips .rtc .p2.srm .turbofile.sav .turbofile_stf.sav .battlebox.sav. A Game Boy game's .srm and its clock's .rtc, a
// Bandai board's EEPROM: one save, as Mesen writes them together.
//
// THE FOLDERS: Mesen's settings are pointed at the game's platform at each launch (MesenSavePaths) - so a game's files are in
// <home>\Saves\<its platform>\, whatever platform was launched last. A folder the user set in Mesen is the user's, for every
// platform: listed and restored there.
//
// THE IDS are the pack's JSON (Shared.Saves, docs\saves-lb14-routing.md §3): {"emu":"mesen","kind":"save"|"state",
// "sys":"<system key>","by":"name","id":"<rom>"} and, for a state, its "slot" - "sys" because two systems' games can share a
// name. A restore goes to the version the row is tied to, else the game, and takes the name of THAT rom today.

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

namespace LbIntegrations.Mesen
{
    public partial class MesenPlugin
    {
        /// <summary>The front desk (Shared.Saves SaveDesk): this plugin's emu, and where it logs.</summary>
        private string SaveEmu => Emu;
        private void SaveLog(string message) => Log.Info(message);

        /// <summary>This plugin's "emu" in the ids (Shared.Saves) - never reused by another plugin.</summary>
        internal const string Emu = "mesen";
        private const string SaveGroupName = "Cartridge save";
        private const string SaveChipText = "SAV";
        private const string DiskGroupName = "Disk save";
        private const string DiskChipText = "DISK";
        private const string StateGroupName = "Save states";
        private const string StateChipText = "STATE";

        /// <summary>Mesen's slots: 1 to 10 (SaveStateManager::MaxIndex), 11 its automatic state.</summary>
        internal const int AutoSlot = 11;

        /// <summary>A game's battery files: the first found is the save, the others its companions.</summary>
        internal static readonly string[] BatteryOrder =
        {
            ".sav", ".srm", ".ips", ".eeprom128", ".eeprom256", ".bs",
            ".chr.sav", ".chr.ips", ".rtc", ".p2.srm", ".turbofile.sav", ".turbofile_stf.sav", ".battlebox.sav",
        };

        /// <summary>The extensions, longest first: "x.chr.sav" is x's CHR save, not the save of a game "x.chr".</summary>
        private static readonly string[] ByLength = BatteryOrder.OrderByDescending(e => e.Length).ToArray();

        /// <summary>A state of a slot: "&lt;rom&gt;_1.mss" .. "_11.mss".</summary>
        private static readonly Regex StateFile = new Regex(@"_(?<slot>\d{1,2})\.mss$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        public override bool SupportsSaveManagement() => true;

        public override IReadOnlyDictionary<int, string> GetPotentialSaveSlots()
        {
            var slots = new Dictionary<int, string>();
            for (int i = 1; i <= 10; i++) slots[i] = i.ToString(CultureInfo.InvariantCulture);
            slots[AutoSlot] = "Auto";
            return slots;
        }

        // ── where Mesen keeps a game's ───────────────────────────────────────

        internal sealed class Folders { public string SaveDir, StateDir; }

        /// <summary>The folders of a game of <paramref name="platform"/>: the pack's for its platform, or the user's own.</summary>
        internal static Folders FoldersFor(string exe, string platform)
        {
            var home = MesenLaunch.HomeOf(exe);
            var own = SaveScan.Memo("mesen.folders|" + exe, () => UsersFolders(exe));
            string Dir(string user, string sub) => user != null ? (Path.IsPathRooted(user) ? user : Path.Combine(home, user))
                                                                : Path.Combine(home, MesenSavePaths.Relative(platform, sub));
            return new Folders { SaveDir = Dir(own.SaveDir, null), StateDir = Dir(own.StateDir, "States") };
        }

        /// <summary>The save and state folders the user set in Mesen himself (null each where the pack's are used).</summary>
        private static Folders UsersFolders(string exe)
        {
            var f = new Folders();
            try
            {
                var json = MesenJson.Load(MesenLaunch.SettingsOf(exe));
                if (!json.Readable || !json.Exists) return f;
                string Own(string ov, string folder)
                {
                    var v = json.GetString(folder);
                    return (json.GetBool(ov) ?? false) && !string.IsNullOrWhiteSpace(v) && !MesenSavePaths.IsOurs(v) ? v.Trim() : null;
                }
                f.SaveDir = Own("Preferences/OverrideSaveDataFolder", "Preferences/SaveDataFolder");
                f.StateDir = Own("Preferences/OverrideSaveStateFolder", "Preferences/SaveStateFolder");
            }
            catch { }
            return f;
        }

        /// <summary>The name Mesen gives a game's files, read once per listing (an archive is opened to find the game inside).</summary>
        internal static string NameOf(string rom) => SaveScan.Memo("mesen.name|" + rom, () =>
        {
            var inner = MesenLaunch.Inner(rom);
            return string.IsNullOrEmpty(inner) ? null : Path.GetFileNameWithoutExtension(inner.Replace('/', '\\'));
        });

        /// <summary>A battery file's game name and extension; null when it is none.</summary>
        internal static (string Name, string Ext)? SplitBattery(string path)
        {
            var file = Path.GetFileName(path ?? "");
            foreach (var ext in ByLength)
                if (file.Length > ext.Length && file.EndsWith(ext, StringComparison.OrdinalIgnoreCase))
                    return (file.Substring(0, file.Length - ext.Length), BatteryOrder.First(e => e.Equals(ext, StringComparison.OrdinalIgnoreCase)));
            return null;
        }

        /// <summary>A game's battery files in a folder, the save first.</summary>
        internal static List<string> BatteryFiles(string dir, string name)
        {
            var list = new List<string>();
            if (dir == null || string.IsNullOrEmpty(name) || !SaveScan.DirExists(dir)) return list;
            foreach (var ext in BatteryOrder)
            {
                var p = Path.Combine(dir, name + ext);
                if (SaveScan.Exists(p)) list.Add(p);
            }
            return list;
        }

        // ── listing ──────────────────────────────────────────────────────────

        GetSavesResponse IOwnSaves.OwnGetSaves(GetSavesArgs args)
        {
            try
            {
                if (args?.Emulator == null) return new GetSavesResponse("No emulator was supplied.");
                var exe = ResolveFullPath(Safe(() => args.Emulator.ApplicationPath));
                if (!MesenLaunch.IsMesenExe(exe)) return new GetSavesResponse("This emulator is not Mesen.");

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
                return new GetSavesResponse("Could not read Mesen saves: " + ex.Message);
            }
        }

        /// <summary>The system a game's files are of, for its id: the one it runs as.</summary>
        private static string SystemKey(string gameId, string platform, string rom)
        {
            var ext = Path.GetExtension(MesenLaunch.Inner(rom) ?? "").ToLowerInvariant();
            return MesenLaunch.SystemFor(gameId, platform, ext, "").System?.Key ?? "unknown";
        }

        /// <summary>Everything one game owns. Returns how many of them were states.</summary>
        private static int Collect(List<GameSaveBase> into, HashSet<string> seen, string exe, string romPath, string gameId, string platform, string appId)
        {
            if (string.IsNullOrWhiteSpace(gameId) || string.IsNullOrWhiteSpace(romPath)) return 0;
            var rom = ResolveFullPath(romPath);
            var name = NameOf(rom);
            if (string.IsNullOrEmpty(name)) return 0;
            var sys = SaveScan.Memo("mesen.sys|" + gameId + "|" + rom, () => SystemKey(gameId, platform, rom));
            var folders = FoldersFor(exe, platform);
            var context = appId ?? gameId;

            var battery = BatteryFiles(folders.SaveDir, name);
            if (battery.Count > 0 && seen.Add("save|" + battery[0] + "|" + context))
            {
                bool disk = battery[0].EndsWith(".ips", StringComparison.OrdinalIgnoreCase);
                into.Add(SaveRow(battery[0], gameId, appId, IdOf("save", sys, name), disk ? DiskGroupName : SaveGroupName, disk ? DiskChipText : SaveChipText));
            }

            int states = 0;
            foreach (var (path, slot) in StateFilesIn(folders.StateDir, name))
            {
                if (!seen.Add("state|" + path + "|" + context)) continue;
                MesenStateThumbs.Refresh(path, exe);
                into.Add(StateRow(path, gameId, appId, slot, StateIdOf(sys, name, slot), StateGroupName));
                states++;
            }
            return states;
        }

        private static string IdOf(string kind, string sys, string name) => SaveId.Write(new SaveIdData { Emu = Emu, Kind = kind, Sys = sys, By = "name", Id = name });
        private static string StateIdOf(string sys, string name, int slot) => SaveId.Write(new SaveIdData { Emu = Emu, Kind = "state", Sys = sys, By = "name", Id = name, Slot = slot });

        /// <summary>A game's states in a folder: &lt;rom&gt;_1.mss to _11.mss - never the thumbnail beside one, nor another game's
        /// ("Zelda II_2_1.mss" is a state of "Zelda II_2").</summary>
        private static IEnumerable<(string Path, int Slot)> StateFilesIn(string dir, string name)
        {
            if (dir == null || string.IsNullOrEmpty(name) || !SaveScan.DirExists(dir)) yield break;
            foreach (var path in SaveScan.Files(dir, name + "_*.mss"))
            {
                var m = StateFile.Match(path);
                if (!m.Success || !int.TryParse(m.Groups["slot"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var slot)) continue;
                if (slot < 1 || slot > AutoSlot) continue;
                if (!string.Equals(Path.GetFileName(path), name + "_" + m.Groups["slot"].Value + ".mss", StringComparison.OrdinalIgnoreCase)) continue;
                yield return (path, slot);
            }
        }

        /// <summary>For the session's thumbnails: the state files of the game this ROM is, on that platform.</summary>
        internal static List<string> StatesOf(string exe, string rom, string platform)
            => StateFilesIn(FoldersFor(exe, platform).StateDir, NameOf(ResolveFullPath(rom))).Select(s => s.Path).ToList();

        /// <summary>One of our thumbnails, beside a state: "&lt;rom&gt;_1.mss.png".</summary>
        private static bool IsStateThumb(string path)
            => LbipStateThumbs.IsThumb(path) && path.EndsWith(".mss" + LbipStateThumbs.Suffix, StringComparison.OrdinalIgnoreCase);

        private static GameSaveGame SaveRow(string path, string gameId, string appId, string groupId, string groupName, string chip)
        {
            long size = 0; DateTime when = default;
            try
            {
                // The size and the date of the whole save: the clock or the CHR RAM changes with it.
                foreach (var f in BatteryCompanions(path).Prepend(path))
                {
                    var i = new FileInfo(f);
                    size += i.Length;
                    if (i.LastWriteTimeUtc > when) when = i.LastWriteTimeUtc;
                }
            }
            catch { }
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

        /// <summary>The files that go with a battery save: the game's other battery files beside it.</summary>
        internal static List<string> BatteryCompanions(string primary)
        {
            var split = SplitBattery(primary);
            if (split == null) return new List<string>();
            return BatteryFiles(Path.GetDirectoryName(primary), split.Value.Name)
                   .Where(p => !string.Equals(p, primary, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        /// <summary>A battery file that is not its game's save, but goes with one there (a .rtc beside its .srm).</summary>
        private static bool IsBatteryCompanion(string path)
        {
            var split = SplitBattery(path);
            if (split == null) return false;
            var files = BatteryFiles(Path.GetDirectoryName(path), split.Value.Name);
            return files.Count > 0 && !string.Equals(files[0], path, StringComparison.OrdinalIgnoreCase)
                   && files.Any(f => string.Equals(f, path, StringComparison.OrdinalIgnoreCase));
        }

        // ── the contract ─────────────────────────────────────────────────────

        /// <summary>None is a container: each is a file (with its companions), which the host copies itself.</summary>
        bool IOwnSaves.OwnIsSaveContainer(GameSaveBase save) => false;

        bool IOwnSaves.OwnUseSaveGroupIdForPersistedMatch(GameSaveBase save) => IsOurs(save);

        bool IOwnSaves.OwnTryBackupSave(GameSaveBase save, string emulatorApplicationPath, string destinationFolder, out string error)
        {
            error = "A Mesen save is a file, not a container.";
            return false;
        }

        /// <summary>A state's thumbnail, and a battery file that goes with its game's save - parts of another save.</summary>
        bool IOwnSaves.OwnIsSecondarySaveFile(string filePath)
        {
            try { return IsStateThumb(filePath) || IsBatteryCompanion(filePath); } catch { return false; }
        }

        /// <summary>A state's thumbnail travels with it; a battery save's other files with it.</summary>
        IReadOnlyList<string> IOwnSaves.OwnGetCompanionSaveFiles(string primaryFilePath)
        {
            try
            {
                if (primaryFilePath == null) return Array.Empty<string>();
                if (StateFile.IsMatch(primaryFilePath)) return LbipStateThumbs.Companion(primaryFilePath);
                return BatteryCompanions(primaryFilePath);
            }
            catch { return Array.Empty<string>(); }
        }

        /// <summary>Active when it is where Mesen reads it now for that game's platform, and still there.</summary>
        bool IOwnSaves.OwnIsSaveActive(GameSaveBase save, string emulatorApplicationPath)
        {
            try
            {
                if (!IsOurs(save) || string.IsNullOrWhiteSpace(save.FileLocation)) return true;
                var exe = ResolveFullPath(emulatorApplicationPath);
                if (!MesenLaunch.IsMesenExe(exe) || !File.Exists(save.FileLocation)) return File.Exists(save.FileLocation);
                var target = TargetDirFor(save, exe);
                return target == null || string.Equals(Path.GetFullPath(Path.GetDirectoryName(save.FileLocation)).TrimEnd('\\'),
                                                       Path.GetFullPath(target).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);
            }
            catch { return true; }
        }

        // ── restore ──────────────────────────────────────────────────────────

        /// <summary>A vault copy back where Mesen reads it, under its own file name (its companions with it).</summary>
        AddSaveResponse IOwnSaves.OwnAddSaveFile(AddSaveArgs args)
        {
            try
            {
                var save = args?.SaveToAdd;
                if (save == null) return new AddSaveResponse("No save was supplied.");
                var source = save.FileLocation;
                if (string.IsNullOrWhiteSpace(source) || !File.Exists(source)) return new AddSaveResponse("This Mesen backup is not a file: " + source);

                var game = PluginHelper.DataManager?.GetGameById(save.GameId);
                var exe = ExecutableOf(game);
                var to = SaveRows.TargetFor(save, PluginHelper.DataManager, Full);
                if (to.Warning != null) Log.Warn("restore of " + save.OriginalFileName + ": " + to.Warning);
                if (to.Refused) return new AddSaveResponse(to.Warning);
                var platform = Safe(() => game?.Platform);
                var targetDir = exe == null ? null : TargetDirFor(save, exe, platform);
                if (targetDir == null) return new AddSaveResponse("Could not work out where Mesen reads this save from.");
                var original = new[] { save.OriginalFileName, Path.GetFileName(source) }.FirstOrDefault(n => !string.IsNullOrWhiteSpace(n));
                var romName = NameOf(to.RomPath) ?? "";

                if (save is GameSaveState picked)
                {
                    // A state goes to the slot picked, when Mesen has it (SlotRestore); the backup's own when none was.
                    var m = StateFile.Match(original);
                    int own = SlotRestore.SlotOf(save.SaveGroupId) ?? (m.Success && int.TryParse(m.Groups["slot"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var f) ? f : -1);
                    var (slot, refused) = SlotRestore.Choose(picked.Slot, own < 1 ? (int?)null : own, Enumerable.Range(1, AutoSlot).ToList(), "Mesen");
                    if (refused != null) { Log.Info("restore of " + original + " refused: " + refused); return new AddSaveResponse(refused); }
                    var stateName = (romName.Length > 0 ? romName : (m.Success ? original.Substring(0, m.Index) : Path.GetFileNameWithoutExtension(original)))
                                    + "_" + slot.ToString(CultureInfo.InvariantCulture) + ".mss";
                    var target = Path.Combine(targetDir, stateName);
                    bool otherCard = !IsOurs(save) || slot != own || SlotRestore.OverAnotherFile(target, save);
                    if (!SlotRestore.MayOverwrite(target, source, otherCard, slot == AutoSlot ? "The automatic state" : "Slot " + slot, Log.Info, out var no))
                        return new AddSaveResponse(no);
                    if (!Overwrite(args, target)) return new AddSaveResponse("Restore cancelled.");
                    var cannot = SaveCompanions.WhyNotWritable(source, target, null);
                    if (cannot != null) { Log.Info("restore of " + stateName + " refused: " + cannot); return new AddSaveResponse(cannot); }
                    Directory.CreateDirectory(targetDir);
                    File.Copy(source, target, overwrite: true);
                    Log.Info("restored " + stateName + " -> " + targetDir);
                    // Its thumbnail with it; without one, the old would be a picture of another moment - made again from the state.
                    var thumb = GetCompanionSaveFiles(source).FirstOrDefault(LbipStateThumbs.IsThumb);
                    if (thumb != null) File.Copy(thumb, LbipStateThumbs.PathOf(target), overwrite: true);
                    else { LbipStateThumbs.Remove(target); MesenStateThumbs.Refresh(target, exe); }
                    return new AddSaveResponse(StateRow(target, picked.GameId, picked.AdditionalApplicationId, slot, SlotRestore.WithSlot(picked.SaveGroupId, slot),
                                                        string.IsNullOrWhiteSpace(picked.SaveGroupName) ? StateGroupName : picked.SaveGroupName));
                }

                // The battery save: Mesen writes the game's own over it when it quits.
                if (MesenLaunch.Running(exe))
                    return new AddSaveResponse("Mesen is running: it would write the game's own save over this one when it quits. Close it first.");
                var split = SplitBattery(original);
                var ext = split?.Ext ?? Path.GetExtension(original);
                var baseName = romName.Length > 0 ? romName : split?.Name ?? Path.GetFileNameWithoutExtension(original);
                var main = Path.Combine(targetDir, baseName + ext);
                bool other = !IsOurs(save) || SlotRestore.OverAnotherFile(main, save);
                if (!SlotRestore.MayOverwrite(main, source, other, "The current save", Log.Info, out var nope)) return new AddSaveResponse(nope);
                if (!Overwrite(args, main)) return new AddSaveResponse("Restore cancelled.");
                // The files of the backup, each under the rom's name today: its own extension kept.
                var copies = new List<(string From, string To)> { (source, main) };
                foreach (var c in GetCompanionSaveFiles(source).Where(c => !LbipStateThumbs.IsThumb(c)))
                {
                    var cs = SplitBattery(c);
                    if (cs != null) copies.Add((c, Path.Combine(targetDir, baseName + cs.Value.Ext)));
                }
                foreach (var (from, into) in copies)
                {
                    var cannot = SaveCompanions.WhyNotWritable(from, into, null);
                    if (cannot != null) { Log.Info("restore of " + Path.GetFileName(into) + " refused: " + cannot); return new AddSaveResponse(cannot); }
                }
                Directory.CreateDirectory(targetDir);
                // A file of the game's that the backup has not (a .rtc) is left as it is: a save is never deleted by a restore.
                foreach (var (from, into) in copies) File.Copy(from, into, overwrite: true);
                Log.Info("restored " + string.Join(", ", copies.Select(c => Path.GetFileName(c.To))) + " -> " + targetDir);
                return new AddSaveResponse(SaveRow(main, save.GameId, save.AdditionalApplicationId, save.SaveGroupId, save.SaveGroupName, save.DisplayChipText));
            }
            catch (Exception ex)
            {
                Log.Warn("AddSaveFile failed", ex);
                return new AddSaveResponse("Could not restore this Mesen save: " + ex.Message);
            }
        }

        private static bool Overwrite(AddSaveArgs args, string target)
        {
            if (!File.Exists(target)) return true;
            try { return args.ShouldOverwriteFunc?.Invoke() ?? true; } catch { return true; }
        }

        /// <summary>The folder a save of this kind belongs in for its game's platform - where Mesen reads it at that game's
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
            if (save == null || !MesenLaunch.IsMesenExe(ResolveFullPath(path))) return LbIntegrations.Saves.ExpectedDir.Unknown("Could not find the Mesen this game runs on.");
            var to = SaveRows.TargetFor(save, PluginHelper.DataManager, Full);
            if (to.Refused) return LbIntegrations.Saves.ExpectedDir.Unknown(to.Warning);
            return LbIntegrations.Saves.ExpectedDir.FolderOr(TargetDirFor(save, ResolveFullPath(path)), "Could not work out where Mesen reads this save from.");
        }

        /// <summary>A path as LaunchBox stores it, made full; null for nothing.</summary>
        private static string Full(string path) => string.IsNullOrWhiteSpace(path) ? null : ResolveFullPath(path);

        /// <summary>The Mesen a game runs on: its own emulator when that is one, else the first in the library.</summary>
        private static string ExecutableOf(IGame game)
        {
            try
            {
                var dm = PluginHelper.DataManager;
                var id = Safe(() => game?.EmulatorId);
                bool IsMesen(string p) => MesenLaunch.IsMesenExe(ResolveFullPath(p));
                // The install a restore was sent to first (SaveForward.WithVia: the choice among two installs, docs\saves-two-installs.md §4).
                var own = LbIntegrations.Saves.SaveForward.ViaEmulator(IsMesen) ?? (string.IsNullOrEmpty(id) ? null : dm?.GetEmulatorById(id));
                var path = Safe(() => own?.ApplicationPath);
                if (!IsMesen(path))
                    path = Safe(() => dm?.GetAllEmulators()?.FirstOrDefault(e => IsMesen(Safe(() => e.ApplicationPath)))?.ApplicationPath);
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
                var files = save is GameSaveState ? new List<string> { loc } : BatteryCompanions(loc).Prepend(loc).ToList();
                foreach (var f in files)
                    try { if (File.Exists(f)) File.Delete(f); }
                    catch (Exception ex) { Log.Warn("could not delete " + f, ex); return new PluginResponse(false, "Could not delete " + Path.GetFileName(f) + "."); }
                if (save is GameSaveState) LbipStateThumbs.Remove(loc);
                Log.Info("deleted " + string.Join(", ", files.Select(Path.GetFileName)));
                return new PluginResponse(true);
            }
            catch (Exception ex)
            {
                Log.Warn("RemoveSave failed", ex);
                return new PluginResponse(false, "Could not delete this Mesen save: " + ex.Message);
            }
        }

        private static bool IsOurs(GameSaveBase save) => SaveRows.IsOurs(save?.SaveGroupId, Emu);
    }
}
