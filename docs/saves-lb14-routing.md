# Saves in LaunchBox 14: who is asked, and what we can do about it

Measured on 07/10/2026 on `G:\LB1326` (LaunchBox **14.0.1**, SDK `Unbroken.LaunchBox.Plugins` **14.0.1.2**) with the save
probe (`tools\save-probe`). Raw logs: `G:\LB1326\Backups\lbip-saveprobe-2026-10-07\saveprobe-2026-10-07.log` (probe) and
`ares-2026-10-07.log` (the per-game trace in `AresSaves.GetSaves`). The sandbox's original data is in that same folder
(`Data\`, `Saves\`, `atlantis-live\`).

## 1. What LaunchBox does (measured)

### Who gets the calls

| Moment | Call path (frames from the probe's stack) | Plugins asked |
|---|---|---|
| Start-up scan, selection scans | `DataManager.UpdateGameSaves` | every plugin, **with the games of its own emulator only** (Main: 1 game, RetroArch: 603 games + 44 apps, Dolphin: 1), and only if `SupportsSaveManagement()` is true |
| Game closed | `GameTimeTracker` → `SaveBackupManager.BackupActiveSave` | **the game's emulator only** (assigned, else the platform's default). A game launched with "Launch With" another emulator: that one only gets `PrepareEmulatorForLaunch` |
| Game Saves opened | `AddEditGameSavesPageViewModel.OnPageEnter` | the game's emulator only, **also for cards persisted under another emulator's name** |
| Restore (`RestoreSaveCommand.Execute`) | | `AddSaveFile` on the **game's** emulator, which writes the file into ITS folders; the row then points there for good and the real save of the other emulator is no longer followed |

`EmulatorFileName` is stored on every row (LaunchBox fills it itself once a row is listed) and shown on the card, but it
**never routes a call**.

Measured on 06/10 and 07/10 with real games too: Zelda (no assigned emulator, SNES default = SUPER ZSNES) launched by
Launch With > ares: ares wrote `Saves\Super Famicom\…(France).ram`, LaunchBox called SUPER ZSNES 4 times at close, ares
never; nothing reached the vault.

### Calls, in order

- **Close**: `SupportsSaveManagement`, `GetSaves`, then per row `TryGetSaveGroupInfo`, `IsSaveContainer`,
  `GetCompanionSaveFiles`; LaunchBox copies a file save itself (`TryBackupSave` only for a container).
  `TryComputeSaveSignature` (live copy vs vault copy) decides whether anything changed - same signature, no backup.
- **Game Saves**: `GetSaves`, `TryGetSaveGroupInfo`, `IsSaveContainer`, `IsSecondarySaveFile`,
  `TryComputeSaveSignature`, `GetCompanionSaveFiles`, and **`IsSaveActive` hundreds of times** (82 then 342 then 176 per
  opening): it must stay cheap.
- **Restore of a state**: `GetPotentialSaveSlots` (the slot LaunchBox asks for), then `AddSaveFile`. The precautionary
  backup of the save in place is a copy by LaunchBox (`-01`), with the companions.
- **`SupportsSaveManagement` = false**: no more `GetSaves`, but rows already in the XML stay on screen and LaunchBox
  still asks the game's plugin about each of them.

### The vault and the rows

- Copies in `Saves\<platform>\<original name>`; a name already taken gets `-01`, `-02` (it never overwrites). Some states
  are renamed `<stem>.state` (ares's `.bs1` of Sonic CD became `Sonic CD (U).state`, same hash).
- Rows are `<GameSave>` elements in `Data\Platforms\<platform>.xml`: the save in place (no `Title`, `FilePath` = the live
  file) and the vault copies (`Title` "Saved Game" / "Save State N", `FilePath` = `Saves\…`).
- **A row is created only when a backup is made or Game Saves is opened**, not by the start-up scan alone.
- **A row keeps its `SaveGroupId` forever**: a plugin that later gives another id for the same file is matched by path
  and the old id stays. The plugin's id only counts when the row is created.
- **`SaveGroupId` is the card**: rows with the same id are one card and its Backup History. Changing a vault row's id
  moves that copy to another card (that is all "Combine With Another Save" does - the code of LiteBox notes it as
  measured: `SaveGroupId` and `MatchLineageId` rewritten, no file moved).

### Limits of `SaveGroupId` (tests 6-9)

A 1 938-character canonical JSON id - quotes, `<>&`, accents, CJK, emoji, tab, `\`, `#` and `|` escaped as
`\u0023` `\u007c` - is stored (XML-escaped), and comes back **identical byte for byte** after the vault, a game's close,
a restart and a restore (`AddSaveFile`: "IDENTICAL to the listing's"). No size or character limit met on the LaunchBox
side.

### How a listed save finds its card (tests 10-12, 08/10)

A card in Game Saves IS a `SaveGroupId`: every row of the game with that id sits under it (the live file and the vault
copies). Measured with the probe on "SaveProbe Test" (XML before/after in
`Backups\lbip-saveprobe-2026-10-07\promote-test-before\`, `before-testA.xml`, `before-testB.xml`, `after-testB.xml`):

| The listing gives | Result |
|---|---|
| a new save, never seen (startup scan only) | listed, but NO row is created - rows only come from opening Game Saves or closing the game (as on 07/10) |
| the same file under another id (`…:v2`) | found by its PATH: the existing row and its id are kept, nothing created |
| the same id from another folder (`saves2\`) | a NEW row, with that id - so in the SAME card (its backup count grows by one); the old row stays |
| another id AND another folder | a new card (two cards for one save) |

`UseSaveGroupIdForPersistedMatch` was never called on the probe (07/10: only on RetroArch), so none of this depends on
it. For us: a stable id keeps a save in its card when an emulator's folder moves; an id that changes only splits the
history if the file moves at the same time. `MatchLineageId` was not needed and is not measured.

### When a listed save takes another card (measured 08-09/10, Secret of Mana)

RASnes9x's `Secret of Mana (USA).srm`, listed by SUPER ZSNES's front desk, was hidden: LaunchBox put it in SUPER
ZSNES's card, under SUPER ZSNES's id. The rule, by calling `AddEditGameSavesPageViewModel.ApplyPersistedMetadata`
(internal static, real body by reflection) on rows built by hand:

- a listed row NOT found by its path among the persisted rows takes the `SaveGroupId` and lineage of a same-game backup
  whose `OriginalFileName` (or, when empty, its file name) is the listed row's file name - whatever its exe, id, lineage
  or own `OriginalFileName`. Another file name, or another game, does not match; `NormalizeSaveMetadata` takes nothing;
- the listed row and its persisted row must be on the SAME game or version to be found by path. LaunchBox persists a
  save of the game on the version that runs the game's own file ("Play Version..."), even when it was listed with no
  version. Ours were listed untied (§ "A version on the same file", below), so they were never found: the persisted row
  counted as a backup (ares 1 instead of 0) and the listed one was matched by name against the vault copies, which are
  rows of the game (SUPER ZSNES 4 instead of 2, the RASnes9x file in its history). RetroArch's rows, listed tied, had
  neither problem.

What the front desk does (plan A, `SavePersist`, one game on screen only):

1. a row of the game listed with no version is tied to the version on the game's own file, when there is one
   (`TieLikeLaunchBox`): listed and persisted meet;
2. a row with no persisted row at its path gets one (`Ensure`, in `Game.Saves` or the version's `Saves`,
   `BlockingCollection<GameSave>`, by reflection) - from the first opening, before LaunchBox writes its own at close;
3. a persisted row of OURS on another game or version than the listed row is moved to it (emptied and refilled: the
   collection has no Remove). Rows that are not ours are never touched.

Measured 09/10: four separate cards, RASnes9x 0, SUPER ZSNES 2, ares 0, RetroArch 0, at the first and the second
opening, and after a LaunchBox restart; one persisted row per path.

Also answered by the desk (`TryGetSaveGroupInfo`): a row that carries its `SaveGroupId` gives it back, else LaunchBox
picks a group itself, from the tile's constructor (`SaveItem..ctor`).
### The integrations shipped with LaunchBox

They are separate plugins in `System\Plugins\`, **not obfuscated** (real IL bodies: `RetroArchPlugin.GetSaves` 2 526
bytes, `Pcsx2Plugin.AddSaveFile` 2 263, `DolphinPlugin.GetSaves` 1 041), so Harmony can patch them - unlike the core
(`LaunchBox.dll`, `Unbroken.LaunchBox.Windows.dll`), whose bodies are decoys.

| Plugin | Save methods it overrides |
|---|---|
| RetroArch | `GetSaves`, `AddSaveFile`, `IsSecondarySaveFile`, `GetCompanionSaveFiles`, `TryGetSaveGroupInfo`, `UseSaveGroupIdForPersistedMatch`, `TryComputeSaveSignature`, `GetPotentialSaveSlots`, `SupportsSaveManagement` |
| Dolphin | `GetSaves`, `AddSaveFile`, `RemoveSave`, `UseSaveGroupIdForPersistedMatch`, `GetPotentialSaveSlots`, `SupportsSaveManagement` |
| PCSX2 | everything (`IsSaveActive`, `IsSaveContainer`, `TryBackupSave`, …) |
| ScummVM, RPCS3, DuckStation, MAME, Cemu, xemu, BigPEmu | **none**: the SDK's default bodies run (`SupportsSaveManagement` → false) |

DOSBox and the "Use ScummVM" game option are built into the core: such a game has no emulator, so no plugin is ever
asked. LaunchBox's help names RetroArch, Dolphin and PCSX2 as the only save-managed emulators.

### Proven with the probe

- **Catch-all**: LaunchBox asks each plugin, emulator by emulator (`PluginRegistry.GetBestEmulatorPlugin` →
  `GetApplicableEmulators`), whether it takes it. A plugin claiming a hand-added emulator (FakeEmu, `FakeEmulateur.exe`)
  then gets `SupportsSaveManagement` and `GetSaves` for that emulator's games, and its save is recorded.
- **Translating a native integration's id**: Harmony on `RetroArchPlugin` - its rows leave with our JSON
  (`{"emu":"retroarch","kind":"saturn","by":"name","id":"atlantis - the lost tales (fr) (disc 1)"}`), and every save
  method gets `saturn-atlantis - the lost tales (fr) (disc 1)` back on the way in (`TryComputeSaveSignature`,
  `AddSaveFile`), our JSON again on the way out (the row `AddSaveFile` returns). LaunchBox stored only the JSON; the
  restore wrote the set, companions included.

### How the stock integrations find the emulator at restore (read 08/10, decompiled)

`AddSaveFile` carries the row only, never the emulator. Each integration finds it its own way:

| Integration | Restore finds the emulator by | From another plugin's front desk (lot 3) |
|---|---|---|
| RetroArch | the GAME's emulator (`game.EmulatorId`, or the version's), the core from the game's command line | wrong folders; listing fine (gets `args.Emulator`) except the Saturn detection (game's core) |
| Dolphin | the GAME's emulator (`TryResolveContext`), then `BuildUserRoots(emulator)` | wrong GameCube/Wii folders; listing fine |
| PCSX2 | `save.EmulatorFileName`, an exe NAME matched against the library, first found | fine once the row carries the right exe name |

Lot 3 restores RetroArch and Dolphin rows itself, through their internal folder/restore functions called with the
row's emulator (reflection; refused and logged if a function is gone). **A future Nixx-RetroArch or Nixx-Dolphin**
(Mehdi, 08/10) shares the exe name with the stock one: never route on the exe name alone (PCSX2's way), never on the
game's emulator alone (RetroArch's and Dolphin's); the owner comes from the id (`retroarch` vs `lb.retroarch`), the
exact emulator from `EmulatorCore` + platform.
### Restoring a state: the slot (measured 08/10, the save probe on "SaveProbe Test")

- The slot list is `GetPotentialSaveSlots()` of the plugin of the GAME's emulator, asked at each restore (`RestoreSaveCommand`), never cached. It takes no argument: a state owned by another emulator gets the front desk's list (Zelda: SUPER ZSNES's 0-9 for an ares state, ares numbers 1-9).
- **An empty list refuses every state restore** ("The emulator for this game/app doesn't appear to support save state restoration!"), the plugin is not called. Today: xemu and bsnes, whose lists are empty.
- The picker shows the list's LABELS and preselects its FIRST entry - not the backup's slot.
- The pick arrives as `SaveToAdd.Slot`; the rest of the row is the backup's, its id included. -1 is passed as is.
- A tile shows "Slot " + the row's `Slot` KEY, never the label; -1 shows "Slot Auto". Hence PPSSPP's "Slot 0" for its "1".
- After a restore the tile shows the returned row's `Slot` at once. Returned with the picked slot and the id recomputed for it, a new tile appears for that slot and the backup's own tile is untouched - coherent.
- The confirmation ("Promote this backup...") comes BEFORE the slot picker; `ShouldOverwriteFunc` then answers true without asking anything.
- **The precautionary copy is of the backup's own tile, not of the destination.** Restored into another, occupied slot, the destination's state is overwritten with no copy kept (seen: slot 1 overwritten, saved only because an identical vault copy existed from 07/10). RetroArch and Dolphin, which honour the pick, have the same hole.
- With a content signature (the probe's), a live file changed behind LaunchBox's back is seen when Game Saves is reopened; without one (our plugins) it was not, until a restart.

The stock integrations: RetroArch and Dolphin list Auto (-1) and 0-9 and honour the pick (`.state<n>` / `<disc>.s<NN>` - Dolphin's Auto would make `.s-01`, not measured); RetroArch's states have no id, Dolphin drops it on the returned row; PCSX2 lists no slot (so, per the above, its states cannot be restored).
Our plugins before the fix: ares, SUPER ZSNES, RASnes9x, Flycast, melonDS write the backup's own name (the pick ignored) and return the PICKED slot with the backup's id - three slots disagreeing; PPSSPP takes the slot from the id; xemu honours the pick; xemu and bsnes list nothing.
### Companions and the signature (measured 08/10, the save probe)

- A backup carries the save's companions (`GetCompanionSaveFiles`): `SaveProbe Test-02.mainsave` and `-02.rtc` came together.
- **Without a signature of the plugin's own (`TryComputeSaveSignature` false, as all our plugins), LaunchBox compares the
  primary file only**: the companion alone changed, LaunchBox restarted, the tile still says backed up and Backup is
  refused. A companion's change is never backed up. Our plugins with companions that are data: bsnes and RASnes9x (.rtc),
  ares (64DD clock), Flycast (the rest of an arcade set). Hence a signature over the primary AND its companions.
- The vault takes a numbered name when the name is taken, whichever tile the other copy belongs to (`-02.srm` for SUPER
  ZSNES's Zelda .srm beside RetroArch's `.srm` and `-01.srm`).
### LaunchBox defects met

- A save tied to an additional application (Atlantis, "Play (FR) (Disc 1)"): **Backup Save** fails with "Can't find the
  game for this save item!", and so does a Restore that needs the precautionary backup (measured 20/09 without any
  plugin, met again 07/10). Automatic backup at close works. Met again 09/10 on Secret of Mana (a version on the game's
  own file): Backup Save fails on RetroArch's stock card too, and on ours tied or untied - nothing a plugin can do. The
  Restore of a SUPER ZSNES vault copy, tied, went through.
- A restore of another emulator's save goes to the game's emulator (above).
- **Make Default** (Edit Game > Additional Versions, measured 08/10 on "SaveProbe Test", XML before/after in
  `G:\LB1326\Backups\lbip-saveprobe-2026-10-07\promote-test-before\`): it does not swap, it OVERWRITES the game with
  the version - the game takes the version's `ApplicationPath` and `Version`, its `Developer`, `Publisher`,
  `Region`, `Status` are emptied (the version had none), the version row stays as it was (same GUID, same path, so
  the game and the version now share one file), and the old main file is referenced nowhere. Every save row of the
  game is DUPLICATED onto the promoted version (16 → 30 rows): the old rom's saves now also show under the new rom's
  version. Seen with the probe plugins only; **to reproduce with RetroArch (stock) before reporting it** (a SNES game,
  a version on another file, an .srm for each, Make Default, compare the XML). LiteBox's own Make Default keeps the old
  file as a version and moves the game's saves onto it (`GameSaveMover.FollowDemotedRom`).
## 2. What it means for us

| The game's emulator is… | How we take part |
|---|---|
| a Nixx plugin | that plugin lists the other Nixx plugins' saves for the game too, and hands every call on a row to the plugin that owns it |
| RetroArch, Dolphin or PCSX2 | Harmony on their save methods: add our rows to `GetSaves`, route our rows' calls to us, let theirs through |
| ScummVM, RPCS3, DuckStation, MAME, Cemu, xemu, BigPEmu | Harmony on the SDK's default bodies for those instances: `SupportsSaveManagement` true, `GetSaves` filled, calls routed |
| no plugin (hand-added exe) | a catch-all plugin claiming the emulators nobody claims (known exe names excluded) |
| no emulator (DOSBox, "Use ScummVM", Windows games) | nothing possible: LiteBox |

**Route by the row's owner, read from its id** (§3, `SaveOwner`): our JSON names it in `emu`, LaunchBox's integrations
write forms of their own. `EmulatorFileName` holds the exe's NAME only (measured 08/10), so it can only be the last
resort, with `EmulatorCore`, for an id nobody recognises. LiteBox's own forms are left to LiteBox: it rewrites ids
(`entry:…` for SmartExtract) and decorates them (`#cN`, `#web`, `#b:<name>`, `#<8hex>`) - a `#` never occurs inside our
JSON, so a decorated id still reads.

Under LiteBox (which asks every plugin itself) the Harmony layers must stay off, or rows are listed twice.

### Refused (Mehdi, 07/10)

- An additional application "Play with <emulator>" per Nixx emulator: its saves would be tied to an additional
  application, whose restore is broken (defect above).
- Changing the game's `EmulatorId` for the length of a session; injecting rows into the WPF view.

## 3. Our ids: before / after (proposal, 07/10)

Canonical JSON: keys always in this order, no spaces, `#` → `\u0023`, `|` → `\u007c`; a key that does not apply is left
out. `emu` the plugin that owns the row - one value per Nixx plugin (`ares`, `xemu`, `cxbx`, ...), `lb.<name>` for a
LaunchBox integration (`lb.retroarch`, `lb.pcsx2`, ...), never shared even when two emulators read the same save;
`kind` (`save`, `state`, `console`, `nand`, `vmu`,
`nvram`), `sys` (only where a plugin has several systems), `by` (`name`, `title`, `disc`, `product`, `set`), `id`,
`slot` (the emulator's own number).

| Plugin | Before | After |
|---|---|---|
| ares save | `ares:save:MegaCD:Sonic CD (U).bram` | `{"emu":"ares","kind":"save","sys":"Mega CD","by":"name","id":"Sonic CD (U).bram"}` |
| ares console copy | same form as a save | `{"emu":"ares","kind":"console","sys":"Mega CD","by":"name","id":"Sonic CD (U)"}` |
| ares state | `ares:state:MegaCD:Sonic CD (U):1` | `{"emu":"ares","kind":"state","sys":"Mega CD","by":"name","id":"Sonic CD (U)","slot":1}` |
| bsnes | `bsnes:save:<stem>` / `bsnes:states:<stem>` | `{"emu":"bsnes","kind":"save","by":"name","id":"<stem>"}` / `…"kind":"state"…` (one `.bsz`, no slot) |
| RASnes9x, SUPER ZSNES | `rasnes9x:save:<stem>` / `…:state:<stem>:0` | `{"emu":"rasnes9x","kind":"save","by":"name","id":"<stem>"}` / `…,"slot":0}` |
| melonDS, no$gba cartridge | `melonds:save:<asset>` / `melonds:state:<asset>:1` | `{"emu":"melonds","kind":"save","by":"name","id":"<asset>"}` / `…,"slot":1}` |
| melonDS, no$gba DSiWare | `melonds:dsiware:00030004484e4741` | `{"emu":"melonds","kind":"nand","by":"title","id":"00030004484e4741"}` |
| Flycast VMU / arcade / state | `flycast-vmu:<product>` / `flycast-arcade:crzytaxi.zip` / `flycast-state:<stem>:0` | `{"emu":"flycast","kind":"vmu","by":"product","id":"<product>"}` / `…"kind":"nvram","by":"set","id":"crzytaxi"}` / `…"kind":"state","by":"name","id":"<stem>","slot":0}` |
| PPSSPP | `ppsspp:ULUS10041` / `ppsspp-state:ULUS10041:0` | `{"emu":"ppsspp","kind":"save","by":"disc","id":"ULUS10041"}` / `…"kind":"state",…,"slot":0}` |
| Xenia | `xenia:4D5307E6` | `{"emu":"xenia","kind":"save","by":"title","id":"4D5307E6"}` |
| xemu, Cxbx | `cxbx:4d530064` (both) / `xemustate:4d530064:1` | `{"emu":"xemu","kind":"save","by":"title","id":"4D530064"}` and `{"emu":"cxbx",…}` / `{"emu":"xemu","kind":"state","by":"title","id":"4D530064","slot":1}` (slot from the snapshot's NAME) |
| Vita3K | `vita3k:title:PCSE00000` | `{"emu":"vita3k","kind":"save","by":"title","id":"PCSE00000"}` |

**Where a save came from is the row's, not the id's (Mehdi, 08/10, after a review of lot 1).** A first version of
lot 1 also froze the save's origin into the id - the game's GUID (`game`), the version's (`ver`) and a fingerprint of
the rom (`rom`: CRC32 of the whole file up to 10 MB, of three 1 MB samples above). All three were dropped:

- **The row already carries the origin**, and keeps it up to date: `GameId` and `AdditionalApplicationId`, filled by our
  `GetSaves`, kept by LaunchBox, re-pointed by LiteBox (its Make Default moves a game's saves onto the version that
  keeps the old rom, its combine moves them to the absorbing game). `AddSaveFile` hands the row over. A copy frozen in
  the id would drift from it.
- **The id must be the same string at every listing**: a card IS a `SaveGroupId` (tests 10-12, §1). The same file
  under another id keeps its card through its path, but a moved file with a changed id makes a second card. An origin
  that changes - a rom patched, a row moved by LiteBox - changed the id with it.
- **A fingerprint costs a read of every rom at every listing**: the full scan at LaunchBox's start lists every game of
  every emulator - gigabytes on a disk or a NAS, a whole download for a OneDrive file on demand - for two rare cases (a
  warning "another file", and finding a file swapped by hand).

So the id is an identity and nothing else - every field stable, none read from the disk:

```
{"emu":"ares","kind":"save","sys":"Mega CD","by":"name","id":"Sonic CD (U).bram"}
```

- **Restore**: `AddSaveFile` gets the row. The target is the version the row is tied to (`AdditionalApplicationId`),
  else the game - its CURRENT path, so a rom renamed (and updated in LaunchBox) is followed, and LiteBox's Make Default
  needs nothing more. A row tied to a version that no longer exists goes to the game WITH a warning, never silently; a
  missing file is kept as the target, with a warning. Today our plugins resolve the target from the game's path and
  ignore the row's version - to fix.
- **A version on the same file as its game**: the save is NOT tied to the version (`AdditionalApplicationId` left
  empty, in our `GetSaves`, and through Harmony for RetroArch). It is the same file, nothing is lost, and LaunchBox's
  "Can't find the game" defect is avoided (test 9b: untied, the Atlantis restore went through).
  **Overruled for the listing on 09/10**: LaunchBox persists the row on that version anyway, so the front desk ties it
  there before handing it over (§1, "When a listed save takes another card"), and the manual Backup Save fails tied or
  not, RetroArch's stock card included. The restore target is unchanged: that version runs the game's own file.
- **A version on ANOTHER file**: whether LaunchBox's defect hits it too was never measured. Set aside (Mehdi, 08/10).
- **A renamed rom is not followed at launch**: no way, through the SDK, to read LaunchBox's save rows before a launch.
  The old save is not lost (its card keeps it) and a Restore puts it under the new name. LiteBox, which does the
  renaming, can follow it.

Not ours to change: LaunchBox's (`pcsx2:…`, `saturn-…`, RetroArch GUIDs) and LiteBox's (`entry:…`, `unit:…`). No
migration (no user base), but existing rows keep their id: start from a cleaned XML. Nothing that can change over a
save's life goes in the id.

Decided (Mehdi, 08/10): `slot` is the emulator's own number; `sys` only where a plugin has several systems; the
extension stays in `id`. **One serializer** writes every id, and it is System.Text.Json's, not ours: `JsonSerializer`
over one class whose properties carry `[JsonPropertyOrder]` (the schema's order), camelCase names, nulls omitted, no
indentation, and an encoder that lets every Unicode range through except `#` and `|` (escaped by the library as
`\u0023`, `\u007C`). Reading is `JsonSerializer.Deserialize`. No home-made writer or parser. So two identical states
always give the same string (LaunchBox and LiteBox compare the string, never the JSON); self-tests pin the exact
strings, so a .NET update that changed one byte would be seen. Whatever the setting, .NET also escapes the quote and
the HTML-sensitive characters (`"` `<` `>` `&` `'` `+` `` ` ``, as `\u0022`...) and writes characters outside the BMP (emoji)
as a surrogate pair (measured 08/10) - stable, only less readable.

**Who owns a row** (never the exe name: `EmulatorFileName` holds the exe's FILE NAME only - `retroarch.exe`,
measured 08/10 - and two plugins may one day drive the same exe): (1) our JSON → its `emu`; (2) a known native form
(`pcsx2:…`, `saturn-…`, a RetroArch GUID, or a translated JSON carrying `native`) → that LaunchBox integration; (3)
otherwise, rarely, the plugin LaunchBox picked for the game's emulator. The list of known exe names only serves the
catch-all's exclusions (lot 6).

## 4. Defects found in our plugins (read, not reproduced)

- no$gba DSiWare: `IsOurs` misses `nogba:dsiware:` → path match instead of id, `IsSaveActive` always true, `RemoveSave`
  returns before the DSiWare branch (the `.dsisave` comes back from the NAND at the next listing).
- ~~Xemu, Cxbx, Vita3K: no `UseSaveGroupIdForPersistedMatch` → matched by path, fragile if the folder moves.~~ Not a
  defect (tests 10-12, 08/10): LaunchBox never called it on the probe, and a save listed with the same id from another
  folder joins its card anyway. Nothing to do beyond keeping ids stable.
- Name-based ids can contain `#` ("Shin Megami Tensei #FE"), which LiteBox cuts at.
- xemu: the LaunchBox slot follows the order snapshots appear in, not their name - a snapshot gone can shift the others'
  ids.
- SUPER ZSNES: the restore folder is derived from the name inside the id (an old backup restores into the old name's
  folder).
- Our plugins resolve a restore's target from the game's path and ignore the row's version.
- (Not a defect: SUPER ZSNES writes into `Emulators\Nixx-SuperZSNES\saves` and `\states`; the `.data.szsnes\` folders
  beside two games are leftovers of 01/10, before that setting.)

## 4b. The plan (decided 08/10)

Decisions: **D1** LiteBox moves to .NET 10 / the LaunchBox 14 SDK only (the .NET 9 build dropped), the plugins with it -
a preliminary step of its own. **D2** the shared machinery lives in one plugin, **Nixx-Saves**. **D3** the stock
integrations' ids are translated to our JSON while LaunchBox runs; uninstalling through the setup puts their original
form back in the XML. **D4** no `.<plugin>save` archives for now. **D5** as above. **D6** a stock integration without
save support (ScummVM, RPCS3, DuckStation, MAME, Cemu, xemu, BigPEmu) answers `SupportsSaveManagement` true for a
platform as soon as ANY save-capable plugin - ours or stock - covers that platform.

| Lot | What | Done when |
|---|---|---|
| 0 | LiteBox net10-only on SDK 14.0.1.2; the plugins net10 on that SDK (init-only rows built in one initializer; `EmulatorFileName`, `EmulatorCore`, `SupportsSaveManagement`, `TryComputeSaveSignature`, `TryGetSaveGroupInfo`, `GetPotentialSaveSlots` available) | every plugin and LiteBox build and load on LB14 and LiteBox; probes green |
| 1 | **Done 08/10, see §4c.** `Shared.Saves` (compiled into each plugin): `SaveId` (System.Text.Json, an identity only, see §3), the row owner (§3), the helpers of our plugins (`IsOurs`, restore target from the row's version or the game and their current path, untie a version on its game's file, cheap `IsSaveActive`). Measured on the way: `EmulatorFileName` is the exe's name only; LaunchBox's Make Default (§1); how a listed save finds its card (tests 10-12) | probe `--saves-selftest`: pinned strings, round trip, awkward characters, same state = same string, foreign ids, restore targets |
| 2 | **Code done 08/10** (5613b46, d67338b, 43246d6, e7193ba, d6b1585 and the next): the 12 plugins with saves on JSON ids through `Shared.Saves` (Demul has none), with the per-plugin fixes of §4 (no$gba DSiWare, SUPER ZSNES states folder, restore to the row's version; xemu's slot already followed the snapshot's name); probes green on the save parts. **Real tests 08/10** on G:\LB1326 (3 old SUPER ZSNES rows removed first, XML in `Backups\lbip-lot2-2026-10-08\`): cards created with the JSON ids and backed up for SUPER ZSNES, melonDS DSiWare, Flycast arcade, PPSSPP state, xemu save + states, Xenia; restored byte for byte for SUPER ZSNES, Flycast and PPSSPP (live file altered first, LaunchBox greys Restore out when the vault copy is the same). Not restored for real: melonDS DSiWare and xemu (their live file is rebuilt from the NAND / the console at each listing - covered by the probes for DSiWare), Xenia (the only save is empty folders). Seen on the way: PPSSPP's slot list reads "1".."5" for slots 0..4 (the card says "Slot 0", the picker "1"); SUPER ZSNES's live row is not persisted (only its vault rows - the restore works); one LaunchBox stayed alive after a close with a `-recovery` process beside it, not reproduced, no plugin thread found that could hold it | one real game per plugin: save, close, vault, restore |
| 3 | **CLOSED 09/10** - the whole record is `docs\saves-lot3.md` (slots, warnings, RetroArch states, companions, the Codex review, measures). Before: **Done 08/10, real test passed** (Zelda on SUPER ZSNES, G:\LB1326: the ares save + state and the RetroArch .srm listed beside SUPER ZSNES's card, each with its own exe; backed up; live files altered; each restored byte for byte into its own emulator's folder - RetroArch through `StockRestore`, ares by forwarding; SUPER ZSNES's file untouched). Found on the way and fixed: LaunchBox constructs no plugin deriving from an abstract base (below); the nesting guard has to be process-wide (each plugin carries its own copy of the router); our rows must carry their exe name (empty, LaunchBox showed another row's); ares answered `IsSaveActive` "the file exists", so its vault copies were "Active" and not restorable. Seen, not fixed: a backup's "Active" verdict is not recomputed when a live file changes behind LaunchBox's back (a restart does it); the slot picker of a foreign state is the front-desk plugin's (SUPER ZSNES's), and ares restores a state under its backup's name whatever slot is picked. Was: **Code done 08/10, real test to do.** No Nixx-Saves plugin for this case (Mehdi, 08/10): a front desk `SaveDesk` in `Shared.Saves` in front of `SaveRouter`, held by each of the 12 plugins (their save code renamed `IOwnSaves.Own*`, the overrides in a generated `<Plugin>SaveDesk.cs`). **Not a base class**: with the plugins deriving from an abstract `NixxSavePlugin`, LaunchBox 14 loaded the DLLs and constructed none of them (measured 08/10: no "plugin constructed" line, the probe constructing the same DLL fine) - a plugin must derive from `EmulatorPlugin` directly; the pack's plugins find each other through `LbSaveOwners` in `LbIntegrations.Catalog` (registered first thing in the plugin's constructor, under a try: an old Catalog only costs the desk); LaunchBox's integrations found among the loaded assemblies of `System\Plugins` and instantiated by the router (`IPluginPaths` for RetroArch). The others asked are the library's emulators that cover the game's platform and whose plugin manages saves; their rows carry that emulator's exe name and core. RetroArch and Dolphin rows are restored by `StockRestore` (their internal steps, called by reflection with the row's emulator - refused and logged if one is missing); PCSX2 by plain forwarding. Off under LiteBox (`HostWillAsk`). Probe `--saves-selftest`: 11 checks added (the "ours" prefix, the core, the row copy). Case "the game's emulator is ours": the game's plugin lists every other save-capable plugin's saves for the game (ours AND stock), and every call on a row goes to its owner - read from the id (`SaveOwner`), the plugin found by its `emu` value; `EmulatorFileName` + `EmulatorCore` only for an id nobody recognises | Zelda on SUPER ZSNES: an ares save (Launch With) and the RetroArch .srm both listed, backed up at close, each restored into its own emulator's folder |
| 4 | Harmony on RetroArch, Dolphin, PCSX2: add the other plugins' rows to their `GetSaves`, route foreign rows, **translate their own ids to JSON** (`native` keeps the original, so the way back is exact); a row with no native id (RetroArch's generic saves) gets a JSON id whose way back is a fresh GUID per group; methods found by name and signature, missing = do nothing and log; a switch file turns it off | Zelda on RetroArch with an ares save; Atlantis translated both ways (done once with the probe, 07/10) |
| 5 | The stock integrations without save support, through the SDK's default bodies, under D6 | a PS1 game on DuckStation with a RetroArch and a Nixx save |
| 6 | The catch-all in Nixx-Saves: claims the emulators nobody claims (stock and pack exe names excluded), passes the launch through untouched, offers nothing in the emulator screens | FakeEmu: launch and Edit Emulator unchanged, its game's saves managed |
| 7 | **Code done 11/10** (setup + probe; the real uninstall to do). The setup: on uninstall, LaunchBox closed, rows whose id is a translated JSON get their `native` id back (or a fresh GUID per group when there was none), in every platform XML, after a backup. As delivered: `StockIdsXml` (Shared.Saves, pure, text in/out: only `SaveGroupId`/`MatchLineageId` of `<GameSave>` rows change; one new GUID per game and by-name id, a lineage follows its card, a `#c2` decoration kept - `StockIds.Reverse` keeps it too now) and `SaveIdsBack` (the setup, before any folder goes: all read and changed in memory, every platform XML copied to `Local\Plugins\.data\nixx-saves-backup\uninstall-<time>\`, each changed file written beside itself, read back, renamed into place; a failure puts back the files already written and **nothing is uninstalled**; read again after: none of ours left; LaunchBox/BigBox/LiteBox asked again just before the first write - one started meanwhile: nothing written - and just before the folders go - then the XML put back from the copy, nothing uninstalled). Decided 11/10: the pack's own rows (an ares save on a RetroArch game) are left as they are - what LaunchBox shows of them without the pack, to see at the real uninstall. Probe `--saves-selftest`: 19 checks (every kind of id, the rest of the file byte for byte, a write failing halfway, an unreadable file, the copy, the encodings); `--saves-back <root>` runs it on a copy - G:\LB1326's XML: 8 RetroArch rows of Zelda back to LaunchBox's GUIDs, nothing else changed | install → play → uninstall → the XML holds only stock formats, Game Saves as before |
| 8 | LiteBox: a state restore done right (lot 3, saves-lot3.md §6.7: the slot picked first, the backup's own preselected, a confirmation that says what is replaced, a copy of the file actually overwritten); the import's emulator chosen in a combobox among the plugins that can read the file (§8); an optional cleaning of orphan rows, with a confirmation (§11); Nixx-Saves's layers off when the host is LiteBox (it asks every plugin itself); read our JSON (`entry` for defect 4); the version view from the row's `AdditionalApplicationId`; offer to re-tie a save to another version (LiteBox owns its rows, LaunchBox's SDK does not let a plugin); fix defects 5-9 of the audit; optionally its own ids in JSON (defects 1-3) | LiteBox self-tests (EntrySaveSelfTest, RommSelfTest) + the games of lots 2-3 |
| 9 | Nothing beside the game: SUPER ZSNES leftovers, ares's hidden `.cue` beside an `.iso`, melonDS states, bsnes, the rest | no write in a games folder on a play of each plugin |
| 10 | The sandbox removed (§6), help pages, this document, the LaunchBox bug report followed, then the release with Mehdi | |

Order: lot 0, then 1 → 2 → 3 (the main case). Lots 4, 5, 6 independent of each other; 7 right after 4; 8 can start once
the JSON format of lot 1 is frozen.

## 4c. Lot 1 as delivered (08/10)

Commits on `savegroupid`: e87bda2 (first version), 7254de8 and 2c0d1b8 (after a Codex review: a safer fingerprint, no
redirection while the target exists), 6146281 (the id back to an identity: no `game`, `ver` or fingerprint), e3a5eea
(tests 10-12). Nothing is wired into a plugin yet: that is lot 2.

`src\Shared.Saves\`, compiled into each plugin (`<Compile Include="..\Shared.Saves\**\*.cs" />`), no package added:

| File | What it does |
|---|---|
| `SaveId.cs` | `SaveIdData` (`emu`, `kind`, `sys`, `by`, `id`, `slot`, `native`, in that order) and `SaveId.Write` / `TryRead` / `Canonical`, all System.Text.Json: `[JsonPropertyOrder]`, camelCase, nulls left out, no indentation, every Unicode range allowed except `#` and `|` (escaped by the library). `TryRead` answers false at once for anything not starting with `{`, and reads through a `#` decoration. |
| `SaveOwner.cs` | Who owns a row, from its id: our JSON → `emu` (`Nixx`, or `LaunchBox` for `lb.<name>`); `pcsx2:` → `lb.pcsx2`, `saturn-` → `lb.retroarch`; `entry:` / `unit:` → LiteBox; anything else (a bare GUID) unknown. |
| `SaveRows.cs` | `IsOurs(id, emu)`; `SameId` (IsSaveActive: an ordinal string comparison, nothing else); `TieTo` (a version on its game's file is not tied); `ChooseTarget` / `TargetFor` (the restore target: the row's version, else the game, current path; a version that no longer exists → the game WITH a warning; a missing file → kept, with a warning). |

The probe: `Probe --saves-selftest` (`src\Probe\SavesCheck.cs`), 51 checks - pinned strings (including what .NET
escapes on its own: `"` `<` `>` `&` `'` `+` `` ` `` and emoji as a surrogate pair), round trip, same state = same string, a
spaced id and a LiteBox decoration read back, awkward characters and a 1 950-character name, foreign ids and owners,
restore targets (both Make Defaults, a version gone, a file gone, the untie rule).

Decided along the way (Mehdi, 08/10), and why:
- **JSON written and read by System.Text.Json only**: no home-made writer or parser.
- **`emu` = the plugin that owns the row**, one value per plugin (`xemu` and `cxbx` apart, no shared `xbox`),
  `lb.<name>` for a LaunchBox integration: two plugins may one day drive the same exe.
- **The owner is read from the id, never from the exe**: `EmulatorFileName` is the exe's name only.
- **No origin in the id** (`game`, `ver`, a fingerprint of the rom were tried, then dropped): the row carries and keeps
  it, a frozen copy drifts, and a fingerprint costs a read of every rom at every listing. The id is an identity.
- **A restore never redirects while its target exists.**

Measured along the way: `EmulatorFileName` (exe name only); LaunchBox's Make Default (§1, "LaunchBox defects met" -
to replay with RetroArch before reporting it); how a listed save finds its card (§1, tests 10-12).

Effect on the rest of the plan: lot 2 has one fix less (the persisted match, §4); lot 3 routes by the owner in the id
rather than by `EmulatorFileName`, and needs the map "`emu` value → loaded plugin"; lots 4 and 7 keep `native` as
designed; lots 5 and 6 give the rows our layers create (a stock emulator without save support, an unclaimed one) an
`emu` of their own (`lb.duckstation`... and one for the catch-all), and lot 7 must then decide what becomes of those
rows at uninstall - they have no native id to give back; lot 8 reads the version from the row. The sandbox is back
to its state before tests 10-12 (§6).


## 5. The bug report (draft for LaunchBox, stock integrations only)

Title: *Save Management only looks at a game's default emulator, so saves made with "Launch With" or a second
RetroArch entry are not tracked (LaunchBox 14.0.1).* Three cases: a second RetroArch entry with another N64 core
(`parallel_n64`) used through Launch With; a game moved from RetroArch to DuckStation (old rows frozen, new progress
never captured); PCSX2 used through Launch With on a game assigned to RetroArch (memory card never backed up). Plus the
restore side (a backup made by another emulator is written by the game's emulator into its own folder) and three asks:
check the emulator actually used at close, route a restore by the row's `EmulatorFileName`, list every configured
emulator's saves. The examples are deduced from the measures above, not replayed with the stock integrations.

### Restoring a save state into another slot loses the state that was there (reproduced 08/10, stock RetroArch only)

Game "Rolan's Curse" (Game Boy) on the stock RetroArch integration, Gambatte core; two states in RetroArch's states
folder, slot 1 and slot 2, different content; only slot 2 backed up.
1. Game Saves > slot 2's tile > its backup > Restore; "Promote this backup...?" Yes; "Pick a slot": 1; "Restored ... and
   archived previous active save as ...".
2. Slot 1's file now holds the backup - as asked. The precautionary copy LaunchBox made (`-01`) is of SLOT 2's live file,
   which the restore did not touch. Slot 1's previous state is gone: not in the states folder, not in the vault, nowhere.
3. Until Game Saves is reopened, everything shows on slot 2's tile; then slot 1 gets its own tile again.

Expected: the precautionary copy is of the file the restore overwrites (the picked slot's). Also: the confirmation is
asked BEFORE the slot is picked (the user cannot know what will be overwritten), and `ShouldOverwriteFunc`, which the
integration calls when the picked slot is occupied, answers true without asking. Same with any integration that honours
the pick (Dolphin; measured with a test plugin).
## 6. The sandbox left on G:\LB1326

- Plugins `Local\Plugins\Nixx-SaveProbeMain`, `…Secondary`, `…CatchAll`; emulators `Emulators\Nixx-SaveProbe*`
  (entries in `Emulators.xml`, ids `5a7e0000-…-a001/a002`); games "SaveProbe Test" (`…b001`, on Main) and "SaveProbe
  CatchAll Test" (`…b002`, on FakeEmu), files in `G:\LB\FakeCatalog\`.
- The vault was emptied and the `<GameSave>` rows removed before the tests; the test rows and copies are what is there
  now. Atlantis's two versions and its live Saturn files were put back.
- Back to the state before: `tools\save-probe\deploy-probe.ps1 -Remove`, delete the three emulator entries, copy
  `Backups\lbip-saveprobe-2026-10-07\Data` and `\Saves` back over `Data` and `Saves` (LaunchBox closed).
