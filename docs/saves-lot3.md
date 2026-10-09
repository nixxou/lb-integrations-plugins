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
- **Status.**
  - Listing, exe names, backup at close, restore by forwarding (ares) and by our own RetroArch restore: **measured OK**
    (Zelda 08/10, Secret of Mana 09/10).
  - Card hijack (a listed save taken into another card by its file name) and wrong backup counts: **fixed and
    measured** (plan A, 1c8192c).
  - **State slots: studied and measured, fixes decided, not coded** (§6).
  - Companion-aware signature, Dolphin and PCSX2 restores through the desk, manual import, startup cost, orphan rows,
    the Codex review and the sandbox cleanup: **open** (§11).
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
- **To do**: `TryComputeSaveSignature` in the desk for our rows: a hash over the primary and its companions (sorted,
  names + contents), only for rows with companions; `IsSaveActive` untouched (string comparison). It also makes a
  change behind LaunchBox's back visible without a restart (§6.1). Cost to watch: called per row at each opening and
  close - hash only small files, or size + date first.

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

Left to measure: restoring that card. Through the desk the GUID resolves by the exe to SUPER ZSNES, which writes the
game's live `.srm` - the live file of ANOTHER card ("Cartridge save"); LaunchBox's precautionary copy is of the restored
card's own live file, which does not exist, so the current save would be overwritten with no copy (the same hole as
§9.2). Probably the same with stock RetroArch.

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
| 1 | Redeploy the clean build on G:\LB1326 (the installed DLLs still carry the 08-09/10 traces) | LaunchBox closed, `deploy-dev.ps1 -All`, hashes; Game Saves on Secret of Mana and on **Zelda** (never seen since plan A) |
| 2 | Dolphin restore through the desk (`StockRestore`) | a GameCube/Wii game on one of our emulators with a Dolphin save; backup, alter, restore, hash in Dolphin's folder |
| 3 | PCSX2 forward | a PS2 game on one of ours with a PCSX2 memory card |
| 4 | Manual import (§8) | Import Save Game File on a card of ours: which card, which id |
| 5 | A game never opened in Game Saves | play with Launch With another emulator, close, read the XML: hijack or not |
| 6 | Desk start-up cost | the `front desk on` lines and the first `GetSaves` times in the 13 logs, full scan duration in LaunchBox's log |
| 7 | Slots: A + B + per-plugin `SlotFile` (§6.6) | probe checks, then the real tests of §6.6 |
| 8 | The Yes/No warning (C), LaunchBox-only, version-gated; BigBox behaviour checked | real test: occupied other slot, No then Yes |
| 9 | xemu "its own slot", PPSSPP keys 1-5 (and the id check), the desk list (problem 4) decided | real restores on xemu and PPSSPP; a foreign state on a bsnes desk |
| 10 | Content signature over primary + companions (§7) | alter only the `.rtc` of a RASnes9x save: Backup offered without a restart |
| 11 | Orphan persisted rows: handle or leave to LaunchBox (decide) | delete a live file, reopen Game Saves |
| 12 | Codex review of the lot | `/codex:adversarial-review` on `savegroupid` |
| 13 | Lot 8 note: LiteBox's proper state restore (§6.7) | in routing §4b |
| 14 | Sandbox cleanup (§12), English API request (§5.8) if wanted, post the two reports | |

Open question: the "other error messages" Mehdi saw during the empty-list test (08/10) were never identified.

---

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
