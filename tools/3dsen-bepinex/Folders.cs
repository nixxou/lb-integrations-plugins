// --nixx-data and --nixx-states: where 3dSen keeps its files (Mehdi 19/10: the pack's portable mode).
//
// 3dSen takes every path from Info.DataPath (read in 1.1.2: GameDB rom.json / CustomProfiles / CoverArt, Gamepads input.cfg,
// SettingManager setting.cfg, SaveStateDB sav.cfg and its "sav" folder, Info.ExportPath), set ONCE in Info.Awake to Unity's
// persistentDataPath (LocalLow\Geod Studio\3dSen, shared by every copy). A postfix of Info.Awake sets it to --nixx-data
// before any of those reads it (Info's Awake runs first: GameDB.Awake already needs DataPath).
//
// The states go to --nixx-states (the game's platform's folder of the install): SaveStateDB.SavFolder (a static, combined
// with DataPath in SaveStateDB.Awake - an absolute folder stays itself) set in a prefix of that Awake, and the getter
// SaveStateDB.ConfigPath (sav.cfg) answered by a postfix. Unity's own Player.log stays where Unity writes it.

using System;
using System.IO;
using HarmonyLib;

namespace LbIntegrations.ThreeDSen.Mod
{
    internal static class Folders
    {
        private static Args _args;
        private static NixxLog _log;

        public static void Install(Harmony h, Args args, NixxLog log)
        {
            _args = args; _log = log;
            if (args.Data != null)
            {
                var awake = Game.Info == null ? null : AccessTools.Method(Game.Info, "Awake");
                if (awake == null) log.Error("Info.Awake not found - 3dSen keeps its data in LocalLow");
                else h.Patch(awake, postfix: new HarmonyMethod(typeof(Folders), nameof(AfterInfoAwake)));
            }
            if (args.States != null && Game.SaveStateDB != null)
            {
                var awake = AccessTools.Method(Game.SaveStateDB, "Awake");
                var cfg = AccessTools.PropertyGetter(Game.SaveStateDB, "ConfigPath");
                if (awake == null || cfg == null || AccessTools.Field(Game.SaveStateDB, "SavFolder") == null)
                    log.Error("SaveStateDB.Awake / ConfigPath / SavFolder not found - the states stay beside the data");
                else
                {
                    h.Patch(awake, prefix: new HarmonyMethod(typeof(Folders), nameof(BeforeStatesAwake)));
                    h.Patch(cfg, postfix: new HarmonyMethod(typeof(Folders), nameof(ConfigPath)));
                }
            }
        }

        private static void AfterInfoAwake()
        {
            try
            {
                Directory.CreateDirectory(_args.Data);
                AccessTools.PropertySetter(Game.Info, "DataPath").Invoke(null, new object[] { _args.Data });
                var export = AccessTools.PropertySetter(Game.Info, "ExportPath");
                export?.Invoke(null, new object[] { Path.Combine(_args.Data, "Export") });
                _log.Info("data folder: " + _args.Data);
            }
            catch (Exception ex) { _log.Error("the data folder could not be set: " + (ex.InnerException ?? ex).Message); }
        }

        private static void BeforeStatesAwake()
        {
            try
            {
                Directory.CreateDirectory(_args.States);
                AccessTools.Field(Game.SaveStateDB, "SavFolder").SetValue(null, _args.States);
                _log.Info("states folder: " + _args.States);
            }
            catch (Exception ex) { _log.Error("the states folder could not be set: " + (ex.InnerException ?? ex).Message); }
        }

        private static void ConfigPath(ref string __result)
        {
            if (_args.States != null) __result = Path.Combine(_args.States, "sav.cfg");
        }
    }
}
