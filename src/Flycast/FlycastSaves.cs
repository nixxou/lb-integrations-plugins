// Save management for Flycast.
//
// THREE KINDS, and they do not share a rule. Every one of them is a FILE - there is no container
// here, unlike the PSP where a save is a set of directories, so IsSaveContainer says false
// throughout and the host copies the files itself.
//
//   Dreamcast VMU   data\<gameId>_vmu_save_A1.bin
//                   Identity is the disc's IP.BIN product number, read from the ROM. Flycast
//                   sanitises it for the file name; we reproduce that exactly (Ipbin.cs).
//
//   Arcade set      data\<rom file name>.nvmem  and siblings sharing that stem
//                   Identity is the ROM's FILE NAME, extension included - settings.content.fileName
//                   is hostfs FileInfo.name (core/emulator.cpp:575). No binary is read at all.
//
//   Save states     data\<rom file name without extension>[_1..9].state
//                   Identity is the file name again, this time WITHOUT the extension
//                   (get_file_basename, core/stdclass.h:135).
//
// So a Dreamcast game carries TWO identities: its disc id for the save, its file name for the
// states. That is not our design, it is Flycast's, and pretending otherwise would break one of them.
//
// WHAT WE DELIBERATELY DO NOT TOUCH, decided with the user on 2026-09-21:
//
//  * The seven SHARED VMUs - vmu_save_A2.bin through vmu_save_D2.bin. Flycast names only port A1
//    per game (core/oslib/oslib.cpp, getVmuPath); every other expansion slot is one file shared by
//    the whole library, like a real memory card carried between games. Offering one as "this game's
//    save" would mean a restore wipes every other game's data. They are ignored, and the log says so
//    when a non-empty one exists, so nobody has to wonder where their data went.
//  * The per-MACHINE files that live in the same folder: dc_nvmem.bin, naomi_nvmem.bin, dc_boot.bin
//    and friends (getRomPrefix, core/hw/flashrom/nvmem.cpp). They are console state and BIOS, not
//    game saves. IsSecondarySaveFile keeps them out.
//
// THE ARCHIVE NAME IS FLYCAST'S OWN. A vault copy keeps the name the emulator uses, so what we
// restore is bit for bit what we backed up and there is no translation table to maintain. Argosy
// names the same Dreamcast save <gameId>.A1.bin, so a future RomM exchange will need a rename - a
// known, declared cost, and RomM is out of scope here.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Unbroken.LaunchBox.Plugins;
using Unbroken.LaunchBox.Plugins.Data;

namespace LbIntegrations.Flycast
{
    public partial class FlycastPlugin
    {
        private const string VmuPrefix = "flycast-vmu:";
        private const string ArcadePrefix = "flycast-arcade:";
        private const string StatePrefix = "flycast-state:";

        private const string VmuChipText = "VMU";
        private const string ArcadeChipText = "NVRAM";
        private const string StateChipText = "Save State";
        private const string StateGroupName = "My Save State";

        /// <summary>Flycast's own suffix for the per-game VMU of port A1.</summary>
        internal const string VmuSuffix = "_vmu_save_A1.bin";

        /// <summary>Every suffix Flycast appends to the arcade stem, in the order we prefer them as the
        /// PRIMARY file of the set. Sources: core/hw/flashrom/nvmem.cpp (.nvmem, -main.nvmem, .nvmem2),
        /// core/hw/maple/maple_jvs.cpp (.eeprom, -main.eeprom, -p&lt;N&gt;.card), core/hw/naomi/
        /// card_reader.cpp (.card), hopper.cpp (-hopper.bin) and systemsp.cpp (the -jp/-us/-exp region
        /// infix). The region and player variants are matched by pattern, not listed.</summary>
        private static readonly string[] ArcadeSuffixes =
        {
            ".nvmem", "-main.nvmem", ".nvmem2",
            ".eeprom", "-main.eeprom",
            ".card", "-hopper.bin",
        };

        /// <summary>Region infixes System SP inserts before .eeprom (core/hw/naomi/systemsp.cpp).</summary>
        private static readonly string[] ArcadeRegions = { "-jp", "-us", "-exp" };

        internal const string StateExtension = ".state";

        public override bool SupportsSaveManagement() => true;

        /// <summary>Flycast cycles ten state slots: `SavestateSlot = (SavestateSlot + 10 + step) % 10`
        /// in core/ui/gui.cpp. Slot 0 is written without a suffix.</summary>
        public override IReadOnlyDictionary<int, string> GetPotentialSaveSlots()
        {
            var slots = new Dictionary<int, string>();
            for (int i = 0; i < 10; i++)
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
                if (!FlycastPaths.IsFlycastExecutable(appPath))
                    return new GetSavesResponse("This emulator is not Flycast.");

                var layout = FlycastPaths.Resolve(appPath);
                if (string.IsNullOrEmpty(layout.InstallDir))
                    return new GetSavesResponse(new List<GameSaveBase>());

                var found = new List<GameSaveBase>();
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                foreach (var app in args.AdditionalApplications
                         ?? (IReadOnlyCollection<IAdditionalApplication>)Array.Empty<IAdditionalApplication>())
                {
                    var game = SafeGame(Safe(() => app.GameId));
                    Collect(found, seen, layout, Safe(() => app.ApplicationPath), game, app);
                }
                foreach (var game in args.Games ?? (IReadOnlyCollection<IGame>)Array.Empty<IGame>())
                    Collect(found, seen, layout, Safe(() => game.ApplicationPath), game, null);

                WarnAboutSharedVmus(layout);

                Log.Info("found " + found.Count + " save(s) under " + layout.DataDir);
                return new GetSavesResponse(found);
            }
            catch (Exception ex)
            {
                Log.Warn("GetSaves failed", ex);
                return new GetSavesResponse("Could not read Flycast saves: " + ex.Message);
            }
        }

        /// <summary>Everything one game owns: its VMU or its arcade set, plus its states.</summary>
        private void Collect(List<GameSaveBase> into, HashSet<string> seen, FlycastLayout layout,
                             string romPath, IGame game, IAdditionalApplication app)
        {
            if (game == null || string.IsNullOrWhiteSpace(romPath)) return;

            string gameId = Safe(() => game.Id) ?? "";
            string appId = app != null ? Safe(() => app.Id) : null;
            string context = appId ?? gameId;

            string fileName = SafeFileName(romPath);
            if (fileName == null) return;
            string stem = StripExtension(fileName);

            // The Dreamcast save, when this ROM is a disc we can read an id out of.
            var product = FlycastGameId.LooksLikeDisc(romPath) ? FlycastGameId.Of(romPath) : null;
            if (product != null)
            {
                var vmuName = Ipbin.SanitizeForFileName(product) + VmuSuffix;
                var vmuPath = FlycastPaths.FirstExisting(layout.VmuDirs, vmuName);
                if (vmuPath != null && seen.Add("vmu|" + product + "|" + context))
                    into.Add(Row(vmuPath, gameId, appId, VmuPrefix + product,
                                 "Visual Memory Unit", VmuChipText));
            }

            // The arcade set: the primary first, companions follow it through GetCompanionSaveFiles.
            var arcade = ArcadeMembers(layout, fileName);
            if (arcade.Count > 0 && seen.Add("arcade|" + fileName + "|" + context))
                into.Add(Row(arcade[0], gameId, appId, ArcadePrefix + fileName,
                             "Cartridge memory", ArcadeChipText));

            // States, one row per occupied slot.
            foreach (var state in States(layout, stem))
            {
                if (!seen.Add("state|" + stem + "|" + state.Slot + "|" + context)) continue;
                into.Add(new GameSaveState
                {
                    GameId = gameId,
                    AdditionalApplicationId = appId,
                    FileLocation = state.Path,
                    OriginalFileName = Path.GetFileName(state.Path),
                    Slot = state.Slot,
                    SaveGroupId = StatePrefix + stem + ":" + state.Slot,
                    SaveGroupName = StateGroupName,
                    DisplayChipText = StateChipText,
                    ReportedFileSizeBytes = state.SizeBytes > 0 ? state.SizeBytes : (long?)null,
                    ReportedLastModifiedUtc = state.LastWriteUtc == default ? (DateTime?)null : state.LastWriteUtc,
                });
            }
        }

        private static GameSaveGame Row(string path, string gameId, string appId,
                                        string groupId, string groupName, string chip)
        {
            long size = 0; DateTime when = default;
            try { var i = new FileInfo(path); size = i.Length; when = i.LastWriteTimeUtc; } catch { }

            return new GameSaveGame
            {
                GameId = gameId,
                AdditionalApplicationId = appId,
                FileLocation = path,
                OriginalFileName = Path.GetFileName(path),
                SaveGroupId = groupId,
                SaveGroupName = groupName,
                DisplayChipText = chip,
                ReportedFileSizeBytes = size > 0 ? size : (long?)null,
                ReportedLastModifiedUtc = when == default ? (DateTime?)null : when,
            };
        }

        // ── the arcade set ───────────────────────────────────────────────────

        /// <summary>Every file of one arcade game's set that exists, primary first.</summary>
        private static List<string> ArcadeMembers(FlycastLayout layout, string fileName)
        {
            var members = new List<string>();
            foreach (var suffix in ArcadeSuffixes)
            {
                var hit = FlycastPaths.FirstExisting(layout.ArcadeDirs, fileName + suffix);
                if (hit != null) members.Add(hit);
            }
            foreach (var region in ArcadeRegions)
            {
                var hit = FlycastPaths.FirstExisting(layout.ArcadeDirs, fileName + region + ".eeprom");
                if (hit != null) members.Add(hit);
            }
            // -p<N>.card, one per player. Four ports is the hardware limit.
            for (int player = 1; player <= 4; player++)
            {
                var hit = FlycastPaths.FirstExisting(layout.ArcadeDirs, fileName + "-p" + player + ".card");
                if (hit != null) members.Add(hit);
            }
            return members;
        }

        /// <summary>Every concrete arcade suffix, LONGEST FIRST.
        ///
        /// The order is the whole point and it was a real defect before the probe caught it:
        /// "crzytaxi.zip-p1.card" ends with ".card" as surely as it ends with "-p1.card", so a list
        /// tried in declaration order cut the stem at "crzytaxi.zip-p1" and then found no set to
        /// attach it to. Matching the longest suffix first removes the ambiguity for every pair that
        /// nests: -main.nvmem inside .nvmem, -main.eeprom and -jp.eeprom inside .eeprom, -p1.card
        /// inside .card.</summary>
        private static readonly string[] ArcadeSuffixesLongestFirst = BuildAllSuffixes();

        private static string[] BuildAllSuffixes()
        {
            var all = new List<string>(ArcadeSuffixes);
            foreach (var region in ArcadeRegions) all.Add(region + ".eeprom");
            for (int player = 1; player <= 4; player++) all.Add("-p" + player + ".card");
            all.Sort((a, b) => b.Length.CompareTo(a.Length));
            return all.ToArray();
        }

        /// <summary>The arcade stem a member file belongs to, or null when it is not one of ours.</summary>
        private static string ArcadeStemOf(string path)
        {
            var name = Path.GetFileName(path ?? "");
            if (string.IsNullOrEmpty(name)) return null;

            foreach (var suffix in ArcadeSuffixesLongestFirst)
                if (name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                {
                    var stem = name.Substring(0, name.Length - suffix.Length);
                    // A file that is ONLY a suffix has no stem and belongs to no game.
                    return stem.Length == 0 ? null : stem;
                }
            return null;
        }

        // ── save states ──────────────────────────────────────────────────────

        private readonly struct StateFile
        {
            public StateFile(string path, int slot, long size, DateTime when)
            { Path = path; Slot = slot; SizeBytes = size; LastWriteUtc = when; }
            public string Path { get; }
            public int Slot { get; }
            public long SizeBytes { get; }
            public DateTime LastWriteUtc { get; }
        }

        /// <summary>The occupied state slots of one game, ordered. Slot 0 has no suffix; 1..9 carry
        /// "_&lt;n&gt;". The ".state.net" and ".state.tmp" files are NOT slots - they are the netplay
        /// and temporary states (index -1 and -2 in getSavestatePath) - and are excluded by requiring
        /// the name to END at .state.</summary>
        private static IEnumerable<StateFile> States(FlycastLayout layout, string stem)
        {
            var found = new List<StateFile>();
            if (string.IsNullOrEmpty(stem)) return found;

            for (int slot = 0; slot < 10; slot++)
            {
                var name = stem + (slot == 0 ? "" : "_" + slot) + StateExtension;
                var path = FlycastPaths.FirstExisting(layout.StateDirs, name);
                if (path == null) continue;
                long size = 0; DateTime when = default;
                try { var i = new FileInfo(path); size = i.Length; when = i.LastWriteTimeUtc; } catch { }
                found.Add(new StateFile(path, slot, size, when));
            }
            return found;
        }

        /// <summary>The stem and slot behind a state file name, or null.</summary>
        private static (string Stem, int Slot)? StateKeyOfName(string fileName)
        {
            if (string.IsNullOrEmpty(fileName)) return null;
            if (!fileName.EndsWith(StateExtension, StringComparison.OrdinalIgnoreCase)) return null;

            var body = fileName.Substring(0, fileName.Length - StateExtension.Length);
            int underscore = body.LastIndexOf('_');
            if (underscore > 0 && underscore == body.Length - 2
                && char.IsDigit(body[body.Length - 1]))
                return (body.Substring(0, underscore), body[body.Length - 1] - '0');
            return (body, 0);
        }

        // ── the shared cards we refuse to claim ──────────────────────────────

        /// <summary>Every expansion slot other than A1, which Flycast shares across the whole library.</summary>
        private static readonly string[] SharedVmuNames =
        {
            "vmu_save_A1.bin", "vmu_save_A2.bin",
            "vmu_save_B1.bin", "vmu_save_B2.bin",
            "vmu_save_C1.bin", "vmu_save_C2.bin",
            "vmu_save_D1.bin", "vmu_save_D2.bin",
        };

        /// <summary>Say once per scan when a shared card holds data. It will never appear in the saves
        /// list - that is the decision - but a user whose save is in one deserves a trail to follow
        /// rather than the silent impression that it was lost.</summary>
        private static void WarnAboutSharedVmus(FlycastLayout layout)
        {
            try
            {
                var occupied = SharedVmuNames
                    .Select(n => FlycastPaths.FirstExisting(layout.VmuDirs, n))
                    .Where(p => p != null && new FileInfo(p).Length > 0)
                    .Select(Path.GetFileName)
                    .ToList();
                if (occupied.Count > 0)
                    Log.Info("shared memory cards hold data and are NOT listed as game saves ("
                             + string.Join(", ", occupied)
                             + "): Flycast names only port A1 per game, the rest belong to the machine");
            }
            catch { }
        }

        // ── the contract ─────────────────────────────────────────────────────

        /// <summary>Nothing here is a container: a VMU is one file, an arcade set is sibling files, a
        /// state is one file. Saying otherwise would make the host ask TryBackupSave to extract
        /// something that does not exist.</summary>
        public override bool IsSaveContainer(GameSaveBase save) => false;

        public override bool UseSaveGroupIdForPersistedMatch(GameSaveBase save) => IsOurs(save);

        /// <summary>Guarded, not decorative: the host should never reach this, and if it does the honest
        /// answer is that there is nothing to extract.</summary>
        public override bool TryBackupSave(GameSaveBase save, string emulatorApplicationPath,
                                           string destinationFolder, out string error)
        {
            error = "A Flycast save is a plain file, not a container.";
            return false;
        }

        /// <summary>What must never become a save of its own.
        ///
        /// Three families, all of which sit in the very folder we list saves from:
        ///   - the shared memory cards, vmu_save_&lt;port&gt;.bin
        ///   - the per-machine NVRAM and BIOS, dc_nvmem.bin / naomi_boot.bin / ...
        ///   - the netplay and temporary states, .state.net and .state.tmp
        /// and, within an arcade set, every member that is not the primary.</summary>
        public override bool IsSecondarySaveFile(string filePath)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(filePath)) return false;
                var name = Path.GetFileName(filePath);
                if (string.IsNullOrEmpty(name)) return false;

                if (SharedVmuNames.Any(n => string.Equals(n, name, StringComparison.OrdinalIgnoreCase)))
                    return true;
                if (IsMachineFile(name)) return true;
                if (name.EndsWith(".state.net", StringComparison.OrdinalIgnoreCase)
                    || name.EndsWith(".state.tmp", StringComparison.OrdinalIgnoreCase)) return true;

                // An arcade member that is not the primary of its set.
                var stem = ArcadeStemOf(filePath);
                if (stem != null)
                {
                    var dir = Path.GetDirectoryName(filePath) ?? "";
                    var primary = ArcadeSuffixes
                        .Select(s => Path.Combine(dir, stem + s))
                        .FirstOrDefault(File.Exists);
                    return primary != null
                           && !string.Equals(primary, Path.GetFullPath(filePath), StringComparison.OrdinalIgnoreCase)
                           && !string.Equals(Path.GetFileName(primary), name, StringComparison.OrdinalIgnoreCase);
                }
                return false;
            }
            catch { return false; }
        }

        /// <summary>A file Flycast keeps per MACHINE, named by getRomPrefix (core/hw/flashrom/nvmem.cpp):
        /// dc_, naomi_, naomi2_, aw_, systemsp_ followed by nvmem.bin, boot.bin, bios.bin or flash.bin.</summary>
        private static bool IsMachineFile(string name)
        {
            string[] prefixes = { "dc_", "naomi_", "naomi2_", "aw_", "systemsp_", "naomidev_" };
            string[] tails = { "nvmem.bin", "boot.bin", "bios.bin", "flash.bin", "nvmem2.bin" };
            foreach (var p in prefixes)
                if (name.StartsWith(p, StringComparison.OrdinalIgnoreCase))
                    foreach (var t in tails)
                        if (name.EndsWith(t, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        /// <summary>The rest of an arcade set. A VMU and a state have no companions: one file each,
        /// and a state carries its screenshot INSIDE itself (the FLYSAVE1 header holds the PNG), so
        /// there is nothing beside it to carry.</summary>
        public override IReadOnlyList<string> GetCompanionSaveFiles(string primaryFilePath)
        {
            try
            {
                var stem = ArcadeStemOf(primaryFilePath);
                if (stem == null) return Array.Empty<string>();

                var dir = Path.GetDirectoryName(Path.GetFullPath(primaryFilePath)) ?? "";
                var layout = new FlycastLayout();
                layout.ArcadeDirs.Add(dir);

                var members = ArcadeMembers(layout, stem);
                var primary = Path.GetFullPath(primaryFilePath);
                return members
                    .Where(m => !string.Equals(Path.GetFullPath(m), primary, StringComparison.OrdinalIgnoreCase))
                    .ToList();
            }
            catch { return Array.Empty<string>(); }
        }

        /// <summary>Is this save in the folder the emulator is actually configured with? Each kind is
        /// judged against its own list, because emu.cfg can redirect them independently.</summary>
        public override bool IsSaveActive(GameSaveBase save, string emulatorApplicationPath)
        {
            try
            {
                if (!IsOurs(save) || string.IsNullOrWhiteSpace(emulatorApplicationPath)) return true;
                var loc = save.FileLocation;
                if (string.IsNullOrWhiteSpace(loc)) return true;

                var layout = FlycastPaths.Resolve(emulatorApplicationPath);
                var dirs = save is GameSaveState ? layout.StateDirs
                         : StartsWith(save.SaveGroupId, ArcadePrefix) ? layout.ArcadeDirs
                         : layout.VmuDirs;

                var full = Path.GetFullPath(loc);
                return dirs.Any(d => full.StartsWith(d, StringComparison.OrdinalIgnoreCase));
            }
            catch { return true; }
        }

        // ── restore ──────────────────────────────────────────────────────────

        /// <summary>Put a vault copy back where Flycast reads it.
        ///
        /// The vault keeps Flycast's own file name, so the target name is simply that name in the
        /// right folder - no translation, and what lands is what was taken. Which folder depends on
        /// the kind, and the kind is carried by the group id.</summary>
        public override AddSaveResponse AddSaveFile(AddSaveArgs args)
        {
            try
            {
                var save = args?.SaveToAdd;
                if (save == null) return new AddSaveResponse("No save was supplied.");

                var source = save.FileLocation;
                if (string.IsNullOrWhiteSpace(source) || !File.Exists(source))
                    return new AddSaveResponse("This Flycast backup is not a file: " + (source ?? "(none)"));

                var layout = LayoutFor(save);
                if (layout == null || string.IsNullOrEmpty(layout.InstallDir))
                    return new AddSaveResponse("Could not locate the Flycast installation for this game.");

                string name = FirstNonBlank(save.OriginalFileName, Path.GetFileName(source));
                var dirs = save is GameSaveState ? layout.StateDirs
                         : StartsWith(save.SaveGroupId, ArcadePrefix) ? layout.ArcadeDirs
                         : layout.VmuDirs;
                string targetDir = dirs.FirstOrDefault() ?? layout.DataDir;
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
                        OriginalFileName = name,
                        Slot = state.Slot,
                        SaveGroupId = state.SaveGroupId,
                        SaveGroupName = string.IsNullOrWhiteSpace(state.SaveGroupName) ? StateGroupName : state.SaveGroupName,
                        DisplayChipText = StateChipText,
                    });

                return new AddSaveResponse(Row(target, save.GameId, save.AdditionalApplicationId,
                                               save.SaveGroupId, save.SaveGroupName,
                                               save.DisplayChipText ?? VmuChipText));
            }
            catch (Exception ex)
            {
                Log.Warn("AddSaveFile failed", ex);
                return new AddSaveResponse("Could not restore this Flycast save: " + ex.Message);
            }
        }

        // ── delete ───────────────────────────────────────────────────────────

        /// <summary>Delete the save. For an arcade set that means the whole set: the SDK default would
        /// remove the primary and orphan its siblings in the same folder.</summary>
        public override PluginResponse RemoveSave(GameSaveBase save)
        {
            try
            {
                if (!IsOurs(save)) return base.RemoveSave(save);

                var loc = save?.FileLocation;
                if (string.IsNullOrWhiteSpace(loc)) return new PluginResponse(false, "This save has no location.");

                var targets = new List<string> { loc };
                targets.AddRange(GetCompanionSaveFiles(loc));

                int removed = 0;
                foreach (var path in targets)
                {
                    try { if (File.Exists(path)) { File.Delete(path); removed++; } }
                    catch (Exception ex) { Log.Warn("could not delete " + path, ex); }
                }
                Log.Info("deleted " + Path.GetFileName(loc) + ": " + removed + " file(s)");
                return new PluginResponse(true);
            }
            catch (Exception ex)
            {
                Log.Warn("RemoveSave failed", ex);
                return new PluginResponse(false, "Could not delete this Flycast save: " + ex.Message);
            }
        }

        // ── plumbing ─────────────────────────────────────────────────────────

        private static bool IsOurs(GameSaveBase save)
            => save?.SaveGroupId != null
               && (StartsWith(save.SaveGroupId, VmuPrefix)
                   || StartsWith(save.SaveGroupId, ArcadePrefix)
                   || StartsWith(save.SaveGroupId, StatePrefix));

        private static bool StartsWith(string value, string prefix)
            => value != null && value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);

        /// <summary>The Flycast installation behind a save row. AddSaveArgs carries no emulator, so it
        /// is re-resolved through the public data manager: the game's own emulator first, then any
        /// Flycast the library knows.</summary>
        private static FlycastLayout LayoutFor(GameSaveBase save)
        {
            try
            {
                var dm = PluginHelper.DataManager;
                var game = dm?.GetGameById(save.GameId);
                var emuId = Safe(() => game?.EmulatorId);
                var emu = string.IsNullOrEmpty(emuId) ? null : dm.GetEmulatorById(emuId);
                var appPath = Safe(() => emu?.ApplicationPath);
                if (FlycastPaths.IsFlycastExecutable(appPath)) return FlycastPaths.Resolve(appPath);

                var any = dm?.GetAllEmulators()?
                    .FirstOrDefault(e => FlycastPaths.IsFlycastExecutable(Safe(() => e.ApplicationPath)));
                var anyPath = Safe(() => any?.ApplicationPath);
                return anyPath == null ? null : FlycastPaths.Resolve(anyPath);
            }
            catch (Exception ex) { Log.Warn("could not resolve the Flycast installation", ex); return null; }
        }

        private static string SafeFileName(string path)
        {
            try { return Path.GetFileName(path); } catch { return null; }
        }

        /// <summary>Everything before the LAST dot, matching Flycast's get_file_basename
        /// (core/stdclass.h:135) rather than Path.GetFileNameWithoutExtension - they agree today but
        /// the emulator's rule is the one that decides the file name.</summary>
        private static string StripExtension(string fileName)
        {
            if (string.IsNullOrEmpty(fileName)) return fileName;
            int dot = fileName.LastIndexOf('.');
            return dot < 0 ? fileName : fileName.Substring(0, dot);
        }

        private static string FirstNonBlank(params string[] candidates)
            => candidates?.FirstOrDefault(c => !string.IsNullOrWhiteSpace(c));

        /// <summary>The game an additional application belongs to, through the public data manager.</summary>
        private static IGame SafeGame(string gameId)
        {
            if (string.IsNullOrEmpty(gameId)) return null;
            try { return PluginHelper.DataManager?.GetGameById(gameId); }
            catch (Exception ex) { Log.Warn("could not resolve game " + gameId, ex); return null; }
        }
    }
}
