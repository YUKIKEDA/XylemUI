# Counts the CPU cycles that builds of XylemUI.E2E spend on one relayout (roadmap row L8): `layout-loop <scene> <kind> <n>` runs
# at two counts, and the difference of the process's cycles (QueryProcessCycleTime, every thread, without the time other
# processes held the CPU) divided by the difference of the counts is the cost of one pass, without the start and the build of
# the tree. Needs no privilege, but cycles still move with the CPU's frequency and caches; instructions.ps1 counts
# instructions. The builds run in the order A, B, B, A for each pair of builds, and one JSON line per run is appended to
# <out>.
#   ./benchmarks/counters/cycles.ps1 -Builds <dir> -Pairs 'old:new','old2:new2' -Out <file> [-Rounds 6]
# <dir>/<name>/XylemUI.E2E.exe is the build named <name>.
param(
    [Parameter(Mandatory = $true)][string]$Builds,
    [Parameter(Mandatory = $true)][string[]]$Pairs,
    [Parameter(Mandatory = $true)][string]$Out,
    [string[]]$Scenes = @('s6b', 'auto_min', 'percent'),
    # change: one leaf's width; fixed_row: the same in a tree whose rows have a fixed size.
    [string]$Kind = 'change',
    [int]$Low = 20000,
    [int]$High = 120000,
    [int]$Rounds = 6,
    # The logical processor the runs are pinned to (not 0, which takes most interrupts), at high priority.
    [int]$Processor = 2
)

$ErrorActionPreference = 'Stop'
Add-Type -TypeDefinition @'
using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
public static class ProcessCycles
{
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryProcessCycleTime(IntPtr process, out ulong cycles);

    // Runs the program to its end on one logical processor at high priority, and returns the cycles of all its threads.
    public static ulong Run(string exe, string arguments, int processor)
    {
        var info = new ProcessStartInfo(exe, arguments) { UseShellExecute = false, RedirectStandardOutput = true };
        using (Process process = Process.Start(info))
        {
            process.ProcessorAffinity = (IntPtr)(1L << processor);
            process.PriorityClass = ProcessPriorityClass.High;
            process.StandardOutput.ReadToEnd();
            process.WaitForExit();
            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException(exe + " failed with exit code " + process.ExitCode + ".");
            }

            if (!QueryProcessCycleTime(process.Handle, out ulong cycles))
            {
                throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            }

            return cycles;
        }
    }
}
'@

function Measure-Cycles([string]$Name, [string]$Scene, [int]$Count) {
    $exe = Join-Path $Builds "$Name\XylemUI.E2E.exe"
    return [ProcessCycles]::Run($exe, "layout-loop $Scene $Kind $Count", $Processor)
}

foreach ($pair in $Pairs) {
    $a, $b = $pair.Split(':')
    for ($round = 1; $round -le $Rounds; $round++) {
        foreach ($scene in $Scenes) {
            foreach ($name in $a, $b, $b, $a) {
                # Not $low and $high: PowerShell's names ignore case, and those overwrote the parameters.
                $fewer = Measure-Cycles $name $scene $Low
                $more = Measure-Cycles $name $scene $High
                $perPass = ($more - $fewer) / ($High - $Low)
                [ordered]@{ build = $name; scene = "$scene.$Kind"; round = $round; cycles_per_pass = $perPass } |
                    ConvertTo-Json -Compress | Add-Content -Path $Out -Encoding utf8
            }
        }
        Write-Host "$pair round $round"
    }
}
