# The save probe into a LaunchBox install: Nixx-SaveProbeMain and Nixx-SaveProbeSecondary (plugins, Local\Plugins) and
# their fake emulators (Emulators\Nixx-SaveProbe<Role>). A test tool - never in the pack. Refuses while LaunchBox runs.
#   .\deploy-probe.ps1                 # -> G:\LB1326
#   .\deploy-probe.ps1 -Remove         # takes it all out again (emulator entries: remove them in LaunchBox)

[CmdletBinding()]
param(
    [string] $LbRoot = 'G:\LB1326',
    [switch] $Remove
)

$ErrorActionPreference = 'Stop'
$here = Split-Path -Parent $MyInvocation.MyCommand.Path

if (Get-Process | Where-Object { $_.Name -in 'LaunchBox', 'BigBox' }) { throw "LaunchBox is running - close it first." }

$roles = @(
    @{ Role = 'Main';      Id = 'a5f0c2e1-7b3d-4c9a-9e61-0d4b8f2a1c01' },
    @{ Role = 'Secondary'; Id = 'a5f0c2e1-7b3d-4c9a-9e61-0d4b8f2a1c02' },
    @{ Role = 'CatchAll';  Id = 'a5f0c2e1-7b3d-4c9a-9e61-0d4b8f2a1c03' }
)

foreach ($r in $roles) {
    $name = "Nixx-SaveProbe$($r.Role)"
    $pluginDir = Join-Path $LbRoot "Local\Plugins\$name"
    $emuDir = Join-Path $LbRoot "Emulators\$name"
    if ($Remove) {
        if (Test-Path $pluginDir) { Remove-Item -LiteralPath $pluginDir -Recurse -Force; "removed $pluginDir" }
        if (Test-Path $emuDir) { Remove-Item -LiteralPath $emuDir -Recurse -Force; "removed $emuDir" }
        continue
    }

    "=== $name"
    $out = & dotnet build (Join-Path $here 'SaveProbe.csproj') -c Release "-p:Role=$($r.Role)" "-p:LbCore=$LbRoot\Core" 2>&1 | ForEach-Object { "$_" }
    if ($LASTEXITCODE -ne 0) { $out | Select-String ' error ' | Select-Object -Unique; throw "plugin build failed ($($r.Role))" }
    if ($r.Role -ne 'CatchAll') { $out = & dotnet build (Join-Path $here 'emu\SaveProbeEmu.csproj') -c Release "-p:Role=$($r.Role)" 2>&1 | ForEach-Object { "$_" } } else { $global:LASTEXITCODE = 0 }
    if ($LASTEXITCODE -ne 0) { $out | Select-String ' error ' | Select-Object -Unique; throw "emulator build failed ($($r.Role))" }

    New-Item -ItemType Directory -Force $pluginDir, $emuDir | Out-Null
    $bin = Join-Path $here "bin\$($r.Role)"
    Copy-Item (Join-Path $bin "SaveProbe$($r.Role).dll") $pluginDir -Force
    if ($r.Role -eq 'Main') { Copy-Item (Join-Path $bin '0Harmony.dll') $pluginDir -Force }
    $manifest = [ordered]@{
        SchemaVersion = 1; PluginId = $r.Id; UserId = 0; Name = $name; Developer = 'nixxou'
        Description = "Test plugin: logs every save call LaunchBox makes ($($r.Role) role). Not for use."
        Credits = ''; OperatingSystemFamily = 'Windows'; InstalledVersion = '0.1'; SourceCodeUrl = ''; SupportUrl = ''; HomePageUrl = ''; TipJarUrl = ''
        SourceKind = 'Local'; Tags = @('5aae585f-ffc5-42bc-9f95-e5e190ef99e2')
    }
    $manifest | ConvertTo-Json | Set-Content -Path (Join-Path $pluginDir 'manifest.json') -Encoding UTF8

    $emuBin = Join-Path $here "emu\bin\$($r.Role)"
    if ($r.Role -ne 'CatchAll') { Get-ChildItem $emuBin -File | Copy-Item -Destination $emuDir -Force }

    foreach ($f in @("SaveProbe$($r.Role).dll")) {
        $a = (Get-FileHash (Join-Path $bin $f)).Hash; $b = (Get-FileHash (Join-Path $pluginDir $f)).Hash
        "  $f " + $(if ($a -eq $b) { 'deployed (hash checked)' } else { 'DIFFERENT - not deployed' })
    }
    if ($r.Role -ne 'CatchAll') { "  emulator -> $emuDir\SaveProbe$($r.Role).exe" } else { "  saves and probe.ini -> $emuDir" }
}
if (-not $Remove) { "log: $env:LOCALAPPDATA\lb-integrations-plugins\saveprobe.log" }
