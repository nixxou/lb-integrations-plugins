// Every option the pack can pass to SUPER ZSNES on its command line, in one table.
//
// FIVE FAMILIES, five spellings on the line - and only the first two need the BepInEx plugin:
//
//   Setting   --nixx-set:<field>=<value>     a field of MainMenuSettings, applied in memory by the
//                                            plugin and kept out of the settings file
//   Game      --nixx-game:<field>=<value>    a field of GameSpecificSettings for the game launched
//   Plugin    --nixx-<name>=<value>          the plugin's own behaviour (quit confirmation, menu key...)
//   Native    --loadstate                    what the emulator itself understands - read off
//                                            MasterExecutor.Awake, which also ignores every other
//                                            argument starting with "-"
//   Unity     -screen-fullscreen 1 ...       the player's standard switches, understood by any Unity
//                                            build; note the space, not "="
//
// The field names are the emulator's own, exactly as Il2CppInterop exposes them and as the plugin
// looks them up by reflection; a typo here is a warning in BepInEx\LogOutput.log, not a crash. The
// list was typed off the 0.310 type dump (docs\superzsnes-bepinex.md, section 3), and the plugin
// can write the live one as <exe>\portable\options.json with --nixx-dump-options. Controller
// bindings (inputData) are deliberately not here yet.
//
// WHERE EACH IS SET (Mehdi, 01/10): a few are the pack's own, for every game (the Nixx window's tab); a
// selection is per game (the game's right-click "Nixx-SuperZSNES : Options..."); the rest stays in the
// catalogue, measured and renderable, but is shown nowhere and never sent. See ScopeOf.

using System.Collections.Generic;

namespace LbIntegrations.SuperZsnes
{
    internal enum OptionFamily { Setting, Game, Plugin, Native, Unity }

    internal enum OptionKind { Bool, Int, Float, Text, Choice }

    /// <summary>Where an option is set: nowhere (kept in the catalogue only), the Nixx window for every game, or a
    /// game's own options window.</summary>
    internal enum OptionScope { Hidden, Global, Game }

    internal sealed class Option
    {
        public string Key;
        public OptionFamily Family;
        public OptionKind Kind;
        public string Group;
        public string Label;
        public string Help;
        public string[] Choices;
        /// <summary>What the emulator does when the option is not passed - shown, never sent.</summary>
        public string Default;
        public decimal Min = decimal.MinValue, Max = decimal.MaxValue;
        public int Decimals;

        /// <summary>The settings.ini key: "<family>.<key>".</summary>
        public string IniKey => Family.ToString().ToLowerInvariant() + "." + Key;

        public OptionScope Scope => SuperZsnesOptions.ScopeOf(IniKey);
    }

    internal static class SuperZsnesOptions
    {
        private static Option Bool(OptionFamily f, string key, string group, string label, string help, string def = null)
            => new Option { Family = f, Key = key, Kind = OptionKind.Bool, Group = group, Label = label, Help = help, Default = def };

        private static Option Int(OptionFamily f, string key, string group, string label, string help, decimal min, decimal max, string def = null)
            => new Option { Family = f, Key = key, Kind = OptionKind.Int, Group = group, Label = label, Help = help, Min = min, Max = max, Default = def };

        private static Option Float(OptionFamily f, string key, string group, string label, string help, decimal min, decimal max, string def = null)
            => new Option { Family = f, Key = key, Kind = OptionKind.Float, Group = group, Label = label, Help = help, Min = min, Max = max, Decimals = 2, Default = def };

        private static Option Text(OptionFamily f, string key, string group, string label, string help, string def = null)
            => new Option { Family = f, Key = key, Kind = OptionKind.Text, Group = group, Label = label, Help = help, Default = def };

        private static Option Choice(OptionFamily f, string key, string group, string label, string help, string[] choices, string def = null)
            => new Option { Family = f, Key = key, Kind = OptionKind.Choice, Group = group, Label = label, Help = help, Choices = choices, Default = def };

        public static readonly IReadOnlyList<Option> All = new List<Option>
        {
            // ── the pack's plugin inside the emulator ─────────────────────────
            Bool(OptionFamily.Plugin, "bepinex", "Integration", "Install the in-process plugin (BepInEx)",
                 "Read by LaunchBox, not sent: when on, installing or updating the emulator also puts BepInEx " + SuperZsnesBepInEx.Build + " and the pack's plugin beside it, silently, and every launch puts the plugin and its docs back if missing. Off: nothing below this line reaches the emulator.", "on"),
            Bool(OptionFamily.Plugin, "log", "Integration", "Write the plugin's diagnostic log",
                 "--nixx-log: the in-process plugin writes what it does to <exe>\\portable\\nixx.log. BepInEx itself stays silent; BepInEx\\config\\BepInEx.cfg turns its own log on.", "off"),
            Bool(OptionFamily.Plugin, "quit-confirm", "Integration", "Escape asks before quitting",
                 "First Escape shows \"Press ESC again to quit\" on the game's text line; a second within 2.5 s saves through the emulator and quits. Off: Escape opens the emulator's menu, as it ships.", "on"),
            Text(OptionFamily.Plugin, "menu-key", "Integration", "Key that opens the emulator's menu",
                 "A Unity KeyCode name: F1, F3, Backspace, Tab... What Escape did before the confirmation took it.", "F1"),
            Bool(OptionFamily.Plugin, "portable", "Integration", "Keep settings beside the emulator",
                 "persistentDataPath answers <exe>\\portable, so szsnes_ui.data lives there instead of %USERPROFILE%\\AppData\\LocalLow.", "on"),
            Bool(OptionFamily.Plugin, "data-folders", "Integration", "Saves, states and cheats beside the emulator",
                 "The SRAM in <exe>\\saves, the states and history in <exe>\\states, the cheats in <exe>\\cheats, rather than beside the ROM - written into the emulator's settings, so it holds when the emulator is opened on its own too. Only an empty folder setting is filled; what was beside a ROM moves there when that game starts.", "on"),
            Bool(OptionFamily.Plugin, "support-popup", "Integration", "Show the \"support us on Patreon\" dialog",
                 "The emulator raises it on its own schedule. Off: it is put back to sleep the frame it appears.", "off"),
            Bool(OptionFamily.Plugin, "version-popup", "Integration", "Show the \"a new version is out\" dialog",
                 "Off hides it; the pack's own update check still runs in LaunchBox.", "off"),
            Bool(OptionFamily.Plugin, "display", "Window", "Always full screen on the primary display",
                 "The display holding the desktop's origin, whatever number Unity gives it: the window is moved there over the first frames, then put in a borderless full screen window at its resolution. Beats -monitor and the remembered display. Needs the BepInEx plugin."),
            Bool(OptionFamily.Plugin, "persist", "Integration", "Write the overrides into the emulator's settings file",
                 "Normally every --nixx-set / --nixx-game value is applied in memory and taken out again around each save, so the file keeps the user's own values. On: they are saved for good.", "off"),

            // ── the emulator's own switches ───────────────────────────────────
            Bool(OptionFamily.Native, "loadstate", "Start-up", "Resume the last state on start",
                 "--loadstate: loads <rom>.szst-last, the state the emulator writes whenever its menu opens."),

            // ── Unity's window switches ───────────────────────────────────────
            Choice(OptionFamily.Unity, "screen-fullscreen", "Window", "Display mode",
                   "-screen-fullscreen 1 or 0. Overrides what the emulator remembered in the registry, for this launch only.",
                   new[] { "fullscreen", "windowed" }),
            Int(OptionFamily.Unity, "screen-width", "Window", "Width", "-screen-width; with a height, the window or the fullscreen resolution.", 320, 7680),
            Int(OptionFamily.Unity, "screen-height", "Window", "Height", "-screen-height.", 240, 4320),
            Bool(OptionFamily.Unity, "popupwindow", "Window", "Borderless window", "-popupwindow: a window without title bar or borders."),
            Int(OptionFamily.Unity, "monitor", "Window", "Monitor", "-monitor N, 1-based, the display to open on.", 1, 8),

            // ── MainMenuSettings ─────────────────────────────────────────────
            Choice(OptionFamily.Setting, "gfxMode", "Display", "Screen effect", "None, CRT-style scanlines, or the 3D gimmick.", new[] { "None", "Scanlines", "Gimmick3D" }),
            Float(OptionFamily.Setting, "scanlineStrength", "Display", "Scanline strength", "0 to 1, when the effect is Scanlines.", 0, 1),
            Int(OptionFamily.Setting, "interpolationMode", "Display", "Interpolation mode", "The emulator's own index; 0 is its first entry.", 0, 8),
            Bool(OptionFamily.Setting, "noBilinearFiltering", "Display", "No bilinear filtering", "Nearest-neighbour scaling."),
            Bool(OptionFamily.Setting, "maxBrightness", "Display", "Maximum brightness", "The Options > Max Brightness switch."),
            Bool(OptionFamily.Setting, "use87aspect", "Display", "8:7 aspect instead of 4:3", "Square pixels, as the console outputs them, rather than the TV's 4:3."),
            Choice(OptionFamily.Setting, "fontType", "Display", "Interface font", "The menu font.", new[] { "Roboto", "ZSNESOrigin", "Upheaval", "Karmatic" }),
            Int(OptionFamily.Setting, "guiEffect", "Display", "Menu background effect", "The emulator's own index (snow, water, fire...).", 0, 8),
            Int(OptionFamily.Setting, "cursorSize", "Display", "Cursor size", "The emulator's own index.", 0, 8),
            Int(OptionFamily.Setting, "cursorType", "Display", "Cursor type", "The emulator's own index.", 0, 8),
            Bool(OptionFamily.Setting, "disableUIInput", "Display", "Disable UI input", "Options > Disable UI Input."),

            Bool(OptionFamily.Setting, "rewindDisabled", "Gameplay", "Disable rewind", "No rewind buffer at all; saves memory and CPU."),
            Int(OptionFamily.Setting, "rewindMode", "Gameplay", "Rewind mode", "The emulator's own index.", 0, 8),
            Int(OptionFamily.Setting, "numRewindFrames", "Gameplay", "Rewind frames", "How many frames the rewind buffer keeps.", 0, 100000),
            Int(OptionFamily.Setting, "rewindFPS", "Gameplay", "Rewind capture rate", "Frames per second captured into the buffer.", 1, 60),
            Float(OptionFamily.Setting, "rewindSpeed", "Gameplay", "Rewind speed", "Playback speed while rewinding.", 0, 10),
            Bool(OptionFamily.Setting, "rewindWidgetDisabled", "Gameplay", "Hide the rewind widget", "The on-screen rewind bar."),
            Bool(OptionFamily.Setting, "historyDisabled", "Gameplay", "Disable save history", "The automatic save-state history."),
            Bool(OptionFamily.Setting, "historyWidgetDisabled", "Gameplay", "Hide the history widget", ""),
            Bool(OptionFamily.Setting, "enhanceWidgetDisabled", "Gameplay", "Hide the enhancement widget", "The on-screen toggles for the enhancement layers."),
            Bool(OptionFamily.Setting, "autoLoadLastSaveState", "Gameplay", "Auto-load the last state", "Options > Auto Load State: resume where the game was left, every time."),
            Bool(OptionFamily.Setting, "lastSaveStateDisabled", "Gameplay", "Do not write the -last state", "Options > Disable Last State."),
            Bool(OptionFamily.Setting, "startAtQuick", "Gameplay", "Start at the quick-load menu", "Opens on the quick-load list rather than the main menu."),
            Bool(OptionFamily.Setting, "allowLoadFromTap", "Gameplay", "Load from a tap", "Touch and mobile oriented."),
            Bool(OptionFamily.Setting, "snesRumble", "Gameplay", "Rumble", "Vibration on supported pads."),
            Bool(OptionFamily.Setting, "rightStickGameSpeed", "Gameplay", "Right stick controls game speed", ""),
            Bool(OptionFamily.Setting, "swapAcceptCancel", "Gameplay", "Swap accept and cancel", "Menu navigation buttons."),

            Float(OptionFamily.Setting, "uiVolumeInv", "Audio", "Interface volume", "Stored as the emulator stores it (\"Inv\": its own scale, 0 to 1).", 0, 1),
            Float(OptionFamily.Setting, "gameVolumeInv", "Audio", "Game volume", "Same scale.", 0, 1),
            Float(OptionFamily.Setting, "msu1VolumeInv", "Audio", "MSU-1 volume", "Same scale.", 0, 1),

            Text(OptionFamily.Setting, "srmPath", "Files", "SRAM folder", "Where <rom>.srm goes. Empty: beside the ROM. {exec} is the emulator's folder, {persist} its data folder.", "beside the ROM"),
            Text(OptionFamily.Setting, "gameSavePath", "Files", "Per-game data folder", "Where <rom>.data.szsnes\\ (states, history, bookmarks) goes. Same tokens.", "beside the ROM"),
            Text(OptionFamily.Setting, "chtPath", "Files", "Cheats folder", "Where <rom>.cht goes.", "beside the ROM"),
            Text(OptionFamily.Setting, "bpsPath", "Files", "Patches folder", "Where <rom>.bps is looked for.", "beside the ROM"),
            Bool(OptionFamily.Setting, "noDirectoryForSaves", "Files", "No per-game folders", "Options > Disable SZSNES Folders: files beside the ROM rather than in <rom>.data.szsnes\\."),
            Text(OptionFamily.Setting, "loadPath", "Files", "Load dialog folder", "Where the emulator's own file browser opens."),
            Bool(OptionFamily.Setting, "disableFetchDrives", "Files", "Do not list drives", "Options > Disable Fetch Drives, in the file browser."),
            Bool(OptionFamily.Setting, "welcomeDialogShown", "Files", "Welcome dialog already shown", "True skips the first-run welcome dialog."),

            // ── GameSpecificSettings, for the game launched ──────────────────
            Bool(OptionFamily.Game, "disEnhanceHires", "Enhancements (this game)", "Disable hi-res redraw", "One of the eight enhancement layers a curated mod may carry."),
            Bool(OptionFamily.Game, "disEnhanceTextures", "Enhancements (this game)", "Disable texture / normal maps", ""),
            Bool(OptionFamily.Game, "disEnhance3D", "Enhancements (this game)", "Disable 3D (Mode 7)", ""),
            Bool(OptionFamily.Game, "disEnhanceAudio", "Enhancements (this game)", "Disable audio replacement", ""),
            Bool(OptionFamily.Game, "disEnhanceOC", "Enhancements (this game)", "Disable the mod's overclock", ""),
            Bool(OptionFamily.Game, "disEnhanceWide", "Enhancements (this game)", "Disable the mod's widescreen", ""),
            Bool(OptionFamily.Game, "disEnhanceBorder", "Enhancements (this game)", "Disable the border", ""),
            Bool(OptionFamily.Game, "disEnhanceGSU", "Enhancements (this game)", "Disable the Super FX enhancement", "Star Fox's 3D models."),
            Bool(OptionFamily.Game, "inaccurateEmuMode", "Enhancements (this game)", "Inaccurate legacy mode", "For old patches that relied on ZSNES's quirks."),
            Int(OptionFamily.Game, "overclock", "Enhancements (this game)", "CPU overclock (%)", "0 is stock.", 0, 400),
            Bool(OptionFamily.Game, "widescreenOverride", "Widescreen (this game)", "Force widescreen", "The emulator's own note: SNES games were not coded for it; for widescreen ROM hacks and the rare game where it works."),
            Int(OptionFamily.Game, "wideScreenBG", "Widescreen (this game)", "Background extension (tiles)", "0 to 7 tiles each side.", 0, 7),
            Int(OptionFamily.Game, "widescreenM7", "Widescreen (this game)", "Mode 7 extension (tiles)", "0 to 7.", 0, 7),
            Int(OptionFamily.Game, "widescreenOBJ", "Widescreen (this game)", "Sprite extension (tiles)", "0 to 7.", 0, 7),
            Int(OptionFamily.Game, "widescreenCOL", "Widescreen (this game)", "Colour-math extension (tiles)", "0 to 7.", 0, 7),
            Int(OptionFamily.Game, "aspectOverride", "Widescreen (this game)", "Aspect override", "The emulator's own index.", 0, 8),
        };

        /// <summary>Mehdi's choice of 01/10: these for every game, those per game, the others nowhere.</summary>
        private static readonly HashSet<string> GlobalKeys = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase)
        {
            "plugin.bepinex", "plugin.quit-confirm", "plugin.menu-key", "plugin.portable", "plugin.data-folders", "plugin.support-popup",
            "plugin.version-popup", "plugin.persist",
        };

        private static readonly HashSet<string> GameKeys = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase)
        {
            "plugin.display", "unity.screen-fullscreen", "unity.screen-width", "unity.screen-height", "unity.popupwindow", "unity.monitor",
            "native.loadstate",
            "setting.gfxMode", "setting.scanlineStrength", "setting.interpolationMode", "setting.noBilinearFiltering",
            "setting.maxBrightness", "setting.use87aspect",
            "setting.rewindDisabled", "setting.snesRumble", "setting.rightStickGameSpeed", "setting.swapAcceptCancel",
            "setting.uiVolumeInv", "setting.gameVolumeInv", "setting.msu1VolumeInv",
        };

        public static OptionScope ScopeOf(string iniKey)
            => GlobalKeys.Contains(iniKey) ? OptionScope.Global : GameKeys.Contains(iniKey) ? OptionScope.Game : OptionScope.Hidden;

        public static Option Find(string iniKey)
        {
            foreach (var o in All) if (string.Equals(o.IniKey, iniKey, System.StringComparison.OrdinalIgnoreCase)) return o;
            return null;
        }
    }
}
