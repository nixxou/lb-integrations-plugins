# puNES: research notes for the Nixx-puNES plugin

Stage 1 (research) of the `nes-punes` branch, 18/10. Everything below was either **measured** on the real
`punes64.exe` 0.111 in the lab (`G:\LB1326\lbip-punes-lab\`), or **read** in puNES's source at the released tag
`v0.111` (commit `613b7b4`, cloned in the lab as `src-v0.111\`). Each fact says which. Nothing here comes from a wiki.
No plugin code yet: the plan is at the end (section 13).

Lab layout (all outside `%TEMP%`, nothing beside the original test ROM):

| Path | What |
|---|---|
| `G:\LB1326\lbip-punes-lab\dl\` | the official release zips, untouched |
| `...\d3d9\`, `...\opengl\` | each zip extracted, untouched (reference copies) |
| `...\run1\`, `...\run2\` | working copies the oracle ran |
| `...\roms\` | copy of `Zelda II - The Adventure of Link (Europe).nes`, plus test archives and a fake FDS image |
| `...\tools\oracle.ps1` | starts the exe, waits, lists its windows (title, rect), optionally posts keys, posts WM_CLOSE, waits for the exit |
| `...\tools\pngin.py` | finds and extracts the PNG embedded in a state file |
| `...\src-v0.111\` | the source at the release tag |

---

## 1. Where it comes from, which build

- Project: https://github.com/punesemu/puNES (author Fabio Cavallo "FHorse", GPL-2.0-or-later).
- **Latest release: `v0.111`, published 2024-02-25** (GitHub API `releases`, read 18/10). Nothing newer is released;
  `https://github.com/punesemu/puNES/releases/latest` redirects (HEAD, `Location`) to `/releases/tag/v0.111`.
- Windows assets of v0.111 (GitHub API; the assets carry **no `digest`** field, they predate it):

  | Asset | Size | SHA-256 (computed on the download, 18/10) |
  |---|---|---|
  | `punes-0.111-x86_64-win-d3d9.zip` | 25 257 797 | `EB973F8DFDE20F831B3CCD39A182FFFD39B80C9070EADF58D3964062CC470E2B` |
  | `punes-0.111-x86_64-win-opengl.zip` | 25 081 941 | `DC48445F0FB0923593B4443CDFA20A6E1E5F1EEC6E80E7C8F5EE106C163AB44D` |
  | `punes-0.111-i686-win-d3d9.zip` / `-opengl.zip` | 22 864 532 / 22 765 804 | not downloaded (32-bit, Windows XP builds) |

  URL: `https://github.com/punesemu/puNES/releases/download/v0.111/<asset>`.
- **WIP builds**: the README of v0.111 (lines 32-37) points to GitHub Actions artifacts through nightly.link
  (`https://nightly.link/punesemu/puNES/workflows/build/master/punes64.wip.d3d9.zip`). The old AppVeyor project's last
  build is from 2022-03-30 (AppVeyor API, read 18/10): dead. On 18/10 the nightly.link URL answers **404**: the last
  successful `build.yml` run on master is 2025-12-31 (run 20616141271, `2ed5b1b`), its artifacts have expired, and every
  master run since 2026-07 failed (latest commit `b1758ec`, 2026-09-01, "Update lib7zip library..."). The project is
  alive but **no WIP build can be downloaded today**, and an Actions artifact needs a GitHub login without nightly.link.
  => The plugin installs the **pinned release v0.111** (the RaSnes9x/Demul model: official URL, SHA-256 checked before
  extraction; a vendored copy in `vendor\punes\` is optional, the release asset has been stable for 2.5 years).
- **D3D9 or OpenGL**: both zips hold the same 12 files except `cgD3D9.dll` / `cgGL.dll` and the exe. The README build
  table says D3D9 is the Windows default (`ENABLE_OPENGL` "instead of Direct3D 9 (only for Windows)"). Proposed: the
  **x86_64 D3D9** build (the one the oracle ran). Open question for Mehdi (section 14).

### The zip (measured, `System.IO.Compression` listing)

Flat, **no top folder**: `7z.dll, avcodec-58.dll, avformat-58.dll, avutil-56.dll, cg.dll, cgD3D9.dll (or cgGL.dll),
dip.cfg, libwinpthread-1.dll, nes20db.xml, punes64.exe, swresample-3.dll, swscale-5.dll`. No config file is shipped:
`puNES.cfg`, `input.cfg`, `recent.cfg` are created by the emulator. `dip.cfg` and `nes20db.xml` are the emulator's own
data (dip-switch and game databases), overwritten on update with no loss.

### The exe (measured)

| Build | `punes64.exe` size | SHA-256 | MD5 |
|---|---|---|---|
| d3d9 | 27 312 672 | `2D4886E18F24EF0DDC7E69F04995B1D43E039FA2B0EBB8A8F0C821A7F3DD27F5` | `FBEDC6C1EC63F13A3E5B7C832941CC4E` |
| opengl | 27 942 432 | `5EC6D367897544573B3715EC16B1B0973E05A33212E79BE9FD4D81B8420713DB` | `2BE2FC2DF34A7CF8CD3A4B18A8027254` |

- Version resource (measured, `VersionInfo`): `FileVersion 0.111`, `ProductVersion 0.111`, `ProductName puNES`,
  `CompanyName FHorse`, `FileDescription Nintendo Entertainment System Emulator`. => version detection can read the
  file version; the plugin's own install is told apart by the stamp `lbip-punes-build.txt` (Bsnes model) and/or the
  exe MD5 (RaSnes9x model).
- `--version` / `-V` prints to stdout (source `cmd_line.cpp:303`) but the exe is a GUI subsystem program: measured, the
  run with `--version` showed no window and printed nothing to a PowerShell `Start-Process` - not usable.
- Exe names: `punes64.exe` (64-bit), `punes.exe` (32-bit) (README "Portable mode" examples). The README says a `_p`
  suffix (`punes64_p.exe`) forces portable mode, so a user's install can be named that way too. No other emulator of
  the pack or of LaunchBox's base ships these names (brief section 2 list): **claiming by file name is enough**
  (`punes64.exe`, `punes.exe`, `punes64_p.exe`, `punes_p.exe`, case-insensitive).

---

## 2. Systems emulated, and the LaunchBox platforms

### What puNES runs (read in `src\core\emu.c` `emu_load_rom`, `src\gui\mainWindow.cpp:1377-1405` open-file filters,
`src\core\common.h:55-72`, `src\core\ines.c:160-195`, `src\core\emu.c:985-1015`)

| What | Files (as the load code dispatches them) | Status in 0.111 |
|---|---|---|
| NES / Famicom cartridge | `.nes` (iNES 1.0 / NES 2.0), `.unf` `.unif` (UNIF, tried when iNES fails) | emulated; region by `--mode` / `preferred mode` = `pal, ntsc, dendy, auto` (`common.h:55` `machine_mode`) |
| Dendy (Famiclone timing) | same `.nes` files | a **timing mode** (`mode dendy`), not a separate system or file type |
| Famicom Disk System | `.fds` (fwNES or headerless), `.qd` (Quick Disk) - `emu.c` -> `fds_load_rom(FDS_FORMAT/QD_FORMAT)` | emulated, needs `disksys.rom` (section 7) |
| Nintendo Vs. System | `.nes` with NES 2.0 console type 1 (`ines.c:173`, `vs_system.ppu`, `vs.c` inputs, "insert coin" shortcut) | emulated **from NES 2.0 `.nes` dumps only**; no loader for MAME-style Vs. sets (zips of split chips) |
| PlayChoice-10 | `.nes` with console type 2 | **only logged** ("Playchoice 10" in `emu.c:1002`, nothing else references `PLAYCHOICE10`): runs as the plain NES game, no PC-10 hardware |
| VT01-VT369, UMC, EPSM, Famicom Network System | console types 4-12 | logged as "[unsupported]" (`emu.c`) |
| NSF / NSFe music | `.nsf`, `.nsfe` (`nsf_load_rom`, `nsfe_load_rom`) | a music player, not a game |
| TAS movie | `.fm2` | replays a movie over a ROM; not a game file |
| Archives | `.zip`, `.7z`, `.rar` (through the shipped `7z.dll`; `.rar` only if `l7z_control_ext(".rar")` succeeds) | opened natively, section 6 |

### Mapping to LaunchBox platform names (names read in a copy of `G:\LB1326\Metadata\LaunchBox.Metadata.db`, 18/10, and
in LiteBox's own maps `LbApiHost\Host\Romm\RommPlatformMap.cs`, `Host\Ra\RaPlatformMap.cs`)

| LaunchBox platform | puNES system | Extensions for the platform row | BIOS | Notes |
|---|---|---|---|---|
| `Nintendo Entertainment System` (main) | NES cartridge | `.nes; .unf; .unif; .nsf; .nsfe; .zip; .7z` | none (optional `gamegenie.rom`, section 7) | Vs. System and PlayChoice-10 `.nes` dumps belong here (LaunchBox has no Vs. System platform; MAME's Vs. sets are "Arcade" and puNES cannot read them) |
| `Nes - Super Mario Bros. Hacks` (LiteBox name, brief section 2) | NES cartridge | same as NES | none | puNES applies a same-name `.ips`/`.bps`/`.xdelta` beside the ROM (`core\patcher.c:55-110`, read) |
| `Nintendo Famicom` (LiteBox name: present in `RommPlatformMap.cs` "Nintendo Famicom" -> `famicom`; NOT a LaunchBox platform, `Famicom` is only an alternate name of NES in `PlatformAlternateNames`) | NES cartridge | same as NES | none | proposed, question for Mehdi (section 14) |
| `Nintendo Famicom Disk System` | FDS | `.fds; .qd; .zip; .7z` | `disksys.rom`, **required** | LaunchBox's own FCEUX/Nestopia rows list `.fds` but have no FDS platform row (brief section 2) |

Not claimed: `Arcade` (Vs. System MAME sets are not loadable by puNES and Arcade belongs to MAME), Dendy (no LaunchBox
platform: it is the per-game region option `mode`), NSF players (no platform; the extensions ride on the NES row as
LaunchBox's FCEUX/Nestopia rows do), `.fm2` (needs the ROM, not a game).

**Picking the system at launch**: puNES picks by itself from the file - `.fds`/`.qd` -> FDS, everything else -> the
cartridge loaders (read, `emu.c`). There is no `--system` switch to write on the platform rows. So the ares priority
(game window > platform row line > table + extension) reduces to: the **table + extension** decides the system for the
plugin's own needs (BIOS check, save folder names, the game window's groups); the only per-game "system-like" choice is
the region (`--mode pal|ntsc|dendy|auto`), offered in the game window. A platform not in the table is refused with
`LbipNotice` (ares model) rather than guessed.

---

## 3. Portable mode, where everything goes

Read in `src\gui\cmd_line.cpp:362-375` (`cmd_line_check_portable`) and `src\gui\qt.cpp:343-372`:

- **Portable** when the exe's base name ends with `_p`, OR `puNES.cfg` exists in the exe's folder, OR `--portable` is
  on the command line. Portable: config folder = data folder = **the exe's folder**.
- Not portable: config and data in `QStandardPaths::GenericConfigLocation\puNES` (`%LOCALAPPDATA%\puNES` on Windows), and
  on the very first run a **wizard dialog** (`dlgWizard`, `mainApplication.cpp:91-101`) asks where to put things - it
  would block a launch.
- Measured (run1, fresh copy of the zip, `--portable <rom>`): the exe created `bios\ cheat\ diff\ jsc\ pgs\ prb\ save\
  screenshot\ shp\`, `input.cfg`, `recent.cfg`, `puNES.cfg` in its own folder; `%LOCALAPPDATA%\puNES` still did not
  exist afterwards.
- Measured (run2, fresh copy + an **empty** `puNES.cfg` beside the exe, no `--portable`): portable, no wizard, the empty
  file was filled with the defaults at exit (10 284 bytes, identical size to run1's).
  => The plugin's install creates an empty `puNES.cfg` beside the exe if none is there (and keeps the user's when
  present); `--portable` is not needed on the line.

Folders (read in `src\core\common.h:35-48`, file names in `memmap.c:1898-1910`, `save_slot.c:759-795`,
`fds.c:1086-1100`, `qt.cpp:932-934`), all under the data folder = the exe's folder in portable mode:

| Folder | Content | File name |
|---|---|---|
| `prb\` | battery RAM (cartridge saves) | `<rom base name>.prb` |
| `save\` | save states | `<rom base name>.p00` ... `.p0B` (NSF: `.n00`...) |
| `diff\` | FDS disk writes (default write mode) | `<rom base name>.ipf` (`.ipq` for `.qd`) |
| `screenshot\` | screenshots | `<rom completeBaseName>...` (`qt.cpp:932-934`) |
| `bios\` | `disksys.rom`, `gamegenie.rom` | |
| `pgs\` | per-game settings | `<rom completeBaseName>.pgs` (`settings.cpp:30`) |
| `cheat\`, `shp\`, `jsc\` | cheats, shader parameters, joystick configs | |

- **None of these folders is configurable** (read: no setting in `puNES.cfg` holds a path for them; measured: the full
  `puNES.cfg` in section 4 has none). => Nothing ever lands beside the game (lot 9 is satisfied by construction, and
  there is nothing to migrate from beside the game), **but per-platform save folders cannot be had through a setting**
  (section 14).
- `<rom base name>` = the file name without its last extension. A `.nes` and a `.fds` with the same base name share
  `save\<name>.pXX` -> the save ids must carry the system (`SaveIdData.Sys`, brief section 0).

---

## 4. `puNES.cfg` (measured)

- Format: INI-like, `[group]` headers, `key=value` (keys with spaces: `preferred mode=auto`), each key preceded by a
  `# possible values: ...` comment line, a blank line between keys and two between groups.
- Encoding: UTF-8, **no BOM** (first bytes `5B 73 79` = `[sy`), **CRLF** (369 CR, 369 LF in the run1 file). Source:
  `objSettings.cpp:2376` `wr_cfg_file`, `setGenerateByteOrderMark(false)`, `NEWLINE`.
- **Rewritten whole at every exit** (measured: `LastWriteTime` = the WM_CLOSE moment in every run). The writer walks
  puNES's fixed key table (`wr_cfg_file`): a line puNES does not know is **dropped** (measured run1 pass 2: an added
  `foo=bar` and `# my comment` were gone after the exit), comments are regenerated, key order is puNES's.
- A value edited in the file while puNES is closed **is kept** (measured: `size window=2x` -> `3x` before the launch,
  still `3x` after, and the window title said "3x").
- What it writes at exit is the GUI group freshly plus the other groups **as read at start** (`mainWindow.cpp:327`
  `settings_save_GUI` -> `objSettings::wr("GUI")`, read). The whole current configuration (including values given on
  the command line) is written only when `save settings on exit=yes` (`emu.c:67`) or when the user presses OK in the
  Settings dialog (`dlgSettings.cpp:132` `settings_save()`) - **read, not measured**: a user who opens Settings during a
  launch with `--fullscreen=yes` could persist it.
- Keys relevant to the plugin (full file of run1 in the lab: `G:\LB1326\lbip-punes-lab\cfg-after-run1.cfg`):
  `[system] preferred mode=auto`, `save battery ram file every 180 sec=no`, `fds bios file=`, `save settings on exit=no`;
  `[video] fullscreen=no`, `fullscreen in window=no`, `size window=2x`, `stretch in fullscreen=no`,
  `use integer scaliung in fullscreen=no` (sic), `filter=none`, `shader=none`, `palette=ntsc`, `vsync=on`;
  `[GUI] allow multiple instances of the emulator=no`, `toolbar hidden=no`; `[fds] write mode=diff file`,
  `disk 1 sida A at reset=yes` (sic), `switch the side automatically=yes`.
- `recent.cfg`: one ROM path per line, forward slashes (measured).
- `pgs\<game>.pgs` (measured, written when a game loads): `[state] last save slot=0`, `last save file used=`,
  `[video] overscan=default`, `[system] dipswitch=-1`, `[ppu] ...` - same format.

---

## 5. Command line (read in `src\gui\cmd_line.cpp`, `dlgCmdLineHelp.cpp`, `settings.h`; measured where said)

- Syntax: `-x value`, `--long value` or `--long=value`. A non-option argument is the ROM; it is taken only if the file
  **exists** (`QFileInfo::exists`).
- **Defect, measured**: every argument is split on `=` first (`arg.split("=")`), the ROM included: a ROM path holding
  `=` is cut and not loaded (run2: `a=b Zelda.nes` -> no game, `recent.cfg` empty). Section 14.
- Measured: `--fullscreen=yes` and `-u yes` both start full screen (window 0,0-2560,1440 on a 2560x1440 screen);
  after the exit `puNES.cfg` still says `fullscreen=no` -> **command-line options are not written back** (as long as
  the user does not save settings, see section 4). => Per-game options go on the command line (Xenia/ares model).
- Options of interest (help strings, `settings.h`): `-m, --mode pal|ntsc|dendy|auto`, `-u, --fullscreen yes|no`,
  `--fullscreen-window yes|no`, `-r, --int-scl-fullscreen yes|no`, `-t, --stretch-fullscreen yes|no`,
  `-s, --size 1x..6x`, `-i, --filter ...`, `--shader ...`, `-p, --palette ...`, `-v, --vsync`,
  `-o, --overscan`, `--overscan-blk-brd`, `--background-pause yes|no`, `--save-battery-ram-file yes|no`,
  `--rewind-minutes`, `--hidden-gui` (start with the GUI hidden), `--portable`, `--language`,
  `--shortcut.<k|j>.<name>=<key>`, `--input.<p1k|p1j...>.<button>=<key>`.
- **`--shortcut.*` IS persisted** (measured: `--shortcut.k.pause=F10` left `pause=F10,NULL` in `input.cfg`): puNES
  writes `input.cfg` at start with the command-line shortcuts in it. Never use it for a per-launch choice.
- Measured: **a second instance hands its ROM to the first and exits** (instance A open without a game, instance B
  started with Zelda II: B exited with code 0 within 5 s, A's title became the PAL game). `allow multiple instances of
  the emulator=no` is the default. => `LbipLaunchGate` must refuse a launch while puNES already runs (LaunchBox would
  otherwise see the process end at once).

---

## 6. Archives (measured)

- puNES opens `.zip` and `.7z` itself (7z.dll shipped): `Outer Zip.zip` and `Outer 7z.7z`, each holding
  `inner name.nes`, both started the game.
- The extracted copy goes to `%TEMP%\puNES` (`gui_temp_folder`, read) and is gone after the exit (measured: no
  `%TEMP%\puNES` left).
- **The saves take the name of the file INSIDE the archive**: `prb\inner name.prb`, `save\inner name.p00` (measured),
  not the archive's name; `recent.cfg` keeps the archive path.
- An archive with **two** ROMs opens a selection dialog titled with the archive name (measured, `Two.zip`); a WM_CLOSE
  then closed the dialog but left puNES running **with no visible window** (it had to be killed). An unattended launch
  of a multi-ROM archive hangs.
- `.rar`: listed in the filter only if `7z.dll` handles it (`mainWindow.cpp:1388`) - not measured (no RAR tool in the
  lab).
=> Proposal (section 13): AutoExtract false; a single-ROM archive whose inner name equals the archive's base name is
passed as is; any other archive is unpacked by the plugin into `<puNES>\Unpacked\<archive base>-<hash>\<archive
base><inner ext>` (`AresLaunch.Unpacked` / RaSnes9x `RomFor` model) so the save names follow the game's file, as for a
plain ROM.

---

## 7. BIOS (read in `src\core\fds.c:255-330`, `cheat.c:105`; measured where said)

- **FDS: `disksys.rom`**, searched in this order: (1) the `fds bios file=` path of `puNES.cfg`; (2) `disksys.rom` in
  the **current directory**; (3) `disksys.rom` in the **game's folder**; (4) `<data folder>\bios\disksys.rom`. The first
  8 KB are read, **no hash check** (`fds.c:322-328`).
- Measured without any BIOS (fake one-side `.fds` built by `tools\fakefds.py`): **no dialog, no error box**, the window
  stays on the empty screen (title switched to NTSC), WM_CLOSE exits normally. => the plugin must check the BIOS and
  refuse the launch with `LbipNotice` naming `bios\disksys.rom` (ares model).
- The plugin puts it in `<puNES>\bios\disksys.rom` (path 4) through `LbipBiosImport` (RetroArch `system\disksys.rom`, the
  other emulators' bios folders). Reference dump: 8 192 bytes (size read in the source, `prgrom_set_size(0x2000)`).
- No FDS game nor `disksys.rom` exists on this machine (searched `G:\LB1326`, `G:\LB`, `G:\LB14B3`, `G:\LB129`,
  `G:\Minerva_Myrient`, depth 6): **FDS was not run with a real disk** - to measure at stage 2 with a dump Mehdi has.
- Optional, NES: `bios\gamegenie.rom` for `cheat mode=gamegenie` (`cheat.c:105`). Not required, not offered by default.

---

## 8. Battery saves (measured)

- Zelda II (battery cartridge), run1: after WM_CLOSE, `prb\Zelda II - The Adventure of Link (Europe).prb`, **8 192
  bytes**, written at the exit (timestamp = the close). The `.prb` holds the cartridge's NVRAM (`memmap.c:1860-1895`:
  WRAM NVRAM, then VRAM NVRAM per NES, then the mapper's extra battery data).
- `save battery ram file every 180 sec` (default `no`) also writes it periodically.
- FDS: the disk's writes go to `diff\<name>.ipf` (`write mode=diff file`, default). `write mode=original file` writes
  **into the `.fds` image beside the game** (`fds.c:640-662`, read) - against the "nothing beside the game" rule: the
  plugin should keep `diff file` (to decide: force or warn, section 14).

## 9. Save states and their thumbnail (measured)

- 12 slots, `SAVE_SLOTS = 12` (`save_slot.h:27`, read), shown 0-9, A, B in the toolbar (seen in the oracle's
  screenshot). File: `save\<rom base>.p%02X` -> `.p00` ... `.p0B` (`save_slot.c:759-795`, read; `.p00` measured).
  NSF uses `.n%02X`. One file per slot -> one row per slot (RaSnes9x model, `GetPotentialSaveSlots` = 0..11).
- The last slot used is kept per game in `pgs\<game>.pgs` (`last save slot=`).
- Measured state `Zelda II ... .p00`: 22 284 bytes; header little-endian u32 `version` = 100 (`SAVE_VERSION`),
  u32 `crc32` of the ROM, u32 `size` = 15 243 = **offset of the preview**; from that offset to the end of the file, a
  **complete PNG, 256x240** (7 041 bytes, `IEND` at EOF). Extracted with `tools\pngin.py` -> `state-p00.png` shows the
  game screen. => `PunesStateThumbs`: read u32 at offset 8, check the PNG signature there, copy to EOF into
  `<state>.png`. No window capture needed.
- The ROM's CRC32 in the header means a state only loads on the same ROM (`save_slot.c:129` and `:229`).
- README of v0.111: states of 0.110 or earlier are not compatible.

## 10. Hotkeys (measured)

`input.cfg`, `[shortcuts]` group, `name=<keyboard>,<joystick>` with Qt key-sequence names, `NULL` for none; UTF-8 no
BOM, CRLF (359 CR = 359 LF). **Written at every start** (from the file + the command-line shortcuts), not at the exit
(timestamps of runs 3-4). A value edited before the launch is kept (measured: `save state=F2,NULL` and
`decrement state slot=NULL,NULL` survived, and F2 then saved a state).

Defaults of 0.111 (measured file): `save state=F1`, `load state=F4`, `increment state slot=F3`,
`decrement state slot=F2`, `hard reset=F11`, `soft reset=F12`, `mode pal=F6`, `mode ntsc=F7`, `mode dendy=F8`,
`mode auto=F9`, `video fullscreen=Alt+Return`, `toggle menubar in fullscreen=Esc`, `quit=Alt+Q`, `pause=Pause`,
`fast forward=Tab`, `save screenshot=Alt+X`, `insert coin=8`, `switch sides=Alt+S`, `eject disk=Alt+E`,
`start or stop rewind mode=Ctrl+Left`, ...

Against the pack's keys (brief section 10: F2 save, F4 load, F6/F7 slot down/up, F11 full screen, Esc quit): **every
one of those keys is already bound by default** (F2 = decrement slot, F6/F7 = PAL/NTSC, F11 = hard reset, Esc = menubar
toggle). The rule "bind only when empty and no other hotkey holds the key" therefore binds **nothing** on a default
install; puNES's own F1/F4/F2/F3 stay. The AutoHotkey Save/Load State scripts send puNES's current keys (read from
`input.cfg`). Esc does not quit: the Exit script is `WinClose ahk_exe punes64.exe`, and an Esc script
(`LbipAhk.EscapeScript`) is needed if Esc must quit (melonDS model). Question for Mehdi (section 14).

Posting `WM_KEYDOWN`/`WM_KEYUP` (F2) to the window from the oracle worked when the freshly opened window was the
foreground one, and did nothing in later runs where it was not (Qt shortcuts follow the active window): AutoHotkey's
`Send` to the active puNES window is the right way, not `PostMessage`.

## 11. WM_CLOSE and full screen (measured)

- WM_CLOSE on the main window (class `Qt5158QWindowIcon`, title `puNES_p (<region>, <size>, <filter>, <palette>)`)
  exits with **code 0, no confirmation**, writes the `.prb` and `puNES.cfg`, in a window and in full screen alike.
- The main window's title starts with `puNES` (`puNES_p` in portable mode); before a game is loaded a hidden small
  window titled `FHorse` exists.
- Region shown in the title: Zelda II (Europe) ran as PAL with `preferred mode=auto` (game database `nes20db.xml`).

## 12. No RetroAchievements

No `rcheevos`/RetroAchievements code in the source (grep, read): `RetroAchievementSupportResponse(isSupported: false)`.

---

## 13. Plan of the plugin (one plugin, `src\Punes`, assembly `Punes`, namespace `LbIntegrations.Punes`)

PackName `Nixx-puNES`; save id `emu = "punes"`, `Sys = "nes" | "fds"` (section 3: `.nes` and `.fds` of one base name
share a state file name). New PluginId GUID at stage 2.

| File | Model | What it does for puNES |
|---|---|---|
| `Punes.csproj` | `Bsnes.csproj` | net10.0-windows, Shared.Lbip/Saves/Compat/Menu/Ui compiled in, SharpCompress for the archives, ILRepack, `docs\help\punes.html` embedded |
| `manifest.json` | Bsnes | `Nixx-puNES`, new GUID, version 1.2.2 |
| `PunesSystems.cs` | `AresSystems.cs` | the ONE table: systems `nes` (.nes .unf .unif .nsf .nsfe) and `fds` (.fds .qd, BIOS `disksys.rom` required); platforms NES (+ hacks, + Famicom if approved) -> nes, FDS -> fds; `ByExtension` (.fds/.qd among NES games -> fds); header citing section 2 |
| `PunesPlugin.cs` | `BsnesPlugin.cs` / `AresPlugin.cs` | ctor (desk, log, row injection), catalogue row (`punes64.exe`, per-platform extensions, `AutoExtract = false`, FDS `RequiredBiosFile = disksys.rom`), claiming by exe name, `IsPlatformSupported`, versions, `IsUpdateAvailable` three answers, `InstallEmulator`, `CreateEmulator` (one platform row per platform, never touching others), BIOS per platform, no RA, `PrepareCore` |
| `PunesBuild.cs` | `RaSnes9xBuild.cs` | pinned v0.111 x86_64 D3D9: the release URL, SHA-256 `EB97...0E2B` checked before extraction, `releases/latest` redirect read to notice a newer tag (logged, offered only when a new pin is shipped), stamp `lbip-punes-build.txt`, exe MD5 |
| `Archives.cs` | `RaSnes9x\Archives.cs` | release zip extracted over the install (flat zip, user's `puNES.cfg`/`input.cfg`/`recent.cfg` are not in it); a game archive unpacked when needed (section 6) |
| `PunesCfg.cs` | `RaSnes9xConf.cs` | editor of `puNES.cfg` and `input.cfg` in place: `[group]`, `key=value`, CRLF, UTF-8 no BOM; writes only when changed, never while puNES runs; creates the empty `puNES.cfg` (portable) |
| `PunesLaunch.cs` | `BsnesLaunch.cs` | `IsPunesExe`, `Running`, the line (`--fullscreen=yes|no`, `--mode=...`, other per-game options before the ROM, never `--shortcut`), the `=`-in-path guard, archive handling (`RomFor`), the FDS BIOS refusal |
| `PunesAhk.cs` | `BsnesAhk.cs` | Exit = `WinClose ahk_exe punes64.exe`; Save/Load State send puNES's bound keys; Esc script if decided |
| `PunesSettings.cs` | `BsnesSettings.cs` | options: Fullscreen (default yes), region mode, size, filter, shader, palette, integer scaling, stretch, hidden GUI, background pause... each "as set in puNES" by default |
| `PunesSettingsPage.cs` | `BsnesSettingsPage.cs` | simple page: install/version, full screen, FDS BIOS status/import |
| `PunesGameMenu.cs` | `BsnesGameMenu.cs` + `SuperZsnesSettingsPage` | per-game window: groups "Region", "Video", "Full screen"..., FDS group (disk side at reset) for fds games only, multi-game rule, "As set in puNES (<value>)" read from `puNES.cfg`, live command-line summary |
| `PunesSaveDesk.cs` | `BsnesSaveDesk.cs` | the desk overrides |
| `PunesSaves.cs` | `RaSnes9xSaves.cs` | `prb\<base>.prb` (cartridge), `diff\<base>.ipf/.ipq` (FDS disk, kind "disk"), states `save\<base>.p00..p0B` one row per slot (`Slot`), `OwnExpectedDir` = `<exe dir>\prb` / `\save` / `\diff` |
| `PunesSavePaths.cs` | `BsnesSavePaths.cs` (reduced) | nothing to set (folders fixed) and nothing beside the game to migrate; only the FDS write mode check |
| `PunesStateThumbs.cs` | `RaSnes9xStateThumbs.cs` | PNG at offset u32@8 to EOF -> `<state>.png` |
| `PunesFirmware.cs` | `AresFirmware.cs` | `LbipBiosImport` of `disksys.rom` (and optionally `gamegenie.rom`) into `<puNES>\bios\` |
| `Log.cs` | Bsnes | `%LOCALAPPDATA%\lb-integrations-plugins\punes.log` |
| `src\Probe\PunesCheck.cs` | `AresCheck.cs` / `RaSnes9xCheck.cs` | systems table vs the source's extension list, cfg editor round trip (CRLF, no BOM, unknown keys), state thumbnail decode on a copy of a real `.p00`, latest-version read, platform rows only added |

## 14. Open questions for Mehdi

1. **Per-platform save folders**: puNES has no setting for them (fixed `prb\`, `save\`, `diff\`, `screenshot\` beside
   its exe). Accept puNES's own folders (nothing lands beside the game anyway, ids carry the system), or swap
   per-platform junctions (`prb` -> `Saves\<platform>\prb`...) before each launch? Recommended: puNES's own folders.
2. **Hotkeys**: every pack key is taken by a puNES default (F2 slot-, F6/F7 PAL/NTSC, F11 hard reset, Esc menubar). Keep
   puNES's (F1 save / F4 load / F2-F3 slot), or rebind its defaults to the pack's (an exception to "only if empty")?
3. **`Nintendo Famicom`** (LiteBox/RomM name) claimed as a NES platform: yes/no?
4. **Archives**: pass a single-ROM archive as is only when its inner name equals its own name, unpack otherwise (saves
   named after the game's file) - agreed?
5. **FDS write mode `original file`** (writes into the `.fds` beside the game): force `diff file`, or leave the user's
   choice and say it?
6. **D3D9 or OpenGL** build (OpenGL has the GLSL shaders; D3D9 is the Windows default)?
7. **ROM path with `=`**: puNES cannot open it (measured). Refuse with a notice, or hand puNES a copy/hard link under
   `Unpacked\` (the save would then follow that name)?

---

## 15. Stage 2, build A (18/10): what is done

The plugin exists in `src\Punes` (assembly `Punes`, namespace `LbIntegrations.Punes`, PackName `Nixx-puNES`, PluginId
`44210e28-d6be-44e4-a69d-4780f6546aee`, manifest 1.2.2). `dotnet build src\Punes\Punes.csproj -c Release` is green and the
ILRepack merge gives `src\Punes\bin\Release\merged\Punes.dll`. One public type derives from `EmulatorPlugin`
(`PunesPlugin`, directly).

| File | What it does now |
|---|---|
| `PunesSystems.cs` | the ONE table: systems `nes` (.nes .unf .unif .nsf .nsfe) and `fds` (.fds .qd, `disksys.rom` required, aliases `FdsBios.bin`, `fds_bios.bin`); platforms `Nintendo Entertainment System` (+ .fds/.qd by extension), `Nintendo Famicom Disk System`, `Nes - Super Mario Bros. Hacks`, `Nintendo Famicom` (question 3 taken as "yes" - the task names the pack's Famicom names; one line to remove if Mehdi says no). `SystemFor`: the file's extension first (the one inside an archive), else the platform's default, else nothing - puNES itself picks by file, there is no `--system` |
| `PunesPlugin.cs` | constructor (log, row injection), catalogue row (one `LbCatalogPlatform` per platform with its own extensions, FDS `RequiredBiosFile = disksys.rom`, `AutoExtract = false`), claiming by exe name (`punes64.exe`, `punes.exe`, `punes64_p.exe`, `punes_p.exe`), AutoHotkey fields filled only when empty, `IsPlatformSupported` (never recommended), versions, `IsUpdateAvailable` (empty path true / not ours false / ours older true), `InstallEmulator`, `CreateEmulator` (one row per platform, IsDefault only where no emulator is default yet, DefaultPlatform not set), BIOS per platform, no RA, `PrepareCore` |
| `PunesBuild.cs` | pinned v0.111 x86_64 D3D9, SHA-256 checked before extraction, developer copy in `%LOCALAPPDATA%\lb-integrations-plugins\dev\`, exe MD5s of both 0.111 builds, stamp `lbip-punes-build.txt`, `/releases/latest` asked once per process off the host's thread (a newer tag only logged and shown on the page) |
| `Archives.cs` | flat zip extracted over the install; `puNES.cfg`, `input.cfg`, `recent.cfg` kept if a zip ever ships them; `dip.cfg` / `nes20db.xml` replaced (puNES's own data) |
| `PunesCfg.cs` | editor of puNES.cfg / input.cfg in place (UTF-8 no BOM, CRLF, nothing dropped, written only when changed); `IsPortable`, `DataDir` (exe folder when portable, else `%LOCALAPPDATA%\puNES`), `EnsurePortable` (empty puNES.cfg beside the exe, unless the user has a `%LOCALAPPDATA%\puNES\puNES.cfg`) |
| `PunesLaunch.cs` | exe names, `Running`, `RomFor` (archive as is only when it holds ONE file, .zip/.7z, named after the archive; else unpacked into `<puNES>\Unpacked\<name>-<hash>\<name><ext>`; two games none named after the archive / no game: refused), `Givable` ('=' refused), the line (plugin options FIRST, then the host's line untouched, so an option on the user's platform line wins), `BindHotkeys` |
| `PunesFirmware.cs` | where puNES looks (cfg path, exe folder, game folder, `bios\`), `disksys.rom` copied into `bios\` from the other emulators' system/bios/firmware folders (siblings under `Emulators\` and the library's), an alias copied under puNES's name |
| `PunesSettings.cs` | options table, each a puNES switch: Screen (page, default full screen), Region `--mode`, Size, IntegerScaling `--int-scl-fullscreen`, PixelAspect, Filter, Shader, Palette, VSync, Interpolation, BackgroundPause, SaveBattery (nes only), Rewind - all but Screen "As set in puNES" by default; `CfgHas` reads puNES.cfg for "As set in puNES (value)" |
| `PunesSettingsPage.cs` | simple page: install / version / newer tag, keys and save folders, Screen for every game, FDS BIOS status + "Look for it now" |
| `PunesGameMenu.cs` | `GameMenu` (icon = the exe's, `LbipMenuIcon`) and the per-game window: tabs Game / Picture / Emulation, the multi-game rule (All selected games (N), groups, Various (n of N)), "As set in puNES (value)", Reset, live "N option(s) added to the command line, before the ROM path:" |
| `PunesAhk.cs` | Running note, Exit = `WinClose, ahk_exe <the entry's exe>`, Save F1 / Load F4 (puNES's defaults) |
| `Log.cs` | `%LOCALAPPDATA%\lb-integrations-plugins\punes.log` |
| `docs\help\punes.html` | the user guide (embedded as `help.html`) |

### Facts measured during build A (lab `G:\LB1326\lbip-punes-lab\run3`, fresh copy of the D3D9 zip + an empty puNES.cfg)

- `--fullscreen=yes --fullscreen-window=yes --mode=ntsc Zelda` -> window `-3,-26,1923,1043` titled "puNES_p (NTSC, 2x, ...)": a
  MAXIMIZED window with its frame, not a borderless one - so `--fullscreen-window` is not offered. After WM_CLOSE (exit 0)
  puNES.cfg still said `fullscreen=no`, `fullscreen in window=no`, `preferred mode=auto`.
- `--fullscreen=no --mode=pal --size=3x --filter=hq2x --palette=sony --rewind-minutes=disabled Zelda` -> title "puNES_p (PAL, 3x,
  Hq2X, Sony CXA2025AS US)"; after the exit puNES.cfg still had `size window=2x`, `filter=none`, `palette=ntsc`,
  `rewind minutes=15`, `preferred mode=auto`: options on the line apply and are NOT kept.
- Read (cmd_line.cpp, case 't'): `--stretch-fullscreen` sets `cfg_from_file.scale = !value` (the window size) - a bug of 0.111,
  not offered. Values are matched EXACTLY against settings.h's `opt_*` snames (objSettings.cpp `val_to_int`): the filter is
  `none` (the help text's `nofilter` would not parse), the palettes are `pal ntsc sony mono green frbnst frbyuv raw`.
- Read (settings.h:1139): the puNES.cfg key is spelled `use integer scaliung in fullscreen`.

### Probe checks (`src\Probe\PunesCheck.cs`, wired in `src\Probe\Program.cs` after the `--rasnes9x-*` lines), all green 18/10

```
Probe.exe <Punes.dll> --punes-systems G:\LB1326\lbip-punes-lab\src-v0.111
Probe.exe <Punes.dll> --punes-cfg G:\LB1326\lbip-punes-lab\cfg-after-run1.cfg
Probe.exe <Punes.dll> --punes-line
Probe.exe <Punes.dll> --punes-archives
Probe.exe <Punes.dll> --punes-hotkeys G:\LB1326\lbip-punes-lab\run1\input.cfg
Probe.exe <Punes.dll> --punes-bios
Probe.exe <Punes.dll> --punes-versions G:\LB1326\lbip-punes-lab\d3d9\punes64.exe
Probe.exe <Punes.dll> --punes-e2e
Probe.exe <Punes.dll> --punes-fetch                                   (network: downloads the pinned zip)
Probe.exe <Punes.dll> --punes-shot <out.png> [--emu <punes64.exe>]    (draws the page and a game's window)
```

### Not done in build A (for build B)

- **Saves in LaunchBox**: no `SaveDesk` yet (`PunesSaveDesk.cs`, `PunesSaves.cs`), no `SupportsSaveManagement`. Plan unchanged
  (section 13): ids `emu = "punes"`, `Sys = "nes" | "fds"`, `prb\<name>.prb`, states `save\<name>.p00..p0B` one row per slot
  (`GetPotentialSaveSlots` 0..11), FDS `diff\<name>.ipf` / `.ipq`, all under `PunesCfg.DataDir(exe)`. The constructor gets
  `_desk = new SaveDesk(...)` as its FIRST line then (the class is already `partial`).
- **State thumbnails** (`PunesStateThumbs.cs`: the PNG from offset u32@8 to EOF -> `<state>.png`).
- **Nothing beside the game**: nothing to move - puNES writes nothing there (fixed folders, measured stage 1); the one case is
  FDS `write mode=original file` (logged by `WarnWriteMode`, question 5).
- **Per-platform save folders**: not possible by a setting (question 1); puNES's own `prb\ save\ diff\` kept.
- **Esc to quit**: not bound (puNES's "toggle menubar in fullscreen" holds it); `LbipAhk.EscapeScript` not used - question 2.
- **Platform rows on an existing entry** (reinstall): not added - adding rows to `args.ExistingEmulator` during the Edit
  Emulator window's update cannot be measured without LaunchBox; only `CreateEmulator` adds rows (never removes any).
- **FDS with a real disk**: still unmeasured (no dump on this machine); the BIOS refusal and the copy are probe-checked only.
- **Vendored copy** of the zip in `vendor\punes\`: not added (25 MB; the asset is unchanged since 2024). The lead's decision.

### For the lead (integration, brief section 17)

- `deploy-dev.ps1`: `'Punes' = @{ Folder = 'Nixx-puNES'; Old = @() }`, and `Punes` in the `-All` list. A dev copy of
  `punes-0.111-x86_64-win-d3d9.zip` in `%LOCALAPPDATA%\lb-integrations-plugins\dev\` is used when present (optional).
- `build-release.ps1`: a `$Stage` row for `Punes` / `Nixx-puNES`.
- `src\Installer`: folder `Nixx-puNES`, assembly `Punes.dll`, its manifest; `What()`: "NES, Famicom, Famicom Disk System".
- `src\NixxSaves\CatchAllClaims.cs` `Names`: `punes64.exe`, `punes.exe`, `punes64_p.exe`, `punes_p.exe`.
- `README.md` row; nothing new merged (Lib.Harmony, SharpCompress as every plugin).
- No change in any `Shared.*` folder.
