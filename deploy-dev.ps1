# Build a plugin and drop it into a LaunchBox / LiteBox install.
#
# The deployed file is verified BY HASH, not by "Copy-Item didn't throw": a copy onto a file held
# open by a running host silently leaves the old bytes in place, and a deploy script that reports
# success while shipping a stale DLL costs an hour of debugging the wrong thing.
#
# It never stops a running process. If the target is locked, it says so and leaves it alone.
#
#   .\deploy-dev.ps1                       # Ppsspp -> G:\LB1326
#   .\deploy-dev.ps1 -All                  # all seven, same root
#   .\deploy-dev.ps1 -LbRoot 'G:\LB'       # somewhere else
#   .\deploy-dev.ps1 -Configuration Debug

[CmdletBinding()]
param(
    # Which plugin to build; the folder name under src\.
    [string] $Plugin = 'Ppsspp',

    # Every plugin, one after the other, into the same root.
    [switch] $All,

    # The LaunchBox install to deploy into. G:\LB1326 is the LaunchBox 14 test install.
    [string] $LbRoot = 'G:\LB1326',

    # Folder name under <LbRoot>\Local\Plugins. Left empty it comes from the table below, which is
    # the same one NixxIntegrations.exe uses - the script and the installer must put a plugin in the
    # SAME folder or a user ends up running two copies.
    [string] $FolderName,

    [ValidateSet('Debug', 'Release')]
    [string] $Configuration = 'Release',

    # For SuperZsnes only: a SUPER ZSNES folder launched once with BepInEx in it, so that
    # tools\superzsnes-bepinex can be built and embedded. Optional here (build-release.ps1 requires
    # it): without it the plugin deploys BepInEx and the docs and carries no in-process plugin.
    [string] $SuperZsnesDir = $env:SUPERZSNES_DIR
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $MyInvocation.MyCommand.Path

# THE NAMING TABLE. One row per plugin: the folder this pack installs into, and every folder name an
# earlier version of this script or of the plugin ever used. The old ones are swept, because
# PluginLoader dedupes by FILE NAME across plugin folders - a stale "melonDS Integration" beside
# "Nixx-melonDS" means two copies of MelonDs.dll and no way to say which one wins.
#
# Keep this in step with src\Installer\Payload.cs. They are the two places a folder name is decided.
$Pack = @{
    'Cxbx'    = @{ Folder = 'Nixx-Cxbx';    Old = @() }
    'Flycast' = @{ Folder = 'Nixx-Flycast'; Old = @('Flycast Integration') }
    'MelonDs' = @{ Folder = 'Nixx-melonDS'; Old = @('MelonDs Integration', 'melonDS Integration') }
    'NoGba'   = @{ Folder = 'Nixx-nogba';   Old = @('NoGba Integration', 'no$gba Integration', 'Nixx-no$gba') }
    'Ppsspp'  = @{ Folder = 'Nixx-PPSSPP';  Old = @('Ppsspp Integration', 'PPSSPP Integration') }
    'Xemu'    = @{ Folder = 'Nixx-Xemu';    Old = @() }
    'Xenia'   = @{ Folder = 'Nixx-Xenia';   Old = @('Xenia Integration') }
    'Vita3k'  = @{ Folder = 'Nixx-Vita3K';  Old = @() }
    'SuperZsnes' = @{ Folder = 'Nixx-SuperZSNES'; Old = @() }
}

if ($All) {
    foreach ($name in @('Cxbx', 'Flycast', 'MelonDs', 'NoGba', 'Ppsspp', 'SuperZsnes', 'Vita3k', 'Xemu', 'Xenia')) {
        Write-Host ""
        Write-Host ("=== " + $name) -ForegroundColor Magenta
        & $MyInvocation.MyCommand.Path -Plugin $name -LbRoot $LbRoot -Configuration $Configuration -SuperZsnesDir $SuperZsnesDir
    }
    return
}

if (-not $Pack.ContainsKey($Plugin)) { throw "Unknown plugin: $Plugin" }
if (-not $FolderName) { $FolderName = $Pack[$Plugin].Folder }

$projectDir = Join-Path $repo "src\$Plugin"
if (-not (Test-Path $projectDir)) { throw "No such plugin: $projectDir" }
if (-not (Test-Path $LbRoot)) { throw "No such LaunchBox root: $LbRoot" }

# The SUPER ZSNES in-process plugin first, when a game folder with interop is at hand: SuperZsnes.csproj
# embeds build\bepinex\SuperZsnes.BepInEx.dll when it is there. See build-release.ps1, step 0.
if ($Plugin -eq 'SuperZsnes') {
    if ($SuperZsnesDir -and (Test-Path (Join-Path $SuperZsnesDir 'BepInEx\interop\Assembly-CSharp.dll'))) {
        Write-Host "Building the SUPER ZSNES in-process plugin..." -ForegroundColor Cyan
        $modOut = Join-Path $repo 'build\bepinex'
        New-Item -ItemType Directory -Force -Path $modOut | Out-Null
        dotnet build (Join-Path $repo 'tools\superzsnes-bepinex\SuperZsnes.BepInEx.csproj') -c $Configuration --nologo -v quiet "-p:SuperZsnesDir=$SuperZsnesDir" "-p:OutDir=$modOut\"
        if ($LASTEXITCODE -ne 0) { throw "Build failed: SuperZsnes.BepInEx" }
    } else {
        Write-Host "  ! no -SuperZsnesDir with BepInEx\interop - the in-process plugin is not built; SuperZsnes.dll will deploy BepInEx and the docs only" -ForegroundColor Yellow
    }
}

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

# ANY OTHER COPY OF THIS PLUGIN IS A SECOND COPY. PluginLoader walks both roots and dedupes by file
# name, so whichever it reaches first wins - and after a rename that is as likely to be the stale one
# as the fresh one. The folders this pack used to use are removed outright when they hold our DLL
# (never on the name alone: a folder somebody else made is not ours to delete), and anything left
# that still holds one is reported rather than touched.
foreach ($root in @("Local\Plugins", "Plugins")) {
    foreach ($old in $Pack[$Plugin].Old) {
        $dir = Join-Path $LbRoot "$root\$old"
        if (-not (Test-Path (Join-Path $dir "$Plugin.dll"))) { continue }
        Remove-Item $dir -Recurse -Force
        Write-Host "  removed $dir - it held an older $Plugin.dll" -ForegroundColor Yellow
    }
}

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

# THE CATALOGUE CONTRACT goes beside the plugin, always. It is the one managed file in the folder
# that is not the plugin itself, and it has to be: a host and a plugin must mean the SAME interface
# type, and type identity in .NET is per-assembly - merged and internalized it would become a
# private type of the plugin that no host could name. See src\Catalog\LbCatalog.cs.
$contract = Join-Path $projectDir "bin\$Configuration\LbIntegrations.Catalog.dll"
if (Test-Path $contract) {
    $contractTarget = Join-Path $targetDir 'LbIntegrations.Catalog.dll'
    Copy-Item $contract $contractTarget -Force
    if ((Get-FileHash $contract -Algorithm SHA256).Hash -ne
        (Get-FileHash $contractTarget -Algorithm SHA256).Hash) {
        throw "The catalogue contract was not written: $contractTarget."
    }
    Write-Host "           $contractTarget"

    # AND IT HAS TO BE THE SAME BYTES IN EVERY PLUGIN FOLDER. Measured 26/09: deploying ONE plugin
    # rebuilds this contract - a fresh MVID alone changes the file - and leaves the other five
    # carrying the previous build. Same assembly identity, different bytes, and LaunchBox opens the
    # next start with
    #     LOCAL EXCEPTION: Could not load file or assembly 'LbIntegrations.Catalog' ...
    #     Assembly with same name is already loaded
    # The plugins all load anyway, so nothing on screen says the deploy was wrong - which is the very
    # kind of quiet drift this script exists to refuse. The siblings are brought back into line here
    # rather than reported and left broken; a locked one is named, because that one needs the host
    # closed and cannot be fixed from here.
    $contractHash = (Get-FileHash $contract -Algorithm SHA256).Hash
    foreach ($key in $Pack.Keys) {
        $otherFolder = $Pack[$key].Folder
        if ($otherFolder -eq $FolderName) { continue }
        $sibling = Join-Path $LbRoot "Local\Plugins\$otherFolder\LbIntegrations.Catalog.dll"
        if (-not (Test-Path $sibling)) { continue }   # that plugin is not deployed into this root
        if ((Get-FileHash $sibling -Algorithm SHA256).Hash -eq $contractHash) { continue }
        try {
            Copy-Item $contract $sibling -Force -ErrorAction Stop
            Write-Host "  realigned $otherFolder - it carried an older catalogue contract" -ForegroundColor Yellow
        } catch {
            Write-Host "  ! $otherFolder still carries an older catalogue contract and is locked. Close the host and deploy again, or LaunchBox will throw at start-up." -ForegroundColor Red
        }
    }
}

# A native companion, when the plugin has one and this checkout has built it. Only melonDS does
# today: reading and writing a DSi NAND is melonDS's own code, so the plugin calls a small GPL
# library rather than reimplementing the format. Its absence is not an error - DSiWare then falls
# back to a click in Manage DSi titles, and everything else works - so this copies it when it is
# there and says nothing when it is not.
# NOT beside the plugin, and that is not tidiness. LaunchBox loads every .dll in a plugin folder as
# a .NET assembly: a native one there produces "System.BadImageFormatException: Bad IL format ...
# failed to load during PluginLoader.LoadAssembly" and an error dialog at every start. Measured, with
# the dialog. So the library goes into native\ and loses the .dll extension; the plugin loads it by
# path through a DllImport resolver.
# TWO PLUGINS NEED IT NOW. The DSi engine lives in src\Shared.Dsi and is compiled into both
# melonDS and no$gba; it P/Invokes this library, and the resolver only ever looks inside the
# plugin's OWN folder - so each one gets its own copy. That is also why both projects are
# GPL-3.0: see THIRD-PARTY.md.
if ($Plugin -eq 'MelonDs' -or $Plugin -eq 'NoGba') {
    $nativeDir = Join-Path $targetDir "native"
    $pairs = @(
        @{ From = "melonds-nand.dll";     To = "melonds-nand.native" },
        @{ From = "melonds-nandtool.exe"; To = "melonds-nandtool.exe" }
    )
    foreach ($pair in $pairs) {
        $companion = Join-Path $repo "build\nand\$($pair.From)"
        if (-not (Test-Path $companion)) { continue }
        New-Item -ItemType Directory -Force -Path $nativeDir | Out-Null
        $companionTarget = Join-Path $nativeDir $pair.To
        Copy-Item $companion $companionTarget -Force
        if ((Get-FileHash $companion -Algorithm SHA256).Hash -ne
            (Get-FileHash $companionTarget -Algorithm SHA256).Hash) {
            throw "The NAND library was not written: $companionTarget."
        }
        Write-Host "           $companionTarget"
    }

    # An older deploy put them in the folder LaunchBox scans. Left there, the error comes back.
    foreach ($stale in @("melonds-nand.dll", "melonds-nandtool.exe")) {
        $old = Join-Path $targetDir $stale
        if (Test-Path $old) {
            Remove-Item $old -Force
            Write-Host "  removed $old - LaunchBox would try to load it as a plugin" -ForegroundColor Yellow
        }
    }
}

# THE VITA3K INSTALL LIBRARY: Vita3K's own PFS decryption and firmware install, built from
# tools\vita3k-install. The plugin loads it into its process, so - like the NAND library - it goes into
# native\ and loses its .dll extension: LaunchBox would try to load a .dll there as an assembly.
# Optional here, as the NAND library is: without it the plugin installs homebrew, refuses a PFS dump
# with a sentence saying why, and falls back to the emulator for the firmware. build-release.ps1
# requires it.
# FLYCAST'S GAME-ID TOOL: Flycast's own disc and cartridge code, the id a game's per-game config is named by -
# its own repository, github.com/nixxou/flycast-id, checked out beside this one and built into build\flycast-id:
#   cmake -S ..\flycast-id -B build\flycast-id -G Ninja -DCMAKE_BUILD_TYPE=Release -DFLYCAST_SOURCE_DIR=../flycast
#   cmake --build build\flycast-id
# (FLYCAST_SOURCE_DIR relative to ..\flycast-id: the Flycast checkout beside it; left out, its submodule.) A PROCESS the plugin runs, never loads - an .exe, so it keeps its name in
# native\; the plugin puts it beside the emulator itself. Optional here: without it an arcade game's id is
# learned from Flycast's log. build-release.ps1 requires it.
if ($Plugin -eq 'Flycast') {
    $fid = Join-Path $repo "build\flycast-id\flycast-id.exe"
    $nativeDir = Join-Path $targetDir "native"
    if (Test-Path $fid) {
        New-Item -ItemType Directory -Force -Path $nativeDir | Out-Null
        $fidTarget = Join-Path $nativeDir "flycast-id.exe"
        Copy-Item $fid $fidTarget -Force
        if ((Get-FileHash $fid -Algorithm SHA256).Hash -ne (Get-FileHash $fidTarget -Algorithm SHA256).Hash) {
            throw "Flycast's game-id tool was not written: $fidTarget."
        }
        Write-Host "           $fidTarget"
    } else {
        Write-Host "  ! no build\flycast-id\flycast-id.exe - build ..\flycast-id (see above), or arcade ids are learned from Flycast's log" -ForegroundColor Yellow
    }
}

if ($Plugin -eq 'Vita3k') {
    $v3k = Join-Path $repo "build\vita3k\vita3k-install.dll"
    $nativeDir = Join-Path $targetDir "native"
    if (Test-Path $v3k) {
        New-Item -ItemType Directory -Force -Path $nativeDir | Out-Null
        $v3kTarget = Join-Path $nativeDir "vita3k-install.native"
        Copy-Item $v3k $v3kTarget -Force
        if ((Get-FileHash $v3k -Algorithm SHA256).Hash -ne (Get-FileHash $v3kTarget -Algorithm SHA256).Hash) {
            throw "The Vita3K install library was not written: $v3kTarget."
        }
        Write-Host "           $v3kTarget"
    } else {
        Write-Host "  ! no build\vita3k\vita3k-install.dll - build tools\vita3k-install, or PFS dumps will be refused" -ForegroundColor Yellow
    }

    # What earlier builds of this script put there: the out-of-process tool the library replaced.
    foreach ($stale in @("vita3k-install.exe", "vita3k-pfs.exe")) {
        $old = Join-Path $nativeDir $stale
        if (Test-Path $old) {
            Remove-Item $old -Force
            Write-Host "  removed $old - the plugin loads the library now" -ForegroundColor Yellow
        }
    }
}

# THE MENU RELAY goes with every plugin: the pack's right-click entries are shown by it, from the
# CLASSIC root - a plugin in Local\Plugins never has its own menu asked on LaunchBox 14. See
# src\Installer\Payload.cs (Menus).
Write-Host "Building the menu relay..." -ForegroundColor Cyan
dotnet build (Join-Path $repo 'src\Menus\Menus.csproj') -c $Configuration --nologo -v quiet
if ($LASTEXITCODE -ne 0) { throw "Build failed: Menus" }
$relay = Join-Path $repo "src\Menus\bin\$Configuration\NixxMenus.dll"
$relayDir = Join-Path $LbRoot 'Plugins\Nixx-Menus'
New-Item -ItemType Directory -Force -Path $relayDir | Out-Null
$relayTarget = Join-Path $relayDir 'NixxMenus.dll'
try { Copy-Item $relay $relayTarget -Force } catch { throw "Could not write $relayTarget -- held open by a running host? $_" }
if ((Get-FileHash $relay -Algorithm SHA256).Hash -ne (Get-FileHash $relayTarget -Algorithm SHA256).Hash) {
    throw "The menu relay was not written: $relayTarget still holds different bytes."
}
Write-Host "           $relayTarget"

Write-Host "Deployed -> $target" -ForegroundColor Green
Write-Host "           $manifestTarget"
Write-Host "  sha256 $($targetHash.Substring(0,16))...  $((Get-Item $target).Length) bytes"
Write-Host ""
Write-Host "Plugins are loaded once at start-up. Restart the host, then look for:" -ForegroundColor Cyan
Write-Host "  [loader] + LbIntegrations.$Plugin.$($Plugin)Plugin  (emulator)  [$Plugin.dll]"
Write-Host "  [emuplugin] `"<your emulator>`" handled by $($Plugin)Plugin"
Write-Host "If the plugin does not appear, tick it in Options > Plugins (LiteBox keeps the list in LiteBox.ini)."
