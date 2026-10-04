// "Apply to my emulators" of the Nixx window's "Your console" tab, for melonDS - two rows:
//   ONE ROW, the owner melonDS shows: the identity's name, language, birthday and colour in [Instance0.Firmware]
//   (MelonDsGameSettings.WriteOwn) - what its own firmware and DSiWare show - and, when it boots on a DS firmware dump, the
//   same written into OUR COPY of it (MelonDsFirmware.UseIdentity; the dump in bios\ never).
// The DSi consoles are not in it: one made from a blank NAND was set up as the identity, one made from a set-up NAND keeps its
// owner (Mehdi, 03/10). Found by the relay by its name, LbIntegrations.MelonDs.IdentityTarget.

using System.Collections.Generic;
using System.Globalization;
using LbIntegrations.Dsi;
using LbIntegrations.Identity;

namespace LbIntegrations.MelonDs
{
    internal static class IdentityTarget
    {
        private static MelonDsLayout Layout()
        {
            var exe = MelonDsPlugin.OurExecutable();
            return exe == null ? null : MelonDsPaths.Resolve(exe);
        }

        private static Dictionary<string, string> Wanted(PackIdentity id)
        {
            var v = new Dictionary<string, string>(System.StringComparer.Ordinal)
            {
                ["FwLanguage"] = id.DsLanguage().ToString(CultureInfo.InvariantCulture),
                ["FwBirthdayMonth"] = id.BirthMonth.ToString(CultureInfo.InvariantCulture),
                ["FwBirthdayDay"] = id.BirthDay.ToString(CultureInfo.InvariantCulture),
                ["FwColour"] = id.Colour.ToString(CultureInfo.InvariantCulture),
            };
            if (id.DsNickname().Length > 0) v["FwUsername"] = id.DsNickname();
            return v;
        }

        private static string Say(IDictionary<string, string> v)
        {
            string Get(string k, string d) => v.TryGetValue(k, out var x) ? x : d;
            int.TryParse(Get("FwLanguage", "1"), out var l);
            int.TryParse(Get("FwColour", "0"), out var c);
            return "\"" + Get("FwUsername", "melonDS") + "\", " + (l >= 0 && l < MelonDsGameSettings.Languages.Length ? MelonDsGameSettings.Languages[l] : "language " + l)
                   + ", " + Get("FwBirthdayMonth", "1") + "/" + Get("FwBirthdayDay", "1") + ", " + MelonDsGameSettings.Colours[System.Math.Min(15, System.Math.Max(0, c))].ToLowerInvariant();
        }

        public static string[][] Plan()
        {
            var id = PackIdentity.Load();
            var layout = id == null ? null : Layout();
            if (layout?.ConfigFile == null) return new string[0][];
            var running = DsiNand.EmulatorRunning() ? "melonDS is running - close it first" : "";
            var now = MelonDsGameSettings.Current(layout.ConfigFile);
            var next = new Dictionary<string, string>(now, System.StringComparer.Ordinal);
            foreach (var kv in Wanted(id)) next[kv.Key] = kv.Value;
            // ONE ROW: the owner melonDS shows - its Firmware settings, and the copy of the DS firmware dump it boots on, which
            // follows them when the dump's owner is set to yours (MelonDsFirmware).
            var active = MelonDsFirmware.Active(layout);
            string label = "melonDS - the owner it shows", nowSaid = Say(now);
            if (active != null)
            {
                var a = active.Value;
                label += " (and in its copy of " + a.Dump + ")";
                if (!a.Identity || a.Owner.Describe() != MelonDsFirmware.Wanted(layout).Describe()) nowSaid += "; " + a.Dump + ": " + a.Owner.Describe();
            }
            return new[] { PackIdentity.Row("melonds", label, nowSaid, Say(next), running) };
        }

        public static string Apply(string key)
        {
            try
            {
                var id = PackIdentity.Load();
                var layout = id == null ? null : Layout();
                if (layout?.ConfigFile == null) return "no melonDS";
                var error = MelonDsGameSettings.WriteOwn(layout, Wanted(id));
                if (error != null) return error;
                // Then the dump's copy, from those very settings - when melonDS boots on one.
                return MelonDsFirmware.Active(layout) == null ? null : MelonDsFirmware.UseIdentity(layout);
            }
            catch (System.Exception ex) { Log.Warn("console identity: could not apply", ex); return ex.Message; }
        }
    }
}
