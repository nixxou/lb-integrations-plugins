# Nixx settings: a "Compatibility" page (plan, 09/10)

Asked by Mehdi on 09/10, after lot 3. Not started. This file is the plan and the texts the page will show.

## 1. What the page is for

The pack works around LaunchBox in several places: things LaunchBox does not do (a game's saves from several
emulators), LaunchBox bugs we route around (a save taken into another card by its file name), and safeguards against
cases we know go wrong (an overwrite LaunchBox will not back up). Each of those is a **patch**. The page:

- lists every patch, grouped by kind: **Missing in LaunchBox**, **LaunchBox bug worked around**, **Safeguard**;
- explains each one in a text the user can unfold: the problem, an example, what the patch does, what happens without it;
- lets each one be turned off (all on by default), so a patch can be dropped the day LaunchBox fixes the bug;
- shows, for each, whether what it relies on is still there (**reflection: OK / missing X**) and, where it can be
  tested without touching anything, whether the bug is still there (**bug: still present / fixed / not testable**).

## 2. How it behaves

- **Nothing applies at once.** The checkboxes are a draft; **Apply** writes them, and a banner says *"Restart
  LaunchBox (or BigBox) for this to take effect."* Same rule for every change of the page, now and later (some will
  need a restart anyway). **Cancel** / closing without Apply drops the draft.
- **The plugins read the choices once, at start-up** (each plugin carries its own copy of the shared code): no cache,
  no patch changing state in the middle of a session.
- **Storage:** one file for the whole pack, `Local\Plugins\.data\nixx-compat.ini` (as `identity.ini`):
  `[patches]` `desk=on`, `planA=on`, ... A missing key = on. Written only by Apply.
- **Dependencies** are shown: a patch that needs another is greyed out with *"needs: Front desk"* when that one is
  off. Turning off a patch that protects saves asks for a confirmation (*"Without it, ... can be lost. Turn it off?"*).
- **The checks run when the page is opened**, in the background, read-only, each under a time limit; never at start-up.
  Their result is shown with the date LaunchBox's version was last checked by hand.
- Under LiteBox the page says the desk patches are not used there (LiteBox does it itself) - LiteBox has its own.

## 3. The patches and their texts

Each text has four parts: **The problem**, **Example**, **What the patch does**, **Without it**.

### Missing in LaunchBox

**Saves from every emulator of a game** (`desk`, on)
- *The problem.* LaunchBox only asks the emulator a game is set to about its saves. A save made by another emulator
  (with "Launch With", or a game moved from one emulator to another) is never shown, never backed up, and a restore
  writes it into the folders of the game's emulator.
- *Example.* Zelda is set to SUPER ZSNES. You play it once with ares (Launch With). Game Saves only shows SUPER ZSNES's
  save; ares's is never backed up when the game closes.
- *What the patch does.* When the game's emulator is one of the pack, its plugin also lists the saves of every other
  emulator that covers the platform (the pack's and LaunchBox's RetroArch, Dolphin, PCSX2), each with its own card and
  its emulator's name, and sends every action (backup, restore, delete) to the emulator that made the save.
- *Without it.* LaunchBox's own behaviour: only the game's emulator's saves.
- Checks: none (plain SDK). Bug test: none (a missing feature).

**RetroArch's states on a game of the pack** (`retroarchView`, on, needs `desk`)
- *The problem.* RetroArch's integration finds its states folder from the core on the GAME's command line. On a game
  set to one of the pack's emulators there is no RetroArch core there: no folder, no state.
- *Example.* Zelda is set to SUPER ZSNES, you saved a state in RetroArch (Snes9x core): its saves show in Game Saves,
  its states do not, and restoring one writes nowhere.
- *What the patch does.* When it asks RetroArch, the pack shows it the game as RetroArch would run it (its own command
  line for that platform); everything else is the real game.
- *Without it.* RetroArch's states of such games are not listed.
- Checks: `IGame.GetEffectiveCommandLine`, RetroArch's `GetSaveStateDirectory`. Bug test: call it with a game that has
  no `-L` - no folder means the bug is still there.

**A state of another emulator on a game whose emulator has no slots** (`foreignSlots`, on, needs `desk`)
- *The problem.* LaunchBox asks the GAME's emulator which slots a state can go to, and refuses every state restore when
  it has none.
- *Example.* An Xbox game set to Cxbx (no states) shows xemu's states; restoring one: "doesn't appear to support save
  state restoration".
- *What the patch does.* Such an emulator offers slots 0-9; the state's own emulator then takes the slot or refuses it
  with its own numbers ("xemu keeps each savestate in its own slot...").
- *Without it.* Those states cannot be restored from LaunchBox.
- Checks: none. Bug test: not testable without a restore.

### LaunchBox bug worked around

**A save taken into another card** (`planA`, on)
- *The problem.* When a save is shown that LaunchBox has not recorded at the same place, LaunchBox looks for a backup of
  the same game with the same FILE NAME and puts the save into that backup's card - whatever emulator made it. It also
  records a game's saves on the version that runs the game's own file, and then counts the recorded copy as an extra
  backup.
- *Example.* Secret of Mana: RASnes9x's "Secret of Mana (USA).srm" has the same name as SUPER ZSNES's backup; it vanished
  from Game Saves, shown inside SUPER ZSNES's card; ares's card said "1 Backups" with none.
- *What the patch does.* Each save shown is recorded at its own place, on the same version LaunchBox would use, so
  LaunchBox finds it and never matches it by name; leftover duplicates of the pack's own rows are removed; a save that
  carries its card answers with it.
- *Without it.* Saves of different emulators with the same file name can disappear into one another's card; backup
  counts can be wrong.
- Checks: `Game.Saves` / versions' `Saves` (`BlockingCollection<GameSave>`, `Add`, `TryTake`), `GameSave`'s properties.
  Bug test: LaunchBox's own matching function on two made-up saves of the same name - taken = still there.

**RetroArch and Dolphin restores from another emulator's game** (`stockRestore`, on, needs `desk`)
- *The problem.* LaunchBox's RetroArch and Dolphin integrations restore into the folders of the GAME's emulator, not of
  the emulator that made the save.
- *Example.* Restoring RetroArch's Zelda save while Zelda is set to SUPER ZSNES: RetroArch finds no core and no folder.
- *What the patch does.* The pack runs their own restore steps with the emulator the save came from (and, with two
  installs of the same emulator, the one the save's card lives in - or refuses when nothing says which).
- *Without it.* Those restores fail or write in the wrong place.
- Checks: their private functions (`GetGameSaveDirectory`, `GetSaveStateDirectory`, `RestoreSaveState`, ...). Bug test:
  none (a design of theirs).

**A change in a save's companion file not seen** (`companionSignature`, on)
- *The problem.* To decide whether a save changed, LaunchBox only compares its main file, not the files that go with it
  (a cartridge's clock .rtc, Satellaview data .psr...).
- *Example.* Only the clock of a RASnes9x save changed: LaunchBox says "backed up" and refuses Backup; the new clock is
  never kept.
- *What the patch does.* The pack's saves with companions get a fingerprint over all their files.
- *Without it.* A change in a companion alone is never backed up.
- Checks: none. Bug test: LaunchBox's default fingerprint on two temporary sets that differ by the companion only.

**Companion files not put back at a restore** (`companionRestore`, on)
- *The problem.* LaunchBox copies a save's companions into the vault at a backup, but none back at a restore.
- *Example.* Restoring a RASnes9x save: the .srm comes back, the .rtc stays the one it replaced.
- *What the patch does.* The pack's restore puts the backup's companions back beside the save; it first checks it can
  write every file, so a restore is never half done.
- *Without it.* A restored save can come back with another moment's clock.
- Checks: none. Bug test: not testable without a restore.

### Safeguard

**An overwrite LaunchBox will not back up** (`overwriteWarning`, on; turning it off asks for a confirmation)
- *The problem.* Before a restore LaunchBox backs up the restored card's own file, not the file that will be
  overwritten. They differ when a state goes into another slot, or when a card without a live file (a manual import)
  is restored over another card's save: that file is replaced and kept nowhere.
- *Example.* You restore the backup of slot 2 into slot 1, which holds another state: the old slot 1 is gone.
- *What the patch does.* In LaunchBox (not BigBox: there the restore is refused with a message), the pack asks
  "Slot 1 already holds another save... Replace it anyway?" - only when the target exists, belongs to another card and
  differs. Lifted automatically from the LaunchBox version that fixes it.
- *Without it.* LaunchBox's behaviour: replaced without a word.
- Checks: LaunchBox's version against the version marked fixed. Bug test: not testable without a restore.

Not on the page (always on, they only refuse what would be wrong): a slot the emulator does not have, a restore whose
install is ambiguous, a restore that cannot write all its files.

## 3b. Changes to LaunchBox's data and behaviour (Mehdi, 09/10: "the database changes and the rest")

Inventory of 09/10 (every plugin of `src\`): **no plugin writes LaunchBox.Metadata.db or `Data\*.xml` itself** - the
metadata database is only read (`LbipMetadataDb`, `AresCompat`; writing it was rejected, Unbroken refreshes it daily,
`FlycastAssociation.cs`); every library change goes through `IDataManager` and reaches the XML when LaunchBox saves.
The page lists them too, in a fourth group, **Changes to your LaunchBox data**, and a fifth, **Hooks into LaunchBox**.
Where a plugin already has its own switch, the page shows it and writes the same setting (one value, not two).

### Changes to your LaunchBox data

**The emulator entry an install creates** (always; part of installing)
- *What.* `AddNewEmulator` + one platform row per system (ares: one per system with its `--system` line), default only
  when no other emulator holds that platform; a reinstall updates the exe path only; Flycast reuses an existing entry and
  adds only the platforms it lacks.
- *Why.* LaunchBox's own integrations build their entry inside the core; the SDK way also works under LiteBox.
- Shown for information, no switch (no install without it).

**Fields filled on an emulator the plugin drives** (`emulatorFields`, on)
- *What.* AutoHotkey exit and savestate scripts (only blank fields, or a script the pack wrote before - the old one sent
  Alt+F4 to whatever window had the focus); ares's platform lines left empty get `--system "<x>"`; Cxbx's entry pointed at
  `cxbxr-ldr.exe`; xemu's exe path followed after its rename (else LaunchBox's own xemu plugin takes the entry back); a
  hand-made Flycast entry gets its missing platform rows.
- *Example.* An emulator added by hand for ares, without a command line: each game would start on the wrong system.
- *Without it.* Those fields stay as they are; the plugin works with what it finds (some games may not start right).

**"Extract archives" forced** (`autoExtract`, on)
- *What.* Off for Cxbx, Xenia, xemu (the plugin unpacks itself, to a RAM disk when it can - LaunchBox extracting first
  would write the whole image to disk for nothing); on for no$gba (it cannot open an archive: "Cartridge not found").
  Overrides the user's setting, on purpose.
- *Without it.* The user's setting is kept, with the costs above.

**Games and versions corrected after an import** (per plugin, already switches)
- *What.* Flycast adds the arcade sets LaunchBox dropped as versions of the right game (vf4b, clubkprz...,
  `RepairImport`); Xenia, Vita3K, Cxbx and PPSSPP set the title / region / version read from the game itself instead of
  LaunchBox's guess (`import_title`, `import_region`, `ImportRegionVersion`...).
- *Example.* A Vita game imported with "Version: (NoNpDrm) [v1.03]" taken from its file name.
- *Without it.* LaunchBox's import as it is.

**A game's command line moved to the plugin** (MelonDs, Vita3K options windows; on OK only)
- *What.* The plugin's own flags, if still in the game's command line, are moved into the plugin's store.
- Shown for information (only on the user's OK).

### Hooks into LaunchBox

**Pack emulators in the "Add emulator" list and the platforms grid** (`rowInjection`, on - today the marker file
`%LOCALAPPDATA%\lb-integrations-plugins\no-metadata` turns it off; the page replaces it)
- *What.* A Harmony hook on LaunchBox's SQLite reader adds the pack's emulator rows (and Flycast, standalone DS
  emulators...) to what LaunchBox reads from LaunchBox.Metadata.db - in memory, the database is never written.
- *Why.* Those emulators are not in LaunchBox's database: nothing to pick, no platforms to narrow.
- *Without it.* The pack's emulators are missing from those lists (they still work once set up).
- Checks: `Microsoft.Data.Sqlite.SqliteCommand.ExecuteDbDataReader` (and its async twin) found. Bug test: LaunchBox's
  database still lacks "Flycast" (a read).

**Knowing when an emulator is opened without a game** (`emulatorOpened`, on)
- *What.* A Harmony hook on `Process.Start` (only for an exe started by LaunchBox's "Open emulator" menu): the plugins
  learn it was opened and when it closes; Cxbx opens its GUI instead of the bare loader.
- *Why.* The SDK has no such event (measured 29/09).
- *Without it.* Opening Cxbx from LaunchBox starts the loader alone; the plugins do not know the emulator ran.
- Checks: `Process.Start()` patchable, `OpenEmulatorMenuAction` on the stack.

**The import wizard steered** (per plugin, mostly already switches)
- *What.* WPF class handlers + reflection on the wizard's view models: Vita3K and Xenia swap the platform for a "Temp"
  one during the scan and write the real one back (`BypassVitaImport`, ...); Vita3K, Xenia, Cxbx and PPSSPP remove
  updates, DLC, firmware, save data and foreign files from the list; Flycast and Demul keep only their arcade sets and
  tick "Force using MAME metadata" (`QuickCheck`, `SortImport`...). A watcher notices the files LaunchBox drops at the
  end (read only).
- *Example.* Choosing the Vita platform made the wizard list the games installed in Vita3K and ignore the files picked.
- *Without it.* LaunchBox's wizard as it is.
- Checks: each view model and property by name (`WizardViewModel`, `RomImportGameListViewModel`, ...) - the obfuscated
  core renames nothing public today, but a version that does breaks the steering silently: the label says so.

Not hooks, not on the page: the launch gate (a second launch refused while a game runs, SDK only); files the plugins
write in the emulators' own folders (BIOS copies, state thumbnails) and in `Local\Plugins\.data`; the installer (a
separate program, LaunchBox closed).

## 4. To build

1. `Shared.Compat` (compiled into every plugin and the menu relay): the registry (id, kind, texts, default, needs,
   checks, bug test), `Compat.Enabled(id)` read once at start-up from `nixx-compat.ini`.
2. Each patch behind its `Compat.Enabled` (the off branch = LaunchBox's behaviour, already the code's fallback).
3. The page in `NixxSettingsForm` (beside "Your console" and "RAM disk"): groups, a row per patch (checkbox, title,
   status labels, unfolding text), Apply / Cancel, the restart banner, confirmations, dependencies.
4. The checks and the bug tests (merge by name, RetroArch's folder, the default fingerprint, Flycast still missing from
   the metadata database), run on opening; the hooks' checks (the SQLite reader, `Process.Start`, the wizard's view
   models by name).
4b. The plugin switches that already exist (import cleaning, titles, regions, Vita bypass, Flycast repair...) shown and
   written from the page through the plugins' own settings; the `no-metadata` marker file replaced by `rowInjection`.
5. Probe: the registry complete (every id has its texts and a default), the ini read/write, each check against the
   current LaunchBox (all OK), each bug test (all "still present" on 14.0.1.2).
6. Real test: turn `planA` off, Apply, restart: Secret of Mana's RASnes9x save is taken again into SUPER ZSNES's card;
   on again, back to its own.
