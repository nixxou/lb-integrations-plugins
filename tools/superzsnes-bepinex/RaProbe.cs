// RetroAchievements, MEASURED FROM INSIDE (Mehdi, 01/10): the site lists the feature as "coming", but the
// 0.310 build carries two clients - RetroAchievements (C#: login, ROM hash, session, achievement sets,
// RCAchievementProcessor evaluating them every frame) and ZRCheevosIntegration (the native rcheevos
// rc_client) - plus a Config > Retroachievements screen (RetroAchievementsOverlay) and an
// OptionsOverlay.EnableRetroachievements() that looks like the switch hiding it.
//
//   --nixx-ra-probe    every call into both clients and the state of the menus, in the log. Secrets are
//                      never written: a password or a token is logged as its length.
//   --nixx-ra-unlock   the RetroAchievements panel SHOWN whenever the options open (OptionsOverlay.retroachievements
//                      is the panel itself, laid over the options - not an entry of them).
//   --nixx-ra-drive    for a test without hands: in the main menu, the options opened at 6 s, the
//                      RetroAchievements dialog at 12 s.
//
// MEASURED 01/10: the panel is hidden in 0.310 and EnableRetroachievements() shows it; it opens with the emulator's
// own "Work In Progress! ... Achievements aren't sent or saved yet and many achievements aren't supported yet". The C# client (RetroAchievements) is created at start, not logged in; the native one
// (ZRCheevosIntegration) never started.
//
// A diagnostic, to be replaced by the real feature once the measurements say what it is. One patch class
// per target, each patched on its own: a target a build lacks costs its line, not the probe.

using System;
using HarmonyLib;
using UnityEngine;

namespace LbIntegrations.SuperZsnes.Mod
{
    internal static class RaProbe
    {
        internal static void Install(Harmony harmony, Action<Harmony, Type, string> patch)
        {
            foreach (var t in new[]
            {
                typeof(RaAwake), typeof(RaStart), typeof(RaLogin), typeof(RaLogout), typeof(RaLoadRom), typeof(RaUserProfile),
                typeof(RaGameId), typeof(RaSession), typeof(RaSets), typeof(RaGrant),
                // Not the native callbacks (LoginCallback, load_game_callback, LogMessage): patched, they were called at
                // once with garbage (01/10) - rcheevos holds pointers to them.
                typeof(RcStart), typeof(RcLogin), typeof(RcLoadGame),
                typeof(OptionsOpened), typeof(OptionsEnableRa), typeof(RaOverlayOpened), typeof(RaOverlayTryLogin), typeof(MenuState),
            })
                patch(harmony, t, "ra probe: " + t.Name);
        }

        internal static void Say(string text) => Plugin.Logger.LogInfo("ra: " + text);
        internal static string Len(string secret) => secret == null ? "null" : "<" + secret.Length + " chars>";
        internal static string Cut(string s, int n = 400) => s == null ? "null" : s.Length <= n ? s : s.Substring(0, n) + "...(" + s.Length + ")";
        internal static string Active(GameObject go) => go == null ? "null" : go.activeSelf ? "shown" : "hidden";
    }

    // ── the C# client ───────────────────────────────────────────────────────
    [HarmonyPatch(typeof(RetroAchievements), "Awake")] internal static class RaAwake { private static void Prefix() => RaProbe.Say("RetroAchievements.Awake"); }
    [HarmonyPatch(typeof(RetroAchievements), "Start")] internal static class RaStart { private static void Prefix() => RaProbe.Say("RetroAchievements.Start"); }
    [HarmonyPatch(typeof(RetroAchievements), "Login")] internal static class RaLogin
    { private static void Prefix(string userId, string password, string token) => RaProbe.Say("RetroAchievements.Login user=" + userId + " password=" + RaProbe.Len(password) + " token=" + RaProbe.Len(token)); }
    [HarmonyPatch(typeof(RetroAchievements), "Logout")] internal static class RaLogout { private static void Prefix() => RaProbe.Say("RetroAchievements.Logout"); }
    [HarmonyPatch(typeof(RetroAchievements), "LoadRom")] internal static class RaLoadRom { private static void Prefix() => RaProbe.Say("RetroAchievements.LoadRom"); }
    [HarmonyPatch(typeof(RetroAchievements), "UserProfileResult")] internal static class RaUserProfile
    { private static void Prefix(bool success, string val) => RaProbe.Say("RetroAchievements.UserProfileResult success=" + success + " reply=" + RaProbe.Len(val)); }
    [HarmonyPatch(typeof(RetroAchievements), "LoadRomGameIdResult")] internal static class RaGameId
    { private static void Prefix(bool success, string val) => RaProbe.Say("RetroAchievements.LoadRomGameIdResult success=" + success + " reply=" + RaProbe.Cut(val)); }
    [HarmonyPatch(typeof(RetroAchievements), "StartSessionResult")] internal static class RaSession
    { private static void Prefix(bool success, string val) => RaProbe.Say("RetroAchievements.StartSessionResult success=" + success + " reply=" + RaProbe.Cut(val)); }
    [HarmonyPatch(typeof(RetroAchievements), "GetAchievementSetsResult")] internal static class RaSets
    { private static void Prefix(bool success, string val) => RaProbe.Say("RetroAchievements.GetAchievementSetsResult success=" + success + " reply=" + RaProbe.Cut(val, 200)); }
    [HarmonyPatch(typeof(RetroAchievements), "GrantAchievement")] internal static class RaGrant
    { private static void Prefix(string title, int numPoints) => RaProbe.Say("RetroAchievements.GrantAchievement \"" + title + "\" " + numPoints + " pt"); }

    // ── the native rcheevos client ──────────────────────────────────────────
    [HarmonyPatch(typeof(ZRCheevosIntegration), "Start")] internal static class RcStart { private static void Prefix() => RaProbe.Say("ZRCheevosIntegration.Start"); }
    [HarmonyPatch(typeof(ZRCheevosIntegration), "Login")] internal static class RcLogin
    { private static void Prefix(string userID, string password) => RaProbe.Say("ZRCheevosIntegration.Login user=" + userID + " password=" + RaProbe.Len(password)); }
    [HarmonyPatch(typeof(ZRCheevosIntegration), "LoadGame")] internal static class RcLoadGame
    { private static void Prefix(int rom_size) => RaProbe.Say("ZRCheevosIntegration.LoadGame " + rom_size + " bytes"); }

    // ── the menus ───────────────────────────────────────────────────────────
    [HarmonyPatch(typeof(OptionsOverlay), "OnEnable")] internal static class OptionsOpened
    {
        private static void Postfix(OptionsOverlay __instance)
        {
            try
            {
                RaProbe.Say("options opened - its RetroAchievements entry is " + RaProbe.Active(__instance.retroachievements));
                if (!Plugin.Opt.RaUnlock || __instance.retroachievements == null) return;
                __instance.retroachievements.SetActive(true);
                RaProbe.Say("the RetroAchievements panel shown by us - now " + RaProbe.Active(__instance.retroachievements));
            }
            catch (Exception ex) { RaProbe.Say("options opened - " + ex.Message); }
        }
    }

    [HarmonyPatch(typeof(OptionsOverlay), "EnableRetroachievements")] internal static class OptionsEnableRa
    { private static void Prefix() => RaProbe.Say("OptionsOverlay.EnableRetroachievements"); }

    [HarmonyPatch(typeof(RetroAchievementsOverlay), "OnEnable")] internal static class RaOverlayOpened
    { private static void Prefix() => RaProbe.Say("the RetroAchievements screen opened"); }

    [HarmonyPatch(typeof(RetroAchievementsOverlay), "TryLogin")] internal static class RaOverlayTryLogin
    { private static void Prefix() => RaProbe.Say("RetroAchievementsOverlay.TryLogin"); }

    /// <summary>In the main menu (no ROM): the state at 4 s; with --nixx-ra-unlock, the options dialog opened at 6 s and
    /// the RetroAchievements dialog at 12 s, the way the menu would open them - their GameObjects set active.</summary>
    [HarmonyPatch(typeof(MainMenuManager), "Update")] internal static class MenuState
    {
        private static int _step;
        private static void Postfix(MainMenuManager __instance)
        {
            float t = Time.realtimeSinceStartup;
            try
            {
                if (_step == 0 && t >= 4f) { _step = 1; State(__instance, "state"); }
                if (!Plugin.Opt.RaDrive) return;
                if (_step == 1 && t >= 6f)
                {
                    _step = 2;
                    RaProbe.Say("opening the options dialog");
                    __instance.optionsDialog?.SetActive(true);
                    State(__instance, "after opening the options");
                }
                if (_step == 2 && t >= 12f)
                {
                    _step = 3;
                    __instance.optionsDialog?.SetActive(false);
                    RaProbe.Say("opening the RetroAchievements dialog");
                    __instance.retroachievementsDialog?.SetActive(true);
                    State(__instance, "after opening the RetroAchievements dialog");
                }
            }
            catch (Exception ex) { RaProbe.Say("menu step " + _step + " - " + ex.Message); }
        }

        private static void State(MainMenuManager m, string when)
        {
            var ra = RetroAchievements.Instance;
            RaProbe.Say(when + " (" + (int)Time.realtimeSinceStartup + " s): RetroAchievements.Instance " + (ra == null ? "none" : "there, logged in " + ra.IsLoggedIn)
                        + "; optionsDialog " + RaProbe.Active(m.optionsDialog) + "; retroachievementsDialog " + RaProbe.Active(m.retroachievementsDialog)
                        + "; devBuild " + RaProbe.Active(m.devBuild));
        }
    }
}
