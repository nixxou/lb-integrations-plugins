// Giving Flycast the keyboard shortcuts it ships without.
//
// THE PROBLEM. Flycast binds no key to saving or loading a state, and none to quitting. Measured in
// core/input/keyboard_device.h:44-67: KeyboardInputMapping binds Tab (menu), Space (fast forward)
// and F12 (screenshot), and nothing else beyond the Dreamcast pad. EMU_BTN_SAVESTATE,
// EMU_BTN_LOADSTATE, EMU_BTN_NEXTSLOT, EMU_BTN_PREVSLOT and EMU_BTN_ESCAPE all work
// (core/input/gamepad_device.cpp:100-145) - they are simply attached to nothing.
//
// So a fresh install can only save a state through Tab and the menu, and Escape does nothing at all,
// which means a front-end's Exit button cannot close Flycast cleanly and a VMU write in flight is
// lost. LaunchBox's answer to this kind of gap is a comment in the AutoHotkey script telling the user
// to go and bind it themselves - their Dolphin entry says exactly that about Reset. We install the
// emulator, so we can do the binding.
//
// THE TRAP, and it is the whole reason this file is careful. A mapping file does not ADD to the
// defaults, it REPLACES them: GamepadDevice::find_mapping (core/input/gamepad_device.cpp:609) uses
// getDefaultMapping() only when no file is found. Writing a file with four hotkeys in it would leave
// the user with four hotkeys and no Dreamcast controls. Every default below is therefore copied from
// keyboard_device.h, with its key in a comment, and the probe compares the two tables.
//
// ONE FILE COVERS ALL FOUR PLATFORMS. Same function, lines 645-648: for a non-Dreamcast system with
// no "_arcade" file, Flycast retries with system = DC_PLATFORM_DREAMCAST and cloneMapping = true. So
// Naomi, Naomi 2 and Atomiswave inherit this file, and writing SDL_Keyboard_arcade.cfg as well would
// only freeze arcade's mapping separately for no gain.
//
// WE NEVER OVERRULE THE USER. An action already bound keeps its key, a key already used is never
// stolen, and the table we return describes what the file SAYS once written - so the AutoHotkey
// scripts built from it are true even when the user had bound F5 himself.
//
// Flycast does not rewrite this file behind us: save_mapping() is reached only from the Controls
// screen (core/ui/settings_controls.cpp:897) and a gamepad-only path (core/sdl/sdl_gamepad.cpp:683).

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace LbIntegrations.Flycast
{
    /// <summary>What the keyboard mapping says after Ensure has run.</summary>
    internal sealed class HotkeyTable
    {
        /// <summary>Flycast option name (btn_quick_save…) to SDL scancode.</summary>
        public readonly Dictionary<string, int> Bindings =
            new Dictionary<string, int>(StringComparer.Ordinal);

        /// <summary>The file these bindings came from, or null when none could be read or written.</summary>
        public string Path;

        /// <summary>True when this call created or changed the file.</summary>
        public bool Written;

        /// <summary>Why nothing was written, when nothing was. Null on success.</summary>
        public string Skipped;

        public int? KeyFor(string option)
            => Bindings.TryGetValue(option, out var code) ? code : (int?)null;
    }

    internal static class FlycastHotkeys
    {
        public const string FileName = "SDL_Keyboard.cfg";

        // ── the option names, from core/input/mapping.cpp:34-70 ──────────────
        public const string OptSaveState = "btn_quick_save";
        public const string OptLoadState = "btn_jump_state";
        public const string OptNextSlot = "btn_next_slot";
        public const string OptPrevSlot = "btn_prev_slot";
        public const string OptEscape = "btn_escape";

        /// <summary>Flycast's own default keyboard mapping, copied from KeyboardInputMapping's
        /// constructor (core/input/keyboard_device.h:44-67) in its order, with the key each scancode
        /// stands for. This is not decoration: our file replaces the defaults outright, so anything
        /// missing here is a control the user loses.</summary>
        private static readonly (string Option, int Code)[] FlycastDefaultKeys =
        {
            ("btn_a", 27),              // X
            ("btn_b", 6),               // C
            ("btn_x", 22),              // S
            ("btn_y", 7),               // D
            ("btn_dpad1_up", 82),       // Up
            ("btn_dpad1_down", 81),     // Down
            ("btn_dpad1_left", 80),     // Left
            ("btn_dpad1_right", 79),    // Right
            ("btn_start", 40),          // Return
            ("btn_trigger_left", 9),    // F
            ("btn_trigger_right", 25),  // V
            ("btn_menu", 43),           // Tab
            ("btn_fforward", 44),       // Space
            ("btn_analog_up", 12),      // I
            ("btn_analog_down", 14),    // K
            ("btn_analog_left", 13),    // J
            ("btn_analog_right", 15),   // L
            ("btn_d", 4),               // Q, the arcade coin
            ("btn_screenshot", 69),     // F12
        };

        /// <summary>Ours. F2 and F4 because that is what RetroArch uses and therefore what LaunchBox
        /// already writes in its own AutoHotkey scripts; Escape because every front-end's Exit means
        /// Escape, and in Flycast it reaches dc_exit().</summary>
        private static readonly (string Option, int Code, string Key)[] OurKeys =
        {
            (OptSaveState, 59, "F2"),
            (OptLoadState, 61, "F4"),
            (OptPrevSlot, 63, "F6"),
            (OptNextSlot, 64, "F7"),
            (OptEscape, 41, "Escape"),
        };

        /// <summary>Flycast's own file format version. Below 3 it reads the pre-2021 layout instead,
        /// and 4 is what a current Flycast writes (core/input/mapping.h:171).</summary>
        private const int FormatVersion = 4;

        private const string DigitalSection = "digital";
        private const string EmulatorSection = "emulator";

        /// <summary>Make sure the keyboard has our shortcuts, and report what it ends up with.
        ///
        /// A file that does not exist is written either way - an emulator with no way to save a state
        /// is not a choice anyone made. <paramref name="mayEditExisting"/> governs only the other
        /// case: false leaves an existing file exactly as it is and merely reads it, which is what
        /// launching a game does, so a Flycast the user set up by hand is described rather than
        /// edited behind his back. Installing passes true.</summary>
        public static HotkeyTable Ensure(FlycastLayout layout, bool mayEditExisting)
        {
            var table = new HotkeyTable();
            try
            {
                if (layout == null || layout.InstallDir.Length == 0)
                {
                    table.Skipped = "no Flycast install to write into";
                    return table;
                }

                var dir = layout.PrimaryMappingsDir;
                var path = System.IO.Path.Combine(dir, FileName);
                table.Path = path;

                var existed = File.Exists(path);
                var current = existed ? ReadBinds(path) : DefaultBinds();

                if (existed && !mayEditExisting)
                {
                    // Read-only pass. The table still describes reality, which is all the AutoHotkey
                    // scripts need.
                    table.Skipped = "the keyboard mapping already exists - left untouched";
                    Fill(table, current);
                    return table;
                }

                var used = new HashSet<int>(current.Values);
                var added = new List<string>();
                foreach (var (option, code, key) in OurKeys)
                {
                    if (current.ContainsKey(option)) continue;   // the user's binding wins
                    if (used.Contains(code)) continue;           // and so does the user's key
                    current[option] = code;
                    used.Add(code);
                    added.Add(key + " -> " + option);
                }

                if (existed && added.Count == 0)
                {
                    table.Skipped = "every shortcut was already bound";
                    Fill(table, current);
                    return table;
                }

                Directory.CreateDirectory(dir);

                var error = WriteBinds(path, current);
                if (error != null)
                {
                    table.Skipped = error;
                    Fill(table, current);
                    return table;
                }

                table.Written = true;
                Fill(table, current);
                Log.Info((existed ? "added to " : "wrote ") + path + ": " + string.Join(", ", added));
                return table;
            }
            catch (Exception ex)
            {
                Log.Warn("could not set up the keyboard shortcuts", ex);
                table.Skipped = ex.GetType().Name + ": " + ex.Message;
                return table;
            }
        }

        private static void Fill(HotkeyTable table, Dictionary<string, int> binds)
        {
            foreach (var pair in binds) table.Bindings[pair.Key] = pair.Value;
        }

        /// <summary>Flycast's defaults, as the starting point for a file that does not exist yet.</summary>
        private static Dictionary<string, int> DefaultBinds()
        {
            var binds = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var (option, code) in FlycastDefaultKeys) binds[option] = code;
            return binds;
        }

        /// <summary>The digital bindings already in the file.
        ///
        /// Port 0 only: an option carrying a port suffix (btn_a2) belongs to another controller port
        /// and is neither ours to move nor ours to count - but its KEY is still taken, so it is kept
        /// under its own name and written back untouched.</summary>
        private static Dictionary<string, int> ReadBinds(string path)
        {
            var binds = new Dictionary<string, int>(StringComparer.Ordinal);

            // The reader takes explicit keys, and Flycast itself stops at the first gap
            // (core/input/mapping.cpp:467-471), so asking for a generous contiguous run is exact.
            var names = Enumerable.Range(0, 256).Select(i => "bind" + i).ToArray();
            var raw = FlycastIni.Read(path, DigitalSection, names);

            for (var i = 0; i < names.Length; i++)
            {
                if (!raw.TryGetValue(names[i], out var line) || string.IsNullOrWhiteSpace(line)) break;

                var colon = line.IndexOf(':');
                if (colon <= 0) continue;
                var codeText = line.Substring(0, colon).Trim();
                var option = line.Substring(colon + 1).Trim();
                if (option.Length == 0) continue;

                // An axis bind would carry a + or - suffix; those live in [analog], but a malformed
                // file could still put one here and it is not ours to interpret.
                if (!int.TryParse(codeText, out var code)) continue;
                binds[option] = code;
            }
            return binds;
        }

        /// <summary>Write the bindings back, renumbered contiguously from bind0 - Flycast stops
        /// reading at the first missing index, so a gap would silently truncate the mapping.
        ///
        /// Everything else in the file survives: FlycastIni.Write rewrites the keys it is given in
        /// place and leaves every other line alone.</summary>
        private static string WriteBinds(string path, Dictionary<string, int> binds)
        {
            var ordered = binds.OrderBy(b => b.Key, StringComparer.Ordinal).ToList();

            var digital = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < ordered.Count; i++)
                digital["bind" + i] = ordered[i].Value + ":" + ordered[i].Key;

            // A file that previously held MORE binds than we are writing would keep its tail, and
            // that tail would contradict us. Blank those indices instead: Flycast stops at the first
            // EMPTY value exactly as it stops at a missing one (core/input/mapping.cpp:469-471), and
            // FlycastIni can rewrite a key but not delete a line.
            var existing = FlycastIni.Read(path, DigitalSection,
                Enumerable.Range(ordered.Count, 64).Select(i => "bind" + i).ToArray());
            foreach (var pair in existing)
                if (!string.IsNullOrWhiteSpace(pair.Value)) digital[pair.Key] = "";

            var error = FlycastIni.Write(path, DigitalSection, digital);
            if (error != null) return error;

            return FlycastIni.Write(path, EmulatorSection, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["mapping_name"] = "Keyboard",
                ["version"] = FormatVersion.ToString(),
            });
        }
    }
}
