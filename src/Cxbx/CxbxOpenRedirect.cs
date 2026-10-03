// "Open Nixx-Cxbx..." in LaunchBox's menu opens Cxbx-Reloaded's window (Mehdi, 03/10).
//
// The menu opens the entry's executable with no argument. When the entry names the loader (cxbxr-ldr.exe - the "launch
// with Cxbx-Reloaded's window" option off), that is a process with nothing to load: it opens nothing anyone can use.
// The GUI beside it is what someone opening the emulator wants - its settings, its EEPROM, its controllers. Told through
// LbEmulatorRedirect (src\Catalog\LbCatalog.cs), asked by the pack's one Process.Start patch (LbipEmulatorOpened).

using System.IO;
using LbIntegrations.Catalog;

namespace LbIntegrations.Cxbx
{
    internal sealed class CxbxOpenRedirect : ILbEmulatorRedirect
    {
        public string RedirectOpen(string exePath)
        {
            if (!CxbxPaths.IsLoader(exePath)) return null;
            var gui = Path.Combine(Path.GetDirectoryName(exePath) ?? "", CxbxPaths.Gui);
            if (!File.Exists(gui)) return null;
            Log.Info("opened without a game: Cxbx-Reloaded's window rather than the loader alone");
            return gui;
        }
    }
}
