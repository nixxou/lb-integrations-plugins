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

// Force-included ahead of melonDS's headers when building with MSVC.
//
// melonDS ships its Windows builds from MinGW - the release preset is release-mingw-x86_64 - so its
// headers use GCC spellings that MSVC has never had to parse. One of them is load-bearing here:
// DSi_AES.h, which DSi_NAND.cpp includes, declares
//
//     __attribute((always_inline)) static void Bswap128(void* Dst, const void* Src)
//
// and MSVC stops on the unknown `__attribute`, taking every use of Bswap128 down with it.
//
// Neutralising the keyword is a hint dropped, not behaviour changed: always_inline is an
// optimisation request, and the function is a sixteen-byte reverse that any compiler inlines on its
// own at /O2. Doing it here rather than by editing melonDS's tree keeps that tree pristine, so it
// can be re-cloned at any tag without carrying a patch.

#pragma once

#ifdef _MSC_VER

// GCC's attribute syntax, in both spellings melonDS uses.
#ifndef __attribute
#define __attribute(x)
#endif
#ifndef __attribute__
#define __attribute__(x)
#endif

// #pragma GCC diagnostic push/pop, which MSVC reports as unknown pragma 4068.
#pragma warning(disable : 4068)

// The POSIX endianness macros. melonDS's vendored sha1.c works them out from <endian.h> or the BSD
// equivalents, finds neither under MSVC, and stops on a deliberate #error telling the porter to say
// what the byte order is. Windows runs on x86-64 and ARM64, both little-endian, and Microsoft ships
// no big-endian target - so this is a statement of fact, not a guess.
#ifndef LITTLE_ENDIAN
#define LITTLE_ENDIAN 1234
#endif
#ifndef BIG_ENDIAN
#define BIG_ENDIAN 4321
#endif
#ifndef PDP_ENDIAN
#define PDP_ENDIAN 3412
#endif
#ifndef BYTE_ORDER
#define BYTE_ORDER LITTLE_ENDIAN
#endif

#endif
