// no$gba opened WITHOUT A GAME by the host (LaunchBox's "Open emulator", or a host that says so) - see
// LbEmulatorOpened in src\Catalog\LbCatalog.cs.
//
// BEFORE IT STARTS, a game's settings a session left in NO$GBA.INI go back to no$gba's own
// (NoGbaGameSettings): otherwise the user would open no$gba to change its own settings and find, and
// save with "Save Now", a game's instead.

using System;
using LbIntegrations.Catalog;

namespace LbIntegrations.NoGba
{
    internal sealed class NoGbaEmulatorOpened : ILbEmulatorOpened
    {
        public void BeforeOpen(string exePath)
        {
            try
            {
                if (!NoGbaPaths.IsNoGbaExecutable(exePath)) return;
                NoGbaGameSettings.Restore(NoGbaPaths.Resolve(exePath), "no$gba is opened on its own");
            }
            catch (Exception ex) { Log.Warn("no$gba opened on its own: could not put things right first", ex); }
        }

        public void AfterExit(string exePath)
        {
            if (NoGbaPaths.IsNoGbaExecutable(exePath)) Log.Info("no$gba, opened on its own, has quit");
        }
    }
}
