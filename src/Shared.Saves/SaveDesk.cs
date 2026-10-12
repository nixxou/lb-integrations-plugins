// The front desk in front of a Nixx plugin's own save code (lot 3, docs\saves-lb14-routing.md §4b).
//
// LaunchBox calls the plugin's overrides (a generated <Plugin>SaveDesk.cs in each plugin folder); each one hands the
// call to this desk, which sends a row that is not the plugin's to its owner and gives the plugin's rows to its own
// code (IOwnSaves, the plugin's save code as it was, renamed Own*). GetSaves adds the saves the other emulators of the
// library keep for the game.
//
// NOT A BASE CLASS (measured 08/10): with the plugins deriving from an abstract NixxSavePlugin, LaunchBox 14 loaded
// their DLLs and constructed none of them - no "plugin constructed" line in any log, the probe constructing the same
// DLL fine. A plugin class must derive from EmulatorPlugin directly, and the assembly must hold no other public
// EmulatorPlugin type.
//
// IsSaveActive is called hundreds of times: the plugin's own ids are told by a prefix (SaveId writes "emu" first),
// and the route of any other id is kept once found (SaveRouter.OwnerOf).
//
// GetPotentialSaveSlots takes nothing that names a row: it stays the plugin's own (a foreign state's slot list comes
// from the plugin of the game's emulator - noted, not routable).

#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Unbroken.LaunchBox.Plugins;
using Unbroken.LaunchBox.Plugins.Data;

namespace LbIntegrations.Saves
{
    /// <summary>A plugin's own save code, behind the desk. Implemented explicitly by each plugin.</summary>
    internal interface IOwnSaves
    {
        GetSavesResponse OwnGetSaves(GetSavesArgs args);
        bool OwnIsSaveActive(GameSaveBase save, string emulatorApplicationPath);
        bool OwnIsSaveContainer(GameSaveBase save);
        bool OwnUseSaveGroupIdForPersistedMatch(GameSaveBase save);
        bool OwnTryComputeSaveSignature(GameSaveBase save, string emulatorApplicationPath, out string? signature);
        bool OwnTryBackupSave(GameSaveBase save, string emulatorApplicationPath, string destinationFolder, out string? error);
        bool OwnTryGetSaveGroupInfo(GameSaveBase save, string primaryFilePath, out string? saveGroupId, out string? saveGroupName);
        bool OwnIsSecondarySaveFile(string filePath);
        IReadOnlyList<string> OwnGetCompanionSaveFiles(string primaryFilePath);
        AddSaveResponse OwnAddSaveFile(AddSaveArgs args);
        PluginResponse OwnRemoveSave(GameSaveBase? save);

        /// <summary>Where a restore of <paramref name="save"/> would write on <paramref name="emulator"/>'s install - the
        /// computation the plugin's restore uses, read only (ExpectedDir, docs\saves-two-installs.md §5).</summary>
        ExpectedDir OwnExpectedDir(IEmulator emulator, GameSaveBase save) => ExpectedDir.Unknown("this plugin does not say yet");
    }

    internal sealed class SaveDesk
    {
        private readonly EmulatorPlugin _plugin;
        private readonly IOwnSaves _own;
        private readonly string _emu;
        private readonly string _ownPrefix;
        private readonly Action<string> _log;

        /// <summary>Built in the plugin's constructor: registers the plugin as the owner of <paramref name="emu"/>.</summary>
        public SaveDesk(EmulatorPlugin plugin, IOwnSaves own, string emu, Action<string> log)
        {
            _plugin = plugin; _own = own; _emu = emu; _log = log; _ownPrefix = OwnPrefix(emu);
            SaveRouter.Log ??= log;
            if (SaveRouter.Active) SaveRouter.Register(emu, plugin);
            Say("front desk " + (SaveRouter.Active ? "on" : "off (the host asks every plugin)") + " for " + emu);
            // LaunchBox's RetroArch, Dolphin and PCSX2 get a desk too (lot 4) - whatever the host: their ids are decoded under
            // LiteBox as well, which calls them directly.
            StockDesk.Install();
        }

        /// <summary>How every id of <paramref name="emu"/> starts: SaveId writes "emu" first (pinned by the probe).</summary>
        internal static string OwnPrefix(string emu) => "{\"emu\":\"" + emu + "\"";

        private void Say(string message) { try { _log("[saves] " + message); } catch { } }

        private static string SafeExe(GetSavesArgs? args) { try { return args?.Emulator?.ApplicationPath ?? ""; } catch { return ""; } }

        /// <summary>The route of a row that is not the plugin's; null for its own (and when the desk is off).</summary>
        private SaveOwnerRoute? Route(GameSaveBase? save)
        {
            var id = save?.SaveGroupId;
            if (id != null && id.StartsWith(_ownPrefix, StringComparison.Ordinal)) return null;
            return SaveRouter.OwnerOf(save, _plugin, _emu);
        }

        public GetSavesResponse GetSaves(GetSavesArgs args)
        {
            StockDesk.Verify();
            // The cards of LaunchBox's integrations in step with our ids before this listing is matched (lot 4).
            StockDesk.EnsureIds(args);
            // A big listing (LaunchBox's start-up scan) asks the disk once per folder, not once per game (SaveScan).
            using (SaveScan.Begin((args?.Games?.Count ?? 0) + (args?.AdditionalApplications?.Count ?? 0)))
                return GetSavesCore(args);
        }

        private GetSavesResponse GetSavesCore(GetSavesArgs args)
        {
            var own = _own.OwnGetSaves(args);
            try
            {
                var mine = own?.WasSuccess == true ? own.FoundSaves ?? Array.Empty<GameSaveBase>() : (IReadOnlyCollection<GameSaveBase>)Array.Empty<GameSaveBase>();
                // The plugin's rows carry its exe name (measured 08/10: left empty, LaunchBox showed another row's
                // "retroarch.exe (snes9x_libretro)" on SUPER ZSNES's card), and the router's last resort reads it.
                var exe = System.IO.Path.GetFileName(SafeExe(args));
                var named = string.IsNullOrEmpty(exe) ? mine.ToList()
                            : mine.Select(r => r != null && string.IsNullOrEmpty(r.EmulatorFileName) ? SaveRows.With(r, exe, null) : r).ToList();
                // Two installs of this plugin, two cards: a new card gets this install's hint (InstallIds, 12/10).
                named = InstallIds.Apply(_emu, SafeExe(args), named, Say);
                var others = SaveRouter.OthersFor(_plugin, args, named);
                // Plan A (09/10): one game on screen - each row tied as LaunchBox ties its persisted row, and given a
                // persisted row at its own path (SavePersist).
                if ((args?.Games?.Count ?? 0) == 1 && LbIntegrations.Compat.Compat.On("planA"))
                {
                    var gameId = args!.Games!.First().Id;
                    named = SavePersist.TieLikeLaunchBox(gameId, named, Say).ToList();
                    others = SavePersist.TieLikeLaunchBox(gameId, others, Say).ToList();
                    SavePersist.Ensure(gameId, named.Concat(others), m => Say(m.Replace("[saves] ", "")));
                }
                // One game on screen (Game Saves): every row handed to LaunchBox, to tell a row it hid from one never sent.
                if ((args?.Games?.Count ?? 0) <= 1)
                    foreach (var r in named.Concat(others))
                        Say("row " + (r is GameSaveState s ? "state " + s.Slot : "save") + " " + r.FileLocation + " exe=" + r.EmulatorFileName
                            + (string.IsNullOrEmpty(r.AdditionalApplicationId) ? "" : " app=" + r.AdditionalApplicationId) + " id=" + r.SaveGroupId);
                if (others.Count == 0 && named.SequenceEqual(mine)) return own!;
                return new GetSavesResponse(named.Concat(others).ToList());
            }
            catch (Exception ex) { Say("others not listed: " + ex.Message); return own!; }
        }

        public bool IsSaveActive(GameSaveBase save, string emulatorApplicationPath)
            => Route(save) is { } r ? SaveForward.IsSaveActive(r, save, emulatorApplicationPath)
                                    : _own.OwnIsSaveActive(save, SaveRouter.OwnInstallPath(_plugin, save, emulatorApplicationPath)!);

        public bool IsSaveContainer(GameSaveBase save)
            => Route(save) is { } r ? SaveForward.IsSaveContainer(r, save) : _own.OwnIsSaveContainer(save);

        public bool UseSaveGroupIdForPersistedMatch(GameSaveBase save)
            => Route(save) is { } r ? SaveForward.UseSaveGroupIdForPersistedMatch(r, save) : _own.OwnUseSaveGroupIdForPersistedMatch(save);

        public bool TryComputeSaveSignature(GameSaveBase save, string emulatorApplicationPath, out string? signature)
        {
            if (Route(save) is { } r) return SaveForward.TryComputeSaveSignature(r, save, emulatorApplicationPath, out signature);
            if (_own.OwnTryComputeSaveSignature(save, SaveRouter.OwnInstallPath(_plugin, save, emulatorApplicationPath)!, out signature)) return true;
            return WithCompanions(save, out signature);
        }

        /// <summary>The slot list of a plugin that has no numbered states of its own (bsnes, no$gba, Cxbx). LaunchBox asks
        /// the GAME emulator's plugin for it (GetPotentialSaveSlots takes no argument) and refuses every state restore on
        /// an empty one - so, with the desk on, another emulator's state listed on such a game could never be restored.
        /// 0-9 then (Mehdi, 09/10, option a): the state's owner takes the pick or refuses it with its own numbers
        /// (SlotRestore). Empty under LiteBox, which asks the owner itself.</summary>
        public static IReadOnlyDictionary<int, string> ForeignSlots()
        {
            var slots = new Dictionary<int, string>();
            if (SaveRouter.Active && LbIntegrations.Compat.Compat.On("foreignSlots"))
                for (int i = 0; i <= 9; i++) slots[i] = i.ToString(System.Globalization.CultureInfo.InvariantCulture);
            return slots;
        }

        /// <summary>A save with companions gets a signature over all of it (docs\saves-lot3.md §7, measured 08/10): LaunchBox's
        /// own compares the primary file only, so a changed .rtc alone was never backed up. SHA-256 of the primary, then of
        /// each companion in order of its tail - the content only, so a vault copy ("-01.srm" + "-01.rtc") and the live set
        /// compare equal when they are. A state's companion is its thumbnail, made again at will: never counted, and a save
        /// without companions keeps LaunchBox's own signature (false). Sets above 64 MB are left to LaunchBox.</summary>
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, string> _signatures = new(StringComparer.OrdinalIgnoreCase);

        private bool WithCompanions(GameSaveBase save, out string? signature)
        {
            signature = null;
            try
            {
                var primary = save?.FileLocation;
                if (!LbIntegrations.Compat.Compat.On("companionSignature")) return false;
                if (save is GameSaveState || string.IsNullOrWhiteSpace(primary) || !File.Exists(primary)) return false;
                var companions = (_own.OwnGetCompanionSaveFiles(primary) ?? Array.Empty<string>())
                                 .Where(c => !string.IsNullOrWhiteSpace(c) && File.Exists(c) && !c.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
                                 .OrderBy(c => Tail(primary, c), StringComparer.OrdinalIgnoreCase).ToList();
                if (companions.Count == 0) return false;
                var files = new[] { primary }.Concat(companions).ToList();
                var infos = files.Select(f => new FileInfo(f)).ToList();
                if (infos.Sum(i => i.Length) > 64L * 1024 * 1024) return false;
                // Kept while every file of the set keeps its size and date (10/10: hashed again on each of the hundreds of calls a
                // Game Saves opening makes - 40 to 124 us a call).
                var key = string.Join("|", infos.Select(i => i.FullName + "*" + i.Length + "*" + i.LastWriteTimeUtc.Ticks + "*" + Tail(primary, i.FullName)));
                if (_signatures.TryGetValue(key, out var known)) { signature = known; return true; }
                using var all = System.Security.Cryptography.IncrementalHash.CreateHash(System.Security.Cryptography.HashAlgorithmName.SHA256);
                foreach (var f in files)
                {
                    using var s = File.OpenRead(f);
                    all.AppendData(System.Security.Cryptography.SHA256.HashData(s));
                    all.AppendData(System.Text.Encoding.UTF8.GetBytes("|" + Tail(primary, f) + "|"));
                }
                signature = "set:" + Convert.ToHexString(all.GetHashAndReset());
                if (_signatures.Count > 512) _signatures.Clear();
                _signatures[key] = signature;
                return true;
            }
            catch (Exception ex) { Say("no signature over the companions of " + save?.FileLocation + ": " + ex.Message); signature = null; return false; }
        }

        /// <summary>What a companion adds to its primary's stem (".rtc", ".bkr"), whatever the stem - the live one or a
        /// vault copy's "-01".</summary>
        private static string Tail(string primary, string file)
        {
            var stem = Path.GetFileNameWithoutExtension(primary);
            var name = Path.GetFileName(file);
            return name.StartsWith(stem, StringComparison.OrdinalIgnoreCase) ? name.Substring(stem.Length) : Path.GetExtension(name);
        }

        public bool TryBackupSave(GameSaveBase save, string emulatorApplicationPath, string destinationFolder, out string? error)
        {
            if (Route(save) is { } r) return SaveForward.TryBackupSave(r, save, emulatorApplicationPath, destinationFolder, out error);
            return _own.OwnTryBackupSave(save, SaveRouter.OwnInstallPath(_plugin, save, emulatorApplicationPath)!, destinationFolder, out error);
        }

        public bool TryGetSaveGroupInfo(GameSaveBase save, string primaryFilePath, out string? saveGroupId, out string? saveGroupName)
        {
            // A row that carries its group answers it (08/10): asked in the tile's constructor (SaveItem..ctor), and left
            // unanswered LaunchBox picks a group itself - the card with a backup of the same file name: RASnes9x's Secret of
            // Mana .srm was tied to SUPER ZSNES's card, its id replaced, then hidden.
            if (!string.IsNullOrEmpty(save?.SaveGroupId) && LbIntegrations.Compat.Compat.On("planA"))
            {
                saveGroupId = save!.SaveGroupId; saveGroupName = save.SaveGroupName;
                return true;
            }
            if (Route(save) is { } r) return SaveForward.TryGetSaveGroupInfo(r, save, primaryFilePath, out saveGroupId, out saveGroupName);
            return _own.OwnTryGetSaveGroupInfo(save, primaryFilePath, out saveGroupId, out saveGroupName);
        }

        public bool IsSecondarySaveFile(string filePath)
            => SaveRouter.OwnerOfPath(filePath, _plugin, _emu) is { } r ? SaveForward.IsSecondarySaveFile(r, filePath) : _own.OwnIsSecondarySaveFile(filePath);

        public IReadOnlyList<string> GetCompanionSaveFiles(string primaryFilePath)
            => SaveRouter.OwnerOfPath(primaryFilePath, _plugin, _emu) is { } r
                ? SaveForward.GetCompanionSaveFiles(r, primaryFilePath)
                : _own.OwnGetCompanionSaveFiles(primaryFilePath);

        public AddSaveResponse AddSaveFile(AddSaveArgs args)
        {
            if (Route(args?.SaveToAdd) is { } r) return SaveForward.AddSaveFile(r, args!, _log);
            // The plugin's own row: the install it goes to is chosen as a forwarded row's is - with two installs of this
            // plugin, the game's own emulator is not necessarily the one the save came from (docs\saves-two-installs.md §4).
            if (args?.SaveToAdd != null && SaveRouter.Active && LbIntegrations.Compat.Compat.On("twoInstalls"))
            {
                var via = SaveRouter.RestoreEmulator(_plugin, args.SaveToAdd, null, out var refused);
                if (refused != null) { Say("restore of its own save refused: " + refused); return new AddSaveResponse(refused); }
                if (via != null) return SaveForward.WithVia(via, () => _own.OwnAddSaveFile(args));
            }
            return _own.OwnAddSaveFile(args!);
        }

        public PluginResponse RemoveSave(GameSaveBase? save)
            => Route(save) is { } r ? SaveForward.RemoveSave(r, save) : _own.OwnRemoveSave(save);
    }
}
