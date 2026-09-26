// SPDX-License-Identifier: GPL-2.0-or-later
// ICryptoOperations over Windows CNG (bcrypt.dll), so the PFS decryptor needs no OpenSSL.
//
// It reproduces OpenSSLCryptoOperations.cpp from psvpfsparser SEMANTICALLY, including the parts that
// are not obvious from the interface:
//
//   - AES key sizes are in BITS and must be 128, as there.
//   - HMAC key sizes are in BYTES.
//   - CBC leaves the last ciphertext block in iv, so a caller can chain calls.
//   - CTR advances iv by size / 16 AFTER the call - whole blocks only, as there.
//
// The proof it is right is not this comment: it is the probe comparing what the tool decrypts
// against a reference install made by the emulator itself.

#pragma once

#include "ICryptoOperations.h"

#include <cstdint>

class CngCryptoOperations : public ICryptoOperations
{
public:
    CngCryptoOperations();
    ~CngCryptoOperations() override;

    int aes_cbc_encrypt(const unsigned char *src, unsigned char *dst, int size, const unsigned char *key, int key_size, unsigned char *iv) const override;
    int aes_cbc_decrypt(const unsigned char *src, unsigned char *dst, int size, const unsigned char *key, int key_size, unsigned char *iv) const override;

    int aes_ctr_encrypt(const unsigned char *src, unsigned char *dst, int size, const unsigned char *key, int key_size, unsigned char *iv) override;
    int aes_ctr_decrypt(const unsigned char *src, unsigned char *dst, int size, const unsigned char *key, int key_size, unsigned char *iv) override;

    int aes_ecb_encrypt(const unsigned char *src, unsigned char *dst, int size, const unsigned char *key, int key_size) const override;
    int aes_ecb_decrypt(const unsigned char *src, unsigned char *dst, int size, const unsigned char *key, int key_size) const override;

    int aes_cmac(const unsigned char *src, unsigned char *dst, int size, const unsigned char *key, int key_size) const override;

    int sha1(const unsigned char *src, unsigned char *dst, int size) const override;
    int sha256(const unsigned char *src, unsigned char *dst, int size) const override;

    int hmac_sha1(const unsigned char *src, unsigned char *dst, int size, const unsigned char *key, int key_size) const override;
    int hmac_sha256(const unsigned char *src, unsigned char *dst, int size, const unsigned char *key, int key_size) const override;

    // How many times aes_cmac was reached. OpenSSL's version passes the key size in bits where
    // EVP_MAC_init wants bytes, so what it computes is not obviously a CMAC at all; whether the
    // path is even taken is measured rather than argued.
    mutable std::uint64_t cmac_calls = 0;

    // One cached key per mode: the hot path re-uses the same tweak key for a whole file, and building
    // a key object costs far more than the 16 bytes it then encrypts. PER INSTANCE, not thread_local:
    // in the DLL the thread is the host's and outlives the library, so a thread-local cache would hold
    // CNG key handles past the FreeLibrary that follows every operation.
    struct KeyCache
    {
        unsigned char bytes[16] = {};
        void *handle = nullptr;          // BCRYPT_KEY_HANDLE
        bool valid = false;
    };

    CngCryptoOperations(const CngCryptoOperations &) = delete;
    CngCryptoOperations &operator=(const CngCryptoOperations &) = delete;

private:
    mutable KeyCache ecb_keys_;
    mutable KeyCache cbc_keys_;

    int aes(bool encrypt, const wchar_t *mode, const unsigned char *src, unsigned char *dst, int size,
            const unsigned char *key, int key_size, unsigned char *iv) const;
    int hash(const wchar_t *alg, bool hmac, const unsigned char *src, unsigned char *dst, int size,
             const unsigned char *key, int key_size, int out_size) const;
    int aes_ctr(const unsigned char *src, unsigned char *dst, int size, const unsigned char *key, int key_size, unsigned char *iv) const;
};
