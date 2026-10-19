// Save management for Ymir - the game's backup RAM and its save states (MesenSaves is the model of this file).
//
//   Backup RAM    <install>\Saves\<platform>\<game>.bin     the console's internal backup RAM, ONE PER LAUNCHBOX GAME (Mehdi
//                                                          20/10): Ymir.toml's InternalBackupRAMImagePath pointed at it for
//                                                          the session (YmirSession). <game>: the .m3u's name, or the disc's
//                                                          without "(Disc N)" - so the discs of a game share it, and a game
//                                                          in LaunchBox as one entry per disc too.
//   Save states   <install>\savestates\<disc hash>\0.savestate .. 9.savestate   slots 1 to 10 (Ymir numbers from 0, measured
//                                                          20/10), one folder per DISC (YmirDisc's hash) - a game of two
//                                                          discs has its states in two folders.
//   not saves     <n>-1.savestate (the state a save replaced, for Ymir's "undo save"), meta.txt; state\bup-int.bin (Ymir's
//                 own RAM, for a disc opened by hand); a backup RAM cartridge's backup\games\bup-ext-*.bin (not listed yet).
//
// THE IDS are the pack's JSON (Shared.Saves): {"emu":"ymir","kind":"save","sys":"saturn","by":"name","id":"<game>"} and
// {"emu":"ymir","kind":"state","sys":"saturn","by":"hash","id":"<disc hash>","slot":n}. A state is restored to the disc of
// its hash when the target game has it, to the target's only disc otherwise; a game of several discs and none of the hash:
// refused, said.

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

namespace LbIntegrations.Ymir
{
    public partial class YmirPlugin
    {
        private string SaveEmu => Emu;
        private void SaveLog(string message) => Log.Info(message);

        internal const string Emu = "ymir";
        private const string Sys = "saturn";
        private const string RamGroupName = "Backup RAM";
        private const string RamChipText = "RAM";
        private const string StateGroupName = "Save states";
        private const string StateChipText = "STATE";
        internal const int Slots = 10;

        private static readonly Regex StateFile = new Regex(@"^(?<n>\d)\.savestate$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        private static readonly Regex UndoFile = new Regex(@"^\d-1\.savestate$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        private static readonly Regex HashDir = new Regex(@"^[0-9A-F]{32}$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        public override bool SupportsSaveManagement() => true;

        public override IReadOnlyDictionary<int, string> GetPotentialSaveSlots()
        {
            var slots = new Dictionary<int, string>();
            for (int i = 1; i <= Slots; i++) slots[i] = i.ToString(CultureInfo.InvariantCulture);
            return slots;
        }

        // ── where Ymir keeps a game's ────────────────────────────────────────

        /// <summary>The name a game's backup RAM is kept under (header): the file without its extension and its "(Disc N)".</summary>
        internal static string RamName(string rom)
        {
            var name = Path.GetFileNameWithoutExtension(rom ?? "");
            var ext = Path.GetExtension(rom ?? "").ToLowerInvariant();
            return ext == ".m3u" ? name : YmirLaunch.WithoutDisc(name);
        }

        private static string SafeName(string name) => string.Concat((name ?? "").Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c)).Trim();

        internal static string RamDir(string exe, string platform)
            => Path.Combine(YmirLaunch.ProfileOf(exe), "Saves", SafeName(string.IsNullOrWhiteSpace(platform) ? "Sega Saturn" : platform));

        internal static string RamPath(string exe, string platform, string rom) => Path.Combine(RamDir(exe, platform), SafeName(RamName(rom)) + ".bin");

        /// <summary>A game's discs, for its states - never unpacking an archive (only one already unpacked is looked at).</summary>
        internal static List<string> DiscsForListing(string rom, string exe)
        {
            try
            {
                var ext = Path.GetExtension(rom ?? "").ToLowerInvariant();
                if (ext == ".m3u") return YmirLaunch.M3uDiscs(rom).Where(SaveScan.Exists).ToList();
                if (YmirLaunch.IsNative(rom)) return YmirLaunch.DiscsOf(rom);
                if (YmirLaunch.IsArchive(rom))
                {
                    var unpacked = Path.Combine(YmirLaunch.ProfileOf(exe), "Unpacked");
                    if (!SaveScan.DirExists(unpacked)) return new List<string>();
                    var dir = Directory.EnumerateDirectories(unpacked, Path.GetFileNameWithoutExtension(rom) + "-*").FirstOrDefault();
                    if (dir == null) return new List<string>();
                    var files = Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories).ToList();
                    var m3u = files.FirstOrDefault(p => p.EndsWith(".m3u", StringComparison.OrdinalIgnoreCase));
                    if (m3u != null) return YmirLaunch.M3uDiscs(m3u).Where(File.Exists).ToList();
                    var disc = files.FirstOrDefault(p => p.EndsWith(".cue", StringComparison.OrdinalIgnoreCase)) ?? files.FirstOrDefault(YmirLaunch.IsNative);
                    return disc == null ? new List<string>() : YmirLaunch.DiscsOf(disc);
                }
            }
            catch { }
            return new List<string>();
        }

        /// <summary>The disc hashes of a game, read once per listing.</summary>
        internal static List<string> HashesOf(string rom, string exe) => SaveScan.Memo("ymir.hashes|" + rom, () =>
            DiscsForListing(rom, exe).Select(d => YmirDisc.Read(d)?.Hash).Where(h => h != null).Distinct(StringComparer.OrdinalIgnoreCase).ToList());

        /// <summary>A disc's states: slot (1-10) and file.</summary>
        internal static IEnumerable<(string Path, int Slot)> StatesOf(string exe, string hash)
        {
            var dir = Path.Combine(YmirLaunch.SaveStatesDirOf(exe), hash ?? "");
            if (string.IsNullOrEmpty(hash) || !SaveScan.DirExists(dir)) yield break;
            foreach (var path in SaveScan.Files(dir, "*.savestate"))
            {
                var m = StateFile.Match(Path.GetFileName(path));
                if (!m.Success) continue;
                yield return (path, int.Parse(m.Groups["n"].Value, CultureInfo.InvariantCulture) + 1);
            }
        }

        // ── listing ──────────────────────────────────────────────────────────

        GetSavesResponse IOwnSaves.OwnGetSaves(GetSavesArgs args)
        {
            try
            {
                if (args?.Emulator == null) return new GetSavesResponse("No emulator was supplied.");
                var exe = ResolveFullPath(Safe(() => args.Emulator.ApplicationPath));
                if (!YmirLaunch.IsYmirExe(exe)) return new GetSavesResponse("This emulator is not Ymir.");

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

                Log.Verbose("found " + (found.Count - states) + " backup RAM(s) and " + states + " state(s) for " + exe);
                return new GetSavesResponse(found);
            }
            catch (Exception ex)
            {
                Log.Warn("GetSaves failed", ex);
                return new GetSavesResponse("Could not read Ymir saves: " + ex.Message);
            }
        }

        private static int Collect(List<GameSaveBase> into, HashSet<string> seen, string exe, string romPath, string gameId, string platform, string appId)
        {
            if (string.IsNullOrWhiteSpace(gameId) || string.IsNullOrWhiteSpace(romPath)) return 0;
            var rom = ResolveFullPath(romPath);
            var context = appId ?? gameId;

            var ram = RamPath(exe, platform, rom);
            if (SaveScan.Exists(ram) && seen.Add("save|" + ram + "|" + context))
                into.Add(RamRow(ram, gameId, appId, IdOfRam(RamName(rom))));

            int states = 0;
            foreach (var hash in HashesOf(rom, exe))
                foreach (var (path, slot) in StatesOf(exe, hash))
                {
                    if (!seen.Add("state|" + path + "|" + context)) continue;
                    into.Add(StateRow(path, gameId, appId, slot, IdOfState(hash, slot), StateGroupName));
                    states++;
                }
            return states;
        }

        private static string IdOfRam(string name) => SaveId.Write(new SaveIdData { Emu = Emu, Kind = "save", Sys = Sys, By = "name", Id = name });
        private static string IdOfState(string hash, int slot) => SaveId.Write(new SaveIdData { Emu = Emu, Kind = "state", Sys = Sys, By = "hash", Id = hash, Slot = slot });

        private static GameSaveGame RamRow(string path, string gameId, string appId, string groupId)
        {
            long size = 0; DateTime when = default;
            try { var i = new FileInfo(path); size = i.Length; when = i.LastWriteTimeUtc; } catch { }
            return new GameSaveGame
            {
                GameId = gameId, AdditionalApplicationId = appId, FileLocation = path, IsDirectory = false,
                OriginalFileName = Path.GetFileName(path), SaveGroupId = groupId, SaveGroupName = RamGroupName, DisplayChipText = RamChipText,
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

        bool IOwnSaves.OwnIsSaveContainer(GameSaveBase save) => false;

        bool IOwnSaves.OwnUseSaveGroupIdForPersistedMatch(GameSaveBase save) => IsOurs(save);

        bool IOwnSaves.OwnTryBackupSave(GameSaveBase save, string emulatorApplicationPath, string destinationFolder, out string error)
        {
            error = "A Ymir save is a file, not a container.";
            return false;
        }

        /// <summary>The state a save replaced (Ymir's undo) and a state folder's meta.txt: Ymir's own, never a row.</summary>
        bool IOwnSaves.OwnIsSecondarySaveFile(string filePath)
        {
            try
            {
                var name = Path.GetFileName(filePath ?? "");
                var parent = Path.GetFileName(Path.GetDirectoryName(filePath ?? "") ?? "");
                if (!HashDir.IsMatch(parent)) return false;
                return UndoFile.IsMatch(name) || string.Equals(name, "meta.txt", StringComparison.OrdinalIgnoreCase);
            }
            catch { return false; }
        }

        IReadOnlyList<string> IOwnSaves.OwnGetCompanionSaveFiles(string primaryFilePath) => Array.Empty<string>();

        /// <summary>Active when it is where Ymir reads it for that game now, and still there.</summary>
        bool IOwnSaves.OwnIsSaveActive(GameSaveBase save, string emulatorApplicationPath)
        {
            try
            {
                if (!IsOurs(save) || string.IsNullOrWhiteSpace(save.FileLocation)) return true;
                if (!File.Exists(save.FileLocation)) return false;
                var exe = ResolveFullPath(emulatorApplicationPath);
                if (!YmirLaunch.IsYmirExe(exe)) return true;
                var target = TargetFileFor(save, exe, out _);
                return target == null || string.Equals(Path.GetFullPath(target), Path.GetFullPath(save.FileLocation), StringComparison.OrdinalIgnoreCase);
            }
            catch { return true; }
        }

        // ── restore ──────────────────────────────────────────────────────────

        /// <summary>Where a row goes back for its game (the version it is tied to, else the game): the backup RAM's file, or the
        /// state of its slot in the folder of the right disc. Null, with why, when there is none.</summary>
        private static string TargetFileFor(GameSaveBase save, string exe, out string why, int? slot = null)
        {
            why = null;
            var to = SaveRows.TargetFor(save, PluginHelper.DataManager, Full);
            if (to.Refused || string.IsNullOrEmpty(to.RomPath)) { why = to.Warning ?? "Could not find the game of this save."; return null; }
            var platform = Safe(() => PluginHelper.DataManager?.GetGameById(save.GameId)?.Platform);
            if (!(save is GameSaveState state)) return RamPath(exe, platform, to.RomPath);

            var hashes = HashesOf(to.RomPath, exe);
            string own = SaveId.TryRead(save.SaveGroupId, out var d) && d?.By == "hash" ? d.Id : Path.GetFileName(Path.GetDirectoryName(save.FileLocation) ?? "");
            string hash = hashes.FirstOrDefault(h => string.Equals(h, own, StringComparison.OrdinalIgnoreCase)) ?? (hashes.Count == 1 ? hashes[0] : null);
            if (hash == null)
            {
                why = hashes.Count == 0 ? "The game's disc could not be read, so Ymir's folder for its states is not known."
                                        : "This state is of a disc this game does not have, and the game has " + hashes.Count + " discs: which one it goes to is not known.";
                return null;
            }
            int n = slot ?? state.Slot ?? SlotRestore.SlotOf(save.SaveGroupId) ?? 1;
            return Path.Combine(YmirLaunch.SaveStatesDirOf(exe), hash, (n - 1).ToString(CultureInfo.InvariantCulture) + ".savestate");
        }

        AddSaveResponse IOwnSaves.OwnAddSaveFile(AddSaveArgs args)
        {
            try
            {
                var save = args?.SaveToAdd;
                if (save == null) return new AddSaveResponse("No save was supplied.");
                var source = save.FileLocation;
                if (string.IsNullOrWhiteSpace(source) || !File.Exists(source)) return new AddSaveResponse("This Ymir backup is not a file: " + source);
                var exe = ExecutableOf(PluginHelper.DataManager?.GetGameById(save.GameId));
                if (exe == null) return new AddSaveResponse("Could not find the Ymir this game runs on.");
                if (YmirLaunch.Running(exe)) return new AddSaveResponse("Ymir is running: close it first.");

                if (save is GameSaveState picked)
                {
                    var m = StateFile.Match(save.OriginalFileName ?? Path.GetFileName(source));
                    int own = SlotRestore.SlotOf(save.SaveGroupId) ?? (m.Success ? int.Parse(m.Groups["n"].Value, CultureInfo.InvariantCulture) + 1 : -1);
                    var (slot, refused) = SlotRestore.Choose(picked.Slot, own < 1 ? (int?)null : own, Enumerable.Range(1, Slots).ToList(), "Ymir");
                    if (refused != null) { Log.Info("restore of " + save.OriginalFileName + " refused: " + refused); return new AddSaveResponse(refused); }
                    var target = TargetFileFor(save, exe, out var why, slot);
                    if (target == null) { Log.Info("restore of " + save.OriginalFileName + " refused: " + why); return new AddSaveResponse(why); }
                    bool otherCard = !IsOurs(save) || slot != own || SlotRestore.OverAnotherFile(target, save);
                    if (!SlotRestore.MayOverwrite(target, source, otherCard, "Slot " + slot, Log.Info, out var no)) return new AddSaveResponse(no);
                    if (!Overwrite(args, target)) return new AddSaveResponse("Restore cancelled.");
                    var cannot = SaveCompanions.WhyNotWritable(source, target, null);
                    if (cannot != null) { Log.Info("restore refused: " + cannot); return new AddSaveResponse(cannot); }
                    Directory.CreateDirectory(Path.GetDirectoryName(target));
                    File.Copy(source, target, overwrite: true);
                    Log.Info("restored a state to slot " + slot + " -> " + target);
                    var hash = Path.GetFileName(Path.GetDirectoryName(target));
                    return new AddSaveResponse(StateRow(target, picked.GameId, picked.AdditionalApplicationId, slot, IdOfState(hash, slot),
                                                        string.IsNullOrWhiteSpace(picked.SaveGroupName) ? StateGroupName : picked.SaveGroupName));
                }

                var ram = TargetFileFor(save, exe, out var whyRam);
                if (ram == null) return new AddSaveResponse(whyRam);
                bool other = !IsOurs(save) || SlotRestore.OverAnotherFile(ram, save);
                if (!SlotRestore.MayOverwrite(ram, source, other, "The game's backup RAM", Log.Info, out var nope)) return new AddSaveResponse(nope);
                if (!Overwrite(args, ram)) return new AddSaveResponse("Restore cancelled.");
                var cant = SaveCompanions.WhyNotWritable(source, ram, null);
                if (cant != null) { Log.Info("restore refused: " + cant); return new AddSaveResponse(cant); }
                Directory.CreateDirectory(Path.GetDirectoryName(ram));
                File.Copy(source, ram, overwrite: true);
                Log.Info("restored the backup RAM -> " + ram);
                return new AddSaveResponse(RamRow(ram, save.GameId, save.AdditionalApplicationId, IdOfRam(Path.GetFileNameWithoutExtension(ram))));
            }
            catch (Exception ex)
            {
                Log.Warn("AddSaveFile failed", ex);
                return new AddSaveResponse("Could not restore this Ymir save: " + ex.Message);
            }
        }

        private static bool Overwrite(AddSaveArgs args, string target)
        {
            if (!File.Exists(target)) return true;
            try { return args.ShouldOverwriteFunc?.Invoke() ?? true; } catch { return true; }
        }

        LbIntegrations.Saves.ExpectedDir IOwnSaves.OwnExpectedDir(IEmulator emulator, GameSaveBase save)
        {
            var exe = ResolveFullPath(Safe(() => emulator?.ApplicationPath));
            if (save == null || !YmirLaunch.IsYmirExe(exe)) return LbIntegrations.Saves.ExpectedDir.Unknown("Could not find the Ymir this game runs on.");
            var target = TargetFileFor(save, exe, out var why);
            return target == null ? LbIntegrations.Saves.ExpectedDir.Unknown(why) : LbIntegrations.Saves.ExpectedDir.FolderOr(Path.GetDirectoryName(target), why);
        }

        private static string Full(string path) => string.IsNullOrWhiteSpace(path) ? null : ResolveFullPath(path);

        /// <summary>The Ymir a game runs on: its own emulator when that is one, else the first in the library.</summary>
        private static string ExecutableOf(IGame game)
        {
            try
            {
                var dm = PluginHelper.DataManager;
                var id = Safe(() => game?.EmulatorId);
                bool IsYmir(string p) => YmirLaunch.IsYmirExe(ResolveFullPath(p));
                var own = LbIntegrations.Saves.SaveForward.ViaEmulator(IsYmir) ?? (string.IsNullOrEmpty(id) ? null : dm?.GetEmulatorById(id));
                var path = Safe(() => own?.ApplicationPath);
                if (!IsYmir(path))
                    path = Safe(() => dm?.GetAllEmulators()?.FirstOrDefault(e => IsYmir(Safe(() => e.ApplicationPath)))?.ApplicationPath);
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
                Log.Info("deleted " + loc);
                return new PluginResponse(true);
            }
            catch (Exception ex)
            {
                Log.Warn("RemoveSave failed", ex);
                return new PluginResponse(false, "Could not delete this Ymir save: " + ex.Message);
            }
        }

        private static bool IsOurs(GameSaveBase save) => SaveRows.IsOurs(save?.SaveGroupId, Emu);
    }
}
