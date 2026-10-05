// An emulator of a plugin opened WITHOUT A GAME from the Nixx window (Mehdi, 05/10) - the same opening as LaunchBox's "Open
// emulator" menu, with what the plugins do around it: the executable to open in its place (LbEmulatorRedirect - Cxbx-Reloaded's
// window for its loader), the plugins told before it starts and after it has quit (LbEmulatorOpened - a session left behind
// put right, a RAM disk freed...). LbipEmulatorOpened sees only the menu's start: this one calls the same contract itself, and
// its Process.Start is not taken for the menu's (no OpenEmulatorMenuAction on the stack), so nothing is told twice.
//
// The relay (src\Menus\Settings.cs) finds it through a plugin's Settings class: string[] Emulators(), "<title>\t<full path>"
// for each of its emulators LaunchBox has; string OpenEmulator(string path), null when opened, else why not.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using LbIntegrations.Catalog;
using Unbroken.LaunchBox.Plugins;
using Unbroken.LaunchBox.Plugins.Data;

namespace LbIntegrations.Lbip
{
    internal static class LbipOpenEmulator
    {
        /// <summary>LaunchBox's root (its Core\ holds the host), for an entry's relative path.</summary>
        private static string Root()
        {
            try { var core = Path.GetDirectoryName(Environment.ProcessPath); return core == null ? null : Path.GetDirectoryName(core); }
            catch { return null; }
        }

        private static string Full(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return null;
            try { return Path.IsPathRooted(path) ? Path.GetFullPath(path) : Root() is string r ? Path.GetFullPath(Path.Combine(r, path)) : null; }
            catch { return null; }
        }

        /// <summary>The emulator entries whose executable <paramref name="isOurs"/> claims: "<title>\t<full path>".</summary>
        public static string[] Find(Func<string, bool> isOurs)
        {
            var found = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                foreach (var e in PluginHelper.DataManager?.GetAllEmulators() ?? new IEmulator[0])
                {
                    string title = null, path = null;
                    try { title = e?.Title; path = Full(e?.ApplicationPath); } catch { }
                    if (path == null || !File.Exists(path) || !isOurs(path) || !seen.Add(path)) continue;
                    found.Add((string.IsNullOrWhiteSpace(title) ? Path.GetFileNameWithoutExtension(path) : title) + "\t" + path);
                }
            }
            catch (Exception ex) { LbipLog.Warn("could not list the emulators", ex); }
            return found.ToArray();
        }

        /// <summary>The emulator at <paramref name="exe"/> opened as LaunchBox's menu opens it. Null when it is, else why not.</summary>
        public static string Open(string exe)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(exe) || !File.Exists(exe)) return "the emulator is not there any more: " + exe;
                var target = exe;
                try { if (LbEmulatorRedirect.Redirect(exe) is string to && File.Exists(to)) { target = to; LbipLog.Info("opened in place of " + exe + ": " + to); } }
                catch { }
                LbipLog.Info("an emulator is opened without a game, from the Nixx window: " + target);
                try { LbEmulatorOpened.Opening(target); } catch { }
                var process = Process.Start(new ProcessStartInfo(target) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(target) });
                if (process == null) return "it did not start";
                System.Threading.Tasks.Task.Run(() =>
                {
                    try { process.WaitForExit(); } catch { }
                    try { LbEmulatorOpened.Exited(target); } catch { }
                });
                return null;
            }
            catch (Exception ex) { LbipLog.Warn("could not open " + exe, ex); return ex.Message; }
        }
    }
}
