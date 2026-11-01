using System.Diagnostics;
using Todo;
using XylemUI.Platform.Windows;
using XylemUI.Primitives;
using XylemUI.Text;
using static XylemUI.Elements;
using static XylemUI.Primitives.Primitive;
using NodeHandle = XylemUI.Handle<XylemUI.UiNode>;

namespace XylemUI.E2E;

// The costs of a tooltip (row P02d2, design §13.1, "Tooltip"): a tooltip of a line of text on a 100 x 30 box over the todo sample
// with 200 items, in a window of 420 x 360 with text and no GPU. Its delays are 0, so that the pointer's move onto the box shows it
// in the same frame with no timer, and the move away hides it. The frame that shows it is timed whole, input and frame; the move away
// after it is not. With it shown, a frame of the pointer moved inside the box is timed, with the bytes 1,000 such frames allocate;
// and, to compare, the same frame inside a box of the same size beside it with no tooltip (PR #564's review).
internal static class TooltipCost
{
    private const int Samples = 31;
    private const int Frames = 1_000;

    public static int Run()
    {
        UiThread.Initialize();
        using var report = new Report("tooltip");
        using var scene = new TooltipScene();

        var show = new double[Samples];
        for (int i = -3; i < Samples; i++)
        {
            double us = scene.TimeShow();
            scene.Away();
            if (i >= 0)
            {
                show[i] = us;
            }
        }

        report.Number("show_frame_us_p50", Median(show));
        (double moveUs, long moveBytes) = scene.TimeMoves();
        report.Number("move_frame_us_p50", moveUs);
        report.Number("move_bytes_per_frame", (double)moveBytes / Frames);
        report.Number("bare_move_frame_us_p50", scene.TimeBareMoves());
        return 0;
    }

    private static double Median(double[] values)
    {
        Array.Sort(values);
        return values[values.Length / 2];
    }

    private sealed class TooltipScene : IDisposable
    {
        private readonly DirectWriteFontProvider _fonts = new();
        private readonly GlyphAtlas _atlas = new(static () => new DirectWriteGlyphRasterizer());
        private readonly UiTree _tree;
        private readonly WindowFrames _frames;
        private readonly WindowInput _input;
        private readonly ScopeHandle _scope;

        public TooltipScene()
        {
            _tree = new UiTree(new TextContext(_fonts));
            NodeHandle root = _tree.CreateNode(new LayoutStyle());
            _frames = new WindowFrames(_tree, root, new NoTarget(), _atlas, new NoScheduler(), default);
            _input = new WindowInput(_tree, _frames, new NoPlatform());
            var model = new TodoModel();
            var options = new TooltipOptions { ShowDelay = TimeSpan.Zero, HideDelay = TimeSpan.Zero };
            UiTree.Current = _tree;
            try
            {
                _scope = Scope.Create(() =>
                {
                    for (int i = 0; i < 200; i++)
                    {
                        _ = model.Add($"Item {i + 1}");
                    }

                    Element anchor = Box().Width(100).Height(30).Tooltip(() => Elements.Text("Saves the file").Padding(6, 3), options);
                    _tree.AppendChild(root, Column(Row(anchor, Box().Width(100).Height(30)), TodoApp.Todo(model)).Node);
                });
            }
            finally
            {
                UiTree.Current = null;
            }

            _input.SetActive(true);
            _frames.Resize(new Size(420, 360), 1);
            _frames.RunFrame();
            Away();
        }

        // The microseconds of the move onto the box and the frame that shows the tooltip.
        public double TimeShow()
        {
            long start = Stopwatch.GetTimestamp();
            Move(50, 15);
            _frames.RunFrame();
            double us = Stopwatch.GetElapsedTime(start).TotalMicroseconds;
            if (_tree.Popups.Count != 1)
            {
                throw new InvalidOperationException("The move onto the box did not show the tooltip.");
            }

            return us;
        }

        public void Away()
        {
            Move(400, 350);
            _frames.RunFrame();
        }

        // The median microseconds of a frame of a move inside the box with the tooltip shown, and the bytes of 1,000 after a warm-up.
        public (double Us, long Bytes) TimeMoves()
        {
            Move(50, 15);
            _frames.RunFrame();
            for (int i = 0; i < 100; i++)
            {
                Frame(i);
            }

            if (_tree.Popups.Count != 1)
            {
                throw new InvalidOperationException("The tooltip did not stay shown while the pointer moved in the box.");
            }

            var times = new double[Samples];
            for (int i = 0; i < Samples; i++)
            {
                long start = Stopwatch.GetTimestamp();
                Frame(i);
                times[i] = Stopwatch.GetElapsedTime(start).TotalMicroseconds;
            }

            // The median first: its sort allocates the first time, and is not a frame's.
            double median = Median(times);
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < Frames; i++)
            {
                Frame(i);
            }

            return (median, GC.GetAllocatedBytesForCurrentThread() - before);
        }

        // The median microseconds of a frame of a move inside the box with no tooltip, beside the anchor.
        public double TimeBareMoves()
        {
            Away();
            for (int i = 0; i < 100; i++)
            {
                BareFrame(i);
            }

            var times = new double[Samples];
            for (int i = 0; i < Samples; i++)
            {
                long start = Stopwatch.GetTimestamp();
                BareFrame(i);
                times[i] = Stopwatch.GetElapsedTime(start).TotalMicroseconds;
            }

            if (_tree.Popups.Count != 0)
            {
                throw new InvalidOperationException("A tooltip showed over the box with none.");
            }

            return Median(times);
        }

        public void Dispose()
        {
            Scope.Dispose(_scope);
            _atlas.Dispose();
            _fonts.Dispose();
        }

        private void BareFrame(int i)
        {
            Move(140 + (i & 7), 15);
            _frames.RunFrame();
        }

        private void Frame(int i)
        {
            Move(40 + (i & 7), 15);
            _frames.RunFrame();
        }

        private void Move(int x, int y) =>
            _input.Handle(
                new PointerInput(
                    PointerInputKind.Move,
                    x,
                    y,
                    PointerButton.None,
                    PointerButtons.None,
                    KeyModifiers.None,
                    0,
                    TimeSpan.Zero,
                    PointerType.Mouse
                )
            );
    }

    private sealed class NoTarget : IRenderTarget
    {
        public void Draw(ReadOnlySpan<DisplayList> lists, Color clearColor, GlyphAtlas atlas) { }

        public void Present() { }
    }

    private sealed class NoScheduler : IFrameScheduler
    {
        public void RequestFrame(Action callback) { }

        public void CancelFrame() { }
    }

    private sealed class NoPlatform : IPointerPlatform
    {
        public void Capture() { }

        public void ReleaseCapture() { }

        public void SetCursor(CursorKind cursor) { }

        public int ReadHistory(int pointerId, Span<PointerHistoryPoint> destination) => 0;
    }
}
