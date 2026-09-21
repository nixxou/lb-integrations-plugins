// Save management for PPSSPP.
//
// A PSP save is presented as a CONTAINER, in the exact sense PCSX2's integration uses the word:
// FileLocation points at something that holds several saves, and the save has to be extracted from
// it. Here the container is PSP\SAVEDATA, shared by every game on the memstick, and the save is the
// set of folders whose names start with one disc id (see PspSaveUnit.cs).
//
// Two consequences worth stating, because both were measured rather than assumed:
//
//  * FileLocation points at the unit's PRIMARY FOLDER, not at SAVEDATA. LiteBox derives
//    ActiveIsDirectory from Directory.Exists, and its NeedsBackup has no container arm, so a
//    FileLocation on SAVEDATA would make it hash every game on the memstick at every page render -
//    the same defect Argosy has in SyncCoordinator.buildInventory. The cost of pointing at the
//    primary instead is that the freshness dot misses a change confined to a sibling folder. That is
//    cosmetic: the dot is NeedsBackup's only caller, and the real copy decision goes through
//    Backup(force:false), which extracts the WHOLE unit through TryBackupSave and compares that.
//
//  * The save is NOT a multi-file "set". LiteBox's set machinery (TailsOf, UniqueStem, Renamed)
//    assumes members sharing a stem and differing by a tail; PARAM.SFO, ICON0.PNG and DATA.BIN share
//    no stem. Hence GetCompanionSaveFiles returns nothing and the container path does the work.
//
// Save STATES are not handled here. They do not travel (the server answers 501) and LiteBox drops
// companions for every state, so a .ppst's sibling .jpg thumbnail could not follow it anyway.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Unbroken.LaunchBox.Plugins;
using Unbroken.LaunchBox.Plugins.Data;

namespace LbIntegrations.Ppsspp
{
    public partial class PpssppPlugin
    {
        private const string GroupPrefix = "ppsspp:";
        private const string ChipText = "Memory Stick";

        public override bool SupportsSaveManagement() => true;

        // ── listing ──────────────────────────────────────────────────────────

        public override GetSavesResponse GetSaves(GetSavesArgs args)
        {
            try
            {
                if (args?.Emulator == null) return new GetSavesResponse("No emulator was supplied.");

                string appPath = Safe(() => args.Emulator.ApplicationPath);
                if (!PpssppPaths.IsPpssppExecutable(appPath))
                    return new GetSavesResponse("This emulator is not PPSSPP.");

                var layout = PpssppPaths.Resolve(appPath);
                if (!Directory.Exists(layout.SaveDataDir))
                {
                    Log.Info("no SAVEDATA directory at " + layout.SaveDataDir + " (" + layout.Reason + ")");
                    return new GetSavesResponse(new List<GameSaveBase>());
                }

                var found = new List<GameSaveBase>();
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                // Additional applications first, then games, each resolved to its own ROM: a version
                // has its own file and therefore its own disc id.
                foreach (var app in args.AdditionalApplications ?? (IReadOnlyCollection<IAdditionalApplication>)Array.Empty<IAdditionalApplication>())
                {
                    var game = SafeGame(Safe(() => app.GameId));
                    var save = SaveFor(layout, Safe(() => app.ApplicationPath), game, app, seen);
                    if (save != null) found.Add(save);
                }
                foreach (var game in args.Games ?? (IReadOnlyCollection<IGame>)Array.Empty<IGame>())
                {
                    var save = SaveFor(layout, Safe(() => game.ApplicationPath), game, null, seen);
                    if (save != null) found.Add(save);
                }

                Log.Info("found " + found.Count + " save(s) under " + layout.SaveDataDir);
                return new GetSavesResponse(found);
            }
            catch (Exception ex)
            {
                Log.Warn("GetSaves failed", ex);
                return new GetSavesResponse("Could not read PPSSPP saves: " + ex.Message);
            }
        }

        private GameSaveGame SaveFor(PpssppLayout layout, string romPath, IGame game,
                                     IAdditionalApplication app, HashSet<string> seen)
        {
            if (game == null) return null;

            var unit = UnitFor(layout.SaveDataDir, romPath, game);
            if (unit == null) return null;

            string gameId = Safe(() => game.Id) ?? "";
            string appId = app != null ? Safe(() => app.Id) : null;
            // One group per disc id per context: a game and one of its versions may legitimately
            // resolve to the same folders, and listing them twice would fight over one rommslot.
            if (!seen.Add(unit.DiscId + "|" + (appId ?? gameId))) return null;

            return new GameSaveGame
            {
                GameId = gameId,
                AdditionalApplicationId = appId,
                // The unit's primary folder - never the SAVEDATA parent. See the header note.
                FileLocation = unit.PrimaryPath,
                OriginalFileName = Path.GetFileName(unit.PrimaryPath),
                SaveGroupId = GroupPrefix + unit.DiscId,
                SaveGroupName = unit.Title,
                DisplayChipText = ChipText,
                ReportedFileSizeBytes = unit.SizeBytes > 0 ? unit.SizeBytes : (long?)null,
                ReportedLastModifiedUtc = unit.LastWriteUtc == default ? (DateTime?)null : unit.LastWriteUtc,
            };
        }

        /// <summary>The unit for a game: by disc id read from the ROM, else by an exact title match
        /// against PARAM.SFO. The title fallback is deliberately strict - Freegosy's word-overlap
        /// scoring conflates "Grand", "Battle" and "War" across unrelated games.</summary>
        private static PspSaveUnit UnitFor(string saveDataDir, string romPath, IGame game)
        {
            var discId = PspDiscId.Of(romPath);
            if (discId != null) return PspSaveUnits.ForDiscId(saveDataDir, discId);

            string title = Safe(() => game.Title);
            if (string.IsNullOrWhiteSpace(title)) return null;

            var normalized = NormalizeTitle(title);
            if (normalized.Length == 0) return null;

            // Match on the GAME's title, never on the save's. PARAM.SFO's TITLE names the game;
            // SAVEDATA_TITLE names the save inside it ("Chapter 3 - Midgar"), so comparing a library
            // entry against that would almost never hit.
            var match = PspSaveUnits.Enumerate(saveDataDir)
                .FirstOrDefault(u => NormalizeTitle(u.GameTitle) == normalized);
            if (match != null)
                Log.Info("no disc id for \"" + title + "\"; matched " + match.DiscId + " by title");
            return match;
        }

        private static string NormalizeTitle(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            var chars = s.Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray();
            return new string(chars);
        }

        // ── the container contract ───────────────────────────────────────────

        public override bool IsSaveContainer(GameSaveBase save) => IsOurs(save);

        /// <summary>Match persisted rows by group id rather than by path: a memstick that moves, or a
        /// game whose primary folder changes name, must not orphan its backup history.</summary>
        public override bool UseSaveGroupIdForPersistedMatch(GameSaveBase save) => IsOurs(save);

        /// <summary>Defensive: LiteBox only calls this on what GetSaves returned, but the SDK contract
        /// says a file inside a save must not become a save of its own.</summary>
        public override bool IsSecondarySaveFile(string filePath)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(filePath)) return false;
                var parent = Path.GetFileName(Path.GetDirectoryName(Path.GetDirectoryName(filePath)) ?? "");
                return string.Equals(parent, "SAVEDATA", StringComparison.OrdinalIgnoreCase);
            }
            catch { return false; }
        }

        /// <summary>Nothing. A PSP save is a set of DIRECTORIES, and LiteBox's companion mechanism is
        /// for sibling FILES - CompanionsOf filters on File.Exists, so a folder would be dropped.</summary>
        public override IReadOnlyList<string> GetCompanionSaveFiles(string primaryFilePath)
            => Array.Empty<string>();

        /// <summary>Is this save on the memstick the emulator is actually configured with?</summary>
        public override bool IsSaveActive(GameSaveBase save, string emulatorApplicationPath)
        {
            try
            {
                if (!IsOurs(save) || string.IsNullOrWhiteSpace(emulatorApplicationPath)) return true;
                var layout = PpssppPaths.Resolve(emulatorApplicationPath);
                var loc = save.FileLocation;
                if (string.IsNullOrWhiteSpace(loc) || string.IsNullOrWhiteSpace(layout.SaveDataDir)) return true;
                return Path.GetFullPath(loc).StartsWith(Path.GetFullPath(layout.SaveDataDir),
                                                        StringComparison.OrdinalIgnoreCase);
            }
            catch { return true; }
        }

        // ── backup ───────────────────────────────────────────────────────────

        /// <summary>Extract the save into <paramref name="destinationFolder"/>.
        ///
        /// THE storage decision: each folder of the unit goes in under its own name, with no
        /// intermediate level. LiteBox copies this folder verbatim into the vault and later hashes it
        /// with SaveHash.OfDirectory and serves it with WriteFolderZip, both of which name entries
        /// relative to the folder root - so the entries come out as "ULUS10064DATA00/PARAM.SFO",
        /// which is what Argosy zips and what RomM's hash is computed over. Measured against sigil's
        /// golden vector; see the probe's --save-unit mode.</summary>
        public override bool TryBackupSave(GameSaveBase save, string emulatorApplicationPath,
                                           string destinationFolder, out string error)
        {
            error = null;
            try
            {
                var unit = LiveUnit(save, emulatorApplicationPath);
                if (unit == null) { error = "This PPSSPP save is no longer on the memstick."; return false; }

                if (!PspSaveUnits.CopyInto(unit, destinationFolder, out error)) return false;
                Log.Info("extracted " + unit.DiscId + ": " + unit.Folders.Count + " folder(s) -> " + destinationFolder);
                return true;
            }
            catch (Exception ex)
            {
                Log.Warn("TryBackupSave failed", ex);
                error = ex.Message;
                return false;
            }
        }

        // ── restore ──────────────────────────────────────────────────────────

        /// <summary>Put a vault copy back on the memstick.
        ///
        /// AddSaveArgs carries no emulator, so the memstick is re-resolved from the game's own
        /// emulator through the public data manager; LiteBox installs a scope that makes that answer
        /// the right emulator id for the group being restored.
        ///
        /// Existing folders of the same disc id are removed first. That mirrors Argosy's
        /// extractDownload, and it is the only way a restore cannot leave a stale sibling behind
        /// pretending to belong to the save.</summary>
        public override AddSaveResponse AddSaveFile(AddSaveArgs args)
        {
            try
            {
                var save = args?.SaveToAdd;
                if (save == null) return new AddSaveResponse("No save was supplied.");

                string source = save.FileLocation;
                if (string.IsNullOrWhiteSpace(source) || !Directory.Exists(source))
                    return new AddSaveResponse("This PPSSPP backup is not a folder: " + (source ?? "(none)"));

                var incoming = Directory.EnumerateDirectories(source).ToList();
                if (incoming.Count == 0)
                    return new AddSaveResponse("This PPSSPP backup holds no save folders.");

                string discId = DiscIdOf(save) ?? DiscIdFromFolders(incoming);
                if (discId == null)
                    return new AddSaveResponse("Could not work out which game this PPSSPP backup belongs to.");

                string saveDataDir = SaveDataDirFor(save);
                if (saveDataDir == null)
                    return new AddSaveResponse("Could not locate PPSSPP's memory stick for this game.");

                var existing = PspSaveUnits.MatchingFolders(saveDataDir, discId);
                if (existing.Count > 0)
                {
                    bool overwrite = true;
                    try { overwrite = args.ShouldOverwriteFunc?.Invoke() ?? true; } catch { }
                    if (!overwrite) return new AddSaveResponse("Restore cancelled.");
                    foreach (var dir in existing)
                        try { Directory.Delete(dir, recursive: true); }
                        catch (Exception ex) { Log.Warn("could not remove " + dir, ex); }
                }

                Directory.CreateDirectory(saveDataDir);
                foreach (var folder in incoming)
                {
                    var name = Path.GetFileName(folder);
                    if (string.IsNullOrEmpty(name)) continue;
                    PspSaveUnits.CopyDirectory(folder, Path.Combine(saveDataDir, name));
                }

                var restored = PspSaveUnits.ForDiscId(saveDataDir, discId);
                Log.Info("restored " + discId + ": " + incoming.Count + " folder(s) -> " + saveDataDir);

                return new AddSaveResponse(new GameSaveGame
                {
                    GameId = save.GameId,
                    AdditionalApplicationId = save.AdditionalApplicationId,
                    FileLocation = restored?.PrimaryPath ?? Path.Combine(saveDataDir, Path.GetFileName(incoming[0])),
                    OriginalFileName = Path.GetFileName(incoming[0]),
                    SaveGroupId = GroupPrefix + discId,
                    SaveGroupName = restored?.Title ?? save.SaveGroupName,
                    DisplayChipText = ChipText,
                });
            }
            catch (Exception ex)
            {
                Log.Warn("AddSaveFile failed", ex);
                return new AddSaveResponse("Could not restore this PPSSPP save: " + ex.Message);
            }
        }

        // ── delete ───────────────────────────────────────────────────────────

        /// <summary>Must be overridden: the SDK default deletes the one path in FileLocation, which
        /// here would remove the primary folder and leave its siblings orphaned on the memstick.</summary>
        public override PluginResponse RemoveSave(GameSaveBase save)
        {
            try
            {
                if (!IsOurs(save)) return base.RemoveSave(save);

                string loc = save?.FileLocation;
                if (string.IsNullOrWhiteSpace(loc)) return new PluginResponse(false, "This save has no location.");

                string saveDataDir = Path.GetDirectoryName(loc);
                string discId = DiscIdOf(save) ?? DiscIdFromFolders(new List<string> { loc });
                if (saveDataDir == null || discId == null)
                    return new PluginResponse(false, "Could not work out which folders this save owns.");

                var folders = PspSaveUnits.MatchingFolders(saveDataDir, discId);
                if (folders.Count == 0) return new PluginResponse(true);   // already gone

                foreach (var dir in folders) Directory.Delete(dir, recursive: true);
                Log.Info("deleted " + discId + ": " + folders.Count + " folder(s)");
                return new PluginResponse(true);
            }
            catch (Exception ex)
            {
                Log.Warn("RemoveSave failed", ex);
                return new PluginResponse(false, "Could not delete this PPSSPP save: " + ex.Message);
            }
        }

        // ── plumbing ─────────────────────────────────────────────────────────

        private static bool IsOurs(GameSaveBase save)
            => save?.SaveGroupId != null
               && save.SaveGroupId.StartsWith(GroupPrefix, StringComparison.OrdinalIgnoreCase);

        private static string DiscIdOf(GameSaveBase save)
        {
            var id = save?.SaveGroupId;
            if (id == null || !id.StartsWith(GroupPrefix, StringComparison.OrdinalIgnoreCase)) return null;
            var rest = id.Substring(GroupPrefix.Length).Trim();
            return rest.Length >= PspSaveUnits.DiscIdLength
                ? rest.Substring(0, PspSaveUnits.DiscIdLength).ToUpperInvariant()
                : null;
        }

        /// <summary>The disc id shared by a set of folder paths - their common 9-character prefix.</summary>
        private static string DiscIdFromFolders(List<string> folders)
        {
            foreach (var f in folders)
            {
                var name = Path.GetFileName(f.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                if (!string.IsNullOrEmpty(name) && name.Length >= PspSaveUnits.DiscIdLength)
                    return name.Substring(0, PspSaveUnits.DiscIdLength).ToUpperInvariant();
            }
            return null;
        }

        /// <summary>The live unit behind a save row, re-resolved from disk.</summary>
        private static PspSaveUnit LiveUnit(GameSaveBase save, string emulatorApplicationPath)
        {
            string discId = DiscIdOf(save);
            string saveDataDir = !string.IsNullOrWhiteSpace(emulatorApplicationPath)
                ? PpssppPaths.Resolve(emulatorApplicationPath).SaveDataDir
                : Path.GetDirectoryName(save?.FileLocation ?? "");

            if (discId == null && !string.IsNullOrWhiteSpace(save?.FileLocation))
                discId = DiscIdFromFolders(new List<string> { save.FileLocation });
            if (discId == null || string.IsNullOrWhiteSpace(saveDataDir)) return null;

            return PspSaveUnits.ForDiscId(saveDataDir, discId);
        }

        /// <summary>PSP\SAVEDATA for the emulator this save's game is assigned to. AddSaveArgs carries
        /// no emulator, so it is re-resolved through the public data manager.</summary>
        private static string SaveDataDirFor(GameSaveBase save)
        {
            try
            {
                var dm = PluginHelper.DataManager;
                var game = dm?.GetGameById(save.GameId);
                var emuId = Safe(() => game?.EmulatorId);
                var emu = string.IsNullOrEmpty(emuId) ? null : dm.GetEmulatorById(emuId);
                var appPath = Safe(() => emu?.ApplicationPath);
                if (PpssppPaths.IsPpssppExecutable(appPath))
                    return PpssppPaths.Resolve(appPath).SaveDataDir;

                // No usable emulator: fall back to any PPSSPP the library knows.
                var any = dm?.GetAllEmulators()?
                    .FirstOrDefault(e => PpssppPaths.IsPpssppExecutable(Safe(() => e.ApplicationPath)));
                var anyPath = Safe(() => any?.ApplicationPath);
                return anyPath == null ? null : PpssppPaths.Resolve(anyPath).SaveDataDir;
            }
            catch (Exception ex) { Log.Warn("could not resolve the memory stick", ex); return null; }
        }

        private static T Safe<T>(Func<T> f)
        {
            try { return f(); } catch { return default; }
        }

        /// <summary>The game an additional application belongs to, through the public data manager.</summary>
        private static IGame SafeGame(string gameId)
        {
            if (string.IsNullOrEmpty(gameId)) return null;
            try { return PluginHelper.DataManager?.GetGameById(gameId); }
            catch (Exception ex) { Log.Warn("could not resolve game " + gameId, ex); return null; }
        }
    }
}
