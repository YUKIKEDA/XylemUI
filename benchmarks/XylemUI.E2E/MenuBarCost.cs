using System.Diagnostics;
using Todo;
using XylemUI.Platform.Windows;
using XylemUI.Text;
using static XylemUI.Elements;
using static XylemUI.Primitives.Primitive;
using NodeHandle = XylemUI.Handle<XylemUI.UiNode>;

namespace XylemUI.E2E;

// The costs of a menu bar (row P02d3, design §13.1, "The menu bar"): a bar of 5 items of text over the todo sample with 200 items,
// each item with a focused look and a menu of 10 items of text with a focused look, in a window of 420 x 360 with text and no GPU.
// Each value is the median of the frame of one key, timed whole, input and frame:
//
// - select_frame_us: F10 selects the bar (the first item focused, the access keys shown); an F10 after each, untimed, leaves it.
// - move_frame_us: Right in the selected bar, the focus to the next item, round at the end; with the bytes of 1,000 such frames and
//   the builds of the window's list they make.
// - open_frame_us: Down in the selected bar opens the item's menu; an Esc after each, untimed, closes it.
// - switch_frame_us: Right in an open menu closes it and opens the next item's menu; with the bytes of 1,000 such frames.
internal static class MenuBarCost
{
    private const int Samples = 31;
    private const int Frames = 1_000;

    public static int Run()
    {
        UiThread.Initialize();
        using var report = new Report("menubar");
        using var scene = new MenuBarScene();
        report.Number("items", 5);

        report.Number("select_frame_us_p50", scene.TimeEach(Key.F10, Key.F10, open: false));

        scene.Press(Key.F10);
        (double moveUs, long moveBytes, int moveBuilds) = scene.TimeFrames(Key.Right, open: false);
        report.Number("move_frame_us_p50", moveUs);
        report.Number("move_bytes_per_frame", (double)moveBytes / Frames);
        report.Number("move_window_builds", moveBuilds);

        report.Number("open_frame_us_p50", scene.TimeEach(Key.Down, Key.Escape, open: true));

        scene.Press(Key.Down);
        (double switchUs, long switchBytes, _) = scene.TimeFrames(Key.Right, open: true);
        report.Number("switch_frame_us_p50", switchUs);
        report.Number("switch_bytes_per_frame", (double)switchBytes / Frames);
        return 0;
    }

    private static double Median(double[] values)
    {
        Array.Sort(values);
        return values[values.Length / 2];
    }

    // The bar above the todo sample.
    private sealed class MenuBarScene : IDisposable
    {
        private readonly DirectWriteFontProvider _fonts = new();
        private readonly GlyphAtlas _atlas = new(static () => new DirectWriteGlyphRasterizer());
        private readonly UiTree _tree;
        private readonly WindowFrames _frames;
        private readonly WindowInput _input;
        private readonly ScopeHandle _scope;
        private readonly NodeHandle _bar;

        public MenuBarScene()
        {
            _tree = new UiTree(new TextContext(_fonts));
            NodeHandle root = _tree.CreateNode(new LayoutStyle());
            _frames = new WindowFrames(_tree, root, new NoTarget(), _atlas, new NoScheduler(), default);
            _input = new WindowInput(_tree, _frames, new NoPlatform());
            var model = new TodoModel();
            NodeHandle bar = default;
            UiTree.Current = _tree;
            try
            {
                _scope = Scope.Create(() =>
                {
                    for (int i = 0; i < 200; i++)
                    {
                        _ = model.Add($"Item {i + 1}");
                    }

                    string[] names = ["_File", "_Edit", "_View", "_Tools", "_Help"];
                    var items = new Element[names.Length];
                    for (int i = 0; i < items.Length; i++)
                    {
                        items[i] = MenuBarItem(AccessText(names[i]).Padding(8, 4), Panel)
                            .Focused(new PaintStyle { Background = Color.FromRgb(0x8090B0) });
                    }

                    Element menuBar = MenuBar(items).Background(Color.FromRgb(0xC0C0C0));
                    bar = menuBar.Node;
                    _tree.AppendChild(root, Column(menuBar, TodoApp.Todo(model)).Node);
                });
            }
            finally
            {
                UiTree.Current = null;
            }

            _bar = bar;

            // The window is active, so that the focus moves.
            _input.SetActive(true);
            _frames.Resize(new Size(420, 360), 1);
            _frames.RunFrame();
        }

        // The down of a key with no modifier, and its frame.
        public void Press(Key key)
        {
            _ = _input.Handle(new KeyInput(KeyEventKind.Down, key, PhysicalKey.None, default, KeyModifiers.None, false, TimeSpan.Zero));
            _frames.RunFrame();
        }

        // The median microseconds of the frame of a key, each followed by an untimed one that undoes it; each is checked to have
        // selected the bar, or opened its menu.
        public double TimeEach(Key key, Key undo, bool open)
        {
            var times = new double[Samples];
            for (int i = -3; i < Samples; i++)
            {
                long start = Stopwatch.GetTimestamp();
                Press(key);
                double us = Stopwatch.GetElapsedTime(start).TotalMicroseconds;
                Check(open);
                Press(undo);
                if (i >= 0)
                {
                    times[i] = us;
                }
            }

            return Median(times);
        }

        // The median microseconds of the frame of a key over the samples, and the bytes and the window's builds of 1,000 such frames
        // after a warm-up; each key moves the focus to another item, of the bar or of the next menu.
        public (double Us, long Bytes, int Builds) TimeFrames(Key key, bool open)
        {
            for (int i = 0; i < 100; i++)
            {
                Press(key);
            }

            NodeHandle focused = _tree.Focus.Focused;
            Press(key);
            Check(open);
            if (_tree.Focus.Focused == focused)
            {
                throw new InvalidOperationException("The key did not move the focus.");
            }

            var times = new double[Samples];
            for (int i = 0; i < Samples; i++)
            {
                long start = Stopwatch.GetTimestamp();
                Press(key);
                times[i] = Stopwatch.GetElapsedTime(start).TotalMicroseconds;
            }

            double median = Median(times);
            int builds = _frames.MainBuilds;
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < Frames; i++)
            {
                Press(key);
            }

            long bytes = GC.GetAllocatedBytesForCurrentThread() - before;
            return (median, bytes, _frames.MainBuilds - builds);
        }

        public void Dispose()
        {
            Scope.Dispose(_scope);
            _atlas.Dispose();
            _fonts.Dispose();
        }

        // A menu's panel: 10 items of text with a focused background, in a column with a background and padding.
        private static Element Panel()
        {
            var items = new Element[10];
            for (int i = 0; i < items.Length; i++)
            {
                items[i] = MenuItem(Elements.Text($"Item {i + 1}").Padding(8, 4), static () => { })
                    .Focused(new PaintStyle { Background = Color.FromRgb(0x3F4A66) });
            }

            return Column(items).Width(160).Padding(4).Background(Color.FromRgb(0x2B3245)).Foreground(Color.FromRgb(0xFFFFFF));
        }

        // Checks where the focus is: on an item of the bar, or in an open menu; a key that did not get there times nothing.
        private void Check(bool open)
        {
            NodeHandle focused = _tree.Focus.Focused;
            bool onItem = !focused.IsNone && _tree.Nodes.GetRef(focused).Parent == _bar;
            bool inMenu = !focused.IsNone && _tree.Popups.PopupContaining(focused) is not null;
            if (open ? !inMenu : !onItem)
            {
                throw new InvalidOperationException("The key did not select the bar or open its menu.");
            }
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
