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
            try
            {
                string id;
                try { id = emu.Id ?? ""; } catch { return; }
                lock (Gate)
                {
                    if (id.Length > 0 && !Done.Add(id)) return;
                }

                var existing = emu.GetAllEmulatorPlatforms() ?? Array.Empty<IEmulatorPlatform>();
                var have = new HashSet<string>(
                    existing.Select(p => { try { return p.Platform ?? ""; } catch { return ""; } }),
                    StringComparer.InvariantCultureIgnoreCase);

                // Does the entry already point somewhere deliberate? If the user attached platforms of
                // their own - even ones we do not cover - they made a choice, and adding to it is
                // helping; REPLACING it would not be.
                bool hadAny = have.Any(p => p.Length > 0);
                var added = new List<string>();

                foreach (var name in FlycastPlatforms.All)
                {
                    if (have.Contains(name)) continue;
                    var platform = emu.AddNewEmulatorPlatform();
                    if (platform == null) continue;
                    platform.Platform = name;
                    // Only claim the default slot when nothing held it: an entry the user pointed at
                    // Naomi on purpose should not silently become a Dreamcast entry.
                    platform.IsDefault = !hadAny
                                         && string.Equals(name, FlycastPlatforms.Dreamcast, StringComparison.Ordinal);
                    added.Add(name);
                }

                if (added.Count == 0) return;

                // A blank command line is worth filling once, for the same reason: it is a default the
                // user never chose, not a decision.
                try
                {
                    if (string.IsNullOrWhiteSpace(emu.CommandLine))
                        emu.CommandLine = FlycastDefaults.CommandLine;
                }
                catch { }

                try
                {
                    if (!hadAny && string.IsNullOrWhiteSpace(emu.DefaultPlatform))
                        emu.DefaultPlatform = FlycastPlatforms.Dreamcast;
                }
                catch { }

                Log.Info("completed \"" + Safe(() => emu.Title) + "\" with " + added.Count
                         + " platform(s) in memory: " + string.Join(", ", added)
                         + " (not saved - LaunchBox's metadata does not know Flycast)");
            }
            catch (Exception ex) { Log.Warn("could not complete the emulator's platforms", ex); }
        }

        private static string Safe(Func<string> f)
        {
            try { return f() ?? ""; } catch { return ""; }
        }
    }
}
