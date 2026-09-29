// Vita3K opened WITHOUT A GAME by the host (LaunchBox's "Open emulator", or a host that says so) - see
// LbEmulatorOpened in src\Catalog\LbCatalog.cs.
//
// BEFORE IT STARTS, a game's settings a session left in its custom config go back to the user's own
// (Vita3kGameConfig): otherwise the user would open Vita3K to change a game's Custom Config and find, and
// edit, the session's instead.

using System;
using LbIntegrations.Catalog;

namespace LbIntegrations.Vita3k
{
    internal sealed class Vita3kEmulatorOpened : ILbEmulatorOpened
    {
        public void BeforeOpen(string exePath)
        {
            try
            {
                if (!Vita3kPaths.IsVita3kExecutable(exePath)) return;
                Vita3kGameConfig.Restore(Vita3kPaths.Resolve(exePath), null, "Vita3K is opened on its own");
            }
            catch (Exception ex) { Log.Warn("Vita3K opened on its own: could not put things right first", ex); }
        }

        public void AfterExit(string exePath)
        {
            if (Vita3kPaths.IsVita3kExecutable(exePath)) Log.Info("Vita3K, opened on its own, has quit");
        }
    }
}
