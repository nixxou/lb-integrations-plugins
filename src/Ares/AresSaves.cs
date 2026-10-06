// Save management for ares - each system's save files and the save states, one FILE each (as the bsnes and RASnes9x
// plugins, after SuperZsnesSaves).
//
// WHERE (ares v148 - mia\pak\pak.cpp Pak::saveLocation, desktop-ui\emulator\emulator.cpp Emulator::locate,
// desktop-ui\program\states.cpp; checked file by file against the v148 tag on 06/10):
//   Paths/Saves empty (ares's default)   beside the game: <game without its extension><ext>, and <game>.bs1 .. .bs9
//   Paths/Saves set ("D:/Saves/")        <Saves><medium>/<game><ext> - the GAME MEDIUM's saveName() ("Game Boy" for a
//                                        Game Boy Color game, "Famicom" for Dendy): AresSystems' SaveFolder; the
//                                        states in <Saves><core>/ - the core's root node ("Mega Drive" for the 32X and
//                                        the Mega CD, but "Game Boy Color", "SuperGrafx", "MSX2"...: StateFolder).
//                                        Both places are looked in.
//   THE EXTENSIONS are the game medium's (mia\medium\*.cpp save()): .ram .eeprom .flash .rtc for cartridges (the PC
//   Engine's HuCard: .ram), .d1a.. for the Famicom Disk System, .disk for the 64DD, .sav for the Neo Geo Pocket, a tape's
//   recording as <game>.wav (MSX, ZX Spectrum, SC-3000) - and two files ares writes beside the game for its pads:
//   the PlayStation's memory card <game>.card, the Nintendo 64's Controller Pak <game>.pak. Each its own row.
//   THE CONSOLE'S OWN (mia\system\*.cpp: Pak::save without a location): ONE file for every game of the system - the
//   Mega CD's, the PC Engine's (HuCard and CD alike) and the LaserActive's backup RAM, the Neo Geo Pocket's cpu/apu RAM,
//   the WonderSwan's owner EEPROM, the 64DD's clock - in <home>\<Pak>.sys\<file> (home: Paths/Home, else ares's
//   Systems\ folder), or <Saves><Pak>\<Pak><ext> when Paths/Saves is set. Listed under every game of that system, said
//   shared; a restore puts it back for all of them, as swapping the memory on the real console would.
//   NOTHING: the Neo Geo's memory card (ares\ng\card\slot.cpp: save() is empty - not kept between sessions), Atari 2600,
//   ColecoVision, a game's CD.
//   STATES: .bs1 .. .bs9, slots 1 to 9 (presentation.cpp: range(9); the current slot is 1 at every load). .bsu and .blu,
//   ares's undo copies, are deleted by ares itself at unload and are not listed.
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
        private const string ConsolePrefix = "ares:console:";
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

        /// <summary>The file ares is given for a game: a .m3u's first disc, a .7z's unpacked cartridge (or where it would
        /// go), else the game's own file.</summary>
        internal static string PlayedFile(string rom, string exe)
        {
            var ext = Path.GetExtension(rom ?? "");
            if (string.Equals(ext, ".m3u", StringComparison.OrdinalIgnoreCase)) return AresLaunch.RomFor(rom, null, exe, out _) ?? rom;
            if (string.Equals(ext, ".7z", StringComparison.OrdinalIgnoreCase) && exe != null)
                return AresLaunch.Unpacked(exe, rom) ?? Path.Combine(AresLaunch.UnpackDir(exe, rom), Path.GetFileNameWithoutExtension(rom) + ".rom");
            return rom;
        }

        internal static string NameOf(string rom, string exe = null) => Path.GetFileNameWithoutExtension(PlayedFile(rom, exe) ?? "");

        internal static Folders FoldersFor(string exe, string rom, AresSystem system)
        {
            var f = new Folders();
            var beside = string.IsNullOrWhiteSpace(rom) ? null : Path.GetDirectoryName(Path.GetFullPath(PlayedFile(rom, exe)));
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
            f.SaveTarget = saveUnder ?? beside;   // pak.cpp:154-172: every system's saves under Paths/Saves when it is set
            f.StateTarget = stateUnder ?? beside;
            return f;
        }

        /// <summary>Where ares keeps a console's own file (desktop-ui.cpp: mia home = Paths/Home, else locate("Systems/") -
        /// the exe's folder, then %LOCALAPPDATA%\ares, else the exe's; pak.cpp saveLocation with Paths/Saves set).</summary>
        internal static string ConsolePath(string exe, AresConsoleFile f)
        {
            if (exe == null || f == null) return null;
            string saves = null, home = null;
            try
            {
                var bml = AresBml.Load(AresLaunch.SettingsOf(exe));
                saves = bml.Get("Paths/Saves");
                home = bml.Get("Paths/Home");
            }
            catch { }
            if (!string.IsNullOrWhiteSpace(saves)) return Path.Combine(Path.GetFullPath(saves.Replace('/', Path.DirectorySeparatorChar)), f.Pak, f.Pak + f.Ext);
            string root;
            if (!string.IsNullOrWhiteSpace(home)) root = Path.GetFullPath(home.Replace('/', Path.DirectorySeparatorChar));
            else
            {
                var beside = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(exe)), "Systems");
                var user = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ares", "Systems");
                root = Directory.Exists(beside) || !Directory.Exists(user) ? beside : user;
            }
            return Path.Combine(root, f.Pak + ".sys", f.File);
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
            var name = NameOf(rom, exe);
            if (name.Length == 0) return;
            var folders = FoldersFor(exe, rom, system);
            var context = appId ?? gameId;

            foreach (var dir in folders.SaveDirs)
                foreach (var ext in system.SaveExtensions)
                {
                    var path = Path.Combine(dir, name + ext);
                    if (string.Equals(path, rom, StringComparison.OrdinalIgnoreCase)) continue;   // a .wav tape is the game, not its recording
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
            foreach (var cf in system.ConsoleFiles)
            {
                var path = ConsolePath(exe, cf);
                if (path != null && File.Exists(path) && seen.Add("console|" + path + "|" + context))
                    into.Add(SaveRow(path, gameId, appId, ConsolePrefix + cf.Pak + ":" + cf.File,
                                     "Console memory (" + cf.File + ", shared by every " + cf.Pak + " game)", "CONSOLE"));
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
                string target = null;
                // The console's own file goes back where ares keeps it - for every game of the system.
                if (save.SaveGroupId != null && save.SaveGroupId.StartsWith(ConsolePrefix, StringComparison.OrdinalIgnoreCase))
                {
                    var parts = save.SaveGroupId.Substring(ConsolePrefix.Length).Split(new[] { ':' }, 2);
                    var cf = parts.Length == 2 ? system?.ConsoleFiles.FirstOrDefault(c => c.Pak == parts[0] && c.File == parts[1]) : null;
                    target = ConsolePath(exe, cf ?? (parts.Length == 2 ? new AresConsoleFile { Pak = parts[0], File = parts[1], Ext = Path.GetExtension(name) } : null));
                    if (target == null) return new AddSaveResponse("Could not work out where ares keeps this console's memory.");
                    targetDir = Path.GetDirectoryName(target);
                }
                if (targetDir == null) return new AddSaveResponse("Could not work out where ares reads this save from.");

                target = target ?? Path.Combine(targetDir, name);
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
                                             || save.SaveGroupId.StartsWith(StatePrefix, StringComparison.OrdinalIgnoreCase)
                                             || save.SaveGroupId.StartsWith(ConsolePrefix, StringComparison.OrdinalIgnoreCase));

        private static string Safe(Func<string> read)
        {
            try { return read(); } catch { return null; }
        }
    }
}
