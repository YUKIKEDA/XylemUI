using System.Diagnostics;
using System.Globalization;
using TerraFX.Interop.Windows;
using static TerraFX.Interop.Windows.Windows;

namespace XylemUI.E2E;

// Startup of an app of the component API (row V1d): launch to the first desktop image that shows the app's marker color #3366CC at
// a point of its client area (DisplayWatch, the definition of design §11.3's startup target), then a WM_CLOSE to its window, as its
// close button sends, and the exit code it returns. samples/Counter has the color in its button: --at 20,40 is inside the button at
// the scales of 100 % to 150 % (its text row is about 19 logical pixels high). A pointer over the button shows its hovered color, and
// the run is counted as missed. With --idle, each run then waits 3 s and measures the idle app as the comparison of frameworks does
// (External.Idle, row M1a): the CPU time of 10 s, then its working set and private bytes (row V3c).
//   app-startup <exe> [--at x,y] [--runs N] [--idle]
internal static class AppStartup
{
    public static int Run(string[] args)
    {
        string app = args.Length > 1 ? args[1] : throw new ArgumentException("app-startup needs the app's executable.");
        int runs = Array.IndexOf(args, "--runs") is int runsAt and >= 0 ? int.Parse(args[runsAt + 1], CultureInfo.InvariantCulture) : 10;
        bool idle = args.Contains("--idle");
        var idleCpu = new List<double>();
        var idleWorkingSet = new List<double>();
        var idlePrivate = new List<double>();
        (int x, int y) = (20, 40);
        if (Array.IndexOf(args, "--at") is int at and >= 0)
        {
            string[] parts = args[at + 1].Split(',');
            (x, y) = (int.Parse(parts[0], CultureInfo.InvariantCulture), int.Parse(parts[1], CultureInfo.InvariantCulture));
        }

        var toScreen = new List<double>();
        int missed = 0;
        int closedWithZero = 0;
        for (int i = 0; i < runs; i++)
        {
            double? ms = DisplayWatch.LaunchToMarkerOnScreen(app, string.Empty, out Process process, out _, x, y);
            using (process)
            {
                if (ms is double value)
                {
                    toScreen.Add(value);
                }
                else
                {
                    missed++;
                }

                if (idle && !process.HasExited)
                {
                    Thread.Sleep(3_000);
                    process.Refresh();
                    TimeSpan before = process.TotalProcessorTime;
                    Thread.Sleep(10_000);
                    process.Refresh();
                    idleCpu.Add((process.TotalProcessorTime - before).TotalMilliseconds);
                    idleWorkingSet.Add(process.WorkingSet64 / 1048576.0);
                    idlePrivate.Add(process.PrivateMemorySize64 / 1048576.0);
                }

                if (Close(process) == 0)
                {
                    closedWithZero++;
                }
            }

            Thread.Sleep(500);
        }

        toScreen.Sort();
        using var report = new Report("app-startup");
        report.Text("app", Path.GetFileName(app));
        report.Series("startup_screen_ms", toScreen);
        report.Number("startup_screen_missed", missed);
        report.Number("closed_with_exit_code_0", closedWithZero);
        report.Number("runs", runs);
        report.Number("exe_bytes", new FileInfo(app).Length);
        if (idle)
        {
            report.Number("idle_seconds", 10);
            report.Series("idle_cpu_ms", idleCpu);
            report.Series("idle_working_set_mb", idleWorkingSet);
            report.Series("idle_private_mb", idlePrivate);
        }

        return missed == 0 && closedWithZero == runs ? 0 : 1;
    }

    // Asks the app's window to close and waits 5 s for the process to end; one that does not is ended, and counts as -1.
    private static int Close(Process process)
    {
        process.Refresh();
        var window = (HWND)process.MainWindowHandle;
        if (window != HWND.NULL)
        {
            _ = PostMessageW(window, WM.WM_CLOSE, 0, 0);
        }

        if (process.WaitForExit(5_000))
        {
            return process.ExitCode;
        }

        process.Kill(entireProcessTree: true);
        return -1;
    }
}
