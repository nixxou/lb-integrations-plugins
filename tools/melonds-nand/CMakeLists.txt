# melonds-nand, built against a melonDS checkout rather than vendoring it.
#
# Two products from one set of objects:
#
#   melonds-nand.dll      the C door the LaunchBox integration plugin calls through P/Invoke
#   melonds-nandtool.exe  the same operations from a command line, for doing them by hand
#
# Only the files melonDS's NAND code actually needs are compiled - measured, not guessed:
# DSi_NAND.cpp calls exactly one function from the rest of the emulator core
# (DSi_AES::DeriveNormalKey, supplied in aes_key.cpp) and seven Platform functions
# (platform_stub.cpp). Everything else it needs is FatFs, tiny-AES and SHA-1, which melonDS carries
# in its own tree.
#
# Point MELONDS_SOURCE_DIR at a melonDS checkout of the tag you intend to match. The NAND format is
# melonDS's, so building against a different version than the emulator you run is the one mistake
# that would matter.
#
#   cmake -S tools/melonds-nand -B build/nand -G Ninja ^
#         -DMELONDS_SOURCE_DIR=../melonDS -DCMAKE_BUILD_TYPE=Release
#   cmake --build build/nand

cmake_minimum_required(VERSION 3.20)
project(melonds-nand CXX C)

set(CMAKE_CXX_STANDARD 17)
set(CMAKE_CXX_STANDARD_REQUIRED ON)

# One file each, no redistributable to ship beside them: the plugin loads the DLL from its own
# folder, and a missing vcruntime would surface as an unexplained failure on someone else's machine.
set(CMAKE_MSVC_RUNTIME_LIBRARY "MultiThreaded$<$<CONFIG:Debug>:Debug>")

if (NOT DEFINED MELONDS_SOURCE_DIR)
    message(FATAL_ERROR
        "MELONDS_SOURCE_DIR is not set. Point it at a melonDS checkout:\n"
        "  git clone --depth 1 --branch 1.1 https://github.com/melonDS-emu/melonDS.git")
endif()

get_filename_component(MELONDS_SOURCE_DIR "${MELONDS_SOURCE_DIR}" ABSOLUTE)
set(MELONDS_SRC "${MELONDS_SOURCE_DIR}/src")

if (NOT EXISTS "${MELONDS_SRC}/DSi_NAND.cpp")
    message(FATAL_ERROR "no DSi_NAND.cpp under ${MELONDS_SRC} - is MELONDS_SOURCE_DIR right?")
endif()

# The shared middle: melonDS's NAND code plus the little this repository adds to make it standalone.
# nand_api.cpp is deliberately NOT here. Its symbols are dllexport-ed, and a static library only
# contributes the objects the linker has a reason to pull - nothing in dll_main.cpp references it, so
# the DLL came out with no exports at all. Each product compiles it directly instead.
add_library(melonds-nand-core STATIC
    platform_stub.cpp
    aes_key.cpp

    "${MELONDS_SRC}/DSi_NAND.cpp"
    "${MELONDS_SRC}/FATIO.cpp"
    "${MELONDS_SRC}/fatfs/ff.c"
    "${MELONDS_SRC}/fatfs/ffsystem.c"
    "${MELONDS_SRC}/fatfs/ffunicode.c"
    "${MELONDS_SRC}/sha1/sha1.c"
    "${MELONDS_SRC}/tiny-AES-c/aes.c"
)

target_include_directories(melonds-nand-core PUBLIC "${MELONDS_SRC}" "${CMAKE_CURRENT_SOURCE_DIR}")

if (MSVC)
    # msvc_compat.h goes in ahead of every translation unit: melonDS's headers are written for the
    # MinGW toolchain its Windows builds use, and one GCC attribute in DSi_AES.h stops MSVC dead.
    # Forcing the shim here keeps the melonDS checkout unpatched.
    target_compile_options(melonds-nand-core PUBLIC
        /W2 /wd4244 /wd4267 /wd4996 /wd4068 /wd4099 /EHsc
        /FImsvc_compat.h)
    target_compile_definitions(melonds-nand-core PUBLIC _CRT_SECURE_NO_WARNINGS NOMINMAX)
endif()

# The library the plugin loads. MDSNAND_BUILDING switches nand_api.h from dllimport to dllexport.
add_library(melonds-nand SHARED dll_main.cpp nand_api.cpp)
target_link_libraries(melonds-nand PRIVATE melonds-nand-core)
target_compile_definitions(melonds-nand PRIVATE MDSNAND_BUILDING)
set_target_properties(melonds-nand PROPERTIES OUTPUT_NAME "melonds-nand" PREFIX "")

# The command line, a thin client of the same API - linked statically so it is one file, and so that
# using it by hand cannot pick up a different DLL than the plugin does.
add_executable(melonds-nandtool main.cpp nand_api.cpp)
target_link_libraries(melonds-nandtool PRIVATE melonds-nand-core)
target_compile_definitions(melonds-nandtool PRIVATE MDSNAND_BUILDING)

install(TARGETS melonds-nand melonds-nandtool RUNTIME DESTINATION . LIBRARY DESTINATION .)
