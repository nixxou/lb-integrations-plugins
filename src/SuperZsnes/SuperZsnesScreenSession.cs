// A game's Window options, kept to its session (Mehdi, 01/10).
//
// -screen-fullscreen, -screen-width, -screen-height, -popupwindow and -monitor are UNITY's switches, not the
// in-process plugin's: the player applies them before any plugin loads, and writes the screen it ran on into
// HKCU\Software\ZEMU Software Inc.\SUPERZSNES ("Screenmanager ...", "UnitySelectMonitor") as it quits - measured
// 01/10: a test launch in a 1280 x 720 window left it there for the next ones. That key is the Windows user's,
// shared by every SUPER ZSNES and every game. So, like the pack's other set-aside-and-put-back sessions:
//
//   1. at a launch that ADDS one of those switches (or the primary-display one, which moves the window), the
//      screen values of the key are written down first - <plugin data>\screen-session.txt, its session named;
//   2. once SUPER ZSNES has quit (+1 s), its watcher puts them back as they were: every value written down set
//      again, every screen value the session added deleted. The watcher holds the launch gate meanwhile;
//   3. a note still there - the host or the machine went mid-session - is put back before anything else: at the
//      next such launch, at the host's start-up, and when SUPER ZSNES is opened on its own (LbEmulatorOpened).
//      Never while SUPERZSNES.exe runs: the screen is its own then.
//
// Only the screen values are touched: the unity.* session counters beside them are not ours.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.Win32;

namespace LbIntegrations.SuperZsnes
{
    internal static class SuperZsnesScreenSession
    {
        private const string Key = @"Software\ZEMU Software Inc.\SUPERZSNES";

        /// <summary>For the probe: another key under HKCU. Set by reflection.</summary>
#pragma warning disable CS0649
        internal static string KeyOverride;
#pragma warning restore CS0649

        private static string KeyPath => KeyOverride ?? Key;
        private static string NotePath => Path.Combine(Path.GetDirectoryName(SuperZsnesSettings.SettingsPath), "screen-session.txt");

        /// <summary>One Begin or Restore at a time; nothing inside waits on anything but the registry and one small file.</summary>
        private static readonly object Gate = new object();

        /// <summary>The id of the session the last Begin started (null: none) - for its watcher.</summary>
        internal static string LastId;

        private static bool IsScreenValue(string name)
            => name.StartsWith("Screenmanager ", StringComparison.Ordinal) || name.StartsWith("UnitySelectMonitor", StringComparison.Ordinal);

        /// <summary>A launch flag that changes what Unity writes there.</summary>
        internal static bool IsScreenFlag(Flag f)
            => f.Name.StartsWith("-screen-", StringComparison.Ordinal) || f.Name == "-popupwindow" || f.Name == "-monitor" || f.Name == "--nixx-display=";

        public static bool Pending => File.Exists(NotePath);

        internal static bool Running()
        {
            try { return Process.GetProcessesByName(SuperZsnesPaths.ProcessName).Length > 0; }
            catch { return false; }
        }

        /// <summary>The screen values written down for this session. False when they could not be (the game then runs
        /// all the same; its Window options may stay). Never throws.</summary>
        public static bool Begin()
        {
            lock (Gate)
            {
                LastId = null;
                try
                {
                    Restore("left behind by a session that did not end");
                    if (Pending) { Log.Warn("screen: a previous session's screen values could not be put back yet - this one's are not written down"); return false; }
                    var id = Guid.NewGuid().ToString("N");
                    var note = new StringBuilder("session\t" + id + "\r\n");
                    int n = 0;
                    using (var key = Registry.CurrentUser.OpenSubKey(KeyPath))
                        if (key != null)
                            foreach (var name in key.GetValueNames().Where(IsScreenValue))
                                if (key.GetValueKind(name) == RegistryValueKind.DWord)
                                {
                                    note.Append("dword\t").Append(name).Append('\t').Append((int)key.GetValue(name)).Append("\r\n");
                                    n++;
                                }
                    Directory.CreateDirectory(Path.GetDirectoryName(NotePath));
                    var tmp = NotePath + ".tmp";
                    File.WriteAllText(tmp, note.ToString(), new UTF8Encoding(false));
                    File.Move(tmp, NotePath, overwrite: true);
                    LastId = id;
                    Log.Info("screen: " + n + " screen value(s) of the registry written down - put back once SUPER ZSNES has quit");
                    return true;
                }
                catch (Exception ex) { Log.Warn("screen: could not write the screen values down", ex); return false; }
            }
        }

        /// <summary>Put the screen values back as a note has them - only <paramref name="session"/>'s when given (a watcher).
        /// Never while SUPER ZSNES runs; never throws.</summary>
        public static void Restore(string why, string session = null)
        {
            lock (Gate)
            {
                try
                {
                    if (!Pending) return;
                    if (Running()) { Log.Info("screen: SUPER ZSNES is running - its screen values go back once it has quit (" + why + ")"); return; }
                    var lines = File.ReadAllLines(NotePath);
                    if (session != null && (lines.FirstOrDefault() ?? "") != "session\t" + session)
                    { Log.Info("screen: the session on is a later launch's - left to it (" + why + ")"); return; }
                    var saved = new Dictionary<string, int>(StringComparer.Ordinal);
                    foreach (var l in lines)
                    {
                        var f = l.Split('\t');
                        if (f.Length == 3 && f[0] == "dword" && int.TryParse(f[2], out var v)) saved[f[1]] = v;
                    }
                    int changed = 0;
                    using (var key = Registry.CurrentUser.CreateSubKey(KeyPath, writable: true))
                    {
                        foreach (var name in key.GetValueNames().Where(IsScreenValue).ToList())
                            if (!saved.ContainsKey(name)) { key.DeleteValue(name, false); changed++; }
                        foreach (var kv in saved)
                        {
                            var now = key.GetValue(kv.Key);
                            if (now is int i && i == kv.Value && key.GetValueKind(kv.Key) == RegistryValueKind.DWord) continue;
                            key.SetValue(kv.Key, kv.Value, RegistryValueKind.DWord);
                            changed++;
                        }
                    }
                    File.Delete(NotePath);
                    Log.Info("screen: the registry's screen values back as they were (" + why + ") - " + (changed == 0 ? "nothing had changed" : changed + " value(s) put back"));
                }
                catch (Exception ex) { Log.Warn("screen: could not put the screen values back (" + why + ") - kept to try again", ex); }
            }
        }

        /// <summary>Wait for the SUPER ZSNES of this launch to come and go, then put its screen values back. Holds the
        /// launch gate until done.</summary>
        public static void WhenDone(string session)
        {
            if (session == null) return;
            var hold = LbIntegrations.Lbip.LbipLaunchGate.HoldOpen();
            System.Threading.Tasks.Task.Run(() =>
            {
                using var held = hold;
                try
                {
                    var armed = DateTime.UtcNow;
                    bool appeared = false;
                    while ((DateTime.UtcNow - armed).TotalSeconds < 120)
                    {
                        if (Running()) { appeared = true; break; }
                        System.Threading.Thread.Sleep(500);
                    }
                    // Whether it came or not, never put back while one runs.
                    while (Running()) { appeared = true; System.Threading.Thread.Sleep(500); }
                    System.Threading.Thread.Sleep(1000);   // Unity writes the key as it quits
                    while (Running()) { appeared = true; System.Threading.Thread.Sleep(500); }
                    Restore(appeared ? "the session is over" : "SUPER ZSNES never started", session);
                }
                catch (Exception ex) { Log.Warn("screen: watching for the end of the session", ex); }
            });
        }
    }

    /// <summary>SUPER ZSNES opened without a game by the host ("Open emulator"): a session's screen values left behind
    /// go back first - see LbEmulatorOpened in src\Catalog\LbCatalog.cs.</summary>
    internal sealed class SuperZsnesEmulatorOpened : LbIntegrations.Catalog.ILbEmulatorOpened
    {
        public void BeforeOpen(string exePath)
        {
            try
            {
                if (SuperZsnesPaths.IsSuperZsnesExecutable(exePath)) SuperZsnesScreenSession.Restore("SUPER ZSNES is opened on its own");
            }
            catch (Exception ex) { Log.Warn("SUPER ZSNES opened on its own: could not put things right first", ex); }
        }

        public void AfterExit(string exePath)
        {
            if (SuperZsnesPaths.IsSuperZsnesExecutable(exePath)) Log.Info("SUPER ZSNES, opened on its own, has quit");
        }
    }
}
