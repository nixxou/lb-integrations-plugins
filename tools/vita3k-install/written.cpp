// SPDX-License-Identifier: GPL-2.0-or-later
// See written.h.

#include "written.h"

#include <windows.h>
#include <bcrypt.h>

#include <map>
#include <memory>
#include <streambuf>

namespace v3k::written {

namespace {

// One SHA-1, through CNG - the same provider the rest of this library already uses for AES.
class Sha1 {
public:
    Sha1()
    {
        if (BCryptOpenAlgorithmProvider(&alg_, BCRYPT_SHA1_ALGORITHM, nullptr, 0) != 0) { alg_ = nullptr; return; }
        if (BCryptCreateHash(alg_, &hash_, nullptr, 0, nullptr, 0, 0) != 0) hash_ = nullptr;
    }
    ~Sha1()
    {
        if (hash_) BCryptDestroyHash(hash_);
        if (alg_) BCryptCloseAlgorithmProvider(alg_, 0);
    }
    Sha1(const Sha1 &) = delete;
    Sha1 &operator=(const Sha1 &) = delete;

    void update(const void *data, std::size_t n)
    {
        const auto *p = static_cast<const unsigned char *>(data);
        while (ok_ && hash_ && n) {
            ULONG take = n > 0x40000000 ? 0x40000000 : static_cast<ULONG>(n);
            if (BCryptHashData(hash_, const_cast<PUCHAR>(p), take, 0) != 0) ok_ = false;
            p += take;
            n -= take;
        }
    }

    // Empty on any failure: the plugin then reads the file itself, as it did before.
    std::string hex()
    {
        unsigned char digest[20];
        if (!ok_ || !hash_ || BCryptFinishHash(hash_, digest, sizeof(digest), 0) != 0) return {};
        static const char *digits = "0123456789ABCDEF";
        std::string out;
        for (unsigned char b : digest) { out += digits[b >> 4]; out += digits[b & 15]; }
        return out;
    }

private:
    BCRYPT_ALG_HANDLE alg_ = nullptr;
    BCRYPT_HASH_HANDLE hash_ = nullptr;
    bool ok_ = true;
};

// Stands in front of the file stream's own buffer: every byte is hashed, then handed on unchanged.
// It buffers nothing itself, so there is never anything of its own left to flush when the stream's
// file is closed - ofstream::close closes its file buffer directly, not this one.
class HashingBuf : public std::streambuf {
public:
    explicit HashingBuf(std::streambuf *inner) : inner_(inner) {}

    std::uint64_t size = 0;
    Sha1 sha;

protected:
    int_type overflow(int_type c) override
    {
        if (traits_type::eq_int_type(c, traits_type::eof())) return traits_type::not_eof(c);
        char ch = traits_type::to_char_type(c);
        if (traits_type::eq_int_type(inner_->sputc(ch), traits_type::eof())) return traits_type::eof();
        sha.update(&ch, 1);
        ++size;
        return c;
    }

    std::streamsize xsputn(const char *s, std::streamsize n) override
    {
        std::streamsize put = inner_->sputn(s, n);
        if (put > 0) { sha.update(s, static_cast<std::size_t>(put)); size += static_cast<std::uint64_t>(put); }
        return put;
    }

    int sync() override { return inner_->pubsync(); }

private:
    std::streambuf *inner_;
};

struct Entry {
    std::unique_ptr<HashingBuf> buf;   // a written file, hashed as it went
    File done;                         // or a copied one, hashed at once
};

bool recording = false;
std::map<std::wstring, Entry> entries;   // keyed by the path as written; a later write replaces

std::wstring key_of(const std::filesystem::path &p) { return p.lexically_normal().generic_wstring(); }

} // namespace

void begin()
{
    entries.clear();
    recording = true;
}

void track(std::ofstream &stream, const std::filesystem::path &path)
{
    if (!recording || !stream.is_open()) return;
    auto buf = std::make_unique<HashingBuf>(stream.rdbuf());
    // basic_ios::rdbuf(sb) - ofstream hides the setter behind its own getter.
    stream.std::basic_ios<char>::rdbuf(buf.get());
    Entry e;
    e.buf = std::move(buf);
    e.done.path = path;
    entries[key_of(path)] = std::move(e);
}

void copied(const std::filesystem::path &source, const std::filesystem::path &destination, std::uint64_t size)
{
    if (!recording) return;
    Entry e;
    e.done.path = destination;
    e.done.size = size;

    std::ifstream in(source, std::ios::binary);
    Sha1 sha;
    std::vector<char> chunk(1 << 20);
    std::uint64_t left = size;
    bool whole = static_cast<bool>(in);
    while (whole && left) {
        auto want = static_cast<std::streamsize>(left < chunk.size() ? left : chunk.size());
        in.read(chunk.data(), want);
        auto got = in.gcount();
        if (got <= 0) { whole = false; break; }
        sha.update(chunk.data(), static_cast<std::size_t>(got));
        left -= static_cast<std::uint64_t>(got);
    }
    // A source shorter than the size it is cut to would be padded by the resize: not what was hashed.
    if (whole && left == 0) e.done.sha1 = sha.hex();
    entries[key_of(destination)] = std::move(e);
}

std::vector<File> end()
{
    recording = false;
    std::vector<File> out;
    for (auto &[key, e] : entries) {
        File f = e.done;
        if (e.buf) {
            f.size = e.buf->size;
            f.sha1 = e.buf->sha.hex();
        }
        if (!f.sha1.empty()) out.push_back(std::move(f));
    }
    entries.clear();
    return out;
}

} // namespace v3k::written
