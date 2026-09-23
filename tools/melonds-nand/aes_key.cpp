/*
    Copyright 2026 nixxou

    This file is part of melonds-nandtool, which is built together with source files from
    melonDS (Copyright 2016-2025 melonDS team) and is therefore distributed under the same terms.

    melonds-nandtool is free software: you can redistribute it and/or modify it under the terms of
    the GNU General Public License as published by the Free Software Foundation, either version 3 of
    the License, or (at your option) any later version.

    It is distributed in the hope that it will be useful, but WITHOUT ANY WARRANTY; without even the
    implied warranty of MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the GNU General
    Public License for more details.

    You should have received a copy of the GNU General Public License along with this program. If
    not, see http://www.gnu.org/licenses/.
*/

// Two methods of melonDS's DSi_AES, supplied here rather than by linking its object.
//
// DSi_NAND.cpp needs exactly one symbol from the rest of the emulator core:
// DSi_AES::DeriveNormalKey. That method is stateless, but it lives in DSi_AES.cpp beside the AES
// engine itself, which references DSi and NDS - so compiling that file would drag in most of the
// emulator to get twenty lines of arithmetic.
//
// Both methods are copied VERBATIM from src/DSi_AES.cpp at tag 1.1, lines 511-547, and the header
// that declares them is melonDS's own. Nothing here is our design; if the upstream algorithm
// changes, this file is wrong and must be updated with it.

#include "DSi_AES.h"

#include <cstring>

namespace melonDS
{

void DSi_AES::ROL16(u8* val, u32 n)
{
    u32 n_coarse = n >> 3;
    u32 n_fine = n & 7;
    u8 tmp[16];

    for (u32 i = 0; i < 16; i++)
    {
        tmp[i] = val[(i - n_coarse) & 0xF];
    }

    for (u32 i = 0; i < 16; i++)
    {
        val[i] = (tmp[i] << n_fine) | (tmp[(i - 1) & 0xF] >> (8 - n_fine));
    }
}

void DSi_AES::DeriveNormalKey(u8* keyX, u8* keyY, u8* normalkey)
{
    const u8 key_const[16] = {0xFF, 0xFE, 0xFB, 0x4E, 0x29, 0x59, 0x02, 0x58,
                              0x2A, 0x68, 0x0F, 0x5F, 0x1A, 0x4F, 0x3E, 0x79};
    u8 tmp[16];

    for (int i = 0; i < 16; i++)
        tmp[i] = keyX[i] ^ keyY[i];

    u32 carry = 0;
    for (int i = 0; i < 16; i++)
    {
        u32 res = tmp[i] + key_const[15-i] + carry;
        tmp[i] = res & 0xFF;
        carry = res >> 8;
    }

    ROL16(tmp, 42);

    memcpy(normalkey, tmp, 16);
}

}
