# Build a plugin and drop it into a LaunchBox / LiteBox install.
#
# The deployed file is verified BY HASH, not by "Copy-Item didn't throw": a copy onto a file held
# open by a running host silently leaves the old bytes in place, and a deploy script that reports
# success while shipping a stale DLL costs an hour of debugging the wrong thing.
#
# It never stops a running process. If the target is locked, it says so and leaves it alone.
#
#   .\deploy-dev.ps1                       # Ppsspp -> G:\LB1326
#   .\deploy-dev.ps1 -LbRoot 'G:\LB'       # somewhere else
#   .\deploy-dev.ps1 -Configuration Debug

[CmdletBinding()]
param(
    # Which plugin to build; the folder name under src\.
    [string] $Plugin = 'Ppsspp',

    # The LaunchBox install to deploy into. G:\LB1326 is the LaunchBox 14 test install.
    [string] $LbRoot = 'G:\LB1326',

    # Folder name under <LbRoot>\Local\Plugins. Deliberately NOT "<name> LaunchBox Integration": that
    # wording makes LiteBox treat the plugin as LaunchBox-owned and enable it implicitly, and it
    # impersonates Unbroken's naming next to their real plugins.
    [string] $FolderName,

    [ValidateSet('Debug', 'Release')]
    [string] $Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $MyInvocation.MyCommand.Path

if (-not $FolderName) { $FolderName = "$Plugin Integration" }

$projectDir = Join-Path $repo "src\$Plugin"
if (-not (Test-Path $projectDir)) { throw "No such plugin: $projectDir" }
if (-not (Test-Path $LbRoot)) { throw "No such LaunchBox root: $LbRoot" }

Write-Host "Building $Plugin ($Configuration)..." -ForegroundColor Cyan
dotnet build (Join-Path $projectDir "$Plugin.csproj") -c $Configuration --nologo
if ($LASTEXITCODE -ne 0) { throw "Build failed." }

# Ship the MERGED assembly when the build produced one. That is the artifact with every dependency
# folded in and internalized, so the plugin folder holds exactly one file and nothing it carries can
# collide with another plugin's copy of the same library.
$merged = Join-Path $projectDir "bin\$Configuration\merged\$Plugin.dll"
$plain  = Join-Path $projectDir "bin\$Configuration\$Plugin.dll"
if (Test-Path $merged) {
    $source = $merged
    Write-Host "  using the merged build (dependencies folded in)"
} elseif (Test-Path $plain) {
    $source = $plain
    Write-Host "  ! using the UNMERGED build - dependencies are not folded in" -ForegroundColor Yellow
} else {
    throw "Build produced no $Plugin.dll under $projectDirin\$Configuration"
}

# Local\Plugins, which is the root LaunchBox 14 manages, and the manifest goes WITH the DLL.
#
# The legacy Plugins\ root takes a bare DLL and is where this script used to put one. LaunchBox 14
# then loads nothing at all and says nothing about it - measured: a Xenia plugin sat there for a day
# with no manifest, never ran, wrote no log line, and simply had no install option in the Add
# Emulator window. A manifest is not optional in the managed root, and its SourceKind must match the
# root it sits in or the core refuses it.
$targetDir = Join-Path $LbRoot "Local\Plugins\$FolderName"
$target = Join-Path $targetDir "$Plugin.dll"
$manifestSource = Join-Path $projectDir "manifest.json"
$manifestTarget = Join-Path $targetDir "manifest.json"
if (-not (Test-Path $manifestSource)) {
    throw "No manifest.json beside $Plugin.csproj. LaunchBox 14 will not load a plugin without one."
}
New-Item -ItemType Directory -Force -Path $targetDir | Out-Null

# A copy left behind in the legacy root would load a SECOND, older copy of the same plugin.
$legacy = Join-Path $LbRoot "Plugins\$FolderName"
if (Test-Path (Join-Path $legacy "$Plugin.dll")) {
    Write-Host "  ! $legacy still holds a $Plugin.dll - remove it, or two copies will load." -ForegroundColor Yellow
}

# Warn, don't act. Killing a host the user is testing with looks exactly like a crash.
$hosts = Get-Process -ErrorAction SilentlyContinue |
         Where-Object { $_.ProcessName -in @('LiteBox', 'LaunchBox', 'BigBox') }
if ($hosts) {
    Write-Host ("  ! running: " + (($hosts | ForEach-Object ProcessName) -join ', ') +
                " -- the copy will fail if it holds the DLL open. Close it, or deploy anyway and see.") -ForegroundColor Yellow
}

$sourceHash = (Get-FileHash $source -Algorithm SHA256).Hash
try {
    Copy-Item $source $target -Force
} catch {
    throw "Could not write $target -- it is probably held open by a running host. $_"
}

$targetHash = (Get-FileHash $target -Algorithm SHA256).Hash
if ($sourceHash -ne $targetHash) {
    throw "Deployed file does not match the build: $target still holds different bytes. Nothing was updated."
}

Copy-Item $manifestSource $manifestTarget -Force
if ((Get-FileHash $manifestSource -Algorithm SHA256).Hash -ne
    (Get-FileHash $manifestTarget -Algorithm SHA256).Hash) {
    throw "The manifest was not written: $manifestTarget. The plugin would not load."
}

Write-Host "Deployed -> $target" -ForegroundColor Green
Write-Host "           $manifestTarget"
Write-Host "  sha256 $($targetHash.Substring(0,16))...  $((Get-Item $target).Length) bytes"
Write-Host ""
Write-Host "Plugins are loaded once at start-up. Restart the host, then look for:" -ForegroundColor Cyan
Write-Host "  [loader] + LbIntegrations.$Plugin.$($Plugin)Plugin  (emulator)  [$Plugin.dll]"
Write-Host "  [emuplugin] `"<your emulator>`" handled by $($Plugin)Plugin"
Write-Host "If the plugin does not appear, tick it in Options > Plugins (LiteBox keeps the list in LiteBox.ini)."
