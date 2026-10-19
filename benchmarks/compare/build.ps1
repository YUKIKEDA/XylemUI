# Builds the apps of the framework comparison (roadmap rows M1a and L1a, docs/benchmarks/m1-baseline.md and
# docs/benchmarks/l1a-layout.md) into <out>/<framework>.
#   ./benchmarks/compare/build.ps1 -Out <directory>
# Needs: the .NET 10 SDK with the MSVC linker (NativeAOT), Flutter (stable), and Rust (stable). The generated Flutter runner,
# the copied font, and the build output are not committed.
param([Parameter(Mandatory = $true)][string]$Out)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$font = Join-Path $root '..\..\tests\XylemUI.Text.Tests\Fonts\XylemUITestSans.ttf'
New-Item -ItemType Directory -Force $Out | Out-Null

function Invoke-Step([string]$Name, [scriptblock]$Body) {
    Write-Host "==> $Name"
    & $Body
    if ($LASTEXITCODE -ne 0) { throw "$Name failed with exit code $LASTEXITCODE." }
}

# XylemUI: the benchmark app itself (its `app` scenario is the scene).
Invoke-Step 'XylemUI (NativeAOT)' {
    dotnet publish (Join-Path $root '..\XylemUI.E2E') -c Release -r win-x64 -o (Join-Path $Out 'xylemui')
}

Invoke-Step 'Avalonia (NativeAOT)' {
    dotnet publish (Join-Path $root 'Avalonia') -c Release -r win-x64 -o (Join-Path $Out 'avalonia')
}

# Flutter: the Windows runner comes from `flutter create`, with the window set to 800 x 600 like the others.
$flutter = Join-Path $root 'flutter'
Push-Location $flutter
try {
    New-Item -ItemType Directory -Force (Join-Path $flutter 'fonts') | Out-Null
    Copy-Item $font (Join-Path $flutter 'fonts\XylemUITestSans.ttf') -Force
    if (-not (Test-Path (Join-Path $flutter 'windows'))) {
        Invoke-Step 'Flutter runner (flutter create)' { flutter create --platforms=windows --project-name compare_flutter . }
    }

    $main = Join-Path $flutter 'windows\runner\main.cpp'
    (Get-Content $main -Raw) -replace 'Win32Window::Size size\(\d+, \d+\);', 'Win32Window::Size size(800, 600);' | Set-Content $main -NoNewline
    Invoke-Step 'Flutter (release)' { flutter build windows --release }
    $built = Join-Path $flutter 'build\windows\x64\runner\Release'
    Copy-Item -Recurse -Force (Join-Path $built '*') (New-Item -ItemType Directory -Force (Join-Path $Out 'flutter')).FullName
}
finally {
    Pop-Location
}

Invoke-Step 'GPUI (release)' {
    cargo build --release --manifest-path (Join-Path $root 'gpui\Cargo.toml') --target-dir (Join-Path $Out 'gpui-target')
}
$gpui = New-Item -ItemType Directory -Force (Join-Path $Out 'gpui')
Copy-Item (Join-Path $Out 'gpui-target\release\compare_gpui.exe') $gpui.FullName -Force

# Taffy, the layout of GPUI, at the version GPUI uses: the layout scene of row L1a.
Invoke-Step 'Taffy (release)' {
    cargo build --release --manifest-path (Join-Path $root 'taffy\Cargo.toml') --target-dir (Join-Path $Out 'taffy-target')
}
$taffy = New-Item -ItemType Directory -Force (Join-Path $Out 'taffy')
Copy-Item (Join-Path $Out 'taffy-target\release\compare_taffy.exe') $taffy.FullName -Force

# The version of each framework the apps were built with, for the tables that cite them: the comparison is made against each
# framework's latest stable release, and a Flutter SDK left on an old release went on measuring 3.38.5 from L1a to L7.
function Get-LockedVersion([string]$Lock, [string]$Name) {
    $text = Get-Content $Lock -Raw
    if ($text -match "(?m)^name = `"$Name`"\r?\nversion = `"([^`"]+)`"") { return $Matches[1] }
    throw "No $Name in $Lock."
}

$avalonia = ([xml](Get-Content (Join-Path $root 'Avalonia\CompareAvalonia.csproj'))).Project.ItemGroup.PackageReference |
    Where-Object { $_.Include -eq 'Avalonia' } | Select-Object -First 1
$versions = [ordered]@{
    flutter = (flutter --version --machine | ConvertFrom-Json).frameworkVersion
    avalonia = $avalonia.Version
    gpui = Get-LockedVersion (Join-Path $root 'gpui\Cargo.lock') 'gpui'
    taffy = Get-LockedVersion (Join-Path $root 'taffy\Cargo.lock') 'taffy'
}
$versions | ConvertTo-Json | Set-Content -Path (Join-Path $Out 'versions.json') -Encoding utf8NoBOM
Write-Host "Versions: $(($versions.GetEnumerator() | ForEach-Object { "$($_.Key) $($_.Value)" }) -join ', ')"
