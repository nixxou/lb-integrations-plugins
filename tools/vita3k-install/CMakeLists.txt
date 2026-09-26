# vita3k-install, built against a Vita3K checkout rather than vendoring it.
#
# The two installs Vita3K performs and a plain unzip cannot, as two products from one set of objects
# - the same arrangement as melonds-nand:
#
#   vita3k-install.dll      the C door the Vita3K plugin loads (api.cpp), shipped as
#                           native\vita3k-install.native so LaunchBox does not try to load it
#   vita3k-installtool.exe  the same operations from a command line (main.cpp), for doing them by
#                           hand and for the comparisons against installs made by Vita3K itself
#
#   decrypt    an installed app through its PFS layer (NoNpDRM dumps)
#   firmware   a PS Vita system update (.PUP) into a virtual filesystem
#
# EVERY SOURCE OF VITA3K'S IS COMPILED UNMODIFIED, from the checkout. What they reach for outside the
# C++ standard library is supplied by shims in shim\ - measured by grep, not guessed:
#
#   OpenSSL      nine EVP calls, AES-128/256 CBC and AES-128 CTR, decryption only  -> shim\evp_cng.cpp
#                (and psvpfsparser's ICryptoOperations                              -> cng_crypto.cpp)
#   boost        Vita3K's `fs` is boost::filesystem                                -> shim\util\fs.h
#   fmt, spdlog  two fmt::format calls and the LOG_* macros                         -> shim\util\*.h
#
# psvpfsparser: the sources that need OpenSSL, zRIF or boost are left out (see the list below);
# Utils.cpp names boost too, but only under #ifdef PSVPFS_BOOST, never defined here.
#
# Point VITA3K_SOURCE_DIR at a Vita3K checkout with four submodules fetched:
#
#   git -C ..\Vita3K submodule update --init --recursive --depth 1 ^
#       external/psvpfstools external/libfat16 external/vita-toolchain external/miniz
#   cmake -S tools/vita3k-install -B build/vita3k -G Ninja -DVITA3K_SOURCE_DIR=../Vita3K -DCMAKE_BUILD_TYPE=Release
#   cmake --build build/vita3k
#
# Measured against Vita3K 0.2.1 build 4098 (bbd5c362) and psvpfsparser d14381f.

cmake_minimum_required(VERSION 3.20)
project(vita3k-install CXX C)

# 20 for std::format, which stands in for fmt::format.
set(CMAKE_CXX_STANDARD 20)
set(CMAKE_CXX_STANDARD_REQUIRED ON)

# One file, no redistributable beside it - same reasoning as melonds-nand.
set(CMAKE_MSVC_RUNTIME_LIBRARY "MultiThreaded$<$<CONFIG:Debug>:Debug>")

if (NOT DEFINED VITA3K_SOURCE_DIR)
    message(FATAL_ERROR
        "VITA3K_SOURCE_DIR is not set. Point it at a Vita3K checkout:\n"
        "  git clone https://github.com/Vita3K/Vita3K.git")
endif()

# Relative to the repository root, so the command above works as written from there.
get_filename_component(VITA3K_SOURCE_DIR "${VITA3K_SOURCE_DIR}" ABSOLUTE BASE_DIR "${CMAKE_CURRENT_SOURCE_DIR}/../..")

set(PSVPFS_DIR   "${VITA3K_SOURCE_DIR}/external/psvpfstools/psvpfsparser")
set(FAT16_DIR    "${VITA3K_SOURCE_DIR}/external/libfat16")
set(TOOLCHAIN_DIR "${VITA3K_SOURCE_DIR}/external/vita-toolchain/src")
set(MINIZ_DIR    "${VITA3K_SOURCE_DIR}/external/miniz")
set(PACKAGES_DIR "${VITA3K_SOURCE_DIR}/vita3k/packages")

foreach (probe "${PSVPFS_DIR}/PfsFilesystem.cpp" "${FAT16_DIR}/src/fat16.cpp" "${TOOLCHAIN_DIR}/self.h"
               "${MINIZ_DIR}/miniz.c" "${PACKAGES_DIR}/src/pup.cpp")
    if (NOT EXISTS "${probe}")
        message(FATAL_ERROR "missing ${probe} - is VITA3K_SOURCE_DIR right, and are its submodules fetched?")
    endif()
endforeach()

set(PSVPFS_SOURCES
    F00DNativeKeyEncryptor.cpp
    FilesDbParser.cpp
    FlagOperations.cpp
    HashTree.cpp
    IcvPrimitives.cpp
    LocalKeyGenerator.cpp
    LocalKeys.cpp
    NodeIcvCalculator.cpp
    PfsCryptEngine.cpp
    PfsCryptEngineBase.cpp
    PfsCryptEngineSelectors.cpp
    PfsFile.cpp
    PfsFilesystem.cpp
    PfsKeyGenerator.cpp
    PfsKeys.cpp
    PfsPageMapper.cpp
    SceKernelUtilsForDriver.cpp
    SceSblSsMgrForDriver.cpp
    SecretGenerator.cpp
    UnicvDbParser.cpp
    UnicvDbTypes.cpp
    UnicvDbUtils.cpp
    Utils.cpp
)
list(TRANSFORM PSVPFS_SOURCES PREPEND "${PSVPFS_DIR}/")

# Vita3K's firmware install: the PUP reader, the SCE segment decryptor and key table, the FAT16 and
# exFAT readers. sce_utils.cpp also carries SELF decryption, unused here but compiled as it stands.
set(FIRMWARE_SOURCES
    "${PACKAGES_DIR}/src/pup.cpp"
    "${PACKAGES_DIR}/src/sce_utils.cpp"
    "${PACKAGES_DIR}/src/exfat.cpp"
    "${FAT16_DIR}/src/fat16.cpp"
    "${MINIZ_DIR}/miniz.c"
)

# The shared middle: everything but the two front doors. api.cpp is deliberately NOT in it - its
# symbols are dllexport-ed, and a static library only contributes the objects the linker has a reason
# to pull, so the DLL would come out with no exports at all. Measured with melonds-nand.
add_library(vita3k-install-core STATIC
    core.cpp
    firmware.cpp
    cng_crypto.cpp
    shim/evp_cng.cpp
    ${PSVPFS_SOURCES}
    ${FIRMWARE_SOURCES}
)

# shim\ FIRST: its util\ and openssl\ must win over anything else of the same name.
target_include_directories(vita3k-install-core PUBLIC
    "${CMAKE_CURRENT_SOURCE_DIR}/shim"
    "${CMAKE_CURRENT_SOURCE_DIR}"
    "${PSVPFS_DIR}"
    "${PACKAGES_DIR}/include"
    "${FAT16_DIR}/include"
    "${TOOLCHAIN_DIR}"
    "${MINIZ_DIR}"
)
target_compile_definitions(vita3k-install-core PUBLIC _CRT_SECURE_NO_WARNINGS NOMINMAX WIN32_LEAN_AND_MEAN UNICODE _UNICODE)
target_link_libraries(vita3k-install-core PUBLIC bcrypt)

if (MSVC)
    # /Zc:preprocessor for __VA_OPT__ in shim\util\log.h. C++-only flags stay off miniz.c.
    target_compile_options(vita3k-install-core PUBLIC /utf-8
        $<$<COMPILE_LANGUAGE:CXX>:/EHsc /Zc:__cplusplus /Zc:preprocessor>)
endif()

add_library(vita3k-install SHARED api.cpp)
target_link_libraries(vita3k-install PRIVATE vita3k-install-core)

add_executable(vita3k-installtool main.cpp)
target_link_libraries(vita3k-installtool PRIVATE vita3k-install-core)
