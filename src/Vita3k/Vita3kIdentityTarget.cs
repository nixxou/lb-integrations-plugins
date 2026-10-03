// "Apply to my emulators" of the Nixx window's "Your console" tab, for Vita3K: the identity's language, date and time
// formats and confirm button into config.yml (Vita3kConfig.FromIdentity) - its PlayStation TV mode left as it is. Found by
// the relay by its name, LbIntegrations.Vita3k.IdentityTarget.

using System.Linq;
using LbIntegrations.Identity;

namespace LbIntegrations.Vita3k
{
    internal static class IdentityTarget
    {
        private static Vita3kLayout Layout()
        {
            var exe = Vita3kPlugin.KnownExecutables().FirstOrDefault();
            return exe == null ? null : Vita3kPaths.Resolve(exe);
        }

        private static VitaSystemSettings Next(VitaSystemSettings now)
        {
            var id = Vita3kConfig.FromIdentity();
            if (id == null) return null;
            return new VitaSystemSettings { Language = id.Language, DateFormat = id.DateFormat, TimeFormat = id.TimeFormat, EnterButton = id.EnterButton, Pstv = now.Pstv };
        }

        public static string[][] Plan()
        {
            var layout = Layout();
            if (layout == null || PackIdentity.Load() == null) return new string[0][];
            var now = Vita3kConfig.Read(layout);
            var next = Next(now);
            return new[]
            {
                PackIdentity.Row("vita3k", "Vita3K - system settings", now.ToString(), next?.ToString(),
                                 Vita3kPaths.EmulatorRunning() ? "Vita3K is running - close it first" : ""),
            };
        }

        public static string Apply(string key)
        {
            try
            {
                var layout = Layout();
                if (layout == null) return "no Vita3K";
                if (Vita3kPaths.EmulatorRunning()) return "Vita3K is running - close it first";
                var next = Next(Vita3kConfig.Read(layout));
                if (next == null) return "no console identity";
                if (!Vita3kConfig.Write(layout, next)) return "config.yml could not be written";
                Log.Info("console identity applied: " + next);
                return null;
            }
            catch (System.Exception ex) { Log.Warn("console identity: could not apply", ex); return ex.Message; }
        }
    }
}
