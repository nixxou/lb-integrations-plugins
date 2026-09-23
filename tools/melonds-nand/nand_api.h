/*
    Copyright 2026 nixxou

    This file is part of melonds-nand, which is built together with source files from melonDS
    (Copyright 2016-2025 melonDS team) and is therefore distributed under the same terms.

    melonds-nand is free software: you can redistribute it and/or modify it under the terms of the
    GNU General Public License as published by the Free Software Foundation, either version 3 of the
    License, or (at your option) any later version.

    It is distributed in the hope that it will be useful, but WITHOUT ANY WARRANTY; without even the
    implied warranty of MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the GNU General
    Public License for more details.

    You should have received a copy of the GNU General Public License along with this program. If
    not, see http://www.gnu.org/licenses/.
*/

// A plain C door onto melonDS's DSi NAND code.
//
// Everything here is a thin pass-through to DSi_NAND.cpp: open an image, ask what is in it, install
// or remove a title, move a save in or out. No operation is reimplemented, because a NAND writer
// that differs from the emulator's is worse than none.
//
// C AND NOT C++ ON PURPOSE. This is what the LaunchBox integration plugin calls through P/Invoke, so
// the boundary is char*, int and an opaque handle - no std::string, no exceptions crossing, no
// assumptions about which compiler built the other side.
//
// ONE NAND AT A TIME, and that is melonDS's constraint rather than a simplification. NANDMount has
// its move constructor deleted because "fatfs maintains a global pointer" to the mounted filesystem
// (DSi_NAND.h:134-137). Two mounts alive at once would have the second quietly steal the first's
// filesystem. mdsnand_open therefore refuses while a handle is open, and says so.
//
// STRINGS ARE UTF-8. Windows paths with accents survive because the implementation widens them
// before touching the filesystem, which is what melonDS's own Platform layer does.

#ifndef MELONDS_NAND_API_H
#define MELONDS_NAND_API_H

#ifdef __cplusplus
extern "C" {
#endif

#ifdef MDSNAND_BUILDING
#define MDSNAND_API __declspec(dllexport)
#else
#define MDSNAND_API __declspec(dllimport)
#endif

/// Result codes. Negative is a failure, 0 is success, and 1 means "the answer is no" - a title that
/// is not installed is not an error.
#define MDSNAND_OK              0
#define MDSNAND_NO              1
#define MDSNAND_ERR_ARGUMENT   -1
#define MDSNAND_ERR_BUSY       -2   /* a NAND is already open */
#define MDSNAND_ERR_OPEN       -3   /* the file could not be opened */
#define MDSNAND_ERR_DECRYPT    -4   /* not a NAND this BIOS can decrypt */
#define MDSNAND_ERR_MOUNT      -5   /* no filesystem inside it */
#define MDSNAND_ERR_FAILED     -6   /* the operation itself failed */

/// Save data kinds, matching melonDS's TitleData_* (DSi_NAND.h:37-42).
#define MDSNAND_SAVE_PUBLIC     0
#define MDSNAND_SAVE_PRIVATE    1
#define MDSNAND_SAVE_BANNER     2

/// The DSiWare title category. Every id this plugin deals with has this as its high word.
#define MDSNAND_CATEGORY_DSIWARE 0x00030004u

typedef struct mdsnand_handle mdsnand_handle;

/// The version of this ABI. Bumped whenever a signature changes, so a caller can refuse a library
/// it does not understand instead of crashing on a mismatched call.
MDSNAND_API int mdsnand_abi_version(void);

/// Was the last import done with a TMD we built ourselves rather than one found on disk? Reset at
/// the start of every mdsnand_import_title. Meant for a caller that wants to say so out loud.
MDSNAND_API int mdsnand_last_tmd_was_generated(void);

/// Open a NAND image. `bios7Path` is the DSi ARM7 BIOS, which carries the ES key at 0x8308.
/// Returns MDSNAND_OK and fills `out`, or a negative code; `mdsnand_last_error` explains.
MDSNAND_API int mdsnand_open(const char* nandPath, const char* bios7Path, mdsnand_handle** out);

MDSNAND_API void mdsnand_close(mdsnand_handle* handle);

/// The console id read out of the image, which is what proves it was decrypted rather than merely
/// opened. Zero when the handle is null.
MDSNAND_API unsigned long long mdsnand_console_id(mdsnand_handle* handle);

/// MDSNAND_OK when the title is installed, MDSNAND_NO when it is not.
MDSNAND_API int mdsnand_title_exists(mdsnand_handle* handle, unsigned int category, unsigned int titleId);

/// Every title id in `category`, written into `ids` (at most `capacity`). Returns the count found,
/// which may exceed `capacity` - call again with a bigger buffer - or a negative code.
MDSNAND_API int mdsnand_list_titles(mdsnand_handle* handle, unsigned int category,
                                    unsigned int* ids, int capacity);

/// Install a DSiWare title from its .nds.
///
/// `tmdPath` may be null, in which case "<appPath>.tmd" is used. A TMD that describes a different
/// title is refused, and a failed write is cleaned up after.
///
/// WHEN THERE IS NO TMD AT ALL, one is BUILT from the .nds - because everything melonDS reads out of
/// a TMD is already in the ROM. Measured in DSi_NAND.cpp rather than assumed: ImportTitle uses the
/// title id and the content id, InitTitleFileStructure takes the save sizes from the ROM HEADER
/// (`header.DSiPublicSavSize`, `header.DSiPrivateSavSize`, lines 1078-1082), and nothing anywhere
/// verifies the content hash or the signature. So a bare .nds is enough, which is what most DSiWare
/// dumps are. Ask mdsnand_last_tmd_was_generated afterwards to know which happened.
///
/// The signature is left zeroed, as fake-signed tools do. melonDS does not look at it; a real DSi
/// would, which is worth knowing if the NAND ever leaves the emulator.
MDSNAND_API int mdsnand_import_title(mdsnand_handle* handle, const char* appPath,
                                     const char* tmdPath, int readOnly);

MDSNAND_API int mdsnand_delete_title(mdsnand_handle* handle, unsigned int category, unsigned int titleId);

/// Copy one of the title's save files out of the NAND, or back into it. `kind` is MDSNAND_SAVE_*.
MDSNAND_API int mdsnand_export_save(mdsnand_handle* handle, unsigned int category, unsigned int titleId,
                                    int kind, const char* outPath);

MDSNAND_API int mdsnand_import_save(mdsnand_handle* handle, unsigned int category, unsigned int titleId,
                                    int kind, const char* inPath);

/// Copy one file out of the NAND's filesystem, by its path inside the image - for example
/// "0:/title/00030004/4b393945/content/title.tmd". For looking at what an install actually wrote,
/// which is the only way to compare ours with the one melonDS's own dialog makes.
MDSNAND_API int mdsnand_export_file(mdsnand_handle* handle, const char* nandPath, const char* outPath);

/// The last failure on this handle, as UTF-8, or "" when there was none. Valid until the next call
/// on the same handle. Pass null for failures that happened before a handle existed.
MDSNAND_API const char* mdsnand_last_error(mdsnand_handle* handle);

/// The title id a .nds carries, read from its header at 0x230. Useful before opening anything.
MDSNAND_API int mdsnand_read_title_id(const char* appPath, unsigned int* category, unsigned int* titleId);

#ifdef __cplusplus
}
#endif

#endif
