# Takes the pictures of the text scene in each framework and reads them (roadmap rows T4 and T2, docs/benchmarks/t4-glyphs.md,
# docs/benchmarks/t2-cleartype.md): every app of build.ps1's <builds> draws the scene black on white, then white on black;
# XylemUI draws it as its window says (ClearType where Windows and the monitor allow it) and in grayscale (xylemui-gray);
# XylemUI.E2E's text-capture copies its client area from the screen; text_quality.py prints the position error and the stem
# darkness, and writes the pictures side by side.
#   ./benchmarks/compare/text.ps1 -Builds <build.ps1's out> -Out <directory>
# The pictures have the display's scale (the scale of Windows' display settings when it runs); text_quality.py reads them all
# the same way.
param(
    [Parameter(Mandatory = $true)][string]$Builds,
    [Parameter(Mandatory = $true)][string]$Out
)

$ErrorActionPreference = 'Stop'
New-Item -ItemType Directory -Force $Out | Out-Null
$capture = Join-Path $Builds 'xylemui\XylemUI.E2E.exe'
$apps = [ordered]@{
    xylemui = @($capture, 'text')
    'xylemui-gray' = @($capture, 'text --gray')
    flutter = @((Join-Path $Builds 'flutter\compare_flutter.exe'), '--text')
    avalonia = @((Join-Path $Builds 'avalonia\CompareAvalonia.exe'), '--text')
    gpui = @((Join-Path $Builds 'gpui-text\compare_gpui_text.exe'), '')
}

$pairs = @()
foreach ($name in $apps.Keys) {
    ($app, $scene) = $apps[$name]
    foreach ($scheme in 'light', 'dark') {
        # An array even for one argument: splatting a lone string passes its characters one by one.
        [string[]]$arguments = @(@($scene) + $(if ($scheme -eq 'dark') { @('--dark') } else { @() }) | Where-Object { $_ })
        & $capture text-capture (Join-Path $Out "$name-$scheme.png") $app @arguments
        if ($LASTEXITCODE -ne 0) { throw "The capture of $name ($scheme) failed." }
    }

    $pairs += "$name=$(Join-Path $Out "$name-light.png"),$(Join-Path $Out "$name-dark.png")"
}

python -I (Join-Path $PSScriptRoot 'text_quality.py') $Out @pairs
if ($LASTEXITCODE -ne 0) { throw 'text_quality.py failed.' }
