// What is left of a game's OWN settings laid over its PPSSPP config (Mehdi, 29/09 - gone 04/10).
//
// A GAME'S OWN SETTINGS ARE PPSSPP'S OWN: its "Game settings" (<memstick>\PSP\SYSTEM\<GAME ID>_ppsspp.ini, loaded when
// the game boots - Core/PSPLoaders.cpp, LoadGameConfig). Until 04/10 this plugin also had an options window of its own
// (Graphics, System, Hacks, Advanced), its values written over that file for a session - the file set aside FIRST as
// <ID>_ppsspp.ini.lbip-bak, a copy or an EMPTY file when there was none, and put back once PPSSPP had quit - and the
// renderer given as --graphics=. All gone; the window keeps a game's updates only (PpssppUpdatesForm).
//
// WHAT STAYS: a .lbip-bak left behind by such a session (the host or the machine went mid-session) is put back - at every
// launch, at the plugin's start-up check, and when PPSSPP is opened without a game (LbEmulatorOpened). The values saved
// then, <install>\lbip-settings*.tsv, are no longer read.

using System;
using System.IO;

namespace LbIntegrations.Ppsspp
{
    internal static class PpssppGameSettings
    {
        private const string BakSuffix = ".lbip-bak", SessionSuffix = ".lbip-session";

        /// <summary>Put back every set-aside game config of this install. Never while PPSSPP runs.</summary>
        public static void Restore(PpssppLayout layout, string why)
        {
            try
            {
                if (layout?.SystemDir == null || !Directory.Exists(layout.SystemDir)) return;
                var baks = Directory.GetFiles(layout.SystemDir, "*_ppsspp.ini" + BakSuffix);
                if (baks.Length == 0) return;
                if (PpssppIni.RunningEmulatorProcess() != null) { Log.Info("game settings: PPSSPP is running - the game configs go back once it has quit"); return; }
                foreach (var bak in baks)
                {
                    var ini = bak.Substring(0, bak.Length - BakSuffix.Length);
                    try
                    {
                        if (File.Exists(ini + SessionSuffix)) File.Delete(ini + SessionSuffix);
                        if (new FileInfo(bak).Length == 0) { if (File.Exists(ini)) File.Delete(ini); File.Delete(bak); Log.Info(Path.GetFileName(ini) + ": the session's is removed - the game had no config of its own (" + why + ")"); }
                        else { File.Move(bak, ini, overwrite: true); Log.Info(Path.GetFileName(ini) + ": the game's own config is back (" + why + ")"); }
                    }
                    catch (Exception ex) { Log.Warn("could not put " + Path.GetFileName(ini) + " back", ex); }
                }
            }
            catch (Exception ex) { Log.Warn("game settings: could not put the game configs back", ex); }
        }
    }
}
