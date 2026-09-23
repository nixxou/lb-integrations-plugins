# Pack a folder of DSiWare TMDs into the index the plugin carries.
#
#   .\tools\pack-tmd-library.ps1 -From "G:\tmd"
#
# THREE DECISIONS, each one measured rather than assumed, and together they take 4.1 MB to 0.59 MB.
#
# 1. THE CERTIFICATES ARE DROPPED. A downloaded TMD is 2312 bytes: 520 of metadata followed by 1792
#    of certificate chain - and that chain is THE SAME 1792 bytes in all 1803 entries that have one
#    (measured: exactly two distinct tails exist, that one and empty). melonDS reads
#    sizeof(TitleMetadata) = 520 and never looks further, so the tail is 3.1 MB of one value repeated.
#
# 2. THE INDEX IS NOT COMPRESSED. It is a sorted table of fixed-size records, so finding a title is a
#    binary search that reads about eleven of them - some four hundred bytes out of a megabyte.
#    Compressing it would mean inflating the whole thing to look at one row.
#
# 3. THE PAYLOAD IS COMPRESSED IN SMALL BLOCKS, sixteen entries each. One lookup inflates one block:
#    8 KB, held for as long as it takes to copy 520 bytes out of it. Blocks of 128 would save 17 KB
#    over the whole file and inflate 65 KB a time; one block for everything would save 5 KB more and
#    inflate 959 KB. Sixteen is where the curve flattens.
#
# The format, all little-endian unless said otherwise:
#
#   magic       8 bytes   "MDSTMD" 0x00 0x02
#   count       u32       number of entries
#   blocks      u32       number of blocks
#   perBlock    u16       entries per block
#   reserved    u16
#   records     count x 36, sorted by title id:
#                 titleId   8 bytes big-endian, as the NAND tree spells it
#                 version   u16, the TitleVersion field
#                 block     u16, which block holds it
#                 offset    u16, where it starts once that block is inflated
#                 length     u16, its length there
#                 sha1      20 bytes, the hash of the CONTENT the TMD describes
#   blockTable  blocks x 8: offset u32 from the start of the payload, length u32 compressed
#   payload     the blocks, each one raw deflate
#
# It is a plain file with a documented layout, and this script rebuilds it from anyone's own copies.
# It is not obscured and is not meant to be.
#
# WHAT THESE FILES ARE. A TMD is Nintendo's signed metadata for a title: a title id, save sizes, age
# ratings, the content's SHA-1 and an RSA signature. No game code. They circulate as preservation
# sets; whether a copy belongs in a published build is a decision for whoever publishes it.
#
# THE LAYOUT OF THE SOURCE DOES NOT MATTER. A TMD carries its own title id at 0x18C, its revision at
# 0x1DC and its content hash at 0x1F4, so this reads every file and files it by what it says about
# itself. The sets in circulation name things differently - "<titleid>.<revision>" in one,
# "<Game Name>/tmd.<revision>" in the other - and neither convention is read here.
#
# EVERY DISTINCT FILE IS KEPT. Deduplication is by CONTENT, not by the TitleVersion field: two
# genuinely different TMDs for one title can carry the same version, and keying on it silently lost
# 86 of 1889 files before this was measured.

[CmdletBinding()]
param(
    # Folder to walk, recursively. Any arrangement.
    [Parameter(Mandatory = $true)]
    [string] $From,

    # The file to write. Defaults to the one the plugin embeds.
    [string] $Out,

    # Entries per compressed block.
    [int] $PerBlock = 16
)

$ErrorActionPreference = 'Stop'

$repo = Split-Path -Parent $PSScriptRoot
if (-not $Out) { $Out = Join-Path $repo 'src\MelonDs\tmd-library.bin' }
if (-not (Test-Path $From)) { throw "No such folder: $From" }

$dsiware = 0x00030004
$metadata = 520     # what melonDS reads; everything after it is certificate chain
$maximum = 1MB

function Read-BE([byte[]] $b, [int] $off, [int] $n) {
    $v = [uint32]0
    for ($i = 0; $i -lt $n; $i++) { $v = ($v -shl 8) -bor [uint32]$b[$off + $i] }
    return $v
}

# The leading comma is not decoration. A PowerShell function RETURNS A PIPELINE, so `return $bytes`
# emits the bytes one at a time and the caller collects them back into an Object[] - which
# BinaryWriter.Write does not treat as bytes. `,` wraps the array in a one-element array, so what
# comes out the other side is still a byte[]. Getting this wrong wrote one byte per block, silently.
function Compress([byte[]] $data) {
    $out = New-Object IO.MemoryStream
    $deflate = New-Object IO.Compression.DeflateStream($out, [IO.Compression.CompressionLevel]::Optimal, $true)
    try { $deflate.Write($data, 0, $data.Length) } finally { $deflate.Dispose() }
    return , [byte[]] $out.ToArray()
}

$sha1 = [Security.Cryptography.SHA1]::Create()
$seen = @{}         # sha1 of the whole file -> already taken, so identical copies collapse
$records = New-Object Collections.ArrayList
$ignored = 0

foreach ($file in Get-ChildItem -Path $From -Recurse -File) {
    if ($file.Length -lt $metadata -or $file.Length -gt $maximum) { $ignored++; continue }

    $bytes = [IO.File]::ReadAllBytes($file.FullName)
    if ((Read-BE $bytes 0x18C 4) -ne $dsiware) { $ignored++; continue }

    $digest = ($sha1.ComputeHash($bytes) | ForEach-Object { $_.ToString('x2') }) -join ''
    if ($seen.ContainsKey($digest)) { continue }
    $seen[$digest] = $true

    # A range index on a byte[] gives back an Object[], and BinaryWriter.Write does not treat that
    # as bytes - it binds to the char[] overload and writes UTF-8 text. Cast, every time.
    $null = $records.Add([pscustomobject]@{
        TitleId = [byte[]] $bytes[0x18C..0x193]
        Sort    = (($bytes[0x18C..0x193] | ForEach-Object { $_.ToString('x2') }) -join '')
        Version = Read-BE $bytes 0x1DC 2
        Sha1    = [byte[]] $bytes[0x1F4..0x207]
        Bytes   = [byte[]] $bytes[0..($metadata - 1)]
    })
}

if ($records.Count -eq 0) { throw "Found no DSiWare metadata under $From" }

# Sorted by title id so the plugin can binary-search; revisions of one title land together, and so
# land in the same block or the next.
$sorted = @($records | Sort-Object Sort, Version)

# ── build the blocks, and note where each entry lands inside its own ─────────
$blocks = New-Object Collections.ArrayList
$placement = New-Object Collections.ArrayList
for ($i = 0; $i -lt $sorted.Count; $i += $PerBlock) {
    $slice = $sorted[$i..([Math]::Min($i + $PerBlock, $sorted.Count) - 1)]
    $raw = New-Object IO.MemoryStream
    foreach ($r in $slice) {
        $null = $placement.Add([pscustomobject]@{
            Block = $blocks.Count; Offset = [int]$raw.Position; Length = $r.Bytes.Length
        })
        $raw.Write($r.Bytes, 0, $r.Bytes.Length)
    }
    $null = $blocks.Add((Compress $raw.ToArray()))
    $raw.Dispose()
}

New-Item -ItemType Directory -Force -Path (Split-Path -Parent $Out) | Out-Null
$stream = [IO.File]::Create($Out)
$writer = New-Object IO.BinaryWriter($stream)
try {
    $writer.Write([byte[]] @(0x4D, 0x44, 0x53, 0x54, 0x4D, 0x44, 0x00, 0x02))   # "MDSTMD" 0 2
    $writer.Write([uint32] $sorted.Count)
    $writer.Write([uint32] $blocks.Count)
    $writer.Write([uint16] $PerBlock)
    $writer.Write([uint16] 0)

    for ($i = 0; $i -lt $sorted.Count; $i++) {
        $r = $sorted[$i]; $p = $placement[$i]
        $writer.Write($r.TitleId)                       # 8
        $writer.Write([uint16] $r.Version)              # 2
        $writer.Write([uint16] $p.Block)                # 2
        $writer.Write([uint16] $p.Offset)               # 2
        $writer.Write([uint16] $p.Length)               # 2
        $writer.Write($r.Sha1)                          # 20
    }

    $offset = [uint32]0
    foreach ($b in $blocks) {
        $writer.Write([uint32] $offset)
        $writer.Write([uint32] $b.Length)
        $offset += $b.Length
    }
    foreach ($b in $blocks) { $writer.Write([byte[]] $b, 0, $b.Length) }
}
finally { $writer.Dispose(); $stream.Dispose() }

# READ BACK WHAT WAS WRITTEN. Every mistake this script has made was a silent one - a slice that
# became an Object[] and got written as text, a payload that wrote one byte per block - and each time
# the counters printed at the end were right while the file was wrong. So the file is reopened and a
# spread of entries is decompressed and compared with what went in. It costs a second.
$check = [IO.File]::ReadAllBytes($Out)
$table = 20 + 36 * $sorted.Count
$payload = $table + 8 * $blocks.Count
$declared = 0
for ($i = 0; $i -lt $blocks.Count; $i++) { $declared += [BitConverter]::ToUInt32($check, $table + $i * 8 + 4) }
if ($check.Length -ne $payload + $declared) {
    throw "The file is $($check.Length) bytes but its own block table accounts for $($payload + $declared)."
}

$step = [Math]::Max(1, [int]($sorted.Count / 40))
for ($i = 0; $i -lt $sorted.Count; $i += $step) {
    $r = $sorted[$i]; $p = $placement[$i]
    $o = [BitConverter]::ToUInt32($check, $table + $p.Block * 8)
    $l = [BitConverter]::ToUInt32($check, $table + $p.Block * 8 + 4)

    $packed = New-Object IO.MemoryStream(, [byte[]] $check[($payload + $o)..($payload + $o + $l - 1)])
    $deflate = New-Object IO.Compression.DeflateStream($packed, [IO.Compression.CompressionMode]::Decompress)
    $flat = New-Object IO.MemoryStream
    try { $deflate.CopyTo($flat) } finally { $deflate.Dispose(); $packed.Dispose() }
    $got = $flat.ToArray(); $flat.Dispose()

    for ($k = 0; $k -lt $r.Bytes.Length; $k++) {
        if ($got[$p.Offset + $k] -ne $r.Bytes[$k]) { throw "Entry $i came back different at byte $k." }
    }
}

$titles = ($sorted | ForEach-Object { $_.Sort } | Sort-Object -Unique).Count
$worst = ($blocks | Measure-Object -Property Length -Maximum).Maximum
Write-Host ""
Write-Host "$($sorted.Count) entries for $titles titles, in $($blocks.Count) blocks" -ForegroundColor Green
Write-Host ("  index      {0:N0} bytes, read without inflating anything" -f (20 + 36 * $sorted.Count))
Write-Host ("  a lookup   inflates one block, at most {0:N0} bytes compressed" -f $worst)
Write-Host ("  total      {0:N2} MB" -f ((Get-Item $Out).Length / 1MB))
Write-Host "  $Out"
if ($ignored) { Write-Host "  $ignored file(s) were not DSiWare metadata" }
