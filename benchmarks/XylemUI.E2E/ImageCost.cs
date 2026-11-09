using System.Diagnostics;
using XylemUI.Imaging;
using XylemUI.Text;
using static XylemUI.Elements;
using NodeHandle = XylemUI.Handle<XylemUI.UiNode>;

namespace XylemUI.E2E;

// The image element's costs (design §5.0.4; row IM1, 1b-2), with no GPU:
//   image-decode <file> [runs]  the decode of one image file at its own size and at the widths 1,024, 400, and 100, on the calling
//                               thread (TryDecodeTo: a JPEG at the smallest DCT scale not below, then the mean over each pixel's area)
//   image-tree [frames]         a window of 100 image elements of 64 x 64 drawn at 32 x 32, against 100 boxes with a background:
//                               the CPU time of a frame that paints again (the display list from the tree), and the bytes the marking
//                               and the frames allocated, counted around each call (precise): counted around the whole loop, about
//                               40 bytes a frame came out with boxes and with images, and none of them in the two calls
//                               The decode is a stand-in (ImageDecodeQueue.DecodeForTests), so that the frames measure the element.
internal static class ImageCost
{
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

    // 10 rows of 10: images of 64 x 64 drawn at 32 x 32, or boxes of 32 x 32 with a background.
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

    private sealed class NoScheduler : IFrameScheduler
    {
        public void RequestFrame(Action callback) { }

        public void CancelFrame() { }
    }
}
