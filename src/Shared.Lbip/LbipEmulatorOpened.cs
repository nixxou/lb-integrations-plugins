// The one Harmony patch that tells the plugins an emulator is being opened WITHOUT A GAME - see
// LbEmulatorOpened in src\Catalog\LbCatalog.cs for the contract.
//
// MEASURED (29/09, a trace of every Process.Start in LaunchBox 14): "Open emulator" in LaunchBox's menu
// is Process.Start of the emulator's executable, arguments empty, working directory its folder, called
// from Unbroken.LaunchBox.Windows.Desktop.MenuActions.OpenEmulatorMenuAction.OnSelect. Everything else
// that goes through Process.Start - LaunchBox's 7-Zip backups, Dolphin's DolphinTool, a plugin's own
// launches - is not that, so the filter is THREE conditions, all required: arguments empty, an .exe,
// and that menu action on the stack. The stack is only walked for a start with no arguments.
//
// Patched ONCE per process, by whichever plugin of this pack comes first; LbEmulatorOpened.Patched says
// it is done, and the others only register their listener. The instance Process.Start() is patched:
// the static overloads all end in it.

using System;
using System.Diagnostics;
using System.IO;
using HarmonyLib;
using LbIntegrations.Catalog;

namespace LbIntegrations.Lbip
{
    internal static class LbipEmulatorOpened
    {
        private const string MenuAction = "OpenEmulatorMenuAction";
        private static readonly object Gate = new object();

        /// <summary>Make sure the patch is in - by this plugin when nobody got there first.</summary>
        public static void Install(string pluginId)
        {
            try
            {
                lock (Gate)
                {
                    if (LbEmulatorOpened.Patched) return;
                    var start = typeof(Process).GetMethod("Start", Type.EmptyTypes);
                    var harmony = new Harmony(pluginId + ".emulator-opened");
                    harmony.Patch(start,
                        prefix: new HarmonyMethod(AccessTools.Method(typeof(LbipEmulatorOpened), nameof(Before))),
                        postfix: new HarmonyMethod(AccessTools.Method(typeof(LbipEmulatorOpened), nameof(After))));
                    LbEmulatorOpened.Patched = true;
                    LbipLog.Info("patched Process.Start - an emulator opened without a game is told to the plugins");
                }
            }
            catch (Exception ex) { LbipLog.Warn("could not patch Process.Start - an emulator opened on its own is not seen", ex); }
        }

        private static void Before(Process __instance, ref string __state)
        {
            __state = null;
            try
            {
                var si = __instance?.StartInfo;
                if (si == null || !string.IsNullOrWhiteSpace(si.Arguments) || si.ArgumentList.Count > 0) return;
                var exe = si.FileName;
                if (string.IsNullOrWhiteSpace(exe) || !exe.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) return;
                if (!FromTheMenu()) return;
                exe = Full(exe, si.WorkingDirectory);
                __state = exe;
                LbipLog.Info("an emulator is opened without a game: " + exe);
                LbEmulatorOpened.Opening(exe);
            }
            catch { }
        }

        private static void After(Process __instance, string __state)
        {
            if (__state == null || __instance == null) return;
            var exe = __state;
            var process = __instance;
            System.Threading.Tasks.Task.Run(() =>
            {
                try { process.WaitForExit(); } catch { }
                LbEmulatorOpened.Exited(exe);
            });
        }

        private static bool FromTheMenu()
        {
            foreach (var frame in new StackTrace(2, false).GetFrames() ?? new StackFrame[0])
            {
                var type = frame.GetMethod()?.DeclaringType;
                if (type != null && type.FullName != null && type.FullName.IndexOf(MenuAction, StringComparison.Ordinal) >= 0) return true;
            }
            return false;
        }

        private static string Full(string exe, string dir)
        {
            try { return Path.IsPathRooted(exe) ? Path.GetFullPath(exe) : Path.GetFullPath(Path.Combine(string.IsNullOrEmpty(dir) ? Environment.CurrentDirectory : dir, exe)); }
            catch { return exe; }
        }
    }
}
