// PPSSPP opened WITHOUT A GAME by the host (LaunchBox's "Open emulator", or a host that says so) - see
// LbEmulatorOpened in src\Catalog\LbCatalog.cs.
//
// BEFORE IT STARTS, a game config a session left over its own goes back (PpssppGameSettings): otherwise the
// user would open PPSSPP to change a game's settings and find, and edit, the session's instead.

using System;
using LbIntegrations.Catalog;

namespace LbIntegrations.Ppsspp
{
    internal sealed class PpssppEmulatorOpened : ILbEmulatorOpened
    {
        public void BeforeOpen(string exePath)
        {
            try
            {
                if (!PpssppPaths.IsPpssppExecutable(exePath)) return;
                PpssppGameSettings.Restore(PpssppPaths.Resolve(exePath), "PPSSPP is opened on its own");
            }
            catch (Exception ex) { Log.Warn("PPSSPP opened on its own: could not put things right first", ex); }
        }

        public void AfterExit(string exePath)
        {
            if (PpssppPaths.IsPpssppExecutable(exePath)) Log.Info("PPSSPP, opened on its own, has quit");
        }
    }
}
