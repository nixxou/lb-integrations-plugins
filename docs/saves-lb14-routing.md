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
out. `emu` the plugin (`xbox` for the id xemu and Cxbx share), `kind` (`save`, `state`, `console`, `nand`, `vmu`,
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
| xemu + Cxbx | `cxbx:4d530064` / `xemustate:4d530064:1` | `{"emu":"xbox","kind":"save","by":"title","id":"4D530064"}` / `{"emu":"xemu","kind":"state","by":"title","id":"4D530064","slot":1}` (slot from the snapshot's NAME) |
| Vita3K | `vita3k:title:PCSE00000` | `{"emu":"vita3k","kind":"save","by":"title","id":"PCSE00000"}` |

Not ours to change: LaunchBox's (`pcsx2:…`, `saturn-…`, RetroArch GUIDs) and LiteBox's (`entry:…`, `unit:…`). No
migration (no user base), but existing rows keep their id: start from a cleaned XML. Data that changes (files' roles,
fingerprints, versions) goes in a manifest inside a `.<plugin>save` archive, never in the id.

Still open: the slot numbering (keep the emulator's), `sys` everywhere or only where needed, extension in `id` or apart.

## 4. Defects found in our plugins (read, not reproduced)

- no$gba DSiWare: `IsOurs` misses `nogba:dsiware:` → path match instead of id, `IsSaveActive` always true, `RemoveSave`
  returns before the DSiWare branch (the `.dsisave` comes back from the NAND at the next listing).
- Xemu, Cxbx, Vita3K: stable ids but no `UseSaveGroupIdForPersistedMatch` → matched by path, fragile if the folder moves.
- Name-based ids can contain `#` ("Shin Megami Tensei #FE"), which LiteBox cuts at.
- xemu: the LaunchBox slot follows the order snapshots appear in, not their name - a snapshot gone can shift the others'
  ids.
- SUPER ZSNES: the restore folder is derived from the name inside the id (an old backup restores into the old name's
  folder).
- SUPER ZSNES writes `<game>.data.szsnes\` beside the game (against "nothing beside the game").

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
