# Local verification gate. Hosted CI (.github/workflows/ci.yml) runs the same steps.
# Usage: ./build.ps1            every test project (what CI runs)
#        ./build.ps1 -Affected  only the test projects the branch's changes reach (before a push, .cursor/rules/engineering.mdc)
#        ./build.ps1 -Parallel  the test projects of a configuration at the same time (with or without -Affected)
# Exits non-zero on the first failure.

param(
    # Run only the test projects that the changes since origin/main, and those of the working tree, reach.
    [switch] $Affected,

    # Run the test projects of a configuration at the same time, each with its output in artifacts/gate. CI runs them one
    # after the other, which is the reference: a test that fails here is run again alone before it is called a failure.
    [switch] $Parallel
)

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

# A merge that a script resolved once left docs/conventions.md empty, and nothing looked at documents (PR #871, Issue #923).
# The tracked documents, rules, and skills must not be empty and must hold no conflict marker.
Write-Host "==> Check documents (not empty, no conflict marker)"
$documents = git ls-files -- docs .cursor/rules .agents/skills .claude/skills "*.md" | Sort-Object -Unique
$broken = foreach ($document in $documents) {
    if ((Get-Item -LiteralPath $document).Length -eq 0) {
        "$document is empty."
    }
    elseif (Select-String -LiteralPath $document -Pattern '^(<<<<<<<|>>>>>>>) ' -CaseSensitive -Quiet) {
        "$document holds a conflict marker."
    }
}
if ($broken) {
    $broken | ForEach-Object { Write-Host "    $_" }
    Write-Error "Check documents failed: $(@($broken).Count) file(s)."
    exit 1
}
# HarfBuzz from source (vendor/versions.lock); a no-op when they are up to date, and on Linux and macOS until
# P04 / P06. The script throws on a failure, which stops this one.
Write-Host "==> Native libraries (vendor/build.ps1)"
& ./vendor/build.ps1
Invoke-Step "Restore" { dotnet restore $solution }

# Test projects follow the tests/*.Tests naming (docs/conventions.md).
# A project whose target framework is Windows-only (net10.0-windows: the Win32 platform, the D3D11 renderer) runs on Windows
# only; the merge gate is Windows.
$testProjects = Get-ChildItem -Path tests -Recurse -Filter *.Tests.csproj -ErrorAction SilentlyContinue

# The test projects a set of changed files reaches: those with a changed file in their own folder or in the folder of a project
# they reference, directly or through others. A file outside every project's folder (the build's settings, vendor/, this script)
# reaches them all, and so does the source generator, which Directory.Build.props gives every project as an analyzer, not as a
# reference a project file names. Documents, rules, and skills reach none: no test reads them.
function Select-AffectedTests([System.IO.FileInfo[]] $tests, [string[]] $changed) {
    $root = (Resolve-Path $PSScriptRoot).Path
    $projects = Get-ChildItem -Path src, tests, samples, benchmarks -Recurse -Filter *.csproj -ErrorAction SilentlyContinue
    $folderOf = @{}
    foreach ($project in $projects) {
        $folderOf[$project.FullName] = [System.IO.Path]::GetRelativePath($root, $project.DirectoryName).Replace('\', '/') + '/'
    }

    $touched = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
    foreach ($file in $changed) {
        $path = $file.Replace('\', '/')
        if ($path -match '^(docs|\.cursor|\.agents|\.claude|\.github/ISSUE_TEMPLATE)/' -or $path -match '^[^/]+\.md$' -or $path -eq '.github/pull_request_template.md') {
            continue
        }

        $owner = $projects | Where-Object { $path.StartsWith($folderOf[$_.FullName], [System.StringComparison]::OrdinalIgnoreCase) } | Select-Object -First 1
        if ($null -eq $owner -or $owner.BaseName -eq 'XylemUI.Generators') {
            Write-Host "    $path reaches every test project"
            return $tests
        }

        [void] $touched.Add($owner.FullName)
    }

    $references = @{}
    foreach ($project in $projects) {
        $references[$project.FullName] = @(
            Select-String -Path $project.FullName -Pattern '<ProjectReference\s[^>]*Include="([^"]+)"' -AllMatches |
                ForEach-Object { $_.Matches } |
                ForEach-Object { [System.IO.Path]::GetFullPath((Join-Path $project.DirectoryName $_.Groups[1].Value)) }
        )
    }

    $reached = foreach ($test in $tests) {
        $seen = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
        $queue = [System.Collections.Generic.Queue[string]]::new()
        $queue.Enqueue($test.FullName)
        $hit = $false
        while ($queue.Count -gt 0 -and -not $hit) {
            $current = $queue.Dequeue()
            if (-not $seen.Add($current)) {
                continue
            }

            $hit = $touched.Contains($current)
            foreach ($next in @($references[$current])) {
                if ($next) {
                    $queue.Enqueue($next)
                }
            }
        }

        if ($hit) {
            $test
        }
    }

    return @($reached)
}

if ($Affected) {
    Write-Host "==> Test projects the changes reach (-Affected)"
    # The branch's commits since it left origin/main, the working tree's changes, and the files not yet added.
    [string[]] $changed = @(git diff --name-only 'origin/main...HEAD') + @(git diff --name-only HEAD) + @(git ls-files --others --exclude-standard) |
        Where-Object { $_ } |
        Sort-Object -Unique
    if ($LASTEXITCODE -ne 0) {
        Write-Error "git could not list the changes since origin/main; fetch it, or run ./build.ps1 with no switch."
        exit 1
    }

    $testProjects = @(Select-AffectedTests $testProjects $changed)
    Write-Host ("    " + ($testProjects.Count -gt 0 ? (($testProjects | ForEach-Object BaseName) -join ', ') : 'none'))
}

# Release is what ships. Debug runs the checks that only exist there (Invariant.Fail throws instead of ending the process).
foreach ($configuration in "Release", "Debug") {
    Invoke-Step "Build $configuration (warnings are errors)" { dotnet build $solution --no-restore -c $configuration }

    # With -Parallel: what each project's run is given, started together after the loop.
    $runs = @()
    foreach ($project in $testProjects) {
        if (-not $IsWindows -and (Select-String -Path $project.FullName -Pattern '<TargetFramework>[^<]*-windows' -Quiet)) {
            Write-Host "==> Skip $($project.BaseName) (Windows only)"
            continue
        }

        # Each test's name as it ends, so that a run that hangs shows which test never ended (PR #410: the Desktop tests stopped
        # once on the Windows runner until the job's 30-minute limit, and the log named none). xUnit's long-running notices do not
        # reach this output. The text tests, 21,000 and more, stay quiet.
        [string[]]$output = $project.BaseName -eq 'XylemUI.Text.Tests' ? @() : @('--output', 'Detailed')

        # The Desktop tests stopped on the Windows runner until the job's 30-minute limit, four times by PR #426, with no stack
        # (row Q5). A run that passes takes well under a minute; after 5 minutes the hang dump names the tests still running, writes
        # a full dump of the process, and ends the run. The CI uploads the dump. A dump of the heap alone held no metadata of the
        # modules, so dotnet-dump named no method of the stuck thread (PR #432's run, #415).
        if ($project.BaseName -eq 'XylemUI.Desktop.Tests') {
            $output += @('--hangdump', '--hangdump-timeout', '5m', '--hangdump-type', 'Full', '--results-directory', (Join-Path $PSScriptRoot 'artifacts' 'hangdump'))
        }
        if ($Parallel) {
            $runs += , @($project, $output)
            continue
        }

        Invoke-Step "Test $($project.BaseName) ($configuration)" { dotnet test --project $project.FullName --no-build -c $configuration @output }
    }

    if ($runs.Count -gt 0) {
        # All at once: the longest project is the time of the whole (XylemUI.Rendering.D3D11.Tests, three minutes of the five
        # that the ten take one after the other; Issue #822). Each run's output goes to a file, and a failed one's is printed.
        Write-Host "==> Test $($runs.Count) projects at the same time ($configuration)"
        $logs = Join-Path $PSScriptRoot 'artifacts' 'gate'
        [void] (New-Item -ItemType Directory -Force $logs)
        $jobs = foreach ($run in $runs) {
            Start-ThreadJob -ThrottleLimit 16 -ArgumentList $run[0].FullName, $configuration, $run[1], (Join-Path $logs "$($run[0].BaseName).$configuration.txt") -ScriptBlock {
                param($path, $configuration, $output, $log)
                $watch = [System.Diagnostics.Stopwatch]::StartNew()
                dotnet test --project $path --no-build -c $configuration @output *> $log
                [pscustomobject]@{ Name = [System.IO.Path]::GetFileNameWithoutExtension($path); Exit = $LASTEXITCODE; Seconds = [int] $watch.Elapsed.TotalSeconds; Log = $log }
            }
        }

        $failed = @()
        foreach ($result in ($jobs | Receive-Job -Wait -AutoRemoveJob | Sort-Object Name)) {
            Write-Host ("    {0,-32} {1,4}s  {2}" -f $result.Name, $result.Seconds, ($result.Exit -eq 0 ? 'passed' : "failed with exit code $($result.Exit)"))
            if ($result.Exit -ne 0) {
                $failed += $result
            }
        }

        foreach ($result in $failed) {
            Write-Host "==> $($result.Name) ($configuration): the end of $($result.Log)"
            Get-Content $result.Log -Tail 60 | Write-Host
        }

        if ($failed.Count -gt 0) {
            Write-Error "Test $(($failed | ForEach-Object Name) -join ', ') ($configuration) failed."
            exit 1
        }
    }
}

Write-Host "build.ps1 completed successfully."
