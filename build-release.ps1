# Build the whole pack into ONE file: release\NixxIntegrations.exe.
#
# Three steps, in this order and for a reason. The plugins are built first, because the release
# is made of their merged DLLs and nothing else. Their files are then staged into one directory -
# this is the only place that decides what a release contains, so the installer project never has to
# reach into a sibling's bin\ folder. And last the installer is published self-contained, with that
# directory handed to it as a property.
#
#   .\build-release.ps1              # the lot
#   .\build-release.ps1 -SkipPlugins # restage and republish from what is already built
#
# The result needs nothing installed on the target machine: it carries the .NET runtime, the six
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
# installer creates.
$Stage = [ordered]@{
    'Flycast' = 'Nixx-Flycast'
    'MelonDs' = 'Nixx-melonDS'
    'NoGba'   = 'Nixx-nogba'
    'Ppsspp'  = 'Nixx-PPSSPP'
    'Vita3k'  = 'Nixx-Vita3K'
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

# THE MENU RELAY: the pack's right-click entries, a classic plugin for Plugins\ - see
# src\Installer\Payload.cs (Menus). One bare DLL, no manifest, nothing merged: it references only
# the SDK, which the host provides.
Write-Host "Building the menu relay..." -ForegroundColor Cyan
dotnet build (Join-Path $repo 'src\Menus\Menus.csproj') -c $Configuration --nologo -v quiet
if ($LASTEXITCODE -ne 0) { throw "Build failed: Menus" }
$relay = Join-Path $repo "src\Menus\bin\$Configuration\NixxMenus.dll"
if (-not (Test-Path $relay)) { throw "The menu relay is missing: $relay" }
$relayDir = Join-Path $payload 'Nixx-Menus'
New-Item -ItemType Directory -Force -Path $relayDir | Out-Null
Copy-Item $relay (Join-Path $relayDir 'NixxMenus.dll') -Force
Write-Host ("  staged {0,-14} {1,8:N0} KB" -f 'Nixx-Menus', ((Get-Item $relay).Length / 1KB))

# The catalogue contract, staged ONCE and installed into every plugin folder. It is the one managed
# file beside a plugin that is not the plugin: a host and a plugin must mean the same interface
# type, and merging it in would make one private type per plugin, none of them nameable by a
# host. Taken from a plugin's
# own bin\ rather than built separately, so the copy that ships is the copy they were compiled
# against.
$contract = Join-Path $repo "src\Flycast\bin\$Configuration\LbIntegrations.Catalog.dll"
if (-not (Test-Path $contract)) { throw "The catalogue contract is missing: $contract" }
Copy-Item $contract (Join-Path $payload 'LbIntegrations.Catalog.dll') -Force
Write-Host ("  staged {0,-14} {1,8:N0} KB" -f 'the contract', ((Get-Item $contract).Length / 1KB))

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

# The Vita3K install library. REQUIRED, for the same reason as the NAND library: a release without
# it is a Vita3K that installs a NoNpDRM dump which then never boots, with nothing to tell the user
# why. Built from tools\vita3k-install against a Vita3K checkout; staged under its .dll name like the
# NAND library, and renamed .native on the way into the plugin folder.
$v3k = Join-Path $repo "build\vita3k\vita3k-install.dll"
if (-not (Test-Path $v3k)) {
    throw "The Vita3K install library is missing: $v3k. Build tools\vita3k-install first - a release without it ships a Vita3K whose NoNpDRM games do not boot."
}
Copy-Item $v3k (Join-Path $nativeDir 'vita3k-install.dll') -Force
Write-Host ("  staged native\{0,-22} {1,8:N0} KB" -f 'vita3k-install.dll', ((Get-Item $v3k).Length / 1KB))

# The RAM disk helper. Built here rather than taken from a checkout, because its source is in this
# repository (tools\ramdisk-helper) - unlike the NAND library, which needs a melonDS checkout.
#
# It lands in the SAME folder LiteBox uses, so the two share one helper and one elevated task. The
# installer writes it only when it is absent, so whichever of the two arrives first owns the file.

Write-Host "Building the RAM disk helper..." -ForegroundColor Cyan
dotnet build (Join-Path $repo 'tools\ramdisk-helper\RamDiskHelper.csproj') -c $Configuration --nologo -v quiet
if ($LASTEXITCODE -ne 0) { throw "Build failed: RamDiskHelper" }

$ramDir = Join-Path $payload 'ramdisk'
New-Item -ItemType Directory -Force -Path $ramDir | Out-Null
foreach ($file in @('RamDiskHelper.exe', 'RamDiskHelper.dll',
                    'RamDiskHelper.deps.json', 'RamDiskHelper.runtimeconfig.json')) {
    $source = Join-Path $repo "tools\ramdisk-helper\bin\$Configuration\$file"
    if (-not (Test-Path $source)) { throw "The RAM disk helper is missing: $source" }
    Copy-Item $source (Join-Path $ramDir $file) -Force
    Write-Host ("  staged ramdisk\{0,-24} {1,8:N0} KB" -f $file, ((Get-Item $source).Length / 1KB))
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
