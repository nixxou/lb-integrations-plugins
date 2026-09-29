// melonDS opened WITHOUT A GAME by the host (LaunchBox's "Open emulator", or a host that says so) -
// see LbEmulatorOpened in src\Catalog\LbCatalog.cs.
//
// BEFORE IT STARTS, what a session that never ended left behind is put right: a game's own video
// settings still in melonDS.toml go back to melonDS's own (MelonDsVideo), and a DSiWare session's RAM
// disk is saved and released (MelonDsRamDisk). Otherwise the user would open melonDS to change its own
// settings and find, and edit, a game's instead - the case Mehdi pointed out on 29/09.

using System;
using LbIntegrations.Catalog;

namespace LbIntegrations.MelonDs
{
    internal sealed class MelonDsEmulatorOpened : ILbEmulatorOpened
    {
        public void BeforeOpen(string exePath)
        {
            try
            {
                if (!MelonDsPaths.IsMelonDsExecutable(exePath)) return;
                var layout = MelonDsPaths.Resolve(exePath);
                MelonDsVideo.Restore(layout, "melonDS is opened on its own");
                MelonDsRamDisk.StartUp(layout, MelonDsPlugin.Bios7Of(layout));
            }
            catch (Exception ex) { Log.Warn("melonDS opened on its own: could not put things right first", ex); }
        }

        public void AfterExit(string exePath)
        {
            if (MelonDsPaths.IsMelonDsExecutable(exePath)) Log.Info("melonDS, opened on its own, has quit");
        }
    }
}
