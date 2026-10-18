// The 3dSen plugin's pieces, run without a host - on COPIES of the lab's files (docs\3dsen.md: G:\LB1326\lbip-3dsen-lab,
// its emu\ copy of 3dSen PC 1.1.2, its locallow-before-2026-10-18\ and locallow-after-tests\ user data):
//   --3dsen <lab folder>      the version constant, the folder check, the profiles and their matching, rom.json's in-place
//                             edit, input.cfg's keys, the archives, the command line, Steam's files (fixtures), the
//                             platforms and the update rule, and a whole launch's preparation into a copy of the user data
//   --3dsen-steam             what this machine's Steam says about 3dSen (read only)
//   --3dsen-prepare <3dSen.exe> --rom <game> --title <title>   a launch prepared FOR REAL into 3dSen's user data folder,
//                             its line printed (for an oracle run; back the folder up first)

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Text;

namespace LbIntegrations.Probe
{
    internal static class ThreeDSenCheck
    {
        private const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
        private static int _bad;

        private static void Check(string what, bool ok, string detail = null)
        {
            if (!ok) _bad++;
            Console.WriteLine("  [" + (ok ? "ok" : "FAIL") + "] " + what + (detail != null ? " - " + detail : ""));
        }

        private static Type T(Assembly asm, string name) => asm.GetType("LbIntegrations.ThreeDSen." + name, true);
        /// <summary>A static method by name: the overload taking these many arguments, its optional ones given their defaults.</summary>
        private static object Call(Assembly asm, string type, string method, params object[] a)
        {
            var m = T(asm, type).GetMethods(Any).Where(x => x.Name == method).OrderBy(x => x.GetParameters().Length)
                                .First(x => x.GetParameters().Length >= a.Length && x.GetParameters().Skip(a.Length).All(q => q.IsOptional));
            return m.Invoke(null, a.Concat(m.GetParameters().Skip(a.Length).Select(q => q.DefaultValue)).ToArray());
        }
        private static void SetStatic(Assembly asm, string type, string field, object value) => T(asm, type).GetField(field, Any).SetValue(null, value);
        private static object F(object o, string name) => o?.GetType().GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(o);

        public static bool Run(Assembly asm, string lab)
        {
            _bad = 0;
            var emu = Path.Combine(lab, "emu");
            var exe = Path.Combine(emu, "3dSen.exe");
            var after = Path.Combine(lab, "locallow-after-tests");
            var before = Path.Combine(lab, "locallow-before-2026-10-18");
            var temp = Path.Combine(Path.GetTempPath(), "lbip-3dsen-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(temp);
            try
            {
                Version(asm, lab, exe);
                Profiles(asm, after);
                RomJson(asm, after, temp);
                Input(asm, before, after, temp);
                Archives(asm, lab, temp);
                Line(asm);
                Steam(asm, temp);
                Plugin(asm);
                Launch(asm, exe, after, lab, temp);
                Saves(asm, after, temp);
            }
            catch (Exception ex) { Check("no exception", false, (ex.InnerException ?? ex).ToString()); }
            finally
            {
                try { SetStatic(asm, "ThreeDSenLaunch", "DataDirOverride", null); SetStatic(asm, "ThreeDSenSettings", "PathOverride", null); SetStatic(asm, "ThreeDSenSteam", "RootOverride", null); } catch { }
                try { Directory.Delete(temp, true); } catch { }
            }
            Console.WriteLine(_bad == 0 ? "  all good" : "  " + _bad + " FAILED");
            return _bad == 0;
        }

        private static void Version(Assembly asm, string lab, string exe)
        {
            Console.WriteLine("-- version, folder --");
            var v = (string)Call(asm, "ThreeDSenInstall", "VersionOf", exe);
            Check("Nes3D.Info.Version of the lab's copy is 1.1.2", v == "1.1.2", v);
            var args = new object[] { Path.Combine(lab, "emu"), null };
            var found = (string)T(asm, "ThreeDSenInstall").GetMethod("ExeIn", Any).Invoke(null, args);
            Check("the install folder itself is accepted", string.Equals(found, exe, StringComparison.OrdinalIgnoreCase), found ?? (string)args[1]);
            // Its parent: a folder of its own holding a junction to the lab's copy (the lab itself may hold several copies).
            var parent = Path.Combine(Path.GetTempPath(), "lbip-3dsen-parent-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(parent);
            try
            {
                var link = Path.Combine(parent, "3dsenpc-win");
                using (var mk = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("cmd.exe", "/c mklink /J \"" + link + "\" \"" + Path.GetDirectoryName(exe) + "\"")
                                                                  { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true })) mk.WaitForExit();
                args = new object[] { parent, null };
                found = (string)T(asm, "ThreeDSenInstall").GetMethod("ExeIn", Any).Invoke(null, args);
                Check("its parent is accepted (its one sub-folder holding 3dSen.exe)", string.Equals(found, Path.Combine(link, "3dSen.exe"), StringComparison.OrdinalIgnoreCase), found ?? (string)args[1]);
                Directory.Delete(link);
            }
            finally { try { Directory.Delete(parent, true); } catch { } }
            args = new object[] { Path.Combine(lab, "roms"), null };
            found = (string)T(asm, "ThreeDSenInstall").GetMethod("ExeIn", Any).Invoke(null, args);
            Check("a folder without 3dSen.exe is refused, saying why", found == null && ((string)args[1] ?? "").Contains("3dSen.exe"), (string)args[1]);
        }

        private static IList<object> ProfileList(Assembly asm, string data) => ((System.Collections.IEnumerable)Call(asm, "ThreeDSenProfiles", "All", data)).Cast<object>().ToList();

        private static string Matched(Assembly asm, IList<object> profiles, string title, string rom)
        {
            var listType = typeof(List<>).MakeGenericType(T(asm, "ThreeDSenProfile"));
            var list = (System.Collections.IList)Activator.CreateInstance(listType);
            foreach (var p in profiles) list.Add(p);
            var m = Call(asm, "ThreeDSenProfiles", "Match", list, title, rom);
            return (string)F(m, "Id");
        }

        private static void Profiles(Assembly asm, string after)
        {
            Console.WriteLine("-- profiles --");
            var json = (System.Collections.IEnumerable)Call(asm, "ThreeDSenRomJson", "Items", Path.Combine(after, "rom.json"));
            var items = json.Cast<object>().ToList();
            var table = ((Array)T(asm, "ThreeDSenProfiles").GetField("BuiltIn", Any).GetValue(null)).Cast<object>()
                .Select(t => ((string)t.GetType().GetField("Item1").GetValue(t), (string)t.GetType().GetField("Item2").GetValue(t))).ToList();
            Check("rom.json of 1.1.2 holds 101 profiles", items.Count == 101, items.Count.ToString());
            var differ = table.Where(t => !items.Any(i => (string)F(i, "Id") == t.Item1 && (string)F(i, "Title") == t.Item2)).Select(t => t.Item1).ToList();
            Check("the built-in table is rom.json's, id for id and title for title", table.Count == 101 && differ.Count == 0, differ.Count == 0 ? null : "differ: " + string.Join(", ", differ));
            var profiles = ProfileList(asm, after);
            Check("All() gives 101 profiles", profiles.Count == 101, profiles.Count.ToString());
            var none = ProfileList(asm, Path.Combine(after, "no-such-folder"));
            Check("without rom.json: the built-in table", none.Count == 101, none.Count.ToString());

            var selfFails = table.Where(t => t.Item1 != "47" && Matched(asm, profiles, t.Item2, null) != t.Item1).Select(t => t.Item2).ToList();
            Check("every profile's own title matches it alone", selfFails.Count == 0, selfFails.Count == 0 ? null : string.Join(" | ", selfFails));
            foreach (var (title, rom, want) in new[]
            {
                ("Zelda II: The Adventure of Link", @"G:\roms\Zelda II - The Adventure of Link (Europe).nes", "73"),
                ((string)null, @"G:\roms\Zelda II - The Adventure of Link (Europe).nes", "73"),
                ("Dig Dug", null, "10"), ("Pac-Man", null, "33"), ("Super Mario Bros.", null, "37"), ("Super Mario Bros. 3", null, "55"),
                ("Mega Man", null, "30"), ("Mega Man 2", null, "31"), ("Adventures of Lolo 2", null, "66"), ("The Legend of Zelda", null, "45"),
                ("DuckTales", null, "59"), ("Chip 'n Dale: Rescue Rangers", null, "58"), ("Mike Tyson's Punch-Out!!", null, "87"),
                ("Batman: The Video Game", null, "3"), ("Dr. Mario", null, "14"), ("Tetris", null, "38"),
                ("Batman: Return of the Joker", null, null), ("Mega Man 4", null, null), ("2D Mode", null, null), ("Some Unknown Game", @"G:\roms\Unknown (USA).nes", null),
            })
            {
                var got = Matched(asm, profiles, title, rom);
                Check("\"" + (title ?? Path.GetFileName(rom)) + "\" -> " + (want ?? "no profile"), got == want, got ?? "none");
            }
        }

        private static void RomJson(Assembly asm, string after, string temp)
        {
            Console.WriteLine("-- rom.json --");
            var dir = Path.Combine(temp, "romjson");
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, "rom.json");
            File.Copy(Path.Combine(after, "rom.json"), path);
            var original = File.ReadAllBytes(path);
            var rom = @"G:\Jeux NES\Arkanoïd ""x"" (Europe).nes";
            var err = (string)Call(asm, "ThreeDSenRomJson", "SetRom", path, "1", "Arkanoid", rom);
            Check("set the ROM of profile 1", err == null, err);
            var bytes = File.ReadAllBytes(path);
            var text = Encoding.UTF8.GetString(bytes);
            Check("read back as written", (string)Call(asm, "ThreeDSenRomJson", "RomOf", path, "1") == rom);
            Check("escaped as JsonUtility writes it", text.Contains("\"romPath\": \"G:\\\\Jeux NES\\\\Arkanoïd \\\"x\\\" (Europe).nes\""));
            Check("LF, no BOM, no newline at the end, as 3dSen writes it", !text.Contains("\r\n") && bytes[0] == (byte)'{' && bytes[^1] == (byte)'}');
            var origText = Encoding.UTF8.GetString(original);
            var cut = origText.IndexOf("\"id\": \"1\",", StringComparison.Ordinal);
            var valueAt = origText.IndexOf("\"romPath\": \"\"", cut, StringComparison.Ordinal) + "\"romPath\": ".Length;
            var expected = origText.Substring(0, valueAt) + "\"G:\\\\Jeux NES\\\\Arkanoïd \\\"x\\\" (Europe).nes\"" + origText.Substring(valueAt + 2);
            Check("nothing else changed, byte for byte", text == expected);
            var stamp = File.GetLastWriteTimeUtc(path);
            System.Threading.Thread.Sleep(30);
            Call(asm, "ThreeDSenRomJson", "SetRom", path, "1", "Arkanoid", rom);
            Check("the same ROM again writes nothing", File.GetLastWriteTimeUtc(path) == stamp);
            err = (string)Call(asm, "ThreeDSenRomJson", "SetRom", path, "custom-9", "A Custom Game", @"C:\x.nes");
            var items = ((System.Collections.IEnumerable)Call(asm, "ThreeDSenRomJson", "Items", path)).Cast<object>().ToList();
            Check("a profile missing from it is added at the end", err == null && items.Count == 102 && (string)F(items[^1], "Id") == "custom-9" && (string)F(items[^1], "RomPath") == @"C:\x.nes", err);
            try { System.Text.Json.JsonDocument.Parse(File.ReadAllBytes(path)).Dispose(); Check("still JSON", true); } catch (Exception ex) { Check("still JSON", false, ex.Message); }

            var fresh = Path.Combine(temp, "fresh");
            Directory.CreateDirectory(fresh);
            err = (string)Call(asm, "ThreeDSenRomJson", "SetRom", Path.Combine(fresh, "rom.json"), "73", "Zelda II: The Adventure of Link", @"G:\z.nes");
            var made = File.ReadAllText(Path.Combine(fresh, "rom.json"));
            Check("no rom.json: one made, in 3dSen's layout", err == null && made.StartsWith("{\n    \"Warning\": \"Don't touch id parameter,  you will regret it\",\n    \"Items\": [\n        {\n            \"id\": \"73\",\n            \"romPath\": \"G:\\\\z.nes\","), made.Replace("\n", "|"));
            var old = Path.Combine(temp, "old");
            Directory.CreateDirectory(old);
            File.WriteAllBytes(Path.Combine(old, "rom.cfg"), new byte[] { 0x0A, 0x00 });
            err = (string)Call(asm, "ThreeDSenRomJson", "SetRom", Path.Combine(old, "rom.json"), "73", "Zelda", @"G:\z.nes");
            Check("an older rom.cfg without rom.json: refused (3dSen would delete it)", err != null && !File.Exists(Path.Combine(old, "rom.json")), err);
        }

        /// <summary>The states: sav.cfg read and written back byte for byte, the ROM inside a state, 2D Mode's states told
        /// apart by it, a restore into another slot and its delete - on a copy of 3dSen's own files (locallow-after-tests).</summary>
        private static void Saves(Assembly asm, string after, string temp)
        {
            Console.WriteLine("-- save states --");
            if ((bool)Call(asm, "ThreeDSenLaunch", "AnyRunning")) { Console.WriteLine("  (3dSen is running here - the state checks are skipped)"); return; }
            var data = Path.Combine(temp, "saves");
            Directory.CreateDirectory(Path.Combine(data, "sav"));
            File.Copy(Path.Combine(after, "sav.cfg"), Path.Combine(data, "sav.cfg"));
            File.Copy(Path.Combine(after, "sav", "quick.sav"), Path.Combine(data, "sav", "quick.sav"));
            var cfg = Path.Combine(data, "sav.cfg");
            var original = File.ReadAllBytes(cfg);
            var slots = (System.Collections.IList)Call(asm, "ThreeDSenSavCfg", "Read", cfg);
            object Slot(System.Collections.IList l, string id) => l.Cast<object>().FirstOrDefault(s => (string)F(s, "SlotId") == id);
            Check("sav.cfg: 37 slots, the quick one Zelda II's (profile 73)", slots != null && slots.Count == 37 && (string)F(Slot(slots, "quick"), "GameId") == "73", slots?.Count.ToString());
            Check("written back byte for byte", ((byte[])Call(asm, "ThreeDSenSavCfg", "Write", slots)).SequenceEqual(original));
            // The encoder a restore uses for the slot it changes, against what 3dSen itself wrote for the quick slot.
            var q = Slot(slots, "quick");
            var raw = (byte[])F(q, "Raw");
            q.GetType().GetField("Raw").SetValue(q, null);
            Check("an item encoded from its fields = 3dSen's own bytes (the quick slot, its picture included)", raw != null && ((byte[])Call(asm, "ThreeDSenSavCfg", "Encode", q)).SequenceEqual(raw));
            q.GetType().GetField("Raw").SetValue(q, raw);
            var quick = Path.Combine(data, "sav", "quick.sav");
            var romName = (string)Call(asm, "ThreeDSenSavCfg", "RomNameOf", quick);
            Check("the ROM inside quick.sav", romName == "Zelda II zipped.nes", romName);

            var profileType = T(asm, "ThreeDSenProfile");
            object Profile(string id) { var p = Activator.CreateInstance(profileType, true); profileType.GetField("Id").SetValue(p, id); profileType.GetField("Title").SetValue(p, id); return p; }
            var fits = T(asm, "ThreeDSenPlugin").GetMethod("StateFits", Any);
            bool Fits(string id, string rom) => (bool)fits.Invoke(null, new object[] { Profile(id), quick, rom });
            Check("a 3D profile's state is the game's whatever its ROM", Fits("73", @"G:\roms\Anything.nes"));
            Check("2D Mode: the state of the game it was made from (an archive's name)", Fits("47", @"G:\roms\Zelda II zipped.zip"));
            Check("2D Mode: not another game's", !Fits("47", @"G:\roms\Mega Man.nes"));

            // A backup of the quick slot restored into slot 5, then deleted.
            var backup = Path.Combine(temp, "backup-quick.sav");
            File.Copy(quick, backup);
            var group = (string)Call(asm, "ThreeDSenPlugin", "StateIdOf", "73", 0);
            var restore = T(asm, "ThreeDSenPlugin").GetMethod("Restore", Any);
            var ra = new object[] { data, backup, null, 5, group, "73", true, null, (Func<bool>)(() => true), null, null };
            var refused = (string)restore.Invoke(null, ra);
            var target = Path.Combine(data, "sav", "5.sav");
            var after5 = (System.Collections.IList)Call(asm, "ThreeDSenSavCfg", "Read", cfg);
            Check("restored into slot 5: the file, its slot given to profile 73", refused == null && File.Exists(target) && (string)F(Slot(after5, "5"), "GameId") == "73", refused);
            var before5 = slots.Cast<object>().Where(s => (string)F(s, "SlotId") != "5").Select(s => Convert.ToBase64String((byte[])Call(asm, "ThreeDSenSavCfg", "Encode", s))).ToList();
            var now5 = after5.Cast<object>().Where(s => (string)F(s, "SlotId") != "5").Select(s => Convert.ToBase64String((byte[])Call(asm, "ThreeDSenSavCfg", "Encode", s))).ToList();
            Check("every other slot of sav.cfg as it was", before5.SequenceEqual(now5));
            var groupFive = (string)Call(asm, "ThreeDSenPlugin", "StateIdOf", "73", 5);
            var del = (string)Call(asm, "ThreeDSenPlugin", "Delete", data, target, groupFive);
            var afterDel = (System.Collections.IList)Call(asm, "ThreeDSenSavCfg", "Read", cfg);
            Check("deleted: the file gone, slot 5 free again", del == null && !File.Exists(target) && string.IsNullOrEmpty((string)F(Slot(afterDel, "5"), "GameId")), del);
            Check("the quick slot untouched", File.Exists(quick) && (string)F(Slot(afterDel, "quick"), "GameId") == "73");

            // The portable mode: a platform's States folder, sav.cfg and the slots side by side.
            var states = Path.Combine(temp, "portable-saves", "Nintendo Entertainment System", "States");
            Directory.CreateDirectory(states);
            ra = new object[] { states, backup, null, 7, group, "73", true, null, (Func<bool>)(() => true), null, null };
            refused = (string)restore.Invoke(null, ra);
            var seven = Path.Combine(states, "7.sav");
            var pslots = (System.Collections.IList)Call(asm, "ThreeDSenSavCfg", "Read", Path.Combine(states, "sav.cfg"));
            Check("portable: restored into States\\7.sav, a new sav.cfg beside it", refused == null && File.Exists(seven) && (string)F(Slot(pslots, "7"), "GameId") == "73", refused);
            del = (string)Call(asm, "ThreeDSenPlugin", "Delete", states, seven, (string)Call(asm, "ThreeDSenPlugin", "StateIdOf", "73", 7));
            pslots = (System.Collections.IList)Call(asm, "ThreeDSenSavCfg", "Read", Path.Combine(states, "sav.cfg"));
            Check("portable: deleted, its slot freed", del == null && !File.Exists(seven) && string.IsNullOrEmpty((string)F(Slot(pslots, "7"), "GameId")), del);
        }

        private static void Input(Assembly asm, string before, string after, string temp)
        {
            Console.WriteLine("-- input.cfg --");
            var dir = Path.Combine(temp, "input");
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, "input.cfg");
            File.Copy(Path.Combine(before, "input.cfg"), path);
            Check("3dSen's defaults: QuickSave unbound", (string)Call(asm, "ThreeDSenInput", "KeyName", dir, "QuickSave") == "None");
            Call(asm, "ThreeDSenInput", "BindStateKeys", dir);
            Check("QuickSave = F2", (string)Call(asm, "ThreeDSenInput", "KeyName", dir, "QuickSave") == "F2");
            Check("QuickLoad = F4", (string)Call(asm, "ThreeDSenInput", "KeyName", dir, "QuickLoad") == "F4");
            Check("Menu = F1 (off Esc, which quits)", (string)Call(asm, "ThreeDSenInput", "KeyName", dir, "Menu") == "F1", (string)Call(asm, "ThreeDSenInput", "KeyName", dir, "Menu"));
            Check("FastForward = L (off Period)", (string)Call(asm, "ThreeDSenInput", "KeyName", dir, "FastForward") == "L", (string)Call(asm, "ThreeDSenInput", "KeyName", dir, "FastForward"));
            Check("RotLeft still F5 (the camera untouched)", (string)Call(asm, "ThreeDSenInput", "KeyName", dir, "RotLeft") == "F5");
            var stamp = File.GetLastWriteTimeUtc(path);
            System.Threading.Thread.Sleep(30);
            Call(asm, "ThreeDSenInput", "BindStateKeys", dir);
            Check("a second launch writes nothing", File.GetLastWriteTimeUtc(path) == stamp);
            Call(asm, "ThreeDSenInput", "BindStateKeys", dir, true);
            Check("slots: QuickSave and QuickLoad off F2 / F4 (the plugin's)", (string)Call(asm, "ThreeDSenInput", "KeyName", dir, "QuickSave") == "None"
                  && (string)Call(asm, "ThreeDSenInput", "KeyName", dir, "QuickLoad") == "None");
            Check("slots: the camera on the keypad (RotLeft Pad4, RotRight Pad6, ZoomIn Pad+)", (string)Call(asm, "ThreeDSenInput", "KeyName", dir, "RotLeft") == "Pad4"
                  && (string)Call(asm, "ThreeDSenInput", "KeyName", dir, "RotRight") == "Pad6" && (string)Call(asm, "ThreeDSenInput", "KeyName", dir, "ZoomIn") == "Pad+",
                  (string)Call(asm, "ThreeDSenInput", "KeyName", dir, "RotLeft"));
            Check("slots: Menu still F1", (string)Call(asm, "ThreeDSenInput", "KeyName", dir, "Menu") == "F1");
            Call(asm, "ThreeDSenInput", "BindStateKeys", dir, false);
            Check("slots off again: F2 / F4 back, the camera back on F5 / F6", (string)Call(asm, "ThreeDSenInput", "KeyName", dir, "QuickSave") == "F2"
                  && (string)Call(asm, "ThreeDSenInput", "KeyName", dir, "RotLeft") == "F5" && (string)Call(asm, "ThreeDSenInput", "KeyName", dir, "RotRight") == "F6",
                  (string)Call(asm, "ThreeDSenInput", "KeyName", dir, "RotLeft"));

            // F2 already the user's on another action: QuickSave left unbound, QuickLoad still bound.
            File.Copy(Path.Combine(before, "input.cfg"), path, true);
            var entries = (System.Collections.IList)Call(asm, "ThreeDSenInput", "ParseMap", File.ReadAllBytes(path));
            for (int i = 0; i < entries.Count; i++)
            {
                var kv = entries[i];
                var key = (string)kv.GetType().GetProperty("Key").GetValue(kv);
                if (key != "Keyboard0 Binding") continue;
                var set = Call(asm, "ThreeDSenInput", "ParseSet", Convert.FromBase64String((string)kv.GetType().GetProperty("Value").GetValue(kv)));
                foreach (var a in (System.Collections.IEnumerable)F(set, "Actions"))
                    if ((string)F(a, "Name") == "RotLeft")
                        foreach (var b in (System.Collections.IEnumerable)F(a, "Bindings")) b.GetType().GetField("Inc").SetValue(b, 0x0FUL);
                entries[i] = Activator.CreateInstance(kv.GetType(), key, Convert.ToBase64String((byte[])Call(asm, "ThreeDSenInput", "WriteSet", set)));
            }
            File.WriteAllBytes(path, (byte[])Call(asm, "ThreeDSenInput", "WriteMap", entries));
            Call(asm, "ThreeDSenInput", "BindStateKeys", dir);
            Check("F2 held by RotLeft: QuickSave left unbound", (string)Call(asm, "ThreeDSenInput", "KeyName", dir, "QuickSave") == "None");
            Check("... QuickLoad still bound to F4", (string)Call(asm, "ThreeDSenInput", "KeyName", dir, "QuickLoad") == "F4");
            var garbage = Path.Combine(temp, "garbage");
            Directory.CreateDirectory(garbage);
            File.WriteAllBytes(Path.Combine(garbage, "input.cfg"), new byte[] { 1, 2, 3 });
            Call(asm, "ThreeDSenInput", "BindStateKeys", garbage);
            Check("a file it does not understand is left as it is", File.ReadAllBytes(Path.Combine(garbage, "input.cfg")).SequenceEqual(new byte[] { 1, 2, 3 }));
        }

        private static void Archives(Assembly asm, string lab, string temp)
        {
            Console.WriteLine("-- archives --");
            var fakeExe = Path.Combine(temp, "emu", "3dSen.exe");
            Directory.CreateDirectory(Path.GetDirectoryName(fakeExe));
            File.WriteAllText(fakeExe, "");
            var zip = Path.Combine(lab, "roms", "Zelda II zipped.zip");
            var args = new object[] { fakeExe, zip, null };
            var got = (string)T(asm, "ThreeDSenLaunch").GetMethod("RomFor", Any).Invoke(null, args);
            Check("a .zip whose first entry is the .nes: given as is (measured)", got == zip, got ?? (string)args[2]);
            var nes = Path.Combine(lab, "roms", "Zelda II - The Adventure of Link (Europe).nes");
            args = new object[] { fakeExe, nes, null };
            Check("a .nes: as is", (string)T(asm, "ThreeDSenLaunch").GetMethod("RomFor", Any).Invoke(null, args) == nes);
            var other = Path.Combine(temp, "Zelda readme first.zip");
            using (var z = ZipFile.Open(other, ZipArchiveMode.Create))
            {
                using (var w = new StreamWriter(z.CreateEntry("readme.txt").Open())) w.Write("hello");
                z.CreateEntryFromFile(nes, "Zelda II.nes");
            }
            args = new object[] { fakeExe, other, null };
            got = (string)T(asm, "ThreeDSenLaunch").GetMethod("RomFor", Any).Invoke(null, args);
            Check("a .zip whose first entry is not the ROM: unpacked under the install's Unpacked\\", got != null && got.StartsWith(Path.Combine(temp, "emu", "Unpacked")) && File.ReadAllBytes(got).SequenceEqual(File.ReadAllBytes(nes)), got ?? (string)args[2]);
            var empty = Path.Combine(temp, "nothing.zip");
            using (var z = ZipFile.Open(empty, ZipArchiveMode.Create)) using (var w = new StreamWriter(z.CreateEntry("readme.txt").Open())) w.Write("hello");
            args = new object[] { fakeExe, empty, null };
            got = (string)T(asm, "ThreeDSenLaunch").GetMethod("RomFor", Any).Invoke(null, args);
            Check("an archive without a .nes: refused, saying why", got == null && ((string)args[2] ?? "").Contains(".nes"), (string)args[2]);
        }

        private static void Line(Assembly asm)
        {
            Console.WriteLine("-- command line --");
            List<string> Sw(string id, string screen, string size) => (List<string>)Call(asm, "ThreeDSenLaunch", "Switches", id, screen, size);
            string Cl(string current, string rom, List<string> sw) => (string)Call(asm, "ThreeDSenLaunch", "CommandLine", current, rom, sw);
            var rom = @"G:\Jeux NES\Zelda II (Europe).nes";
            var line = Cl("\"" + rom + "\"", rom, Sw("73", "window", "1600x900"));
            Check("window: the game path off, the whole set on", line == "-id=73 -screen-fullscreen 0 -screen-width 1600 -screen-height 900", line);
            line = Cl("-id=5 -screen-fullscreen 1 -screen-width 800 -screen-height 600 -logFile log.txt \"" + rom + "\"", rom, Sw("73", "asis", "1280x720"));
            Check("ours of an earlier line out, the user's kept; \"as 3dSen had it\" passes no screen", line == "-id=73 -logFile log.txt", line);
            var res = Call(asm, "ThreeDSenLaunch", "PrimaryResolution");
            line = Cl("\"" + rom + "\"", rom, Sw("73", "full", null));
            Check("full screen at the monitor's resolution", res != null && line.StartsWith("-id=73 -screen-fullscreen 1 -screen-width "), line);
        }

        private static void Steam(Assembly asm, string temp)
        {
            Console.WriteLine("-- Steam's files (fixtures) --");
            var root = Path.Combine(temp, "Steam");
            var lib = Path.Combine(temp, "Library2");
            Directory.CreateDirectory(Path.Combine(root, "steamapps"));
            File.WriteAllText(Path.Combine(root, "steam.exe"), "");
            Directory.CreateDirectory(Path.Combine(lib, "steamapps", "common", "3dSen"));
            File.WriteAllText(Path.Combine(lib, "steamapps", "common", "3dSen", "3dSen.exe"), "");
            File.WriteAllText(Path.Combine(root, "steamapps", "libraryfolders.vdf"),
                "\"libraryfolders\"\n{\n\t\"0\"\n\t{\n\t\t\"path\"\t\t\"" + root.Replace("\\", "\\\\") + "\"\n\t\t\"apps\"\n\t\t{\n\t\t\t\"228980\"\t\t\"1\"\n\t\t}\n\t}\n"
                + "\t\"1\"\n\t{\n\t\t\"path\"\t\t\"" + lib.Replace("\\", "\\\\") + "\"\n\t\t\"apps\"\n\t\t{\n\t\t\t\"1147940\"\t\t\"123\"\n\t\t}\n\t}\n}\n");
            File.WriteAllText(Path.Combine(lib, "steamapps", "appmanifest_1147940.acf"),
                "\"AppState\"\n{\n\t\"appid\"\t\t\"1147940\"\n\t\"name\"\t\t\"3dSen PC\"\n\t\"StateFlags\"\t\t\"4\"\n\t\"installdir\"\t\t\"3dSen\"\n}\n");
            SetStatic(asm, "ThreeDSenSteam", "RootOverride", root);
            var s = Call(asm, "ThreeDSenSteam", "Read");
            var want = Path.Combine(lib, "steamapps", "common", "3dSen", "3dSen.exe");
            Check("the second library's manifest gives the exe", string.Equals((string)F(s, "Exe"), want, StringComparison.OrdinalIgnoreCase), (string)F(s, "Exe"));
            Check("... seen as a Steam path", (bool)Call(asm, "ThreeDSenSteam", "IsSteamPath", want));
            File.WriteAllText(Path.Combine(lib, "steamapps", "appmanifest_1147940.acf"), "\"AppState\"\n{\n\t\"StateFlags\"\t\t\"1026\"\n\t\"installdir\"\t\t\"3dSen\"\n}\n");
            s = Call(asm, "ThreeDSenSteam", "Read");
            Check("not fully installed (StateFlags without 4): no exe, the folder known", F(s, "Exe") == null && F(s, "InstallDir") != null, (string)F(s, "StateFlags"));
            SetStatic(asm, "ThreeDSenSteam", "RootOverride", Path.Combine(temp, "NoSteam"));
            s = Call(asm, "ThreeDSenSteam", "Read");
            Check("no Steam: said so", F(s, "SteamRoot") == null);
            SetStatic(asm, "ThreeDSenSteam", "RootOverride", null);
        }

        /// <summary>The response's bool property whose name holds <paramref name="part"/> (the SDK's names, read by reflection).</summary>
        private static bool Flag(object r, string part)
            => (bool)r.GetType().GetProperties().First(p => p.PropertyType == typeof(bool) && p.Name.Contains(part)).GetValue(r);

        private static void Plugin(Assembly asm)
        {
            Console.WriteLine("-- platforms, update rule --");
            var type = T(asm, "ThreeDSenPlugin");
            var plugin = Activator.CreateInstance(type);
            bool Supported(string p) { var r = type.GetMethod("IsPlatformSupported").Invoke(plugin, new object[] { p }); return Flag(r, "Support"); }
            bool Recommended(string p) { var r = type.GetMethod("IsPlatformSupported").Invoke(plugin, new object[] { p }); return Flag(r, "Recommend"); }
            Check("Nintendo Entertainment System supported, never recommended", Supported("Nintendo Entertainment System") && !Recommended("Nintendo Entertainment System"));
            Check("Nes - Super Mario Bros. Hacks supported", Supported(" nes - super mario bros. hacks "));
            Check("Nintendo Famicom Disk System NOT supported (no FDS in 3dSen)", !Supported("Nintendo Famicom Disk System"));
            var up = type.GetMethods().First(m => m.Name == "IsUpdateAvailable" && m.DeclaringType == type);
            var a = new object[] { "", null };
            Check("Download button: true for the empty path", (bool)up.Invoke(plugin, a) && a[1] != null);
            a = new object[] { @"C:\Games\3dSen\3dSen.exe", null };
            Check("an install: never an update", !(bool)up.Invoke(plugin, a));
            var rows = ((System.Collections.IEnumerable)type.GetMethod("EmulatorRows").Invoke(plugin, null)).Cast<object>().ToList();
            var row = rows.Single();
            var platforms = ((System.Collections.IEnumerable)F(row, "Platforms")).Cast<object>().Select(p => (string)F(p, "Platform")).ToList();
            Check("catalogue row Nixx-3DSen, its two platforms", (string)F(row, "Name") == "Nixx-3DSen" && platforms.SequenceEqual(new[] { "Nintendo Entertainment System", "Nes - Super Mario Bros. Hacks" }), string.Join(", ", platforms));
            var plugins = asm.GetTypes().Where(t => t.IsPublic && typeof(Unbroken.LaunchBox.Plugins.EmulatorPlugin).IsAssignableFrom(t)).ToList();
            Check("one public EmulatorPlugin, deriving it directly", plugins.Count == 1 && plugins[0].BaseType == typeof(Unbroken.LaunchBox.Plugins.EmulatorPlugin), string.Join(", ", plugins.Select(p => p.FullName)));
        }

        private static void Launch(Assembly asm, string exe, string after, string lab, string temp)
        {
            Console.WriteLine("-- a launch, into a copy of the user data --");
            var data = Path.Combine(temp, "data");
            Directory.CreateDirectory(data);
            foreach (var f in new[] { "rom.json", "input.cfg" }) File.Copy(Path.Combine(after, f), Path.Combine(data, f));
            SetStatic(asm, "ThreeDSenLaunch", "DataDirOverride", data);
            SetStatic(asm, "ThreeDSenSettings", "PathOverride", Path.Combine(temp, "settings", "settings.ini"));
            var rom = Path.Combine(lab, "roms", "Zelda II - The Adventure of Link (Europe).nes");
            var prepare = T(asm, "ThreeDSenPlugin").GetMethod("Prepare", Any);
            var a = new object[] { exe, "probe-game", "Zelda II: The Adventure of Link", rom, "\"" + rom + "\"", null, null };
            var refusal = (string)prepare.Invoke(null, a);
            if (refusal != null && refusal.Contains("already running")) { Console.WriteLine("  (3dSen is running here - the launch checks are skipped)"); return; }
            Check("Zelda II prepared", refusal == null, refusal);
            Check("the line: profile 73 and the screen, no game path", ((string)a[5] ?? "").StartsWith("-id=73 -screen-fullscreen 1 ") && !((string)a[5]).Contains(".nes"), (string)a[5]);
            Check("rom.json: profile 73 plays the game", (string)Call(asm, "ThreeDSenRomJson", "RomOf", Path.Combine(data, "rom.json"), "73") == rom);
            a = new object[] { exe, "probe-unknown", "Some Unknown Game", rom.Replace("Zelda II - The Adventure of Link (Europe)", "Unknown"), "", null, null };
            File.Copy(rom, (string)a[3], true);
            try
            {
                refusal = (string)prepare.Invoke(null, a);
                Check("a game with no profile: launched in 2D Mode (Mehdi 19/10)", refusal == null && ((string)a[5] ?? "").StartsWith("-id=47 "), refusal ?? (string)a[5]);
                Call(asm, "ThreeDSenSettings", "SetGameValue", "probe-unknown", "Profile", "47");
                Call(asm, "ThreeDSenSettings", "SetGameValue", "probe-unknown", "Screen", "window");
                refusal = (string)prepare.Invoke(null, a);
                Check("... with 2D Mode chosen in its window: launched windowed", refusal == null && ((string)a[5] ?? "").StartsWith("-id=47 -screen-fullscreen 0 -screen-width 1280 -screen-height 720"), refusal ?? (string)a[5]);
            }
            finally { try { File.Delete((string)a[3]); } catch { } }

            Console.WriteLine("-- the portable mode (the in-process plugin taken as there) --");
            var fake = Path.Combine(temp, "emu-portable", "3dSen.exe");
            SetStatic(asm, "ThreeDSenLaunch", "PortableOverride", true);
            try
            {
                var pdata = (string)Call(asm, "ThreeDSenLaunch", "DataDir", fake);
                var proot = (string)Call(asm, "ThreeDSenLaunch", "StatesRoot", fake, "Nintendo Entertainment System");
                Check("data: <install>\\portable", pdata == Path.Combine(temp, "emu-portable", "portable"), pdata);
                Check("states: <install>\\Saves\\<platform>\\States", proot == Path.Combine(temp, "emu-portable", "Saves", "Nintendo Entertainment System", "States"), proot);
                Check("a platform's name made a folder name", (string)Call(asm, "ThreeDSenLaunch", "PlatformFolder", "NES: Hacks?") == "NES_ Hacks_");
                Check("the slots of a States folder are in it, sav.cfg beside them", (string)Call(asm, "ThreeDSenSavCfg", "StatePath", proot, "5") == Path.Combine(proot, "5.sav")
                      && (string)Call(asm, "ThreeDSenSavCfg", "RootOf", Path.Combine(proot, "5.sav")) == proot);
                Call(asm, "ThreeDSenLaunch", "FirstPortable", fake);
                Check("first portable launch: LocalLow's games copied in", File.Exists(Path.Combine(pdata, "rom.json")) && File.Exists(Path.Combine(data, "rom.json")));
                Call(asm, "ThreeDSenSettings", "SetGameValue", "probe-game", "Volume", "0.3");
                a = new object[] { fake, "probe-game", "Zelda II: The Adventure of Link", rom, "", null, "Nintendo Entertainment System" };
                refusal = (string)prepare.Invoke(null, a);
                var pl = (string)a[5] ?? "";
                Check("portable launch prepared", refusal == null, refusal);
                Check("the line asks the plugin: data, states, Esc, slots", pl.Contains("--nixx-data=") && pl.Contains("--nixx-states=") && pl.Contains("--nixx-esc")
                      && pl.Contains("--nixx-slots=on") && pl.Contains("--nixx-quit-confirm=on"), pl);
                Check("the game's own 3dSen setting passed, nothing it did not choose", pl.Contains("--nixx-set:Volume=0.3") && !pl.Contains("--nixx-set:Fov"), pl);
                Check("rom.json written in the portable folder", (string)Call(asm, "ThreeDSenRomJson", "RomOf", Path.Combine(pdata, "rom.json"), "73") == rom);
                Check("... a game's line never carries a --nixx- token of an old line", ((string)Call(asm, "ThreeDSenLaunch", "CommandLine", "--nixx-esc \"" + rom + "\"", rom, new List<string>())).IndexOf("--nixx", StringComparison.Ordinal) < 0);
            }
            finally { SetStatic(asm, "ThreeDSenLaunch", "PortableOverride", null); }
        }

        /// <summary>A launch's preparation FOR REAL - into 3dSen's own user data folder (rom.json, input.cfg) - and the line
        /// printed, for an oracle run of 3dSen with it. Back the data folder up first.</summary>
        public static bool PrepareReal(Assembly asm, string exe, string rom, string title)
        {
            Console.WriteLine("-- a launch prepared into " + Call(asm, "ThreeDSenLaunch", "DataDir") + " --");
            var a = new object[] { exe, "probe-real", title, rom, "\"" + rom + "\"", null, null };
            var refusal = (string)T(asm, "ThreeDSenPlugin").GetMethod("Prepare", Any).Invoke(null, a);
            Console.WriteLine(refusal == null ? "LINE " + a[5] : "REFUSED " + refusal);
            return refusal == null;
        }

        /// <summary>This machine's Steam, read only.</summary>
        public static bool RealSteam(Assembly asm)
        {
            Console.WriteLine("-- Steam on this machine --");
            var s = Call(asm, "ThreeDSenSteam", "Read");
            foreach (var l in (string[])Call(asm, "ThreeDSenInstall", "Describe", s)) Console.WriteLine("  " + l);
            Console.WriteLine("  3dSen's data folder: " + Call(asm, "ThreeDSenLaunch", "DataDir"));
            Console.WriteLine("  Unity remembers: " + (Call(asm, "ThreeDSenLaunch", "Remembered") ?? "(nothing)"));
            var r = Call(asm, "ThreeDSenLaunch", "PrimaryResolution");
            Console.WriteLine("  primary monitor: " + (r?.ToString() ?? "(unknown)"));
            return true;
        }
    }
}
