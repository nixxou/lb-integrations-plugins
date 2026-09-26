// SPDX-License-Identifier: GPL-2.0-or-later
// Stand-in for Vita3K's util/string_utils.h: the one function sce_utils.cpp calls, with the same
// behaviour as vita3k/util/src/string_utils.cpp - an odd-length string gives an empty result.

#pragma once

#include <charconv>
#include <cstdint>
#include <string_view>
#include <vector>

namespace string_utils {

inline std::vector<uint8_t> string_to_byte_array(std::string_view string)
{
    if (string.length() % 2 != 0) return {};

    std::vector<uint8_t> bytes;
    bytes.reserve(string.length() / 2);
    for (std::size_t i = 0; i < string.length(); i += 2) {
        uint8_t byte = 0;
        std::from_chars(string.data() + i, string.data() + i + 2, byte, 16);
        bytes.push_back(byte);
    }
    return bytes;
}

} // namespace string_utils
