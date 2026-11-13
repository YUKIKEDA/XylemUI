#!/usr/bin/env pwsh
# The size measurement of row LC2 (Issue #862): publishes the spike once per configuration (NativeAOT, Release, win-x64) and
# records the size of each executable. README.md says what the configurations are.
#
#   ./spike/CldrSize/measure.ps1 -OutRoot C:\path\to\scratch [-MapFile] [-Configs baseline,b-date-10] [-TimeoutSeconds 900]
#
# Writes <OutRoot>\results.csv and <OutRoot>\results.md, and keeps each executable (and, with -MapFile, its ILC map) in
# <OutRoot>\<config>\.
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $OutRoot,
    [string[]] $Configs,
    [switch] $MapFile,
    [int] $TimeoutSeconds = 900,
    # The symbols of the map counted as generated data: the RVA fields that hold the bytes of a ReadOnlySpan<byte> literal.
    [string] $DataSymbolPattern = 'FieldRvaData|PrivateImplementationDetails',
    # Runs tools/CldrSizeSpike/generate.py first when Generated/ is missing.
    [string] $CldrZip
)

$ErrorActionPreference = 'Stop'

# NativeAOT finds the linker through vswhere, which lives here and is not on PATH on this machine.
$vsInstaller = 'C:\Program Files (x86)\Microsoft Visual Studio\Installer'
if (($env:PATH -split ';') -notcontains $vsInstaller) {
    $env:PATH = "$vsInstaller;$env:PATH"
}

$project = Join-Path $PSScriptRoot 'CldrSize.csproj'
$generated = Join-Path $PSScriptRoot 'Generated'
$manifestPath = Join-Path $generated 'manifest.json'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path

if (-not (Test-Path -LiteralPath $manifestPath)) {
    if (-not $CldrZip) {
        throw "Generated\manifest.json is missing: pass -CldrZip <cldr-49.0.0-json-full.zip> or run tools/CldrSizeSpike/generate.py."
    }
    & python -I (Join-Path $repoRoot 'tools\CldrSizeSpike\generate.py') --zip $CldrZip
    if ($LASTEXITCODE -ne 0) {
        throw "generate.py failed with exit code $LASTEXITCODE"
    }
}

$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json -AsHashtable
if (-not $Configs) {
    # The baseline first: every growth is measured against it.
    $Configs = @('baseline') + @($manifest.configs.Keys | Where-Object { $_ -ne 'baseline' } | Sort-Object)
}

New-Item -ItemType Directory -Force -Path $OutRoot | Out-Null
$OutRoot = (Resolve-Path -LiteralPath $OutRoot).Path
$objDir = Join-Path $PSScriptRoot 'obj'
$binDir = Join-Path $PSScriptRoot 'bin'
$dataObjDir = Join-Path $PSScriptRoot 'Data\obj'
$dataBinDir = Join-Path $PSScriptRoot 'Data\bin'

function Measure-Map([string] $path) {
    # Reads every element that has a Name and a Length (or Size) attribute; the ILC map is XML with one node per element.
    $content = [System.IO.File]::ReadAllText($path)
    $nodes = [System.Collections.Generic.List[object]]::new()
    foreach ($m in [regex]::Matches($content, '<\w+\s([^>]*?)/?>')) {
        $attributes = $m.Groups[1].Value
        $name = [regex]::Match($attributes, '\bName="([^"]*)"')
        $length = [regex]::Match($attributes, '\b(?:Length|Size)="(\d+)"')
        if ($name.Success -and $length.Success) {
            $nodes.Add([pscustomobject]@{ Name = [System.Net.WebUtility]::HtmlDecode($name.Groups[1].Value); Length = [long]$length.Groups[1].Value })
        }
    }

    $data = @($nodes | Where-Object { $_.Name -match $DataSymbolPattern })
    $top = @($nodes | Sort-Object Length -Descending | Select-Object -First 40)
    [pscustomobject]@{
        Nodes = $nodes.Count
        DataNodes = $data.Count
        DataBytes = [long](($data | Measure-Object Length -Sum).Sum ?? 0)
        Top = $top
    }
}

$results = [System.Collections.Generic.List[object]]::new()
foreach ($config in $Configs) {
    if ($manifest.configs.Keys -notcontains $config) {
        Write-Warning "unknown configuration $config"
        continue
    }

    $info = $manifest.configs[$config]
    $out = Join-Path $OutRoot $config
    foreach ($dir in @($objDir, $binDir, $dataObjDir, $dataBinDir, $out)) {
        if (Test-Path -LiteralPath $dir) {
            Remove-Item -LiteralPath $dir -Recurse -Force
        }
    }

    New-Item -ItemType Directory -Force -Path $out | Out-Null
    $log = Join-Path $out 'publish.log'
    $errLog = Join-Path $out 'publish.err.log'
    $arguments = @(
        'publish', "`"$project`"", '-c', 'Release', '-r', 'win-x64', '-o', "`"$out`"", "-p:SpikeConfig=$config",
        '--disable-build-servers', '-nologo'
    )
    if ($MapFile) {
        $arguments += '-p:IlcGenerateMapFile=true'
    }

    $watch = [System.Diagnostics.Stopwatch]::StartNew()
    $process = Start-Process -FilePath 'dotnet' -ArgumentList $arguments -NoNewWindow -PassThru `
        -RedirectStandardOutput $log -RedirectStandardError $errLog
    $null = $process.Handle # caches the handle so that ExitCode is readable after the exit
    $status = 'ok'
    if (-not $process.WaitForExit($TimeoutSeconds * 1000)) {
        $process.Kill($true)
        $process.WaitForExit()
        $status = "timeout after $TimeoutSeconds s"
    } elseif ($process.ExitCode -ne 0) {
        $status = "exit code $($process.ExitCode) (see $log)"
    }

    $watch.Stop()
    $exe = Join-Path $out 'CldrSize.exe'
    $exeBytes = $null
    if ($status -eq 'ok') {
        if (Test-Path -LiteralPath $exe) {
            $exeBytes = (Get-Item -LiteralPath $exe).Length
        } else {
            $status = 'no CldrSize.exe in the output'
        }
    }

    $map = $null
    if ($MapFile -and $status -eq 'ok') {
        $mapSource = Get-ChildItem -LiteralPath $objDir -Recurse -File -ErrorAction SilentlyContinue |
            Where-Object { $_.Name -like 'CldrSize.map*' } | Select-Object -First 1
        if ($null -eq $mapSource) {
            $status = 'ok, but no map file under obj\'
        } else {
            $mapCopy = Join-Path $out $mapSource.Name
            Copy-Item -LiteralPath $mapSource.FullName -Destination $mapCopy
            $map = Measure-Map $mapCopy
            $map.Top | ForEach-Object { '{0,10}  {1}' -f $_.Length, $_.Name } |
                Set-Content -LiteralPath (Join-Path $out 'map-top40.txt') -Encoding utf8NoBOM
            if ($map.Nodes -eq 0) {
                $status = 'ok, but the map has no node with Name and Length (check its format)'
            }
        }
    }

    $results.Add([pscustomobject]@{
        Config = $config
        Way = $info.way
        Families = ($info.families -join '+')
        ReferencedLocales = $info.referencedLocales
        EmittedLocales = $info.emittedLocales
        RawReferencedBytes = $info.referencedDataBytes
        ExeBytes = $exeBytes
        GrowthBytes = $null
        MapDataNodes = ${map}?.DataNodes
        MapDataBytes = ${map}?.DataBytes
        MapDataGrowth = $null
        Seconds = [math]::Round($watch.Elapsed.TotalSeconds, 1)
        Status = $status
    })
    $shown = if ($null -ne $exeBytes) { '{0:N0} bytes' -f $exeBytes } else { '-' }
    Write-Host ('{0,-14} {1,8:N1} s  {2,16}  {3}' -f $config, $watch.Elapsed.TotalSeconds, $shown, $status)
}

$baseline = $results | Where-Object { $_.Config -eq 'baseline' -and $null -ne $_.ExeBytes } | Select-Object -First 1
if ($null -eq $baseline) {
    Write-Warning 'no baseline in this run: the growth columns are empty'
} else {
    foreach ($r in $results) {
        if ($null -ne $r.ExeBytes) {
            $r.GrowthBytes = $r.ExeBytes - $baseline.ExeBytes
        }

        if ($null -ne $r.MapDataBytes -and $null -ne $baseline.MapDataBytes) {
            $r.MapDataGrowth = $r.MapDataBytes - $baseline.MapDataBytes
        }
    }
}

$csv = Join-Path $OutRoot 'results.csv'
$results | Export-Csv -LiteralPath $csv -NoTypeInformation -Encoding utf8NoBOM

$md = [System.Collections.Generic.List[string]]::new()
$md.Add('| Config | Way | Families | Referenced locales | Emitted locales | Raw referenced bytes | Exe bytes | Growth | Map data growth | Seconds | Status |')
$md.Add('| --- | --- | --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | --- |')
foreach ($r in $results) {
    $f = { param($v) if ($null -eq $v) { '-' } else { '{0:N0}' -f $v } }
    $md.Add("| $($r.Config) | $($r.Way) | $($r.Families) | $($r.ReferencedLocales) | $($r.EmittedLocales) | $(& $f $r.RawReferencedBytes) | $(& $f $r.ExeBytes) | $(& $f $r.GrowthBytes) | $(& $f $r.MapDataGrowth) | $($r.Seconds) | $($r.Status) |")
}

$mdPath = Join-Path $OutRoot 'results.md'
$md | Set-Content -LiteralPath $mdPath -Encoding utf8NoBOM
Write-Host "wrote $csv and $mdPath"
$failed = @($results | Where-Object { $_.Status -ne 'ok' })
if ($failed.Count -gt 0) {
    Write-Warning "$($failed.Count) configuration(s) did not finish cleanly: $(($failed | ForEach-Object Config) -join ', ')"
    exit 1
}
