# Measures input to display for the comparison apps built by build.ps1 (roadmap row R3, docs/benchmarks/r3-input.md): rounds
# of XylemUI, Avalonia, Flutter, and GPUI in four scenes (static and animated, each uncovered and with a covering window),
# one `input-latency` run each per round, interleaved. Each run appends one JSON line to <Built>/input.jsonl.
#   ./benchmarks/compare/input.ps1 -Built <build.ps1 -Out> -PresentMon <PresentMon-2.6.0-x64.exe> [-Rounds 3] [-Presses 200]
# The runs click once on each app's window and send it the space key (XylemUI.E2E/InputLatency.cs): nobody uses the machine
# while they run. Avalonia runs in its default composition, as its users get it (its --redirection variant is for PresentMon's
# frame counts only, M1a).
param(
    [Parameter(Mandatory = $true)][string]$Built,
    [Parameter(Mandatory = $true)][string]$PresentMon,
    [int]$Rounds = 3,
    [int]$Presses = 200
)

$ErrorActionPreference = 'Stop'
$e2e = Join-Path $Built 'xylemui\XylemUI.E2E.exe'
$apps = @(
    @{ Name = 'XylemUI'; App = $e2e; Extra = @('--app-args', 'app') },
    @{ Name = 'Avalonia'; App = (Join-Path $Built 'avalonia\CompareAvalonia.exe'); Extra = @() },
    @{ Name = 'Flutter'; App = (Join-Path $Built 'flutter\compare_flutter.exe'); Extra = @() },
    @{ Name = 'GPUI'; App = (Join-Path $Built 'gpui\compare_gpui.exe'); Extra = @() }
)
$scenes = @(@(), @('--static'), @('--covered'), @('--static', '--covered'))
$results = Join-Path $Built 'input.jsonl'
for ($round = 1; $round -le $Rounds; $round++) {
    foreach ($scene in $scenes) {
        foreach ($app in $apps) {
            Write-Host "==> round $round, $($app.Name) $($scene -join ' ')"
            $line = & $e2e input-latency --presentmon $PresentMon --app $app.App --name $app.Name --presses $Presses @($app.Extra) @scene
            if ($LASTEXITCODE -ne 0) { throw "input-latency failed for $($app.Name) with exit code $LASTEXITCODE." }
            $line | Add-Content -Path $results -Encoding utf8
        }
    }
}
