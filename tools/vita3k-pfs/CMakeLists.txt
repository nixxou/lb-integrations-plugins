# vita3k-pfs, built against a Vita3K checkout rather than vendoring it.
#
# One product: vita3k-pfs.exe, which decrypts an installed PS Vita app through its PFS layer - the
# step Vita3K's installer performs and a plain unzip cannot. The Vita3K plugin runs it as a separate
# process; nothing here is loaded into a host.
#
# Only psvpfsparser is compiled, and not all of it - measured, not guessed. Of its sources, exactly
# these reach outside the C++ standard library, and all of them are left out:
#
#   OpenSSLCryptoOperations.cpp, CryptoOperationsFactory.cpp   OpenSSL  -> cng_crypto.cpp instead
#   PsvPfsParserConfig.cpp, rif2zrif.cpp, zrif2rif.cpp         zRIF     -> the key is read from the
#                                                                          licence at 0x50 (main.cpp)
#   F00DFileKeyEncryptor.cpp, F00DKeyEncryptorFactory.cpp      boost    -> only the native encryptor
#                                                                          is used, as Vita3K does
#
# Utils.cpp names boost too, but only under #ifdef PSVPFS_BOOST, which is never defined here.
#
# Point PSVPFS_SOURCE_DIR at psvpfsparser inside a Vita3K checkout - it is a nested submodule:
#
#   git -C ..\Vita3K submodule update --init --recursive --depth 1 external/psvpfstools
#   cmake -S tools/vita3k-pfs -B build/pfs -G Ninja ^
#         -DPSVPFS_SOURCE_DIR=../Vita3K/external/psvpfstools/psvpfsparser -DCMAKE_BUILD_TYPE=Release
#   cmake --build build/pfs
#
# Measured against psvpfsparser d14381f871a69009bd18b2aaec2213a6738bebba, the commit Vita3K pins.

cmake_minimum_required(VERSION 3.20)
project(vita3k-pfs CXX)

set(CMAKE_CXX_STANDARD 17)
set(CMAKE_CXX_STANDARD_REQUIRED ON)

# One file, no redistributable beside it - same reasoning as melonds-nand.
set(CMAKE_MSVC_RUNTIME_LIBRARY "MultiThreaded$<$<CONFIG:Debug>:Debug>")

if (NOT DEFINED PSVPFS_SOURCE_DIR)
    message(FATAL_ERROR
        "PSVPFS_SOURCE_DIR is not set. Point it at psvpfsparser inside a Vita3K checkout:\n"
        "  git -C <Vita3K> submodule update --init --recursive --depth 1 external/psvpfstools")
endif()

# Relative to the repository root, so the command above works as written from there.
get_filename_component(PSVPFS_SOURCE_DIR "${PSVPFS_SOURCE_DIR}" ABSOLUTE BASE_DIR "${CMAKE_CURRENT_SOURCE_DIR}/../..")

if (NOT EXISTS "${PSVPFS_SOURCE_DIR}/PfsFilesystem.cpp")
    message(FATAL_ERROR "no PfsFilesystem.cpp under ${PSVPFS_SOURCE_DIR} - is PSVPFS_SOURCE_DIR right?")
endif()

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
list(TRANSFORM PSVPFS_SOURCES PREPEND "${PSVPFS_SOURCE_DIR}/")

add_executable(vita3k-pfs
    main.cpp
    cng_crypto.cpp
    ${PSVPFS_SOURCES}
)

target_include_directories(vita3k-pfs PRIVATE "${CMAKE_CURRENT_SOURCE_DIR}" "${PSVPFS_SOURCE_DIR}")
target_compile_definitions(vita3k-pfs PRIVATE _CRT_SECURE_NO_WARNINGS NOMINMAX WIN32_LEAN_AND_MEAN UNICODE _UNICODE)
target_link_libraries(vita3k-pfs PRIVATE bcrypt)

if (MSVC)
    target_compile_options(vita3k-pfs PRIVATE /utf-8 /EHsc)
endif()
