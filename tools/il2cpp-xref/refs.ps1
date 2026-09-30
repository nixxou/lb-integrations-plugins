# Who references what, inside the game's own code range: callers (E8 rel32) of the named methods,
# and methods whose bytes carry the address of the named string literals. Uses xref.ps1's cache.
param(
    [string[]] $Methods = @(),
    [string[]] $Literals = @(),
    [int64] $From = 0x10378000,
    [int64] $To = 0x104C0000
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$bytes = [System.IO.File]::ReadAllBytes((Join-Path $root 'x\GameAssembly.dll'))
$sym = Import-Clixml (Join-Path $root 'inspector\symbols2.clixml')
$startArr = [int64[]]$sym.Starts; $endArr = [int64[]]$sym.Ends; $nameArr = [string[]]$sym.Names; $lits = $sym.Lits

$peOff = [BitConverter]::ToInt32($bytes, 0x3C)
$numSections = [BitConverter]::ToUInt16($bytes, $peOff + 6)
$optSize = [BitConverter]::ToUInt16($bytes, $peOff + 20)
$imageBase = [int64][BitConverter]::ToUInt32($bytes, $peOff + 24 + 28)
$sections = @()
for ($s = 0; $s -lt $numSections; $s++) {
    $h = $peOff + 24 + $optSize + $s * 40
    $sections += [pscustomobject]@{ VSize = [int64][BitConverter]::ToUInt32($bytes, $h + 8); VA = [int64][BitConverter]::ToUInt32($bytes, $h + 12); RawSize = [int64][BitConverter]::ToUInt32($bytes, $h + 16); RawPtr = [int64][BitConverter]::ToUInt32($bytes, $h + 20) }
}
function VaToOff([int64] $va) {
    $rva = $va - $imageBase
    foreach ($sec in $sections) { $size = [Math]::Max($sec.VSize, $sec.RawSize); if ($rva -ge $sec.VA -and $rva -lt $sec.VA + $size) { return [int64]($sec.RawPtr + ($rva - $sec.VA)) } }
    return -1
}
function MethodAt([int64] $va) {
    $idx = [Array]::BinarySearch($startArr, $va)
    if ($idx -lt 0) { $idx = (-bnot $idx) - 1 }
    if ($idx -lt 0) { return $null }
    if ($va -ge $startArr[$idx] -and $va -lt $endArr[$idx]) { return $nameArr[$idx] }
    return $null
}

$wantCalls = @{}
foreach ($m in $Methods) {
    $leaf = $m.Split('.')[-1]
    for ($i = 0; $i -lt $nameArr.Length; $i++) { if ($nameArr[$i] -eq $m -or $nameArr[$i] -like "*.$leaf") { $wantCalls[$startArr[$i]] = $nameArr[$i] } }
}
$wantLits = @{}
foreach ($l in $Literals) { foreach ($k in $lits.Keys) { if ($lits[$k] -eq $l) { $wantLits[[int64]$k] = $l } } }
Write-Host ("looking for {0} method start(s) and {1} literal address(es) in 0x{2:X8}-0x{3:X8}" -f $wantCalls.Count, $wantLits.Count, $From, $To)
foreach ($k in $wantLits.Keys) { Write-Host ("  literal 0x{0:X8} = ""{1}""" -f $k, $wantLits[$k]) }

$off0 = VaToOff $From
$len = [int]($To - $From)
$found = New-Object System.Collections.Generic.List[string]
for ($p = 0; $p -lt $len - 5; $p++) {
    $o = $off0 + $p
    if ($bytes[$o] -eq 0xE8) {
        $rel = [int64][BitConverter]::ToInt32($bytes, [int]($o + 1))
        $target = ($From + $p + 5 + $rel) % 4294967296
        if ($target -lt 0) { $target += 4294967296 }
        if ($wantCalls.ContainsKey($target)) { $found.Add(("call  {0,-45} from {1}  @0x{2:X8}" -f $wantCalls[$target], (MethodAt ($From + $p)), ($From + $p))) }
    }
    $dw = [int64][BitConverter]::ToUInt32($bytes, [int]$o)
    if ($wantLits.ContainsKey($dw)) { $found.Add(("lit   {0,-45} in   {1}  @0x{2:X8}" -f ('"' + $wantLits[$dw] + '"'), (MethodAt ($From + $p)), ($From + $p))) }
}
$found | Sort-Object | Get-Unique | ForEach-Object { "  $_" }
