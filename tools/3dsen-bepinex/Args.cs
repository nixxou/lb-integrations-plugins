// The command line the LaunchBox plugin gives 3dSen, read by this plugin. Everything of ours starts with "--nixx-"; 3dSen
// itself reads only "-id=<profile>" and Unity its "-screen-*" switches (docs\3dsen.md section 3), so ours are left alone by
// both. One argument each, "--name=value"; a value with spaces is ONE quoted argument (Windows unquotes it).
//
//   --nixx-data=<folder>       3dSen's data folder (Info.DataPath: rom.json, input.cfg, setting.cfg, CustomProfiles,
//                              CoverArt, Export) instead of LocalLow\Geod Studio\3dSen - the pack's portable mode
//   --nixx-states=<folder>     its states (sav.cfg and the slot files) instead of <data>\sav - the game's platform's
//   --nixx-battery=<folder>    the cartridge's battery RAM kept in <folder>\<rom>.srm (Battery; 3dSen keeps none)
//   --nixx-slots=on|off        F6 / F7 pick the slot (quick, 1..36), F2 / F4 save / load it (default off)
//   --nixx-quit-confirm=on|off Esc twice to quit, "Press Esc again" shown between (default on); Esc is ours only when given
//   --nixx-esc=on|off          Esc quits at all (default off: 3dSen's own Esc, its menu, stays)
//   --nixx-set:<Name>=<value>  a property of 3dSen's SettingManager, for this session (Overrides)
//   --nixx-persist=on|off      write those into setting.cfg too (default off: 3dSen's own settings come back at quit)
//   --nixx-log                 the full log in <data>\nixx.log
//   --nixx-dump-options        <data>\nixx-options.txt: every setting and every list of 3dSen's menus, with their values

using System;
using System.Collections.Generic;
using System.Globalization;

namespace LbIntegrations.ThreeDSen.Mod
{
    internal sealed class Args
    {
        public string Data, States, Battery;
        public bool Slots, QuitConfirm = true, Esc, Persist, Log, Dump;
        public readonly List<KeyValuePair<string, string>> Sets = new List<KeyValuePair<string, string>>();
        public readonly List<string> Unknown = new List<string>();

        public static Args Parse(string[] argv)
        {
            var a = new Args();
            foreach (var raw in argv ?? new string[0])
            {
                if (raw == null || !raw.StartsWith("--nixx-", StringComparison.OrdinalIgnoreCase)) continue;
                var body = raw.Substring(7);
                if (body.StartsWith("set:", StringComparison.OrdinalIgnoreCase))
                {
                    var kv = body.Substring(4);
                    int eq = kv.IndexOf('=');
                    if (eq > 0) a.Sets.Add(new KeyValuePair<string, string>(kv.Substring(0, eq).Trim(), kv.Substring(eq + 1)));
                    else a.Unknown.Add(raw);
                    continue;
                }
                int at = body.IndexOf('=');
                var name = (at < 0 ? body : body.Substring(0, at)).ToLowerInvariant();
                var value = at < 0 ? null : body.Substring(at + 1);
                switch (name)
                {
                    case "data": a.Data = Path(value); break;
                    case "states": a.States = Path(value); break;
                    case "battery": a.Battery = Path(value); break;
                    case "slots": a.Slots = Bool(value, true); break;
                    case "quit-confirm": a.QuitConfirm = Bool(value, true); break;
                    case "esc": a.Esc = Bool(value, true); break;
                    case "persist": a.Persist = Bool(value, true); break;
                    case "log": a.Log = Bool(value, true); break;
                    case "dump-options": a.Dump = Bool(value, true); break;
                    default: a.Unknown.Add(raw); break;
                }
            }
            return a;
        }

        private static string Path(string v) => string.IsNullOrWhiteSpace(v) ? null : v.Trim().Trim('"');

        /// <summary>true/false/on/off/1/0/yes/no; no value: <paramref name="bare"/>.</summary>
        internal static bool Bool(string v, bool bare)
        {
            if (v == null) return bare;
            switch (v.Trim().ToLowerInvariant())
            {
                case "1": case "on": case "true": case "yes": return true;
                case "0": case "off": case "false": case "no": return false;
                default: return bare;
            }
        }

        internal static bool TryNumber(string v, out double d)
            => double.TryParse((v ?? "").Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out d);
    }
}
