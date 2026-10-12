# Saves: two installs of the same plugin

Decided with Mehdi on 12/10, after Codex's review of the branch (finding 2: "reject installation selection when a card
has live files in multiple installs"). Measured the same day on G:\LB1326. Nothing coded yet.

## 1. How LaunchBox files a listed save (measured 08/10, `saves-lb14-routing.md` tests 10-12)

A plugin lists a live file F with an id X (`GetSaves`):

1. LaunchBox looks for a persisted row at F's **path**. Found: F is in that row's card, and the row **keeps its old id** -
   the plugin's id does not count.
2. No row at that path: a new row with id X. If another row of the game has id X, it joins **that card** (its backup
   count grows by one). Otherwise a new card.
3. A row found by neither takes the id of a same-game backup with the same file name (Secret of Mana, 08-09/10) - the
   desk avoids it by persisting its own rows at their path (plan A, `SavePersist.Ensure`).

The card is the id; a file already known is found by its path. Adding something to the ids of NEW cards needs no
migration: known files keep their id.

## 2. The problem, measured (12/10)

The pack's ids hold the file name only (`{"emu":"ares",...,"by":"name","id":"Zelda.ram"}`), so two installs of ares give
the same id to two different files. Set up: `Emulators\ares-second` (a copy of `Nixx-ares`, its `settings.bml` paths
pointed to its own folder, an entry "ares-second" on SNES only), a Zelda `.ram` in it differing by one byte. Zelda on
RetroArch, its ares card: one live row (`Nixx-ares`), 2 backups.

- At the start-up scan the second ares listed its file with exactly the same id; the desk persisted a row at its path.
- Game Saves: ONE ares card "Save (ram)", showing `Nixx-ares`'s file, **3 Backups** (2 before). The second install's
  save has no card of its own.
- The XML: 4 rows with the same id - `Nixx-ares`'s live file, the 2 vault copies, and
  `G:\LB1326\Emulators\ares-second\Saves\Super Famicom\...ram`, **the second install's live file, counted as a backup**.

What follows (read, not tried): restoring that "backup" copies the second install's save over the first one's, with no
question (same card); restoring a real backup goes to the install of the first live row found (`PickInstall`); the
second install's changes are never backed up apart.

## 3. Options

| | For | Against |
|---|---|---|
| A. Refuse at restore when ambiguous (Codex) | one place, no id change | the cards stay mixed (histories, signatures flipping between two files): the symptom only |
| B. A hint of the install in the id of new cards: a CRC32 of the emulator's folder | two installs → two ids → two cards, one live file each; no migration (section 1); computed without the XML, the same for the same install; a backup carries where it came from | a renamed/moved emulator folder changes the hash, hence a new card (today: same id, same card) → needs an adoption rule |
| C. Attach by the live row, a plain number to make the id unique (`"n":2`) | no hash | needs the XML to decide, depends on which install is listed first; says nothing at restore without a live row; attaching by path is what LaunchBox already does - what is missing is a distinct id |

## 4. Choices

1. **B.** The id of every NEW card of the pack's plugins gets `"inst"`: CRC32 of the emulator's exe folder (full path,
   lower-case, normalised; relative to the LaunchBox root when inside it, absolute otherwise). Not the save folder (it
   follows the config: changing `Paths/Saves` would stale every hash; saves and states differ; it does not identify an
   install). Not the exe name (two exes of one folder share the saves: `pcsx2-qt.exe` / `-avx2`; an update renaming the
   exe would stale the hint). **A hint only**: never corrected, never used as a destination (Mehdi: a hash cannot be
   tempted into operational use). Existing cards are not touched.
2. **Adoption at listing**, only for a file found by no path: one live row of ours, same game, emulator, kind and name,
   whose file **no longer exists** → the listed file takes its id (a renamed/moved folder, ares moving files out of the
   game folder: the card kept as today). Several such rows → none adopted, a new card.
3. **Restore: the install**, when several of the owner's library emulators tie on the score (`RestoreEmulator`), grouped
   by destination (`OwnExpectedSaveDir/StateDir`, section 5 - two entries writing the same place are one):
   a. the one holding the card's live row (its destination is that row's folder);
   b. else the backup's `inst`;
   c. else a window: "This save can go to several installs of ares - where?", a combobox of the full destination
      folders (the game's emulator preselected when it is one of them), OK / Cancel. BigBox: refused with a message.
   With one install, none of this runs.
4. **The two holes**, without which 3 is ignored: each plugin's restore takes the chosen install FIRST (today: the
   game's own emulator first, `AresSaves.EmulatorOf` and the 10 like it); and a row of the plugin's own (the game on
   ares, an ares row) goes through the choice too (today `SaveDesk.Route` hands it straight to the plugin's code).
5. **Dropped**: the number (C), elimination ("B is the nightly's, so A is the stable's"), "the card of the same name
   whatever its hash".

## 5. The expected folder of each plugin

`IOwnSaves` gets `OwnExpectedSaveDir(emulator, game/version)` and `OwnExpectedStateDir(...)`: where a restore of that
game WOULD write for THAT install - read only (no folder created, no session captured, no migration). Answers a folder,
a pack the plugin writes and applies at the next launch (Cxbx, xemu, Vita3K, DSiWare), or unknown with a reason (default
answer: unknown). The restore uses the same function: what the window shows is what is written. Sources, in order: what
the pack forces (command line, keys it writes, the SUPER ZSNES BepInEx mod, "nothing beside the games"), the emulator's
config key, the emulator's default.

Found while reading the 12 plugins (each its own fix):

- SUPER ZSNES: before the emulator's first exit the settings file lacks the mod's `{exec}/saves`, `{exec}/states`:
  `FoldersFor` answers "beside the ROM" while the mod already redirects.
- melonDS: a state restore's last fallback is `DefaultSaveDir` (the saves folder).
- RASnes9x: a `.7z` is unpacked to `%TEMP%`; with `Dir:SRAM` empty Snes9x writes there, the code looks beside the `.7z`.
- Flycast: listing searches every folder of `Dreamcast.VMUPath`/`SavePath`/`SavestatePath`, restore writes to the first;
  Flycast and PPSSPP save calls do not `ResolveFullPath` the exe (launch does).
- Relative paths in ares (`Paths/Saves`), bsnes (`Path/Saves`), melonDS (`SaveFilePath`) are resolved against LaunchBox's
  working directory: to measure what each emulator does before changing.
- bsnes writes beside the game by default and nothing moves it ("nothing beside the games", lot 9).

## 6. Order

1. **Done 12/10.** Codex finding 1 (separate): the restore of an old-name backup after a ROM rename skipped the overwrite
   warning. `SlotRestore.OverAnotherFile`: the target exists and is none of the restored card's known live files →
   "another card's", the Yes / No asks. In ares, RASnes9x, Flycast, SUPER ZSNES, melonDS, PPSSPP states, and bsnes, which
   had no warning at all (it now asks for a manual import too, as the others). A card with no live file known keeps the
   caller's rule. Probe `--saves-selftest`: 5 checks (old name onto the new name's save, onto its own, no live file,
   nothing at the target, through MayOverwrite with No).
2. **Done 12/10** (8c0ca6c, b5a24fa, feb8862, ec96980, 183ff8d, 8dccc81, 5a8a1d2, 5bbffb0). The expected-folder
   functions, in the 12 plugins, each the computation its restore already uses (extracted where it was inline):
   ares `Destination`, bsnes / RASnes9x / SUPER ZSNES `TargetDirFor`, melonDS `RestoreDirIn`, Flycast `TargetDirIn`,
   PPSSPP the memstick's SAVEDATA / PPSSPP_STATE, no$gba BATTERY, packs for Cxbx, xemu (save, or its state's own slot),
   Vita3K, DSiWare, Xenia's profile folder (`ResolveRestoreTargetIn`). Measured first (12/10): ares, bsnes, melonDS
   and Flycast hand a configured path to Windows as it is (their sources), and LaunchBox starts an emulator with its
   exe's folder as current directory (ares, read off the running process) - `ExpectedDir.OfConfig`: relative from the
   exe's folder. Fixed on the way: those relative paths (read from LaunchBox's current directory before); SUPER ZSNES's
   empty folders filled by the mod from the first session; a melonDS state's last resort in savestates\, not saves\;
   Flycast's "data/" and data\ one folder; the exe resolved in Flycast's and PPSSPP's save calls. Not changed:
   RASnes9x with an EMPTY Dir:SRAM and a .7z (Snes9x writes into the %TEMP% unpack folder - to decide); Flycast listing
   every configured folder but restoring into the first; Xenia's target, the most recently written profile rather than
   the signed-in one (to decide). Probe `Probe <plugin.dll> --expected` (bsnes, melonDS, SUPER ZSNES, RASnes9x, Flycast,
   PPSSPP), `--ares-expected`, `--saves-selftest` (OfConfig). Seen, older than this step: `--melonds` 2 failures (DSi
   mode, another game's override), `--vita3k` 8.
3. **Done 12/10** (27b6a49, b1ac27f). The mechanism of section 4 behind the switch `twoInstalls` (compatibility
   page): `InstallIds` at listing, `SaveRouter.PickInstall` + `InstallChoice` at restore, `SaveForward.WithVia` for the
   install chosen, own rows through the same choice. Probe `--saves-selftest` `TwoInstalls` (24 cases). Found by the real
   test and fixed (b1ac27f): the second install's card was not "Active" - `SaveRouter.EmulatorFor` kept the FIRST of two
   installs with the same exe name, which judged the file outside its save folder. At a tie: the install that listed
   this file, else the one whose folder holds it, else the card's hint; and a plugin's own rows get their row's install
   (`OwnInstallPath`) for IsSaveActive / the signature / the backup when it drives several library emulators.
   Real test (Zelda ALttP France, Nixx-ares + `ares-second`, both on SNES):
   - listing: two cards, the second's live row persisted with `"inst":"a9bb7230"`, both "Active" after the fix;
   - closing a session started with Launch With > ares-second: `-04.ram` = its live file, on its own card;
   - its live file damaged (64 bytes to `AA`), Restore of `-04` on its card: back into
     `Emulators\ares-second\Saves\Super Famicom\` (hash equal to `-04`), the pre-restore backup `-05` on its card,
     Nixx-ares's file untouched, no window (the card's live row decided).
   Left from before the mechanism: the Nixx-ares card holds `-02.ram`, which is ares-second's content (backed up while
   both files shared one card) - so the same close also made `-03.ram` of Nixx-ares's unchanged file (its last backup
   differed). Not cleaned: the history is the user's. Not exercised in LaunchBox: the window (probe only).

## 7. Sandbox

`Emulators\ares-second` and its entry (id `58d2c0ef-21a6-41c2-98f6-6da1e0c151e1`) kept for the real test. Back: delete
the folder, put back `Data\Emulators.xml` and `Data\Platforms\Super Nintendo Entertainment System.xml` from
`Backups\lbip-twoares-2026-10-12\` (the SNES XML holds the second install's row since 12/10 20:17).
