// --stock-saves <LaunchBox root> --scratch <empty folder>: lot 4.0, the measures taken on LaunchBox's OWN integrations
// (RetroArch, Dolphin, PCSX2) before any of lot 4's code is written. Their real dlls are loaded from <root>\System\Plugins,
// LaunchBox's Core resolved for them; RetroArch is called on a COPY of its folder (its settings, saves and states, no cores),
// Dolphin on its real folder for reading only. Nothing of LaunchBox's library is touched: the games and emulators are stubs.
//
// What it answers (docs\saves-lot4.md): which of the 13 save methods each one overrides; its EmulatorName; the rows and ids
// its GetSaves gives (Atlantis's Saturn set, Zelda's .srm, a Wii game on Dolphin); how its per-row methods answer for an
// empty id, a GUID, a native id and our JSON; whether Harmony replaces a row argument (ref __0) for the original.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using HarmonyLib;
using Unbroken.LaunchBox.Plugins;
using Unbroken.LaunchBox.Plugins.Data;

namespace LbIntegrations.Probe
{
    internal static class StockSavesCheck
    {
        private static readonly string[] SaveMethods =
        {
            "SupportsSaveManagement", "GetSaves", "AddSaveFile", "RemoveSave", "IsSaveActive", "IsSaveContainer",
            "TryBackupSave", "UseSaveGroupIdForPersistedMatch", "TryComputeSaveSignature", "TryGetSaveGroupInfo",
            "IsSecondarySaveFile", "GetCompanionSaveFiles", "GetPotentialSaveSlots",
        };

        public static bool Run(string root, string scratch)
        {
            if (root == null || scratch == null) { Console.Error.WriteLine("--stock-saves <LaunchBox root> --scratch <empty folder>"); return false; }
            root = Path.GetFullPath(root);
            scratch = Path.GetFullPath(scratch);
            Directory.CreateDirectory(scratch);
            if (Directory.EnumerateFileSystemEntries(scratch).Any()) { Console.Error.WriteLine("the scratch folder must be empty: " + scratch); return false; }

            // LaunchBox's Core and each integration's own folder, as LaunchBox resolves them.
            var dirs = new List<string> { Path.Combine(root, "Core") };
            dirs.AddRange(Directory.GetDirectories(Path.Combine(root, "System", "Plugins")));
            AssemblyLoadContext.Default.Resolving += (ctx, name) =>
            {
                foreach (var d in dirs)
                {
                    var p = Path.Combine(d, name.Name + ".dll");
                    if (File.Exists(p)) return ctx.LoadFromAssemblyPath(p);
                }
                return null;
            };
            var families = new[] { ("RetroArch", "RetroArchPlugin"), ("Dolphin", "DolphinPlugin"), ("Pcsx2", "Pcsx2Plugin") };
            var types = new Dictionary<string, Type>();
            foreach (var (family, typeName) in families)
            {
                var dll = Directory.GetFiles(Path.Combine(root, "System", "Plugins"), "Unbroken.LaunchBox.Windows." + family + ".dll", SearchOption.AllDirectories).FirstOrDefault();
                if (dll == null) { Console.WriteLine("  " + family + ": no dll"); continue; }
                var asm = AssemblyLoadContext.Default.LoadFromAssemblyPath(dll);
                var t = asm.GetTypes().FirstOrDefault(x => x.Name == typeName && typeof(EmulatorPlugin).IsAssignableFrom(x));
                Console.WriteLine("  " + family + ": " + (t?.FullName ?? "no " + typeName) + "  [" + Path.GetFileName(Path.GetDirectoryName(dll)) + "]");
                if (t != null) types[family] = t;
            }

            // Lot 5: LaunchBox's integrations without save support, loaded too (the router lists what is loaded).
            var noSave = new Dictionary<string, Type>();
            foreach (var (family, typeName) in new[] { ("BigPEmu", "BigPEmuPlugin"), ("Cemu", "CemuPlugin"), ("DuckStation", "DuckStationPlugin"), ("Mame", "MamePlugin"),
                                                      ("Rpcs3", "Rpcs3Plugin"), ("ScummVm", "ScummVmPlugin"), ("Xemu", "XemuPlugin") })
            {
                var dll = Directory.GetFiles(Path.Combine(root, "System", "Plugins"), "Unbroken.LaunchBox.Windows." + family + ".dll", SearchOption.AllDirectories).FirstOrDefault();
                if (dll == null) { Console.WriteLine("  " + family + ": no dll"); continue; }
                try
                {
                    var t = AssemblyLoadContext.Default.LoadFromAssemblyPath(dll).GetTypes().FirstOrDefault(x => x.Name == typeName && typeof(EmulatorPlugin).IsAssignableFrom(x));
                    if (t != null) noSave[family] = t; else Console.WriteLine("  " + family + ": no " + typeName);
                }
                catch (Exception ex) { Console.WriteLine("  " + family + ": " + (ex.InnerException ?? ex).Message); }
            }

            Section("1. The save methods: overridden, or the SDK's body");
            var sdk = typeof(EmulatorPlugin);
            foreach (var name in SaveMethods)
            {
                var baseM = sdk.GetMethods(BindingFlags.Public | BindingFlags.Instance).Where(m => m.Name == name && m.IsVirtual).ToList();
                if (baseM.Count != 1) { Console.WriteLine("  " + name.PadRight(32) + " SDK: " + baseM.Count + " virtual(s)"); continue; }
                var ps = baseM[0].GetParameters().Select(p => p.ParameterType).ToArray();
                var line = "  " + name.PadRight(32);
                foreach (var (family, t) in types)
                {
                    var m = t.GetMethod(name, BindingFlags.Public | BindingFlags.Instance, null, ps, null);
                    line += (family + "=" + (m == null ? "MISSING" : m.DeclaringType == sdk ? "sdk" : "own")).PadRight(18);
                }
                Console.WriteLine(line + "  (" + string.Join(", ", ps.Select(p => p.Name)) + ")");
            }

            // The instances, as the router makes them (IPluginPaths for RetroArch).
            // The Compatibility page's checks of lot 4, on the real dlls.
            foreach (var id in new[] { "ra.saves", "dolphin.saves", "pcsx2.saves", "sdk.saves" })
            {
                var (state, detail) = LbIntegrations.Compat.CompatChecks.Check(id);
                Console.WriteLine("  check " + id.PadRight(14) + state + "  " + detail);
            }

            var stock = LbIntegrations.Saves.SaveRouter.Stock();
            Section("2. EmulatorName and the emu the router gives");
            foreach (var (emu, plugin) in stock)
                Console.WriteLine("  " + plugin.GetType().Name.PadRight(18) + " EmulatorName=\"" + plugin.EmulatorName + "\"  emu=" + emu);
            EmulatorPlugin Of(string typeName) => stock.Select(s => s.Plugin).FirstOrDefault(p => p.GetType().Name == typeName);
            var ra = Of("RetroArchPlugin");
            var dolphin = Of("DolphinPlugin");
            var pcsx2 = Of("Pcsx2Plugin");

            // RetroArch's folder copied without its cores and system files: its settings, saves, states, info.
            var raSource = Path.Combine(root, "Emulators", "RetroArch");
            var raCopy = Path.Combine(scratch, "RetroArch");
            Directory.CreateDirectory(raCopy);
            foreach (var f in Directory.GetFiles(raSource)) File.Copy(f, Path.Combine(raCopy, Path.GetFileName(f)));
            foreach (var sub in new[] { "saves", "states", "info", "config" })
                if (Directory.Exists(Path.Combine(raSource, sub))) CopyTree(Path.Combine(raSource, sub), Path.Combine(raCopy, sub));
            Directory.CreateDirectory(Path.Combine(raCopy, "cores"));
            Console.WriteLine("  RetroArch copied to " + raCopy);

            var raEmu = new StubEmulator { Title = "RetroArch", ApplicationPath = Path.Combine(raCopy, "retroarch.exe") };
            void Line(StubEmulator e, string platform, string cmd) { var l = e.AddNewEmulatorPlatform(); l.Platform = platform; l.CommandLine = cmd; l.IsDefault = true; }
            Line(raEmu, "Sega Saturn", "-L \"cores\\mednafen_saturn_libretro.dll\" -f");
            Line(raEmu, "Super Nintendo Entertainment System", "-L \"cores\\snes9x_libretro.dll\" -f");
            var dolphinEmu = new StubEmulator { Title = "Dolphin", ApplicationPath = Path.Combine(root, "Emulators", "Dolphin", "Dolphin.exe") };
            Line(dolphinEmu, "Nintendo Wii", "");
            Line(dolphinEmu, "Nintendo GameCube", "");
            foreach (var e in new[] { raEmu, dolphinEmu }) { Blank(e); foreach (var l in e.GetAllEmulatorPlatforms()) Blank(l); }
            PluginHelper.DataManager = new StubDataManager(raEmu, dolphinEmu);

            IGame Game(string id, string title, string path, string platform, StubEmulator e)
            {
                var g = StubGame.With(StubGame.Create(id, title, path, e.Id), "Platform", platform);
                // Every text the stub leaves null made empty: the integrations run regexes on them, as on a real game's.
                foreach (var p in typeof(IGame).GetProperties().Where(p => p.PropertyType == typeof(string) && p.CanRead))
                    if (p.GetValue(g) == null) g = StubGame.With(g, p.Name, "");
                // What LaunchBox answers: the emulator's line for the game's platform (RetroArch reads its -L core from it).
                var line = e.GetAllEmulatorPlatforms().FirstOrDefault(l => l.Platform == platform);
                return StubGame.With(g, "GetEffectiveCommandLine()", (e.CommandLine + " " + line?.CommandLine).Trim());
            }
            var atlantis = Game("7d337113-7b35-4db5-ae36-e72d37c08c4d", "Atlantis: The Lost Tales", @"G:\saturn\Atlantis - The Lost Tales (FR) (Disc 1).chd", "Sega Saturn", raEmu);
            var zelda = Game("2cf24c23-76ee-4892-acd7-eb2c1e964d5f", "The Legend of Zelda: A Link to the Past", @"G:\LB\FakeCatalog\Legend of Zelda, The - A Link to the Past (France).sfc", "Super Nintendo Entertainment System", raEmu);
            var animal = Game("920e499a-a943-4c73-890e-a4357514fa47", "Animal Crossing: City Folk", @"C:\Users\mehdi\Downloads\Animal-Crossing-Lets-Go-to-the-City-Europe-En-Fr-De-Es-It.rvz", "Nintendo Wii", dolphinEmu);

            // A null handed to a regex inside an integration: its pattern says which text it wanted (no source read).
            var spy = new Harmony("lbip.probe.regex-spy");
            spy.Patch(typeof(System.Text.RegularExpressions.Regex).GetMethod("Match", new[] { typeof(string) }), prefix: new HarmonyMethod(typeof(StockSavesCheck).GetMethod(nameof(NullMatch), BindingFlags.NonPublic | BindingFlags.Static)));
            Section("3. The rows GetSaves gives");
            var raRows = Rows(ra, raEmu, atlantis, zelda);
            var dolphinRows = Rows(dolphin, dolphinEmu, animal);
            // Outside LaunchBox their GetSaves reach LaunchBox's core (Tokens, a resolver), which only LaunchBox sets up: the
            // listings are measured in LaunchBox. The per-row methods are asked about rows made here, as LaunchBox persists them.
            if (raRows.Count == 0)
            {
                var saves = Path.Combine(raCopy, "saves");
                raRows.Add(new GameSaveGame { GameId = atlantis.Id, FileLocation = Path.Combine(saves, "Beetle Saturn", "Atlantis - The Lost Tales (FR) (Disc 1).bcr"), EmulatorFileName = "retroarch.exe", EmulatorCore = "mednafen_saturn_libretro", SaveGroupId = "", SaveGroupName = "My Save File" });
                raRows.Add(new GameSaveGame { GameId = zelda.Id, FileLocation = Path.Combine(saves, "Snes9x", "Legend of Zelda, The - A Link to the Past (France).srm"), EmulatorFileName = "retroarch.exe", EmulatorCore = "snes9x_libretro", SaveGroupId = "" });
                raRows.Add(new GameSaveState { GameId = zelda.Id, FileLocation = Path.Combine(raCopy, "states", "Snes9x", "Legend of Zelda, The - A Link to the Past (France).state"), EmulatorFileName = "retroarch.exe", EmulatorCore = "snes9x_libretro", SaveGroupId = "", Slot = 0 });
                Console.WriteLine("  rows made by hand: " + string.Join(", ", raRows.Select(r => Path.GetFileName(r.FileLocation))));
            }
            Console.WriteLine("  GetSavesArgs: " + string.Join(", ", typeof(GetSavesArgs).GetProperties().Select(p => p.PropertyType.Name + " " + p.Name)));
            Console.WriteLine("  AddSaveArgs: " + string.Join(", ", typeof(AddSaveArgs).GetProperties().Select(p => p.PropertyType.Name + " " + p.Name)));

            Section("4. The per-row methods, for each form of id");
            // PCSX2: no install in the sandbox - a row as its ids look (pcsx2:<serial>), a card file that does not exist.
            var pcsx2Emu = new StubEmulator { Title = "PCSX2", ApplicationPath = Path.Combine(scratch, "PCSX2", "pcsx2-qt.exe") }; Blank(pcsx2Emu);
            var pcsx2Rows = new List<GameSaveBase> { new GameSaveGame { GameId = "g-ps2", FileLocation = Path.Combine(scratch, "PCSX2", "memcards", "Mcd001.ps2"), EmulatorFileName = "pcsx2-qt.exe", SaveGroupId = "pcsx2:SLUS-20062" } };
            foreach (var (plugin, emu, rows) in new[] { (ra, raEmu, raRows), (dolphin, dolphinEmu, dolphinRows), (pcsx2, pcsx2Emu, pcsx2Rows) })
            {
                if (plugin == null) continue;
                foreach (var row in rows.Take(3))
                {
                    Console.WriteLine("  " + plugin.GetType().Name + " / " + Path.GetFileName(row.FileLocation) + " (" + row.GetType().Name + ", listed id \"" + row.SaveGroupId + "\")");
                    foreach (var id in new[] { row.SaveGroupId, "", "c380b29f2702480a982dda74c642a367", "saturn-atlantis - the lost tales (fr) (disc 1)", "pcsx2:SLUS-20062", "{\"emu\":\"lb.retroarch\",\"kind\":\"save\",\"native\":\"x\"}" }.Distinct())
                    {
                        var r = WithId(row, id);
                        string Try(Func<object> f) { try { return Show(f()); } catch (Exception ex) { return "throws " + (ex.InnerException ?? ex).GetType().Name; } }
                        var active = Try(() => plugin.IsSaveActive(r, emu.ApplicationPath));
                        var match = Try(() => plugin.UseSaveGroupIdForPersistedMatch(r));
                        var sig = Try(() => plugin.TryComputeSaveSignature(r, emu.ApplicationPath, out var s) ? "true " + Cut(s) : "false");
                        var info = Try(() => plugin.TryGetSaveGroupInfo(r, r.FileLocation, out var g, out var n) ? "true id=\"" + g + "\" name=\"" + n + "\"" : "false");
                        var container = Try(() => plugin.IsSaveContainer(r));
                        Console.WriteLine("     id \"" + Cut(id) + "\": active=" + active + "  match=" + match + "  container=" + container + "  sig=" + sig + "  info=" + info);
                    }
                }
            }

            Section("5. Harmony: a row argument replaced for the original (ref __0)");
            HarmonyReplace(ra, raRows.FirstOrDefault(), raEmu.ApplicationPath);

            // Lot 4's hooks (StockDesk) on the same dlls: what they log, and that every answer is the original's.
            Section("6. StockDesk's hooks on these dlls");
            LbIntegrations.Saves.SaveRouter.Log = Console.WriteLine;
            string Answers(EmulatorPlugin p, GameSaveBase r, string exe)
            {
                string Try(Func<object> f) { try { return Show(f()); } catch (Exception ex) { return "throws " + (ex.InnerException ?? ex).GetType().Name; } }
                return Try(() => p.IsSaveActive(r, exe)) + "/" + Try(() => p.UseSaveGroupIdForPersistedMatch(r)) + "/" + Try(() => p.IsSaveContainer(r))
                       + "/" + Try(() => p.TryGetSaveGroupInfo(r, r.FileLocation, out var g, out _) + " " + g) + "/" + Try(() => string.Join(",", p.GetCompanionSaveFiles(r.FileLocation).Select(Path.GetFileName)));
            }
            var before = raRows.Concat(pcsx2Rows).Select(r => Answers(r.EmulatorFileName == "pcsx2-qt.exe" ? pcsx2 : ra, r, r.EmulatorFileName == "pcsx2-qt.exe" ? pcsx2Emu.ApplicationPath : raEmu.ApplicationPath)).ToList();
            // One of PCSX2's row methods made to fail its patch (Codex review 10/10): PCSX2 alone is left in its own form.
            LbIntegrations.Saves.StockDesk.FailForTests = (emu, m) => emu == "lb.pcsx2" && m == "IsSaveActive";
            LbIntegrations.Saves.StockDesk.Install();
            LbIntegrations.Saves.StockDesk.FailForTests = null;
            LbIntegrations.Saves.StockDesk.Install();   // twice: hooked once
            // Hooked but not checked yet: not complete - only a self-check that passed makes it so (Codex review 10/10).
            bool hookedNotChecked = !LbIntegrations.Saves.StockDesk.Complete("lb.retroarch");
            LbIntegrations.Saves.StockDesk.Verify();    // the self-check: each integration says its hooks are in
            var after = raRows.Concat(pcsx2Rows).Select(r => Answers(r.EmulatorFileName == "pcsx2-qt.exe" ? pcsx2 : ra, r, r.EmulatorFileName == "pcsx2-qt.exe" ? pcsx2Emu.ApplicationPath : raEmu.ApplicationPath)).ToList();
            var all = Harmony.GetAllPatchedMethods().Where(m => Harmony.GetPatchInfo(m)?.Owners.Contains(LbIntegrations.Saves.StockDesk.HarmonyId) == true).ToList();
            Console.WriteLine("  hooked: " + all.Count + " method(s), each " + string.Join(",", all.Select(m => Harmony.GetPatchInfo(m).Prefixes.Count(p => p.owner == LbIntegrations.Saves.StockDesk.HarmonyId)).Distinct()) + " prefix");
            // A v2 host (LaunchBox 14.1): its integrations route their saves themselves and the desk hooks none of them
            // (lb141-plan step 4) - what follows tests the hooks, so it is 14.0's.
            if (LbIntegrations.Compat.LbHostShape.Describe().Contains("retroarch V2"))
            {
                Console.WriteLine(all.Count == 0 ? "  OK    a v2 host (LaunchBox 14.1): nothing hooked - the steps below are 14.0's"
                                                 : "  FAIL  a v2 host, and " + all.Count + " method(s) hooked");
                return all.Count == 0;
            }
            for (int i = 0; i < before.Count; i++)
                Console.WriteLine("  " + (before[i] == after[i] ? "same   " : "CHANGED") + " " + after[i] + (before[i] == after[i] ? "" : "   (was " + before[i] + ")"));

            // Step 6: a row in OUR form gets the answers its native form gets; the group it names comes back in our form.
            Section("7. Their ids in our form (step 6)");
            int bad = 0;
            void Expect(string what, bool ok, string got) { Console.WriteLine("  " + (ok ? "OK    " : "FAIL  ") + what + (ok ? "" : "   got " + got)); if (!ok) bad++; }
            var sat = raRows[0];
            const string Native = "saturn-atlantis - the lost tales (fr) (disc 1)";
            var ours = LbIntegrations.Saves.StockIds.Encode("lb.retroarch", Native, false, "x", null)!;
            var asOurs = WithId(sat, ours);
            Expect("RetroArch: our Saturn id matched as its own", ra.UseSaveGroupIdForPersistedMatch(asOurs), "false");
            Expect("... the caller's row untouched", asOurs.SaveGroupId == ours, asOurs.SaveGroupId);
            var okInfo = ra.TryGetSaveGroupInfo(asOurs, asOurs.FileLocation, out var gid, out _);
            Expect("... the group it names: the very id that came in", okInfo && gid == ours, gid);
            var fresh = ra.TryGetSaveGroupInfo(WithId(sat, ""), sat.FileLocation, out var gid2, out _);
            Expect("... named from a row with no id: ours", fresh && gid2 == ours, gid2);
            var ps2 = WithId(pcsx2Rows[0], LbIntegrations.Saves.StockIds.Encode("lb.pcsx2", "pcsx2:SLUS-20062", false, "x", null)!);
            Expect("PCSX2: our id matched as its own", pcsx2.UseSaveGroupIdForPersistedMatch(ps2), "false");
            Expect("PCSX2: ... a container, as its own", pcsx2.IsSaveContainer(ps2), "false");
            var otherEmu = WithId(sat, LbIntegrations.Saves.StockIds.Encode("lb.pcsx2", Native, false, "x", null)!);
            Expect("another integration's id is not decoded (RetroArch: no match)", !ra.UseSaveGroupIdForPersistedMatch(otherEmu), "true");
            var why = LbIntegrations.Saves.StockDesk.IncompleteWhy("lb.pcsx2") ?? "";
            Expect("PCSX2 with a row method not hooked (failure injected on IsSaveActive): incomplete", why.Contains("IsSaveActive: failure injected"), why);
            Expect("... RetroArch goes on, fully hooked", LbIntegrations.Saves.StockDesk.Complete("lb.retroarch"), LbIntegrations.Saves.StockDesk.IncompleteWhy("lb.retroarch") ?? "");
            Expect("RetroArch hooked but not yet self-checked: not complete", hookedNotChecked, "complete");
            Expect("an integration never hooked (not loaded, not found): not complete", !LbIntegrations.Saves.StockDesk.Complete("lb.never-loaded"), "complete");
            // The migration with the live filter: only a checked integration's cards change (pcsx2: names its owner without any hook).
            var cards = new List<LbIntegrations.Saves.PersistedRow>
            {
                new LbIntegrations.Saves.PersistedRow { GameId = "g", SaveGroupId = "pcsx2:SLUS-20062", EmulatorFileName = "pcsx2-qt.exe" },
                new LbIntegrations.Saves.PersistedRow { GameId = "g", SaveGroupId = Native, EmulatorFileName = "retroarch.exe" },
            };
            var plan = LbIntegrations.Saves.StockIdMigration.Plan(cards, _ => null, true, () => "fresh", null, e => LbIntegrations.Saves.StockDesk.Complete(e));
            Expect("migration: the incomplete PCSX2's card left native", plan.All(c => c.Row.SaveGroupId != "pcsx2:SLUS-20062"), string.Join(",", plan.Select(c => c.GroupId)));
            Expect("... the checked RetroArch's card in our form", plan.Any(c => c.Row.SaveGroupId == Native && c.GroupId == ours), string.Join(",", plan.Select(c => c.GroupId)));
            Console.WriteLine("  (outside LaunchBox Dolphin is incomplete too - its module initializer: " + (LbIntegrations.Saves.StockDesk.IncompleteWhy("lb.dolphin") ?? "complete") + ")");

            // Step 7: another emulator's row on RetroArch goes to its owner - a pack plugin (a stand-in registered as "ares"),
            // another LaunchBox integration (PCSX2's twin) - and its own still reach its own code.
            Section("8. Other emulators' rows routed (step 7)");
            var fake = new FakeOwner();
            LbIntegrations.Catalog.LbSaveOwners.Register("ares", fake);
            var aresRow = new GameSaveGame { GameId = zelda.Id, FileLocation = @"C:\x\Zelda.ram", EmulatorFileName = "ares.exe",
                                             SaveGroupId = "{\"emu\":\"ares\",\"kind\":\"save\",\"by\":\"name\",\"id\":\"Zelda.ram\"}", SaveGroupName = "ares card" };
            Expect("an ares row: IsSaveActive answered by ares", !ra.IsSaveActive(aresRow, raEmu.ApplicationPath) && fake.Calls.Contains("IsSaveActive"), string.Join(",", fake.Calls));
            Expect("... its signature by ares", ra.TryComputeSaveSignature(aresRow, raEmu.ApplicationPath, out var fs) && fs == "ares-signature", fs);
            Expect("... its group: its own id (Plan A)", ra.TryGetSaveGroupInfo(aresRow, aresRow.FileLocation, out var fg, out var fn) && fg == aresRow.SaveGroupId && fn == "ares card", fg);
            Expect("... its restore by ares", ra.AddSaveFile(new AddSaveArgs { SaveToAdd = aresRow })?.Message == "restored by ares", "");
            Expect("... matched as ares says", ra.UseSaveGroupIdForPersistedMatch(aresRow) == true && fake.Calls.Contains("UseSaveGroupIdForPersistedMatch"), "");
            fake.Calls.Clear();
            Expect("RetroArch's own row: its own code, ares never asked", ra.IsSaveActive(sat, raEmu.ApplicationPath) && fake.Calls.Count == 0, string.Join(",", fake.Calls));
            var ps2OnRa = WithId(sat, LbIntegrations.Saves.StockIds.Encode("lb.pcsx2", "pcsx2:SLUS-20062", false, "x", null)!);
            Expect("a PCSX2 row on RetroArch: matched as PCSX2 says (its twin, decoded there)", ra.UseSaveGroupIdForPersistedMatch(ps2OnRa), "false");
            // A forwarded answer stays as its owner gave it (Codex review 10/10: Harmony runs the postfix after a skip).
            var nativeBack = new GameSaveGame { GameId = aresRow.GameId, FileLocation = aresRow.FileLocation, EmulatorFileName = aresRow.EmulatorFileName,
                                                SaveGroupId = aresRow.SaveGroupId, SaveGroupName = "native result" };
            var back = ra.AddSaveFile(new AddSaveArgs { SaveToAdd = nativeBack });
            Expect("a restore by ares giving a native pcsx2: row: not re-encoded as RetroArch's", back?.SaveAdded?.SaveGroupId == "pcsx2:SLUS-20062", back?.SaveAdded?.SaveGroupId ?? back?.Message ?? "null");
            var ps2Native = WithId(sat, "pcsx2:SLUS-20062");
            Expect("a native pcsx2: row on RetroArch: its group is its own id, not wrapped as RetroArch's",
                   ra.TryGetSaveGroupInfo(ps2Native, ps2Native.FileLocation, out var pg, out _) && pg == "pcsx2:SLUS-20062", pg);
            // A row with no owner in its id (empty, a GUID) that another plugin listed through a desk: to that plugin (review
            // 10/10: RetroArch's rows on a Dolphin game, ids left native, went to Dolphin's own code).
            var listedFile = Path.Combine(scratch, "listed-by-ares.ram");
            LbIntegrations.Saves.SaveRouter.NoteListed(listedFile, "ares", fake, raEmu);
            fake.Calls.Clear();
            var unowned = new GameSaveGame { GameId = zelda.Id, FileLocation = listedFile, SaveGroupId = "", EmulatorFileName = "ares.exe" };
            Expect("an empty id on a file ares listed: answered by ares", !ra.IsSaveActive(unowned, raEmu.ApplicationPath) && fake.Calls.Contains("IsSaveActive"), string.Join(",", fake.Calls));
            fake.Calls.Clear();
            var guidRow = new GameSaveGame { GameId = zelda.Id, FileLocation = listedFile, SaveGroupId = "c380b29f2702480a982dda74c642a367" };
            Expect("... a GUID on it: ares too", ra.UseSaveGroupIdForPersistedMatch(guidRow) && fake.Calls.Contains("UseSaveGroupIdForPersistedMatch"), string.Join(",", fake.Calls));
            fake.Calls.Clear();
            Expect("... a GUID on a file nobody listed: RetroArch's own code", ra.IsSaveActive(WithId(sat, "c380b29f2702480a982dda74c642a367"), raEmu.ApplicationPath) == ra.IsSaveActive(sat, raEmu.ApplicationPath) && fake.Calls.Count == 0, string.Join(",", fake.Calls));
            Expect("... the router: ares's file is ares's own to ares, another's to RetroArch", LbIntegrations.Saves.SaveRouter.ListedOwner(listedFile, "ares") == null && LbIntegrations.Saves.SaveRouter.ListedOwner(listedFile, "lb.retroarch")?.Emu == "ares", "");
            // A restore of that card: the row is a BACKUP in LaunchBox's vault, never listed - its card's live file was (second
            // review 10/10: Dolphin's own AddSaveFile restored RetroArch's backups).
            var vaultRow = new GameSaveGame { GameId = zelda.Id, SaveGroupId = "c380b29f2702480a982dda74c642a368",
                                              FileLocation = Path.Combine(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..")), "Saves", "Super Nintendo", "Zelda", "listed-by-ares-01.ram") };
            var wasLive = LbIntegrations.Saves.SaveRouter.CardLivePaths;
            LbIntegrations.Saves.SaveRouter.CardLivePaths = (g, id) => id == vaultRow.SaveGroupId ? new[] { listedFile } : Array.Empty<string>();
            try
            {
                fake.Calls.Clear();
                ra.AddSaveFile(new AddSaveArgs { SaveToAdd = vaultRow });
                Expect("a backup of a card whose live file ares listed: restored by ares", fake.Calls.Contains("AddSaveFile"), string.Join(",", fake.Calls));
                fake.Calls.Clear();
                var vaultOther = new GameSaveGame { GameId = zelda.Id, SaveGroupId = "c380b29f2702480a982dda74c642a369", FileLocation = vaultRow.FileLocation };
                Expect("... a backup of a card nobody listed: not ares's", LbIntegrations.Saves.SaveRouter.OwnerForStock(vaultOther, "lb.retroarch") == null, "ares");
            }
            finally { LbIntegrations.Saves.SaveRouter.CardLivePaths = wasLive; }
            // A restore forwarded by a desk tells the owner which install the row came from (review 10/10: two installs, the
            // first one's save overwritten).
            fake.Via = null;
            LbIntegrations.Saves.SaveForward.AddSaveFile(new LbIntegrations.Saves.SaveOwnerRoute(fake, "ares", dolphinEmu), new AddSaveArgs { SaveToAdd = aresRow }, _ => { });
            Expect("a forwarded restore: the owner sees the install the row came from", fake.Via == dolphinEmu.Id && !string.IsNullOrEmpty(fake.Via), fake.Via ?? "null");
            Expect("... and nothing once it is over", LbIntegrations.Saves.SaveForward.ViaEmulator(_ => true) == null, "still set");
            // A RetroArch row restored through a desk with `stockRestore` off: refused, never handed to their own AddSaveFile
            // (it takes the game's emulator and answers success - review 10/10).
            LbIntegrations.Compat.Compat.UseForTests(new Dictionary<string, bool> { ["stockRestore"] = false });
            AddSaveResponse offAnswer;
            try { offAnswer = LbIntegrations.Saves.SaveForward.AddSaveFile(new LbIntegrations.Saves.SaveOwnerRoute(ra, "lb.retroarch", raEmu), new AddSaveArgs { SaveToAdd = sat }, _ => { }); }
            finally { LbIntegrations.Compat.Compat.UseForTests(null); }
            Expect("stockRestore off: a RetroArch row's restore through a desk refused", offAnswer?.WasSuccess == false && (offAnswer.Message ?? "").Contains("Compatibility"), offAnswer?.Message ?? "null");
            // A game handed through a view (SaveGameView): the persisted cards are read off the real game (review 10/10).
            var coreEmu = new StubEmulator { Title = "RetroArch (bsnes)", ApplicationPath = raEmu.ApplicationPath, CommandLine = "-L cores\\bsnes_libretro.dll" };
            var view = LbIntegrations.Saves.SaveGameView.As(zelda, coreEmu);
            Expect("a game seen through a view: a view (the test means something)", !ReferenceEquals(view, zelda), "the game itself");
            Expect("... its persisted rows read off the real game", ReferenceEquals(LbIntegrations.Saves.PersistedRows.Holders(view).First().Holder, zelda), "the view");
            // A view made by ANOTHER copy of the shared code (measured 11/10 in LaunchBox: SUPER ZSNES's desk made it, ares's
            // hooks read it): the ares plugin's own SaveGameView makes one, ours reads it.
            var aresDll = Path.Combine(root, "Local", "Plugins", "Nixx-ares", "Ares.dll");
            if (File.Exists(aresDll))
            {
                object otherView = null;
                try
                {
                    var other = Assembly.LoadFile(aresDll).GetType("LbIntegrations.Saves.SaveGameView", true);
                    otherView = other.GetMethod("As", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic, null, new[] { typeof(IGame), typeof(IEmulator) }, null)
                                     .Invoke(null, new object[] { zelda, coreEmu });
                }
                catch (Exception ex) { Console.WriteLine("  (another copy's view not made: " + (ex.InnerException ?? ex).Message + ")"); }
                if (otherView != null)
                {
                    Expect("a view made by another plugin's copy: not one of ours (the test means something)", !(otherView is LbIntegrations.Saves.SaveGameView.Forward) && !ReferenceEquals(otherView, zelda), otherView.GetType().FullName);
                    Expect("... its persisted rows read off the real game all the same", ReferenceEquals(LbIntegrations.Saves.PersistedRows.Holders((IGame)otherView).First().Holder, zelda), "the view");
                }
            }
            else Console.WriteLine("  (no " + aresDll + ": the view of another copy not tried)");

            // Another Harmony over ONE of RetroArch's hooks later on (Codex review 10/10): the self-check of every decoding
            // method catches it, RetroArch is left in its own form.
            LbIntegrations.Saves.StockDesk.LostForTests = (emu, m) => emu == "lb.retroarch" && m == "IsSaveActive";
            LbIntegrations.Saves.StockDesk.Verify(again: true);
            LbIntegrations.Saves.StockDesk.LostForTests = null;
            var lostWhy = LbIntegrations.Saves.StockDesk.IncompleteWhy("lb.retroarch") ?? "";
            Expect("RetroArch's IsSaveActive hook gone, the others in: incomplete", lostWhy.Contains("hooks lost on IsSaveActive"), lostWhy);
            Expect("... its rows no longer routed (desk off): an ares row is RetroArch's own code's", ra.IsSaveActive(aresRow, raEmu.ApplicationPath), "false");
            // Lot 5.0: LaunchBox's integrations without save support - what they are and what the SDK answers for them.
            Section("9. Integrations without save support (lot 5.0)");
            foreach (var (family, t) in noSave)
            {
                var own = SaveMethods.Where(name =>
                {
                    var baseM = typeof(EmulatorPlugin).GetMethods(BindingFlags.Public | BindingFlags.Instance).FirstOrDefault(m => m.Name == name && m.IsVirtual);
                    var m = baseM == null ? null : t.GetMethod(name, BindingFlags.Public | BindingFlags.Instance, null, baseM.GetParameters().Select(p => p.ParameterType).ToArray(), null);
                    return m != null && m.DeclaringType != typeof(EmulatorPlugin);
                }).ToList();
                Expect(family + ": no save method of its own", own.Count == 0, string.Join(", ", own));
                var inst = stock.FirstOrDefault(s => s.Plugin.GetType() == t);
                if (inst.Plugin == null) { Console.WriteLine("     not instantiated by the router"); continue; }
                string Try(Func<object> f) { try { return Show(f()); } catch (Exception ex) { return "throws " + (ex.InnerException ?? ex).GetType().Name; } }
                var gs = Try(() => { var r = inst.Plugin.GetSaves(new GetSavesArgs { Games = Array.Empty<IGame>(), AdditionalApplications = Array.Empty<IAdditionalApplication>() }); return (r?.WasSuccess == true ? "success" : "FAILURE") + " \"" + r?.Message + "\""; });
                Console.WriteLine("     emu=" + inst.Emu + "  SupportsSaveManagement=" + Try(() => inst.Plugin.SupportsSaveManagement()) + "  GetSaves=" + gs
                                  + "  IsSaveActive=" + Try(() => inst.Plugin.IsSaveActive(new GameSaveGame(), "")) + "  AddSaveFile=" + Try(() => inst.Plugin.AddSaveFile(new AddSaveArgs { SaveToAdd = new GameSaveGame() })?.Message ?? "null"));
            }

            // Lot 5, step 1: hooked, and D6 - DuckStation says it supports saves only while a plugin with saves covers PS1.
            var duck = stock.FirstOrDefault(s => s.Emu == "lb.duckstation").Plugin;
            if (duck != null)
            {
                Expect("DuckStation hooked as an integration without save support", LbIntegrations.Saves.StockDesk.NoSave("lb.duckstation"), "not classed");
                Expect("RetroArch is not (it has saves of its own)", !LbIntegrations.Saves.StockDesk.NoSave("lb.retroarch"), "classed");
                var duckEmu = new StubEmulator { Title = "DuckStation", ApplicationPath = Path.Combine(root, "Emulators", "DuckStation", "duckstation-qt-x64-ReleaseLTCG.exe") };
                Line(duckEmu, "Sony Playstation", "");
                Blank(duckEmu); foreach (var l in duckEmu.GetAllEmulatorPlatforms()) Blank(l);
                bool Claimed() { try { return duck.GetApplicableEmulators(new IEmulator[] { duckEmu })?.Any() == true; } catch { return false; } }
                Console.WriteLine("     DuckStation claims the stand-in emulator: " + Claimed());
                bool Says(params IEmulator[] library)
                {
                    PluginHelper.DataManager = new StubDataManager(library);
                    LbIntegrations.Saves.SaveRouter.ForgetStock();
                    try { return duck.SupportsSaveManagement(); } finally { PluginHelper.DataManager = new StubDataManager(raEmu, dolphinEmu); LbIntegrations.Saves.SaveRouter.ForgetStock(); }
                }
                if (Claimed())
                {
                    Expect("D6: no plugin with saves on PS1 - DuckStation keeps the SDK's false", !Says(duckEmu, raEmu, dolphinEmu), "true");
                    var raPs1 = new StubEmulator { Title = "RetroArch", ApplicationPath = raEmu.ApplicationPath };
                    Line(raPs1, "Sony Playstation", "-L \"cores\\mednafen_psx_hw_libretro.dll\" -f");
                    Blank(raPs1); foreach (var l in raPs1.GetAllEmulatorPlatforms()) Blank(l);
                    Expect("D6: RetroArch covers PS1 - DuckStation says it supports saves", Says(duckEmu, raPs1), "false");
                    LbIntegrations.Compat.Compat.UseForTests(new Dictionary<string, bool> { ["stockNoSaves"] = false });
                    try { Expect("D6: ... the switch off - the SDK's false again", !Says(duckEmu, raPs1), "true"); }
                    finally { LbIntegrations.Compat.Compat.UseForTests(null); }
                }
                else Console.WriteLine("     (the stand-in is not DuckStation's: D6 not tried here - to see in LaunchBox)");

                // Lot 5, step 2: its listing is the other emulators' saves (a success even empty), their rows go to their owners,
                // and a row nobody claims is never deleted by the SDK's body.
                var none = new GetSavesArgs { Games = Array.Empty<IGame>(), AdditionalApplications = Array.Empty<IAdditionalApplication>() };
                var listed = duck.GetSaves(none);
                Expect("DuckStation's GetSaves: a success (the SDK's \"not supported\" replaced)", listed?.WasSuccess == true, listed?.Message ?? "null");
                LbIntegrations.Compat.Compat.UseForTests(new Dictionary<string, bool> { ["stockNoSaves"] = false });
                try { Expect("... the switch off: the SDK's failure again", duck.GetSaves(none)?.WasSuccess == false, "success"); }
                finally { LbIntegrations.Compat.Compat.UseForTests(null); }
                fake.Calls.Clear();
                Expect("an ares row on DuckStation: answered by ares", !duck.IsSaveActive(aresRow, duckEmu.ApplicationPath) && fake.Calls.Contains("IsSaveActive"), string.Join(",", fake.Calls));
                var orphan = Path.Combine(scratch, "nobody.srm");
                File.WriteAllText(orphan, "x");
                var orphanRow = new GameSaveGame { GameId = zelda.Id, FileLocation = orphan, SaveGroupId = "" };
                // A Yes / No (Mehdi 12/10), No by default; answered here instead of a window.
                var asked = new List<string>();
                LbIntegrations.Saves.SlotRestore.Instead = (title, text) => { asked.Add(text); return false; };
                try
                {
                    var removed = duck.RemoveSave(orphanRow);
                    Expect("a row nobody claims, removed on DuckStation: the user asked", asked.Count == 1 && asked[0].Contains(orphan), string.Join(" | ", asked));
                    Expect("... answered No: refused, its file left", removed?.WasSuccess == false && File.Exists(orphan), removed?.Message ?? "null");
                    asked.Clear();
                    LbIntegrations.Compat.Compat.UseForTests(new Dictionary<string, bool> { ["stockNoSaves"] = false });
                    try { removed = duck.RemoveSave(orphanRow); }
                    finally { LbIntegrations.Compat.Compat.UseForTests(null); }
                    Expect("... `stockNoSaves` off: still asked, still kept", asked.Count == 1 && removed?.WasSuccess == false && File.Exists(orphan), asked.Count + " " + removed?.Message);
                    asked.Clear();
                    LbIntegrations.Saves.SlotRestore.HostOverride = "bigbox";
                    try { removed = duck.RemoveSave(orphanRow); }
                    finally { LbIntegrations.Saves.SlotRestore.HostOverride = null; }
                    Expect("... in BigBox: refused without a question, kept", asked.Count == 0 && removed?.WasSuccess == false && File.Exists(orphan), asked.Count + " " + removed?.Message);
                    LbIntegrations.Saves.SlotRestore.Instead = (title, text) => { asked.Add(text); return true; };
                    removed = duck.RemoveSave(orphanRow);
                    Expect("... answered Yes: the SDK's body deletes it", removed?.WasSuccess == true && !File.Exists(orphan), removed?.Message ?? "null");
                }
                finally { LbIntegrations.Saves.SlotRestore.Instead = null; }
            }

            Console.WriteLine("  " + (bad == 0 ? "all good" : bad + " FAILURE(S)"));
            Console.WriteLine();
            Console.WriteLine("  scratch left in " + scratch);
            return true;
        }

        /// <summary>A pack plugin's stand-in: answers the save calls with its own markers and notes each one.</summary>
        private sealed class FakeOwner : EmulatorPlugin
        {
            public readonly List<string> Calls = new List<string>();
            /// <summary>The emulator a desk said the row came from, seen inside AddSaveFile.</summary>
            public string Via;
            public override string EmulatorName => "Fake ares";
            public override IEnumerable<IEmulator> GetApplicableEmulators(IEnumerable<IEmulator> emulators) => Array.Empty<IEmulator>();
            public override string GetCurrentVersion(string applicationPath) => "";
            public override IEnumerable<EmulatorControllerVersion> GetInstallableVersions() => Array.Empty<EmulatorControllerVersion>();
            public override EmulatorInstallResponse InstallEmulator(InstallEmulatorArgs args) => null;
            public override EmulatorSupportResponse IsPlatformSupported(string platform) => null;
            public override bool SupportsSaveManagement() => true;
            public override bool IsSaveActive(GameSaveBase save, string emulatorApplicationPath) { Calls.Add("IsSaveActive"); return false; }
            public override bool UseSaveGroupIdForPersistedMatch(GameSaveBase save) { Calls.Add("UseSaveGroupIdForPersistedMatch"); return true; }
            public override bool TryComputeSaveSignature(GameSaveBase save, string emulatorApplicationPath, out string signature) { Calls.Add("TryComputeSaveSignature"); signature = "ares-signature"; return true; }
            public override AddSaveResponse AddSaveFile(AddSaveArgs args)
            {
                Calls.Add("AddSaveFile");
                Via = LbIntegrations.Saves.SaveForward.ViaEmulator(_ => true)?.Id;
                // Asked to: a restored row in another integration's NATIVE form, as an incomplete PCSX2 gives one back.
                if (args?.SaveToAdd?.SaveGroupName == "native result")
                    return new AddSaveResponse(new GameSaveGame { GameId = args.SaveToAdd.GameId, FileLocation = args.SaveToAdd.FileLocation, SaveGroupId = "pcsx2:SLUS-20062" });
                return new AddSaveResponse("restored by ares");
            }
        }

        private static List<GameSaveBase> Rows(EmulatorPlugin plugin, IEmulator emu, params IGame[] games)
        {
            var all = new List<GameSaveBase>();
            if (plugin == null) return all;
            foreach (var g in games)
            {
                try
                {
                    var rows = plugin.GetSaves(new GetSavesArgs { Emulator = emu, Games = new[] { g }, AdditionalApplications = Array.Empty<IAdditionalApplication>() })?.FoundSaves ?? Array.Empty<GameSaveBase>();
                    Console.WriteLine("  " + plugin.GetType().Name + " / " + g.Title + ": " + rows.Count() + " row(s)");
                    foreach (var r in rows)
                    {
                        Console.WriteLine("     " + r.GetType().Name.PadRight(13) + " id=\"" + r.SaveGroupId + "\" lineage=\"" + r.MatchLineageId + "\" slot=" + Slot(r) + " core=" + r.EmulatorCore + " exe=" + r.EmulatorFileName + " group=\"" + r.SaveGroupName + "\"");
                        Console.WriteLine("       " + r.FileLocation);
                    }
                    all.AddRange(rows);
                }
                catch (Exception ex) { Console.WriteLine("  " + plugin.GetType().Name + " / " + g.Title + ": throws " + (ex.InnerException ?? ex)); }
            }
            return all;
        }

        /// <summary>Every writable text left null made empty, as LaunchBox's own objects carry them.</summary>
        private static void Blank(object o)
        {
            foreach (var p in o.GetType().GetProperties().Where(p => p.PropertyType == typeof(string) && p.CanRead && p.CanWrite))
                if (p.GetValue(o) == null) p.SetValue(o, "");
        }

        private static void NullMatch(System.Text.RegularExpressions.Regex __instance, string input) { if (input == null) Console.WriteLine("     (a regex got null: /" + __instance + "/)"); }

        private static string Slot(GameSaveBase r) => r is GameSaveState s ? s.Slot.ToString() : "-";

        private static GameSaveBase WithId(GameSaveBase row, string id)
        {
            var copy = (GameSaveBase)typeof(object).GetMethod("MemberwiseClone", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(row, null);
            typeof(GameSaveBase).GetProperty(nameof(GameSaveBase.SaveGroupId)).SetValue(copy, id);
            return copy;
        }

        // ── Harmony: does a prefix that swaps __0 for a copy reach the original? ──
        private static string _seen;
        private static bool Swap(ref GameSaveBase __0) { __0 = WithId(__0, "REPLACED"); return true; }
        private static void Spy(GameSaveBase __0) => _seen = __0?.SaveGroupId;

        private static void HarmonyReplace(EmulatorPlugin plugin, GameSaveBase row, string exe)
        {
            if (plugin == null || row == null) { Console.WriteLine("  nothing to try it on"); return; }
            var m = plugin.GetType().GetMethod("TryGetSaveGroupInfo", BindingFlags.Public | BindingFlags.Instance);
            var h = new Harmony("lbip.probe.stock-saves");
            try
            {
                h.Patch(m, prefix: new HarmonyMethod(typeof(StockSavesCheck).GetMethod(nameof(Swap), BindingFlags.NonPublic | BindingFlags.Static)) { priority = Priority.First });
                h.Patch(m, prefix: new HarmonyMethod(typeof(StockSavesCheck).GetMethod(nameof(Spy), BindingFlags.NonPublic | BindingFlags.Static)) { priority = Priority.Last });
                var before = row.SaveGroupId;
                var ok = plugin.TryGetSaveGroupInfo(row, row.FileLocation, out var g, out var n);
                Console.WriteLine("  " + m.DeclaringType.Name + "." + m.Name + ": the later prefix saw \"" + _seen + "\"; the original answered " + ok + " id=\"" + g + "\"; the caller's row still \"" + row.SaveGroupId + "\" (was \"" + before + "\")");
            }
            catch (Exception ex) { Console.WriteLine("  throws " + ex.Message); }
            finally { h.UnpatchAll("lbip.probe.stock-saves"); }
        }

        private static void CopyTree(string from, string to)
        {
            Directory.CreateDirectory(to);
            foreach (var f in Directory.GetFiles(from)) File.Copy(f, Path.Combine(to, Path.GetFileName(f)));
            foreach (var d in Directory.GetDirectories(from)) CopyTree(d, Path.Combine(to, Path.GetFileName(d)));
        }

        private static string Show(object o) => o switch { null => "null", bool b => b ? "true" : "false", _ => o.ToString() };
        private static string Cut(string s) => s == null ? "null" : s.Length > 48 ? s.Substring(0, 45) + "..." : s;
        private static void Section(string title) { Console.WriteLine(); Console.WriteLine("== " + title); }
    }
}
