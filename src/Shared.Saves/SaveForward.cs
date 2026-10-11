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
            // Their own AddSaveFile takes the GAME's emulator - ours here: it would write elsewhere and answer success
            // (StockRestore's header). With its fix turned off, refused (review 10/10).
            if (r.Stock && StockRestore.Handles(r.Emu))
                return new AddSaveResponse("This " + r.Emu + " save can only be restored here with \"RetroArch and Dolphin restores on a game "
                                           + "of the pack\" on (Nixx menu, Compatibility) - or with the game set to that emulator.");
            // The install the row came from, for an owner that would otherwise take the game's emulator - not its own here -
            // or the library's first (review 10/10: with two ares installs, B's save was restored over A's). Two installs
            // and nothing says which: refused, as StockRestore does.
            IEmulator? via = r.Emulator;
            if (!r.Stock && args?.SaveToAdd != null)
            {
                via = SaveRouter.RestoreEmulator(r.Plugin, args.SaveToAdd, r.Emulator, out var refused);
                if (refused != null) { SaveRouter.Say("restore of a " + r.Emu + " save refused: " + refused); return new AddSaveResponse(refused); }
            }
            var key = ViaKey + Environment.CurrentManagedThreadId;
            string? id = null;
            try { id = via?.Id; } catch { }
            var before = AppDomain.CurrentDomain.GetData(key);   // put back after: a restore within a restore keeps the outer one's
            AppDomain.CurrentDomain.SetData(key, id);
            try { return Ask(r, () => r.Plugin.AddSaveFile(args!), new AddSaveResponse("The " + r.Emu + " plugin could not restore this save.")); }
            finally { AppDomain.CurrentDomain.SetData(key, before); }
        }

        /// <summary>Process-wide (every plugin carries its own copy of this class), per thread: the emulator of the library a
        /// desk is restoring a row of, while the owner's AddSaveFile runs.</summary>
        private const string ViaKey = "LbIntegrations.Saves.RestoreVia|";

        /// <summary>For an owner's AddSaveFile: the emulator a desk says the row being restored came from, when it is one of
        /// <paramref name="isMine"/>'s (by its exe path) - else null, and the owner chooses as before.</summary>
        public static IEmulator? ViaEmulator(Func<string?, bool> isMine)
        {
            try
            {
                if (AppDomain.CurrentDomain.GetData(ViaKey + Environment.CurrentManagedThreadId) is not string id || id.Length == 0) return null;
                var e = PluginHelper.DataManager?.GetEmulatorById(id);
                string? path = null;
                try { path = e?.ApplicationPath; } catch { }
                return e != null && isMine(path) ? e : null;
            }
            catch { return null; }
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
