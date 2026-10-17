using System.Diagnostics;
using System.Globalization;
using XylemUI.Platform.Windows;

namespace XylemUI.E2E;

// The comparison of frameworks (roadmap row M1a, docs/benchmarks/m1-baseline.md): the same measurements, taken from outside the
// process, for any app that shows the scene (benchmarks/compare/). PresentMon (ETW, run without elevation by a member of
// Performance Log Users) records every process, and the rows are matched by process id, since a process started after the
// trace shows as <unknown> to an unelevated PresentMon.
//   external --presentmon <exe> --app <exe> [--app-args <args>] [--name <label>] [--runs N]
//     (--app-args go before the app's own argument: "app" for XylemUI.E2E itself; --frames-args only for the frames, such as
//     Avalonia's --redirection)
//     startup: launch to the scene's marker rectangle on the screen (DisplayWatch: the compositor's present of the desktop
//              image that shows it), and to the window's composed content (ScreenWatch), runs times each, with the app's
//              `--static` argument (it draws once and stays idle). --frames-only skips startup and idle.
//     idle:    10 s of the static app, 3 s after launch: CPU time, working set, private bytes.
//     frames:  10 s of the animated app (no argument), traced from 2 s after its launch: MsUntilDisplayed, MsGPUBusy, the
//              interval between presents and between display changes, and refreshes missed (a display change more than 1.5
//              refreshes).
//     size:    the bytes of the files the app needs (its directory without .pdb and .xml), and of the executable.
// The marker is drawn with the scene's content, so a framework that presents a blank frame first is measured to its content.
internal static class External
{
    public static int Run(string[] args)
    {
        string presentMon = Option(args, "--presentmon") ?? throw new ArgumentException("--presentmon is required.");
        string app = Option(args, "--app") ?? throw new ArgumentException("--app is required.");
        string name = Option(args, "--name") ?? Path.GetFileNameWithoutExtension(app);
        int runs = int.Parse(Option(args, "--runs") ?? "10", CultureInfo.InvariantCulture);
        string prefix = Option(args, "--app-args") ?? string.Empty;
        string staticArgs = (prefix + " --static").Trim();
        string framesArgs = (prefix + " " + (Option(args, "--frames-args") ?? string.Empty)).Trim();
        string work = Path.Combine(Path.GetTempPath(), "xylemui-e2e-" + Environment.ProcessId.ToString(CultureInfo.InvariantCulture));
        _ = Directory.CreateDirectory(work);
        try
        {
            bool framesOnly = args.Contains("--frames-only");
            var toScreen = new List<double>();
            var toContent = new List<double>();
            for (int i = 0; i < (framesOnly ? 0 : runs); i++)
            {
                // To the screen (the definition of the startup target), then to the window's composed content (robust when the
                // window is covered), each from its own launch.
                Measure(toScreen, DisplayWatch.LaunchToMarkerOnScreen(app, staticArgs, out Process screen), screen);
                Measure(toContent, ScreenWatch.LaunchToMarker(app, staticArgs, out Process content), content);
            }

            (double cpu, double workingSet, double privateBytes) = framesOnly ? (0, 0, 0) : Idle(app, staticArgs);
            string framesCsv = Path.Combine(work, "frames.csv");

            // The app runs for 2 s before the trace starts and names it by animated id: a PresentMon started by this animated
            // recorded none of the presents of an app this animated started after it (M1a).
            using Process animated = Process.Start(new ProcessStartInfo(app, framesArgs) { UseShellExecute = false })!;
            ScreenWatch.BringToFront(animated);
            Thread.Sleep(2_000);
            using (Process monitor = StartPresentMon(presentMon, framesCsv, seconds: 10, animated.Id))
            {
                monitor.WaitForExit();
                bool exitedEarly = animated.HasExited;
                int earlyExitCode = exitedEarly ? animated.ExitCode : 0;
                Kill(animated);
                Frames(framesCsv, animated.Id, out var untilDisplayed, out var gpuBusy, out var betweenPresents, out var betweenDisplays);
                using var report = new Report("external");
                report.Text("name", name);
                report.Text("frames_args", framesArgs);
                report.Number("frames_app_exited_early", exitedEarly ? 1 : 0);
                report.Number("frames_app_exit_code", earlyExitCode);
                report.Number("frames_app_pid", animated.Id);
                report.Series("launch_to_screen_ms", toScreen);
                report.Series("launch_to_content_ms", toContent);

                // Runs that gave no result: without them a median would leave out exactly the slow or failed launches.
                report.Number("startup_screen_missed", framesOnly ? 0 : runs - toScreen.Count);
                report.Number("startup_content_missed", framesOnly ? 0 : runs - toContent.Count);
                report.Number("startup_runs", runs);
                if (!framesOnly)
                {
                    report.Number("idle_cpu_ms", cpu);
                    report.Number("idle_working_set_mb", workingSet);
                    report.Number("idle_private_mb", privateBytes);
                }

                report.Series("until_displayed_ms", untilDisplayed);
                report.Series("gpu_busy_ms", gpuBusy);
                report.Series("between_presents_ms", betweenPresents);
                double refresh = Median(betweenDisplays);
                report.Number("missed_refreshes", betweenDisplays.Count(ms => ms > 1.5 * refresh));
                report.Number("display_changes", betweenDisplays.Count);
                string directory = Path.GetDirectoryName(Path.GetFullPath(app))!;

                // What the app needs to run: every file of its directory but debug symbols and XML documentation.
                long shipped = Directory
                    .EnumerateFiles(directory, "*", SearchOption.AllDirectories)
                    .Where(f => !f.EndsWith(".pdb", StringComparison.OrdinalIgnoreCase) && !f.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
                    .Sum(f => new FileInfo(f).Length);
                report.Number("shipped_bytes", shipped);
                report.Number("exe_bytes", new FileInfo(app).Length);
            }

            return 0;
        }
        finally
        {
            // --keep leaves the PresentMon captures for a look at the rows.
            if (!args.Contains("--keep"))
            {
                Directory.Delete(work, recursive: true);
            }
            else
            {
                Console.Error.WriteLine("captures: " + work);
            }
        }
    }

    // The scene as an app, for the external measurements of XylemUI itself: animated, or drawn once with --static. With
    // `period` > 0 it draws one frame every `period` ms at most (waiting on the UI thread before it draws), to compare at another
    // framework's frame rate (M1a: Flutter drew the scene every 46 ms on the measuring machine, whatever the scene).
    public static int App(bool isStatic, int period)
    {
        UiThread.Initialize();
        using var scene = new Scene("XylemUI E2E app");
        Win32MessageLoop.SetFrameClock(scene.Renderer.FrameLatencyWaitableObject);
        int frame = 0;
        Action? step = null;
        long last = 0;
        step = () =>
        {
            if (period > 0 && last != 0)
            {
                double left = period - Stopwatch.GetElapsedTime(last).TotalMilliseconds;
                if (left > 0)
                {
                    Thread.Sleep(TimeSpan.FromMilliseconds(left));
                }
            }

            last = Stopwatch.GetTimestamp();
            scene.Build(frame++);
            scene.Renderer.Render(scene.List, scene.Clear, scene.Atlas);
            if (!isStatic)
            {
                Win32MessageLoop.FrameScheduler.RequestFrame(step!);
            }
        };
        scene.Window.Closed += (_, _) => Win32MessageLoop.Quit();
        Win32MessageLoop.FrameScheduler.RequestFrame(step);
        return Win32MessageLoop.Run();
    }

    private static void Measure(List<double> into, double? milliseconds, Process process)
    {
        using (process)
        {
            Thread.Sleep(500);
            Kill(process);
        }

        if (milliseconds is double value)
        {
            into.Add(value);
        }

        // A pause between launches, so that one app's teardown does not overlap the next launch.
        Thread.Sleep(1_000);
    }

    private static (double CpuMs, double WorkingSetMb, double PrivateMb) Idle(string app, string staticArgs)
    {
        using Process process = Process.Start(new ProcessStartInfo(app, staticArgs) { UseShellExecute = false })!;
        ScreenWatch.BringToFront(process);
        Thread.Sleep(3_000);
        process.Refresh();
        TimeSpan before = process.TotalProcessorTime;
        Thread.Sleep(10_000);
        process.Refresh();
        double cpu = (process.TotalProcessorTime - before).TotalMilliseconds;
        double workingSet = process.WorkingSet64 / 1048576.0;
        double privateBytes = process.PrivateMemorySize64 / 1048576.0;
        Kill(process);
        return (cpu, workingSet, privateBytes);
    }

    private static Process StartPresentMon(string presentMon, string csv, int seconds, int processId)
    {
        var info = new ProcessStartInfo(
            presentMon,
            $"--process_id {processId} --output_file \"{csv}\" --timed {seconds} --terminate_after_timed --no_console_stats --qpc_time"
        )
        {
            UseShellExecute = false,
            RedirectStandardError = true,
            RedirectStandardOutput = true,

            // PresentMon writes its messages in UTF-16.
            StandardErrorEncoding = System.Text.Encoding.Unicode,
        };
        Process monitor = Process.Start(info)!;
        monitor.OutputDataReceived += (_, _) => { };
        monitor.BeginOutputReadLine();

        // The trace runs once PresentMon says so on its error stream.
        string? line;
        while ((line = monitor.StandardError.ReadLine()) is not null && !line.Contains("Started recording", StringComparison.Ordinal)) { }

        // The rest of the error stream is read on a thread, so that PresentMon never blocks on a full pipe.
        var drain = new Thread(() => _ = monitor.StandardError.ReadToEnd()) { IsBackground = true };
        drain.Start();
        return monitor;
    }

    private static void Frames(
        string csv,
        int processId,
        out List<double> untilDisplayed,
        out List<double> gpuBusy,
        out List<double> betweenPresents,
        out List<double> betweenDisplays
    )
    {
        untilDisplayed = [];
        gpuBusy = [];
        betweenPresents = [];
        betweenDisplays = [];
        foreach (Dictionary<string, string> row in Rows(csv, processId))
        {
            Add(untilDisplayed, row["MsUntilDisplayed"]);
            Add(gpuBusy, row["MsGPUBusy"]);
            Add(betweenPresents, row["MsBetweenPresents"]);
            Add(betweenDisplays, row["MsBetweenDisplayChange"]);
        }
    }

    private static IEnumerable<Dictionary<string, string>> Rows(string csv, int processId)
    {
        // PresentMon writes no file when it records no present.
        if (!File.Exists(csv))
        {
            yield break;
        }

        string[] lines = File.ReadAllLines(csv);
        string[] header = lines[0].Split(',');
        string id = processId.ToString(CultureInfo.InvariantCulture);
        int idColumn = Array.IndexOf(header, "ProcessID");
        foreach (string line in lines.Skip(1))
        {
            string[] cells = line.Split(',');
            if (cells.Length == header.Length && cells[idColumn] == id)
            {
                yield return header.Zip(cells).ToDictionary(pair => pair.First, pair => pair.Second);
            }
        }
    }

    private static void Add(List<double> values, string cell)
    {
        if (double.TryParse(cell, NumberStyles.Float, CultureInfo.InvariantCulture, out double value))
        {
            values.Add(value);
        }
    }

    private static double Median(List<double> values) => values.Count == 0 ? 0 : values.Order().ElementAt(values.Count / 2);

    private static void Kill(Process process)
    {
        if (!process.HasExited)
        {
            process.Kill(entireProcessTree: true);
            process.WaitForExit();
        }
    }

    private static string? Option(string[] args, string name)
    {
        int i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }
}
