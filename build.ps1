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
# HarfBuzz from source (vendor/versions.lock); a no-op when they are up to date, and on Linux and macOS until
# P04 / P06. The script throws on a failure, which stops this one.
Write-Host "==> Native libraries (vendor/build.ps1)"
& ./vendor/build.ps1
Invoke-Step "Restore" { dotnet restore $solution }

# Test projects follow the tests/*.Tests naming (docs/conventions.md).
# A project whose target framework is Windows-only (net10.0-windows: the Win32 platform, the D3D11 renderer) runs on Windows
# only; the merge gate is Windows.
$testProjects = Get-ChildItem -Path tests -Recurse -Filter *.Tests.csproj -ErrorAction SilentlyContinue

# Release is what ships. Debug runs the checks that only exist there (Invariant.Fail throws instead of ending the process).
foreach ($configuration in "Release", "Debug") {
    Invoke-Step "Build $configuration (warnings are errors)" { dotnet build $solution --no-restore -c $configuration }

    foreach ($project in $testProjects) {
        if (-not $IsWindows -and (Select-String -Path $project.FullName -Pattern '<TargetFramework>[^<]*-windows' -Quiet)) {
            Write-Host "==> Skip $($project.BaseName) (Windows only)"
            continue
        }

        # Each test's name as it ends, so that a run that hangs shows which test never ended (PR #410: the Desktop tests stopped
        # once on the Windows runner until the job's 30-minute limit, and the log named none). xUnit's long-running notices do not
        # reach this output. The text tests, 21,000 and more, stay quiet.
        [string[]]$output = $project.BaseName -eq 'XylemUI.Text.Tests' ? @() : @('--output', 'Detailed')
        Invoke-Step "Test $($project.BaseName) ($configuration)" { dotnet test --project $project.FullName --no-build -c $configuration @output }
    }
}

Write-Host "build.ps1 completed successfully."
