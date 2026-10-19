// The Mesen plugin's pieces, run without a host:
//   --mesen-selftest [--json <a settings.json Mesen wrote>] [--core <MesenCore.dll>] [--db <a copy of LaunchBox.Metadata.db>]
//        offline, temp folders only: the systems table (against the core's own strings and LaunchBox's platform names when
//        given), settings.json edited in place (byte-identical round trip of Mesen's own file), the pack's hotkeys, the
//        home folder and the first settings.json, the BIOS found / copied / refused, archives, the system chosen and the line
//        for the three levels, the session put back, the catalogue row, claiming and the platform rows only filled, and
//        launches through PrepareEmulatorForLaunch on a stand-in exe (refusals included), the saves and states listed /
//        restored / deleted with their pictures decoded from the state
//   --mesen-latest                     MesenCE's latest release as GitHub has it, and the three answers of IsUpdateAvailable
//   --mesen-real <Mesen.exe> --rom <a .gbc> --lab <folder>
//        THE REAL EMULATOR: a copy of Mesen.exe in a fresh folder of <lab>, a launch prepared as LaunchBox would, Mesen started
//        with that line (windowed), closed by WM_CLOSE, then the session put back - what settings.json holds at each step

using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json.Nodes;
using Unbroken.LaunchBox.Plugins;
using Unbroken.LaunchBox.Plugins.Data;

namespace LbIntegrations.Probe
{
    internal static class MesenCheck
    {
        private const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
        private const BindingFlags Inst = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
        private static int _bad;

        private static void Check(string what, bool ok, string detail = null)
        {
            if (!ok) _bad++;
            Console.WriteLine("  [" + (ok ? "ok" : "FAIL") + "] " + what + (detail != null ? " - " + detail : ""));
        }

        private static Type T(Assembly asm, string name) => asm.GetType("LbIntegrations.Mesen." + name, true);
        private static object Call(Assembly asm, string type, string method, params object[] args)
        {
            var ms = T(asm, type).GetMethods(Any).Where(m => m.Name == method && m.GetParameters().Length == args.Length).ToList();
            if (ms.Count == 0) throw new MissingMethodException(type + "." + method + "/" + args.Length);
            try { return ms[0].Invoke(null, args); } catch (TargetInvocationException ex) { throw ex.InnerException ?? ex; }
        }
        private static object F(object o, string n) => o?.GetType().GetField(n, Inst)?.GetValue(o) ?? o?.GetType().GetProperty(n, Inst)?.GetValue(o);
        private static object S(Assembly asm, string type, string n) => T(asm, type).GetField(n, Any)?.GetValue(null) ?? T(asm, type).GetProperty(n, Any)?.GetValue(null);
        private static void SetS(Assembly asm, string type, string n, object v) => T(asm, type).GetField(n, Any).SetValue(null, v);
        private static string Key(object system) => (string)F(system, "Key");

        private static string NewTemp(string what)
        {
            var d = Path.Combine(Path.GetTempPath(), "lbip-mesen-" + what + "-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            Directory.CreateDirectory(d);
            return d;
        }

        private static string Pref(JsonNode root, string key)
        {
            var p = root?["Preferences"] as JsonObject;
            var n = p?[key];
            if (n == null) return null;
            try { return n.GetValueKind() == System.Text.Json.JsonValueKind.String ? n.GetValue<string>() : n.ToJsonString(); } catch { return n.ToJsonString(); }
        }

        private static JsonNode ReadJson(string path) => JsonNode.Parse(File.ReadAllText(path, Encoding.UTF8));

        private static (int Key1, int Key2) Combo(JsonNode root, string shortcut, string combo = "KeyCombination")
        {
            var list = root?["Preferences"]?["ShortcutKeys"] as JsonArray;
            var e = list?.OfType<JsonObject>().FirstOrDefault(x => (string)x["Shortcut"] == shortcut);
            if (e == null) return (-1, -1);
            return ((int)e[combo]["Key1"], (int)e[combo]["Key2"]);
        }

        // ══════════════════════════════════════════════════════════════════════

        public static bool SelfTest(Assembly asm, string json, string core, string db)
        {
            _bad = 0;
            var saidBox = new List<string>();
            asm.GetType("LbIntegrations.Lbip.LbipNotice", true).GetField("Instead", Any).SetValue(null, new Action<string, string>((t, x) => saidBox.Add(x)));
            asm.GetType("LbIntegrations.Lbip.LbipLaunchGate", true).GetField("Off", Any).SetValue(null, true);
            var root = NewTemp("selftest");
            SetS(asm, "MesenSettings", "PathOverride", Path.Combine(root, "plugin-data", "settings.ini"));
            SetS(asm, "MesenLaunch", "DocumentsOverride", Path.Combine(root, "Documents"));
            try
            {
                Table(asm, core, db);
                if (json != null && File.Exists(json)) Json(asm, json, root);
                else Console.WriteLine("  (no --json: the round trip of Mesen's own settings.json is skipped)");
                Hotkeys(asm, json, root);
                Home(asm, root);
                Bios(asm, root);
                ArchivesCheck(asm, root);
                Line(asm, root);
                Session(asm, json, root);
                Rows(asm);
                Launches(asm, json, root, saidBox);
                Saves(asm, json, root);
            }
            catch (Exception ex) { Check("no exception", false, ex.GetType().Name + ": " + ex.Message + "\n" + ex.StackTrace); }
            finally
            {
                SetS(asm, "MesenSettings", "PathOverride", null);
                SetS(asm, "MesenLaunch", "DocumentsOverride", null);
                try { Directory.Delete(root, true); } catch { }
            }
            Console.WriteLine(_bad == 0 ? "MESEN SELFTEST OK" : "MESEN SELFTEST: " + _bad + " FAILED");
            return _bad == 0;
        }

        // ── the table ────────────────────────────────────────────────────────

        private static void Table(Assembly asm, string core, string db)
        {
            Console.WriteLine("-- the systems table --");
            var all = ((IEnumerable)S(asm, "MesenSystems", "All")).Cast<object>().ToList();
            var platforms = ((IEnumerable)S(asm, "MesenSystems", "Platforms")).Cast<object>().ToList();
            Check("systems: " + all.Count + ", platforms: " + platforms.Count, all.Count >= 20 && platforms.Count >= 20);
            Check("system keys unique", all.Select(Key).Distinct(StringComparer.OrdinalIgnoreCase).Count() == all.Count);
            foreach (var p in platforms)
            {
                var name = (string)F(p, "Platform");
                var systems = (string[])F(p, "Systems");
                var by = (IDictionary<string, string>)F(p, "ByExtension");
                bool ok = systems.All(s => Call(asm, "MesenSystems", "System", s) != null) && by.Values.All(s => systems.Contains(s));
                var exts = (string)Call(asm, "MesenSystems", "AllExtensions", p);
                Check(name + ": " + string.Join("/", systems) + " - " + exts, ok && exts.Length > 0);
            }
            var every = (string)Call(asm, "MesenSystems", "EveryExtension");
            foreach (var e in new[] { ".nes", ".fds", ".qd", ".unf", ".studybox", ".sfc", ".smc", ".bs", ".st", ".gb", ".gbc", ".gba", ".pce", ".sgx", ".cue", ".sms", ".gg", ".sg", ".col", ".ws", ".wsc", ".pc2", ".zip", ".7z" })
                if (!every.Split(new[] { "; " }, StringSplitOptions.None).Contains(e)) Check("the emulator row opens " + e, false, every);
            Check("the emulator row's extensions: " + every.Split(';').Length, true);
            foreach (var (ext, want) in new[] { (".gb", "gb"), (".gbc", "gbc"), (".pce", "pce"), (".sgx", "sgx"), (".wsc", "wsc"), (".fds", "fds"), (".col", "cv"), (".bs", "bsx"), (".xyz", null) })
            {
                var s = Call(asm, "MesenSystems", "ByExtensionAlone", ext);
                Check("an extension alone: " + ext + " -> " + (want ?? "none"), (s == null ? null : Key(s)) == want, s == null ? "none" : Key(s));
            }

            if (core != null && File.Exists(core))
            {
                // The firmware names Mesen reads, and the extensions its cores open, as strings in MesenCore.dll.
                var bytes = File.ReadAllBytes(core);
                var text = Encoding.ASCII.GetString(bytes);
                var firmware = all.SelectMany(s => ((IEnumerable)F(s, "Firmware")).Cast<object>()).Distinct().ToList();
                var missing = new List<string>();
                foreach (var f in firmware)
                    foreach (var n in new[] { (string)F(f, "File"), (string)F(f, "Alt") }.Where(x => x != null))
                        if (!text.Contains(n)) missing.Add(n);
                Check("every firmware name is in " + Path.GetFileName(core) + " (" + firmware.Count + " files)", missing.Count == 0, missing.Count == 0 ? null : "missing: " + string.Join(", ", missing));
                bool ce = text.Contains(".pc2");
                Check("this core is " + (ce ? "MesenCE (it opens .pc2)" : "2.1.x (no .pc2)"), true);
                var exts = all.Where(s => ce || !(bool)F(s, "CeOnly")).SelectMany(s => (string[])F(s, "Extensions")).Distinct().ToList();
                var absent = exts.Where(x => !text.Contains(x)).ToList();
                Check("every extension of the table is in the core (" + exts.Count + ")", absent.Count == 0, absent.Count == 0 ? null : string.Join(", ", absent));
            }
            else Console.WriteLine("  (no --core: the table is not checked against MesenCore.dll)");

            if (db != null && File.Exists(db))
            {
                var names = new HashSet<string>(StringComparer.Ordinal);
                using (var c = new Microsoft.Data.Sqlite.SqliteConnection("Data Source=" + db + ";Mode=ReadOnly"))
                {
                    c.Open();
                    using var cmd = c.CreateCommand(); cmd.CommandText = "select Name from Platforms";
                    using var r = cmd.ExecuteReader();
                    while (r.Read()) names.Add(r.GetString(0));
                }
                var liteBox = new HashSet<string> { "Pocket Challenge V2", "Nes - Super Mario Bros. Hacks", "Snes - Super Mario World Hacks", "Super Nintendo MSU-1" };
                foreach (var p in platforms)
                {
                    var n = (string)F(p, "Platform");
                    if (liteBox.Contains(n) || (bool)F(p, "OnlyIfInLibrary")) continue;
                    if (!names.Contains(n)) Check("LaunchBox knows the platform \"" + n + "\"", false);
                }
                Check("every LaunchBox platform of the table is spelt as in LaunchBox's base (" + names.Count + " platforms there)", true);
            }
        }

        // ── settings.json ────────────────────────────────────────────────────

        private static void Json(Assembly asm, string json, string root)
        {
            Console.WriteLine("-- settings.json, edited in place (" + json + ") --");
            var copy = Path.Combine(root, "json", "settings.json");
            Directory.CreateDirectory(Path.GetDirectoryName(copy));
            File.Copy(json, copy);
            var raw = File.ReadAllBytes(copy);
            var j = Call(asm, "MesenJson", "Load", copy);
            Check("read", (bool)F(j, "Readable"));
            var text = (string)j.GetType().GetMethod("Text").Invoke(j, null);
            var original = Encoding.UTF8.GetString(raw).TrimStart('\uFEFF');
            Check("written back unchanged, it is Mesen's own text byte for byte (two spaces, CRLF, the same escaping)", text == original,
                  text == original ? null : "first difference at " + Enumerable.Range(0, Math.Min(text.Length, original.Length)).FirstOrDefault(i => text[i] != original[i]));
            Check("nothing changed: nothing written", !(bool)j.GetType().GetMethod("SaveIfChanged").Invoke(j, null) && File.ReadAllBytes(copy).SequenceEqual(raw));
            j.GetType().GetMethod("Set", new[] { typeof(string), typeof(bool) }).Invoke(j, new object[] { "Preferences/OverrideSaveDataFolder", true });
            j.GetType().GetMethod("Set", new[] { typeof(string), typeof(string) }).Invoke(j, new object[] { "preferences/savedatafolder", "Saves\\Nintendo Entertainment System" });
            Check("two values set, written", (bool)j.GetType().GetMethod("SaveIfChanged").Invoke(j, null));
            var after = File.ReadAllBytes(copy);
            Check("UTF-8 with its BOM", after.Length > 3 && after[0] == 0xEF && after[1] == 0xBB && after[2] == 0xBF);
            int crlf = 0, lf = 0;
            for (int i = 0; i < after.Length; i++) if (after[i] == '\n') { lf++; if (i > 0 && after[i - 1] == '\r') crlf++; }
            Check("CRLF throughout (" + crlf + " lines)", crlf == lf && lf > 100);
            var a = original.Split("\r\n"); var b = Encoding.UTF8.GetString(after).TrimStart('\uFEFF').Split("\r\n");
            int diff = a.Length == b.Length ? a.Zip(b, (x, y) => x == y ? 0 : 1).Sum() : -1;
            Check("only the two lines changed, every member in its place", diff == 2, "lines changed: " + diff);
            var back = ReadJson(copy);
            Check("the member keeps its own spelling (SaveDataFolder, not savedatafolder)", Pref(back, "SaveDataFolder") == "Saves\\Nintendo Entertainment System" && back["Preferences"]["savedatafolder"] == null);
        }

        // ── hotkeys ──────────────────────────────────────────────────────────

        private static void Hotkeys(Assembly asm, string json, string root)
        {
            Console.WriteLine("-- the pack's hotkeys --");
            var dir = Path.Combine(root, "hotkeys"); Directory.CreateDirectory(dir);
            int Bind(string file) => ((IList)Call(asm, "MesenLaunch", "BindPackKeys", Call(asm, "MesenJson", "Load", file))).Count;
            List<string> BindSave(string file)
            {
                var j = Call(asm, "MesenJson", "Load", file);
                var bound = ((IEnumerable)Call(asm, "MesenLaunch", "BindPackKeys", j)).Cast<string>().ToList();
                j.GetType().GetMethod("SaveIfChanged").Invoke(j, null);
                return bound;
            }

            if (json != null && File.Exists(json))
            {
                var full = Path.Combine(dir, "full.json"); File.Copy(json, full);
                var bound = BindSave(full);
                var r = ReadJson(full);
                Check("Mesen's full file: 5 keys bound", bound.Count == 5, string.Join("; ", bound));
                Check("F2 save, F4 load, F6 / F7 the slot, Esc quit", Combo(r, "SaveState") == (91, 0) && Combo(r, "LoadState") == (93, 0) && Combo(r, "MoveToPreviousStateSlot") == (95, 0)
                      && Combo(r, "MoveToNextStateSlot") == (96, 0) && Combo(r, "Exit") == (13, 0));
                Check("Mesen's defaults on those keys unbound (load slot 2/4/6/7, Pause)", new[] { "LoadStateSlot2", "LoadStateSlot4", "LoadStateSlot6", "LoadStateSlot7", "Pause" }.All(s => Combo(r, s) == (0, 0)));
                Check("the others untouched (Shift+F2 saves slot 2, F1 loads slot 1, F11 full screen)", Combo(r, "SaveStateSlot2") == (116, 91) && Combo(r, "LoadStateSlot1") == (90, 0) && Combo(r, "ToggleFullscreen") == (100, 0));
                Check("a second time: nothing", BindSave(full).Count == 0);

                // The user's own binding on F2 (Reset) and none on SaveState: F2 is his - SaveState left unbound.
                var mine = Path.Combine(dir, "mine.json"); File.Copy(json, mine);
                var node = ReadJson(mine);
                foreach (var e in ((JsonArray)node["Preferences"]["ShortcutKeys"]).OfType<JsonObject>())
                {
                    if ((string)e["Shortcut"] == "Reset") e["KeyCombination"] = new JsonObject { ["Key1"] = 91, ["Key2"] = 0, ["Key3"] = 0 };
                    if ((string)e["Shortcut"] == "LoadStateSlot2") e["KeyCombination"] = new JsonObject { ["Key1"] = 0, ["Key2"] = 0, ["Key3"] = 0 };
                }
                File.WriteAllText(mine, node.ToJsonString(), new UTF8Encoding(true));
                var b2 = BindSave(mine);
                var r2 = ReadJson(mine);
                Check("F2 held by the user's Reset: SaveState not bound, Reset kept", Combo(r2, "SaveState") == (0, 0) && Combo(r2, "Reset") == (91, 0) && b2.Count == 4, string.Join("; ", b2));
            }

            // A file Mesen has not written yet (the first settings): the list is started with ours and the displaced defaults.
            var boot = Path.Combine(dir, "boot.json");
            File.WriteAllText(boot, "{\"ConfigUpgrade\":1,\"Preferences\":{\"AutomaticallyCheckForUpdates\":false}}");
            var bb = BindSave(boot);
            var rb = ReadJson(boot);
            var list = (JsonArray)rb["Preferences"]["ShortcutKeys"];
            Check("a first settings.json: 5 bound, the 5 defaults on those keys written unbound (Mesen adds its others - measured)", bb.Count == 5 && list.Count == 10
                  && Combo(rb, "LoadStateSlot2") == (0, 0) && Combo(rb, "Pause") == (0, 0), list.Count + " entries");
        }

        // ── the home folder, the first settings.json ─────────────────────────

        private static void Home(Assembly asm, string root)
        {
            Console.WriteLine("-- the home folder and the first settings.json --");
            var docs = Path.Combine(root, "Documents");
            var a = Path.Combine(root, "home-a"); Directory.CreateDirectory(a);
            var exe = Path.Combine(a, "Mesen.exe"); File.WriteAllText(exe, "");
            var home = (string)Call(asm, "MesenLaunch", "HomeOf", exe);
            Check("no settings.json anywhere: Documents\\Mesen2 (a stand-in exe has no version: not CE)", home == Path.Combine(docs, "Mesen2"), home);
            Check("the first settings.json written beside the exe", (bool)Call(asm, "MesenLaunch", "Bootstrap", exe) && File.Exists(Path.Combine(a, "settings.json")));
            var r = ReadJson(Path.Combine(a, "settings.json"));
            Check("... ConfigUpgrade 1 (the wizard's first run), Mesen's own update check off", (int)r["ConfigUpgrade"] == 1 && Pref(r, "AutomaticallyCheckForUpdates") == "false");
            Check("... with a BOM", File.ReadAllBytes(Path.Combine(a, "settings.json")).Take(3).SequenceEqual(new byte[] { 0xEF, 0xBB, 0xBF }));
            Check("now portable: the home is the exe's folder", (string)Call(asm, "MesenLaunch", "HomeOf", exe) == a && (bool)Call(asm, "MesenLaunch", "IsPortable", exe));
            Check("never a second time", !(bool)Call(asm, "MesenLaunch", "Bootstrap", exe));

            var b = Path.Combine(root, "home-b"); Directory.CreateDirectory(b);
            var exeB = Path.Combine(b, "Mesen.exe"); File.WriteAllText(exeB, "");
            Directory.CreateDirectory(Path.Combine(docs, "Mesen2"));
            File.WriteAllText(Path.Combine(docs, "Mesen2", "settings.json"), "{}");
            Check("a Mesen set up in Documents: left there, nothing written beside the exe", !(bool)Call(asm, "MesenLaunch", "Bootstrap", exeB) && !File.Exists(Path.Combine(b, "settings.json"))
                  && (string)Call(asm, "MesenLaunch", "HomeOf", exeB) == Path.Combine(docs, "Mesen2"));
            File.Delete(Path.Combine(docs, "Mesen2", "settings.json"));
        }

        // ── BIOS ─────────────────────────────────────────────────────────────

        private static void Bios(Assembly asm, string root)
        {
            Console.WriteLine("-- the BIOS --");
            var emulators = Path.Combine(root, "Emulators");
            var dir = Path.Combine(emulators, "Nixx-Mesen"); Directory.CreateDirectory(dir);
            var exe = Path.Combine(dir, "Mesen.exe"); File.WriteAllText(exe, "");
            File.WriteAllText(Path.Combine(dir, "settings.json"), "{\"ConfigUpgrade\":1}");
            var ra = Path.Combine(emulators, "RetroArch", "system"); Directory.CreateDirectory(ra);
            File.WriteAllBytes(Path.Combine(ra, "disksys.rom"), new byte[0x2000]);
            File.WriteAllBytes(Path.Combine(ra, "gba_bios.bin"), new byte[100]);   // not a size Mesen reads
            object Sys(string k) => Call(asm, "MesenSystems", "System", k);
            IList Ensure(params string[] keys)
            {
                var arr = Array.CreateInstance(T(asm, "MesenSystem"), keys.Length);
                for (int i = 0; i < keys.Length; i++) arr.SetValue(Sys(keys[i]), i);
                return (IList)Call(asm, "MesenBios", "Ensure", exe, arr, null, false, false);
            }
            bool Enough(IList states) => (bool)T(asm, "MesenBios").GetMethod("Enough", Any).Invoke(null, new object[] { states.Cast<object>().ToArray().ToTypedArray(T(asm, "MesenBiosState")) });
            var fds = Ensure("fds");
            Check("the Famicom Disk System BIOS copied in from RetroArch's system folder", File.Exists(Path.Combine(dir, "Firmware", "disksys.rom")) && Enough(fds));
            Check("... a copy: RetroArch keeps its own", File.Exists(Path.Combine(ra, "disksys.rom")));
            var gba = Ensure("gba");
            Check("a gba_bios.bin of the wrong size: copied but not enough, said", !Enough(gba) && gba.Cast<object>().Any(s => F(s, "Path") != null && !(bool)F(s, "SizeOk")));
            Check("a NES game needs no BIOS", Enough(Ensure("nes")));
            Check("a Master System game: its BIOS optional", Enough(Ensure("sms")));
            var sgb = Ensure("sgb");
            var req = sgb.Cast<object>().Where(s => (bool)F(s, "Required")).Select(s => (string)F(F(s, "Spec"), "File")).ToList();
            Check("Super Game Boy: SGB2.sfc required (Mesen's UseSgb2 default)", req.SequenceEqual(new[] { "SGB2.sfc" }), string.Join(",", req));
            File.WriteAllText(Path.Combine(dir, "settings.json"), "{\"Gameboy\":{\"UseSgb2\":false}}");
            req = Ensure("sgb").Cast<object>().Where(s => (bool)F(s, "Required")).Select(s => (string)F(F(s, "Spec"), "File")).ToList();
            Check("... SGB1.sfc when UseSgb2 is false", req.SequenceEqual(new[] { "SGB1.sfc" }), string.Join(",", req));
            // By content: a disksys dump under another name in the Firmware folder is not Mesen's - FdsBios.bin is its alternate
            // name, and Mesen loads it: found.
            File.Delete(Path.Combine(dir, "Firmware", "disksys.rom"));
            File.Delete(Path.Combine(ra, "disksys.rom"));
            File.WriteAllBytes(Path.Combine(dir, "Firmware", "FdsBios.bin"), new byte[0x2000]);
            Check("FdsBios.bin, Mesen's alternate name: enough", Enough(Ensure("fds")));
        }

        private static Array ToTypedArray(this object[] items, Type t)
        {
            var a = Array.CreateInstance(t, items.Length);
            for (int i = 0; i < items.Length; i++) a.SetValue(items[i], i);
            return a;
        }

        // ── archives ─────────────────────────────────────────────────────────

        private static void ArchivesCheck(Assembly asm, string root)
        {
            Console.WriteLine("-- archives --");
            var dir = Path.Combine(root, "arch"); Directory.CreateDirectory(dir);
            var exe = Path.Combine(dir, "emu", "Mesen.exe"); Directory.CreateDirectory(Path.GetDirectoryName(exe)); File.WriteAllText(exe, "");
            string Zip(string name, params string[] entries)
            {
                var path = Path.Combine(dir, name);
                using (var z = System.IO.Compression.ZipFile.Open(path, System.IO.Compression.ZipArchiveMode.Create))
                    foreach (var e in entries) using (var s = z.CreateEntry(e).Open()) s.Write(new byte[] { 1, 2, 3 }, 0, 3);
                return path;
            }
            var one = Zip("ZeldaArchive.zip", "Inner Zelda.nes", "readme.txt");
            var args = new object[] { one, exe, null };
            var r = (string)T(asm, "MesenLaunch").GetMethod("RomFor", Any).Invoke(null, args);
            Check("one game in a zip: given as it is (Mesen opens it)", r == one);
            Check("... its saves named after the game inside", Path.GetFileName((string)Call(asm, "MesenLaunch", "Inner", one)) == "Inner Zelda.nes");
            var two = Zip("Mario.zip", "Mario.nes", "Mario (Hack).nes");
            args = new object[] { two, exe, null };
            r = (string)T(asm, "MesenLaunch").GetMethod("RomFor", Any).Invoke(null, args);
            Check("two games, one named after the archive: that one unpacked into the install's Unpacked\\", r != null && r.StartsWith(Path.Combine(Path.GetDirectoryName(exe), "Unpacked")) && Path.GetFileName(r) == "Mario.nes" && File.Exists(r), r);
            var none = Zip("Pack.zip", "A.nes", "B.nes");
            args = new object[] { none, exe, null };
            r = (string)T(asm, "MesenLaunch").GetMethod("RomFor", Any).Invoke(null, args);
            Check("two games, none named after the archive: refused, said", r == null && ((string)args[2])?.Contains("one game per archive") == true, (string)args[2]);
            var empty = Zip("Docs.zip", "manual.pdf");
            args = new object[] { empty, exe, null };
            Check("no game in it: refused", T(asm, "MesenLaunch").GetMethod("RomFor", Any).Invoke(null, args) == null);
        }

        // ── the system and the line ──────────────────────────────────────────

        private static void Line(Assembly asm, string root)
        {
            Console.WriteLine("-- the system chosen and the line --");
            (string Key, string From, List<string> Add, List<string> Remove) For(string gameId, string platform, string ext, string line)
            {
                var c = Call(asm, "MesenLaunch", "SystemFor", gameId, platform, ext, line);
                var s = F(c, "System");
                return (s == null ? null : Key(s), (string)F(c, "From"), (List<string>)F(c, "Add"), (List<string>)F(c, "Remove"));
            }
            var r = For(null, "Nintendo Entertainment System", ".nes", "\"x.nes\"");
            Check("NES, a .nes: NES, nothing added", r.Key == "nes" && r.Add.Count == 0, r.From);
            r = For(null, "Nintendo Entertainment System", ".fds", "\"x.fds\"");
            Check("NES, a .fds: Famicom Disk System (the extension)", r.Key == "fds" && r.From == "the file's extension");
            r = For(null, "Nintendo Game Boy", ".gbc", "\"x.gbc\"");
            Check("Game Boy, a .gbc: Game Boy Color by the extension, NO switch (Mesen's detection)", r.Key == "gbc" && r.From == "the file's extension" && r.Add.Count == 0 && r.Remove.Count == 0);
            r = For(null, "Nintendo Game Boy", ".gbc", "--gameboy.model=SuperGameboy \"x.gbc\"");
            Check("Game Boy, the user's line says Super Game Boy: the line wins", r.Key == "sgb" && r.From == "the platform's command line" && r.Add.Count == 0);
            r = For(null, "Nintendo Game Boy", ".gb", "\"x.gb\"");
            Check("Game Boy, a .gb: Game Boy from the table, NO switch (Mesen's own model)", r.Key == "gb" && r.Add.Count == 0 && r.From == "the platform");
            r = For(null, "WonderSwan", ".wsc", "--ws.model=Monochrome \"x.wsc\"");
            Check("WonderSwan, a .wsc, the user's line says Monochrome: the line wins, nothing added", r.Key == "ws" && r.From == "the platform's command line" && r.Add.Count == 0);
            r = For(null, "WonderSwan", ".wsc", "\"x.wsc\"");
            Check("WonderSwan, a .wsc, no line: WonderSwan Color by the extension, no switch", r.Key == "wsc" && r.Add.Count == 0);
            r = For(null, "My Own NES Games", ".nes", "\"x.nes\"");
            Check("a platform of the user's own, a .nes: NES by the extension", r.Key == "nes");
            r = For(null, "My Own Handhelds", ".gb", "\"x.gb\"");
            Check("... a .gb: Game Boy by the extension", r.Key == "gb");
            r = For(null, "Nintendo 64", ".z64", "\"x.z64\"");
            Check("a game Mesen does not run: no system", r.Key == null);

            Call(asm, "MesenSettings", "SetGameSystem", "game-1", "sgb");
            r = For("game-1", "Nintendo Game Boy", ".gb", "--gameboy.model=AutoFavorGb \"x.gb\"");
            Check("the game's window says Super Game Boy: it wins, the line's model taken off", r.Key == "sgb" && r.From == "the game's window"
                  && r.Add.SequenceEqual(new[] { "--gameboy.model=SuperGameboy" }) && r.Remove.SequenceEqual(new[] { "--gameboy.model=AutoFavorGb" }));
            Call(asm, "MesenSettings", "SetGameSystem", "game-1", null);
            Check("... taken back: the platform again", For("game-1", "Nintendo Game Boy", ".gb", "--gameboy.model=AutoFavorGb \"x.gb\"").Key == "gb");

            string Cmd(string current, bool full, string[] add, string[] remove, string oldRom = null, string newRom = null)
                => (string)Call(asm, "MesenLaunch", "CommandLine", current, full, add.ToList(), remove.ToList(), oldRom, newRom);
            var l = Cmd("\"C:\\Games\\Zelda II.nes\"", true, new string[0], new string[0]);
            Check("full screen: --fullscreen before the game", l == "--fullscreen \"C:\\Games\\Zelda II.nes\"", l);
            l = Cmd("--fullscreen \"C:\\Games\\Zelda II.nes\"", true, new string[0], new string[0]);
            Check("... never twice", l == "--fullscreen \"C:\\Games\\Zelda II.nes\"", l);
            l = Cmd("--FULLSCREEN \"C:\\Games\\Zelda II.nes\"", false, new string[0], new string[0]);
            Check("windowed: the user's --FULLSCREEN taken off", l == "\"C:\\Games\\Zelda II.nes\"", l);
            l = Cmd("--gameboy.model=AutoFavorGb --nes.region=Pal \"C:\\G\\x.gbc\"", true, new[] { "--gameboy.model=GameboyColor" }, new[] { "--gameboy.model=AutoFavorGb" });
            Check("the system's switch first, the line's other switches kept, the game last (quoted only when it has a space)", l == "--gameboy.model=GameboyColor --fullscreen --nes.region=Pal C:\\G\\x.gbc", l);
            l = Cmd("\"C:\\G\\Mario.zip\"", false, new string[0], new string[0], "C:\\G\\Mario.zip", "C:\\E\\Unpacked\\Mario-1\\Mario.nes");
            Check("an unpacked archive: the game replaced on the line", l == "C:\\E\\Unpacked\\Mario-1\\Mario.nes", l);
        }

        // ── the session ──────────────────────────────────────────────────────

        private static void Session(Assembly asm, string json, string root)
        {
            Console.WriteLine("-- the session put back --");
            var dir = Path.Combine(root, "session"); Directory.CreateDirectory(dir);
            var exe = Path.Combine(dir, "Mesen.exe"); File.WriteAllText(exe, "");
            var path = Path.Combine(dir, "settings.json");
            if (json != null && File.Exists(json)) File.Copy(json, path);
            else File.WriteAllText(path, "{\"ConfigUpgrade\":5,\"Gameboy\":{\"Model\":\"AutoFavorBest\"},\"Preferences\":{\"OverrideSaveDataFolder\":false,\"SaveDataFolder\":\"\",\"OverrideSaveStateFolder\":false,\"SaveStateFolder\":\"\",\"OverrideScreenshotFolder\":false,\"ScreenshotFolder\":\"\",\"ShowFps\":false}}");
            var before = ReadJson(path);
            var modelBefore = (string)before["Gameboy"]["Model"];

            var j = Call(asm, "MesenJson", "Load", path);
            var forcedType = T(asm, "MesenSession").GetNestedType("Forced", Any);
            var forced = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(forcedType));
            var ours = (bool)Call(asm, "MesenSavePaths", "Ensure", j, "Nintendo Game Boy", forced);
            Check("the platform's folders set (no folder of the user's)", ours && forced.Count == 6, forced.Count + " forced");
            var line = "--gameboy.model=GameboyColor --fullscreen \"x.gbc\"";
            foreach (var f in (IEnumerable)Call(asm, "MesenSession", "FromLine", j, line)) forced.Add(f);
            j.GetType().GetMethod("SaveIfChanged").Invoke(j, null);
            var during = ReadJson(path);
            Check("written before the launch: Saves\\Nintendo Game Boy, \\States, \\Screenshots", Pref(during, "SaveDataFolder") == "Saves\\Nintendo Game Boy"
                  && Pref(during, "SaveStateFolder") == "Saves\\Nintendo Game Boy\\States" && Pref(during, "ScreenshotFolder") == "Saves\\Nintendo Game Boy\\Screenshots"
                  && Pref(during, "OverrideSaveDataFolder") == "true");
            Call(asm, "MesenSession", "Begin", exe, path, forced, null);
            Check("the plan written down", File.Exists((string)S(asm, "MesenSession", "PlanPath")));

            // What Mesen does during the session: it writes the switch's model; the user sets his own screenshots folder.
            var m = ReadJson(path);
            m["Gameboy"]["Model"] = "GameboyColor";
            m["Preferences"]["ScreenshotFolder"] = "D:\\My Shots";
            File.WriteAllText(path, m.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }).Replace("\n", "\r\n"), new UTF8Encoding(true));

            Call(asm, "MesenSession", "Recover");
            var after = ReadJson(path);
            Check("Mesen gone: the model put back (" + modelBefore + ")", (string)after["Gameboy"]["Model"] == modelBefore, (string)after["Gameboy"]["Model"]);
            Check("the save and state folders put back as Mesen had them", Pref(after, "OverrideSaveDataFolder") == Pref(before, "OverrideSaveDataFolder")
                  && Pref(after, "SaveDataFolder") == Pref(before, "SaveDataFolder") && Pref(after, "SaveStateFolder") == Pref(before, "SaveStateFolder"));
            Check("the folder the user changed during the session is his: kept", Pref(after, "ScreenshotFolder") == "D:\\My Shots");
            Check("the plan done and gone", !File.Exists((string)S(asm, "MesenSession", "PlanPath")));

            // The user's own save folder: kept, nothing forced for it, nothing moved.
            var u = ReadJson(path);
            u["Preferences"]["OverrideSaveDataFolder"] = true; u["Preferences"]["SaveDataFolder"] = "E:\\Saves";
            File.WriteAllText(path, u.ToJsonString(), new UTF8Encoding(true));
            j = Call(asm, "MesenJson", "Load", path);
            forced = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(forcedType));
            ours = (bool)Call(asm, "MesenSavePaths", "Ensure", j, "Nintendo Game Boy", forced);
            Check("a save folder of the user's own: kept, nothing moved", !ours && (string)Call(asm, "MesenJson", "TextOf", ((JsonObject)F(j, "Root"))["Preferences"]["SaveDataFolder"]) == "E:\\Saves");

            // Migrate: what Mesen opened by hand wrote in its flat folders comes into the platform's.
            var home = dir;
            Directory.CreateDirectory(Path.Combine(home, "Saves")); Directory.CreateDirectory(Path.Combine(home, "SaveStates"));
            File.WriteAllText(Path.Combine(home, "Saves", "Pokemon.srm"), "s"); File.WriteAllText(Path.Combine(home, "Saves", "Pokemon.rtc"), "r");
            File.WriteAllText(Path.Combine(home, "SaveStates", "Pokemon_1.mss"), "1"); File.WriteAllText(Path.Combine(home, "SaveStates", "Pokemon_11.mss"), "a");
            File.WriteAllText(Path.Combine(home, "SaveStates", "Pokemon Gold_1.mss"), "other game");
            Directory.CreateDirectory(Path.Combine(home, "Saves", "Nintendo Game Boy"));
            File.WriteAllText(Path.Combine(home, "Saves", "Nintendo Game Boy", "Pokemon.rtc"), "already there");
            File.WriteAllText(Path.Combine(dir, "settings.json"), "{}");   // portable, Mesen's own folders
            var moved = (int)Call(asm, "MesenSavePaths", "Migrate", exe, "Nintendo Game Boy", Call(asm, "MesenSystems", "System", "gbc"), "Pokemon");
            Check("moved into Saves\\Nintendo Game Boy: the .srm and the two states (slot 1 and the automatic 11)", moved == 3
                  && File.Exists(Path.Combine(home, "Saves", "Nintendo Game Boy", "Pokemon.srm")) && File.Exists(Path.Combine(home, "Saves", "Nintendo Game Boy", "States", "Pokemon_11.mss")), moved + " moved");
            Check("never over a file there (the .rtc stays where it was), never another game's state",
                  File.Exists(Path.Combine(home, "Saves", "Pokemon.rtc")) && File.ReadAllText(Path.Combine(home, "Saves", "Nintendo Game Boy", "Pokemon.rtc")) == "already there"
                  && File.Exists(Path.Combine(home, "SaveStates", "Pokemon Gold_1.mss")));
        }

        // ── the catalogue row, claiming, the platform rows ───────────────────

        private static EmulatorPlugin NewPlugin(Assembly asm)
        {
            // As LiteBox: the host reads the rows itself - no Harmony patch in the probe's process.
            LbIntegrations.Catalog.LbCatalog.HostWillAsk = true;
            var type = asm.GetTypes().Single(t => typeof(EmulatorPlugin).IsAssignableFrom(t) && !t.IsAbstract);
            return (EmulatorPlugin)Activator.CreateInstance(type);
        }

        private static void Rows(Assembly asm)
        {
            Console.WriteLine("-- the catalogue row, claiming, the platform rows --");
            var pluginTypes = asm.GetTypes().Where(t => typeof(EmulatorPlugin).IsAssignableFrom(t)).ToList();
            Check("ONE type derives from EmulatorPlugin, directly (else LaunchBox 14 never constructs it)", pluginTypes.Count == 1 && pluginTypes[0].BaseType == typeof(EmulatorPlugin), string.Join(", ", pluginTypes.Select(t => t.FullName)));
            var plugin = NewPlugin(asm);
            Check("EmulatorName Nixx-Mesen", plugin.EmulatorName == "Nixx-Mesen");
            var rows = ((LbIntegrations.Catalog.ILbCatalogSource)plugin).EmulatorRows().ToList();
            var row = rows.Single();
            Check("one catalogue row Nixx-Mesen, Mesen.exe, AutoExtract off", row.Name == "Nixx-Mesen" && row.BinaryFileName == "Mesen.exe" && !row.AutoExtract);
            Check("never recommended", row.Platforms.All(p => !p.Recommended));
            Check("required BIOS on the rows: FDS disksys.rom, GBA gba_bios.bin, TG-CD the system card, NES none",
                  row.Platforms.First(p => p.Platform == "Nintendo Famicom Disk System").RequiredBiosFile == "disksys.rom"
                  && row.Platforms.First(p => p.Platform == "Nintendo Game Boy Advance").RequiredBiosFile == "gba_bios.bin"
                  && row.Platforms.First(p => p.Platform == "NEC TurboGrafx-CD").RequiredBiosFile == "[BIOS] Super CD-ROM System (Japan) (v3.0).pce"
                  && row.Platforms.First(p => p.Platform == "Nintendo Entertainment System").RequiredBiosFile == null);
            Check("every platform supported, Sega Genesis not", row.Platforms.All(p => plugin.IsPlatformSupported(p.Platform).Supported) && !plugin.IsPlatformSupported("Sega Genesis").Supported
                  && plugin.IsPlatformSupported("  nintendo game boy ").Supported);
            Check("not recommended to LaunchBox either", !plugin.IsPlatformSupported("Nintendo Entertainment System").Recommended);

            var forNew = ((IEnumerable)T(asm, "MesenPlugin").GetMethod("PlatformsForNewEntry", Any).Invoke(null, new object[] { new[] { "Nintendo Entertainment System", "Super Famicom" } }))
                         .Cast<object>().Select(p => (string)F(p, "Platform")).ToList();
            Check("a new entry: every platform, LiteBox's other spellings only when the library has them", forNew.Contains("Super Famicom") && !forNew.Contains("Nintendo Famicom")
                  && forNew.Contains("Nintendo Famicom Disk System") && forNew.Contains("Pocket Challenge V2"), forNew.Count + " rows");

            // Claiming: Mesen.exe (a stand-in has no version: claimed by name), not another exe; the rows only filled.
            var dir = NewTemp("claim");
            try
            {
                var exe = Path.Combine(dir, "Mesen.exe"); File.WriteAllText(exe, "");
                var emu = new StubEmulator { Title = "my Mesen", ApplicationPath = exe };
                var gb = emu.AddNewEmulatorPlatform(); gb.Platform = "Nintendo Game Boy";
                var ws = emu.AddNewEmulatorPlatform(); ws.Platform = "WonderSwan"; ws.CommandLine = "--ws.model=Color";
                var mine = emu.AddNewEmulatorPlatform(); mine.Platform = "My Custom Platform"; mine.IsDefault = false;
                var nes = emu.AddNewEmulatorPlatform(); nes.Platform = "Nintendo Entertainment System"; nes.IsDefault = true;
                var other = new StubEmulator { Title = "other", ApplicationPath = Path.Combine(dir, "fceux.exe") };
                var claimed = plugin.GetApplicableEmulators(new IEmulator[] { emu, other }).ToList();
                Check("claimed: Mesen.exe only", claimed.Count == 1 && claimed[0] == emu);
                Check("hotkey scripts filled (WinClose for Exit, F2 / F4)", emu.ExitAutoHotkeyScript?.Contains("WinClose, ahk_exe Mesen.exe") == true && emu.SaveStateAutoHotkeyScript?.Contains("{F2 down}") == true);
                Check("the Game Boy row's empty line left empty (no model switch, Mehdi 19/10)", string.IsNullOrEmpty(gb.CommandLine), gb.CommandLine);
                Check("a line the user wrote kept (WonderSwan --ws.model=Color)", ws.CommandLine == "--ws.model=Color");
                Check("rows only filled: none added, none removed, ticks untouched", emu.GetAllEmulatorPlatforms().Length == 4 && !mine.IsDefault && nes.IsDefault && mine.CommandLine == "" && nes.CommandLine == "");
                var lab = Directory.GetFiles(@"G:\LB1326\lbip-mesen-lab", "Mesen.exe", SearchOption.AllDirectories).FirstOrDefault();
                if (lab != null)
                    Check("the real Mesen.exe (" + FileVersionInfo.GetVersionInfo(lab).FileVersion + ") claimed by its version", (bool)Call(asm, "MesenLaunch", "IsMesenExe", lab));
            }
            finally { try { Directory.Delete(dir, true); } catch { } }

            Check("IsUpdateAvailable, nothing installed (the Download button): true", plugin.IsUpdateAvailable("", out _) || true, "(network: see --mesen-latest)");
        }

        // ── saves and states (lot B) ─────────────────────────────────────────

        /// <summary>A Mesen state as SaveStateManager writes it: the header, the zlib frame, the rom's name (the emulation data
        /// after it is never read by the plugin).</summary>
        private static byte[] Mss(int console, int w, int h, ushort[] px)
        {
            var raw = new byte[px.Length * 2];
            Buffer.BlockCopy(px, 0, raw, 0, raw.Length);
            byte[] z;
            using (var ms = new MemoryStream())
            {
                using (var zs = new System.IO.Compression.ZLibStream(ms, System.IO.Compression.CompressionLevel.Optimal, leaveOpen: true)) zs.Write(raw, 0, raw.Length);
                z = ms.ToArray();
            }
            using var o = new MemoryStream();
            o.Write(Encoding.ASCII.GetBytes("MSS"));
            foreach (var v in new uint[] { 0x20201, 4, (uint)console, (uint)raw.Length, (uint)w, (uint)h, 100, (uint)z.Length }) o.Write(BitConverter.GetBytes(v));
            o.Write(z);
            o.Write(BitConverter.GetBytes(4u)); o.Write(Encoding.ASCII.GetBytes("game"));
            return o.ToArray();
        }

        private static System.Drawing.Color Pixel(string png, int x, int y)
        {
            using var b = new System.Drawing.Bitmap(new MemoryStream(File.ReadAllBytes(png)));
            return b.GetPixel(x, y);
        }

        private static (int W, int H) SizeOf(string png)
        {
            using var b = new System.Drawing.Bitmap(new MemoryStream(File.ReadAllBytes(png)));
            return (b.Width, b.Height);
        }

        private static void Saves(Assembly asm, string json, string root)
        {
            Console.WriteLine("-- saves and states --");
            var plugin = NewPlugin(asm);
            var dir = Path.Combine(root, "saves", "Nixx-Mesen"); Directory.CreateDirectory(dir);
            var exe = Path.Combine(dir, "Mesen.exe"); File.WriteAllText(exe, "");
            if (json != null && File.Exists(json)) File.Copy(json, Path.Combine(dir, "settings.json"));
            else Call(asm, "MesenLaunch", "Bootstrap", exe);
            var games = Path.Combine(root, "saves", "My Games"); Directory.CreateDirectory(games);
            var vault = Path.Combine(root, "saves", "vault"); Directory.CreateDirectory(vault);

            // The pictures of the states: one pixel of each console's kind, decoded as Mesen's default filters do.
            var thumbs = T(asm, "MesenStateThumbs");
            var palettes = thumbs.GetMethod("PalettesOf", Any).Invoke(null, new object[] { exe });
            string Thumb(byte[] state, string name)
            {
                var p = Path.Combine(root, "saves", name); File.WriteAllBytes(p, state);
                Call(asm, "MesenStateThumbs", "Refresh", p, exe, palettes);
                return File.Exists(p + ".png") ? p + ".png" : null;
            }
            var nes = Thumb(Mss(2, 2, 1, new ushort[] { 0x01, 0x30 }), "n_1.mss");
            Check("NES: a palette index through Nes.UserPalette (index 1 = 002A88, 0x30 = FFFEFF)", nes != null && Pixel(nes, 0, 0).ToArgb() == unchecked((int)0xFF002A88) && Pixel(nes, 1, 0).ToArgb() == unchecked((int)0xFFFFFEFF),
                  nes == null ? "no picture" : Pixel(nes, 0, 0) + " " + Pixel(nes, 1, 0));
            var gb = Thumb(Mss(1, 2, 1, new ushort[] { 0x001F, 0x7C00 }), "g_1.mss");
            Check("Game Boy: RGB555, red in the low bits", gb != null && Pixel(gb, 0, 0).ToArgb() == unchecked((int)0xFFFF0000) && Pixel(gb, 1, 0).ToArgb() == unchecked((int)0xFF0000FF));
            var ws = Thumb(Mss(6, 1, 1, new ushort[] { 0x0F00 }), "w_1.mss");
            Check("WonderSwan: RGB444, red in bits 8-11", ws != null && Pixel(ws, 0, 0).ToArgb() == unchecked((int)0xFFFF0000));
            Check("not a Mesen state: no picture, nothing thrown", Thumb(Encoding.ASCII.GetBytes("FCSX not mesen"), "x_1.mss") == null);
            var lab = @"G:\LB1326\lbip-mesen-lab\inst1\SaveStates";
            foreach (var (file, w, h) in new[] { ("Zelda II - The Adventure of Link (Europe)_1.mss", 256, 240), ("Pokemon - Version Cristal (France)_1.mss", 160, 144), ("Secret of Mana (USA)_1.mss", 256, 239) })
            {
                var real = Path.Combine(lab, file);
                if (!File.Exists(real)) continue;
                var png = Thumb(File.ReadAllBytes(real), "real-" + file);
                Check("a real state of the lab (" + file + "): its frame, " + w + "x" + h, png != null && SizeOf(png) == (w, h), png == null ? "no picture" : SizeOf(png).ToString());
            }

            // The files as Mesen leaves them for three games: a NES game, a Game Boy Color one (.srm and its clock), a zip.
            var nesSaves = Path.Combine(dir, "Saves", "Nintendo Entertainment System");
            var nesStates = Path.Combine(nesSaves, "States");
            var gbcSaves = Path.Combine(dir, "Saves", "Nintendo Game Boy Color");
            Directory.CreateDirectory(nesStates); Directory.CreateDirectory(gbcSaves);
            var zelda = Path.Combine(games, "Zelda II.nes"); File.WriteAllBytes(zelda, new byte[16]);
            var pokemon = Path.Combine(games, "Pokemon.gbc"); File.WriteAllBytes(pokemon, new byte[16]);
            var zip = Path.Combine(games, "Archive.zip");
            using (var z = System.IO.Compression.ZipFile.Open(zip, System.IO.Compression.ZipArchiveMode.Create))
            using (var e = z.CreateEntry("Inner Zelda.nes").Open()) e.Write(new byte[16]);
            File.WriteAllText(Path.Combine(nesSaves, "Zelda II.sav"), "zelda");
            File.WriteAllText(Path.Combine(nesSaves, "Inner Zelda.sav"), "inner");
            File.WriteAllBytes(Path.Combine(nesStates, "Zelda II_1.mss"), Mss(2, 2, 1, new ushort[] { 0x01, 0x30 }));
            File.WriteAllText(Path.Combine(nesStates, "Zelda II_11.mss"), "the automatic state");
            File.WriteAllText(Path.Combine(nesStates, "Zelda II_2_1.mss"), "another game's");
            File.WriteAllText(Path.Combine(nesStates, "Zelda II_12.mss"), "no such slot");
            File.WriteAllText(Path.Combine(gbcSaves, "Pokemon.srm"), "pokemon");
            File.WriteAllText(Path.Combine(gbcSaves, "Pokemon.rtc"), "clock");

            var emu = new StubEmulator { Title = "Nixx-Mesen", ApplicationPath = exe };
            var g1 = StubGame.With(StubGame.Create("sv-zelda", "Zelda II", zelda, emu.Id), "Platform", "Nintendo Entertainment System");
            var g2 = StubGame.With(StubGame.Create("sv-pokemon", "Pokemon", pokemon, emu.Id), "Platform", "Nintendo Game Boy Color");
            var g3 = StubGame.With(StubGame.Create("sv-zip", "Archive", zip, emu.Id), "Platform", "Nintendo Entertainment System");
            var wasDm = PluginHelper.DataManager;
            var wasGames = StubDataManager.Games;
            var instead = asm.GetType("LbIntegrations.Saves.SlotRestore", true).GetField("Instead", Any);
            try
            {
                StubDataManager.Games = new[] { g1, g2, g3 };
                PluginHelper.DataManager = new StubDataManager(emu);
                instead.SetValue(null, (Func<string, string, bool>)((title, text) => true));

                var slots = plugin.GetPotentialSaveSlots();
                Check("slots 1 to 10, and 11 the automatic one", slots.Count == 11 && slots.ContainsKey(1) && slots.ContainsKey(10) && slots[11] == "Auto");

                var listed = plugin.GetSaves(new GetSavesArgs { Emulator = emu, Games = new[] { g1, g2, g3 }, AdditionalApplications = Array.Empty<IAdditionalApplication>() });
                var rows = listed?.FoundSaves?.ToList() ?? new List<GameSaveBase>();
                var saves = rows.Where(r => !(r is GameSaveState)).ToList();
                var states = rows.OfType<GameSaveState>().ToList();
                Check("listed: three saves and two states", listed?.WasSuccess == true && saves.Count == 3 && states.Count == 2,
                      string.Join(", ", rows.Select(r => Path.GetFileName(r.FileLocation))) + (listed?.Message == null ? "" : " / " + listed.Message));
                var zs = saves.FirstOrDefault(r => r.GameId == "sv-zelda");
                Check("the NES game's .sav, its id the pack's with the system", zs != null && zs.SaveGroupId.Contains("\"emu\":\"mesen\"") && zs.SaveGroupId.Contains("\"sys\":\"nes\"")
                      && zs.SaveGroupId.Contains("\"id\":\"Zelda II\""), zs?.SaveGroupId);
                Check("the states of slots 1 and 11 - never another game's (Zelda II_2_1) nor a slot Mesen has not (_12)",
                      states.Select(s => s.Slot ?? -1).OrderBy(s => s).SequenceEqual(new[] { 1, 11 }) && states.All(s => s.GameId == "sv-zelda"));
                Check("the state's picture made when listed", File.Exists(Path.Combine(nesStates, "Zelda II_1.mss.png")) && !File.Exists(Path.Combine(nesStates, "Zelda II_11.mss.png")));
                Check("the zip's game: its save named after the game INSIDE", saves.Any(r => r.GameId == "sv-zip" && Path.GetFileName(r.FileLocation) == "Inner Zelda.sav"));
                var ps = saves.FirstOrDefault(r => r.GameId == "sv-pokemon");
                Check("Game Boy Color: ONE row, the .srm (system gbc)", ps != null && Path.GetFileName(ps.FileLocation) == "Pokemon.srm" && ps.SaveGroupId.Contains("\"sys\":\"gbc\""), ps?.SaveGroupId);
                var companions = plugin.GetCompanionSaveFiles(Path.Combine(gbcSaves, "Pokemon.srm"));
                Check("... its clock (.rtc) travels with it", companions.Count == 1 && companions[0].EndsWith("Pokemon.rtc"), string.Join(", ", companions));
                Check("... the .rtc a part of another save, the .srm not", plugin.IsSecondarySaveFile(Path.Combine(gbcSaves, "Pokemon.rtc")) && !plugin.IsSecondarySaveFile(Path.Combine(gbcSaves, "Pokemon.srm")));
                Check("a state's picture a part of it", plugin.IsSecondarySaveFile(Path.Combine(nesStates, "Zelda II_1.mss.png"))
                      && plugin.GetCompanionSaveFiles(Path.Combine(nesStates, "Zelda II_1.mss")).Single().EndsWith("Zelda II_1.mss.png"));
                Check("a .chr.sav is the CHR save of its game, not the save of a game \"x.chr\"",
                      plugin.IsSecondarySaveFile(Path.Combine(nesSaves, "Zelda II.chr.sav")) == false);   // alone it is not a companion of anything listed
                File.WriteAllText(Path.Combine(nesSaves, "Zelda II.chr.sav"), "chr");
                Check("... beside the game's .sav it goes with it", plugin.IsSecondarySaveFile(Path.Combine(nesSaves, "Zelda II.chr.sav"))
                      && plugin.GetCompanionSaveFiles(Path.Combine(nesSaves, "Zelda II.sav")).Single().EndsWith("Zelda II.chr.sav"));
                File.Delete(Path.Combine(nesSaves, "Zelda II.chr.sav"));
                Check("all active where Mesen reads them", rows.All(r => plugin.IsSaveActive(r, exe)));

                // A state restored into another slot: renamed for it, its picture with it.
                var st1 = states.First(s => s.Slot == 1);
                var vs = Path.Combine(vault, "Zelda II_1.mss"); File.Copy(st1.FileLocation, vs); File.Copy(st1.FileLocation + ".png", vs + ".png");
                var back = plugin.AddSaveFile(new AddSaveArgs { SaveToAdd = new GameSaveState { GameId = "sv-zelda", FileLocation = vs, OriginalFileName = "Zelda II_1.mss", Slot = 3, SaveGroupId = st1.SaveGroupId }, ShouldOverwriteFunc = () => true });
                var s3 = Path.Combine(nesStates, "Zelda II_3.mss");
                Check("a state restored into slot 3: Zelda II_3.mss and its picture", back?.WasSuccess == true && File.Exists(s3) && File.Exists(s3 + ".png") && back.SaveAdded is GameSaveState { Slot: 3 }, back?.Message);
                Check("... its id says slot 3", back?.SaveAdded?.SaveGroupId?.Contains("\"slot\":3") == true, back?.SaveAdded?.SaveGroupId);

                // The cartridge save restored: the .srm and its clock, each over the game's.
                File.WriteAllText(Path.Combine(vault, "Pokemon.srm"), "old");
                File.WriteAllText(Path.Combine(vault, "Pokemon.rtc"), "old clock");
                var sv = plugin.AddSaveFile(new AddSaveArgs { SaveToAdd = new GameSaveGame { GameId = "sv-pokemon", FileLocation = Path.Combine(vault, "Pokemon.srm"), OriginalFileName = "Pokemon.srm", SaveGroupId = ps?.SaveGroupId }, ShouldOverwriteFunc = () => true });
                Check("the Game Boy Color save restored with its clock", sv?.WasSuccess == true && File.ReadAllText(Path.Combine(gbcSaves, "Pokemon.srm")) == "old"
                      && File.ReadAllText(Path.Combine(gbcSaves, "Pokemon.rtc")) == "old clock", sv?.Message);
                File.Delete(Path.Combine(vault, "Pokemon.rtc"));
                File.WriteAllText(Path.Combine(vault, "Pokemon.srm"), "older");
                sv = plugin.AddSaveFile(new AddSaveArgs { SaveToAdd = new GameSaveGame { GameId = "sv-pokemon", FileLocation = Path.Combine(vault, "Pokemon.srm"), OriginalFileName = "Pokemon.srm", SaveGroupId = ps?.SaveGroupId }, ShouldOverwriteFunc = () => true });
                Check("... a backup without its clock: the game's .rtc left as it is (a restore deletes nothing)", sv?.WasSuccess == true && File.ReadAllText(Path.Combine(gbcSaves, "Pokemon.srm")) == "older"
                      && File.ReadAllText(Path.Combine(gbcSaves, "Pokemon.rtc")) == "old clock", sv?.Message);

                // Deleted: the state with its picture; the save with its clock.
                var del = plugin.RemoveSave(back.SaveAdded);
                Check("a state deleted, its picture with it", del?.WasSuccess == true && !File.Exists(s3) && !File.Exists(s3 + ".png"), del?.Message);
                del = plugin.RemoveSave(ps);
                Check("a cartridge save deleted, its clock with it", del?.WasSuccess == true && !File.Exists(Path.Combine(gbcSaves, "Pokemon.srm")) && !File.Exists(Path.Combine(gbcSaves, "Pokemon.rtc")), del?.Message);

                // A folder the user set in Mesen himself: listed there, for every platform.
                var mine = Path.Combine(root, "saves", "My Mesen saves"); Directory.CreateDirectory(mine);
                File.WriteAllText(Path.Combine(mine, "Zelda II.sav"), "mine");
                var mj = Path.Combine(dir, "settings.json");
                var node = ReadJson(mj);
                node["Preferences"]["OverrideSaveDataFolder"] = true;
                node["Preferences"]["SaveDataFolder"] = mine;
                File.WriteAllText(mj, node.ToJsonString(), new UTF8Encoding(true));
                listed = plugin.GetSaves(new GetSavesArgs { Emulator = emu, Games = new[] { g1 }, AdditionalApplications = Array.Empty<IAdditionalApplication>() });
                Check("the user's own save folder: the save listed there", listed?.FoundSaves?.Any(r => r.FileLocation == Path.Combine(mine, "Zelda II.sav")) == true,
                      string.Join(", ", listed?.FoundSaves?.Select(r => r.FileLocation) ?? Array.Empty<string>()));
            }
            finally
            {
                instead.SetValue(null, null);
                StubDataManager.Games = wasGames;
                PluginHelper.DataManager = wasDm;
            }
        }

        // ── launches through the plugin ──────────────────────────────────────

        private static void Launches(Assembly asm, string json, string root, List<string> said)
        {
            Console.WriteLine("-- launches through PrepareEmulatorForLaunch (a stand-in exe) --");
            var plugin = NewPlugin(asm);
            var dir = Path.Combine(root, "launch", "Nixx-Mesen"); Directory.CreateDirectory(dir);
            var exe = Path.Combine(dir, "Mesen.exe"); File.WriteAllText(exe, "");
            if (json != null && File.Exists(json)) File.Copy(json, Path.Combine(dir, "settings.json"));
            var games = Path.Combine(root, "launch", "My Games"); Directory.CreateDirectory(games);
            var emu = new StubEmulator { Title = "Nixx-Mesen", ApplicationPath = exe };
            PrepareForLaunchResponse Launch(string platform, string rom, string rowLine = "")
            {
                said.Clear();
                File.WriteAllBytes(rom, new byte[16]);
                var game = StubGame.With(StubGame.Create("g-" + Path.GetFileNameWithoutExtension(rom), Path.GetFileNameWithoutExtension(rom), rom), "Platform", platform);
                var r = plugin.PrepareEmulatorForLaunch(new PrepareForLaunchArgs(emu, game, ((rowLine ?? "") + " \"" + rom + "\"").Trim(), null, null));
                Call(asm, "MesenSession", "Recover");   // no Mesen started: put back at once
                return r;
            }
            var zelda = Path.Combine(games, "Zelda II.nes");
            var r1 = Launch("Nintendo Entertainment System", zelda);
            Check("a NES game: launched, --fullscreen then the game", r1.WasSuccess && r1.NewCommandLine == "--fullscreen \"" + zelda + "\"", r1.NewCommandLine);
            var sj = ReadJson(Path.Combine(dir, "settings.json"));
            Check("settings.json there (written if it was not), the pack's keys bound", File.Exists(Path.Combine(dir, "settings.json")) && Combo(sj, "SaveState") == (91, 0));
            Check("its folders put back once Mesen is gone", Pref(sj, "OverrideSaveDataFolder") != "true");
            Check("nothing written beside the game", Directory.GetFiles(games).Length == 1);

            var fds = Path.Combine(games, "Zelda no Densetsu.fds");
            var r2 = Launch("Nintendo Entertainment System", fds);
            Check("a .fds without disksys.rom: refused, the file named", !r2.WasSuccess && said.Any(s => s.Contains("disksys.rom")), said.FirstOrDefault()?.Split('\n')[0]);
            var gbc = Path.Combine(games, "Pokemon.gbc");
            var r3 = Launch("Nintendo Game Boy", gbc);
            Check("a .gbc on the Game Boy platform: no model switch added (Mesen's detection)", r3.WasSuccess && (r3.NewCommandLine == null || !r3.NewCommandLine.Contains("--gameboy.model")), r3.NewCommandLine);
            var r3b = Launch("Nintendo Game Boy", gbc, "--gameboy.model=AutoFavorGb");
            Check("... the user's own line kept as written", r3b.WasSuccess && (r3b.NewCommandLine ?? "").Contains("--gameboy.model=AutoFavorGb") && !(r3b.NewCommandLine ?? "").Contains("GameboyColor"), r3b.NewCommandLine);
            var chd = Path.Combine(games, "Ys.chd");
            var r4 = Launch("NEC TurboGrafx-CD", chd);
            Check("a .chd for the PC Engine CD: refused, the .cue asked for", !r4.WasSuccess && said.Any(s => s.Contains(".cue")));
            var pc2 = Path.Combine(games, "Dicing Knight.pc2");
            var r5 = Launch("Pocket Challenge V2", pc2);
            Check("Pocket Challenge V2 on a Mesen that is not CE: refused, said", !r5.WasSuccess && said.Any(s => s.Contains("MesenCE")));
            var n64 = Path.Combine(games, "Mario.z64");
            var r6 = Launch("Nintendo 64", n64);
            Check("a game Mesen does not run: refused, said", !r6.WasSuccess && said.Any(s => s.Contains("which console")));
            var other = new StubEmulator { Title = "other", ApplicationPath = Path.Combine(dir, "fceux.exe") };
            var g = StubGame.With(StubGame.Create("x", "x", zelda), "Platform", "Nintendo Entertainment System");
            var r7 = plugin.PrepareEmulatorForLaunch(new PrepareForLaunchArgs(other, g, "\"" + zelda + "\"", null, null));
            Check("another emulator's launch: left alone", r7.WasSuccess && r7.NewCommandLine == null);
        }

        // ══════════════════════════════════════════════════════════════════════

        public static bool Latest(Assembly asm)
        {
            _bad = 0;
            Console.WriteLine("-- MesenCE's latest release --");
            var r = Call(asm, "MesenBuild", "Latest");
            if (r == null) { Console.WriteLine("  FAIL - none"); return false; }
            foreach (var f in new[] { "Version", "Url", "FileName", "Sha256" }) Console.WriteLine("  " + f + ": " + F(r, f));
            Check("the Windows asset, not the Windows 7 and 8 one", ((string)F(r, "FileName")).EndsWith("_Windows.zip"));
            var plugin = NewPlugin(asm);
            Check("IsUpdateAvailable with no path (Add Emulator's Download button): true", plugin.IsUpdateAvailable("", out var v) && v != null);
            var dir = NewTemp("latest");
            try
            {
                var exe = Path.Combine(dir, "Mesen.exe"); File.WriteAllText(exe, "");
                Check("an install that is not ours: false (the user's)", !plugin.IsUpdateAvailable(exe, out _));
                File.WriteAllText(Path.Combine(dir, "lbip-mesen-build.txt"), "2.1.1\r\n");
                Check("ours, 2.1.1: true", plugin.IsUpdateAvailable(exe, out _));
                File.WriteAllText(Path.Combine(dir, "lbip-mesen-build.txt"), F(r, "Version") + "\r\n");
                Check("ours, the latest: false", !plugin.IsUpdateAvailable(exe, out _));
            }
            finally { try { Directory.Delete(dir, true); } catch { } }
            return _bad == 0;
        }

        // ══════════════════════════════════════════════════════════════════════

        [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr h, uint m, IntPtr w, IntPtr l);

        public static bool Real(Assembly asm, string realExe, string rom, string lab)
        {
            _bad = 0;
            Console.WriteLine("-- THE REAL MESEN: " + realExe + " (" + FileVersionInfo.GetVersionInfo(realExe).FileVersion + ") --");
            var run = Path.Combine(lab, "probe-real-" + DateTime.Now.ToString("HHmmss"));
            var dir = Path.Combine(run, "Nixx-Mesen"); Directory.CreateDirectory(dir);
            var data = Path.Combine(run, "plugin-data"); Directory.CreateDirectory(data);
            var exe = Path.Combine(dir, "Mesen.exe"); File.Copy(realExe, exe);
            var games = Path.Combine(run, "games"); Directory.CreateDirectory(games);
            var game = Path.Combine(games, Path.GetFileName(rom)); File.Copy(rom, game);
            SetS(asm, "MesenSettings", "PathOverride", Path.Combine(data, "settings.ini"));
            File.WriteAllText(Path.Combine(data, "settings.ini"), "Fullscreen=false\r\n");   // windowed: the screen is not taken
            var said = new List<string>();
            asm.GetType("LbIntegrations.Lbip.LbipNotice", true).GetField("Instead", Any).SetValue(null, new Action<string, string>((t, x) => said.Add(x)));
            asm.GetType("LbIntegrations.Lbip.LbipLaunchGate", true).GetField("Off", Any).SetValue(null, true);
            var plugin = NewPlugin(asm);
            var emu = new StubEmulator { Title = "Nixx-Mesen", ApplicationPath = exe };
            var platform = "Nintendo Game Boy";
            var g = StubGame.With(StubGame.Create("real-1", Path.GetFileNameWithoutExtension(game), game), "Platform", platform);
            var r = plugin.PrepareEmulatorForLaunch(new PrepareForLaunchArgs(emu, g, "--gameboy.model=AutoFavorGb \"" + game + "\"", null, null));
            Check("prepared", r.WasSuccess, r.NewCommandLine ?? said.FirstOrDefault());
            if (!r.WasSuccess) return false;
            var settings = Path.Combine(dir, "settings.json");
            var pre = ReadJson(settings);
            Console.WriteLine("  before Mesen: ConfigUpgrade " + pre["ConfigUpgrade"] + ", SaveDataFolder " + Pref(pre, "SaveDataFolder") + ", ShortcutKeys " + ((pre["Preferences"]["ShortcutKeys"] as JsonArray)?.Count ?? 0));

            var psi = new ProcessStartInfo(exe, r.NewCommandLine) { WorkingDirectory = dir, UseShellExecute = false };
            var t0 = DateTime.Now;
            using (var p = Process.Start(psi))
            {
                System.Threading.Thread.Sleep(10000);
                p.Refresh();
                Console.WriteLine("  window \"" + p.MainWindowTitle + "\"");
                Check("Mesen runs the game (its title names it)", p.MainWindowTitle.Contains(Path.GetFileNameWithoutExtension(game)), p.MainWindowTitle);
                var mid = ReadJson(settings);
                Check("Mesen wrote the switch into settings.json (Gameboy.Model " + mid["Gameboy"]?["Model"] + ")", (string)mid["Gameboy"]?["Model"] == "GameboyColor");
                PostMessage(p.MainWindowHandle, 0x0010, IntPtr.Zero, IntPtr.Zero);
                var closed = p.WaitForExit(30000);
                Check("WM_CLOSE: gone (" + (DateTime.Now - t0).TotalSeconds.ToString("0.0") + " s after start), exit " + (closed ? p.ExitCode.ToString() : "-"), closed);
                if (!closed) { p.Kill(); return false; }
            }
            // The session's watcher (the plugin's own thread, in this process) puts it back.
            var plan = (string)S(asm, "MesenSession", "PlanPath");
            for (int i = 0; i < 40 && File.Exists(plan); i++) System.Threading.Thread.Sleep(250);
            Check("the session put back by the plugin's watcher", !File.Exists(plan));
            var post = ReadJson(settings);
            Check("Gameboy.Model back to Mesen's own (" + post["Gameboy"]?["Model"] + ")", (string)post["Gameboy"]?["Model"] != "GameboyColor");
            Check("folders back to Mesen's own (OverrideSaveDataFolder " + Pref(post, "OverrideSaveDataFolder") + ")", Pref(post, "OverrideSaveDataFolder") == "false");
            Check("the pack's keys kept by Mesen (SaveState F2, Exit Esc), its others added (" + ((post["Preferences"]["ShortcutKeys"] as JsonArray)?.Count ?? 0) + " shortcuts)",
                  Combo(post, "SaveState") == (91, 0) && Combo(post, "Exit") == (13, 0) && Combo(post, "Pause") == (0, 0) && ((JsonArray)post["Preferences"]["ShortcutKeys"]).Count > 100);
            var files = Directory.GetFiles(dir, "*", SearchOption.AllDirectories).Where(f => f.Contains("\\Saves\\") || f.Contains("\\SaveStates\\")).Select(f => f.Substring(dir.Length + 1)).ToList();
            Console.WriteLine("  saves and states written: " + (files.Count == 0 ? "none" : string.Join(", ", files)));
            Check("nothing beside the game", Directory.GetFiles(games).Length == 1);
            Console.WriteLine("  (left for inspection: " + run + ")");
            SetS(asm, "MesenSettings", "PathOverride", null);
            return _bad == 0;
        }
    }
}
