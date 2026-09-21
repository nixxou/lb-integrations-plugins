// The save-state assertion.
//
// Everything here goes through the PUBLIC plugin contract - GetSaves, IsSecondarySaveFile,
// GetCompanionSaveFiles, GetPotentialSaveSlots, AddSaveFile, RemoveSave - because that is the only
// surface a host has. Nothing reaches into the plugin's internals, so a check that passes here says
// the host would see the same thing.
//
// The listing half is READ-ONLY and runs against the real emulator install. The restore and delete
// halves WRITE, so they run against a throwaway portable PPSSPP built in the temp folder: an empty
// PPSSPPWindows64.exe with no installed.txt beside it is, by PpssppPaths' own rule, a portable
// install whose memstick is <dir>\memstick. The user's own memory stick is never touched.
//
// The assertions that matter, in order of what would actually break:
//
//   1. the undo pair is NOT listed as a slot          - a laxer name pattern invents a phantom slot
//   2. the screenshot and the undo file are secondary - or each would become a save of its own
//   3. a restore reproduces the EXACT file name       - PPSSPP loads a slot by exact name
//   4. a restore with no way to know the version REFUSES rather than writing a dead file
//   5. delete takes the slot, its screenshot and its undo pair

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Unbroken.LaunchBox.Plugins;
using Unbroken.LaunchBox.Plugins.Data;

namespace LbIntegrations.Probe
{
    internal static class StateCheck
    {
        public static bool Run(EmulatorPlugin plugin, string emuPath, string romPath)
        {
            Console.WriteLine();
            Console.WriteLine("-- save states " + new string('-', 47));
            Console.WriteLine("  emulator : " + emuPath);
            Console.WriteLine("  rom      : " + romPath);

            if (string.IsNullOrWhiteSpace(emuPath) || !File.Exists(emuPath))
            { Console.WriteLine("  no emulator at that path"); return false; }
            if (string.IsNullOrWhiteSpace(romPath) || !File.Exists(romPath))
            { Console.WriteLine("  no rom at that path"); return false; }

            bool ok = true;
            ok &= Listing(plugin, emuPath, romPath, out var listed);
            ok &= Secondary(plugin, listed);
            ok &= Companions(plugin, listed);
            ok &= Slots(plugin);
            ok &= RoundTrip(plugin, listed);
            ok &= RefusesWithoutVersion(plugin);

            Console.WriteLine();
            Console.WriteLine("  " + (ok ? "OK - the state contract holds" : "NOT OK - see above"));
            return ok;
        }

        // ── 1. listing ───────────────────────────────────────────────────────

        private static bool Listing(EmulatorPlugin plugin, string emuPath, string romPath,
                                    out List<GameSaveState> listed)
        {
            listed = new List<GameSaveState>();
            var emulator = new StubEmulator { Title = "PPSSPP", ApplicationPath = emuPath };
            var game = StubGame.Create(Guid.NewGuid().ToString(), "Probe Game", romPath, emulator.Id);

            var response = plugin.GetSaves(new GetSavesArgs
            {
                Emulator = emulator,
                Games = new[] { game },
            });

            if (response is not { WasSuccess: true })
            { Console.WriteLine("  GetSaves failed: " + (response?.Message ?? "no reason given")); return false; }

            var all = (response.FoundSaves ?? (IReadOnlyCollection<GameSaveBase>)Array.Empty<GameSaveBase>()).ToList();
            listed = all.OfType<GameSaveState>().ToList();
            var saves = all.Where(s => s is not GameSaveState).ToList();

            Console.WriteLine();
            Console.WriteLine("  listed   : " + saves.Count + " save(s), " + listed.Count + " state(s)");
            foreach (var s in listed)
                Console.WriteLine("    slot " + s.Slot + "  " + Path.GetFileName(s.FileLocation)
                                  + "   group=" + s.SaveGroupId);

            bool ok = true;

            // THE assertion: PPSSPP's undo buffer shares the slot's stem and must never be a row.
            var undo = listed.Where(s => (Path.GetFileName(s.FileLocation) ?? "")
                                         .IndexOf(".undo.", StringComparison.OrdinalIgnoreCase) >= 0).ToList();
            if (undo.Count > 0)
            {
                Console.WriteLine("  FAIL - the undo buffer was listed as a slot: "
                                  + string.Join(", ", undo.Select(s => Path.GetFileName(s.FileLocation))));
                ok = false;
            }
            else Console.WriteLine("  the .undo pair is not listed as a slot   OK");

            if (listed.Count == 0)
            {
                Console.WriteLine("  FAIL - no state listed; save one in PPSSPP first, or the rom does not match");
                ok = false;
            }

            foreach (var s in listed)
            {
                if (s.Slot == null) { Console.WriteLine("  FAIL - a state came back with no slot"); ok = false; }
                if (!File.Exists(s.FileLocation ?? ""))
                { Console.WriteLine("  FAIL - FileLocation does not exist: " + s.FileLocation); ok = false; }
                if (plugin.IsSaveContainer(s))
                { Console.WriteLine("  FAIL - a state claimed to be a container"); ok = false; }
            }
            if (ok) Console.WriteLine("  every state has a slot, a real file, and is not a container   OK");
            return ok;
        }

        // ── 2. what must not become a save of its own ────────────────────────

        private static bool Secondary(EmulatorPlugin plugin, List<GameSaveState> listed)
        {
            var state = listed.FirstOrDefault();
            if (state == null) return false;

            string ppst = state.FileLocation;
            string stem = ppst.Substring(0, ppst.Length - ".ppst".Length);
            var cases = new (string Path, bool Expected, string What)[]
            {
                (ppst,                  false, "the state itself"),
                (stem + ".jpg",         true,  "its screenshot"),
                (stem + ".undo.ppst",   true,  "the undo buffer"),
                (stem + ".undo.jpg",    true,  "the undo screenshot"),
            };

            Console.WriteLine();
            bool ok = true;
            foreach (var c in cases)
            {
                bool got = plugin.IsSecondarySaveFile(c.Path);
                bool good = got == c.Expected;
                ok &= good;
                Console.WriteLine("  IsSecondarySaveFile " + (got ? "true " : "false") + "  " + c.What
                                  + (good ? "   OK" : "   FAIL, expected " + c.Expected));
            }
            return ok;
        }

        // ── 3. the screenshot follows ────────────────────────────────────────

        private static bool Companions(EmulatorPlugin plugin, List<GameSaveState> listed)
        {
            var state = listed.FirstOrDefault();
            if (state == null) return false;

            var companions = plugin.GetCompanionSaveFiles(state.FileLocation) ?? Array.Empty<string>();
            string jpg = Path.ChangeExtension(state.FileLocation, ".jpg");
            bool wanted = File.Exists(jpg);
            bool got = companions.Any(c => string.Equals(c, jpg, StringComparison.OrdinalIgnoreCase));

            Console.WriteLine();
            Console.WriteLine("  companions: " + (companions.Count == 0 ? "(none)" : string.Join(", ",
                                companions.Select(Path.GetFileName))));
            if (wanted && !got) { Console.WriteLine("  FAIL - the screenshot exists but was not offered"); return false; }
            if (!wanted && companions.Count > 0)
            { Console.WriteLine("  FAIL - companions offered for a state with no screenshot"); return false; }
            Console.WriteLine("  the screenshot is offered when it exists   OK");
            return true;
        }

        // ── 4. slots ─────────────────────────────────────────────────────────

        private static bool Slots(EmulatorPlugin plugin)
        {
            var slots = plugin.GetPotentialSaveSlots();
            Console.WriteLine();
            if (slots == null || slots.Count == 0) { Console.WriteLine("  FAIL - no slots declared"); return false; }
            Console.WriteLine("  slots     : " + string.Join(", ", slots.OrderBy(k => k.Key)
                                .Select(k => k.Key + "=\"" + k.Value + "\"")));
            bool ok = slots.ContainsKey(0) && slots.Count == 5;
            Console.WriteLine("  five slots, numbered from 0 in the file name   " + (ok ? "OK" : "FAIL"));
            return ok;
        }

        // ── 5. restore, into a throwaway install ─────────────────────────────

        private static bool RoundTrip(EmulatorPlugin plugin, List<GameSaveState> listed)
        {
            var state = listed.FirstOrDefault();
            if (state == null) return false;

            Console.WriteLine();
            Console.WriteLine("-- restore round-trip  [WRITES, in the temp folder] " + new string('-', 10));

            string sandbox = NewSandbox(out var stateDir, out var emuExe);
            string vault = Path.Combine(sandbox, "vault");
            Directory.CreateDirectory(vault);
            try
            {
                string expectedName = Path.GetFileName(state.FileLocation);
                string vaultPpst = Path.Combine(vault, expectedName);
                File.Copy(state.FileLocation, vaultPpst);
                var jpg = Path.ChangeExtension(state.FileLocation, ".jpg");
                if (File.Exists(jpg)) File.Copy(jpg, Path.ChangeExtension(vaultPpst, ".jpg"));

                PluginHelper.DataManager = new StubDataManager(
                    new StubEmulator { Title = "PPSSPP", ApplicationPath = emuExe });

                var response = plugin.AddSaveFile(new AddSaveArgs
                {
                    SaveToAdd = new GameSaveState
                    {
                        FileLocation = vaultPpst,
                        OriginalFileName = expectedName,
                        Slot = state.Slot,
                        SaveGroupId = state.SaveGroupId,
                        GameId = state.GameId,
                    },
                    ShouldOverwriteFunc = () => true,
                });

                if (response is not { WasSuccess: true })
                { Console.WriteLine("  AddSaveFile failed: " + (response?.Message ?? "no reason given")); return false; }

                var landed = response.SaveAdded?.FileLocation;
                Console.WriteLine("  restored to : " + landed);

                bool ok = true;
                if (!File.Exists(landed ?? "")) { Console.WriteLine("  FAIL - nothing was written"); return false; }

                // The name is the whole point: PPSSPP looks for this exact string.
                if (!string.Equals(Path.GetFileName(landed), expectedName, StringComparison.Ordinal))
                { Console.WriteLine("  FAIL - name is \"" + Path.GetFileName(landed) + "\", expected \"" + expectedName + "\""); ok = false; }
                else Console.WriteLine("  the file name is reproduced exactly   OK");

                // In the right folder of the throwaway install, not somewhere invented.
                if (!string.Equals(Path.GetDirectoryName(landed), stateDir, StringComparison.OrdinalIgnoreCase))
                { Console.WriteLine("  FAIL - landed in " + Path.GetDirectoryName(landed) + ", expected " + stateDir); ok = false; }
                else Console.WriteLine("  it landed in PPSSPP_STATE of the sandbox   OK");

                // Byte for byte.
                if (!SameBytes(state.FileLocation, landed))
                { Console.WriteLine("  FAIL - the restored state differs from the original"); ok = false; }
                else Console.WriteLine("  identical byte for byte   OK");

                if (File.Exists(jpg))
                {
                    if (!File.Exists(Path.ChangeExtension(landed, ".jpg")))
                    { Console.WriteLine("  FAIL - the screenshot did not follow"); ok = false; }
                    else Console.WriteLine("  the screenshot followed   OK");
                }

                // Delete takes the slot AND the undo pair, so nothing can resurrect it.
                string stem = landed.Substring(0, landed.Length - ".ppst".Length);
                File.WriteAllText(stem + ".undo.ppst", "x");
                File.WriteAllText(stem + ".undo.jpg", "x");
                var removed = plugin.RemoveSave(new GameSaveState
                {
                    FileLocation = landed,
                    Slot = state.Slot,
                    SaveGroupId = state.SaveGroupId,
                });
                var left = new[] { landed, stem + ".jpg", stem + ".undo.ppst", stem + ".undo.jpg" }
                           .Where(File.Exists).Select(Path.GetFileName).ToList();
                if (removed is not { WasSuccess: true })
                { Console.WriteLine("  FAIL - RemoveSave: " + (removed?.Message ?? "no reason given")); ok = false; }
                else if (left.Count > 0)
                { Console.WriteLine("  FAIL - left behind: " + string.Join(", ", left)); ok = false; }
                else Console.WriteLine("  delete took the slot, its screenshot and its undo pair   OK");

                return ok;
            }
            finally { Nuke(sandbox); }
        }

        // ── 6. refusing beats guessing ───────────────────────────────────────

        private static bool RefusesWithoutVersion(EmulatorPlugin plugin)
        {
            Console.WriteLine();
            Console.WriteLine("-- negative: no version to be found " + new string('-', 26));

            string sandbox = NewSandbox(out _, out var emuExe);
            try
            {
                string vault = Path.Combine(sandbox, "vault");
                Directory.CreateDirectory(vault);
                // A backup whose name says nothing, restored into a state folder that holds nothing
                // for this game: there is no honest way to know the disc version.
                string anonymous = Path.Combine(vault, "state.bin");
                File.WriteAllText(anonymous, "not really a state");

                PluginHelper.DataManager = new StubDataManager(
                    new StubEmulator { Title = "PPSSPP", ApplicationPath = emuExe });

                var response = plugin.AddSaveFile(new AddSaveArgs
                {
                    SaveToAdd = new GameSaveState
                    {
                        FileLocation = anonymous,
                        Slot = 0,
                        SaveGroupId = "ppsspp-state:ULUS10516:0",
                    },
                    ShouldOverwriteFunc = () => true,
                });

                bool refused = response is not { WasSuccess: true };
                Console.WriteLine("  answer    : " + (refused ? "refused - " + response?.Message : "ACCEPTED"));
                Console.WriteLine("  " + (refused
                    ? "a restore with no knowable version is refused   OK"
                    : "FAIL - it wrote a file PPSSPP would never load"));
                return refused;
            }
            finally { Nuke(sandbox); }
        }

        // ── the throwaway install ────────────────────────────────────────────

        /// <summary>A portable PPSSPP that exists only as paths: an empty executable with no
        /// installed.txt beside it, which is exactly what PpssppPaths calls portable.</summary>
        private static string NewSandbox(out string stateDir, out string emuExe)
        {
            string root = Path.Combine(Path.GetTempPath(), "lbip-state-" + Guid.NewGuid().ToString("N"));
            string install = Path.Combine(root, "PPSSPP");
            stateDir = Path.Combine(install, "memstick", "PSP", "PPSSPP_STATE");
            Directory.CreateDirectory(stateDir);
            emuExe = Path.Combine(install, "PPSSPPWindows64.exe");
            File.WriteAllBytes(emuExe, Array.Empty<byte>());
            return root;
        }

        private static void Nuke(string dir)
        {
            try { if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true); } catch { }
        }

        private static bool SameBytes(string a, string b)
        {
            try { return File.ReadAllBytes(a).AsSpan().SequenceEqual(File.ReadAllBytes(b)); }
            catch { return false; }
        }
    }
}
