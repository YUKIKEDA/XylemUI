# Makes xylemui.xml, Chrome's results for the layout tests in html/ (design §3.2, row L3), with the gentest of Taffy: it opens
# each page in Chrome for Testing through ChromeDriver and writes the boxes' styles and rectangles as Taffy's XML tests. Only the
# border-box, left-to-right variant is kept, as for Fixtures/Taffy. The results in xylemui.xml are from Chrome for Testing
# 155.0.8059.84 (the version gentest downloads, written in the file).
#   ./tests/XylemUI.Core.Tests/Fixtures/XylemUI/Generate.ps1 -Taffy <a checkout of https://github.com/DioxusLabs/taffy> -Work <an empty folder>
# Needs cargo. The work folder takes the copy of gentest, its build, and Chrome (about 500 MB); delete it afterwards.
param(
    [Parameter(Mandatory = $true)][string]$Taffy,
    [Parameter(Mandatory = $true)][string]$Work
)

$ErrorActionPreference = 'Stop'
New-Item -ItemType Directory -Force (Join-Path $Work 'scripts') | Out-Null
foreach ($tool in 'gentest', 'getchrome') {
    Copy-Item -Recurse -Force (Join-Path $Taffy "scripts/$tool") (Join-Path $Work 'scripts')
}

# gentest reads every folder of test_fixtures; only ours is there.
$pages = Join-Path $Work 'test_fixtures/xylemui'
New-Item -ItemType Directory -Force $pages | Out-Null
Copy-Item (Join-Path $PSScriptRoot 'html/*.html') $pages

$gentest = Join-Path $Work 'scripts/gentest'
cargo build --release --manifest-path (Join-Path $gentest 'Cargo.toml')
if ($LASTEXITCODE -ne 0) { throw 'cargo build failed.' }
Push-Location $Work
try {
    $env:CARGO_MANIFEST_DIR = $gentest
    & (Join-Path $gentest 'target/release/gentest')
    if ($LASTEXITCODE -ne 0) { throw 'gentest failed.' }
}
finally {
    Pop-Location
}

$chrome = (Get-ChildItem (Join-Path $Work '.chrome-for-testing') -Directory | Select-Object -First 1).Name
$tests = Get-ChildItem (Join-Path $Work 'tests/xml/xylemui') -Filter '*__border_box_ltr.xml' | Sort-Object Name | ForEach-Object {
    (Get-Content $_.FullName -Raw -Encoding utf8).Trim()
}
$content = "<tests source=`"Chrome for Testing $chrome, through the gentest of Taffy`">`n" + ($tests -join "`n") + "`n</tests>`n"
Set-Content -Path (Join-Path $PSScriptRoot 'xylemui.xml') -Value $content -Encoding utf8NoBOM -NoNewline
Write-Host "xylemui: $($tests.Count) tests from Chrome for Testing $chrome"
