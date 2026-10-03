// "Apply to my emulators" of the Nixx window's "Your console" tab, for Cxbx-Reloaded: nothing to write - its Language option's
// default reads the identity at every launch (CxbxEeprom.WindowsLanguage). Its row says so, greyed, so nobody wonders why the
// Xbox is not in the list. Found by the relay by its name, LbIntegrations.Cxbx.IdentityTarget.

using LbIntegrations.Identity;

namespace LbIntegrations.Cxbx
{
    internal static class IdentityTarget
    {
        public static string[][] Plan()
        {
            if (PackIdentity.Load() == null) return new string[0][];
            return new[] { PackIdentity.Row("cxbx", "Cxbx-Reloaded - the Xbox's language", "", "",
                                            "nothing to write: it takes your console's language at every launch") };
        }

        public static string Apply(string key) => null;
    }
}
