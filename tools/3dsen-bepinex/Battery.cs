// --nixx-battery=<folder>: the cartridge's battery RAM kept on disk, as a NES keeps it (after a first fix of the same idea
// that Mehdi pointed at, 19/10 - docs\3dsen.md section 16).
//
// WHY 3dSen LOSES IT ([src] 3dSen 1.1.2, Nes3D.DefaultCore): the cartridge's RAM is Memory.saveRam (8 KB, $6000-$7FFF), which
// Memory.Reset zeroes - and Engine.LoadGameProfile resets right after LoadRom - and nothing ever writes it to disk: only a
// save state carries it. Cartridge.save_ram_present is the iNES battery bit (header byte 6, bit 1).
//
// WHAT THIS DOES, by name (Harmony, nothing referenced at build time):
//   - Core.LoadRom, postfix: the game's file <folder>\<rom>.srm read into saveRam (zeroed first), battery games only;
//   - Memory.Reset, around it: a battery game's saveRam kept through the reset (a NES's battery RAM survives one); other
//     games keep 3dSen's zeroing;
//   - written to disk: in Core.CloseRom (3dSen switching games, or quitting from its menu - Engine.Quit -> Stop), in
//     OnApplicationQuit (the window closed - WM_CLOSE from AutoHotkey or LaunchBox - and the pack's Esc, which calls
//     Application.Quit: neither goes through CloseRom, the first fix lost the game there), and every 2 s when it changed
//     (a 3dSen killed or crashed loses at most that). Only when the bytes differ from what is on disk; a game that never
//     wrote its RAM (all zero, no file yet) gets no file.
//
// THE FILE: the name of the ROM 3dSen plays (Cartridge.romName: the file's, an archive's own name with ".nes"), its extension
// replaced by .srm - so <rom file name>.srm, the pack's rule. Written atomically (a .tmp beside it, then moved over).
//
// A STATE LOADED brings the battery RAM it was made with (3dSen's Memory.LoadState): what is written next is that one -
// what every emulator does (a state is the whole console at that moment).

using System;
using System.IO;
using System.Reflection;
using HarmonyLib;

namespace LbIntegrations.ThreeDSen.Mod
{
    internal static class Battery
    {
        private static NixxLog _log;
        private static string _folder;
        private static object _core;              // Nes3D.DefaultCore.Core of the last LoadRom
        private static string _lastPath;
        private static byte[] _lastBytes;         // what _lastPath holds on disk (null: unknown / no file)
        private static readonly object Gate = new object();

        public static void Install(Harmony h, Args a, NixxLog log)
        {
            _log = log;
            if (a.Battery == null) return;
            _folder = a.Battery;
            var core = AccessTools.TypeByName("Nes3D.DefaultCore.Core");
            var memory = AccessTools.TypeByName("Nes3D.DefaultCore.Memory");
            if (core == null || memory == null) { log.Error("battery: Nes3D.DefaultCore.Core / Memory not found - no battery saves"); return; }
            Patch(h, core, "LoadRom", null, nameof(LoadRomPostfix));
            Patch(h, core, "CloseRom", nameof(CloseRomPrefix), null);
            Patch(h, memory, "Reset", nameof(ResetPrefix), nameof(ResetPostfix));
            log.Info("battery: saves in " + _folder);
        }

        private static void Patch(Harmony h, Type t, string name, string prefix, string postfix)
        {
            var m = AccessTools.Method(t, name);
            if (m == null) { _log.Error("battery: " + t.FullName + "." + name + " not found"); return; }
            h.Patch(m, prefix == null ? null : new HarmonyMethod(typeof(Battery), prefix), postfix == null ? null : new HarmonyMethod(typeof(Battery), postfix));
        }

        // ── the hooks ────────────────────────────────────────────────────────

        private static void LoadRomPostfix(object __instance, bool __result)
        {
            try
            {
                if (!__result) return;
                lock (Gate)
                {
                    _core = __instance;
                    var ram = SaveRam(__instance);
                    var path = PathOf(__instance);
                    _lastPath = path; _lastBytes = null;
                    if (ram == null || path == null) return;          // not a battery game: 3dSen's own behaviour
                    Array.Clear(ram, 0, ram.Length);
                    if (!File.Exists(path)) { _log.Info("battery: no " + Path.GetFileName(path) + " yet"); return; }
                    var data = File.ReadAllBytes(path);
                    Array.Copy(data, ram, Math.Min(data.Length, ram.Length));
                    _lastBytes = (byte[])ram.Clone();
                    _log.Info("battery: " + path + " loaded (" + data.Length + " bytes)");
                }
            }
            catch (Exception ex) { _log.Error("battery: load: " + ex.Message); }
        }

        private static void CloseRomPrefix(object __instance) => Flush(__instance, "game closed");

        private static void ResetPrefix(object __instance, out byte[] __state)
        {
            __state = null;
            try
            {
                var core = _core;
                if (core == null || !ReferenceEquals(Game.Get(core, "mem"), __instance) || PathOf(core) == null) return;
                var ram = Game.Get(__instance, "saveRam") as byte[];
                if (ram != null) __state = (byte[])ram.Clone();
            }
            catch { __state = null; }
        }

        private static void ResetPostfix(object __instance, byte[] __state)
        {
            try
            {
                if (__state == null) return;
                if (Game.Get(__instance, "saveRam") is byte[] ram) Array.Copy(__state, ram, Math.Min(__state.Length, ram.Length));
            }
            catch { }
        }

        // ── writing ──────────────────────────────────────────────────────────

        /// <summary>The plugin's own calls: every 2 s, and when the application quits.</summary>
        public static void Flush(string why) { if (_folder != null) Flush(_core, why); }

        private static void Flush(object core, string why)
        {
            if (_folder == null || core == null) return;
            try
            {
                lock (Gate)
                {
                    var ram = SaveRam(core);
                    var path = PathOf(core);
                    if (ram == null || path == null) return;
                    var bytes = (byte[])ram.Clone();
                    if (path == _lastPath && _lastBytes != null && Same(bytes, _lastBytes)) return;
                    if (path != _lastPath) { _lastPath = path; _lastBytes = File.Exists(path) ? File.ReadAllBytes(path) : null; if (_lastBytes != null && Same(bytes, _lastBytes)) return; }
                    if (_lastBytes == null && AllZero(bytes) && !File.Exists(path)) return;   // the game never wrote its RAM
                    Directory.CreateDirectory(Path.GetDirectoryName(path));
                    var tmp = path + ".tmp";
                    File.WriteAllBytes(tmp, bytes);
                    if (File.Exists(path)) File.Delete(path);
                    File.Move(tmp, path);
                    _lastBytes = bytes;
                    _log.Info("battery: " + path + " written (" + why + ")");
                }
            }
            catch (Exception ex) { _log.Error("battery: write: " + ex.Message); }
        }

        // ── 3dSen's members ──────────────────────────────────────────────────

        /// <summary>The cartridge's RAM, or null when the game has no battery.</summary>
        private static byte[] SaveRam(object core)
        {
            var cart = Game.Get(core, "cartrige");                   // 3dSen's spelling
            if (!(Game.Get(cart, "save_ram_present") is bool battery) || !battery) return null;
            return Game.Get(Game.Get(core, "mem"), "saveRam") as byte[];
        }

        /// <summary>&lt;folder&gt;\&lt;rom&gt;.srm for a battery game, else null.</summary>
        private static string PathOf(object core)
        {
            var cart = Game.Get(core, "cartrige");
            if (!(Game.Get(cart, "save_ram_present") is bool battery) || !battery) return null;
            var name = Path.GetFileNameWithoutExtension(Game.Get(cart, "romName") as string ?? "");
            if (string.IsNullOrEmpty(name)) return null;
            foreach (var c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
            return Path.Combine(_folder, name + ".srm");
        }

        private static bool Same(byte[] a, byte[] b)
        {
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
            return true;
        }

        private static bool AllZero(byte[] a)
        {
            foreach (var x in a) if (x != 0) return false;
            return true;
        }
    }
}
