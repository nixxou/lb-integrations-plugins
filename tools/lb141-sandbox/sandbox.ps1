# The LaunchBox 14.1 measurement sandbox (docs\lb141-plan.md, step 3): a small library in a LaunchBox install, built from
# files of the 14.0 test install, to measure what LaunchBox does with saves - with the pack, or without any third-party
# plugin. LaunchBox must be closed. Every game, version and emulator has a fixed id, so logs and XML read the same each time.
#
#   .\sandbox.ps1 -Action build      # RetroArch (minimal), the games, their saves; Data copied aside first
#   .\sandbox.ps1 -Action pack-off   # the pack's plugins moved to Backups\lbip-pack-off (LaunchBox alone)
#   .\sandbox.ps1 -Action pack-on    # put back
#   .\sandbox.ps1 -Action state      # what the library holds now: save rows, vault, live files with hashes
#   .\sandbox.ps1 -Action reset      # Data, saves and vault back to what build left
#
# The library (Super Nintendo Entertainment System, Nintendo Game Boy):
#   Zelda          RetroArch (snes9x)                      Launch With "RetroArch bsnes"; restore of that save (T1, T2)
#   Mana USA       RetroArch (snes9x) + "Play Version..."  a version on the game's own file (T4 Backup Save, T5 its card)
#   Mana France    RetroArch bsnes   + "(Germany)" own file on RetroArch (snes9x)   which emulator at close (T6), Make Default (T7)
#   Rolan's Curse  RetroArch (gambatte), states in slots 1 and 2                     a state restored into another slot (T3)

[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidateSet('build', 'pack-off', 'pack-on', 'state', 'reset')][string] $Action,
    [string] $Root = 'G:\LB14B3',
    [string] $From = 'G:\LB1326'
)
$ErrorActionPreference = 'Stop'

$Ids = @{
    RaA = '5a7e0141-0000-4000-8000-0000000000a1'; RaB = '5a7e0141-0000-4000-8000-0000000000b1'
    Zelda = '5a7e0141-0000-4000-8000-000000000001'; ManaUs = '5a7e0141-0000-4000-8000-000000000002'
    ManaUsPlay = '5a7e0141-0000-4000-8000-000000000a02'; ManaFr = '5a7e0141-0000-4000-8000-000000000003'
    ManaFrDe = '5a7e0141-0000-4000-8000-000000000a03'; Rolan = '5a7e0141-0000-4000-8000-000000000004'
}
$Snes = 'Super Nintendo Entertainment System'; $Gb = 'Nintendo Game Boy'
$Ra = Join-Path $Root 'Emulators\RetroArch'
$Roms = Join-Path $Root 'Games\lb141-sandbox'
$Kept = Join-Path $Root 'Backups\lbip-sandbox-built'

function Assert-Closed {
    $p = Get-Process LaunchBox, BigBox, LiteBox -ErrorAction SilentlyContinue | Where-Object { $_.Path -like "$Root\*" }
    if ($p) { throw "LaunchBox is running from $Root - close it first." }
}

function Write-Xml([string] $path, [string] $body) {
    [IO.File]::WriteAllText($path, "<?xml version=`"1.0`" standalone=`"yes`"?>`r`n<LaunchBox>`r`n$body</LaunchBox>`r`n", (New-Object Text.UTF8Encoding($false)))
}

function Emulator([string] $id, [string] $title, [string] $snesCore) {
    $x = "  <Emulator><ID>$id</ID><Title>$title</Title><ApplicationPath>Emulators\RetroArch\retroarch.exe</ApplicationPath><CommandLine /><DefaultPlatform />" +
         "<NoQuotes>false</NoQuotes><NoSpace>false</NoSpace><HideConsole>false</HideConsole><FileNameWithoutExtensionAndPath>false</FileNameWithoutExtensionAndPath>" +
         "<AutoExtract>true</AutoExtract><UseStartupScreen>false</UseStartupScreen><MonitorStartupShutdownWithProcess>true</MonitorStartupShutdownWithProcess>" +
         "<UsePauseScreen>false</UsePauseScreen><LoginToCheevoOnGameLaunch>false</LoginToCheevoOnGameLaunch><EnableHardcoreAchievements>false</EnableHardcoreAchievements></Emulator>`r`n"
    $x += "  <EmulatorPlatform><Emulator>$id</Emulator><Platform>$Snes</Platform><CommandLine>-L `"cores\$snesCore`" -f</CommandLine><Default>true</Default><M3uDiscLoadEnabled>false</M3uDiscLoadEnabled><AutoExtract>true</AutoExtract></EmulatorPlatform>`r`n"
    if ($snesCore -eq 'snes9x_libretro.dll') {
        $x += "  <EmulatorPlatform><Emulator>$id</Emulator><Platform>$Gb</Platform><CommandLine>-L `"cores\gambatte_libretro.dll`" -f</CommandLine><Default>true</Default><M3uDiscLoadEnabled>false</M3uDiscLoadEnabled><AutoExtract>true</AutoExtract></EmulatorPlatform>`r`n"
    }
    $x
}

function Game([string] $id, [string] $title, [string] $rom, [string] $emu, [string] $platform) {
    "  <Game><ID>$id</ID><Title>$([Security.SecurityElement]::Escape($title))</Title><ApplicationPath>$([Security.SecurityElement]::Escape($rom))</ApplicationPath><CommandLine />" +
    "<Emulator>$emu</Emulator><Platform>$platform</Platform><DateAdded>2026-10-16T22:30:00+02:00</DateAdded><Status>Imported ROM</Status><Version /><Region />" +
    "<Favorite>false</Favorite><Hide>false</Hide><Broken>false</Broken><Completed>false</Completed><Portable>false</Portable></Game>`r`n"
}

function App([string] $id, [string] $game, [string] $name, [string] $rom, [string] $emu) {
    "  <AdditionalApplication><Id>$id</Id><GameID>$game</GameID><ApplicationPath>$([Security.SecurityElement]::Escape($rom))</ApplicationPath><CommandLine />" +
    "<Name>$([Security.SecurityElement]::Escape($name))</Name><UseDosBox>false</UseDosBox><UseEmulator>true</UseEmulator><WaitForExit>false</WaitForExit>" +
    "<Section>Version</Section><EmulatorId>$emu</EmulatorId><SideA>false</SideA><SideB>false</SideB><Priority>0</Priority><AutoRunAfter>false</AutoRunAfter><AutoRunBefore>false</AutoRunBefore></AdditionalApplication>`r`n"
}

function Platform([string] $name) {
    "  <Platform><Category /><LocalDbParsed>false</LocalDbParsed><Name>$name</Name><Folder /><ScrapeAs /><VideoPath /><HideInBigBox>false</HideInBigBox><DisableAutoImport>true</DisableAutoImport></Platform>`r`n"
}

function Seed([string] $path, [string] $text) {
    New-Item -ItemType Directory -Force (Split-Path $path) | Out-Null
    [IO.File]::WriteAllText($path, $text)
}

function Build {
    Assert-Closed
    $before = Join-Path $Root ('Backups\lbip-sandbox-before-' + (Get-Date -Format 'yyyy-MM-dd_HH-mm-ss'))
    robocopy (Join-Path $Root 'Data') (Join-Path $before 'Data') /E /NFL /NDL /NJH /NJS /NP | Out-Null
    Write-Host "Data copied aside to $before"

    # RetroArch: its own files, the three cores, its settings. No assets (its menu is plain), no BIOS needed here.
    $src = Join-Path $From 'Emulators\RetroArch'
    New-Item -ItemType Directory -Force (Join-Path $Ra 'cores') | Out-Null
    Get-ChildItem $src -File | Where-Object { $_.Extension -in '.exe', '.dll', '.cfg' } | Copy-Item -Destination $Ra -Force
    foreach ($c in 'snes9x_libretro.dll', 'bsnes_libretro.dll', 'gambatte_libretro.dll') { Copy-Item (Join-Path $src "cores\$c") (Join-Path $Ra 'cores') -Force }
    if (Test-Path (Join-Path $src 'info')) { robocopy (Join-Path $src 'info') (Join-Path $Ra 'info') snes9x* bsnes_libretro* gambatte* /NFL /NDL /NJH /NJS /NP | Out-Null }

    # The games: copies, so nothing of the 14.0 install is written.
    New-Item -ItemType Directory -Force $Roms | Out-Null
    foreach ($f in 'Legend of Zelda, The - A Link to the Past (France).sfc', 'Secret of Mana (USA).sfc', 'Secret of Mana (France).sfc', 'Secret of Mana (Germany).sfc') {
        Copy-Item (Join-Path 'G:\LB\FakeCatalog' $f) $Roms -Force
    }
    Copy-Item "G:\LB\FakeCatalog\gbX\Rolan's Curse (USA).zip" $Roms -Force
    $r = { param($n) Join-Path $Roms $n }

    # Emulators.xml: LaunchBox's own "not assigned" row kept, our two added.
    [xml]$emus = Get-Content (Join-Path $Root 'Data\Emulators.xml')
    $keep = ($emus.LaunchBox.ChildNodes | Where-Object { $_.ID -ne $Ids.RaA -and $_.ID -ne $Ids.RaB -and $_.Emulator -ne $Ids.RaA -and $_.Emulator -ne $Ids.RaB } | ForEach-Object { '  ' + $_.OuterXml + "`r`n" }) -join ''
    Write-Xml (Join-Path $Root 'Data\Emulators.xml') ($keep + (Emulator $Ids.RaA 'RetroArch' 'snes9x_libretro.dll') + (Emulator $Ids.RaB 'RetroArch bsnes' 'bsnes_libretro.dll'))

    # Platforms.xml: ours added beside what is there.
    [xml]$plats = Get-Content (Join-Path $Root 'Data\Platforms.xml')
    $keep = ($plats.LaunchBox.ChildNodes | Where-Object { -not ($_.LocalName -eq 'Platform' -and $_.Name -in $Snes, $Gb) } | ForEach-Object { '  ' + $_.OuterXml + "`r`n" }) -join ''
    Write-Xml (Join-Path $Root 'Data\Platforms.xml') ($keep + (Platform $Snes) + (Platform $Gb))

    Write-Xml (Join-Path $Root "Data\Platforms\$Snes.xml") (
        (Game $Ids.Zelda 'The Legend of Zelda: A Link to the Past' (& $r 'Legend of Zelda, The - A Link to the Past (France).sfc') $Ids.RaA $Snes) +
        (Game $Ids.ManaUs 'Secret of Mana' (& $r 'Secret of Mana (USA).sfc') $Ids.RaA $Snes) +
        (App $Ids.ManaUsPlay $Ids.ManaUs 'Play (USA) Version...' (& $r 'Secret of Mana (USA).sfc') $Ids.RaA) +
        (Game $Ids.ManaFr 'Secret of Mana (France)' (& $r 'Secret of Mana (France).sfc') $Ids.RaB $Snes) +
        (App $Ids.ManaFrDe $Ids.ManaFr 'Secret of Mana (Germany)' (& $r 'Secret of Mana (Germany).sfc') $Ids.RaA))
    Write-Xml (Join-Path $Root "Data\Platforms\$Gb.xml") (Game $Ids.Rolan "Rolan's Curse" (& $r "Rolan's Curse (USA).zip") $Ids.RaA $Gb)

    # Saves and states: made-up bytes, each one saying what it is, so a copy is recognised by its content.
    foreach ($d in 'saves', 'states') { if (Test-Path (Join-Path $Ra $d)) { Remove-Item (Join-Path $Ra $d) -Recurse -Force } }
    Seed (Join-Path $Ra 'saves\Snes9x\Legend of Zelda, The - A Link to the Past (France).srm') 'zelda snes9x v1'
    Seed (Join-Path $Ra 'saves\bsnes\Legend of Zelda, The - A Link to the Past (France).srm') 'zelda bsnes v1'
    Seed (Join-Path $Ra 'saves\Snes9x\Secret of Mana (USA).srm') 'mana usa snes9x v1'
    Seed (Join-Path $Ra 'saves\bsnes\Secret of Mana (France).srm') 'mana france bsnes v1'
    Seed (Join-Path $Ra 'saves\Snes9x\Secret of Mana (Germany).srm') 'mana germany snes9x v1'
    Seed (Join-Path $Ra "states\Gambatte\Rolan's Curse (USA).state1") 'rolan state slot 1'
    Seed (Join-Path $Ra "states\Gambatte\Rolan's Curse (USA).state2") 'rolan state slot 2'

    $saves = Join-Path $Root 'Saves'
    foreach ($p in $Snes, $Gb) { if (Test-Path (Join-Path $saves $p)) { Remove-Item (Join-Path $saves $p) -Recurse -Force } }

    if (Test-Path $Kept) { Remove-Item $Kept -Recurse -Force }
    robocopy (Join-Path $Root 'Data') (Join-Path $Kept 'Data') /E /NFL /NDL /NJH /NJS /NP | Out-Null
    robocopy (Join-Path $Ra 'saves') (Join-Path $Kept 'ra-saves') /E /NFL /NDL /NJH /NJS /NP | Out-Null
    robocopy (Join-Path $Ra 'states') (Join-Path $Kept 'ra-states') /E /NFL /NDL /NJH /NJS /NP | Out-Null
    Write-Host "built; kept as $Kept for -Action reset"
}

function Reset {
    Assert-Closed
    if (-not (Test-Path $Kept)) { throw "nothing built yet ($Kept)" }
    robocopy (Join-Path $Kept 'Data') (Join-Path $Root 'Data') /MIR /NFL /NDL /NJH /NJS /NP | Out-Null
    foreach ($d in 'saves', 'states') { robocopy (Join-Path $Kept "ra-$d") (Join-Path $Ra $d) /MIR /NFL /NDL /NJH /NJS /NP | Out-Null }
    foreach ($p in $Snes, $Gb) { $v = Join-Path $Root "Saves\$p"; if (Test-Path $v) { Remove-Item $v -Recurse -Force } }
    Write-Host "reset to the built library"
}

function Pack([bool] $on) {
    Assert-Closed
    $off = Join-Path $Root 'Backups\lbip-pack-off'
    $places = @(@((Join-Path $Root 'Local\Plugins'), 'Nixx-*', 'Local-Plugins'), @((Join-Path $Root 'Plugins'), 'Nixx-Menus', 'Plugins'))
    foreach ($pl in $places) {
        $sub = Join-Path $off $pl[2]
        if ($on) {
            if (Test-Path $sub) { Get-ChildItem $sub -Directory | ForEach-Object { Move-Item $_.FullName $pl[0] } }
        } else {
            New-Item -ItemType Directory -Force $sub | Out-Null
            Get-ChildItem $pl[0] -Directory -Filter $pl[1] -ErrorAction SilentlyContinue | ForEach-Object { Move-Item $_.FullName $sub }
        }
    }
    $left = @(Get-ChildItem (Join-Path $Root 'Local\Plugins') -Directory -Filter 'Nixx-*' -EA 0) + @(Get-ChildItem (Join-Path $Root 'Plugins') -Directory -Filter 'Nixx-*' -EA 0)
    Write-Host ("pack " + ($(if ($on) { 'on' } else { 'off' })) + ": " + $left.Count + " Nixx folder(s) in place")
}

function State {
    foreach ($p in $Snes, $Gb) {
        $f = Join-Path $Root "Data\Platforms\$p.xml"
        if (-not (Test-Path $f)) { continue }
        [xml]$x = Get-Content $f
        $titles = @{}; foreach ($g in $x.LaunchBox.Game) { $titles[$g.ID] = $g.Title + ' | ' + $g.ApplicationPath + ' | emu ' + $g.Emulator }
        $apps = @{}; foreach ($a in $x.LaunchBox.AdditionalApplication) { $apps[$a.Id] = $a.Name }
        Write-Host "== $p"
        foreach ($g in $x.LaunchBox.Game) { Write-Host ("  game " + $titles[$g.ID]) }
        foreach ($a in $x.LaunchBox.AdditionalApplication) { Write-Host ("  app  " + $a.Name + ' | ' + $a.ApplicationPath + ' | emu ' + $a.EmulatorId + ' | of ' + $a.GameID) }
        foreach ($s in $x.LaunchBox.GameSave) {
            $on = if ($s.AdditionalApplicationId) { 'app ' + $apps[$s.AdditionalApplicationId] } else { 'game' }
            Write-Host ("  save [" + $on + "] " + $s.FilePath + ' | ' + $s.EmulatorFileName + '/' + $s.EmulatorCore + ' | group ' + $s.SaveGroupName + ' | id ' + $s.SaveGroupId + ' | orig ' + $s.OriginalFileName + $(if ($s.Slot) { ' | slot ' + $s.Slot }))
        }
    }
    Write-Host "== files"
    foreach ($d in (Join-Path $Ra 'saves'), (Join-Path $Ra 'states'), (Join-Path $Root "Saves")) {
        if (Test-Path $d) { Get-ChildItem $d -Recurse -File | ForEach-Object { Write-Host ("  " + $_.FullName.Substring($Root.Length + 1) + ' = "' + ([IO.File]::ReadAllText($_.FullName)) + '" ' + $_.LastWriteTime.ToString('HH:mm:ss')) } }
    }
}

switch ($Action) {
    'build' { Build }
    'pack-off' { Pack $false }
    'pack-on' { Pack $true }
    'state' { State }
    'reset' { Reset }
}
