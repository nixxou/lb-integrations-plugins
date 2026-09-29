// Flycast opened WITHOUT A GAME by the host (LaunchBox's "Open emulator", or a host that says so) - see
// LbEmulatorOpened in src\Catalog\LbCatalog.cs.
//
// BEFORE IT STARTS, a game's keys a session took out of its section go back (FlycastGameConfigSession):
// otherwise the user would open Flycast to change a game's config and find it without them.

using System;
using LbIntegrations.Catalog;

namespace LbIntegrations.Flycast
{
    internal sealed class FlycastEmulatorOpened : ILbEmulatorOpened
    {
        public void BeforeOpen(string exePath)
        {
            try
            {
                if (!FlycastPaths.IsFlycastExecutable(exePath)) return;
                FlycastGameConfigSession.Restore(FlycastPaths.Resolve(exePath), "Flycast is opened on its own");
            }
            catch (Exception ex) { Log.Warn("Flycast opened on its own: could not put things right first", ex); }
        }

        public void AfterExit(string exePath)
        {
            if (FlycastPaths.IsFlycastExecutable(exePath)) Log.Info("Flycast, opened on its own, has quit");
        }
    }
}
