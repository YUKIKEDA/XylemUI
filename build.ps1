# Local verification gate. Hosted CI (.github/workflows/ci.yml) runs the same steps.
# Usage: ./build.ps1
# Exits non-zero on the first failure.

$ErrorActionPreference = "Stop"
Set-Location $PSScriptRoot

function Invoke-Step([string] $name, [scriptblock] $command) {
    Write-Host "==> $name"
    & $command
    if ($LASTEXITCODE -ne 0) {
        Write-Error "$name failed with exit code $LASTEXITCODE."
        exit $LASTEXITCODE
    }
}

$solution = "XylemUI.slnx"

Invoke-Step "Restore local tools" { dotnet tool restore }
Invoke-Step "Check format (CSharpier)" { dotnet csharpier check . }
Invoke-Step "Restore" { dotnet restore $solution }
Invoke-Step "Build (warnings are errors)" { dotnet build $solution --no-restore -c Release }

# Test projects follow the tests/*.Tests naming (docs/conventions.md).
# Projects for one platform (XylemUI.Platform.Windows.Tests) run on that platform only; the merge gate is Windows.
$testProjects = Get-ChildItem -Path tests -Recurse -Filter *.Tests.csproj -ErrorAction SilentlyContinue
foreach ($project in $testProjects) {
    if ($project.BaseName -like "*.Windows.Tests" -and -not $IsWindows) {
        Write-Host "==> Skip $($project.BaseName) (Windows only)"
        continue
    }

    Invoke-Step "Test $($project.BaseName)" { dotnet test --project $project.FullName --no-build -c Release }
}

Write-Host "build.ps1 completed successfully."
