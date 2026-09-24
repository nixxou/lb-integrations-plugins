// Save management for melonDS.
//
// THREE KINDS, ALL THREE ONE FILE. Two are named after the ROM rather than after anything read out
// of it; the third is named after nothing the user chose, because it is assembled rather than
// written - and that is the whole DSiWare story.
//
//   Battery save   <asset name>.sav
//   Save states    <asset name>.ml1 .. .ml8
//
// THE ASSET NAME IS THE ROM'S FILE NAME WITHOUT ITS LAST EXTENSION, which melonDS computes as
// romname.substr(0, romname.rfind('.')) and then passes to getAssetPath (EmuInstance.cpp:445-484,
// :1882-1884). So "Foo (USA).nds" saves to "Foo (USA).sav", parentheses and spaces untouched. When
// the ROM came out of an archive the name is the INNER entry's, not the archive's - see NdsHeader.
//
// TWO DISPOSITIONS, and both have to be read. [Instance0] SaveFilePath and SavestatePath are empty
// on a stock melonDS, and empty means "beside the ROM". This plugin fills them on installs it makes
// itself, pointing at <install>\saves and <install>\savestates, and never touches them on a melonDS
// the user set up. So a save is looked for in the configured folder when there is one, and next to
// the ROM otherwise - never in both, or the same save would be listed twice.
//
// .mln IS NOT A SAVESTATE EXTENSION, however much it looks like one. It appears exactly once in the
// tree, as the default suffix of the "Save state -> File..." dialog (Window.cpp:1583). The slots are
// ".ml" + the digit, and the load dialog's own glob is (*.ml*). A plugin globbing for .mln would
// find nothing and report a game with eight states as having none.
//
// SLOTS ARE 1 TO 8 ON DISK AND 1 TO 8 ON SCREEN (Window.cpp:356, :372) - no off-by-one to reconcile,
// unlike PPSSPP. Slot 0 is the file dialog's menu entry, not a slot.
//
// A THIRD KIND, and it is the odd one: a DSiWare save. It lives INSIDE the title's NAND, at
// title/<category>/<id>/data/public.sav (DSi_NAND.cpp:1077-1092), and the image is 240 MB of which a
// few kilobytes are the save. Handing the image to the host would mean hashing all of it to draw a
// freshness dot, and a vault copy of 240 MB per backup. So the difference the session made is taken
// out of the NAND and THAT is what is listed - as one file, <title id>\state.dsisave - while the
// NAND stays the truth: a restore goes back through it, and a deletion throws the save away and
// forgets the working image, so the next launch rebuilds a console that has never played this title.
// See DsiWorkspace.RefreshSave and DsiWorkspace.DropState.
//
// IT WAS A FOLDER UNTIL IT WAS PACKED, and the reason it stopped being one is the host's dispatch,
// not tidiness: IsSaveContainer picks between a path that copies a directory into the vault and a
// path that copies a file, and the container path is where every defect this file ever had came
// from. See DsiSaveFile.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Unbroken.LaunchBox.Plugins;
using Unbroken.LaunchBox.Plugins.Data;
using LbIntegrations.Dsi;

namespace LbIntegrations.MelonDs
{
    public partial class MelonDsPlugin
    {
        internal const string SaveExtension = ".sav";
        internal const string StatePrefix = "melonds:state:";
        internal const string SavePrefix = "melonds:save:";
        internal const string DsiWarePrefix = "melonds:dsiware:";

        private const string SaveGroupName = "Cartridge save";
        private const string SaveChipText = "SAV";
        private const string StateGroupName = "Save states";
        private const string StateChipText = "STATE";
        private const string DsiWareGroupName = "DSiWare save";
        private const string DsiWareChipText = "NAND";

        /// <summary>melonDS's menus run slot 1 through 8 inclusive: `for (int i = 1; i &lt; 9; i++)`.</summary>
        internal const int FirstSlot = 1;
        internal const int LastSlot = 8;

        public override bool SupportsSaveManagement() => true;

        public override IReadOnlyDictionary<int, string> GetPotentialSaveSlots()
        {
            var slots = new Dictionary<int, string>();
            for (int i = FirstSlot; i <= LastSlot; i++)
                slots[i] = i.ToString(System.Globalization.CultureInfo.InvariantCulture);
            return slots;
        }

        // ── listing ──────────────────────────────────────────────────────────

        public override GetSavesResponse GetSaves(GetSavesArgs args)
        {
            try
            {
                if (args?.Emulator == null) return new GetSavesResponse("No emulator was supplied.");

                string appPath = Safe(() => args.Emulator.ApplicationPath);
                if (!MelonDsPaths.IsMelonDsExecutable(appPath))
                    return new GetSavesResponse("This emulator is not melonDS.");

                var layout = MelonDsPaths.Resolve(ResolveFullPath(appPath));
                if (string.IsNullOrEmpty(layout.InstallDir))
                    return new GetSavesResponse(new List<GameSaveBase>());

                var found = new List<GameSaveBase>();
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                int states = 0;

                // Additional applications first, then games: a version has its own file and therefore
                // its own asset name.
                foreach (var app in args.AdditionalApplications
                         ?? (IReadOnlyCollection<IAdditionalApplication>)Array.Empty<IAdditionalApplication>())
                {
                    var game = SafeGame(Safe(() => app.GameId));
                    states += Collect(found, seen, layout, Safe(() => app.ApplicationPath), game, app);
                }
                foreach (var game in args.Games ?? (IReadOnlyCollection<IGame>)Array.Empty<IGame>())
                    states += Collect(found, seen, layout, Safe(() => game.ApplicationPath), game, null);

                Log.Verbose("found " + (found.Count - states) + " save(s) and " + states + " state(s) for "
                            + layout.InstallDir);
                return new GetSavesResponse(found);
            }
            catch (Exception ex)
            {
                Log.Warn("GetSaves failed", ex);
                return new GetSavesResponse("Could not read melonDS saves: " + ex.Message);
            }
        }

        /// <summary>Everything one game owns. Returns how many of them were states.</summary>
        private int Collect(List<GameSaveBase> into, HashSet<string> seen, MelonDsLayout layout,
                            string romPath, IGame game, IAdditionalApplication app)
        {
            if (game == null || string.IsNullOrWhiteSpace(romPath)) return 0;

            var rom = NdsHeader.Describe(ResolveFullPath(romPath));
            if (string.IsNullOrWhiteSpace(rom.AssetName)) return 0;

            string gameId = Safe(() => game.Id) ?? "";
            string appId = app != null ? Safe(() => app.Id) : null;
            string context = appId ?? gameId;

            // A DSiWare title keeps its save INSIDE its NAND, not beside the ROM, and what comes
            // out is not one file. Measured on one real session: the game's own public.sav was
            // 16 KB out of 4.2 MB across eleven files - the console settings, the menu's data and
            // the built-in apps' saves had all moved too. So the unit is the whole difference -
            // packed into one .dsisave, which is what makes it a file here. See DsiSaveFile.
            if (rom.IsDSiWare)
            {
                var state = DsiWorkspace.RefreshSave(layout, rom.TitleId, Bios7Of(layout));
                if (state != null && File.Exists(state)
                    && seen.Add("dsiware|" + state + "|" + context))
                    into.Add(Row(state, gameId, appId, DsiWarePrefix + rom.TitleId,
                                 DsiWareGroupName, DsiWareChipText));
                return 0;                                 // no .sav and no .ml<n> for DSiWare
            }

            var saveDir = layout.HasRedirectedSaves ? layout.SaveDir : rom.RomDir;
            var stateDir = layout.HasRedirectedStates ? layout.StateDir : rom.RomDir;

            var savePath = Combine(saveDir, rom.AssetName + SaveExtension);
            if (savePath != null && File.Exists(savePath) && seen.Add("save|" + savePath + "|" + context))
                into.Add(Row(savePath, gameId, appId, SavePrefix + rom.AssetName,
                             SaveGroupName, SaveChipText));

            int states = 0;
            for (int slot = FirstSlot; slot <= LastSlot; slot++)
            {
                var statePath = Combine(stateDir, rom.AssetName + StateExtension(slot));
                if (statePath == null || !File.Exists(statePath)) continue;
                if (!seen.Add("state|" + statePath + "|" + context)) continue;

                long size = 0; DateTime when = default;
                try { var i = new FileInfo(statePath); size = i.Length; when = i.LastWriteTimeUtc; } catch { }

                into.Add(new GameSaveState
                {
                    GameId = gameId,
                    AdditionalApplicationId = appId,
                    FileLocation = statePath,
                    IsDirectory = false,
                    OriginalFileName = Path.GetFileName(statePath),
                    Slot = slot,
                    SaveGroupId = StatePrefix + rom.AssetName + ":" + slot,
                    SaveGroupName = StateGroupName,
                    DisplayChipText = StateChipText,
                    ReportedFileSizeBytes = size > 0 ? size : (long?)null,
                    ReportedLastModifiedUtc = when == default ? (DateTime?)null : when,
                });
                states++;
            }
            return states;
        }

        /// <summary>".ml" followed by the slot digit - getSavestateName, EmuInstance.cpp:696-700.</summary>
        internal static string StateExtension(int slot) => ".ml" + (char)('0' + slot);

        private static GameSaveGame Row(string path, string gameId, string appId,
                                        string groupId, string groupName, string chip)
        {
            long size = 0; DateTime when = default;
            try { var i = new FileInfo(path); size = i.Length; when = i.LastWriteTimeUtc; }
            catch { }

            return new GameSaveGame
            {
                GameId = gameId,
                AdditionalApplicationId = appId,
                FileLocation = path,
                // SAID EXPLICITLY, because the flag matters and its absence is invisible. It is
                // false for all three kinds now, but the cost of getting it wrong is worth recording:
                // a host that believes a directory is a file finds nothing there and treats the save
                // as gone - measured on LaunchBox 14 with Xenia, where the card was drawn from vault
                // copies alone under a violet "In Vault" pill while the real save sat right there.
                // LiteBox covered for it, computing the same thing from Directory.Exists, so the
                // fault only ever showed in one of the two hosts.
                IsDirectory = false,
                OriginalFileName = Path.GetFileName(path),
                SaveGroupId = groupId,
                SaveGroupName = groupName,
                DisplayChipText = chip,
                ReportedFileSizeBytes = size > 0 ? size : (long?)null,
                ReportedLastModifiedUtc = when == default ? (DateTime?)null : when,
            };
        }

        // ── the contract ─────────────────────────────────────────────────────

        /// <summary>NONE OF THE THREE IS A CONTAINER ANY MORE, and that single answer is what
        /// moves a DSiWare save from the host's folder path to its file path.
        ///
        /// The host dispatches a backup on this: a container is extracted by the plugin into a temp
        /// folder and lands in the vault as a FOLDER with no extension; a file is copied straight in
        /// and lands as "&lt;game name&gt;&lt;extension&gt;". A DSiWare state really is several files, and
        /// saying so was honest - but it bought the container path, which is where every defect this
        /// plugin had in save management came from: backups that made empty folders, restores
        /// refused for not being a file, a delete that quietly did nothing. Packing the state into
        /// one .dsisave buys the other path, the one every other save in this repository takes.</summary>
        public override bool IsSaveContainer(GameSaveBase save) => false;

        public override bool UseSaveGroupIdForPersistedMatch(GameSaveBase save) => IsOurs(save);

        /// <summary>Guarded, not decorative. IsSaveContainer answers false, so the host never asks
        /// - and if it ever did, the honest answer is that a .dsisave IS the extract.
        ///
        /// This used to do the work, and the history is worth keeping: saying a save is a container
        /// is a promise that this method can extract it, and the host takes it literally - it makes
        /// a destination, asks, and records a backup from whatever turns up. Refusing here, which an
        /// earlier version did with a message claiming the save was a plain file, left the host an
        /// empty folder and no backup, every session, silently. Measured on Zelda: Four Swords, 0
        /// backups after several evenings and a trail of empty directories in the vault.
        ///
        /// WHAT THAT PATH ALSO CARRIED was the last hook before a backup: this refreshed the save
        /// first, because a backup asked for outside the save window has not been through GetSaves
        /// and the working image may hold a session nobody wrote down. Measured in the host, every
        /// route to a backup lists first - SaveBackupService.OnGameClosed calls ScanBase, the sweep
        /// calls it per game, the Edit Game window rebuilds its groups - and listing is what calls
        /// RefreshSave. The residue, if some host ever backed up without listing, is one session of
        /// lag; it is not a loss.</summary>
        public override bool TryBackupSave(GameSaveBase save, string emulatorApplicationPath,
                                           string destinationFolder, out string error)
        {
            error = "A melonDS save is one file, not a container.";
            return false;
        }

        /// <summary>The melonDS a save belongs to, from the emulator path when the host supplied one
        /// and from the save's own location when it did not - a state folder is
        /// &lt;install&gt;\dsi\&lt;title&gt;\state, so the install is three levels up.</summary>
        private static MelonDsLayout LayoutForSave(GameSaveBase save, string emulatorApplicationPath)
        {
            try
            {
                if (MelonDsPaths.IsMelonDsExecutable(emulatorApplicationPath))
                    return MelonDsPaths.Resolve(ResolveFullPath(emulatorApplicationPath));

                var titleDir = Path.GetDirectoryName(save?.FileLocation);
                var dsiDir = Path.GetDirectoryName(titleDir);
                var install = Path.GetDirectoryName(dsiDir);
                var exe = string.IsNullOrEmpty(install) ? null : MelonDsPaths.FindExecutable(install);
                return exe == null ? null : MelonDsPaths.Resolve(exe);
            }
            catch { return null; }
        }

        /// <summary>Nothing melonDS writes beside a save belongs to it.
        ///
        /// A PLUGIN MUST NEVER CALL ITS OWN SAVE SECONDARY - the mistake that made Xenia's live saves
        /// show as vault copies. Here the question is easy: a .sav and a .ml&lt;n&gt; are each a save in
        /// their own right, one per slot, and there is no second file to demote. What this does catch
        /// is melonDS's GBA slot-2 companion, which shares the folder and the naming rule but belongs
        /// to a cartridge this plugin does not manage.</summary>
        public override bool IsSecondarySaveFile(string filePath) => false;

        /// <summary>A melonDS save has no companions: the battery save is one file, and a state
        /// carries no sidecar - no screenshot, no undo file.</summary>
        public override IReadOnlyList<string> GetCompanionSaveFiles(string primaryFilePath)
            => Array.Empty<string>();

        /// <summary>Is this save where the emulator would actually read it?
        ///
        /// Both dispositions count as active: a redirected install reads from its configured folder, a
        /// stock one from beside the ROM. What is NOT active is a save sitting in the other one after
        /// the setting changed - which is precisely the case this has to catch, since this plugin
        /// redirects the installs it makes.</summary>
        public override bool IsSaveActive(GameSaveBase save, string emulatorApplicationPath)
        {
            try
            {
                if (!IsOurs(save) || string.IsNullOrWhiteSpace(emulatorApplicationPath)) return true;
                var loc = save.FileLocation;
                if (string.IsNullOrWhiteSpace(loc)) return true;

                var layout = MelonDsPaths.Resolve(ResolveFullPath(emulatorApplicationPath));

                // A DSiWare save is active when it sits beside the NAND it was pulled out of. The
                // NAND is the truth; the extracted file is a view of it.
                if (IsDsiWare(save))
                {
                    var mine = DsiWorkspace.SavePathFor(layout, TitleIdOf(save));
                    return mine != null && Exists(loc)
                           && string.Equals(Path.GetFullPath(loc), Path.GetFullPath(mine),
                                            StringComparison.OrdinalIgnoreCase);
                }

                bool state = save is GameSaveState;
                var configured = state ? layout.StateDir : layout.SaveDir;

                if (!string.IsNullOrWhiteSpace(configured))
                {
                    var dir = Path.GetDirectoryName(Path.GetFullPath(loc)) ?? "";
                    if (!string.Equals(dir.TrimEnd(Path.DirectorySeparatorChar),
                                       Path.GetFullPath(configured).TrimEnd(Path.DirectorySeparatorChar),
                                       StringComparison.OrdinalIgnoreCase))
                        return false;
                }

                // AND IT MUST STILL BE THERE. Being in the right folder is not enough: the path can
                // name a place that no longer exists, and "active" means the save the emulator would
                // actually read. Measured on Xenia, where a reinstall moved every save and the old
                // records still passed the folder test.
                return Exists(loc);
            }
            catch { return true; }
        }

        // ── restore ──────────────────────────────────────────────────────────

        /// <summary>Put a vault copy back where melonDS reads it.
        ///
        /// The vault keeps melonDS's own file name, so the target name is that name in the right
        /// folder - no translation, and what lands is what was taken. Which folder depends on the
        /// kind and on how the install is configured.</summary>
        public override AddSaveResponse AddSaveFile(AddSaveArgs args)
        {
            try
            {
                var save = args?.SaveToAdd;
                if (save == null) return new AddSaveResponse("No save was supplied.");

                var source = save.FileLocation;
                if (string.IsNullOrWhiteSpace(source))
                    return new AddSaveResponse("This melonDS backup has no location.");

                // THE FOLDER CASE GOES FIRST, and that is not tidiness. A DSiWare save is a folder,
                // so what the host hands back out of the vault is a folder - and the File.Exists
                // that used to guard this method rejected every one of them before the branch below
                // could see it, with "this backup is not a file". A restore that can never happen,
                // for a save whose whole point is that it is not a file.
                if (IsDsiWare(save))
                {
                    if (!File.Exists(source))
                        return new AddSaveResponse("This DSiWare backup is not a file: " + source);
                    return RestoreDsiWare(save, source);
                }

                if (!File.Exists(source))
                    return new AddSaveResponse("This melonDS backup is not a file: " + source);

                string name = FirstNonBlank(save.OriginalFileName, Path.GetFileName(source));
                var targetDir = RestoreDirFor(save, name);
                if (targetDir == null)
                    return new AddSaveResponse("Could not work out where melonDS reads this save from.");

                string target = Path.Combine(targetDir, name);
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
                    return new AddSaveResponse(new GameSaveState
                    {
                        GameId = state.GameId,
                        AdditionalApplicationId = state.AdditionalApplicationId,
                        FileLocation = target,
                        IsDirectory = false,
                        OriginalFileName = name,
                        Slot = state.Slot,
                        SaveGroupId = state.SaveGroupId,
                        SaveGroupName = string.IsNullOrWhiteSpace(state.SaveGroupName)
                            ? StateGroupName : state.SaveGroupName,
                        DisplayChipText = StateChipText,
                    });

                return new AddSaveResponse(Row(target, save.GameId, save.AdditionalApplicationId,
                                               save.SaveGroupId, save.SaveGroupName,
                                               save.DisplayChipText ?? SaveChipText));
            }
            catch (Exception ex)
            {
                Log.Warn("AddSaveFile failed", ex);
                return new AddSaveResponse("Could not restore this melonDS save: " + ex.Message);
            }
        }

        /// <summary>Put a vault copy back inside the title's NAND, then re-read what landed.</summary>
        private static AddSaveResponse RestoreDsiWare(GameSaveBase save, string source)
        {
            try
            {
                var dm = PluginHelper.DataManager;
                var layout = LayoutFor(dm?.GetGameById(save.GameId), dm);
                if (layout == null) return new AddSaveResponse("Could not locate the melonDS installation.");

                var titleId = TitleIdOf(save);
                if (!DsiWorkspace.RestoreSave(layout, titleId, Bios7Of(layout), source, out var error))
                    return new AddSaveResponse("Could not restore this DSiWare save: " + error);

                // WORKS ON NOTHING AT ALL, which is the case deleting a save leaves behind: the
                // title folder is gone, reference.txt and the cached metadata with it. The state is
                // written into a folder created on the spot, and the next launch rebuilds the image,
                // reinstalls the title, takes a fresh reference walk and applies this state onto it -
                // in that order, in PrepareDsiWare. Nothing here depends on what the delete removed.
                var mirror = DsiWorkspace.RefreshSave(layout, titleId, Bios7Of(layout));
                Log.Info("restored the DSiWare save of " + titleId
                         + "; the next launch rebuilds its console around it");

                // The row that comes back has to describe the same thing the listing does, or the
                // host looks for it in the wrong shape and decides the save is not there.
                return new AddSaveResponse(Row(mirror ?? source, save.GameId, save.AdditionalApplicationId,
                                               save.SaveGroupId, save.SaveGroupName ?? DsiWareGroupName,
                                               DsiWareChipText));
            }
            catch (Exception ex)
            {
                Log.Warn("restoring a DSiWare save failed", ex);
                return new AddSaveResponse("Could not restore this DSiWare save: " + ex.Message);
            }
        }

        /// <summary>Where a restored save belongs. The configured folder when the install has one;
        /// otherwise beside the game's own ROM, which has to be found through the data manager because
        /// AddSaveArgs carries neither the emulator nor the ROM.</summary>
        private static string RestoreDirFor(GameSaveBase save, string fileName)
        {
            try
            {
                var dm = PluginHelper.DataManager;
                var game = dm?.GetGameById(save.GameId);

                var layout = LayoutFor(game, dm);
                if (layout != null)
                {
                    var configured = save is GameSaveState ? layout.StateDir : layout.SaveDir;
                    if (!string.IsNullOrWhiteSpace(configured)) return configured;
                }

                var romPath = ResolveFullPath(Safe(() => game?.ApplicationPath));
                var romDir = string.IsNullOrWhiteSpace(romPath) ? null : Path.GetDirectoryName(romPath);
                if (!string.IsNullOrWhiteSpace(romDir)) return romDir;

                // Last resort: beside where the emulator would have put it by default.
                return layout?.DefaultSaveDir;
            }
            catch (Exception ex) { Log.Warn("could not resolve a restore folder for " + fileName, ex); return null; }
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
                    Log.Warn("asked to delete a melonDS save with no location");
                    return new PluginResponse(false, "This save has no location.");
                }

                // A DSiWare save is a FOLDER of ours, and deleting it means something precise:
                // the next launch rebuilds the image from the user's dump, installs the title, and
                // restores nothing. That is a title that has never been played.
                //
                // This used to refuse. It was written when the row pointed at a copy of public.sav
                // extracted from the NAND, where deleting the copy really would have changed nothing
                // in the game. The row has pointed at dsi\<title>\state\ since the save became the
                // whole folder, and the refusal outlived its reason - a Delete Save that quietly
                // does nothing is worse than either answer.
                if (IsDsiWare(save))
                {
                    var layout = LayoutFor(PluginHelper.DataManager?.GetGameById(save.GameId),
                                           PluginHelper.DataManager);
                    if (layout == null)
                        return new PluginResponse(false, "Could not find the melonDS this save belongs to.");

                    if (!DsiWorkspace.DropState(layout, TitleIdOf(save), out var why))
                        return new PluginResponse(false, "Could not delete this DSiWare save: " + why);
                    return new PluginResponse(true);
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
                return new PluginResponse(false, "Could not delete this melonDS save: " + ex.Message);
            }
        }

        // ── plumbing ─────────────────────────────────────────────────────────

        private static bool Exists(string path)
        {
            try { return File.Exists(path) || Directory.Exists(path); } catch { return false; }
        }

        private static bool IsOurs(GameSaveBase save)
            => save?.SaveGroupId != null
               && (StartsWith(save.SaveGroupId, SavePrefix)
                   || StartsWith(save.SaveGroupId, StatePrefix)
                   || StartsWith(save.SaveGroupId, DsiWarePrefix));

        private static bool IsDsiWare(GameSaveBase save)
            => save?.SaveGroupId != null && StartsWith(save.SaveGroupId, DsiWarePrefix);

        /// <summary>The title id carried by a DSiWare save's group id.</summary>
        private static string TitleIdOf(GameSaveBase save)
            => IsDsiWare(save) ? save.SaveGroupId.Substring(DsiWarePrefix.Length) : null;

        /// <summary>DSi.BIOS7Path, resolved, or null. Without it a NAND cannot be opened at all.</summary>
        private static string Bios7Of(MelonDsLayout layout)
        {
            try
            {
                var map = MelonDsToml.Read(layout.ConfigFile, MelonDsPaths.DSiTable, "BIOS7Path");
                if (!map.TryGetValue("BIOS7Path", out var value) || string.IsNullOrWhiteSpace(value))
                    return null;
                return Path.IsPathRooted(value) || string.IsNullOrEmpty(layout.ConfigDir)
                    ? value
                    : Path.GetFullPath(Path.Combine(layout.ConfigDir, value));
            }
            catch { return null; }
        }

        private static bool StartsWith(string value, string prefix)
            => value != null && value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);

        private static string Combine(string dir, string name)
        {
            try { return string.IsNullOrWhiteSpace(dir) ? null : Path.Combine(dir, name); }
            catch { return null; }
        }

        private static string FirstNonBlank(params string[] candidates)
            => candidates?.FirstOrDefault(c => !string.IsNullOrWhiteSpace(c));

        /// <summary>The melonDS installation behind a game: its own emulator first, then any melonDS
        /// the library knows.</summary>
        private static MelonDsLayout LayoutFor(IGame game, IDataManager dm)
        {
            try
            {
                var emuId = Safe(() => game?.EmulatorId);
                var emu = string.IsNullOrEmpty(emuId) ? null : dm?.GetEmulatorById(emuId);
                var appPath = Safe(() => emu?.ApplicationPath);
                if (MelonDsPaths.IsMelonDsExecutable(appPath))
                    return MelonDsPaths.Resolve(ResolveFullPath(appPath));

                var any = dm?.GetAllEmulators()?
                    .FirstOrDefault(e => MelonDsPaths.IsMelonDsExecutable(Safe(() => e.ApplicationPath)));
                var anyPath = Safe(() => any?.ApplicationPath);
                return anyPath == null ? null : MelonDsPaths.Resolve(ResolveFullPath(anyPath));
            }
            catch (Exception ex) { Log.Warn("could not resolve the melonDS installation", ex); return null; }
        }

        /// <summary>The game an additional application belongs to, through the public data manager.</summary>
        private static IGame SafeGame(string gameId)
        {
            try
            {
                return string.IsNullOrWhiteSpace(gameId) ? null : PluginHelper.DataManager?.GetGameById(gameId);
            }
            catch { return null; }
        }
    }
}
