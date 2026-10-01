// Save management for SUPER ZSNES - the cartridge save and the save states, as melonDS's are: one FILE each.
//
//   Battery save   <name>.srm                              in the SRAM folder
//   Save states    <name>.data.szsnes\<name>.szst<slot>    in the per-game data folder
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

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Unbroken.LaunchBox.Plugins;
using Unbroken.LaunchBox.Plugins.Data;

namespace LbIntegrations.SuperZsnes
{
    public partial class SuperZsnesPlugin
    {
        private const string SavePrefix = "superzsnes:save:";
        private const string StatePrefix = "superzsnes:state:";
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
            var raw = SuperZsnesCurrent.Raw(exePath, "srmPath", "gameSavePath", "noDirectoryForSaves");
            string Dir(string member)
            {
                var v = raw.TryGetValue(member, out var o) ? o as string : null;
                if (string.IsNullOrWhiteSpace(v)) return romDir;
                var persist = Path.GetDirectoryName(SuperZsnesPaths.SettingsFile(exePath)) ?? exeDir;
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

        // ── listing ──────────────────────────────────────────────────────────

        public override GetSavesResponse GetSaves(GetSavesArgs args)
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
                    states += Collect(found, seen, exe, Safe(() => app.ApplicationPath), Safe(() => app.GameId), Safe(() => app.Id));
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
            if (srm != null && File.Exists(srm) && seen.Add("save|" + srm + "|" + context))
                into.Add(SaveRow(srm, gameId, appId, SavePrefix + name));

            int states = 0;
            var dir = StatesOf(folders, name);
            if (dir == null || !Directory.Exists(dir)) return 0;
            foreach (var path in Directory.GetFiles(dir, name + ".szst*"))
            {
                var m = StateFile.Match(path);
                if (!m.Success || !Path.GetFileName(path).StartsWith(name + ".szst", StringComparison.OrdinalIgnoreCase)) continue;   // -last, and anything else
                if (!int.TryParse(m.Groups["slot"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var slot)) continue;
                if (!seen.Add("state|" + path + "|" + context)) continue;
                into.Add(StateRow(path, gameId, appId, slot, StatePrefix + name + ":" + slot, StateGroupName));
                states++;
            }
            return states;
        }

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
        public override bool IsSaveContainer(GameSaveBase save) => false;

        public override bool UseSaveGroupIdForPersistedMatch(GameSaveBase save) => IsOurs(save);

        public override bool TryBackupSave(GameSaveBase save, string emulatorApplicationPath, string destinationFolder, out string error)
        {
            error = "A SUPER ZSNES save is one file, not a container.";
            return false;
        }

        /// <summary>A plugin must never call its own save secondary; and nothing beside a .srm or a .szst is part of it.</summary>
        public override bool IsSecondarySaveFile(string filePath) => false;

        /// <summary>No companions: the state carries its own screenshot inside it (no sidecar was ever written).</summary>
        public override IReadOnlyList<string> GetCompanionSaveFiles(string primaryFilePath) => Array.Empty<string>();

        /// <summary>Active when it is where the emulator reads it now - the folder its settings name - and still there.</summary>
        public override bool IsSaveActive(GameSaveBase save, string emulatorApplicationPath)
        {
            try
            {
                if (!IsOurs(save) || string.IsNullOrWhiteSpace(save.FileLocation)) return true;
                var exe = ResolveFullPath(emulatorApplicationPath);
                if (!SuperZsnesPaths.IsSuperZsnesExecutable(exe) || !File.Exists(save.FileLocation)) return File.Exists(save.FileLocation);
                var target = TargetDirFor(save, exe, NameOfSave(save));
                return target == null || string.Equals(Path.GetFullPath(Path.GetDirectoryName(save.FileLocation)).TrimEnd('\\'),
                                                       Path.GetFullPath(target).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);
            }
            catch { return true; }
        }

        // ── restore ──────────────────────────────────────────────────────────

        /// <summary>A vault copy back where the emulator reads it, under its own file name.</summary>
        public override AddSaveResponse AddSaveFile(AddSaveArgs args)
        {
            try
            {
                var save = args?.SaveToAdd;
                if (save == null) return new AddSaveResponse("No save was supplied.");
                var source = save.FileLocation;
                if (string.IsNullOrWhiteSpace(source) || !File.Exists(source)) return new AddSaveResponse("This SUPER ZSNES backup is not a file: " + source);

                var name = new[] { save.OriginalFileName, Path.GetFileName(source) }.FirstOrDefault(n => !string.IsNullOrWhiteSpace(n));
                var game = PluginHelper.DataManager?.GetGameById(save.GameId);
                var exe = ExecutableOf(game);
                var targetDir = exe == null ? null : TargetDirFor(save, exe, NameOfSave(save), game);
                if (targetDir == null) return new AddSaveResponse("Could not work out where SUPER ZSNES reads this save from.");

                var target = Path.Combine(targetDir, name);
                if (File.Exists(target))
                {
                    bool overwrite = true;
                    try { overwrite = args.ShouldOverwriteFunc?.Invoke() ?? true; } catch { }
                    if (!overwrite) return new AddSaveResponse("Restore cancelled.");
                }
                Directory.CreateDirectory(targetDir);
                File.Copy(source, target, overwrite: true);
                Log.Info("restored " + name + " -> " + targetDir);

                if (save is GameSaveState state)
                    return new AddSaveResponse(StateRow(target, state.GameId, state.AdditionalApplicationId, state.Slot ?? 0, state.SaveGroupId,
                                                        string.IsNullOrWhiteSpace(state.SaveGroupName) ? StateGroupName : state.SaveGroupName));
                return new AddSaveResponse(SaveRow(target, save.GameId, save.AdditionalApplicationId, save.SaveGroupId, save.SaveGroupName, save.DisplayChipText));
            }
            catch (Exception ex)
            {
                Log.Warn("AddSaveFile failed", ex);
                return new AddSaveResponse("Could not restore this SUPER ZSNES save: " + ex.Message);
            }
        }

        /// <summary>The folder a save of this kind belongs in for this game, as the emulator's settings have it.</summary>
        private static string TargetDirFor(GameSaveBase save, string exe, string name, IGame game = null)
        {
            game = game ?? PluginHelper.DataManager?.GetGameById(save.GameId);
            var rom = ResolveFullPath(Safe(() => game?.ApplicationPath));
            var folders = FoldersFor(exe, rom);
            return save is GameSaveState ? StatesOf(folders, string.IsNullOrEmpty(name) ? NameOf(rom) : name) : folders.SaveDir;
        }

        /// <summary>The ROM name a save's group id carries: "superzsnes:save:&lt;name&gt;", "superzsnes:state:&lt;name&gt;:&lt;slot&gt;".</summary>
        private static string NameOfSave(GameSaveBase save)
        {
            var id = save?.SaveGroupId ?? "";
            if (id.StartsWith(SavePrefix, StringComparison.OrdinalIgnoreCase)) return id.Substring(SavePrefix.Length);
            if (!id.StartsWith(StatePrefix, StringComparison.OrdinalIgnoreCase)) return null;
            var rest = id.Substring(StatePrefix.Length);
            var colon = rest.LastIndexOf(':');
            return colon > 0 ? rest.Substring(0, colon) : rest;
        }

        /// <summary>The SUPER ZSNES a game runs on: its own emulator when that is one, else the first in the library.</summary>
        private static string ExecutableOf(IGame game)
        {
            try
            {
                var dm = PluginHelper.DataManager;
                var id = Safe(() => game?.EmulatorId);
                var own = string.IsNullOrEmpty(id) ? null : dm?.GetEmulatorById(id);
                var path = Safe(() => own?.ApplicationPath);
                if (!SuperZsnesPaths.IsSuperZsnesExecutable(path))
                    path = Safe(() => dm?.GetAllEmulators()?.FirstOrDefault(e => SuperZsnesPaths.IsSuperZsnesExecutable(Safe(() => e.ApplicationPath)))?.ApplicationPath);
                return string.IsNullOrEmpty(path) ? null : ResolveFullPath(path);
            }
            catch { return null; }
        }

        // ── delete ───────────────────────────────────────────────────────────

        public override PluginResponse RemoveSave(GameSaveBase save)
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
                return new PluginResponse(false, "Could not delete this SUPER ZSNES save: " + ex.Message);
            }
        }

        private static bool IsOurs(GameSaveBase save)
            => save?.SaveGroupId != null && (save.SaveGroupId.StartsWith(SavePrefix, StringComparison.OrdinalIgnoreCase)
                                             || save.SaveGroupId.StartsWith(StatePrefix, StringComparison.OrdinalIgnoreCase));

        private static string Safe(Func<string> read)
        {
            try { return read(); } catch { return null; }
        }
    }
}
