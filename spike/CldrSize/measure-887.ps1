#!/usr/bin/env pwsh
# The size measurement of Issue #887 (row LC2, step 1): the interval formats and the currency symbols, measured the way
# Issue #862 measured the other tables. README.md says what the configurations are.
#
#   ./spike/CldrSize/measure-887.ps1 -OutRoot C:\path\to\scratch [-CldrZip <zip>] [-TimeoutSeconds 900]
#
# Runs tools/CldrSizeSpike/generate.py, then measure.ps1 with the ILC map for baseline and the 12 configurations of
# Issue #887, and prints one line per configuration: the executable size, the growth over baseline and over the base of the
# same locale count, and the raw referenced bytes of each table. Writes <OutRoot>\results-887.md beside measure.ps1's files.
#
# Time: 13 publishes. Issue #862's 22 publishes took about 7 minutes (8 to 45 s each), so expect about 3 to 10 minutes.
# Each publish is killed after -TimeoutSeconds (900 by default), so the whole run ends within 13 x 900 s even if one hangs.
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $OutRoot,
    [string] $CldrZip = (Join-Path $PSScriptRoot 'cldr-zip\cldr-49.0.0-json-full.zip'),
    [int] $TimeoutSeconds = 900
)

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$manifestPath = Join-Path $PSScriptRoot 'Generated\manifest.json'

Write-Host ('[1/3] {0:HH:mm:ss} generating the tables from {1} (a few seconds)' -f (Get-Date), $CldrZip)
if (-not (Test-Path -LiteralPath $CldrZip)) {
    throw "no CLDR zip at ${CldrZip}: download it as tools/CldrSizeSpike/README.md says, or pass -CldrZip"
}
& python -I (Join-Path $repoRoot 'tools\CldrSizeSpike\generate.py') --zip $CldrZip
if ($LASTEXITCODE -ne 0) {
    throw "generate.py failed with exit code $LASTEXITCODE"
}

$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json -AsHashtable
[string[]] $configs = @('baseline') + @($manifest.issue887.configs)
Write-Host ('[2/3] {0:HH:mm:ss} publishing {1} configurations (expected 3 to 10 minutes; each publish times out after {2} s)' -f (Get-Date), $configs.Count, $TimeoutSeconds)
& (Join-Path $PSScriptRoot 'measure.ps1') -OutRoot $OutRoot -Configs $configs -MapFile -TimeoutSeconds $TimeoutSeconds
$measureExit = $LASTEXITCODE

Write-Host ('[3/3] {0:HH:mm:ss} summarizing' -f (Get-Date))
$OutRoot = (Resolve-Path -LiteralPath $OutRoot).Path
$rows = @{}
foreach ($r in (Import-Csv -LiteralPath (Join-Path $OutRoot 'results.csv'))) {
    $rows[$r.Config] = $r
}

function Format-Bytes([object] $value) {
    if ($null -eq $value -or "$value" -eq '') { '-' } else { '{0:N0}' -f [long]$value }
}

$tables = @('plural', 'number', 'date', 'interval', 'currency')
$likelyBytes = ($manifest.raw | Where-Object { $_.family -eq 'likely' } | Select-Object -First 1).uniqueBytes
$md = [System.Collections.Generic.List[string]]::new()
$md.Add('| Config | Locales | Exe bytes | Growth over baseline | Growth over c-base | Map data growth | Raw: plural | number | date | interval | currency | likely | Raw total | Status |')
$md.Add('| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | --- |')
foreach ($config in $configs) {
    $r = $rows[$config]
    $info = $manifest.configs[$config]
    if ($null -eq $r) {
        Write-Warning "no result for $config"
        continue
    }

    $base = $null
    if ($config -like 'c-*') {
        $baseName = 'c-base-' + ($config -split '-')[-1]
        $baseRow = $rows[$baseName]
        if ($null -ne $baseRow -and $r.ExeBytes -ne '' -and $baseRow.ExeBytes -ne '') {
            $base = [long]$r.ExeBytes - [long]$baseRow.ExeBytes
        }
    }

    $raw = foreach ($t in $tables) {
        if ($info.referenced.ContainsKey($t)) { Format-Bytes $info.referenced[$t].bytes } else { '-' }
    }
    $likelyShown = if ($info.families -contains 'likely') { Format-Bytes $likelyBytes } else { '-' }
    $line = "| $config | $($info.referencedLocales) | $(Format-Bytes $r.ExeBytes) | $(Format-Bytes $r.GrowthBytes) | $(Format-Bytes $base) | $(Format-Bytes $r.MapDataGrowth) | $($raw -join ' | ') | $likelyShown | $(Format-Bytes $info.referencedDataBytes) | $($r.Status) |"
    $md.Add($line)
    Write-Host ('{0,-16} exe {1,12} bytes  growth {2,10}  over c-base {3,10}  raw plural/number/date/interval/currency/likely {4}/{5}  {6}' -f `
            $config, (Format-Bytes $r.ExeBytes), (Format-Bytes $r.GrowthBytes), (Format-Bytes $base), ($raw -join '/'), $likelyShown, $r.Status)
}

$mdPath = Join-Path $OutRoot 'results-887.md'
$md | Set-Content -LiteralPath $mdPath -Encoding utf8NoBOM
Write-Host "wrote $mdPath"
if ($measureExit -ne 0) {
    Write-Warning "measure.ps1 exited with ${measureExit}: see the Status column and <OutRoot>\<config>\publish.log"
    exit $measureExit
}
