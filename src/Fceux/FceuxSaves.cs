// Save management for FCEUX - FIRST LOT: the front desk only, FCEUX's own files not listed yet.
//
// What is in place now: the desk (FceuxSaveDesk.cs) routes every row that is not this plugin's to its owner and adds the
// other emulators' saves of the game to its listing (Shared.Saves SaveDesk); this plugin lists nothing of its own yet, so
// Game Saves shows no FCEUX card - the files are where FceuxSavePaths puts them, untouched.
//
// THE NEXT LOT (docs\fceux.md, the plan) lists, by FCEUX's own names (file.cpp FCEU_MakeFName, state.cpp; measured 18/10):
//
//   Battery save    <rom>.sav            "odnonvol" = <FCEUX>\Saves\<platform>             a cartridge's
//   FDS disk save   <rom>.fds            the same folder                                   the WHOLE modified disk (fds.cpp:906)
//   Save states     <rom>.fc0 .. .fc9    "odstates" = <FCEUX>\Saves\<platform>\States      one file per slot - one row each
//                                                                                          (Slot in the id, SlotRestore)
//   not saves       <rom>-bak.fcN, <rom>.bak.fc0, <rom>-resume.fcs, <rom>-autosaveN.fcs
//
// with ids {"emu":"fceux","kind":"save"|"state","sys":"nes"|"fds","by":"name","id":"<rom>","slot":n} - "sys" because a
// disk and a cartridge can share a base name and both write .fcN states; <rom> is the ROM INSIDE an archive. Decided
// here, before the first release: an id is kept for ever by LaunchBox.

using System;
using System.Collections.Generic;
using System.Globalization;
using LbIntegrations.Saves;
using Unbroken.LaunchBox.Plugins;
using Unbroken.LaunchBox.Plugins.Data;

namespace LbIntegrations.Fceux
{
    public partial class FceuxPlugin
    {
        /// <summary>The front desk (Shared.Saves SaveDesk): this plugin's emu, and where it logs.</summary>
        private string SaveEmu => Emu;
        private void SaveLog(string message) => Log.Info(message);

        /// <summary>This plugin's "emu" in the ids (Shared.Saves) - never reused by another plugin.</summary>
        internal const string Emu = "fceux";

        public override bool SupportsSaveManagement() => true;

        /// <summary>FCEUX's ten slots, .fc0 to .fc9 (state.cpp, one file each).</summary>
        public override IReadOnlyDictionary<int, string> GetPotentialSaveSlots()
        {
            var slots = new Dictionary<int, string>();
            for (int i = 0; i <= 9; i++) slots[i] = i.ToString(CultureInfo.InvariantCulture);
            return slots;
        }

        GetSavesResponse IOwnSaves.OwnGetSaves(GetSavesArgs args)
            => new GetSavesResponse(new List<GameSaveBase>());   // the next lot - see the header

        bool IOwnSaves.OwnIsSaveActive(GameSaveBase save, string emulatorApplicationPath)
        {
            try { return string.IsNullOrWhiteSpace(save?.FileLocation) || System.IO.File.Exists(save.FileLocation); }
            catch { return true; }
        }

        bool IOwnSaves.OwnIsSaveContainer(GameSaveBase save) => false;

        bool IOwnSaves.OwnUseSaveGroupIdForPersistedMatch(GameSaveBase save) => IsOurs(save);

        bool IOwnSaves.OwnTryBackupSave(GameSaveBase save, string emulatorApplicationPath, string destinationFolder, out string error)
        {
            error = "An FCEUX save is one file, not a container.";
            return false;
        }

        bool IOwnSaves.OwnIsSecondarySaveFile(string filePath) => false;

        IReadOnlyList<string> IOwnSaves.OwnGetCompanionSaveFiles(string primaryFilePath) => Array.Empty<string>();

        AddSaveResponse IOwnSaves.OwnAddSaveFile(AddSaveArgs args)
            => new AddSaveResponse("Restoring an FCEUX save is not available in this version of " + PackName + ".");

        PluginResponse IOwnSaves.OwnRemoveSave(GameSaveBase save) => base.RemoveSave(save);

        private static bool IsOurs(GameSaveBase save) => SaveRows.IsOurs(save?.SaveGroupId, Emu);
    }
}
