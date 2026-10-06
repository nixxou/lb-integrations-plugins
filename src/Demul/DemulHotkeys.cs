// The save-state keys, bound in Demul's pad plugin - the pack's keys (Mehdi, 07/10: F2 save state, F4 load state,
// F6 / F7 previous / next slot, Esc quit, F11 full screen).
//
// WHERE DEMUL KEEPS THEM, read off demul_251220.7z: padDemul.ini, [GLOBAL0] SAVESTATE / LOADSTATE / PREVSTATE /
// NEXTSTATE ("Global / hot keys" in padDemul.dll's own dialog) - all four shipped at 0, unbound. The values are
// DirectInput scan codes (padDemul.dll opens the keyboard with c_dfDIKeyboard; the shipped table agrees: START = 28,
// Enter; COIN = 6, the 5 key): F1 59, F2 60, F4 62, F5 63, F6 64, F7 65. The slot they act on is the one of File >
// Save States > Slot 0..9, which PREVSTATE / NEXTSTATE move. RetroBat binds the same four to the same keys
// (emulatorLauncher, Demul.Controllers.cs: SAVESTATE 60, LOADSTATE 62, NEXTSTATE 65, PREVSTATE 64).
//
// F2 AND F4 ARE DEMUL'S OWN: the shipped [GLOBAL0] has SERVICE = 60 (F2) and TEST = 62 (F4) - the arcade boards'
// service menu, "F4 then F2 to navigate" on Batocera's wiki. Those two move one key out, F2 -> F1 and F4 -> F5, at the
// moment their key is taken for the states and only while they still hold Demul's shipped value: TEST or SERVICE
// the user set to anything else stays as it is, and so does one he puts back on F4 / F2 later (the move happens once,
// with the binding). Not F3 - this plugin's help sends CV1000 and Gaelco players to it - and not 9 / 0 (RetroBat's),
// already the service coin and TEST2.
//
// NEVER A KEY THE USER BOUND: a state key is set only while it is unbound (0, empty or missing), and only when its
// scan code is held by nothing else in padDemul.ini but the TEST / SERVICE above - else it stays unbound and the log
// says what holds it. No earlier build of this plugin bound anything, so there is nothing to supersede.
//
// Written before Demul starts (at each launch, as Demul.ini): padDemul.dll reads the file when it starts.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace LbIntegrations.Demul
{
    internal static class DemulHotkeys
    {
        public const string FileName = "padDemul.ini";
        private const string Section = "GLOBAL0";

        private sealed class Bind
        {
            public string Action, Code, KeyName, DisplacedKey, DisplacedShipped, DisplacedTo, DisplacedToName;
        }

        private static readonly Bind[] Binds =
        {
            new Bind { Action = "SAVESTATE", Code = "60", KeyName = "F2", DisplacedKey = "SERVICE", DisplacedShipped = "60", DisplacedTo = "59", DisplacedToName = "F1" },
            new Bind { Action = "LOADSTATE", Code = "62", KeyName = "F4", DisplacedKey = "TEST", DisplacedShipped = "62", DisplacedTo = "63", DisplacedToName = "F5" },
            new Bind { Action = "PREVSTATE", Code = "64", KeyName = "F6" },
            new Bind { Action = "NEXTSTATE", Code = "65", KeyName = "F7" },
        };

        /// <summary>Bind what can be bound in the padDemul.ini beside demul.exe. Never throws: a key left as it was is
        /// no reason to refuse a launch.</summary>
        public static void Ensure(string emuDir)
        {
            try
            {
                var path = Path.Combine(emuDir, FileName);
                // Missing: padDemul.dll writes its defaults when it starts - F2 / F4 on SERVICE / TEST again - so nothing
                // is written into a file of our own; the next launch finds Demul's.
                if (!File.Exists(path)) { Log.Info(FileName + " not there yet - state keys left to the next launch"); return; }
                var ini = DemulIni.Load(path);
                var bound = new List<string>();
                foreach (var b in Binds)
                {
                    var now = ini.Get(Section, b.Action);
                    if (!Unbound(now)) continue;
                    var holders = ini.Entries()
                        .Where(e => e.Value == b.Code && !(Is(e.Section, Section) && Is(e.Key, b.Action)))
                        .ToList();
                    bool Displaced((string Section, string Key, string Value) e)
                        => b.DisplacedKey != null && Is(e.Section, Section) && Is(e.Key, b.DisplacedKey) && e.Value == b.DisplacedShipped;
                    var others = holders.Where(e => !Displaced(e)).ToList();
                    if (others.Count > 0)
                    {
                        Log.Info(FileName + ": " + b.KeyName + " is already " + Describe(others) + " - " + b.Action + " left unbound");
                        continue;
                    }
                    if (holders.Count > 0)
                    {
                        var onTarget = ini.Entries().Where(e => e.Value == b.DisplacedTo).ToList();
                        if (onTarget.Count > 0)
                        {
                            Log.Info(FileName + ": " + b.DisplacedKey + " cannot leave " + b.KeyName + " for " + b.DisplacedToName + ", which is "
                                     + Describe(onTarget) + " - " + b.Action + " left unbound");
                            continue;
                        }
                        ini.Set(Section, b.DisplacedKey, b.DisplacedTo);
                        bound.Add(b.DisplacedKey + " " + b.KeyName + " -> " + b.DisplacedToName);
                    }
                    ini.Set(Section, b.Action, b.Code);
                    bound.Add(b.Action + " " + b.KeyName);
                }
                ini.SaveIfChanged();
                if (bound.Count > 0) Log.Info(FileName + ": " + string.Join(", ", bound));
            }
            catch (Exception ex) { Log.Warn(FileName + " could not be written - the state keys stay as they were", ex); }
        }

        private static bool Unbound(string v) => string.IsNullOrWhiteSpace(v) || v.Trim() == "0";

        private static bool Is(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

        private static string Describe(List<(string Section, string Key, string Value)> entries)
            => string.Join(", ", entries.Select(e => "[" + e.Section + "] " + e.Key));
    }
}
