// SPDX-License-Identifier: GPL-2.0-or-later
// Stand-in for <openssl/evp.h>: exactly the nine EVP calls Vita3K's firmware code makes, over
// Windows CNG, so it builds without OpenSSL.
//
// Measured surface (grep of pup.cpp and sce_utils.cpp): EVP_CIPHER_fetch with "AES-128-CBC",
// "AES-128-CTR" and "AES-256-CBC"; decryption only; padding always switched off. That is all this
// implements. Anything else fails loudly at fetch time rather than quietly producing wrong bytes.
//
// Streaming as OpenSSL does it: an Update may be followed by another on the same context, CBC keeps
// the last ciphertext block as its next IV, and CTR keeps its counter and its position inside the
// current keystream block. With padding off, a trailing partial CBC block is not output - OpenSSL
// would hold it and then fail Final.

#pragma once

#include <cstdint>

struct EVP_CIPHER;
struct EVP_CIPHER_CTX;

EVP_CIPHER *EVP_CIPHER_fetch(void *libctx, const char *algorithm, const char *properties);
void EVP_CIPHER_free(EVP_CIPHER *cipher);

EVP_CIPHER_CTX *EVP_CIPHER_CTX_new();
void EVP_CIPHER_CTX_free(EVP_CIPHER_CTX *ctx);
int EVP_CIPHER_CTX_set_padding(EVP_CIPHER_CTX *ctx, int pad);

int EVP_DecryptInit_ex(EVP_CIPHER_CTX *ctx, const EVP_CIPHER *type, void *impl,
                       const unsigned char *key, const unsigned char *iv);
int EVP_DecryptUpdate(EVP_CIPHER_CTX *ctx, unsigned char *out, int *outl,
                      const unsigned char *in, int inl);
int EVP_DecryptFinal_ex(EVP_CIPHER_CTX *ctx, unsigned char *outm, int *outl);
