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

using System;
using System.Collections.Generic;
using System.IO;
using Unbroken.LaunchBox.Plugins;
using Unbroken.LaunchBox.Plugins.Data;
using LbIntegrations.Dsi;

namespace LbIntegrations.NoGba
{
    public partial class NoGbaPlugin
    {
        internal const string SavePrefix = "nogba:save:";
        private const string SaveGroupName = "Cartridge save";
        private const string SaveChipText = "SAV";

        public override bool SupportsSaveManagement() => true;

        /// <summary>No numbered slots. no$gba's File menu offers one "Write snapshot" and one "Load
        /// snapshot", with no slot to choose - and nothing was ever observed on disk. An invented
        /// set of slots would be a promise the emulator does not keep.</summary>
        public override IReadOnlyDictionary<int, string> GetPotentialSaveSlots()
            => new Dictionary<int, string>();

        public override GetSavesResponse GetSaves(GetSavesArgs args)
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

            var asset = NoGbaRoms.AssetName(ResolveFullPath(romPath));
            if (string.IsNullOrWhiteSpace(asset)) return;

            var path = Path.Combine(layout.BatteryDir, asset + NoGbaPaths.SaveExtension);
            if (!File.Exists(path)) return;

            string gameId = Safe(() => game.Id) ?? "";
            string appId = app != null ? Safe(() => app.Id) : null;
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
                SaveGroupId = SavePrefix + asset,
                SaveGroupName = SaveGroupName,
                DisplayChipText = SaveChipText,
                ReportedFileSizeBytes = size > 0 ? size : (long?)null,
                ReportedLastModifiedUtc = when == default ? (DateTime?)null : when,
            });
        }

        /// <summary>Say what is in SNAP\, once per listing, and nothing more. Snapshots are not
        /// declared as saves because none has ever been seen; if one shows up, this is the line that
        /// says what it is called.</summary>
        private static void LookForSnapshots(NoGbaLayout layout)
        {
            try
            {
                if (layout?.SnapDir == null || !Directory.Exists(layout.SnapDir)) return;
                var files = Directory.GetFiles(layout.SnapDir);
                if (files.Length == 0) return;

                var names = new List<string>();
                foreach (var f in files) names.Add(Path.GetFileName(f));
                Log.Info("SNAP holds " + files.Length + " file(s) - " + string.Join(", ", names)
                         + ". Snapshots are not listed as saves yet: none had ever been observed on "
                         + "disk when this was written. Please report these names.");
            }
            catch { }
        }

        // ── the contract ─────────────────────────────────────────────────────

        /// <summary>A cartridge save is one file, never a folder of parts.</summary>
        public override bool IsSaveContainer(GameSaveBase save) => false;

        public override bool UseSaveGroupIdForPersistedMatch(GameSaveBase save) => IsOurs(save);

        /// <summary>Guarded rather than decorative: saying a save is a container is a promise that
        /// TryBackupSave can extract it, and the host takes that literally - it makes a destination
        /// folder, asks, and records a backup from whatever appears. Refusing here would leave an
        /// empty folder and no backup. IsSaveContainer answers false, so this is never reached.</summary>
        public override bool TryBackupSave(GameSaveBase save, string emulatorApplicationPath,
                                           string destinationFolder, out string error)
        {
            error = "A no$gba cartridge save is one file, not a container.";
            return false;
        }

        public override bool IsSecondarySaveFile(string filePath) => false;

        /// <summary>None. See the header: a companion here would cost the save its interoperability
        /// with every other DS emulator for no gain.</summary>
        public override IReadOnlyList<string> GetCompanionSaveFiles(string primaryFilePath)
            => Array.Empty<string>();

        /// <summary>Is this save where no$gba would actually read it? There is exactly one place it
        /// can be, so this only has to notice a copy that has wandered out of it.</summary>
        public override bool IsSaveActive(GameSaveBase save, string emulatorApplicationPath)
        {
            try
            {
                if (!IsOurs(save) || string.IsNullOrWhiteSpace(emulatorApplicationPath)) return true;
                var loc = save.FileLocation;
                if (string.IsNullOrWhiteSpace(loc) || !File.Exists(loc)) return false;

                var layout = NoGbaPaths.Resolve(ResolveFullPath(emulatorApplicationPath));
                if (layout?.BatteryDir == null) return true;

                var here = Path.GetDirectoryName(Path.GetFullPath(loc));
                return string.Equals(here, Path.GetFullPath(layout.BatteryDir),
                                     StringComparison.OrdinalIgnoreCase);
            }
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
                if (string.IsNullOrWhiteSpace(source) || !File.Exists(source))
                    return new AddSaveResponse("This no$gba backup is not a file: " + (source ?? "(none)"));

                var layout = LayoutForSave(save);
                if (layout?.BatteryDir == null)
                    return new AddSaveResponse("Could not find the no$gba this save belongs to.");

                var name = FirstNonBlank(save.OriginalFileName, Path.GetFileName(source));
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

        public override PluginResponse RemoveSave(GameSaveBase save)
        {
            try
            {
                if (!IsOurs(save)) return base.RemoveSave(save);

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

        private static bool IsOurs(GameSaveBase save)
            => save?.SaveGroupId != null
               && save.SaveGroupId.StartsWith(SavePrefix, StringComparison.OrdinalIgnoreCase);

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
