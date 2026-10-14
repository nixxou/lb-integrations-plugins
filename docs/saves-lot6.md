# Saves, lot 6: the catch-all (Nixx-Saves)

Plan: `docs\saves-lb14-routing.md` §4b, row 6. Lots 4 and 5 put the front desk on LaunchBox's own integrations; this lot
covers the last case of §2's table: a game set to an emulator **no plugin drives** (one added by hand, one LaunchBox has
no integration for). LaunchBox asks only the plugin of a game's emulator about its saves (measured 07/10): such a game
has no Game Saves at all, so a save another emulator keeps for it (RetroArch, ares, through Launch With or before the
game moved) is never listed nor backed up.

## 1. What was known before (the save probe, 09-10/10)

- LaunchBox picks an emulator's plugin by asking every loaded plugin, emulator by emulator
  (`PluginRegistry.GetBestEmulatorPlugin` -> `GetApplicableEmulators`). A plugin that takes a hand-added emulator
  (`FakeEmulateur.exe`, the probe's CatchAll) then gets `SupportsSaveManagement` and `GetSaves` for its games, and its
  saves are recorded.
- The probe's CatchAll got `PluginInitialized` before any `GetApplicableEmulators` (the probe's log, 6 sessions). The
  SDK says that event is sent right after THIS plugin is loaded - not after all of them (review 13/10).
- **Measured in LaunchBox 13/10**: a plugin of `Local\Plugins` gets `PluginInitialized` only, never the start-up
  events; LaunchBox's start-up save scan asks the plugins 3 s later. So a question coming through LaunchBox's own
  `PluginRegistry.GetBestEmulatorPlugin` is answered at once (LaunchBox has loaded its plugins); only the pack's own
  calls wait for the registry to settle.
- `Unbroken.LaunchBox.Windows.Plugins.PluginRegistry` (read 13/10): a public type, members obfuscated; its property
  `EmulatorPlugins` (`ConcurrentDictionary<string, EmulatorPlugin>`) keeps its name, its ilspy body is a decoy (called by
  reflection, the real body runs, as everywhere in the core). **Measured in LaunchBox 13/10**: the static field of the type's
  own type stays null; the live registry is the instance field of `PluginRuntimeManager.Shared` (read by type).
- The SDK's own bodies (ilspy on `Unbroken.LaunchBox.Plugins.dll`): `PrepareEmulatorForLaunch` success with no new line
  (the pack's plugins return the same when they change nothing); `IsUpdateAvailable` would answer TRUE for an empty path
  with the first installable version; `RemoveSave` deletes the file.

## 2. The plugin (`src\NixxSaves`, folder `Nixx-Saves`, assembly `NixxSaves.dll`)

- **Which emulators** (`CatchAllClaims`): the ones no OTHER plugin LaunchBox loaded takes - each one asked
  (`LbPluginRegistry`, Shared.Compat), never in competition with one. Takes nothing:
  - outside LaunchBox (LiteBox asks every plugin itself), with `catchAll` or the desk off;
  - while LaunchBox may still be loading plugins: before LaunchBox/BigBox says its start-up is complete, only once the
    registry has held the same number of emulator plugins for 3 s (a plugin loaded after Nixx-Saves must be asked);
    nothing is kept from such an answer;
  - when the registry cannot be read (nothing taken on a guess);
  - an emulator another plugin takes, or whose plugin throws when asked;
  - an exe the pack or LaunchBox has an integration for (`retroarch`, `duckstation`, `ares`... by the start of the exe
    name, with the other names the pack accepts: `x.emu`, `reicast`, `nogba`...): a user who turned that plugin off in
    LaunchBox's plugin manager meant its emulator to have none;
  - an emulator without an exe.
  Kept 5 minutes per emulator (id + exe), and only while the registry holds the same number of plugins.
- **Its saves**: none of its own. Its desk (`SaveDesk`, emu `catchall`) lists the other emulators' saves of the game and
  hands every call on one to its owner, as the pack's plugins do (lot 3). `SupportsSaveManagement` true only when a plugin
  WITH saves covers a platform of an emulator it takes (lot 5's D6, `SaveRouter.OthersCoverPlatformsOf` with the
  catch-all's own rule for "its emulators").
- **A row nobody claims** (a card LaunchBox kept from before): the SDK's answers, except `RemoveSave` (asked first, No by
  default: `SlotRestore.MayDeleteUnclaimed`, lot 5) and `AddSaveFile` (refused: no folder to write to).
- **It never counts as a plugin with saves** for the others (`SaveRouter.CatchAllEmu`): neither in lot 5's D6 of
  DuckStation & co nor in a desk's listing of the other emulators.
- `IsPlatformSupported`: supported only on the platforms of the emulators it took, never recommended - "supported"
  everywhere could have LaunchBox offer Nixx-Saves where no plugin is recommended (review 13/10).
- **Nothing else**: the launch is the SDK's (untouched); no version, no installable version, no update (overridden: the
  SDK's body would offer one), no install, no BIOS, no RetroAchievements; no menu, no settings page.
- **Its ids**: none - every row it lists carries its owner's id. Nothing for lot 7 at uninstall.
- Compatibility page: `catchAll` ("Missing in LaunchBox", needs `desk`, in the "Saves of every emulator" card, off in safe
  mode - it reads LaunchBox's registry by reflection); checks `catchall.loaded`, `lb.pluginRegistry`, `sdk.saves`.
- Log: `%LOCALAPPDATA%\lb-integrations-plugins\nixx-saves.log` - each emulator taken or left once, with why.
- Packaging: `deploy-dev.ps1` (`-Plugin NixxSaves`), `build-release.ps1`, the setup's `Payload` (folder, assembly, files),
  `Installer.csproj`, the setup's list of plugins.

## 3. Measured / to measure

Probe `Probe <NixxSaves.dll> --catchall` (13/10, 29 checks, all green; the settling, the platforms, the names after the review): taken / left by every rule above, D6 both ways,
the listing (a fake plugin's save of the game listed with its id, its restore handed back to it), an empty listing a
success, an unclaimed row (asked: No keeps, Yes deletes; restore refused), the launch untouched, nothing to install or
update, no BIOS or RetroAchievements. `--saves-selftest`, `--stock-saves` green.

To see in LaunchBox (Mehdi; `Data` saved first):

1. **The probe's CatchAll set aside first** (`Local\Plugins\Nixx-SaveProbeCatchAll` out): else it takes FakeEmu and
   Nixx-Saves leaves it (correctly).
2. FakeEmu's game ("SaveProbe CatchAll Test") with a RetroArch or ares save made through Launch With: Game Saves lists it
   (through Nixx-Saves), a backup at the game's close, a restore into its own emulator's folder.
3. **Launch unchanged**: the FakeEmu game starts with its own command line (nothing in `nixx-saves.log` but the claim).
4. **Edit Emulator unchanged** for FakeEmu: no version, no update badge, no Download/Install button, no BIOS tab change.
5. LaunchBox's start: `nixx-saves.log` says the registry was read (`N emulator plugins`) and which emulators it took; no
   pack or stock emulator among them.
6. `catchAll` off, restart: FakeEmu's game has no Game Saves again.
7. **Add Emulator and the Import ROMs wizard** on a platform no plugin recommends (Nintendo 64...): Nixx-Saves is never
   offered (review 13/10: `IsPlatformSupported` now false there - to see that FakeEmu's game still gets Game Saves with it).
8. **Start-up time** with a big hand-added emulator on a platform RetroArch covers: its games go through RetroArch's
   listing at every start (as on lot 5's DuckStation).
9. The pack's logs after a session: no line repeated every few minutes about a free emulator.

### Measured in LaunchBox, 14-15/10 (G:\LB1326, `Data` saved in `Backups\lbip-before-lot6-2026-10-14\`)

- **Passed.** Point 5: at each start, the registry is read (20 emulator plugins); FakeEmu, the two SaveProbe emulators and
  two hand-made ones (`yt-dlp (2).exe`) are taken; no pack or stock emulator. Point 2, listing: FakeEmu's game lists the
  ares `.ram` through Nixx-Saves. Restore: "handed to ares", the file written in ares's folder, twice (to the first copy,
  then to `-01`). Point 3: FakeEmu starts as before. Point 4: no version, update or download in Edit Emulator. Point 7:
  Nixx-Saves not offered in Add Emulator for Nintendo 64.
- **No backup at the game's close.** The `.ram` changed while FakeEmu ran (content and time, same size), and no
  `[SaveBackup]` line at the close, twice, nor at the next start's automatic backup. A restore of the same card did make
  its precautionary copy ("content signature/timestamp changed"). So the comparison can see the change; the exit and
  start-up paths do not take it. LaunchBox's code for it cannot be read (its bodies are decrypted at run time). Next:
  the same change on a game whose emulator is a pack plugin (Zelda on ares) - if it fails too, it is every plugin of
  ours (no content signature: lot 3's F); if not, it is the catch-all's.
- **A row nobody lists is deleted by LaunchBox itself.** Delete Save on the old probe card: LaunchBox's own question
  ("This will permanently delete the save file...") and the file gone, with no plugin asked (no `RemoveSave` in any of
  the pack's logs). `MayDeleteUnclaimed` only runs when a plugin is asked; for a card no plugin lists, LaunchBox does
  not ask one. Its own question is explicit, so left as it is.

Known and left: with two taken emulators of one exe name in two folders, restoring a save nobody claims says "which
install" instead of "belongs to no emulator" (still refused).
