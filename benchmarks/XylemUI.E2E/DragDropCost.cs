using System.Diagnostics;
using Todo;
using XylemUI.Platform.Windows;
using XylemUI.Text;
using NodeHandle = XylemUI.Handle<XylemUI.UiNode>;

namespace XylemUI.E2E;

// The costs of a drag in the app (row DD1, design §7, "Drag and drop"): the todo sample with 200 items, in a window of 420 x 360 with
// text and no GPU, whose rows are drag sources with a preview and drop targets with a drag-over look. A drag starts from the first
// row; each sample is one move of the pointer, alternately over the second and the third row, and its frame: the hit test, the
// target's leave and enter with its drag-over look, the preview moved, and the frame that draws them, timed whole, with the bytes of
// 1,000 such frames and the builds of the window's list they make.
internal static class DragDropCost
{
    private const int Samples = 31;
    private const int Frames = 1_000;

    public static int Run()
    {
        UiThread.Initialize();
        using var report = new Report("dragdrop");
        using var scene = new DragScene();
        report.Number("items", 200);
        scene.Start();
        (double us, long bytes, int builds) = scene.TimeMoves();
        report.Number("move_frame_us_p50", us);
        report.Number("move_bytes_per_frame", (double)bytes / Frames);
        report.Number("move_window_builds", builds);
        scene.Release();
        return 0;
    }

    private static double Median(double[] values)
    {
        Array.Sort(values);
        return values[values.Length / 2];
    }

    private sealed class DragScene : IDisposable
    {
        private readonly DirectWriteFontProvider _fonts = new();
        private readonly GlyphAtlas _atlas = new(static () => new DirectWriteGlyphRasterizer());
        private readonly UiTree _tree;
        private readonly WindowFrames _frames;
        private readonly WindowInput _input;
        private readonly ScopeHandle _scope;
        private readonly NodeHandle[] _rows = new NodeHandle[3];
        private int _step;

        public DragScene()
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

                    _tree.AppendChild(root, TodoApp.Todo(model).Node);
                });
            }
            finally
            {
                UiTree.Current = null;
            }

            _input.SetActive(true);
            _frames.Resize(new Size(420, 360), 1);
            _frames.RunFrame();

            // The rows are the drop targets, made in the order of the list.
            int found = 0;
            foreach (NodeHandle node in _tree.DropTargets!.Keys)
            {
                if (found < _rows.Length)
                {
                    _rows[found++] = node;
                }
            }
        }

        // Presses the first row and moves past the drag size; a drag that did not start times nothing.
        public void Start()
        {
            (int x, int y) = Center(_rows[0]);
            Send(PointerInputKind.Move, x, y, PointerButton.None, PointerButtons.None);
            Send(PointerInputKind.Down, x, y, PointerButton.Left, PointerButtons.Left);
            Send(PointerInputKind.Move, x + 10, y, PointerButton.None, PointerButtons.Left);
            _frames.RunFrame();
            if (!_input.Dragging)
            {
                throw new InvalidOperationException("The press did not start a drag.");
            }
        }

        public void Release()
        {
            (int x, int y) = Center(_rows[0]);
            Send(PointerInputKind.Up, x, y, PointerButton.Left, PointerButtons.None);
            _frames.RunFrame();
        }

        // The median microseconds of a move's frame over the samples, and the bytes and the window's builds of 1,000 after a warm-up.
        public (double Us, long Bytes, int Builds) TimeMoves()
        {
            for (int i = 0; i < 100; i++)
            {
                Move();
            }

            NodeHandle before = Over();
            Move();
            if (Over() == before || Over().IsNone)
            {
                throw new InvalidOperationException("The move did not change the drop target.");
            }

            var times = new double[Samples];
            for (int i = 0; i < Samples; i++)
            {
                long start = Stopwatch.GetTimestamp();
                Move();
                times[i] = Stopwatch.GetElapsedTime(start).TotalMicroseconds;
            }

            double median = Median(times);
            int builds = _frames.MainBuilds;
            long allocated = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < Frames; i++)
            {
                Move();
            }

            long bytes = GC.GetAllocatedBytesForCurrentThread() - allocated;
            return (median, bytes, _frames.MainBuilds - builds);
        }

        public void Dispose()
        {
            Scope.Dispose(_scope);
            _atlas.Dispose();
            _fonts.Dispose();
        }

        // A move over the second or the third row, and its frame.
        private void Move()
        {
            _step ^= 1;
            (int x, int y) = Center(_rows[1 + _step]);
            Send(PointerInputKind.Move, x, y, PointerButton.None, PointerButtons.Left);
            _frames.RunFrame();
        }

        // The row with the drag-over state, or none.
        private NodeHandle Over()
        {
            foreach (NodeHandle row in _rows)
            {
                if ((_tree.Nodes.GetRef(row).Pseudo & PseudoState.DragOver) != 0)
                {
                    return row;
                }
            }

            return default;
        }

        private (int X, int Y) Center(NodeHandle node)
        {
            Rect box = _frames.WindowBox(node);
            return ((int)(box.X + (box.Width / 2)), (int)(box.Y + (box.Height / 2)));
        }

        private void Send(PointerInputKind kind, int x, int y, PointerButton button, PointerButtons buttons) =>
            _input.Handle(new PointerInput(kind, x, y, button, buttons, KeyModifiers.None, 1, TimeSpan.Zero, PointerType.Mouse));
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
