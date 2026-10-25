#!/usr/bin/env pwsh
# Builds the native libraries of vendor/versions.lock from source (design §9): a static library for NativeAOT, which links it
# into the executable, and a DLL for the JIT. Windows x64 only for now; Linux and macOS come with roadmap rows P04 and P06.
#
#   ./vendor/build.ps1          build what is missing or out of date
#   ./vendor/build.ps1 -Force   build everything again
#
# Output: vendor/out/win-x64/static/<name>.lib and vendor/out/win-x64/shared/<name>.dll. Nothing here is committed.
param([switch]$Force)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$vendor = $PSScriptRoot
$cache = Join-Path $vendor '.cache'
$work = Join-Path $vendor '.build'
$out = Join-Path $vendor 'out/win-x64'

if (-not $IsWindows) {
    Write-Host '==> Skip native libraries (Windows only until P04 / P06)'
    return
}

# The CMake options of each library: no optional dependency, nothing but the library itself.
$options = @{
    harfbuzz = @('-DHB_BUILD_SUBSET=OFF', '-DHB_BUILD_RASTER=OFF', '-DHB_BUILD_VECTOR=OFF', '-DHB_BUILD_GPU=OFF', '-DHB_BUILD_UTILS=OFF')
}

function Get-Locks {
    Get-Content (Join-Path $vendor 'versions.lock') |
        Where-Object { $_ -notmatch '^\s*(#|$)' } |
        ForEach-Object {
            $name, $version, $sha, $urls = -split $_
            [pscustomobject]@{ Name = $name; Version = $version; Sha256 = $sha.ToLowerInvariant(); Urls = @($urls) }
        }
}

# The MSVC environment for x64, found through vswhere (as the NativeAOT linker does).
function Get-VcVars {
    $installer = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer'
    $vswhere = Join-Path $installer 'vswhere.exe'
    # vcvars64.bat calls vswhere by name (.cursor/rules/engineering.mdc).
    $env:PATH += ";$installer"
    $path = & $vswhere -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -find 'VC/Auxiliary/Build/vcvars64.bat'
    if (-not $path) { throw 'MSVC (the C++ desktop workload) was not found.' }
    return $path
}

function Invoke-InMsvc([string]$vcvars, [string]$command) {
    cmd /c "call `"$vcvars`" >nul && $command"
    if ($LASTEXITCODE -ne 0) { throw "Failed: $command" }
}

function Get-Sha256([string]$path) { (Get-FileHash -Algorithm SHA256 $path).Hash.ToLowerInvariant() }

function Get-Source($lock) {
    New-Item -ItemType Directory -Force $cache | Out-Null
    # Named by library and version, not by URL, so that every URL saves to the same file (tar finds the compression itself).
    $archive = Join-Path $cache "$($lock.Name)-$($lock.Version).source"
    if ((Test-Path $archive) -and (Get-Sha256 $archive) -ne $lock.Sha256) {
        Remove-Item $archive
    }

    if (-not (Test-Path $archive)) {
        # Each URL in turn, checked as soon as it is downloaded: a host that answers with an error page (or a wrong file) does
        # not stop the next URL from being tried.
        foreach ($url in $lock.Urls) {
            Write-Host "==> Download $($lock.Name) $($lock.Version) from $url"
            try {
                Invoke-WebRequest -Uri $url -OutFile "$archive.part"
            }
            catch {
                Write-Host "    failed: $($_.Exception.Message)"
                continue
            }

            $actual = Get-Sha256 "$archive.part"
            if ($actual -eq $lock.Sha256) {
                Move-Item "$archive.part" $archive
                break
            }

            Write-Host "    SHA-256 is $actual, not $($lock.Sha256) (vendor/versions.lock)"
            Remove-Item "$archive.part"
        }

        if (-not (Test-Path $archive)) { throw "No URL of $($lock.Name) in vendor/versions.lock gave the file with its SHA-256." }
    }

    $source = Join-Path $work "src/$($lock.Name)-$($lock.Version)"
    if (-not (Test-Path $source)) {
        New-Item -ItemType Directory -Force (Join-Path $work 'src') | Out-Null
        # Windows's own tar (bsdtar): a GNU tar first on PATH (Git's) reads `C:` in a path as a remote host.
        & (Join-Path $env:SystemRoot 'System32/tar.exe') -xf $archive -C (Join-Path $work 'src')
        if ($LASTEXITCODE -ne 0) { throw "Could not extract $archive." }
    }

    return $source
}

$vcvars = Get-VcVars
$script = (Get-FileHash -Algorithm SHA256 $PSCommandPath).Hash.ToLowerInvariant()
# A line with no CMake options is data for a generator (unicode-ucd, tools/UnicodeTableGen), not a library to build.
foreach ($lock in Get-Locks | Where-Object { $options.ContainsKey($_.Name) }) {
    $stamp = Join-Path $out "$($lock.Name).stamp"
    # The stamp covers the script too: a change to the build options builds again.
    $expected = "$($lock.Version) $($lock.Sha256) $script"
    if (-not $Force -and (Test-Path $stamp) -and (Get-Content $stamp) -eq $expected) {
        Write-Host "==> $($lock.Name) $($lock.Version) is up to date"
        continue
    }

    $source = Get-Source $lock
    foreach ($kind in 'static', 'shared') {
        Write-Host "==> Build $($lock.Name) $($lock.Version) ($kind)"
        $build = Join-Path $work "$($lock.Name)-$kind"
        if (Test-Path $build) { Remove-Item -Recurse -Force $build }
        $shared = if ($kind -eq 'shared') { 'ON' } else { 'OFF' }
        # The static library links into a NativeAOT executable, whose runtime uses the static CRT (/MT): the CRTs must match, or
        # the link fails with LNK2038. The DLL, for the JIT, uses the dynamic CRT (/MD) like any DLL.
        $runtime = if ($kind -eq 'shared') { 'MultiThreadedDLL' } else { 'MultiThreaded' }
        $configure = @(
            "cmake -S `"$source`" -B `"$build`" -G `"NMake Makefiles`" -DCMAKE_BUILD_TYPE=Release",
            "-DBUILD_SHARED_LIBS=$shared -DCMAKE_POLICY_DEFAULT_CMP0091=NEW -DCMAKE_MSVC_RUNTIME_LIBRARY=$runtime",
            ($options[$lock.Name] -join ' ')
        ) -join ' '
        Invoke-InMsvc $vcvars $configure
        Invoke-InMsvc $vcvars "cmake --build `"$build`""

        $target = Join-Path $out $kind
        New-Item -ItemType Directory -Force $target | Out-Null
        $pattern = if ($kind -eq 'shared') { "$($lock.Name).dll" } else { "$($lock.Name).lib" }
        $built = Get-ChildItem -Path $build -Recurse -Filter $pattern | Select-Object -First 1
        if (-not $built) { throw "The build of $($lock.Name) ($kind) made no $pattern." }
        Copy-Item $built.FullName (Join-Path $target $pattern) -Force
    }

    Set-Content -Path $stamp -Value $expected
}
