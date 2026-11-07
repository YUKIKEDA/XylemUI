# Copies Taffy's XML layout tests (Chrome's results, MIT, see LICENSE) into one file per group, for the layout tests of
# XylemUI (design §3.2, roadmap rows L1a and L1b). Only the border-box, left-to-right variants are taken: XylemUI's sizes are
# always border-box, and right-to-left comes with row P03. The grid group comes from the v0.15.1 tag, the version P02f ports
# (design §3.3, "The shape of P02f"); the other groups from the commit their rows ported.
#   ./tests/XylemUI.Core.Tests/Fixtures/Taffy/Import.ps1 -Taffy <a checkout of https://github.com/DioxusLabs/taffy> [-Groups grid]
param(
    [Parameter(Mandatory = $true)][string]$Taffy,
    [string[]]$Groups = @('block', 'blockflex', 'flex', 'leaf', 'grid')
)

$ErrorActionPreference = 'Stop'
$commit = (git -C $Taffy rev-parse --short HEAD).Trim()
foreach ($group in $Groups) {
    $tests = Get-ChildItem (Join-Path $Taffy "tests/xml/$group") -Filter '*__border_box_ltr.xml' | Sort-Object Name | ForEach-Object {
        (Get-Content $_.FullName -Raw -Encoding utf8).Trim()
    }
    $content = "<tests source=`"https://github.com/DioxusLabs/taffy/tree/$commit/tests/xml/$group`">`n" + ($tests -join "`n") + "`n</tests>`n"
    Set-Content -Path (Join-Path $PSScriptRoot "$group.xml") -Value $content -Encoding utf8NoBOM -NoNewline
    # The gate checks the XML's format too: unformatted, every test shows as changed (PR #134).
    dotnet csharpier format (Join-Path $PSScriptRoot "$group.xml")
    if ($LASTEXITCODE -ne 0) { throw 'CSharpier failed.' }
    Write-Host "$group`: $($tests.Count) tests from $commit"
}
