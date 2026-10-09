# Saves, lot 3: the front desk - everything measured, decided, built and left to do

State on 09/10/2026, branch `savegroupid` of `lb-integrations-plugins`, last commits 1c8192c and 97fd74f (nothing pushed).
Reference measures and the plan of every lot: `docs\saves-lb14-routing.md` (cited below as "routing §n"). This file is
the lot's own record: what LaunchBox does, what we built, what we tried and dropped, the bugs (ours and LaunchBox's),
the slot work, and what is still to do, with how to test it.

Test installs: `G:\LB1326` (LaunchBox 14.0.1, SDK 14.0.1.2, our pack deployed, the save probe), `G:\LB` (14.0.1.2,
used on 09/10 for the plugin-free repro). Logs of our plugins: `%LOCALAPPDATA%\lb-integrations-plugins\<plugin>.log`.
LaunchBox's debug log: `<install>\Logs\Debug <date>.log` (on in G:\LB1326 only).

---

## 0. In one page

- **The problem.** LaunchBox 14 asks only the plugin of the game's emulator about saves (listing, close, restore). A
  save made by another emulator (Launch With, a version on another emulator, a second RetroArch entry) is never
  listed, never backed up, and a restore writes it into the game emulator's folders.
- **The answer (lot 3).** When the game's emulator is one of ours, its plugin is a **front desk**: it lists the saves
  of every save-capable plugin covering the platform (ours and LaunchBox's RetroArch / Dolphin / PCSX2) and hands
  every call on a row to the row's owner, read from the row's id.
- **Status: lot 3 CLOSED on 09/10** (branch `savegroupid`, not pushed). Everything below measured in LaunchBox
  (G:\LB1326) unless said:
  - listing, exe names, backup at close, restores by forwarding (ares, RASnes9x, SUPER ZSNES, xemu) and by our own
    RetroArch restore (saves and states), on games with and without a version on their file;
  - card hijack and wrong backup counts fixed (plan A, 1c8192c, twins removed 0f6808c);
  - state slots: the pick applied, a slot the emulator lacks refused, the Yes/No warning, xemu "Its own slot", PPSSPP
    keys 1-5, 0-9 on desks without slots (§6);
  - RetroArch's states on a game of ours (`SaveGameView`), four emulators' states on one game (§6.6c);
  - companions: in the signature, put back at a restore, all destinations tried before writing (§7);
  - the Codex review's two findings, fixed and measured before/after (§9b); startup cost +0.35 s (§11);
  - the sandboxes cleaned (§12).
  - every patch switchable from the **Compatibility** page of the Nixx settings (5f215e2, `docs\compat-page-plan.md`):
    `Shared.Compat` registry and texts, choices read at start-up from `Local\Plugins\.data\nixx-compat.ini`, reflection
    checks and two bug tests; probe 11 checks. Not tried in LaunchBox (Mehdi, 09/10: taken as good).
  - **Not measured, set aside:** Dolphin and PCSX2 through the desk (none of our plugins covers GameCube, Wii or PS2:
    the desk never lists their saves today); Flycast arcade sets of several files (the cartridge games at hand write one
    file); the startup cost on a big library (before lot 4).
- **LaunchBox bugs met** (not ours, reports written): manual Backup Save on a game imported with versions; a state
  restored into another occupied slot is lost; Make Default (to replay with RetroArch first). §9.

---

## 1. What LaunchBox 14 does (measured, the facts the desk is built on)

### 1.1 Who is asked

| Moment | Call path | Plugins asked |
|---|---|---|
| Start-up scan, selection scans | `DataManager.UpdateGameSaves` | every plugin, with the games of **its own** emulator only, if `SupportsSaveManagement()` |
| Game closed | `GameTimeTracker` → `SaveBackupManager.BackupActiveSave` | **the game's emulator only** (assigned, else the platform default). Launch With another emulator: that one only gets `PrepareEmulatorForLaunch` |
| Game Saves opened | `AddEditGameSavesPageViewModel.OnPageEnter` | the game's emulator only, also for cards persisted under another emulator's name |
| Restore | `RestoreSaveCommand.Execute` → `AddSaveFile` | the **game's** emulator |

`EmulatorFileName` is stored on every row and shown on the card but **never routes a call**; it holds the exe's FILE
NAME only (`retroarch.exe`), measured 08/10.

### 1.2 Calls and their order

- **Close**: `SupportsSaveManagement`, `GetSaves`, then per row `TryGetSaveGroupInfo`, `IsSaveContainer`,
  `GetCompanionSaveFiles`; LaunchBox copies a file save itself (`TryBackupSave` only for a container);
  `TryComputeSaveSignature` (live vs vault) decides whether anything changed.
- **Game Saves**: `GetSaves`, `TryGetSaveGroupInfo`, `IsSaveContainer`, `IsSecondarySaveFile`,
  `TryComputeSaveSignature`, `GetCompanionSaveFiles`, and **`IsSaveActive` hundreds of times** (82, 342, 176 per
  opening). It must stay a string comparison.
- **Restore of a state**: `GetPotentialSaveSlots` (asked at each restore, never cached), then `AddSaveFile`. The
  precautionary backup is LaunchBox's own copy (`-01`), companions included.
- `SupportsSaveManagement` false: no more `GetSaves`, but persisted rows stay on screen and the game's plugin is still
  asked about each of them.
- Stack of a tile's construction: `OnPageEnter → PopulateSaveGroups → SaveGroup.AddVersion → SaveItem.IsActive`;
  `SaveItem..ctor` calls `TryGetSaveGroupInfo`.

### 1.3 Rows, cards, vault

- Rows are `<GameSave>` elements of `Data\Platforms\<platform>.xml`. In memory: `Game.Saves` and
  `AdditionalApplicationBase.Saves`, `BlockingCollection<Unbroken.LaunchBox.Data.GameSave>` (in
  `Unbroken.LaunchBox.dll`). `GameSave` fields: `GameId`, `AdditionalApplicationId`, `EmulatorFileName`, `EmulatorCore`,
  `Title`, `SaveGroupName`, `DisplayChipText`, `SaveGroupId`, `MatchLineageId`, `MigrationFamilyId`, `FilePath`,
  `OriginalFileName`, `Slot`, `ReportedFileSizeBytes`, `ReportedLastModifiedUtc`, `Md5`. `DataManager` also has
  `Save(bool)`, `UpdateGameSaves`, `PopulateSaveMetadata(GameSave, GameBase, AdditionalApplicationBase)` (static),
  `RepairGameSaveMetadata`.
- **A card IS a `SaveGroupId`** (tests 10-12, routing §1): every row with that id is one card and its Backup History.

  | The listing gives | Result |
  |---|---|
  | a save never seen, at the start-up scan | listed, no row created (rows come from Game Saves or a close) |
  | the same file under another id | found by its PATH, the old id kept |
  | the same id from another folder | a new row in the SAME card |
  | another id AND another folder | a new card |

  `UseSaveGroupIdForPersistedMatch` was never called by LaunchBox on the probe.
- **LaunchBox persists a game save's row on the version that runs the game's own file** ("Play Version..."), even when
  the plugin listed it with no version (measured 09/10, §5).
- Vault: `Saves\<platform>\<original name>`; a taken name gets `-01`, `-02`, **whichever card the other copy belongs
  to** (measured 08/10: SUPER ZSNES's Zelda `.srm` became `-02.srm` beside RetroArch's `.srm` and `-01.srm`). It never
  overwrites. Some states are renamed `<stem>.state` (ares `.bs1` → `Sonic CD (U).state`; RetroArch's slot-2 state →
  `Rolan's Curse (USA).state`, no slot in the name).
- At backup, LaunchBox sets the vault row's `OriginalFileName` from the live file's name; the plugin's value is ignored.
- `SaveGroupId` survives everything (1 938 characters, any Unicode, `#`/`|` escaped): identical byte for byte after the
  vault, a close, a restart and a restore.

### 1.4 Plugin loading facts that shaped the code

- **A plugin must derive from `EmulatorPlugin` directly.** With an abstract intermediate base (`NixxSavePlugin`), the
  DLL loads, no plugin is constructed, and nothing is logged (08/10).
- **Each plugin carries its own copy of the shared code**: `static` and `[ThreadStatic]` are per plugin. Only a named
  thread slot (`Thread.GetNamedDataSlot`) is process-wide.
- **The core is obfuscated** (decoy bodies, no Harmony), but **reflection runs the real body** (also used for the Vita
  import). LaunchBox's integrations (`System\Plugins\`) are not obfuscated.

### 1.5 How LaunchBox's integrations find their emulator at restore (decompiled 08/10)

`AddSaveArgs` carries the row only.

| Integration | Finds the emulator by | From our desk |
|---|---|---|
| RetroArch | the GAME's emulator (`game.EmulatorId` or the version's), core from the game's command line | wrong folders → the desk restores it itself |
| Dolphin | the GAME's emulator (`TryResolveContext`), `BuildUserRoots(emulator)` | wrong folders → the desk restores it itself |
| PCSX2 | `save.EmulatorFileName`, an exe NAME matched against the library, first found | fine once the row carries the exe → plain forward |

A future Nixx-RetroArch or Nixx-Dolphin shares the exe name: never route on the exe alone, never on the game's emulator
alone; the owner comes from the id (`retroarch` vs `lb.retroarch`), the exact emulator from `EmulatorCore` + platform.

### 1.6 Which save methods each integration implements (13 in the SDK)

| Method | RetroArch | Dolphin | PCSX2 | Ours |
|---|---|---|---|---|
| `SupportsSaveManagement`, `GetSaves`, `AddSaveFile` | yes | yes | yes | 12 |
| `GetPotentialSaveSlots` | yes | yes | — | 9 (not Cxbx, xemu, Vita3K) |
| `RemoveSave` | — | yes | yes | 12 |
| `IsSaveActive`, `IsSaveContainer` | — | — | yes | 12 |
| `TryBackupSave` | — | — | yes | 9 |
| `UseSaveGroupIdForPersistedMatch` | yes | yes | yes | 9 |
| `IsSecondarySaveFile`, `GetCompanionSaveFiles` | yes | — | — | 12 |
| `TryComputeSaveSignature` | yes | — | yes | **none** |
| `TryGetSaveGroupInfo` | yes | — | — | the desk (since 1c8192c) |

---

## 2. The front desk as built

### 2.1 Files (`src\Shared.Saves\`, compiled into each of the 12 save plugins)

| File | Role |
|---|---|
| `SaveId.cs`, `SaveOwner.cs` | lot 1: the JSON id (System.Text.Json only) and the owner read from an id |
| `SaveRows.cs` | `IsOurs`, `SameId`, `TieTo`, `ChooseTarget`/`TargetFor`, `With(row, exe, core)`, `WithVersion(row, versionId)` |
| `SaveDesk.cs` | `IOwnSaves` (the plugin's former save code, methods renamed `Own*`) and `SaveDesk`, the object every override delegates to |
| `SaveRouter.cs` | registration, LaunchBox's integrations, owner lookup, the other emulators' rows |
| `StockRestore.cs` | RetroArch and Dolphin restores with the row's emulator (their private statics, by reflection) |
| `SavePersist.cs` | plan A: rows tied as LaunchBox ties them, persisted rows created and moved (§5) |
| `src\<Plugin>\<Plugin>SaveDesk.cs` | generated: one override per save method → `_desk`, base fallback for missing `Own*` |

Each plugin's constructor starts with `_desk = new SaveDesk(this, this, SaveEmu, SaveLog);` - composition, not a base
class (§1.4). `<Plugin>Saves.cs` implements `IOwnSaves.Own*` explicitly, with private `SaveEmu` / `SaveLog`.

### 2.2 Registration and discovery

- `SaveDesk..ctor` → `SaveRouter.Register(emu, plugin)` into `LbSaveOwners` (in the embedded `LbIntegrations.Catalog`,
  refreshed in be6ee73), guarded by `NoInlining` + `try`: an old Catalog only costs the desk. Logs
  `front desk on for <emu>`.
- `SaveRouter.Stock()` finds LaunchBox's integrations among the loaded assemblies of `System\Plugins\` and instantiates
  them (RetroArch needs `IPluginPaths`).
- `DriverOf`: a plugin counts for an emulator only if its `GetApplicableEmulators` returns **that exact** emulator.
- Off under LiteBox (`HostWillAsk`): LiteBox asks every plugin itself.

### 2.3 Owner of a row

1. Our JSON → `emu` → the registered Nixx plugin.
2. A known native form (`pcsx2:…` → `lb.pcsx2`, `saturn-…` → `lb.retroarch`, a translated JSON with `native`).
3. Else `EmulatorFileName` + `EmulatorCore` against the library (last resort; RetroArch GUID ids land here).

`OwnerOf` caches the route under `selfEmu|id|exe|core|gameId`; an unregistered Nixx owner is not cached (it may register
later). `OwnerOfPath` finds the owner by the emulator's folder, for the calls that carry only a path
(`IsSecondarySaveFile`, `GetCompanionSaveFiles`). A row whose id starts with the desk's own prefix is answered by the
plugin's own code without a lookup.

### 2.4 `GetSaves`

1. The plugin's own rows (`OwnGetSaves`), each given the plugin's exe name when it has none (`SaveRows.With`).
2. `SaveRouter.OthersFor`: for each emulator of the library covering the platform whose plugin manages saves, its rows
   for the game, with **that** emulator's exe and core (`CoreIn` reads `-L "cores\x_libretro.dll"` → `x_libretro`).
   Dedup by **full path** (`FullOf`), so a file reached by two emulators (RetroArch with two cores) is listed once.
3. Nesting guard: the named thread slot `LbIntegrations.Saves.DeskDepth`; a desk asked from inside another desk lists
   only its own rows.
4. One game on screen: `SavePersist.TieLikeLaunchBox` on both lists, then `SavePersist.Ensure` (§5).
5. One game or fewer: every row handed back is logged (`row save|state <n> <path> exe=… app=… id=…`).

### 2.5 The other calls

| Method | Routing |
|---|---|
| `IsSaveActive`, `IsSaveContainer`, `UseSaveGroupIdForPersistedMatch`, `TryComputeSaveSignature`, `TryBackupSave`, `RemoveSave` | by the row's owner; own rows to `Own*` |
| `TryGetSaveGroupInfo` | a row carrying a `SaveGroupId` answers it (true, its id and name); else by owner |
| `IsSecondarySaveFile`, `GetCompanionSaveFiles` | by path (`OwnerOfPath`) |
| `AddSaveFile` | `lb.retroarch` / `lb.dolphin` → `StockRestore` with the row's emulator; `lb.pcsx2` and Nixx → forward to the owner |
| `GetPotentialSaveSlots` | **not routed** - it takes no argument (§6) |

A call that throws in the owner returns a safe fallback (`Ask`) and is logged.

---

## 3. Real tests passed

### 3.1 Zelda: A Link to the Past (SNES, SUPER ZSNES's desk), 08/10

| Card | Listed with | Backup | Restore |
|---|---|---|---|
| ares save `.ram` | `ares.exe` | OK | forwarded to ares, byte for byte |
| ares state `.bs1` | `ares.exe` | OK | forwarded to ares, byte for byte (after the `IsSaveActive` fix) |
| RetroArch `.srm` | `retroarch.exe (snes9x_libretro)` | OK | `StockRestore` with the right emulator, byte for byte |
| SUPER ZSNES `.srm` | `SUPERZSNES.exe` | — | never touched |

### 3.2 Secret of Mana (SNES, SUPER ZSNES's desk), 08-09/10

Four emulators listed (RASnes9x, SUPER ZSNES, ares save + state, RetroArch) plus ares's France version
(`4d33e65b…`). After plan A: four separate cards, counts RASnes9x 0, SUPER ZSNES 2, ares 0, RetroArch 0, ares state 0,
at the first and second opening and after a restart; one persisted row per path. Restore of SUPER ZSNES's `-01`:
the live file took its content (51C6A54C); LaunchBox skipped its precautionary copy (live identical to a vault copy).

### 3.2b Zelda again, clean build, 09/10

- Plan A on a game WITHOUT a version on its file: four cards (ares save 2, SUPER ZSNES 1, RetroArch 2, ares state 3),
  each with its exe - nothing broken.
- A save never seen, at a close, without opening Game Saves (measure 5): a RASnes9x `.srm` for Zelda (same file name as
  the SUPER ZSNES and RetroArch vault copies) - at the close LaunchBox created its row under `{"emu":"rasnes9x",…}` and
  backed it up at once (`-04.srm`, same id). No hijack.
- Manual import and its restore: §8.

### 3.3 Lot 2 on the way (08/10)

JSON cards and backups for SUPER ZSNES, melonDS DSiWare, Flycast arcade, PPSSPP state, xemu save + states, Xenia;
restores byte for byte for SUPER ZSNES, Flycast, PPSSPP (Mahjong Fight Club, slot 0).

### 3.4 Probe

`Probe --saves-selftest` green: lot 1's 51 checks plus the desk's (our prefix, the core, row copies, `WithVersion`,
`StockRestore.Handles`).

---

## 4. Bugs of ours found and fixed during the lot

| Bug | Cause | Fix |
|---|---|---|
| No plugin constructed at all | abstract intermediate base | composition (`SaveDesk` + `IOwnSaves` + generated overrides), d9a7234 |
| ares files labelled `RASnes9x.exe` | `[ThreadStatic]` guard per plugin: RASnes9x's desk re-listed through ares's | named thread slot, f4a651f |
| A card showed another row's exe (`retroarch.exe (snes9x_libretro)` on SUPER ZSNES) - 10 plugins of 12 | `EmulatorFileName` left empty, LaunchBox shows another row's | the desk names every row, 9433e0b |
| Two rows for one file | dedup on non-absolute paths (RetroArch with two cores) | dedup by full path, 5c28b89 |
| ares vault copies "Active", never restorable | `OwnIsSaveActive` = "the file exists" | active only under `Paths/Saves` (cached `SavesRootOf`) or beside the rom (`RomsOf`), c5394e2 |
| RASnes9x's Secret of Mana `.srm` hidden in SUPER ZSNES's card; ares 1 backup, SUPER ZSNES 4 | listed and persisted rows on different versions, then a match by file name (§5) | plan A + `TryGetSaveGroupInfo`, 1c8192c |

Seen, not fixed (LaunchBox): a card's "backed up" verdict is not recomputed when a live file changes behind LaunchBox's
back without a signature of ours - a restart, or a content signature (the probe's), shows it (§7).

---

## 5. The swallowed save (08-09/10)

### 5.1 Symptom

Secret of Mana, SUPER ZSNES's desk: RASnes9x's `Emulators\Nixx-RASnes9x\Saves\Secret of Mana (USA).srm` did not show.
LaunchBox asked SUPER ZSNES about it under **SUPER ZSNES's** id (`IsSaveActive`, `TryComputeSaveSignature` with
`{"emu":"superzsnes",…}`), and the file appeared in SUPER ZSNES's Backup History (F7B57C16, `RASnes9x.exe`). Counts were
wrong too: ares "Save (ram)" 1 backup for 0, SUPER ZSNES 4 for 2.

### 5.2 Hypotheses eliminated by measure

| Hypothesis | Result |
|---|---|
| same content as another save | eliminated (content changed, still taken) |
| the `.rtc` companion | eliminated (the probe with a companion stays apart) |
| same card name | eliminated ("RASnes9x save", still taken) |
| same file name, or same name + size | not enough (the probe's twin stayed apart - same exe as its card) |
| no lineage | eliminated (lineage given, still taken) |
| same `OriginalFileName` | eliminated (emptied, still taken) |
| `UseSaveGroupIdForPersistedMatch` false, `MigrationFamilyId` set | no effect |
| a prefix in the original name (`rasnes9x:<file>`) | dropped: LaunchBox rewrites `OriginalFileName` at backup |

### 5.3 The rule (reflection on `ApplyPersistedMetadata`)

`AddEditGameSavesPageViewModel.ApplyPersistedMetadata(SaveItem item, IEnumerable<SaveItem> persistedItems,
IEnumerable<SaveItem> launchBoxBackups)` is internal static and runs its real body by reflection. 13 hand-built cases
(`SaveMatchExperiment`, removed since) plus 6 on `NormalizeSaveMetadata`:

- a listed row **not found by its path** among the persisted rows takes the `SaveGroupId` and lineage of a same-game
  backup whose `OriginalFileName` - or, when empty, its file name - **equals the listed row's file name**, whatever its
  exe, id, lineage or own `OriginalFileName`;
- another file name (case 5, 10-12) or another game (case 13) does not match;
- `NormalizeSaveMetadata(primary, secondary)` takes nothing;
- which backups are passed in (the upstream filter) is not known.

### 5.4 Root cause

The listed row and its persisted row must be on the **same** game or version to be found by path. LaunchBox persisted
ours on "Play Version..." (`c0f567b1…`, the version on the game's own file); we listed them untied (lot 1's
`SaveRows.TieTo`, chosen after test 9b). So they never met:
- the persisted row was counted as a backup (ares 1);
- the listed row, unmatched, was matched by name against the vault copies, which are rows of the game: RASnes9x's
  `.srm` taken into SUPER ZSNES's card (4 = 2 vault copies + the RASnes9x row + its persisted twin).

RetroArch lists its rows tied to that version: neither effect.

### 5.5 Iterations

| Try | Result |
|---|---|
| v1: persist our rows on the game | duplicates - LaunchBox also wrote its own on the version at close, shown as a backup |
| v2: persist wherever a row at that path is, else the version on the game's file | one row per path, counts still wrong |
| tie the listed rows to that version | counts right; manual Backup Save "Can't find the game" |
| untie again + move our persisted rows back onto the game | first opening wrong (LaunchBox read the rows before our move), Backup Save still failing - **on RetroArch's stock card too** → the Backup bug is LaunchBox's (§9.1) |
| **final**: tie like LaunchBox + persist + move ours to the listed row's owner | right at the first and second opening and after a restart |

### 5.6 The fix (`SavePersist`, one game on screen only)

1. **`TieLikeLaunchBox(gameId, rows)`**: a row of the game listed with no version is tied (`SaveRows.WithVersion`) to the
   version whose `ApplicationPath` equals the game's, when there is one.
2. **`Ensure(gameId, rows)`**: owners = `game.Saves` + each version's `Saves` (property `Saves`, its generic item type,
   its `Add(GameSave)` and `TryTake(out GameSave)`, all by reflection). For each row with an id and a path:
   - target = the row's version, else the game; a version that no longer exists → nothing written;
   - already a persisted row at that path on the target → nothing;
   - one of **our** rows (JSON id) persisted at that path with that id on **another** owner → moved: taken out
     (`TryTake` until empty, the rest added back - the collection has no `Remove`), `AdditionalApplicationId` set to the
     target's, added to the target; logged `persist: moved the row of …`;
   - a row of someone else at that path anywhere → nothing (never doubled);
   - else a new `GameSave`: `GameId`, `AdditionalApplicationId`, `FilePath` (full), `SaveGroupId`, `MatchLineageId` (the
     id if none), `EmulatorFileName`, `EmulatorCore`, `SaveGroupName`, `DisplayChipText`, `OriginalFileName` (the file
     name if none), `Slot` for a state; logged `persist: added a row at its own path …`.
   - any failure: `persist failed, nothing more written: …` and the listing goes on.
3. **`TryGetSaveGroupInfo`** in the desk answers with the row's own `SaveGroupId`, so the tile's constructor never picks
   a group by name.

Paths are compared after `Path.GetFullPath` against LaunchBox's root (`AppContext.BaseDirectory\..`): the XML stores
some relative (`..\LB\FakeCatalog\…`, `Emulators\…`).

### 5.7 Limits of the fix

- **Reflection on internals**: a LaunchBox update that changes `Saves` / `GameSave` → nothing written, logged, back to
  the old behaviour (the hijack).
- **Only Game Saves of one game** runs it. The start-up scan and multi-game listings do not (rows are not created there
  anyway). A game closed after a session without ever opening Game Saves: LaunchBox writes its rows itself at close -
  **not measured** whether the hijack happens then (§11, test 5).
- **Orphans**: a persisted row whose file disappeared is not removed.
- The tie overrules lot 1's "a version on its game's file is not tied" for the **listing**; the restore target is the
  same file either way.

### 5.8 What to ask LaunchBox for (instead of reflection)

1. `IReadOnlyList<GameSaveBase> IGame.GetGameSaves()` (and on `IAdditionalApplication`): the persisted rows.
2. `IGame.AddOrUpdateGameSave(GameSaveBase)`, matched by `FileLocation`, written at the next `IDataManager.Save()`.
3. `IGame.RemoveGameSave(GameSaveBase)`: orphans and wrongly merged rows.
4. `IGame.TryBackupGameSave(GameSaveBase, out string error)`: what the Backup button does (makes §6's occupied-slot
   case safe on our side).
Wider asks: a plugin's id is authoritative (no merge by file name when `UseSaveGroupIdForPersistedMatch` is true, or a
new `IsSaveIdentityAuthoritative`); `AddSaveArgs` carries the emulator, game and version; `GetPotentialSaveSlots(state,
emulator)`; list every save-capable emulator of a game (would make the desk unnecessary); a default signature covering
the companions. Not drafted in English yet.

---

## 6. State slots

### 6.1 What LaunchBox does (measured 08/10 with the probe on "SaveProbe Test", Nixx-SaveProbeMain, steps 1-5)

Probe knobs used (`tools\save-probe`, `probe.ini`): `Slots`, `SlotLabel`, `RestoreSlot`, `ReturnId`, `ReturnSlot`.

| Step | Setting | Measured |
|---|---|---|
| 1 | slots 1-3 labelled `S-1..S-3` | the tile shows **"Slot " + the KEY** ("Slot 2"), never the label |
| 2 | backup of slot 2 restored, `S-3` picked; restore into the picked slot, id recomputed | the list is asked **at the restore** (`RestoreSaveCommand`), not cached; the picker shows the **labels** and preselects the **first** entry (not the backup's slot 2); `SaveToAdd.Slot` = 3, the rest of the row (id included) is the backup's; returned with slot 3 + its id: `.mainstate3` created identical, a new "Slot 3" tile at once, slot 2 untouched - **coherent**; **the precautionary copy was of slot 2** (`-02.mainstate2`), not of slot 3 |
| 3 | same backup into occupied `S-1` | no question asked: "Promote this backup…?" comes **before** the picker; `ShouldOverwriteFunc` returns true without a prompt; slot 1 overwritten with no copy (saved only by an identical copy from 07/10) |
| 4 | `Slots=empty` | **every state restore refused** ("The emulator for this game/app doesn't appear to support save state restoration!"), the plugin never called |
| 5 | `Auto` (-1) then 0-3 | -1 passed as is; a tile with slot -1 shows "Slot Auto" |

Also: with a content signature (the probe's), a live file changed behind LaunchBox's back is seen at the next opening
of Game Saves; without one (all our plugins) only after a restart.

**`GetPotentialSaveSlots()` takes no argument and belongs to the game's emulator's plugin**: a state owned by another
emulator gets the desk's list (Zelda: SUPER ZSNES's 0-9 for an ares state whose slots are 1-9). The desk cannot route it.

### 6.2 LaunchBox's integrations

| | List | Restore | Row returned |
|---|---|---|---|
| RetroArch | Auto (-1), 0-9 | **honours the pick**: `.state`, `.state<n>`, `.state.auto` | picked slot, no id (its states have none) |
| Dolphin | Auto (-1), 0-9 | **honours the pick**: `<disc>.s<NN>`, refuses without a slot; Auto would give `.s-01` (`{-1:D2}`, not measured) | picked slot, id dropped |
| PCSX2 | none | no state branch in its restore | - (its states cannot be restored) |

### 6.3 Our plugins (read 08/10, corrected 09/10)

| Plugin | List (`GetPotentialSaveSlots`) | Real slots, file | Restore today | Row returned today |
|---|---|---|---|---|
| ares | 1-9 | `.bs1`-`.bs9` | backup's own name, **pick ignored** | picked slot + the old slot's id ❌ |
| SUPER ZSNES | 0-9 | 0-9 | backup's name, **ignored** | same ❌ |
| RASnes9x | 0-9 | `.000`-`.009` | backup's name, **ignored** | same ❌ |
| Flycast | 0-9 | 0-9 | backup's name, **ignored** | same ❌ |
| melonDS | 1-8 (`FirstSlot..LastSlot`) | 1-8 | backup's name, **ignored** | same ❌ |
| PPSSPP | keys 0-4, **labels 1-5** | files `_0`-`_4` | slot read from the id first, **pick ignored** | - |
| xemu | **none** (no override) | named QEMU snapshots in the qcow2, numbered by the plugin (`hdd\games\<title>.slots`) | honours the pick (≤ 0 → the file's slot, else 1) - blocked by the empty list | - |
| bsnes | **empty** | ONE `.bsz` zip holding every slot (`Quick/Slot 1..N.bst`, Undo/Redo, Managed), `.bsz.png` companion | listed as a **save row** (`GameSaveGame`, group "Save states (all slots, one file)"): restored as a file, the empty list does not block it | - |
| no$gba | **empty** | one "Write/Load snapshot", nothing seen on disk | no state rows | - |
| Cxbx, Vita3K, Xenia | none | no states listed | - | - |

The defect of the five: three facts disagree after a restore - the file name (backup's slot), the row's `Slot` (picked)
and the slot in the id (backup's). Seen live on Zelda: picked 2, ares wrote `.bs1`, the tile said "Slot 2", the id
`"slot":1`. At the next listing the tile goes back to "Slot 1".

### 6.4 The problems, worst first

1. **The pick is not applied** (ares, SUPER ZSNES, RASnes9x, Flycast, melonDS, PPSSPP), where RetroArch and Dolphin
   apply it.
2. **The returned row contradicts itself** (five plugins): wrong labels on tiles until the next listing.
3. **xemu's states cannot be restored at all** (empty list).
4. **The list comes from the desk's plugin** for a foreign state: numberings differ (0-9, 1-9, 1-8, -1-9), a 0 means
   nothing to ares or melonDS. **And a desk with an empty list (bsnes, no$gba, xemu, Cxbx, Vita3K, Xenia) blocks the
   restore of every other emulator's state** listed on its games (found 09/10, not measured yet).
5. **PPSSPP's keys 0-4 labelled 1-5**: tile "Slot 0" for the emulator's slot 1, permanently.
6. **The slot is part of our identity** (in the JSON id): a state restored into another slot must change id, else it
   stays tied to the old card. The natives avoid it (no id, id dropped, id without slot).
7. **LaunchBox's own hole**: an occupied other destination is overwritten with no copy (§9.2).

### 6.5 Decisions (Mehdi, 08/10)

- **A. Apply the pick, as the natives do.** The restore writes the picked slot's file (thumbnail included) and returns a
  row with **the slot written and the id recomputed for it** - the behaviour measured as coherent (step 2).
- **B. Refuse a slot the emulator does not have**, with a clear message (e.g. a 0 from SUPER ZSNES's list for an ares
  state: "ares numbers its states 1 to 9"). Never guess (no "0 means 1").
- **C. Occupied other destination: a Yes/No warning**, not a refusal and not a home-made backup.
  - Shown only when all three hold: the picked slot is not the backup's; it already holds a state; that state differs
    from the backup. A restore into its own slot never sees it.
  - Text (English, like LaunchBox): *"Slot 1 already holds another save state. Because of a LaunchBox bug, it will not
    be backed up before being replaced: it will be lost. Replace it anyway?"* Yes overwrites (as the natives), No
    cancels with nothing written.
  - **LaunchBox only**: never under LiteBox (desk off, LiteBox does the right restore itself, lot 8), never in an
    unattended restore (RomM sync), and in BigBox no dialog: refuse that case with a message if BigBox offers the
    picker at all (to check).
  - Shown in our `AddSaveFile`, on LaunchBox's UI thread (seen in the stack under `RestoreSaveCommand.Execute`).
  - **Gated by LaunchBox's version**: below the version that fixes the bug; the bound is raised once the fix is
    verified with the probe (it cannot be detected).
  - Rejected: refusing outright (blocks a legitimate use); a side copy in `lbip-replaced\` (invisible to LaunchBox);
    calling `BackupSaveCommand.TryBackup(SaveItem, out Exception)` by reflection in the middle of LaunchBox's restore
    (no public `SaveItem` constructor, inside its command on its UI thread - risky).
- **D. xemu: one entry, "its own slot"** (key 0). Snapshots are named; the plugin's numbers are per game, unbounded, and
  a restored `slot-N.xemustate` replaces the snapshot **whose name is inside the file**, whatever N. Restoring "into
  another slot" would rename a snapshot: not offered. Restore always under the snapshot's own name (the code already
  takes the file's slot when the pick is ≤ 0); any other pick refused (rule B).
- **E. PPSSPP: keys 1-5**, the emulator's own numbers; the plugin maps them to files `_0`-`_4`. The id keeps the file
  index so existing tiles do not change - **to verify**.
- **bsnes**: studied 09/10 - one `.bsz` for every slot, listed as a save row, restored as a file: nothing to change for
  its own states. Its empty list only matters as a desk (problem 4).
- **F. A content signature** in the common desk (§7).
- **One convention for all our plugins**: key = the emulator's own number, label = the key.

### 6.6 Design of the fix (to code)

Common part (`Shared.Saves`, e.g. `SlotRestore`):
- input: the backup row (`SaveToAdd`, its id read by `SaveId.TryRead`), the picked `Slot`, the owner plugin's slot set;
- the owner says, per plugin, its valid slots and **the file of a slot** (`SlotFile(game target, slot)`), its
  companions (thumbnail) and how a slot is written into an id;
- picked slot not in the set → refused (`PluginResponse(false, "<emu> numbers its states …")`); -1 → the backup's slot
  for an emulator without Auto;
- destination ≠ backup's slot, occupied, different content → the warning (C), when allowed (LaunchBox, not BigBox, not
  unattended, version below the bound);
- write the backup into the slot's file (and its companions), return a `GameSaveState` with that `Slot`, the id
  recomputed (`SaveId.Write` with the new slot), the same `GameId` / `AdditionalApplicationId`.

Per plugin:

| Plugin | Work |
|---|---|
| ares | slots 1-9, `SlotFile` = `<name>.bs<n>` (+ thumbnail), restore through `SlotRestore` |
| SUPER ZSNES | 0-9, its states folder, same |
| RASnes9x | 0-9, `.000`-`.009`, same |
| Flycast | 0-9, same |
| melonDS | 1-8, same |
| PPSSPP | keys 1-5 → files `_0`-`_4`; stop reading the slot from the id first; check the id keeps the file index |
| xemu | add `GetPotentialSaveSlots` = `{0: "Its own slot"}`; accept 0 or the file's slot, refuse the rest |
| bsnes, no$gba, Cxbx, Vita3K, Xenia | nothing for their own rows; decide problem 4 (below) |

Open design point - **the desk's list** (problem 4): the desk's `GetPotentialSaveSlots` cannot know which state is
restored. Options to decide: (a) a desk whose own list is empty returns a generic list (e.g. 0-9) so foreign states can
be restored, and its own restore refuses what it cannot take; (b) the union of the slot sets of the platform's
save-capable plugins; (c) leave it (foreign states unrestorable on those desks). (a) or (b) with rule B keeps it safe.

Tests (probe first, then real):
- probe: slot set, `SlotFile`, the recomputed id, refusal messages, the warning's three conditions (unit level);
- real, G:\LB1326: ares state on Zelda restored into another free slot (new tile, coherent), into its own slot (no
  warning), into another occupied slot (warning; No → nothing written; Yes → overwritten), a 0 from SUPER ZSNES's
  list (refused); PPSSPP tile reads "Slot 1" for slot 1; xemu state restored; a foreign state on a bsnes desk.

### 6.6b Measured after the fix (09/10, Zelda, ares state, SUPER ZSNES's list 0-9; 81fdba3/70be6b0 + eed1654)

| Step | Pick | Result |
|---|---|---|
| backup `-02` (8DB9) into free slot 2 | 2 | `.bs2` created = 8DB9, slot 1 untouched, row `Slot=2` and `"slot":2`, a "Slot 2" tile - coherent |
| backup `-01` into slot 0 | 0 | refused: "ares has no slot 0: ares numbers its states 1 to 9. Pick one of those." - nothing written |
| backup into occupied slot 2 (other content) | 2, **No** | our warning, then nothing written (both slots unchanged) |
| same | 2, **Yes** | slot 2 replaced (B8BF); no precautionary copy of its old content, as expected (it was still in the vault as `-02`) |
| backup `-01` into its own slot 1 | 1 | no warning; LaunchBox skipped its copy (the old content was already the vault's `.bs1`) |

**A refusal or a No shows LaunchBox's own error after ours.** `AddSaveResponse` has no "cancelled" apart from a
failure: LaunchBox shows our message, then logs `RestoreSave: plugin.AddSaveFile failed` and pops up "LaunchBox
encountered an error while attempting to restore the save file. This typically occurs when the file is marked
read-only or is in use..." - misleading, harmless (nothing written). The natives answer a No to `ShouldOverwriteFunc` the
same way. Cosmetic, LaunchBox's.

**Twin rows on Secret of Mana** (found in the XML after these tests): four paths with two rows each, one on the game,
one on "Play Version..." - left by the put-back trial of 09/10 01:37 (our rows moved onto the game, LaunchBox
re-persisting its own on the version at close). Not made again since (4 at 09:20, 4 before the slot tests), but never
cleaned: `Ensure` stopped as soon as the target had its row. Now a twin of OURS (same id, same path) on another owner is
removed (`persist: removed a twin row`).

### 6.6c Four emulators' states on one game (09/10, Zelda, SUPER ZSNES's desk)

Set up: ares slots 1 and 2, SUPER ZSNES slot 1 (a copy of its real `.szst-last`), RASnes9x slots 1 and 3 (`.001`,
`.003`), RetroArch slot 1 (`states\Snes9x\….state1`) - three "Slot 1" on one game.

- **RetroArch's state was not listed at first.** Its integration finds its states folder (`GetSaveStateDirectory`)
  from the core on the GAME's command line when `sort_savestates_enable` is on (`-L cores\x.dll`, then
  `info\x.info`'s corename); the game's emulator being SUPER ZSNES, there is none: no folder, no state (its saves go
  another way and were listed). Our restore of one (`StockRestore`, the same function) would have found no folder
  either. **Fixed** (5700c29): `SaveGameView`, a `DispatchProxy` over `IGame` / `IAdditionalApplication` that answers
  `GetEffectiveCommandLine` with the asked emulator's line for the game's platform and forwards the rest, handed to
  LaunchBox's integrations (`lb.*`) in `OthersFor` and in `StockRestore`.
- **Then**: seven tiles, one per emulator and slot, each with its exe; one persisted row per path; backups made under
  distinct vault names - `….state` (RetroArch's, renamed by LaunchBox, `OriginalFileName` `.state1`), `….001`,
  `….003`, `….szst1` with its thumbnail `….png` (the SUPER ZSNES vault row has no `OriginalFileName`; our restore falls
  back on the vault file's name).
- **Restores** (live files altered first, hashes checked after):

  | Restore | Result |
  |---|---|
  | RetroArch slot 1 → free slot 2 | through the desk's own RetroArch steps: `.state2` = the backup, `.state1` untouched |
  | RASnes9x slot 3 → occupied slot 1 | our warning, Yes: `.001` = the slot 3 backup, `.003` untouched; no copy of the old `.001` (it was in the vault) |
  | SUPER ZSNES slot 1 → its own | restored with its thumbnail, no warning; LaunchBox's copy (`-01.szst1` + `-01.png`) of the right file |

  The other emulators' states never moved.

### 6.7 LiteBox (lot 8)

LiteBox runs the whole restore itself: pick the slot first, preselect the backup's slot, confirm saying what is
replaced, back up the file actually overwritten. No restriction needed there.

---

## 7. Companions and the signature

- Measured 08/10 (probe, `Companions` knob): a backup carries the companions (`-02.mainsave` and `-02.rtc` together).
- **Without a plugin signature, LaunchBox compares the primary file only**: the companion alone changed, LaunchBox
  restarted, the tile still "backed up", Backup refused. A changed `.rtc` alone is never backed up.
- Concerned: bsnes and RASnes9x (`.rtc`, bsnes `.psr`, bsnes `.bsz.png`), ares (64DD clock), Flycast (the rest of an
  arcade set).
- **Done** (eed1654): `SaveDesk.WithCompanions`, used when the plugin's own `TryComputeSaveSignature` says nothing:
  SHA-256 of the primary, then of each companion in the order of its tail (".rtc", ".psr"), content only, so a vault
  set (`-05.srm` + `-05.rtc`) and the live one compare equal; thumbnails (`.png`) and states never counted; a save
  without companions keeps LaunchBox's own signature; sets above 64 MB left to LaunchBox.
- **Measured 09/10** (Zelda, RASnes9x `.srm` identical to its vault copy `-04.srm`): an `.rtc` added beside it - the
  card showed "!", Backup made `-05.srm` + `-05.rtc` (LaunchBox's log: "content signature/timestamp changed"); then ONLY
  the `.rtc` changed - "!" again at the next opening of Game Saves, without a restart, Backup made `-06.srm` (same
  content) + `-06.rtc` (the new one). Before, neither change was ever backed up.
- **At a restore, LaunchBox copies no companion back** (measured 09/10: Zelda's RASnes9x `-10.srm` restored, its live
  `.rtc` stayed the one it replaced). ares put its own back; RASnes9x and bsnes did not. **Fixed** (7f7c5e6):
  `SaveCompanions.CopyBack` puts the backup's companions beside the restored file under its stem; a backup without one
  leaves the live one. After: `.srm` and `.rtc` both back to the backup's.
- **Everything is tried before anything is written** (Mehdi, 09/10; 0425f9b): `SaveCompanions.WhyNotWritable` opens the
  save and each companion's destination for writing (read-only, held by another program, a folder without write access -
  a save beside the roms on a share) before the copy; else a clear refusal, nothing written. Before, a companion that
  could not be written failed after the save had been replaced.
- Flycast's arcade sets: their companions are found by the set's name in Flycast's own folders, not beside a vault copy;
  whether a set of several files comes back whole at a restore is not measured - to check on a real multi-file set.

---

## 8. Manual import (`TryGetSaveGroupInfo`)

LaunchBox calls `TryGetSaveGroupInfo` for a file it discovers itself (Import Save Game File…, Import Save State
File…). The desk answers with the row's id when the row carries one (1c8192c) - that covers the tiles. For an imported
file there is no row yet: **not measured** which card it lands on and with which id (probably a new card with a
LaunchBox GUID, so a duplicate card).

**Measured 09/10 on Zelda** (Import Save Game File, `Zelda import test.srm`, random 8 KB): LaunchBox copies the file
into the vault under the GAME's name with the next free number (`…(France)-03.srm`), touches no live file, and makes a
**new card** "My Save File", "In Vault", 0 backups: `SaveGroupId` a fresh LaunchBox GUID (`a2289149…`), `EmulatorFileName`
the game's emulator (`SUPERZSNES.exe`), `OriginalFileName` the source's name, no `Title`. That is LaunchBox's "Make New
Save seeded from a file", and LiteBox copies it on purpose (`SaveManager.Import`, LbApiHost) - **not to change**.

**Restoring that card, measured 09/10 (test A)**: the desk resolved the GUID by the exe to SUPER ZSNES, which wrote the
game's live `.srm` (9F1DCBC35C35 → the import's 32E1CD8C021A). LaunchBox made **no precautionary copy** (its log goes
straight from `RestoreSave: invoking plugin.AddSaveFile` to `completed successfully`): it copies the restored card's own
live file, and an "In Vault" card has none. The overwritten content survived only because an identical copy from 07/10
was in the vault (`-02.srm`). Then LaunchBox moved the live file's row to the restored card: "My Save File" (GUID)
became Active on the `.srm`, "Cartridge save" (our JSON) dropped to In Vault. Same hole as §9.2, entered from a card
without a live file. Probably the same with stock RetroArch (not measured).

Usually harmless (the close backup has the content already), lost when the live save changed after its last backup
unseen: played outside LaunchBox, a sync that wrote it, a companion-only change (§7).

**Decided (Mehdi, 09/10): the same Yes/No warning as for slots** (§6.5 C, same gates), in the desk's `AddSaveFile`: the
target live file exists, belongs to another card, and differs from what is restored. LiteBox already does it right
(`SaveAction_SetActive`, LbApiHost `EditGameWindowSaves.cs`: the group holding the destination - same version, entry,
slot or core - is backed up under its own id first, its row handed over after; `ShouldOverwriteFunc` really asks).

**Later (Mehdi, 09/10), not for this lot**: ideally an imported file is offered to every plugin of the platform's
emulators, each saying whether it can read it, and the user picks the emulator in a combobox. Not doable cleanly in
LaunchBox (the import dialog is LaunchBox's; we only see `TryGetSaveGroupInfo` and the restore afterwards). Easy in
LiteBox, whose import is its own (lot 8).

---

## 9. LaunchBox bugs met (nothing a plugin can fix)

### 9.1 Manual Backup Save on a game imported with versions - "Can't find the game for this save item!"

- Clean repro 09/10 on **G:\LB, 14.0.1.2, no third-party plugin** (`Plugins\` emptied into
  `G:\LB\Plugins-off-2026-10-09-bugrepro`, Data saved in `G:\LB\Backups\Data-before-2026-10-09-bugrepro`): ROM import of
  `Super Metroid (USA).sfc` + `Super Metroid (Europe) (En,Fr,De).sfc` + `F-Zero (USA).sfc` (fake files) on RetroArch -
  LaunchBox itself creates "Play (USA) Version..." on the game's file; `.srm` files placed in `saves\Snes9x`; **first**
  Backup Save: F-Zero OK (vault copy made), Super Metroid fails, nothing written. Debug log on G:\LB1326 (Secret of
  Mana): only `[SaveBackup] Creating new backup … Reason: Existing backups found, but none matched the current Save
  Group ID.` before the error.
- A restore that needs the precautionary backup fails the same way ("LaunchBox failed to back up the save item!", then
  "…read-only or in use"). Automatic backup at close works. Also seen 20/09 on Atlantis (Saturn, saves tied to "Play
  (FR) (Disc 1)").
- Consequence for us: test 9b's "untying avoids it" held for the Restore only; the desk ties (§5).
- English report written (09/10), not posted. On G:\LB, 76 games have such a version: it is the common case.

### 9.2 A state restored into another, occupied slot loses that state

Reproduced 08/10 with stock RetroArch only (Rolan's Curse, Game Boy, Gambatte; `states\Gambatte\…state1` "the one that
must not be lost" AB088FBB9EA6, `…state2` "the backup that gets restored" DFAADE3D2777; only slot 2 backed up; its live
file altered; restore of slot 2's backup into slot 1): slot 1 overwritten, the precautionary copy is of slot 2, slot 1's
state gone everywhere. Plus: confirmation before the picker, `ShouldOverwriteFunc` returns true without asking, the
restored state shows on slot 2's tile until Game Saves is reopened, the picker preselects the first entry. English report
written (routing §5, f5dd5df), not posted.

### 9.3 Restore of another emulator's save goes to the game's emulator

The lot's starting point (§1.1), worked around by the desk. Draft report in routing §5.

### 9.4 Make Default

Overwrites the game with the version (path, Version; Developer/Publisher/Region/Status emptied), the old file referenced
nowhere, every save row duplicated onto the version (16 → 30). Seen with the probe plugins; **to reproduce with stock
RetroArch before reporting** (routing §1).

---

## 9b. The Codex review of 09/10 (adversarial, branch against main)

Two findings, both confirmed by a measure before the fix and gone after it (111d2b0):

1. **A file's owner, outside its emulator's folder.** `GetCompanionSaveFiles` / `IsSecondarySaveFile` get a path only;
   `OwnerOfPath` knew an owner only by its emulator's folder, else the desk answered for itself. Measured: RASnes9x's
   battery saves pointed to `G:\LB\FakeCatalog\ras-saves` (`Dir:SRAM`), the Zelda `.srm` backed up through SUPER
   ZSNES's desk WITHOUT its `.rtc` (`-07.srm` alone). Fix: the desk remembers, per full path, which plugin and which
   emulator listed each other plugin's live file (`SaveRouter._listed`, filled in `OthersFor`), asked first. After:
   `-10.srm` + `-10.rtc`.
2. **Which install a restore writes into.** `EmulatorFor` kept the first of a tie: two installs of one exe name, core
   and platform in different folders. Measured with a minimal second RetroArch (`Emulators\RetroArch-B\`: exe copy,
   `retroarch.cfg`, `info\snes9x_libretro.info`, an emulator entry "RetroArch B" on SNES): restoring B's backup wrote
   into the MAIN RetroArch's `.srm`. Fix: `SaveRouter.RestoreEmulator` (used by `StockRestore`): on a tie between
   different folders, the card's live file (`SavePersist.LivePathsOf`, by reflection) says which - the emulator that
   listed it this session, else the one whose folder holds it; still tied, the restore is refused with a message. One
   folder for both (one exe, two cores: RetroArch + Retroarch2) is not ambiguous. After: written into RetroArch-B, the
   main one untouched. Probe: 7 checks (`PickInstall`).

Seen on the way: LaunchBox's exit deadlock (§9.5) twice in the day.

### 9.5 LaunchBox stays alive after closing

Its window closes, its log ends with "Calling Environment.Exit", the process stays (no window, CPU still, ~1 GB). A dump
(`dotnet-dump collect --type Heap`, `clrstack -all`): the UI thread waits in `Environment._Exit`; the finalizer thread
finalizes a `CoverFlow.FlowImage` whose `Dispose` does `Dispatcher.Invoke` to the UI thread - a deadlock; no plugin
frame anywhere. Random (GC timing). The data is written before: killing it loses nothing.

## 10. Dead ends and rejected options (so they are not tried again)

- An abstract base class for our plugins (no plugin constructed).
- `[ThreadStatic]` as a pack-wide guard.
- Routing on `EmulatorFileName` (exe name only; two plugins may share an exe).
- The origin in the id (`game`, `ver`, a rom fingerprint) - lot 1, dropped: the row carries it.
- An additional application "Play with <emulator>" per emulator, changing the game's `EmulatorId` for a session,
  injecting rows into the WPF view (Mehdi, 07/10).
- Harmony on the core (decoy bodies).
- Tweaking listed rows to escape the merge (lineage, `OriginalFileName`, `UseSaveGroupIdForPersistedMatch` false,
  `MigrationFamilyId`, a prefixed original name) - §5.2.
- Persisting our rows on the game while LaunchBox puts them on the version (duplicates) - §5.5.
- For occupied slots: refusing, a side copy, `BackupSaveCommand.TryBackup` by reflection - §6.5 C.
- Restoring an xemu state into another slot (named snapshots) - §6.5 D.

---

## 11. Left to do, with how to test it

Order proposed: 1, then 7-10 (the slot and signature code, same files), then 2-6 in one LaunchBox session, then 11-14.

| # | What | How |
|---|---|---|
| 1 | ~~Redeploy the clean build~~ **done 09/10**, Zelda checked (§3.2b) | |
| 2 | Dolphin restore through the desk - **not reachable today**: none of our plugins covers GameCube or Wii, so no desk lists Dolphin's saves. To measure when one does | |
| 3 | PCSX2 forward - **not reachable today** (no plugin of ours on PS2), same | |
| 4 | ~~Manual import~~ **measured 09/10** (§8); the warning for a restore over another card's live file to code with 8 | |
| 5 | ~~A save never seen, at a close~~ **measured 09/10, no hijack** (§3.2b) | |
| 6 | ~~Desk start-up cost~~ **measured 09/10**: LaunchBox's full save scan ~3.2 s desk off (06-07/10) vs ~3.55 s desk on (08-09/10), +0.35 s on G:\LB1326 (23 emulators); in the background. **Big library, 09/10** (§11b): 17.9 s with the desk, 3.7 s without - fixed by the RetroArch config cache, 4.7 s | |
| 7 | ~~Slots~~ **done and measured 09/10** (§6.6b, §6.6c) | |
| 8 | ~~The Yes/No warning~~ **done and measured 09/10** (No and Yes); BigBox: refused with a message (probe only) | |
| 9 | ~~xemu, PPSSPP, desks without slots~~ **done and measured 09/10**: xemu refused slot 5 then restored slot 3 through Cxbx's desk; PPSSPP's new row `Slot=1` (a row persisted before keeps its old key - test rows deleted) | |
| 10 | ~~Content signature over primary + companions~~ **done and measured 09/10** (§7) | |
| 11 | ~~Orphan rows~~ **decided 09/10: left to LaunchBox** (shown "In Vault", their backups kept; a drive away is not a deleted save); a cleaning action with confirmation can come in LiteBox (lot 8) | |
| 12 | ~~Codex review~~ **done 09/10** (§9b) | |
| 13 | ~~Lot 8 note~~ **done**: routing §4b, lot 8 | |
| 14 | ~~Sandbox cleanup~~ **done 09/10** (§12). Left to Mehdi: the English API request (§5.8) if wanted, posting the two bug reports (§9.1, §9.2) | |

Open question: the "other error messages" Mehdi saw during the empty-list test (08/10) were never identified.

Also found 09/10, not for this lot: a game on **Demul** (no save management) gets no desk - saves made on it with Launch With Flycast are never listed. LaunchBox's own limit, as for any emulator without a save plugin (lots 5-6).

---

### 11b. Start-up cost on a big library, and the RetroArch config cache (09/10)

Bench on G:\LB1326: 2,000 fake SNES games added on SUPER ZSNES (XML backed up as
`Backups\lbip-2026-10-09-lot3-mesures\SNES-before-bench.xml`, restored after), the save probe plugins moved out. The
number is LaunchBox's own log line "Game save scan full completed in X seconds".

| Run | Setup | Scan |
|---|---|---|
| 1 | desk on | 17.90 s |
| 2 | desk off (`desk=off`) | 3.74 s |
| 3 | desk on, probe plugins out | 17.91 s |
| 5 | desk on, RetroArch config cache on | **4.71 s** |
| 6 | desk on, cache off (`raConfigCache=off`) | 17.94 s |

Where the 14 s went (desk log, run 3): RetroArch 6.4 s, Retroarch2 (same exe, other core) 5.9 s, ares 1.4 s. With the
cache: RetroArch 0.5 s, Retroarch2 + RASnes9x 0.7 s, ares 1.5 s.

**Cause - LaunchBox's RetroArch integration, not LaunchBox's core.** `Unbroken.LaunchBox.Windows.RetroArch.dll`
(System\Plugins\RetroArch LaunchBox Integration, decompiled): `GetGameSaveDirectory` and `GetSaveStateDirectory` both
rebuild `<retroarch.exe folder>\retroarch.cfg` and call the private static `LoadConfiguration(string)` - a
`File.ReadAllLines` split on `=`, no cache - for EVERY game. Nothing in it depends on the game (no per-game / per-core
override is read); with `sort_savestates_enable` the core's `info\<core>.info` is read too. The sandbox's cfg is
111 KB / 3,387 lines. Only the desk asks RetroArch about thousands of games at once (the scan hands our plugin all
of its games); without the pack, whether LaunchBox's own scan pays the same per RetroArch game is NOT measured.

**Fix - `Shared.Saves\RaConfigCache.cs`, compat switch `raConfigCache` (Hook group, check `ra.loadConfig`).** A Harmony
prefix on `LoadConfiguration` answers from a cache keyed by full path, kept while the file's write time and length are
unchanged (stamped BEFORE the read, so a file changed during a read is read again); each caller gets its own copy of
the dictionary. Installed from `SaveRouter.Register` (every plugin's start) and when the stock integrations are
scanned; if RetroArch's dll is not loaded yet, on its `AssemblyLoad`.
- Measured first outside LaunchBox (scratchpad program on the real dll): 4,000 calls 3.9 s → 0.36 s, same folders
  answered, the cache re-read after `savefile_directory` was edited in a copy of the cfg. The body is the real one
  (not a decoy as in LaunchBox's obfuscated core) and the prefix ran on every call (not inlined).
- **Trap met (run 4, 5.09 s):** every plugin merges its OWN copy of Harmony and of this class, so neither
  `lock (typeof(Harmony))` nor `Harmony.GetPatchInfo(...).Owners` sees the others - twelve plugins stacked twelve
  prefixes. The once-per-process mark is now `AppDomain.CurrentDomain.SetData`, under `lock (string.Intern(...))`
  (both shared by every copy): run 5, one plugin patched (ares, the first), 4.71 s.
- Same trap to keep in mind for any future once-per-process patch of the pack: a static of the shared code is one
  copy PER PLUGIN (see the memory "LB14 : greffon = EmulatorPlugin direct").

### 11c. SaveScan: a big listing touches the disk once per folder (09/10)

At start-up LaunchBox hands each plugin ALL its games in one GetSaves, and the desk hands them again to every other
emulator of the platform. Audit (two agents, 13 plugins): each plugin asked the same questions per game - ares parsed
settings.bml 3 times per game, Xenia walked its whole content tree per game, PPSSPP listed SAVEDATA per game, SUPER
ZSNES decoded its NRBF settings per game, Cxbx worked out its data folder 5 times per game...

**`Shared.Saves\SaveScan.cs`** - opened by `SaveDesk.GetSaves` for a listing of MORE THAN 50 games and versions (the
scan), dropped when it returns (Mehdi: no memory kept outside the scan). Game Saves, a close, a restore: the real calls.
- `Exists` / `DirExists` / `Files(dir, pattern)`: a folder listed once, answers from memory, with .NET's own rules
  (`EnumerationOptions.Compatible`, hidden files included; `NormalizeInputs`: `*.*`/`.` = `*`, else
  `TranslateWin32Expression` + `MatchesWin32Expression`); anything odd goes to the real call.
- `Text` / `Lines` / `Bytes`: a file read once; `Memo(key, f)`: a value worked out once.
- A write during the listing calls `Touched(path)` (thumbnails, ares/xemu/Cxbx settings, titles.tsv, Xenia's relaunch
  map, Vita3K's marker). Captures (Xbox sync, DSi RefreshSave, Vita3K session) and "is the emulator running" are left
  as real calls - they write; they belong to the second pass.
- The desk: the emulators' platforms and the games by id kept for the listing.
- The SAME ANSWERS, not a new algorithm: probe `SavesCheck.Scan` (64 questions on a tricky folder, identical) and
  `--saves-ab <plugin.dll> --emu <exe> --roms <folder|list.txt> --platform <p> --count <n>` (the same games in one call
  and in calls of 50, every field of every row compared):

| Plugin | Games | Rows (identical) | Calls of 50 | One call |
|---|---|---|---|---|
| ares | 2000 | 364 | 1808 ms | 66 ms |
| Xenia (fake content tree) | 2000 | 334 | 420 ms | 32 ms |
| xemu | 300 | 200 | 1665 ms | 1404 ms (sync untouched) |
| Cxbx | 300 | 200 | 871 ms | 641 ms (sync untouched) |
| PPSSPP (fake install) | 2000 | 2000 | 635 ms | 213 ms |
| bsnes (fake install) | 2000 | 726 | 328 ms | 84 ms |
| RASnes9x | 2000 | 181 | 323 ms | 33 ms |
| SUPER ZSNES | 2000 | 363 | 866 ms | 13 ms |
| Flycast (fake Naomi sets) | 1000 | 54 | 358 ms | 39 ms |
| melonDS | 600 | 450 | 261 ms | 64 ms |
| no$gba | 600 | 150 | 135 ms | 73 ms |
| Vita3K | 600 | 300 | 66 ms | 15 ms |

**LaunchBox bench** (§11b, 2000 SNES games on SUPER ZSNES, desk on): 17.9 s → 4.71 s (RetroArch config cache) →
**3.27 s** with SaveScan - below the desk-off scan of before (3.74 s). ares 1.5 s → 0.13 s.

**Second pass (behaviour changes, not done):** xemu/Cxbx sync (two process snapshots per game, qcow2/zip/SHA-256 per
played game - a stamp on console mtime and size), per-disc caches kept on disk (Flycast, Vita3K, Xenia title ids),
melonDS/no$gba header cache, RetroArch's `GetFiles` per game (clean-room clone, other project).

**Trap met while measuring:** a LaunchBox killed after its exit left `Data\InputBindings.xml` and
`Data\GameControllers.xml` full of zeros ("InputBindings.xml is corrupt; LaunchBox cannot continue"); restored from
`Backups\Automatic LaunchBox Startup Data Backup 2026-10-09 19-50-13.7z`, the corrupt copies kept in
`Backups\lbip-2026-10-09-lot3-mesures\`. The bench now closes LaunchBox and waits; it never killed it again.

## 12. Sandbox left behind

G:\LB1326:
- the save probe: `Local\Plugins\Nixx-SaveProbeMain`, `…Secondary`, `…CatchAll`, emulators `Emulators\Nixx-SaveProbe*`
  (`Emulators.xml` ids `5a7e0000-…-a001/a002`), games "SaveProbe Test" and "SaveProbe CatchAll Test", `probe.ini`
  knobs, twin folders, fake `.rtc` files; removal: `tools\save-probe\deploy-probe.ps1 -Remove` + routing §6;
- Rolan's Curse's two fake RetroArch states and their vault copies;
- RASnes9x's Secret of Mana `.srm` (altered during §5, original to put back);
- SNES XML snapshots: `Super Nintendo Entertainment System.xml.before-tie`, `.before-putback`, `.before-tie2`, plus the
  older `.plante-par-nous`, `.avant-titre-vivant`, `.before-M4`, `.before-unknown-element`;
- original data: `Backups\lbip-saveprobe-2026-10-07\` (Data, Saves), `Backups\lbip-lot2-2026-10-08\`.

**Cleaned 09/10** (everything archived first: `G:\LB1326\Backups\lbip-cleanup-2026-10-09.zip`, `G:\LB\Backups\lbip-cleanup-2026-10-09.zip`, each with a `MAP.txt` of where each file was): the 33 test files and 26 Zelda rows below, RetroArch-B and its entry, `ras-saves`, the import file, ares `.bs1` and RASnes9x's `Dir:SRAM` put back; on G:\LB the two test games, their saves and 24 media, the SNES XML back to before the import. Left: the save probe (needed for the next lots), the older test rows of 07-08/10 (routing §6).

G:\LB1326, from the 09/10 tests (originals in `Backups\lbip-2026-10-09-lot3-mesures\`):
- `Emulators\RetroArch-B\` and its emulator entry "RetroArch B" (`Emulators-before-test2.xml` is the file before);
- RASnes9x's `Dir:SRAM` pointed to `G:\LB\FakeCatalog\ras-saves` (`snes9x.conf.before-ext`), and that folder;
- the fake states on Zelda (SUPER ZSNES `.szst1`, RASnes9x `.001`/`.003`, RetroArch `.state1`/`.state2`), the RASnes9x
  `.srm`/`.rtc` for Zelda, the import test file, Batman's slot 3 (restored), the vault copies they made.

G:\LB:
- `Plugins-off-2026-10-09-bugrepro\` (ExtendDB, Ppsspp Integration, Xenia Integration, ExtendDB.zip) to move back into
  `Plugins\`;
- the test games Super Metroid and F-Zero (`FakeCatalog\bugrepro\`), their `.srm` in `Emulators\RetroArch\saves\Snes9x`,
  F-Zero's vault copy; `Backups\Data-before-2026-10-09-bugrepro\`, `Backups\SNES-after-import-2026-10-09-bugrepro.xml`.

---

## 13. Commits of the lot

| Commit | What |
|---|---|
| f1b73d5 | the desk when the game's emulator is ours |
| 5c28b89 | one row per file when two emulators share an exe |
| d9a7234 | the desk by composition, not inheritance |
| f4a651f | pack-wide nesting guard, dedup by full path |
| 9433e0b | rows carry their emulator's exe name |
| c5394e2 | ares: a save is active only where ares reads it |
| 9abc32f | Zelda real test (doc) |
| 1247a42 | how RetroArch, Dolphin, PCSX2 find their emulator at restore (doc) |
| a7007a7, 36e638c | probe knobs for slots; the slot measures (doc) |
| f5dd5df | LaunchBox bug: state restored into another slot (doc) |
| 33f15b8, f0b9227 | probe `Companions`, desk row log; default signature ignores companions, vault numbering (doc) |
| be6ee73 (LbApiHost) | embedded Catalog refreshed for `LbSaveOwners` |
| 1c8192c | plan A: each row keeps its card; `TryGetSaveGroupInfo` |
| 97fd74f | probe `--is-active` and the save-probe knobs of 08-09/10 |
