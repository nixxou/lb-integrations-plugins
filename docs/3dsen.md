# Nixx-3DSen: research notes (stage 1) and build notes (stage 2)

3dSen PC (Geod Studio) turns NES games into 3D voxel dioramas. It is a commercial Unity game sold on Steam: the
plugin never downloads it, it **asks** the user whether to use the Steam install or a folder they pick (Mehdi's
install flow). These notes are what was measured on 18/10/2026, and how; the plan of the plugin is at the end
(section 12). Stage 2 starts from here and must not re-derive anything below. **What stage 2 built, and what
is left, is section 13.**

Lab: `G:\LB1326\lbip-3dsen-lab\`
- `emu\` - the 3dSen copy used for every oracle run (extracted from `G:\3DSen\3dsenpc-win.zip`, never `G:\3DSen`
  itself);
- `roms\` - a copy of `Zelda II - The Adventure of Link (Europe).nes` (SHA-256
  `53E12C61B3CB9DE99754A4DB84C62FCF0931F3AF9168A23D21F15F7CDA6CE536`) and `Zelda II zipped.zip`;
- `decomp\acs\` - `Assembly-CSharp.dll` decompiled with ilspycmd 9.1 (`ilspycmd -p`), `decomp\inc\` - `InControl.dll`;
- `locallow-before-2026-10-18\` + `registry-before-2026-10-18.reg` - the user data and the Unity registry key as
  they were BEFORE the tests (restored after them, hash-checked); `locallow-after-tests\` - what the tests left;
- `t2-id73-windowed.png`, `t3-r-path.png` - screenshots of two runs;
- `tools\` - the measurement scripts: `oracle.ps1` (start, wait, screenshot, WM_CLOSE, list what was written),
  `oracle-keys.ps1` (same, posting key presses), `inputcfg.py` (`input.cfg` reader/rewriter), `savcfg.py`
  (`sav.cfg` + `.sav` header decoder, thumbnail to PNG). Python run with `-I`.

Every fact below says where it comes from: **[src]** = read in the decompiled `Assembly-CSharp.dll` of 1.1.2
(file named), **[run]** = measured by running the exe, **[file]** = read in a file on disk, **[web]** = Steam's
public API.

---

## 1. Identity, version, sources

| What | Value | How |
|---|---|---|
| Product | 3dSen PC, Geod Studio, Steam app **1147940** | [src] `Nes3D/Info.cs` `AppId = 1147940u`; [web] `store.steampowered.com/api/storesearch/?term=3dsen` |
| Version | **1.1.2** | [src] `Info.Version = "1.1.2"` (a C# `const`: a literal field in the metadata, readable without loading the assembly); the About text `VRUIController.Start` says the same |
| Exe | `3dSen.exe`, **32-bit** Unity 2019.4.41f2 player, at the root of the install | [file] `UnityCrashHandler32.exe`, `3dSen_Data\Plugins\x86\`; `Player.log` "Initialize engine version: 2019.4.41f2" |
| Exe file version | `2019.4.41.7021524` = **Unity's**, not 3dSen's: useless for version detection | [file] `VersionInfo` |
| Copy used | `G:\3DSen\3dsenpc-win.zip` SHA-256 `081E0E912C23FD40CE3C728FA2ECE9DAF7B870160CEE25976DD7873223A70D80`; `3dSen.exe` `71FAA02AAA4BCDD054A736428C0E146FB64C2E7BB7A23AA65E64045230782870`; `Assembly-CSharp.dll` `CB26E810A85F1B5C27BFE546CBBDE5C8A2C31F22E7AA45898314FD089A515548`; files dated 05/11/2025 | [file] |
| Steam builds | public branch buildid `20680754` updated 2025-11-05 (= the date of our copy's files); a `beta` branch `25759945` updated 2026-10-07 (not measured) | [web] `api.steamcmd.net/v1/info/1147940` |
| Steam install | `installdir` **`3dSen`**, Windows launch entry `3dSen.exe`, no arguments | [web] same |
| Steam news | "Version 1.1" announced 2025-11-04 (the last version note); community profiles announced after (Metal Storm 2026-03, Totally Rad / Krion Conquest 2026-05) as `.3dn` files on itch.io | [web] `ISteamNews/GetNewsForApp` |

Provenance of the local copy: the `Player.log` left in the user data shows it was first run from a folder named
`G:/Geod.Studio.3dSen.PC.v1.1.2.x86-BTCR/...`, i.e. not from a Steam install. Its `steam_api.dll` is Valve-signed
(Authenticode valid, Steam Client API 04.28.51.07). Nothing in these notes depends on where it came from, but
**stage 2 must re-run the launch oracle (section 4) on a real Steam install** before release: see open question Q1.

The VR edition is another product: app **954280**, `installdir` `3dSenVR`, exe **`3dSenVR.exe`** (64-bit),
launched by Steam with `-vrmode OpenVR`, or `-vrmode None` "In Desktop Mode"; a Looking Glass DLC (2463890,
`LGP\3dSenLGP.exe`) [web]. Its code shares `VRUIController` with the PC build (the class name says it), so the same
`-id=` launch very likely works, but nothing of it was measured: **out of scope**, a later addition (another exe
name, another data folder - presumably `LocalLow\Geod Studio\3dSenVR`, unmeasured).

---

## 2. What 3dSen runs: one system, and only profiled games in 3D

**Systems: the NES only, iNES files only.** [src] `Nes3D/Engine.cs` `Engine()` builds the core list with ONE core,
`Nes3D.DefaultCore.Core` (a C# NES); `Core` enum lists `Nes3D, Bizhawk, Fceumm` and `fceumm_core.dll` ships in
`Plugins\x86`, but neither is ever constructed (the list holds the default core alone; `SettingManager.Core` is
taken modulo the list's count). `DefaultCore/Cartridge.cs` `LoadCart` throws "Rom has an corrupted header" unless the
file starts with `NES\x1A`: **no Famicom Disk System** (`.fds` starts `FDS\x1A` or raw sides; there is no FDS mapper
nor BIOS), no UNIF. Mappers implemented (`DefaultCore/Mapper*.cs`): 0, 1 (+SUROM), 2, 3, 4, 5, 7, 9, 10, 11, 13, 15,
16, 18, 22, 28, 30, 32, 34, 41, 64, 66, 71, 87, 101, 225, 228 (+ VRC2/4, MMC2/4 helpers). PAL/NTSC: header byte 10
(`region = nesHeader[10] & 3`) applied when the "Auto TV mode" setting is on (`Engine.LoadRom`).

Mehdi's rule (a plugin claims every platform its emulator runs) therefore gives:

| LaunchBox platform | Claimed | Why |
|---|---|---|
| `Nintendo Entertainment System` | yes, the default | the only system (Famicom is one of its alternate names in LaunchBox's base, not a platform) |
| `Nes - Super Mario Bros. Hacks` (LiteBox) | yes | iNES files; they play under the "Super Mario Bros." profile (id 37) only if the user picks it (section 3) |
| `Nintendo Famicom Disk System` | **no** | measured above: no loader, no BIOS |
| Vs. System / PlayChoice-10 / Dendy | no | no such LaunchBox platform in `LaunchBox.Metadata.db` (queried 18/10: names like `%Vs%`, `%PlayChoice%`, `%Dendy%` return nothing NES-related); no Vs. palette/DIP code in the core |

No BIOS anywhere. Saves are not per platform (section 6), so there is no per-system folder question.

**Only profiled games render in 3D.** A "game" in 3dSen is a **profile** (built-in or custom), not a ROM:
[src] `Nes3D/GameDB.cs` builds `gameDB` from `ProfileDB` (a ScriptableObject in the assets: the built-in profiles,
data loaded from the Addressables bundle `StreamingAssets\aa\StandaloneWindows\defaultprofiles_assets_all_*.bundle`)
plus every `CustomProfiles\*.3dn` of the user data folder (`MetaProfile`: protobuf, field 7 `Id`, 8 `Title`, 9 `Maker`,
10 `GameDev`, 11 `Des`; field 1 must be `"3dn"`). The user binds ONE ROM file to each profile; **nothing checks that
the ROM is the right game** (no CRC/hash anywhere in `Nes3D*`: grep for crc/md5/sha1/hash/checksum returns nothing).
A wrong ROM simply plays with the wrong 3D data. Profile 47 **"2D Mode"** is the plain 2D emulator, for any ROM.

The list is **readable**: `rom.json` (section 5) holds every profile id and title once 3dSen has run once, and the
custom ones are `.3dn` files whose id/title are protobuf fields. The 101 built-in profiles of 1.1.2 ([file]
`rom.json` written by 1.1.2, 101 items):

| id | title | id | title | id | title |
|---|---|---|---|---|---|
| 1 | Arkanoid | 35 | Road Fighter | 69 | Adventure Island |
| 2 | Balloon Fight | 36 | Super Bat Puncher | 70 | Snow Brothers |
| 3 | Batman | 37 | Super Mario Bros. | 71 | Jackie Chan's Action KungFu |
| 4 | Battle Kid | 38 | Tetris - Nintendo | 72 | Depths |
| 5 | Battle City | 39 | Tetris - Tengen | 73 | Zelda II: The Adventure of Link |
| 6 | Bomberman | 40 | Urban Champion | 74 | Castlevania II: Simon's Quest |
| 7 | Castlevania | 41 | Wrecking Crew | 75 | Solomon's Key |
| 8 | Circus Charlie | 42 | Yie Ar Kungfu | 76 | Fire 'n Ice |
| 9 | Contra | 43 | Bubble Bobble | 77 | Final Fantasy |
| 10 | Digdug | 44 | Wild Gun Man | 78 | Dragon Warrior I |
| 11 | Donkey Kong | 45 | Legend Of Zelda | 79 | Dragon Warrior II |
| 12 | Donkey Kong 3 | 46 | Zooming Secretary | 80 | Felix the Cat |
| 13 | Donkey Kong Jr | 47 | 2D Mode | 81 | Gradius |
| 14 | Dr Mario | 48 | City Connection | 82 | Burger Time |
| 15 | Duck Hunt | 49 | Micro Mages | 83 | Life Force |
| 16 | Elevator Action | 50 | TwinBee | 84 | Ninja Gaiden III |
| 17 | Excitebike | 51 | Pinball | 85 | Blaster Master |
| 18 | Galaga | 52 | Flappy Bird | 86 | Super Spike V'ball |
| 19 | Gyromite | 53 | Flappy Bird FPV | 87 | Punch Out!! |
| 20 | Hogan's Alley | 54 | Shatterhand | 88 | Lunar Pool |
| 21 | Ice Climber | 55 | Super Mario Bros. 3 | 89 | Moai-kun |
| 22 | Journey To Silius | 56 | Goonies | 90 | Binary Land |
| 23 | Shadow Of Ninja / Kage | 57 | Pooyan | 91 | Flippull |
| 24 | Kungfu / Spartan X | 58 | Chip & Dale Rescue Ranger | 92 | Nuts & Milk |
| 25 | Legacy Of The Wizard | 59 | Duck Tales | 93 | Quarth |
| 26 | Legend Of Kage | 60 | Adv of Lolo | 94 | Puzznic |
| 27 | Lode Runner | 61 | Antarctic Adventure | 95 | Palamedes |
| 28 | Mappy | 62 | Clu Clu Land | 96 | Yoshi |
| 29 | Mario Bros. | 63 | Spy Hunter | 97 | Flappy |
| 30 | Mega Man 1 | 64 | Mega Man 3 | 98 | Tetris - Famicom |
| 31 | Mega Man 2 | 65 | Ghosts N Goblins | 99 | Dragon Warrior III |
| 32 | Metroid | 66 | Adv Of Lolo 2 | 100 | Tiny Toon Adventures |
| 33 | Pacman | 67 | Adv Of Lolo 3 | 101 | Jackal |
| 34 | Popeye | 68 | Duck Tales 2 | | |

The titles are 3dSen's own spellings ("Digdug", "Pacman", "Adv of Lolo", "Kungfu / Spartan X", "Dragon Warrior I"),
so a LaunchBox title does not match them as is: stage 2 needs a normalised comparison (`LbipTitleNormalizer`) plus a
small alias table, and the game's window must let the user pick the profile when no match is found (section 12).

---

## 3. How a game is started

**Command line: `3dSen.exe -id=<profile id>`.** [src] `Nes3D.UI/VRUIController.cs` `ProcessArgs` (called at the end
of `Start`): the first argument starting with `-id=` selects `GameDB.Ins[id]`; an unknown id logs
"Profile with id ... couldn't be found!" and stays in the menu; a profile with an **empty `romPath`** opens the
"Select ROM" file browser (`FlashAndRunGame`), otherwise `RunGame` -> `Emulator.OpenGame` -> `Engine.LoadGameProfile`,
which reads `currentGame.romPath`. **The ROM path is never on the command line: it is the profile's `romPath` in
`rom.json`.** So a launch = write the ROM's absolute path into the profile's `romPath` in `rom.json` while 3dSen is
not running, then start `3dSen.exe -id=<id>`.

[src] `UIController.ProcessArgs` reads `--r=<rom path>`, but that controller sets `EngineMode = ROM` and is the 3dSen
Maker UI: **[run]** `3dSen.exe "--r=G:\...\Zelda II ... .nes"` opens the normal menu and loads nothing
(`t3-r-path.png`). The PC build's scene uses `VRUIController` (desktop mode when `VrRuntime == 0`).

[run] Oracle, `G:\LB1326\lbip-3dsen-lab\emu\3dSen.exe -id=73 -screen-fullscreen 0 -screen-width 1280
-screen-height 720`, with profile 73's `romPath` set to the lab's Zelda II: the game starts by itself in 3D, no
click, `Player.log` "Load profile Zelda II: The Adventure of Link" (`t2-id73-windowed.png`). Same with `romPath`
pointing at `Zelda II zipped.zip`: loads, no error in the log.

**Archives: 3dSen opens `.zip` and `.7z` itself**, the **first entry** only: [src] `Engine.LoadRom(string)`:
`.zip` -> `lzip.entry2Buffer(path, lzip.ninfo[0])`, `.7z` -> `lzma.decode2Buffer(path, lzma.ninfo[0])`, anything
else read raw and must be iNES. The ROM file browser filter is `nes|zip|7z` (`FlashAndRunGame`). [run] zip measured
above; `.7z` read in the source only. An archive whose first entry is not the ROM fails ("Invalid rom file."): the
plugin should check the first entry and unpack otherwise (section 12). `AutoExtract = false`.

**Steam DRM: none active in 1.1.2.** [src] `SteamManager.cs` would call `SteamAPI.RestartAppIfNecessary(1147940)`
(which quits the process and relaunches through Steam, dropping our arguments), but `SteamManager` is only created by
its `Instance` getter or a scene object, and nothing in `Assembly-CSharp` references it (grep: no use outside its own
file). [run] six direct launches of the exe with Steam running and logged in: no relaunch, no Steam window, the game
ran each time. To be confirmed on the Steam build (Q1); the fallback, if Steam does intercept, is
`steam.exe -applaunch 1147940 -id=<id> ...` (not measured).

**Window / full screen: Unity's own switches, and Unity REMEMBERS them.** [run] `-screen-fullscreen 0
-screen-width 1280 -screen-height 720` -> a 1280x720 window (1296x759 with the frame). After that run the registry
key `HKCU\Software\Geod Studio\3dSen` held `Screenmanager Fullscreen mode = 3` (windowed), `Width 0x500`,
`Height 0x2d0`, `Use Native = 0`, and a later plain start would have been windowed. `-screen-fullscreen 1` alone then
gave a full-screen window but at the remembered 1920x1080 size, not the monitor's 2560x1440. **So the plugin always
passes the full set**: `-screen-fullscreen 1 -screen-width <monitor w> -screen-height <monitor h>` for full screen,
`-screen-fullscreen 0 -screen-width <w> -screen-height <h>` for a window. The user's original values were
`Fullscreen mode = 1` (FullScreenWindow), native resolution. Unity's `-window-mode` / `-monitor` were not tried.
3dSen also has a "Full Screen" toggle in its menu (`UIController.FullScreenClick`, `Screen.SetResolution`).

Other switches: none of 3dSen's own (`GetCommandLineArgs` is read only by the two `ProcessArgs`; `-smoke-test` is a
UIWidgets test). `boot.config`: `single-instance=` empty, `vr-enabled=0`.

**Quit: WM_CLOSE is clean.** [run] five runs, `PostMessage(WM_CLOSE)` to the main window -> exit code 0 in
252-313 ms, after writing `input.cfg`, `rom.json`, `sav.cfg`, `setting.cfg` (all with the same millisecond).
[src] `Emulator.WantsToQuit` -> engine `Quit` -> `SettingManager.Save`; `GameDB`, `SaveStateDB`, `Gamepads` save in
`OnApplicationQuit`. The window title is `3dSen` (class: Unity's), the process `3dSen`.

---

## 4. Where everything is written: the user data folder, never the install

`Application.persistentDataPath` = **`%USERPROFILE%\AppData\LocalLow\Geod Studio\3dSen`** ([src] `Info.Awake`
`DataPath = Application.persistentDataPath`; company/product from `3dSen_Data\app.info` = `Geod Studio` / `3dSen`).
It is fixed by Unity from those two names: **no setting, no switch moves it**, and it is **shared by every copy of
3dSen of the Windows user** (two installs = one data folder). The install folder is never written ([run] the
install and the ROM folder unchanged after every run). Nothing is written beside the game either.

| File | Format | Written | Holds |
|---|---|---|---|
| `rom.json` | JSON (Unity `JsonUtility` pretty print via `Nes3D.Utils.JsonHelper`), UTF-8 **no BOM**, **LF**, 4-space indent, `{"Warning": "...", "Items": [{"id","romPath","title","lastTime"}]}` | at quit, whole (`GameDB.SaveConfig` in `OnApplicationQuit`) and when a ROM is picked | one item per profile: the bound ROM path, the last play time (Unix seconds) |
| `rom.cfg` | protobuf, older format | read only when `rom.json` is absent, **deleted** when `rom.json` exists (`GameDB.Awake`) | - |
| `setting.cfg` | protobuf-net `SettingManager` (fields 1-38: volume, speed, render mode, shadows, skybox, TV mode, fov...) | at quit, whole | general settings - the plugin leaves them alone |
| `input.cfg` | protobuf-net `Dictionary<string,string>` (repeated field 1 = {1: key, 2: value}); values = base64 of InControl `PlayerActionSet` data | at quit, whole (`Gamepads.OnApplicationQuit`) | keys and pad bindings (section 7) |
| `sav.cfg` | protobuf-net `List<SaveConfig>` | at each save state and at quit | the 36 + 1 state slots (section 6) |
| `sav\<n>.sav`, `sav\quick.sav` | state files | at each save state | section 6 |
| `CustomProfiles\*.3dn` | protobuf `MetaProfile` + 3D data | when the user imports a profile | community 3D profiles |
| `CoverArt\<title>_3dsen_<id>.png` | PNG | when cover art is set | the menu's tile pictures |
| `Export\` | 3D scene export | **F3** (hard-coded, `VRUIController.HotKeyProcess`) | - |
| `Player.log`, `Player-prev.log` | Unity log | every run | useful lines: `Load profile <title>`, `Save state was created with Default core and with format 2` |
| registry `HKCU\Software\Geod Studio\3dSen` | Unity PlayerPrefs | at quit | window mode / resolution (section 3), `LastPath` |

`rom.json` edited by the plugin: [run] replacing `"romPath": ""` of id 73 by the JSON-escaped absolute path
(backslashes doubled, LF kept, no BOM) was read correctly, and 3dSen's rewrite at quit kept it and set `lastTime`.
Any item missing from `rom.json` is recreated empty by 3dSen ([src] `GameDB.AddGame`), and items whose profile no
longer exists are dropped (`GameDB.Awake`). **Never write it while 3dSen runs: it is rewritten whole at quit.**

---

## 5. Battery saves: there are none

[src] `DefaultCore/Memory.cs`: the cartridge RAM (`saveRam`, 8 KB) lives in memory only, is **zeroed at every ROM
load** (`Memory.Reset`) and is only ever serialised inside save states; nothing in `Nes3D.DefaultCore` opens a file
except `Cartridge.LoadCart(string)` for reading. [run] Zelda II started, then WM_CLOSE: no `.sav`/`.srm` anywhere
(not beside the ROM, not in the data folder); only the five config files above changed. So **a battery-backed game
(Zelda II, Final Fantasy, Dragon Warrior...) loses its in-game save when 3dSen quits**; the only way to keep progress
is a save state. The plugin's help page and the game window must say so plainly. The task's oracle "WM_CLOSE writes
the battery save" therefore has a negative answer, by design of 3dSen.

---

## 6. Save states: 36 global slots + one quick slot, the picture inside `sav.cfg`

[src] `Nes3D/SaveStateDB.cs`, `Nes3D/SaveConfig.cs`, `Nes3D/Engine.cs`:
- 36 numbered slots (`MaxCount = 36`, ids `"1"`..`"36"`) and `"quick"`, **shared by all games**: each slot records
  the profile it belongs to (`gameId`). Files: `<data>\sav\<slot>.sav` (`SaveConfig.SlotId` setter).
- `sav.cfg` = protobuf-net `List<SaveConfig>`: field 1 `SlotId` (string), 2 `gameId` (string, profile id; empty =
  free slot), 3 `texture` = `Texture2DSur` {1 width, 2 height, 3 `TextureFormat` (3 = RGB24), 4 raw bytes},
  4 `lastTime` (Unix seconds). [run] after a quick save: 37 records, the quick one `gameId 73`, texture 256x192 fmt 3,
  147 456 bytes, file 147 844 bytes.
- At start, a slot whose profile no longer exists is **deleted** (file removed, `gameId` cleared) (`SaveStateDB.Awake`).
- A `.sav` file = BinaryWriter string `"__Default"` (the core's name, format 2) then a protobuf `SaveStateWrapper`
  {1 core data, 2 scripting floats, 3 movie, 4 camera}. The core data embeds **the whole ROM** (PRG and CHR, plus
  the header, `romPath` and `romName`: `Cartridge.SaveState`), so a state is self-contained: loading one does NOT read
  the ROM (`Engine.LoadStateProfile`). [run] `quick.sav` 283 992 bytes for a 262 160-byte ROM; `romName` inside =
  `Zelda II zipped.nes` (an archive's name with `.nes`), `romPath` empty.
- **Thumbnail**: the slot's picture is `sav.cfg` field 3, raw RGB24, rows bottom-up (Unity), 256x192. A save made
  from the menu stores the screenshot taken at pause (`VRUIController.Pause` -> `ScreenRecorder.Capture`); a save
  made by the **QuickSave hotkey stores no picture**: `SaveStateProfile(QuickSave)` passes a null texture, and
  `AssignSS(null)` keeps a fresh texture: [run] all 147 456 bytes = `0xCD` (uniform grey). So: the emulator's own
  picture when it is not uniform, else a window capture (`LbipStateThumbs.Watch`).
- Quick save / load [run]: with QuickSave bound to F2 and QuickLoad to F4 in `input.cfg` (section 7), `WM_KEYDOWN F2`
  posted to the window wrote `sav\quick.sav` and `sav.cfg` 2.6 s later; in a new session `F4` loaded it
  (`Player.log` "Save state was created with Default core and with format 2"). Pad: Select + RotDown saves,
  Select + RotUp loads (`HotKeyProcess`; also written on the menu's help panel).
- The old `Engine.QuickSave` path (`<data>\quick.sav`, not in `sav\`) is reached only by the unused 3dSen Maker UI.

Consequences for the plugin's saves (section 12): states are the ONLY saves; one row per slot whose `gameId` is the
game's profile; restoring = writing `sav\<slot>.sav` + its `sav.cfg` record (protobuf, fields above) while 3dSen is
not running. Since slots are shared between games, restoring into a slot of another game must ask
(`SlotRestore.MayOverwrite`).

---

## 7. Keys and their storage

[src] `Nes3D/Gamepads.cs`: 19 actions (`ActionIdent`) and keyboard defaults (`DefKeys`): Left/Right/Up/Down =
arrows, A = X, B = Z, Select = Space, Start = Return, RotLeft F5, RotRight F6, RotUp F7, RotDown F8, ZoomIn F9,
ZoomOut F10, **Menu = Escape**, RollBack = Comma, FastForward = Period, **QuickSave = none, QuickLoad = none**. Plus
**F3 = export the 3D scene** (hard-coded). [run] the menu's input tab shows the same (`t3-r-path.png`).

Storage [src] `Keyboard.cs`, `Gamepads.SaveConfig`; InControl `PlayerActionSet.Save`, `PlayerAction.Save`,
`KeyCombo.Save`: `input.cfg` entries `Keyboard0 Binding`, `Keyboard1 Binding`, `Gamepad0 Binding`, `Gamepad1 Binding`
(the gamepad ones empty strings here). Each keyboard value = base64 of: `"BIND"`, u16 version 2, i32 action count,
then per action: .NET `BinaryWriter` string name (7-bit length + UTF-8), i32 binding count, per binding i32 type
(2 = `KeyBindingSource`) + `KeyCombo` = i32 include size, u64 include keys (one `InControl.Key` per byte), i32 exclude
size, u64 exclude keys. `InControl.Key` values (enum order in `InControl/Key.cs`): `None 0x00`, `Escape 0x0D`,
`F1 0x0E` .. `F12 0x19`, `X 0x3B`, `Z 0x3D`, `Return 0x48`, `Comma 0x49`, `Period 0x4A`, `Space 0x4C`, arrows
`0x53`-`0x56`. [file] the default file has `QuickSave` / `QuickLoad` = one key binding of size 1, key `None` (0x00).

[run] `tools\inputcfg.py` (parse + exact round trip of both layers, then rewrite) set
`QuickSave = F2 (0x0F)` and `QuickLoad = F4 (0x11)`: 3dSen used them (section 6) and its own rewrite at quit kept
them. So the pack's F2 / F4 can be bound **in 3dSen's format**, only when the binding is `None` and no other action
holds the key (F2 and F4 are free by default). The pack's other keys collide: **F6/F7 = camera rotation** (and there
is no slot concept for the quick slot), **Esc = 3dSen's menu** (pause + menu; it does not quit), F11 is free but
3dSen has no full-screen action to bind. Exit: `WinClose, ahk_exe 3dSen.exe` (measured clean, section 3). Whether
Esc should quit (pack) or keep opening 3dSen's menu (needed to save into numbered slots, change settings) is
question Q3.

---

## 8. Version detection

- The exe's version resource is Unity's (section 1): not usable.
- `3dSen_Data\Managed\Assembly-CSharp.dll`, type `Nes3D.Info`, literal field `Version` (= `"1.1.2"`): readable with
  `System.Reflection.Metadata` (Constant table) without loading the assembly. This is `GetCurrentVersion`.
- A Steam install also has `steamapps\appmanifest_1147940.acf` `buildid` (20680754 for 1.1.2 public, [web]).
- No update is ever offered by us (commercial; Steam updates it): `IsUpdateAvailable` = true only for the empty path
  (the Download button of Add Emulator, which is where the "Steam or folder?" question is asked), false otherwise.

---

## 9. Steam detection (measured on this machine, values not recorded - memory "no-account-data-in-repo")

- Steam root: `HKCU\Software\Valve\Steam` `SteamPath` (forward slashes, lower case: `c:/program files (x86)/steam`)
  and `SteamExe`. [run] present.
- Logged in: `HKCU\Software\Valve\Steam\ActiveProcess` `ActiveUser` (DWORD, the 32-bit account id, non-zero when a
  user is logged in) and `pid` (the running `steam.exe`, checked alive). [run] both set while Steam ran.
  `config\loginusers.vdf` of this Steam client has **no `MostRecent` key** (keys seen: `AccountName`, `PersonaName`,
  `RememberPassword`, `WantsOfflineMode`, `SkipOfflineModeWarning`, `AutoLogin`, `Timestamp`): do not rely on
  `MostRecent`; `AutoLoginUser` is in the registry too.
- Libraries: `steamapps\libraryfolders.vdf` (KeyValues text, tabs): `"libraryfolders" { "0" { "path" "...", "apps"
  { "<appid>" "<size>" ... } } }`; the library holding 1147940 lists it under `apps`, then
  `<path>\steamapps\appmanifest_1147940.acf` gives `installdir` (expected `3dSen`, [web]) and `StateFlags`
  (4 = fully installed - Steam's convention, not measured here), the exe = `<path>\steamapps\common\3dSen\3dSen.exe`.
  [file] here: one library, 3dSen **not installed** (manifests present: 228980, 993090, 1290270, 2417000).
- Not installed: `steam://install/1147940` asks Steam to install it. **Not measured** (it would open Steam's UI in
  Mehdi's session); what Steam shows when the account does not own the game is unknown (Q2). Ownership cannot be read
  reliably offline.

---

## 10. Things checked that do not apply

- RetroAchievements: none (no RA code in `Assembly-CSharp`; `SupportsRetroAchievements` = false).
- BIOS: none (no FDS, section 2).
- Cheats / screenshots folders: no setting; `Export\` (F3) is the only other output.
- Exe-name collision: `3dSen.exe` is unique in the pack (the VR edition is `3dSenVR.exe`).
- Multiple instances: `single-instance=` empty in `boot.config` (not measured); the plugin refuses a launch while one
  runs anyway (it must write `rom.json` first).

---

## 11. Open questions for Mehdi

- **Q1** The copy measured is not a Steam install. Run the launch oracle once on the Steam build (`3dSen.exe -id=73`
  started directly, Steam running): does it start the game, or does Steam take over (then the plugin starts it with
  `steam.exe -applaunch 1147940 -id=...`, which also counts play time in Steam)?
- **Q2** `steam://install/1147940` on an account that does not own 3dSen: Steam's store page or an error? Can be
  tried by Mehdi on the plugin's "Install with Steam" path.
- **Q3** Esc: keep 3dSen's menu on Esc (needed for the 36 numbered slots and settings) and quit with the LaunchBox
  exit key only, or map Esc to quit like the rest of the pack (the menu then has no keyboard key unless rebound)?
- **Q4** Saves live in `LocalLow\Geod Studio\3dSen`, outside the install, and cannot be moved by 3dSen. Leave them
  there (proposed), or make that folder a junction to `<install>\Saves\` (moves them with the install, but a Steam
  install folder is Steam's, and two installs would fight over the one junction)?
- **Q5** Games without a 3D profile: refuse with a notice (proposed: Mehdi's rule "only offer it on games it can
  render in 3D"), or allow "2D Mode" when the user picks it in the game's window?
- **Q6** No battery saves (section 5): a notice the first time a battery game (iNES header flag 6 bit 1) is launched?

---

## 12. Plan of the plugin (one plugin, `src\ThreeDSen`, assembly `ThreeDSen`, `Nixx-3DSen`)

Mapped onto the reference files (brief section 3). `emu` id for saves: `"3dsen"`.

| File | Model | What it does here |
|---|---|---|
| `ThreeDSen.csproj` | `Bsnes.csproj` | AssemblyName `ThreeDSen`, RootNamespace `LbIntegrations.ThreeDSen`, help `docs\help\3dsen.html`; also `System.Reflection.Metadata` is in the framework (no package) |
| `manifest.json` | Bsnes | new PluginId, Name `Nixx-3DSen`, HomePageUrl the Steam store page, Credits Geod Studio |
| `ThreeDSenPlugin.cs` | `BsnesPlugin.cs` | the one `EmulatorPlugin` (direct); platforms `Nintendo Entertainment System` (+ `Nes - Super Mario Bros. Hacks`); extensions `.nes; .zip; .7z`; `AutoExtract = false`; no BIOS, no RA; claim by exe name `3dSen.exe`; `IsUpdateAvailable` empty path -> true else false; `GetInstallableVersions` -> one entry "3dSen (Steam or your copy)"; `InstallEmulator` = the choice dialog (below), never a download |
| `ThreeDSenSteam.cs` | new | Steam root / logged-in user / libraries / `appmanifest_1147940.acf` (KeyValues reader), install dir, `steam://install/1147940`, `-applaunch` fallback (Q1) |
| `ThreeDSenInstall.cs` | new (replaces `*Build.cs`) | the dialog "Use 3dSen from Steam / Choose the folder": Steam found+installed -> its path; Steam not installed or not logged in -> say so, offer `steam://install`; folder -> must hold `3dSen.exe` + `3dSen_Data\Managed\Assembly-CSharp.dll` with `Nes3D.Info.Version`; the entry is created on that path (no copy, no stamp: `GetCurrentVersion` reads the version constant) |
| `ThreeDSenProfiles.cs` | new (like `AresSystems.cs`: the one table) | the 101 built-in profiles of 1.1.2 (section 2) as a fallback, merged with `rom.json` items and `CustomProfiles\*.3dn` (protobuf fields 7/8); matching a LaunchBox game: the game's own choice (window) > normalised title + aliases; no match -> refused with `LbipNotice` (Q5) |
| `ThreeDSenRomJson.cs` | `BsnesBml.cs` (in-place editor) | set one item's `romPath`, keep everything else byte for byte (LF, no BOM, 4 spaces, the `Warning` line); add the item if missing; refuse while 3dSen runs |
| `ThreeDSenInput.cs` | `RaSnes9xConf.cs` role | `input.cfg` reader/writer (both layers, exact round trip as measured): QuickSave F2 / QuickLoad F4 only when bound to `None` and the key is free |
| `ThreeDSenLaunch.cs` | `BsnesLaunch.cs` | exe name, `Running`, the line: `-id=<id>` + `-screen-fullscreen 0/1 -screen-width -screen-height` (always the full set, section 3); archives: pass `.zip`/`.7z` when the first entry is the ROM, else unpack to `<install>\Unpacked\...` like `RaSnes9xLaunch.RomFor` (for a Steam install: Q4's folder question applies - prefer a folder under `%LOCALAPPDATA%`? decide at stage 2) |
| `ThreeDSenAhk.cs` | `BsnesAhk.cs` | Exit = `WinClose, ahk_exe 3dSen.exe`; SaveState = F2, LoadState = F4; Esc per Q3 |
| `ThreeDSenSaves.cs` | `RaSnes9xSaves.cs` (one row per slot) | rows from `sav.cfg` + `sav\*.sav` where `gameId` = the game's profile; slots 1-36 + quick; `SaveId` {Emu "3dsen", Kind "state", Id = profile id or rom name (decide before release), Slot}; restore = write `.sav` + the `sav.cfg` record (protobuf writer); shared slots -> `SlotRestore.MayOverwrite`; no battery rows |
| `ThreeDSenSavCfg.cs` | new | `sav.cfg` protobuf reader/writer (fields of section 6), round trip exact |
| `ThreeDSenStateThumbs.cs` | `RaSnes9xStateThumbs.cs` | `<slot>.sav.png` from the `sav.cfg` texture (RGB24 bottom-up) unless uniform (`0xCD`: hotkey saves), else `LbipStateThumbs.Watch` capture |
| `ThreeDSenSavePaths.cs` | `BsnesSavePaths.cs` | nothing to redirect (no folder setting; nothing beside the game - measured); kept minimal or dropped, per Q4 |
| `ThreeDSenSettings.cs`, `ThreeDSenSettingsPage.cs` | Bsnes | page: the install (Steam or folder, version), screen (full screen / window size); per game: the 3D profile (dropdown, "Automatic (<matched title>)"), full screen override; summary line "N option(s) added to the command line" |
| `ThreeDSenGameMenu.cs`, `ThreeDSenSaveDesk.cs`, `Log.cs` | Bsnes | as the models |
| `src\Probe\ThreeDSenCheck.cs` | `BsnesCheck.cs` | profile table vs a `rom.json` copy; `rom.json` edit round trip; `input.cfg` and `sav.cfg` round trips on copies of the lab files (`locallow-after-tests`); version constant read from the lab's `Assembly-CSharp.dll`; Steam vdf/acf parsing on fixtures; platform rows only added |

Shared code needs: none foreseen (KeyValues/protobuf readers are local to the plugin).

---

## 13. Stage 2, build A (18/10/2026): what is done, what is open

Branch `nes-3dsen`. `dotnet build src\ThreeDSen\ThreeDSen.csproj -c Release` is green, the ILRepack merge passes
(`bin\Release\merged\ThreeDSen.dll`), the probe check `--3dsen` is green.

### Files (`src\ThreeDSen\`)

| File | What it does |
|---|---|
| `ThreeDSen.csproj`, `manifest.json` | as Bsnes's; AssemblyName `ThreeDSen`, RootNamespace `LbIntegrations.ThreeDSen`, **PluginId `c466f77e-dde3-46ad-942b-636b904dbe66`**, Name `Nixx-3DSen`, help `docs\help\3dsen.html` |
| `ThreeDSenPlugin.cs` | the one `EmulatorPlugin` (direct, no base in between - checked by the probe); catalogue row + platform rows (`Nintendo Entertainment System`, `Nes - Super Mario Bros. Hacks`, `.nes; .zip; .7z`, AutoExtract false, never recommended); claim by `3dSen.exe`; AHK fields filled when empty; versions; `InstallEmulator` = the question; `PrepareCore` -> `Prepare` (host-free, the probe calls it) |
| `ThreeDSenSteam.cs` | Steam root (registry `SteamPath`), logged in (`ActiveProcess` `ActiveUser` + a live `pid`), `libraryfolders.vdf` / `appmanifest_1147940.acf` (KeyValues reader), `StateFlags & 4`, `steam://install/1147940` |
| `ThreeDSenInstall.cs` | the version (`Nes3D.Info.Version` read from `Assembly-CSharp.dll`'s metadata with System.Reflection.Metadata, never loaded), the folder check (`3dSen.exe` there or in its one sub-folder; the VR exe named and refused), the question window (own STA thread, TopMost, no owner - see its comment) |
| `ThreeDSenProfiles.cs` | the 101 built-in profiles (= rom.json of 1.1.2, checked by the probe id for id), rom.json's profiles first; the matching: the game's choice > whole title / file name, normalised as LaunchBox's CompareName, + an alias table; ambiguous = none; 2D Mode never automatic |
| `ThreeDSenRomJson.cs` | rom.json reader (Utf8JsonReader, keeps byte offsets) and in-place editor of ONE `romPath` (the rest byte for byte: LF, no BOM, no final newline; re-read before writing; temp + move); adds a missing item; makes the file when absent; REFUSES when only `rom.cfg` exists (3dSen would delete it) |
| `ThreeDSenInput.cs` | input.cfg (both layers: protobuf map + InControl BIND) reader/writer; QuickSave F2 / QuickLoad F4 only when all their bindings are `None` and no action of either keyboard set holds the key; anything not understood = file untouched |
| `ThreeDSenLaunch.cs` | exe name, `AnyRunning` (any 3dSen: one shared data folder), the data folder (`FOLDERID_LocalAppDataLow` + `Geod Studio\3dSen`), the screen switches (always the full set; primary monitor via `EnumDisplaySettings` with the thread per-monitor DPI aware), the command line (game path and our old switches out, the user's tokens kept), archives (`RomFor`) |
| `ThreeDSenAhk.cs` | Running note, Exit `WinClose, ahk_exe 3dSen.exe`, SaveState F2, LoadState F4 |
| `ThreeDSenSettings.cs` | options `Screen` (full / window / asis, default full), `WindowSize` (1280x720 default ... 2560x1440); per game `Profile` (any profile id) |
| `ThreeDSenSettingsPage.cs` | the Nixx page (simple): the install(s) and versions, Steam's state, the data folder, the keys as read in input.cfg, the screen for every game |
| `ThreeDSenGameMenu.cs` | `GameMenu` contract + per-game window: profile ("Automatic (<matched title>)", 2D Mode, the list), screen, window size; the multi-game rule (LbipSelection, Various, Reset); the command line live at the bottom |
| `Log.cs` | `%LOCALAPPDATA%\lb-integrations-plugins\3dsen.log` |
| `docs\help\3dsen.html` | the user guide |

### Decisions taken in this build (from the measurements above)

- **Launch line** `-id=<id> -screen-fullscreen 1 -screen-width W -screen-height H` (or `0` + the window's size; nothing
  for "As 3dSen last had it"). The game path is taken OFF the host's line (NewCommandLine replaces the whole line).
- **Archives**: a `.zip` whose FIRST entry is a `.nes` is given as is (measured in stage 1); a `.7z` (read in the source
  only, never measured) and any other `.zip` are unpacked to `<3dSen folder>\Unpacked\<name>-<md5 of its path>\<name>.nes`.
  For a Steam install that folder is inside Steam's `common\3dSen` (a cache; Steam's uninstall removes it). Q4 may move it.
- **Games without a profile** (Q5): refused with a notice that says how to choose one; 2D Mode is offered in the game's
  window, never automatic. Matching uses the WHOLE title only (the text before ":" would make "Batman: Return of the
  Joker" the profile "Batman").
- **3dSen running** (any copy): launch refused - rom.json is rewritten whole at its quit.
- **rom.json absent but rom.cfg present**: refused (making rom.json makes 3dSen delete rom.cfg).
- **Esc** (Q3): not mapped; Esc keeps opening 3dSen's menu (its 36 slots). F6 / F7 / F11 not bound (camera keys / no action).
- **Save folders / "nothing beside the game"**: nothing to do - 3dSen writes nothing beside the game (stage 1, measured)
  and its data folder cannot be moved (Q4 open). No SaveDesk in this build.

### Measured in stage 2

- **[run] end to end, 18/10**: the probe's `--3dsen-prepare` (the plugin's own `Prepare`) wrote into the REAL data folder
  (`rom.json`: profile 73 -> the lab's Zelda II `.nes`; `input.cfg`: QuickSave F2 / QuickLoad F4) and printed
  `-id=73 -screen-fullscreen 1 -screen-width 1920 -screen-height 1080`; `tools\oracle.ps1` started the lab's 3dSen with
  it: window `0,0 1920x1080`, Player.log "Load profile Zelda II: The Adventure of Link", the game on screen in 3D
  (`t6-stage2-full.png`); WM_CLOSE -> exit 0 in 312 ms; 3dSen's own rewrite kept our `romPath` (lastTime set) and F2 / F4.
  The data folder and the registry key were backed up first (`locallow-before-stage2\`, `registry-before-stage2.reg`),
  what the run left is kept in `locallow-after-stage2\`, then both were RESTORED and checked identical (12 files by
  SHA-256, the registry export byte-identical).
- **input.cfg**: the C# writer, on a copy of 3dSen's default file, produces BYTE FOR BYTE the file 3dSen itself wrote
  after the stage-1 F2 / F4 test (`locallow-after-tests\input.cfg`) - a probe check.
- **The primary monitor here**: `EnumDisplaySettings` 1920x1080, as `Win32_VideoController` and `Screen.PrimaryScreen`
  say (this machine has virtual display adapters; Unity's remembered 2560x1440 comes from another display setup). DPI
  virtualisation of that call is guarded against (thread per-monitor aware), not measured.
- Steam on this machine (`--3dsen-steam`): found, logged in, 3dSen not installed - as stage 1 said.

### Not measured / open (for build B and Mehdi)

- **Saves (build B)**: SaveDesk + `IOwnSaves` (one row per `sav.cfg` slot whose `gameId` = the game's profile; slots
  1-36 + quick; restore = the `.sav` + its `sav.cfg` record; `SlotRestore.MayOverwrite`), `ThreeDSenSavCfg.cs` (protobuf
  reader/writer), thumbnails (`sav.cfg` texture RGB24 bottom-up; uniform 0xCD = a hotkey save -> `LbipStateThumbs.Watch`),
  the save id (profile id vs rom name - decide before release), the battery notice (Q6).
- Q1 (direct exe launch on a real Steam build), Q2 (`steam://install` for a non-owner), Q3 (Esc), Q4 (data folder, and the
  Unpacked folder of a Steam install), Q5 (implemented as refusal + a choice in the game's window).
- The AHK `Send {F2}` (SendInput) reaching 3dSen: only PostMessage WM_KEYDOWN was measured (stage 1).
- `-window-mode` / `-monitor`: not used, not tried. A second monitor as the primary: not tried.
- The question window: built and compiled, never shown in LaunchBox (Mehdi's click: Add Emulator > Nixx-3DSen > Download).
- `.3dn` custom profiles are read through rom.json only (3dSen lists them there once it has run after their import); a
  `.3dn` imported and never seen by 3dSen since is not offered until then.

### For the lead (integration, brief section 17)

- `deploy-dev.ps1` `$Pack`: `'ThreeDSen' = @{ Folder = 'Nixx-3DSen'; Old = @() }` and `ThreeDSen` in the `-All` list (no
  vendored archive).
- `build-release.ps1` `$Stage`: the ThreeDSen row.
- `src\Installer\Payload.cs`: folder `Nixx-3DSen`, assembly `ThreeDSen.dll`, files dll / manifest / `Contract`;
  `Installer.csproj` two EmbeddedResource; `InstallerForm.What()`: "3dSen PC (NES games in 3D, from your Steam install or
  your copy)".
- `src\NixxSaves\CatchAllClaims.cs` `Names`: `3dSen.exe`.
- `src\Probe\Program.cs`: ALREADY wired on this branch, just before the bsnes lines: `--3dsen [<lab>]` (default
  `G:\LB1326\lbip-3dsen-lab`), `--3dsen-steam` (read only), `--3dsen-prepare <3dSen.exe> --rom <game> --title <title>`
  (WRITES 3dSen's real data folder - oracle use only, back it up first).
- Shared code: **no change**.

## 14. Mehdi's decisions (19/10/2026) and build B

- **Q3 Esc**: Esc quits, as in the whole pack: the running AutoHotkey script closes 3dSen's window on Esc, in its window
  only (`LbipAhk.EscapeScript`). 3dSen's **Menu moves to F1** in `input.cfg`. Fast forward moves to **L** (held: 3dSen
  reads it with `Pressed` every frame, `Engine` re-arms 30 fast frames). Same rule as puNES / FCEUX: an action is set only
  when unbound, on 3dSen's default (Menu Esc, FastForward Period) or on the pack's key; never one key on two actions.
- **Q4 saves**: left in `LocalLow\Geod Studio\3dSen` - listed and restored there, no junction.
- **Q5 no profile**: the game starts in **2D Mode** (profile 47) instead of being refused. 2D Mode is every such game, so
  its states are told apart by the ROM inside each state (`Cartridge.romName`, `ThreeDSenSavCfg.RomNameOf`; a zip that
  3dSen opened itself gives `<zip name>.nes`).
- **Q6 battery**: no notice.

Build B (saves): `ThreeDSenSaves` (IOwnSaves through SaveDesk), `ThreeDSenSavCfg` (sav.cfg protobuf, read / written back
byte for byte), `ThreeDSenStateThumbs` (3dSen's own picture, else a capture). One row per slot holding the game's profile
(0 = quick, 1..36), id `{"emu":"3dsen","kind":"state","by":"profile","id":"<profile>","slot":n}`. Restore writes the slot's
file and that one `sav.cfg` item (every other byte for byte), asks before taking another game's slot, and is refused while
3dSen runs. Probe `--3dsen` "save states": round trip, the encoder against 3dSen's own quick item, the ROM inside a
state, 2D Mode's filter, a restore into slot 5 and its delete - all on copies of 3dSen's own files.

## 15. The in-process plugin (BepInEx, 19/10/2026)

Mehdi asked for the model of SUPER ZSNES: a BepInEx plugin inside 3dSen, to pass as much as possible on the command line
and to make Esc ask before quitting. His choices: **portable mode**, **F6 / F7 pick the slot (Quick, 1-36)**, **Esc twice**.

### What is installed, where

- BepInEx **5.4.23.5, x86, Unity Mono** (3dSen 1.1.2 is Unity 2019.4, Mono, 32-bit), downloaded at install time from
  BepInEx's GitHub release (`BepInEx_win_x86_5.4.23.5.zip`) and checked against its SHA-256
  `37651c79e40d6f909572a4f461ac25350bb3ef8fe7fbd29f1aa8791a33b84c82` before extraction. LGPL: the pack carries none of it.
  In 3dSen's folder: `winhttp.dll` (Doorstop, a new file - 3dSen ships none), `doorstop_config.ini`, `.doorstop_version`,
  `BepInEx\core\`, `BepInEx\config\BepInEx.cfg` (silent: no console, no disk log; written only when absent).
- The pack's plugin `BepInEx\plugins\ThreeDSen.BepInEx.dll` (`tools\3dsen-bepinex`, netstandard2.0, HarmonyX patches
  resolved by name), embedded in `ThreeDSen.dll` at build time (`deploy-dev.ps1` / `build-release.ps1` take
  `-ThreeDSenDir` and `-BepInEx5Core` to compile it). Each launch puts it back when its bytes differ; no network at launch.
- Installed by **Download** (after Steam or a folder was chosen), or by **Install now** on the Nixx page (per 3dSen
  folder, with **Remove**). Turned off on the Nixx page: BepInEx is taken away at the next launch; the portable data stays.

### What the plugin does (its switches, all `--nixx-*`, stripped from any earlier line)

| switch | effect |
|---|---|
| `--nixx-data=<dir>` | `Nes3D.Info.DataPath` (and its export path) set in `Info.Awake`: rom.json, input.cfg, setting.cfg, custom profiles, cover art there |
| `--nixx-states=<dir>` | `SaveStateDB.SavFolder` set before `SaveStateDB.Awake`, and `ConfigPath` -> `<dir>\sav.cfg`: the slots and their index side by side |
| `--nixx-esc`, `--nixx-quit-confirm=on/off` | Esc: "Press Esc again to quit" on screen, quits on a second press within 2.5 s (or at once with off) |
| `--nixx-slots=on/off` | F6 / F7 pick the slot (Quick, 1..36, said on screen), F2 saves into it with 3dSen's own capture, F4 loads it (refused, on screen, when empty or another game's) |
| `--nixx-set:<SettingManager property>=<value>` | one of 3dSen's settings for this session: applied after `SettingManager.Load`, swapped out around `Save` so `setting.cfg` keeps what 3dSen had; a value changed in 3dSen's own menu during the session is kept |
| `--nixx-log` | its log, `nixx.log` in the data folder |
| `--nixx-dump-options` | the catalogue of 3dSen's settings (how the values below were measured) |

### Portable mode (the plugin on and in the folder)

- Data: `<3dSen folder>\portable`. States: `<3dSen folder>\Saves\<platform>\States` (`sav.cfg` and `<slot>.sav` together;
  `ThreeDSenSavCfg.StateDir` takes a folder named `States` as holding the slots itself).
- First launch: what `LocalLow\Geod Studio\3dSen` has is **copied** in (rom.json, input.cfg, setting.cfg, CustomProfiles,
  CoverArt; its states into the NES's States folder) - never moved, LocalLow stays every other copy's.
- The saves code goes by install and by platform: listing per game platform, a state live only in its own root of this
  install, restore into the platform's root of the install the restore is for (`SaveForward.ViaEmulator`, else the game's
  emulator), delete frees the slot in the `sav.cfg` beside the file.
- Without the plugin everything is as in section 14 (LocalLow, AutoHotkey's Esc closes the window).

### Keys with the slots (input.cfg, `ThreeDSenInput.SlotsTable`)

3dSen's QuickSave / QuickLoad taken off F2 / F4 (the plugin reads those itself), its camera (F5-F10 by default, F6 / F7
being the pack's slot keys) moved to the keypad: RotLeft Pad4, RotRight Pad6, RotUp Pad8, RotDown Pad2, ZoomIn Pad+,
ZoomOut Pad-. Menu F1, fast forward L as before. Slots off again (or the plugin removed): F2 / F4 back on QuickSave /
QuickLoad and the camera back on F5-F10 when it is still on those keypad keys (`PlainTable`); a key the user chose is
never moved.

### Esc and AutoHotkey

One running script for both: on Esc it reads the folder of the 3dSen it sees (`WinGet ProcessPath`); with `winhttp.dll`
and the plugin there it sends Esc on (the plugin asks), else it closes the window. The script of build B is replaced when
found word for word (`ThreeDSenAhk.Superseded`).

### A game's own 3dSen settings (its window, "As set in 3dSen" until chosen)

Measured with `--nixx-dump-options` (values = what 3dSen's own handlers set): TV system NTSC / PAL / Dendy; Speed -8..+8;
Volume 0..1 (by 0.1); menu music; field of view 45..95 (70 + 5 per step); graphics Low / Medium / High; anti-aliasing
None / FFAA / SMAA; frame rate 60 / 90 / 120; rendering Direct / Render texture; texture resolution Native, 2/3, 1/2, 1/3,
1/4; background Default / Color / Gradient / Fantasy; TV frame None / TV 1-3; shadows; fast render; mirror.

### Measured in the lab (`G:\LB1326\lbip-3dsen-lab\emu-bep`, a copy of 3dSen 1.1.2)

BepInEx loads the plugin; data and states redirected; Esc twice quits with exit code 0; a forced Volume leaves
`setting.cfg` identical; an F2 save has a real picture; F4 loads it. Not yet seen: a numbered slot picked with F7 (the
window was not in front), AutoHotkey's Esc passed through to the plugin, the whole thing from LaunchBox - Mehdi's test.
Probe `--3dsen` adds: the slot keys and back, the portable paths, the first-launch copy, the plugin's switches on the
line, a restore and delete in a `States` folder.


## 16. Cartridge saves through the plugin (19/10/2026)

Mehdi pointed at a first BepInEx fix of 3dSen's missing battery saves ("Battery Save Fix": three Harmony hooks on
`Nes3D.DefaultCore` - `Core.LoadRom` postfix reading `<rom>.srm`, `Core.CloseRom` prefix writing it, `Memory.Reset`
replaced so `saveRam` is not zeroed). Its reading of 3dSen holds ([src] 1.1.2): `Memory.saveRam` (8 KB, `$6000-$7FFF`) is
zeroed by `Memory.Reset`, `Engine.LoadGameProfile` resets right after `LoadRom`, and only a state ever carries it;
`Cartridge.save_ram_present` is the iNES battery bit. What it misses, and what the pack's plugin (`tools\3dsen-bepinex\Battery.cs`,
`--nixx-battery=<folder>`) does instead:

- **Written at every way out.** `CloseRom` is reached only through `Engine.Stop` - switching games, or quitting from
  3dSen's own menu (`Engine.Quit`). Closing the window (WM_CLOSE: AutoHotkey's Exit, LaunchBox) and the pack's Esc
  (`Application.Quit`) never call it: the first fix lost the game there - the pack's usual way out. The plugin also writes
  in `OnApplicationQuit`, and every 2 s when the RAM changed (a 3dSen killed or crashed loses at most that).
- **Kept through a reset only for a battery game**, around `Memory.Reset` (prefix copy, postfix back) instead of replacing
  its body: other games keep 3dSen's zeroing.
- Written only when the bytes differ from the file, atomically (`.tmp` then moved); a game that never wrote its RAM (all
  zero, no file yet) gets no file.
- The file: `<3dSen folder>\Saves\<platform>\<game file name>.srm` (the ROM 3dSen plays is the file's name, an archive's own
  name with `.nes`), beside the platform's `States`. The pack's rule, nothing beside the game.
- LaunchBox: one "Cartridge save" row (GameSaveGame, chip `SRM`, id `{"emu":"3dsen","kind":"save","by":"name","id":"<rom name>"}`);
  active in this install's folder of the game's platform; restored under the game's file name today, refused while 3dSen
  runs (it would write its own at quit) and without the plugin (3dSen would never read it).
- A state loaded brings the battery RAM it was made with (`Memory.LoadState`): that is what is written next, as in every
  emulator.

Probe `--3dsen`: `--nixx-battery` on the line, the file name, a restore byte for byte, the refusal without the plugin. Not
yet measured in 3dSen: a battery game saved, 3dSen closed by its window, the `.srm` read back (Mehdi's test, with the
build of section 15).
