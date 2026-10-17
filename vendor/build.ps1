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
    freetype = @('-DFT_DISABLE_ZLIB=TRUE', '-DFT_DISABLE_BZIP2=TRUE', '-DFT_DISABLE_PNG=TRUE', '-DFT_DISABLE_HARFBUZZ=TRUE', '-DFT_DISABLE_BROTLI=TRUE')
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

function Get-Source($lock) {
    New-Item -ItemType Directory -Force $cache | Out-Null
    $archive = Join-Path $cache ([IO.Path]::GetFileName($lock.Urls[0]))
    if (-not (Test-Path $archive)) {
        # The first URL that answers; the SHA-256 below checks whichever served it.
        foreach ($url in $lock.Urls) {
            Write-Host "==> Download $($lock.Name) $($lock.Version) from $url"
            try {
                Invoke-WebRequest -Uri $url -OutFile "$archive.part"
                Move-Item "$archive.part" $archive
                break
            }
            catch {
                Write-Host "    failed: $($_.Exception.Message)"
            }
        }

        if (-not (Test-Path $archive)) { throw "No URL of $($lock.Name) in vendor/versions.lock answered." }
    }

    $actual = (Get-FileHash -Algorithm SHA256 $archive).Hash.ToLowerInvariant()
    if ($actual -ne $lock.Sha256) {
        Remove-Item $archive
        throw "SHA-256 of $archive is $actual, not $($lock.Sha256) (vendor/versions.lock)."
    }

    $source = Join-Path $work "src/$($lock.Name)-$($lock.Version)"
    if (-not (Test-Path $source)) {
        New-Item -ItemType Directory -Force (Join-Path $work 'src') | Out-Null
        tar -xf $archive -C (Join-Path $work 'src')
        if ($LASTEXITCODE -ne 0) { throw "Could not extract $archive." }
    }

    return $source
}

$vcvars = Get-VcVars
$script = (Get-FileHash -Algorithm SHA256 $PSCommandPath).Hash.ToLowerInvariant()
foreach ($lock in Get-Locks) {
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
