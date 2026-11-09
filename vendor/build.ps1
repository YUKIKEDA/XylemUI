#!/usr/bin/env pwsh
# Builds the native libraries of vendor/versions.lock from source, or takes them from a release archive (design §9): a static
# library for NativeAOT, which links it into the executable, and a DLL for the JIT. Windows x64 only for now; Linux and macOS come
# with roadmap rows P04 and P06.
#
#   ./vendor/build.ps1          build what is missing or out of date
#   ./vendor/build.ps1 -Force   build everything again
#
# Output: vendor/out/win-x64/static/<name>.lib and vendor/out/win-x64/shared/<name>.dll; the build machine's HarfBuzz with its
# subset library in vendor/out/win-x64/tool (the icon build tool of XylemUI.Theme.Fluent, design §5.0.4); the Fluent icon files
# in vendor/out/fluent. Nothing here is committed.
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
    'libjpeg-turbo' = @('-DENABLE_SHARED=OFF', '-DWITH_TURBOJPEG=OFF', '-DWITH_SIMD=ON', '-DWITH_TOOLS=OFF', '-DWITH_TESTS=OFF')
    libwebp = @(
        '-DWEBP_BUILD_ANIM_UTILS=OFF', '-DWEBP_BUILD_CWEBP=OFF', '-DWEBP_BUILD_DWEBP=OFF', '-DWEBP_BUILD_GIF2WEBP=OFF',
        '-DWEBP_BUILD_IMG2WEBP=OFF', '-DWEBP_BUILD_VWEBP=OFF', '-DWEBP_BUILD_WEBPINFO=OFF', '-DWEBP_BUILD_WEBPMUX=OFF',
        '-DWEBP_BUILD_EXTRAS=OFF', '-DWEBP_BUILD_LIBWEBPMUX=OFF'
    )
}

# The image decoders link only into the shim (native/imaging), static with the static CRT, for its library and its DLL alike: the
# static libraries each build installs (design §5.0.4, row IM1).
$intoShim = @{
    'libjpeg-turbo' = @('jpeg-static.lib')
    libwebp = @('libwebp.lib', 'libsharpyuv.lib')
}

# The files of skcms, by their paths under the shim's build folder (design §5.0.4, IM1's Grill, Q28): skcms.cc includes its
# headers as src/..., and each transform file includes its own folder's headers.
$skcms = [ordered]@{
    'skcms-cc'            = 'skcms/skcms.cc'
    'skcms-h'             = 'skcms/skcms.h'
    'skcms-transform-inl' = 'skcms/src/Transform_inl.h'
    'skcms-transform-h'   = 'skcms/src/skcms_Transform.h'
    'skcms-baseline'      = 'skcms/src/skcms_TransformBaseline.cc'
    'skcms-hsw'           = 'skcms/src/skcms_TransformHsw.cc'
    'skcms-skx'           = 'skcms/src/skcms_TransformSkx.cc'
    'skcms-internals'     = 'skcms/src/skcms_internals.h'
    'skcms-public'        = 'skcms/src/skcms_public.h'
    'skia-license'        = 'skcms/LICENSE'
}

# The files taken as they are, by the name each gets in vendor/out/fluent (design §5.0.4).
$files = @{
    'fluent-regular-ttf'  = 'FluentSystemIcons-Regular.ttf'
    'fluent-filled-ttf'   = 'FluentSystemIcons-Filled.ttf'
    'fluent-regular-json' = 'FluentSystemIcons-Regular.json'
    'fluent-filled-json'  = 'FluentSystemIcons-Filled.json'
}

# The libraries taken from a release archive, by their paths in it (design §4.4.1): AccessKit's static library is built with the
# static CRT and names no default library, so it links into a NativeAOT executable as it is (spike S10, Issue #623).
$prebuilt = @{
    accesskit = @{
        static = 'lib/windows/x86_64/msvc/static/accesskit.lib'
        shared = 'lib/windows/x86_64/msvc/shared/accesskit.dll'
    }
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
    $archive = Get-Archive $lock
    $source = Join-Path $work "src/$($lock.Name)-$($lock.Version)"
    if (-not (Test-Path $source)) {
        Expand-Download $archive (Join-Path $work 'src')
    }

    return $source
}

# Windows's own tar (bsdtar), which reads zip archives too: a GNU tar first on PATH (Git's) reads `C:` in a path as a remote host.
function Expand-Download([string]$archive, [string]$into) {
    New-Item -ItemType Directory -Force $into | Out-Null
    & (Join-Path $env:SystemRoot 'System32/tar.exe') -xf $archive -C $into
    if ($LASTEXITCODE -ne 0) { throw "Could not extract $archive." }
}

# The archive of a line, downloaded once and checked against its SHA-256.
function Get-Archive($lock) {
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
                # GitHub's API gives a release asset's bytes only when asked for them; without the header it answers with JSON.
                $headers = if ($url -like 'https://api.github.com/repos/*/releases/assets/*') { @{ Accept = 'application/octet-stream' } } else { @{} }
                Invoke-WebRequest -Uri $url -OutFile "$archive.part" -Headers $headers
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

    return $archive
}

$script = (Get-FileHash -Algorithm SHA256 $PSCommandPath).Hash.ToLowerInvariant()
foreach ($lock in Get-Locks | Where-Object { $prebuilt.ContainsKey($_.Name) }) {
    $stamp = Join-Path $out "$($lock.Name).stamp"
    $expected = "$($lock.Version) $($lock.Sha256) $script"
    if (-not $Force -and (Test-Path $stamp) -and (Get-Content $stamp) -eq $expected) {
        Write-Host "==> $($lock.Name) $($lock.Version) is up to date"
        continue
    }

    # Extracted into a folder of its own, which then holds the archive's one folder (accesskit-c-0.23.1).
    $source = Join-Path $work "prebuilt/$($lock.Name)-$($lock.Version)"
    if (-not (Test-Path $source)) {
        Expand-Download (Get-Archive $lock) $source
    }

    $root = Get-ChildItem -Path $source -Directory | Select-Object -First 1
    if (-not $root) { throw "The archive of $($lock.Name) holds no folder." }
    foreach ($kind in 'static', 'shared') {
        $file = Join-Path $root.FullName $prebuilt[$lock.Name][$kind]
        if (-not (Test-Path $file)) { throw "The archive of $($lock.Name) has no $($prebuilt[$lock.Name][$kind])." }
        $target = Join-Path $out $kind
        New-Item -ItemType Directory -Force $target | Out-Null
        Write-Host "==> Take $($lock.Name) $($lock.Version) ($kind)"
        Copy-Item $file (Join-Path $target (Split-Path $file -Leaf)) -Force
    }

    Set-Content -Path $stamp -Value $expected
}

foreach ($lock in Get-Locks | Where-Object { $files.ContainsKey($_.Name) }) {
    $target = Join-Path $vendor 'out/fluent'
    $file = Join-Path $target $files[$lock.Name]
    if (-not $Force -and (Test-Path $file) -and (Get-Sha256 $file) -eq $lock.Sha256) {
        Write-Host "==> $($lock.Name) $($lock.Version) is up to date"
        continue
    }

    New-Item -ItemType Directory -Force $target | Out-Null
    Write-Host "==> Take $($lock.Name) $($lock.Version)"
    Copy-Item (Get-Archive $lock) $file -Force
}

# NASM, which libjpeg-turbo's SIMD is assembled with: taken onto this process's PATH, never shipped (design §5.0.4).
foreach ($lock in Get-Locks | Where-Object { $_.Name -eq 'nasm' }) {
    $tools = Join-Path $work 'tools'
    $nasm = Join-Path $tools "nasm-$($lock.Version)"
    if (-not (Test-Path (Join-Path $nasm 'nasm.exe'))) {
        Expand-Download (Get-Archive $lock) $tools
    }

    $env:PATH = "$nasm;$env:PATH"
}

$vcvars = Get-VcVars
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
    # HarfBuzz also builds a third time for the build machine, with its subset library, which the icon build tool of
    # XylemUI.Theme.Fluent loads; the libraries an app links keep the subset out (design §5.0.4).
    $kinds = if ($lock.Name -eq 'harfbuzz') { 'static', 'shared', 'tool' } elseif ($intoShim.ContainsKey($lock.Name)) { @('static') } else { 'static', 'shared' }
    foreach ($kind in $kinds) {
        Write-Host "==> Build $($lock.Name) $($lock.Version) ($kind)"
        $build = Join-Path $work "$($lock.Name)-$kind"
        if (Test-Path $build) { Remove-Item -Recurse -Force $build }
        $shared = if ($kind -ne 'static') { 'ON' } else { 'OFF' }
        # The static library links into a NativeAOT executable, whose runtime uses the static CRT (/MT): the CRTs must match, or
        # the link fails with LNK2038. The DLL, for the JIT, uses the dynamic CRT (/MD) like any DLL.
        $runtime = if ($kind -ne 'static') { 'MultiThreadedDLL' } else { 'MultiThreaded' }
        $kindOptions = if ($kind -eq 'tool') { $options[$lock.Name] -replace '^-DHB_BUILD_SUBSET=OFF$', '-DHB_BUILD_SUBSET=ON' } else { $options[$lock.Name] }
        $configure = @(
            "cmake -S `"$source`" -B `"$build`" -G `"NMake Makefiles`" -DCMAKE_BUILD_TYPE=Release",
            "-DBUILD_SHARED_LIBS=$shared -DCMAKE_POLICY_DEFAULT_CMP0091=NEW -DCMAKE_MSVC_RUNTIME_LIBRARY=$runtime",
            ($kindOptions -join ' ')
        ) -join ' '
        Invoke-InMsvc $vcvars $configure
        Invoke-InMsvc $vcvars "cmake --build `"$build`""

        if ($intoShim.ContainsKey($lock.Name)) {
            Invoke-InMsvc $vcvars "cmake --install `"$build`" --prefix `"$(Join-Path $work "install/$($lock.Name)")`""
            continue
        }

        $target = Join-Path $out $kind
        New-Item -ItemType Directory -Force $target | Out-Null
        $patterns = switch ($kind) {
            'static' { @("$($lock.Name).lib") }
            'shared' { @("$($lock.Name).dll") }
            'tool' { @("$($lock.Name).dll", "$($lock.Name)-subset.dll") }
        }
        foreach ($pattern in $patterns) {
            $built = Get-ChildItem -Path $build -Recurse -Filter $pattern | Select-Object -First 1
            if (-not $built) { throw "The build of $($lock.Name) ($kind) made no $pattern." }
            Copy-Item $built.FullName (Join-Path $target $pattern) -Force
        }
    }

    Set-Content -Path $stamp -Value $expected
}

# The shim of the image decoders (design §5.0.4): native/imaging/xylemui_image.c with the one C file of Wuffs, and skcms as C++
# compiled with clang-cl (its transforms for AVX2 and AVX-512 built with those instruction sets; skcms picks one at run time; under
# MSVC skcms has no SIMD, IM1's Grill, Q31), compiled with the static
# CRT into xylemui_image.lib, which NativeAOT links with libjpeg-turbo's and libwebp's static libraries, and into
# xylemui_image.dll, which links them in, for the JIT. Its stamp covers the source, the libraries' pins, and this script.
$shimSource = Join-Path $vendor '../native/imaging/xylemui_image.c'
$locks = @{}
foreach ($lock in Get-Locks) { $locks[$lock.Name] = $lock }
$stamp = Join-Path $out 'xylemui_image.stamp'
$expected = ((@('wuffs', 'libjpeg-turbo', 'libwebp', 'llvm') + @($skcms.Keys) | ForEach-Object { $locks[$_].Sha256 }) + @((Get-Sha256 $shimSource), $script)) -join ' '
if (-not $Force -and (Test-Path $stamp) -and (Get-Content $stamp) -eq $expected) {
    Write-Host '==> xylemui_image is up to date'
}
else {
    Write-Host '==> Build xylemui_image'
    $build = Join-Path $work 'xylemui_image'
    if (Test-Path $build) { Remove-Item -Recurse -Force $build }
    New-Item -ItemType Directory -Force $build | Out-Null
    Copy-Item (Get-Archive $locks['wuffs']) (Join-Path $build 'wuffs-v0.4.c')
    foreach ($name in $skcms.Keys) {
        $path = Join-Path $build $skcms[$name]
        New-Item -ItemType Directory -Force (Split-Path $path) | Out-Null
        Copy-Item (Get-Archive $locks[$name]) $path
    }

    $includes = @('libjpeg-turbo', 'libwebp') | ForEach-Object { "/I`"$(Join-Path $work "install/$_/include")`"" }
    $libraries = foreach ($name in $intoShim.Keys) { foreach ($file in $intoShim[$name]) { "`"$(Join-Path $work "install/$name/lib/$file")`"" } }
    $obj = Join-Path $build 'xylemui_image.obj'
    $flags = "/nologo /O2 /Gy /MT /DNDEBUG /W3 /I`"$build`" $($includes -join ' ')"
    Invoke-InMsvc $vcvars "cl $flags /c `"$shimSource`" /Fo`"$obj`""
    # clang-cl from LLVM's release, for skcms alone: the archive's clang-cl and the compiler's own headers (the intrinsics), taken
    # out once into the build folder. Windows's tar reads the xz archive; its zstd one needs more memory than tar allows.
    $llvm = Join-Path $work "tools/llvm-$($locks['llvm'].Version)"
    $clang = Join-Path $llvm "clang+llvm-$($locks['llvm'].Version)-x86_64-pc-windows-msvc/bin/clang-cl.exe"
    if (-not (Test-Path $clang)) {
        New-Item -ItemType Directory -Force $llvm | Out-Null
        $root = "clang+llvm-$($locks['llvm'].Version)-x86_64-pc-windows-msvc"
        & (Join-Path $env:SystemRoot 'System32/tar.exe') -xf (Get-Archive $locks['llvm']) -C $llvm "$root/bin/clang-cl.exe" "$root/lib/clang"
        if ($LASTEXITCODE -ne 0) { throw 'Could not take clang-cl out of the LLVM archive.' }
    }

    $objects = @("`"$obj`"")
    foreach ($unit in @(@('skcms/skcms.cc', ''), @('skcms/src/skcms_TransformBaseline.cc', ''), @('skcms/src/skcms_TransformHsw.cc', '/arch:AVX2'), @('skcms/src/skcms_TransformSkx.cc', '/arch:AVX512'))) {
        $unitObj = Join-Path $build ([IO.Path]::GetFileNameWithoutExtension($unit[0]) + '.obj')
        Invoke-InMsvc $vcvars "`"$clang`" /nologo /O2 /Gy /MT /DNDEBUG /std:c++17 /EHsc $($unit[1]) /c `"$(Join-Path $build $unit[0])`" /Fo`"$unitObj`""
        $objects += "`"$unitObj`""
    }

    Invoke-InMsvc $vcvars "lib /nologo /OUT:`"$(Join-Path $build 'xylemui_image.lib')`" $($objects -join ' ')"
    Invoke-InMsvc $vcvars "link /nologo /DLL /OPT:REF /OPT:ICF /OUT:`"$(Join-Path $build 'xylemui_image.dll')`" /IMPLIB:`"$(Join-Path $build 'xylemui_image_dll.lib')`" $($objects -join ' ') $($libraries -join ' ')"
    foreach ($kind in 'static', 'shared') {
        New-Item -ItemType Directory -Force (Join-Path $out $kind) | Out-Null
    }

    Copy-Item (Join-Path $build 'xylemui_image.lib') (Join-Path $out 'static/xylemui_image.lib') -Force
    Copy-Item (Join-Path $build 'xylemui_image.dll') (Join-Path $out 'shared/xylemui_image.dll') -Force
    Copy-Item (Join-Path $build 'skcms/LICENSE') (Join-Path $out 'skcms.LICENSE') -Force
    foreach ($name in $intoShim.Keys) {
        foreach ($file in $intoShim[$name]) {
            Copy-Item (Join-Path $work "install/$name/lib/$file") (Join-Path $out "static/$file") -Force
        }
    }

    Set-Content -Path $stamp -Value $expected
}
