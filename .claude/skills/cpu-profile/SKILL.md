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
| Whether two builds differ in instructions, cycles, or stalls | NativeAOT Release | Intel VTune hardware events (below) |

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

`frames N` presents with the vertical sync, so a run lasts N / 60 seconds: in R4c `frames 20000 --curves` took about 5.5 minutes for each build. Choose N for the length of run the samples need.

### Comparing two builds

To find where a build is slower than main, profile both the same way and compare the leaf weights function by function: sum the weight of each `XylemUI.E2E.exe!` line of `profile -detail` by name, join the two lists, and sort by the difference. A function that exists in one build only is code that was inlined in the other (R4c: `Faded<NoGroups>` and `WriteGlyphRun` showed up as functions of their own; `Memmove` grew under a copy of a command). Then compare the functions' instructions: `dumpbin /disasm` of each published executable, the lines of one function with the addresses taken out, `diff`ed. The count tells whether the code grew, and the diff shows what grew (in R4c, four more XMM registers saved in `WriteCurve`). See `.cursor/rules/bench.mdc` for when a remaining difference may be called code placement.

## NativeAOT: count hardware events with VTune

`benchmarks/counters/instructions.ps1` counts the instructions and cycles of one relayout for pairs of builds; `-Events` adds cache, branch, and memory events, and `summarize.py` reads the reports (`docs/benchmarks/l8-counters.md`). `-Command input-loop -Scenes wheel,move` counts one pointer input instead (`benchmarks/XylemUI.E2E/InputLoop.cs`, `docs/benchmarks/i1a-touch-pen.md`).

To see which function a difference sits in, report a result by function: `vtune -report hw-events -group-by function -r <result> -format csv -csv-delimiter comma -report-output <file>.csv`. Read only a large difference, such as a function one build calls and the other does not: the samples move between functions from run to run. In I1a this showed `SpanHelpers.Memmove` at about 290 instructions a wheel, which main did not call (PR #389).

- The hardware events need the VTune sampling driver, which the medium-integrity agent shell cannot use ("Run the product as administrator"). The owner approves the UAC prompt, and only while present: send the elevated run only when the owner says it can be approved now.
- VTune's raw result is about 100 MB per run; the runs of L8 reached 20 GB. Keep the CSV of `vtune -report hw-events` and delete the result directories in the same turn.
- Do not run another measurement on the same core while VTune runs. In L8 a cycle run on the pinned core distorted both.
- An event that the counters cannot schedule with the others is left out of the report without an error (`TOPDOWN.SLOTS` in L8). Check the columns of the CSV against `-Events`.
- Do not point xperf or VTune at the Microsoft symbol server: in L8 it downloaded 4.3 GB and did not end. The native PDB next to the executable is enough.

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
