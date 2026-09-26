// SPDX-License-Identifier: GPL-2.0-or-later
// vita3k-installtool: the operations of vita3k-install.dll from a command line - for doing them by
// hand, and for the comparisons against installs made by Vita3K itself. The plugin does not run it;
// it loads the DLL. Same objects, see CMakeLists.txt.
//
//   vita3k-installtool decrypt  <app dir> <licence> <destination dir> [--verbose]
//   vita3k-installtool firmware <update.pup> <vita fs dir>            [--verbose]
//   vita3k-installtool selftest
//
// Output: one final line on stdout, "OK ..." or "ERROR ...", and exit code 0 / 1. With --verbose the
// library's chatter and "PROGRESS done/total" lines go to stderr.

#include "core.h"

#include <cstdio>
#include <cwchar>
#include <string>
#include <vector>

namespace fs = std::filesystem;

int wmain(int argc, wchar_t **argv)
{
    bool verbose = false;
    std::vector<std::wstring> args;
    for (int i = 1; i < argc; i++) {
        if (std::wcscmp(argv[i], L"--verbose") == 0) verbose = true;
        else args.push_back(argv[i]);
    }

    v3k::Progress progress = nullptr;
    if (verbose)
        progress = [](std::uint64_t done, std::uint64_t total) {
            std::fprintf(stderr, "PROGRESS %llu/%llu\n", (unsigned long long)done, (unsigned long long)total);
        };

    std::string message;
    int result = 2;

    if (args.size() == 1 && args[0] == L"selftest") {
        result = v3k::selftest(message);
        std::fputs(message.c_str(), stdout);
        return result;
    }
    if (args.size() == 4 && args[0] == L"decrypt")
        result = v3k::decrypt(fs::path(args[1]), fs::path(args[2]), fs::path(args[3]), verbose, progress, message);
    else if (args.size() == 3 && args[0] == L"firmware")
        result = v3k::firmware(fs::path(args[1]), fs::path(args[2]), verbose, progress, message);
    else {
        std::printf("usage: vita3k-installtool decrypt <app dir> <licence> <destination dir> [--verbose]\n"
                    "       vita3k-installtool firmware <update.pup> <vita fs dir> [--verbose]\n"
                    "       vita3k-installtool selftest\n");
        return 2;
    }

    std::printf("%s\n", message.c_str());
    return result;
}
