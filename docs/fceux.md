# FCEUX: research notes for the Nixx-FCEUX plugin

Stage 1 (research), 18/10/2026; stage 2 build A (the first lot of code), 18/10/2026. Everything below was either
MEASURED on the real executables in the lab or READ in FCEUX's own source at the released tag; each fact says which.
Nothing here comes from a wiki. The next stage starts from this file: read section 13 (what is built, what is open)
first, then section 11 (the plan) and section 12 (open questions), then come back to the facts.

Lab: `G:\LB1326\lbip-fceux-lab\` (not %TEMP%: a cleaner wipes old files there).

| Path in the lab | What |
|---|---|
| `dl\` | the four downloaded zips, untouched |
| `x-win64\`, `x-win32\`, `x-win64-QtSDL\`, `x-interim64\` | pristine extractions |
| `run64\`, `run32\`, `runqt\` | copies the oracles ran in (their `fceux.cfg`, `sav\`, `fcs\`, `snaps\` were written by FCEUX) |
| `src-2.7.0\` | `git clone --depth 1 --branch fceux-2.7.0` of TASEmulators/fceux, commit `8cdbeeb248f5a13aa97a58b4a0437ae13fb6aea8` |
| `roms\` | a COPY of `G:\LB\FakeCatalog\nes\Zelda II - The Adventure of Link (Europe).nes` (SHA-256 `53e12c61...ce536`), `arc\` (the same in a .zip and a .7z), `uni\` (renamed copies, "Zelda é.nes" and a Japanese name), `Fake Disk (Japan).fds` (a synthetic 1-side FDS image, NOT a game) |
| `saves test\` | folders the dir-override oracles wrote into |
| `tools\` | the oracle scripts (below), `ahk.exe` (a copy of LaunchBox's `ThirdParty\AutoHotkey\AutoHotkey.exe`, v1.1.24.03) |
| `fceux.cfg.firstrun` | the config FCEUX 2.7.0 wrote on its very first run (no config before) |

Oracle scripts (`tools\`): `fceux-oracle.ps1` (start the exe with an argument line, list its windows WITH the text of a
dialog's children, optional screenshot, then WM_CLOSE and wait), `fceux-keys.ps1` (same, plus `cmd:<id>` = a WM_COMMAND
posted to the main window, i.e. a menu item), `fcx_set.py` (set keys of fceux.cfg), `fcx_hotkeys.py` (decode the hotkey
table of fceux.cfg), `fcx_bind.py` (write hotkey bindings into it), `fcx_state.py` (decode a savestate and its embedded
screen to PNG). Only processes the scripts started were ever closed or killed.

---

## 1. Builds, sources, versions

### Releases (GitHub API `repos/TASEmulators/fceux/releases`, read 18/10/2026)

| Tag | Published | Windows assets |
|---|---|---|
| `fceux-2.7.0` (latest, not prerelease) | 2026-10-14 | `fceux-2.7.0-win64.zip`, `fceux-2.7.0-win32.zip`, `fceux-2.7.0-win64-QtSDL.zip` |
| `v2.6.6` | 2023-08-27 | same three names with 2.6.6 |
| `v2.6.5` | 2023-02-08 | same |
| `fceux-2.6.4` | 2022-03-24 | win32 + `qfceux-2.6.4-win64.zip` |
| `interim-build` (prerelease, rolling) | assets refreshed at each master build | `fceux-win64.zip`, `fceux-win32.zip`, `fceux-win64-QtSDL.zip` (no version in the name) |

THE TAG SCHEME IS NOT STABLE: `fceux-2.7.0`, `v2.6.6`, `fceux-2.6.4`. The version must be read from the tag with a
`\d+\.\d+\.\d+` match, and the asset name built as `fceux-<version>-win64.zip`. The 2.7.0 release note says its tag "was
bumped once due to a major bug found shortly after initial release" - the assets of a release can be replaced.

The 2.7.0 assets carry `digest` fields in the API (`sha256:...`); the 2.6.x ones do not (null).

Downloaded 18/10/2026, SHA-256 checked against the API digests (all equal):

| File | Size | SHA-256 |
|---|---|---|
| `fceux-2.7.0-win64.zip` | 4 842 676 | `f851d608fc21bc7d289d25ef92cb29ca6c96a21c3df28f82aab2a8328df1f9ff` |
| `fceux-2.7.0-win32.zip` | 4 026 805 | `a8bb8bbc1cbd8b20f3433cc90b653d5809eb44f6e8c4d59812f2430cae193f34` |
| `fceux-2.7.0-win64-QtSDL.zip` | 96 884 928 | `7fcb3065dc9dcff20f972d3f427c7dee529223b28500d9de831cd445731b6560` |
| interim `fceux-win64.zip` | 4 842 766 | `7f5f3f95550c4e885e151aa486e4fca46ef452b96c7ed7ff821aee78c5cd94be` |

URLs: `https://github.com/TASEmulators/fceux/releases/download/fceux-2.7.0/<asset>`; interim:
`https://github.com/TASEmulators/fceux/releases/download/interim-build/<asset>`.

Without the API (60 requests/hour): `HEAD https://github.com/TASEmulators/fceux/releases/latest` answers `302` with
`Location: https://github.com/TASEmulators/fceux/releases/tag/fceux-2.7.0` (measured 18/10 with curl); the asset URL
then answers `302` to `release-assets.githubusercontent.com`.

Interim builds: `appveyor.yml` (at the tag) deploys every master build to the `interim-build` release (prerelease,
"Latest auto builds off master branch - commit: ...") and tags to a normal release. On 18/10 the interim release was the
SAME commit as 2.7.0 (`8cdbeeb...`), but its `fceux64.exe` is a different build (5 620 736 bytes, MD5
`617c9f268807a1cb660e2b86a6a5ed18`) from the release's (5 620 224 bytes, MD5 `1c5bba0dacf6c322c85a791a990baea8`).
The plugin should install the numbered release (a known version), not the interim.

### The three Windows builds

| Asset | Exe | Front end | Config | Size |
|---|---|---|---|---|
| win64 | `fceux64.exe` (zip is FLAT, no top folder, 99 entries) | native Win32 (`src\drivers\win`), DirectDraw + DirectInput | `fceux.cfg` beside the exe, key/value text | 4.8 MB |
| win32 | `fceux.exe` (flat, 99 entries) | the same code, 32-bit | the same `fceux.cfg` format (measured: first-run files identical but for `RAMInitSeed`) | 4.0 MB |
| win64-QtSDL | `bin\qfceux.exe` (zip has `bin\`, `doc\`, `luaScripts\`, `palettes\`, `tools\`) | Qt 6.9.3 + SDL 2.32.8 (`src\drivers\Qt`) | `bin\fceux.cfg` in ANOTHER format (`SDL.Key = value`, CRLF, measured) | 97 MB |

The win64 zip holds: `fceux64.exe`, `lua5.1.dll`, `lua51.dll`, `auxlib.lua`, `7z_64.dll` (archives), `fceux.chm`,
`taseditor.chm`, `palettes\*.pal`, `luaScripts\`, `tools\taseditor_patterns.txt`. No `fceux.cfg` (created at first run).

LaunchBox's own metadata row "FCEUX" (`Emulators` table of `LaunchBox.Metadata.db`, read in a copy 18/10) has
`BinaryFileName fceux.exe` (the classic 32-bit build), extensions `.7z; .fds; .gz; .nes; .nsf; .rar; .unf; .zip`, one
platform row `Nintendo Entertainment System`, no Famicom Disk System row.

### Version detection

- NO version resource in any of the three exes (`VersionInfo.FileVersion` empty, measured with PowerShell).
- The exe carries the ASCII string `FCEUX 2.7.0` (5 occurrences in `fceux64.exe`, measured with grep) - it is
  `FCEU_NAME_AND_VERSION` (`src\version.h`: `FCEU_VERSION_STRING "2.7.0"`). The window title is
  `FCEUX 2.7.0: <rom name>` (measured). An install that is not ours can be described by scanning the exe for
  `FCEUX \d+\.\d+\.\d+`, never recognised as ours by it.
- Ours: the stamp beside the exe (`lbip-fceux-build.txt`, Bsnes pattern) holding the tag's version.
- `qfceux.exe --help` prints the version and the git revision (`git Rev: 8cdbeeb...`), measured; the classic build has no
  `--help`.

### Which build the plugin integrates

**The Qt / SDL win64 build, `bin\qfceux.exe` (Mehdi's choice, 18/10)** - see §14. Its full screen is a borderless
window (no display mode change, unlike the classic build's exclusive DirectDraw), its renderer is OpenGL (ReShade), its
hotkeys are text. The plugin claims `qfceux.exe` only; the classic `fceux64.exe` / `fceux.exe` (LaunchBox's own row) are
no longer claimed. §3-§6 below describe the classic build as first researched; §14 is what the plugin does now.

---

## 2. Systems, and the LaunchBox platforms

### What FCEUX 2.7.0 runs (read in the source at the tag)

`src\fceu.cpp` `FCEUI_LoadGameVirtual`: the file is tried, IN THIS ORDER, by content (magic), never by extension:
`iNESLoad` (`"NES\x1A"`, iNES and NES 2.0, `ines.cpp:828`), `NSFLoad` (`"NESM\x1a"`, `nsf.cpp:180`), `UNIFLoad`
(UNIF), `FDSLoad` (fwNES `"FDS\x1a"` header or a raw disk starting with `*NINTENDO-HVC*`, `fds.cpp:796`). No system
switch exists or is needed: the system is the file's content.

| System | Files | Notes (source) |
|---|---|---|
| NES / Famicom cartridge | `.nes` (iNES, NES 2.0), `.unf`/`.unif` (UNIF) | Windows open dialog filter: `*.nes;*.nsf;*.fds;*.unf;*.zip;*.rar;*.7z;*.gz` (`drivers\win\window.cpp:1182`) |
| Famicom Disk System | `.fds` | needs the BIOS `disksys.rom` (section 7) |
| NSF music | `.nsf` (not NSFe) | a player, no saves |
| Vs. UniSystem | `.nes` files | `vsuni.cpp`: detected from the iNES/NES 2.0 header or its game table; hotkeys for coins and DIP switches exist (`EMUCMD_VSUNI_*`) but have NO default key |
| PlayChoice-10 | - | NOT emulated: only a header field of the iNES header editor (`drivers\win\header_editor.cpp`) |
| Regions | NTSC / PAL / Dendy | `ines.cpp:1114-1124`: NES 2.0 header's TV field, else the FILE NAME: `(E)`, `(Europe)`, `(PAL)`, `(F)`, `(G)`, `(I)` -> PAL. Dendy is never auto-detected (`fceu.cpp:554`). Forced with `-pal 1` / `-dendy 1` (section 4) |

In archives only `nes`, `fds`, `nsf` entries are considered (`fceu.cpp:439`, `romextensions`): a `.unf` inside a zip is
not found.

### LaunchBox platforms (names checked in a copy of `G:\LB1326\Metadata\LaunchBox.Metadata.db`, 18/10)

| LaunchBox platform | System | Extensions on the row | BIOS | Notes |
|---|---|---|---|---|
| `Nintendo Entertainment System` (default, first) | NES cartridge; `.fds` -> FDS by extension | `.nes; .unf; .unif; .nsf; .fds; .zip; .7z; .rar; .gz` | `disksys.rom` only for a `.fds` | LaunchBox's own row also lists `.fds` |
| `Nintendo Famicom Disk System` | FDS | `.fds; .zip; .7z; .rar; .gz` | `disksys.rom` REQUIRED | not on LaunchBox's FCEUX row: ours adds it |
| `Nes - Super Mario Bros. Hacks` (LiteBox's name, brief section 2) | NES cartridge | as NES | - | claimed by name |

Not claimed:
- Vs. UniSystem: LaunchBox has no Vs. platform; Vs. games live under `Arcade` (MAME's sets). FCEUX runs Vs. games
  only from iNES `.nes` files, which a user files under the NES platform - they work there.
- PlayChoice-10: not emulated.
- NSF music: no LaunchBox platform; `.nsf` stays in the NES row's extensions as on LaunchBox's own FCEUX row.
- `Nintendo Famicom`: not a LaunchBox platform (absent from `Platforms`, 18/10); the name appears only in LiteBox's
  RomM map (`LbApiHost\Host\Romm\RommPlatformMap.cs:30`). Open question 1.

---

## 3. Configuration: `fceux.cfg`

- WHERE: beside the exe, always (`drivers\win\main.cpp:315` `GetBaseDirectory` = `GetModuleFileName` folder;
  `main.cpp:719` loads `<base>\fceux.cfg`). Portable; nothing under AppData (measured: only the exe folder changed).
- FORMAT (`drivers\common\config.cpp`): one `key value\n` per line, the keys of a `std::map` written in BYTE ORDER (so
  the quoted keys `"fs"`, `"odnonvol"`... come first, `!version 1` before them), separator one space, the value to the
  end of the line (spaces kept, leading whitespace skipped). `#` starts a comment. Integers of 1/2/4 bytes as decimal,
  every other blob as `base64:` (standard alphabet, `=` padding: measured, all 22 blobs round-trip with Python's
  `base64`). Strings raw; an empty string = nothing after the space (`"odnonvol" `).
- ENCODING / ENDINGS: LF only (`fopen(...,"wb")`; measured: 412 LF, 0 CRLF), no BOM, bytes in the ANSI code page (the
  build is not Unicode: section 4).
- REWRITTEN WHOLE AT EXIT (`main.cpp:589-594` `DriverKill` -> `SaveConfig`), always, even when nothing changed
  (measured: the file's time changes at every close). Keys FCEUX does not know are KEPT (measured: a `zzzUnknownKey 42`
  line survived a session; the map is loaded then updated). So: write before the launch, never while it runs, and
  whatever FCEUX holds in memory at exit wins.
- Command-line options that change a config variable are SAVED at exit too (measured: `-clipsides 1 -no8lim 1` turned
  `"eoptions" 1261569` into `1261833` in the file). A per-game switch leaks into every other game unless the plugin puts
  the value back (section 11).
- `-cfg <name>` loads another config AFTER fceux.cfg, as `<base>\<name>` (relative to the exe folder only), and the exit
  save then goes to THAT file (`main.cpp:737-744`, `cfgFile.assign`).

Keys the plugin cares about (first-run values, `fceux.cfg.firstrun`):

| Key | First-run value | Meaning (source: `drivers\win\config.cpp`) |
|---|---|---|
| `"fs"` | 0 | full screen (`NAC("fs",fullscreen)`); applied at start ONLY when a ROM is on the line (`main.cpp:788`) |
| `"vmcx"`, `"vmcy"`, `"vmcb"` | 0, 0, 0 | full-screen mode (custom mode 0): width, height, bpp |
| `"odnonvol"` | empty | battery saves folder (empty = `<base>\sav`) |
| `"odstates"` | empty | savestates folder (empty = `<base>\fcs`) |
| `"odsnaps"` | empty | screenshots (empty = `<base>\snaps`) |
| `"odcheats"` | empty | cheats (empty = `<base>\cheats`) |
| `"odfdsrom"` | empty | folder of `disksys.rom` (empty = `<base>`) |
| `"odbase"` | empty | base directory override (empty = exe folder) |
| `"odmovies"`, `"odlua"`, `"odavi"`, `"odinput"`, `"odmacro"`, `"odmemwatch"`, `"odroms"` | empty | TAS / tools folders, out of scope |
| `"palyo"` | 1 | PAL emulation (`pal_emulation`) |
| `dendy` | 0 | Dendy |
| `"genie"` | 0 | Game Genie (needs `gg.rom` in the base folder, `file.cpp:750`) |
| `"eoptions"` | 1261569 | bit flags (clip sides, sprite limit, ...) |
| `newppu` | 0 | new PPU core |
| `CurrentState` | 0 | the current savestate slot, SAVED across sessions (measured: 1 after a "next slot") |
| `backupSavestates` | 1 | keep `<name>-bak.fc<n>` when a slot is overwritten |
| `compressSavestates` | 1 | zlib-compressed states |
| `bindSavestate` | 1 | a loaded movie's name is glued into the state name (no movie: nothing) |
| `AutoResumePlay` | 0 | `<name>-resume.fcs` written at close / loaded at start when 1 |
| `EnableAutosave` | 0 | periodic `<name>-autosave<n>.fcs` when 1 |
| `SingleInstanceOnly` | 0 | a second instance starts as its own process |
| `ResumeROM`, `recent_files[0..9]` | | the last ROM paths (written at exit) |
| `FCEUD_CommandMapping_V2` | base64 | the hotkeys (section 6) |
| `GamePadConfig_V2` | base64 | pad buttons, same record layout (default P1: F, D, S, Enter, arrows) |

Directory overrides are used AS WRITTEN (`file.cpp:474` `FCEUI_SetDirOverride` stores the string; a relative one would
resolve against the process's current directory): the plugin writes absolute paths. Measured with absolute folders with
spaces (`G:\LB1326\lbip-fceux-lab\saves test\...`): the `.sav`, the states and the screenshots went there, and MISSING
folders were CREATED by FCEUX (sav, states and snaps each measured).

---

## 4. Command line (classic Windows build)

Read in `drivers\win\args.cpp` (`ParseArgies`) and `drivers\common\args.cpp` (`ParseArguments`):

`fceux64.exe [options] <rom>` - every option takes ONE value (`-pal 1`, not `-pal`); the first argument that is not an
option is the ROM. Options: `-pal`, `-dendy`, `-noicon`, `-gg`, `-no8lim`, `-clipsides`, `-nothrottle`, `-playmovie f`,
`-lua f`, `-palette f`, `-loadstate f`, `-readonly`, `-stopframe`, `-framedisplay`, `-inputdisplay`, `-allowUDLR`,
`-stopmovie`, `-shutmovie`, `-bginput`, `-turbo`, `-pause`, `-cfg f`, `-avi f`, `-avicapture`, `-dumpinput f`,
`-playinput f`.

THERE IS NO FULL-SCREEN SWITCH in the classic build (`-nofs` is commented out, `args.cpp`). Full screen is the config's
`"fs"` (section 5).

Measured: `"<path with spaces>.nes"` quoted loads (title `FCEUX 2.7.0: Zelda II - The Adventure of Link (Europe)`);
`-dendy 1`, `-pal 0`, `-clipsides 1 -no8lim 1` accepted. Region switches vs the config: `"palyo"` stayed 1 and `dendy` 0
in the file after `-dendy 1` and `-pal 0` sessions with the "(Europe)" ROM (the name forces PAL at every load); a clean
measurement with a USA-named copy is still TO DO in stage 2 before the Region option is written.

ANSI ONLY (`main(int argc, char *argv[])`): measured, `Zelda é.nes` (representable in the ANSI code page) loads and saves
`Zelda é.sav`; a Japanese file name gives the modal error `Error opening "...\??????.nes"!`. The plugin must not pass a
path that is not representable in the system's ANSI code page (section 11).

The Qt build's `--help` (measured) has `--fullscreen {0|1}`, `--pal`, `--loadstate`, `--savestate`, `--no-config`... -
for the record only.

---

## 5. Full screen

- `"fs" 1` in fceux.cfg + a ROM on the line -> starts in full screen (`main.cpp:788`, `if(!t) fullscreen=0`).
- MEASURED on a FIRST-RUN config: `"fs" 1` gives the modal error "Horizontal resolution is too low." and the window
  stays blank (WM_CLOSE then does nothing, the dialog is modal). Why (`drivers\win\video.cpp`): at start-up
  `SetVideoMode(1)` calls `RecalcCustom()` BEFORE DirectDraw exists; `ResetCustomMode` (which copies the current display
  mode into custom mode 0) returns early while `lpDD7` is null, so `vmcx`/`vmcy` stay 0. Alt+Enter from a windowed
  session works because DirectDraw then exists: measured, one Alt+Enter wrote `"vmcx" 2560`, `"vmcy" 1440`,
  `"vmcb" 32` (the primary display's mode) and `"fs" 1` at exit.
- So the plugin must write `vmcx`/`vmcy`/`vmcb` (the target display's current mode, what `ResetCustomMode` copies) when
  they are 0, before writing `"fs" 1`.
- Full screen is EXCLUSIVE DirectDraw (`DDSCL_EXCLUSIVE | DDSCL_FULLSCREEN` + `SetDisplayMode`, `video.cpp:440-447`).
  MEASURED ON THIS MACHINE (a VM with virtual displays: Parsec / "Virtual Display" adapters): a start with `"fs" 1` and
  the mode set showed a black 1920x1080 area, the process stopped responding, and after it was killed the desktop
  topology had changed (three displays before, one 1920x1080 display after). Full screen is NOT validated here and must
  not be tested again on this machine; Mehdi tests it on his real display. Closing from full screen took 5-6 s
  (measured twice, exit code 0).
- Alt+Enter toggles full screen (`EMUCMD_MISC_TOGGLEFULLSCREEN`, default Alt+Enter).

---

## 6. Hotkeys

Stored in `FCEUD_CommandMapping_V2` (`drivers\win\input.cpp:1839`, `VAC(FCEUD_CommandMapping, 2)`): base64 of an array
of `ButtConfig`, one per `EMUCMD_*` command IN ENUM ORDER (`src\input.h:110-264`, 129 commands; index = enum value).
Each record is 92 bytes (`drivers\win\input.h:44`, `MAXBUTTCONFIG` 4):

```
uint8  ButtType[4]     0 = keyboard (BUTTC_KEYBOARD)
uint8  DeviceNum[4]
int32  ButtonNum[4]    keyboard: DirectInput scan code | CMD_KEY_* modifier bits
uint32 NumC            how many of the 4 are used
GUID   DeviceInstance[4]  (16 bytes each)
```

Modifiers (`drivers\win\input.h:6-15`): LSHIFT `1<<16`, RSHIFT `1<<17` (SHIFT = both, `0x30000`), LCTRL `1<<18`, RCTRL
`1<<19` (CTRL `0xC0000`), LALT `1<<20`, RALT `1<<21` (ALT `0x300000`). Scan codes: Esc `0x01`, F1-F10 `0x3B`-`0x44`,
F11 `0x57`, F12 `0x58`, I `0x17`, P `0x19`, Enter `0x1C`.

Decoded from the first-run file (`fcx_hotkeys.py`) and equal to `drivers\win\mapinput.cpp:41-99`
(`DefaultCommandMapping`):

| Command (enum index) | Default |
|---|---|
| `HIDE_MENU_TOGGLE` (5) | Esc |
| `EXIT` (6) | none |
| `SAVE_SLOT_0..9` (14-23) | 0-9 (select the slot) |
| `SAVE_SLOT_NEXT` / `PREV` (24 / 25) | none |
| `SAVE_STATE` (26) | I (save to the current slot) |
| `SAVE_STATE_SLOT_0..9` (28-37) | Shift+F10, Shift+F1..F9 |
| `LOAD_STATE` (38) | P (load the current slot) |
| `LOAD_STATE_SLOT_0..9` (40-49) | F10, F1..F9 |
| `SCREENSHOT` (4) | F12 |
| `MISC_TOGGLEFULLSCREEN` (100) | Alt+Enter |
| `FDS_EJECT_INSERT` / `FDS_SIDE_SELECT` (65 / 66) | none |
| `VSUNI_COIN` (67) and the DIP switches | none |
| `TASEDITOR_CANCEL_SEEKING` (113) | Esc (TAS editor only) |

So the pack's keys collide with FCEUX's defaults: F2 / F4 / F6 / F7 are "load slot 2 / 4 / 6 / 7", Esc hides the menu.
`FCEUI_HandleEmuCommands` tests every command of the table in turn (`src\input.cpp:1017`, with
`FCEUD_TestCommandState`, `drivers\win\input.cpp:1844`), so two commands on one key both fire;
a command matches only when no extra modifier is held (`DTestButton(...,1,1)`), so Shift+F2 does not fire F2's.

MEASURED write (`fcx_bind.py`, then AutoHotkey): `SAVE_STATE` = {I, F2}, `LOAD_STATE` = {P, F4}, `LOAD_STATE_SLOT_2`
and `_4` emptied, `EXIT` = Esc, `HIDE_MENU_TOGGLE` emptied -> F2 saved `.fc1` (current slot 1), Esc quit FCEUX and the
`.sav` was written; the bindings were still in the file after FCEUX's own exit save.

KEYBOARD INPUT IS DIRECTINPUT (`drivers\win\keyboard.cpp:47`, `GetDeviceState`, foreground non-exclusive). Measured:
SendKeys (PowerShell) and SendInput with scan codes only reached nothing; AutoHotkey 1.1.24's `Send {Shift down}{F2
down}...` (LaunchBox's own AutoHotkey) DID reach FCEUX (Shift+F2 -> `.fc2`, F12 -> screenshot, I -> current slot). The
menu items also work by WM_COMMAND to the main window (`MENU_SAVESTATE` 40344, `MENU_LOADSTATE` 40343,
`MENU_NEXTSAVESTATE` 40347, `ID_FILE_SCREENSHOT` 40272, `drivers\win\resource.h`) - measured, used by the oracles.

---

## 7. BIOS (Famicom Disk System)

- `disksys.rom`, exactly 8 192 bytes (size checked, nothing else: `fds.cpp:869`), read from `<odfdsrom>\disksys.rom`, or
  `<base>\disksys.rom` when `"odfdsrom"` is empty (`file.cpp:751-755`).
- MEASURED missing: a `.fds` start shows the MODAL error `FDS BIOS ROM image missing: G:\LB1326\lbip-fceux-lab\run64\
  disksys.rom` (the exe folder) and WM_CLOSE does not close it. A launch without the BIOS must be refused by the plugin
  (LbipNotice naming the file and the folder).
- Known good dump: SHA-256 `fdc1a76e654feea993fcb38366e05ee5f4eb641f86fe6bebaeefd412e112dd72` (the one ares's table
  carries, `src\Ares\AresSystems.cs`, names `disksys.rom`, `fds_bios.bin`). FCEUX does not check it; the plugin can say
  "unknown dump" without refusing.
- No FDS game and no `disksys.rom` exist on this machine (searched `G:\LB`, the LaunchBox installs, the emulator folders):
  FDS loading and FDS saves are read in the source, not measured.
- `gg.rom` (Game Genie) is read from the base folder, only when Game Genie is on (`file.cpp:750`).

---

## 8. Saves, states, screenshots

All measured with Zelda II unless "source".

| What | Name | Default folder | Notes |
|---|---|---|---|
| Battery save | `<rom name>.sav` | `<exe>\sav\` | 8 192 bytes for Zelda II; written at close (WM_CLOSE, Esc bound to EXIT, the window's X); loaded at start. Only for a battery game (`cart.cpp:545/574`, source). |
| FDS disk save | `<rom name>.fds` | `<exe>\sav\` (the `odnonvol` folder) | source (`fds.cpp:906-1010`): the WHOLE modified disk, 65 500 bytes per side, no header, written at close only if the disk was written; loaded INSTEAD of the original disk at start. A generic extension: never confuse with a game. |
| Savestates | `<rom name>.fc0` .. `.fc9` | `<exe>\fcs\` | one file per slot; `CurrentState` (persisted) is the slot I/P use |
| Overwritten state | `<rom name>-bak.fc<n>` | same | `backupSavestates 1` (default): the previous content of the slot (measured) - not a save of its own |
| Undo of a load | `<rom name>.bak.fc0` | same | `state.cpp:1134` (source) |
| Resume / autosave | `<rom name>-resume.fcs`, `<rom name>-autosave<n>.fcs` | same | only with `AutoResumePlay` / `EnableAutosave` (off by default) |
| Screenshot | `<rom name>-<n>.png` | `<exe>\snaps\` | F12 |
| Cheats | `<rom name>.cht` | `<exe>\cheats\` | source (`file.cpp:741`) |

`<rom name>` is `FileBase`: the file name without its extension; FOR AN ARCHIVE, THE NAME OF THE FILE INSIDE IT, not the
archive's (measured: `Zelda2-zip.zip` and `Zelda2-7z.7z`, both holding the Zelda II ROM, wrote
`Zelda II - The Adventure of Link (Europe).sav` / `.fc1`).

Nothing is written beside the ROM in normal play (measured: the ROM folders held only the ROMs after every oracle). Two
debugger-only exceptions, source: `<rom>.deb` (`pref.cpp:217`, only if the debugger was opened in the session) and the
code/data logger's `.cdl` (only if it was used). Out of scope (debugger), left to the user.

### Savestate format and its picture (thumbnail)

`state.cpp:379-470` + measured with `fcx_state.py`: 16-byte header `FCSX`, uint32 LE uncompressed size, uint32 LE
FCEUX version (20700 for 2.7.0), uint32 LE compressed size (`0xFFFFFFFF` = not compressed); then zlib data. Inside,
chunks: 1 byte type + uint32 LE size + data. Chunk **8** is `XBackBuf`, the PPU output: 256x256 bytes, the first 240
lines the picture, each byte a palette index (values seen: 79, 128-176; the NES colour is `value & 0x3F`). Decoded with
the 64-colour default palette, it is EXACTLY FCEUX's own screenshot of the same moment (compared side by side,
`tools\state-fc2.png` vs `run64\snaps\...-1.png`). The default palette is the table `palette[]` of
`src\palettes\palettes.h` (`P64(x)` = x*4), byte-identical to the shipped `palettes\FCEUX.pal` (192 bytes).

So the thumbnail is the emulator's OWN image (no window capture): `<state>.png` decoded from chunk 8. A user palette
(`-palette`, `"cpalette"`) changes the colours FCEUX shows, not the indices: the default palette is a fair rendering.

### Folders, and "nothing beside the game"

FCEUX's defaults are already inside the install (`sav\`, `fcs\`, `snaps\`, `cheats\`), never beside the game; but they
are global, not per platform. The `od*` keys take absolute folders and FCEUX creates them (measured). See the plan.

### Close

WM_CLOSE to the `FCEUXWindowClass` window: windowed, FCEUX quits in about 0.1 s, exit code 0, `.sav` and `fceux.cfg`
written (measured several times, win64 and win32). From full screen 5-6 s. A modal error dialog (missing BIOS, bad file,
full-screen error) blocks WM_CLOSE: the process stays (measured 3 times; the oracle then killed its own process).
AutoHotkey `WinClose, ahk_exe fceux64.exe` quit it the same way (measured).

---

## 9. Archives

FCEUX opens archives itself through `7z_64.dll` / `7z.dll` (`drivers\win\archive.cpp`: every format the DLL knows,
recognised by signature; plus zlib/minizip/gzip in the core, `file.cpp:317`). Measured: `.zip` and `.7z` load and save
under the inner ROM's name. An archive with several ROM entries opens FCEUX's own chooser (`fceu.cpp:444`, source).
So `AutoExtract = false`; the save's name comes from the archive's entry, which the plugin reads (SharpCompress) to list
saves.

---

## 10. Other behaviour measured

- First run creates `cheats\`, `fcs\`, `movies\`, `sav\`, `snaps\` beside the exe and `fceux.cfg` (83 926 bytes).
- No file in `%APPDATA%` / `%LOCALAPPDATA%` (portable).
- `SingleInstanceOnly 0`: a second launch is a second process (source `main.cpp:796`).
- Window class `FCEUXWindowClass`, title `FCEUX 2.7.0: <rom name>` (windowed; `FCEUX 2.7.0` only in full screen).
- The Qt build (measured once): portable too, `bin\fceux.cfg` (18 372 bytes, CRLF, `SDL.Hotkeys.X = Alt+Return` text
  hotkeys, `SDL.Fullscreen = 0`), same `sav\` / `fcs\` naming, plus `bookmarks\`, `gameinfo\`, `input\`, `avi\`, `wav\`.

---

## 11. Plan of the plugin (mapped onto Bsnes / RaSnes9x / Ares)

`src\Fceux\`, assembly `Fceux`, namespace `LbIntegrations.Fceux`, PackName `Nixx-FCEUX`, a new PluginId GUID,
`emu = "fceux"` in the save ids (never reused).

| File | From | What it does here |
|---|---|---|
| `Fceux.csproj` | `Bsnes.csproj` | AssemblyName `Fceux`, help `docs\help\fceux.html`; SharpCompress already referenced (archive entry names) |
| `manifest.json` | `Bsnes\manifest.json` | `Nixx-FCEUX`, new GUID, HomePageUrl `https://fceux.com/` (and the GitHub project) |
| `FceuxPlugin.cs` | `BsnesPlugin.cs` + Ares's platform handling | the `EmulatorPlugin` (directly), rows from `FceuxSystems`, claim by exe name `fceux64.exe` / `fceux.exe`, IsUpdateAvailable's three answers, CreateEmulator one row per platform, BIOS per platform, no RetroAchievements (FCEUX has none), PrepareCore |
| `FceuxSystems.cs` | `AresSystems.cs` | the one table: systems `NES` (`.nes .unf .unif .nsf`, save `.sav`), `FDS` (`.fds`, save `.fds`, BIOS `disksys.rom` 8192 B, SHA-256 above); platforms `Nintendo Entertainment System` {NES, ByExtension `.fds` -> FDS}, `Nintendo Famicom Disk System` {FDS}, `Nes - Super Mario Bros. Hacks` {NES}; archives `.zip .7z .rar .gz` |
| `FceuxBuild.cs` | `AresBuild.cs` (numbered releases) | API `releases/latest` (asset `fceux-<v>-win64.zip`, check its `digest`), fallback HEAD `/releases/latest` -> tag in `Location`, version = `\d+\.\d+\.\d+` of the tag; cache 10 min; stamp `lbip-fceux-build.txt`; `IsNewer` |
| `Archives.cs` | `Bsnes\Archives.cs` | flat zip extracted OVER the install (no top folder to strip), `fceux.cfg` never in the zip so the user's stays; plus `InnerRomName(archive)` (the single nes/fds/nsf entry) for the saves |
| `FceuxCfg.cs` | `BsnesBml.cs` / `RaSnes9xConf.cs` | editor of fceux.cfg in place: read as bytes (ANSI), keep every line and its order, set a key's value, add a missing key in byte order, LF, no BOM, write only when changed, via temp + File.Replace; `Hotkeys` get/set on `FCEUD_CommandMapping_V2` (92-byte records, enum order) |
| `FceuxLaunch.cs` | `BsnesLaunch.cs` + `RaSnes9xLaunch.cs` | exe names, `Running(exe)`; before launch: `"fs"` from the Fullscreen option and `vmcx/vmcy/vmcb` when 0 (section 5), the folders (below), the pack's hotkeys (below), the game's options; command line: per-game switches before the ROM (`-pal`, `-dendy`, `-gg`, `-no8lim`, `-clipsides`), Windows tokenisation; a ROM path not representable in ANSI -> refused with a notice, or a copy/link under `<exe>\Unpacked\` (open question 4) |
| `FceuxAhk.cs` | `BsnesAhk.cs` | Exit `WinClose, ahk_exe fceux64.exe` (and fceux.exe); SaveState F2, LoadState F4 (measured that AutoHotkey's Send reaches FCEUX); `Superseded` list |
| `FceuxSettings.cs` / `FceuxSettingsPage.cs` | Bsnes's | simple page: install + version, BIOS `disksys.rom` (found / import / missing), Full screen |
| `FceuxGameMenu.cs` | `BsnesGameMenu.cs` / `SuperZsnesSettingsPage` | the rich per-game window: Screen (full screen, clip left/right 8 px, no 8-sprite limit), System (region: as detected / NTSC / PAL / Dendy, Game Genie, new PPU - config only), the bottom line "N option(s) added to the command line, before the ROM path:"; FDS group only for FDS games |
| `FceuxSaveDesk.cs` | `BsnesSaveDesk.cs` | copy, names changed |
| `FceuxSaves.cs` | `RaSnes9xSaves.cs` (one row per slot) | `.sav` (NES) and `.fds` (FDS disk save) as GameSaveGame, states `.fc0`-`.fc9` one row per slot (`Slot` in the id, `GetPotentialSaveSlots` 0..9, `SlotRestore`), `-bak.fcN`, `.bak.fc0`, `-resume.fcs`, `-autosaveN.fcs` ignored; ids `{Emu fceux, Kind, Sys nes|fds, By name, Id <rom name>, Slot}` - `Sys` because a `.fds` game and a `.nes` game can share a base name and the states `.fcN` have the same extension for both; the rom name of an ARCHIVE is its inner entry's |
| `FceuxSavePaths.cs` | `AresSavePaths.cs` / `BsnesSavePaths.cs` | at install and each launch: `odnonvol` / `odstates` / `odsnaps` / `odcheats` set to `<exe>\Saves\<platform>\...` of the GAME's platform when empty or when they hold a folder the pack set (another platform's, or a stale one); a folder the user chose is kept. Files of THIS game in FCEUX's default `sav\` / `fcs\` / `snaps\` / `cheats\` (written before the pack) MOVED into the new folder, never over an existing file |
| `FceuxStateThumbs.cs` | `RaSnes9xStateThumbs.cs` | `<state>.png` from chunk 8 of the state (zlib, `ZLibStream`), 256x240, the default palette embedded (192 bytes); refreshed at listing and `AfterSession` |
| `FceuxFirmware.cs` | `AresFirmware.cs` | `disksys.rom` found with `LbipBiosImport` (RetroArch `system\`, other emulators' bios folders, `fds_bios.bin` accepted as a name) and copied into the exe folder (or `odfdsrom`) |
| `Log.cs` | `Bsnes\Log.cs` | `fceux.log` |
| `src\Probe\FceuxCheck.cs` | `AresCheck.cs` / `BsnesCheck.cs` | cfg editor round trip on a copy of `fceux.cfg.firstrun` (order, LF, unknown keys), hotkey records (decode the first-run table = the default table above), state thumbnail decode of a real `.fcN` (equal to a reference PNG), latest version read, systems table, platform rows only added |

The hotkeys the plugin binds (pack, Mehdi 07/10; RaSnes9x precedent of unbinding a default on the same key): F2 added to
`SAVE_STATE` and F4 to `LOAD_STATE` (FCEUX's own I / P kept), F6 `SAVE_SLOT_PREV` and F7 `SAVE_SLOT_NEXT` (empty by
default), Esc `EXIT` (empty by default); each only when no other command or pad button holds the key, and FCEUX's
DEFAULT on that key (`LOAD_STATE_SLOT_2/4/6/7`, `HIDE_MENU_TOGGLE` on Esc) emptied only while it is still exactly the
default. F11 for full screen: `MISC_TOGGLEFULLSCREEN` already has Alt+Enter - open question 3.

Per-game command-line switches leak into fceux.cfg at exit (section 3): after the session the plugin puts back the
config values it overrode (`eoptions` bits, `"genie"`, `"palyo"`, `dendy`) when they still hold what the launch forced.

FDS side switching has no default key (`FDS_EJECT_INSERT`, `FDS_SIDE_SELECT`): the pack could bind two keys for FDS
games (open question 2).

---

## 12. Open questions (for Mehdi / the lead)

1. `Nintendo Famicom`: claim it by name (it is in LiteBox's RomM map, not in LaunchBox's platforms)? And where do the
   saves of `Nes - Super Mario Bros. Hacks` go: `Saves\Nintendo Entertainment System\` (same console) or
   `Saves\Nes - Super Mario Bros. Hacks\` (per platform name)?
2. FDS games need "switch disk side" / "eject": FCEUX has no default key. Bind two keys (which?) when empty?
3. F11 full screen: add it as a second binding of `MISC_TOGGLEFULLSCREEN` (Alt+Enter stays)? The pack rule says "bind
   only when empty".
4. A ROM path with characters outside the ANSI code page cannot be opened by FCEUX (measured). Refuse with a notice, or
   hand FCEUX a copy/hard link under `<exe>\Unpacked\` with an ANSI name (the save then takes that name)?
5. Full screen is exclusive DirectDraw and could not be validated on this machine (section 5). Mehdi to test a launch
   with the plugin's full screen on his real display. If it misbehaves, the alternative is the Qt build
   (`qfceux.exe --fullscreen 1`, 97 MB, other config format) - a second integration, not planned.
6. `qfceux.exe` (Qt build) installed by a user: claim it (needs its own config editor) or leave it to the catch-all?

---

## 13. Stage 2, build A: what is built, what is open (18/10/2026)

### Built (src\Fceux, assembly `Fceux`, namespace `LbIntegrations.Fceux`)

PackName `Nixx-FCEUX`, PluginId `f805dff2-11aa-4a4c-8117-9deb2698c755` (manifest.json and `FceuxSettings.PluginId`),
`emu = "fceux"` in the save ids. One public `EmulatorPlugin` type, `FceuxPlugin`, deriving it directly.

| File | State |
|---|---|
| `Fceux.csproj`, `manifest.json`, `Log.cs` | copies of Bsnes's, names changed; help `docs\help\fceux.html` embedded as `help.html`; the ILRepack merge passes (`bin\Release\merged\Fceux.dll`) |
| `FceuxSystems.cs` | the one table (section 2): systems `nes` (`.nes .unf .unif .nsf`), `fds` (`.fds`, `disksys.rom` 8 192 B); platforms `Nintendo Entertainment System` (`.fds` there = disk), `Nintendo Famicom Disk System`, `Nintendo Famicom` (LiteBox's RomM name, claimed by name - decision of this stage on open question 1), `Nes - Super Mario Bros. Hacks`; `SystemFor` (extension, the ROM inside an archive, else the platform's); `FolderOf` (the platform's folder name under `Saves\`) |
| `FceuxPlugin.cs` | catalogue row (`AutoExtract = false`, one `LbCatalogPlatform` per platform with its own extensions, the FDS BIOS named), row injection in the constructor and the three host events, claim by exe name (`fceux64.exe`, `fceux.exe`; not `qfceux.exe`), hotkey scripts filled when empty, missing platform rows ADDED (in memory on a claimed entry, only the platforms the library has; all of them on an entry the install creates; never a row removed or unticked; default only where no emulator is), `IsUpdateAvailable`'s three answers, install/update, BIOS per platform, no RetroAchievements, `PrepareCore` |
| `FceuxBuild.cs` | API `releases/latest` (asset `fceux-<v>-win64.zip`, `digest` SHA-256 checked after the download), fallback HEAD `/releases/latest` -> tag from `Location`, version = `\d+\.\d+\.\d+` of the tag, 10-minute cache, stamp `lbip-fceux-build.txt`, `DescribedVersion` ("FCEUX x.y.z" in the exe's bytes, for the page only) |
| `Archives.cs` | release zip extracted OVER the install (flat; a single top folder would be stripped; `fceux.cfg` never replaced); a game's archive: its ROM entries (`RomInside` for the save name / system), `Unpack` for the UNIF case |
| `FceuxCfg.cs` | fceux.cfg editor: lines kept as bytes, a key set in place (its CR kept), a new key at its byte-order place, LF, no BOM, ANSI code page, written only when changed (temp + move); `Representable` (a path FCEUX can take) |
| `FceuxHotkeys.cs` | the 92-byte records of `FCEUD_CommandMapping_V2`; FCEUX 2.7.0's default table rebuilt byte for byte; the pack's F2 / F4 / F6 / F7 / Esc bound by the rule of section 11; a table of another length (2.6.4: 128 commands) left alone |
| `FceuxSavePaths.cs` | `"odnonvol"` / `"odstates"` / `"odsnaps"` / `"odcheats"` = `<exe>\Saves\<platform>` (+ `States`, `Screenshots`, `Cheats`) of the GAME's platform at each launch when empty or the pack's (under this install's `Saves\`, or the pack's shape gone stale); a folder the user chose is kept; at install the NES's; the game's files in FCEUX's own `sav\` `fcs\` `snaps\` `cheats\` MOVED (never over a file); nothing beside the game is FCEUX's, so nothing there is touched |
| `FceuxFirmware.cs` | `disksys.rom` (or `fds_bios.bin`) copied from RetroArch's `system\` and the other emulators' bios/system/firmware folders (`LbipBiosImport`) at install and at each disk launch; a disk launch without an 8 192-byte BIOS refused with `LbipNotice` naming the file and folder |
| `FceuxLaunch.cs` | `PrepareConfig` before the launch (never while FCEUX runs from that folder): folders, `"fs"` from the Fullscreen option, `vmcx`/`vmcy`/`vmcb` = the primary display's mode when 0 (section 5), the hotkeys, then the migration; the line is the host's, unchanged, except a `.unf`/`.unif` inside an archive (FCEUX does not look for them there): unpacked into `<exe>\Unpacked\<archive>-<hash>\` and the line pointed at it; a path outside the ANSI code page refused with a notice (decision of this stage on open question 4: refused, no copy) |
| `FceuxAhk.cs` | Exit `WinClose, ahk_exe <exe>`; Save / Load State = `PostMessage, 0x111, 40344 / 40343` to `ahk_class FCEUXWindowClass` (FCEUX's own menu commands, current slot): they need neither the focus nor a key, so a rebound key cannot turn a save into a load; `Superseded` = the other exe name's scripts |
| `FceuxSettings.cs`, `FceuxSettingsPage.cs` | the store (Bsnes's) with one option, `Fullscreen` (default full screen); the simple Nixx page: the install and its version, the FDS BIOS (state + "Look for it"), the screen |
| `FceuxGameMenu.cs` | the `GameMenu` contract and the menu icon (`LbipMenuIcon`); `Entries` returns none yet |
| `FceuxSaveDesk.cs`, `FceuxSaves.cs` | the front desk wired (other emulators' saves listed, rows routed to their owners); `GetPotentialSaveSlots` = 0..9; FCEUX's own files NOT listed yet |

### Measured in this stage (lab `G:\LB1326\lbip-fceux-lab\run-plugin\`, a copy of the 2.7.0 win64 install)

- `--fceux-prepare` wrote fceux.cfg for Zelda II on `Nintendo Entertainment System` (windowed), then FCEUX was started by
  hand on the copied ROM with `tools\plugin-keys.ahk` (AutoHotkey 1.1.24 Send) then `tools\plugin-scripts.ahk` (the
  plugin's PostMessage scripts, the taskbar holding the focus): the states `.fc0` / `.fc1`, their `-bak` copies and the
  `.bak.fc0` undo landed in `Saves\Nintendo Entertainment System\States\`, the `.sav` (8 192 bytes) in
  `Saves\Nintendo Entertainment System\`; F7 + F2 wrote `.fc1`, F6 + F4 loaded slot 0, Esc quit; the menu-command scripts
  saved / changed slot / loaded without the focus and `WinClose` quit (exit 0). Files dated 18/10 18:31.
- The pack's folders survived FCEUX's own exit save (they are in the file it rewrote).
- Full screen was NOT tried again (section 5).

### Probe checks (`src\Probe\FceuxCheck.cs`, wired in `src\Probe\Program.cs`), all green on 18/10

```
dotnet run --project src\Probe -c Release -- src\Fceux\bin\Release\merged\Fceux.dll --fceux-systems G:\LB1326\lbip-fceux-lab\src-2.7.0
dotnet run --project src\Probe -c Release -- <dll> --fceux-cfg G:\LB1326\lbip-fceux-lab\fceux.cfg.firstrun
dotnet run --project src\Probe -c Release -- <dll> --fceux-hotkeys G:\LB1326\lbip-fceux-lab\fceux.cfg.firstrun
dotnet run --project src\Probe -c Release -- <dll> --fceux-launch [G:\LB1326\lbip-fceux-lab\fceux.cfg.firstrun]
dotnet run --project src\Probe -c Release -- <dll> --fceux-latest            (network)
dotnet run --project src\Probe -c Release -- <dll> --fceux-extract G:\LB1326\lbip-fceux-lab\dl\fceux-2.7.0-win64.zip <new folder>
dotnet run --project src\Probe -c Release -- <dll> --fceux-prepare <fceux64.exe> --rom <rom> --platform <name> [--settings <ini>]   (writes a REAL install's fceux.cfg, for oracles)
```

The generic probe (`<dll> --emu <exe> --platform ...`) CALLS PrepareEmulatorForLaunch, which writes the install's
fceux.cfg (full screen by default): point it at a lab copy only. `--saves-selftest` stays green.

### Open, for build B (next stage)

1. Saves listing (`OwnGetSaves`): `.sav` / `.fds` (disk save) as GameSaveGame, `.fc0`-`.fc9` one row per slot, ids
   `{emu fceux, kind, sys nes|fds, by name, id <rom name, the ROM inside an archive>, slot}` (decided, section 11);
   `-bak.fcN`, `.bak.fc0`, `-resume.fcs`, `-autosaveN.fcs` ignored; restore / delete (`SaveRows.TargetFor`,
   `SlotRestore`, `SaveCompanions`), `OwnExpectedDir`, `ExecutableOf` (two installs: `inst` is the desk's).
2. State thumbnails `<state>.png` from chunk 8 (section 8), at listing and after the session; `OwnGetCompanionSaveFiles`
   / `OwnIsSecondarySaveFile` for the `.png`.
3. The per-game window (SuperZsnes model): region (-pal / -dendy - MEASURE first whether they persist in fceux.cfg, with
   a USA-named copy of the ROM), Game Genie, clip sides, no sprite limit, full screen per game; the bottom line of switches;
   putting back after the session what a switch leaked into fceux.cfg (section 3). `GameMenu.Entries` then offers
   "Nixx-FCEUX : Options...".
4. Mehdi's tests in LaunchBox: install from Add Emulator, a NES launch (full screen on his real display - open question 5),
   Esc / pause-screen Save / Load / Exit, an FDS game with his `disksys.rom`.
5. Still open for Mehdi: questions 2 (FDS side keys), 3 (F11 as a second full-screen binding), 5, 6 of section 12;
   questions 1 and 4 were decided as above and can be overruled.

### For the lead's integration (brief section 17)

- `deploy-dev.ps1`: `'Fceux' = @{ Folder = 'Nixx-FCEUX'; Old = @() }` and `Fceux` in the `-All` list (no vendored archive).
- `build-release.ps1`: a `$Stage` row for `Fceux` / `Nixx-FCEUX`.
- `src\Installer`: folder `Nixx-FCEUX`, assembly `Fceux.dll`, dll + manifest + contract; `What()`: "NES, Famicom, Famicom
  Disk System".
- `src\NixxSaves\CatchAllClaims.cs`: `fceux64.exe`, `fceux.exe` in `Names`.
- `src\Probe\Program.cs`: the seven `--fceux-*` lines are already in this branch (near the `--bsnes-*` lines).
- No change to any `Shared.*` folder.

**For the integration, now:** CatchAllClaims names `qfceux.exe` (not `fceux64.exe` / `fceux.exe`).

---

## 14. The Qt / SDL build (18/10/2026, Mehdi: "ok, on part sur la qt/SDL")

Everything below is MEASURED on `qfceux.exe` 2.7.0 (lab `G:\LB1326\lbip-fceux-lab\runqt2` and `x-extract-qt`, Zelda II,
windowed only - no full-screen test on this machine).

### Install
- Asset `fceux-<v>-win64-QtSDL.zip` (211 entries: `bin\`, `doc\`, `luaScripts\`, `palettes\`, `tools\`), extracted as it
  is into `Emulators\Nixx-FCEUX\`; the exe is `bin\qfceux.exe`; the stamp `lbip-fceux-build.txt` beside it. The install
  folder (`FceuxSavePaths.InstallDir`) is `bin`'s parent. `DescribedVersion` reads "FCEUX 2.7.0" in its bytes.

### fceux.cfg (`bin\fceux.cfg`, portable)
- `name = value`, CRLF, no BOM; FCEUX rewrites it whole at exit (610 lines), keeping only the names it knows. A missing
  name takes FCEUX's default, so the plugin may write a file holding only its own lines: MEASURED, a 19-line file
  (screen + hotkeys) came back as 610 lines with every plugin line kept.
- An option on the command line is stored too (`--pal 1` left `SDL.PAL = 1`): the plugin writes `SDL.Fullscreen` in the
  file, never on the line.
- Video driver `SDL.VideoDriver`: 0 OpenGL (default), 1 SDL, 2 QPainter, 3 Vulkan. Full screen is `showFullScreen` (a
  borderless window, the display mode untouched).

### Hotkeys (`SDL.Hotkeys.<action> = <Qt key text>`, one key per action)
The pack's: SaveState F2, LoadState F4, SelectStatePrev / Next F6 / F7, FullScreen F11, Quit Esc, Pause P, Turbo L (held -
the Qt build has no fast-forward toggle), Screenshot F8, VolumeMute F9, SoftReset H, HardReset Shift+Esc. FCEUX's own
LoadState2 / 4 / 6 / 7 / 8 / 9 sat on F2 / F4 / F6 / F7 / F8 / F9: emptied. No PAL / NTSC hotkey exists. Same rule as
puNES: only an action at its default, empty, or already the pack's is set; never a key on two actions (`FceuxHotkeys`).

### Folders: junctions
The Qt build has no setting for its folders (`drivers\Qt\config.cpp` CreateDirs, under the exe's folder). `bin\sav`,
`bin\fcs`, `bin\snaps`, `bin\cheats` are made junctions to `Saves\<platform>`, `\States`, `\Screenshots`, `\Cheats`,
pointed at the launching game's platform before each launch (`FceuxSavePaths.Ensure`, the puNES mechanism). MEASURED:
F2 wrote `States\<rom>.fc0`, F8 `Screenshots\<rom>-0.png`, Esc quit (code 0) and wrote `<rom>.sav` - all through the
junctions, which were still junctions after the exit. Files of a real folder (FCEUX run before the plugin) are moved to
their game's platform (the library by name, else the NES), never over a different file (that one goes to
`Saves\Unsorted\<kind>\`). A link of the user's is kept. No junctions on the volume: `lbip-fceux-folders.txt`, FCEUX
keeps its own folders.

### Pause screen scripts
No menu command to post in Qt: Save / Load activate the window and send F2 / F4; Exit is `WinClose` (WM_CLOSE writes
the `.sav`). The scripts written for the classic build are replaced when found as written.

### BIOS
`disksys.rom` beside the exe (`bin\`), no setting.

### Probes (all green 18/10)
`--fceux-systems`, `--fceux-cfg <qt first-run cfg>`, `--fceux-hotkeys <qt first-run cfg>`, `--fceux-launch [<cfg>]`
(junctions, moves, Unsorted, user link kept, disk BIOS, ANSI refusal, .unf in a zip), `--fceux-extract <QtSDL zip>`,
`--fceux-latest`, `--fceux-prepare` (used for the real run above). Fixture: `G:\LB1326\lbip-fceux-lab\qt-fceux.cfg.firstrun`
(a first-run file - note it carries `SDL.PAL = 1` from the `--pal` measure).

### Open
- Full screen on Mehdi's real display (F11, and `SDL.Fullscreen = 1` at start).
- Non-ANSI paths with the Qt build (kept refused, as for the classic build, until measured).
- The FDS disk side / eject keys - not bound yet.
