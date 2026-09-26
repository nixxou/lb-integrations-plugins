// SPDX-License-Identifier: GPL-2.0-or-later
// The two installs Vita3K performs and a plain unzip cannot, plus the crypto self-test.
//
// DECRYPT: an installed app through its PFS layer, the way Vita3K's own installer does it in
// decrypt_install_nonpdrm (pkg.cpp):
//
//     execute(zRIF, title, title + "_dec", native, "")
//     remove_all(title); rename(title + "_dec", title)
//
// NOT THAT execute(): it takes a zRIF - the licence deflated and base64-encoded - only to decode it
// straight back to the same structure and read its key. The key sits at offset 0x50 of the licence
// (SceNpDrmLicense.key, zRIF/rif.h), so it is read there and handed to the lower-level execute()
// sequence directly. That drops zlib, libb64 and libzrif, and changes nothing about what gets
// decrypted. Every cryptographic call goes through ICryptoOperations, answered by CNG: see
// cng_crypto.h.
//
// FIRMWARE: Vita3K's install_pup, in firmware.cpp.

#include "core.h"

#include "cng_crypto.h"

#include "F00DNativeKeyEncryptor.h"
#include "LocalKeyGenerator.h"
#include "PfsFilesystem.h"
#include "zipsource.h"

#include <cstdio>
#include <cstring>
#include <fstream>
#include <iostream>
#include <memory>
#include <streambuf>
#include <vector>

namespace fs = std::filesystem;

// Defined in firmware.cpp, which sees Vita3K's `fs` rather than this file's.
int run_firmware(const std::filesystem::path &pup, const std::filesystem::path &vita_fs, bool verbose,
                 const std::function<void(std::uint32_t)> &progress, std::string &version, std::string &error);

namespace {

struct NullBuffer : std::streambuf
{
    int overflow(int c) override { return c; }
};

int fail(std::string &message, const std::string &why)
{
    message = "ERROR " + why;
    return 1;
}

std::size_t count_files(const fs::path &root)
{
    std::size_t n = 0;
    std::error_code ec;
    for (auto it = fs::recursive_directory_iterator(root, ec); !ec && it != fs::recursive_directory_iterator(); it.increment(ec))
        if (it->is_regular_file(ec)) n++;
    return n;
}

// Long-path form of an absolute path. Vita3K's extract_file opens every file with _wfopen and writes
// WITHOUT checking the handle: past MAX_PATH that is fwrite on NULL - the crash measured when the
// emulator installed a firmware from a 174-character folder. Under the \\?\ prefix there is no such
// limit. Safe because the firmware code builds every path with operator/ and single components:
// Windows stops translating '/' under this prefix, and none is ever inserted.
fs::path long_path(const fs::path &p)
{
    std::wstring s = fs::absolute(p).wstring();
    if (s.rfind(L"\\\\?\\", 0) != 0) s = L"\\\\?\\" + s;
    return fs::path(s);
}

// The library prints to std::cout in places it was not given a stream. In the command-line tool
// stdout carries the one result line; in the DLL it is somebody else's process. Silenced either way,
// for exactly the duration of a call.
struct CoutSilenced
{
    NullBuffer nothing;
    std::streambuf *saved;
    explicit CoutSilenced(bool verbose) : saved(std::cout.rdbuf()) { std::cout.rdbuf(verbose ? std::cerr.rdbuf() : &nothing); }
    ~CoutSilenced() { std::cout.rdbuf(saved); }
};

} // namespace

namespace v3k {

int decrypt(const fs::path &src, const fs::path &licence, const fs::path &dst, bool verbose,
            const Progress &progress, std::string &message, std::vector<written::File> *files,
            bool consume_source)
{
    // Recording stops on every way out, the throwing ones included.
    struct Recording {
        std::vector<written::File> *out;
        bool kept = false;
        Recording(std::vector<written::File> *o, bool consume, const fs::path &root) : out(o)
        { if (out) written::begin(consume, root); }
        ~Recording() { if (out && !kept) written::end(); }
        void keep() { if (out) { *out = written::end(); kept = true; } }
    } recording(files, consume_source, src);

    try {
        if (!fs::is_directory(src)) return fail(message, "no app directory at " + src.string());
        if (!fs::exists(src / "sce_pfs")) return fail(message, "no sce_pfs in " + src.string() + " - nothing to decrypt");

        std::ifstream in(licence, std::ios::binary);
        if (!in) return fail(message, "cannot read the licence " + licence.string());
        std::vector<unsigned char> lic((std::istreambuf_iterator<char>(in)), std::istreambuf_iterator<char>());
        if (lic.size() < 0x60) return fail(message, "the licence is " + std::to_string(lic.size()) + " bytes - too short to hold a key");

        unsigned char klicensee[0x10];
        std::memcpy(klicensee, lic.data() + 0x50, 0x10);   // SceNpDrmLicense.key

        std::error_code ec;
        fs::remove_all(dst, ec);

        NullBuffer nothing;
        std::ostream quiet(&nothing);
        std::ostream &out = verbose ? std::cerr : quiet;
        CoutSilenced silence(verbose);

        auto cryptops = std::make_shared<CngCryptoOperations>();
        std::shared_ptr<IF00DKeyEncryptor> iF00D = std::make_shared<F00DNativeKeyEncryptor>(cryptops);

        PfsProgressCallback pfs_progress = nullptr;
        if (progress)
            pfs_progress = [&progress](std::uint64_t done, std::uint64_t total, const std::string &) { progress(done, total); };

        PfsFilesystem pfs(cryptops, iF00D, out, klicensee, src);
        if (pfs.mount() < 0) return fail(message, "the PFS layer did not decrypt (mount)");
        if (pfs.decrypt_files(dst, pfs_progress) < 0) return fail(message, "the PFS layer did not decrypt (decrypt)");
        if (get_keystone(cryptops, dst) < 0) return fail(message, "the PFS layer did not decrypt (keystone check)");

        if (verbose) std::fprintf(stderr, "aes_cmac calls: %llu\n", (unsigned long long)cryptops->cmac_calls);
        recording.keep();
        message = "OK " + std::to_string(count_files(dst)) + " file(s)";
        if (files) message += ", " + std::to_string(files->size()) + " hashed as written";
        if (files && consume_source) message += ", " + std::to_string(written::consumed()) + " source(s) consumed as they went";
        return 0;
    } catch (const std::exception &e) {
        return fail(message, std::string("the decryptor threw: ") + e.what());
    }
}

int decrypt_zip(const fs::path &zip, const std::string &root, const fs::path &stage, const fs::path &licence,
                const fs::path &dst, const Progress &progress, std::string &message, std::vector<written::File> *files)
{
    // Small entries are extracted for real: psvpfsparser opens some metadata by path (files.db,
    // unicv.db, keystone, sealedkey) rather than through the door zipsource serves.
    constexpr std::uint64_t SmallLimit = 64 * 1024;
    struct Closer { ~Closer() { zipsource::close(); } } closer;

    std::string why;
    std::size_t placeholders = 0, extracted = 0;
    try {
        if (!zipsource::open(zip, root, stage, SmallLimit, why, placeholders, extracted))
            return fail(message, "reading from the zip is not possible: " + why);
    } catch (const std::exception &e) {
        return fail(message, std::string("laying out the stage threw: ") + e.what());
    }

    int r = decrypt(stage, licence, dst, false, progress, message, files, false);
    if (r == 0 && zipsource::failed())
        return fail(message, "an entry of the zip did not read back whole - not trusting this decrypt");
    if (r == 0)
        message += ", read from the zip (" + std::to_string(placeholders) + " placeholder(s), "
                   + std::to_string(extracted) + " small file(s) extracted)";
    return r;
}

int firmware(const fs::path &pup, const fs::path &vita_fs, bool verbose, const Progress &progress,
             std::string &message)
{
    try {
        if (!fs::is_regular_file(pup)) return fail(message, "no firmware file at " + pup.string());

        std::error_code ec;
        fs::create_directories(vita_fs, ec);
        if (!fs::is_directory(vita_fs)) return fail(message, "cannot create " + vita_fs.string());

        std::function<void(std::uint32_t)> percent = nullptr;
        if (progress) percent = [&progress](std::uint32_t p) { progress(p, 100); };

        std::string version, error;
        if (run_firmware(long_path(pup), long_path(vita_fs), verbose, percent, version, error) < 0)
            return fail(message, "the firmware install threw: " + error);

        // install_pup reports almost none of its failures - an invalid PUP is a log line and a normal
        // return. So the verdict is what landed on disk.
        std::string parts;
        std::size_t total = 0;
        for (const char *part : { "os0", "vs0", "sa0", "pd0" }) {
            const auto dir = vita_fs / part;
            if (!fs::is_directory(dir)) continue;
            const auto n = count_files(dir);
            if (n == 0) continue;
            total += n;
            parts += std::string(parts.empty() ? "" : ", ") + part + " " + std::to_string(n);
        }
        if (fs::exists(vita_fs / "PUP_DEC")) return fail(message, "the work folder PUP_DEC was left behind - the install stopped halfway");
        if (total == 0) return fail(message, "nothing was extracted - not a firmware update, or it did not decrypt");

        message = "OK firmware " + (version.empty() ? std::string("(no version.txt)") : version) + " - " + parts + " file(s)";
        return 0;
    } catch (const std::exception &e) {
        return fail(message, std::string("the firmware install threw: ") + e.what());
    }
}

// Known answers, so a wrong CNG wrapper shows up here and not as a game that will not boot.
namespace {

bool hex(const char *s, std::vector<unsigned char> &out)
{
    out.clear();
    for (std::size_t i = 0; s[i] && s[i + 1]; i += 2) {
        unsigned v;
        if (std::sscanf(s + i, "%2x", &v) != 1) return false;
        out.push_back(static_cast<unsigned char>(v));
    }
    return true;
}

} // namespace

int selftest(std::string &report)
{
    CngCryptoOperations c;
    int bad = 0;
    report.clear();
    auto check = [&](const char *name, const unsigned char *got, const char *want_hex) {
        std::vector<unsigned char> want;
        hex(want_hex, want);
        bool ok = std::memcmp(got, want.data(), want.size()) == 0;
        report += std::string("  ") + (ok ? "ok   " : "FAIL ") + name + "\n";
        if (!ok) bad++;
    };

    std::vector<unsigned char> k, p, iv;
    unsigned char o[64];

    hex("000102030405060708090a0b0c0d0e0f", k);
    hex("00112233445566778899aabbccddeeff", p);
    c.aes_ecb_encrypt(p.data(), o, 16, k.data(), 128);
    check("AES-128 ECB encrypt (FIPS-197 C.1)", o, "69c4e0d86a7b0430d8cdb78070b4c55a");
    c.aes_ecb_decrypt(o, o, 16, k.data(), 128);
    check("AES-128 ECB decrypt, in place", o, "00112233445566778899aabbccddeeff");

    hex("2b7e151628aed2a6abf7158809cf4f3c", k);
    hex("6bc1bee22e409f96e93d7e117393172aae2d8a571e03ac9c9eb76fac45af8e51", p);

    hex("000102030405060708090a0b0c0d0e0f", iv);
    c.aes_cbc_encrypt(p.data(), o, 32, k.data(), 128, iv.data());
    check("AES-128 CBC encrypt (SP 800-38A F.2.1)", o, "7649abac8119b246cee98e9b12e9197d5086cb9b507219ee95db113a917678b2");
    check("CBC leaves the last ciphertext block as IV", iv.data(), "5086cb9b507219ee95db113a917678b2");

    hex("f0f1f2f3f4f5f6f7f8f9fafbfcfdfeff", iv);
    c.aes_ctr_encrypt(p.data(), o, 32, k.data(), 128, iv.data());
    check("AES-128 CTR encrypt (SP 800-38A F.5.1)", o, "874d6191b620e3261bef6864990db6ce9806f66b7970fdff8617187bb9fffdff");
    check("CTR advances the IV by whole blocks", iv.data(), "f0f1f2f3f4f5f6f7f8f9fafbfcfdff01");

    c.aes_cmac(nullptr, o, 0, k.data(), 128);
    check("AES-CMAC, empty (RFC 4493 ex. 1)", o, "bb1d6929e95937287fa37d129b756746");
    c.aes_cmac(p.data(), o, 16, k.data(), 128);
    check("AES-CMAC, 16 bytes (RFC 4493 ex. 2)", o, "070a16b46b4d4144f79bdd9dd04a287c");

    const unsigned char abc[] = { 'a', 'b', 'c' };
    c.sha1(abc, o, 3);
    check("SHA-1 \"abc\"", o, "a9993e364706816aba3e25717850c26c9cd0d89d");
    c.sha256(abc, o, 3);
    check("SHA-256 \"abc\"", o, "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad");

    unsigned char k20[20];
    std::memset(k20, 0x0b, 20);
    const unsigned char hi[] = { 'H', 'i', ' ', 'T', 'h', 'e', 'r', 'e' };
    c.hmac_sha1(hi, o, 8, k20, 20);
    check("HMAC-SHA1 (RFC 2202 case 1)", o, "b617318655057264e28bc0b6fb378c8ef146be00");
    c.hmac_sha256(hi, o, 8, k20, 20);
    check("HMAC-SHA256 (RFC 4231 case 1)", o, "b0344c61d8db38535ca8afceaf0bf12b881dc200c9833da726e9376c2e32cff7");

    report += bad == 0 ? "OK selftest\n" : "ERROR " + std::to_string(bad) + " known answer(s) wrong\n";
    return bad == 0 ? 0 : 1;
}

} // namespace v3k
