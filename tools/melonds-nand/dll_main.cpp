/*
    Copyright 2026 nixxou

    This file is part of melonds-nand, which is built together with source files from melonDS
    (Copyright 2016-2025 melonDS team) and is therefore distributed under the same terms.

    melonds-nand is free software: you can redistribute it and/or modify it under the terms of the
    GNU General Public License as published by the Free Software Foundation, either version 3 of the
    License, or (at your option) any later version.

    It is distributed in the hope that it will be useful, but WITHOUT ANY WARRANTY; without even the
    implied warranty of MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the GNU General
    Public License for more details.

    You should have received a copy of the GNU General Public License along with this program. If
    not, see http://www.gnu.org/licenses/.
*/

// The DLL has no entry point worth writing: the API lives in nand_api.cpp, which is linked in from
// the static library, and there is nothing to set up before the first call. This file exists so the
// shared target has a translation unit of its own, and so that the one thing worth saying about
// loading this library is written somewhere.
//
// NOTHING RUNS AT LOAD TIME, deliberately. A DLL that opened files or spawned threads in DllMain
// would do it inside the frontend's loader lock, which is the classic way to deadlock a host that
// has done nothing wrong.

#include <windows.h>

BOOL APIENTRY DllMain(HMODULE, DWORD reason, LPVOID)
{
    if (reason == DLL_PROCESS_ATTACH) DisableThreadLibraryCalls(nullptr);
    return TRUE;
}
