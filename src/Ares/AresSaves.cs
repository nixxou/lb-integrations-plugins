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
//   THE CONSOLE'S OWN (mia\system\*.cpp: Pak::save without a location): ONE file for every game of the system, in
//   <home>\<Pak>.sys\<file> (home: Paths/Home, else ares's Systems\ folder), or <Saves><Pak>\<Pak><ext> with Paths/Saves.
//   Made each game's (AresConsoleSession): the backup RAM of the Mega CD, the PC Engine (HuCard and CD) and the
//   LaserActive is the game's <game>.bram beside its saves, a row of its own; the 64DD's clock, <game>.rtc, travels with
//   the game's .disk (a companion). The Neo Geo Pocket's cpu/apu RAM and the WonderSwan's owner EEPROM hold the
//   console's settings and no game's save (the games save on their cartridge): one console, never listed.
//   A DISC GAME is named by its .m3u (its identity): <m3u>.card; the states keep each disc's name.
//   NOTHING: the Neo Geo's memory card (ares\ng\card\slot.cpp: save() is empty - not kept between sessions), Atari 2600,
//   ColecoVision, a game's CD.
//   STATES: .bs1 .. .bs9, slots 1 to 9 (presentation.cpp: range(9); the current slot is 1 at every load). .bsu and .blu,
//   ares's undo copies, are deleted by ares itself at unload and are not listed.
// THE GAME'S NAME is the file ares was given: a zip's or a .7z's own name (the .7z is unpacked under it), a .m3u's first
// disc.
//
// THE IDS are the pack's JSON (Shared.Saves, docs\saves-lb14-routing.md §3): {"emu":"ares","kind":"save","sys":"<node>",
// "by":"name","id":"<name><ext>"}, "kind":"console" for the game's own copy of its console's memory, "kind":"state"
// with "id":"<name>" and its "slot". A restore goes to the version the row is tied to, else the game (SaveRows.TargetFor),
// under that rom's names today: a save already named after one of them keeps its name; a cartridge save, or a game's
// console copy, named after an old rom takes the game's name now; a disc game's state keeps its disc's name.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using LbIntegrations.Lbip;
using LbIntegrations.Saves;
using System.Text.RegularExpressions;
using Unbroken.LaunchBox.Plugins;
using Unbroken.LaunchBox.Plugins.Data;

namespace LbIntegrations.Ares
{
    public partial class AresPlugin
    {
        /// <summary>The front desk (Shared.Saves SaveDesk): this plugin's emu, and where it logs.</summary>
        private string SaveEmu => Emu;
        private void SaveLog(string message) => Log.Info(message);

        /// <summary>This plugin's "emu" in the ids (Shared.Saves).</summary>
        private const string Emu = "ares";
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
        /// go), else the game's own file. A big listing works it out once per game, not 4 times (SaveScan).</summary>
        internal static string PlayedFile(string rom, string exe) => LbIntegrations.Saves.SaveScan.Memo("ares.played|" + rom + "|" + exe, () => PlayedFileCore(rom, exe));

        private static string PlayedFileCore(string rom, string exe)
        {
            var ext = Path.GetExtension(rom ?? "");
            if (string.Equals(ext, ".m3u", StringComparison.OrdinalIgnoreCase)) return AresLaunch.RomFor(rom, null, exe, out _) ?? rom;
            if (string.Equals(ext, ".7z", StringComparison.OrdinalIgnoreCase) && exe != null)
                return AresLaunch.Unpacked(exe, rom) ?? Path.Combine(AresLaunch.UnpackDir(exe, rom), Path.GetFileNameWithoutExtension(rom) + ".rom");
            return rom;
        }

        /// <summary>ares's folder: a relative path of its settings.bml is relative to it (ExpectedDir.OfConfig).</summary>
        private static string ExeDir(string exe) { try { return exe == null ? null : Path.GetDirectoryName(Path.GetFullPath(exe)); } catch { return null; } }

        internal static string NameOf(string rom, string exe = null) => Path.GetFileNameWithoutExtension(PlayedFile(rom, exe) ?? "");

        internal static Folders FoldersFor(string exe, string rom, AresSystem system)
        {
            var f = new Folders();
            var beside = string.IsNullOrWhiteSpace(rom) ? null : Path.GetDirectoryName(Path.GetFullPath(PlayedFile(rom, exe)));
            string saves = null;
            try
            {
                // A big listing: settings.bml read once, not 3 times per game (LbIntegrations.Saves.SaveScan).
                var v = LbIntegrations.Saves.SaveScan.Memo("ares.saves|" + exe, () => AresBml.Load(AresLaunch.SettingsOf(exe)).Get("Paths/Saves"));
                saves = LbIntegrations.Saves.ExpectedDir.OfConfig(v, ExeDir(exe));   // relative: from ares's folder, its current directory
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
        internal static string ConsolePath(string exe, AresConsoleFile f) => ConsolePath(exe, f, null);

        /// <summary>The same, with Paths/Saves read as <paramref name="savesAs"/> when it is given ("" = unset): where ares
        /// kept it before the plugin set the folder (AresSavePaths).</summary>
        internal static string ConsolePath(string exe, AresConsoleFile f, string savesAs)
        {
            if (exe == null || f == null) return null;
            string saves = null, home = null;
            try
            {
                var bml = AresBml.Load(AresLaunch.SettingsOf(exe));
                saves = savesAs ?? bml.Get("Paths/Saves");
                home = bml.Get("Paths/Home");
            }
            catch { }
            var savesDir = LbIntegrations.Saves.ExpectedDir.OfConfig(saves, ExeDir(exe));
            string root;
            if (LbIntegrations.Saves.ExpectedDir.OfConfig(home, ExeDir(exe)) is string h) root = h;
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
            try { line = LbIntegrations.Saves.SaveScan.Memo("ares.line|" + emu?.Id + "|" + platform, () => emu?.GetAllEmulatorPlatforms()?.FirstOrDefault(r => string.Equals(r?.Platform, platform, StringComparison.OrdinalIgnoreCase))?.CommandLine); } catch { }
            return AresSystems.System(AresLaunch.SystemFor(gameId, platform, rom, line).System);
        }

        /// <summary>For a game's window: the system its platform gives, by name (its own choice left out).</summary>
        internal static string SystemOfForUi(IEmulator emu, string gameId, string platform, string rom) => SystemOf(emu, gameId, platform, rom)?.Name;

        // ── listing ──────────────────────────────────────────────────────────

        GetSavesResponse IOwnSaves.OwnGetSaves(GetSavesArgs args)
        {
            try
            {
                if (args?.Emulator == null) return new GetSavesResponse("No emulator was supplied.");
                var exe = ResolveFullPath(Safe(() => args.Emulator.ApplicationPath));
                if (!AresLaunch.IsAresExe(exe)) return new GetSavesResponse("This emulator is not ares.");

                AresConsoleSession.Recover();   // a session LaunchBox did not see end: its memory taken back first
                var found = new List<GameSaveBase>();
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var dm = PluginHelper.DataManager;
                int games = args.Games?.Count ?? 0, apps = args.AdditionalApplications?.Count ?? 0;
                foreach (var app in args.AdditionalApplications ?? (IReadOnlyCollection<IAdditionalApplication>)Array.Empty<IAdditionalApplication>())
                {
                    var game = dm?.GetGameById(Safe(() => app.GameId));
                    var system = SystemOf(args.Emulator, Safe(() => app.GameId), Safe(() => game?.Platform), Safe(() => app.ApplicationPath));
                    int before = found.Count;
                    // A version on its game's own file is not tied to the version (SaveRows.TieTo).
                    Collect(found, seen, exe, system, Safe(() => app.ApplicationPath), Safe(() => app.GameId),
                            SaveRows.TieTo(Safe(() => app.Id), Safe(() => game?.ApplicationPath), Safe(() => app.ApplicationPath), Full));
                    Trace("app \"" + Safe(() => app.Name) + "\" of \"" + Safe(() => game?.Title) + "\"", Safe(() => game?.Platform), Safe(() => app.ApplicationPath), exe, system, found.Count - before);
                }
                foreach (var game in args.Games ?? (IReadOnlyCollection<IGame>)Array.Empty<IGame>())
                {
                    var system = SystemOf(args.Emulator, Safe(() => game.Id), Safe(() => game.Platform), Safe(() => game.ApplicationPath));
                    int before = found.Count;
                    Collect(found, seen, exe, system, Safe(() => game.ApplicationPath), Safe(() => game.Id), null);
                    Trace("game \"" + Safe(() => game.Title) + "\" (emulator " + (Safe(() => game.EmulatorId) is string e && e.Length > 0 ? Safe(() => dm?.GetEmulatorById(e)?.Title) ?? e : "none") + ")",
                          Safe(() => game.Platform), Safe(() => game.ApplicationPath), exe, system, found.Count - before);
                }

                Log.Info("GetSaves: " + games + " game(s), " + apps + " app(s) asked, " + found.Count + " file(s) found for " + exe);
                return new GetSavesResponse(found);
            }
            catch (Exception ex)
            {
                Log.Warn("GetSaves failed", ex);
                return new GetSavesResponse("Could not read ares saves: " + ex.Message);
            }
        }

        /// <summary>One line per game a listing asks for (07/10: LaunchBox's listings answered 0 for games whose files
        /// were there - which game it asks, as what, where it was looked for).</summary>
        private static void Trace(string what, string platform, string rawPath, string exe, AresSystem system, int found)
        {
            if (LbIntegrations.Saves.SaveScan.Active) return;   // a big listing (the start-up scan): its summary line only
            try
            {
                var rom = string.IsNullOrWhiteSpace(rawPath) ? null : ResolveFullPath(rawPath);
                var where = system == null || rom == null ? "-" : string.Join(" ; ", FoldersFor(exe, rom, system).SaveDirs.Concat(FoldersFor(exe, rom, system).StateDirs).Distinct(StringComparer.OrdinalIgnoreCase));
                Log.Info("  " + what + " [" + (platform ?? "no platform") + "] path \"" + rawPath + "\" -> " + (rom ?? "-") + (rom != null && !File.Exists(rom) ? " (MISSING)" : "")
                         + ", system " + (system?.Name ?? "NONE") + ", looked in " + where + ": " + found);
            }
            catch (Exception ex) { Log.Info("  " + what + ": trace failed - " + ex.Message); }
        }
        internal static void Collect(List<GameSaveBase> into, HashSet<string> seen, string exe, AresSystem system, string romPath, string gameId, string appId)
        {
            if (system == null || string.IsNullOrWhiteSpace(gameId) || string.IsNullOrWhiteSpace(romPath)) return;
            var rom = ResolveFullPath(romPath);
            var identity = AresConsoleSession.IdentityOf(rom, exe);
            if (identity.Length == 0) return;
            // The game's name first, then its discs' (a .m3u's): a card not yet brought back, the states of each disc.
            var names = new[] { identity }.Concat(system.Disc ? AresConsoleSession.DiscNames(rom, system, exe) : new List<string>())
                                          .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            var folders = FoldersFor(exe, rom, system);
            var context = appId ?? gameId;

            foreach (var name in names)
                foreach (var dir in folders.SaveDirs)
                    foreach (var ext in system.SaveExtensions)
                    {
                        var path = Path.Combine(dir, name + ext);
                        if (string.Equals(path, rom, StringComparison.OrdinalIgnoreCase)) continue;   // a .wav tape is the game, not its recording
                        if (LbIntegrations.Saves.SaveScan.Exists(path) && seen.Add("save|" + path + "|" + context))
                            into.Add(SaveRow(path, gameId, appId, IdOf("save", system, name + ext), SaveGroupName + " (" + ext.TrimStart('.') + ")", ext.TrimStart('.').ToUpperInvariant()));
                    }
            foreach (var name in names)
                foreach (var dir in folders.StateDirs)
                    for (int slot = 1; slot <= 9; slot++)
                    {
                        var path = Path.Combine(dir, name + ".bs" + slot);
                        if (LbIntegrations.Saves.SaveScan.Exists(path) && seen.Add("state|" + path + "|" + context))
                            into.Add(StateRow(path, gameId, appId, slot, IdOf("state", system, name, slot)));
                    }
            // The game's own copy of its console's memory (AresConsoleSession) - the 64DD's clock travels with its .disk
            // instead (GetCompanionSaveFiles). The console's settings alone (PerGame false) are no game's save.
            foreach (var cf in system.ConsoleFiles.Where(c => c.PerGame && c.Ext != ".rtc"))
                foreach (var dir in folders.SaveDirs)
                {
                    var path = Path.Combine(dir, identity + cf.Ext);
                    if (LbIntegrations.Saves.SaveScan.Exists(path) && seen.Add("save|" + path + "|" + context))
                        into.Add(SaveRow(path, gameId, appId, IdOf("console", system, identity + cf.Ext), "Console memory (" + cf.Pak + " " + cf.File + ")", "BRAM"));
                }
        }

        private static string IdOf(string kind, AresSystem system, string id, int? slot = null)
            => SaveId.Write(new SaveIdData { Emu = Emu, Kind = kind, Sys = system.Node, By = "name", Id = id, Slot = slot });

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

        bool IOwnSaves.OwnIsSaveContainer(GameSaveBase save) => false;

        bool IOwnSaves.OwnUseSaveGroupIdForPersistedMatch(GameSaveBase save) => IsOurs(save);

        bool IOwnSaves.OwnTryBackupSave(GameSaveBase save, string emulatorApplicationPath, string destinationFolder, out string error)
        {
            error = "An ares save is one file, not a container.";
            return false;
        }

        /// <summary>The 64DD's clock beside its disk, a state's thumbnail beside it: part of their save, not saves of their own.</summary>
        bool IOwnSaves.OwnIsSecondarySaveFile(string filePath)
        {
            try
            {
                if (filePath == null) return false;
                if (LbipStateThumbs.IsThumb(filePath)) return IsStateFile(filePath.Substring(0, filePath.Length - LbipStateThumbs.Suffix.Length));
                return filePath.EndsWith(".rtc", StringComparison.OrdinalIgnoreCase) && File.Exists(Path.ChangeExtension(filePath, ".disk"));
            }
            catch { return false; }
        }

        /// <summary>One of ares's slots: .bs1 .. .bs9.</summary>
        internal static bool IsStateFile(string path)
        {
            var ext = Path.GetExtension(path ?? "");
            return ext.Length == 4 && ext.StartsWith(".bs", StringComparison.OrdinalIgnoreCase) && ext[3] >= '1' && ext[3] <= '9';
        }

        /// <summary>For the session: the thumbnails of the states this game writes (LbipStateThumbs).</summary>
        private static void WatchStateThumbs(string exe, AresSystem system, string rom)
        {
            try
            {
                if (system == null || string.IsNullOrWhiteSpace(rom)) return;
                var full = ResolveFullPath(rom);
                var names = new HashSet<string>(new[] { AresConsoleSession.IdentityOf(full, exe) }
                    .Concat(system.Disc ? AresConsoleSession.DiscNames(full, system, exe) : new List<string>()), StringComparer.OrdinalIgnoreCase);
                LbipStateThumbs.Watch(exe, FoldersFor(exe, full, system).StateDirs,
                    p => IsStateFile(p) && names.Contains(Path.GetFileNameWithoutExtension(p)), Log.Info);
            }
            catch (Exception ex) { Log.Warn("state thumbnails", ex); }
        }

        /// <summary>A 64DD disk's save takes its clock along (the game's copy of the console's time.rtc, AresConsoleSession):
        /// backed up and restored together, so the time the game sees goes on from the save's.</summary>
        IReadOnlyList<string> IOwnSaves.OwnGetCompanionSaveFiles(string primaryFilePath)
        {
            try
            {
                if (IsStateFile(primaryFilePath)) return LbipStateThumbs.Companion(primaryFilePath);
                if (string.IsNullOrWhiteSpace(primaryFilePath) || !primaryFilePath.EndsWith(".disk", StringComparison.OrdinalIgnoreCase)) return Array.Empty<string>();
                var clock = Path.ChangeExtension(primaryFilePath, ".rtc");
                return File.Exists(clock) ? new[] { clock } : Array.Empty<string>();
            }
            catch { return Array.Empty<string>(); }
        }

        /// <summary>Active when the file is where ares reads it: under its Paths/Saves, or beside the rom - and still
        /// there. Not merely "the file exists" (measured 08/10: a backup in LaunchBox's vault was then "Active" and
        /// its Restore never offered). Called hundreds of times: the settings are read once per exe.</summary>
        bool IOwnSaves.OwnIsSaveActive(GameSaveBase save, string emulatorApplicationPath)
        {
            try
            {
                if (!IsOurs(save) || string.IsNullOrWhiteSpace(save.FileLocation)) return true;
                var file = ResolveFullPath(save.FileLocation);
                if (!File.Exists(file)) return false;
                var dir = Path.GetDirectoryName(file)?.TrimEnd(Path.DirectorySeparatorChar) ?? "";
                var exe = string.IsNullOrWhiteSpace(emulatorApplicationPath) ? null : ResolveFullPath(emulatorApplicationPath);
                var root = exe == null ? null : SavesRootOf(exe);
                if (root != null && dir.StartsWith(root, StringComparison.OrdinalIgnoreCase)
                    && (dir.Length == root.Length || dir[root.Length] == Path.DirectorySeparatorChar)) return true;
                foreach (var rom in RomsOf(save))
                {
                    var beside = Path.GetDirectoryName(Path.GetFullPath(PlayedFile(rom, exe)))?.TrimEnd(Path.DirectorySeparatorChar);
                    if (beside != null && string.Equals(beside, dir, StringComparison.OrdinalIgnoreCase)) return true;
                }
                return false;
            }
            catch { return true; }
        }

        private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, (DateTime Written, long Length, string Root)> _savesRoot = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>ares's Paths/Saves for <paramref name="exe"/>, full; null when it is not set (saves beside the rom). Kept while
        /// settings.bml keeps its date and size (09/10: kept for the whole session before, a Game Saves opened before the first
        /// launch wrote Paths/Saves read "not set" until LaunchBox restarted - every save in Saves\ shown inactive).</summary>
        private static string SavesRootOf(string exe)
        {
            var settings = AresLaunch.SettingsOf(exe);
            DateTime written = default; long length = -1;
            try { var fi = new FileInfo(settings); if (fi.Exists) { written = fi.LastWriteTimeUtc; length = fi.Length; } } catch { }
            if (_savesRoot.TryGetValue(exe, out var c) && c.Written == written && c.Length == length) return c.Root;
            string root = null;
            try
            {
                var v = AresBml.Load(settings).Get("Paths/Saves");
                root = LbIntegrations.Saves.ExpectedDir.OfConfig(v, ExeDir(exe))?.TrimEnd(Path.DirectorySeparatorChar);
            }
            catch { }
            _savesRoot[exe] = (written, length, root);
            return root;
        }

        /// <summary>The roms a row can belong to: its version's when it names one, and its game's.</summary>
        private static IEnumerable<string> RomsOf(GameSaveBase save)
        {
            IGame game = null;
            try { game = PluginHelper.DataManager?.GetGameById(save.GameId); } catch { }
            if (game == null) yield break;
            if (!string.IsNullOrEmpty(save.AdditionalApplicationId))
            {
                string ap = null;
                try { ap = game.GetAllAdditionalApplications()?.FirstOrDefault(a => a?.Id == save.AdditionalApplicationId)?.ApplicationPath; } catch { }
                if (!string.IsNullOrWhiteSpace(ap)) yield return ResolveFullPath(ap);
            }
            var gp = Safe(() => game.ApplicationPath);
            if (!string.IsNullOrWhiteSpace(gp)) yield return ResolveFullPath(gp);
        }

        // ── restore ──────────────────────────────────────────────────────────

        AddSaveResponse IOwnSaves.OwnAddSaveFile(AddSaveArgs args)
        {
            try
            {
                var save = args?.SaveToAdd;
                if (save == null) return new AddSaveResponse("No save was supplied.");
                var source = save.FileLocation;
                if (string.IsNullOrWhiteSpace(source) || !File.Exists(source)) return new AddSaveResponse("This ares backup is not a file: " + source);

                var original = new[] { save.OriginalFileName, Path.GetFileName(source) }.FirstOrDefault(n => !string.IsNullOrWhiteSpace(n));
                var emu = EmulatorOf(PluginHelper.DataManager?.GetGameById(save.GameId));
                var exe = emu == null ? null : ResolveFullPath(Safe(() => emu.ApplicationPath));
                if (exe == null) return new AddSaveResponse("Could not find the ares this game runs on.");
                if (AresLaunch.Running(exe)) return new AddSaveResponse("ares is running: it would write the game's own over this one. Close it first.");
                // Where it goes: the same answer as OwnExpectedDir gives for this install (one computation).
                var where = Destination(emu, save, out var to, out var system);
                if (to?.Warning != null) Log.Warn("restore of " + original + ": " + to.Warning);
                if (to?.Refused == true) return new AddSaveResponse(to.Warning);
                var rom = to?.RomPath;
                // A session left unfinished is finished first: taking its memory back later would undo this restore.
                AresConsoleSession.Recover();
                if (!where.Known) return new AddSaveResponse(where.Reason);
                var targetDir = where.Path;

                var name = NameNow(original, save is GameSaveState, rom, exe, system);
                // A state goes to the slot picked, when ares has it (SlotRestore); the backup's own when none was.
                int slot = 0, own = 0;
                if (save is GameSaveState picked)
                {
                    var m = StateFile.Match(name);
                    own = SlotRestore.SlotOf(save.SaveGroupId) ?? (m.Success ? int.Parse(m.Groups["slot"].Value, CultureInfo.InvariantCulture) : 0);
                    var (s, refused) = SlotRestore.Choose(picked.Slot, own == 0 ? (int?)null : own, Enumerable.Range(1, 9).ToList(), "ares");
                    if (refused != null) { Log.Info("restore of " + original + " refused: " + refused); return new AddSaveResponse(refused); }
                    slot = s;
                    name = (m.Success ? name.Substring(0, m.Index) : Path.GetFileNameWithoutExtension(name)) + ".bs" + slot.ToString(CultureInfo.InvariantCulture);
                }
                var target = Path.Combine(targetDir, name);
                // Over another card's save (another slot, or a card of LaunchBox's own - a manual import): LaunchBox
                // keeps no copy of it - asked first (SlotRestore).
                bool otherCard = !SaveRows.IsOurs(save.SaveGroupId, Emu) || (save is GameSaveState && slot != own) || SlotRestore.OverAnotherFile(target, save);
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
                Log.Info("restored " + original + " -> " + target);
                // Its companions, found beside the copy by its stem (the 64DD's clock with its disk).
                foreach (var companion in GetCompanionSaveFiles(source))
                {
                    var with = LbipStateThumbs.IsThumb(companion) ? LbipStateThumbs.PathOf(target) : Path.Combine(targetDir, Path.GetFileNameWithoutExtension(target) + Path.GetExtension(companion));
                    File.Copy(companion, with, overwrite: true);
                    Log.Info("restored with it " + Path.GetFileName(with));
                }
                // A state restored without its thumbnail: the old one would be a picture of another moment.
                if (save is GameSaveState && !GetCompanionSaveFiles(source).Any(LbipStateThumbs.IsThumb)) LbipStateThumbs.Remove(target);
                // The slot written, and the id that goes with it: file, tile and id agree.
                if (save is GameSaveState state) return new AddSaveResponse(StateRow(target, state.GameId, state.AdditionalApplicationId, slot, SlotRestore.WithSlot(state.SaveGroupId, slot)));
                return new AddSaveResponse(SaveRow(target, save.GameId, save.AdditionalApplicationId, save.SaveGroupId, save.SaveGroupName, save.DisplayChipText));
            }
            catch (Exception ex)
            {
                Log.Warn("AddSaveFile failed", ex);
                return new AddSaveResponse("Could not restore this ares save: " + ex.Message);
            }
        }

        LbIntegrations.Saves.ExpectedDir IOwnSaves.OwnExpectedDir(IEmulator emulator, GameSaveBase save) => Destination(emulator, save, out _, out _);

        /// <summary>The folder a restore of <paramref name="save"/> writes to on <paramref name="emulator"/>'s install
        /// (ExpectedDir): today's rom of its game or version (SaveRows.TargetFor), its system, ares's Paths/Saves and the
        /// system's sub-folder, else beside the played file. Read only.</summary>
        private static LbIntegrations.Saves.ExpectedDir Destination(IEmulator emulator, GameSaveBase save, out RestoreTarget to, out AresSystem system)
        {
            to = null; system = null;
            var exe = emulator == null ? null : ResolveFullPath(Safe(() => emulator.ApplicationPath));
            if (exe == null || save == null) return LbIntegrations.Saves.ExpectedDir.Unknown("Could not find the ares this game runs on.");
            var dm = PluginHelper.DataManager;
            to = SaveRows.TargetFor(save, dm, Full);
            if (to.Refused) return LbIntegrations.Saves.ExpectedDir.Unknown(to.Warning);
            var game = dm?.GetGameById(save.GameId);
            system = SystemOf(emulator, Safe(() => game?.Id), Safe(() => game?.Platform), to.RomPath);
            var folders = FoldersFor(exe, to.RomPath, system);
            return LbIntegrations.Saves.ExpectedDir.FolderOr(save is GameSaveState ? folders.StateTarget : folders.SaveTarget,
                                                             "Could not work out where ares reads this save from.");
        }

        /// <summary>The file name a restored save takes for this rom TODAY - see <see cref="NameFor"/>.</summary>
        private static string NameNow(string original, bool state, string rom, string exe, AresSystem system)
        {
            if (string.IsNullOrWhiteSpace(rom) || system == null) return original;
            var identity = AresConsoleSession.IdentityOf(rom, exe);
            if (identity.Length == 0) return original;
            var names = new[] { identity }.Concat(system.Disc ? AresConsoleSession.DiscNames(rom, system, exe) : new List<string>()).ToList();
            var exts = system.SaveExtensions
                .Concat(system.ConsoleFiles.Select(c => c.Ext))
                .Concat(Enumerable.Range(1, 9).Select(n => ".bs" + n.ToString(CultureInfo.InvariantCulture)));
            return NameFor(original, names, exts, keepUnknown: state && system.Disc);
        }

        /// <summary>The rule, apart from ares's files so the probe can hold it to cases. The backup's extension is
        /// one ares writes (<paramref name="exts"/>, the longest that fits - ".d1a" and the like are not
        /// Path.GetExtension's); what is left of the name is compared WHOLE with the game's names today (Codex
        /// review, 08/10: a "starts with" test kept "Zelda.rev1.srm" after the rom became "Zelda.sfc", and ares never
        /// read it). Named after one of them: kept. Otherwise it takes <paramref name="names"/>[0], the game's name -
        /// unless <paramref name="keepUnknown"/> (a disc game's state: which disc it was is not known any more).</summary>
        internal static string NameFor(string original, IReadOnlyList<string> names, IEnumerable<string> exts, bool keepUnknown)
        {
            if (string.IsNullOrEmpty(original) || names == null || names.Count == 0) return original;
            var ext = exts?.Where(e => !string.IsNullOrEmpty(e) && original.Length > e.Length && original.EndsWith(e, StringComparison.OrdinalIgnoreCase))
                           .OrderByDescending(e => e.Length).FirstOrDefault()
                      ?? Path.GetExtension(original);
            var stem = original.Substring(0, original.Length - ext.Length);
            if (names.Any(n => string.Equals(stem, n, StringComparison.OrdinalIgnoreCase))) return original;
            if (keepUnknown) return original;
            return names[0] + original.Substring(original.Length - ext.Length);
        }
        /// <summary>A path as LaunchBox stores it, made full; null for nothing.</summary>
        private static string Full(string path) => string.IsNullOrWhiteSpace(path) ? null : ResolveFullPath(path);

        /// <summary>The ares a game runs on: its own emulator when that is one, else the first in the library.</summary>
        private static IEmulator EmulatorOf(IGame game)
        {
            try
            {
                var dm = PluginHelper.DataManager;
                var id = Safe(() => game?.EmulatorId);
                var own = string.IsNullOrEmpty(id) ? null : dm?.GetEmulatorById(id);
                if (own != null && AresLaunch.IsAresExe(Safe(() => own.ApplicationPath))) return own;
                // Restored through another plugin's desk: the install the save came from (SaveForward).
                return LbIntegrations.Saves.SaveForward.ViaEmulator(AresLaunch.IsAresExe)
                       ?? dm?.GetAllEmulators()?.FirstOrDefault(e => AresLaunch.IsAresExe(Safe(() => e.ApplicationPath)));
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
                return new PluginResponse(false, "Could not delete this ares save: " + ex.Message);
            }
        }

        private static bool IsOurs(GameSaveBase save) => SaveRows.IsOurs(save?.SaveGroupId, Emu);

        private static string Safe(Func<string> read)
        {
            try { return read(); } catch { return null; }
        }
    }
}
