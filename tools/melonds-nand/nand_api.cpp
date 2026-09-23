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
    PutBE32(tmd.PublicSaveSize, publicSave);
    PutBE32(tmd.PrivateSaveSize, privateSave);

    tmd.SrlFlag = 1;                      // this content is an SRL, which a .nds is
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

int mdsnand_abi_version(void) { return 2; }

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
