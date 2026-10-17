# Measures the comparison apps built by build.ps1 (roadmap row M1a, docs/benchmarks/m1-baseline.md): rounds of XylemUI,
# Avalonia, Flutter, and GPUI, one `external` run each per round, interleaved, so that a change of the machine's state over
# the session spreads over every framework. Each run appends one JSON line to <out>/results.jsonl.
#   ./benchmarks/compare/measure.ps1 -Built <build.ps1 -Out> -PresentMon <PresentMon-2.6.0-x64.exe> [-Rounds 10]
param(
    [Parameter(Mandatory = $true)][string]$Built,
    [Parameter(Mandatory = $true)][string]$PresentMon,
    [int]$Rounds = 10
)

$ErrorActionPreference = 'Stop'
$e2e = Join-Path $Built 'xylemui\XylemUI.E2E.exe'
$apps = @(
    @{ Name = 'XylemUI'; App = $e2e; Extra = @('--app-args', 'app') },
    @{ Name = 'Avalonia'; App = (Join-Path $Built 'avalonia\CompareAvalonia.exe'); Extra = @('--frames-args', '--redirection') },
    @{ Name = 'Flutter'; App = (Join-Path $Built 'flutter\compare_flutter.exe'); Extra = @() },
    @{ Name = 'GPUI'; App = (Join-Path $Built 'gpui\compare_gpui.exe'); Extra = @() },
    # XylemUI's frames at Flutter's frame period on the measuring machine (46 ms, M1a), to compare present latency at one rate.
    @{ Name = 'XylemUI at 46 ms'; App = $e2e; Extra = @('--app-args', 'app', '--frames-args', '--period 46', '--frames-only') }
)
$results = Join-Path $Built 'results.jsonl'

# Every other window is minimized while the apps run, so that each app's window opens uncovered on the primary display (the
# startup to the screen reads the desktop image there); they are restored at the end.
$shell = New-Object -ComObject Shell.Application
$shell.MinimizeAll()
try {
for ($round = 1; $round -le $Rounds; $round++) {
    foreach ($app in $apps) {
        Write-Host "==> round $round, $($app.Name)"
        $line = & $e2e external --presentmon $PresentMon --app $app.App --name $app.Name --runs 1 @($app.Extra)
        if ($LASTEXITCODE -ne 0) { throw "external failed for $($app.Name) with exit code $LASTEXITCODE." }
        $line | Add-Content -Path $results -Encoding utf8
    }
}
}
finally {
    $shell.UndoMinimizeALL()
}
