# Poor man's IL2CPP method reader for a 32-bit x86 GameAssembly.dll.
#
# For each method named on the command line (Class.Method or just Method), list in address order:
#   - every E8 rel32 call whose target is the START of a known method  -> "call  Class.Method"
#   - every 4-byte value equal to a string literal's address            -> "lit   \"...\""
#   - every 4-byte value equal to a known method's start                -> "&     Class.Method"
# Method names and ranges come from Il2CppInspector's types-all.cs (classes tracked by brace
# depth, so nested types do not swallow their parent's methods); literal addresses from
# metadata.json. VA <-> file offset goes through the PE section table.
param(
    [Parameter(Mandatory = $true)][string[]] $Targets,
    [switch] $Hex,
    [switch] $Rebuild
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$bin  = Join-Path $root 'x\GameAssembly.dll'
$types = Join-Path $root 'inspector\types-all.cs'
$json  = Join-Path $root 'inspector\metadata.json'
$cache = Join-Path $root 'inspector\symbols2.clixml'

$bytes = [System.IO.File]::ReadAllBytes($bin)

# ---- PE sections -----------------------------------------------------------
$peOff = [BitConverter]::ToInt32($bytes, 0x3C)
$numSections = [BitConverter]::ToUInt16($bytes, $peOff + 6)
$optSize = [BitConverter]::ToUInt16($bytes, $peOff + 20)
$imageBase = [int64][BitConverter]::ToUInt32($bytes, $peOff + 24 + 28)
$sections = @()
for ($s = 0; $s -lt $numSections; $s++) {
    $h = $peOff + 24 + $optSize + $s * 40
    $sections += [pscustomobject]@{
        Name = ([System.Text.Encoding]::ASCII.GetString($bytes, $h, 8)).Trim([char]0)
        VSize = [int64][BitConverter]::ToUInt32($bytes, $h + 8)
        VA = [int64][BitConverter]::ToUInt32($bytes, $h + 12)
        RawSize = [int64][BitConverter]::ToUInt32($bytes, $h + 16)
        RawPtr = [int64][BitConverter]::ToUInt32($bytes, $h + 20)
    }
}
function VaToOff([int64] $va) {
    $rva = $va - $imageBase
    foreach ($sec in $sections) {
        $size = [Math]::Max($sec.VSize, $sec.RawSize)
        if ($rva -ge $sec.VA -and $rva -lt $sec.VA + $size) { return [int64]($sec.RawPtr + ($rva - $sec.VA)) }
    }
    return -1
}

# ---- symbols (cached) ------------------------------------------------------
if ((Test-Path $cache) -and -not $Rebuild) {
    $sym = Import-Clixml $cache
    $startArr = [int64[]]$sym.Starts; $endArr = [int64[]]$sym.Ends; $nameArr = [string[]]$sym.Names; $lits = $sym.Lits
} else {
    Write-Host "parsing $types ..."
    $starts = New-Object System.Collections.Generic.List[int64]
    $ends   = New-Object System.Collections.Generic.List[int64]
    $names  = New-Object System.Collections.Generic.List[string]
    $rxClass = [regex]'^\s*(?:\[[^\]]*\]\s*)*(?:public|private|protected|internal|static|sealed|abstract|partial|readonly|unsafe|\s)*\b(?:class|struct|interface|enum)\s+([\w`]+)'
    $rxAddr  = [regex]'0x([0-9A-Fa-f]+)-0x([0-9A-Fa-f]+)'
    $rxName  = [regex]'([\w`.]+)\s*(?:\(|\{)'
    $stack = New-Object System.Collections.Generic.List[object]   # (name, depth)
    $depth = 0
    $pending = $null
    foreach ($line in [System.IO.File]::ReadLines($types)) {
        $code = $line.Split('//')[0]
        $m = $rxClass.Match($code)
        if ($m.Success -and $code -notmatch '\(') { $pending = $m.Groups[1].Value }
        if ($line -match '//\s*0x[0-9A-Fa-f]+-0x') {
            $nm = $rxName.Matches($code)
            $name = if ($nm.Count -gt 0) { $nm[$nm.Count - 1].Groups[1].Value } else { '?' }
            $cls = if ($stack.Count -gt 0) { $stack[$stack.Count - 1].Name } else { '?' }
            $addrs = $rxAddr.Matches($line)
            $k = 0
            foreach ($a in $addrs) {
                $suffix = if ($addrs.Count -gt 1) { if ($k -eq 0) { '.get' } else { '.set' } } else { '' }
                $starts.Add([Convert]::ToInt64($a.Groups[1].Value, 16))
                $ends.Add([Convert]::ToInt64($a.Groups[2].Value, 16))
                $names.Add("$cls.$name$suffix")
                $k++
            }
        }
        foreach ($ch in $code.ToCharArray()) {
            if ($ch -eq '{') {
                $depth++
                if ($pending) { $stack.Add([pscustomobject]@{ Name = $pending; Depth = $depth }); $pending = $null }
            } elseif ($ch -eq '}') {
                if ($stack.Count -gt 0 -and $stack[$stack.Count - 1].Depth -eq $depth) { $stack.RemoveAt($stack.Count - 1) }
                $depth--
            }
        }
    }
    Write-Host "  $($starts.Count) methods"
    Write-Host "parsing literals in $json ..."
    $lits = @{}
    $text = [System.IO.File]::ReadAllText($json)
    $rxLit = [regex]'"virtualAddress":\s*"0x([0-9A-Fa-f]+)",\s*"name":\s*"StringLiteral_[^"]*",\s*"string":\s*"((?:[^"\\]|\\.)*)"'
    foreach ($m in $rxLit.Matches($text)) { $lits[[int64][Convert]::ToInt64($m.Groups[1].Value, 16)] = $m.Groups[2].Value }
    $text = $null
    Write-Host "  $($lits.Count) literals"
    $order = 0..($starts.Count - 1) | Sort-Object { $starts[$_] }
    $startArr = [int64[]]@($order | ForEach-Object { $starts[$_] })
    $endArr   = [int64[]]@($order | ForEach-Object { $ends[$_] })
    $nameArr  = [string[]]@($order | ForEach-Object { $names[$_] })
    Export-Clixml -Path $cache -InputObject @{ Starts = $startArr; Ends = $endArr; Names = $nameArr; Lits = $lits }
}
$byStart = @{}
for ($i = 0; $i -lt $startArr.Length; $i++) {
    if (-not $byStart.ContainsKey($startArr[$i])) { $byStart[$startArr[$i]] = $nameArr[$i] }
    elseif ($byStart[$startArr[$i]] -notlike "*$($nameArr[$i])*") { $byStart[$startArr[$i]] += " | " + $nameArr[$i] }
}
function MethodAt([int64] $va) {
    $idx = [Array]::BinarySearch($startArr, $va)
    if ($idx -lt 0) { $idx = (-bnot $idx) - 1 }
    if ($idx -lt 0) { return $null }
    if ($va -ge $startArr[$idx] -and $va -lt $endArr[$idx]) { return $nameArr[$idx] }
    return $null
}

# ---- the targets -----------------------------------------------------------
foreach ($t in $Targets) {
    $leaf = $t.Split('.')[-1]
    $hits = @()
    for ($i = 0; $i -lt $nameArr.Length; $i++) {
        if ($nameArr[$i] -eq $t -or ($t -notmatch '\.' -and $nameArr[$i] -like "*.$leaf")) { $hits += $i }
    }
    if ($hits.Count -eq 0) {
        # class mismatch: fall back to the leaf name and say which classes carry it
        for ($i = 0; $i -lt $nameArr.Length; $i++) { if ($nameArr[$i] -like "*.$leaf") { $hits += $i } }
        if ($hits.Count -eq 0) { Write-Host "== $t : not found"; continue }
    }
    foreach ($h in $hits) {
        $start = $startArr[$h]; $end = $endArr[$h]
        $off = VaToOff $start
        Write-Host ("== {0}  0x{1:X8}-0x{2:X8}  ({3} bytes, file 0x{4:X})" -f $nameArr[$h], $start, $end, ($end - $start), $off)
        if ($off -lt 0) { continue }
        $len = [int]($end - $start)
        if ($Hex) {
            for ($p = 0; $p -lt $len; $p += 32) {
                $n = [Math]::Min(32, $len - $p)
                Write-Host ("   {0:X8}  {1}" -f ($start + $p), (($bytes[($off + $p)..($off + $p + $n - 1)] | ForEach-Object { $_.ToString('X2') }) -join ' '))
            }
        }
        for ($p = 0; $p -le $len - 5; $p++) {
            $va = $start + $p
            if ($bytes[$off + $p] -eq 0xE8) {
                $rel = [int64][BitConverter]::ToInt32($bytes, [int]($off + $p + 1))
                $target = ($va + 5 + $rel) % 4294967296
                if ($target -lt 0) { $target += 4294967296 }
                if ($byStart.ContainsKey($target)) { Write-Host ("   +{0,-5:X4} call  {1}  @0x{2:X8}" -f $p, $byStart[$target], $target); continue }
                $inside = MethodAt $target
                if ($inside) { Write-Host ("   +{0,-5:X4} call  {1} (+into)" -f $p, $inside) }
            }
            $dw = [int64][BitConverter]::ToUInt32($bytes, [int]($off + $p))
            if ($lits.ContainsKey($dw)) { Write-Host ("   +{0,-5:X4} lit   ""{1}""" -f $p, $lits[$dw]) }
            elseif ($byStart.ContainsKey($dw) -and $dw -ne $start) { Write-Host ("   +{0,-5:X4} &     {1}" -f $p, $byStart[$dw]) }
        }
    }
}
