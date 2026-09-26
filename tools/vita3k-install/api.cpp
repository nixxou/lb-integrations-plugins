// SPDX-License-Identifier: GPL-2.0-or-later
// vita3k-install.dll: the C door the Vita3K plugin calls. Loaded, used and UNLOADED per operation -
// see "One load per operation" below.
//
//   int v3k_abi_version(void)
//   int v3k_decrypt (app, licence, destination, progress, user, message, message_len)
//   int v3k_firmware(pup, vita_fs,              progress, user, message, message_len)
//   int v3k_selftest(message, message_len)
//
// Paths are UTF-16. Each returns 0 on success and writes one line to `message` - "OK ..." or
// "ERROR ...", NUL-terminated and cut to fit. `progress` may be null; it is called on the calling
// thread with (user, done, total): bytes for decrypt, percent out of 100 for firmware.
//
// THIS CODE RUNS INSIDE THE HOST NOW, so a crash in it is a crash of LaunchBox. Vita3K's firmware code
// is not armoured - extract_file writes through a FILE* it never checked - so every export runs its
// work under structured exception handling: an access violation becomes an ERROR line and a failed
// install, not a vanished front end. It cannot catch everything: a __fastfail (0xC0000409, what a
// stack cookie or an invalid-parameter handler raises) terminates the process by design. The known
// cause of a crash, a path past MAX_PATH, is removed at the source instead - core.cpp's long_path.
//
// ONE LOAD PER OPERATION. pup.cpp numbers the partition pieces it extracts with a function-local
// `static int typecount` that nothing ever resets. In a process that installs one firmware and exits
// that is harmless; in a DLL kept loaded it keeps counting across installs, and past 99 the two-digit
// names ("os0-{:0>2}") sort out of order when they are joined back together - a silently corrupt
// partition image. Unloading the DLL after each call re-initialises its statics, and changes not a
// line of Vita3K. The plugin therefore loads it, calls once, and frees it.

#include "core.h"

#include <windows.h>

#include <cstdio>
#include <cstring>
#include <string>

#define V3K_API extern "C" __declspec(dllexport)

typedef void(__cdecl *v3k_progress_fn)(void *user, std::uint64_t done, std::uint64_t total);

namespace {

constexpr int AbiVersion = 1;

void copy_out(const char *text, char *out, int out_len)
{
    if (!out || out_len <= 0) return;
    const std::size_t n = text ? std::strlen(text) : 0;
    const std::size_t take = n < static_cast<std::size_t>(out_len - 1) ? n : static_cast<std::size_t>(out_len - 1);
    if (take) std::memcpy(out, text, take);
    out[take] = 0;
}

v3k::Progress wrap(v3k_progress_fn cb, void *user)
{
    if (!cb) return nullptr;
    return [cb, user](std::uint64_t done, std::uint64_t total) { cb(user, done, total); };
}

// The C++ work, in frames of their own: a function holding __try cannot also hold objects that need
// unwinding.
int decrypt_work(const wchar_t *app, const wchar_t *licence, const wchar_t *dst, v3k_progress_fn cb, void *user,
                 char *message, int message_len)
{
    std::string m;
    int r = v3k::decrypt(app, licence, dst, false, wrap(cb, user), m);
    copy_out(m.c_str(), message, message_len);
    return r;
}

int firmware_work(const wchar_t *pup, const wchar_t *vita_fs, v3k_progress_fn cb, void *user,
                  char *message, int message_len)
{
    std::string m;
    int r = v3k::firmware(pup, vita_fs, false, wrap(cb, user), m);
    copy_out(m.c_str(), message, message_len);
    return r;
}

int selftest_work(char *message, int message_len)
{
    std::string m;
    int r = v3k::selftest(m);
    copy_out(m.c_str(), message, message_len);
    return r;
}

int crashed(unsigned long code, char *message, int message_len)
{
    char text[160];
    std::snprintf(text, sizeof(text),
                  "ERROR the native code crashed (exception 0x%08lX) - the host survived, this install did not", code);
    copy_out(text, message, message_len);
    return 3;
}

} // namespace

V3K_API int v3k_abi_version() { return AbiVersion; }

V3K_API int v3k_decrypt(const wchar_t *app, const wchar_t *licence, const wchar_t *destination,
                        v3k_progress_fn progress, void *user, char *message, int message_len)
{
    unsigned long code = 0;
    __try {
        return decrypt_work(app, licence, destination, progress, user, message, message_len);
    } __except (code = GetExceptionCode(), EXCEPTION_EXECUTE_HANDLER) {
        return crashed(code, message, message_len);
    }
}

V3K_API int v3k_firmware(const wchar_t *pup, const wchar_t *vita_fs,
                         v3k_progress_fn progress, void *user, char *message, int message_len)
{
    unsigned long code = 0;
    __try {
        return firmware_work(pup, vita_fs, progress, user, message, message_len);
    } __except (code = GetExceptionCode(), EXCEPTION_EXECUTE_HANDLER) {
        return crashed(code, message, message_len);
    }
}

V3K_API int v3k_selftest(char *message, int message_len)
{
    unsigned long code = 0;
    __try {
        return selftest_work(message, message_len);
    } __except (code = GetExceptionCode(), EXCEPTION_EXECUTE_HANDLER) {
        return crashed(code, message, message_len);
    }
}
