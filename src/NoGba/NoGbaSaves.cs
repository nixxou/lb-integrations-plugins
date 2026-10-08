// Save management for no$gba, which is the simplest of the five plugins here - and simple for a
// reason worth stating.
//
// ONE FILE PER GAME, IN ONE PLACE. BATTERY\<asset name>.SAV, beside the executable. There is no
// second location to reconcile: no$gba has no save-path setting, so unlike melonDS there is no
// "configured folder or beside the ROM" question, and unlike Xenia no per-title directory tree.
// A save is active if it is in this installation's BATTERY folder, and that is the whole rule.
//
// THE NAME COMES FROM THE ROM FILE, NOT FROM THE ROM. Measured: "mk.nds" produced "BATTERY\mk.SAV".
// no$gba reads no internal title. For a ROM that arrived in an archive the name is therefore the
// INNER entry's - which is what NoGbaRoms unpacks it under, precisely so these two agree.
//
// NO COMPANIONS, EVER. A cartridge save is one file. This matters beyond tidiness: LiteBox's save
// layout table, derived from sigil's save_layout.c, describes the DS as a single `{stem}.sav`
// member, and SaveHash.Of hashes a one-member set as that file's plain MD5. So a no$gba save hashes
// identically to the same bytes from RetroArch or melonDS, and travels through RomM and Argosy with
// no special case anywhere. Claiming a companion would break that for nothing.
//
// SNAPSHOTS ARE NOT LISTED, and that is a measurement rather than an omission. F8 (Write snapshot)
// creates the SNAP\ folder and left nothing in it across several sessions, including clean exits.
// Until a snapshot is observed on disk, declaring slots would mean describing a file format and a
// naming scheme that have not been seen. The folder is scanned and what turns up is logged, so the
// first person to produce one tells us what it is called.
//
// A SNAPSHOT WOULD STILL GET ITS PICTURE (Mehdi, 07/10: a PNG of the same name beside each state).
// SNAP\ is watched while no$gba runs, and a file written there gets <its name>.png, taken from the
// emulator's window (LbipStateThumbs.Watch). Nothing more is wired - no companion, no restore - since
// nothing is listed; once a snapshot has been seen and is listed, its PNG is its companion.

using System;
using System.Collections.Generic;
using System.IO;
using Unbroken.LaunchBox.Plugins;
using Unbroken.LaunchBox.Plugins.Data;
using LbIntegrations.Dsi;
using LbIntegrations.Lbip;
using LbIntegrations.Saves;

namespace LbIntegrations.NoGba
{
    public partial class NoGbaPlugin
    {
        /// <summary>The front desk (Shared.Saves SaveDesk): this plugin's emu, and where it logs.</summary>
        private string SaveEmu => Emu;
        private void SaveLog(string message) => Log.Info(message);

        /// <summary>This plugin's "emu" in the ids (Shared.Saves, docs\saves-lb14-routing.md §3):
        /// {"emu":"nogba","kind":"save","by":"name","id":"<asset name>"}, and for a DSiWare save - which lives
        /// INSIDE the NAND, not in BATTERY\, packed into a single .dsisave - "kind":"nand","by":"title".
        /// The same shape as melonDS's, under the emulator's own "emu": the two never share a row even
        /// when they share a console. A restore goes to the version the row is tied to, else the game
        /// (SaveRows.TargetFor), under that rom's asset name today.</summary>
        private const string Emu = "nogba";
        private const string DsiWareGroupName = "DSiWare save";
        private const string DsiWareChipText = "NAND";
        private const string SaveGroupName = "Cartridge save";
        private const string SaveChipText = "SAV";

        public override bool SupportsSaveManagement() => true;

        /// <summary>No numbered slots. no$gba's File menu offers one "Write snapshot" and one "Load
        /// snapshot", with no slot to choose - and nothing was ever observed on disk. An invented
        /// set of slots would be a promise the emulator does not keep.</summary>
        public override IReadOnlyDictionary<int, string> GetPotentialSaveSlots()
            => new Dictionary<int, string>();

        GetSavesResponse IOwnSaves.OwnGetSaves(GetSavesArgs args)
        {
            try
            {
                if (args?.Emulator == null) return new GetSavesResponse("No emulator was supplied.");

                string appPath = Safe(() => args.Emulator.ApplicationPath);
                if (!NoGbaPaths.IsNoGbaExecutable(appPath))
                    return new GetSavesResponse("This emulator is not no$gba.");

                var layout = NoGbaPaths.Resolve(ResolveFullPath(appPath));
                if (string.IsNullOrEmpty(layout.InstallDir))
                    return new GetSavesResponse(new List<GameSaveBase>());

                var found = new List<GameSaveBase>();
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                // Additional applications first, then games: a version has its own ROM file and
                // therefore its own save name.
                foreach (var app in args.AdditionalApplications
                         ?? (IReadOnlyCollection<IAdditionalApplication>)Array.Empty<IAdditionalApplication>())
                {
                    var game = SafeGame(Safe(() => app.GameId));
                    Collect(found, seen, layout, Safe(() => app.ApplicationPath), game, app);
                }
                foreach (var game in args.Games ?? (IReadOnlyCollection<IGame>)Array.Empty<IGame>())
                    Collect(found, seen, layout, Safe(() => game.ApplicationPath), game, null);

                LookForSnapshots(layout);
                Log.Verbose("found " + found.Count + " save(s) in " + layout.BatteryDir);
                return new GetSavesResponse(found);
            }
            catch (Exception ex)
            {
                Log.Warn("GetSaves failed", ex);
                return new GetSavesResponse("Could not read no$gba saves: " + ex.Message);
            }
        }

        private void Collect(List<GameSaveBase> into, HashSet<string> seen, NoGbaLayout layout,
                             string romPath, IGame game, IAdditionalApplication app)
        {
            if (game == null || string.IsNullOrWhiteSpace(romPath)) return;

            string gameIdEarly = Safe(() => game.Id) ?? "";
            // A version on its game's own file is not tied to the version (SaveRows.TieTo).
            string appIdEarly = app == null ? null
                : SaveRows.TieTo(Safe(() => app.Id), Safe(() => game.ApplicationPath), Safe(() => app.ApplicationPath), Full);

            // A DSiWARE TITLE KEEPS ITS SAVE INSIDE ITS NAND, at title/<cat>/<id>/data/public.sav,
            // and the image is 240 MB of which a few kilobytes are the save. So what is listed is
            // the difference a session made, packed into one file - see DsiSaveFile. Measured on a
            // real no$gba session: the game's own public.sav, the console settings, the launcher and
            // two built-in apps' saves all moved, which is why the unit is the whole difference.
            var header = NdsHeader.Describe(ResolveFullPath(romPath));
            if (header.Known && header.IsDSiWare)
            {
                var state = DsiWorkspace.RefreshSave(layout, header.TitleId, DsiBios7(layout));
                if (state != null && File.Exists(state)
                    && seen.Add("dsiware|" + state + "|" + (appIdEarly ?? gameIdEarly)))
                {
                    long bytes = 0; DateTime stamp = default;
                    try { var i = new FileInfo(state); bytes = i.Length; stamp = i.LastWriteTimeUtc; }
                    catch { }

                    into.Add(new GameSaveGame
                    {
                        GameId = gameIdEarly,
                        AdditionalApplicationId = appIdEarly,
                        FileLocation = state,
                        IsDirectory = false,
                        OriginalFileName = Path.GetFileName(state),
                        SaveGroupId = SaveId.Write(new SaveIdData { Emu = Emu, Kind = "nand", By = "title", Id = header.TitleId }),
                        SaveGroupName = DsiWareGroupName,
                        DisplayChipText = DsiWareChipText,
                        ReportedFileSizeBytes = bytes > 0 ? bytes : (long?)null,
                        ReportedLastModifiedUtc = stamp == default ? (DateTime?)null : stamp,
                    });
                }
                return;                                   // no .SAV in BATTERY\ for a DSiWare
            }

            var asset = NoGbaRoms.AssetName(ResolveFullPath(romPath));
            if (string.IsNullOrWhiteSpace(asset)) return;

            var path = Path.Combine(layout.BatteryDir, asset + NoGbaPaths.SaveExtension);
            if (!File.Exists(path)) return;

            string gameId = gameIdEarly;
            string appId = appIdEarly;
            if (!seen.Add(path + "|" + (appId ?? gameId))) return;

            long size = 0; DateTime when = default;
            try { var i = new FileInfo(path); size = i.Length; when = i.LastWriteTimeUtc; } catch { }

            into.Add(new GameSaveGame
            {
                GameId = gameId,
                AdditionalApplicationId = appId,
                FileLocation = path,
                IsDirectory = false,
                OriginalFileName = Path.GetFileName(path),
                SaveGroupId = SaveId.Write(new SaveIdData { Emu = Emu, Kind = "save", By = "name", Id = asset }),
                SaveGroupName = SaveGroupName,
                DisplayChipText = SaveChipText,
                ReportedFileSizeBytes = size > 0 ? size : (long?)null,
                ReportedLastModifiedUtc = when == default ? (DateTime?)null : when,
            });
        }

        /// <summary>The DSi ARM7 BIOS, needed by every call into the NAND library.</summary>
        private static string DsiBios7(NoGbaLayout layout)
        {
            foreach (var file in NoGbaBios.Files)
                if (string.Equals(file.OurName, "biosdsi7.bin", StringComparison.OrdinalIgnoreCase))
                    return NoGbaBios.Find(layout, file);
            return null;
        }

        /// <summary>Is this one of ours AND a DSiWare one?</summary>
        private static bool IsDsiWare(GameSaveBase save)
            => IsOurs(save) && SaveId.TryRead(save.SaveGroupId, out var d) && d.Kind == "nand";

        private static string TitleIdOf(GameSaveBase save)
            => IsDsiWare(save) && SaveId.TryRead(save.SaveGroupId, out var d) ? d.Id : null;

        /// <summary>Say what is in SNAP\, once per listing, and nothing more. Snapshots are not
        /// declared as saves because none has ever been seen; if one shows up, this is the line that
        /// says what it is called.</summary>
        private static void LookForSnapshots(NoGbaLayout layout)
        {
            try
            {
                if (layout?.SnapDir == null || !Directory.Exists(layout.SnapDir)) return;
                // Our own thumbnails are not what this is asking about.
                var names = new List<string>();
                foreach (var f in Directory.GetFiles(layout.SnapDir))
                    if (IsSnapshotCandidate(f)) names.Add(Path.GetFileName(f));
                if (names.Count == 0) return;

                Log.Info("SNAP holds " + names.Count + " file(s) - " + string.Join(", ", names)
                         + ". Snapshots are not listed as saves yet: none had ever been observed on "
                         + "disk when this was written. Please report these names.");
            }
            catch { }
        }

        /// <summary>A file in SNAP\ that could be a snapshot: anything but one of our thumbnails,
        /// finished (.png) or being written (.png.part).</summary>
        private static bool IsSnapshotCandidate(string path)
            => path != null && !LbipStateThumbs.IsThumb(path)
               && !path.EndsWith(LbipStateThumbs.Suffix + ".part", StringComparison.OrdinalIgnoreCase);

        /// <summary>For the session: a thumbnail beside each file no$gba writes into SNAP\. Its name
        /// is not known (see the header), so any file there counts.</summary>
        internal static void WatchSnapshotThumbs(string exe, NoGbaLayout layout)
        {
            try
            {
                if (layout?.SnapDir == null || string.IsNullOrWhiteSpace(exe)) return;
                LbipStateThumbs.Watch(exe, new[] { layout.SnapDir }, IsSnapshotCandidate, Log.Info);
            }
            catch (Exception ex) { Log.Warn("snapshot thumbnails", ex); }
        }

        // ── the contract ─────────────────────────────────────────────────────

        /// <summary>A cartridge save is one file, never a folder of parts.</summary>
        bool IOwnSaves.OwnIsSaveContainer(GameSaveBase save) => false;

        bool IOwnSaves.OwnUseSaveGroupIdForPersistedMatch(GameSaveBase save) => IsOurs(save);

        /// <summary>Guarded rather than decorative: saying a save is a container is a promise that
        /// TryBackupSave can extract it, and the host takes that literally - it makes a destination
        /// folder, asks, and records a backup from whatever appears. Refusing here would leave an
        /// empty folder and no backup. IsSaveContainer answers false, so this is never reached.</summary>
        bool IOwnSaves.OwnTryBackupSave(GameSaveBase save, string emulatorApplicationPath,
                                           string destinationFolder, out string error)
        {
            error = "A no$gba cartridge save is one file, not a container.";
            return false;
        }

        bool IOwnSaves.OwnIsSecondarySaveFile(string filePath) => false;

        /// <summary>None. See the header: a companion here would cost the save its interoperability
        /// with every other DS emulator for no gain.</summary>
        IReadOnlyList<string> IOwnSaves.OwnGetCompanionSaveFiles(string primaryFilePath)
            => Array.Empty<string>();

        /// <summary>Is this save where no$gba would actually read it? There is exactly one place it
        /// can be, so this only has to notice a copy that has wandered out of it.</summary>
        bool IOwnSaves.OwnIsSaveActive(GameSaveBase save, string emulatorApplicationPath)
        {
            try
            {
                if (!IsOurs(save) || string.IsNullOrWhiteSpace(emulatorApplicationPath)) return true;
                var loc = save.FileLocation;
                if (string.IsNullOrWhiteSpace(loc) || !File.Exists(loc)) return false;

                var layout = NoGbaPaths.Resolve(ResolveFullPath(emulatorApplicationPath));
                if (layout?.BatteryDir == null) return true;

                // A DSiWare save is active when it sits where it is pulled out of the NAND - not in BATTERY\.
                // (It used to be answered true for every one: IsOurs did not know the DSiWare ids.)
                if (IsDsiWare(save))
                {
                    var mine = DsiWorkspace.SavePathFor(layout, TitleIdOf(save));
                    return mine != null && string.Equals(Path.GetFullPath(loc), Path.GetFullPath(mine), StringComparison.OrdinalIgnoreCase);
                }

                var here = Path.GetDirectoryName(Path.GetFullPath(loc));
                return string.Equals(here, Path.GetFullPath(layout.BatteryDir),
                                     StringComparison.OrdinalIgnoreCase);
            }
            catch { return true; }
        }

        // ── restore ──────────────────────────────────────────────────────────

        AddSaveResponse IOwnSaves.OwnAddSaveFile(AddSaveArgs args)
        {
            try
            {
                var save = args?.SaveToAdd;
                if (save == null) return new AddSaveResponse("No save was supplied.");

                var source = save.FileLocation;
                if (string.IsNullOrWhiteSpace(source) || !File.Exists(source))
                    return new AddSaveResponse("This no$gba backup is not a file: " + (source ?? "(none)"));

                var layout = LayoutForSave(save);
                if (layout?.BatteryDir == null)
                    return new AddSaveResponse("Could not find the no$gba this save belongs to.");

                // A DSiWARE SAVE GOES BACK THROUGH THE NAND, not into BATTERY\. It lands under our
                // own name - <title id>\state.dsisave - whatever the vault copy was called, because
                // that is what every other part of the plugin looks for.
                if (IsDsiWare(save))
                {
                    var titleId = TitleIdOf(save);
                    if (!DsiWorkspace.RestoreSave(layout, titleId, DsiBios7(layout), source,
                                                  out var why))
                        return new AddSaveResponse("Could not restore this DSiWare save: " + why);

                    var mirror = DsiWorkspace.RefreshSave(layout, titleId, DsiBios7(layout));
                    Log.Info("restored the DSiWare save of " + titleId
                             + "; the next launch rebuilds its console around it");

                    return new AddSaveResponse(new GameSaveGame
                    {
                        GameId = save.GameId,
                        AdditionalApplicationId = save.AdditionalApplicationId,
                        FileLocation = mirror ?? source,
                        IsDirectory = false,
                        OriginalFileName = Path.GetFileName(mirror ?? source),
                        SaveGroupId = save.SaveGroupId,
                        SaveGroupName = save.SaveGroupName ?? DsiWareGroupName,
                        DisplayChipText = DsiWareChipText,
                    });
                }

                // The rom it goes to - the row's version's, else its game's (SaveRows.TargetFor) - and that rom's
                // asset name TODAY: a renamed rom gets its save back.
                var to = SaveRows.TargetFor(save, PluginHelper.DataManager, Full);
                if (to.Warning != null) Log.Warn("restore of " + save.OriginalFileName + ": " + to.Warning);
                if (to.Refused) return new AddSaveResponse(to.Warning);
                var original = FirstNonBlank(save.OriginalFileName, Path.GetFileName(source));
                var asset = string.IsNullOrWhiteSpace(to.RomPath) ? null : NoGbaRoms.AssetName(to.RomPath);
                var name = string.IsNullOrWhiteSpace(asset) ? original : asset + Path.GetExtension(original);
                var target = Path.Combine(layout.BatteryDir, name);

                if (File.Exists(target))
                {
                    bool overwrite = true;
                    try { overwrite = args.ShouldOverwriteFunc?.Invoke() ?? true; } catch { }
                    if (!overwrite) return new AddSaveResponse("Restore cancelled.");
                }

                Directory.CreateDirectory(layout.BatteryDir);
                File.Copy(source, target, overwrite: true);
                Log.Info("restored " + name + " -> " + layout.BatteryDir);

                return new AddSaveResponse(new GameSaveGame
                {
                    GameId = save.GameId,
                    AdditionalApplicationId = save.AdditionalApplicationId,
                    FileLocation = target,
                    IsDirectory = false,
                    OriginalFileName = name,
                    SaveGroupId = save.SaveGroupId,
                    SaveGroupName = string.IsNullOrWhiteSpace(save.SaveGroupName)
                        ? SaveGroupName : save.SaveGroupName,
                    DisplayChipText = save.DisplayChipText ?? SaveChipText,
                });
            }
            catch (Exception ex)
            {
                Log.Warn("AddSaveFile failed", ex);
                return new AddSaveResponse("Could not restore this no$gba save: " + ex.Message);
            }
        }

        // ── delete ───────────────────────────────────────────────────────────

        PluginResponse IOwnSaves.OwnRemoveSave(GameSaveBase save)
        {
            try
            {
                if (!IsOurs(save)) return base.RemoveSave(save);

                // A DSiWARE SAVE IS OURS TO DELETE PROPERLY: the file, the title's folder and the
                // working image when it still holds the same title. Deleting the file alone would
                // leave the next launch to capture the session straight back out of the image.
                if (IsDsiWare(save))
                {
                    var layout = LayoutForSave(save);
                    if (layout == null)
                        return new PluginResponse(false, "Could not find the no$gba this save belongs to.");
                    if (!DsiWorkspace.DropState(layout, TitleIdOf(save), out var why))
                        return new PluginResponse(false, "Could not delete this DSiWare save: " + why);
                    return new PluginResponse(true);
                }

                var loc = save?.FileLocation;
                if (string.IsNullOrWhiteSpace(loc))
                {
                    Log.Warn("asked to delete a no$gba save with no location");
                    return new PluginResponse(false, "This save has no location.");
                }

                try { if (File.Exists(loc)) File.Delete(loc); }
                catch (Exception ex)
                {
                    Log.Warn("could not delete " + loc, ex);
                    return new PluginResponse(false, "Could not delete " + Path.GetFileName(loc) + ".");
                }

                Log.Info("deleted " + Path.GetFileName(loc));
                return new PluginResponse(true);
            }
            catch (Exception ex)
            {
                Log.Warn("RemoveSave failed", ex);
                return new PluginResponse(false, "Could not delete this no$gba save: " + ex.Message);
            }
        }

        // ── plumbing ─────────────────────────────────────────────────────────

        private static bool IsOursDsiWare(GameSaveBase save) => IsDsiWare(save);

        /// <summary>Ours: the cartridge saves AND the DSiWare ones (the DSiWare ids used to be left out, so their
        /// delete went to the host's default and the .dsisave came back from the NAND at the next listing).</summary>
        private static bool IsOurs(GameSaveBase save) => SaveRows.IsOurs(save?.SaveGroupId, Emu);

        /// <summary>A path as LaunchBox stores it, made full; null for nothing.</summary>
        private static string Full(string path) => string.IsNullOrWhiteSpace(path) ? null : ResolveFullPath(path);

        /// <summary>The installation a save belongs to. From the save's own location when it is in a
        /// BATTERY folder - which is where ours always are - and from the library otherwise.</summary>
        private static NoGbaLayout LayoutForSave(GameSaveBase save)
        {
            try
            {
                var dir = Path.GetDirectoryName(save?.FileLocation);
                if (!string.IsNullOrEmpty(dir)
                    && string.Equals(Path.GetFileName(dir), NoGbaPaths.BatteryDirName,
                                     StringComparison.OrdinalIgnoreCase))
                {
                    var install = Path.GetDirectoryName(dir);
                    var exe = install == null ? null : NoGbaPaths.FindExecutable(install);
                    if (exe != null) return NoGbaPaths.Resolve(exe);
                }

                var dm = PluginHelper.DataManager;
                var game = dm?.GetGameById(save?.GameId);
                var emuId = Safe(() => game?.EmulatorId);
                var emu = string.IsNullOrEmpty(emuId) ? null : dm?.GetEmulatorById(emuId);
                var appPath = Safe(() => emu?.ApplicationPath);
                if (NoGbaPaths.IsNoGbaExecutable(appPath))
                    return NoGbaPaths.Resolve(ResolveFullPath(appPath));

                foreach (var candidate in dm?.GetAllEmulators() ?? Array.Empty<IEmulator>())
                {
                    var path = Safe(() => candidate.ApplicationPath);
                    if (NoGbaPaths.IsNoGbaExecutable(path))
                        return NoGbaPaths.Resolve(ResolveFullPath(path));
                }
                return null;
            }
            catch (Exception ex) { Log.Warn("could not resolve the no$gba installation", ex); return null; }
        }

        private static IGame SafeGame(string gameId)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(gameId)) return null;
                return PluginHelper.DataManager?.GetGameById(gameId);
            }
            catch { return null; }
        }

        private static string FirstNonBlank(params string[] values)
        {
            foreach (var value in values ?? Array.Empty<string>())
                if (!string.IsNullOrWhiteSpace(value)) return value;
            return null;
        }
    }
}
