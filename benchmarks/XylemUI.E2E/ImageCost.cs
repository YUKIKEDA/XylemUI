using System.Diagnostics;
using XylemUI.Imaging;
using XylemUI.Text;
using static XylemUI.Elements;
using NodeHandle = XylemUI.Handle<XylemUI.UiNode>;

namespace XylemUI.E2E;

// The image element's costs (design §5.0.4; row IM1, 1b-2 and 3a), with no GPU:
//   image-decode <file> [runs]  the decode of one image file at its own size and at the widths 1,024, 400, and 100, on the calling
//                               thread (TryDecodeTo: a JPEG at the smallest DCT scale not below, then the mean over each pixel's area)
//   image-tree [frames]         a window of 100 image elements of 64 x 64 drawn at 32 x 32, against 100 boxes with a background:
//                               the CPU time of a frame that paints again (the display list from the tree), and the bytes the marking
//                               and the frames allocated, counted around each call (precise): counted around the whole loop, about
//                               40 bytes a frame came out with boxes and with images, and none of them in the two calls
//   image-scroll [frames]       the scene of 100 images of design §11.3 under the limits ImageCache gives a new window (row IM1,
//                               3a): a scroll view 200 high over 100 image elements, each of a source of its own decoded to
//                               512 x 512 (4 bytes a pixel: the 100 are the default 104,857,600 bytes exactly) and drawn at
//                               32 x 32, scrolled by a wheel of 50 pixels down and up in turn, against 100 boxes scrolled as
//                               they are. The CPU time of the frame each wheel asks for, the bytes the wheels and the frames
//                               allocated, and the cache's counts after them
//                               The decode is a stand-in (ImageDecodeQueue.DecodeForTests), so that the frames measure the element.
internal static class ImageCost
{
    // The side of an image of image-scroll: 100 images of 512 x 512 are the default byte limit.
    private const int ScrollSide = 512;

    private static readonly int[] Widths = [0, 1_024, 400, 100];

    public static int Decode(string path, int runs)
    {
        byte[] data = File.ReadAllBytes(path);
        var decoder = new ImageDecoder();
        using var report = new Report("image-decode");
        report.Text("file", Path.GetFileName(path));
        report.Number("bytes", data.Length);
        foreach (int width in Widths)
        {
            var times = new List<double>(runs);
            int w = 0;
            int h = 0;
            for (int run = -2; run < runs; run++)
            {
                long start = Stopwatch.GetTimestamp();
                if (!decoder.TryDecodeTo(data, width, 0, TextureCache.MaxSide, out _, out w, out h))
                {
                    Console.Error.WriteLine("The image was not read.");
                    return 1;
                }

                if (run >= 0)
                {
                    times.Add(Stopwatch.GetElapsedTime(start).TotalMilliseconds);
                }
            }

            string name = width == 0 ? "whole" : $"w{width}";
            report.Text($"{name}_size", $"{w}x{h}");
            report.Number($"{name}_eighths", decoder.LastEighths);
            report.Series($"{name}_ms", times);
        }

        return 0;
    }

    public static int Tree(int frames)
    {
        UiThread.Initialize();
        ImageDecodeQueue.DecodeForTests = static (_, _, _) => new ImageResult(new byte[64 * 64 * 4], 64, 64);
        using var report = new Report("image-tree");
        foreach (bool images in new[] { false, true })
        {
            var atlas = new GlyphAtlas(static () => new Platform.Windows.DirectWriteGlyphRasterizer());
            var tree = new UiTree();
            NodeHandle root = tree.CreateNode(new LayoutStyle());
            var window = new WindowFrames(tree, root, new NoTarget(), atlas, new NoScheduler(), default);
            NodeHandle grid = default;
            UiTree.Current = tree;
            try
            {
                _ = Scope.Create(() => tree.AppendChild(root, grid = Grid(images).Node));
            }
            finally
            {
                UiTree.Current = null;
            }

            window.Resize(new Size(420, 420), 1);
            var times = new List<double>(frames);
            long invalidateBytes = 0;
            long frameBytes = 0;
            for (int frame = -10; frame < frames; frame++)
            {
                long b0 = GC.GetTotalAllocatedBytes(precise: true);
                Invalidation.Invalidate(tree, grid, ChangeKind.Color);
                long b1 = GC.GetTotalAllocatedBytes(precise: true);
                long start = Stopwatch.GetTimestamp();
                window.RunFrame();
                long b2 = GC.GetTotalAllocatedBytes(precise: true);
                if (frame >= 0)
                {
                    times.Add(Stopwatch.GetElapsedTime(start).TotalMilliseconds);
                    invalidateBytes += b1 - b0;
                    frameBytes += b2 - b1;
                }
            }

            string name = images ? "images" : "boxes";
            report.Series($"{name}_frame_ms", times);
            report.Number($"{name}_textures", tree.Images.Count);
            report.Number($"{name}_invalidate_bytes", invalidateBytes);
            report.Number($"{name}_frame_bytes", frameBytes);
            atlas.Dispose();
        }

        ImageDecodeQueue.DecodeForTests = null;
        return 0;
    }

    public static int Scroll(int frames)
    {
        UiThread.Initialize();
        ImageDecodeQueue.DecodeForTests = static (_, _, _) => new ImageResult(new byte[ScrollSide * ScrollSide * 4], ScrollSide, ScrollSide);
        using var report = new Report("image-scroll");
        report.Number("frames", frames);
        bool asNamed = true;
        foreach (bool images in new[] { false, true })
        {
            using var atlas = new GlyphAtlas(static () => new Platform.Windows.DirectWriteGlyphRasterizer());
            var tree = new UiTree();
            NodeHandle root = tree.CreateNode(new LayoutStyle());
            var target = new TakingTarget();
            var scheduler = new HeldFrames();
            var window = new WindowFrames(tree, root, target, atlas, scheduler, default);
            var input = new WindowInput(tree, window, new NoPlatform());
            NodeHandle view = default;
            UiTree.Current = tree;
            try
            {
                _ = Scope.Create(() => tree.AppendChild(root, view = ScrollView(Grid(images)).Width(420).Height(200).Node));
            }
            finally
            {
                UiTree.Current = null;
            }

            window.Resize(new Size(420, 420), 1);
            var times = new List<double>(frames);
            long wheelBytes = 0;
            long frameBytes = 0;
            int scrolled = 0;
            int presents = 0;
            for (int frame = -10; frame < frames; frame++)
            {
                float before = tree.ScrollOffsetOf(view).Y;
                int presented = target.Presents;
                var delta = new System.Numerics.Vector2(0, (frame & 1) == 0 ? 50 : -50);
                long b0 = GC.GetTotalAllocatedBytes(precise: true);
                input.Handle(
                    new PointerInput(
                        PointerInputKind.Wheel,
                        5,
                        5,
                        PointerButton.None,
                        PointerButtons.None,
                        KeyModifiers.None,
                        0,
                        TimeSpan.Zero,
                        PointerType.Mouse,
                        delta
                    )
                );
                long b1 = GC.GetTotalAllocatedBytes(precise: true);
                long start = Stopwatch.GetTimestamp();
                scheduler.Run();
                long b2 = GC.GetTotalAllocatedBytes(precise: true);
                if (frame >= 0)
                {
                    times.Add(Stopwatch.GetElapsedTime(start).TotalMilliseconds);
                    wheelBytes += b1 - b0;
                    frameBytes += b2 - b1;
                    scrolled += tree.ScrollOffsetOf(view).Y != before ? 1 : 0;
                    presents += target.Presents - presented;
                }
            }

            string name = images ? "images" : "boxes";
            report.Series($"{name}_frame_ms", times);
            report.Number($"{name}_scrolled", scrolled);
            report.Number($"{name}_presents", presents);
            report.Number($"{name}_wheel_bytes", wheelBytes);
            report.Number($"{name}_frame_bytes", frameBytes);
            report.Number($"{name}_textures", tree.Images.Count);

            // A wheel that moved nothing, or a frame that did not run, measured another scene than the one named.
            asNamed &= scrolled == frames && presents == frames;
            if (images)
            {
                WindowImageCache cache = tree.CachedImages;
                report.Number("decodes", cache.Decodes);
                report.Number("hits", cache.Hits);
                report.Number("counted", cache.CountedCount);
                report.Number("counted_bytes", cache.CountedBytes);
                report.Number("evicted", cache.EvictedCount);
                report.Number("refused", cache.RefusedCount);
                report.Number("maximum_count", cache.MaximumCount);
                report.Number("maximum_bytes", cache.MaximumBytes);
            }
        }

        ImageDecodeQueue.DecodeForTests = null;
        return asNamed ? 0 : 1;
    }

    // 10 rows of 10: images drawn at 32 x 32 (of 64 x 64, or as large as image-scroll decodes them), or boxes of 32 x 32 with a
    // background.
    private static Element Grid(bool images)
    {
        var rows = new Element[10];
        for (int r = 0; r < rows.Length; r++)
        {
            var row = new Element[10];
            for (int c = 0; c < row.Length; c++)
            {
                row[c] = images
                    ? Image(ImageSource.FromBytes(new byte[] { (byte)r, (byte)c })).Width(32).Height(32)
                    : Box().Width(32).Height(32).Background(new Paint(Color.FromRgb(0x3366CC)));
            }

            rows[r] = Row(row);
        }

        return Column(rows);
    }

    private sealed class NoTarget : IRenderTarget
    {
        public void Draw(ReadOnlySpan<DisplayList> lists, Color clearColor, GlyphAtlas atlas, TextureCache images) { }

        public void Present() { }
    }

    // Takes the uploads and the releases of each draw as a renderer does, so that the cache drops the pixels of an image drawn.
    private sealed class TakingTarget : IRenderTarget
    {
        public int Presents { get; private set; }

        public void Draw(ReadOnlySpan<DisplayList> lists, Color clearColor, GlyphAtlas atlas, TextureCache images)
        {
            images.TakeReleased();
            images.TakeUploads();
        }

        public void Present() => Presents++;
    }

    private sealed class NoScheduler : IFrameScheduler
    {
        public void RequestFrame(Action callback) { }

        public void CancelFrame() { }
    }

    // Keeps the frame asked for until Run runs it.
    private sealed class HeldFrames : IFrameScheduler
    {
        private Action? _callback;

        public void RequestFrame(Action callback) => _callback = callback;

        public void CancelFrame() => _callback = null;

        public void Run()
        {
            Action? callback = _callback;
            _callback = null;
            callback?.Invoke();
        }
    }

    private sealed class NoPlatform : IPointerPlatform
    {
        public void Capture() { }

        public void ReleaseCapture() { }

        public void SetCursor(CursorKind cursor) { }
    }
}
