// SPDX-License-Identifier: GPL-2.0-or-later
// Stand-in for Vita3K's util/log.h (spdlog).
//
// The firmware code logs with fmt-style format strings and arguments such as fs::path, which
// std::format cannot print before C++26. The messages matter only to a person debugging, so the raw
// format string is written to stderr when --verbose asked for it, arguments untouched, and nothing is
// written otherwise. An error is never signalled through a log line here: the tool checks what landed
// on disk instead, since install_pup reports almost none of its failures.

#pragma once

#include <util/fs.h>

// The real log.h pulls in spdlog, and Vita3K's headers rely on what that drags along without naming
// it - sce_types.h uses std::unordered_map, pup.cpp std::function. Named here instead.
#include <algorithm>
#include <array>
#include <cstdint>
#include <cstdio>
#include <cstring>
#include <functional>
#include <map>
#include <memory>
#include <optional>
#include <sstream>
#include <string>
#include <tuple>
#include <unordered_map>
#include <vector>

namespace vita3k_shim {
inline bool verbose = false;

template <typename... Args>
inline void log(const char *level, const char *format, Args &&...)
{
    if (verbose) std::fprintf(stderr, "[%s] %s\n", level, format);
}
} // namespace vita3k_shim

#define LOG_TRACE(fmtstr, ...) ::vita3k_shim::log("trace", fmtstr __VA_OPT__(, ) __VA_ARGS__)
#define LOG_DEBUG(fmtstr, ...) ::vita3k_shim::log("debug", fmtstr __VA_OPT__(, ) __VA_ARGS__)
#define LOG_INFO(fmtstr, ...) ::vita3k_shim::log("info", fmtstr __VA_OPT__(, ) __VA_ARGS__)
#define LOG_WARN(fmtstr, ...) ::vita3k_shim::log("warn", fmtstr __VA_OPT__(, ) __VA_ARGS__)
#define LOG_ERROR(fmtstr, ...) ::vita3k_shim::log("error", fmtstr __VA_OPT__(, ) __VA_ARGS__)
#define LOG_CRITICAL(fmtstr, ...) ::vita3k_shim::log("critical", fmtstr __VA_OPT__(, ) __VA_ARGS__)
