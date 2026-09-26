// SPDX-License-Identifier: GPL-2.0-or-later
// Decrypting an app STRAIGHT FROM ITS ZIP - no copy of the encrypted game anywhere.
//
// WHY: psvpfsparser wants a folder that looks like the encrypted app, so the plugin used to unpack
// the whole archive before decrypting it. On a RAM disk that is the game twice (an ImDisk drive holds
// its whole declared size in memory for the session); in %TEMP% it was the game written to the system
// disk at every launch. Neither is acceptable.
//
// HOW: the folder psvpfsparser is given still looks like the app, but holds almost nothing:
//   - the index (sce_pfs/), the package folder, and every small entry are extracted for real -
//     psvpfsparser opens those directly, by path (files.db, unicv.db, keystone, sealedkey...)
//   - every other entry becomes a SPARSE PLACEHOLDER of the right size: no cluster allocated, but
//     the listings and file_size calls psvpfsparser makes in several places see what they expect
//   - every read of a data file goes through sce_junction::open, in OUR copy of Utils.cpp; there the
//     stream is handed a buffer that decompresses the zip entry chunk by chunk as it is read
//   - the few unencrypted files, copied by copy_existing_file (also in our copy), are written
//     straight from the entry
// It holds because psvpfsparser reads every source front to back - read() only, never seekg - which
// is all a compressed entry allows. Measured on the sources: the mount reads the FIRST SECTOR of each
// file (a unicv image); only the old icv format reads them whole at mount.

#pragma once

#include <cstdint>
#include <filesystem>
#include <fstream>
#include <string>

namespace v3k::zipsource {

// Open `zip` and lay out `stage` from the entries under `root` (UTF-8, "" or "foo/"). Entries of at
// most `small_limit` bytes, and everything under sce_pfs/ and sce_sys/package/, are extracted; the
// rest become placeholders. False, with `error`, on anything it cannot do - the caller then falls
// back to unpacking.
bool open(const std::filesystem::path &zip, const std::string &root, const std::filesystem::path &stage,
          std::uint64_t small_limit, std::string &error, std::size_t &placeholders, std::size_t &extracted);

void close();
bool active();

// Hand `in` a buffer reading the entry behind the placeholder `real`. False when it has none - the
// caller opens the file itself, as before.
bool attach(std::ifstream &in, const std::filesystem::path &real);

enum class Copy { NotMapped, Ok, Failed };

// Write the entry behind `real` to `destination`, cut or zero-padded to `size` - what
// copy_existing_file does from a file, done from the zip.
Copy copy_to(const std::filesystem::path &real, const std::filesystem::path &destination, std::uint64_t size);

// Did any read fail (a corrupt entry, a truncated archive)? The decrypt is then not to be trusted.
bool failed();

} // namespace v3k::zipsource
