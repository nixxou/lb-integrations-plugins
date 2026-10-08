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
            var own = _own.OwnGetSaves(args);
            try
            {
                var mine = own?.WasSuccess == true ? own.FoundSaves ?? Array.Empty<GameSaveBase>() : (IReadOnlyCollection<GameSaveBase>)Array.Empty<GameSaveBase>();
                // The plugin's rows carry its exe name (measured 08/10: left empty, LaunchBox showed another row's
                // "retroarch.exe (snes9x_libretro)" on SUPER ZSNES's card), and the router's last resort reads it.
                var exe = System.IO.Path.GetFileName(SafeExe(args));
                var named = string.IsNullOrEmpty(exe) ? mine.ToList()
                            : mine.Select(r => r != null && string.IsNullOrEmpty(r.EmulatorFileName) ? SaveRows.With(r, exe, null) : r).ToList();
                var others = SaveRouter.OthersFor(_plugin, args, named);
                // Plan A (09/10): one game on screen - each row tied as LaunchBox ties its persisted row, and given a
                // persisted row at its own path (SavePersist).
                if ((args?.Games?.Count ?? 0) == 1)
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
            => Route(save) is { } r ? Ask(r, () => r.Plugin.IsSaveActive(save, r.EmulatorPath ?? emulatorApplicationPath), false)
                                    : _own.OwnIsSaveActive(save, emulatorApplicationPath);

        public bool IsSaveContainer(GameSaveBase save)
            => Route(save) is { } r ? Ask(r, () => r.Plugin.IsSaveContainer(save), false) : _own.OwnIsSaveContainer(save);

        public bool UseSaveGroupIdForPersistedMatch(GameSaveBase save)
            => Route(save) is { } r ? Ask(r, () => r.Plugin.UseSaveGroupIdForPersistedMatch(save), false) : _own.OwnUseSaveGroupIdForPersistedMatch(save);

        public bool TryComputeSaveSignature(GameSaveBase save, string emulatorApplicationPath, out string? signature)
        {
            if (Route(save) is { } r)
            {
                string? s = null;
                var ok = Ask(r, () => r.Plugin.TryComputeSaveSignature(save, r.EmulatorPath ?? emulatorApplicationPath, out s), false);
                signature = s;
                return ok;
            }
            return _own.OwnTryComputeSaveSignature(save, emulatorApplicationPath, out signature);
        }

        public bool TryBackupSave(GameSaveBase save, string emulatorApplicationPath, string destinationFolder, out string? error)
        {
            if (Route(save) is { } r)
            {
                string? e = null;
                var ok = Ask(r, () => r.Plugin.TryBackupSave(save, r.EmulatorPath ?? emulatorApplicationPath, destinationFolder, out e), false);
                error = e;
                return ok;
            }
            return _own.OwnTryBackupSave(save, emulatorApplicationPath, destinationFolder, out error);
        }

        public bool TryGetSaveGroupInfo(GameSaveBase save, string primaryFilePath, out string? saveGroupId, out string? saveGroupName)
        {
            // A row that carries its group answers it (08/10): asked in the tile's constructor (SaveItem..ctor), and left
            // unanswered LaunchBox picks a group itself - the card with a backup of the same file name: RASnes9x's Secret of
            // Mana .srm was tied to SUPER ZSNES's card, its id replaced, then hidden.
            if (!string.IsNullOrEmpty(save?.SaveGroupId))
            {
                saveGroupId = save!.SaveGroupId; saveGroupName = save.SaveGroupName;
                return true;
            }
            if (Route(save) is { } r)
            {
                string? id = null, name = null;
                var ok = Ask(r, () => r.Plugin.TryGetSaveGroupInfo(save, primaryFilePath, out id, out name), false);
                saveGroupId = id; saveGroupName = name;
                return ok;
            }
            return _own.OwnTryGetSaveGroupInfo(save, primaryFilePath, out saveGroupId, out saveGroupName);
        }

        public bool IsSecondarySaveFile(string filePath)
            => SaveRouter.OwnerOfPath(filePath, _plugin) is { } r ? Ask(r, () => r.Plugin.IsSecondarySaveFile(filePath), false) : _own.OwnIsSecondarySaveFile(filePath);

        public IReadOnlyList<string> GetCompanionSaveFiles(string primaryFilePath)
            => SaveRouter.OwnerOfPath(primaryFilePath, _plugin) is { } r
                ? Ask(r, () => r.Plugin.GetCompanionSaveFiles(primaryFilePath), (IReadOnlyList<string>)Array.Empty<string>()) ?? Array.Empty<string>()
                : _own.OwnGetCompanionSaveFiles(primaryFilePath);

        public AddSaveResponse AddSaveFile(AddSaveArgs args)
        {
            if (Route(args?.SaveToAdd) is not { } r) return _own.OwnAddSaveFile(args!);
            var desk = r.Stock && StockRestore.Handles(r.Emu);
            Say("restore of a " + r.Emu + " save handed to " + (desk ? "the desk's own " + r.Emu + " steps" : r.Emu));
            if (desk) return StockRestore.Add(r, args!, _log);
            return Ask(r, () => r.Plugin.AddSaveFile(args!), new AddSaveResponse("The " + r.Emu + " plugin could not restore this save."));
        }

        public PluginResponse RemoveSave(GameSaveBase? save)
            => Route(save) is { } r ? Ask(r, () => r.Plugin.RemoveSave(save), new PluginResponse(false, "The " + r.Emu + " plugin could not remove this save."))
                                    : _own.OwnRemoveSave(save);

        private T Ask<T>(SaveOwnerRoute r, Func<T> call, T fallback)
        {
            try { return call(); }
            catch (Exception ex) { Say(r.Emu + " failed: " + ex.Message); return fallback; }
        }
    }
}
