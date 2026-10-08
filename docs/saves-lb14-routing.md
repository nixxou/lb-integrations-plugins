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

### LaunchBox defects met

- A save tied to an additional application (Atlantis, "Play (FR) (Disc 1)"): **Backup Save** fails with "Can't find the
  game for this save item!", and so does a Restore that needs the precautionary backup (measured 20/09 without any
  plugin, met again 07/10). Automatic backup at close works.
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

**Route by `EmulatorFileName`** (+ `EmulatorCore`), as LiteBox does (`PairForCore`) - never by the `SaveGroupId`, which
LiteBox rewrites (`entry:…` for SmartExtract) and decorates (`#cN`, `#web`, `#b:<name>`, `#<8hex>`).

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

**The original game, recorded in the id (Mehdi, 08/10).** A row keeps the id it was created with (measured, test 7), so
the id is also where the save's origin is kept - no extra file (no index, no sidecar, no stream). Three more keys,
written once at creation:

- `game`: the LaunchBox game's GUID; `ver`: the additional version's GUID when the save belongs to one, else left out;
- `rom`, the fingerprint of **the file LaunchBox launches**, as it is on disk - the version's `ApplicationPath` if
  the save belongs to a version, else the game's: `{"size":…,"crc":"<CRC32 of the whole file>"}` up to 10 MB,
  `{"size":…,"head":…,"mid":…,"tail":…}` above, the CRC32 of the first, the middle and the last MB, reached by seeks
  (3 MB read whatever the size) - a SAMPLE, not a proof: two variants of one disc of the same size patched outside the
  samples print the same (Codex review, 08/10). An archive is fingerprinted as the archive: nothing is opened or
  unpacked. No creation date (it changes with every copy). Kept in memory per session, keyed on path + size + last
  write time: an unchanged file is never read twice; no file on disk. **No `rom` at all** when the plugin asks for none, or
  when the file cannot be read safely (a disk image open in its emulator, a VHDX, falls here and needs nothing else): it is opened with no other writer allowed, its size taken from that handle, a short read
  refused, its write time compared before and after - any doubt, no print, since a wrong one would be frozen for ever.

```
{"emu":"ares","kind":"save","sys":"Super Famicom","by":"name","id":"Zelda (France).ram","game":"2cf24c23-…","rom":{"size":1048576,"head":"9A1C…","tail":"44E0…"}}
```

What they are for:
- **Restore**: `AddSaveFile` gets the row and its id. The plugin takes the CURRENT path of the version (`ver`) or the game
  (`game`) and writes the save under the name the emulator expects today, even after a rename. When that file
  EXISTS, the save is written there whatever its fingerprint - a different one is only logged, never a
  redirection (patch the main ISO and keep the original as a version: the save still goes to the game launched). When it
  is GONE, the game's and its versions' files are fingerprinted, and ONE matching file is taken; none or several keep
  the target above with a warning - never a dialog, the save stays in the vault (Mehdi, 08/10). Only the game's own files are
  searched, never a folder: a game whose path is broken in LaunchBox is LaunchBox's to fix.
  The target is taken in this order: the version the ROW is tied to today (`AdditionalApplicationId`, which LiteBox's
  Make Default updates), else `ver`, else `game`. Today our plugins resolve the target from the game's path and ignore
  the row's version - to fix.
- **Never to match**: the listing still matches by name/path; when the rom changes, the listing's id differs, LaunchBox
  falls back to the path and keeps the original id - the card and its history stay together.
- **A version on the same file as its game**: the save is NOT tied to the version (`AdditionalApplicationId` left
  empty, in our `GetSaves`, and through Harmony for RetroArch). It is the same file, nothing is lost, and LaunchBox's
  "Can't find the game" defect is avoided (test 9b: untied, the Atlantis restore went through).
- **A version on ANOTHER file**: whether LaunchBox's defect hits it too was never measured. Never tying a save to a
  version (relying on `ver`) would avoid it in every case, but the save would then show under the game, not the version.
  Set aside (Mehdi, 08/10).
- **A renamed rom is not followed at launch**: no way, through the SDK, to read LaunchBox's save rows before a launch.
  The old save is not lost (its card keeps it) and a Restore puts it under the new name. LiteBox, which does the
  renaming, can follow it.

Not ours to change: LaunchBox's (`pcsx2:…`, `saturn-…`, RetroArch GUIDs) and LiteBox's (`entry:…`, `unit:…`). No
migration (no user base), but existing rows keep their id: start from a cleaned XML. Nothing that has to change over a
save's life goes in the id - only what identifies it and, frozen at creation, where it came from.

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
- Xemu, Cxbx, Vita3K: stable ids but no `UseSaveGroupIdForPersistedMatch` → matched by path, fragile if the folder moves.
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
| 1 | `Shared.Saves` (compiled into each plugin, System.IO.Hashing merged like the other packages): `SaveId` (System.Text.Json, see §3), `RomPrint` (10 MB rule, session cache), the row owner (§3), a base for our plugins (`IsOurs`, persisted match by id, restore target from `ver`/`game` and the current path then by fingerprint, untie a version on its game's file, cheap `IsSaveActive`). Before the restore part: measure in the sandbox what LaunchBox does when an additional version is promoted to main (GUIDs, save rows) | probe `--saves-selftest`: pinned strings, round trip, awkward characters, same state = same string, RomPrint both sizes + cache, foreign ids |
| 2 | The 13 plugins on JSON ids through the base, with the per-plugin fixes of §4; start from a cleaned XML | one real game per plugin: save, close, vault, restore |
| 3 | Nixx-Saves, case "the game's emulator is ours": the game's plugin lists every other save-capable plugin's saves for the game (ours AND stock, through the registry), and every call on a row goes to its owner (`EmulatorFileName` + `EmulatorCore`) | Zelda on SUPER ZSNES: an ares save (Launch With) and the RetroArch .srm both listed, backed up at close, each restored into its own emulator's folder |
| 4 | Harmony on RetroArch, Dolphin, PCSX2: add the other plugins' rows to their `GetSaves`, route foreign rows, **translate their own ids to JSON** (`native` keeps the original, so the way back is exact); a row with no native id (RetroArch's generic saves) gets a JSON id whose way back is a fresh GUID per group; methods found by name and signature, missing = do nothing and log; a switch file turns it off | Zelda on RetroArch with an ares save; Atlantis translated both ways (done once with the probe, 07/10) |
| 5 | The stock integrations without save support, through the SDK's default bodies, under D6 | a PS1 game on DuckStation with a RetroArch and a Nixx save |
| 6 | The catch-all in Nixx-Saves: claims the emulators nobody claims (stock and pack exe names excluded), passes the launch through untouched, offers nothing in the emulator screens | FakeEmu: launch and Edit Emulator unchanged, its game's saves managed |
| 7 | The setup: on uninstall, LaunchBox closed, rows whose id is a translated JSON get their `native` id back (or a fresh GUID per group when there was none), in every platform XML, after a backup | install → play → uninstall → the XML holds only stock formats, Game Saves as before |
| 8 | LiteBox: Nixx-Saves's layers off when the host is LiteBox (it asks every plugin itself); read our JSON (`entry` for defect 4, `ver` for the version view); fix defects 5-9 of the audit; optionally its own ids in JSON (defects 1-3) | LiteBox self-tests (EntrySaveSelfTest, RommSelfTest) + the games of lots 2-3 |
| 9 | Nothing beside the game: SUPER ZSNES leftovers, ares's hidden `.cue` beside an `.iso`, melonDS states, bsnes, the rest | no write in a games folder on a play of each plugin |
| 10 | The sandbox removed (§6), help pages, this document, the LaunchBox bug report followed, then the release with Mehdi | |

Order: lot 0, then 1 → 2 → 3 (the main case). Lots 4, 5, 6 independent of each other; 7 right after 4; 8 can start once
the JSON format of lot 1 is frozen.

## 5. The bug report (draft for LaunchBox, stock integrations only)

Title: *Save Management only looks at a game's default emulator, so saves made with "Launch With" or a second
RetroArch entry are not tracked (LaunchBox 14.0.1).* Three cases: a second RetroArch entry with another N64 core
(`parallel_n64`) used through Launch With; a game moved from RetroArch to DuckStation (old rows frozen, new progress
never captured); PCSX2 used through Launch With on a game assigned to RetroArch (memory card never backed up). Plus the
restore side (a backup made by another emulator is written by the game's emulator into its own folder) and three asks:
check the emulator actually used at close, route a restore by the row's `EmulatorFileName`, list every configured
emulator's saves. The examples are deduced from the measures above, not replayed with the stock integrations.

## 6. The sandbox left on G:\LB1326

- Plugins `Local\Plugins\Nixx-SaveProbeMain`, `…Secondary`, `…CatchAll`; emulators `Emulators\Nixx-SaveProbe*`
  (entries in `Emulators.xml`, ids `5a7e0000-…-a001/a002`); games "SaveProbe Test" (`…b001`, on Main) and "SaveProbe
  CatchAll Test" (`…b002`, on FakeEmu), files in `G:\LB\FakeCatalog\`.
- The vault was emptied and the `<GameSave>` rows removed before the tests; the test rows and copies are what is there
  now. Atlantis's two versions and its live Saturn files were put back.
- Back to the state before: `tools\save-probe\deploy-probe.ps1 -Remove`, delete the three emulator entries, copy
  `Backups\lbip-saveprobe-2026-10-07\Data` and `\Saves` back over `Data` and `Saves` (LaunchBox closed).
