---
name: cpu-profile
description: >-
  Find where the time and the allocations of a XylemUI frame go on Windows: sample a NativeAOT
  Release binary with Visual Studio's collector and xperf, or trace a JIT run with dotnet-trace.
  Use when the user asks for a profile, a hot path, why a frame stage is slow, or where an
  allocation that the Zero-Alloc test reports comes from.
---

# CPU and allocation profile on this machine

The rules this serves: `.cursor/rules/bench.mdc` (profile before a speed row) and `.cursor/rules/open-ends.mdc` (explain a figure before the PR). Write what the profile shows on the Issue.

The agent shell is medium integrity. A kernel logger does not start: do not run `wpr` or `xperf -on`, and do not switch to hand-written timers because a session failed to start.

## Which tool

| Question | Build | Tool |
| --- | --- | --- |
| Which function owns the time in the shipped binary | NativeAOT Release | Visual Studio collector + `xperf` (below) |
| Where the time goes, quickly, while iterating | JIT Release | `dotnet-trace` CPU sampling |
| Who allocates on a path the Zero-Alloc test flagged | JIT Debug or Release | `dotnet-trace` with GC allocation events |
| How often the GC runs and how long it pauses | either | `dotnet-counters` / `dotnet-trace` GC events |

Acceptance numbers come from the NativeAOT binary only. JIT profiles include tiering and are for finding where to look.

If `dotnet-trace` or `dotnet-counters` is not in `.config/dotnet-tools.json`, add it as a local tool in the PR that first needs it; do not install it globally.

## NativeAOT: sample with the Visual Studio collector

Publish the workload (a `benchmarks/E2E` scenario or a sample) so that the native PDB sits next to the executable:

```powershell
dotnet publish benchmarks/E2E -c Release -r win-x64 -o $scratch\publish
```

`VSDiagnostics.exe` is under Visual Studio's Diagnostics Hub collector; `CpuUsageHigh.json` is next to it in `AgentConfigs`. The session id is an integer from 1 to 255.

```powershell
$vs = "C:\Program Files\Microsoft Visual Studio\18\Community\Team Tools\DiagnosticsHub\Collector\VSDiagnostics.exe"
$cfg = "C:\Program Files\Microsoft Visual Studio\18\Community\Team Tools\DiagnosticsHub\Collector\AgentConfigs\CpuUsageHigh.json"
& $vs start 4 "/launch:$exe" "/launchArgs:$args" "/loadConfig:$cfg" "/scratchLocation:$scratch"
```

If the start says the class is not registered, start again with a different session id. That is not the profiler being unavailable. When the process has exited:

```powershell
& $vs stop 4 "/output:$scratch\cpu.diagsession"
& $vs expandDiagSession "$scratch\cpu.diagsession"
```

The sample ETL is `sc.user_aux.etl` under the expanded directory. Decode it with the publish directory on the symbol path:

```powershell
$env:_NT_SYMBOL_PATH = "$scratch\publish"
$env:_NT_SYMCACHE_PATH = "$scratch\symcache"
xperf -i $etl -symbols -o "$scratch\profile.txt" -a profile -detail
xperf -i $etl -symbols -o "$scratch\stacks.txt" -a stack -butterfly 50 -process $procName -pid $pid
```

`-o` comes before `-a`. `profile -detail` is the leaf hit; `stack -butterfly` is the inclusive tree. Read a function's own block, the one that contains `***itself***`.

In a NativeAOT binary, GC and allocation helpers appear as runtime functions (`RhpNewFast`, `RhpGcAlloc`, `WKS::gc_heap::*`). Their inclusive share is the cost of allocation and collection; report it separately from the frame stages. Frames shown as `ntdll.dll!***unknown***` still have an RVA; resolve it with `dumpbin /exports C:\Windows\System32\ntdll.dll`.

## JIT: dotnet-trace

```powershell
dotnet dotnet-trace collect --format Speedscope --output "$scratch\cpu.nettrace" -- $exe $args
```

For allocations, collect the GC allocation-tick events and read the stacks of the types that dominate:

```powershell
dotnet dotnet-trace collect --profile gc-verbose --output "$scratch\alloc.nettrace" -- $exe $args
```

The allocation tick fires about every 100 KB, so it shows who allocates most, not every allocation. To find a single allocation that the Zero-Alloc test reports, bisect the frame instead: wrap each stage in `GC.GetAllocatedBytesForCurrentThread()` in a scratch copy of the test, then read the code of the stage that moved.

## What to report

Inclusive shares of the process samples for the functions that own the time, the allocation and GC share, the sample count and rate, the build (NativeAOT or JIT), and the commit. A leaf list alone hides a caller that spends its time in callees.

Delete the publish directory, traces, and expanded sessions in the same turn (`.cursor/rules/scratch.mdc`).
