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

// The implementation of nand_api.h. Every function is a guard, a call into melonDS, and a message.
//
// NOTHING IS REIMPLEMENTED HERE. The decryption, the FAT access, the title tree, the ticket - all of
// it is DSi_NAND.cpp. What this file adds is a door that C# can knock on: no exceptions crossing the
// boundary, no C++ types in the signatures, and a readable sentence for every failure.

#define MDSNAND_BUILDING
#include "nand_api.h"

#include "DSi_NAND.h"
#include "DSi_TMD.h"
#include "Platform.h"

#include "sha1/sha1.hpp"

// fatfs directly, for the walk. The filesystem is already mounted by NANDMount - it keeps a global
// pointer, which is why only one NAND can be open at a time - so these calls act on it.
#include "fatfs/ff.h"

#include <algorithm>

#include <cstdio>
#include <cstring>
#include <memory>
#include <string>
#include <vector>

using namespace melonDS;

namespace
{

/// The one live mount. melonDS's NANDMount cannot be moved because fatfs keeps a global pointer to
/// the mounted filesystem, so a second one would silently displace the first.
bool g_open = false;

/// For failures that happen before there is a handle to hang a message on.
std::string g_globalError;

constexpr long EsKeyYOffset = 0x8308;

}

struct mdsnand_handle
{
    std::unique_ptr<DSi_NAND::NANDImage> Image;
    std::unique_ptr<DSi_NAND::NANDMount> Mount;
    std::string Error;

    int Fail(int code, std::string message)
    {
        Error = std::move(message);
        return code;
    }

    void Clear() { Error.clear(); }
};

namespace
{

int GlobalFail(int code, std::string message)
{
    g_globalError = std::move(message);
    return code;
}

bool ReadEsKeyY(const char* biosPath, DSi_NAND::DSiKey& key)
{
    FILE* f = std::fopen(biosPath, "rb");
    if (!f) return false;
    bool ok = _fseeki64(f, EsKeyYOffset, SEEK_SET) == 0
              && std::fread(key.data(), 1, key.size(), f) == key.size();
    std::fclose(f);
    return ok;
}

/// Whether the last import built its own TMD. A single flag rather than a field on the handle: the
/// question is about the call, and the caller asks immediately afterwards.
bool g_tmdGenerated = false;

void PutBE16(u8* out, unsigned int value)
{
    out[0] = (u8)(value >> 8); out[1] = (u8)value;
}

void PutLE32(u8* out, unsigned int value)
{
    out[0] = (u8)value;         out[1] = (u8)(value >> 8);
    out[2] = (u8)(value >> 16); out[3] = (u8)(value >> 24);
}

void PutBE32(u8* out, unsigned int value)
{
    out[0] = (u8)(value >> 24); out[1] = (u8)(value >> 16);
    out[2] = (u8)(value >> 8);  out[3] = (u8)value;
}

/// Build the TMD a DSiWare title would have come with, from the ROM itself.
///
/// Every field melonDS reads is filled from the .nds header, whose layout was measured on a real
/// DSiWare rather than taken from a struct: title id at 0x230 (low word first), public save size at
/// 0x238, private at 0x23C. The rest is filled the way a real TMD has it, so that what lands in the
/// NAND looks like what belongs there.
bool BuildTmd(const char* appPath, DSi_TMD::TitleMetadata& tmd)
{
    FILE* f = std::fopen(appPath, "rb");
    if (!f) return false;

    u8 header[0x300] = {};
    bool ok = std::fread(header, 1, sizeof(header), f) == sizeof(header);
    if (!ok) { std::fclose(f); return false; }

    // The content is the whole ROM, and its hash is over the whole file.
    if (_fseeki64(f, 0, SEEK_END) != 0) { std::fclose(f); return false; }
    long long length = _ftelli64(f);
    _fseeki64(f, 0, SEEK_SET);

    SHA1_CTX sha;
    SHA1Init(&sha);
    std::vector<u8> chunk(1 << 20);
    for (;;)
    {
        size_t read = std::fread(chunk.data(), 1, chunk.size(), f);
        if (read == 0) break;
        SHA1Update(&sha, chunk.data(), (uint32_t)read);
    }
    u8 digest[20] = {};
    SHA1Final(digest, &sha);
    std::fclose(f);

    auto le32 = [&](int offset) -> unsigned int
    {
        return (unsigned int)header[offset] | ((unsigned int)header[offset + 1] << 8)
             | ((unsigned int)header[offset + 2] << 16) | ((unsigned int)header[offset + 3] << 24);
    };

    unsigned int titleLow = le32(0x230);
    unsigned int titleHigh = le32(0x234);
    unsigned int publicSave = le32(0x238);
    unsigned int privateSave = le32(0x23C);

    std::memset(&tmd, 0, sizeof(tmd));

    // RSA-2048 with SHA-1, the type every DSi TMD carries. The signature itself stays zero: this is
    // fake-signed, exactly like the TMDs the homebrew tools produce, and melonDS checks neither.
    PutBE32((u8*)&tmd.SignatureType, 0x00010001);
    std::snprintf(tmd.SignatureName, sizeof(tmd.SignatureName), "Root-CA00000001-CP00000007");

    PutBE32(tmd.TitleId, titleHigh);
    PutBE32(tmd.TitleId + 4, titleLow);
    // LITTLE-endian, and that is measured rather than reasoned. A TMD downloaded from Nintendo for
    // 99Bullets carries 00 40 00 00 where the ROM header says 0x4000 - so the field is stored the
    // same way round as the header, not byte-swapped like the title id beside it. melonDS's own
    // GetPublicSaveSize reads it big-endian, which disagrees with every real TMD; it gets away with
    // it because InitTitleFileStructure sizes the save from the ROM header instead.
    PutLE32(tmd.PublicSaveSize, publicSave);
    PutLE32(tmd.PrivateSaveSize, privateSave);

    tmd.SrlFlag = 0;                      // 0 in every real TMD, checked against a downloaded one
    PutBE16((u8*)&tmd.NumberOfContents, 1);
    PutBE16((u8*)&tmd.BootContentIndex, 0);

    // Content 0, version 0 - so melonDS names the installed file 00000000.app.
    PutBE32(tmd.Contents.ContentId, 0);
    PutBE16(tmd.Contents.ContentIndex, 0);
    PutBE16(tmd.Contents.ContentType, 1);
    PutBE32(tmd.Contents.ContentSize, (unsigned int)(length >> 32));
    PutBE32(tmd.Contents.ContentSize + 4, (unsigned int)(length & 0xFFFFFFFFu));
    std::memcpy(tmd.Contents.ContentSha1Hash, digest, sizeof(digest));

    return true;
}

bool ReadTmd(const std::string& path, DSi_TMD::TitleMetadata& tmd)
{
    FILE* f = std::fopen(path.c_str(), "rb");
    if (!f) return false;
    bool ok = std::fread(&tmd, sizeof(tmd), 1, f) == 1;
    std::fclose(f);
    return ok;
}

bool Valid(mdsnand_handle* handle)
{
    return handle && handle->Mount && *handle->Mount;
}

}

extern "C"
{

int mdsnand_abi_version(void) { return 4; }

int mdsnand_last_tmd_was_generated(void) { return g_tmdGenerated ? 1 : 0; }

int mdsnand_open(const char* nandPath, const char* bios7Path, mdsnand_handle** out)
{
    if (!nandPath || !bios7Path || !out)
        return GlobalFail(MDSNAND_ERR_ARGUMENT, "a required argument was null");
    *out = nullptr;

    if (g_open)
        return GlobalFail(MDSNAND_ERR_BUSY,
            "another NAND is already open; melonDS's mount keeps a global filesystem pointer, "
            "so only one can be used at a time");

    DSi_NAND::DSiKey esKeyY {};
    if (!ReadEsKeyY(bios7Path, esKeyY))
        return GlobalFail(MDSNAND_ERR_OPEN,
            std::string("cannot read the ES key from ") + bios7Path
            + " - it must be a DSi ARM7 BIOS of at least 0x8318 bytes");

    auto* file = Platform::OpenLocalFile(nandPath, Platform::FileMode::ReadWriteExisting);
    if (!file)
        return GlobalFail(MDSNAND_ERR_OPEN,
            std::string("cannot open ") + nandPath + " for writing");

    auto handle = std::make_unique<mdsnand_handle>();
    handle->Image = std::make_unique<DSi_NAND::NANDImage>(file, esKeyY);
    if (!*handle->Image)
        return GlobalFail(MDSNAND_ERR_DECRYPT,
            std::string(nandPath) + " is not a NAND this BIOS can decrypt - check that the dump and "
            "dsi_bios7.bin come from the same console");

    handle->Mount = std::make_unique<DSi_NAND::NANDMount>(*handle->Image);
    if (!*handle->Mount)
        return GlobalFail(MDSNAND_ERR_MOUNT,
            std::string("no filesystem could be mounted inside ") + nandPath);

    g_open = true;
    *out = handle.release();
    g_globalError.clear();
    return MDSNAND_OK;
}

void mdsnand_close(mdsnand_handle* handle)
{
    if (!handle) return;
    // The mount goes first: it holds a reference to the image.
    handle->Mount.reset();
    handle->Image.reset();
    delete handle;
    g_open = false;
}

unsigned long long mdsnand_console_id(mdsnand_handle* handle)
{
    return handle && handle->Image ? handle->Image->GetConsoleID() : 0ull;
}

int mdsnand_title_exists(mdsnand_handle* handle, unsigned int category, unsigned int titleId)
{
    if (!Valid(handle)) return MDSNAND_ERR_ARGUMENT;
    handle->Clear();
    return handle->Mount->TitleExists(category, titleId) ? MDSNAND_OK : MDSNAND_NO;
}

int mdsnand_list_titles(mdsnand_handle* handle, unsigned int category, unsigned int* ids, int capacity)
{
    if (!Valid(handle) || capacity < 0 || (capacity > 0 && !ids)) return MDSNAND_ERR_ARGUMENT;
    handle->Clear();

    std::vector<u32> found;
    handle->Mount->ListTitles(category, found);

    int n = (int)found.size();
    for (int i = 0; i < n && i < capacity; i++) ids[i] = found[i];
    return n;                                    // the caller compares this with capacity
}

int mdsnand_import_title(mdsnand_handle* handle, const char* appPath, const char* tmdPath, int readOnly)
{
    if (!Valid(handle) || !appPath) return MDSNAND_ERR_ARGUMENT;
    handle->Clear();
    g_tmdGenerated = false;

    unsigned int category = 0, titleId = 0;
    if (mdsnand_read_title_id(appPath, &category, &titleId) != MDSNAND_OK)
        return handle->Fail(MDSNAND_ERR_FAILED, std::string("cannot read a DS header from ") + appPath);

    if (category != MDSNAND_CATEGORY_DSIWARE)
    {
        char buffer[128];
        std::snprintf(buffer, sizeof(buffer),
                      "this is not DSiWare: its title category is %08x, not 00030004", category);
        return handle->Fail(MDSNAND_ERR_FAILED, buffer);
    }

    DSi_TMD::TitleMetadata tmd {};
    std::string tmdFile = tmdPath ? std::string(tmdPath) : std::string(appPath) + ".tmd";
    if (!ReadTmd(tmdFile, tmd))
    {
        // No TMD on disk: build one. Everything melonDS reads out of a TMD is in the ROM - see the
        // note on mdsnand_import_title - so a bare .nds, which is what most DSiWare dumps are, is
        // enough. An explicit --tmd that could not be read is still an error: the caller named a
        // file and meant it.
        if (tmdPath)
            return handle->Fail(MDSNAND_ERR_FAILED, "no usable TMD at " + tmdFile);

        if (!BuildTmd(appPath, tmd))
            return handle->Fail(MDSNAND_ERR_FAILED,
                std::string("no TMD beside ") + appPath + ", and one could not be built from it");
        g_tmdGenerated = true;
    }

    if (tmd.GetCategory() != category || tmd.GetID() != titleId)
    {
        char buffer[160];
        std::snprintf(buffer, sizeof(buffer),
                      "the TMD describes title %08x%08x but the ROM is %08x%08x",
                      tmd.GetCategory(), tmd.GetID(), category, titleId);
        return handle->Fail(MDSNAND_ERR_FAILED, buffer);
    }

    if (handle->Mount->TitleExists(category, titleId)) return MDSNAND_OK;

    // Clear anything half-installed that would get in the way, as melonDS's own importer does.
    handle->Mount->DeleteTitle(category, titleId);

    if (!handle->Mount->ImportTitle(appPath, tmd, readOnly != 0))
    {
        handle->Mount->DeleteTitle(category, titleId);   // never leave a partial install behind
        return handle->Fail(MDSNAND_ERR_FAILED,
            std::string("importing ") + appPath + " failed; check that the NAND dump is valid");
    }
    return MDSNAND_OK;
}

int mdsnand_delete_title(mdsnand_handle* handle, unsigned int category, unsigned int titleId)
{
    if (!Valid(handle)) return MDSNAND_ERR_ARGUMENT;
    handle->Clear();
    if (!handle->Mount->TitleExists(category, titleId)) return MDSNAND_NO;
    handle->Mount->DeleteTitle(category, titleId);
    return MDSNAND_OK;
}

int mdsnand_export_save(mdsnand_handle* handle, unsigned int category, unsigned int titleId,
                        int kind, const char* outPath)
{
    if (!Valid(handle) || !outPath) return MDSNAND_ERR_ARGUMENT;
    handle->Clear();
    if (!handle->Mount->TitleExists(category, titleId)) return MDSNAND_NO;

    if (!handle->Mount->ExportTitleData(category, titleId, kind, outPath))
        return handle->Fail(MDSNAND_ERR_FAILED,
            std::string("could not write the save to ") + outPath);
    return MDSNAND_OK;
}

int mdsnand_import_save(mdsnand_handle* handle, unsigned int category, unsigned int titleId,
                        int kind, const char* inPath)
{
    if (!Valid(handle) || !inPath) return MDSNAND_ERR_ARGUMENT;
    handle->Clear();
    if (!handle->Mount->TitleExists(category, titleId)) return MDSNAND_NO;

    if (!handle->Mount->ImportTitleData(category, titleId, kind, inPath))
        return handle->Fail(MDSNAND_ERR_FAILED,
            std::string("could not read the save from ") + inPath
            + " - melonDS refuses one whose size does not match the title's");
    return MDSNAND_OK;
}

namespace
{

/// One file's SHA-1, read through fatfs in chunks rather than into one buffer: a NAND holds files of
/// a few bytes and files of several megabytes, and there is no reason to size for the worst.
bool HashInNand(const char* path, unsigned long long& size, char out[41])
{
    FF_FIL file;
    if (f_open(&file, path, FA_OPEN_EXISTING | FA_READ) != FR_OK) return false;

    SHA1_CTX sha;
    SHA1Init(&sha);
    std::vector<u8> chunk(64 * 1024);
    size = 0;
    for (;;)
    {
        UINT read = 0;
        if (f_read(&file, chunk.data(), (UINT)chunk.size(), &read) != FR_OK) { f_close(&file); return false; }
        if (read == 0) break;
        SHA1Update(&sha, chunk.data(), (uint32_t)read);
        size += read;
    }
    f_close(&file);

    u8 digest[20] = {};
    SHA1Final(digest, &sha);
    for (int i = 0; i < 20; i++) std::snprintf(out + i * 2, 3, "%02x", digest[i]);
    out[40] = 0;
    return true;
}

/// Depth-first, collecting lines. Recursion depth is bounded by the tree itself - a DSi NAND is a
/// handful of levels - but it is capped anyway rather than trusting an image we did not write.
void WalkInto(const std::string& path, std::vector<std::string>& lines, int depth)
{
    if (depth > 16) return;

    FF_DIR dir;
    if (f_opendir(&dir, path.c_str()) != FR_OK) return;

    std::vector<std::string> subdirs;
    for (;;)
    {
        FF_FILINFO info;
        if (f_readdir(&dir, &info) != FR_OK) break;
        if (!info.fname[0]) break;

        std::string full = path + "/" + info.fname;
        if (info.fattrib & AM_DIR)
        {
            lines.push_back("D -\t-\t" + full);
            subdirs.push_back(full);
            continue;
        }

        unsigned long long size = 0;
        char digest[41] = {};
        if (!HashInNand(full.c_str(), size, digest))
        {
            lines.push_back("F ?\t?\t" + full);      // there, but unreadable - worth saying so
            continue;
        }
        lines.push_back("F " + std::to_string(size) + "\t" + digest + "\t" + full);
    }
    f_closedir(&dir);

    // The directory is closed before descending: fatfs holds state per open directory, and a walk
    // that kept one open per level would hold as many as the tree is deep for no reason.
    for (const auto& sub : subdirs) WalkInto(sub, lines, depth + 1);
}

}

int mdsnand_import_file(mdsnand_handle* handle, const char* nandPath, const char* inPath)
{
    if (!Valid(handle) || !nandPath || !inPath) return MDSNAND_ERR_ARGUMENT;
    handle->Clear();

    if (!handle->Mount->ImportFile(nandPath, inPath))
        return handle->Fail(MDSNAND_ERR_FAILED,
            std::string("could not write ") + inPath + " into the NAND at " + nandPath);
    return MDSNAND_OK;
}

int mdsnand_remove_file(mdsnand_handle* handle, const char* nandPath)
{
    if (!Valid(handle) || !nandPath) return MDSNAND_ERR_ARGUMENT;
    handle->Clear();

    // RemoveFile returns void upstream, so the only way to know is to look afterwards.
    handle->Mount->RemoveFile(nandPath);

    FF_FILINFO info;
    if (f_stat(nandPath, &info) == FR_OK)
        return handle->Fail(MDSNAND_ERR_FAILED,
            std::string("could not remove ") + nandPath + " - it is still there");
    return MDSNAND_OK;
}

int mdsnand_walk(mdsnand_handle* handle, const char* root, const char* manifestPath)
{
    if (!Valid(handle) || !manifestPath) return MDSNAND_ERR_ARGUMENT;
    handle->Clear();

    std::string start = (root && *root) ? root : "0:";
    while (start.size() > 1 && start.back() == '/') start.pop_back();

    std::vector<std::string> lines;
    WalkInto(start, lines, 0);

    // Sorted, so two manifests can be compared line by line. fatfs returns directory entries in
    // whatever order they sit in the FAT, which is an artefact of when things were written.
    std::sort(lines.begin(), lines.end(),
              [](const std::string& a, const std::string& b)
              {
                  auto pa = a.find_last_of('\t'), pb = b.find_last_of('\t');
                  return a.compare(pa, std::string::npos, b, pb, std::string::npos) < 0;
              });

    FILE* out = std::fopen(manifestPath, "wb");
    if (!out)
        return handle->Fail(MDSNAND_ERR_OPEN, std::string("cannot write ") + manifestPath);
    for (const auto& line : lines) std::fprintf(out, "%s\n", line.c_str());
    std::fclose(out);

    return (int)lines.size();
}

int mdsnand_export_file(mdsnand_handle* handle, const char* nandPath, const char* outPath)
{
    if (!Valid(handle) || !nandPath || !outPath) return MDSNAND_ERR_ARGUMENT;
    handle->Clear();

    if (!handle->Mount->ExportFile(nandPath, outPath))
        return handle->Fail(MDSNAND_ERR_FAILED,
            std::string("could not read ") + nandPath + " out of the NAND");
    return MDSNAND_OK;
}

const char* mdsnand_last_error(mdsnand_handle* handle)
{
    if (handle) return handle->Error.c_str();
    return g_globalError.c_str();
}

int mdsnand_read_title_id(const char* appPath, unsigned int* category, unsigned int* titleId)
{
    if (!appPath || !category || !titleId) return MDSNAND_ERR_ARGUMENT;

    FILE* f = std::fopen(appPath, "rb");
    if (!f) return GlobalFail(MDSNAND_ERR_OPEN, std::string("cannot open ") + appPath);

    // 0x230 holds the low word, 0x234 the high one - the reverse of how an id is written.
    unsigned int words[2] = {0, 0};
    bool ok = _fseeki64(f, 0x230, SEEK_SET) == 0 && std::fread(words, 4, 2, f) == 2;
    std::fclose(f);
    if (!ok) return GlobalFail(MDSNAND_ERR_FAILED, std::string(appPath) + " is too short to be a DS ROM");

    *titleId = words[0];
    *category = words[1];
    return MDSNAND_OK;
}

}
