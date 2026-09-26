// SPDX-License-Identifier: GPL-2.0-or-later
// The three operations, shared by the DLL (api.cpp) and the command-line tool (main.cpp).
//
// Each returns 0 on success and fills `message` with one line: "OK ..." or "ERROR ...". Progress is
// reported as (done, total) - bytes for decrypt, percent out of 100 for firmware.

#pragma once

#include <cstdint>
#include <filesystem>
#include <functional>
#include <string>
#include <vector>

#include "written.h"

namespace v3k {

using Progress = std::function<void(std::uint64_t done, std::uint64_t total)>;

// `files`, when given, receives every file written with its size and SHA-1, hashed on the way to the
// disk (written.h) - so the caller need not read the tree back to fingerprint it.
int decrypt(const std::filesystem::path &src, const std::filesystem::path &licence,
            const std::filesystem::path &dst, bool verbose, const Progress &progress, std::string &message,
            std::vector<written::File> *files = nullptr);

int firmware(const std::filesystem::path &pup, const std::filesystem::path &vita_fs, bool verbose,
             const Progress &progress, std::string &message);

int selftest(std::string &report);

} // namespace v3k
