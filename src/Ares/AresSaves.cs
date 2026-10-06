// Save management for ares - each system's save files and the save states, one FILE each (as the bsnes and RASnes9x
// plugins, after SuperZsnesSaves).
//
// WHERE (ares v148, mia\pak\pak.cpp:154-172 and desktop-ui\program\states.cpp, read 06/10):
//   Paths/Saves empty (ares's default)   beside the game: <game without its extension><ext>, and <game>.bs1 .. .bs9
//   Paths/Saves set ("D:/Saves/")        <Saves><system>/<game><ext>, the states in <Saves><core>/ - the core's folder
//                                        is the family's ("Mega Drive" for the 32X and the Mega CD: AresSystems'
//                                        SaveFolder / StateFolder). Both places are looked in.
//   THE EXTENSIONS are the system's (mia\medium\*.cpp): .ram .eeprom .flash .rtc for cartridges, .bram for the Mega CD and
//   the PC Engine CD, .card for the PlayStation's memory card, .d1a.. for the Famicom Disk System - each its own row, so a
//   cartridge's .ram and its .rtc are both kept.
//   STATES: .bs1 .. .bs9, slots 1 to 9 (the current slot is 1 at every load). .bsu and .blu, ares's undo copies, are
//   deleted by ares itself at unload and are not listed.
// THE GAME'S NAME is the file ares was given: a zip's or a .7z's own name (the .7z is unpacked under it), a .m3u's first
// disc.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Unbroken.LaunchBox.Plugins;
using Unbroken.LaunchBox.Plugins.Data;

namespace LbIntegrations.Ares
{
    public partial class AresPlugin
    {
        private const string SavePrefix = "ares:save:";
        private const string StatePrefix = "ares:state:";
        private const string SaveGroupName = "Save";
        private const string StateGroupName = "Save states";
        private const string StateChipText = "STATE";

        private static readonly Regex StateFile = new Regex(@"\.bs(?<slot>[1-9])$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        public override bool SupportsSaveManagement() => true;

        public override IReadOnlyDictionary<int, string> GetPotentialSaveSlots()
        {
            var slots = new Dictionary<int, string>();
            for (int i = 1; i <= 9; i++) slots[i] = i.ToString(CultureInfo.InvariantCulture);
            return slots;
        }

        // ── where ares keeps a game's ────────────────────────────────────────

        internal sealed class Folders { public List<string> SaveDirs = new List<string>(), StateDirs = new List<string>(); public string SaveTarget, StateTarget; }

        /// <summary>The file ares is given for a game: a .m3u's first disc, else the game's own file.</summary>
        internal static string PlayedFile(string rom)
        {
            if (!string.Equals(Path.GetExtension(rom ?? ""), ".m3u", StringComparison.OrdinalIgnoreCase)) return rom;
            var played = AresLaunch.RomFor(rom, null, out _);
            return played ?? rom;
        }

        internal static string NameOf(string rom) => Path.GetFileNameWithoutExtension(PlayedFile(rom) ?? "");

        internal static Folders FoldersFor(string exe, string rom, AresSystem system)
        {
            var f = new Folders();
            var beside = string.IsNullOrWhiteSpace(rom) ? null : Path.GetDirectoryName(Path.GetFullPath(PlayedFile(rom)));
            string saves = null;
            try
            {
                var v = AresBml.Load(AresLaunch.SettingsOf(exe)).Get("Paths/Saves");
                if (!string.IsNullOrWhiteSpace(v)) saves = Path.GetFullPath(v.Replace('/', Path.DirectorySeparatorChar));
            }
            catch { }
            string Under(string folder) => saves == null || system == null ? null : Path.Combine(saves, folder ?? system.Name);
            var saveUnder = Under(system?.SaveFolder);
            var stateUnder = Under(system?.StateFolder);
            foreach (var d in new[] { saveUnder, beside }) if (d != null && !f.SaveDirs.Contains(d, StringComparer.OrdinalIgnoreCase)) f.SaveDirs.Add(d);
            foreach (var d in new[] { stateUnder, beside }) if (d != null && !f.StateDirs.Contains(d, StringComparer.OrdinalIgnoreCase)) f.StateDirs.Add(d);
            f.SaveTarget = system != null && system.Disc ? beside : saveUnder ?? beside;
            f.StateTarget = stateUnder ?? beside;
            return f;
        }

        /// <summary>The system a game runs as, from what the host knows of it - see AresLaunch.SystemFor.</summary>
        internal static AresSystem SystemOf(IEmulator emu, string gameId, string platform, string rom)
        {
            string line = null;
            try { line = emu?.GetAllEmulatorPlatforms()?.FirstOrDefault(r => string.Equals(r?.Platform, platform, StringComparison.OrdinalIgnoreCase))?.CommandLine; } catch { }
            return AresSystems.System(AresLaunch.SystemFor(gameId, platform, rom, line).System);
        }

        /// <summary>For a game's window: the system its platform gives, by name (its own choice left out).</summary>
        internal static string SystemOfForUi(IEmulator emu, string gameId, string platform, string rom) => SystemOf(emu, gameId, platform, rom)?.Name;

        // ── listing ──────────────────────────────────────────────────────────

        public override GetSavesResponse GetSaves(GetSavesArgs args)
        {
            try
            {
                if (args?.Emulator == null) return new GetSavesResponse("No emulator was supplied.");
                var exe = ResolveFullPath(Safe(() => args.Emulator.ApplicationPath));
                if (!AresLaunch.IsAresExe(exe)) return new GetSavesResponse("This emulator is not ares.");

                var found = new List<GameSaveBase>();
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var dm = PluginHelper.DataManager;
                foreach (var app in args.AdditionalApplications ?? (IReadOnlyCollection<IAdditionalApplication>)Array.Empty<IAdditionalApplication>())
                {
                    var game = dm?.GetGameById(Safe(() => app.GameId));
                    Collect(found, seen, exe, SystemOf(args.Emulator, Safe(() => app.GameId), Safe(() => game?.Platform), Safe(() => app.ApplicationPath)),
                            Safe(() => app.ApplicationPath), Safe(() => app.GameId), Safe(() => app.Id));
                }
                foreach (var game in args.Games ?? (IReadOnlyCollection<IGame>)Array.Empty<IGame>())
                    Collect(found, seen, exe, SystemOf(args.Emulator, Safe(() => game.Id), Safe(() => game.Platform), Safe(() => game.ApplicationPath)),
                            Safe(() => game.ApplicationPath), Safe(() => game.Id), null);

                Log.Verbose("found " + found.Count + " file(s) for " + exe);
                return new GetSavesResponse(found);
            }
            catch (Exception ex)
            {
                Log.Warn("GetSaves failed", ex);
                return new GetSavesResponse("Could not read ares saves: " + ex.Message);
            }
        }

        internal static void Collect(List<GameSaveBase> into, HashSet<string> seen, string exe, AresSystem system, string romPath, string gameId, string appId)
        {
            if (system == null || string.IsNullOrWhiteSpace(gameId) || string.IsNullOrWhiteSpace(romPath)) return;
            var rom = ResolveFullPath(romPath);
            var name = NameOf(rom);
            if (name.Length == 0) return;
            var folders = FoldersFor(exe, rom, system);
            var context = appId ?? gameId;

            foreach (var dir in folders.SaveDirs)
                foreach (var ext in system.SaveExtensions)
                {
                    var path = Path.Combine(dir, name + ext);
                    if (File.Exists(path) && seen.Add("save|" + path + "|" + context))
                        into.Add(SaveRow(path, gameId, appId, SavePrefix + system.Node + ":" + name + ext, SaveGroupName + " (" + ext.TrimStart('.') + ")", ext.TrimStart('.').ToUpperInvariant()));
                }
            foreach (var dir in folders.StateDirs)
                for (int slot = 1; slot <= 9; slot++)
                {
                    var path = Path.Combine(dir, name + ".bs" + slot);
                    if (File.Exists(path) && seen.Add("state|" + path + "|" + context))
                        into.Add(StateRow(path, gameId, appId, slot, StatePrefix + system.Node + ":" + name + ":" + slot));
                }
        }

        private static GameSaveGame SaveRow(string path, string gameId, string appId, string groupId, string groupName, string chip)
        {
            long size = 0; DateTime when = default;
            try { var i = new FileInfo(path); size = i.Length; when = i.LastWriteTimeUtc; } catch { }
            return new GameSaveGame
            {
                GameId = gameId, AdditionalApplicationId = appId, FileLocation = path, IsDirectory = false,
                OriginalFileName = Path.GetFileName(path), SaveGroupId = groupId, SaveGroupName = groupName, DisplayChipText = chip,
                ReportedFileSizeBytes = size > 0 ? size : (long?)null, ReportedLastModifiedUtc = when == default ? (DateTime?)null : when,
            };
        }

        private static GameSaveState StateRow(string path, string gameId, string appId, int slot, string groupId)
        {
            long size = 0; DateTime when = default;
            try { var i = new FileInfo(path); size = i.Length; when = i.LastWriteTimeUtc; } catch { }
            return new GameSaveState
            {
                GameId = gameId, AdditionalApplicationId = appId, FileLocation = path, IsDirectory = false,
                OriginalFileName = Path.GetFileName(path), Slot = slot, SaveGroupId = groupId, SaveGroupName = StateGroupName, DisplayChipText = StateChipText,
                ReportedFileSizeBytes = size > 0 ? size : (long?)null, ReportedLastModifiedUtc = when == default ? (DateTime?)null : when,
            };
        }

        // ── the contract ─────────────────────────────────────────────────────

        public override bool IsSaveContainer(GameSaveBase save) => false;

        public override bool UseSaveGroupIdForPersistedMatch(GameSaveBase save) => IsOurs(save);

        public override bool TryBackupSave(GameSaveBase save, string emulatorApplicationPath, string destinationFolder, out string error)
        {
            error = "An ares save is one file, not a container.";
            return false;
        }

        public override bool IsSecondarySaveFile(string filePath) => false;

        public override IReadOnlyList<string> GetCompanionSaveFiles(string primaryFilePath) => Array.Empty<string>();

        public override bool IsSaveActive(GameSaveBase save, string emulatorApplicationPath)
        {
            try { return string.IsNullOrWhiteSpace(save?.FileLocation) || File.Exists(save.FileLocation); }
            catch { return true; }
        }

        // ── restore ──────────────────────────────────────────────────────────

        public override AddSaveResponse AddSaveFile(AddSaveArgs args)
        {
            try
            {
                var save = args?.SaveToAdd;
                if (save == null) return new AddSaveResponse("No save was supplied.");
                var source = save.FileLocation;
                if (string.IsNullOrWhiteSpace(source) || !File.Exists(source)) return new AddSaveResponse("This ares backup is not a file: " + source);

                var name = new[] { save.OriginalFileName, Path.GetFileName(source) }.FirstOrDefault(n => !string.IsNullOrWhiteSpace(n));
                var dm = PluginHelper.DataManager;
                var game = dm?.GetGameById(save.GameId);
                var emu = EmulatorOf(game);
                var exe = emu == null ? null : ResolveFullPath(Safe(() => emu.ApplicationPath));
                if (exe == null) return new AddSaveResponse("Could not find the ares this game runs on.");
                if (AresLaunch.Running(exe)) return new AddSaveResponse("ares is running: it would write the game's own over this one. Close it first.");
                var system = SystemOf(emu, Safe(() => game?.Id), Safe(() => game?.Platform), Safe(() => game?.ApplicationPath));
                var folders = FoldersFor(exe, ResolveFullPath(Safe(() => game?.ApplicationPath)), system);
                var targetDir = save is GameSaveState ? folders.StateTarget : folders.SaveTarget;
                if (targetDir == null) return new AddSaveResponse("Could not work out where ares reads this save from.");

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
                if (save is GameSaveState state) return new AddSaveResponse(StateRow(target, state.GameId, state.AdditionalApplicationId, state.Slot ?? 1, state.SaveGroupId));
                return new AddSaveResponse(SaveRow(target, save.GameId, save.AdditionalApplicationId, save.SaveGroupId, save.SaveGroupName, save.DisplayChipText));
            }
            catch (Exception ex)
            {
                Log.Warn("AddSaveFile failed", ex);
                return new AddSaveResponse("Could not restore this ares save: " + ex.Message);
            }
        }

        /// <summary>The ares a game runs on: its own emulator when that is one, else the first in the library.</summary>
        private static IEmulator EmulatorOf(IGame game)
        {
            try
            {
                var dm = PluginHelper.DataManager;
                var id = Safe(() => game?.EmulatorId);
                var own = string.IsNullOrEmpty(id) ? null : dm?.GetEmulatorById(id);
                if (own != null && AresLaunch.IsAresExe(Safe(() => own.ApplicationPath))) return own;
                return dm?.GetAllEmulators()?.FirstOrDefault(e => AresLaunch.IsAresExe(Safe(() => e.ApplicationPath)));
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
                return new PluginResponse(false, "Could not delete this ares save: " + ex.Message);
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
