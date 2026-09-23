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
  `list`, `exists`, `import`, `delete`, `export-save`, `import-save`.

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

It writes nothing unless you ask it to. These modes do — the last two only inside the temp folder:

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

--melonds
    the whole melonDS contract against a forged install: both save dispositions, the .ml<n>
    slots, the name taken from inside an archive, the TOML writer, the DS/DSi/DSiWare decision
    read out of a ROM header, and the per-title NAND - that it is copied and not invented,
    that your own dump is never written to, and that one title's NAND never seeds another's.
    When this build carries the metadata index it also reads that index a second way, from
    the resource and by the packer's own documented layout, and requires the two readings to
    agree.
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

**DSiWare gets one NAND per title, under `dsi\`.** A DSiWare title is not a cartridge: melonDS boots
it out of the NAND, and its save lives there too, at `title/<category>/<id>/data/public.sav`
(`DSi_NAND.cpp:1077-1092`). The NAND is therefore the game's container rather than a side file, and
one per title makes that container self-standing - copied, backed up or deleted without touching any
other game.

```
<install>\dsi\base.bin                  your own dump, copied here once
<install>\dsi\0003000412345678\nand.bin  one per title id
```

On the first launch of a DSiWare game the plugin captures whatever `DSi.NANDPath` points at as
`base.bin` - **before** overwriting it, or the original would be lost as a source - copies it for that
title, points `DSi.NANDPath` at the copy and switches to DSi mode. Later launches reuse it. A
per-title NAND is never taken as the base for another title; that would seed one game's state into
every other. Free space is checked first: a dump is around 240 MB. The `no-dsi-nand` marker beside
the log turns the whole thing off.

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

**A DSiWare save is extracted, not the image that holds it.** It lives inside the NAND, at
`title/<category>/<id>/data/public.sav` - a few kilobytes inside 240 MB. Handing the image to the host
would mean hashing all of it to draw a freshness dot, and a 240 MB vault copy per backup. So the save
is exported beside its NAND and THAT is what is listed, as a plain file like every other save here.

The NAND stays the truth. The extraction happens only when melonDS has written to the image since the
last one - the trigger is the NAND's own timestamp, so the steady state costs two calls to
`GetLastWriteTimeUtc` and nothing else. A restore goes back **through** the NAND and the extract is
then re-read, so what the page shows is what the emulator will load. A deletion is refused rather than
faked: removing the extracted copy would change nothing in the game, and melonDS has no way to blank a
title's save short of removing the title.

**No RetroAchievements.** There is no `rcheevos` submodule, no vendored `rc_*` source, no menu entry
and no configuration key anywhere in the tree. RA support for melonDS exists only in the libretro
core, which is a different project.

**LaunchBox knows no standalone DS emulator at all.** Its metadata database has one `Nintendo DS`
row, RetroArch with the desmume core, so the row injection matters more here than anywhere else: it
is what puts melonDS in the Add Emulator window.

## License

MIT, see `LICENSE`. The shipped binary statically links third-party components, one of them
LGPL-2.1 — `THIRD-PARTY.md` lists them and explains how the relinking requirement is met.
