using System.Diagnostics;
using Todo;
using XylemUI.Platform.Windows;
using XylemUI.Text;
using static XylemUI.Elements;
using static XylemUI.Primitives.Primitive;
using NodeHandle = XylemUI.Handle<XylemUI.UiNode>;

namespace XylemUI.E2E;

// The costs of a menu (row P02d, design §7.3, "Checks"): a menu of 10 items of text, each with a hovered and a focused look, opened
// from a box over the todo sample with 200 items, in a window of 420 x 360 with text and no GPU. The frame that opens it is timed in
// three parts, as the todo scene does: the change (the signal set, whose effect makes the popup's part), the layout pass, and the
// rest of the frame (the window's list and the popup's built, their glyphs rasterized by DirectWrite); the close after it is not
// timed. With the menu open, a frame of Down (the focus to the next item, round at the end) and a frame of the pointer moved over the
// next item (its hover and its focus) are timed whole, input and frame, with the bytes they allocate and the builds of the window's
// list they make.
internal static class MenuCost
{
    private const int Samples = 31;
    private const int Frames = 1_000;

    public static int Run()
    {
        UiThread.Initialize();
        using var report = new Report("menu");
        using var scene = new MenuScene();
        report.Number("items", 10);

        var change = new double[Samples];
        var layout = new double[Samples];
        var draw = new double[Samples];
        for (int i = -3; i < Samples; i++)
        {
            (double c, double l, double d) = scene.TimeOpen();
            scene.Close();
            if (i >= 0)
            {
                (change[i], layout[i], draw[i]) = (c, l, d);
            }
        }

        report.Number("open_change_us_p50", Median(change));
        report.Number("open_layout_us_p50", Median(layout));
        report.Number("open_draw_stage_us_p50", Median(draw));

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

    // The todo sample under a 100 x 30 box at the top left, with the menu below the box.
    private sealed class MenuScene : IDisposable
    {
        private readonly DirectWriteFontProvider _fonts = new();
        private readonly GlyphAtlas _atlas = new(static () => new DirectWriteGlyphRasterizer());
        private readonly Signal<bool> _open = Signal.Create(false);
        private readonly UiTree _tree;
        private readonly WindowFrames _frames;
        private readonly WindowInput _input;
        private readonly ScopeHandle _scope;
        private int _step;

        public MenuScene()
        {
            _tree = new UiTree(new TextContext(_fonts));
            NodeHandle root = _tree.CreateNode(new LayoutStyle());
            _frames = new WindowFrames(_tree, root, new NoTarget(), _atlas, new NoScheduler(), default);
            _input = new WindowInput(_tree, _frames, new NoPlatform());
            var model = new TodoModel();
            UiTree.Current = _tree;
            try
            {
                _scope = Scope.Create(() =>
                {
                    for (int i = 0; i < 200; i++)
                    {
                        _ = model.Add($"Item {i + 1}");
                    }

                    _tree.AppendChild(root, Column(Box().Width(100).Height(30).Menu(_open, Panel), TodoApp.Todo(model)).Node);
                });
            }
            finally
            {
                UiTree.Current = null;
            }

            // The window is active, so that the menu's focus moves.
            _input.SetActive(true);
            _frames.Resize(new Size(420, 360), 1);
            _frames.RunFrame();
        }

        public void Open()
        {
            _open.Set(true);
            _frames.RunFrame();
        }

        public void Close()
        {
            _open.Set(false);
            _frames.RunFrame();
        }

        // The microseconds of the signal set, of the layout pass after it, and of the rest of the frame.
        public (double Change, double Layout, double Draw) TimeOpen()
        {
            long start = Stopwatch.GetTimestamp();
            _open.Set(true);
            long changed = Stopwatch.GetTimestamp();
            LayoutEngine.RunLayoutPass(_tree);
            long laidOut = Stopwatch.GetTimestamp();
            _frames.RunFrame();
            long drawn = Stopwatch.GetTimestamp();
            return (
                Stopwatch.GetElapsedTime(start, changed).TotalMicroseconds,
                Stopwatch.GetElapsedTime(changed, laidOut).TotalMicroseconds,
                Stopwatch.GetElapsedTime(laidOut, drawn).TotalMicroseconds
            );
        }

        // The median microseconds of a frame of an input over the samples, and the bytes and the window's builds of 1,000 such
        // frames after a warm-up.
        public (double Us, long Bytes, int Builds) TimeFrames(Action input)
        {
            for (int i = 0; i < 100; i++)
            {
                Frame(input);
            }

            // Each input moves the focus to another item; one that did not would time nothing.
            NodeHandle focused = _tree.Focus.Focused;
            Frame(input);
            if (_tree.Focus.Focused == focused || _tree.Popups.PopupContaining(_tree.Focus.Focused) is null)
            {
                throw new InvalidOperationException("The input did not move the focus between the menu's items.");
            }

            var times = new double[Samples];
            for (int i = 0; i < Samples; i++)
            {
                long start = Stopwatch.GetTimestamp();
                Frame(input);
                times[i] = Stopwatch.GetElapsedTime(start).TotalMicroseconds;
            }

            int builds = _frames.MainBuilds;
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < Frames; i++)
            {
                Frame(input);
            }

            long bytes = GC.GetAllocatedBytesForCurrentThread() - before;
            return (Median(times), bytes, _frames.MainBuilds - builds);
        }

        public void Down() =>
            _ = _input.Handle(new KeyInput(KeyEventKind.Down, Key.Down, PhysicalKey.None, default, KeyModifiers.None, false, TimeSpan.Zero));

        // The pointer over the item after the one it was over: the items are 28.4 px high from y 34 (30 for the box, 4 for the panel's
        // padding).
        public void Hover()
        {
            _step = (_step + 1) % 10;
            int y = 34 + 14 + (int)(_step * 28.4f);
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

        private void Frame(Action input)
        {
            input();
            _frames.RunFrame();
        }

        // The menu's panel: 10 items of text with a hovered and a focused background, in a column with a background and padding.
        private static Element Panel()
        {
            var items = new Element[10];
            for (int i = 0; i < items.Length; i++)
            {
                items[i] = MenuItem(Elements.Text($"Item {i + 1}").Padding(8, 4), static () => { })
                    .Hovered(new PaintStyle { Background = Color.FromRgb(0x3F4A66) })
                    .Focused(new PaintStyle { Background = Color.FromRgb(0x3F4A66) });
            }

            return Column(items).Width(160).Padding(4).Background(Color.FromRgb(0x2B3245)).Foreground(Color.FromRgb(0xFFFFFF));
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
