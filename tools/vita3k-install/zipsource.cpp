// SPDX-License-Identifier: GPL-2.0-or-later
// See zipsource.h.

#include "zipsource.h"

#include <windows.h>
#include <winioctl.h>

#include <cstdio>
#include <deque>
#include <map>
#include <memory>
#include <streambuf>
#include <vector>

#include "miniz.h"

namespace fs = std::filesystem;

namespace v3k::zipsource {

namespace {

mz_zip_archive archive;
FILE *archive_file = nullptr;
bool is_open = false;
bool read_failed = false;
std::map<std::wstring, mz_uint> placeholders_by_path;   // lexically normal, generic, lower-cased

std::wstring key_of(const fs::path &p)
{
    auto s = p.lexically_normal().generic_wstring();
    CharLowerBuffW(s.data(), static_cast<DWORD>(s.size()));
    return s;
}

// Reads one entry, front to back, in chunks - never more of it in memory than one chunk.
class ZipBuf : public std::streambuf {
public:
    explicit ZipBuf(mz_uint index) : buffer_(1 << 20)
    {
        state_ = mz_zip_reader_extract_iter_new(&archive, index, 0);
        if (!state_) read_failed = true;
    }
    ~ZipBuf() override
    {
        if (state_) mz_zip_reader_extract_iter_free(state_);
    }
    ZipBuf(const ZipBuf &) = delete;
    ZipBuf &operator=(const ZipBuf &) = delete;

protected:
    int_type underflow() override
    {
        if (gptr() < egptr()) return traits_type::to_int_type(*gptr());
        std::size_t got = pull(buffer_.data(), buffer_.size());
        if (got == 0) return traits_type::eof();
        setg(buffer_.data(), buffer_.data(), buffer_.data() + got);
        return traits_type::to_int_type(*gptr());
    }

    // Large reads go straight into the caller's memory: psvpfsparser reads whole sectors, and a
    // detour through the chunk would be a copy per byte for nothing.
    std::streamsize xsgetn(char *s, std::streamsize n) override
    {
        std::streamsize done = 0;
        while (done < n) {
            if (gptr() < egptr()) {
                std::streamsize take = std::min<std::streamsize>(egptr() - gptr(), n - done);
                std::memcpy(s + done, gptr(), static_cast<std::size_t>(take));
                gbump(static_cast<int>(take));
                done += take;
                continue;
            }
            std::size_t got = pull(s + done, static_cast<std::size_t>(n - done));
            if (got == 0) break;
            done += static_cast<std::streamsize>(got);
        }
        return done;
    }

private:
    std::size_t pull(char *into, std::size_t n)
    {
        if (!state_) return 0;
        std::size_t got = mz_zip_reader_extract_iter_read(state_, into, n);
        if (got == 0 && n > 0 && state_->status < 0) read_failed = true;
        return got;
    }

    mz_zip_reader_extract_iter_state *state_ = nullptr;
    std::vector<char> buffer_;
};

// The buffers handed out. psvpfsparser reads one source at a time and lets each stream die at the
// end of its scope, so only the last few can still be in use; older ones are freed as new ones are
// made - one decompressor alive per file would add up to hundreds of MB on a big game.
std::deque<std::unique_ptr<ZipBuf>> live;
constexpr std::size_t KeepAlive = 4;

bool starts_with_ci(const std::string &s, const std::string &prefix)
{
    if (s.size() < prefix.size()) return false;
    return CompareStringOrdinal(std::wstring(s.begin(), s.begin() + prefix.size()).c_str(), -1,
                                std::wstring(prefix.begin(), prefix.end()).c_str(), -1, TRUE) == CSTR_EQUAL;
}

std::wstring widen(const std::string &utf8)
{
    if (utf8.empty()) return {};
    int n = MultiByteToWideChar(CP_UTF8, 0, utf8.data(), static_cast<int>(utf8.size()), nullptr, 0);
    std::wstring w(static_cast<std::size_t>(n), L'\0');
    MultiByteToWideChar(CP_UTF8, 0, utf8.data(), static_cast<int>(utf8.size()), w.data(), n);
    return w;
}

// A file of `size` bytes that takes no room: sparse, its length set, nothing written.
bool placeholder(const fs::path &path, std::uint64_t size)
{
    HANDLE h = CreateFileW(path.c_str(), GENERIC_READ | GENERIC_WRITE, 0, nullptr, CREATE_ALWAYS,
                           FILE_ATTRIBUTE_NORMAL, nullptr);
    if (h == INVALID_HANDLE_VALUE) return false;
    DWORD ret = 0;
    bool ok = DeviceIoControl(h, FSCTL_SET_SPARSE, nullptr, 0, nullptr, 0, &ret, nullptr) != 0;
    LARGE_INTEGER end;
    end.QuadPart = static_cast<LONGLONG>(size);
    ok = ok && SetFilePointerEx(h, end, nullptr, FILE_BEGIN) && SetEndOfFile(h);
    CloseHandle(h);
    return ok;
}

bool extract(mz_uint index, const fs::path &path)
{
    FILE *out = _wfopen(path.c_str(), L"wb");
    if (!out) return false;
    bool ok = mz_zip_reader_extract_to_cfile(&archive, index, out, 0) != 0;
    ok = (std::fclose(out) == 0) && ok;
    return ok;
}

} // namespace

bool open(const fs::path &zip, const std::string &root, const fs::path &stage, std::uint64_t small_limit,
          std::string &error, std::size_t &placeholders, std::size_t &extracted)
{
    close();
    placeholders = extracted = 0;

    archive_file = _wfopen(zip.c_str(), L"rb");
    if (!archive_file) { error = "cannot open the archive"; return false; }
    _fseeki64(archive_file, 0, SEEK_END);
    auto size = static_cast<mz_uint64>(_ftelli64(archive_file));
    _fseeki64(archive_file, 0, SEEK_SET);

    mz_zip_zero_struct(&archive);
    if (!mz_zip_reader_init_cfile(&archive, archive_file, size, 0)) {
        error = std::string("not a zip miniz can read: ") + mz_zip_get_error_string(mz_zip_get_last_error(&archive));
        std::fclose(archive_file);
        archive_file = nullptr;
        return false;
    }
    is_open = true;
    read_failed = false;

    const fs::path stage_root = stage.lexically_normal();
    mz_uint count = mz_zip_reader_get_num_files(&archive);
    for (mz_uint i = 0; i < count; ++i) {
        mz_zip_archive_file_stat st;
        if (!mz_zip_reader_file_stat(&archive, i, &st)) { error = "an entry cannot be read"; close(); return false; }

        std::string name = st.m_filename;
        for (auto &c : name) if (c == '\\') c = '/';
        if (!root.empty()) {
            if (!starts_with_ci(name, root)) continue;
            name = name.substr(root.size());
        }
        if (name.empty() || name == "/") continue;

        // NOTHING OUTSIDE THE STAGE. An archive is somebody else's file.
        fs::path rel = fs::path(widen(name)).lexically_normal();
        if (rel.is_absolute() || rel.has_root_name() || (!rel.empty() && *rel.begin() == "..")) {
            error = "an entry escapes the destination: " + name;
            close();
            return false;
        }
        fs::path target = stage_root / rel;

        std::error_code ec;
        if (st.m_is_directory) { fs::create_directories(target, ec); continue; }
        fs::create_directories(target.parent_path(), ec);

        bool real = st.m_uncomp_size <= small_limit || starts_with_ci(name, "sce_pfs/")
                    || starts_with_ci(name, "sce_sys/package/");
        if (real) {
            if (!extract(i, target)) { error = "could not extract " + name; close(); return false; }
            ++extracted;
        } else {
            if (!placeholder(target, st.m_uncomp_size)) { error = "could not lay a placeholder for " + name; close(); return false; }
            placeholders_by_path[key_of(target)] = i;
            ++placeholders;
        }
    }
    return true;
}

void close()
{
    live.clear();
    placeholders_by_path.clear();
    if (is_open) mz_zip_reader_end(&archive);
    is_open = false;
    if (archive_file) { std::fclose(archive_file); archive_file = nullptr; }
}

bool active() { return is_open; }
bool failed() { return read_failed; }

bool attach(std::ifstream &in, const fs::path &real)
{
    if (!is_open) return false;
    auto it = placeholders_by_path.find(key_of(real));
    if (it == placeholders_by_path.end()) return false;

    while (live.size() >= KeepAlive) live.pop_front();
    live.push_back(std::make_unique<ZipBuf>(it->second));
    in.std::basic_ios<char>::rdbuf(live.back().get());
    in.clear();
    return true;
}

Copy copy_to(const fs::path &real, const fs::path &destination, std::uint64_t size)
{
    if (!is_open) return Copy::NotMapped;
    auto it = placeholders_by_path.find(key_of(real));
    if (it == placeholders_by_path.end()) return Copy::NotMapped;

    ZipBuf source(it->second);
    std::ofstream out(destination, std::ios::out | std::ios::trunc | std::ios::binary);
    if (!out) return Copy::Failed;
    std::vector<char> chunk(1 << 20);
    std::uint64_t left = size;
    while (left) {
        auto want = static_cast<std::streamsize>(left < chunk.size() ? left : chunk.size());
        auto got = source.sgetn(chunk.data(), want);
        if (got <= 0) break;
        out.write(chunk.data(), got);
        left -= static_cast<std::uint64_t>(got);
    }
    out.close();
    if (!out) return Copy::Failed;
    // Shorter than the size it is cut to: zero-padded, exactly what resize_file does to a copy.
    std::error_code ec;
    fs::resize_file(destination, size, ec);
    return ec || read_failed ? Copy::Failed : Copy::Ok;
}

} // namespace v3k::zipsource
