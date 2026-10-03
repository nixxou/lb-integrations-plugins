// "Apply to my emulators" of the Nixx window's "Your console" tab, for PPSSPP: the identity's nickname, language, date and
// time formats and confirm button into ppsspp.ini's [SystemParam] - the keys PpssppPlugin.ApplyIdentity writes at a first
// install, over what is there. A game's own options still go over them for its session. Found by the relay by its name,
// LbIntegrations.Ppsspp.IdentityTarget.

using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using LbIntegrations.Identity;

namespace LbIntegrations.Ppsspp
{
    internal static class IdentityTarget
    {
        private static readonly string[] Keys = { "NickName", "GameLanguage", "ParamDateFormat", "ParamTimeFormat", "ButtonPreference" };

        private static PpssppLayout Layout()
        {
            var root = PackIdentity.LaunchBoxRoot();
            var exe = root == null ? null : PpssppPaths.FindExecutable(Path.Combine(root, "Emulators", "Nixx-PPSSPP"));
            return exe == null ? null : PpssppPaths.Resolve(exe);
        }

        private static Dictionary<string, string> Wanted(PackIdentity id)
        {
            var v = new Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase)
            {
                ["GameLanguage"] = id.PspLanguage().ToString(CultureInfo.InvariantCulture),
                ["ParamDateFormat"] = id.DateOrder == "mdy" ? "1" : id.DateOrder == "dmy" ? "2" : "0",
                ["ParamTimeFormat"] = id.Clock24 ? "0" : "1",
                ["ButtonPreference"] = id.Confirm == "circle" ? "0" : "1",
            };
            if (id.PspNickname().Length > 0) v["NickName"] = id.PspNickname();
            return v;
        }

        private static readonly string[] Languages = { "Japanese", "English", "French", "Spanish", "German", "Italian", "Dutch", "Portuguese", "Russian", "Korean", "Chinese (traditional)", "Chinese (simplified)" };

        private static string Say(IDictionary<string, string> v)
        {
            string Get(string k) => v.TryGetValue(k, out var x) ? x : null;
            var lang = int.TryParse(Get("GameLanguage"), out var l) ? (l >= 0 && l < Languages.Length ? Languages[l] : l < 0 ? "PPSSPP's language" : "language " + l) : "PPSSPP's language";
            var date = Get("ParamDateFormat") == "1" ? "month/day/year" : Get("ParamDateFormat") == "2" ? "day/month/year" : "year/month/day";
            return "\"" + (Get("NickName") ?? "PPSSPP") + "\", " + lang + ", " + date + ", " + (Get("ParamTimeFormat") == "1" ? "12-hour" : "24-hour")
                   + ", " + (Get("ButtonPreference") == "0" ? "○" : "✕") + " confirms";
        }

        public static string[][] Plan()
        {
            var id = PackIdentity.Load();
            var layout = id == null ? null : Layout();
            if (layout?.ConfigFile == null) return new string[0][];
            var now = PpssppIni.Read(layout.ConfigFile, "SystemParam", Keys);
            var next = new Dictionary<string, string>(now, System.StringComparer.OrdinalIgnoreCase);
            foreach (var kv in Wanted(id)) next[kv.Key] = kv.Value;
            var running = PpssppIni.RunningEmulatorProcess();
            return new[] { PackIdentity.Row("ppsspp", "PPSSPP - system settings", Say(now), Say(next), running != null ? "PPSSPP is running - close it first" : "") };
        }

        public static string Apply(string key)
        {
            try
            {
                var id = PackIdentity.Load();
                var layout = id == null ? null : Layout();
                if (layout?.ConfigFile == null) return "no PPSSPP";
                Directory.CreateDirectory(Path.GetDirectoryName(layout.ConfigFile));
                var error = PpssppIni.Write(layout.ConfigFile, "SystemParam", Wanted(id));
                if (error == null) Log.Info("console identity applied to ppsspp.ini");
                return error;
            }
            catch (System.Exception ex) { Log.Warn("console identity: could not apply", ex); return ex.Message; }
        }
    }
}
