// SPDX-License-Identifier: GPL-2.0-or-later
// The firmware operation: Vita3K's own install_pup (vita3k/packages/src/pup.cpp), compiled unmodified
// and called directly - the same code `Vita3K.exe --firmware` runs, without the rest of the emulator
// starting around it.
//
// Its own file because the firmware sources see `fs` as the namespace from shim/util/fs.h, and
// core.cpp's `namespace fs = std::filesystem` alias cannot live beside it.
//
// Progress is install_pup's own: it calls back at 10, 20, 30, 70 and 100 - coarse, and the long
// stretch is 30 to 70, the segment decryption. It is what the emulator's own window shows too.

#include <packages/functions.h>
#include <util/log.h>

#include <exception>
#include <string>

int run_firmware(const std::filesystem::path &pup, const std::filesystem::path &vita_fs, bool verbose,
                 const std::function<void(std::uint32_t)> &progress, std::string &version, std::string &error)
{
    vita3k_shim::verbose = verbose;
    try {
        version = install_pup(vita_fs, pup, progress);
        return 0;
    } catch (const std::exception &e) {
        error = e.what();
        return -1;
    }
}
