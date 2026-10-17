# Times the layout scene of roadmap row L1a (docs/benchmarks/l1a-layout.md) in XylemUI, Flutter, Avalonia, and Taffy (the layout
# of GPUI), built by build.ps1: rounds of the four, interleaved, each run printing or writing one JSON line, appended to
# <built>/layout.jsonl.
#   ./benchmarks/compare/layout.ps1 -Built <build.ps1 -Out> [-Rounds 5]
param(
    [Parameter(Mandatory = $true)][string]$Built,
    [int]$Rounds = 5
)

$ErrorActionPreference = 'Stop'
$results = Join-Path $Built 'layout.jsonl'
$file = Join-Path $Built 'layout-run.json'
for ($round = 1; $round -le $Rounds; $round++) {
    Write-Host "==> round $round"
    & (Join-Path $Built 'xylemui\XylemUI.E2E.exe') layout | Add-Content -Path $results -Encoding utf8
    & (Join-Path $Built 'taffy\compare_taffy.exe') | Add-Content -Path $results -Encoding utf8

    # The Flutter and Avalonia apps are GUI programs: they write the line to a file.
    foreach ($app in (Join-Path $Built 'flutter\compare_flutter.exe'), (Join-Path $Built 'avalonia\CompareAvalonia.exe')) {
        $process = Start-Process -Wait -PassThru -FilePath $app -ArgumentList '--layout', $file
        if ($process.ExitCode -ne 0) { throw "$app failed with exit code $($process.ExitCode)." }
        Get-Content $file | Add-Content -Path $results -Encoding utf8
    }
}
