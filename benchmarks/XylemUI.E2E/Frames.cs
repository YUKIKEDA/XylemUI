using System.Diagnostics;
using XylemUI.Platform.Windows;

namespace XylemUI.E2E;

// Frames of the scene, paced by the swap chain (design §11.3; roadmap row M1a): the CPU time of building the display list, of
// Draw (atlas upload, instances, the draw call), and of Present; the GPU time of the draws (timestamp queries); the time from
// each present call to the refresh that showed it (frame statistics); and the interval between frames. A frame whose interval
// is more than 1.5 times the median missed a refresh. `heavy` loads the GPU with full-window translucent rectangles. The text is
// ClearType where the window allows it, or grayscale with --gray (roadmap row T2); the report names it and the draws of a frame.
// --shapes, --curves, and --groups change the scene's rectangles (rows R4a, R4b, and R4c, Scene.Build); --scopes puts them in clips
// and transforms (row R4d). --tree draws the plain scene from a UI tree with WindowFrames (row V1b, TreeScene): build_ms is then the
// draw stage (the display list from the tree and its glyphs), layout_ms the layout pass after the boxes moved, and cpu_frame_ms
// leaves the layout out.
internal static class Frames
{
    public static int Run(
        int frames,
        int heavy,
        bool warp,
        bool gray = false,
        bool shapes = false,
        bool curves = false,
        bool groups = false,
        int layers = 50,
        string scopes = "",
        int nested = 0,
        bool tree = false
    )
    {
        UiThread.Initialize();
        using var scene = new Scene("XylemUI E2E frames", warp, gray);
        using TreeScene? treeScene = tree ? new TreeScene(scene, scene.Font) : null;
        var layout = new List<double>(frames);
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
            long t1;
            long t2;
            long t3;
            if (treeScene is not null)
            {
                layout.Add(treeScene.Update(frame));
                t0 = Stopwatch.GetTimestamp();
                treeScene.RunFrame();
                t1 = treeScene.Target.DrawStart;
                t2 = treeScene.Target.DrawEnd;
                t3 = treeScene.Target.PresentEnd;
            }
            else
            {
                scene.Build(frame, heavy, shapes, curves, groups, layers, scopes, nested);
                t1 = Stopwatch.GetTimestamp();
                scene.Renderer.Draw(scene.List, scene.Clear, scene.Atlas);
                t2 = Stopwatch.GetTimestamp();
                scene.Renderer.Present();
                t3 = Stopwatch.GetTimestamp();
            }

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
        report.Text("shapes", shapes ? "yes" : "no");
        report.Text("curves", curves ? "yes" : "no");
        report.Text("groups", groups ? "yes" : "no");
        report.Text("scopes", scopes.Length > 0 ? scopes : "none");
        report.Text("tree", tree ? "yes" : "no");
        report.Number("nested", nested);
        report.Number("layers", scene.Renderer.LayerCount);
        report.Number("layer_bytes", scene.Renderer.LayerBytes);
        report.Number("layers_refused", scene.Renderer.LayersRefused);
        report.Number("draws", scene.Renderer.LastDrawCount);
        report.Series("build_ms", [.. build.Skip(Warm)]);
        if (tree)
        {
            report.Series("layout_ms", [.. layout.Skip(Warm)]);
        }

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
