// A keyboard mapping for a melonDS that has none.
//
// MELONDS SHIPS WITH NOTHING BOUND. Its default table gives Instance*.Keyboard and
// Instance*.Joystick the value -1 (Config.cpp:51-52) and there is no table of defaults anywhere else
// - no "restore defaults" in the input dialog, no first-run initialisation. So a melonDS installed
// by this plugin starts up unable to play: every DS button is unmapped, and the only way forward is
// Config > Input and twelve clicks. That is not a state a frontend should hand somebody.
//
// THE VALUES ARE Qt KEY CODES, not scancodes and not characters. EmuInstance reads them straight out
// of [Instance0.Keyboard] and compares them with what Qt reports for a key press
// (EmuInstanceInput.cpp:118-130, :302-318). Verified against a real configuration: 16777234 is
// Qt::Key_Left, 16777220 is Qt::Key_Return, 32 is space, and a letter is its uppercase ASCII value.
//
// Because Qt names a key by the character it PRODUCES, a mapping written in letters follows whatever
// layout its owner uses: Qt::Key_A is the key labelled A, wherever the keyboard puts it.
//
// THE PAD IS DELIBERATELY LEFT ALONE. melonDS opens SDL_GameController only for rumble and sensors
// (EmuInstanceInput.cpp:244-261) and reads every button through SDL_JoystickGetButton, GetHat and
// GetAxis - raw indices, which mean different things on different hardware. A default there would be
// a guess about somebody's controller rather than a fact about melonDS, so that one is theirs to
// make. A keyboard is not the same case: Qt names a key by the character it produces, so a mapping
// in letters follows whatever layout its owner has.
//
// NOTHING ALREADY BOUND IS EVER TOUCHED. Only a key that is -1 or missing is written, so a mapping
// somebody made is theirs and stays theirs - including a deliberately unbound button.

using System;
using System.Collections.Generic;
using System.Globalization;
using LbIntegrations.Dsi;

namespace LbIntegrations.MelonDs
{
    internal static class MelonDsInput
    {
        /// <summary>The table melonDS reads the keyboard from, for instance 0.</summary>
        public const string KeyboardTable = "Instance0.Keyboard";

        // Qt key codes, the handful that are not a letter's ASCII value.
        private const int Left = 0x01000012;        // 16777234
        private const int Up = 0x01000013;
        private const int Right = 0x01000014;
        private const int Down = 0x01000015;
        private const int Return = 0x01000004;      // 16777220
        private const int Backspace = 0x01000003;
        private const int Tab = 0x01000001;

        private static int Letter(char c) => char.ToUpperInvariant(c);

        /// <summary>The layout a DS player expects, and the one DeSmuME has used for twenty years:
        /// the D-pad on the arrows, the face buttons in a cluster under the left hand, the shoulders
        /// above them.
        ///
        /// It is a convention rather than a measurement - melonDS has none to copy - so it is written
        /// out here to be argued with rather than buried in a call.</summary>
        private static readonly (string Key, int Code, string Why)[] Buttons =
        {
            ("Up", Up, "arrow keys for the D-pad"),
            ("Down", Down, null),
            ("Left", Left, null),
            ("Right", Right, null),

            ("A", Letter('X'), "A and B where a DS player's thumb expects them"),
            ("B", Letter('Z'), null),
            ("X", Letter('S'), "X and Y above them"),
            ("Y", Letter('A'), null),

            ("L", Letter('Q'), "the shoulders above the face buttons"),
            ("R", Letter('W'), null),

            ("Start", Return, "Start on Return, Select on Backspace"),
            ("Select", Backspace, null),

            // TWO HOTKEYS, AND ONLY TWO. The rest are a matter of taste and an unbound hotkey costs
            // nothing - but these two cost a game.
            //
            // HK_Lid is not a convenience: several DS games require the lid to be closed to be
            // finished at all - Phantom Hourglass stamps a map that way, Hotel Dusk has a puzzle
            // that needs it. Unbound, those games cannot be completed and nothing says why.
            ("HK_Lid", Letter('L'), "closing the lid, which some games require to be finished"),
            ("HK_FastForward", Tab, "fast forward while held"),
        };

        /// <summary>Beside the log, like every other switch here. Present, no key is ever bound and
        /// melonDS is left exactly as it ships - which is to say unplayable until somebody opens
        /// Config > Input, but that is then a choice rather than an accident.</summary>
        public const string KillSwitch = "no-melonds-input";

        /// <summary>Bind what is not bound. Returns how many keys were written, or -1 on failure.
        ///
        /// Never overwrites: a key with any value other than -1 has been answered by somebody, and
        /// "unbound on purpose" is an answer too - which is why an ABSENT key and an EXPLICIT -1 are
        /// treated the same way only when nothing else in the table is set. See ShouldBind.</summary>
        public static int EnsureDefaults(MelonDsLayout layout, out string error)
        {
            error = null;
            try
            {
                if (layout?.ConfigFile == null) { error = "no configuration to write to"; return -1; }
                if (Log.Disabled(KillSwitch)) return 0;

                var names = new List<string>();
                foreach (var button in Buttons) names.Add(button.Key);
                var current = MelonDsToml.Read(layout.ConfigFile, KeyboardTable, names.ToArray());

                var wanted = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (var button in Buttons)
                {
                    var set = current.TryGetValue(button.Key, out var value) ? value : null;
                    if (!ShouldBind(set)) continue;
                    wanted[button.Key] = button.Code.ToString(CultureInfo.InvariantCulture);
                }
                if (wanted.Count == 0) return 0;

                error = MelonDsToml.Write(layout.ConfigFile, KeyboardTable, wanted, force: true);
                if (error != null) return -1;

                Log.Info("bound " + wanted.Count + " unmapped key(s) in " + KeyboardTable
                         + " - melonDS ships with nothing bound at all, so a fresh install cannot be "
                         + "played until somebody does this. Anything already mapped was left alone.");
                return wanted.Count;
            }
            catch (Exception ex) { error = ex.GetType().Name + ": " + ex.Message; return -1; }
        }

        /// <summary>Is every DS button unbound? That is a configuration nobody can play, and the only
        /// case where this plugin touches the mapping of an installation it did not make.</summary>
        public static bool NothingIsBound(MelonDsLayout layout)
        {
            try
            {
                if (layout?.ConfigFile == null) return false;
                var names = new[] { "A", "B", "X", "Y", "L", "R", "Up", "Down", "Left", "Right",
                                    "Start", "Select" };
                var current = MelonDsToml.Read(layout.ConfigFile, KeyboardTable, names);
                foreach (var name in names)
                {
                    var set = current.TryGetValue(name, out var value) ? value : null;
                    if (!ShouldBind(set)) return false;       // something is bound; leave it alone
                }
                return true;
            }
            catch { return false; }
        }

        /// <summary>Absent, blank, or melonDS's own "nothing here" value.</summary>
        private static bool ShouldBind(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return true;
            return int.TryParse(value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture,
                                out var number) && number < 0;
        }

        /// <summary>The mapping, in words, for the note left beside a fresh install.</summary>
        public static IEnumerable<string> Describe()
        {
            foreach (var button in Buttons)
                if (button.Why != null) yield return button.Why;
        }
    }
}
