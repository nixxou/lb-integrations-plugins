// What the emulator writes for a game, kept in ITS OWN FOLDER rather than beside the ROM (Mehdi, 01/10):
//
//   srmPath       "{exec}/saves"    <rom>.srm, the cartridge's battery save
//   gameSavePath  "{exec}/states"   <rom>.data.szsnes\ - the states (.szst0..., .szst-last), history, bookmarks
//   chtPath       "{exec}/cheats"   <rom>.cht
//
// MEASURED 01/10: the emulator expands {exec} to its own folder, creates the three folders itself, writes the
// SRAM to saves\<rom>.srm and a state saved with F2 to states\<rom>.data.szsnes\<rom>.szst0 - and READS the SRAM
// from there too, so a save left beside the ROM is not seen any more.
//
// WRITTEN FOR GOOD, the one exception to "nothing of ours lands in the user's file": set on the settings object
// right after LoadMainMenuSave and BEFORE the overrides take their originals, they are the user's values from
// then on and the emulator saves them like any other. Only an EMPTY field is set - empty is the emulator's
// "beside the ROM"; a folder the user chose is theirs. In this plugin rather than in LaunchBox's: the settings
// file does not exist before a first launch, and the emulator opened on its own runs this too.
//
// NOTHING IS MOVED (Mehdi, 01/10): what an older session left beside a ROM stays there. --nixx-data-folders=off
// turns the whole thing off.

using System;

namespace LbIntegrations.SuperZsnes.Mod
{
    internal static class DataFolders
    {
        private static readonly (string Field, string Folder)[] Fields =
        {
            ("srmPath", "saves"), ("gameSavePath", "states"), ("chtPath", "cheats"),
        };

        internal static void Apply(MainMenuManager.MainMenuSettings settings)
        {
            if (!Plugin.Opt.DataFolders || settings == null) return;
            var type = settings.GetType();
            foreach (var (field, folder) in Fields)
            {
                try
                {
                    var prop = type.GetProperty(field);
                    if (prop == null || prop.PropertyType != typeof(string)) { Plugin.Logger.LogWarning("data folders: no field " + field + " in this build"); continue; }
                    var current = (string)prop.GetValue(settings);
                    if (!string.IsNullOrWhiteSpace(current)) continue;
                    prop.SetValue(settings, "{exec}/" + folder);
                    Plugin.Logger.LogInfo("data folders: " + field + " empty (beside the ROM) -> {exec}/" + folder + ", kept in the emulator's settings");
                }
                catch (Exception ex) { Plugin.Logger.LogWarning("data folders: " + field + " - " + ex.Message); }
            }
        }
    }
}
