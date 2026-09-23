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

// melonds-nandtool - the command line over the same API the plugin calls.
//
// The integration plugin talks to melonds-nand.dll directly; this executable exists so a person can
// do the same thing by hand - look inside a NAND, install a title, pull a save out - without a
// frontend and without the Manage DSi titles dialog. It is deliberately a thin client of nand_api.h
// so that what it does and what the plugin does cannot drift apart.
//
// Exit codes are meant to be read by a program: 0 success, 1 the answer is no (a title that is not
// installed), 2 a usage error, 3 a failure while working.

#include "nand_api.h"

#include <cstdio>
#include <cstdlib>
#include <cstring>
#include <string>
#include <vector>

namespace
{

constexpr int ExitOk = 0;
constexpr int ExitNo = 1;
constexpr int ExitUsage = 2;
constexpr int ExitFailed = 3;

void Usage()
{
    std::fprintf(stderr,
        "melonds-nandtool - install and inspect DSiWare titles in a melonDS DSi NAND\n"
        "\n"
        "usage: melonds-nandtool --nand <nand.bin> --bios7 <dsi_bios7.bin> <command>\n"
        "\n"
        "commands:\n"
        "  info                            print the console id read out of the image\n"
        "  list                            print every installed title id, one per line\n"
        "  exists --title <16 hex>         exit 0 when installed, 1 when not\n"
        "  import --app <rom.nds> [--tmd <file>] [--readonly]\n"
        "                                  install a DSiWare title; the .tmd defaults to\n"
        "                                  <rom.nds>.tmd beside it\n"
        "  delete --title <16 hex>         remove a title and its data\n"
        "  export-save --title <16 hex> --type <public|private|banner> --out <file>\n"
        "  import-save --title <16 hex> --type <public|private|banner> --in <file>\n"
        "\n"
        "exit codes: 0 ok, 1 no, 2 usage, 3 failed\n");
}

const char* Arg(int argc, char** argv, const char* name)
{
    for (int i = 1; i < argc - 1; i++)
        if (std::strcmp(argv[i], name) == 0) return argv[i + 1];
    return nullptr;
}

bool Flag(int argc, char** argv, const char* name)
{
    for (int i = 1; i < argc; i++)
        if (std::strcmp(argv[i], name) == 0) return true;
    return false;
}

/// A 16-hex-digit title id, high word then low, the way the NAND tree spells it.
bool ParseTitleId(const char* text, unsigned int& category, unsigned int& id)
{
    if (!text || std::strlen(text) != 16) return false;
    char* end = nullptr;
    std::string high(text, 8), low(text + 8, 8);
    category = (unsigned int)std::strtoul(high.c_str(), &end, 16);
    if (*end) return false;
    id = (unsigned int)std::strtoul(low.c_str(), &end, 16);
    return *end == 0;
}

int ParseKind(const char* text)
{
    if (!text) return -1;
    if (std::strcmp(text, "public") == 0) return MDSNAND_SAVE_PUBLIC;
    if (std::strcmp(text, "private") == 0) return MDSNAND_SAVE_PRIVATE;
    if (std::strcmp(text, "banner") == 0) return MDSNAND_SAVE_BANNER;
    return -1;
}

/// Turn an API code into an exit code, printing whatever the library had to say.
int Report(mdsnand_handle* handle, int code)
{
    if (code == MDSNAND_OK) return ExitOk;
    if (code == MDSNAND_NO) return ExitNo;
    const char* message = mdsnand_last_error(handle);
    if (message && *message) std::fprintf(stderr, "%s\n", message);
    return ExitFailed;
}

int Run(int argc, char** argv, mdsnand_handle* nand, const char* command)
{
    if (std::strcmp(command, "info") == 0)
    {
        std::printf("%016llx\n", mdsnand_console_id(nand));
        return ExitOk;
    }

    if (std::strcmp(command, "list") == 0)
    {
        // Ask for the count first, then read it: the library reports how many there are even when
        // the buffer is too small, so there is no guessing at a size.
        int count = mdsnand_list_titles(nand, MDSNAND_CATEGORY_DSIWARE, nullptr, 0);
        if (count < 0) return Report(nand, count);

        std::vector<unsigned int> ids((size_t)count);
        if (count > 0) mdsnand_list_titles(nand, MDSNAND_CATEGORY_DSIWARE, ids.data(), count);
        for (unsigned int id : ids) std::printf("%08x%08x\n", MDSNAND_CATEGORY_DSIWARE, id);
        return ExitOk;
    }

    if (std::strcmp(command, "exists") == 0)
    {
        unsigned int category, id;
        if (!ParseTitleId(Arg(argc, argv, "--title"), category, id))
        { std::fprintf(stderr, "--title wants 16 hex digits\n"); return ExitUsage; }
        return Report(nand, mdsnand_title_exists(nand, category, id));
    }

    if (std::strcmp(command, "import") == 0)
    {
        const char* app = Arg(argc, argv, "--app");
        if (!app) { std::fprintf(stderr, "import wants --app\n"); return ExitUsage; }

        int code = mdsnand_import_title(nand, app, Arg(argc, argv, "--tmd"),
                                        Flag(argc, argv, "--readonly") ? 1 : 0);
        if (code == MDSNAND_OK)
        {
            unsigned int category = 0, id = 0;
            if (mdsnand_read_title_id(app, &category, &id) == MDSNAND_OK)
                std::printf("%08x%08x\n", category, id);
        }
        return Report(nand, code);
    }

    if (std::strcmp(command, "delete") == 0)
    {
        unsigned int category, id;
        if (!ParseTitleId(Arg(argc, argv, "--title"), category, id))
        { std::fprintf(stderr, "--title wants 16 hex digits\n"); return ExitUsage; }
        return Report(nand, mdsnand_delete_title(nand, category, id));
    }

    bool exporting = std::strcmp(command, "export-save") == 0;
    if (exporting || std::strcmp(command, "import-save") == 0)
    {
        unsigned int category, id;
        if (!ParseTitleId(Arg(argc, argv, "--title"), category, id))
        { std::fprintf(stderr, "--title wants 16 hex digits\n"); return ExitUsage; }

        int kind = ParseKind(Arg(argc, argv, "--type"));
        if (kind < 0) { std::fprintf(stderr, "--type wants public, private or banner\n"); return ExitUsage; }

        const char* file = Arg(argc, argv, exporting ? "--out" : "--in");
        if (!file)
        { std::fprintf(stderr, "%s wants %s\n", command, exporting ? "--out" : "--in"); return ExitUsage; }

        return Report(nand, exporting ? mdsnand_export_save(nand, category, id, kind, file)
                                      : mdsnand_import_save(nand, category, id, kind, file));
    }

    std::fprintf(stderr, "unknown command: %s\n", command);
    return ExitUsage;
}

}

int main(int argc, char** argv)
{
    if (argc < 2) { Usage(); return ExitUsage; }

    const char* nandPath = Arg(argc, argv, "--nand");
    const char* biosPath = Arg(argc, argv, "--bios7");

    // The command is the first argument that is neither an option nor an option's value.
    const char* command = nullptr;
    for (int i = 1; i < argc; i++)
    {
        if (argv[i][0] == '-') { if (i + 1 < argc && argv[i + 1][0] != '-') i++; continue; }
        command = argv[i];
        break;
    }

    if (!command) { Usage(); return ExitUsage; }
    if (!nandPath || !biosPath)
    { std::fprintf(stderr, "--nand and --bios7 are both required\n"); return ExitUsage; }

    mdsnand_handle* nand = nullptr;
    if (mdsnand_open(nandPath, biosPath, &nand) != MDSNAND_OK)
    {
        std::fprintf(stderr, "%s\n", mdsnand_last_error(nullptr));
        return ExitFailed;
    }

    int result = Run(argc, argv, nand, command);
    mdsnand_close(nand);
    return result;
}
