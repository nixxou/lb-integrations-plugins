# Saves, lot 9: nothing beside the game

Mehdi, 07/10: *"rien ne doit être sauvegardé à côté du jeu, mais dans des répertoires dédiés (par plateforme si possible)
dans le répertoire de l'émulateur"* - and what is already beside a game is moved there at its launch, never over a file
already in the new place. Lot 9 of `saves-lb14-routing.md` §4b. Built 13/10 (branch `savegroupid`), from a read-only audit
of the 13 plugins (four agents, one per family).

## 1. The rule for files already beside a game

The same in every plugin:

- **The emulator's own extensions** - nobody else writes them - are moved at **every launch** (and before a restore):
  ares `.bs1`-`.bs9/.bsu/.blu` (+ `.png`), bsnes `.bsz` (+ `.bsz.png`), melonDS `.ml1`-`.ml8` (+ `.png`) and `.mch`,
  Snes9x `.000`-`.009` (+ `.png`), `.oops`, `.smv`, SUPER ZSNES's `<name>.data.szsnes\` folder.
- **The generic ones** - `.srm`, `.rtc`, `.psr`, `.sav`, `.cht`, written under the same name by bsnes, Snes9x, SUPER ZSNES,
  RetroArch and other DS emulators, plus ares' save and console files (`.ram`, `.eeprom`, `.card`, `.wav`...) - are
  **copied** (never moved: RetroArch saving in its content's folder reads `<game>.srm` there too) **when last written
  before the moment the plugin set that folder** on an
  emulator that had been running with the setting present and empty (`LbipBesideGame`, `<install>\lbip-beside-game.ini`,
  the latest moment kept; noted BEFORE the setting is changed - if the note cannot be written the setting is left empty
  and retried at the next launch): until then the
  emulator read that file as the game's own, whoever wrote it. One written after is somebody else's, and stays (logged).
  A setting found empty on a fresh install (no settings file yet: the emulator never ran) notes nothing.
  *First version (13/10, morning) moved them at the one launch where the setting was found empty - so for ONE game per
  install; every other game's save vanished from the game and from Game Saves (review 13/10). Replaced the same day.*
- Never over a file already in the target (that one is kept, the old one left where it is, logged). Never the ROM itself
  (no extension a ROM of that system can have). A restore never moves the file it reads from (LiteBox's Replace from
  another group's live save can point beside the game).
- A folder the USER set is never changed; an EMPTY setting is the emulator's "beside the game" default, the very thing
  the rule forbids, so it is set - on every install the pack drives, not only on one it made. A folder the PACK set that
  no longer points into the install (install moved, portable LaunchBox on another drive letter: absolute, named as the
  pack names it, gone) is set again to this install, without noting a moment.
- The moment is a file date: a file copied from a FAT drive, restored by a tool that keeps old dates, or touched across
  a daylight-saving change by a tool writing local times may be judged on the wrong side. Wrong side = left beside the
  game (logged), never lost.
- **A copy is made once** (`<install>\lbip-beside-game-copied.txt`: size, date, path): the emulator's copy deleted to
  start over, or a switch to an empty named branch, never brings the old file beside the game back.
- **An empty setting the plugin could not note stays empty, and nothing is taken out of where the emulator writes then**:
  RASnes9x reads a present-and-empty `Dir:*` as "beside the game" (no move); ares never moves a file from `Discs\` or
  `Unpacked\` INTO the game's folder; SUPER ZSNES's `.data.szsnes\` moves only once SUPER ZSNES itself wrote its states
  folder into its settings (the mod may be deployed and still fail to load).
- A stale pack path is told by its shape: `<a folder named as the install>\<Saves|States...>`, gone. A user's own
  `N:\Sync\Saves` on a drive not mounted at the launch is kept.

## 2. What the audit found, and what was done

| Plugin | Before 13/10 | Done in lot 9 |
|---|---|---|
| ares | `Paths/*` forced and saves migrated since 07/10 (2a5dbb8). **A hidden `.cue` written BESIDE an .iso/.bin/.img** (`CueFor`); a restore before any launch could go beside the game | The `.cue` goes to `<ares>\Discs\<hash>\` (the hash of the game's path, the name once only for MAX_PATH; a `.m3u`'s discs share ONE folder, since ares's Change Disc dialog opens in the folder of the disc it loaded - two discs of one name get a folder each) beside a **link to the image under its own name**: a hard link when on the install's volume (it IS the file - a symbolic link's size reads 0 to a C runtime that does not follow it), with a `.source` file noting its image, else a symbolic link (needs Windows' developer mode or the right) - ares follows no absolute path in a `.cue`'s FILE (measured 06/10). A link is deleted without ever touching its attributes (on a hard link they are the image's): a read-only one through Windows' ignore-read-only disposition. `AresSavePaths.Ensure` runs before the disc is prepared. `SweepDiscs` (at every launch) removes the links whose image is gone or replaced (a hard link would keep a deleted ISO's bytes on the disk). The plugin's old hidden `.cue` beside the game (hidden, and exactly our one-track text) is removed once the new one is made; a user's own `.cue` is kept and used. Without any link possible: beside the game as before, with a warning in the log. The restore calls `Ensure` and `Migrate` first |
| bsnes | **No folder forced** (`Path/Saves`, `States`, `Cheats`, `Screenshots` empty = beside the game); its own restore and thumbnails wrote beside the game | `BsnesSavePaths`: the empty nodes set to `<bsnes>/Saves/`, `/States/`, `/Cheats/`, `/Screenshots/` (absolute, trailing slash: bsnes uses the node as a prefix), at install, launch and restore; migration by §1 |
| RASnes9x | `Dir:SRAM` / `Dir:Savestates` forced when empty (12/10); migration only at that launch | Also `Dir:Screenshots`, `Dir:Movies`, `Dir:SPCs`, `Dir:Cheats` (Snes9x's own defaults `.\Screenshots`...); migration by §1 (the states, `.oops`, `.smv` at every launch; `.srm`/`.rtc`/`.cht` at the launch the folder was empty) |
| SUPER ZSNES | Folders filled by the in-process mod (01/10, "nothing is moved"); **`<name>.data.szsnes\` left beside two games** | `MigrateStates` at launch: the game's `.data.szsnes\` beside it moved file by file into the states folder, the empty folder removed. Not when the states folder is the game's own (the mod off) or flat. **The `.srm` beside the game is not moved** (its owner cannot be told - §1, SUPER ZSNES's folder was set by the mod, never "found empty" by the C# side) |
| melonDS | Folders set at an install of ours only; **`CheatFilePath` never set; the second window's keys never set**; state thumbnails and a restore went beside the ROM when a key was empty | `MelonDsSavePaths`: every empty `SaveFilePath` / `SavestatePath` / `CheatFilePath` of `[Instance0]` and of every other `[InstanceN]` melonDS wrote, set to `saves\`, `savestates\`, `cheats\` (absolute) at every launch and before a restore; migration by §1, never for DSiWare (its save is in the NAND) |
| xemu | All in the install; **screenshots (F12) in xemu's working directory** (`general.screenshot_dir` empty) | `general.screenshot_dir` set to `<xemu>\Screenshots` in the user's `xemu.toml` when empty, at install and every session |
| no$gba, PPSSPP, Cxbx, Xenia (canary), Flycast, Demul, Vita3K | Nothing beside a game, nothing found there | Nothing |

## 3. Left as they are (to decide or to measure)

- **Flycast's screenshots go to Windows' Pictures library** (no key moves them; F12 is bound by the plugin). Not beside a
  game, but not in the emulator's folder. Options: stop binding F12, or move `Flycast-*.png` after the session (Flycast has
  no end-of-session hook in the plugin today).
- **Xenia master** (`xenia.exe`, claimed too): without `portable.txt` its storage is `Documents\Xenia`. The pack installs
  canary; a master someone points the plugin at keeps its own choice.
- **Cxbx `DataStorageToggle`**: set to the exe's folder at install, not re-checked at launch (a user who sets %APPDATA% moves
  the saves there - his choice).
- **SUPER ZSNES's `.srm` and `.cht` beside games** from 01/10 (`G:\LB\FakeCatalog`): left, owner unknown.
- **To do at the merge of the SUPER ZSNES rewrite (Mehdi, 13/10)**: a `.data.szsnes\` state left beside the ROM must not
  stay there when a file of the same name is already in `states\` (measured 13/10, Secret of Mana: `.szst-last` in both,
  the old one left beside the ROM by the never-overwrite rule). Move it into a folder of its own in the install
  (a `last`/conflict folder under `states\`), never beside the game. Not done now: the plugin is being rewritten for the
  emulator's update.
- **ares installs whose `Paths/*` were set before 13/10** (2a5dbb8, 07/10) have no moment noted: their generic leftovers
  beside games (Mehdi's FF7 `.card`) stay where they are. Copy them by hand if they are ares's.
- **RASnes9x installs whose `Dir:SRAM` was set on 12/10** have no moment either: the other games' `.srm`/`.rtc` beside
  their ROMs stay (Snes9x no longer reads them there). Same remedy, by hand.
- **xemu's `screenshot_dir`** is absolute: an install moved keeps pointing to the old place (F12 fails silently, no data).
- **A `Discs\` link that cannot be deleted** (the old one held open by an ares still running) makes that launch write the
  hidden `.cue` beside the game as before; the next launch puts it right.
- Optional hardening not done: Demul `[files] nvram` refilled when empty (it is absolute and right today); Vita3K refusing a
  `--use-vhdx` folder inside the game's folder (an explicit choice on the line only).

- **Copying or backing up the LaunchBox folder** copies `Emulators\Nixx-ares\Discs\` too: a hard link is a full second copy
  for Explorer or robocopy without `/SL`, a symbolic link is followed. Exclude `Discs\` from such copies (it is rebuilt at
  each launch); deleting a game frees its space at ares's next launch (`SweepDiscs`).
- A `.m3u` mixing ISOs and `.cue`/`.chd` discs: Change Disc opens in `Discs\`, where only the ISOs' `.cue`s are.
- `SweepDiscs` deletes only what it can prove is ours: a symbolic link, or a hard link with its `.source` whose image is
  gone or no longer the same file (volume serial + file index). A plain file in `Discs\` (a save ares wrote there with
  `Paths/Saves` emptied by hand) is never touched.

## 4. Measured / to measure in a real run

Probe (13/10, all green): `--ares-discs` (the `.cue` in Discs\, symbolic and hard link, the old hidden `.cue` removed, the
user's kept, a `.m3u` in one folder, an image replaced / deleted swept), `--lot9-bsnes`, `--lot9-melonds` (nodes set, the
user's kept, migration by §1 at the first and a later launch), `--rasnes9x-conf` (the new keys, a later launch), and the
existing `--expected` of every plugin, `--ares-expected`, `--saves-selftest`. Copy semantics (13/10, after review): the
generic file stays beside the game as well, a restore's source is never moved, a stale pack path is set again, a plain
file in `Discs\` survives the sweep, a copy is never made twice, a user's folder on an absent drive is kept. Second
review (13/10, after the copy rule): nothing that deletes or overwrites; its six medium points fixed the same day (above).

To see in LaunchBox (Mehdi):
1. ares, a Mega CD `.iso` (Sonic CD): boots through the `.cue` in `Emulators\Nixx-ares\Discs\` - a HARD link when the ISO is on
   the install's volume, a symbolic one otherwise (both to try: one ISO on G:, one on another drive); the old hidden `.cue`
   beside the ISO gone; its saves still found (same name).
2. ares, a multi-disc `.m3u` of ISOs: System > Change Disc shows the other discs.
3. bsnes: a launch sets the four folders, a save and a state land in `Emulators\<bsnes>\Saves` / `States`; a SECOND game's
   `.srm` beside its ROM (written before that first launch) is copied at its own launch, and stays beside the ROM too.
4. melonDS: a state saved lands in `savestates\`, its thumbnail beside it; `CheatFilePath` set.
5. xemu: F12 lands in `Emulators\Nixx-Xemu\Screenshots`.
6. SUPER ZSNES: Zelda's `.data.szsnes\` beside the ROM moved into `states\` at its launch.
