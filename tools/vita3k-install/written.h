// SPDX-License-Identifier: GPL-2.0-or-later
// Every file a decrypt writes, with its size and SHA-1 - computed AS IT IS WRITTEN, from the bytes on
// their way to the disk, never by reading the file back.
//
// WHY: the plugin fingerprints a freshly installed game before it boots, and reading back files that
// were just written is the expensive way - real-time antivirus scans each new file on its first open
// (measured on the firmware: a walk of fresh copies 3985 ms, the same walk warm 503 ms). The bytes all
// pass through here anyway; hashing them on the way costs no read at all.
//
// Two ways a decrypted tree gets its files, both in psvpfsparser's Utils.cpp (our copy, pfs\Utils.cpp):
//   create_empty_file    opens the output stream every decrypted file is written through - track()
//                        puts a hashing buffer in front of it
//   copy_existing_file   copies a file the PFS layer does not encrypt, then cuts it to size -
//                        copied() hashes the SOURCE's first `size` bytes, which the copy has just read
//
// Recording is off unless begin() was called, so the command-line tool and the firmware install are
// untouched.
//
// CONSUMING THE SOURCE. With `consume`, each encrypted source file is deleted as soon as its
// decrypted copy is finished - so a staging copy and the decrypted tree, on the same RAM disk, never
// both hold the whole game: the peak is the game plus its largest file, not twice the game. A
// decrypted file is known to be finished when the next one starts (psvpfsparser works one file at a
// time) or when recording ends; a copied one, as soon as it is hashed. Only files under
// `source_root` are ever deleted.

#pragma once

#include <cstdint>
#include <filesystem>
#include <fstream>
#include <string>
#include <vector>

namespace v3k::written {

struct File {
    std::filesystem::path path;   // as written - absolute
    std::uint64_t size = 0;
    std::string sha1;             // upper-case hex, the manifest's own spelling
};

void begin(bool consume = false, const std::filesystem::path &source_root = {});
void track(std::ofstream &stream, const std::filesystem::path &path, const std::filesystem::path &source = {});
void copied(const std::filesystem::path &source, const std::filesystem::path &destination, std::uint64_t size);

// Finish every hash and stop recording. A path written twice keeps its last write.
std::vector<File> end();

// How many source files the last recording consumed.
std::size_t consumed();

} // namespace v3k::written
