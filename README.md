# lb-integrations-plugins

Emulator integration plugins for LaunchBox and LiteBox, for emulators LaunchBox does not cover.

LaunchBox ships 11 official "\<Emulator\> LaunchBox Integration" plugins — RetroArch, Dolphin, MAME,
PCSX2, ScummVM, BigPEmu, Xemu, Azahar, CEMU, RPCS3, DuckStation. Its metadata database knows 35
emulators, so **24 had no integration at all**: no download and update, no BIOS checks, no
RetroAchievements, no save management. The plugin catalogue is closed to third parties (every entry
is published by Unbroken Software), so these are installed by hand.

| Plugin | Emulator | Platform | Status |
|---|---|---|---|
| `src/Ppsspp` | PPSSPP | Sony PSP | download / update, BIOS, RetroAchievements, launch, save management |
| `src/Xenia` | Xenia (canary) | Microsoft Xbox 360 | download / update, launch fixes, save management |
| `src/Flycast` | Flycast | Sega Dreamcast, Sega Naomi, Sega Naomi 2, Sammy Atomiswave | download / update, BIOS, RetroAchievements, launch, save management |
| `src/MelonDs` | melonDS | Nintendo DS | download / update, BIOS, DS/DSi mode, per-title DSi NAND, save management (GPL-3.0) |
| `src/NoGba` | no$gba | Nintendo Game Boy Advance, Nintendo DS | download / update, BIOS, **raw save format**, save management |

## Building

```
dotnet tool restore
dotnet build src\Ppsspp\Ppsspp.csproj -c Release
```

No LaunchBox installation is needed: the SDK is committed under `vendor\` (see `vendor\README.md`
for which version and why).

A Release build runs a merge step (ILRepack, pinned in `dotnet-tools.json`) that folds every
dependency into the assembly and marks its types `internal`, producing
**`bin\Release\merged\Ppsspp.dll`** — one file whose reference table holds exactly one
non-framework entry, `Unbroken.LaunchBox.Plugins`. That is the file to install; `deploy-dev.ps1`
prefers it automatically. See `THIRD-PARTY.md` for what is folded in and under which licences.

### The melonDS NAND library

Optional, and only useful with DSiWare. It needs a melonDS checkout of the tag you run, and the C++
toolchain that ships with Visual Studio - `cl.exe`, `cmake.exe` and `ninja.exe` all live under the VS
install rather than on `PATH`:

```
git clone --depth 1 --branch 1.1 https://github.com/melonDS-emu/melonDS.git ..\melonDS

cmake -S tools\melonds-nand -B build\nand -G Ninja ^
      -DMELONDS_SOURCE_DIR=..\melonDS -DCMAKE_BUILD_TYPE=Release
cmake --build build\nand
```

That builds two things, from one set of objects:

- **`melonds-nand.dll`** - the C door the plugin calls through P/Invoke. It travels with the plugin's
  build output and `deploy-dev.ps1` copies it beside the DLL.
- **`melonds-nandtool.exe`** - the same operations from a command line, for doing them by hand:
  `list`, `exists`, `import`, `delete`, `export-save`, `import-save`, `export-file`, `import-file`,
  `remove-file`, and `walk`.

`walk` writes every file in the image with its size and SHA-1, sorted by path. Comparing two of those
compares two NANDs **by file**, which is the only comparison that means anything: a FAT directory
entry carries the wall clock (`get_fattime`, `ffsystem.c:107`) and the allocator places clusters in
the order operations happened, so two images built from identical inputs differ in raw bytes while
holding exactly the same files - measured, four differing 64 KB blocks and not one byte of content.
Nothing upstream offers this; melonDS walks directories in three places and exposes none of them.

What that buys, measured on one real session of one title:

```
base NAND                    -> + install        7 entries added, nothing else
                                                 (ticket, the title tree, the 6.2 MB .app, its tmd,
                                                  and an empty public.sav)
rebuilt reference            -> played NAND      4 files differ, 16 KB each:
                                                   shared1/TWLCFG0.dat, TWLCFG1.dat   system settings
                                                   shared2/launcher/wrap.bin          the DSi menu
                                                   title/00030017/484e4145/.../private.sav
```

and putting those four files back into the rebuilt reference makes it **identical to the played NAND,
file for file**. A whole session is 80 KB of difference inside a 240 MB image.

Both are one file each, linked against the static runtime, importing nothing but `KERNEL32`.

**`tools/melonds-nand` and `src/MelonDs` are GPL-3.0**, unlike the rest of this repository: the
library is built from melonDS's source, and the plugin loads it into its own process. See
`THIRD-PARTY.md` and `src/MelonDs/LICENSE.md`. The other three plugins are unaffected.

### The DSiWare metadata index

Also optional, also only useful with DSiWare. Installing a title into a NAND needs its `.tmd`, and
most DSiWare dumps are the `.nds` alone. The plugin can carry an index of them:

```
.\tools\pack-tmd-library.ps1 -From "D:\wherever\your\tmds\are"
dotnet build src\MelonDs\MelonDs.csproj -c Release
```

The packer walks a folder recursively and files every TMD by what it says about ITSELF - title id at
`0x18C`, revision at `0x1DC`, content SHA-1 at `0x1F4` - so no naming convention is assumed and the
sets in circulation can be thrown at it as they are. It writes `src\MelonDs\tmd-library.bin`, which
the build embeds when it is there and skips when it is not. **It is not in this repository**: it is
Nintendo's signed metadata, and whether a copy belongs in a published build is a decision to take on
purpose. Without it the plugin asks Nintendo's update server instead, which still answers.

The file is 0.6 MB for about 1700 titles, and its layout is written out at the top of the packer. A
sorted table of 36-byte records is stored uncompressed, so finding a title inflates nothing; the
metadata itself sits in deflated blocks of sixteen, of which a lookup inflates exactly one - 8 KB,
held for as long as it takes to copy 520 bytes out of it, then dropped.

## Installing

```
.\deploy-dev.ps1                      # builds, copies into G:\LB1326, verifies by hash
.\deploy-dev.ps1 -Plugin Xenia
.\deploy-dev.ps1 -LbRoot 'G:\LB'
```

It deploys the DLL **and** `src\<Plugin>\manifest.json` into `Local\Plugins\`, and verifies both by
hash. The manifest is not optional there: LaunchBox 14 loads nothing without one and says nothing
about it - a plugin can sit in a plugin folder for a day, never run, write no log line, and simply
have no install option in the Add Emulator window.

Or by hand. LaunchBox 14 reads three plugin roots and the choice matters:

| root | for | our plugins |
|---|---|---|
| `System\Plugins\` | Unbroken's own, `SourceKind: "LaunchBox"` | **no** - the core refuses a manifest whose declared origin does not match the root, with *"The plugin manifest does not match its managed install location"* |
| `Local\Plugins\` | third-party, managed, `SourceKind: "Local"` | **yes**, with a `manifest.json` beside the DLL |
| `Plugins\` | the legacy root, no manifest needed | works, but is not the managed location |

So: `<LaunchBox>\Local\Plugins\<Name>\<Plugin>.dll` plus its `manifest.json`. Before LaunchBox 14,
`Plugins\` is the only option.

Plugins are loaded once at start-up, so restart the frontend. Under LiteBox, tick the plugin in
**Options ▸ Plugins** the first time; it is not auto-enabled, deliberately, because the name that
would auto-enable it (`... LaunchBox Integration`) impersonates Unbroken's own.

## Testing without a frontend

`src\Probe` is a host stand-in. It loads a plugin, finds its `EmulatorPlugin`, and calls the
read-only half of the contract, printing what comes back — seconds instead of a frontend restart.

```
dotnet run --project src\Probe -- src\Ppsspp\bin\Release\Ppsspp.dll --emu "D:\Emulators\PPSSPP\PPSSPPWindows64.exe"
```

It writes nothing unless you ask it to. These modes do — the last three only inside the temp folder:

```
--inject-ra-test
    writes RetroAchievements credentials into the emulator you named with --emu.

--save-unit <PSP\SAVEDATA> --disc-id ULUS10064 [--expect-hash <md5>]
    extracts that save the way a backup would, checks the layout, prints the hash RomM and
    Argosy compute for it, and - when --emu is given - restores it and re-extracts to prove
    the round trip is lossless. The restore DELETES and rewrites that disc id's folders.

--flycast
    the whole Flycast contract against a forged install: disc ids, the three save regimes,
    BIOS, and how an emulator entry gets its platforms.

--hotkeys
    writes Flycast's keyboard mapping on a forged install and checks that none of Flycast's
    own default keys were lost in the process.

--melonds-real --melonds-base <nand.bin> --rom <dsiware.nds>
               --melonds-bios7 <f> --melonds-bios9 <f> [--melonds-played <nand.bin>]
    the DSiWare path against a GENUINE dump, on copies - nothing given is written to.
    Builds the working image, installs the title, migrates a played per-title NAND if
    one is given, and then requires the rebuilt image to hold what the played one held,
    file for file. That last line is the whole point: everything else proves the plugin
    agrees with our reading of melonDS, this proves the reading.

--melonds
    ... and which DSi a title needs: that the region mask beats a letter that disagrees,
    that a letter answers when there is no mask, that a file name may narrow an answer
    but never overrule one, and that a title saying nothing anywhere gets no answer
    rather than a guess. Plus what a DSiWare launch does when it has nothing to run on.

--melonds
    the whole melonDS contract against a forged install: both save dispositions, the .ml<n>
    slots, the name taken from inside an archive, the TOML writer, the DS/DSi/DSiWare decision
    read out of a ROM header, and the per-title NAND - that it is copied and not invented,
    that your own dump is never written to, and that one title's NAND never seeds another's.
    When this build carries the metadata index it also reads that index a second way, from
    the resource and by the packer's own documented layout, and requires the two readings to
    agree.

--nogba
    the no$gba contract against a forged install. First that the save format is set to Raw -
    the one line the whole plugin exists for - including after somebody's Options > Save
    Options reverted it. Then that writing that one key leaves the other fifty settings, the
    "do not edit" banner and the hex key mapping character for character where they were,
    and that a second pass changes not one byte. Then that a BIOS sitting in
    RetroArch\system is copied under the name no$gba reads, that one nobody has is not
    invented, and that a save is listed under the name of the ROM INSIDE the archive
    rather than the archive's own.
```

Point the first two at a throwaway install and a throwaway account.

Two modes write nothing at all: `--disc-id-of <rom>` reads a disc id out of a `.pbp`, `.iso`, `.cso`
or `.zso`, and `--rows` confronts the metadata row injection with a real `Microsoft.Data.Sqlite`.

`--flycast-real --emu <flycast.exe> --rom <game>` is the one that needs a real Flycast; it reads and
compares, and writes nothing.

## Notes on PPSSPP

**It needs no BIOS.** PPSSPP high-level-emulates the BIOS and the PSP's operating system; its own FAQ
says so and LaunchBox's metadata database agrees. `assets\flash0\font\` holds 18 freely
redistributable replacement fonts shipped with the emulator, not firmware. `GetBiosFilesForPlatform`
returning nothing is correct — please do not "fix" it.

**Installs are portable, and that constrains updates.** The release zip contains no `installed.txt`,
and its absence beside the executable makes PPSSPP keep everything — saves, save states,
`ppsspp.ini`, the RetroAchievements token — under `memstick\` *inside the emulator folder*. So an
update extracts **over** the folder and never clears it first. The cost is that files dropped between
releases linger.

This plugin also never converts an existing installer-based install to portable mode: that would
move the user's saves without telling them.

**RetroAchievements credentials go to two places.** PPSSPP marks `AchievementsToken` as `DONT_SAVE`:
it reads the key from `ppsspp.ini` but never writes it back, so that a user can post their ini for
debugging without leaking a credential. The copy it maintains is
`<memstick>\PSP\SYSTEM\ppsspp_retroachievements.dat`, raw bytes. A token written only to the ini
survives exactly until PPSSPP next exits.

Also: the ini key for hardcore mode is `AchievementsChallengeMode`, and it defaults to *on*.

**A save is several folders, and the set is the unit.** PPSSPP writes one folder per purpose under
`PSP\SAVEDATA\`, all sharing the game's 9-character disc id: `ULUS10064DATA00`,
`ULUS10064SETTINGS`, `ULUS10064SYSTEM`. All of them together are one save. This matters beyond
tidiness: Argosy, the Android client that shares these saves through RomM, **deletes every folder
matching the disc id** before unpacking a restore, so handing it an incomplete set destroys the
folders left out.

Folders holding installed game data - a readable `PARAM.SFO` of at most 64 KiB that declares neither
`SAVEDATA_PARAMS` nor `SAVEDATA_FILE_LIST` - are excluded, the same way and by the same rule Argosy
excludes them.

**A save is matched to a game by disc id, read from the ROM.** `.pbp`, `.iso`, `.cso`, `.zso` and
`.chd` are all parsed. Anything else falls back to matching on the game title from `PARAM.SFO` —
which is `TITLE`, not `SAVEDATA_TITLE`: the former names the game, the latter names the save inside
it.

**The backup layout is an interop contract, not a preference.** A backup contains each folder under
its own name at the root, because that is what makes the content hash equal the one RomM and Argosy
compute for the same save. `--save-unit` asserts it against a value derived from the golden vectors
in `argosy-launcher/sigil/scripts/romm_hash_vectors.py`; if you change how saves are stored, that
assertion is what will tell you the two ends have stopped agreeing.

Save STATES are deliberately not managed. They never leave the machine that wrote them (a state
belongs to one core at one version, and the server refuses them), and LiteBox drops companion files
for every state, so a `.ppst`'s sibling `.jpg` thumbnail could not travel with it anyway.

**Downloads come from GitHub.** ppsspp.org publishes a different set of Windows packages — a zip with
both the 32- and 64-bit builds, plus an Inno installer and a paid Gold variant. This plugin installs
the GitHub x64 zip and does not offer the installer, but it recognises an install made either way.

## Notes on Flycast

**Portable, always.** On Windows `setupPath()` (core/windows/winmain.cpp) unconditionally puts
`emu.cfg` beside the executable and everything written under `<exe>\data\`. There is no `%APPDATA%`
branch and no installed/portable choice to interpret, unlike PPSSPP.

**One emulator, four platforms.** Dreamcast, Naomi, Naomi 2 and Atomiswave. The arcade three read
their ROMs straight out of `.zip`, so the emulator entry must NOT auto-extract — handing Flycast an
extracted folder gives it something it cannot load.

**The BIOS are optional, and declared per romset.** Dreamcast runs most games without one; the arcade
platforms do not run at all without theirs. Both are declared so the frontend can tell the user which
file is missing instead of letting a game fail silently.

**It ships with no save-state keys, so this plugin adds them.** Flycast's default keyboard mapping
binds Tab, Space and F12 and nothing else: `EMU_BTN_SAVESTATE`, `EMU_BTN_LOADSTATE` and even
`EMU_BTN_ESCAPE` work but are attached to no key. On install the plugin writes
`mappings\SDL_Keyboard.cfg` with **F2** save, **F4** load, **F6**/**F7** slot, **Escape** quit — F2 and
F4 because that is what LaunchBox's own scripts already assume for RetroArch, and Escape because a
frontend's Exit button means Escape and `dc_exit()` closes the VMU files properly where a killed
process does not.

Two things make that file delicate, and both are why it carries more than four lines. A mapping file
REPLACES Flycast's defaults rather than adding to them (`GamepadDevice::find_mapping` falls back to
`getDefaultMapping()` only when no file exists), so it must also carry all nineteen default bindings
or the user loses his controls — `--hotkeys` exists to check exactly that. And one file covers all
four platforms: with no `_arcade` file, Flycast clones the Dreamcast mapping for arcade.

A mapping file that already exists is never rewritten. An action the user has bound keeps his key, a
key he already uses is never taken, and the emulator's AutoHotkey fields quote whatever the file
really says — a script naming a key the emulator ignores would fail silently, which is worse than no
script. Those fields are used by LaunchBox's and BigBox's pause screen; LiteBox stores them but has
no pause screen yet.

## Notes on Xenia

**Canary, not master.** `xenia-project/xenia` has been frozen since February 2026 and xenia.jp marks
it inactive; `xenia-canary` ships almost daily. The plugin **claims** both (`xenia_canary.exe` and
`xenia.exe`) and **reads saves from both** content layouts, but only **installs** canary.

**There is no version to read.** Xenia has no Win32 version resource and no `--version` flag, and
`--help` opens a modal dialog in a windowed app - never invoke it. What exists is a build string
compiled into the binary (`canary_experimental@74c4e4a on Sep 21 2026`), which this plugin reads out
of the file. Canary's "version" is a 7-character git SHA, so releases are ordered by publication
date, never by tag.

**A save is the `00000001` folder, not the title folder.** Under one title id sit `00000001` (saved
games), `00000002` (downloadable content) and `000B0000` (title updates). Only the first is a save.
This is also where Argosy stops, and matching it is what lets a save sync between the two.

**Two content layouts, sometimes in the same install.** Canary keys content by profile before title
(`content\<XUID>\<TITLEID>\<TYPE>\`); master does not (`content\<TITLEID>\<TYPE>\`). Canary
migrates in place, so a real install can hold both at once - at the top of `content\`, a 16-hex name
is a profile and an 8-hex name is a legacy title id.

**The save's `.header` travels outside the fingerprint.** Display name, thumbnail and license mask
live in a file one level above the save, which Argosy does not send. Including it would move the
content hash and the two ends could never agree; leaving it behind would lose the name and the
picture. It goes into LiteBox's reserved `.litebox-plugin` folder inside the backup, which is
excluded from every hash and from the zip served to clients.

**A restore merges, and never invents a profile.** Argosy deletes nothing on an Xbox 360 restore, so
neither does this. And a save can only be restored into a profile that exists: constructing a XUID
would produce a directory the emulator never reads, so with no profile the plugin says so instead.

**Two launch defaults are corrected** on the command line, because Xenia regenerates its TOML on
every start and exit so writing there is pointless: `--license_mask=1` (the default `0` boots many
XBLA titles in trial mode and hides owned DLC - the most common Xenia support question) and
`--discord=false`. Anything you set yourself is left alone.

**melonDS ships with no keyboard mapping at all, so the plugin writes one.** Its default table gives
`Instance*.Keyboard` the value `-1` (`Config.cpp:51-52`) and there is no table of defaults anywhere
else - no "restore defaults" button, no first-run step. A fresh installation therefore cannot be
played until somebody opens Config > Input and clicks twelve times, which is not a state to hand
anybody. Arrows for the D-pad, X and Z for A and B, S and A above them, Q and W for the shoulders,
Return and Backspace for Start and Select, plus `HK_Lid` on L - that last one is not a convenience,
several games cannot be FINISHED without closing the lid.

Only what is unbound is written, so a mapping somebody made is theirs, including a button they left
unbound on purpose. It happens at install, and at launch only when **every** DS button is unbound -
which means melonDS was never set up at all. The `no-melonds-input` marker beside the log turns it off.

**The pad is deliberately left alone.** melonDS opens `SDL_GameController` only for rumble and
sensors (`EmuInstanceInput.cpp:244-261`) and reads every button through `SDL_JoystickGetButton`,
`GetHat` and `GetAxis` - raw indices, which mean different things on different hardware. A default
there would be a guess about somebody's controller rather than a fact about melonDS.

**The savestate and exit keys are not configurable and are not written anywhere.** melonDS's `HK_*`
enumeration has no entry for saving, loading or quitting; those are Qt menu shortcuts fixed in the
source - `Shift+F1..F8` to save, `F1..F8` to load, `F12` to undo a load, `Ctrl+Q` to quit
(`Window.cpp:359-401`). The AutoHotkey scripts cite exactly those, which is the only lever there is.

**No RetroAchievements.** Neither fork supports it. Canary has native Xbox 360 achievements written
into the profile's `.gpd` files, which is unrelated machinery.

**Title ids** are read from the content: STFS containers (`CON`/`LIVE`/`PIRS` - Games on Demand,
XBLA, DLC), `.xex` files, extracted folders, and disc images through XDVDFS. `.zar` is not supported
- it carries its magic in a footer and holds no metadata.

## Notes on melonDS

**Portable by construction, and that is decided at build time.** `pathInit()`
(`src/frontend/qt_sdl/main.cpp:180-214`) looks for a **directory** named `portable` beside the
executable, then falls back to the executable's own folder because `WIN32_PORTABLE` is defined -
`option(PORTABLE ... ON)` at `src/frontend/qt_sdl/CMakeLists.txt:196-201`, which the release preset
does not override. The per-user branch is `#else`-d out of the Windows build entirely. So
`melonDS.toml` sits next to `melonDS.exe`, and this plugin never has to guess.

**The install is one file.** `melonDS-1.1-windows-x86_64.zip` holds a single entry, `melonDS.exe`,
statically linked. No Qt DLLs, no platform plugin folder, nothing to keep in step.

**Its configuration has sparse defaults, which makes writing one safe.** melonDS declares defaults as
partial maps and resolves a missing key by walking it backwards until one matches (`Config.cpp:49-128`
and `FindDefault`, `Config.cpp:663-679`). A key that is absent keeps its default instead of becoming
unset - so a `melonDS.toml` containing nothing but two paths is a complete, valid configuration. That
is the opposite of PPSSPP's `controls.ini`, where a partial file unbinds everything it omits.

**It rewrites that file on exit, but keeps what it does not recognise.** `Config::Save`
(`Config.cpp:809-819`) truncates and reserialises the whole document. Unknown keys survive; comments
and formatting do not. This plugin therefore refuses to write while melonDS is running, exactly as
the PPSSPP one does.

**Saves are redirected on installs this plugin made, and only those.** Out of the box melonDS writes
`<rom>.sav` next to the ROM - an empty `SaveFilePath` means "the ROM's directory"
(`EmuInstance.cpp:445-484`). A download through this plugin points `SaveFilePath` and `SavestatePath`
at `saves\` and `savestates\` inside the install, which keeps a ROM library clean and works when the
ROM sits on read-only media. A melonDS you set up yourself is never touched, and both dispositions
are read when saves are listed.

**A save's name comes from the ROM, and from inside an archive when there is one.** The name is
`romname.substr(0, romname.rfind('.'))` - so `Foo (USA).nds` saves to `Foo (USA).sav`. When melonDS
loads a ROM out of a zip, 7z or rar, that name is the **entry's**, not the archive's, so this plugin
opens the archive to find it.

**Savestates are `.ml1` to `.ml8`, and `.mln` is a decoy.** `getSavestateName`
(`EmuInstance.cpp:696-707`) builds `".ml" + slot`, and the menus run slot 1 to 8
(`Window.cpp:356, :372`) - the same numbering on disk and on screen, unlike PPSSPP. `.mln` appears
exactly once in the whole tree, as the default suffix of the "save to file" dialog
(`Window.cpp:1583`). Globbing for it would find nothing.

**It already has save-state keys, so this plugin adds none.** That is the difference with Flycast and
PPSSPP. melonDS's configurable hotkeys are the `HK_*` list (`EmuInstance.h:36-59`), which contains no
save-state, load-state, slot or quit entry at all, and every binding in it starts unbound
(`Config.cpp:51-52`). The save-state keys are Qt menu shortcuts set in code (`Window.cpp:353-401`):
**F1-F8** load slots 1-8, **Shift+F1-Shift+F8** save them, **F12** undoes a state load, and **Ctrl+Q**
quits. They cannot be rebound, so the AutoHotkey scripts quote them as constants and say where they
come from.

**DS needs no BIOS; DSi needs real dumps.** `Emu.ExternalBIOSEnable` is absent from the default table
so it is false, and with it false melonDS uses its built-in FreeBIOS (`EmuInstance.cpp:868-896`) and
**generates** a firmware (`:1013-1025`). The three DS files are therefore declared optional. DSi mode
is the other regime: `verifySetup` calls `verifyDSiBIOS` and `verifyDSiNAND` unconditionally
(`:633-659`) and there is no generated DSi firmware.

**The console mode is chosen per ROM, which means this plugin edits a global setting.** melonDS has no
command-line option for DS versus DSi - `CLI.cpp` accepts a ROM, `--boot`, `--fullscreen` and
`--archive-file`, on 1.1 and on master alike - so `Emu.ConsoleType` in the TOML is the only lever. The
ROM says which mode it wants, through melonDS's own predicates (`NDS_Header.h:206, :219`): `UnitCode &
0x02` means DSi-capable, and a `DSiTitleIDHigh` of `0x00030004` means DSiWare. A DSi title switches
the mode only when the three DSi files are configured **and present**; otherwise it starts in DS mode
and the log says why.

**What may be handed in, and why the list is not melonDS's.** The ROM extensions are its own -
`.nds`, `.srl`, `.dsi`, `.ids` (`Window.cpp:95`). The containers are not: melonDS opens archives
itself through libarchive and accepts a long list, but this plugin has to read the INNER entry's name
out of one, because that name is what the save is called. A container it cannot open is a save
silently misfiled. So the declared set is what was measured by handing the plugin one of each -
`.zip`, `.7z`, `.rar`, `.tar`, `.tgz` - and the probe holds that measurement.

**A DS game in an archive is simply forwarded**; melonDS unpacks it and nothing here has to. **A
DSiWare title in an archive is not**, and that difference used to be a dead end: a DSiWare has to be
INSTALLED into a NAND first, both melonDS's importer and ours read a `.nds` from disk, and the plugin
refused. It prepared a NAND, installed nothing into it, and left the DSi menu showing no game, with a
log line for an explanation. Now the `.nds` is unpacked to a temporary file, installed from there,
and deleted. A `.tmd` you put beside the ARCHIVE is still found - the unpacked copy has nothing
beside it, so both paths are carried.

Two entry points are needed for that. `ArchiveFactory` wants a container it can seek around in, which
a gzipped tar is not - measured: `.tar` opens, `.tar.gz` does not. `ReaderFactory` reads a stream
forwards instead, which is exactly what a compressed tar is, so it picks up what the other cannot.

**What melonDS needs, and what it does not.** Read out of `EmuInstance::verifySetup` (`:633-665`)
and `loadFirmware` (`:1012-1050`) rather than out of a wiki:

| launching | needs |
|---|---|
| a DS game | **nothing** - a built-in BIOS and a generated firmware |
| a DS game, with your own console's | `biosnds7.bin`, `biosnds9.bin`, `dsfirmware.bin` |
| **DSiWare** | `biosdsi7.bin`, `biosdsi9.bin`, `dsifirmware.bin`, **and a NAND of the right region** |

Those names are melonDS's own community's. **RetroArch's are accepted just as well** - `bios7.bin`,
`dsi_bios7.bin` and the rest, as its melonDS cores declare them in their own `.info` files. Two
conventions exist for the same seven files and neither is wrong, so a file is found under either.

The dependency list names the file really **in the declared folder**, under whichever of the two
names it has; only a file that is nowhere falls back to RetroArch's name, since that is whose folder
it is. A file in the legacy folder still works and is deliberately NOT named there - pointing the
host's own check at a folder that does not hold it is the defect this arrangement exists to remove
rather than to move around.

**Setting the paths is the plugin's job, not yours.** Drop a file in `bios\` and it gets configured.
Nobody should have to go into Config > Emu settings and type three paths to make a game start.

**And the switch follows the files.** `Emu.ExternalBIOSEnable` is what makes melonDS demand all three
DS files (`verifySetup`, whatever the console type), so it goes **on** when all three are there - you
get your own console's boot animation and settings - and **off** when they are not, which boots on
the built-in BIOS and a generated firmware. Either way the game runs; left on without the files,
melonDS refuses to start at all. A path you set yourself, anywhere you like, is never overwritten.

**A BIOS is found by NAME, and that is deliberate.** These six names are what the plugin declares to
LaunchBox, so they are what its BIOS check looks for and what you read before going to find one;
accepting something else would make the declared name and the accepted name different things. The
size melonDS demands is checked too, but only to **say** something - a file with the right name is
handed over whatever its length, because melonDS makes the final call and is better at it, and
silently refusing a file you deliberately put there would be second-guessing you with less
information.

A NAND is the opposite case and is treated the opposite way: it has no canonical name, so its region
is read out of the file. Name where there is a convention, contents where there is none.

**The DSi firmware is required whatever that setting says**, and it is a trap worth naming.
`verifySetup` only checks it when `ExternalBIOSEnable` is on, which makes it look optional. It is
not: `loadFirmware`'s built-in branch for DSi mode is an empty `// TODO` that falls straight through
to opening `DSi.FirmwarePath` anyway. Believing the verify step would have meant declaring a file
unnecessary that melonDS then fails without.

**They go in `Emulators\RetroArch\system\`**, which the install creates when it is not there.
Somebody else's folder, on purpose: nearly everybody running LaunchBox has RetroArch, and anybody who
ever set up a DS core there already has these seven files in it. Asking for a second copy, in a
second folder, under a second set of names, would be inventing work for the sake of owning a
directory.

`Emulators\melonDS\bios\`, where this plugin used to ask, is still searched - an installation set up
before the move keeps working untouched. Drop a file in either and launch: the plugin finds it and
points melonDS at it. A path you configured yourself, anywhere you like, is left alone; only what is
absent or broken is looked up.

**A DSi NAND is region locked, so the right one is chosen per game.** The system menu that launches an
installed title is built for one region and refuses titles from another - which is a blank screen and
no explanation. Put as many region dumps in `RetroArch\system\` as you own, **under any names you
like** - a dump is found by its size, between 220 and 260 MB, and identified by what is inside it:

```
your dump                 -> 0:/sys/HWINFO_S.dat, byte 0x90   -> the region it came from
the game's header         -> DSiRegionMask at 0x1B0           -> the regions that accept it
```

Neither side is read from a file name. The offset in the NAND is measured, not derived: the file
announces `EntrySize = 0x1C`, and 128 (its RSA-SHA1 HMAC) + 4 + 4 + 28 is 164, exactly the
`static_assert` on `DSiSerialData`. Checked against six dumps - AUS, CHN, EUR, JPN, KOR, USA - and the
region read out of each matched its name in all six, with language masks matching melonDS's
`AmericaLanguages`, `EuropeLanguages` and the rest. Opening a dump costs about 150 ms, so the answers
are remembered in `dsi\nands.txt` and forgotten again when a file's size or date changes.

**Three things say what a game wants, and they are tried in order of authority.** The region **mask**
first: it is the field melonDS itself is handed, and it is the only one of the three that can say
"several regions" or "region free" without a table of special cases. Then the region **letter**, the
fourth character of the game code - which is also the last byte of the title id, so it costs nothing
to read. Then the **parentheses in the file name**, `(Japan)`, `(Europe, Australia)`, used only to
narrow an answer that named several regions. A rename is the least trustworthy link in the chain and
is never allowed to overrule the header.

**Two dumps of the same region is a real case, and the choice between them must not drift.** People
keep the dump of their own console next to a clean stock image. Directory order is not an ordering -
it is whatever the filesystem hands back, and it moves when files are added or renamed - and every
save is the difference against one particular dump, so a choice that wanders replays saves onto a
console they never came from. The rule: a dump that **already has a configured console** (below)
wins, because that is the one the saves came from; failing that, the name, ordinally. It is one
`File.Exists` - nothing is opened to decide. The log says which one was taken whenever there is more
than one to take.

If the NAND for a game's region is not there, a window says so and offers to open the folder. This
plugin brings its own windows because the LaunchBox SDK has no message API of any kind - no
`ShowMessage`, nothing - so the alternative was a line in a log file nobody reads when a game fails to
start. There are four subjects: this one, the two that build a console, and one for a save whose
console has gone missing. All four are below.

**DSiWare runs on one working NAND, rebuilt at every launch.** A DSiWare title is not a cartridge:
melonDS boots it out of the NAND, and its save lives there too, at
`title/<category>/<id>/data/public.sav` (`DSi_NAND.cpp:1077-1092`). So an image has to exist and hold
the title. The first design gave each title a copy of the dump - 240 MB per game. Walking both images
showed what actually differs after a session:

```
your base NAND        -> + the title installed    7 entries added, nothing else
that rebuilt image    -> the NAND actually played  5 files, 80 KB:
                                                     shared1/TWLCFG0.dat  } console settings
                                                     shared1/TWLCFG1.dat  } (the DSi keeps two)
                                                     shared2/launcher/wrap.bin
                                                     the title's own public.sav
                                                     the launcher's private.sav
```

So the difference is the save, and the image is scratch:

```
..\RetroArch\system\                     YOURS - BIOS, firmware, and a NAND per region you own
    DSi_Nand_USA_1.4.5.bin                 and it is never written to, by anything here

<install>\dsi\                           OURS - everything this plugin fabricates
    DSi_Nand_USA_1.4.5.bin                 the CONSOLE built from that dump - same file name
    DSi_Nand_USA_1.4.5.bin.recipe.zip      how to rebuild it from the dump alone
    DSi_Nand_USA_1.4.5.bin.recipe.txt      which dump, which identity, which region
    bases\<identity>.bin                   a console REBUILT for a save that asked for it
    work.bin                               the working image, rebuilt every launch
    tmd\0003000412345678.tmd               the title's metadata, once per machine
    0003000412345678\state\                the files that differ - tens of kilobytes
```

**The name is the link, and that is the whole mechanism.** `dsi\<the dump's file name>` exists means
that dump has been set up and this is its console - one `File.Exists`, no hash, no index - and since a
dump has exactly one console, "which console" is never asked. `bases\` holds only **rebuilds**, for
the case where the identity written in a save matches nothing on disk.

**480 MB for the whole library instead of 240 MB per game**, and an old per-title NAND found on disk
is turned into its state and deleted on the next launch of that game - nothing is removed until the
state has been written down.

**The working image is kept, as a cache of one.** Relaunching the same game is the common case, and
rebuilding for it would copy 240 MB and reinstall a title to arrive at what is already on disk -
about a quarter of a second, measured, and 240 MB written for nothing. So a launch of the same title
from the same ROM uses the image as it stands. Launching anything else rebuilds over it, which is the
eviction: there is only ever one.

**A console has to be set up, and your dump is not where that happens.** A NAND out of a real
console carries that console's name, language, birthday and colour; one from anywhere else carries a
stranger's, or an unfinished welcome sequence the DSi menu insists on completing. Completing it
**writes into the image** - and every DSiWare save afterwards is a difference measured against that
image, so it then has to stay exactly as it is forever.

Both facts are true at once, and the only way to hold them together is for the image that gets
written into to be **ours**. So the dump is copied to `dsi\<its own name>` and melonDS is pointed at
the copy. The dump is read and never opened for writing - by this or by anything downstream, since
the working image is itself a copy.

This is the second arrangement. The first configured the dump in place and kept a pristine copy as
`<dump>.lock` to repair it afterwards, which meant the folder where somebody keeps pristine dumps held
one that was not, under the name saying it was. The `.lock`, the `.bak`, the three-state machine read
off them and the "never touch this file again" warning all existed to manage that damage; none of them
survived the damage not being done. Both suffixes are still refused by the NAND scan, because a 240 MB
leftover would otherwise pass the size gate on somebody's disk today.

So the first time a dump is used, a window says so, and offers to build one now. melonDS is opened on
the copy, with `ConsoleType = 1` and `DirectBoot = false`, and **one menu item has to be clicked**:
*File > Boot firmware*, which starts the DSi menu held in the NAND. Set the console up, quit melonDS,
and a second window asks whether it worked. Yes writes the recipe beside it; no deletes the copy, and
there is nothing to put back. The game you launched does not start that time - launch it again once
the console is ready.

**That click cannot be done for you, so it is said on screen instead**, in a window that appears over
melonDS once melonDS is up. Those two keys decide how a *ROM* starts, and the setup has no ROM: with
nothing to run, melonDS sits on its splash screen saying "File->Open ROM... to get started". Measured
on 1.1: the command line does carry `-b/--boot auto|always|never`, but it is handed to `preloadROMs`
and does nothing without a ROM - launched with `-b always` and no ROM, melonDS stayed on the splash,
no window opened and the NAND was not written. Saying it in the window *before* would be saying it to
somebody about to look somewhere else, which is exactly what went wrong the first time.

**There is no "play without a console", and that is a deliberate reversal.** The window used to
offer *Not now*, which started the game on the raw dump - the behaviour from before any of this
existed, kept out of a reflex not to gate somebody's game. Measured against the save model it was
not the free option it looked like. An unconfigured NAND makes the DSi menu run its welcome sequence
**inside the game**, and those settings land in `shared1/TWLCFG0.dat` and `TWLCFG1.dat`, which the
capture files under *that one title's* state and the restore replays at every launch. The console
setup then lives once per game, buried in each save, instead of once in a console - and those saves
carry no recipe, so nothing can rebuild them anywhere else. Closing the window abandons the launch.

**None of this happens without a window, and nothing is refused without one either.** Every step is
gated on the dialog being available - with windows suppressed there is nobody to ask, so that one
case runs on the dump and says so in the log. A silent refusal would make DSiWare unplayable with no
way to find out why; turning the windows off is an explicit act, and being asked nothing is what it
means. Copying 240 MB and starting an emulator nobody asked for would be the worse failure of the
two.

**And the space this takes is bounded by your configurations, not by your library:** one console per
dump you set up - in practice one, at most one per region - plus one rebuild per *previous*
configuration still claimed by a save, which is zero until you reconfigure something. Reconfiguring
once does not make an image per game: every save from before names the same old console, so they all
share one rebuild. `bases\` is a cache and can be deleted at any time; the recipes make it again.

**A save carries the recipe for its own console, so losing that console is survivable.** This is what
the lock could not do. A DSiWare save is a delta against ONE base image, so if that image goes - a
new machine, a reconfigured NAND, a deleted file - every save made on it stops meaning anything.
Silently: nothing errors, they simply no longer apply.

But a console is only three things, and just one of them is irreplaceable:

```
the dump       a pristine NAND, in the user's folder     the user has it, and it never changes
the recipe     dump -> console, a few dozen KB           we can compute it
the console    the dump plus its console setup           DERIVED: rebuildable from the two
```

So at the moment the console is built - the one moment both images exist side by side - the difference
between them is written down as `<console>.recipe.zip`, and every save captured afterwards takes a copy.
A save then carries everything needed to rebuild the console it belongs to from a dump anybody can
re-obtain.

**The recipe is itself a delta**, in exactly the format a save uses, so `MelonDsDelta.Capture` and
`Apply` are reused unchanged with the original as the reference instead of a fresh install. Measured:
the console setup touches `shared1/TWLCFG0.dat`, `TWLCFG1.dat` and the launcher's `private.sav`. Tens
of kilobytes.

**And it is a zip, which was not a preference and is now the house style.** When a save was a folder
that folder was flat by construction (`FlatName` turns `0:/shared1/TWLCFG0.dat` into
`0__shared1_TWLCFG0.dat`), and backup and restore both copied its top level only - so a recipe
*folder* inside it would have been the first subdirectory in the tree and would have been lost at the
first backup. One file survived both without a line of code. The save has since been packed the same
way, by the same writer, so the recipe is now simply one entry inside it - and it is written
deterministically for the same reason the save is: it is copied into every save made on that console,
so a recipe that varied would make all of them vary with it.

**The identity is derived, never assigned.** It is a hash of the entries the recipe names, read out of
the *image* rather than the recipe, and cached in `dsi\identities.txt` on size and write time. Three
alternatives died against this: nothing is written inside the NAND, so there is no foreign directory
whose failure mode would be an image that no longer boots; it cannot go stale, unlike a token beside
the file, which survives the file being swapped; and two byte-identical consoles are correctly
recognised as the same, where a random id would have forced a pointless rebuild.

At launch it is one string comparison. When it does not match, the original is looked for among the
dumps - by name, then by size, then by hash - the console is rebuilt into `dsi\bases\<identity>.bin`,
**and the rebuild is checked** against the identity it was supposed to produce. A rebuild is
configured by construction and lives in a folder of ours, so it is never offered for setup: doing so
would change its identity and break the very save being recovered.

**When the dump cannot be found either, the choice is put to you.** A window names the file, its size
and its sha256 - put it back anywhere in your BIOS folder and the save returns by itself, since it is
found by contents and not by name. Or start a new game on a console of the **same region**, because a
DSi menu refuses a title from another one and offering anything else would be no offer at all.

**Starting again does not delete what was there.** That is the trap in this option: the next capture
would write the new session straight over the old state. So before a fresh game begins, the old state
is moved aside to `state.orphan-<console>`, and a log line says so. It applies again the day that dump
comes back. Without it, "start a new game" would quietly mean "delete the one you had".

What no recipe can do is cross consoles: a NAND is encrypted with its own console's id, so "the same
dump" means literally the same file - which the window says, rather than leaving somebody to discover
it.

A save made before any of this existed carries no record, and gets no opinion: the current base is
used, exactly as before, and the recipe is attached at the next capture. The same rule `work.sum`
follows.

Reuse is allowed only when nothing could have made the image stale - it exists, a marker says it
holds this title built from this ROM (path, length and write time, not a hash: this runs on every
launch and a DSiWare `.nds` is several megabytes), the walk of that install is still there, and
melonDS is not in the middle of using it. Getting it wrong would cost nothing that cannot be rebuilt,
since the state on disk is the save and the image is scratch, but it would cost a session.

A launch captures whatever the working image still holds **before** rebuilding it, so a session is
never thrown away unread; that happens on every launch, including of a plain cartridge. Free space is
checked first: a dump is around 240 MB. The `no-dsi-nand` marker beside the log turns the whole thing
off, and `no-melonds-dialog` silences the window without silencing the log.

An earlier layout kept the user's dump as `dsi\base.bin` and built every title on it. Nothing writes
one any more, but one found there is still accepted as a last resort, with a log line saying where
region dumps go - an install that worked yesterday does not stop working today.

**A deletion is a difference too, and it is recorded as one.** A state describes how a console
differs from a fresh install of the title, and "differs" runs both ways: a file the install has and
the console no longer does is as much a difference as one whose contents changed. So the index has
two line shapes, and a restore replays both - putting files back, and taking files away:

```
F  <flat name>  0:/path      this file differs from the fresh install; here it is
X  -            0:/path      this file was deleted, so delete it again on the rebuild
```

Without the second, a file the player deleted would come back at the next rebuild, looking exactly
like a save that did not take. Directories are never carried: importing a file makes the path it
needs.

**Nothing decides which files matter.** The five above are what one measurement found; they are not a
list the code carries. It walks the image, compares, and keeps what differs - a title writing
somewhere nobody expected is captured because it differed, not because it was foreseen.

**The comparison is by file, never by byte.** A FAT directory entry carries the wall clock
(`get_fattime`, `ffsystem.c:107`) and the allocator places clusters in the order operations happened,
so two installs of one title into one base, a second apart, produce images whose **bytes differ and
whose manifests are identical** - both halves measured. A byte delta would be mostly noise and would
not apply to a reference regenerated later; a file delta has neither problem.

**Only what is inside the filesystem is captured**, and measured, that is everything that moves: the
region below `0x10EE00` - the MBR, the stage2 loader and the "DSi eMMC CID/CPU" footer holding the
console identity the decryption key comes from - did not change in one byte across a real session. It
comes from the console the title was built on, and every rebuild starts from that same image.

**Without the native library none of this is possible** - nothing can be installed, walked or
captured - so that case keeps a NAND of its own per title, which is where a manual import through
Manage DSi titles survives.

**A NAND cannot be fabricated, only copied.** `NANDImage` opens an existing file and reads the
`ConsoleID` out of it (`DSi_NAND.h:52-64`), and the decryption depends on console-unique data plus the
ES key in `dsi_bios7.bin` at `0x8308`. So a dump of your own console is required; there is nothing to
generate.

**Installing the title into its NAND is done by melonDS's own code**, through `melonds-nand.dll`.
`NANDMount::ImportTitle` is reachable only through the Manage DSi titles dialog upstream - there is no
command-line option on 1.1 or on master - and reimplementing AES-CTR over a NAND plus FAT writing in
C# would mean a second implementation of a format where being subtly different is the same as being
wrong. When the library is absent the plugin falls back to asking for one click in Manage DSi titles,
and everything else still works.

**The metadata comes from four places, in order.** A `.tmd` is Nintendo's signed description of a
title - title id, save sizes, ratings, the content's SHA-1 - and `NANDMount::ImportTitle` will not
install without one. The plugin looks for `<rom>.nds.tmd` beside the ROM first, because a complete
dump ships it and because it is the user's own choice; then in the index it carries, if this build was
given one; then at Nintendo's update server, at the address melonDS's own dialog uses
(`TitleManagerDialog.cpp:485`), which still answers - 200 with 2312 bytes, of which melonDS reads the
first 520. Only when all three fail is one built from the ROM header, and the log says it is unsigned.

**The revision is chosen by hash, not by date.** About a hundred titles have several revisions, and a
TMD carries the SHA-1 of the content it describes. So the right one for a given dump is the one whose
hash IS that dump's - an exact answer rather than a guess at "probably the newest". The newest wins
only when none matches, and the log says it had to, because that is also the case where the DSi menu
is most likely to refuse the title.

**A DSiWare title is started from the DSi menu, because direct booting loses the save.** Passed as a
ROM argument, melonDS does not refuse a DSiWare `.nds`: `UnitCode & 0x02` sends it down the DSi branch
of `SetupDirectBoot`, the NAND is mounted and the game opens with no menu and no click. It is
tempting, and it does not work. Measured on a title installed from real signed metadata, one launch,
play, save, quit:

```
nand.bin     changed          melonDS did write to the image
public.sav   byte-identical   nothing of the game's reached its save
```

and the game said so on screen - *"99Bullets data was corrupted and has been deleted"*. Even that
deletion did not land. The extraction ran after the NAND changed and returned the same bytes, so the
failure is upstream of it: there was nothing new to extract. Booting through the menu costs one click
and saves correctly.

This took two passes, because two unrelated bugs put the same sentence on screen - an unsigned TMD the
DSi menu refused, and this. An empty file named `dsi-direct-boot` beside the log still switches it
over for anyone who wants to re-run the measurement; it is read on every launch rather than
remembered, so it can be flipped with the host running, and the log names which way each launch went.

**A DSiWare save is the whole difference, packed into one file: `.dsisave`.** Not the image - 240 MB
is far too much to hash for a freshness dot or to copy into a vault. But not one file out of it
either, which is what this used to hand over and it was wrong: measured on one real session, the
game's own `public.sav` was **16 KB out of 4.2 MB across eleven files**. The console settings, the
menu's data and the built-in applications' saves had all moved too, so a backup would have captured a
fifth of a save and a restore would have put a game's progress back into a console that had forgotten
it.

```
<install>\dsi\0003000412345678\state.dsisave     what the host lists, backs up and hands back
Saves\Nintendo DSiware\The Game (USA).dsisave     what a backup of it is called
```

It was a folder first, with `IsDirectory` set - the contract has a shape for that, and Xenia in this
same repository uses it because an Xbox 360 save is a folder too. And this paragraph used to argue
for it: *a folder beats packing it into an archive - nothing to pack, nothing to unpack, and no
archive quietly changing its own bytes between two identical writes and making the host think the
save moved.*

**The objection was right, and it is the reason the packing is careful.** The host fingerprints a
file save by hashing its bytes, so an archive that came out different from identical content would
make the freshness dot flicker at every launch. Three rules settle it - entries sorted by name, one
constant timestamp, no compression at all - and the probe packs the same content twice, in two
orders, from files stamped years apart, and requires the same sha256. Stored rather than a fixed
compression level because this assembly targets `net9.0-windows` and .NET 9 moved to zlib-ng: pinning
the level would not pin the bytes. The writer is SharpCompress, pinned at 0.41.0 and merged into the
DLL, so its header layout ships with us instead of coming from whatever runtime the host is on.

**What the folder cost in exchange was the host's container path**, and that is what settled it.
`IsSaveContainer` picks between a branch that asks the plugin to extract a save into a temp folder -
which then lands in the vault as a folder with no extension - and a branch that copies one file. The
container branch is where all three of this plugin's save-management defects lived. The file branch
is the one every other save in this repository takes.

**The working image carries a receipt, so a save that arrived from elsewhere is not overwritten.**
A session is written down at the START of the next launch, because nothing says the emulator has
quit. That is correct while the image is the newest thing on disk - and it stops being true the
moment something else writes the save: a RomM sync, a restore from another machine, a file dropped
in by hand. The save then holds the new session, the image still holds the old one, and the capture
puts the old one back on top. Nothing errors. The sync is simply undone.

So `dsi\work.sum` lists every member of the save the image was last agreed with, by CRC32, size and
name, written at the two moments the two are in step - just after a capture, and just after a
rebuild. Before a capture the save is summed again: same, and the capture is the newest thing and
goes ahead; different, and somebody else got there first, so the image is dropped instead of written
and the next launch rebuilds around the save that arrived. An image is always rebuildable in a
quarter of a second; a save that came from elsewhere is not.

**A capture never reads an image somebody else is writing**, and that is a separate guard from the
receipt. Walking a live image does not fail - it produces a LIE: the filesystem is read half-updated,
files that are really there come back missing, and every one of them is recorded as a *deletion*,
which a restore then honours. Measured on a real evening: two walks 27 milliseconds apart, one
missing 56 of about 60 entries and the next 45, taken four seconds after melonDS's last write. What
came out was a save asking for the title's own `public.sav` to be deleted.

So the file itself is asked - `FileShare.None`, which succeeds only when nobody holds it at all -
and the answer means different things in two places.

**A capture cannot wait long**, because it is asked for while a window is being drawn: melonDS still
running means there is nothing to capture anyway, so it gives up at once rather than block the host
through somebody's game, and that session is written down at the next launch. When the process has
gone but the handle has not, it waits three seconds - measured, the gap is milliseconds.

**A LAUNCH waits properly, and that is the one that matters.** The image holds a session nobody has
written down; a launch captures it and then builds over it, so going ahead while melonDS still has
it is precisely how a session is lost - and the rebuild would fail anyway, since a file somebody has
open cannot be replaced. So a launch waits **three minutes**, silent for the first ten seconds and
then showing a window that says what is happening and offers to stop. Stopping does not start the
game on a state nobody can describe: it abandons the launch, leaves the session exactly where it is,
and the next launch finds it. Three seconds would have been the wrong number here - the question is
not "has a handle been released" but "has somebody finished playing".

**And removals are confirmed by reading twice.** A removal is the one thing in a delta that destroys
rather than restores, so when there are any, the image is walked again and the two walks have to
agree. The obvious guard instead - refuse a capture that deletes too much - was written and thrown
away: deleting several titles from the DSi menu removes their tickets, their contents and their data
in one go, which is an ordinary thing to do and would have lost the whole session silently. A second
walk asks a different question - *is this image still?* - and a quiet image answers the same twice
whether ten titles were deleted or none.

**The receipt reads the members, never the file's own bytes**, and that is deliberate even though
the bytes are now deterministic. Hashing the container would tie the decision "is this session worth
keeping" to whatever a zip writer does with its headers. The determinism is a convenience for the
host; it is not a foundation for us.

A restore still throws the image away itself rather than leaning on this, which is the one place
that duplication is deliberate. The receipt exists to notice writers who do not know about us; a
restore is *our own* doing, and laundering something known first-hand through a heuristic is a worse
answer than acting on it. It also covers what the receipt cannot: an installation upgraded from
before receipts existed has none yet, so "has the save moved" has no answer until the next capture
writes one - and unlike a delete, nothing else would stop the capture, since the reference walk
survives a restore.

**The check runs before anything decides whether to reuse, and it DROPS the image rather than merely
refusing it.** That ordering is the whole point and it is easy to get subtly wrong: a launch that
does not reuse goes on to *capture* the image first, so an image that was only refused would still
be written over the save that has just arrived - the guard undone by its own fallback. The same
question is asked again inside the capture, for the title that is not being launched: a launch of
game B captures whatever game A left in the image, and A's save may have been synced in the
meantime.

CRC32 rather than a cryptographic hash, on purpose: the question is "did this change", not "is this
what somebody claims it is". There is no adversary, only two writers who do not know about each
other. No receipt at all - an installation from before this existed - means no opinion, never
"assume the worst".

**Calling a save a container is a promise, and the host takes it literally.** It is worth recording
what that cost, because the answer is now "no" for all three kinds. `IsSaveContainer` used to say yes
for a DSiWare save, so the host made a destination folder, asked `TryBackupSave` to lay the save out
in it, and recorded a backup from whatever turned up. Refusing there does not produce "no backup" -
it produces an EMPTY FOLDER and no backup, once per session, with nothing on screen to say why. That
is exactly what happened: 0 backups after several evenings and a trail of empty directories in
`Saves\Nintendo DSiware\`, because the refusal was written when a DSiWare save was still one
extracted file and was never revisited when it became a folder.

That branch also carried the last hook before a backup: it refreshed the save first, since a backup
asked for outside the save window has not been through `GetSaves` and the working image may hold a
session nobody wrote down. Measured in the host, every route to a backup lists first - the on-close
service scans, the sweep scans per game, the Edit Game window rebuilds its groups - and listing is
what refreshes. The residue, if some host ever backed up without listing, is one session of lag.

A capture happens only when this title is the one the working image holds AND melonDS has written to
it since the last one, so the steady state costs two calls to `GetLastWriteTimeUtc`. A restore
replaces the save rather than merging into it - a state describes one moment, and a file that stopped
differing has to stop being restored - which one file gets for free where a folder had to be swapped
wholesale. Anything that is not an archive carrying `files.txt` is refused rather than half-applied.

**And a restore has to work on nothing at all**, because that is what a delete leaves behind: the
title folder is gone, the reference walk and the cached metadata with it. It does. The save is
written into a folder created on the spot, and the next launch rebuilds the image, reinstalls the
title, takes a fresh reference walk and applies the restored state onto it, in that order. Nothing in
the restore depends on what the delete removed. The guard at the top of the restore path once
demanded a *file* while a DSiWare backup was a folder, and rejected every one of them with "this
backup is not a file" before the folder branch three lines below could ever see it - a guard that
outlived a change of shape, which is now the shape it was written for. Back up, delete, restore is
one sequence the probe performs end to end, through the host's own entry points.

**A restore drops the working image; it does not write into it.** Applying a state onto an image that
has been played looks like the same thing and is not: the apply puts back the files the state names
and takes away nothing the current session added. Restore a save that has A and B onto an image that
has A, B and C, and C survives into play - and the next capture writes C back into the save,
growing the restored save back into what it was restored to be rid of. A state describes a fresh
install and nothing else, so that is the only surface it is allowed to land on. The image is scratch;
the next launch rebuilds it in a quarter of a second.

**A deletion has to reach three places, or it is not one.** *Delete Save* on a DSiWare row means "this
title has never been played", and three things hold that: the save file, the working image when it
still happens to hold the same title - the next question about this game's saves would capture it
straight back out - and, on an installation without the native library, the per-title image, which
*is* the save and where nothing else holds it.

So the whole title folder goes, not just the save inside it - the reference walk and the cached
`.tmd` are not saves and keeping them would cost nothing, but a folder named after the game still
sitting there after somebody deleted that game's save reads as a delete that did not work. Both are
recovered on the next launch, the metadata from the carried index, which is in the assembly and needs
no network.

The working image is **left where it is**, and used to be thrown away here. Two independent things
now stop it putting the save back, and neither of them is the delete's to remember: the reference
walk went with the folder, and nothing can be captured without one; and the receipt below no longer
matches a save that is not there, so the next launch drops the image before deciding
anything. An image nothing can read is scratch, and the next DSiWare launch rebuilds over it.

The title's `.tmd` is kept in `dsi\tmd\` rather than in the title's own folder, for the same reason
read the other way: it describes the TITLE, not the save. It used to sit inside the title folder,
which was tidy and wrong - a delete took it too. For the titles the carried index holds that costs
nothing; for one that came from Nintendo's server it costs a second download, and on a machine that
is offline at the next launch it costs the metadata altogether, leaving an unsigned TMD built from
the ROM that the DSi menu may refuse. A copy kept under the old arrangement is moved, not re-fetched.

Deleting the per-title image also loses a title imported by hand through Manage DSi titles, which the
log says out loud because it has to be done again. Refused while melonDS is running, since it will
write its session back on the way out.

**No RetroAchievements.** There is no `rcheevos` submodule, no vendored `rc_*` source, no menu entry
and no configuration key anywhere in the tree. RA support for melonDS exists only in the libretro
core, which is a different project.

**LaunchBox knows no standalone DS emulator at all.** Its metadata database has one `Nintendo DS`
row, RetroArch with the desmume core, so the row injection matters more here than anywhere else: it
is what puts melonDS in the Add Emulator window.

## Notes on no$gba

**The reason this plugin exists is one line of configuration.** no$gba writes its cartridge saves
COMPRESSED, in a container of its own, and it does so by default. Measured on Mario Kart DS:

```
default   25,487 bytes   "NocashGbaBackupMediaSavDataFile"
Raw      262,144 bytes   "MKDSSV10…"   - 256 KB exactly, the game's own signature
```

The first is readable by no$gba and by nothing else — not RetroArch, not melonDS, not DeSmuME, not a
save editor, not RomM, not Argosy. The second is the plain battery image everything speaks. The
setting has existed for years, it is three menus deep, and nothing anywhere suggests it matters.
The plugin sets it at install and checks it at every launch, because *Options ▸ Save Options*
rewrites the whole file from the running configuration and silently reverts it.

**Everything lives beside the executable, under a fixed name, and none of it is configurable.** The
full 2541-byte configuration no$gba writes for itself was read: it holds fifty-odd keys and **not one
path**. No save folder, no BIOS folder, no snapshot directory.

```
NO$GBA.INI      written only by Options ▸ Save Options - never at first run, never on exit
BATTERY\        cartridge saves, "<rom file name without its last extension>.SAV"
SNAP\           snapshots (F8 writes, F7 loads)
BIOSNDS7.ROM …  the BIOS and firmware files, by name, in this folder and nowhere else
```

So this plugin cannot point the emulator at anything; it can only put files where the emulator
already looks. BIOS files are therefore **copied** out of `Emulators\RetroArch\system\` — the same
folder the melonDS plugin declares — under the names no$gba wants. They are declared **optional**,
and that is not laziness: no$gba's default is to start a cartridge directly without running any boot
code, and it runs games perfectly well with none of them. They buy accuracy, not function.

**The INI is edited even though it says not to.** Its first line reads `;no$gba 3.0 generated config
file - do not edit`. Three things were measured before ignoring it: a partial file is valid (a
hand-written three-line INI was read and every unmentioned setting kept its default); no$gba never
rewrites it on exit (108 bytes in, 108 bytes out, byte for byte, across a full session); and a
**wrong value is ignored in silence**. That last one is the trap — `Reset/Startup Entrypoint ==
GBA/NDS BIOS` does nothing at all, because the value the emulator accepts is `GBA/NDS BIOS (Nintendo
logo)`, the exact label of the drop-down entry. No error, no log, no fallback. Every value this
plugin writes is a named constant carrying the menu label it was read from.

**Archives are the host's job, and that is a forced setting.** no$gba cannot open one. Handed a
`.zip` it puts up a modal box reading "Cartridge not found" and waits — which from a frontend is not
a failed launch but a hung one, behind a dialog nobody was expecting. LaunchBox has a flag for
exactly this, `Auto-Extract`, and the plugin turns it on and keeps it on. It is the only user setting
anything here overrides, because there is no configuration in which leaving it off is what somebody
wanted.

**There is no version to read, so the server's answer is the version.** The executable's version
resource says, literally, `FileVersion = "Windows version"`. The download is a single zip on the
author's own site at a URL that has not changed in years, so a `HEAD` request settles it:
`Last-Modified` becomes the label a person sees and the `ETag` is what an update check compares.
Nothing parses the HTML page. An installation this plugin did not make reports no version at all,
honestly, rather than one invented from a file size.

**Snapshots are not listed as saves.** F8 creates the `SNAP\` folder and left nothing in it across
several sessions, including clean exits. Declaring slots would mean describing a file format and a
naming scheme nobody here has seen; the folder is scanned and whatever turns up is named in the log
instead.

**DSiWare works, and it runs on the same engine melonDS does.** The machinery between a NAND dump
and a save - choosing a dump by region, building a configured console from it, installing the title,
taking the difference a session made, packing it as a `.dsisave`, rebuilding a console from its
recipe - talks to the IMAGE, not to the emulator. So it lives once, in `src/Shared.Dsi`, and both
plugins compile it in. That is also why this plugin is GPL-3.0: the engine calls melonDS's NAND
code. See `THIRD-PARTY.md`.

Measured on a real installation: a console built by the melonDS side booted under no$gba, our own
tool installed a title into it, the DSi menu ran it, and the walk afterwards found **the same set of
files a melonDS session produces** - the title's `public.sav`, `shared1/TWLCFG0.dat` and
`TWLCFG1.dat`, `shared2/launcher/wrap.bin`, two built-in apps' `private.sav` - plus
`sys/log/sysmenu.log`, the menu's own journal, which moves at every boot and so shows up in every
no$gba save whether or not anything was played.

**no$gba's documentation says it cannot install a DSiWare title into a NAND, and that is true.** It
does not matter: the emulator never installs anything here. This plugin does, through
`melonds-nand.dll`, and hands over a finished image. The same is true on the melonDS side, which
is why the two arrangements are one.

Three things are not like melonDS, and all three are measured:

```
no path setting      no$gba reads a FIXED name beside its exe, DSi-1.mmc, and its INI holds
                     fifty-odd keys and not one path. So the working image IS that file.
no boot without      started with no ROM it sits idle - no logo, and the eMMC is never even
  a cartridge        opened. The title's own .nds goes in the slot to turn the machine on;
                     the title itself is launched from the DSi menu, out of the NAND.
two global settings  NDS Mode/Colors == DSi (retail/16MB)
                     Reset/Startup Entrypoint == GBA/NDS BIOS (Nintendo logo)
                     written per launch and written back, since there is no command line
                     to carry them and a GBA game must not boot through a BIOS.
```

Those two values are the drop-down's own labels, character for character. **A value no$gba does not
recognise is ignored in silence** - no error, no fallback, nothing in the file to see - so both are
named constants carrying the label they were read from, and the probe asserts them.

**A fixed file name can already be taken**, which melonDS never has to worry about: its working
image lives in a folder this plugin owns, so the path answers "is this mine". Here it says nothing,
and somebody who set a DSi up by hand has their own `DSi-1.mmc` sitting exactly there. The marker
beside the working image answers instead - `work.title` is written whenever we build one - and an
image with no marker is moved to `DSi-1.mmc.yours` rather than built over. Never deleted.

**Setting a console up costs two copies of 240 MB here, once per console, ever.** Without a path
setting, "run the emulator on this image" means "make this image be DSi-1.mmc", and then put it
back. melonDS is simply pointed at the copy.

**No RetroAchievements.** no$gba predates the idea and is a single packed executable with no network
features beyond its own link emulation.

## License

MIT for most of it, see `LICENSE`.

**Four directories are GPL-3.0-or-later**, and they are the ones that touch melonDS's NAND code:
`tools/melonds-nand`, `src/Shared.Dsi`, `src/MelonDs` and `src/NoGba`. The shared DSi engine calls
that library through P/Invoke, and both plugins compile the engine in, so the licence follows it.
Flycast, Xenia and PPSSPP touch none of it and remain MIT. `THIRD-PARTY.md` sets out the whole of
it, including the LGPL-2.1 component the shipped binary statically links and how the relinking
requirement is met.
