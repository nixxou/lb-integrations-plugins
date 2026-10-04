# Third-party components

Each plugin is built by merging its dependencies into a single assembly with ILRepack
`/internalize`. The components below are therefore **statically linked** into the shipped file, and
their licences apply to it.

The merge is not cosmetic. A LaunchBox plugin folder is a shared namespace: the host resolves a
dependency by simple name across every plugin's folder, so two plugins carrying different versions
of the same library is a real hazard. After the merge the assembly's reference table holds exactly
one non-framework entry — `Unbroken.LaunchBox.Plugins` — so there is nothing left to collide.

## Merged into `Xenia.dll`

| Component | Licence | Used for |
|---|---|---|
| [SharpCompress](https://github.com/adamhathcock/sharpcompress) | MIT | reading the release archive - canary's Windows asset became a `.7z` in June 2026 |
| [ZstdSharp](https://github.com/oleg-st/ZstdSharp) | MIT | the zstd of a ZArchive (`.zar`) - `src/Shared.Zar`, our reader of the [ZArchive](https://github.com/Exzap/ZArchive) format (MIT No Attribution) |
| [Lib.Harmony](https://github.com/pardeike/Harmony) | MIT | the two postfixes that let LaunchBox read our emulator rows |

## Under its own licence inside `Xenia.dll`

`src/Xenia/Xdvdfs.cs` is **MPL-2.0**, derived from `sigil/src/xdvdfs.c` in
[argosy-launcher](https://github.com/abduznik/Freegosy). Its structure, its two-magic validation and
its partition-base probing order are sigil's work; the C# is ours. MPL-2.0 is file-scoped copyleft:
that one file stays MPL and its source is in this repository, which is what the licence asks.

## Merged into `Cxbx.dll`

| Component | Licence | Used for |
|---|---|---|
| [SharpCompress](https://github.com/adamhathcock/sharpcompress) | MIT | the release archive, the zips and 7z a game comes in, and the save files |
| [ZstdSharp](https://github.com/oleg-st/ZstdSharp) | MIT | the zstd of a ZArchive (`.zar`), read by `src/Shared.Zar` |
| [CHDSharp](https://github.com/purelogiccode/CHDSharp) | MIT | reading `.chd` disc images (`src/Shared.Disc`) |
| VendoredLZMA (LZMA SDK) | public domain | a CHD codec |
| VendoredZLib | zlib | a CHD codec |
| VendoredZSTD | MIT | a CHD codec |
| **VendoredFlac** | **LGPL-2.1** | a CHD codec - see "LGPL-2.1 and how it is satisfied" |
| Microsoft.Extensions.Logging.Abstractions | MIT | a CHDSharp dependency |
| Microsoft.Extensions.DependencyInjection.Abstractions | MIT | idem |
| System.Diagnostics.DiagnosticSource | MIT | idem |
| System.IO.Hashing | MIT | idem |
| [Lib.Harmony](https://github.com/pardeike/Harmony) | MIT | the postfixes that let LaunchBox read our emulator row |

The CSO and CCI readers (`src/Shared.Disc/SectorImage.cs`, with its LZ4 block decoder) are ours, written from
the formats: CSO as [antangelo/ciso](https://github.com/antangelo/ciso) writes it, CCI as Team Resurgent's
XboxToolkit reads it - its layout only, none of its (GPL-3.0) code.

## Under its own licence inside `Cxbx.dll`

`src/Cxbx/Xdvdfs.cs` is **MPL-2.0**: `src/Xenia/Xdvdfs.cs` (derived from sigil's `xdvdfs.c`, see above)
extended to the whole tree. Same terms: that one file stays MPL and its source is in this repository.

Nothing of Cxbx-Reloaded (GPL-2.0) is in here. The plugin downloads its builds from GitHub at the
user's request and runs them; it links none of its code and carries none of its files.

## Merged into `Xemu.dll`

The same components as `Cxbx.dll` above (SharpCompress, ZstdSharp, CHDSharp and its codecs and dependencies,
Lib.Harmony), under the same licences, VendoredFlac's LGPL-2.1 satisfied the same way. `src/Cxbx/Xdvdfs.cs` is
compiled in (MPL-2.0, see above). Nothing of xemu (GPL-2.0) is in here: the plugin downloads its builds and xemu's
dashboard disk (xemu-project/xemu-dashboard) from GitHub at the user's request.

## Under its own licence inside `Xemu.dll` - GPL-2.0-or-later

| Component | Licence | Used for |
|---|---|---|
| [XboxEepromEditor](https://github.com/Ernegien/XboxEepromEditor) (fork: [nixxou/XboxEepromEditor](https://github.com/nixxou/XboxEepromEditor)) - `Cryptography/HmacSha1.cs`, `Cryptography/RC4.cs`, `Types/EepromVersion.cs`, the time zone table of `Eeprom.cs` | GPL-2.0-or-later | opening and sealing an original Xbox's EEPROM: its security section (the game region) is RC4-encrypted under an HMAC-SHA1 key with the Xbox's own constants |

Copied into `src/Xemu/Eeprom/` with their notices, the namespace changed and nothing else; the licence text is
`src/Xemu/Eeprom/LICENSE.txt`. Compiled into the plugin, they make **`src/Xemu` GPL-2.0-or-later** - see
`src/Xemu/LICENSE.md`. The installer that carries `Xemu.dll` is GPL-3.0-or-later already (below), which GPL-2.0-or-later
allows.

## Merged into `SuperZsnes.dll`

| Component | Licence | Used for |
|---|---|---|
| [SharpCompress](https://github.com/adamhathcock/sharpcompress) | MIT | reading the release archive - a plain zip today, read by sniffing so a change of container upstream costs nothing |
| [Lib.Harmony](https://github.com/pardeike/Harmony) | MIT | the postfixes that let LaunchBox read our emulator row |

Nothing of SUPER ZSNES itself is in here, and nothing could be: it is closed source. The plugin
downloads the build from zsnes.com at the user's request, reads what the About box and the site say
about its version, and runs it. It links none of its code and redistributes none of its files.

## Loaded into the emulator by `tools/superzsnes-bepinex`

The in-process plugin (`SuperZsnes.BepInEx.dll`, MIT, ours) runs under these, which it references
at run time from the emulator's `BepInEx\` folder and never merges or carries:

| Component | Licence | Role |
|---|---|---|
| [BepInEx 6](https://github.com/BepInEx/BepInEx) (IL2CPP, bleeding edge) | LGPL-2.1 | the loader: Doorstop's `winhttp.dll`, a .NET 6 runtime in the process, the chainloader |
| [HarmonyX](https://github.com/BepInEx/HarmonyX) | MIT | the detours, resolved by name |
| [Il2CppInterop](https://github.com/BepInEx/Il2CppInterop) | LGPL-3.0 | the generated interop assemblies the plugin compiles against |
| [Cpp2IL](https://github.com/SamboyCoding/Cpp2IL) | MIT | reads the IL2CPP metadata for the generator |

None of them is redistributed by this repository. `SuperZsnes.dll` **downloads** BepInEx at
install time, from its own builds site (`builds.bepinex.dev`), a pinned bleeding-edge build
(`6.0.0-be.788+5b766a3`) verified against a sha256 written in `SuperZsnesBepInEx.cs`, and extracts
the unmodified archive over the emulator folder. It then writes a `BepInEx.cfg` that turns the
console and the disk log off, its own plugin into `BepInEx\plugins\`, and `docs\superzsnes\*.md`
into `BepInEx\nixx-docs\`. Nothing of BepInEx is inside our binaries.

## Merged into `Vita3k.dll`

| Component | Licence | Used for |
|---|---|---|
| [SharpCompress](https://github.com/adamhathcock/sharpcompress) | MIT | reading the release archive - Vita3K publishes a flat `.zip`, but the reader picks its decoder by sniffing rather than by extension |
| [Lib.Harmony](https://github.com/pardeike/Harmony) | MIT | the postfixes that let LaunchBox read our emulator row |

Nothing of Vita3K itself is in here. The plugin downloads their build, runs their executable and
reads what it writes; it compiles none of their code and links none of their libraries. The PS Vita
firmware it fetches is Sony's, fetched from Sony's own update servers at the user's request and
never redistributed.

It does LOAD one library built from their code: `native\vita3k-install.native`, which decrypts a
NoNpDRM dump and installs the firmware the way their installer does. That library is described under
`tools/vita3k-install` below, and loading it into the plugin's process is why **`src/Vita3k` is
GPL-2.0-or-later** - see its `LICENSE.md`.

## Merged into `NoGba.dll`

| Component | Licence | Used for |
|---|---|---|
| [SharpCompress](https://github.com/adamhathcock/sharpcompress) | MIT | reading the release archive, and reading a game archive's entry name - no$gba cannot open an archive itself |
| [Harmony](https://github.com/pardeike/Harmony) | MIT | describing no$gba to LaunchBox, whose metadata database has no row for it |

## Merged into `Flycast.dll`

| Component | Licence | Used for |
|---|---|---|
| [CHDSharp](https://github.com/purelogiccode/CHDSharp) | MIT | reading `.chd` disc images |
| VendoredLZMA (LZMA SDK) | public domain | a CHD codec |
| VendoredZLib | zlib | a CHD codec |
| VendoredZSTD | MIT | a CHD codec |
| **VendoredFlac** | **LGPL-2.1** | a CHD codec - see below |
| Microsoft.Extensions.Logging.Abstractions | MIT | a CHDSharp dependency |
| Microsoft.Extensions.DependencyInjection.Abstractions | MIT | idem |
| System.Diagnostics.DiagnosticSource | MIT | idem |
| System.IO.Hashing | MIT | idem |
| [Lib.Harmony](https://github.com/pardeike/Harmony) | MIT | the two postfixes that let LaunchBox read our emulator rows |

Dreamcast discs are distributed as `.chd` as often as `.gdi`, and the disc id lives inside the
image, so reading CHD is not optional here. Flycast's release archive is a plain `.zip`, so unlike
the Xenia plugin this one needs no SharpCompress - `System.IO.Compression` reads it.

## Merged into `MelonDs.dll`

| Component | Licence | Used for |
|---|---|---|
| [SharpCompress](https://github.com/adamhathcock/sharpcompress) | MIT | reading a game archive - melonDS loads a DS ROM out of a `.zip`, `.7z` or `.rar`, and the save is named after the entry inside it |
| ZstdSharp | MIT | a SharpCompress dependency |
| [Lib.Harmony](https://github.com/pardeike/Harmony) | MIT | the two postfixes that let LaunchBox read our emulator rows |

The version of Harmony is pinned to 2.4.2 across every plugin that uses it, and to what ExtendDB
ships: `HarmonySharedState` is found by module name across assemblies, so two different versions in
one process would each keep their own state.

## Merged into `Ppsspp.dll`

| Component | Licence | Used for |
|---|---|---|
| [CHDSharp](https://github.com/purelogiccode/CHDSharp) | MIT | reading `.chd` disc images |
| VendoredLZMA (LZMA SDK) | public domain | a CHD codec |
| VendoredZLib | zlib | a CHD codec |
| VendoredZSTD | MIT | a CHD codec |
| [Lib.Harmony](https://github.com/pardeike/Harmony) | MIT | the two postfixes that let LaunchBox read our emulator rows |
| **VendoredFlac** | **LGPL-2.1** | a CHD codec — see below |
| Microsoft.Extensions.Logging.Abstractions | MIT | a CHDSharp dependency |
| Microsoft.Extensions.DependencyInjection.Abstractions | MIT | idem |
| System.Diagnostics.DiagnosticSource | MIT | idem |
| System.IO.Hashing | MIT | idem |

CHDSharp's licence asks for its authors to be named where it is used: **CHDSharp by Peterson Fernandes
(@purelogiccode) and Gordon Jefferyes (@gjefferyes)** - in the PPSSPP, Flycast and Cxbx plugins and the RAM
disk helper.

All four CHD codecs are needed to open a CHD, not just the one a given file uses: `chdman` writes
its whole codec list into the header by default (`lzma, zlib, huff, flac`), and the codec table is
built at open time. Removing the FLAC decoder makes reads throw — measured, not assumed.

## LGPL-2.1 and how it is satisfied

VendoredFlac is LGPL-2.1, and merging it in is static linking. LGPL-2.1 §6 requires that a recipient
be able to relink the work with a modified version of the library. That is satisfied here by
**§6(a)**: this repository is public and holds the complete corresponding source of the plugin,
together with the exact build step that performs the merge, so anyone can substitute their own build
of the FLAC decoder and rebuild.

Concretely, to relink with a different FLAC:

```
dotnet tool restore                      # pins ILRepack, see dotnet-tools.json
dotnet build src\Ppsspp\Ppsspp.csproj -c Release
dotnet build src\Flycast\Flycast.csproj -c Release
dotnet build src\Cxbx\Cxbx.csproj -c Release
dotnet build tools\ramdisk-helper\RamDiskHelper.csproj -c Release   # embedded there, not merged: swap the resource
```

The merge is the `MergePlugin` target at the end of `src\Ppsspp\Ppsspp.csproj`. Replace
`VendoredFlac.dll` in `src\Ppsspp\bin\Release\` before that target runs — or change the
`CHDSharp` package reference — and the merged output picks up your copy.

## `tools/melonds-nand`, `src/Shared.Dsi`, `src/MelonDs` and `src/NoGba` - GPL-3.0

Four directories in this repository are **not** MIT, and they are the ones that touch melonDS's
NAND code.

`tools/melonds-nand` is compiled together with source files from
[melonDS](https://github.com/melonDS-emu/melonDS) - `DSi_NAND.cpp`, `FATIO.cpp`, FatFs, tiny-AES-c
and SHA-1 - which are GPL-3.0-or-later. Its own sources carry that notice, and
`tools/melonds-nand/LICENSE` holds the licence text. It builds two things: `melonds-nand.dll`, the C
door the plugin calls, and `melonds-nandtool.exe`, the same operations from a command line.

| Component | Licence | Used for |
|---|---|---|
| [melonDS](https://github.com/melonDS-emu/melonDS) | GPL-3.0-or-later | the whole DSi NAND implementation: decryption, FAT access, title import, save export |

**`src/Shared.Dsi` is GPL-3.0-or-later**, because it is the code that calls that library: the DSi
engine - choosing a dump, building a console, installing a title, taking the difference a session
made, packing it as a `.dsisave`, rebuilding a console from a recipe. It lived inside `src/MelonDs`
until no$gba needed the same machinery, and it is compiled into both plugins rather than copied,
because four thousand lines that took three measured defects to get right should not exist twice.

**`src/MelonDs` and `src/NoGba` are GPL-3.0-or-later with it**, because each one compiles that engine
in and therefore loads the library into its own process through P/Invoke - see the LICENSE.md in
each. An earlier version invoked an executable at arm's length and stayed MIT; direct calls were
chosen knowingly, since reading a DSiWare save out of its NAND happens while a page is drawn rather
than once per launch, and the same reasoning now covers both emulators.

This paragraph used to say the other plugins stayed MIT *because* this repository copies its shared
pieces rather than linking them. That is still true of `Log.cs` and `Archives.cs`, where two copies
differ by a namespace and a comment. It stopped being true of the DSi engine the day a second
emulator needed it, and of the metadata-row trio the day a fourth and fifth plugin published rows -
both now live in a folder of their own, `src/Shared.Dsi` and `src/Shared.Lbip`, compiled into the
plugins that need them. The licence follows the code rather than the other way round, and
`src/Shared.Lbip` changes nothing here: it is this repository's own work, MIT like the rest of it.

**Flycast, Xenia and PPSSPP are unaffected and remain MIT**: none of them touches the NAND library,
the shared DSi folder, or any assembly that does.

Two files in the tool are melonDS's own work rather than ours, and say so at the top:
`aes_key.cpp` carries `DSi_AES::ROL16` and `DSi_AES::DeriveNormalKey` copied verbatim from
`src/DSi_AES.cpp`, because compiling that file would have pulled in most of the emulator for twenty
lines of arithmetic.

## `tools/vita3k-install` - GPL-2.0-or-later

`tools/vita3k-install` builds two products from one set of objects, as `tools/melonds-nand` does -
`vita3k-install.dll`, the C door the Vita3K plugin loads (shipped as `native\vita3k-install.native`),
and `vita3k-installtool.exe`, the same operations from a command line. They do the two installs
[Vita3K](https://github.com/Vita3K/Vita3K) performs and a plain unzip cannot:

- **`decrypt`** - an installed app through its PFS layer, the step Vita3K's installer performs in
  `decrypt_install_nonpdrm` for a NoNpDRM dump;
- **`firmware`** - a PS Vita system update (`.PUP`) into a virtual filesystem, Vita3K's `install_pup`,
  which `Vita3K.exe --firmware` runs with the rest of the emulator starting around it.

Every source it takes from Vita3K is compiled **unmodified**, from a checkout - **except one**:
psvpfsparser's `Utils.cpp` is compiled from a copy, `tools/vita3k-install/pfs/Utils.cpp`, with three
changes marked `LBIP:` - two calls that hash every file a decrypt writes as it is written, and delete
its encrypted source once it is done when asked to (see `tools/vita3k-install/written.h`); a path
computation done as a string instead of through `std::filesystem::relative`, which fails on a RAM
disk; and every read of a source file served from the game's zip when the file is only a placeholder
(see `tools/vita3k-install/zipsource.h`), so an encrypted game is decrypted without any copy of it. The build refuses to run if the original changes, so the copy cannot silently drift from it.

| Component | Licence | Used for |
|---|---|---|
| [Vita3K](https://github.com/Vita3K/Vita3K) - `vita3k/packages/src/pup.cpp`, `sce_utils.cpp`, `exfat.cpp` | GPL-2.0-or-later | reading the PUP, decrypting its SCE segments with the key table `sce_utils.cpp` carries, extracting the FAT16 and exFAT partitions |
| [libfat16](https://github.com/Vita3K/libfat16) | MIT | reading the FAT16 partition images (`os0`, `vs0`, `sa0`) |
| [miniz](https://github.com/richgel999/miniz) | MIT | inflating the compressed firmware segments, and reading a game straight from its zip |
| [vita-toolchain](https://github.com/vitasdk/vita-toolchain) - `src/self.h` only | MIT | the SELF header structures `sce_utils.cpp` names |
| psvpfsparser ([Vita3K fork](https://github.com/Vita3K/psvpfsparser), originally by motoharu-gosuto) | **none stated** - see below | parsing `files.db` / `unicv.db` and decrypting every file of an app |

What those sources reach for outside the C++ standard library is supplied by this repository's own
shims under `shim\`, not by the libraries themselves: OpenSSL (nine EVP calls, AES-128/256 CBC and
AES-128 CTR, decryption only - `shim\evp_cng.cpp`, over Windows CNG), boost (Vita3K's `fs` is
boost::filesystem - `shim\util\fs.h`), fmt and spdlog (`shim\util\*.h`). psvpfsparser's own crypto
interface is answered by `cng_crypto.cpp`, and of its sources the tool leaves out every one that needs
OpenSSL, zRIF or boost.

**psvpfsparser carries no licence file**, in the Vita3K fork or upstream, and this repository does not
invent one for it. Vita3K distributes it inside a GPL-2.0-or-later program without further notice;
this tool does the same, and its own sources (`api.cpp`, `core.*`, `main.cpp`, `firmware.cpp`,
`cng_crypto.*`, `shim\`) are offered under GPL-2.0-or-later like the Vita3K code they are compiled
with. That is Vita3K's posture,
reproduced rather than improved on - worth settling before a release carries the binary.

The same applies to the SCE key table in `sce_utils.cpp`: it is Vita3K's, published in their
repository, compiled here as it stands.

**Measured against the emulator itself**, not merely built: the crypto against published known answers
(`vita3k-installtool selftest`: FIPS-197, SP 800-38A, RFC 4493, 2202, 4231); `decrypt` against a game
installed by Vita3K - 35 files, byte-identical; `firmware` against a firmware installed by Vita3K - all
four partitions, 1825 files, byte-identical, from a folder deeper than MAX_PATH that the emulator
itself crashes in.

## `src/Installer` - the single-file release

`release/NixxIntegrations.exe` carries every merged plugin as an embedded resource, alongside the
.NET runtime, `melonds-nand` and the RAM disk helper. Two of those plugins - `MelonDs.dll` and `NoGba.dll` - are
GPL-3.0-or-later, so **the installer is distributed under GPL-3.0-or-later as well**, and its
`LICENSE.md` says so.

That is the cautious reading rather than the only one. The installer does not link against anything
it carries: the plugins are opaque bytes to it, written to disk and never loaded, which is close to
what the GPL calls mere aggregation. The cautious reading was taken because it costs nothing here -
every line of the installer is this repository's own work, and nobody is worse off for it being
GPL - and because "it is probably aggregation" is a poor thing to discover you were wrong about
after publishing.

`tools/ramdisk-helper` is this repository's own work, but for what its Xbox view reads with (1.7+): it
compiles `src/Cxbx/Xdvdfs.cs` (MPL-2.0, see above) and `src/Shared.Zar` / `src/Shared.Disc`, and EMBEDS
ZstdSharp, CHDSharp and CHDSharp's codecs and dependencies - the components and licences of the `Cxbx.dll`
table, VendoredFlac (LGPL-2.1) included, satisfied the same way. It reads a key-value file and shells to
`imdisk.exe` / `aim_ll.exe`. **ImDisk itself is never bundled** - not by this pack
and not by LiteBox. It is a separate free download the user installs, and the only thing either
product does with it is check whether `System32\\imdisk.exe` is there.

`tmd-library.bin` travels inside `MelonDs.dll` and therefore inside the release. That is the same
decision `.gitignore` has left open since it was written, now more visible for being published
rather than built locally.

## Not merged

`src\Catalog` builds `LbIntegrations.Catalog.dll`, this repository's own work and MIT like the rest
of it. It is referenced by every plugin and deliberately **not** merged: it declares the interface a
host and a plugin both have to name, and type identity in .NET is per-assembly - internalized into
five DLLs it would become five private types no host could reach. It ships beside each plugin
instead, which is the one exception to one managed file per plugin folder. It has no dependencies
and its version is pinned, because one copy of it serves the whole process.

`vendor\Unbroken.LaunchBox.Plugins.dll` is Unbroken Software's SDK. It is referenced with
`<Private>false</Private>` and deliberately **not** merged: the host must provide it at runtime so
the plugin's `EmulatorPlugin` and `IEmulator` types are the same types the host uses. It is
committed to this repository only so the projects build without a LaunchBox installation.

## This repository

Everything written here is MIT, `LICENSE`.
