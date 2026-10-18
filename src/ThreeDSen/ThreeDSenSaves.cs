// Save management for 3dSen - its save STATES, the only saves it has (docs\3dsen.md sections 5 and 6).
//
// NO BATTERY SAVE: 3dSen keeps the cartridge's RAM in memory only, zeroed at every ROM load, and writes it nowhere but
// inside its states ([src] DefaultCore/Memory.cs; [run] Zelda II played then closed: no .sav/.srm anywhere). So there is
// no cartridge row, and the game's window and the help say plainly that a battery game's progress lives in its states.
//
// THE STATES, read through ThreeDSenSavCfg: 36 numbered slots and a quick one, sav\<slot>.sav in 3dSen's user data
// folder (LocalLow\Geod Studio\3dSen - Unity's persistentDataPath, no setting moves it), indexed by sav.cfg, which says
// for each slot the PROFILE (the 3D game, ThreeDSenProfiles) it holds a state of. The slots are SHARED by every game: 3dSen
// fills the first free one or the one the user picks, whatever the game. A game's states are therefore the slots whose
// profile is the game's (its own choice in its window, else the profile its title matches - the launch's rule). Two
// LaunchBox games on one profile (two regions of Zelda II) see the same states - as 3dSen does: they are one game to it.
// One row per slot: GameSaveState, Slot 0 = the quick slot, 1..36 the numbered ones (GetPotentialSaveSlots).
//
// 2D MODE (profile 47) IS EVERY GAME WITHOUT A 3D PROFILE (Mehdi 19/10: such a game starts in it): its slots are told
// apart by the ROM inside each state (Cartridge.romName, ThreeDSenSavCfg.RomNameOf - the file's name, an archive's name with
// ".nes" when 3dSen opened the archive itself) against the game's file - a 2D state of another game is not this game's.
//
// NOTHING BESIDE THE GAME: 3dSen never writes beside the ROM ([run], every run of stage 1 and 2). WHERE THE STATES ARE
// (ThreeDSenLaunch.StatesRoot):
//   - with the pack's in-process plugin (portable mode, docs\3dsen.md section 15): <3dSen folder>\Saves\<platform>\States,
//     sav.cfg and the slots side by side, one per platform and per install - so a listing reads each game's platform's;
//   - without it: 3dSen's own folder (LocalLow), shared by every copy of 3dSen of the Windows user (question Q4).
// A state's own root is its folder (ThreeDSenSavCfg.RootOf): active, delete and the slot freed go by it.
//
// THE IDS (Shared.Saves, docs\saves-lb14-routing.md §3): {"emu":"3dsen","kind":"state","by":"profile","id":"<profile id>",
// "slot":<n>} - what 3dSen itself says a state belongs to, stable (a profile id never changes; a rom renamed changes
// nothing). "inst" is added by the desk (docs\saves-two-installs.md): without the plugin every install shares the one
// folder, so both answer the same OwnExpectedDir and the desk takes them for one destination; portable, each its own.
//
// ACTIVE: the file is there AND its slot still holds the card's profile - a slot 3dSen has since reused for another game is
// not this card's live file any more.
//
// RESTORE: refused while any 3dSen runs (it rewrites sav.cfg whole when it quits). The slot: the one picked in LaunchBox,
// else the backup's own (SlotRestore); written: sav\<slot>.sav and that ONE item of sav.cfg (the profile of the game the
// row goes to TODAY, the backup's time, a picture - its thumbnail made 3dSen's 256 x 192 texture, so 3dSen's own Load menu
// shows it, else blank as 3dSen's quick saves are), every other item byte for byte. A slot holding another game's state,
// or another slot of this card, is "another card's file": asked first (SlotRestore.MayOverwrite).
//
// DELETE: the file and its thumbnail, and its slot freed in sav.cfg as 3dSen's own Delete does (gameId emptied).

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using LbIntegrations.Lbip;
using LbIntegrations.Saves;
using Unbroken.LaunchBox.Plugins;
using Unbroken.LaunchBox.Plugins.Data;

namespace LbIntegrations.ThreeDSen
{
    public partial class ThreeDSenPlugin
    {
        /// <summary>The front desk (Shared.Saves SaveDesk): this plugin's emu, and where it logs.</summary>
        private string SaveEmu => Emu;
        private void SaveLog(string message) => Log.Info(message);

        /// <summary>This plugin's "emu" in the ids (Shared.Saves).</summary>
        internal const string Emu = "3dsen";
        private const string StateGroupName = "Save states";
        private const string StateChipText = "STATE";

        public override bool SupportsSaveManagement() => true;

        /// <summary>The quick slot (0) and the 36 numbered ones.</summary>
        public override IReadOnlyDictionary<int, string> GetPotentialSaveSlots()
        {
            var slots = new Dictionary<int, string> { [0] = "Quick" };
            for (int i = 1; i <= ThreeDSenSavCfg.Numbered; i++) slots[i] = i.ToString(CultureInfo.InvariantCulture);
            return slots;
        }

        internal static string StateIdOf(string profileId, int slot)
            => SaveId.Write(new SaveIdData { Emu = Emu, Kind = "state", By = "profile", Id = profileId, Slot = slot });

        private static bool IsOurs(GameSaveBase save) => SaveRows.IsOurs(save?.SaveGroupId, Emu);

        /// <summary>The profile and slot an id of ours names.</summary>
        private static bool Parts(string groupId, out string profile, out int slot)
        {
            profile = null; slot = -1;
            if (!SaveId.TryRead(groupId, out var d) || d == null || d.Emu != Emu || d.Slot == null) return false;
            profile = d.Id; slot = d.Slot.Value;
            return !string.IsNullOrEmpty(profile) && slot >= 0 && slot <= ThreeDSenSavCfg.Numbered;
        }

        // ── what is read once per listing ────────────────────────────────────

        private static List<ThreeDSenSlot> Slots(string data)
        {
            var path = ThreeDSenSavCfg.PathIn(data);
            return SaveScan.Memo("3dsen.sav|" + data, () => StampedCache.Get(path, "3dsen.sav.cfg", () => ThreeDSenSavCfg.Read(path)));
        }

        private static List<ThreeDSenProfile> Profiles(string data)
        {
            var path = Path.Combine(data, ThreeDSenRomJson.FileName);
            return SaveScan.Memo("3dsen.profiles|" + data, () => StampedCache.Get(path, "3dsen.profiles", () => ThreeDSenProfiles.All(data)));
        }

        /// <summary>A game's profile, as a launch takes it: its own choice (still in 3dSen), else its title's match, else 2D
        /// Mode. Null: none.</summary>
        internal static ThreeDSenProfile ProfileOf(IReadOnlyList<ThreeDSenProfile> profiles, string gameId, string title, string rom)
        {
            var chosen = ThreeDSenSettings.GameValue(gameId, ThreeDSenSettings.ProfileKey);
            if (chosen != null) return profiles.FirstOrDefault(p => p.Id == chosen);
            return ThreeDSenProfiles.Match(profiles, title, rom) ?? profiles.FirstOrDefault(p => p.Id == ThreeDSenProfiles.TwoDModeId);
        }

        /// <summary>A state of <paramref name="profile"/> is the game's: any for a 3D profile (3dSen's own rule), for 2D Mode
        /// only one made from the game's file (by its name, with or without its extension - an archive's ROM is named
        /// "&lt;archive&gt;.nes"). A 2D state whose ROM cannot be read is nobody's.</summary>
        internal static bool StateFits(ThreeDSenProfile profile, string state, string rom)
        {
            if (profile?.Id != ThreeDSenProfiles.TwoDModeId) return true;
            var inside = SaveScan.Memo("3dsen.romname|" + state + "|" + SafeStamp(state), () => ThreeDSenSavCfg.RomNameOf(state));
            if (string.IsNullOrEmpty(inside) || string.IsNullOrWhiteSpace(rom)) return false;
            return string.Equals(Path.GetFileNameWithoutExtension(inside), Path.GetFileNameWithoutExtension(rom), StringComparison.OrdinalIgnoreCase);
        }

        private static long SafeStamp(string path) { try { return File.GetLastWriteTimeUtc(path).Ticks; } catch { return 0; } }

        // ── listing ──────────────────────────────────────────────────────────

        GetSavesResponse IOwnSaves.OwnGetSaves(GetSavesArgs args)
        {
            try
            {
                if (args?.Emulator == null) return new GetSavesResponse("No emulator was supplied.");
                var exe = ResolveFullPath(Safe(() => args.Emulator.ApplicationPath));
                if (!ThreeDSenLaunch.IsExe(exe)) return new GetSavesResponse("This emulator is not 3dSen.");
                var found = new List<GameSaveBase>();
                var profiles = Profiles(ThreeDSenLaunch.DataDir(exe));
                var roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var games = args.Games ?? (IReadOnlyCollection<IGame>)Array.Empty<IGame>();
                foreach (var app in args.AdditionalApplications ?? (IReadOnlyCollection<IAdditionalApplication>)Array.Empty<IAdditionalApplication>())
                {
                    var gameId = Safe(() => app.GameId);
                    var game = games.FirstOrDefault(g => Safe(() => g.Id) == gameId) ?? Safe(() => PluginHelper.DataManager?.GetGameById(gameId));
                    Collect(found, seen, roots, exe, Safe(() => game?.Platform), profiles, gameId, Safe(() => game?.Title), Safe(() => app.ApplicationPath),
                            SaveRows.TieTo(Safe(() => app.Id), Safe(() => game?.ApplicationPath), Safe(() => app.ApplicationPath), Full));
                }
                foreach (var game in games)
                    Collect(found, seen, roots, exe, Safe(() => game.Platform), profiles, Safe(() => game.Id), Safe(() => game.Title), Safe(() => game.ApplicationPath), null);
                foreach (var root in roots) { var slots = Slots(root); if (slots != null) ThreeDSenStateThumbs.Orphans(root, slots); }
                Log.Verbose("found " + found.Count + " state(s) for " + exe);
                return new GetSavesResponse(found);
            }
            catch (Exception ex)
            {
                Log.Warn("GetSaves failed", ex);
                return new GetSavesResponse("Could not read 3dSen's save states: " + ex.Message);
            }
        }

        private static void Collect(List<GameSaveBase> into, HashSet<string> seen, HashSet<string> roots, string exe, string platform, List<ThreeDSenProfile> profiles,
                                    string gameId, string title, string romPath, string appId)
        {
            if (string.IsNullOrWhiteSpace(gameId) || string.IsNullOrWhiteSpace(romPath)) return;
            // The states of the game's platform (portable mode), or 3dSen's one folder.
            var data = ThreeDSenLaunch.StatesRoot(exe, platform);
            var slots = Slots(data);
            // Nothing saved by 3dSen yet (or an index it does not read back): no state, and no title matched for nothing.
            if (slots == null || slots.All(s => s.Empty)) return;
            roots.Add(data);
            var profile = ProfileOf(profiles, gameId, title, ResolveFullPath(romPath));
            if (profile == null) return;
            var context = appId ?? gameId;
            var full = ResolveFullPath(romPath);
            foreach (var s in slots.Where(s => s.GameId == profile.Id && s.Number >= 0))
            {
                var path = ThreeDSenSavCfg.StatePath(data, s.SlotId);
                if (!SaveScan.Exists(path) || !StateFits(profile, path, full) || !seen.Add(path + "|" + context)) continue;
                ThreeDSenStateThumbs.Refresh(path, s);
                into.Add(StateRow(path, gameId, appId, s.Number, StateIdOf(profile.Id, s.Number), StateGroupName));
            }
        }

        /// <summary>For the game's window: a game's states, quick first, as "Quick (18/10 17:52)", "slot 5 (...)".</summary>
        internal static List<string> StatesText(string exe, string platform, string gameId, string title, string rom)
        {
            var data = ThreeDSenLaunch.StatesRoot(exe, platform);
            var slots = ThreeDSenSavCfg.Read(ThreeDSenSavCfg.PathIn(data));
            if (slots == null) return new List<string>();
            var profile = ProfileOf(ThreeDSenProfiles.All(ThreeDSenLaunch.DataDir(exe)), gameId, title, rom);
            if (profile == null) return new List<string>();
            return slots.Where(s => s.GameId == profile.Id && s.Number >= 0 && File.Exists(ThreeDSenSavCfg.StatePath(data, s.SlotId))
                                    && StateFits(profile, ThreeDSenSavCfg.StatePath(data, s.SlotId), rom))
                        .OrderBy(s => s.Number)
                        .Select(s => (s.IsQuick ? "Quick" : "slot " + s.SlotId) + " ("
                                     + DateTimeOffset.FromUnixTimeSeconds(Math.Max(0, s.LastTime)).LocalDateTime.ToString("dd/MM HH:mm", CultureInfo.InvariantCulture) + ")")
                        .ToList();
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

        /// <summary>One file each, which the host copies itself.</summary>
        bool IOwnSaves.OwnIsSaveContainer(GameSaveBase save) => false;

        bool IOwnSaves.OwnUseSaveGroupIdForPersistedMatch(GameSaveBase save) => IsOurs(save);

        bool IOwnSaves.OwnTryBackupSave(GameSaveBase save, string emulatorApplicationPath, string destinationFolder, out string error)
        {
            error = "A 3dSen state is one file, not a container.";
            return false;
        }

        /// <summary>A state's thumbnail (sav\5.sav.png) is part of the state, never a save of its own.</summary>
        bool IOwnSaves.OwnIsSecondarySaveFile(string filePath)
            => LbipStateThumbs.IsThumb(filePath) && ThreeDSenSavCfg.NumberOfFile(filePath.Substring(0, filePath.Length - LbipStateThumbs.Suffix.Length)) >= 0;

        /// <summary>A state travels with its thumbnail.</summary>
        IReadOnlyList<string> IOwnSaves.OwnGetCompanionSaveFiles(string primaryFilePath)
        {
            try { return ThreeDSenSavCfg.NumberOfFile(primaryFilePath) >= 0 ? LbipStateThumbs.Companion(primaryFilePath) : Array.Empty<string>(); }
            catch { return Array.Empty<string>(); }
        }

        /// <summary>Active when it is where 3dSen reads it, still there, and its slot still holds the card's profile.</summary>
        bool IOwnSaves.OwnIsSaveActive(GameSaveBase save, string emulatorApplicationPath)
        {
            try
            {
                if (!IsOurs(save) || string.IsNullOrWhiteSpace(save.FileLocation)) return true;
                if (!File.Exists(save.FileLocation)) return false;
                if (!Parts(save.SaveGroupId, out var profile, out var slot)) return true;
                // Live where its slot's file is in its own states root (the platform's, or LocalLow's), and that root is this 3dSen's.
                var data = ThreeDSenSavCfg.RootOf(save.FileLocation);
                var expected = ThreeDSenSavCfg.StatePath(data, ThreeDSenSavCfg.SlotIdOf(slot));
                if (!string.Equals(Path.GetFullPath(save.FileLocation), Path.GetFullPath(expected), StringComparison.OrdinalIgnoreCase)) return false;
                var exe = ResolveFullPath(emulatorApplicationPath);
                if (ThreeDSenLaunch.IsExe(exe) && !IsRootOf(exe, data)) return false;
                var path = ThreeDSenSavCfg.PathIn(data);
                var slots = StampedCache.Get(path, "3dsen.sav.cfg", () => ThreeDSenSavCfg.Read(path));
                var item = slots?.FirstOrDefault(s => s.Number == slot);
                return item == null || item.GameId == profile;
            }
            catch { return true; }
        }

        LbIntegrations.Saves.ExpectedDir IOwnSaves.OwnExpectedDir(IEmulator emulator, GameSaveBase save)
        {
            var path = Safe(() => emulator?.ApplicationPath);
            if (save == null || !ThreeDSenLaunch.IsExe(path)) return LbIntegrations.Saves.ExpectedDir.Unknown("Could not find the 3dSen this game runs on.");
            // Portable: this install's folder of the game's platform; else every copy of 3dSen of the Windows user keeps its
            // states in the one folder.
            var game = Safe(() => PluginHelper.DataManager?.GetGameById(save.GameId));
            return LbIntegrations.Saves.ExpectedDir.Folder(ThreeDSenSavCfg.StateDir(ThreeDSenLaunch.StatesRoot(ResolveFullPath(path), Safe(() => game?.Platform))));
        }

        /// <summary>A states root is this 3dSen's: LocalLow's when it is not portable, else one of its own Saves\&lt;platform&gt;\States.</summary>
        internal static bool IsRootOf(string exe, string root)
        {
            try
            {
                var r = Path.GetFullPath(root).TrimEnd('\\');
                if (!ThreeDSenLaunch.Portable(exe)) return string.Equals(r, Path.GetFullPath(ThreeDSenLaunch.DataDir()).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);
                var saves = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(exe)), ThreeDSenLaunch.SavesFolder)).TrimEnd('\\');
                return string.Equals(Path.GetFileName(r), ThreeDSenLaunch.StatesFolder, StringComparison.OrdinalIgnoreCase)
                       && string.Equals(Path.GetDirectoryName(Path.GetDirectoryName(r)), saves, StringComparison.OrdinalIgnoreCase);
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
                if (!(save is GameSaveState state)) return new AddSaveResponse("3dSen keeps no cartridge save - only save states can be restored to it.");
                var source = save.FileLocation;
                if (string.IsNullOrWhiteSpace(source) || !File.Exists(source)) return new AddSaveResponse("This 3dSen backup is not a file: " + source);

                var dm = PluginHelper.DataManager;
                var to = SaveRows.TargetFor(save, dm, Full);
                if (to.Warning != null) Log.Warn("restore of " + save.OriginalFileName + ": " + to.Warning);
                if (to.Refused) return new AddSaveResponse(to.Warning);
                var game = Safe(() => dm?.GetGameById(save.GameId));
                var exe = ExecutableOf(game);
                if (!ThreeDSenLaunch.IsExe(exe)) return new AddSaveResponse("Could not find the 3dSen this game runs on.");
                var data = ThreeDSenLaunch.StatesRoot(exe, Safe(() => game?.Platform));
                var profile = ProfileOf(ThreeDSenProfiles.All(ThreeDSenLaunch.DataDir(exe)), save.GameId, Safe(() => game?.Title), to.RomPath);
                if (profile == null)
                    return new AddSaveResponse("3dSen has no profile for this game (not even its 2D Mode), so a state cannot be given to it. Choose its profile first: right-click the game > Nixx-3DSen : Options...");
                var thumb = GetCompanionSaveFiles(source).FirstOrDefault(LbipStateThumbs.IsThumb);
                var refused = Restore(data, source, thumb, state.Slot, save.SaveGroupId, profile.Id, IsOurs(save), save,
                                      () => { try { return args.ShouldOverwriteFunc?.Invoke() ?? true; } catch { return true; } }, out var target, out var slot);
                if (refused != null) { Log.Info("restore of " + save.OriginalFileName + " refused: " + refused); return new AddSaveResponse(refused); }
                return new AddSaveResponse(StateRow(target, state.GameId, state.AdditionalApplicationId, slot, SlotRestore.WithSlot(state.SaveGroupId, slot),
                                                    string.IsNullOrWhiteSpace(state.SaveGroupName) ? StateGroupName : state.SaveGroupName));
            }
            catch (Exception ex)
            {
                Log.Warn("AddSaveFile failed", ex);
                return new AddSaveResponse("Could not restore this 3dSen state: " + ex.Message);
            }
        }

        /// <summary>The restore's work, host-free (the probe calls it): <paramref name="source"/> (and its thumbnail) into the
        /// slot picked - or the backup's own - of <paramref name="data"/>, given to <paramref name="profileId"/>. Null when done,
        /// else why not.</summary>
        internal static string Restore(string data, string source, string thumb, int? picked, string groupId, string profileId, bool ours, GameSaveBase save,
                                       Func<bool> shouldOverwrite, out string target, out int slot)
        {
            target = null; slot = -1;
            if (ThreeDSenLaunch.AnyRunning())
                return "3dSen is running: it writes its list of states when it quits, which would undo this restore. Close it first.";
            Parts(groupId, out _, out var own);
            if (own < 0) own = ThreeDSenSavCfg.NumberOfFile(source);
            var (s, no) = SlotRestore.Choose(picked, own < 0 ? (int?)null : own, Enumerable.Range(0, ThreeDSenSavCfg.Numbered + 1).ToList(), "3dSen");
            if (no != null) return no;
            slot = s;

            var cfgPath = ThreeDSenSavCfg.PathIn(data);
            List<ThreeDSenSlot> slots;
            if (File.Exists(cfgPath))
            {
                slots = ThreeDSenSavCfg.Read(cfgPath);
                if (!ThreeDSenSavCfg.Writable(slots))
                    return "3dSen's list of states (" + cfgPath + ") is not as 3dSen 1.1.2 writes it, so it was left alone and nothing was restored.";
            }
            else slots = ThreeDSenSavCfg.Fresh();
            var slotId = ThreeDSenSavCfg.SlotIdOf(slot);
            var item = slots.FirstOrDefault(x => x.SlotId == slotId);
            if (item == null) { item = new ThreeDSenSlot { SlotId = slotId }; slots.Add(item); }
            target = ThreeDSenSavCfg.StatePath(data, slotId);
            var what = slot == 0 ? "The quick slot" : "Slot " + slotId;

            // Another card's live file: another game's state in that slot, or another slot than the backup's own.
            bool otherCard = !ours || slot != own || (!item.Empty && item.GameId != profileId) || SlotRestore.OverAnotherFile(target, save);
            if (!SlotRestore.MayOverwrite(target, source, otherCard, what, Log.Info, out var refused)) return refused;
            if (File.Exists(target) && !(shouldOverwrite?.Invoke() ?? true)) return "Restore cancelled.";
            var cannot = SaveCompanions.WhyNotWritable(source, target, null);
            if (cannot != null) return cannot;

            // The slot's item: this game's profile, the backup's time, its picture.
            byte[] pixels;
            Image picture = null;
            try { if (thumb != null && File.Exists(thumb)) using (var ms = new MemoryStream(File.ReadAllBytes(thumb))) picture = new Bitmap(ms); } catch { picture = null; }
            using (picture) pixels = ThreeDSenSavCfg.PixelsOf(picture);
            item.GameId = profileId;
            item.LastTime = new DateTimeOffset(File.GetLastWriteTimeUtc(source), TimeSpan.Zero).ToUnixTimeSeconds();
            item.Width = ThreeDSenSavCfg.ThumbWidth; item.Height = ThreeDSenSavCfg.ThumbHeight; item.Format = ThreeDSenSavCfg.Rgb24;
            item.Pixels = pixels;
            item.Raw = null;

            Directory.CreateDirectory(ThreeDSenSavCfg.StateDir(data));
            var part = target + ".lbip-part";
            File.Copy(source, part, overwrite: true);
            File.Move(part, target, overwrite: true);
            ThreeDSenSavCfg.Save(cfgPath, slots);
            Log.Info("restored " + Path.GetFileName(source) + " -> " + target + " (profile " + profileId + ")");

            // Its thumbnail with it; without one, the old one would be a picture of another moment.
            if (thumb != null && File.Exists(thumb)) File.Copy(thumb, LbipStateThumbs.PathOf(target), overwrite: true);
            else LbipStateThumbs.Remove(target);
            return null;
        }

        /// <summary>The 3dSen a restore goes to: the install it was sent to (SaveForward.WithVia), else the game's emulator,
        /// else the first 3dSen LaunchBox has.</summary>
        private static string ExecutableOf(IGame game)
        {
            try
            {
                var dm = PluginHelper.DataManager;
                var id = Safe(() => game?.EmulatorId);
                var own = LbIntegrations.Saves.SaveForward.ViaEmulator(ThreeDSenLaunch.IsExe) ?? (string.IsNullOrEmpty(id) ? null : dm?.GetEmulatorById(id));
                var path = Safe(() => own?.ApplicationPath);
                if (!ThreeDSenLaunch.IsExe(path))
                    path = Safe(() => dm?.GetAllEmulators()?.FirstOrDefault(e => ThreeDSenLaunch.IsExe(Safe(() => e.ApplicationPath)))?.ApplicationPath);
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
                // The slot is freed in the sav.cfg beside the file (its platform's, or LocalLow's).
                var loc = save?.FileLocation;
                var why = Delete(string.IsNullOrWhiteSpace(loc) ? ThreeDSenLaunch.DataDir() : ThreeDSenSavCfg.RootOf(loc), loc, save?.SaveGroupId);
                return why == null ? new PluginResponse(true) : new PluginResponse(false, why);
            }
            catch (Exception ex)
            {
                Log.Warn("RemoveSave failed", ex);
                return new PluginResponse(false, "Could not delete this 3dSen state: " + ex.Message);
            }
        }

        /// <summary>The delete's work, host-free: the file, its thumbnail, and its slot freed in sav.cfg when it still holds the
        /// card's profile (3dSen's own SaveConfig.Delete empties gameId). Null when done.</summary>
        internal static string Delete(string data, string loc, string groupId)
        {
            if (string.IsNullOrWhiteSpace(loc)) return "This save has no location.";
            bool live = string.Equals(Path.GetDirectoryName(Path.GetFullPath(loc)), Path.GetFullPath(ThreeDSenSavCfg.StateDir(data)), StringComparison.OrdinalIgnoreCase);
            if (live && ThreeDSenLaunch.AnyRunning()) return "3dSen is running: close it first - it keeps its list of states in memory.";
            try { if (File.Exists(loc)) File.Delete(loc); }
            catch (Exception ex) { Log.Warn("could not delete " + loc, ex); return "Could not delete " + Path.GetFileName(loc) + "."; }
            LbipStateThumbs.Remove(loc);
            if (live && Parts(groupId, out var profile, out var slot))
            {
                var cfgPath = ThreeDSenSavCfg.PathIn(data);
                var slots = ThreeDSenSavCfg.Read(cfgPath);
                var item = slots?.FirstOrDefault(s => s.Number == slot);
                if (ThreeDSenSavCfg.Writable(slots) && item != null && item.GameId == profile)
                {
                    item.GameId = ""; item.Raw = null;
                    ThreeDSenSavCfg.Save(cfgPath, slots);
                }
            }
            Log.Info("deleted " + loc);
            return null;
        }

        /// <summary>A path as LaunchBox stores it, made full; null for nothing.</summary>
        private static string Full(string path) => string.IsNullOrWhiteSpace(path) ? null : ResolveFullPath(path);

        private static T Safe<T>(Func<T> read)
        {
            try { return read(); } catch { return default; }
        }
    }
}
