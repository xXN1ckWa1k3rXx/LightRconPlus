param([string]$ReferenceDirectory = (Join-Path $PSScriptRoot '..\.local\references'))
$ErrorActionPreference = 'Stop'
$output = Join-Path $PSScriptRoot '..\src\LightRcon.Core\Data'
# Factual identifiers and labels from Procon Frostbite. See THIRD-PARTY-NOTICES.md.
$labels = [System.Collections.Generic.Dictionary[string,string]]::new([StringComparer]::OrdinalIgnoreCase)
foreach ($line in Get-Content -LiteralPath (Join-Path $ReferenceDirectory 'procon-au.loc')) {
    if ($line -match '^global\.Weapons\.([^=]+)=(.*)$') { $labels[$Matches[1]] = $Matches[2].Trim() }
}
foreach ($game in @('BF3', 'BFHL')) {
    $maps = [ordered]@{}; $teams = [ordered]@{}; $weapons = [ordered]@{}
    foreach ($line in Get-Content -LiteralPath (Join-Path $ReferenceDirectory ($game + '.def'))) {
        if ($line -match '^procon\.protected\.maps\.add "([^"]+)" "([^"]+)" "([^"]+)" "([^"]+)"') {
            $mode,$file,$modeName,$name = $Matches[1],$Matches[2],$Matches[3],$Matches[4]
            if (!$maps.Contains($file)) { $maps[$file] = @{ FileName=$file; Name=$name; Modes=[System.Collections.Generic.List[object]]::new() } }
            $maps[$file].Modes.Add(@{ Code=$mode; Name=$modeName })
        }
        elseif ($line -match '^procon\.protected\.teamnames\.add "([^"]+)" (\d+) "([^"]+)" "([^"]*)" "([^"]+)"') {
            $map,$index,$label,$flag,$mode = $Matches[1],[int]$Matches[2],$Matches[3],$Matches[4],$Matches[5]
            $key = $map + '|' + $mode
            if (!$teams.Contains($key)) { $teams[$key] = [System.Collections.Generic.List[string]]::new() }
            while ($teams[$key].Count -le $index) { $teams[$key].Add('') }
            $code = ($label -split '\.')[-1]
            if ($code -in @('attackers','defenders') -and $flag) { $code += ':' + ($flag -replace '\.png$','') }
            $teams[$key][$index] = $code
        }
        elseif ($line -match '^procon\.protected\.weapons\.add \S+ "([^"]+)"') {
            $id=$Matches[1]; $weapons[$id] = if ($labels.ContainsKey($id)) { $labels[$id] } else { $id }
        }
    }
    @($maps.Values) | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $output ($game.ToLowerInvariant() + '-maps.json')) -Encoding utf8
    $teams | ConvertTo-Json -Depth 6 -Compress | Set-Content -LiteralPath (Join-Path $output ($game.ToLowerInvariant() + '-teams.json')) -Encoding utf8
    $weapons | ConvertTo-Json -Depth 3 | Set-Content -LiteralPath (Join-Path $output ($game.ToLowerInvariant() + '-weapons.json')) -Encoding utf8
    Write-Host "$game : $($maps.Count) maps, $($teams.Count) team combinations, $($weapons.Count) weapon IDs."
}
