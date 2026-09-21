// Completing a Flycast emulator entry the user made by hand.
//
// THE PROBLEM, in the user's words: once Flycast is typed in rather than picked from the list, it
// looks like it covers every platform. LaunchBox's Add Emulator name list comes from its metadata
// database, and Flycast is not in it - 35 rows, measured, with Demul, nullDC and Redream present
// and all three dead for years. No metadata row means no `EmulatorPlatforms` rows, and nothing to
// narrow the Associated Platforms page with.
//
// THE CHEAP FIX, and the reason it is preferred to the obvious one. We know the four platforms
// already: IsPlatformSupported answers them, and InstallEmulator attaches them when it creates the
// entry itself. What was missing is the case where the user created the emulator by hand. So when
// this plugin claims an emulator, it completes it - through the public SDK, on the user's own
// emulator entry, and nowhere else.
//
// The alternative was to INSERT a row into LaunchBox.Metadata.db. That would also put "Flycast" in
// the Add Emulator dropdown, which this does not. It was dropped because it writes into a 393 MB
// data store that belongs to Unbroken and is refreshed from their servers daily - measured at 10:01
// two days running - so the row would have to be rewritten at every load, forever, and a user
// seeing Flycast in that list would reasonably conclude LaunchBox supports it. This does none of
// that, and fixes the thing that was actually wrong.
//
// NOTHING IS SAVED. The additions live in the host's object graph for the session; no call to
// IDataManager.Save is made here. The emulator entry is the user's, and silently rewriting their
// Emulators.xml because they opened a page is not ours to do. We are loaded at every start, so the
// association is there whenever we are - and if the host persists for its own reasons, the result
// is the association the user would have chosen anyway.

using System;
using System.Collections.Generic;
using System.Linq;
using Unbroken.LaunchBox.Plugins;
using Unbroken.LaunchBox.Plugins.Data;

namespace LbIntegrations.Flycast
{
    /// <summary>The launch defaults, in one place so the plugin and any registration agree.</summary>
    internal static class FlycastDefaults
    {
        /// <summary>Fullscreen, transiently. `-config section:key=value` is Flycast's documented way to
        /// set a value for one run (core/cfg/cl.cpp), and transient is what we want: Flycast rewrites
        /// emu.cfg on exit, window geometry included (core/sdl/sdl.cpp), so a persisted setting would
        /// fight the user's own window every time they close it.</summary>
        public const string CommandLine = "-config window:fullscreen=yes";
    }

    internal static class FlycastAssociation
    {
        /// <summary>Emulator ids already completed, so this costs nothing after the first call. The
        /// host asks which emulators we claim often; the work must happen once.</summary>
        private static readonly HashSet<string> Done = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>What each Flycast executable's keyboard mapping says, so the file is read once.
        ///
        /// The CACHE IS THE TABLE, never the fact of having run: an earlier version remembered which
        /// executables it had handled and skipped them, which meant the FIRST object carrying a given
        /// Flycast got the scripts and every later one did not - and the later one is exactly the
        /// object the Add Emulator window is showing. Measured: the log said the scripts were set,
        /// and the window stayed empty.</summary>
        private static readonly Dictionary<string, object> HotkeyTables =
            new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        private static readonly object Gate = new object();

        /// <summary>Complete every Flycast emulator the library holds, without waiting to be asked.
        ///
        /// EnsurePlatforms alone only runs when the host asks which emulators we claim, which can be
        /// AFTER a page has already drawn its platform list - the user then sees the wrong thing once
        /// and has to reopen it. Both hosts announce when they are up, so we do it then instead:
        /// LaunchBox raises LaunchBoxStartupCompleted, LiteBox raises PluginInitialized when its
        /// window is shown. Idempotent, so being called from both costs nothing.</summary>
        public static void SweepAll()
        {
            try
            {
                var dm = PluginHelper.DataManager;
                if (dm == null)
                {
                    // Before the host has a data manager there is nothing to walk. Not an error: the
                    // per-emulator path still catches it later.
                    Log.Info("no data manager yet - platforms will be completed when an emulator is claimed");
                    return;
                }

                var emulators = dm.GetAllEmulators() ?? Array.Empty<IEmulator>();
                int seen = 0;
                foreach (var emu in emulators)
                {
                    string path;
                    try { path = emu?.ApplicationPath; } catch { continue; }
                    if (!FlycastPaths.IsFlycastExecutable(path)) continue;
                    seen++;
                    EnsurePlatforms(emu);
                }
                Log.Info("startup sweep: " + seen + " Flycast emulator(s) in the library");
            }
            catch (Exception ex) { Log.Warn("startup sweep failed", ex); }
        }

        /// <summary>Give this emulator the platforms Flycast actually covers, if it has none of them.
        /// Never throws: a plugin that cannot complete an association must still claim the emulator.</summary>
        public static void EnsurePlatforms(IEmulator emu)
        {
            if (emu == null) return;

            WarnAboutUnknownPlatforms(emu);

            var call = NextCall();
            var before = Dump(emu, call, "in ");
            ClearOurDefaultPlatform(emu);

            // OUTSIDE the platform work below, which has three early returns - a row being edited,
            // an id already seen, and above all "every platform we cover is already there", the
            // normal case. The hotkey scripts have nothing to do with any of them; hung off that
            // method's tail, they were almost never set.
            EnsureHotkeyScripts(emu);

            try
            {
                string id;
                try { id = emu.Id ?? ""; } catch { return; }
                lock (Gate)
                {
                    if (id.Length > 0 && !Done.Add(id)) return;
                }

                IEmulatorPlatform[] existing = emu.GetAllEmulatorPlatforms() ?? Array.Empty<IEmulatorPlatform>();

                // A row with no name yet means someone is typing in the grid. Adding anything beside
                // it produced a duplicate Sega Dreamcast once already; wait until they are done.
                if (existing.Any(p => string.IsNullOrWhiteSpace(Safe(() => p.Platform))))
                {
                    Log.Info("\"" + Safe(() => emu.Title) + "\" has a row being edited - leaving it alone");
                    return;
                }

                var mine = new HashSet<string>(FlycastPlatforms.All, StringComparer.InvariantCultureIgnoreCase);
                var have = new HashSet<string>(
                    existing.Select(p => (Safe(() => p.Platform) ?? "").Trim()).Where(n => n.Length > 0),
                    StringComparer.InvariantCultureIgnoreCase);

                // UNCHECK what is not ours, never remove it.
                //
                // When LaunchBox does not know an emulator it associates it with everything, every row
                // marked "Default Emulator" - and that column means "this emulator is the DEFAULT for
                // this platform", so Flycast ends up the default for the SNES and everything else. That
                // is the mess. Clearing the flag fixes it and destroys nothing: the association stays,
                // the user keeps whatever they set up, and a wrong guess on our side costs one tick
                // rather than a row they cannot get back.
                var unchecked_ = new List<string>();
                foreach (var row in existing)
                {
                    var name = (Safe(() => row.Platform) ?? "").Trim();
                    if (name.Length == 0 || mine.Contains(name)) continue;
                    bool isDefault = false;
                    try { isDefault = row.IsDefault; } catch { }
                    if (!isDefault) continue;
                    try { row.IsDefault = false; unchecked_.Add(name); } catch { }
                }

                var added = new List<string>();
                foreach (var name in FlycastPlatforms.All)
                {
                    if (have.Contains(name)) continue;
                    var platform = emu.AddNewEmulatorPlatform();
                    if (platform == null) continue;
                    platform.Platform = name;
                    // See FlycastPlugin.EnsureEmulator: IsDefault is per PLATFORM, not per emulator.
                    platform.IsDefault = true;
                    added.Add(name);
                }

                if (unchecked_.Count > 0)
                    Log.Info("\"" + Safe(() => emu.Title) + "\": cleared \"default emulator\" on "
                             + unchecked_.Count + " platform(s) Flycast does not run: "
                             + string.Join(", ", unchecked_.Take(8))
                             + (unchecked_.Count > 8 ? ", ..." : ""));

                // The unchecking above already happened and is logged; nothing more to do when
                // every platform we cover was already there.
                if (added.Count == 0) return;


                // A blank command line is worth filling once, for the same reason: it is a default the
                // user never chose, not a decision.
                try
                {
                    if (string.IsNullOrWhiteSpace(emu.CommandLine))
                        emu.CommandLine = FlycastDefaults.CommandLine;
                }
                catch { }


                Log.Info("completed \"" + Safe(() => emu.Title) + "\" with " + added.Count
                         + " platform(s) in memory: " + string.Join(", ", added)
                         + " (not saved - LaunchBox's metadata does not know Flycast)");
            }
            catch (Exception ex) { Log.Warn("could not complete the emulator's platforms", ex); }
            finally
            {
                // LAST, whichever way we left: the point of the pair is to show what WE changed, and
                // an "out" taken before the unticking and the adding showed nothing of the kind.
                var after = Dump(emu, call, "out");
                if (after == before) Log.Verbose("call #" + call + " out - we changed nothing");
            }
        }

        /// <summary>Undo the DefaultPlatform earlier versions of this plugin wrote - see
        /// FlycastPlugin.EnsureEmulator for what it costs.
        ///
        /// ONLY when it is exactly the value we used to write. A user who chose Sega Dreamcast
        /// himself would have chosen the same string, and there is no telling the two apart; the
        /// trade is deliberate, because the field buys nothing visible and costs a phantom row on
        /// every open. Anything else in there is somebody's decision and stays.</summary>
        private static void ClearOurDefaultPlatform(IEmulator emu)
        {
            try
            {
                if (!string.Equals(Safe(() => emu.DefaultPlatform), FlycastPlatforms.Dreamcast,
                                   StringComparison.InvariantCultureIgnoreCase)) return;
                emu.DefaultPlatform = "";
                Log.Info("cleared DefaultPlatform on \"" + Safe(() => emu.Title)
                         + "\" - the edit window turns it into a duplicate platform row");
            }
            catch { }
        }

        /// <summary>Describe Flycast's save-state and exit keys in the emulator's AutoHotkey fields,
        /// which is what LaunchBox's and BigBox's pause screen sends.
        ///
        /// Here rather than in EnsureEmulator because that one only touches a BRAND NEW entry, while
        /// an entry the user made himself deserves these too - and this runs on the startup sweep.
        ///
        /// Only a blank field is filled. A script the user wrote is his answer to the same question,
        /// and ours has no business replacing it.</summary>
        internal static void EnsureHotkeyScripts(IEmulator emu)
        {
            try
            {
                var path = Safe(() => emu.ApplicationPath);
                if (string.IsNullOrWhiteSpace(path)) return;
                if (!FlycastPaths.IsFlycastExecutable(path)) return;

                // Nothing to do only when there is nothing left to fill. This is the idempotence, and
                // it belongs on the OBJECT rather than on the executable: the host hands us the same
                // Flycast under a new object every time a window asks, and each of those objects
                // needs filling in its own right.
                if (!IsBlank(() => emu.SaveStateAutoHotkeyScript)
                    && !IsBlank(() => emu.LoadStateAutoHotkeyScript)
                    && !IsBlank(() => emu.ExitAutoHotkeyScript)) return;

                HotkeyTable table;
                var full = FlycastPlugin.ResolveFullPath(path);
                lock (Gate)
                {
                    // Read-only: here we are only describing what the emulator already does. Cached
                    // because this reads a file and the host asks often.
                    if (!HotkeyTables.TryGetValue(full, out var cached))
                    {
                        cached = FlycastHotkeys.Ensure(FlycastPaths.Resolve(full), mayEditExisting: false);
                        HotkeyTables[full] = cached;
                    }
                    table = (HotkeyTable)cached;
                }

                var set = new List<string>();
                if (Fill(() => emu.SaveStateAutoHotkeyScript,
                         v => emu.SaveStateAutoHotkeyScript = v, FlycastAhk.SaveState(table))) set.Add("save");
                if (Fill(() => emu.LoadStateAutoHotkeyScript,
                         v => emu.LoadStateAutoHotkeyScript = v, FlycastAhk.LoadState(table))) set.Add("load");
                if (Fill(() => emu.ExitAutoHotkeyScript,
                         v => emu.ExitAutoHotkeyScript = v, FlycastAhk.Exit(table))) set.Add("exit");

                if (set.Count > 0)
                    Log.Info("hotkey scripts on \"" + Safe(() => emu.Title) + "\": set "
                             + string.Join(", ", set));
            }
            catch (Exception ex) { Log.Warn("could not describe the hotkeys on the emulator entry", ex); }
        }

        private static bool IsBlank(Func<string> get)
        {
            try { return string.IsNullOrWhiteSpace(get()); } catch { return false; }
        }

        /// <summary>Write the script only into a field the user has left blank. True when it landed.</summary>
        private static bool Fill(Func<string> get, Action<string> set, string value)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(get())) return false;
                set(value);
                return true;
            }
            catch { return false; }
        }

        /// <summary>Every platform row this emulator carries right now, as one line, with a call
        /// number so two successive calls can be compared.
        ///
        /// This is how you tell "the host re-adds a row each time the window opens" apart from "there
        /// are two emulator entries" - the question that cost two wrong guesses. Verbose, so it costs
        /// nothing unless the trace marker is there.</summary>
        /// <summary>Say so when a row names a platform the library does not have.
        ///
        /// Worth its own line because of what it looks like in the grid: a row reading
        /// "Sega Dreamcaxst" is one character from the real thing and renders as an identical-looking
        /// duplicate. Measured - it cost a wrong diagnosis and a feature built on it. A typo cannot
        /// be corrected for the user (the name may be a platform they are about to create), but it
        /// can be named.</summary>
        private static void WarnAboutUnknownPlatforms(IEmulator emu)
        {
            try
            {
                var dm = PluginHelper.DataManager;
                if (dm == null) return;

                var known = new HashSet<string>(
                    (dm.GetAllPlatforms() ?? Array.Empty<IPlatform>()).Select(p => Safe(() => p.Name)),
                    StringComparer.InvariantCultureIgnoreCase);
                if (known.Count == 0) return;

                var seen = new HashSet<string>(StringComparer.InvariantCultureIgnoreCase);
                foreach (var row in emu.GetAllEmulatorPlatforms() ?? Array.Empty<IEmulatorPlatform>())
                {
                    var name = (Safe(() => row.Platform) ?? "").Trim();
                    if (name.Length == 0) continue;

                    // TWICE THE SAME PLATFORM: said, never repaired.
                    //
                    // An earlier version removed the surplus row. It never caught anything on a real
                    // library - the duplicate people actually saw came from DefaultPlatform, which is
                    // no longer written - and a silent repair is the wrong trade here twice over. It
                    // is the only thing in this plugin that would DELETE a row, and it would hide the
                    // very symptom that led to the real cause. A line in the log keeps the diagnosis
                    // and costs nobody a row they cannot get back.
                    if (!seen.Add(name)
                        && FlycastPlatforms.All.Contains(name, StringComparer.InvariantCultureIgnoreCase))
                    {
                        lock (Gate) { if (!UnknownSaid.Add("dup:" + name)) continue; }
                        Log.Warn("\"" + Safe(() => emu.Title) + "\" is associated with \"" + name
                                 + "\" more than once - nothing here removes it, but it is worth"
                                 + " knowing where the second row came from");
                        continue;
                    }

                    if (known.Contains(name)) continue;

                    // A platform WE run is never a typo, whatever the library happens to hold. Not
                    // having a Sega Naomi platform yet is the ordinary state of most libraries, and
                    // saying a name is "one slip away" from itself is nonsense - measured, it is
                    // what this said on Mehdi's install.
                    if (FlycastPlatforms.All.Contains(name, StringComparer.InvariantCultureIgnoreCase))
                        continue;

                    // Only a NEAR MISS of a platform we run. An unknown platform is ordinary - the
                    // user may be about to create it - but a name one or two characters from one of
                    // ours is the confusing one: it renders in the grid as an identical-looking
                    // duplicate row, which is exactly how this was misdiagnosed.
                    var near = FlycastPlatforms.All.FirstOrDefault(ours => Distance(name, ours) <= 2);
                    if (near == null) continue;

                    lock (Gate) { if (!UnknownSaid.Add(name)) continue; }
                    Log.Warn("\"" + Safe(() => emu.Title) + "\" is associated with \"" + name
                             + "\", which this library does not have and is one slip away from \""
                             + near + "\" - in the grid the two rows look identical");
                }
            }
            catch { }
        }

        /// <summary>Levenshtein distance, capped by the caller at 2. Small and iterative because it
        /// runs over a handful of short names and nothing here deserves a dependency.</summary>
        private static int Distance(string a, string b)
        {
            a = (a ?? "").ToLowerInvariant();
            b = (b ?? "").ToLowerInvariant();
            if (Math.Abs(a.Length - b.Length) > 2) return int.MaxValue;

            var previous = new int[b.Length + 1];
            var current = new int[b.Length + 1];
            for (var j = 0; j <= b.Length; j++) previous[j] = j;

            for (var i = 1; i <= a.Length; i++)
            {
                current[0] = i;
                for (var j = 1; j <= b.Length; j++)
                    current[j] = Math.Min(Math.Min(current[j - 1] + 1, previous[j] + 1),
                                          previous[j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1));
                Array.Copy(current, previous, current.Length);
            }
            return previous[b.Length];
        }

        private static readonly HashSet<string> UnknownSaid =
            new HashSet<string>(StringComparer.InvariantCultureIgnoreCase);

        private static int NextCall() { lock (Gate) return ++_dumpCount; }

        private static string Dump(IEmulator emu, int call, string phase)
        {
            try
            {
                var rows = emu.GetAllEmulatorPlatforms() ?? Array.Empty<IEmulatorPlatform>();
                var line = rows.Length + " row(s): " + string.Join(", ", rows.Select(p =>
                {
                    var name = Safe(() => p.Platform);
                    bool def = false;
                    try { def = p.IsDefault; } catch { }
                    return (name.Length == 0 ? "(blank)" : name) + (def ? "*" : "");
                }));

                Log.Verbose("call #" + call + " " + phase + " \"" + Safe(() => emu.Title) + "\" id="
                            + (Safe(() => emu.Id) ?? "?") + " - " + line);
                return line;
            }
            catch { return ""; }
        }

        private static int _dumpCount;

        private static string Safe(Func<string> f)
        {
            try { return f() ?? ""; } catch { return ""; }
        }
    }
}
