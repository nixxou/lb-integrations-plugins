// "Apply to my emulators" of the Nixx window's "Your console" tab, for melonDS - two rows:
//   its Firmware settings   the identity's name, language, birthday and colour in [Instance0.Firmware] (MelonDsGameSettings
//                           .WriteOwn): what the firmware melonDS makes itself always shows, and what the override shows
//   the DS firmware dump    when melonDS boots on one: the identity over its owner - the override on (MelonDsFirmware
//                           .UseIdentity), the dump's copy and its answer as the first launch on it would have left them
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
            var rows = new List<string[]> { PackIdentity.Row("melonds", "melonDS - its Firmware settings", Say(now), Say(next), running) };
            var active = MelonDsFirmware.Active(layout);
            if (active != null)
            {
                var a = active.Value;
                rows.Add(PackIdentity.Row("melonds-dump", "melonDS - DS firmware dump " + a.Dump,
                    (a.Override ? "your console shown over its owner, " : "its owner shown, ") + a.Owner.Describe(),
                    "your console shown over its owner", running));
            }
            return rows.ToArray();
        }

        public static string Apply(string key)
        {
            try
            {
                var id = PackIdentity.Load();
                var layout = id == null ? null : Layout();
                if (layout?.ConfigFile == null) return "no melonDS";
                if (key == "melonds-dump") return MelonDsFirmware.UseIdentity(layout);
                return MelonDsGameSettings.WriteOwn(layout, Wanted(id));
            }
            catch (System.Exception ex) { Log.Warn("console identity: could not apply", ex); return ex.Message; }
        }
    }
}
