// SPDX-License-Identifier: GPL-2.0-or-later
// The nine EVP calls of shim/openssl/evp.h, over Windows CNG. See that header for the scope.
//
// Both modes are built on AES-ECB and done a whole buffer at a time - one CNG call per Update, not
// one per block: the firmware's partitions run to over a hundred megabytes.

#include "openssl/evp.h"

#include <windows.h>
#include <bcrypt.h>

#include <cstring>
#include <string>
#include <vector>

#ifndef NT_SUCCESS
#define NT_SUCCESS(s) (((NTSTATUS)(s)) >= 0)
#endif

enum class Mode { Cbc, Ctr };

struct EVP_CIPHER
{
    Mode mode;
    int key_bytes;
};

struct EVP_CIPHER_CTX
{
    Mode mode = Mode::Cbc;
    BCRYPT_KEY_HANDLE key = nullptr;
    unsigned char iv[16] = {};         // CBC: previous ciphertext block. CTR: current counter.
    unsigned char stream[16] = {};     // CTR: keystream of the current counter block
    int stream_used = 16;              // CTR: bytes of it already consumed (16 = none left)
};

namespace {

BCRYPT_ALG_HANDLE ecb()
{
    static BCRYPT_ALG_HANDLE h = [] {
        BCRYPT_ALG_HANDLE a = nullptr;
        if (!NT_SUCCESS(BCryptOpenAlgorithmProvider(&a, BCRYPT_AES_ALGORITHM, nullptr, 0))) return (BCRYPT_ALG_HANDLE) nullptr;
        const wchar_t *mode = BCRYPT_CHAIN_MODE_ECB;
        if (!NT_SUCCESS(BCryptSetProperty(a, BCRYPT_CHAINING_MODE, (PUCHAR)mode,
                                          (ULONG)((wcslen(mode) + 1) * sizeof(wchar_t)), 0))) return (BCRYPT_ALG_HANDLE) nullptr;
        return a;
    }();
    return h;
}

bool ecb_run(BCRYPT_KEY_HANDLE key, bool encrypt, const unsigned char *in, unsigned char *out, std::size_t n)
{
    if (n == 0) return true;
    ULONG done = 0;
    NTSTATUS s = encrypt
        ? BCryptEncrypt(key, (PUCHAR)in, (ULONG)n, nullptr, nullptr, 0, out, (ULONG)n, &done, 0)
        : BCryptDecrypt(key, (PUCHAR)in, (ULONG)n, nullptr, nullptr, 0, out, (ULONG)n, &done, 0);
    return NT_SUCCESS(s) && done == n;
}

void increment_be(unsigned char *counter)
{
    for (int i = 15; i >= 0; i--)
        if (++counter[i] != 0) break;
}

} // namespace

EVP_CIPHER *EVP_CIPHER_fetch(void *, const char *algorithm, const char *)
{
    const std::string name = algorithm ? algorithm : "";
    if (name == "AES-128-CBC") return new EVP_CIPHER{ Mode::Cbc, 16 };
    if (name == "AES-256-CBC") return new EVP_CIPHER{ Mode::Cbc, 32 };
    if (name == "AES-128-CTR") return new EVP_CIPHER{ Mode::Ctr, 16 };
    std::fprintf(stderr, "evp shim: unsupported cipher %s\n", name.c_str());
    return nullptr;
}

void EVP_CIPHER_free(EVP_CIPHER *cipher) { delete cipher; }

EVP_CIPHER_CTX *EVP_CIPHER_CTX_new() { return new EVP_CIPHER_CTX(); }

void EVP_CIPHER_CTX_free(EVP_CIPHER_CTX *ctx)
{
    if (!ctx) return;
    if (ctx->key) BCryptDestroyKey(ctx->key);
    delete ctx;
}

int EVP_CIPHER_CTX_set_padding(EVP_CIPHER_CTX *, int) { return 1; }

int EVP_DecryptInit_ex(EVP_CIPHER_CTX *ctx, const EVP_CIPHER *type, void *, const unsigned char *key, const unsigned char *iv)
{
    if (!ctx || !type || !key || !ecb()) return 0;
    if (ctx->key) { BCryptDestroyKey(ctx->key); ctx->key = nullptr; }
    if (!NT_SUCCESS(BCryptGenerateSymmetricKey(ecb(), &ctx->key, nullptr, 0, (PUCHAR)key, (ULONG)type->key_bytes, 0)))
        return 0;
    ctx->mode = type->mode;
    if (iv) std::memcpy(ctx->iv, iv, 16); else std::memset(ctx->iv, 0, 16);
    ctx->stream_used = 16;
    return 1;
}

int EVP_DecryptUpdate(EVP_CIPHER_CTX *ctx, unsigned char *out, int *outl, const unsigned char *in, int inl)
{
    if (outl) *outl = 0;
    if (!ctx || !ctx->key || inl < 0) return 0;
    if (inl == 0) return 1;

    if (ctx->mode == Mode::Cbc) {
        const std::size_t n = static_cast<std::size_t>(inl) & ~static_cast<std::size_t>(0xF);
        if (n == 0) return 1;
        // Kept before the call: in and out may be the same buffer.
        std::vector<unsigned char> cipher(in, in + n);
        std::vector<unsigned char> plain(n);
        if (!ecb_run(ctx->key, false, cipher.data(), plain.data(), n)) return 0;
        for (std::size_t i = 0; i < n; i++)
            out[i] = plain[i] ^ (i < 16 ? ctx->iv[i] : cipher[i - 16]);
        std::memcpy(ctx->iv, cipher.data() + n - 16, 16);
        if (outl) *outl = static_cast<int>(n);
        return 1;
    }

    // CTR
    std::size_t n = static_cast<std::size_t>(inl), pos = 0;
    while (pos < n && ctx->stream_used < 16) {                 // the rest of a started block
        out[pos] = in[pos] ^ ctx->stream[ctx->stream_used++];
        pos++;
    }
    const std::size_t blocks = (n - pos + 15) / 16;
    if (blocks) {
        std::vector<unsigned char> counters(blocks * 16), stream(blocks * 16);
        for (std::size_t b = 0; b < blocks; b++) {
            std::memcpy(&counters[b * 16], ctx->iv, 16);
            increment_be(ctx->iv);
        }
        if (!ecb_run(ctx->key, true, counters.data(), stream.data(), counters.size())) return 0;
        std::size_t k = 0;
        for (; pos < n; pos++, k++) out[pos] = in[pos] ^ stream[k];
        // A partial last block leaves keystream for the next Update.
        const std::size_t used_in_last = k - (blocks - 1) * 16;
        std::memcpy(ctx->stream, &stream[(blocks - 1) * 16], 16);
        ctx->stream_used = static_cast<int>(used_in_last);
    }
    if (outl) *outl = inl;
    return 1;
}

int EVP_DecryptFinal_ex(EVP_CIPHER_CTX *, unsigned char *, int *outl)
{
    if (outl) *outl = 0;
    return 1;
}
