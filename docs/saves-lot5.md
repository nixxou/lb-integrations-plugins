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
