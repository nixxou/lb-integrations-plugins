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
// Save STATES are handled too, and they are the other shape entirely: one FILE per slot, not a set
// of directories. They therefore take none of the container path - IsSaveContainer says false, the
// host copies the file itself, and TryBackupSave is never asked for one. Their naming, and the undo
// pair that a careless glob would mistake for a slot, are in PpssppState.cs.
//
// An earlier version of this file refused to handle states, on the grounds that they do not
// synchronise with RomM (the server answers 501). That was a bad reason: it is a statement about
// what travels to a server, and it says nothing about listing, backing up or restoring a state on
// this machine - which is what the Game Saves page is for.
//
// One real limit, measured rather than assumed: LiteBox drops companions for every state
// (SaveManager.cs, `save is GameSaveState ? new List<string>()`). GetCompanionSaveFiles still
// returns the sibling .jpg, because the SDK contract asks for it and LaunchBox honours it - but
// under LiteBox a restored state comes back without its screenshot. PPSSPP writes a fresh one the
// next time that slot is saved, so the cost is cosmetic.

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

        /// <summary>Group id prefix for a state. Deliberately NOT built on GroupPrefix: every
        /// container predicate keys on that prefix, and a state must fall on the other side of all
        /// of them. The discriminator is the SDK's own GameSaveState type; the prefix only keeps our
        /// rows apart from another plugin's.</summary>
        private const string StatePrefix = "ppsspp-state:";
        private const string StateChipText = "Save State";
        private const string StateGroupName = "My Save State";

        public override bool SupportsSaveManagement() => true;

        /// <summary>The slots a state can live in. PPSSPP's `SaveStateSlotCount` defaults to 5 and the
        /// file name carries the slot 0-based, while PPSSPP's own UI labels them from 1 - hence the
        /// key/label split rather than two conflicting numbers.</summary>
        public override IReadOnlyDictionary<int, string> GetPotentialSaveSlots()
        {
            var slots = new Dictionary<int, string>();
            for (int i = 0; i < PpssppStates.DefaultSlotCount; i++)
                slots[i] = (i + 1).ToString(System.Globalization.CultureInfo.InvariantCulture);
            return slots;
        }

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
                // Each folder gates only its own kind: a memstick can perfectly well hold states and
                // no save yet, which is exactly what a freshly installed game looks like.
                bool haveSaveData = Directory.Exists(layout.SaveDataDir);
                bool haveStates = Directory.Exists(layout.SaveStateDir);
                if (!haveSaveData && !haveStates)
                {
                    Log.Info("neither SAVEDATA nor PPSSPP_STATE under " + layout.PspDir + " (" + layout.Reason + ")");
                    return new GetSavesResponse(new List<GameSaveBase>());
                }

                var found = new List<GameSaveBase>();
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var seenStates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                int stateCount = 0;

                // Additional applications first, then games, each resolved to its own ROM: a version
                // has its own file and therefore its own disc id.
                foreach (var app in args.AdditionalApplications ?? (IReadOnlyCollection<IAdditionalApplication>)Array.Empty<IAdditionalApplication>())
                {
                    var game = SafeGame(Safe(() => app.GameId));
                    var romPath = Safe(() => app.ApplicationPath);
                    if (haveSaveData)
                    {
                        var save = SaveFor(layout, romPath, game, app, seen);
                        if (save != null) found.Add(save);
                    }
                    if (haveStates)
                        stateCount += AddStates(found, layout, romPath, game, app, seenStates);
                }
                foreach (var game in args.Games ?? (IReadOnlyCollection<IGame>)Array.Empty<IGame>())
                {
                    var romPath = Safe(() => game.ApplicationPath);
                    if (haveSaveData)
                    {
                        var save = SaveFor(layout, romPath, game, null, seen);
                        if (save != null) found.Add(save);
                    }
                    if (haveStates)
                        stateCount += AddStates(found, layout, romPath, game, null, seenStates);
                }

                Log.Info("found " + (found.Count - stateCount) + " save(s) and " + stateCount
                         + " state(s) under " + layout.PspDir);
                return new GetSavesResponse(found);
            }
            catch (Exception ex)
            {
                Log.Warn("GetSaves failed", ex);
                return new GetSavesResponse("Could not read PPSSPP saves: " + ex.Message);
            }
        }

        /// <summary>Append one row per occupied state slot of this game. Returns how many were added.
        ///
        /// The disc id comes from the ROM and from nowhere else. SaveFor's title fallback is
        /// deliberately NOT reused: it exists to recognise a SAVEDATA folder written by a game whose
        /// disc we could not read, whereas a state file name already carries its own disc id - so a
        /// title guess here would only ever attach one game's states to another.</summary>
        private int AddStates(List<GameSaveBase> into, PpssppLayout layout, string romPath,
                              IGame game, IAdditionalApplication app, HashSet<string> seen)
        {
            if (game == null) return 0;

            string discId = PspDiscId.Of(romPath);
            if (discId == null) return 0;

            string gameId = Safe(() => game.Id) ?? "";
            string appId = app != null ? Safe(() => app.Id) : null;
            string context = appId ?? gameId;
            int added = 0;

            foreach (var st in PpssppStates.ForDiscId(layout.SaveStateDir, discId))
            {
                // Same rule as a save: one row per slot per context, so a game and one of its
                // versions pointing at the same disc do not list the slot twice.
                if (!seen.Add(st.DiscId + "|" + st.Slot + "|" + context)) continue;

                into.Add(new GameSaveState
                {
                    GameId = gameId,
                    AdditionalApplicationId = appId,
                    FileLocation = st.Path,
                    // Carries the disc VERSION, which the group id deliberately does not. A restore
                    // reads it back from here; see AddSaveFile.
                    OriginalFileName = st.FileName,
                    Slot = st.Slot,
                    // Disc id and slot only: a game patched from 1.00 to 1.01 keeps its history
                    // instead of orphaning it, the way Dolphin's "<game>-<disc>-State-<slot>" does.
                    SaveGroupId = StatePrefix + st.DiscId + ":" + st.Slot,
                    SaveGroupName = StateGroupName,
                    DisplayChipText = StateChipText,
                    ReportedFileSizeBytes = st.SizeBytes > 0 ? st.SizeBytes : (long?)null,
                    ReportedLastModifiedUtc = st.LastWriteUtc == default ? (DateTime?)null : st.LastWriteUtc,
                });
                added++;
            }
            return added;
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
                // A DIRECTORY, and the host must be told so. Measured on LaunchBox 14: it asked for
                // this save on every page open, got it, and still drew the card from the vault copies
                // alone with the violet "In Vault" pill - because a save whose FileLocation it treats
                // as a file simply is not there, and a save that is not there cannot be the active
                // one. LiteBox never showed the fault: it computes ActiveIsDirectory as
                // `save.IsDirectory || Directory.Exists(path)` and so covered for the missing flag.
                IsDirectory = true,
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

        /// <summary>A save is a container; a STATE is a single file and must never be one, or the host
        /// would ask TryBackupSave to extract a folder that does not exist.</summary>
        public override bool IsSaveContainer(GameSaveBase save) => IsOurs(save);

        /// <summary>Match persisted rows by group id rather than by path: a memstick that moves, or a
        /// game whose primary folder changes name, must not orphan its backup history. True for a
        /// state too - its group id is disc id plus slot, which survives the game being patched.</summary>
        public override bool UseSaveGroupIdForPersistedMatch(GameSaveBase save)
            => IsOurs(save) || IsOurState(save);

        /// <summary>Defensive: LiteBox only calls this on what GetSaves returned, but the SDK contract
        /// says a file inside a save must not become a save of its own. Two kinds here: anything under
        /// a SAVEDATA unit, and - inside PPSSPP_STATE - a slot's screenshot or its undo buffer.</summary>
        public override bool IsSecondarySaveFile(string filePath)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(filePath)) return false;

                var name = Path.GetFileName(filePath);
                var dir = Path.GetFileName(Path.GetDirectoryName(filePath) ?? "");
                if (string.Equals(dir, PpssppPaths.SaveStateDirName, StringComparison.OrdinalIgnoreCase))
                    return PpssppStates.IsUndo(name)
                           || name.EndsWith(PpssppStates.ThumbnailExtension, StringComparison.OrdinalIgnoreCase);

                var parent = Path.GetFileName(Path.GetDirectoryName(Path.GetDirectoryName(filePath)) ?? "");
                return string.Equals(parent, "SAVEDATA", StringComparison.OrdinalIgnoreCase);
            }
            catch { return false; }
        }

        /// <summary>For a SAVEDATA unit, nothing: it is a set of DIRECTORIES, and the companion
        /// mechanism is for sibling FILES - CompanionsOf filters on File.Exists, so a folder would be
        /// dropped. For a STATE, the sibling screenshot, which is a file and does qualify.
        ///
        /// LiteBox ignores this for states (it hands back an empty companion list for every one), so
        /// the screenshot only really follows under LaunchBox. Returning it anyway is the contract,
        /// and costs nothing where it is ignored.</summary>
        public override IReadOnlyList<string> GetCompanionSaveFiles(string primaryFilePath)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(primaryFilePath)) return Array.Empty<string>();
                if (!primaryFilePath.EndsWith(PpssppStates.Extension, StringComparison.OrdinalIgnoreCase))
                    return Array.Empty<string>();
                var jpg = PpssppStates.ThumbnailFor(primaryFilePath);
                return jpg == null ? Array.Empty<string>() : new[] { jpg };
            }
            catch { return Array.Empty<string>(); }
        }

        /// <summary>Is this save on the memstick the emulator is actually configured with? A state is
        /// judged against PPSSPP_STATE, a save against SAVEDATA - same question, different folder.</summary>
        public override bool IsSaveActive(GameSaveBase save, string emulatorApplicationPath)
        {
            try
            {
                bool ours = IsOurs(save), state = IsOurState(save);
                if ((!ours && !state) || string.IsNullOrWhiteSpace(emulatorApplicationPath)) return true;

                var layout = PpssppPaths.Resolve(emulatorApplicationPath);
                var root = state ? layout.SaveStateDir : layout.SaveDataDir;
                var loc = save.FileLocation;
                if (string.IsNullOrWhiteSpace(loc) || string.IsNullOrWhiteSpace(root)) return true;
                if (!Path.GetFullPath(loc).StartsWith(Path.GetFullPath(root),
                                                      StringComparison.OrdinalIgnoreCase)) return false;

                // AND IT MUST STILL BE THERE. Being under the emulator's folder is not enough: the
                // path can name a place that no longer exists, and "active" means the save the
                // emulator would actually read.
                //
                // Measured on Xenia, where reinstalling the emulator moved every save under a new
                // profile folder: the old records still passed the prefix test, the host picked a dead
                // one as the group's active save, and the game showed its vault copy and nothing live.
                // A memory stick moved or a state deleted by hand reaches the same state here.
                return Exists(loc);
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
                // A state is not a container and the host copies its file itself, so this should
                // never be reached for one. The guard is not decoration: without it, LiveUnit would
                // take "ULUS10516_1.01_0.ppst", read its first nine characters as a disc id, and
                // cheerfully back up the game's SAVEDATA instead of the state.
                if (save is GameSaveState)
                {
                    error = "A PPSSPP save state is a single file, not a container.";
                    return false;
                }

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
                if (save is GameSaveState state) return RestoreState(state, args);

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
                    IsDirectory = true,          // a PSP save is a folder - see the note above
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

        /// <summary>Put a state back in PPSSPP_STATE.
        ///
        /// The whole difficulty is the FILE NAME. PPSSPP loads a slot by looking for
        /// "&lt;disc id&gt;_&lt;disc version&gt;_&lt;slot&gt;.ppst" exactly, and the group id carries
        /// only disc id and slot - on purpose, so that patching a game does not orphan its history.
        /// The version is therefore looked for in three places, in order: the name the backup still
        /// carries, the name the row was listed under, and any state already in the folder for this
        /// game. If none of them answers, the restore is REFUSED. Writing a file under a guessed
        /// version would report success and leave the user with a slot the emulator never shows.</summary>
        private AddSaveResponse RestoreState(GameSaveState state, AddSaveArgs args)
        {
            string source = state.FileLocation;
            if (string.IsNullOrWhiteSpace(source) || !File.Exists(source))
                return new AddSaveResponse("This PPSSPP save state is not a file: " + (source ?? "(none)"));

            var (discId, slot) = StateKeyOf(state);
            if (discId == null)
                return new AddSaveResponse("Could not work out which game this save state belongs to.");
            if (slot == null) slot = state.Slot;
            if (slot == null)
                return new AddSaveResponse("A PPSSPP save state needs a slot.");

            string stateDir = SaveStateDirFor(state);
            if (stateDir == null)
                return new AddSaveResponse("Could not locate PPSSPP's memory stick for this game.");

            string version = VersionFrom(Path.GetFileName(source), discId)
                             ?? VersionFrom(state.OriginalFileName, discId)
                             ?? PpssppStates.VersionFromFolder(stateDir, discId);
            if (version == null)
                return new AddSaveResponse(
                    "This backup does not say which disc version it belongs to, and PPSSPP_STATE holds "
                    + "nothing for " + discId + " to learn it from. Save a state in PPSSPP once, then "
                    + "restore again.");

            string target = Path.Combine(stateDir, PpssppStates.NameFor(discId, version, slot.Value));
            if (File.Exists(target))
            {
                bool overwrite = true;
                try { overwrite = args?.ShouldOverwriteFunc?.Invoke() ?? true; } catch { }
                if (!overwrite) return new AddSaveResponse("Restore cancelled.");
            }

            Directory.CreateDirectory(stateDir);
            File.Copy(source, target, overwrite: true);

            // The screenshot when the backup kept one. Absent is normal - LiteBox drops companions for
            // states - and PPSSPP writes a new one the next time this slot is saved.
            var jpg = PpssppStates.ThumbnailFor(source);
            if (jpg != null)
            {
                try { File.Copy(jpg, Path.ChangeExtension(target, PpssppStates.ThumbnailExtension), overwrite: true); }
                catch (Exception ex) { Log.Warn("could not restore the state screenshot", ex); }
            }
            Log.Info("restored state " + discId + " slot " + slot.Value + " -> " + target);

            return new AddSaveResponse(new GameSaveState
            {
                GameId = state.GameId,
                AdditionalApplicationId = state.AdditionalApplicationId,
                FileLocation = target,
                OriginalFileName = Path.GetFileName(target),
                Slot = slot,
                SaveGroupId = StatePrefix + discId + ":" + slot.Value,
                SaveGroupName = string.IsNullOrWhiteSpace(state.SaveGroupName) ? StateGroupName : state.SaveGroupName,
                DisplayChipText = StateChipText,
                Title = state.Title,
            });
        }

        // ── delete ───────────────────────────────────────────────────────────

        /// <summary>Must be overridden: the SDK default deletes the one path in FileLocation, which
        /// here would remove the primary folder and leave its siblings orphaned on the memstick.</summary>
        public override PluginResponse RemoveSave(GameSaveBase save)
        {
            try
            {
                if (save is GameSaveState) return RemoveState(save);
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

        /// <summary>Delete one state slot: the file, its screenshot, and the undo pair PPSSPP keeps
        /// for that same slot.
        ///
        /// The undo pair goes too, and that is a decision rather than an obvious truth. It is the
        /// state this slot held before it was last overwritten, so leaving it behind would let
        /// "undo last save state" resurrect something the user has just deleted, and would leave
        /// megabytes on the memstick under a name nothing lists any more.</summary>
        private PluginResponse RemoveState(GameSaveBase save)
        {
            string loc = save?.FileLocation;
            if (string.IsNullOrWhiteSpace(loc)) return new PluginResponse(false, "This save state has no location.");

            string stem = loc.EndsWith(PpssppStates.Extension, StringComparison.OrdinalIgnoreCase)
                ? loc.Substring(0, loc.Length - PpssppStates.Extension.Length)
                : loc;

            var targets = new[]
            {
                stem + PpssppStates.Extension,
                stem + PpssppStates.ThumbnailExtension,
                stem + ".undo" + PpssppStates.Extension,
                stem + ".undo" + PpssppStates.ThumbnailExtension,
            };

            int removed = 0;
            foreach (var path in targets)
            {
                try { if (File.Exists(path)) { File.Delete(path); removed++; } }
                catch (Exception ex) { Log.Warn("could not delete " + path, ex); }
            }
            Log.Info("deleted state " + Path.GetFileName(stem) + ": " + removed + " file(s)");
            return new PluginResponse(true);
        }

        // ── plumbing ─────────────────────────────────────────────────────────

        /// <summary>A SAVEDATA save of ours. A state is excluded by TYPE, not by prefix: every
        /// container predicate hangs off this, and the SDK already gives us the distinction.</summary>
        /// <summary>A path that is there, file or folder - a PSP save is a directory, a state a file.</summary>
        private static bool Exists(string path)
        {
            try { return File.Exists(path) || Directory.Exists(path); } catch { return false; }
        }

        private static bool IsOurs(GameSaveBase save)
            => save is not GameSaveState
               && save?.SaveGroupId != null
               && save.SaveGroupId.StartsWith(GroupPrefix, StringComparison.OrdinalIgnoreCase);

        private static bool IsOurState(GameSaveBase save)
            => save is GameSaveState
               && save.SaveGroupId != null
               && save.SaveGroupId.StartsWith(StatePrefix, StringComparison.OrdinalIgnoreCase);

        /// <summary>Disc id and slot out of a state's group id, "ppsspp-state:&lt;disc&gt;:&lt;slot&gt;".
        /// Falls back to the file name when the row carries no group id of ours.</summary>
        private static (string DiscId, int? Slot) StateKeyOf(GameSaveState state)
        {
            var id = state?.SaveGroupId;
            if (id != null && id.StartsWith(StatePrefix, StringComparison.OrdinalIgnoreCase))
            {
                var rest = id.Substring(StatePrefix.Length);
                int colon = rest.LastIndexOf(':');
                if (colon > 0)
                {
                    var disc = rest.Substring(0, colon).Trim().ToUpperInvariant();
                    if (int.TryParse(rest.Substring(colon + 1).Trim(), out var slot) && disc.Length > 0)
                        return (disc, slot);
                }
            }

            foreach (var name in new[] { Path.GetFileName(state?.FileLocation ?? ""), state?.OriginalFileName })
                if (PpssppStates.TryParseName(name, out var d, out _, out var s)) return (d, s);

            return (null, null);
        }

        /// <summary>The disc version inside a state file name, when that name belongs to this game.</summary>
        private static string VersionFrom(string fileName, string discId)
            => PpssppStates.TryParseName(fileName, out var d, out var ver, out _)
               && string.Equals(d, discId, StringComparison.OrdinalIgnoreCase)
                ? ver : null;

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

        /// <summary>PSP\SAVEDATA for the emulator this save's game is assigned to.</summary>
        private static string SaveDataDirFor(GameSaveBase save)
            => MemstickFolderFor(save, l => l.SaveDataDir);

        /// <summary>PSP\PPSSPP_STATE for the emulator this state's game is assigned to.</summary>
        private static string SaveStateDirFor(GameSaveBase save)
            => MemstickFolderFor(save, l => l.SaveStateDir);

        /// <summary>One folder of the memstick behind a save row. AddSaveArgs carries no emulator, so
        /// it is re-resolved through the public data manager.</summary>
        private static string MemstickFolderFor(GameSaveBase save, Func<PpssppLayout, string> pick)
        {
            try
            {
                var dm = PluginHelper.DataManager;
                var game = dm?.GetGameById(save.GameId);
                var emuId = Safe(() => game?.EmulatorId);
                var emu = string.IsNullOrEmpty(emuId) ? null : dm.GetEmulatorById(emuId);
                var appPath = Safe(() => emu?.ApplicationPath);
                if (PpssppPaths.IsPpssppExecutable(appPath))
                    return pick(PpssppPaths.Resolve(appPath));

                // No usable emulator: fall back to any PPSSPP the library knows.
                var any = dm?.GetAllEmulators()?
                    .FirstOrDefault(e => PpssppPaths.IsPpssppExecutable(Safe(() => e.ApplicationPath)));
                var anyPath = Safe(() => any?.ApplicationPath);
                return anyPath == null ? null : pick(PpssppPaths.Resolve(anyPath));
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
