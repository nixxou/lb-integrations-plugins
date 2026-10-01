// What runs inside SUPER ZSNES, once BepInEx has put a .NET runtime there.
//
// The emulator is IL2CPP and closed; everything below is a Harmony patch resolved BY NAME through
// the interop assemblies BepInEx generates, so an emulator update that keeps its class and member
// names keeps this working, and one that renames them fails at patch time with a line in
// BepInEx\LogOutput.log rather than a crash. docs\superzsnes-bepinex.md has the whole story.
//
// WHAT IT DOES, each line measured in the game's own log:
//
//   PORTABLE      Application.persistentDataPath answers <exe>\portable. The emulator builds the
//                 settings path in three places (GetConfigFilePath, LoadMainMenuSave,
//                 SaveMainMenuSave), all through that getter, so the getter is the one patch.
//   OVERRIDES     --nixx-set:<field>=<value> and --nixx-game:<field>=<value> set fields of
//                 MainMenuSettings and of the current game's GameSpecificSettings in memory, and
//                 are taken out again around every save so the user's file stays theirs. Overrides.cs.
//   ESCAPE        the first press shows "Press ESC again to quit" on the game's text line, the
//                 second within 2.5 s lets the emulator save through its own EscapeBackToMenu and
//                 then calls MainMenuManager.OnExit. The menu key (F1) does what Escape did.
//                 The pad's Exit button, which the emulator turns into a bare Application.Quit, is
//                 left alone.
//   POPUPS        the "check out our Patreon" dialog (MainMenuManager.supportUs) is put back to
//                 sleep the frame it wakes up; the "new version" one can be, on request.
//
// Options.cs has the grammar. Two traps for whoever adds to this: Object.FindObjectOfType and the
// new Input System's ButtonControl properties are stripped from this build ("Method unstripping
// failed"); the game's singletons and BepInEx's UnityInput work. And MasterExecutor.Update asks
// EscapePressed twice a frame.

using System;
using System.IO;
using BepInEx;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using UnityEngine;

namespace LbIntegrations.SuperZsnes.Mod
{
    [BepInPlugin("com.nixxou.superzsnes", "SUPER ZSNES integration", "0.5.0")]
    public class Plugin : BasePlugin
    {
        internal static NixxLog Logger;
        internal static Options Opt = new Options();

        /// <summary>Where the emulator's data goes instead of LocalLow: the folder "portable" beside
        /// SUPERZSNES.exe. Computed once from the process, not from Unity, so it is right before any
        /// Unity API is safe to call.</summary>
        internal static string PortableRoot;

        internal static KeyCode MenuKey = KeyCode.F1;

        public override void Load()
        {
            var exeDir = Path.GetDirectoryName(Environment.ProcessPath ?? AppContext.BaseDirectory) ?? AppContext.BaseDirectory;
            PortableRoot = Path.Combine(exeDir, "portable");
            var args = Environment.GetCommandLineArgs();
            Opt = Options.Parse(args);
            Logger = new NixxLog(Log, PortableRoot, Opt.Log);
            try
            {
                Directory.CreateDirectory(PortableRoot);
                Logger.LogInfo("SUPER ZSNES integration plugin " + Version + " under BepInEx " + typeof(BasePlugin).Assembly.GetName().Version);
                Logger.LogInfo("command line: " + args.Length + " argument(s)");
                for (int i = 0; i < args.Length; i++) Logger.LogInfo("  [" + i + "] " + args[i]);
                foreach (var u in Opt.Unknown) Logger.LogWarning("unknown option ignored: " + u);
                Logger.LogInfo("options: portable=" + Opt.Portable + " quit-confirm=" + Opt.QuitConfirm + " menu-key=" + Opt.MenuKey
                            + " support-popup=" + Opt.SupportPopup + " version-popup=" + Opt.VersionPopup + " persist=" + Opt.Persist
                            + " display=" + (Opt.Display ?? "as is") + " log=" + Opt.Log + " set=" + Opt.Settings.Count + " game=" + Opt.GameSettings.Count);
                if (Opt.Display != null && Opt.Display != "primary") Logger.LogWarning("display \"" + Opt.Display + "\" is not understood; only \"primary\" is");

                if (!Enum.TryParse(Opt.MenuKey, true, out MenuKey)) { Logger.LogWarning("menu-key \"" + Opt.MenuKey + "\" is not a KeyCode; F1 kept"); MenuKey = KeyCode.F1; }

                // ONE FEATURE PER PATCH CLASS, EACH ON ITS OWN. A future build that renames one
                // method must cost that one feature and a log line, not the whole plugin: PatchAll
                // resolves the target by name when it runs, and throws for that class alone.
                var harmony = new Harmony("com.nixxou.superzsnes");
                if (Opt.Portable) Patch(harmony, typeof(PersistentDataPathPatch), "portable data folder");
                Patch(harmony, typeof(EscapePatch), "Escape confirmation and menu key");
                Patch(harmony, typeof(Overrides.AfterLoad), "settings overrides");
                Patch(harmony, typeof(Overrides.AfterGameSettings), "per-game overrides");
                Patch(harmony, typeof(Overrides.AroundSave), "keeping overrides out of the file");
                Patch(harmony, typeof(PopupPatch), "popups and primary display");
                if (Opt.RaProbe) RaProbe.Install(harmony, Patch);
                Logger.LogInfo("patched: " + string.Join(", ", harmony.GetPatchedMethods()));
            }
            catch (Exception ex)
            {
                Logger.LogError("load failed - " + ex);
            }
        }

        public const string Version = "0.5.0";

        private void Patch(Harmony harmony, Type patchClass, string feature)
        {
            try { harmony.PatchAll(patchClass); }
            catch (Exception ex)
            {
                // The name it could not find is in the message: that is what a new build renamed.
                Logger.LogError("NOT patched - " + feature + " is off for this build: " + (ex.InnerException ?? ex).Message);
            }
        }

        /// <summary>One line on the game's own text overlay. Empty clears it. Reached through the
        /// executor's own field: Object.FindObjectOfType is stripped from this build.</summary>
        internal static void Say(string text)
        {
            try
            {
                var ui = MasterExecutor.Instance?.gameStateUI;
                if (ui != null) ui.SetGameUIString(text ?? "");
                else Logger.LogWarning("no GameStateUI to say: " + text);
            }
            catch (Exception ex) { Logger.LogWarning("Say failed - " + ex.Message); }
        }
    }

    /// <summary>UnityEngine.Application.persistentDataPath, answered from beside the exe.</summary>
    [HarmonyPatch(typeof(Application), nameof(Application.persistentDataPath), MethodType.Getter)]
    internal static class PersistentDataPathPatch
    {
        private static bool _said;

        private static bool Prefix(ref string __result)
        {
            __result = Plugin.PortableRoot;
            if (!_said)
            {
                _said = true;
                Plugin.Logger.LogInfo("persistentDataPath asked - answering " + __result);
            }
            return false;
        }
    }

    /// <summary>Escape asks once, then quits; the menu key opens the menu.</summary>
    [HarmonyPatch(typeof(ZInputSystem), nameof(ZInputSystem.EscapePressed))]
    internal static class EscapePatch
    {
        private const float Window = 2.5f;

        private static float _armedUntil;      // realtimeSinceStartup; 0 when not armed
        private static int _lastPressFrame = -1;
        private static int _lastMenuKeyFrame = -1;
        private static bool _quitting;
        private static bool _failureLogged;

        private static void Postfix(ref bool __result)
        {
            try
            {
                int frame = Time.frameCount;
                float now = Time.realtimeSinceStartup;

                if (_armedUntil > 0f && now >= _armedUntil)
                {
                    _armedUntil = 0f;
                    Plugin.Say("");
                }

                var executor = MasterExecutor.Instance;
                var menu = executor?.mainMenuManager ?? MainMenuManager.Instance;
                bool inGame = menu != null && executor != null && menu.gameRunning && !menu.IsInMenu();
                if (!inGame || _quitting) return;

                // The menu key: what Escape used to do. Once per frame, whichever question comes first.
                if (UnityInput.Current.GetKeyDown(Plugin.MenuKey))
                {
                    if (_lastMenuKeyFrame != frame)
                    {
                        _lastMenuKeyFrame = frame;
                        _armedUntil = 0f;
                        __result = true;
                    }
                    else __result = false;
                    return;
                }

                if (!__result || !Plugin.Opt.QuitConfirm) return;

                if (_lastPressFrame == frame) { __result = false; return; }
                _lastPressFrame = frame;
                __result = false;

                if (_armedUntil > 0f && now < _armedUntil)
                {
                    _armedUntil = 0f;
                    _quitting = true;
                    Plugin.Logger.LogInfo("Escape confirmed - saving through the emulator, then quitting");
                    Plugin.Say("");
                    try { executor.EscapeBackToMenu(true); }
                    catch (Exception ex) { Plugin.Logger.LogWarning("EscapeBackToMenu failed - " + ex.Message); }
                    try { menu.OnExit(); }
                    catch (Exception ex) { Plugin.Logger.LogWarning("OnExit failed - " + ex.Message); Application.Quit(); }
                    return;
                }

                _armedUntil = now + Window;
                Plugin.Say("Press ESC again to quit   (" + Plugin.MenuKey + ": menu)");
            }
            catch (Exception ex)
            {
                if (!_failureLogged) { _failureLogged = true; Plugin.Logger.LogWarning("EscapePressed postfix - " + ex); }
            }
        }
    }

    /// <summary>The per-frame hook, on the menu manager's Update: the dialogs the emulator raises
    /// on its own schedule are put back to sleep the frame they wake (supportUs is the "Just this
    /// once, we want to let you know ... Patreon" one), and the primary-display move runs its
    /// steps.</summary>
    [HarmonyPatch(typeof(MainMenuManager), "Update")]
    internal static class PopupPatch
    {
        private static int _hiddenSupport, _hiddenVersion;
        private static bool _failureLogged;

        private static void Postfix(MainMenuManager __instance)
        {
            try
            {
                if (Plugin.Opt.Display == "primary") PrimaryDisplay.Tick();

                if (!Plugin.Opt.SupportPopup)
                {
                    var go = __instance.supportUs;
                    if (go != null && go.activeSelf)
                    {
                        go.SetActive(false);
                        if (_hiddenSupport++ == 0) Plugin.Logger.LogInfo("support popup hidden");
                    }
                }
                if (!Plugin.Opt.VersionPopup)
                {
                    var go = __instance.newVersion;
                    if (go != null && go.activeSelf)
                    {
                        go.SetActive(false);
                        if (_hiddenVersion++ == 0) Plugin.Logger.LogInfo("new-version popup hidden");
                    }
                }
            }
            catch (Exception ex)
            {
                if (!_failureLogged) { _failureLogged = true; Plugin.Logger.LogWarning("popup patch - " + ex); }
            }
        }
    }
}
