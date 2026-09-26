// SPDX-License-Identifier: GPL-2.0-or-later
// Stand-in for Vita3K's util/fs.h, so its firmware code compiles WITHOUT boost and fmt.
//
// Vita3K's `fs` is boost::filesystem, with boost's fs::ifstream / fs::ofstream. Here it is the
// standard library's, plus those two names: std::ifstream and std::ofstream take a
// std::filesystem::path since C++17. Only what pup.cpp, sce_utils.cpp and exfat.cpp actually use is
// provided - measured by grep, see CMakeLists.txt - and each function has the semantics of the
// original in vita3k/util/src/fs.cpp.

#pragma once

#include <cstdint>
#include <cstdio>
#include <filesystem>
#include <format>
#include <fstream>
#include <string>
#include <vector>

namespace fs {
using namespace std::filesystem;
using ifstream = std::ifstream;
using ofstream = std::ofstream;
} // namespace fs

#define FOPEN(filename, params) _wfopen(filename, L##params)

namespace fs_utils {

inline std::string path_to_utf8(const fs::path &path)
{
    const auto u8 = path.u8string();
    return std::string(u8.begin(), u8.end());
}

// NOT a join: Vita3K appends the second path to the first as text ("x.pkg" + ".seg02").
inline fs::path path_concat(const fs::path &path1, const fs::path &path2)
{
    return fs::path(path1.native() + path2.native());
}

template <typename T>
inline bool read_data(const fs::path &path, std::vector<T> &data)
{
    data.clear();
    std::ifstream file(path, std::ios::binary | std::ios::ate);
    if (!file.is_open()) return false;
    const auto size = static_cast<std::size_t>(file.tellg());
    data.resize(size);
    file.seekg(0, std::ios::beg);
    file.read(reinterpret_cast<char *>(data.data()), static_cast<std::streamsize>(size));
    return static_cast<bool>(file);
}

} // namespace fs_utils

// pup.cpp names two files with fmt::format. Its format strings ("{}-{:0>2}.pkg", "unknown-0x{:X}.pkg")
// are valid std::format strings, so the call is forwarded unchanged.
namespace fmt {
template <typename... Args>
inline std::string format(std::format_string<Args...> f, Args &&...args)
{
    return std::format(f, std::forward<Args>(args)...);
}
} // namespace fmt
