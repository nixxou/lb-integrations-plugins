// Save management for SUPER ZSNES - the cartridge save and the save states, as melonDS's are: one FILE each.
//
//   Battery save   <name>.srm                              in the SRAM folder
//   Save states    <name>.data.szsnes\<name>.szst<slot>    in the per-game data folder   (and <name>.szst<slot>.png
//                                                                                        beside each: its screenshot,
//                                                                                        SuperZsnesStateThumbs)
//
// THE NAME is the file name the emulator was given, its last extension off - for an ARCHIVE the archive's, not the
// ROM inside it (measured 01/10: zipped.zip holding inner.sfc saves to zipped.srm and zipped.data.szsnes\).
//
// THE FOLDERS are the emulator's own settings, read off its settings file (SuperZsnesCurrent.Raw): srmPath and
// gameSavePath, "{exec}" being its folder and "{persist}" its data folder; EMPTY means beside the ROM. The pack
// fills them with {exec}/saves and {exec}/states (tools\superzsnes-bepinex\DataFolders.cs), but a save is looked
// for where the emulator READS it - measured: with srmPath set, a .srm beside the ROM is not seen. With
// noDirectoryForSaves on, the states sit in the folder itself, not in <name>.data.szsnes\ (Options > Disable SZSNES
// Folders - read off its text, not measured).
//
// NOT LISTED, on purpose (Mehdi, 01/10):
//   <name>.szst-last   the RESUME state: rewritten every time the menu opens and every time the emulator quits,
//                      read by --loadstate, "Auto-load the last state" and Continue. Backing up or restoring a
//                      file that is replaced at every session buys nothing - and would show a state that changes
//                      every time the game is played;
//   .szhistory, .bookmark-szst   the rewind history and its bookmarks, the emulator's own working files.
//
// SLOTS are whatever number follows ".szst" - SaveState(slotPostfix) takes a string and the build names no limit;
// slot 0 is the one F2 writes.
//
// THE IDS are the pack's JSON (Shared.Saves, docs\saves-lb14-routing.md §3): {"emu":"superzsnes","kind":"save",
// "by":"name","id":"<name>"} and "kind":"state" with its "slot". A restore goes to the version the row is tied to, else
// the game, under the name of THAT rom today - the states' <name>.data.szsnes folder too, no longer the name the id was
// written with (an old backup used to go back into its old name's folder).

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

namespace LbIntegrations.SuperZsnes
{
    public partial class SuperZsnesPlugin
    {
        /// <summary>The front desk (Shared.Saves SaveDesk): this plugin's emu, and where it logs.</summary>
        private string SaveEmu => Emu;
        private void SaveLog(string message) => Log.Info(message);

        /// <summary>This plugin's "emu" in the ids (Shared.Saves).</summary>
        private const string Emu = "superzsnes";
        private const string SaveGroupName = "Cartridge save";
        private const string SaveChipText = "SRM";
        private const string StateGroupName = "Save states";
        private const string StateChipText = "STATE";

        private static readonly Regex StateFile = new Regex(@"\.szst(?<slot>\d+)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        public override bool SupportsSaveManagement() => true;

        public override IReadOnlyDictionary<int, string> GetPotentialSaveSlots()
        {
            var slots = new Dictionary<int, string>();
            for (int i = 0; i <= 9; i++) slots[i] = i.ToString(CultureInfo.InvariantCulture);
            return slots;
        }

        // ── where the emulator keeps a game's ────────────────────────────────

        /// <summary>The SRAM folder and the states folder the emulator uses for a ROM, from its settings file.</summary>
        internal sealed class Folders
        {
            public string SaveDir, StateDir;
            public bool Flat;     // noDirectoryForSaves: the states in StateDir itself
        }

        internal static Folders FoldersFor(string exePath, string romPath)
        {
            var exeDir = Path.GetDirectoryName(Path.GetFullPath(exePath));
            var romDir = string.IsNullOrWhiteSpace(romPath) ? null : Path.GetDirectoryName(Path.GetFullPath(romPath));
            // A big listing decodes the settings file once, not once per game (LbIntegrations.Saves.SaveScan).
            var raw = LbIntegrations.Saves.SaveScan.Memo("superzsnes.raw|" + exePath, () => LbIntegrations.Lbip.StampedCache.Get(SuperZsnesPaths.SettingsFile(exePath), "superzsnes.raw|" + exePath, () => SuperZsnesCurrent.Raw(exePath, "srmPath", "gameSavePath", "noDirectoryForSaves")));   // and Game Saves: while the settings file is unchanged
            // The pack's in-process plugin fills an EMPTY folder setting as the settings are loaded (DataFolders.Apply), so
            // from the first session - before the emulator has written it into its file (review 12/10: "beside the ROM"
            // was answered while the session already wrote to <exe>\saves). Only when it is deployed and wanted.
            bool filled = LbIntegrations.Saves.SaveScan.Memo("superzsnes.filled|" + exePath, () => SuperZsnesPaths.IsPortable(exeDir) && SuperZsnesSettings.DataFoldersWanted);
            string Dir(string member)
            {
                var v = raw.TryGetValue(member, out var o) ? o as string : null;
                if (string.IsNullOrWhiteSpace(v) && filled) v = member == "srmPath" ? "{exec}/saves" : member == "gameSavePath" ? "{exec}/states" : null;
                if (string.IsNullOrWhiteSpace(v)) return romDir;
                var persist = LbIntegrations.Saves.SaveScan.Memo("superzsnes.persist|" + exePath, () => Path.GetDirectoryName(SuperZsnesPaths.SettingsFile(exePath) ?? "")) ?? exeDir;
                var expanded = v.Replace("{exec}", exeDir, StringComparison.OrdinalIgnoreCase)
                                .Replace("{persist}", persist, StringComparison.OrdinalIgnoreCase)
                                .Replace('/', Path.DirectorySeparatorChar);
                try { return Path.GetFullPath(Path.IsPathRooted(expanded) ? expanded : Path.Combine(exeDir, expanded)); }
                catch { return romDir; }
            }
            return new Folders
            {
                SaveDir = Dir("srmPath"),
                StateDir = Dir("gameSavePath"),
                Flat = raw.TryGetValue("noDirectoryForSaves", out var f) && f is bool b && b,
            };
        }

        /// <summary>Where a ROM's states are: its .data.szsnes folder, or the states folder itself when flat.</summary>
        private static string StatesOf(Folders folders, string name)
            => folders.StateDir == null ? null : folders.Flat ? folders.StateDir : Path.Combine(folders.StateDir, name + ".data.szsnes");

        internal static string NameOf(string romPath) => Path.GetFileNameWithoutExtension(romPath ?? "");

        /// <summary>Nothing beside the game (Mehdi, 07/10; lot 9, 13/10): a game's states folder still beside it - <name>.data.szsnes\,
        /// left by SUPER ZSNES before the pack filled its states folder (01/10) - moved into the states folder, file by file,
        /// never over a file already there: the same bytes are dropped, different ones go to <c>conflicts\</c> under their date
        /// (PlaceStateFile); the folder beside the game goes once empty. Its name is SUPER ZSNES's own: no other emulator writes it. The .srm beside the game is NOT moved: bsnes,
        /// Snes9x and RetroArch write that name too, and nothing tells whose it is (docs\saves-lot9.md). Nothing when the states
        /// folder is the game's own (the pack's folders not in place) or flat (noDirectoryForSaves: no per-game folder).
        /// Never throws.</summary>
        internal static int MigrateStates(string exe, string rom)
        {
            int moved = 0;
            try
            {
                if (string.IsNullOrWhiteSpace(exe) || string.IsNullOrWhiteSpace(rom)) return 0;
                var full = Path.GetFullPath(rom);
                var romDir = Path.GetDirectoryName(full);
                var name = NameOf(full);
                var folders = FoldersFor(exe, full);
                if (name.Length == 0 || folders.Flat || folders.StateDir == null
                    || string.Equals(Path.GetFullPath(folders.StateDir).TrimEnd('\\'), romDir.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)) return 0;
                var from = Path.Combine(romDir, name + ".data.szsnes");
                if (!Directory.Exists(from)) return 0;
                // Only once SUPER ZSNES ITSELF wrote its states folder into its settings (review 13/10): a folder assumed from the
                // mod being deployed is not one SUPER ZSNES uses when the mod failed to load - it still writes beside the game,
                // and each session's states would be carried away at the next launch.
                var written = SuperZsnesCurrent.Raw(exe, "gameSavePath");
                if (!(written != null && written.TryGetValue("gameSavePath", out var gsp) && gsp is string g && !string.IsNullOrWhiteSpace(g)))
                { Log.Info("saves: " + from + " left beside the game - SUPER ZSNES's own settings name no states folder yet"); return 0; }
                var to = Path.Combine(folders.StateDir, name + ".data.szsnes");
                var aside = Path.Combine(folders.StateDir, ConflictsFolder, name + ".data.szsnes");
                foreach (var file in Directory.GetFiles(from, "*", SearchOption.AllDirectories))
                {
                    var rel = Path.GetRelativePath(from, file);
                    var target = Path.Combine(to, rel);
                    var how = PlaceStateFile(file, target, Path.Combine(aside, rel));
                    if (how == "moved") moved++;
                    else if (how == "duplicate") Log.Info("saves: " + file + " removed - the same file is already " + target);
                    else Log.Info("saves: " + file + " differs from " + target + " - moved aside to " + how.Substring("conflict:".Length));
                }
                // The folder beside the game, once nothing is left in it.
                foreach (var dir in Directory.GetDirectories(from, "*", SearchOption.AllDirectories).OrderByDescending(d => d.Length).Append(from))
                    try { if (!Directory.EnumerateFileSystemEntries(dir).Any()) Directory.Delete(dir); } catch { }
                if (moved > 0) Log.Info("saves: " + moved + " state file(s) of " + name + " moved from " + from + " to " + to);
            }
            catch (Exception ex) { Log.Warn("saves: could not move the game's states out of its folder", ex); }
            return moved;
        }

        /// <summary>Where a state left beside a game goes when the states folder already has a different file of that name.</summary>
        internal const string ConflictsFolder = "conflicts";

        /// <summary>One file of a game's states folder beside it, put into the states folder (lot 9). Moved; when that name is
        /// already there: removed if it is the same bytes (nothing is lost), else moved into the conflicts folder under its date,
        /// never over a file. Never left beside the game (Mehdi, 13/10: Secret of Mana's .szst-last stayed there). Returns
        /// "moved", "duplicate" or "conflict:" and where it went.</summary>
        internal static string PlaceStateFile(string file, string target, string asideTarget)
        {
            if (!File.Exists(target))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(target));
                File.Move(file, target);
                return "moved";
            }
            if (SameBytes(file, target)) { File.Delete(file); return "duplicate"; }
            var dir = Path.GetDirectoryName(asideTarget);
            var stem = Path.GetFileName(asideTarget) + "." + File.GetLastWriteTime(file).ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
            var dest = Path.Combine(dir, stem);
            for (int i = 2; File.Exists(dest); i++) dest = Path.Combine(dir, stem + "-" + i);
            Directory.CreateDirectory(dir);
            File.Move(file, dest);
            return "conflict:" + dest;
        }

        private static bool SameBytes(string a, string b)
        {
            var fa = new FileInfo(a); var fb = new FileInfo(b);
            if (fa.Length != fb.Length) return false;
            using var sa = fa.OpenRead(); using var sb = fb.OpenRead();
            var x = new byte[81920]; var y = new byte[81920];
            int n;
            while ((n = sa.Read(x, 0, x.Length)) > 0)
            {
                int m = 0;
                while (m < n) { int k = sb.Read(y, m, n - m); if (k <= 0) return false; m += k; }
                for (int i = 0; i < n; i++) if (x[i] != y[i]) return false;
            }
            return sb.Read(y, 0, 1) == 0;
        }

        // ── listing ──────────────────────────────────────────────────────────

        GetSavesResponse IOwnSaves.OwnGetSaves(GetSavesArgs args)
        {
            try
            {
                if (args?.Emulator == null) return new GetSavesResponse("No emulator was supplied.");
                var exe = ResolveFullPath(Safe(() => args.Emulator.ApplicationPath));
                if (!SuperZsnesPaths.IsSuperZsnesExecutable(exe)) return new GetSavesResponse("This emulator is not SUPER ZSNES.");

                var found = new List<GameSaveBase>();
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                int states = 0;
                foreach (var app in args.AdditionalApplications ?? (IReadOnlyCollection<IAdditionalApplication>)Array.Empty<IAdditionalApplication>())
                {
                    // A version on its game's own file is not tied to the version (SaveRows.TieTo).
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
                return new GetSavesResponse("Could not read SUPER ZSNES saves: " + ex.Message);
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
                into.Add(SaveRow(srm, gameId, appId, SaveId.Write(new SaveIdData { Emu = Emu, Kind = "save", By = "name", Id = name })));

            int states = 0;
            foreach (var (path, slot) in StateFilesIn(StatesOf(folders, name), name))
            {
                if (!seen.Add("state|" + path + "|" + context)) continue;
                SuperZsnesStateThumbs.Refresh(path);
                into.Add(StateRow(path, gameId, appId, slot, SaveId.Write(new SaveIdData { Emu = Emu, Kind = "state", By = "name", Id = name, Slot = slot }), StateGroupName));
                states++;
            }
            return states;
        }

        /// <summary>A game's slot states in its folder: &lt;name&gt;.szst&lt;n&gt; - never -last, nor the thumbnail beside a
        /// state ("Zelda.szst0.png" is matched by the glob, not by StateFile).</summary>
        private static IEnumerable<(string Path, int Slot)> StateFilesIn(string dir, string name)
        {
            if (dir == null || string.IsNullOrEmpty(name) || !LbIntegrations.Saves.SaveScan.DirExists(dir)) yield break;
            foreach (var path in LbIntegrations.Saves.SaveScan.Files(dir, name + ".szst*"))
            {
                var m = StateFile.Match(path);
                if (!m.Success || !Path.GetFileName(path).StartsWith(name + ".szst", StringComparison.OrdinalIgnoreCase)) continue;   // -last, and anything else
                if (!int.TryParse(m.Groups["slot"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var slot)) continue;
                yield return (path, slot);
            }
        }

        /// <summary>For the session's thumbnails: the slot states of the game this ROM is.</summary>
        internal static IEnumerable<string> StatesOf(string exe, string rom)
        {
            var name = NameOf(ResolveFullPath(rom));
            return StateFilesIn(StatesOf(FoldersFor(exe, rom), name), name).Select(s => s.Path).ToList();
        }

        /// <summary>One of our thumbnails, beside a slot state: "&lt;name&gt;.szst0.png".</summary>
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
            error = "A SUPER ZSNES save is one file, not a container.";
            return false;
        }

        /// <summary>A plugin must never call its own save secondary; beside a .srm or a .szst, only a state's thumbnail is
        /// part of it.</summary>
        bool IOwnSaves.OwnIsSecondarySaveFile(string filePath) => IsStateThumb(filePath);

        /// <summary>A state's thumbnail: the screenshot it carries, made a PNG beside it (SuperZsnesStateThumbs).</summary>
        IReadOnlyList<string> IOwnSaves.OwnGetCompanionSaveFiles(string primaryFilePath)
            => primaryFilePath != null && StateFile.IsMatch(primaryFilePath) ? LbipStateThumbs.Companion(primaryFilePath) : Array.Empty<string>();

        /// <summary>Active when it is where the emulator reads it now - the folder its settings name - and still there.</summary>
        bool IOwnSaves.OwnIsSaveActive(GameSaveBase save, string emulatorApplicationPath)
        {
            try
            {
                if (!IsOurs(save) || string.IsNullOrWhiteSpace(save.FileLocation)) return true;
                var exe = ResolveFullPath(emulatorApplicationPath);
                if (!SuperZsnesPaths.IsSuperZsnesExecutable(exe) || !File.Exists(save.FileLocation)) return File.Exists(save.FileLocation);
                var target = TargetDirFor(save, exe);
                return target == null || string.Equals(Path.GetFullPath(Path.GetDirectoryName(save.FileLocation)).TrimEnd('\\'),
                                                       Path.GetFullPath(target).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);
            }
            catch { return true; }
        }

        // ── restore ──────────────────────────────────────────────────────────

        /// <summary>A vault copy back where the emulator reads it, under its own file name.</summary>
        AddSaveResponse IOwnSaves.OwnAddSaveFile(AddSaveArgs args)
        {
            try
            {
                var save = args?.SaveToAdd;
                if (save == null) return new AddSaveResponse("No save was supplied.");
                var source = save.FileLocation;
                if (string.IsNullOrWhiteSpace(source) || !File.Exists(source)) return new AddSaveResponse("This SUPER ZSNES backup is not a file: " + source);

                var game = PluginHelper.DataManager?.GetGameById(save.GameId);
                var exe = ExecutableOf(game);
                var to = SaveRows.TargetFor(save, PluginHelper.DataManager, Full);
                if (to.Warning != null) Log.Warn("restore of " + save.OriginalFileName + ": " + to.Warning);
                if (to.Refused) return new AddSaveResponse(to.Warning);
                var targetDir = exe == null ? null : TargetDirFor(save, exe, to.RomPath);
                if (targetDir == null) return new AddSaveResponse("Could not work out where SUPER ZSNES reads this save from.");
                // The name of the rom it goes to TODAY, with the backup's own extension (.srm, .szst3...).
                var original = new[] { save.OriginalFileName, Path.GetFileName(source) }.FirstOrDefault(n => !string.IsNullOrWhiteSpace(n));
                var romName = NameOf(to.RomPath);
                var name = romName.Length > 0 ? romName + Path.GetExtension(original) : original;
                // A state goes to the slot picked, when SUPER ZSNES has it (SlotRestore); the backup's own when none was.
                int slot = 0, own = -1;
                if (save is GameSaveState picked)
                {
                    var m = StateFile.Match(name);
                    own = SlotRestore.SlotOf(save.SaveGroupId) ?? (m.Success && int.TryParse(m.Groups["slot"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var f) ? f : -1);
                    var (s, refused) = SlotRestore.Choose(picked.Slot, own < 0 ? (int?)null : own, Enumerable.Range(0, 10).ToList(), "SUPER ZSNES");
                    if (refused != null) { Log.Info("restore of " + original + " refused: " + refused); return new AddSaveResponse(refused); }
                    slot = s;
                    name = Path.GetFileNameWithoutExtension(name) + ".szst" + slot.ToString(CultureInfo.InvariantCulture);
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
                Directory.CreateDirectory(targetDir);
                File.Copy(source, target, overwrite: true);
                Log.Info("restored " + name + " -> " + targetDir);

                if (save is GameSaveState)
                {
                    // Its thumbnail with it; without one, the old would be a picture of another moment - made again from the state.
                    var thumb = GetCompanionSaveFiles(source).FirstOrDefault(LbipStateThumbs.IsThumb);
                    if (thumb != null) File.Copy(thumb, LbipStateThumbs.PathOf(target), overwrite: true);
                    else { LbipStateThumbs.Remove(target); SuperZsnesStateThumbs.Refresh(target); }
                }
                if (save is GameSaveState state)
                    return new AddSaveResponse(StateRow(target, state.GameId, state.AdditionalApplicationId, slot, SlotRestore.WithSlot(state.SaveGroupId, slot),
                                                        string.IsNullOrWhiteSpace(state.SaveGroupName) ? StateGroupName : state.SaveGroupName));
                return new AddSaveResponse(SaveRow(target, save.GameId, save.AdditionalApplicationId, save.SaveGroupId, save.SaveGroupName, save.DisplayChipText));
            }
            catch (Exception ex)
            {
                Log.Warn("AddSaveFile failed", ex);
                return new AddSaveResponse("Could not restore this SUPER ZSNES save: " + ex.Message);
            }
        }

        /// <summary>The folder a save of this kind belongs in for this rom - the row's version's or its game's
        /// (SaveRows.TargetFor) - as the emulator's settings have it; a state's is that rom's .data.szsnes.</summary>
        private static string TargetDirFor(GameSaveBase save, string exe, string rom = null)
        {
            rom ??= SaveRows.TargetFor(save, PluginHelper.DataManager, Full).RomPath;
            var folders = FoldersFor(exe, rom);
            return save is GameSaveState ? StatesOf(folders, NameOf(rom)) : folders.SaveDir;
        }

        LbIntegrations.Saves.ExpectedDir IOwnSaves.OwnExpectedDir(IEmulator emulator, GameSaveBase save)
        {
            var path = Safe(() => emulator?.ApplicationPath);
            if (save == null || !SuperZsnesPaths.IsSuperZsnesExecutable(path)) return LbIntegrations.Saves.ExpectedDir.Unknown("Could not find the SUPER ZSNES this game runs on.");
            var to = SaveRows.TargetFor(save, PluginHelper.DataManager, Full);
            if (to.Refused) return LbIntegrations.Saves.ExpectedDir.Unknown(to.Warning);
            return LbIntegrations.Saves.ExpectedDir.FolderOr(TargetDirFor(save, ResolveFullPath(path), to.RomPath), "Could not work out where SUPER ZSNES reads this save from.");
        }

        /// <summary>A path as LaunchBox stores it, made full; null for nothing.</summary>
        private static string Full(string path) => string.IsNullOrWhiteSpace(path) ? null : ResolveFullPath(path);

        /// <summary>The SUPER ZSNES a game runs on: its own emulator when that is one, else the first in the library.</summary>
        private static string ExecutableOf(IGame game)
        {
            try
            {
                var dm = PluginHelper.DataManager;
                var id = Safe(() => game?.EmulatorId);
                // The install a restore was sent to first (SaveForward.WithVia: the choice among two installs, docs\saves-two-installs.md §4).
                var own = LbIntegrations.Saves.SaveForward.ViaEmulator(SuperZsnesPaths.IsSuperZsnesExecutable) ?? (string.IsNullOrEmpty(id) ? null : dm?.GetEmulatorById(id));
                var path = Safe(() => own?.ApplicationPath);
                if (!SuperZsnesPaths.IsSuperZsnesExecutable(path))
                    path = Safe(() => (LbIntegrations.Saves.SaveForward.ViaEmulator(SuperZsnesPaths.IsSuperZsnesExecutable)   // through a desk: the save's install
                                       ?? dm?.GetAllEmulators()?.FirstOrDefault(e => SuperZsnesPaths.IsSuperZsnesExecutable(Safe(() => e.ApplicationPath))))?.ApplicationPath);
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
                return new PluginResponse(false, "Could not delete this SUPER ZSNES save: " + ex.Message);
            }
        }

        private static bool IsOurs(GameSaveBase save) => SaveRows.IsOurs(save?.SaveGroupId, Emu);

        private static string Safe(Func<string> read)
        {
            try { return read(); } catch { return null; }
        }
    }
}
