// The icon of a plugin's right-click entry (Mehdi, 29/09): its EMULATOR's own, taken from the executable of
// the first emulator of the library that is ours - no picture shipped, and it follows the emulator's build.
//
// Compiled into each plugin that has a game menu (a folder of its own, not Shared.Lbip: that one is also
// built into plugins without WinForms, and this needs System.Drawing).
//
// Asked when the entry is built - the relay (src\Menus\Menus.cs) then keeps it. NEVER NULL: the host turns it
// into its own menu image, and a null there can cost the whole entry. A plain icon until an executable of
// ours is found, kept only once one is.

using System;
using System.Drawing;
using System.IO;
using Unbroken.LaunchBox.Plugins;
using Unbroken.LaunchBox.Plugins.Data;

namespace LbIntegrations.Lbip
{
    internal static class LbipMenuIcon
    {
        private static Image _icon;

        /// <param name="isOurs">Is this application path one of our emulator's?</param>
        /// <param name="resolve">A library path made full.</param>
        public static Image Of(Func<string, bool> isOurs, Func<string, string> resolve)
        {
            if (_icon != null) return _icon;
            try
            {
                foreach (var e in PluginHelper.DataManager?.GetAllEmulators() ?? new IEmulator[0])
                {
                    string app = null;
                    try { app = e?.ApplicationPath; } catch { }
                    if (string.IsNullOrWhiteSpace(app) || !isOurs(app)) continue;
                    var exe = resolve(app);
                    if (string.IsNullOrEmpty(exe) || !File.Exists(exe)) continue;
                    var icon = FromExecutable(exe);
                    if (icon != null) return _icon = icon;
                }
            }
            catch { }
            return SystemIcons.Application.ToBitmap();
        }

        /// <summary>The executable's first icon, large enough to stay sharp on a scaled display; null when it
        /// has none.</summary>
        internal static Image FromExecutable(string exe)
        {
            try
            {
                using var big = Icon.ExtractIcon(exe, 0, 48);
                if (big != null) return big.ToBitmap();
            }
            catch { }
            try
            {
                using var associated = Icon.ExtractAssociatedIcon(exe);
                return associated?.ToBitmap();
            }
            catch { return null; }
        }
    }
}
