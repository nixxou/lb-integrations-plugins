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

// The seven Platform functions melonDS's NAND code actually calls.
//
// melonDS's core is written against a Platform layer the frontend supplies - 70 functions in
// Platform.h, covering files, threads, networking, savestates and more. A tool that only opens a
// NAND needs almost none of them: DSi_NAND.cpp and FATIO.cpp between them call OpenLocalFile,
// CloseFile, FileRead, FileWrite, FileSeek, FileLength and Log, and nothing else. Measured by
// grepping those two files, not assumed - which is what makes this tool a few hundred lines rather
// than a fork of the frontend.
//
// OpenLocalFile normally resolves a relative path against the emulator's own directory. Here every
// path comes from the command line, so it is the same thing as OpenFile: the caller said what it
// meant.

#include "Platform.h"

#include <cstdarg>
#include <cstdio>
#include <string>

namespace melonDS::Platform
{

struct FileHandle
{
    FILE* fp;
};

static const char* ModeString(FileMode mode)
{
    bool read = (mode & FileMode::Read) != FileMode::None;
    bool write = (mode & FileMode::Write) != FileMode::None;
    bool append = (mode & FileMode::Append) != FileMode::None;
    bool preserve = (mode & FileMode::Preserve) != FileMode::None;
    bool text = (mode & FileMode::Text) != FileMode::None;

    if (append) return text ? "a" : "ab";
    if (write && read) return preserve ? (text ? "r+" : "r+b") : (text ? "w+" : "w+b");
    if (write) return text ? "w" : "wb";
    return text ? "r" : "rb";
}

FileHandle* OpenFile(const std::string& path, FileMode mode)
{
    if ((mode & FileMode::ReadWrite) == FileMode::None) return nullptr;

    FILE* fp = std::fopen(path.c_str(), ModeString(mode));
    if (!fp) return nullptr;

    auto* handle = new FileHandle;
    handle->fp = fp;
    return handle;
}

FileHandle* OpenLocalFile(const std::string& path, FileMode mode)
{
    // Every path this tool is given is one the caller spelled out.
    return OpenFile(path, mode);
}

bool CloseFile(FileHandle* file)
{
    if (!file) return false;
    bool ok = std::fclose(file->fp) == 0;
    delete file;
    return ok;
}

bool IsEndOfFile(FileHandle* file) { return file && std::feof(file->fp) != 0; }

bool FileReadLine(char* str, int count, FileHandle* file)
{
    return file && std::fgets(str, count, file->fp) != nullptr;
}

bool FileSeek(FileHandle* file, s64 offset, FileSeekOrigin origin)
{
    if (!file) return false;
    int whence = origin == FileSeekOrigin::Start ? SEEK_SET
               : origin == FileSeekOrigin::Current ? SEEK_CUR
               : SEEK_END;
    return _fseeki64(file->fp, offset, whence) == 0;
}

void FileRewind(FileHandle* file) { if (file) std::rewind(file->fp); }

u64 FilePosition(FileHandle* file) { return file ? (u64)_ftelli64(file->fp) : 0; }

u64 FileRead(void* data, u64 size, u64 count, FileHandle* file)
{
    return file ? (u64)std::fread(data, (size_t)size, (size_t)count, file->fp) : 0;
}

u64 FileWrite(const void* data, u64 size, u64 count, FileHandle* file)
{
    return file ? (u64)std::fwrite(data, (size_t)size, (size_t)count, file->fp) : 0;
}

u64 FileWriteFormatted(FileHandle* file, const char* fmt, ...)
{
    if (!file || !fmt) return 0;
    std::va_list args;
    va_start(args, fmt);
    u64 written = (u64)std::vfprintf(file->fp, fmt, args);
    va_end(args);
    return written;
}

bool FileFlush(FileHandle* file) { return file && std::fflush(file->fp) == 0; }

u64 FileLength(FileHandle* file)
{
    if (!file) return 0;
    long long pos = _ftelli64(file->fp);
    _fseeki64(file->fp, 0, SEEK_END);
    long long len = _ftelli64(file->fp);
    _fseeki64(file->fp, pos, SEEK_SET);
    return (u64)len;
}

/// melonDS logs a great deal at Debug level while walking a NAND. Only warnings and errors are
/// printed, and on stderr, so a caller can read this tool's real output on stdout.
void Log(LogLevel level, const char* fmt, ...)
{
    if (level != LogLevel::Warn && level != LogLevel::Error) return;
    std::va_list args;
    va_start(args, fmt);
    std::vfprintf(stderr, fmt, args);
    va_end(args);
}

}
