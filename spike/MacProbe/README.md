# MacProbe: the macOS CI probe (spike S12a)

What a GitHub-hosted macOS runner can answer before a real Mac is available. It is option (b) of Q0 in the S12/P06 grill
report: a NativeAOT console app and a workflow, run by hand, whose only output is log lines. Nobody here has a Mac, so the
code has never run. Everything is written to be read from the logs alone.

The spike is outside `XylemUI.slnx` and `build.ps1`, and references no XylemUI project. It also stays out of the gate's format
check through the root `.csharpierignore`. Beside the project there are an empty `Directory.Build.props`, an empty
`Directory.Build.targets`, and a `Directory.Packages.props` with central package management off, so the repository's
`TreatWarningsAsErrors`, `AnalysisLevel=latest-recommended`, `EnforceCodeStyleInBuild`, `Features=strict`, StyleCop,
`XylemUI.Generators`, and central package versions do not apply (`.cursor/rules/scratch.mdc`).

## Run it

```text
gh workflow run mac-probe.yml --ref spike/s12a-macos-ci-probe
gh run watch                                   # or: gh run list --workflow mac-probe.yml
gh run download <run-id>                       # artifact mac-probe-<runner>-<attempt>: summary.txt and log/
```

The workflow has only the `workflow_dispatch` trigger. GitHub documents (from memory, unverified) that it dispatches a workflow
only if the file is also on the default branch. If `gh workflow run` answers 404 "workflow not found", there are two ways out.
One is to merge `.github/workflows/mac-probe.yml` to `main` alone: it still runs only by hand. The other is to add a temporary
`push: branches: [spike/s12a-macos-ci-probe]` trigger on this branch.

The input `intel_runner` (default `macos-15-intel`) adds an optional x64 job with `continue-on-error`. `macos-13`, the old x64
label, was retired in late 2025 (from memory, unverified). Pass `-f intel_runner=` to skip the x64 job. If the label no longer
exists, its job stays queued: cancel it, and the `macos-latest` job and its artifact are kept.

## Reading the output

- `PROBE <name>: <value>` is one answer. The runner script writes `runner.*`, `native.*`, `accesskit.*`, `publish.*`, `exe.*`,
  `run.*` and `metal_tool.*`. The probe executable writes `<rid>.<group>.<name>`, for example
  `osx-arm64.metal.draw_pass`. Every answer is also in `summary.txt` and in the job summary.
- `STEP <rid>.<group>.<name>` is printed before each step. If a native call crashes the process, this is the last line before
  the crash, and `run.<rid>.<group>` reports the exit code (139 for SIGSEGV, 134 for an abort, which is also what an uncaught
  NSException gives) with that step.
- A step that hangs for more than 30 s (`PROBE_STEP_TIMEOUT`) is reported as `FAILED hung ...` by an in-process watchdog,
  which then ends the process with 124. The script also kills a group's process after 180 s (exit 137).
- Each group runs in its own process (`MacProbe --only <group>`), so a crash in `metal` does not hide `coretext`.
- Before each `objc_msgSend`, the code checks with `class_respondsToSelector`. A wrong selector therefore gives a `FAILED`
  line, not an NSException that would end the process.
- The run step does not fail the job. A step's failure is a `FAILED` line, and every later step still runs.

## What each probe answers

The Q numbers are those of the grill report. The rows S12 and P06 are in `docs/roadmap.md`.

| # | Lines | Question it answers | Report |
| --- | --- | --- | --- |
| 1 | `runner.*`, `publish.*`, `exe.*.{size,file,dylibs,only_system_dylibs,codesign,minos}`, `<rid>.env.*` | Does `dotnet publish -r osx-arm64 -p:PublishAot=true` succeed, and does the cross-publish to `osx-x64` succeed? How big is the executable? Which dylibs does it link, and are they only `/usr/lib` and `/System/Library`? Is it ad-hoc signed by the linker? Also: the macOS, Xcode and SDK versions, the CPU, Rosetta, and whether `Main` runs on the main thread | Q0, Q2, Q5 (definition (a) of a single bundle), Q15 (signing) |
| 2 | `<rid>.objc.*` | Do `objc_getClass`, `sel_registerName` and `objc_msgSend` work through `LibraryImport` with one signature per call shape? This covers `NSAutoreleasePool`, `-[NSProcessInfo operatingSystemVersionString]`, and NSString round trips in UTF-8 and UTF-16. Also: the cost of one `objc_msgSend` from C#, and the managed bytes it allocates (zero expected) | Q3 (hand-written runtime calls; the allocation premise) |
| 3 | `<rid>.metal.*`, `runner.gpu*` | Does `MTLCreateSystemDefaultDevice` return a device, and what are its name, families and `MTLCopyAllDevices` count? Then: a 4x4 BGRA texture cleared and read back; MSL compiled at run time and a triangle drawn and read back; a `CAMetalLayer` made with no window, `setDevice:`, `nextDrawable` (timed), and a render into the drawable | Q0, Q15 (pixel tests on CI: `XylemUI.Testing.Metal`), Q8 (`nextDrawable` with no window) |
| 4 | `<rid>.coretext.*` | Do `CTFontCreateWithName` and the system UI font work, and what are their family and PostScript names? Which font does `CTFontCreateForString` choose for Japanese and for an emoji? `CTFontCreateForStringWithLanguage` (if exported) for 直 under ja, zh-Hans, zh-Hant and ko. Also: the font file URL of each font, and one glyph rasterized into a gray `CGBitmapContext` (ink count and time) | Q10 (ii) font bytes by URL, (iii) language-aware fallback, (i) rasterizer |
| 5 | `<rid>.classreg.*` | Can C# register a class with `objc_allocateClassPair`, `class_addIvar`, `class_addMethod` (an `[UnmanagedCallersOnly]` IMP) and `class_addProtocol(NSTextInputClient)`? Does the method run through `objc_msgSend`, `-performSelector:` and `-performSelectorOnMainThread:...waitUntilDone:YES`, and can it read the ivar? Also: the cost of the round trip and its managed allocations | Q3 (a), Q11 (`NSTextInputClient` view), interop.mdc (an ivar holds a handle id) |
| 6 | `<rid>.blocks.*` | Does clang's own global block have the layout this probe assumes? Are C#-built blocks invoked by clang-compiled code, copied by `Block_copy` (stack to malloc, captured value kept), and run by `dispatch_sync` and `dispatch_async` on a private serial queue? Does a block on `_dispatch_main_q` run while the main thread spins `CFRunLoopRunInMode`? And `dispatch_*_f` with no block | Q4 (C# block literal against a `.m` shim), the shim rule (D3, #441) |
| 7 | `native.*`, `accesskit.*`, `<rid>.staticlink.*`, `<rid>.accesskit.*`, `exe.*.accesskit_growth` | Can a clang static library be linked with `NativeLibrary` + `DirectPInvoke` and called, with no new dylib? And the same for AccessKit's `libaccesskit.a` from the archive pinned in `vendor/versions.lock`: does it link, do its node functions run, and how much does the executable grow (the macOS adapter is kept in the link)? Also: the deployment target (`minos`) of every member of each library | Q5, Issue #761, report §2 fact 14 |
| 8 | `<rid>.display.*` | How many CG displays does the runner have? Does `CVDisplayLinkCreateWithActiveCGDisplays` succeed, and what are its nominal period and its callbacks in 1 s? Are the `CADisplayLink` class and `-[NSView/NSWindow/NSScreen displayLinkWithTarget:selector:]` present (presence only)? Also: `NSScreen` count, `backingScaleFactor` and `maximumFramesPerSecond` | Q7, Q2 (the macOS 14 floor), §6.0 matrix ("macOS 2x fixed" is questioned in §0.2 of the report) |
| 9 | `<rid>.runloop.*` | A `CFRunLoopSource` in common modes on the main run loop, signalled from a worker with `CFRunLoopSourceSignal` + `CFRunLoopWakeUp`: the wakeup latency (median and max of 20), and the idle CPU of one second of `CFRunLoopRunInMode` | Q6 (option (a), the drain's wakeup) |
| - | `metal_tool.*` | Do `xcrun metal` and `metallib` compile a `.metal` file on the runner? | Q9 (build-time `.metallib`) |

What it does not answer, and leaves to the Mac (S12): windows, input, IME, live resize, the display link driving real frames,
present-to-display latency, and anything that needs a visible window or a user session.

## Interop choices to check against the logs

- Every system library is bound lazily by absolute path (`LibraryImport("/System/Library/Frameworks/Metal.framework/Metal")`)
  and loaded up front with `NativeLibrary.Load`, because `objc_getClass` finds a class only once its framework is loaded. As a
  result, `otool -L` lists only what NativeAOT links itself. The `dyld_images` line of each group lists what was loaded at run
  time, and anything outside `/usr/lib` and `/System`.
- `objc_msgSend` has one `LibraryImport` per call shape (`Native.cs`, class `Msg`). No shape returns a struct, so
  `objc_msgSend_stret` (x86_64 only) is never needed. Struct arguments (`MTLClearColor`, `CGSize`, `MTLRegion`) are passed by
  the platform ABI. `BOOL` is read as one byte.
- The assembly has `[DisableRuntimeMarshalling]`. Every signature is blittable, with no `string` marshalling.
- Blocks follow the Clang "Block Implementation Specification" (`clang/docs/Block-ABI-Apple.rst`). The layout is written in
  `BlockProbes.cs` from memory, not fetched. The `clang_layout` line prints the layout clang itself produced on the runner.
- `dispatch_get_main_queue()` is a macro (`&_dispatch_main_q`). The probe reads the symbol with `dlsym`, which is the kind of
  macro the shim rule names.

## Not verified without a run

- Whether any of this compiles: `dotnet` was not run when the spike was written. The first run's `publish.*` lines are the
  compile check.
- That `NativeLibrary.Load` and lazy `LibraryImport` accept framework paths that exist only in the dyld shared cache.
- That NativeAOT cross-publishes `osx-x64` from an arm64 runner (and the reverse), and that the x64 binary runs under
  Rosetta on the runner.
- The system libraries that AccessKit's Rust staticlib needs at link time. `MacProbe.csproj` passes AppKit, Foundation,
  CoreFoundation, `-lobjc` and `-liconv`, from memory. An undefined symbol shows in `publish.<rid>.accesskit`.
- That `actions/setup-dotnet` with `global.json` and `macos-latest` give an SDK that publishes NativeAOT for both RIDs.
- That the AccessKit archive holds `lib/macos/{arm64,x86_64}/static/libaccesskit.a`. This was checked on Windows in the
  archive that `vendor/build.ps1` extracts: 10,046,784 and 10,197,072 bytes. The runner downloads it again and checks the
  SHA-256 from `vendor/versions.lock`.
- The enum values used, all from memory: `MTLPixelFormatBGRA8Unorm` 80, `MTLStorageModeManaged` 1, `MTLLoadActionClear` 2,
  `MTLStoreActionStore` 1, `MTLPrimitiveTypeTriangle` 3, `MTLCommandBufferStatusCompleted` 4, `kCTFontUIFontSystem` 2, and the
  `MTLGPUFamily` values. If one is wrong, a pixel or a status line shows it.
