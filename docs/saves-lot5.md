# Saves, lot 5: the desk on LaunchBox's integrations without save support

Plan: `docs\saves-lb14-routing.md` §4b, row 5 and decision D6. Lot 4 (`docs\saves-lot4.md`) put a desk on LaunchBox's
RetroArch, Dolphin and PCSX2; this lot does the same for the seven of its integrations that have no save support at all,
so that a PS1 game set to DuckStation lists, backs up and restores the saves another emulator (RetroArch, ares) keeps
for it.

## 1. Lot 5.0: what they are (measured 12/10, G:\LB1326, LaunchBox 14.0.1)

- **The seven**: `BigPEmuPlugin`, `CemuPlugin`, `DuckStationPlugin`, `MamePlugin`, `Rpcs3Plugin`, `ScummVmPlugin`,
  `XemuPlugin`, in `System\Plugins\<X> LaunchBox Integration\Unbroken.LaunchBox.Windows.<X>.dll`; emus by the router's
  rule: `lb.bigpemu`, `lb.cemu`, `lb.duckstation`, `lb.mame`, `lb.rpcs3`, `lb.scummvm`, `lb.xemu`.
- **None overrides any of the 13 save methods** (read in each dll's metadata, then checked on the loaded types by the
  probe): they run the SDK's bodies - already hooked once by lot 4 (StockDesk), filtered on the instance's type.
- **The SDK's bodies** (decompiled, `Core\Unbroken.LaunchBox.Plugins.dll`), checked on each instance by the probe:

  | Method | Answer |
  |---|---|
  | `SupportsSaveManagement` | false |
  | `GetSaves` | a failure: "This feature is not supported by this emulator plugin" |
  | `AddSaveFile` | a failure: "This feature is currently not supported by this emulator plugin!" |
  | `RemoveSave` | **deletes the row's file** (`File.Delete`), success |
  | `IsSaveActive` | true |
  | `TryBackupSave`, `TryComputeSaveSignature`, `TryGetSaveGroupInfo` | false |
  | `IsSaveContainer`, `UseSaveGroupIdForPersistedMatch`, `IsSecondarySaveFile` | false |
  | `GetCompanionSaveFiles`, `GetPotentialSaveSlots` | empty |

- `SupportsSaveManagement()` takes no platform: D6 ("supports saves as soon as a save-capable plugin covers the platform")
  is decided per integration, over the platforms of all the emulators it drives.
- Sandbox: no DuckStation emulator entry; one PS1 game (Final Fantasy VII, on RetroArch); PS1 covered by RetroArch and
  Nixx-ares; MAME 0.288 for Arcade, which no save-capable plugin covers.

Probe: `--stock-saves <root> --scratch <dir>`, section 9.

## 2. Step 1: hooked, and D6 (12/10)

- `StockDesk.Integrations` gets the seven; each is classed when hooked: **no save support** = none of the SDK's 13 save
  methods its own (`StockDesk.NoSave`, process-wide). `SupportsSaveManagement` is hooked on the SDK's body for those only
  (lot 4's three keep their own answer, never hooked).
- **D6** (`SaveRouter.OthersCoverPlatformsOf`): true when a plugin WITH saves (ours, or RetroArch/Dolphin/PCSX2) drives a
  library emulator sharing a platform with one of the integration's emulators; those without saves never count (and are
  never asked: their answer is this rule). Kept a minute, cleared with `ForgetStock`. Only with `stockDesk` and the new
  `stockNoSaves` on, the integration Complete (hooked, self-check passed), under LaunchBox.
- Not for them: the migration of ids (they have no cards of their own).
- Compatibility page: `stockNoSaves` (Missing, Needs `stockDesk`), check `nosave.saves`.
- `GetSaves` still the SDK's failure at this step: deployed so, it shows what LaunchBox does with an integration that says
  it supports saves and lists none (5.0, in LaunchBox).
- Probe `--stock-saves` section 9: the seven hooked as without saves, DuckStation's self-check; D6 on the real
  DuckStation (a stand-in DuckStation emulator on PS1): no plugin with saves on PS1 - false; RetroArch on PS1 - true; the
  switch off - false.

## 3. Lot 5.0 in LaunchBox (12/10, G:\LB1326, step 1 deployed)

DuckStation installed from LaunchBox's list (`Emulators\DuckStation\duckstation-qt-x64-ReleaseLTCG.exe`, Sony Playstation),
Final Fantasy VII set to it (its saves: RetroArch's Beetle PSX HW `.srm` and `.1.mcr`, persisted when it was on RetroArch).

- **LaunchBox asks `SupportsSaveManagement` at its start** (01:13:38, during the start-up scan) - D6 answered true (PS1
  covered by Nixx-ares). Then it lists DuckStation in the scan, and asks its `GetSaves` for the ONE game when FF7's Game Saves
  opens. In the first session (DuckStation added in it) nothing was asked: the answer is taken once, at the start - an
  emulator added since gets its saves at the next start.
- `GetSaves` still the SDK's failure at this step: LaunchBox shows the game's PERSISTED rows anyway (both RetroArch rows,
  0 backups, the yellow mark: "no backup"). Nothing against the design: step 2 answers that listing with the other
  emulators' saves.

## 4. Step 2: the listing and the rows (12/10, 153923f, e151d9e)

- **`GetSaves`** of an integration without saves (`StockDesk.Desk`: `stockDesk` and `stockNoSaves` on, Complete, under
  LaunchBox): the SDK's failure replaced by a success, then the other emulators' rows added as on lot 4's three
  (`OthersFor`, only the rows it can route, Plan A on a one-game listing). A success even when empty.
- **The row methods**: every row is another emulator's - `Foreign()` routes it to its owner as on lot 4 (by id, then by who
  listed the file, then a vault row by its card's live paths). A row nobody claims gets the SDK's answer, except
  **`RemoveSave`**: the SDK's body deletes the file, so the user is asked (Mehdi, 12/10) - "Nobody claims this save: <path>
  ... Delete the file anyway?", **No** by default (`SlotRestore.MayDeleteUnclaimed`, same window as the restore warning).
  BigBox, LiteBox and a question that cannot be shown keep the file. Not tied to `stockNoSaves`: with it off LaunchBox
  still shows the persisted rows (T3) - only `stockDesk`, the integration without saves and Complete.
- Probe `--stock-saves` section 9: DuckStation's `GetSaves` a success (the SDK's failure with the switch off); an ares row
  on DuckStation answered by ares; a row nobody claims: asked, No keeps the file, `stockNoSaves` off still asks, BigBox
  refuses without a question, Yes lets the SDK delete it.
- In passing: `SlotRestore.WithSlot` kept every field of an id but its `dir` (lot 4) - kept now. No effect before: only
  the pack's plugins call it, and their ids carry no `dir`.

## 5. Real tests (12/10, G:\LB1326, Data saved in `Backups\lbip-lot5-2026-10-12\`)

Final Fantasy VII on DuckStation; its saves: RetroArch's (Beetle PSX HW `.srm`, `.1.mcr`, Disc 2 `.srm`; SwanStation
`.srm`) and ares's (`.card`, state slot 1).

- **The game's versions get in the way at the close.** FF7 has three Additional Versions on RetroArch (Disc 1/2/3), the
  Disc 1 one on the game's own path. At the end of the game LaunchBox asked **RetroArch** about "the game + the Disc 1
  version" (its `GetSaves`: 1 game, 1 app) - not DuckStation, although DuckStation ran (its `playtime.dat`). The backups
  went through RetroArch's desk: its Beetle rows and ares's, labelled with the version's core
  ("ares.exe (mednafen_psx_hw_libretro)"), and the SwanStation `.srm` none (RetroArch lists the version core's saves).
  LaunchBox's own choice, not the desk's - its core is obfuscated, measured only. For the tests the versions were taken
  out (`Sony Playstation.xml` saved first in `ff7-versions\`), their 10 rows hung on the game, then put back as they were
  (the 2 backups made in between hung on the Disc 1 version, like their live saves).
- **T1** (versions out): Game Saves lists 5 rows through DuckStation (RetroArch's and ares's). At the close only
  DuckStation is asked: the Beetle `.srm` (altered for the test) backed up (`-01`, "content signature/timestamp
  changed"), the SwanStation `.srm` too (`-02`, its first). The Beetle `.srm` restored from its first backup: "handed to
  the desk's own lb.retroarch steps", written to `Emulators\RetroArch\saves\Beetle PSX HW\`, hash back to the original.
  The Disc 2 `.srm` none: with its version out, its row hung on the game, whose path is Disc 1 - nobody lists that file.
- **T2**: MAME at the start: "its emulators' platforms [Arcade] covered by no plugin with saves" - false, never asked
  about saves.
- **T3**: `stockNoSaves` off, restart: DuckStation and MAME "false - a switch is off", DuckStation's `GetSaves` never
  asked. FF7's Game Saves still shows its PERSISTED rows (as in 5.0) - their row methods still routed by `stockDesk`.
  A PS1 game without persisted rows has no page.
- **T4**: Zelda (lot 4) as before - RetroArch's desk lists 6 rows, 18 persisted, ares and SUPER ZSNES rows to their owners.
- Start-up full scan: 1.36 s.
- Not measured: whether LaunchBox offers Delete on a row of an integration that answered false (the question is there
  either way).
