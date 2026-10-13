// Nixx-Saves: the catch-all of the pack's saves (lot 6, docs\saves-lot6.md; plan docs\saves-lb14-routing.md §4b, row 6).
//
// LaunchBox asks only the plugin of a game's emulator about its saves (measured 07/10). A game set to an emulator NO
// plugin drives - one added by hand, FakeEmulateur.exe, an emulator LaunchBox has no integration for - has no Game Saves
// at all: a save another emulator keeps for it (RetroArch, ares... through Launch With, or before the game moved) is never
// listed nor backed up. This plugin takes those emulators (CatchAllClaims: only those no other plugin takes) and does for
// their games what the pack's own plugins do through their front desk: it lists the other emulators' saves and hands
// every call on one to the emulator that made it (SaveDesk, lot 3).
//
// IT HAS NO SAVES OF ITS OWN, and changes nothing else:
//   - the launch goes through untouched (PrepareEmulatorForLaunch is the SDK's: success, the line as it is);
//   - nothing in LaunchBox's emulator screens: no version, no update, no install, no BIOS, no RetroAchievements;
//   - SupportsSaveManagement true only when another plugin WITH saves covers a platform of one of the emulators it takes
//     (lot 5's D6); else the SDK's false;
//   - a row nobody claims gets the SDK's answers, except RemoveSave - the SDK's body deletes the file: asked first, No by
//     default (lot 5, SlotRestore.MayDeleteUnclaimed) - and AddSaveFile, which has no folder to write to: refused.
//
// Its ids: none - every row it lists is another emulator's, with that emulator's id (nothing for lot 7 at uninstall).
// Its emu, for the router: "catchall".

#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using LbIntegrations.Saves;
using Unbroken.LaunchBox.Plugins;
using Unbroken.LaunchBox.Plugins.Data;

namespace LbIntegrations.SavesCatchAll
{
    public sealed class NixxSavesPlugin : EmulatorPlugin, ISystemEventsPlugin, IOwnSaves
    {
        public const string Name = "Nixx-Saves";
        public const string SaveEmu = "catchall";

        private readonly SaveDesk _desk;
        private bool? _supports;

        public NixxSavesPlugin()
        {
            _desk = new SaveDesk(this, this, SaveEmu, m => Log.Info(m));
            Log.Info("plugin constructed, assembly " + typeof(NixxSavesPlugin).Assembly.Location);
        }

        public override string EmulatorName => Name;

        public void OnEventRaised(string eventType)
        {
            try
            {
                if (eventType != SystemEventTypes.PluginInitialized
                    && eventType != SystemEventTypes.LaunchBoxStartupCompleted
                    && eventType != SystemEventTypes.BigBoxStartupCompleted) return;
                // PluginInitialized: this plugin is loaded (others may still come); the start-up events: all are.
                if (eventType == SystemEventTypes.PluginInitialized) CatchAllClaims.Initialized(); else CatchAllClaims.Started();
                Log.Info("host event \"" + eventType + "\" - " + (CatchAllClaims.Active(out var why) ? "takes the emulators no plugin drives" : "takes nothing: " + why)
                         + "; LaunchBox's registry: " + LbIntegrations.Compat.LbPluginRegistry.Describe(out _));
            }
            catch (Exception ex) { Log.Warn("OnEventRaised", ex); }
        }

        // ── claiming ─────────────────────────────────────────────────────────

        public override IEnumerable<IEmulator> GetApplicableEmulators(IEnumerable<IEmulator> emulators)
        {
            try { return (emulators ?? Enumerable.Empty<IEmulator>()).Where(e => CatchAllClaims.Claims(this, e)).ToList(); }
            catch (Exception ex) { Log.Warn("GetApplicableEmulators: takes nothing", ex); return Array.Empty<IEmulator>(); }
        }

        // ── nothing in LaunchBox's emulator screens ──────────────────────────

        /// <summary>Only the platforms of the emulators it takes: "supported" everywhere could have LaunchBox offer Nixx-Saves where no
        /// plugin is recommended (review 13/10). Never "recommended".</summary>
        public override EmulatorSupportResponse IsPlatformSupported(string platform) => new EmulatorSupportResponse(CatchAllClaims.CoversPlatform(platform), false);
        public override string? GetCurrentVersion(string applicationPath) => null;
        public override IEnumerable<EmulatorControllerVersion>? GetInstallableVersions() => Array.Empty<EmulatorControllerVersion>();
        public override EmulatorInstallResponse InstallEmulator(InstallEmulatorArgs args) => new EmulatorInstallResponse(Name + " installs no emulator: it only manages the saves of the emulators no plugin drives.");
        public override bool IsUpdateAvailable(IEmulator? emulator, out EmulatorControllerVersion? version) { version = null; return false; }
        public override bool IsUpdateAvailable(string? emulatorAppPath, out EmulatorControllerVersion? version) { version = null; return false; }

        // ── the saves: the front desk ────────────────────────────────────────

        public override bool SupportsSaveManagement()
        {
            bool yes = false;
            try
            {
                yes = CatchAllClaims.Active(out var why)
                      && SaveRouter.OthersCoverPlatformsOf(SaveEmu, StockDesk.NoSave, e => CatchAllClaims.Claims(this, e));
                if (_supports != yes) Log.Info("SupportsSaveManagement -> " + yes + (why == null ? "" : " (" + why + ")"));
            }
            catch (Exception ex) { Log.Warn("SupportsSaveManagement: false", ex); yes = false; }
            _supports = yes;
            return yes;
        }

        public override GetSavesResponse GetSaves(GetSavesArgs args) => _desk.GetSaves(args);
        public override bool IsSaveActive(GameSaveBase save, string emulatorApplicationPath) => _desk.IsSaveActive(save, emulatorApplicationPath);
        public override bool IsSaveContainer(GameSaveBase save) => _desk.IsSaveContainer(save);
        public override bool UseSaveGroupIdForPersistedMatch(GameSaveBase save) => _desk.UseSaveGroupIdForPersistedMatch(save);
        public override bool TryComputeSaveSignature(GameSaveBase save, string emulatorApplicationPath, out string? signature) => _desk.TryComputeSaveSignature(save, emulatorApplicationPath, out signature);
        public override bool TryBackupSave(GameSaveBase save, string emulatorApplicationPath, string destinationFolder, out string? error) => _desk.TryBackupSave(save, emulatorApplicationPath, destinationFolder, out error);
        public override bool TryGetSaveGroupInfo(GameSaveBase save, string primaryFilePath, out string? saveGroupId, out string? saveGroupName) => _desk.TryGetSaveGroupInfo(save, primaryFilePath, out saveGroupId, out saveGroupName);
        public override bool IsSecondarySaveFile(string filePath) => _desk.IsSecondarySaveFile(filePath);
        public override IReadOnlyList<string> GetCompanionSaveFiles(string primaryFilePath) => _desk.GetCompanionSaveFiles(primaryFilePath);
        public override AddSaveResponse AddSaveFile(AddSaveArgs args) => _desk.AddSaveFile(args);
        public override PluginResponse RemoveSave(GameSaveBase? save) => _desk.RemoveSave(save);
        /// <summary>No numbered states of its own: another emulator's state listed here keeps its restore (SaveDesk.ForeignSlots).</summary>
        public override IReadOnlyDictionary<int, string> GetPotentialSaveSlots() => SaveDesk.ForeignSlots();

        // ── its own saves: none ──────────────────────────────────────────────

        GetSavesResponse IOwnSaves.OwnGetSaves(GetSavesArgs args) => new GetSavesResponse(new List<GameSaveBase>());
        bool IOwnSaves.OwnIsSaveActive(GameSaveBase save, string emulatorApplicationPath) => base.IsSaveActive(save, emulatorApplicationPath);
        bool IOwnSaves.OwnIsSaveContainer(GameSaveBase save) => base.IsSaveContainer(save);
        bool IOwnSaves.OwnUseSaveGroupIdForPersistedMatch(GameSaveBase save) => base.UseSaveGroupIdForPersistedMatch(save);
        bool IOwnSaves.OwnTryComputeSaveSignature(GameSaveBase save, string emulatorApplicationPath, out string? signature) => base.TryComputeSaveSignature(save, emulatorApplicationPath, out signature);
        bool IOwnSaves.OwnTryBackupSave(GameSaveBase save, string emulatorApplicationPath, string destinationFolder, out string? error) => base.TryBackupSave(save, emulatorApplicationPath, destinationFolder, out error);
        bool IOwnSaves.OwnTryGetSaveGroupInfo(GameSaveBase save, string primaryFilePath, out string? saveGroupId, out string? saveGroupName) => base.TryGetSaveGroupInfo(save, primaryFilePath, out saveGroupId, out saveGroupName);
        bool IOwnSaves.OwnIsSecondarySaveFile(string filePath) => base.IsSecondarySaveFile(filePath);
        IReadOnlyList<string> IOwnSaves.OwnGetCompanionSaveFiles(string primaryFilePath) => base.GetCompanionSaveFiles(primaryFilePath);

        AddSaveResponse IOwnSaves.OwnAddSaveFile(AddSaveArgs args)
        {
            Log.Info("restore refused, nobody claims the save: " + args?.SaveToAdd?.FileLocation + " id=" + args?.SaveToAdd?.SaveGroupId);
            return new AddSaveResponse("This save belongs to no emulator known here: there is no folder to restore it to. Restore it from the emulator that made it.");
        }

        PluginResponse IOwnSaves.OwnRemoveSave(GameSaveBase? save)
        {
            // The SDK's body deletes the file: a save nobody claims is deleted only when the user says so (lot 5).
            if (!SlotRestore.MayDeleteUnclaimed(save?.FileLocation, m => Log.Info(m), out var refused)) return new PluginResponse(false, refused);
            return base.RemoveSave(save);
        }
    }
}
