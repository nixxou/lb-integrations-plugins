# Build the whole pack into ONE file: release\NixxIntegrations.exe.
#
# Three steps, in this order and for a reason. The five plugins are built first, because the release
# is made of their merged DLLs and nothing else. Their files are then staged into one directory -
# this is the only place that decides what a release contains, so the installer project never has to
# reach into a sibling's bin\ folder. And last the installer is published self-contained, with that
# directory handed to it as a property.
#
#   .\build-release.ps1              # the lot
#   .\build-release.ps1 -SkipPlugins # restage and republish from what is already built
#
# The result needs nothing installed on the target machine: it carries the .NET runtime, the five
# plugins and the DSi NAND library. About 60 MB, of which roughly 50 is the runtime.

[CmdletBinding()]
param(
    # Reuse the plugin builds already in bin\Release. Saves a couple of minutes when only the
    # installer changed; never use it for an actual release.
    [switch] $SkipPlugins,

    [ValidateSet('Debug', 'Release')]
    [string] $Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $MyInvocation.MyCommand.Path

# Project -> the folder its files are staged under. These names ARE the plugin folder names the
# installer creates, except no$gba: a dollar in an MSBuild LogicalName is a property expansion
# waiting to happen, so the resource path says "nogba" and Payload.cs maps it back.
$Stage = [ordered]@{
    'Flycast' = 'Nixx-Flycast'
    'MelonDs' = 'Nixx-melonDS'
    'NoGba'   = 'Nixx-nogba'
    'Ppsspp'  = 'Nixx-PPSSPP'
    'Xenia'   = 'Nixx-Xenia'
}

$payload = Join-Path $repo 'build\payload'
$release = Join-Path $repo 'release'

# ── 1. the plugins ──────────────────────────────────────────────────────────

if (-not $SkipPlugins) {
    foreach ($name in $Stage.Keys) {
        Write-Host "Building $name ($Configuration)..." -ForegroundColor Cyan
        dotnet build (Join-Path $repo "src\$name\$name.csproj") -c $Configuration --nologo -v quiet
        if ($LASTEXITCODE -ne 0) { throw "Build failed: $name" }
    }
}

# ── 2. staging ──────────────────────────────────────────────────────────────

if (Test-Path $payload) { Remove-Item $payload -Recurse -Force }
New-Item -ItemType Directory -Force -Path $payload | Out-Null

foreach ($name in $Stage.Keys) {
    $dir = Join-Path $payload $Stage[$name]
    New-Item -ItemType Directory -Force -Path $dir | Out-Null

    # The MERGED assembly and nothing else. That is the artifact with every dependency folded in and
    # internalized, so a plugin folder holds exactly one managed file and nothing it carries can
    # collide with another plugin's copy of the same library.
    $merged = Join-Path $repo "src\$name\bin\$Configuration\merged\$name.dll"
    if (-not (Test-Path $merged)) {
        throw "No merged build for ${name}: $merged. The merge step only runs in Release."
    }
    Copy-Item $merged (Join-Path $dir "$name.dll") -Force

    # LaunchBox 14 loads NOTHING without a manifest and says nothing about it - measured: a plugin
    # sat in the managed root for a day, never ran and wrote no log line.
    $manifest = Join-Path $repo "src\$name\manifest.json"
    if (-not (Test-Path $manifest)) { throw "No manifest.json for $name" }
    Copy-Item $manifest (Join-Path $dir 'manifest.json') -Force

    Write-Host ("  staged {0,-14} {1,8:N0} KB" -f $Stage[$name], ((Get-Item $merged).Length / 1KB))
}

# The DSi NAND library. REQUIRED here where deploy-dev.ps1 treats it as optional, and the difference
# is deliberate: a developer without it still gets four working plugins, but a release without it is
# a melonDS and a no$gba that cannot touch a DSi NAND, shipped to somebody who cannot tell why.
$nativeDir = Join-Path $payload 'native'
New-Item -ItemType Directory -Force -Path $nativeDir | Out-Null
foreach ($file in @('melonds-nand.dll', 'melonds-nandtool.exe')) {
    $source = Join-Path $repo "build\nand\$file"
    if (-not (Test-Path $source)) {
        throw "The DSi NAND library is missing: $source. Build tools\melonds-nand first - a release without it ships a melonDS that cannot read a NAND."
    }
    Copy-Item $source (Join-Path $nativeDir $file) -Force
    Write-Host ("  staged native\{0,-22} {1,8:N0} KB" -f $file, ((Get-Item $source).Length / 1KB))
}

# ── 3. one file ─────────────────────────────────────────────────────────────

if (Test-Path $release) { Remove-Item $release -Recurse -Force }

Write-Host "Publishing the installer (self-contained, single file)..." -ForegroundColor Cyan
dotnet publish (Join-Path $repo 'src\Installer\Installer.csproj') `
    -c $Configuration -o $release --nologo -v quiet "-p:PayloadDir=$payload"
if ($LASTEXITCODE -ne 0) { throw "publish failed" }

$exe = Join-Path $release 'NixxIntegrations.exe'
if (-not (Test-Path $exe)) { throw "NixxIntegrations.exe missing after publish" }

# Publishing leaves the debug symbols beside it; the release is the one file.
Get-ChildItem $release -File | Where-Object { $_.Name -ne 'NixxIntegrations.exe' } | Remove-Item -Force

$hash = (Get-FileHash $exe -Algorithm SHA256).Hash
Write-Host ""
Write-Host "release -> $exe" -ForegroundColor Green
Write-Host ("            {0:N1} MB" -f ((Get-Item $exe).Length / 1MB))
Write-Host "  sha256   $hash"
