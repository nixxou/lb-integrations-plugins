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

## 2b. LaunchBox's save defects on 14.1 b3 (measured 16/10, LaunchBox alone)

Sandbox `tools\lb141-sandbox\sandbox.ps1` in `G:\LB14B3`, the pack moved aside (`-Action pack-off`), RetroArch twice
(snes9x and bsnes, one exe), made-up saves whose bytes say what they are. **Automatic Save Backups is OFF in a new
install**: until ticked, the close scans ("Scanning ... without creating backups") and backs nothing up.

| Test | 14.0.1 | 14.1 b3 |
|---|---|---|
| T1 Launch With: the save of the emulator actually used, at close | never asked | **fixed**: "Scanning ... with the Launch With emulator 'RetroArch bsnes'", backed up |
| T2 restore of another emulator's save (game on snes9x, save of bsnes) | written into the game's core folder | **fixed**: back into `saves\bsnes`, `AddSaveFile` given the emulator, the live file backed up first (`-01`) |
| T3 a state restored into another, occupied slot | that slot's state lost | **fixed**: "Slot 1 already contains ... LaunchBox will back it up and then replace it", `-01` in the vault |
| T4 Backup Save, a version on the game's own file | "Can't find the game for this save item!" | **fixed** |
| T5 the row of a game whose version runs the game's own file | tied to the version | **unchanged**: tied to "Play (USA) Version..." (listed and vault) |
| T6 a version on its OWN file, another emulator: which one at close | the version's | **fixed**: the game's (the one that ran) |
| T7 Make Default | game overwritten by the version, saves duplicated | **unchanged**: the game takes the version's path and emulator, the version stays (same id, same path), the game's own file is no longer referenced, the game's 2 save rows are copied onto the version (3 → 5) |
| Saturn trunk (`CopySaturnCompanionFiles`) | another group overwritten | **unchanged** (code identical, read) |
| A save taken into another card by its file name (`ApplyPersistedMetadata`) | present | **unchanged** (the page's bug test, 16/10) |

Consequences for the steps below: most of the pack's routing exists for T1/T2/T6 and is no longer needed on 14.1 for
LaunchBox's own integrations; the overwrite warning (T3) neither. What stays: T5's tie, the Saturn trunk, the merge by
file name, Make Default (LiteBox already follows the demoted rom), and everything about OUR plugins, which are v1 and still
get no emulator in `AddSaveFile`.

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
