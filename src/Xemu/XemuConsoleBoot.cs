// xemu started WITHOUT A DISC (Mehdi, 05/10).
//
// ON A GAME'S CONSOLE, from its menu ("Nixx-Xemu : Options..."): hdd\games\<title id>.qcow2, its dashboard - to clear a cache,
// see or delete its saves. The same session as a launch of the game (XemuPlugin.MakeSession: its save into its console, its
// options, its region, the keys of its save) but no disc; its end the same (XemuSession.Watch: the user's xemu.toml merged
// back, the console captured into the save). Not "the last game's" when xemu is opened on its own: from the game's menu, one
// knows whose console it is.
//
// ON ITS OWN (LaunchBox's "Open emulator", the Nixx window's Open button): no argument can be given, so for the time it runs
// xemu.toml ITSELF is the session's - the user's kept aside as xemu-user.toml, every game's options and your console's
// EEPROM set over it (XemuSessionConfig.Make) - and after it, merged back by the same rule as a game's (MergeBack) and put in
// place again. Its console stays hdd\standalone.qcow2, no game's. Left behind (the host killed), put right at the next start,
// open or launch.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using LbIntegrations.Catalog;
using Unbroken.LaunchBox.Plugins.Data;

namespace LbIntegrations.Xemu
{
    internal static class XemuConsoleBoot
    {
        /// <summary>xemu started on <paramref name="game"/>'s console, no disc. Null when started, else why not.</summary>
        public static string Boot(IGame game)
        {
            try
            {
                var exe = XemuLibrary.For(game);
                if (exe == null) return "there is no xemu of this plugin in the library";
                if (XemuPaths.Running(exe)) return "xemu is already running - close it first";
                if (XemuPaths.McpxPath(exe) == null || XemuPaths.FlashPath(exe) == null) return "the Xbox's BIOS is not in " + XemuPaths.BiosDir(exe);
                if (!File.Exists(XemuPaths.BaseHdd(exe))) return "xemu's console disk is missing - update xemu from LaunchBox";
                var rom = XemuPlugin.ResolveFullPath(XemuPlugin.Safe(() => game.ApplicationPath));
                string why = "it has no file";
                var xbe = rom == null ? null : XemuDisc.XbeForConsole(rom, exe, out why);
                if (xbe == null) return "the game's title id could not be read: " + why;
                var titleId = xbe.TitleIdText;

                RestoreOwn(exe, "before a game's console");
                try { if (XemuSessionConfig.MergeBack(XemuPaths.TomlOf(exe), XemuSessionConfig.SessionPath(exe))) Log.Info("xemu.toml: a session left behind merged back"); }
                catch (Exception ex) { Log.Warn("xemu.toml: a session left behind could not be merged back", ex); }

                var options = XemuOptions.Effective(XemuPlugin.Safe(() => game.Id));
                var problem = XemuPlugin.MakeSession(exe, titleId, xbe, options, null, out var hdd, out _);
                if (problem != null) return problem;
                var session = XemuSessionConfig.SessionPath(exe);
                Log.Info("console boot: " + titleId + " \"" + xbe.TitleName + "\" on " + Path.GetFileName(hdd) + ", no disc");
                var p = Process.Start(new ProcessStartInfo(exe, "-config_path \"" + session + "\"") { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(exe) });
                if (p == null) return "xemu did not start";
                XemuSession.Watch(exe, hdd, null);
                return null;
            }
            catch (Exception ex) { Log.Warn("console boot", ex); return ex.Message; }
        }

        // ── xemu on its own ──────────────────────────────────────────────────

        private static string UserCopy(string exe) => XemuPaths.Dir(exe) is string d ? Path.Combine(d, "xemu-user.toml") : null;

        /// <summary>Before xemu opens on its own: xemu.toml made the session's - every game's options, your console's EEPROM.</summary>
        public static void BeforeOwn(string exe)
        {
            try
            {
                if (!XemuPaths.IsOurs(exe)) return;
                RestoreOwn(exe, "a session on its own left behind");
                try { XemuSessionConfig.MergeBack(XemuPaths.TomlOf(exe), XemuSessionConfig.SessionPath(exe)); } catch { }
                XemuSession.Standalone(exe, "opened on its own");
                var toml = XemuPaths.TomlOf(exe);
                var user = UserCopy(exe);
                if (toml == null || !File.Exists(toml)) return;
                var options = XemuOptions.Effective(null);
                var said = new List<string>();
                string eeprom = null;
                try { eeprom = Eeprom.XemuEeprom.Prepare(XemuPaths.Eeprom(exe), XemuPaths.SessionEeprom(exe), null, options, said); }
                catch (Exception ex) { Log.Warn("on its own: the console's settings could not be made", ex); }
                var set = new List<(string, string, string)> { ("general", "show_welcome", "false") };
                if (eeprom != null) set.Add(("sys.files", "eeprom_path", XemuToml.Literal(eeprom)));
                set.AddRange(XemuOptions.TomlOf(options));
                File.Copy(toml, user, overwrite: true);
                XemuSessionConfig.Make(user, toml, set);
                Log.Info("on its own: xemu.toml set for the session (every game's options" + (said.Count > 0 ? ", " + string.Join(", ", said) : "") + "), yours kept aside");
            }
            catch (Exception ex) { Log.Warn("on its own: xemu.toml could not be set", ex); }
        }

        /// <summary>After it - or a session on its own left behind: what xemu wrote merged back into the user's xemu.toml, put in place.</summary>
        public static void RestoreOwn(string exe, string why)
        {
            try
            {
                var user = UserCopy(exe);
                var toml = XemuPaths.TomlOf(exe);
                if (user == null || !File.Exists(user) || XemuPaths.Running(exe)) return;
                if (File.Exists(toml)) XemuSessionConfig.MergeBack(user, toml);       // the session (xemu.toml) into the user's copy, then removed
                File.Move(user, toml, overwrite: true);
                XemuSession.Standalone(exe, why);
                Log.Info("on its own: your xemu.toml back (" + why + ")");
            }
            catch (Exception ex) { Log.Warn("on its own: xemu.toml could not be put back", ex); }
        }
    }

    /// <summary>LaunchBox's "Open emulator" and the Nixx window's Open button, for an xemu of this plugin.</summary>
    internal sealed class XemuEmulatorOpened : ILbEmulatorOpened
    {
        public void BeforeOpen(string exePath) { if (XemuPaths.IsOurs(exePath)) XemuConsoleBoot.BeforeOwn(exePath); }

        public void AfterExit(string exePath)
        {
            if (!XemuPaths.IsOurs(exePath)) return;
            // The process may be gone before its last write: a moment, then put back.
            System.Threading.Thread.Sleep(1500);
            XemuConsoleBoot.RestoreOwn(exePath, "opened on its own, now closed");
        }
    }
}
