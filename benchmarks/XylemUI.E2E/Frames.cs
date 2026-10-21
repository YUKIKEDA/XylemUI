using System.Diagnostics;
using XylemUI.Platform.Windows;

namespace XylemUI.E2E;

// Frames of the scene, paced by the swap chain (design §11.3; roadmap row M1a): the CPU time of building the display list, of
// Draw (atlas upload, instances, the draw call), and of Present; the GPU time of the draws (timestamp queries); the time from
// each present call to the refresh that showed it (frame statistics); and the interval between frames. A frame whose interval
// is more than 1.5 times the median missed a refresh. `heavy` loads the GPU with full-window translucent rectangles. The text is
// ClearType where the window allows it, or grayscale with --gray (roadmap row T2); the report names it and the draws of a frame.
internal static class Frames
{
    public static int Run(int frames, int heavy, bool warp, bool gray = false)
    {
        UiThread.Initialize();
        using var scene = new Scene("XylemUI E2E frames", warp, gray);
        scene.Renderer.StartMeasuringFrames();
        Win32MessageLoop.SetFrameClock(scene.Renderer.FrameLatencyWaitableObject);
        var build = new List<double>(frames);
        var draw = new List<double>(frames);
        var present = new List<double>(frames);
        var cpu = new List<double>(frames);
        var interval = new List<double>(frames);
        var gpu = new List<double>(frames);
        var latency = new List<double>(frames);
        int frame = 0;
        long last = 0;
        Action? step = null;
        step = () =>
        {
            long t0 = Stopwatch.GetTimestamp();
            if (last != 0)
            {
                interval.Add(Stopwatch.GetElapsedTime(last, t0).TotalMilliseconds);
            }

            last = t0;
            scene.Build(frame, heavy);
            long t1 = Stopwatch.GetTimestamp();
            scene.Renderer.Draw(scene.List, scene.Clear, scene.Atlas);
            long t2 = Stopwatch.GetTimestamp();
            scene.Renderer.Present();
            long t3 = Stopwatch.GetTimestamp();
            build.Add(Stopwatch.GetElapsedTime(t0, t1).TotalMilliseconds);
            draw.Add(Stopwatch.GetElapsedTime(t1, t2).TotalMilliseconds);
            present.Add(Stopwatch.GetElapsedTime(t2, t3).TotalMilliseconds);
            cpu.Add(Stopwatch.GetElapsedTime(t0, t3).TotalMilliseconds);
            while (scene.Renderer.TryReadGpuMilliseconds(out double gpuMs))
            {
                gpu.Add(gpuMs);
            }

            if (scene.Renderer.TryReadPresentLatency(out double latencyMs, out _))
            {
                latency.Add(latencyMs);
            }

            if (++frame < frames)
            {
                Win32MessageLoop.FrameScheduler.RequestFrame(step!);
            }
            else
            {
                Win32MessageLoop.Quit();
            }
        };
        Win32MessageLoop.FrameScheduler.RequestFrame(step);
        _ = Win32MessageLoop.Run();
        Win32MessageLoop.SetFrameClock(0);

        // The first frames warm the atlas and the buffers; they are left out.
        const int Warm = 10;
        double median = interval.Skip(Warm).Order().ElementAt(interval.Count / 2);
        int missed = interval.Skip(Warm).Count(ms => ms > 1.5 * median);
        using var report = new Report(heavy == 0 ? "frames" : "frames-heavy");
        report.Text("gpu", scene.Renderer.IsWarp ? "warp" : "hardware");
        report.Number("heavy", heavy);
        report.Text("coverage", scene.Coverage.ToString());
        report.Number("draws", scene.Renderer.LastDrawCount);
        report.Series("build_ms", [.. build.Skip(Warm)]);
        report.Series("draw_ms", [.. draw.Skip(Warm)]);
        report.Series("present_ms", [.. present.Skip(Warm)]);
        report.Series("cpu_frame_ms", [.. cpu.Skip(Warm)]);
        report.Series("gpu_ms", [.. gpu.Skip(Warm)]);
        report.Series("present_latency_ms", [.. latency.Skip(Warm)]);
        report.Series("interval_ms", [.. interval.Skip(Warm)]);
        report.Number("missed_frames", missed);
        report.Number("frames_measured", interval.Count - Warm);
        report.Number("gpu_frames_skipped", scene.Renderer.GpuFramesSkipped);
        return 0;
    }
}
