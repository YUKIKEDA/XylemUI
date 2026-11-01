using System.Diagnostics;
using Todo;
using XylemUI.Platform.Windows;
using XylemUI.Primitives;
using XylemUI.Text;
using static XylemUI.Elements;
using static XylemUI.Primitives.Primitive;
using NodeHandle = XylemUI.Handle<XylemUI.UiNode>;

namespace XylemUI.E2E;

// The costs of a select (row P02d2, design §13.1, "Select"): a select of 10 options of text, each with a hovered and a focused look,
// 160 wide at the top left over the todo sample with 200 items, in a window of 420 x 360 with text and no GPU. The frame that opens
// it is timed whole, the key Enter and the frame, with whether a gen-0 collection ran in it; the close after it is not. With it open, a frame of Down (the focus to the next
// row, Home back at the end) and a frame of the pointer moved over the next row are timed whole, with the bytes 1,000 such frames
// allocate and the builds of the window's list they make.
internal static class SelectCost
{
    private const int Samples = 31;
    private const int Frames = 1_000;

    public static int Run()
    {
        UiThread.Initialize();
        using var report = new Report("select");
        using var scene = new SelectScene();
        report.Number("options", 10);

        // Each opening's frame is kept with whether a gen-0 collection ran in it, to see what spreads its range (PR #569's review).
        var open = new double[Samples];
        var withGc = new List<double>();
        var withoutGc = new List<double>();
        for (int i = -3; i < Samples; i++)
        {
            int collections = GC.CollectionCount(0);
            double us = scene.TimeOpen();
            bool collected = GC.CollectionCount(0) != collections;
            scene.Close();
            if (i >= 0)
            {
                open[i] = us;
                (collected ? withGc : withoutGc).Add(us);
            }
        }

        report.Number("open_frame_us_p50", Median(open));
        report.Number("open_frames_with_gen0_gc", withGc.Count);
        report.Number("open_frame_with_gc_us_p50", withGc.Count > 0 ? Median([.. withGc]) : -1);
        report.Number("open_frame_without_gc_us_p50", withoutGc.Count > 0 ? Median([.. withoutGc]) : -1);
        scene.Open();
        (double keyUs, long keyBytes, int keyBuilds) = scene.TimeFrames(scene.Down);
        report.Number("key_frame_us_p50", keyUs);
        report.Number("key_bytes_per_frame", (double)keyBytes / Frames);
        report.Number("key_window_builds", keyBuilds);
        (double hoverUs, long hoverBytes, int hoverBuilds) = scene.TimeFrames(scene.Hover);
        report.Number("hover_frame_us_p50", hoverUs);
        report.Number("hover_bytes_per_frame", (double)hoverBytes / Frames);
        report.Number("hover_window_builds", hoverBuilds);
        return 0;
    }

    private static double Median(double[] values)
    {
        Array.Sort(values);
        return values[values.Length / 2];
    }

    private sealed class SelectScene : IDisposable
    {
        private static readonly string[] Options = Enumerable.Range(1, 10).Select(i => $"Option {i}").ToArray();

        private readonly DirectWriteFontProvider _fonts = new();
        private readonly GlyphAtlas _atlas = new(static () => new DirectWriteGlyphRasterizer());
        private readonly Signal<string> _value = Signal.Create("Option 1");
        private readonly UiTree _tree;
        private readonly WindowFrames _frames;
        private readonly WindowInput _input;
        private readonly ScopeHandle _scope;
        private NodeHandle _box;
        private int _step;

        public SelectScene()
        {
            _tree = new UiTree(new TextContext(_fonts));
            NodeHandle root = _tree.CreateNode(new LayoutStyle());
            _frames = new WindowFrames(_tree, root, new NoTarget(), _atlas, new NoScheduler(), default);
            _input = new WindowInput(_tree, _frames, new NoPlatform());
            var model = new TodoModel();
            var look = new PaintStyle { Background = Color.FromRgb(0x3F4A66) };
            UiTree.Current = _tree;
            try
            {
                _scope = Scope.Create(() =>
                {
                    for (int i = 0; i < 200; i++)
                    {
                        _ = model.Add($"Item {i + 1}");
                    }

                    Element select = Select(
                            _value,
                            Options,
                            option => Elements.Text(option).Padding(8, 4).Hovered(look).Focused(look),
                            new SelectOptions<string> { Panel = list => list.Padding(4).Background(Color.FromRgb(0x2B3245)) }
                        )
                        .Width(160);
                    _box = select.Node;
                    _tree.AppendChild(root, Column(select, TodoApp.Todo(model)).Node);
                });
            }
            finally
            {
                UiTree.Current = null;
            }

            // The window is active, so that the select's focus moves.
            _input.SetActive(true);
            _frames.Resize(new Size(420, 360), 1);
            _frames.RunFrame();
            _ = _tree.Focus.Focus(_box);
            _frames.RunFrame();
        }

        public void Open()
        {
            Key(XylemUI.Key.Enter);
            _frames.RunFrame();
        }

        public void Close()
        {
            Key(XylemUI.Key.Escape);
            _frames.RunFrame();
        }

        // The microseconds of Enter on the box and the frame that opens the list.
        public double TimeOpen()
        {
            long start = Stopwatch.GetTimestamp();
            Open();
            double us = Stopwatch.GetElapsedTime(start).TotalMicroseconds;
            if (_tree.Popups.Count != 1)
            {
                throw new InvalidOperationException("Enter did not open the list.");
            }

            return us;
        }

        // The median microseconds of a frame of an input over the samples, and the bytes and the window's builds of 1,000 such
        // frames after a warm-up.
        public (double Us, long Bytes, int Builds) TimeFrames(Action input)
        {
            for (int i = 0; i < 100; i++)
            {
                Frame(input);
            }

            // Each input moves the focus to another row; one that did not would time nothing.
            NodeHandle focused = _tree.Focus.Focused;
            Frame(input);
            if (_tree.Focus.Focused == focused || _tree.Popups.PopupContaining(_tree.Focus.Focused) is null)
            {
                throw new InvalidOperationException("The input did not move the focus between the list's rows.");
            }

            var times = new double[Samples];
            for (int i = 0; i < Samples; i++)
            {
                long start = Stopwatch.GetTimestamp();
                Frame(input);
                times[i] = Stopwatch.GetElapsedTime(start).TotalMicroseconds;
            }

            // The median first: its sort allocates the first time, and is not a frame's.
            double median = Median(times);
            int builds = _frames.MainBuilds;
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < Frames; i++)
            {
                Frame(input);
            }

            long bytes = GC.GetAllocatedBytesForCurrentThread() - before;
            return (median, bytes, _frames.MainBuilds - builds);
        }

        // Down, or Home at the last row, which the list stops at.
        public void Down()
        {
            _step = (_step + 1) % 10;
            Key(_step == 0 ? XylemUI.Key.Home : XylemUI.Key.Down);
        }

        // The pointer over the row after the one it was over, among the first 6, which the list's 200 px show: the rows are about 28 px
        // high from y 32 (the box and the panel's padding).
        public void Hover()
        {
            _step = (_step + 1) % 6;
            int y = 32 + 14 + (int)(_step * 28.4f);
            _input.Handle(
                new PointerInput(
                    PointerInputKind.Move,
                    60,
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

        public void Dispose()
        {
            Scope.Dispose(_scope);
            _atlas.Dispose();
            _fonts.Dispose();
        }

        private void Key(Key key) =>
            _ = _input.Handle(new KeyInput(KeyEventKind.Down, key, PhysicalKey.None, default, KeyModifiers.None, false, TimeSpan.Zero));

        private void Frame(Action input)
        {
            input();
            _frames.RunFrame();
        }
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
