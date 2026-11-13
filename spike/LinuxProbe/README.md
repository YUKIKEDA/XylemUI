# Linux probe (spike)

A throwaway measurement for rows P04 and P05. It answers question 2 of the Grill preparation in `.dev/grill/P04-P05.md` on
GitHub's Ubuntu runners. The numbers feed questions 3 to 10, 19, and 20 of that report. Nothing here is framework code. The probe
is not in `XylemUI.slnx`, and `./build.ps1` does not build it.

## How to run it

```sh
gh workflow run linux-probe.yml --ref spike/lx-linux-probe
gh run list --workflow linux-probe.yml --limit 1
gh run download <run id>      # linux-probe-x64-<attempt>/ and linux-probe-arm64-<attempt>/
```

The workflow runs only on `workflow_dispatch`. It has two jobs:

- `probe (linux-x64, ubuntu-latest)`.
- `probe (linux-arm64, ubuntu-24.04-arm)`. This job has `continue-on-error: true`, because the arm64 label may not exist for the
  repository.

Each job uploads one artifact. It holds:

- `summary.txt`: every `PROBE <name>: <value>` line.
- `full-log.txt`.
- The raw files behind the lines: the publish logs, `readelf`/`ldd`/`objdump` output, `vulkaninfo`, `wayland-info`, and the
  weston, sway, and Xvfb logs.

The summary also goes to the job's summary page. A failed step writes `PROBE <name>: FAILED <reason>`, and the script goes on. The
job therefore passes whenever the script ran to its end. Read the summary, not the job's colour.

## What each probe answers

| Probe lines | Asks | Report question |
| --- | --- | --- |
| `runner.*` | OS, glibc, compilers, and what is installed before the probe installs anything | Q19, Q20 |
| `aot.base.*` | NativeAOT `linux-x64`/`linux-arm64` publish of a console app: size, `.dbg` size, `DT_NEEDED`, `ldd`, the newest `GLIBC_` symbol version it needs, IL warnings | Q2.1, Q19 |
| `aot.wgpu.*` | The same app with `libwgpu_native.a` linked (`NativeLibrary` + `DirectPInvoke`): publish, size growth, new `DT_NEEDED`, the libraries on the link line | Q2.1, Q5, Q19 |
| `wgpu.bare.*` | wgpu adapters on the runner before Mesa is installed | Q20 |
| `wgpu.lavapipe.*`, `wgpu.vulkan_only.*`, `wgpu.gl_only.*` | Every adapter, with its backend, type, and dual-source blending. Then the default and the fallback adapter, device creation, a 64×64 RGBA8 clear through a render pass, the copy to a buffer, and the read-back of one pixel. Times for each step, for the first frame, and for 20 more frames. RSS (`VmRSS`, `RssAnon`, `RssFile`, `Threads`) before and after each step | Q2.2, Q5, Q6, Q20, design §12.3 (lavapipe), §5.3 (memory), row P04 (LCD AA needs dual-source blending) |
| `vulkan.*` | Vulkan ICDs and `vulkaninfo --summary` after `mesa-vulkan-drivers` | Q20 |
| `fontconfig.*` | `FcInitLoadConfigAndFonts` time and memory; `FcFontMatch` of `sans`, `sans-serif:lang=ja`, `:lang=ja`, `Noto Sans CJK JP` with family, style, file, coverage of U+3042 and U+6F22; the time of a repeated match | Q18, design §4.5 |
| `display.preexisting.*`, `wayland.none.*`, `x11.none.*` | Whether the runner has a compositor or an X server of its own | Q3, Q20 |
| `weston.*`, `wayland.weston.*` | `weston --backend=headless`: `wl_display_connect(NULL)` and a roundtrip from the NativeAOT process; the globals weston advertises | Q3, Q10, Q8, Q20 |
| `sway.*`, `wayland.sway.*` | The same under headless sway. weston has no text-input-v3, so a "no" from weston alone would say nothing about the protocol | Q10, Q8, Q20 |
| `xvfb.*`, `x11.xvfb.*` | `XOpenDisplay(NULL)` and `xcb_connect(NULL)` against Xvfb | Q3b, Q20 |
| `dbus.runner.*`, `dbus.inner.*`, `env.*` | Whether the runner has a session bus; under `dbus-run-session`, whether the session bus answers, whether `org.a11y.Bus.GetAddress` returns the AT-SPI bus (at-spi2-core), and whether that bus answers `ListNames` | Q11, Q12, Q20 |
| `wayland.<compositor>.has.*` | `zwp_text_input_manager_v3`, `wp_fractional_scale_manager_v1`, and the other protocols Q3 lists (text-input-v1, input-method-v2, viewporter, presentation, xdg-decoration, primary selection, cursor-shape, xdg-activation, dmabuf) | Q8, Q10, Q7 |

The probe does not measure the parts of Q2 that need a real window and a desktop: Present blocking (Q2.4), the IME matrix (Q2.5),
Present-to-photon, and memory on hardware Vulkan (Q2.2 says hardware *and* lavapipe; this probe gives lavapipe only).

## wgpu-native pin

| | |
| --- | --- |
| Tag | `v29.0.1.1`, released 2026-06-23. It was the newest release when the spike was written (listed with `GET /repos/gfx-rs/wgpu-native/releases`) |
| x86_64 | `https://github.com/gfx-rs/wgpu-native/releases/download/v29.0.1.1/wgpu-linux-x86_64-release.zip`, SHA-256 `95a4d90c071005a98d03eab348beaa6b07e16eb00d1dcdb9f8348f75eb97ec5a`. Mirror: `https://api.github.com/repos/gfx-rs/wgpu-native/releases/assets/455571719` |
| aarch64 | `https://github.com/gfx-rs/wgpu-native/releases/download/v29.0.1.1/wgpu-linux-aarch64-release.zip`, SHA-256 `015fcdf1dbae82e614a783cc38017e5399ae0927a889fe9b69c9b664bc61b47a`. Mirror: `https://api.github.com/repos/gfx-rs/wgpu-native/releases/assets/455571722` |
| Contents (x86_64) | `include/webgpu/webgpu.h`, `include/webgpu/wgpu.h`, `lib/libwgpu_native.a` (51,963,136 bytes), `lib/libwgpu_native.so` (9,330,952 bytes), `wgpu-native-meta/` |

The workflow pins the tag in `WGPU_NATIVE_TAG` and the hash in each job's `WGPU_NATIVE_SHA256`. The script refuses an archive
whose hash differs.

## How it is built

- **The wgpu calls are in C** (`native/wgpu_probe.c`), one function per step. C# calls that shim through `LibraryImport`. Only
  integers, doubles, and pointers to C#'s own buffers cross the boundary. Why: the render path needs about fifteen webgpu.h structs.
  They hold string views, chained structs, callback-info structs passed by value, and 64-bit flags. Hand-written C# layouts of
  these could not be checked here, because nothing compiles for Linux on this machine. gcc on the runner checks every field
  against the pinned header. A wrong layout would be a silent memory corruption in C#; in C it is a compile error in the log. The
  design's rule (shim only where P/Invoke cannot express the API, row P06) is for the framework, not for this probe.
- One function goes from C# straight into the archive: `wgpuGetVersion`, with `DirectPInvoke Include="wgpu_native"`. It shows
  that a direct import into wgpu-native's archive links too.
- `ci/run-probe.sh` compiles the shim with gcc and adds `wgpu_probe.o` to a copy of `libwgpu_native.a` (`libxylem_probe.a`). With
  one archive, the link does not depend on the order in which NativeAOT passes `NativeLibrary` items. The size growth includes the
  shim, whose code was about 6.3 KB when compiled with gcc -O2 in WSL.
- System libraries for the link (`LinuxProbe.csproj`): `-lm -lgcc_s -ldl -lpthread`. A C program linked against the same archive
  with gcc in WSL (Ubuntu 22.04, glibc 2.35) needed `libm.so.6`, `libgcc_s.so.1`, `libc.so.6`, and `ld-linux-x86-64.so.2`, and
  `GLIBC_2.35` at most. If the first NativeAOT link fails on `multiple definition`, the script publishes again with
  `-Wl,--allow-multiple-definition` and records that it had to.
- libwayland-client, libX11, libxcb, libfontconfig, and libc's `gnu_get_libc_version` and `__errno_location` are opened with
  `dlopen` (`NativeLibrary.TryLoad`). The executable's `DT_NEEDED` therefore shows only what NativeAOT and wgpu-native need.
- Each section runs in its own process (`LinuxProbe <section> <label>`), with a timeout. A crash in native code loses one section,
  and the log records the exit code and the signal.
- The repository's build settings are off for this folder. The local `Directory.Build.props` and `Directory.Build.targets` are
  empty, and the local `Directory.Packages.props` sets `ManagePackageVersionsCentrally=false`. This drops warnings as errors,
  `AnalysisLevel=latest-recommended`, `EnforceCodeStyleInBuild`, StyleCop, the BannedApiAnalyzers, and the `XylemUI.Generators`
  analyzer reference. `.editorconfig` still applies, but its diagnostics stay warnings. `AllowUnsafeBlocks` is on for the
  function pointers and the shim's buffers.

## What was verified before the first run, and what was not

Verified on the author's machine:

- `native/wgpu_probe.c` compiles without warnings with gcc 11.4 (`-std=gnu17 -Wall -Wextra`) against the pinned x86_64 header.
- The shim was linked statically with `libwgpu_native.a` into a C test program, and that program ran in WSL. WSL has no
  lavapipe, so its only adapter was OpenGL through D3D12 on the host's GPU. On that adapter, every step ran: instance,
  enumeration, adapter, device, targets, clear, copy, map. The pixel read back as (64, 127, 191, 255) for an expected
  (64, 128, 191, 255), and 0 pixels were off by more than 1. The fallback request failed there with "No suitable graphics
  adapter found", which is expected without a CPU adapter.
- `ci/run-probe.sh` passes `bash -n`.

Not verified:

- **The C# code was never compiled** (no `dotnet` was run). Expect a compile error or two in the first run.
- **CSharpier was not run** on the C# files and the `.csproj`. `./build.ps1` runs `dotnet csharpier check .` on the whole
  repository, this folder included. A pull request from this branch could fail the gate on formatting until
  `dotnet csharpier format spike/LinuxProbe` is run. A push to this branch alone does not start `ci.yml`, whose `push` trigger
  is `main` only.
- The NativeAOT link with the Rust archive: duplicate symbols (unwinder, compiler builtins) or a missing system library.
- The weston, sway, and Xvfb command lines on the runner's versions: weston's `--renderer=pixman` (with a retry without it), and
  sway's `WLR_RENDERER=pixman` with the headless backend.
- The `wayland-info` output format that the script greps (`interface: 'name', version: N`).
- Whether `org.a11y.Bus` starts by D-Bus activation without a display.
- Whether the `ubuntu-24.04-arm` label is available to this repository.
- Whether `wgpuInstanceEnumerateAdapters` lists a GL adapter without a display (EGL surfaceless).
