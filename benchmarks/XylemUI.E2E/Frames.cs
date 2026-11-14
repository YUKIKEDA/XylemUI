using System.Diagnostics;
using XylemUI.Platform.Windows;

namespace XylemUI.E2E;

// Frames of the scene, paced by the swap chain (design §11.3; roadmap row M1a): the CPU time of building the display list, of
// Draw (atlas upload, instances, the draw call), and of Present; the GPU time of the draws (timestamp queries); the time from
// each present call to the refresh that showed it (frame statistics); and the interval between frames. A frame whose interval
// is more than 1.5 times the median missed a refresh. `heavy` loads the GPU with full-window translucent rectangles. The text is
// ClearType where the window allows it, or grayscale with --gray (roadmap row T2); the report names it and the draws of a frame.
// --shapes, --curves, and --groups change the scene's rectangles (rows R4a, R4b, and R4c, Scene.Build); --scopes puts them in clips
// and transforms (row R4d); --images draws them as images (row IM1). --tree draws the plain scene from a UI tree with WindowFrames (row V1b, TreeScene): build_ms is then the
// draw stage (the display list from the tree and its glyphs), layout_ms the layout pass after the boxes moved, and cpu_frame_ms
// leaves the layout out. --no-text leaves the line of text out of either scene, so that the draw stages compare the boxes alone.
// --hover (with --tree, row V1c) keeps the boxes still with a hovered background and moves the pointer between two of them each
// frame: input_ms is the input's delivery of the move, and the frame draws for the change of color alone.
// --scroll (with --tree, row V2) keeps the boxes still in a scroll container and changes its scroll position each frame: the frame
// draws them moved with no layout (layout_ms is the pass that finds nothing to do).
// --list virtual and --list for (with --tree, row P02e) put a box over 10,000 rows 20 high in place of the boxes, made with
// VirtualFor or with For, and scroll it 7 px each frame: layout_ms is the materialization stage with its layout passes.
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
        bool tree = false,
        bool noText = false,
        bool hover = false,
        bool scroll = false,
        string list = "",
        int resizes = 0,
        int resizesLate = 0,
        bool images = false
    )
    {
        UiThread.Initialize();
        using var scene = new Scene("XylemUI E2E frames", warp, gray);

        // The clock is set before the tree scene, whose WindowFrames presents its first frame on the synchronous resize path, and
        // that present is told to the loop's scheduler: an app (Win32PlatformWindow) does both in this order, so the scheduler counts
        // the present that did not wait (design §2.11, "Presents that did not wait"; Issue #679).
        Win32MessageLoop.SetFrameClock(scene.Renderer.FrameLatencyWaitableObject, scene.Renderer.TryReadLastRefresh, scene.Renderer.RefreshRate);
        using TreeScene? treeScene = tree ? new TreeScene(scene, scene.Font, noText, hover, scroll, list) : null;
        if (treeScene is not null && treeScene.Target.PresentEnd != 0)
        {
            Win32MessageLoop.FrameScheduler.FramePresented();
        }

        var layout = new List<double>(frames);
        var input = new List<double>(frames);
        scene.Renderer.StartMeasuringFrames();

        // A resize drag's frames, each presented on the synchronous resize path without waiting on the swap chain's object, and
        // told to the loop's scheduler as an app's WindowFrames tells it: the object keeps a signal for each, up to 40 on the
        // measuring machine (PR #680's review), and the frames after them must not fall a refresh behind or stall.
        for (int i = 0; i < resizes && treeScene is not null; i++)
        {
            treeScene.PresentOnResizePath();
            Win32MessageLoop.FrameScheduler.FramePresented();
        }

        var build = new List<double>(frames);
        var draw = new List<double>(frames);
        var present = new List<double>(frames);
        var cpu = new List<double>(frames);
        var interval = new List<double>(frames);
        var gpu = new List<double>(frames);
        var latency = new List<double>(frames);

        // A resize drag that starts while a frame waits for its late start (--resizes-late N, PR #695's review): N presents on the
        // synchronous resize path at the first loop iteration after frame 300 that finds the late start waiting, as WM_SIZE would
        // give inside the OS's modal loop.
        int lateResizes = resizesLate;
        Win32MessageLoop.IterationHook = () =>
        {
            if (lateResizes > 0 && treeScene is not null && Win32MessageLoop.Frames.IsLateWaiting && interval.Count >= 300)
            {
                for (int i = 0; i < lateResizes; i++)
                {
                    treeScene.PresentOnResizePath();
                    Win32MessageLoop.FrameScheduler.FramePresented();
                }

                lateResizes = 0;
            }
        };

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
                input.Add(treeScene.InputMilliseconds);
                t0 = Stopwatch.GetTimestamp();
                treeScene.RunFrame();
                t1 = treeScene.Target.DrawStart;
                t2 = treeScene.Target.DrawEnd;
                t3 = treeScene.Target.PresentEnd;
            }
            else
            {
                scene.Build(frame, heavy, shapes, curves, groups, layers, scopes, nested, noText, images);
                t1 = Stopwatch.GetTimestamp();
                if (images)
                {
                    scene.Renderer.Draw(scene.Lists, scene.Clear, scene.Atlas, scene.Images);
                }
                else
                {
                    scene.Renderer.Draw(scene.List, scene.Clear, scene.Atlas);
                }

                t2 = Stopwatch.GetTimestamp();
                scene.Renderer.Present();
                t3 = Stopwatch.GetTimestamp();
            }

            // A callback that presents says so, or the next frame waits on the 16 ms timer in place of the swap chain (design §2.11,
            // I1c). The tree's frames tell a scheduler of their own (TreeScene's NoScheduler), not this loop's, so they are told here
            // too, when the frame presented.
            if (treeScene is null || treeScene.Target.PresentEnd >= t0)
            {
                Win32MessageLoop.FrameScheduler.FramePresented();
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
        Win32MessageLoop.IterationHook = null;

        // Read before the clock goes back to the timer, which forgets the period.
        double derivedPeriod = Win32MessageLoop.Frames.DerivedPeriodMilliseconds;
        (long lateWakes, double latestWake) = Win32MessageLoop.Frames.LateWakes;
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
        report.Text("images", images ? "yes" : "no");
        report.Text("scopes", scopes.Length > 0 ? scopes : "none");
        report.Text("tree", tree ? "yes" : "no");
        report.Number("resizes", resizes);
        report.Number("resizes_late", resizesLate - lateResizes);
        report.Text("text", noText ? "no" : "yes");
        report.Text("hover", hover ? "yes" : "no");
        report.Text("scroll", scroll ? "yes" : "no");
        report.Text("list", list.Length > 0 ? list : "none");
        report.Number("nested", nested);
        report.Number("layers", scene.Renderer.LayerCount);
        report.Number("layer_bytes", scene.Renderer.LayerBytes);
        report.Number("layers_refused", scene.Renderer.LayersRefused);
        report.Number("draws", scene.Renderer.LastDrawCount);
        report.Series("build_ms", [.. build.Skip(Warm)]);
        if (tree)
        {
            report.Series("layout_ms", [.. layout.Skip(Warm)]);
            if (hover)
            {
                report.Series("input_ms", [.. input.Skip(Warm)]);
            }
        }

        report.Series("draw_ms", [.. draw.Skip(Warm)]);
        report.Series("present_ms", [.. present.Skip(Warm)]);
        report.Series("cpu_frame_ms", [.. cpu.Skip(Warm)]);
        report.Series("gpu_ms", [.. gpu.Skip(Warm)]);
        report.Series("present_latency_ms", [.. latency.Skip(Warm)]);
        report.Series("interval_ms", [.. interval.Skip(Warm)]);
        report.Number("missed_frames", missed);

        // Whether the window had the foreground at the end, one of the window states Issue #184 compares against missed frames.
        report.Number(
            "foreground",
            TerraFX.Interop.Windows.Windows.GetForegroundWindow() == (TerraFX.Interop.Windows.HWND)scene.Window.Handle ? 1 : 0
        );
        report.Number("topmost", MonitorChoice.IsTopmost(scene.Window.Handle) ? 1 : 0);

        // The frames run on the wait's timeout, and the signals taken without a frame for presents that did not wait (Issue #679).
        report.Number("frame_clock_timeouts", Win32MessageLoop.Frames.ClockTimeouts);
        report.Number("signals_owed", Win32MessageLoop.Frames.SignalsOwed);
        report.Number("late_starts", Win32MessageLoop.Frames.LateStarts);
        report.Number("derived_period_ms", derivedPeriod);
        report.Number("late_wakes", lateWakes);
        report.Number("latest_wake_ms", latestWake);
        report.Number("frames_measured", interval.Count - Warm);
        report.Number("gpu_frames_skipped", scene.Renderer.GpuFramesSkipped);
        return 0;
    }
}
