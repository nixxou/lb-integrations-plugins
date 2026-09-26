// SPDX-License-Identifier: GPL-2.0-or-later
#include "cng_crypto.h"

#include <windows.h>
#include <bcrypt.h>

#include <cstring>
#include <vector>

#ifndef NT_SUCCESS
#define NT_SUCCESS(s) (((NTSTATUS)(s)) >= 0)
#endif

// The algorithm providers are opened once. The PFS decryptor calls AES-ECB on a 16-byte tweak for
// every block of every file - millions of calls on a real game - and opening a provider per call
// would be the whole cost of the run.
namespace {

struct Providers
{
    BCRYPT_ALG_HANDLE aes_ecb = nullptr;
    BCRYPT_ALG_HANDLE aes_cbc = nullptr;
    BCRYPT_ALG_HANDLE sha1 = nullptr;
    BCRYPT_ALG_HANDLE sha256 = nullptr;
    BCRYPT_ALG_HANDLE hmac_sha1 = nullptr;
    BCRYPT_ALG_HANDLE hmac_sha256 = nullptr;
    bool ok = false;

    Providers()
    {
        ok = open(&aes_ecb, BCRYPT_AES_ALGORITHM, 0, BCRYPT_CHAIN_MODE_ECB)
             && open(&aes_cbc, BCRYPT_AES_ALGORITHM, 0, BCRYPT_CHAIN_MODE_CBC)
             && open(&sha1, BCRYPT_SHA1_ALGORITHM, 0, nullptr)
             && open(&sha256, BCRYPT_SHA256_ALGORITHM, 0, nullptr)
             && open(&hmac_sha1, BCRYPT_SHA1_ALGORITHM, BCRYPT_ALG_HANDLE_HMAC_FLAG, nullptr)
             && open(&hmac_sha256, BCRYPT_SHA256_ALGORITHM, BCRYPT_ALG_HANDLE_HMAC_FLAG, nullptr);
    }

    ~Providers()
    {
        for (auto h : { aes_ecb, aes_cbc, sha1, sha256, hmac_sha1, hmac_sha256 })
            if (h) BCryptCloseAlgorithmProvider(h, 0);
    }

    static bool open(BCRYPT_ALG_HANDLE *h, const wchar_t *alg, ULONG flags, const wchar_t *mode)
    {
        if (!NT_SUCCESS(BCryptOpenAlgorithmProvider(h, alg, nullptr, flags))) return false;
        if (!mode) return true;
        return NT_SUCCESS(BCryptSetProperty(*h, BCRYPT_CHAINING_MODE, (PUCHAR)mode,
                                            (ULONG)((wcslen(mode) + 1) * sizeof(wchar_t)), 0));
    }
};

Providers &providers()
{
    static Providers p;
    return p;
}

// One cached key per mode: the hot path re-uses the same tweak key for a whole file, and building a
// key object costs far more than the 16 bytes it then encrypts.
struct KeyCache
{
    unsigned char bytes[16] = {};
    BCRYPT_KEY_HANDLE handle = nullptr;
    bool valid = false;

    ~KeyCache() { if (handle) BCryptDestroyKey(handle); }

    BCRYPT_KEY_HANDLE get(BCRYPT_ALG_HANDLE alg, const unsigned char *key)
    {
        if (valid && std::memcmp(bytes, key, 16) == 0) return handle;
        if (handle) { BCryptDestroyKey(handle); handle = nullptr; }
        valid = false;
        if (!NT_SUCCESS(BCryptGenerateSymmetricKey(alg, &handle, nullptr, 0, (PUCHAR)key, 16, 0)))
            return nullptr;
        std::memcpy(bytes, key, 16);
        valid = true;
        return handle;
    }
};

thread_local KeyCache ecb_keys;
thread_local KeyCache cbc_keys;

void increment_be(unsigned char *counter, std::uint64_t by)
{
    for (int i = 15; i >= 0 && by; i--) {
        by += counter[i];
        counter[i] = static_cast<unsigned char>(by);
        by >>= 8;
    }
}

} // namespace

CngCryptoOperations::CngCryptoOperations() {}
CngCryptoOperations::~CngCryptoOperations() {}

int CngCryptoOperations::aes(bool encrypt, const wchar_t *mode, const unsigned char *src, unsigned char *dst,
                             int size, const unsigned char *key, int key_size, unsigned char *iv) const
{
    if (key_size != 128) return -1;
    if (size < 0 || (size & 0xF) != 0) return -1;   // no padding, as there
    if (size == 0) return 0;

    auto &p = providers();
    if (!p.ok) return -1;

    const bool cbc = std::wcscmp(mode, BCRYPT_CHAIN_MODE_CBC) == 0;
    BCRYPT_KEY_HANDLE k = cbc ? cbc_keys.get(p.aes_cbc, key) : ecb_keys.get(p.aes_ecb, key);
    if (!k) return -1;

    // CNG writes the chained IV back into the buffer it is given; OpenSSL's EVP copies it. The
    // caller-visible IV rule is applied by the CBC wrappers below, so CNG gets a scratch copy.
    unsigned char scratch[16];
    PUCHAR ivp = nullptr;
    ULONG ivn = 0;
    if (cbc) { std::memcpy(scratch, iv, 16); ivp = scratch; ivn = 16; }

    ULONG out = 0;
    NTSTATUS s = encrypt
        ? BCryptEncrypt(k, (PUCHAR)src, (ULONG)size, nullptr, ivp, ivn, dst, (ULONG)size, &out, 0)
        : BCryptDecrypt(k, (PUCHAR)src, (ULONG)size, nullptr, ivp, ivn, dst, (ULONG)size, &out, 0);
    return (NT_SUCCESS(s) && out == (ULONG)size) ? 0 : -1;
}

int CngCryptoOperations::aes_cbc_encrypt(const unsigned char *src, unsigned char *dst, int size, const unsigned char *key, int key_size, unsigned char *iv) const
{
    if (size == 0) return 0;
    int r = aes(true, BCRYPT_CHAIN_MODE_CBC, src, dst, size, key, key_size, iv);
    if (r != 0) return r;
    std::memcpy(iv, dst + size - 0x10, 0x10);        // the new IV is the last encoded block
    return 0;
}

int CngCryptoOperations::aes_cbc_decrypt(const unsigned char *src, unsigned char *dst, int size, const unsigned char *key, int key_size, unsigned char *iv) const
{
    if (size == 0) return 0;
    unsigned char next[0x10];
    std::memcpy(next, src + size - 0x10, 0x10);       // before the call: src and dst may alias
    int r = aes(false, BCRYPT_CHAIN_MODE_CBC, src, dst, size, key, key_size, iv);
    if (r != 0) return r;
    std::memcpy(iv, next, 0x10);
    return 0;
}

int CngCryptoOperations::aes_ecb_encrypt(const unsigned char *src, unsigned char *dst, int size, const unsigned char *key, int key_size) const
{
    return aes(true, BCRYPT_CHAIN_MODE_ECB, src, dst, size, key, key_size, nullptr);
}

int CngCryptoOperations::aes_ecb_decrypt(const unsigned char *src, unsigned char *dst, int size, const unsigned char *key, int key_size) const
{
    return aes(false, BCRYPT_CHAIN_MODE_ECB, src, dst, size, key, key_size, nullptr);
}

// CTR built on ECB: the keystream is the encrypted counter, big-endian, one block at a time. The
// last partial block is handled, as OpenSSL does. Then iv advances by size / 16 - WHOLE blocks only,
// from the value it had on entry: EVP never writes the counter back, so psvpfsparser does the
// addition itself, and this reproduces that addition rather than the counter's true position.
int CngCryptoOperations::aes_ctr(const unsigned char *src, unsigned char *dst, int size, const unsigned char *key, int key_size, unsigned char *iv) const
{
    if (key_size != 128) return -1;
    if (size < 0) return -1;

    unsigned char counter[16], stream[16];
    std::memcpy(counter, iv, 16);

    for (int off = 0; off < size; off += 16) {
        if (aes_ecb_encrypt(counter, stream, 16, key, key_size) != 0) return -1;
        int n = (size - off) < 16 ? (size - off) : 16;
        for (int i = 0; i < n; i++) dst[off + i] = src[off + i] ^ stream[i];
        increment_be(counter, 1);
    }

    increment_be(iv, static_cast<std::uint64_t>(size / 0x10));
    return 0;
}

int CngCryptoOperations::aes_ctr_encrypt(const unsigned char *src, unsigned char *dst, int size, const unsigned char *key, int key_size, unsigned char *iv)
{
    return aes_ctr(src, dst, size, key, key_size, iv);
}

int CngCryptoOperations::aes_ctr_decrypt(const unsigned char *src, unsigned char *dst, int size, const unsigned char *key, int key_size, unsigned char *iv)
{
    return aes_ctr(src, dst, size, key, key_size, iv);
}

// AES-CMAC, RFC 4493.
int CngCryptoOperations::aes_cmac(const unsigned char *src, unsigned char *dst, int size, const unsigned char *key, int key_size) const
{
    cmac_calls++;
    if (key_size != 128) return -1;
    if (size < 0) return -1;

    auto shift_left = [](const unsigned char *in, unsigned char *out) {
        unsigned char carry = 0;
        for (int i = 15; i >= 0; i--) {
            unsigned char b = in[i];
            out[i] = static_cast<unsigned char>((b << 1) | carry);
            carry = (b & 0x80) ? 1 : 0;
        }
    };

    unsigned char zero[16] = {}, L[16], K1[16], K2[16];
    if (aes_ecb_encrypt(zero, L, 16, key, key_size) != 0) return -1;
    shift_left(L, K1);
    if (L[0] & 0x80) K1[15] ^= 0x87;
    shift_left(K1, K2);
    if (K1[0] & 0x80) K2[15] ^= 0x87;

    int blocks = (size + 15) / 16;
    bool complete = size > 0 && (size % 16) == 0;
    if (blocks == 0) blocks = 1;

    unsigned char last[16] = {};
    const unsigned char *tail = src + (blocks - 1) * 16;
    if (complete) {
        for (int i = 0; i < 16; i++) last[i] = tail[i] ^ K1[i];
    } else {
        int rem = size - (blocks - 1) * 16;
        for (int i = 0; i < rem; i++) last[i] = tail[i];
        last[rem] = 0x80;
        for (int i = 0; i < 16; i++) last[i] ^= K2[i];
    }

    unsigned char x[16] = {}, y[16];
    for (int b = 0; b < blocks - 1; b++) {
        for (int i = 0; i < 16; i++) y[i] = x[i] ^ src[b * 16 + i];
        if (aes_ecb_encrypt(y, x, 16, key, key_size) != 0) return -1;
    }
    for (int i = 0; i < 16; i++) y[i] = x[i] ^ last[i];
    return aes_ecb_encrypt(y, dst, 16, key, key_size);
}

int CngCryptoOperations::hash(const wchar_t *alg, bool hmac, const unsigned char *src, unsigned char *dst, int size,
                              const unsigned char *key, int key_size, int out_size) const
{
    auto &p = providers();
    if (!p.ok || size < 0) return -1;

    BCRYPT_ALG_HANDLE h;
    const bool is1 = std::wcscmp(alg, BCRYPT_SHA1_ALGORITHM) == 0;
    if (hmac) h = is1 ? p.hmac_sha1 : p.hmac_sha256;
    else      h = is1 ? p.sha1 : p.sha256;

    NTSTATUS s = BCryptHash(h, hmac ? (PUCHAR)key : nullptr, hmac ? (ULONG)key_size : 0,
                            (PUCHAR)src, (ULONG)size, dst, (ULONG)out_size);
    return NT_SUCCESS(s) ? 0 : -1;
}

int CngCryptoOperations::sha1(const unsigned char *src, unsigned char *dst, int size) const
{
    return hash(BCRYPT_SHA1_ALGORITHM, false, src, dst, size, nullptr, 0, 20);
}

int CngCryptoOperations::sha256(const unsigned char *src, unsigned char *dst, int size) const
{
    return hash(BCRYPT_SHA256_ALGORITHM, false, src, dst, size, nullptr, 0, 32);
}

int CngCryptoOperations::hmac_sha1(const unsigned char *src, unsigned char *dst, int size, const unsigned char *key, int key_size) const
{
    return hash(BCRYPT_SHA1_ALGORITHM, true, src, dst, size, key, key_size, 20);
}

int CngCryptoOperations::hmac_sha256(const unsigned char *src, unsigned char *dst, int size, const unsigned char *key, int key_size) const
{
    return hash(BCRYPT_SHA256_ALGORITHM, true, src, dst, size, key, key_size, 32);
}
