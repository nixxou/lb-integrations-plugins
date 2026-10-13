// RetroAchievements in SUPER ZSNES: the login LaunchBox hands over, passed to the in-process plugin.
//
// 0.320 (13/10) is the first build that logs in and sends unlocks - softcore only, "hardcore mode will eventually
// come" (release notes). It keeps the account in its settings file (NRBF) as rauserID and raencT, the token AES-encrypted
// under a key derived from the machine's Unity deviceUniqueIdentifier: nothing outside its process can write that. So
// this side only leaves portable\nixx-ra-login.bin beside the executable - "v1\n<user>\n<token>", DPAPI-protected for
// the current Windows user, never the token in clear - and the BepInEx plugin (tools\superzsnes-bepinex\Cheevos.cs)
// reads it, deletes it, and puts the login in the settings through the emulator's own StringEnc, before the emulator
// logs in from them. The emulator then keeps it, encrypted its own way, as if typed in its Retroachievements screen.
//
// HARDCORE IS NOT OFFERED: the emulator does not enforce it (states, rewind, cheats stay on), so the plugin says
// isHardcore false and a hardcore request from LaunchBox is a line in the log, not a switch.

using System;
using System.Collections.Concurrent;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace LbIntegrations.SuperZsnes
{
    internal static class SuperZsnesCheevos
    {
        public const string HandoffName = "nixx-ra-login.bin";
        public static readonly Version FirstWithCheevos = new Version(0, 320);

        /// <summary>Does the SUPER ZSNES at this path have RetroAchievements: its version, read off the scene, is 0.320 or
        /// later. Remembered per executable while its scene file keeps its date - LaunchBox asks often.</summary>
        public static bool BuildHasCheevos(string exePath)
        {
            try
            {
                if (string.IsNullOrEmpty(exePath) || !File.Exists(exePath)) return false;
                var level0 = Path.Combine(Path.GetDirectoryName(exePath), SuperZsnesPaths.DataFolder, "level0");
                var stamp = File.Exists(level0) ? File.GetLastWriteTimeUtc(level0).Ticks : 0;
                if (_known.TryGetValue(exePath, out var known) && known.Stamp == stamp) return known.Has;
                var v = SuperZsnesPaths.InstalledVersion(exePath);
                bool has = v != null && Version.TryParse(v, out var parsed) && parsed >= FirstWithCheevos;
                _known[exePath] = (stamp, has);
                return has;
            }
            catch { return false; }
        }

        private static readonly ConcurrentDictionary<string, (long Stamp, bool Has)> _known
            = new ConcurrentDictionary<string, (long, bool)>(StringComparer.OrdinalIgnoreCase);

        /// <summary>The handoff written for the next start. Null when done, else why not. Only what LaunchBox gave: without
        /// both a user and a token nothing is written, and the emulator's own login stays.</summary>
        public static string Write(string exePath, string user, string token, bool? hardcore)
        {
            if (string.IsNullOrEmpty(user) || string.IsNullOrEmpty(token)) return null;
            if (user.IndexOf('\n') >= 0 || token.IndexOf('\n') >= 0) return "the RetroAchievements login has a line break in it";
            if (!SuperZsnesPaths.IsSuperZsnesExecutable(exePath)) return "this emulator is not SUPER ZSNES";
            if (!BuildHasCheevos(exePath)) return "this SUPER ZSNES predates RetroAchievements (0.320)";
            var exeDir = Path.GetDirectoryName(exePath);
            if (!SuperZsnesBepInEx.HasBepInEx(exeDir) || !SuperZsnesBepInEx.HasPlugin(exeDir))
                return "the pack's in-process plugin is not in place beside SUPER ZSNES - log in from its Config > Retroachievements screen, or install BepInEx from the Nixx window";
            try
            {
                var portable = Path.Combine(exeDir, "portable");
                Directory.CreateDirectory(portable);
                var path = Path.Combine(portable, HandoffName);
                var tmp = path + ".tmp";
                File.WriteAllBytes(tmp, Dpapi.Protect(Encoding.UTF8.GetBytes("v1\n" + user + "\n" + token)));
                File.Move(tmp, path, overwrite: true);
                Log.Info("RetroAchievements login handed to SUPER ZSNES for " + user
                         + (hardcore == true ? " - softcore: SUPER ZSNES has no hardcore mode yet" : ""));
                return null;
            }
            catch (Exception ex) { Log.Warn("could not write " + HandoffName, ex); return "Could not hand the RetroAchievements login to SUPER ZSNES: " + ex.Message; }
        }

        /// <summary>CryptProtectData for the current user, with the entropy the in-process plugin uses to read it back.
        /// P/Invoke on both sides: the runtime BepInEx carries has no ProtectedData.</summary>
        private static class Dpapi
        {
            private static readonly byte[] Entropy = Encoding.ASCII.GetBytes("nixx-superzsnes-ra-v1");

            [StructLayout(LayoutKind.Sequential)]
            private struct Blob { public int Size; public IntPtr Data; }

            [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
            private static extern bool CryptProtectData(ref Blob dataIn, string description, ref Blob entropy, IntPtr reserved, IntPtr prompt, int flags, ref Blob dataOut);

            [DllImport("kernel32.dll")]
            private static extern IntPtr LocalFree(IntPtr mem);

            internal static byte[] Protect(byte[] data)
            {
                var hIn = GCHandle.Alloc(data, GCHandleType.Pinned);
                var hEnt = GCHandle.Alloc(Entropy, GCHandleType.Pinned);
                try
                {
                    var input = new Blob { Size = data.Length, Data = hIn.AddrOfPinnedObject() };
                    var entropy = new Blob { Size = Entropy.Length, Data = hEnt.AddrOfPinnedObject() };
                    var output = new Blob();
                    if (!CryptProtectData(ref input, "SUPER ZSNES RetroAchievements login", ref entropy, IntPtr.Zero, IntPtr.Zero, 1 /* UI_FORBIDDEN */, ref output))
                        throw new InvalidOperationException("CryptProtectData failed, error " + Marshal.GetLastWin32Error());
                    try
                    {
                        var bytes = new byte[output.Size];
                        Marshal.Copy(output.Data, bytes, 0, output.Size);
                        return bytes;
                    }
                    finally { LocalFree(output.Data); }
                }
                finally { hIn.Free(); hEnt.Free(); }
            }
        }
    }
}
