using System.Diagnostics;
using XylemUI.Imaging;
using XylemUI.Platform.Windows;
using static XylemUI.Elements;
using NodeHandle = XylemUI.Handle<XylemUI.UiNode>;

namespace XylemUI.E2E;

// image-play <file> [seconds] [--warp]: an animated GIF or WebP played in a real window by the message loop, for row IM1's 3c
// (design §5.0.4, "How it advances"): how late each frame of the animation shows against the duration its file gives, how often
// the UI thread wakes, and what it allocates.
//
// A frame of the animation shows in the first frame of the window after its timer rang and its pixels arrived, so it shows late by
// up to the wait for that frame of the window. late_ms is, for each frame after the first, the time between the loop iterations
// that showed it and the frame before it, less the duration of the frame before it. The durations are read again from the file
// after the run, by a decoder that gives the frames in the same order.
internal static class ImagePlay
{
    public static int Run(string path, int seconds, bool warp)
    {
        UiThread.Initialize();
        using var scene = new Scene("XylemUI E2E image play", warp);
        Win32MessageLoop.SetFrameClock(scene.Renderer.FrameLatencyWaitableObject, scene.Renderer.TryReadLastRefresh, scene.Renderer.RefreshRate);
        using var report = new Report("image-play");
        report.Text("gpu", scene.Renderer.IsWarp ? "warp" : "hardware");
        report.Text("file", Path.GetFileName(path));
        report.Number("seconds", seconds);

        // The loop's context before the elements, as XylemApp.Run sets it: an image keeps the context it was made with.
        SynchronizationContext ui = Win32MessageLoop.Dispatcher.SynchronizationContext;
        SynchronizationContext.SetSynchronizationContext(ui);
        var tree = new UiTree();
        NodeHandle root = tree.CreateNode(new LayoutStyle());
        var target = new TreeScene.TimingTarget(scene);
        var frames = new WindowFrames(tree, root, target, scene.Atlas, Win32MessageLoop.FrameScheduler, scene.Clear, scene.Renderer.TextRendering);
        UiTree.Current = tree;
        try
        {
            _ = Scope.Create(() => tree.AppendChild(root, Image(ImageSource.FromFile(path)).Node));
        }
        finally
        {
            UiTree.Current = null;
        }

        float scale = scene.Window.ScaleFactor;
        frames.Resize(new Size(scene.Renderer.Width / scale, scene.Renderer.Height / scale), scale);

        WindowImageCache cache = tree.CachedImages;
        var shownAt = new List<long>(1 << 16);
        int iterations = 0;
        int seen = 0;
        long firstIteration = 0;
        long bytesAtFirst = 0;
        Win32MessageLoop.IterationHook = () =>
        {
            if (cache.Advances != seen)
            {
                seen = cache.Advances;
                shownAt.Add(Stopwatch.GetTimestamp());
                if (shownAt.Count == 1)
                {
                    // Counted from the first frame of the animation that followed another: the decode of the image is before it.
                    firstIteration = iterations;
                    bytesAtFirst = GC.GetAllocatedBytesForCurrentThread();
                }
            }

            iterations++;
        };

        using var stop = new Timer(
            _ => ui.Post(static _ => Win32MessageLoop.Quit(), null),
            null,
            TimeSpan.FromSeconds(seconds),
            Timeout.InfiniteTimeSpan
        );
        _ = Win32MessageLoop.Run();
        long bytes = GC.GetAllocatedBytesForCurrentThread() - bytesAtFirst;
        Win32MessageLoop.IterationHook = null;
        Win32MessageLoop.SetFrameClock(0);

        // The durations of the frames that showed, from a decoder of its own: frame k + 1 followed frame k after frame k's.
        var late = new List<double>();
        var durations = new List<double>();
        using (ImageAnimation? animation = ImageAnimation.TryOpen(File.ReadAllBytes(path), 0, 0, TextureCache.MaxSide))
        {
            if (animation is null || shownAt.Count < 2)
            {
                report.Number("advances", shownAt.Count);
                return 1;
            }

            report.Number("width", animation.Width);
            report.Number("height", animation.Height);
            byte[] pixels = new byte[animation.Width * animation.Height * 4];

            // The first call gives frame 0, which the image's decode showed; the advance i showed frame i + 1.
            _ = animation.Next(pixels, out _);
            for (int i = 0; i + 1 < shownAt.Count; i++)
            {
                if (animation.Next(pixels, out TimeSpan duration) != AnimationStep.Frame)
                {
                    break;
                }

                durations.Add(duration.TotalMilliseconds);
                late.Add(Stopwatch.GetElapsedTime(shownAt[i], shownAt[i + 1]).TotalMilliseconds - duration.TotalMilliseconds);
            }
        }

        double played = Stopwatch.GetElapsedTime(shownAt[0], shownAt[^1]).TotalSeconds;
        report.Number("advances", shownAt.Count);
        report.Series("duration_ms", durations);
        report.Series("late_ms", late);
        report.Number("refresh_hz", scene.Renderer.RefreshRate);
        report.Number("iterations", iterations - firstIteration);
        report.Number("iterations_per_second", (iterations - firstIteration) / played);
        report.Number("iterations_per_advance", (double)(iterations - firstIteration) / (shownAt.Count - 1));
        report.Number("presents", frames.PresentCount);
        report.Number("ui_bytes", bytes);
        cache.ReleaseAll();
        return 0;
    }
}
