# Calibrate the SUPER ZSNES RetroAchievements token cipher against this machine.
#
# What the build does (read off StringEnc, 0.310, x86):
#   key   = SHA256( UTF8( "VWSZaVVTXSC" + SystemInfo.deviceUniqueIdentifier + "YYZSUJA" ) )
#   raencT = IV(16) || AES-256-CBC-PKCS7(key, IV, UTF8(token))        (GetEPW; GetNPW is the inverse)
#
# The one thing not readable from the build is deviceUniqueIdentifier: Unity's Windows player hashes
# WMI strings (its UnityPlayer.dll carries Win32_BaseBoard, Win32_BIOS, Win32_OperatingSystem,
# Win32_ComputerSystem, SerialNumber, Manufacturer, Model, DeviceId), and the exact recipe is not
# documented beyond "a hash of BaseBoard, BIOS and OS serial numbers". So this script takes the
# encrypted token out of szsnes_ui.data and tries every plausible recipe until one decrypts to
# printable text. Run it AFTER logging in once inside the emulator (Config > Retroachievements).
param(
    [string] $Settings = (Join-Path $env:USERPROFILE 'AppData\LocalLow\ZEMU Software Inc_\SUPERZSNES\szsnes_ui.data'),
    [int] $MaxFields = 4
)
$ErrorActionPreference = 'Stop'

# ---- raencT out of the NRBF: an ArraySinglePrimitive record (0x0F id len type=2 bytes) ----------
$b = [System.IO.File]::ReadAllBytes($Settings)
$arrays = @()
for ($i = 0; $i -lt $b.Length - 10; $i++) {
    if ($b[$i] -ne 0x0F) { continue }
    $len = [BitConverter]::ToInt32($b, $i + 5)
    if ($b[$i + 9] -ne 2) { continue }              # PrimitiveTypeEnum.Byte
    if ($len -lt 32 -or $len -gt 4096 -or ($len % 16) -ne 0 -or $i + 10 + $len -gt $b.Length) { continue }
    $arrays += [pscustomobject]@{ Offset = $i; Id = [BitConverter]::ToInt32($b, $i + 1); Length = $len; Bytes = $b[($i + 10)..($i + 9 + $len)] }
}
if ($arrays.Count -eq 0) { Write-Host "no byte[] of a plausible size in $Settings - log in inside the emulator first"; exit 2 }
foreach ($a in $arrays) { Write-Host ("candidate raencT @0x{0:X} id={1} {2} bytes" -f $a.Offset, $a.Id, $a.Length) }

# ---- the WMI strings Unity may hash ----------------------------------------------------------
function W($class, $prop) { try { "$((Get-CimInstance -ClassName $class -ErrorAction Stop | Select-Object -First 1).$prop)" } catch { '' } }
$fields = [ordered]@{
    'BaseBoard.SerialNumber' = W Win32_BaseBoard SerialNumber
    'BIOS.SerialNumber'      = W Win32_BIOS SerialNumber
    'OS.SerialNumber'        = W Win32_OperatingSystem SerialNumber
    'BaseBoard.Manufacturer' = W Win32_BaseBoard Manufacturer
    'BIOS.Manufacturer'      = W Win32_BIOS Manufacturer
    'CS.Manufacturer'        = W Win32_ComputerSystem Manufacturer
    'CS.Model'               = W Win32_ComputerSystem Model
    'Processor.DeviceId'     = W Win32_Processor DeviceId
    'Processor.ProcessorId'  = W Win32_Processor ProcessorId
    'CSProduct.UUID'         = W Win32_ComputerSystemProduct UUID
}
Write-Host ("fields: " + (($fields.Keys | ForEach-Object { "{0}[{1}]" -f $_, $fields[$_].Length }) -join ' '))

# ---- ordered subsets of up to $MaxFields fields, joined with nothing / space / newline -----------
$keys = @($fields.Keys)
function Orderings([string[]] $pool, [int] $k) {
    if ($k -eq 0) { return ,@() }
    $out = @()
    foreach ($p in $pool) {
        $rest = $pool | Where-Object { $_ -ne $p }
        foreach ($tail in (Orderings $rest ($k - 1))) { $out += ,(@($p) + $tail) }
    }
    return $out
}
$combos = @()
for ($k = 1; $k -le $MaxFields; $k++) { $combos += Orderings $keys $k }
Write-Host "$($combos.Count) field orderings"

$sha1 = [System.Security.Cryptography.SHA1]::Create(); $md5 = [System.Security.Cryptography.MD5]::Create(); $sha256 = [System.Security.Cryptography.SHA256]::Create()
function Hex($bytes) { ([BitConverter]::ToString($bytes) -replace '-', '') }
function TryKey([string] $deviceId, [string] $label) {
    $material = [System.Text.Encoding]::UTF8.GetBytes("VWSZaVVTXSC" + $deviceId + "YYZSUJA")
    $key = $sha256.ComputeHash($material)
    foreach ($a in $script:arrays) {
        try {
            $aes = New-Object System.Security.Cryptography.AesManaged
            $aes.Key = $key; $aes.IV = $a.Bytes[0..15]; $aes.Mode = 'CBC'; $aes.Padding = 'PKCS7'
            $plain = $aes.CreateDecryptor().TransformFinalBlock($a.Bytes, 16, $a.Length - 16)
            $text = [System.Text.Encoding]::UTF8.GetString($plain)
            if ($text -match '^[\x20-\x7E]{4,}$') {
                Write-Host ""
                Write-Host "MATCH  deviceUniqueIdentifier recipe: $label"
                Write-Host "       deviceUniqueIdentifier = $deviceId"
                Write-Host ("       token = {0}... ({1} chars)" -f $text.Substring(0, [Math]::Min(6, $text.Length)), $text.Length)
                return $true
            }
        } catch { }
    }
    return $false
}

$tried = 0
foreach ($combo in $combos) {
    foreach ($sep in @('', ' ', "`n")) {
        $concat = ($combo | ForEach-Object { $fields[$_] }) -join $sep
        $bytes = [System.Text.Encoding]::UTF8.GetBytes($concat)
        foreach ($h in @(@('sha1', $sha1), @('md5', $md5), @('sha256', $sha256))) {
            $hex = Hex $h[1].ComputeHash($bytes)
            foreach ($case in @($hex.ToLower(), $hex)) {
                $tried++
                if (TryKey $case ("{0}({1}) sep='{2}' {3}case" -f $h[0], ($combo -join '+'), ($sep -replace "`n", '\n'), $(if ($case -eq $hex) { 'upper' } else { 'lower' }))) { exit 0 }
            }
        }
        # the raw concatenation itself, in case the player does not hash on this path
        $tried++
        if (TryKey $concat ("raw({0}) sep='{1}'" -f ($combo -join '+'), ($sep -replace "`n", '\n'))) { exit 0 }
    }
}
Write-Host "no recipe matched after $tried keys"
exit 1
