// Is an emulator of a plugin among those a game can be launched with - its own emulator, or one LaunchBox's "Launch With"
// lists for it (Mehdi, 04/10: "j'aimerais avoir les options à partir du moment où c'est dans les Launch With")? A plugin's
// right-click entry shows then, beside another emulator's when the game is set to that one: an Xbox game on Cxbx-Reloaded
// gets "Nixx-Cxbx : Options..." and "Nixx-Xemu : Options..." both.
//
// "Launch With" offers the emulators whose platforms (Edit Emulator > Associated Platforms) hold the game's platform. Asked at
// every right-click: lookups in memory only.

using System;
using System.Linq;
using Unbroken.LaunchBox.Plugins;
using Unbroken.LaunchBox.Plugins.Data;

namespace LbIntegrations.Lbip
{
    internal static class LbipLaunchWith
    {
        /// <param name="isOurs">whether an emulator's application path (as LaunchBox holds it, maybe relative) is the plugin's</param>
        public static bool Offers(IGame game, Func<string, bool> isOurs)
        {
            try
            {
                var dm = PluginHelper.DataManager;
                if (dm == null || game == null) return false;
                var ownId = game.EmulatorId;
                var own = string.IsNullOrWhiteSpace(ownId) ? null : dm.GetEmulatorById(ownId);
                if (own != null && isOurs(own.ApplicationPath)) return true;
                var platform = game.Platform;
                if (string.IsNullOrWhiteSpace(platform)) return false;
                foreach (var e in dm.GetAllEmulators() ?? new IEmulator[0])
                {
                    if (e == null || !isOurs(e.ApplicationPath)) continue;
                    IEmulatorPlatform[] rows;
                    try { rows = e.GetAllEmulatorPlatforms() ?? Array.Empty<IEmulatorPlatform>(); } catch { continue; }
                    if (rows.Any(r => r != null && string.Equals(r.Platform, platform, StringComparison.OrdinalIgnoreCase))) return true;
                }
                return false;
            }
            catch { return false; }
        }
    }
}
