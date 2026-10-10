# Saves, lot 4: the front desk and our ids on LaunchBox's RetroArch, Dolphin and PCSX2

State on 10/10/2026, branch `savegroupid` (nothing pushed). Plan: `docs\saves-lb14-routing.md` §4b, lot 4. Decisions
(Mehdi, 10/10): Harmony on LaunchBox's own integrations (not the `nixx-retroarch` clone), all three in this lot, and
everything at once - the desk AND their native ids translated into our JSON (`native` keeps the original), the cards
already persisted migrated. The way back at uninstall is lot 7.

## 1. Lot 4.0: measured outside LaunchBox (probe `--stock-saves`)

`Probe --stock-saves <LaunchBox root> --scratch <empty folder>`: the real dlls of `<root>\System\Plugins` loaded,
LaunchBox's `Core` resolved for them, RetroArch called on a copy of its folder (settings, saves, states; no cores).

### 1.1 Who overrides what (G:\LB1326, LaunchBox 14.0.1)

Types: `Unbroken.LaunchBox.Windows.RetroArch.RetroArchPlugin`, `…Dolphin.DolphinPlugin`, `…Pcsx2.Pcsx2Plugin`, in
`System\Plugins\<RetroArch|Dolphin|PCSX2> LaunchBox Integration\`. `EmulatorName` "RetroArch", "Dolphin", "PCSX2": the
router's emus are `lb.retroarch`, `lb.dolphin`, `lb.pcsx2`.

| Method | RetroArch | Dolphin | PCSX2 |
|---|---|---|---|
| SupportsSaveManagement, GetSaves, AddSaveFile, UseSaveGroupIdForPersistedMatch, GetPotentialSaveSlots | own | own | own |
| RemoveSave | SDK | own | own |
| IsSaveActive, IsSaveContainer, TryBackupSave | SDK | SDK | own |
| TryComputeSaveSignature | own | SDK | own |
| TryGetSaveGroupInfo | own | SDK | SDK |
| IsSecondarySaveFile, GetCompanionSaveFiles | own | SDK | SDK |

"SDK" = `EmulatorPlugin`'s own body: a patch there runs for every plugin and must filter on the instance's type.

### 1.2 How they answer, row by row (rows made as LaunchBox persists them)

- **RetroArch `UseSaveGroupIdForPersistedMatch` is true ONLY for a `saturn-…` id** - false for an empty id, a GUID, our
  JSON. RetroArch matches cards by id for its Saturn sets only.
- RetroArch `TryGetSaveGroupInfo` and `TryComputeSaveSignature` work from the FILE, whatever id comes in: Atlantis's
  `.bcr` gives `saturn-atlantis - the lost tales (fr) (disc 1)` and a set signature; Zelda's `.srm` and state: false.
- **PCSX2 decides by the id**: `UseSaveGroupIdForPersistedMatch` and `IsSaveContainer` true only for `pcsx2:…`; its
  `IsSaveActive` and `TryComputeSaveSignature` need an install (none in the sandbox: they throw on our stub).
- The SDK's `IsSaveActive` answers true.
- So a JSON id reaching them unchanged would turn Saturn and PCSX2 matching off: every inbound row must be decoded to
  its native id before the original runs.

### 1.3 Harmony

A prefix that replaces the row argument (`ref GameSaveBase __0`, a `MemberwiseClone` with another `SaveGroupId`) reaches
the original: a later prefix and the original see the copy, the caller's row is untouched.

### 1.4 Not measurable outside LaunchBox

Their `GetSaves` reach LaunchBox's core (RetroArch: `Unbroken.LaunchBox.Tokens.TokenLocator`; Dolphin: an obfuscated
resolver), whose static set-up only LaunchBox does - both throw in the probe. The listed rows, Dolphin's native ids,
which row LaunchBox hands the per-row methods when listed and persisted ids differ, and the scan's cost are measured in
LaunchBox with the logging-only build (step 5 of the plan).

Harness notes: `IGame.GetEffectiveCommandLine()` is a method, the probe's `StubGame` now answers a method given as
`"Name()"`; RetroArch reads its `-L "cores\<core>.dll"` from it.

### 1.5 The hooks in the probe (StockDesk, watch only)

- Typed hooks, one pair per method: a hook taking `object[] __args` LOSES the out values (measured: RetroArch's
  `TryGetSaveGroupInfo` then gave back an empty id). With typed hooks every answer is the original's (RetroArch, PCSX2).
- An SDK body is patched where it is declared (`EmulatorPlugin`), once for the three; Harmony refuses it seen from the
  derived type. Each integration is marked hooked BEFORE its methods are patched: a failure never leads to a second
  prefix (measured once: two prefixes on `GetSaves` after a failure). 21 methods, 1 prefix each, `Install` twice.
- In the probe, Harmony cannot hook Dolphin's GetSaves/AddSaveFile/RemoveSave and PCSX2's AddSaveFile/RemoveSave/
  TryComputeSaveSignature: `The type initializer for '<Module>' threw` - their module initializer needs LaunchBox, as
  their GetSaves do. To be seen in LaunchBox.

## 2. Lot 4.0: measured in LaunchBox (G:\LB1326, 10/10, the watch-only build)

### 2.1 Another Harmony on the same methods drops ours

First sessions: hooked (all 21 methods, Dolphin's and PCSX2's too - their module initializer runs in LaunchBox), never
called. The sandbox's save probe (`Local\Plugins\Nixx-SaveProbe*`) patches the same save methods with its OWN copy of
Harmony, 0.5 s after ours: its replacement (`GetSaves_Patch2` in a stack) holds its hooks only. Two copies of Harmony do
not share their patches (each plugin of the pack sees 0 prefixes where the copy that hooked sees 1). The save probe is set
aside for lot 4 (`Backups\lbip-lot4-saveprobe-off\`, to put back after) - its observer logs what StockDesk logs now.
A self-check stays (`StockDesk.Verify`, once per process at the first listing): a sentinel row handed to each
integration must come through our hook, else the log says `HOOKS LOST`.

### 2.2 What LaunchBox hands them (save probe off)

- Start-up scan: RetroArch's `GetSaves` on 604 games, 11 rows, 157 ms; Dolphin 1 game, 0 rows (LaunchBox itself
  cannot read the disc id of the `.rvz`). Scan 1.78 s.
- Atlantis: RetroArch lists `saturn-atlantis - the lost tales (fr) (disc 1)`, lineage empty, twice (on the game and on
  its version `e8cbe532`). Zelda: RetroArch lists its `.srm` and two states (slots 0 and 5) with an EMPTY id.
- **The per-row methods get the PERSISTED rows** (their XML ids): Zelda's cards are LaunchBox's GUIDs (`710dbdc7` the
  `.srm`, `dcb7e565` state 0, `504c41ff` state 5) - matched by path (RetroArch's persisted match is off but for
  Saturn), the GUID kept. Atlantis's cards still carry the 07/10 probe format.
- **RetroArch is asked about every persisted row of its game, other emulators' included**: Zelda's ares `.ram` and
  `.bs1`, SUPER ZSNES's `.srm` - it answers `IsSaveActive` true for them. The routing of lot 4 is exactly that.
- RetroArch's `TryGetSaveGroupInfo` on a vault copy derives another id from the file name (`saturn-�-01`).
- So the plan's rule holds: once migrated, a card holds `{�,"native":"<GUID>"}` and the listing gives that id back
  (listed = persisted), never a by-name id that would stand beside it.

## 3. Real tests (G:\LB1326, 10/10, steps 6-8 deployed, save probe set aside)

- **Migration at start-up**: 18 persisted rows of 6 games given our ids at the first listing; the platform XML copied to
  `Local\Plugins\.data\nixx-saves-backup\2026-10-10_14-37-51` first; the save probe's cards left as they are (their
  owner is not certain with the probe set aside). Start-up scan 1.37 s (1.78 s watch-only).
- **A change made by reflection does NOT reach the XML at a normal close** (measured: no platform file changed after a
  start and a close). LaunchBox writes a game when it changes it itself: the migration lives in memory, redone (idempotent)
  at each start, and a game's cards reach the XML in our form when LaunchBox saves that game (below).
- **T1 passed (done-when)**: Zelda set to RetroArch. Game Saves listed, through RetroArch's desk, SUPER ZSNES's save and
  ares's saves beside RetroArch's. The ares card's backup `-01.ram` restored: "restore of a ares save handed to ares",
  ares's own restore wrote `Emulators\Nixx-ares\Saves\Super Famicom\...ram` (A964A373..., the backup's content);
  RetroArch's `.srm` untouched (E40D8C35...). Live files before the test: `Backups\lbip-lot4-2026-10-10\Zelda-*-before-T1`.
- **T2 (listing)**: Atlantis listed once per holder as `{"emu":"lb.retroarch","kind":"save","native":"saturn-atlantis
  - the lost tales (fr) (disc 1)"}`. After the session the XML of Zelda and Atlantis holds our ids (Zelda's GUID cards
  `{..."native":"710dbdc7..."}`, states `"kind":"state"`); rows and cards counted before/after: Zelda 18/6 = 18/6,
  Atlantis 4/1 = 4/1 - nothing doubled, nothing split. Atlantis's restore not tried yet.
- Left: T2's restore, T3 (Rolan's Curse, Pokemon's two cards), T4 (`stockIds` off then on), T5 (Zelda back on SUPER
  ZSNES), T6 (Dolphin: needs a disc LaunchBox can read), T7 (PCSX2: none installed), the save probe put back.

### 3.1 T2 and T3 (10/10, evening)

- **T2's restore, first try: "LaunchBox failed to back up the save item!"** - LaunchBox's own pre-backup, after its
  `[SaveBackup] Creating new backup` line and before any call of ours (every call before it got RetroArch's own id and
  answered as usual). The LaunchBox 14 defect measured on 20/09 on this very game with no plugin of ours loaded: a save
  persisted on a VERSION running the game's own file (`Play (FR) (Disc 1)`, `e8cbe532`). That version and its save row
  removed from `Sega Saturn.xml` (copy: `Backups\lbip-lot4-2026-10-10\Sega Saturn.xml.before-version-removal`).
- **T2 passed** then: the restore of the vault's `.bcr` (A1C32B71...) - LaunchBox's pre-backup made `-02` (the old live
  set, with `.bkr`/`.smpc`), RetroArch's own AddSaveFile handed `saturn-atlantis�`, the row it gave back given our id
  (`restore -> � {"emu":"lb.retroarch",�,"native":"saturn-�"}`), live `.bcr` = A1C32B71, companions unchanged. The XML:
  4 rows, ONE card, in our form. Live set before: `Backups\lbip-lot4-2026-10-10\Atlantis-live-before-T2\`.
- RetroArch names each vault copy's group after its file (`saturn-�-01`, `-02`): given our form as is - LaunchBox
  groups them as it did before.
- **T3**: Rolan's Curse 2 cards, Pokemon 1 card, as before; their XML still holds the GUIDs - LaunchBox wrote nothing for
  them (the migration lives in memory until LaunchBox saves the game).
- **T4 passed** - the switch from the Compatibility page (its first use in LaunchBox; the confirmation box opened behind
  the window, found by Mehdi): `stockIds=off` written to `nixx-compat.ini`. Next start: "12 persisted row(s) given
  their own ids back (3 game(s))", Atlantis listed and named as `saturn-atlantis�`, its XML back to that id on all 4 rows
  (one card) - the way back exact. Switched on again: "10 persisted row(s) given our ids (4 game(s))" - the rows whose XML
  held native ids then (Game Boy 6, Saturn 4; Zelda's 8 kept our form in the XML all along, LaunchBox wrote nothing for
  it while off - read back into their own form in memory, as designed).
- Seen: the Compatibility page's confirmation box can open behind LaunchBox's window (to look at in Menus).
- **T5 passed** (lot 3 not broken): Zelda set back to SUPER ZSNES (XML copy `�before-T5`). RetroArch's card listed by
  SUPER ZSNES's desk in our form (backups `-01` = the live file, Active; `.srm` of 04/10). The `.srm` restored: "restore
  of a lb.retroarch save handed to the desk's own lb.retroarch steps" (StockRestore, our id decoded), RetroArch's live
  `.srm` = 7D00A454 (the backup), SUPER ZSNES's untouched (E40D8C35). Live files before: `Zelda-*-before-T5`.

## 4. Codex review of lot 4 (10/10)

One finding, fixed (a5f789b): a row method that failed to hook (a LaunchBox update, a patch refused) was logged and
ignored, `Verify` tests one method only, so the migration gave the cards our ids anyway - the unhooked method then saw
our JSON undecoded (PCSX2 decides by its `pcsx2:` prefix). Now each integration keeps the list of its row methods not
hooked (a missing method, a refused patch, a SDK body that failed for another, hooks lost to another Harmony):
**incomplete** = its desk off and its cards keep or get back their own ids for the session (`StockIdMigration` takes a
filter per integration), the others go on; logged `NOT every row method hooked (�)`. No retry within a session (the
next start tries again). Probe: a failure injected on PCSX2's `IsSaveActive` ? PCSX2 incomplete, RetroArch complete; the
plan with one integration left out and back for that one only. In LaunchBox: all three complete, as before.
