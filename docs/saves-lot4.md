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

### 3.2 T8 (11/10): Zelda on SUPER ZSNES, a RetroArch backup restored through the desk

- The 08/10 backup (`-01.srm`) went back into `Emulators\RetroArch\saves\Snes9x\` by the desk's own RetroArch steps
  (hash checked); SUPER ZSNES's `.srm` untouched; the SNES XML's 18 rows of Zelda unchanged (no card added, no id changed).
- **Found and fixed (59c3a6e)**: the RetroArch rows came out of the desk with by-name ids, not their GUID cards. The view
  of the game is made by SUPER ZSNES's copy of the shared code and read by ares's (the copy holding the hooks): `is
  Forward` never recognised it. `SaveGameView.Real` now knows a view by its type's name; the probe has the ares dll make
  one and reads it (it failed without the fix).
- **Measured, LaunchBox's**: opening Game Saves, LaunchBox lists the game several times; one of them hands NOT the game
  its data holds but a working copy of it whose `Saves` is empty (Zelda: "handed 0 persisted row(s), the data's 18
  (another object)"). The cards were looked up on the object handed: none found, RetroArch's rows got by-name ids (no
  harm seen - LaunchBox matches its ordinary saves by path, the XML stayed the same). Now the lookup reads the game of
  LaunchBox's data (`GetGameById`): the 4 listings of Zelda gave each row its GUID card. Plan A was never at risk: it
  already writes on the data's game (`SavePersist.Ensure`, GetGameById). A guard on Plan A, made first on the wrong
  reading ("LaunchBox empties Game.Saves"), was taken out; no cache (decided 11/10).
- A diagnostic line on one-game listings: `cards: game handed N persisted row(s), the data's N (the same object |
  another object); N path(s) ...; per row: ...`.

## 4. Codex review of lot 4 (10/10)

One finding, fixed (a5f789b): a row method that failed to hook (a LaunchBox update, a patch refused) was logged and
ignored, `Verify` tests one method only, so the migration gave the cards our ids anyway - the unhooked method then saw
our JSON undecoded (PCSX2 decides by its `pcsx2:` prefix). Now each integration keeps the list of its row methods not
hooked (a missing method, a refused patch, a SDK body that failed for another, hooks lost to another Harmony):
**incomplete** = its desk off and its cards keep or get back their own ids for the session (`StockIdMigration` takes a
filter per integration), the others go on; logged `NOT every row method hooked (�)`. No retry within a session (the
next start tries again). Probe: a failure injected on PCSX2's `IsSaveActive` ? PCSX2 incomplete, RetroArch complete; the
plan with one integration left out and back for that one only. In LaunchBox: all three complete, as before.

Second review, two findings, fixed (79ac7eb):
- **A persisted card taken across games**: the listing's lookup was keyed by path and version only - in the start-up scan
  (hundreds of games at once) games sharing one file (a PCSX2 memory card: one `pcsx2:<serial>` card per game on one
  `.ps2`) overwrote each other, and a row with its own id was given another game's. Now keyed by game too, and a persisted
  card is taken ONLY by a row listed with no id (`StockDesk.PickCard`; two candidates: none).
- **The self-check tested one method**: another Harmony replacing only `IsSaveActive` would have passed. The sentinel now
  goes through all 8 methods whose hook decodes an id (AddSaveFile, RemoveSave, IsSaveActive, IsSaveContainer,
  TryBackupSave, UseSaveGroupIdForPersistedMatch, TryComputeSaveSignature, TryGetSaveGroupInfo); each hook notes it and
  returns at once (the integration's code never runs on it; its file does not exist). At the first listing and again
  before each one-game listing (Game Saves), so a later patch is caught before that game's cards are used. Probe: one hook
  made "lost" on RetroArch's IsSaveActive ? incomplete, its desk off. In LaunchBox: all three "hooks in", scan 1.52 s.

Third review, two findings, fixed:
- **Complete without proof**: an integration counted as complete when nothing had FAILED - one never hooked (its type
  renamed by a LaunchBox update, its dll not loaded yet, loaded after the first listing) was "complete", and the migration,
  which names `pcsx2:`/`saturn-` owners without any hook, gave its cards our ids. Now complete = hooked by this format of
  the pack AND the self-check passed (an `ok` mark per integration, set by `Verify`, cleared when hooks are lost). `Verify`
  checks an integration not yet checked at every listing (one loaded late is caught at the next one), and `EnsureIds`
  runs a whole-library pass again whenever the set of integrations in our form changes (one checked late, one lost, the
  switch turned), logged `ids: whole library, our form for: ...`.
- **A forwarded answer re-encoded**: Harmony runs the postfixes after a prefix that skipped the original, so a restore
  forwarded to another emulator (or the group id of another emulator's row, Plan A's echo) went through the receiving
  integration's encoding - a native `pcsx2:` row given back by an incomplete PCSX2 came out as `lb.retroarch`'s. The
  prefixes of `AddSaveFile` and `TryGetSaveGroupInfo` now mark a forwarded call (and the self-check) and their postfixes
  leave the answer as given.
- Probe: RetroArch hooked but not yet checked ? not complete; an emu never hooked ? not complete; the migration with the
  live filter leaves the incomplete PCSX2's `pcsx2:` card native and puts RetroArch's Saturn card in our form; a restore by
  the stand-in owner giving back a native `pcsx2:` row and a native `pcsx2:` row's group on RetroArch both stay `pcsx2:`.

Independent review (10/10, an agent of ours - Codex out of credits), whole branch; each claim checked in the code:
- **Rows with no owner in their id handled by the wrong integration** (c966526): a stock desk lists other integrations'
  rows through their twin, which gives them our ids only when complete AND `stockIds` on - otherwise an empty id or a
  GUID, and `OwnerForStock` had no answer for those: Dolphin's own code restored, removed and backed up RetroArch's rows
  on a GameCube game. Now such a row goes to the plugin that LISTED its file through a desk (`SaveRouter.ListedOwner`,
  the same table as companions), and another emulator's row no owner claims is not offered.
- **The cards of a game seen through a view read as none** (c966526): `SaveGameView` hands LaunchBox's integrations a
  DispatchProxy with the SDK's members only - no `Saves` (checked in the SDK dll) - so `PersistedCards` and the per-game
  pass found no card of the game itself. `PersistedRows.Holders` reads the real object.
- **Two saves of one name in two folders, one card** (9b32c34): RetroArch's cores keep their states in folders of their
  own (sort_savestates_enable), and a by-name id was the file name and slot only. It now holds `dir`, the folder
  relative to LaunchBox's root, lower case; a card persisted before keeps its id (a listed row takes its card).
- **A restore through a desk into the wrong install** (24ee183, lot 3): the owners took the game's emulator - not
  theirs then - else the library's first. The desk now picks the install (`RestoreEmulator`, refused when two and
  nothing says which) and tells the owner while its AddSaveFile runs (AppDomain data per thread; `ViaEmulator` in the
  11 plugins with a desk).
- Small: a RetroArch/Dolphin restore through a desk with `stockRestore` off is refused (their own AddSaveFile writes
  elsewhere and answers success); LaunchBox's integrations listed again when one of `System\Plugins` loads (it would
  never have been checked); a second copy of an integration's dll makes it incomplete; the migration's lock is the
  process's; `SavePersist.TakeOut` puts back every row it took out whatever fails; the first copy of the platform XML
  (`nixx-saves-backup\first`) is kept for good - the pass runs again at each start while LaunchBox has not written it.
- Checked and dropped by the review: row copies (every settable field), `#` in ids, RestoreSet's rollback, LiteBox
  reading our JSON (it falls back on the path).

Second independent review (10/10), on those fixes above all; each claim checked in the code:
- **A restore did not follow the "listed by" routing**: a restore's row is the BACKUP, in LaunchBox's vault (`Saves\`),
  whose path was never listed - Dolphin's code still restored RetroArch's backups. A vault row with no owner in its id now
  goes to whoever listed one of its card's live files (`SavePersist.LivePathsOf`; vault rows only, the live ones are asked
  about hundreds of times).
- **`dir` split a card made before it** at its first restore: `StockIds.Result` re-encoded a by-name id with the folder.
  A by-name id naming the very file restored (name, slot, folder when it holds one) now comes back as it came.
- **`first` was made from today's XML** on an install that had run the branch: the oldest dated copy becomes `first`;
  every copy is made in a temporary folder and renamed (never half kept); once per PROCESS (it was once per plugin copy).
- Small: the "listed by" table emptied past 200000 entries only, and said; `TakeOut` puts back the row being moved too
  when it fails; the restore's install hint is put back as it was, not cleared.
- Left as it is: a file two emulators both list (a `.srm` beside the rom) can send an integration's own row with no id
  to the other one - telling them apart needs a table of what each integration listed itself; rare with nothing kept
  beside the games.

Codex review, 11/10, two findings, left as they are (Mehdi):
- **The uninstall does not give the cards their own ids back** (our JSON once LaunchBox has written it: RetroArch's
  `saturn-` and PCSX2's `pcsx2:` matching break without the pack) - that is lot 7's, as planned; the Compatibility
  page's "the setup gives them back" holds once lot 7 is in.
- **A RetroArch Saturn restore is not all or nothing** (`StockRestore` copies the `.bcr`, then RetroArch's own
  `CopySaturnCompanionFiles`: a companion that fails leaves the `.bcr` replaced) - those are LaunchBox's own steps and
  its own behaviour; Dolphin's likewise.

Codex review, 11/10 evening: the Saturn finding again (left, as above), and one fixed:
- **A RetroArch save restored through a desk onto ANOTHER card's file, without a word**: `StockRestore` asked
  `SlotRestore.MayOverwrite` for states only. An "In Vault" card (its live file deleted, the rom renamed) restored onto
  the `.srm` a newer card lives on: LaunchBox answers "overwrite" and copies nothing of that file (lot 3's bug) - the
  newer card's save gone. Now the saves branch asks too, when the target exists and is none of the restored card's live
  files (`StockRestore.OtherCard`, the card's ids in both forms): the usual "Replace it anyway?" in LaunchBox, a refusal
  in BigBox, nothing under LiteBox; never for the card's own file nor for the same content. Natively (a game set to
  RetroArch) LaunchBox has the same hole - this covers the desk's restores only. Probe: 4 checks.
