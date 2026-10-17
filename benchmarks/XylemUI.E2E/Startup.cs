using System.Diagnostics;
using System.Text.Json;
using XylemUI.Platform.Windows;

namespace XylemUI.E2E;

// Startup and idle (design §11.3; roadmap row M1a).
//   startup: launches this executable as `startup-child` ten times, one after another, and measures from just before the
//            process is created to the display refresh that showed the first frame (frame statistics, SyncQPCTime; the
//            performance counter is one clock for every process). Also the part up to Main, and the executable's size.
//   startup-child: shows the scene once, waits until frame statistics say it reached the display, prints the counters, and
//            exits; with --idle, first stays idle for 10 s and reports the CPU time and the memory of that time.
internal static class Startup
{
    public static int Launch(int runs)
    {
        string exe = Environment.ProcessPath!;
        var toDisplay = new List<double>();
        var toMain = new List<double>();
        for (int i = 0; i < runs; i++)
        {
            long launched = Stopwatch.GetTimestamp();
            using JsonDocument child = RunChild(exe, "startup-child");
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

    public static int Child(bool idle)
    {
        long main = Stopwatch.GetTimestamp();
        UiThread.Initialize();
        using var scene = new Scene("XylemUI E2E startup");
        scene.Renderer.StartMeasuringFrames();
        Win32MessageLoop.SetFrameClock(scene.Renderer.FrameLatencyWaitableObject);
        long firstCall = 0;
        long displayed = 0;
        int frame = 0;
        Action? step = null;
        step = () =>
        {
            if (frame++ == 0)
            {
                scene.Build(0);
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

            // Not shown yet: look again at the next wake (the clock's timeout, since nothing is presented).
            Win32MessageLoop.FrameScheduler.RequestFrame(step!);
        };
        Win32MessageLoop.FrameScheduler.RequestFrame(step);
        _ = Win32MessageLoop.Run();
        Win32MessageLoop.SetFrameClock(0);

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

    // The idle child three times, one after another: the CPU time and the memory of 10 idle seconds after the first frame.
    public static int Idle(int runs)
    {
        var cpu = new List<double>();
        var workingSet = new List<double>();
        var privateBytes = new List<double>();
        for (int i = 0; i < runs; i++)
        {
            using JsonDocument child = RunChild(Environment.ProcessPath!, "startup-child --idle");
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
