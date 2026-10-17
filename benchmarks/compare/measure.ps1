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
    @{ Name = 'GPUI'; App = (Join-Path $Built 'gpui\compare_gpui.exe'); Extra = @() }
)
$results = Join-Path $Built 'results.jsonl'
for ($round = 1; $round -le $Rounds; $round++) {
    foreach ($app in $apps) {
        Write-Host "==> round $round, $($app.Name)"
        $line = & $e2e external --presentmon $PresentMon --app $app.App --name $app.Name --runs 1 @($app.Extra)
        if ($LASTEXITCODE -ne 0) { throw "external failed for $($app.Name) with exit code $LASTEXITCODE." }
        $line | Add-Content -Path $results -Encoding utf8
    }
}
