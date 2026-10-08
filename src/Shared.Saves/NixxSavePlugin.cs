// The base of every Nixx plugin that manages saves: the front desk (SaveRouter) in front of the plugin's own
// save code (lot 3, docs\saves-lb14-routing.md §4b).
//
// LaunchBox calls the overrides below; each one sends a row that is not ours to its owner, and hands ours to
// the plugin's Own* method - the plugin's save code as it was, renamed. GetSaves adds the saves the other
// emulators of the library keep for the game. A plugin's code that falls back on "base.X" calls base.OwnX
// (EmulatorPlugin's behaviour), never back into the desk.
//
// IsSaveActive is called hundreds of times: our own ids are told by a prefix (SaveId writes "emu" first), and
// the route of any other id is kept once found (SaveRouter.OwnerOf).
//
// GetPotentialSaveSlots takes nothing that names a row: it stays the plugin's own (a foreign state's slot list
// comes from the plugin of the game's emulator - noted, not routable).

#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using Unbroken.LaunchBox.Plugins;
using Unbroken.LaunchBox.Plugins.Data;

namespace LbIntegrations.Saves
{
    public abstract class NixxSavePlugin : EmulatorPlugin
    {
        private readonly string _ownPrefix;

        protected NixxSavePlugin()
        {
            _ownPrefix = OwnPrefix(SaveEmu);
            SaveRouter.Log ??= SaveLog;
            if (SaveRouter.Active) SaveRouter.Register(SaveEmu, this);
        }

        /// <summary>This plugin's "emu" in the ids (Shared.Saves, docs\saves-lb14-routing.md §3). A constant: it is
        /// read by the base constructor.</summary>
        protected abstract string SaveEmu { get; }

        /// <summary>Where the desk writes what it did - the plugin's log.</summary>
        protected abstract void SaveLog(string message);

        /// <summary>How every id of <paramref name="emu"/> starts: SaveId writes "emu" first (pinned by the probe).</summary>
        internal static string OwnPrefix(string emu) => "{\"emu\":\"" + emu + "\"";

        /// <summary>The route of a row that is not ours; null for ours (and when the desk is off).</summary>
        private SaveOwnerRoute? Route(GameSaveBase? save)
        {
            var id = save?.SaveGroupId;
            if (id != null && id.StartsWith(_ownPrefix, StringComparison.Ordinal)) return null;
            return SaveRouter.OwnerOf(save, this, SaveEmu);
        }

        // ── the desk ─────────────────────────────────────────────────────────

        public sealed override GetSavesResponse GetSaves(GetSavesArgs args)
        {
            var own = OwnGetSaves(args);
            try
            {
                var mine = own?.WasSuccess == true ? own.FoundSaves ?? Array.Empty<GameSaveBase>() : (IReadOnlyCollection<GameSaveBase>)Array.Empty<GameSaveBase>();
                var others = SaveRouter.OthersFor(this, args, mine);
                if (others.Count == 0) return own!;
                return new GetSavesResponse(mine.Concat(others).ToList());
            }
            catch (Exception ex) { SaveLog("[saves] others not listed: " + ex.Message); return own!; }
        }

        public sealed override bool IsSaveActive(GameSaveBase save, string emulatorApplicationPath)
            => Route(save) is { } r ? Ask(r, () => r.Plugin.IsSaveActive(save, r.EmulatorPath ?? emulatorApplicationPath), false)
                                    : OwnIsSaveActive(save, emulatorApplicationPath);

        public sealed override bool IsSaveContainer(GameSaveBase save)
            => Route(save) is { } r ? Ask(r, () => r.Plugin.IsSaveContainer(save), false) : OwnIsSaveContainer(save);

        public sealed override bool UseSaveGroupIdForPersistedMatch(GameSaveBase save)
            => Route(save) is { } r ? Ask(r, () => r.Plugin.UseSaveGroupIdForPersistedMatch(save), false) : OwnUseSaveGroupIdForPersistedMatch(save);

        public sealed override bool TryComputeSaveSignature(GameSaveBase save, string emulatorApplicationPath, out string? signature)
        {
            if (Route(save) is { } r)
            {
                string? s = null;
                var ok = Ask(r, () => r.Plugin.TryComputeSaveSignature(save, r.EmulatorPath ?? emulatorApplicationPath, out s), false);
                signature = s;
                return ok;
            }
            return OwnTryComputeSaveSignature(save, emulatorApplicationPath, out signature);
        }

        public sealed override bool TryBackupSave(GameSaveBase save, string emulatorApplicationPath, string destinationFolder, out string? error)
        {
            if (Route(save) is { } r)
            {
                string? e = null;
                var ok = Ask(r, () => r.Plugin.TryBackupSave(save, r.EmulatorPath ?? emulatorApplicationPath, destinationFolder, out e), false);
                error = e;
                return ok;
            }
            return OwnTryBackupSave(save, emulatorApplicationPath, destinationFolder, out error);
        }

        public sealed override bool TryGetSaveGroupInfo(GameSaveBase save, string primaryFilePath, out string? saveGroupId, out string? saveGroupName)
        {
            if (Route(save) is { } r)
            {
                string? id = null, name = null;
                var ok = Ask(r, () => r.Plugin.TryGetSaveGroupInfo(save, primaryFilePath, out id, out name), false);
                saveGroupId = id; saveGroupName = name;
                return ok;
            }
            return OwnTryGetSaveGroupInfo(save, primaryFilePath, out saveGroupId, out saveGroupName);
        }

        public sealed override bool IsSecondarySaveFile(string filePath)
            => SaveRouter.OwnerOfPath(filePath, this) is { } r ? Ask(r, () => r.Plugin.IsSecondarySaveFile(filePath), false) : OwnIsSecondarySaveFile(filePath);

        public sealed override IReadOnlyList<string> GetCompanionSaveFiles(string primaryFilePath)
            => SaveRouter.OwnerOfPath(primaryFilePath, this) is { } r
                ? Ask(r, () => r.Plugin.GetCompanionSaveFiles(primaryFilePath), (IReadOnlyList<string>)Array.Empty<string>()) ?? Array.Empty<string>()
                : OwnGetCompanionSaveFiles(primaryFilePath);

        public sealed override AddSaveResponse AddSaveFile(AddSaveArgs args)
        {
            if (Route(args?.SaveToAdd) is not { } r) return OwnAddSaveFile(args!);
            SaveLog("[saves] restore of a " + r.Emu + " save handed to " + (r.Stock && StockRestore.Handles(r.Emu) ? "the desk's own " + r.Emu + " steps" : r.Emu));
            if (r.Stock && StockRestore.Handles(r.Emu)) return StockRestore.Add(r, args!, SaveLog);
            return Ask(r, () => r.Plugin.AddSaveFile(args!), new AddSaveResponse("The " + r.Emu + " plugin could not restore this save."));
        }

        public sealed override PluginResponse RemoveSave(GameSaveBase? save)
            => Route(save) is { } r ? Ask(r, () => r.Plugin.RemoveSave(save), new PluginResponse(false, "The " + r.Emu + " plugin could not remove this save."))
                                    : OwnRemoveSave(save);

        private T Ask<T>(SaveOwnerRoute r, Func<T> call, T fallback)
        {
            try { return call(); }
            catch (Exception ex) { SaveLog("[saves] " + r.Emu + " failed: " + ex.Message); return fallback; }
        }

        // ── the plugin's own ─────────────────────────────────────────────────

        protected virtual GetSavesResponse OwnGetSaves(GetSavesArgs args) => base.GetSaves(args);
        protected virtual bool OwnIsSaveActive(GameSaveBase save, string emulatorApplicationPath) => base.IsSaveActive(save, emulatorApplicationPath);
        protected virtual bool OwnIsSaveContainer(GameSaveBase save) => base.IsSaveContainer(save);
        protected virtual bool OwnUseSaveGroupIdForPersistedMatch(GameSaveBase save) => base.UseSaveGroupIdForPersistedMatch(save);
        protected virtual bool OwnTryComputeSaveSignature(GameSaveBase save, string emulatorApplicationPath, out string? signature)
            => base.TryComputeSaveSignature(save, emulatorApplicationPath, out signature);
        protected virtual bool OwnTryBackupSave(GameSaveBase save, string emulatorApplicationPath, string destinationFolder, out string? error)
            => base.TryBackupSave(save, emulatorApplicationPath, destinationFolder, out error);
        protected virtual bool OwnTryGetSaveGroupInfo(GameSaveBase save, string primaryFilePath, out string? saveGroupId, out string? saveGroupName)
            => base.TryGetSaveGroupInfo(save, primaryFilePath, out saveGroupId, out saveGroupName);
        protected virtual bool OwnIsSecondarySaveFile(string filePath) => base.IsSecondarySaveFile(filePath);
        protected virtual IReadOnlyList<string> OwnGetCompanionSaveFiles(string primaryFilePath) => base.GetCompanionSaveFiles(primaryFilePath);
        protected virtual AddSaveResponse OwnAddSaveFile(AddSaveArgs args) => base.AddSaveFile(args);
        protected virtual PluginResponse OwnRemoveSave(GameSaveBase? save) => base.RemoveSave(save);
    }
}
