// Where Steam keeps 3dSen PC (app 1147940), read off Steam's own files - never by starting Steam, never over the network.
//
// MEASURED 18/10/2026 on this machine's Steam client (docs\3dsen.md section 9; no value of the account is recorded):
//   - Steam's folder: HKCU\Software\Valve\Steam, value SteamPath (forward slashes, lower case) - SteamExe beside it.
//   - Someone logged in: HKCU\Software\Valve\Steam\ActiveProcess, ActiveUser (DWORD, the 32-bit account id, non-zero while
//     a user is logged in) and pid (the steam.exe running, checked alive here). config\loginusers.vdf has NO "MostRecent"
//     key on this client: not relied on.
//   - The libraries: steamapps\libraryfolders.vdf, Valve's KeyValues text ("libraryfolders" { "0" { "path" "C:\\..."
//     "apps" { "<appid>" "<size>" } } }, backslashes doubled, tabs) - the library holding 1147940 lists it under "apps".
//   - The install: <library>\steamapps\appmanifest_1147940.acf, "AppState" { "installdir" "3dSen", "StateFlags" "4" },
//     the game in <library>\steamapps\common\<installdir>\3dSen.exe. StateFlags 4 = fully installed: read on this client's
//     own manifests (four apps installed, all 4); 3dSen itself is not installed here, its installdir "3dSen" is Steam's
//     public app info (api.steamcmd.net, 18/10).
//   - Not installed: steam://install/1147940 asks Steam to install it (Steam's own URL scheme). NOT measured: it opens
//     Steam's window in the user's session; what an account that does not own the game sees is unknown (question Q2).
//
// Every reader here is defensive: a Steam folder half removed, a vdf being rewritten, a registry key missing - each gives
// "not found", never an exception to the host.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.Win32;

namespace LbIntegrations.ThreeDSen
{
    internal static class ThreeDSenSteam
    {
        public const uint AppId = 1147940;
        public const string InstallUrl = "steam://install/1147940";

        /// <summary>For the probe: a Steam folder of fixtures, instead of the registry's.</summary>
        internal static string RootOverride;

        /// <summary>What Steam says about 3dSen, read now.</summary>
        internal sealed class Status
        {
            public string SteamRoot;        // null: no Steam
            public bool LoggedIn;           // a user logged in to a Steam client that runs
            public string Library;          // the library holding 3dSen, or null
            public string InstallDir;       // <library>\steamapps\common\<installdir>, or null
            public string StateFlags;       // the manifest's, as written
            public string Exe;              // 3dSen.exe there, when it is on disk and fully installed
        }

        public static Status Read()
        {
            var s = new Status();
            try
            {
                s.SteamRoot = SteamRoot();
                if (s.SteamRoot == null) return s;
                s.LoggedIn = LoggedIn();
                foreach (var lib in Libraries(s.SteamRoot))
                {
                    var acf = Path.Combine(lib, "steamapps", "appmanifest_" + AppId + ".acf");
                    if (!File.Exists(acf)) continue;
                    var state = Parse(File.ReadAllText(acf, Encoding.UTF8));
                    var app = Child(state, "AppState");
                    var dir = Text(app, "installdir");
                    if (string.IsNullOrWhiteSpace(dir)) continue;
                    s.Library = lib;
                    s.InstallDir = Path.Combine(lib, "steamapps", "common", dir);
                    s.StateFlags = Text(app, "StateFlags");
                    var exe = Path.Combine(s.InstallDir, ThreeDSenLaunch.ExecutableName);
                    // 4 = fully installed (see the header); an update in progress has more bits - the exe is still there to run.
                    if (File.Exists(exe) && int.TryParse(s.StateFlags, out var flags) && (flags & 4) != 0) s.Exe = exe;
                    break;
                }
            }
            catch (Exception ex) { Log.Warn("Steam could not be read", ex); }
            return s;
        }

        /// <summary>Steam's folder: the registry's SteamPath, when it holds steam.exe.</summary>
        public static string SteamRoot()
        {
            if (RootOverride != null) return Directory.Exists(RootOverride) ? RootOverride : null;
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
                var path = key?.GetValue("SteamPath") as string;
                if (string.IsNullOrWhiteSpace(path)) return null;
                path = Path.GetFullPath(path.Replace('/', '\\'));
                return File.Exists(Path.Combine(path, "steam.exe")) ? path : null;
            }
            catch { return null; }
        }

        /// <summary>A Steam client runs (ActiveProcess\pid alive) with a user logged in (ActiveUser non-zero).</summary>
        public static bool LoggedIn()
        {
            if (RootOverride != null) return false;
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam\ActiveProcess");
                if (key == null) return false;
                var user = Convert.ToInt64(key.GetValue("ActiveUser") ?? 0);
                var pid = Convert.ToInt32(key.GetValue("pid") ?? 0);
                if (user == 0 || pid == 0) return false;
                try { using var p = Process.GetProcessById(pid); return !p.HasExited; } catch { return false; }
            }
            catch { return false; }
        }

        /// <summary>Every library folder of libraryfolders.vdf, Steam's own folder first.</summary>
        public static List<string> Libraries(string steamRoot)
        {
            var list = new List<string> { steamRoot };
            try
            {
                var vdf = Path.Combine(steamRoot, "steamapps", "libraryfolders.vdf");
                if (File.Exists(vdf))
                {
                    var root = Child(Parse(File.ReadAllText(vdf, Encoding.UTF8)), "libraryfolders");
                    foreach (var kv in root ?? new Dictionary<string, object>())
                    {
                        var path = Text(kv.Value as Dictionary<string, object>, "path");
                        if (string.IsNullOrWhiteSpace(path)) continue;
                        try { path = Path.GetFullPath(path); } catch { continue; }
                        if (!list.Any(l => string.Equals(l.TrimEnd('\\'), path.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))) list.Add(path);
                    }
                }
            }
            catch (Exception ex) { Log.Warn("libraryfolders.vdf could not be read", ex); }
            return list;
        }

        /// <summary>Asks Steam to install 3dSen (steam://install). False when the URL could not be handed to Windows.</summary>
        public static bool AskToInstall()
        {
            try
            {
                using var p = Process.Start(new ProcessStartInfo(InstallUrl) { UseShellExecute = true });
                Log.Info("asked Steam to install 3dSen (" + InstallUrl + ")");
                return true;
            }
            catch (Exception ex) { Log.Warn("Steam could not be asked to install 3dSen", ex); return false; }
        }

        /// <summary>Is <paramref name="exe"/> inside a Steam library's steamapps\common?</summary>
        public static bool IsSteamPath(string exe)
        {
            try
            {
                var root = SteamRoot();
                if (root == null || string.IsNullOrWhiteSpace(exe)) return false;
                var full = Path.GetFullPath(exe);
                return Libraries(root).Any(l => full.StartsWith(Path.Combine(l, "steamapps", "common") + "\\", StringComparison.OrdinalIgnoreCase));
            }
            catch { return false; }
        }

        // ── Valve's KeyValues text ───────────────────────────────────────────

        /// <summary>"key" "value" and "key" { ... }, keys case-insensitive as Steam reads them; \\ \" \n \t unescaped.
        /// Comments (//) skipped. A malformed file gives what was read before the fault.</summary>
        internal static Dictionary<string, object> Parse(string text)
        {
            int i = 0;
            return Block(text ?? "", ref i);
        }

        private static Dictionary<string, object> Block(string t, ref int i)
        {
            var d = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            while (true)
            {
                var key = Token(t, ref i, out bool brace);
                if (key == null) return d;
                if (brace) { if (key == "}") return d; continue; }
                var value = Token(t, ref i, out brace);
                if (value == null) return d;
                if (brace && value == "{") d[key] = Block(t, ref i);
                else if (!brace) d[key] = value;
                else return d;
            }
        }

        private static string Token(string t, ref int i, out bool brace)
        {
            brace = false;
            while (i < t.Length)
            {
                char c = t[i];
                if (char.IsWhiteSpace(c)) { i++; continue; }
                if (c == '/' && i + 1 < t.Length && t[i + 1] == '/') { while (i < t.Length && t[i] != '\n') i++; continue; }
                if (c == '{' || c == '}') { i++; brace = true; return c.ToString(); }
                var sb = new StringBuilder();
                if (c == '"')
                {
                    i++;
                    while (i < t.Length && t[i] != '"')
                    {
                        if (t[i] == '\\' && i + 1 < t.Length)
                        {
                            var n = t[i + 1];
                            sb.Append(n == 'n' ? '\n' : n == 't' ? '\t' : n);
                            i += 2;
                            continue;
                        }
                        sb.Append(t[i++]);
                    }
                    i++;
                    return sb.ToString();
                }
                while (i < t.Length && !char.IsWhiteSpace(t[i]) && t[i] != '{' && t[i] != '}' && t[i] != '"') sb.Append(t[i++]);
                return sb.ToString();
            }
            return null;
        }

        internal static Dictionary<string, object> Child(Dictionary<string, object> d, string key)
            => d != null && d.TryGetValue(key, out var v) ? v as Dictionary<string, object> : null;

        internal static string Text(Dictionary<string, object> d, string key)
            => d != null && d.TryGetValue(key, out var v) ? v as string : null;
    }
}
