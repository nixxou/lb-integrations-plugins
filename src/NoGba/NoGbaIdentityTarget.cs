// "Apply to my emulators" of the Nixx window's "Your console" tab, for no$gba: the identity as the owner its DS games show,
// written into FIRMWARE.BIN - its copy of the firmware dump (NoGbaFirmware). Its DSiWare consoles are never in it. Found by the
// relay by its name, LbIntegrations.NoGba.IdentityTarget.

using LbIntegrations.Dsi;
using LbIntegrations.Identity;

namespace LbIntegrations.NoGba
{
    internal static class IdentityTarget
    {
        public static string[][] Plan()
        {
            var id = PackIdentity.Load();
            var layout = id == null ? null : NoGbaSettingsPage.Layout();
            if (layout == null) return new string[0][];
            var active = NoGbaFirmware.Active(layout);
            if (active == null)
                return new[] { PackIdentity.Row("nogba", "no$gba - the owner its DS games show", "", "", "no copy of a DS firmware dump of yours to write into") };
            return new[] { PackIdentity.Row("nogba", "no$gba - the owner its DS games show (FIRMWARE.BIN)", active.Value.Owner.Describe(), DsOwner.Of(id).Describe(),
                                            DsiNand.EmulatorRunning() ? "no$gba is running - close it first" : "") };
        }

        public static string Apply(string key)
        {
            var layout = NoGbaSettingsPage.Layout();
            return layout == null ? "no no$gba" : NoGbaFirmware.SetOwner(layout, identity: true);
        }
    }
}