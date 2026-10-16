# LaunchBox 14.1 - making the pack run on 14.0 and 14.1

Started 16/10, on LaunchBox **14.1 beta 3** (14.1.0.3, install `G:\LB14B3`) beside 14.0.1.2 (`G:\LB1326`). Branch `lb141`.

## 1. What changed (read 16/10)

**The SDK.** Nothing of 14.0.1.2 was removed or changed its signature: only nullable annotations and `[Obsolete]`
(`EmulatorPlugin` = "schema version 1 compatibility base", `IPluginPaths`, `IEmulatorWithCores`). Added: a "schema
version 2" model - `IEmulatorIntegration` plus optional capabilities (`IEmulatorSaveCapability`,
`IEmulatorLaunchPreparationCapability`, `IEmulatorRetroAchievementsCapability`, `IEmulatorSettingsCapability`...) - and
services injected in a plugin's constructor (`IAchievementManager`, `INotificationManager`, `IArchiveExtractionManager`,
`IPluginLogger`, data / secret / file stores). In v2, `AddSaveFile` gets the emulator (`EmulatorSaveAddRequest.Emulator`)
and a `DestinationFileLocation`. Reference copy: `vendor\lb-14.1-reference\`.

**Manifests.** LaunchBox's own are `SchemaVersion: 2` with `Components` (`ComponentType: "emulator/1"`, explicit
`Assembly` / `Class`, `Applications`) and `Compatibility.ApplicationVersionRange`. Ours (schema 1) still load from
`Local\Plugins`: the 15 plugins were constructed under 14.1 b3 (logs, 16/10 21:46).

**The core.** A v1 plugin is wrapped in `EmulatorPluginAdapter` (field of type `EmulatorPlugin`). `PluginRegistry` has
no `EmulatorPlugins` any more: `EmulatorIntegrations` (`ConcurrentDictionary<string, IEmulatorIntegration>`) and
`GetBestEmulatorIntegration` instead of `GetBestEmulatorPlugin`. The WPF view models, `Game.Saves`, `GameSave`,
`NotificationCenter`, `Strings.LabelLaunchWithMenu`, Big Box's `SelectOptionPopupViewModel` kept their names.

**LaunchBox's integrations.** All moved to v2 (RetroArch, Dolphin, PCSX2, MAME, ScummVM, Xemu, BigPEmu; storefronts and
RetroAchievements are new v2 plugins). RetroArch / Dolphin / PCSX2 restore now use the emulator they are handed
(`args.Emulator`), not the game's. RetroArch's `GetGameSaveDirectory` / `GetSaveStateDirectory` take a core name more;
Dolphin's `Restore*` take the v2 request first; `LoadConfiguration(string)` unchanged. `CopySaturnCompanionFiles` is
unchanged byte for byte (the trunk overwrite remains).

## 2. What it breaks in the pack (measured 16/10, pack 1.2.1 under 14.1 b3)

- StockDesk: "RetroArchPlugin not found ... not hooked" for every integration - the desk on LaunchBox's cards is off.
- Catch-all: "PluginRegistry has no EmulatorPlugins any more" - Nixx-Saves takes nothing.
- StockRestore: the statics' signatures changed - a restore through it is refused (with a message).
- The Compatibility page said green where it was not: a type not deriving from `EmulatorPlugin` read as "not loaded"
  (NotHere, hidden by an Ok beside it), and the statics were checked by name only.

Unchanged and working: row injection (Sqlite), the menus relay, the import wizard watch, `Process.Start` patch,
retroarch.cfg cache.

## 3. The rule

One build, against the 14.0.1.2 SDK. Each zone that differs picks its way ONCE, from `Shared.Compat\LbHostShape.cs`
(the shape of the host: native integrations' form, registry's form, the SDK's model) - never from a version number, so
an unknown shape is logged and switched off rather than taken down the wrong branch. V2 types are reached by name
(reflection, Harmony with `object` arguments). The v1 ways stay as long as 14.0 is supported. Every step is checked on
both installs.

## 4. Steps

0. Branch, `LbHostShape` (logged as `[host]` by every desk), this document, the 14.1 SDK for reference.
1. The Compatibility page tells the truth: a missing type in a loaded assembly = Missing; statics by exact signature;
   v2 variants of the checks; the host's shape shown.
2. Registry and catch-all: `EmulatorIntegrations` (adapter unwrapped by its `EmulatorPlugin` field, v2 integrations
   asked by reflection), `GetBestEmulatorIntegration` in `CatchAllClaims`.
3. Measurements on the clean 14.1 install (`G:\LB14B3\Backups\lbip-clean-14.1b3-2026-10-16`, no third-party plugin):
   Launch With saves, which emulator a restore gets, state into an occupied slot, Backup Save with a version on the same
   file, which card a version on the same file gets, which emulator a version on its own file gets at close, Make
   Default, Saturn trunk, empty slot list, companions, RetroArch states without `-L`, merge by file name.
4. StockDesk V2: instances from the registry, hooks on `IEmulatorSaveCapability`, `Verify` extended - its scope cut
   down by what step 3 finds fixed.
5. StockRestore: off on 14.1 if LaunchBox hands the right emulator; else a V2 on the new signatures.
6. Workarounds gated by `FixedIn` from step 3 (overwriteWarning, foreignSlots, planA, retroarchView, companions).
7. Our plugins under 14.1: Add Emulator's Download button (`IsUpdateAvailable` now goes through
   `EmulatorUpdateScope`), RetroAchievements credentials at launch, Launch With, Big Box popup, import wizard, menus.
8. Probe (`--stock-saves`, `--catchall`) against both installs; release 1.3.0.
9. Reports to Unbroken: Saturn trunk, whatever step 3 still finds, API asks (persisted save rows, GetSaves for every
   emulator of a game, menu roles from `Local\Plugins`).

Later, at 14.1 stable: move our plugins to v2 (minimum 14.1).
