// Confronting the keyboard mapping we write with what Flycast would read back.
//
// The stakes are asymmetric here, and that shapes the assertions. Getting a hotkey wrong costs the
// user a shortcut; getting the file wrong costs him his CONTROLS, because a mapping file replaces
// Flycast's defaults rather than adding to them. So the first and longest check is not that our keys
// are there - it is that none of Flycast's own are missing.
//
// Everything runs on a forged install in the temp folder. Nothing here touches a real Flycast.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Unbroken.LaunchBox.Plugins;
using Unbroken.LaunchBox.Plugins.Data;

namespace LbIntegrations.Probe
{
    internal static class HotkeyCheck
    {
        private static int _fail;

        private static void Check(string what, bool good)
        {
            if (!good) _fail++;
            Console.WriteLine("  " + what.PadRight(54) + (good ? "OK" : "FAIL"));
        }

        /// <summary>Flycast's default keyboard mapping, read INDEPENDENTLY of the plugin - typed out
        /// again from core/input/keyboard_device.h:44-67 rather than borrowed from the table under
        /// test, so that a slip in either copy shows up as a disagreement.</summary>
        private static readonly Dictionary<string, int> FlycastDefaults = new Dictionary<string, int>
        {
            ["btn_a"] = 27, ["btn_b"] = 6, ["btn_x"] = 22, ["btn_y"] = 7,
            ["btn_dpad1_up"] = 82, ["btn_dpad1_down"] = 81,
            ["btn_dpad1_left"] = 80, ["btn_dpad1_right"] = 79,
            ["btn_start"] = 40, ["btn_trigger_left"] = 9, ["btn_trigger_right"] = 25,
            ["btn_menu"] = 43, ["btn_fforward"] = 44,
            ["btn_analog_up"] = 12, ["btn_analog_down"] = 14,
            ["btn_analog_left"] = 13, ["btn_analog_right"] = 15,
            ["btn_d"] = 4, ["btn_screenshot"] = 69,
        };

        public static bool Run(EmulatorPlugin plugin)
        {
            _fail = 0;
            Console.WriteLine();
            Console.WriteLine("-- flycast hotkeys, on a FORGED install  [WRITES, in the temp folder] ---");

            var asm = plugin.GetType().Assembly;
            var hotkeys = asm.GetType("LbIntegrations.Flycast.FlycastHotkeys");
            var paths = asm.GetType("LbIntegrations.Flycast.FlycastPaths");
            var ahk = asm.GetType("LbIntegrations.Flycast.FlycastAhk");
            if (hotkeys == null || paths == null || ahk == null)
            {
                Console.WriteLine("  FAIL - FlycastHotkeys is not in this assembly");
                return false;
            }

            var root = Path.Combine(Path.GetTempPath(), "lbip-hotkeys-" + Guid.NewGuid().ToString("N"));
            try
            {
                var install = Path.Combine(root, "Flycast");
                Directory.CreateDirectory(install);
                var exe = Path.Combine(install, "flycast.exe");
                File.WriteAllBytes(exe, Array.Empty<byte>());
                var mapping = Path.Combine(install, "mappings", "SDL_Keyboard.cfg");

                object Ensure(bool create)
                {
                    var layout = paths.GetMethod("Resolve", BindingFlags.Public | BindingFlags.Static)
                                      .Invoke(null, new object[] { exe });
                    return hotkeys.GetMethod("Ensure", BindingFlags.Public | BindingFlags.Static)
                                  .Invoke(null, new[] { layout, (object)create });
                }

                // ── a fresh install ──────────────────────────────────────────
                var table = Ensure(true);
                Check("a missing mapping file is created", File.Exists(mapping));

                var binds = ReadBinds(mapping);
                Console.WriteLine("  binds written : " + binds.Count);

                var lost = FlycastDefaults.Where(d => !binds.TryGetValue(d.Key, out var c) || c != d.Value)
                                          .Select(d => d.Key).ToList();
                Check("every Flycast default survives", lost.Count == 0);
                if (lost.Count > 0) Console.WriteLine("     lost: " + string.Join(", ", lost));

                Check("F2 saves state", binds.TryGetValue("btn_quick_save", out var f2) && f2 == 59);
                Check("F4 loads state", binds.TryGetValue("btn_jump_state", out var f4) && f4 == 61);
                Check("F6 and F7 cycle the slot",
                      binds.TryGetValue("btn_prev_slot", out var f6) && f6 == 63
                      && binds.TryGetValue("btn_next_slot", out var f7) && f7 == 64);
                Check("Escape quits", binds.TryGetValue("btn_escape", out var esc) && esc == 41);

                // Flycast stops reading at the first gap, so a hole would silently truncate the map.
                Check("the bind indices are contiguous from 0", ContiguousFrom0(mapping, binds.Count));

                // ── the scripts describe the file ────────────────────────────
                Check("the save-state script sends F2", Ahk(ahk, "SaveState", table).Contains("{F2 down}"));
                Check("the load-state script sends F4", Ahk(ahk, "LoadState", table).Contains("{F4 up}"));
                Check("the exit script sends Escape", Ahk(ahk, "Exit", table).Contains("{Escape down}"));

                // ── running again must change nothing ────────────────────────
                var before = File.ReadAllBytes(mapping);
                Ensure(true);
                Check("running twice leaves the file byte for byte",
                      File.ReadAllBytes(mapping).AsSpan().SequenceEqual(before));

                // ── a file we did not write ──────────────────────────────────
                // The user bound save state to F5 himself and uses F4 for something else. Neither may
                // be touched, and the script must quote HIS key, not ours.
                File.WriteAllText(mapping,
                    "[digital]\r\n"
                    + "bind0 = 27:btn_a\r\n"
                    + "bind1 = 62:btn_quick_save\r\n"     // F5
                    + "bind2 = 61:btn_screenshot\r\n"     // F4, taken
                    + "\r\n[analog]\r\nbind0 = 0-:btn_analog_left\r\n"
                    + "\r\n[emulator]\r\nmapping_name = Mine\r\ndead_zone = 17\r\n");

                var mine = File.ReadAllBytes(mapping);
                var readOnly = Ensure(false);
                Check("mayEditExisting false never writes",
                      File.ReadAllBytes(mapping).AsSpan().SequenceEqual(mine));
                Check("it still reports HIS key for save state",
                      Ahk(ahk, "SaveState", readOnly).Contains("{F5 down}"));

                var merged = Ensure(true);
                binds = ReadBinds(mapping);
                Check("his save-state key is left alone",
                      binds.TryGetValue("btn_quick_save", out var his) && his == 62);
                Check("a key he already uses is not stolen",
                      binds["btn_screenshot"] == 61
                      && !binds.TryGetValue("btn_jump_state", out var stolen2));
                Check("the actions he left free are still added",
                      binds.ContainsKey("btn_escape") && binds.ContainsKey("btn_next_slot"));
                Check("an unbindable action gets a comment, not a wrong key",
                      Ahk(ahk, "LoadState", merged).TrimStart().StartsWith(";"));

                var text = File.ReadAllText(mapping);
                Check("his other sections survive",
                      text.Contains("[analog]") && text.Contains("dead_zone = 17"));

                // ── and the scripts must actually reach the emulator entry ───
                //
                // THE case that was broken: an entry that already carries all four platforms. The
                // platform pass returns early for it - there is nothing to add - and the scripts used
                // to hang off that pass's tail, so they were set almost never. Asserted on the
                // commonest shape rather than on a fresh one.
                var emulator = new StubEmulator { Title = "Flycast", ApplicationPath = exe };
                foreach (var name in new[] { "Sega Dreamcast", "Sega Naomi", "Sega Naomi 2", "Sammy Atomiswave" })
                    emulator.AddNewEmulatorPlatform().Platform = name;

                plugin.GetApplicableEmulators(new IEmulator[] { emulator });

                Check("a fully-associated entry still gets its scripts",
                      (emulator.SaveStateAutoHotkeyScript ?? "").Contains("{F5 down}")
                      && (emulator.ExitAutoHotkeyScript ?? "").Contains("{Escape down}"));

                // THE one that bit: a SECOND object for the same Flycast. The host hands us the same
                // emulator under a new object every time a window asks - the entry we create during
                // install, then the one the Add Emulator window is showing. Remembering "this
                // executable is done" filled the first and left the second empty, which is exactly
                // what the user saw: the log said the scripts were set, and the window was blank.
                var second = new StubEmulator { Title = "Flycast", ApplicationPath = exe };
                foreach (var name in new[] { "Sega Dreamcast", "Sega Naomi", "Sega Naomi 2", "Sammy Atomiswave" })
                    second.AddNewEmulatorPlatform().Platform = name;

                plugin.GetApplicableEmulators(new IEmulator[] { second });

                Check("a SECOND object for the same Flycast is filled too",
                      (second.SaveStateAutoHotkeyScript ?? "").Contains("{F5 down}")
                      && (second.LoadStateAutoHotkeyScript ?? "").TrimStart().StartsWith(";")
                      && (second.ExitAutoHotkeyScript ?? "").Contains("{Escape down}"));

                // A script the user wrote is his answer to the same question.
                var scripted = new StubEmulator
                {
                    Title = "Flycast (scripted)",
                    ApplicationPath = exe,
                    SaveStateAutoHotkeyScript = "Send {F9}",
                };
                plugin.GetApplicableEmulators(new IEmulator[] { scripted });
                Check("a script the user wrote is not replaced",
                      scripted.SaveStateAutoHotkeyScript == "Send {F9}");

                Console.WriteLine();
                Console.WriteLine(_fail == 0
                    ? "  OK - the mapping matches our reading of Flycast"
                    : "  " + _fail + " FAILURE(S)");
                return _fail == 0;
            }
            finally
            {
                try { Directory.Delete(root, true); } catch { }
            }
        }

        private static string Ahk(Type ahk, string name, object table)
            => (string)ahk.GetMethod(name, BindingFlags.Public | BindingFlags.Static)
                          .Invoke(null, new[] { table }) ?? "";

        /// <summary>The [digital] binds, parsed here rather than through the plugin - the point is to
        /// read the file the way Flycast does, not the way we wrote it.</summary>
        private static Dictionary<string, int> ReadBinds(string path)
        {
            var binds = new Dictionary<string, int>(StringComparer.Ordinal);
            var inDigital = false;
            foreach (var raw in File.ReadAllLines(path))
            {
                var line = raw.Trim();
                if (line.StartsWith("[")) { inDigital = line == "[digital]"; continue; }
                if (!inDigital || line.Length == 0 || line.StartsWith(";")) continue;

                var eq = line.IndexOf('=');
                if (eq < 0) continue;
                var value = line.Substring(eq + 1).Trim();
                var colon = value.IndexOf(':');
                if (colon <= 0) continue;
                if (int.TryParse(value.Substring(0, colon), out var code))
                    binds[value.Substring(colon + 1).Trim()] = code;
            }
            return binds;
        }

        private static bool ContiguousFrom0(string path, int expected)
        {
            var seen = new List<int>();
            var inDigital = false;
            foreach (var raw in File.ReadAllLines(path))
            {
                var line = raw.Trim();
                if (line.StartsWith("[")) { inDigital = line == "[digital]"; continue; }
                if (!inDigital) continue;
                var eq = line.IndexOf('=');
                if (eq < 0 || !line.StartsWith("bind")) continue;
                if (line.Substring(eq + 1).Trim().Length == 0) break;   // Flycast stops here too
                if (int.TryParse(line.Substring(4, eq - 4).Trim(), out var n)) seen.Add(n);
            }
            seen.Sort();
            return seen.Count == expected && seen.SequenceEqual(Enumerable.Range(0, expected));
        }
    }
}
