// Which emulators the catch-all takes (lot 6, docs\saves-lot6.md): those NO OTHER plugin drives.
//
// LaunchBox picks an emulator's plugin by asking each loaded plugin, emulator by emulator, whether it takes it
// (PluginRegistry.GetBestEmulatorPlugin -> GetApplicableEmulators, measured with the save probe 09/10). Two plugins
// taking one emulator is a choice LaunchBox makes in obfuscated code - so the catch-all never competes: it asks every
// OTHER plugin LaunchBox loaded (LbPluginRegistry) first, and takes the emulator only when none of them does.
//
// Takes nothing:
//   - outside LaunchBox (LiteBox asks every plugin itself), with the switch off (Compatibility page, "catchAll");
//   - while LaunchBox may still be loading plugins (review 13/10: PluginInitialized is sent right after THIS plugin is
//     loaded, not after all of them): before LaunchBox/BigBox says its start-up is complete, only once the registry
//     has held the same number of emulator plugins for SettleSeconds - a plugin loaded after Nixx-Saves must be asked -
//     unless LaunchBox ITSELF asks, through its PluginRegistry.GetBestEmulatorPlugin: it has loaded its plugins then.
//     (Measured 13/10: a plugin of Local\Plugins gets PluginInitialized only, never the start-up events, and LaunchBox's
//     start-up save scan asks 3 s after it - the settling alone made the catch-all miss it, and LaunchBox asks a plugin
//     whether it supports saves at its start only.)
//   - when the registry cannot be read - an emulator another plugin might drive is never taken on a guess;
//   - an emulator another plugin takes, or whose plugin THROWS when asked (it may well take it);
//   - an exe the pack or one of LaunchBox's integrations is made for (Names): a user who turned that plugin off in
//     LaunchBox's plugin manager meant its emulator to have none, not the catch-all;
//   - an emulator with no exe.
//
// Kept 5 minutes per emulator (its id and exe), and only while the registry holds the same number of plugins: LaunchBox
// asks at every game shown, each answer costs a question to every plugin, and each question is a line in their logs.
// Re-entry (a plugin asking the router while the catch-all asks it) takes nothing.

#nullable enable

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using LbIntegrations.Compat;
using Unbroken.LaunchBox.Plugins;
using Unbroken.LaunchBox.Plugins.Data;

namespace LbIntegrations.SavesCatchAll
{
    internal static class CatchAllClaims
    {
        public const string Switch = "catchAll";
        private const int SettleSeconds = 3;
        private const int KeepSeconds = 300;

        /// <summary>Exe names (lower case, start of the name) of the emulators the pack and LaunchBox's integrations are made for.</summary>
        internal static readonly string[] Names =
        {
            // LaunchBox's integrations (System\Plugins, 14.0.1)
            "retroarch", "dolphin", "pcsx2", "duckstation", "mame", "rpcs3", "scummvm", "cemu", "xemu", "bigpemu",
            // the pack (and the names its plugins accept: x.emu.exe is the pack's xemu, reicast is Flycast's old name)
            "ares", "bsnes", "cxbx", "demul", "flycast", "reicast", "melonds", "no$gba", "nogba", "no-gba", "no_gba", "ppsspp",
            "rasnes9x", "super zsnes", "superzsnes", "vita3k", "xenia", "x.emu",
        };

        private static readonly ConcurrentDictionary<string, (long At, int Plugins, bool Yes)> Known = new(StringComparer.OrdinalIgnoreCase);
        private static readonly ConcurrentDictionary<string, IEmulator> Taken = new(StringComparer.OrdinalIgnoreCase);
        private static readonly ConcurrentDictionary<string, byte> Said = new(StringComparer.OrdinalIgnoreCase);
        private static readonly ThreadLocal<int> Asking = new ThreadLocal<int>(() => 0);
        private static int _initialized, _started;
        private static readonly object SettleGate = new object();
        private static int _lastCount = -1;
        private static long _countSince;

        /// <summary>LaunchBox loaded this plugin (PluginInitialized).</summary>
        public static void Initialized() => Interlocked.Exchange(ref _initialized, 1);

        /// <summary>LaunchBox or BigBox finished starting: every plugin is loaded.</summary>
        public static void Started() { Interlocked.Exchange(ref _initialized, 1); Interlocked.Exchange(ref _started, 1); }

        /// <summary>For the probe: forget what was found; <paramref name="ready"/> = as after LaunchBox's start.</summary>
        internal static void ResetForTests(bool ready)
        {
            Known.Clear(); Taken.Clear(); Said.Clear();
            _initialized = ready ? 1 : 0; _started = ready ? 1 : 0;
            lock (SettleGate) { _lastCount = -1; _countSince = 0; }
        }

        /// <summary>For the probe: loaded, LaunchBox's start not complete, the settling clock reset.</summary>
        internal static void InitializedOnlyForTests() { Known.Clear(); AskedByLaunchBoxForTests = false; _initialized = 1; _started = 0; lock (SettleGate) { _lastCount = -1; _countSince = 0; } }

        /// <summary>The catch-all may take emulators now - under LaunchBox, its switch on, LaunchBox loaded it.</summary>
        public static bool Active(out string? why)
        {
            why = null;
            if (!LbIntegrations.Saves.SaveRouter.LaunchBoxHost) { why = "not LaunchBox (LiteBox asks every plugin itself)"; return false; }
            if (!LbIntegrations.Saves.SaveRouter.Active) { why = "the saves desk is off"; return false; }
            if (!Compat.Compat.On(Switch)) { why = "its switch is off"; return false; }
            if (Volatile.Read(ref _initialized) == 0) { why = "LaunchBox has not loaded it yet"; return false; }
            return true;
        }

        /// <summary>The registry's plugins once LaunchBox has loaded them all (see the header), else null and why.</summary>
        private static IReadOnlyList<EmulatorPlugin>? Settled(out string? why)
        {
            var all = LbPluginRegistry.EmulatorPlugins(out why);
            if (all == null) return null;
            if (Volatile.Read(ref _started) == 1) return all;
            // LaunchBox asking through its own choice of a plugin: its plugins are loaded - for good (measured 13/10: its
            // question whether the catch-all supports saves came 2 ms after, not through GetBestEmulatorPlugin, and the
            // settling made it answer no for the session).
            if (AskedByLaunchBox()) { Started(); return all; }
            var now = System.Diagnostics.Stopwatch.GetTimestamp();
            lock (SettleGate)
            {
                if (all.Count != _lastCount) { _lastCount = all.Count; _countSince = now; }
                if (now - _countSince < System.Diagnostics.Stopwatch.Frequency * SettleSeconds)
                { why = "LaunchBox may still be loading plugins (" + all.Count + " so far)"; return null; }
            }
            return all;
        }

        /// <summary>For the probe: the answer of <see cref="AskedByLaunchBox"/>, instead of the call stack.</summary>
        internal static bool? AskedByLaunchBoxForTests;

        /// <summary>The question comes from LaunchBox choosing an emulator's plugin (PluginRegistry.GetBestEmulatorPlugin is
        /// on the stack - its name is kept, measured with the save probe), not from the pack's own code.</summary>
        private static bool AskedByLaunchBox()
        {
            if (AskedByLaunchBoxForTests is bool b) return b;
            try
            {
                foreach (var f in new System.Diagnostics.StackTrace(2, false).GetFrames() ?? Array.Empty<System.Diagnostics.StackFrame>())
                {
                    var m = f.GetMethod();
                    if (m != null && m.Name == "GetBestEmulatorPlugin" && m.DeclaringType?.FullName == LbPluginRegistry.TypeName) return true;
                }
            }
            catch { }
            return false;
        }

        /// <summary>True when the catch-all takes <paramref name="emulator"/> - the rules of the header.</summary>
        public static bool Claims(EmulatorPlugin self, IEmulator? emulator)
        {
            if (emulator == null || Asking.Value > 0) return false;
            if (!Active(out _)) return false;
            string path = Safe(() => emulator.ApplicationPath) ?? "";
            if (path.Trim().Length == 0) return false;
            var key = (Safe(() => emulator.Id) ?? "") + "|" + path;
            var exe = (Path.GetFileName(path.Trim().Trim('"')) ?? "").ToLowerInvariant();
            var name = Names.FirstOrDefault(n => exe.StartsWith(n, StringComparison.Ordinal));
            if (name != null) return Note(key, emulator, path, false, "an exe the pack or LaunchBox has a plugin for (" + name + ")", -1);
            var all = Settled(out var unsettled);
            // Not settled or unreadable: nothing taken, and nothing kept - asked again next time.
            if (all == null) { Say(key, emulator, path, false, unsettled ?? "LaunchBox's plugin registry unreadable"); return false; }
            var now = System.Diagnostics.Stopwatch.GetTimestamp();
            if (Known.TryGetValue(key, out var k) && k.Plugins == all.Count && now - k.At < System.Diagnostics.Stopwatch.Frequency * KeepSeconds) return k.Yes;
            bool yes = Decide(self, emulator, all, out var why);
            return Note(key, emulator, path, yes, why, all.Count);
        }

        private static bool Note(string key, IEmulator emulator, string path, bool yes, string why, int plugins)
        {
            Known[key] = (System.Diagnostics.Stopwatch.GetTimestamp(), plugins, yes);
            if (yes) Taken[key] = emulator; else Taken.TryRemove(key, out _);
            Say(key, emulator, path, yes, why);
            return yes;
        }

        private static void Say(string key, IEmulator emulator, string path, bool yes, string why)
        {
            if (Said.TryAdd(key + "|" + yes + "|" + why, 0))
                Log.Info((yes ? "takes " : "leaves ") + "\"" + Safe(() => emulator.Title) + "\" (" + Path.GetFileName(path) + "): " + why);
        }

        /// <summary>A platform of an emulator taken so far - the only platforms it says it supports (IsPlatformSupported):
        /// "supported" everywhere could have LaunchBox offer Nixx-Saves where no plugin is recommended (review 13/10).</summary>
        public static bool CoversPlatform(string? platform)
        {
            if (string.IsNullOrEmpty(platform)) return false;
            foreach (var e in Taken.Values)
                foreach (var p in Safe(() => e.GetAllEmulatorPlatforms()) ?? Array.Empty<IEmulatorPlatform>())
                    if (string.Equals(Safe(() => p?.Platform), platform, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        private static bool Decide(EmulatorPlugin self, IEmulator emulator, IReadOnlyList<EmulatorPlugin> all, out string why)
        {
            Asking.Value++;
            try
            {
                foreach (var p in all)
                {
                    if (p == null || ReferenceEquals(p, self) || p.GetType().Assembly == typeof(CatchAllClaims).Assembly) continue;
                    try
                    {
                        var taken = p.GetApplicableEmulators(new[] { emulator });
                        if (taken != null && taken.Any(x => ReferenceEquals(x, emulator) || (x != null && string.Equals(Safe(() => x.Id), Safe(() => emulator.Id), StringComparison.OrdinalIgnoreCase))))
                        { why = "driven by " + PluginName(p); return false; }
                    }
                    catch (Exception ex) { why = PluginName(p) + " threw when asked (" + ex.GetType().Name + ") - it may take it"; return false; }
                }
            }
            finally { Asking.Value--; }
            why = "no other plugin takes it (" + all.Count + " asked)";
            return true;
        }

        private static string PluginName(EmulatorPlugin p)
        {
            string n = "";
            try { n = p.EmulatorName ?? ""; } catch { }
            return (n.Length > 0 ? n : p.GetType().Name) + " [" + p.GetType().Name + "]";
        }

        private static T? Safe<T>(Func<T> f) { try { return f(); } catch { return default; } }
    }
}
