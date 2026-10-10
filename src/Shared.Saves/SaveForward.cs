// A call on a row handed to the row's owner (lot 3's SaveDesk, lot 4's StockDesk): one method per call LaunchBox makes,
// each asking the owner's plugin under a try. Shared by the two desks so they forward the same way; never called with the
// plugin being served as the owner (the route is another plugin, or a twin whose own code then answers).

#nullable enable

using System;
using System.Collections.Generic;
using Unbroken.LaunchBox.Plugins;
using Unbroken.LaunchBox.Plugins.Data;

namespace LbIntegrations.Saves
{
    internal static class SaveForward
    {
        public static bool IsSaveActive(SaveOwnerRoute r, GameSaveBase save, string emulatorApplicationPath)
            => Ask(r, () => r.Plugin.IsSaveActive(save, r.EmulatorPath ?? emulatorApplicationPath), false);

        public static bool IsSaveContainer(SaveOwnerRoute r, GameSaveBase save)
            => Ask(r, () => r.Plugin.IsSaveContainer(save), false);

        public static bool UseSaveGroupIdForPersistedMatch(SaveOwnerRoute r, GameSaveBase save)
            => Ask(r, () => r.Plugin.UseSaveGroupIdForPersistedMatch(save), false);

        public static bool TryComputeSaveSignature(SaveOwnerRoute r, GameSaveBase save, string emulatorApplicationPath, out string? signature)
        {
            string? s = null;
            var ok = Ask(r, () => r.Plugin.TryComputeSaveSignature(save, r.EmulatorPath ?? emulatorApplicationPath, out s), false);
            signature = s;
            return ok;
        }

        public static bool TryBackupSave(SaveOwnerRoute r, GameSaveBase save, string emulatorApplicationPath, string destinationFolder, out string? error)
        {
            string? e = null;
            var ok = Ask(r, () => r.Plugin.TryBackupSave(save, r.EmulatorPath ?? emulatorApplicationPath, destinationFolder, out e), false);
            error = e;
            return ok;
        }

        public static bool TryGetSaveGroupInfo(SaveOwnerRoute r, GameSaveBase save, string primaryFilePath, out string? saveGroupId, out string? saveGroupName)
        {
            string? id = null, name = null;
            var ok = Ask(r, () => r.Plugin.TryGetSaveGroupInfo(save, primaryFilePath, out id, out name), false);
            saveGroupId = id; saveGroupName = name;
            return ok;
        }

        public static bool IsSecondarySaveFile(SaveOwnerRoute r, string filePath)
            => Ask(r, () => r.Plugin.IsSecondarySaveFile(filePath), false);

        public static IReadOnlyList<string> GetCompanionSaveFiles(SaveOwnerRoute r, string primaryFilePath)
            => Ask(r, () => r.Plugin.GetCompanionSaveFiles(primaryFilePath), (IReadOnlyList<string>)Array.Empty<string>()) ?? Array.Empty<string>();

        /// <summary>A restore: LaunchBox's RetroArch and Dolphin rows by the desk's own steps (their AddSaveFile would use the
        /// GAME's emulator - StockRestore), anything else by the owner.</summary>
        public static AddSaveResponse AddSaveFile(SaveOwnerRoute r, AddSaveArgs args, Action<string> log)
        {
            var desk = r.Stock && StockRestore.Handles(r.Emu) && LbIntegrations.Compat.Compat.On("stockRestore");
            SaveRouter.Say("restore of a " + r.Emu + " save handed to " + (desk ? "the desk's own " + r.Emu + " steps" : r.Emu));
            if (desk) return StockRestore.Add(r, args, log);
            return Ask(r, () => r.Plugin.AddSaveFile(args), new AddSaveResponse("The " + r.Emu + " plugin could not restore this save."));
        }

        public static PluginResponse RemoveSave(SaveOwnerRoute r, GameSaveBase? save)
            => Ask(r, () => r.Plugin.RemoveSave(save), new PluginResponse(false, "The " + r.Emu + " plugin could not remove this save."));

        public static T Ask<T>(SaveOwnerRoute r, Func<T> call, T fallback)
        {
            try { return call(); }
            catch (Exception ex) { SaveRouter.Say(r.Emu + " failed: " + ex.Message); return fallback; }
        }
    }
}
