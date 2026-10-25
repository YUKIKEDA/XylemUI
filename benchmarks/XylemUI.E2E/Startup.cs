using System.Diagnostics;
using System.Text.Json;
using XylemUI.Platform.Windows;

namespace XylemUI.E2E;

// Startup and idle (design §11.3; roadmap row M1a).
//   startup: launches this executable as `startup-child` ten times, one after another, and measures from just before the
//            process is created to the display refresh that showed the first frame (frame statistics, SyncQPCTime; the
//            performance counter is one clock for every process). Also the part up to Main, and the executable's size.
//   startup-child: shows the scene once, waits until frame statistics say it reached the display, prints the counters, and
//            exits; with --idle, first stays idle for 10 s and reports the CPU time and the memory of that time. If the display
//            never shows the frame (a hidden window's, for one), the child ends with exit code 1 after DisplayTimeout.
internal static class Startup
{
    // The longest wait for the first frame to reach the display. Without it, a child whose frame the display never showed waited
    // forever: with a hidden window, main's child hung in 4 of 5 runs (PR #268).
    private static readonly TimeSpan DisplayTimeout = TimeSpan.FromSeconds(2);

    // `scopes` (Scene.Build's) draws the first frame in clips and transforms (row R4d), which waits for their shaders.
    public static int Launch(int runs, string scopes = "")
    {
        string exe = Environment.ProcessPath!;
        var toDisplay = new List<double>();
        var toMain = new List<double>();
        for (int i = 0; i < runs; i++)
        {
            long launched = Stopwatch.GetTimestamp();
            using JsonDocument child = RunChild(exe, scopes.Length > 0 ? $"startup-child --scopes {scopes}" : "startup-child");
            JsonElement root = child.RootElement;
            toDisplay.Add(Stopwatch.GetElapsedTime(launched, root.GetProperty("displayed_qpc").GetInt64()).TotalMilliseconds);
            toMain.Add(Stopwatch.GetElapsedTime(launched, root.GetProperty("main_qpc").GetInt64()).TotalMilliseconds);
        }

        using var report = new Report("startup");
        report.Series("launch_to_display_ms", toDisplay);
        report.Series("launch_to_main_ms", toMain);
        report.Number("exe_bytes", new FileInfo(exe).Length);
        return 0;
    }

    public static int Child(bool idle, bool gray = false, string scopes = "")
    {
        long main = Stopwatch.GetTimestamp();
        UiThread.Initialize();
        using var scene = new Scene("XylemUI E2E startup", gray: gray);
        scene.Renderer.StartMeasuringFrames();
        Win32MessageLoop.SetFrameClock(scene.Renderer.FrameLatencyWaitableObject);
        long firstCall = 0;
        long displayed = 0;
        bool timedOut = false;
        int frame = 0;
        Action? step = null;
        step = () =>
        {
            if (frame++ == 0)
            {
                scene.Build(0, scopes: scopes);
                firstCall = Stopwatch.GetTimestamp();
                scene.Renderer.Render(scene.List, scene.Clear, scene.Atlas);
            }
            else if (scene.Renderer.TryReadPresentLatency(out _, out long refresh))
            {
                displayed = refresh;
                if (!idle)
                {
                    Win32MessageLoop.Quit();
                }
                else
                {
                    StartIdle();
                }

                return;
            }
            else if (Stopwatch.GetElapsedTime(firstCall) > DisplayTimeout)
            {
                timedOut = true;
                Win32MessageLoop.Quit();
                return;
            }

            // Not shown yet: look again at the next wake (the clock's timeout, since nothing is presented).
            Win32MessageLoop.FrameScheduler.RequestFrame(step!);
        };
        Win32MessageLoop.FrameScheduler.RequestFrame(step);
        _ = Win32MessageLoop.Run();
        Win32MessageLoop.SetFrameClock(0);
        if (timedOut)
        {
            Console.Error.WriteLine($"startup-child: the first frame did not reach the display within {DisplayTimeout.TotalSeconds} s.");
            return 1;
        }

        if (idle)
        {
            File.WriteAllText(
                OutputOf(Environment.ProcessId),
                $"{{\"displayed_qpc\":{displayed.ToString(System.Globalization.CultureInfo.InvariantCulture)}}}"
            );
        }

        using var report = new Report("startup-child");
        report.Number("main_qpc", main);
        report.Number("first_present_call_qpc", firstCall);
        report.Number("displayed_qpc", displayed);
        if (idle)
        {
            report.Number("idle_cpu_ms", _idleCpu.TotalMilliseconds);
            report.Number("idle_seconds", 10);
            report.Number("working_set_bytes", _workingSet);
            report.Number("private_bytes", _privateBytes);
        }

        return 0;
    }

    // The check of the startup measurement from outside (M1a review): in the same run, the compositor's present of the first
    // desktop image with the marker (DisplayWatch) against the refresh the app's frame statistics report for its first frame.
    public static int Check(int runs)
    {
        var differences = new List<double>();
        for (int i = 0; i < runs; i++)
        {
            _ = DisplayWatch.LaunchToMarkerOnScreen(Environment.ProcessPath!, "startup-child --idle", out Process process, out long seen);
            int id = process.Id;
            using (process)
            {
                process.WaitForExit();
            }

            using JsonDocument child = JsonDocument.Parse(File.ReadAllText(OutputOf(id)));
            long displayed = child.RootElement.GetProperty("displayed_qpc").GetInt64();
            File.Delete(OutputOf(id));
            differences.Add(Stopwatch.GetElapsedTime(displayed, seen).TotalMilliseconds);
        }

        using var report = new Report("startup-check");
        report.Series("desktop_present_minus_frame_statistics_ms", differences);
        return 0;
    }

    private static string OutputOf(int processId) => Path.Combine(Path.GetTempPath(), $"xylemui-e2e-child-{processId}.json");

    private static TimeSpan _idleCpu;
    private static long _workingSet;
    private static long _privateBytes;

    // Ten seconds with no frame requested; a thread posts a message at the end. The loop waits on its queue only.
    private static void StartIdle()
    {
        Process self = Process.GetCurrentProcess();
        TimeSpan cpuBefore = self.TotalProcessorTime;
        UiDispatcher dispatcher = Win32MessageLoop.Dispatcher;
        dispatcher.MessageHandler = _ =>
        {
            self.Refresh();
            _idleCpu = self.TotalProcessorTime - cpuBefore;
            _workingSet = self.WorkingSet64;
            _privateBytes = self.PrivateMemorySize64;
            Win32MessageLoop.Quit();
        };
        var timer = new Thread(() =>
        {
            Thread.Sleep(TimeSpan.FromSeconds(10));
            dispatcher.Post(new IdleOver());
        })
        {
            IsBackground = true,
        };
        timer.Start();
    }

    private static JsonDocument RunChild(string exe, string arguments)
    {
        using var process = Process.Start(new ProcessStartInfo(exe, arguments) { RedirectStandardOutput = true, UseShellExecute = false })!;
        string output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"{arguments} exited with {process.ExitCode}: {output}");
        }

        return JsonDocument.Parse(output);
    }

    // The idle child three times, one after another: the CPU time and the memory of 10 idle seconds after the first frame; with
    // gray, its text is grayscale instead of the window's ClearType (roadmap row T2).
    public static int Idle(int runs, bool gray = false)
    {
        var cpu = new List<double>();
        var workingSet = new List<double>();
        var privateBytes = new List<double>();
        for (int i = 0; i < runs; i++)
        {
            using JsonDocument child = RunChild(Environment.ProcessPath!, gray ? "startup-child --idle --gray" : "startup-child --idle");
            JsonElement root = child.RootElement;
            cpu.Add(root.GetProperty("idle_cpu_ms").GetDouble());
            workingSet.Add(root.GetProperty("working_set_bytes").GetInt64() / 1048576.0);
            privateBytes.Add(root.GetProperty("private_bytes").GetInt64() / 1048576.0);
        }

        using var report = new Report("idle");
        report.Number("idle_seconds", 10);
        report.Series("idle_cpu_ms", cpu);
        report.Series("working_set_mb", workingSet);
        report.Series("private_mb", privateBytes);
        return 0;
    }

    private sealed record IdleOver : UiMessage;
}
