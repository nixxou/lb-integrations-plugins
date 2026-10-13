// RetroAchievements: the login LaunchBox holds, given to SUPER ZSNES the way it keeps its own.
//
// 0.320 (13/10) is the first build that logs in and sends unlocks: ZRetroAchievements, the front end of the native
// rcheevos client (ZRCheevosIntegration), softcore only - "hardcore mode will eventually come" (release notes). It keeps
// the account in MainMenuSettings: rauserID in clear, raencT = StringEnc.GetEPW(token), AES under a key derived from the
// machine's deviceUniqueIdentifier (05-emulator-internals.md). Only the emulator's own process can compute that key, so
// LaunchBox cannot write it; this plugin can, through the emulator's own StringEnc.
//
// THE HANDOFF. Right before a launch, the LaunchBox plugin writes portable\nixx-ra-login.bin: "v1\n<user>\n<token>",
// DPAPI-protected for the current Windows user (never the token in clear on disk). It is read and DELETED at Load,
// then put into the settings at the end of LoadMainMenuSave, before ZRetroAchievements.Start logs in from them - the
// emulator does the rest exactly as if the user had typed the login in its Config > Retroachievements screen, and
// saves it in its settings file encrypted its own way. No file, nothing touched: the emulator's own login stays.
//
// Hardcore is never switched on here: the emulator does not enforce it yet (states, rewind, cheats stay available),
// and an rcheevos client in hardcore beside them is how an account gets flagged.

using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using HarmonyLib;

namespace LbIntegrations.SuperZsnes.Mod
{
    internal static class Cheevos
    {
        public const string HandoffName = "nixx-ra-login.bin";

        private static string _user, _token;

        internal static bool Pending => _user != null;

        /// <summary>At Load: the handoff read and deleted, whatever happens next - a token is not left behind.</summary>
        internal static void TakeHandoff(string portableRoot)
        {
            var path = Path.Combine(portableRoot, HandoffName);
            if (!File.Exists(path)) return;
            try
            {
                var lines = Encoding.UTF8.GetString(Dpapi.Unprotect(File.ReadAllBytes(path))).Split('\n');
                if (lines.Length >= 3 && lines[0] == "v1" && lines[1].Length > 0 && lines[2].Length > 0)
                {
                    _user = lines[1];
                    _token = lines[2];
                    Plugin.Logger.LogInfo("RetroAchievements: a login from LaunchBox for " + _user);
                }
                else Plugin.Logger.LogWarning("RetroAchievements: " + HandoffName + " is not in the expected form - ignored");
            }
            catch (Exception ex) { Plugin.Logger.LogWarning("RetroAchievements: could not read " + HandoffName + " - " + ex.Message); }
            finally { try { File.Delete(path); } catch { } }
        }

        /// <summary>At the end of LoadMainMenuSave: the account written into the settings when it is not the one there.</summary>
        [HarmonyPatch(typeof(MainMenuManager), "LoadMainMenuSave")]
        internal static class AfterLoad
        {
            private static void Postfix(MainMenuManager __instance)
            {
                if (!Pending) return;
                try
                {
                    var settings = __instance.mainMenuSettings;
                    if (settings == null) { Plugin.Logger.LogWarning("RetroAchievements: no settings after LoadMainMenuSave"); return; }
                    var enc = new StringEnc();
                    enc.Init();
                    string current = null;
                    try { if (settings.raencT != null && settings.raencT.Length > 0) current = enc.GetNPW(settings.raencT); } catch { }
                    if (settings.rauserID == _user && current == _token)
                        Plugin.Logger.LogInfo("RetroAchievements: the emulator already holds this login");
                    else
                    {
                        settings.rauserID = _user;
                        settings.raencT = enc.GetEPW(_token);
                        Plugin.Logger.LogInfo("RetroAchievements: login set for " + _user + (current == null ? "" : " (it held another)"));
                    }
                }
                catch (Exception ex) { Plugin.Logger.LogWarning("RetroAchievements: could not set the login - " + ex.Message); }
                finally { _user = _token = null; }
            }
        }

        /// <summary>The emulator's answer, in the log: did the login LaunchBox gave work.</summary>
        [HarmonyPatch(typeof(ZRetroAchievements), "LoginCallback")]
        internal static class LoginResult
        {
            private static void Prefix(ZRCheevosIntegration.RC_ERROR val, string message)
                => Plugin.Logger.LogInfo("RetroAchievements: login " + (val == ZRCheevosIntegration.RC_ERROR.RC_OK ? "OK" : val + " \"" + message + "\""));
        }
    }

    /// <summary>CryptUnprotectData for the current user, with the pack's entropy. P/Invoke: the runtime BepInEx carries has
    /// no ProtectedData.</summary>
    internal static class Dpapi
    {
        private static readonly byte[] Entropy = Encoding.ASCII.GetBytes("nixx-superzsnes-ra-v1");

        [StructLayout(LayoutKind.Sequential)]
        private struct Blob { public int Size; public IntPtr Data; }

        [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool CryptUnprotectData(ref Blob dataIn, IntPtr description, ref Blob entropy, IntPtr reserved, IntPtr prompt, int flags, ref Blob dataOut);

        [DllImport("kernel32.dll")]
        private static extern IntPtr LocalFree(IntPtr mem);

        internal static byte[] Unprotect(byte[] data)
        {
            var hIn = GCHandle.Alloc(data, GCHandleType.Pinned);
            var hEnt = GCHandle.Alloc(Entropy, GCHandleType.Pinned);
            try
            {
                var input = new Blob { Size = data.Length, Data = hIn.AddrOfPinnedObject() };
                var entropy = new Blob { Size = Entropy.Length, Data = hEnt.AddrOfPinnedObject() };
                var output = new Blob();
                if (!CryptUnprotectData(ref input, IntPtr.Zero, ref entropy, IntPtr.Zero, IntPtr.Zero, 1 /* UI_FORBIDDEN */, ref output))
                    throw new InvalidOperationException("CryptUnprotectData failed, error " + Marshal.GetLastWin32Error());
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
