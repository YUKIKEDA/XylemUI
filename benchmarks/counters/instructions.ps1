# Counts the instructions and cycles that builds of XylemUI.E2E spend on one relayout (roadmap row L8), with Intel VTune's
# hardware events: INST_RETIRED.ANY and CPU_CLK_UNHALTED.THREAD, sampled every 100,003 events. As cycles.ps1 does, each build
# runs `layout-loop <scene> <kind> <n>` at two counts, so that the difference without the start and the build of the tree can
# be divided by the difference of the counts. The builds run in the order A, B, B, A for each pair of builds.
#
# The hardware events need the VTune sampling driver, which a shell of medium integrity cannot use ("Run the product as
# administrator"): run this once from a PowerShell started as administrator. It writes VTune's results and one CSV report
# per run under <out>; reading them is left to the agent (summarize.py next to this file).
#   ./benchmarks/counters/instructions.ps1 -Builds <dir> -Pairs 'old:new','old2:new2' -Out <dir> [-Rounds 2]
# With -Command input-loop, each scene is an input (wheel, move) that runs `input-loop <scene> <n>` instead (row I1a).
# <dir>/<name>/XylemUI.E2E.exe is the build named <name>.
param(
    [Parameter(Mandatory = $true)][string]$Builds,
    [Parameter(Mandatory = $true)][string[]]$Pairs,
    [Parameter(Mandatory = $true)][string]$Out,
    [string[]]$Scenes = @('s6b', 'auto_min', 'percent'),
    # change: one leaf's width; fixed_row: the same in a tree whose rows have a fixed size.
    [string]$Kind = 'change',
    # layout-loop, or input-loop: then each scene is an input of InputLoop.cs, and Kind only names the run.
    [string]$Command = 'layout-loop',
    [int]$Low = 20000,
    [int]$High = 120000,
    [int]$Rounds = 2,
    # The hardware events, each sampled every 100,003: instructions and cycles by default; cache and branch misses can be added.
    [string[]]$Events = @('INST_RETIRED.ANY', 'CPU_CLK_UNHALTED.THREAD'),
    # The logical processor each run is pinned to at high priority (cmd's start /affinity), or -1 for none. This CPU mixes
    # performance and efficiency cores, whose caches and branch predictors differ: logical 2 is a performance core.
    [int]$Processor = 2,
    [string]$VTune = 'C:\Program Files (x86)\Intel\oneAPI\vtune\latest\bin64\vtune.exe'
)

$ErrorActionPreference = 'Stop'
$principal = [Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'Run this from a PowerShell started as administrator: VTune needs it for the hardware events.'
}

New-Item -ItemType Directory -Force $Out | Out-Null
$eventConfig = ($Events | ForEach-Object { "${_}:sa=100003" }) -join ','
$index = 0
foreach ($pair in $Pairs) {
    $a, $b = $pair.Split(':')
    for ($round = 1; $round -le $Rounds; $round++) {
        foreach ($scene in $Scenes) {
            foreach ($name in $a, $b, $b, $a) {
                foreach ($count in $Low, $High) {
                    $index++
                    $tag = '{0:D3}-{1}-{2}-{3}-r{4}' -f $index, $name, "$scene.$Kind", $count, $round
                    $result = Join-Path $Out $tag
                    $exe = Join-Path $Builds "$name\XylemUI.E2E.exe"
                    $arguments = if ($Command -eq 'layout-loop') { @('layout-loop', $scene, $Kind, $count) } else { @($Command, $scene, $count) }
                    if ($Processor -ge 0) {
                        $mask = '{0:X}' -f (1L -shl $Processor)
                        & $VTune -collect-with runsa -knob "event-config=$eventConfig" -r $result -- cmd.exe /c start '""' /affinity $mask /high /wait $exe @arguments | Out-Null
                    }
                    else {
                        & $VTune -collect-with runsa -knob "event-config=$eventConfig" -r $result -- $exe @arguments | Out-Null
                    }
                    if ($LASTEXITCODE -ne 0) { throw "VTune failed for $tag with exit code $LASTEXITCODE." }
                    & $VTune -report hw-events -group-by process -r $result -format csv -csv-delimiter comma -report-output "$result.csv" | Out-Null
                    if ($LASTEXITCODE -ne 0) { throw "The report of $tag failed with exit code $LASTEXITCODE." }
                    Write-Host $tag
                }
            }
        }
    }
}

Write-Host "Done: $index runs under $Out."
