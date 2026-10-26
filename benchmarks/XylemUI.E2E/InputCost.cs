using System.Diagnostics;
using XylemUI.Text;
using NodeHandle = XylemUI.Handle<XylemUI.UiNode>;

namespace XylemUI.E2E;

// The cost of delivering the pointer's input (row V1c, design §7, "Pointer input"), with no GPU: a move over a leaf at the end of a
// chain of nested boxes, each with a move handler in both phases, through a route of depth 1 and of 50; and a move between two leaves
// that changes the hover (a leave and an enter, and the hovered state of the leaves). The display list is built once, as a frame
// builds it; the moves change nothing that asks for a frame, so each one is the input's work alone.
internal static class InputCost
{
    private const int Moves = 10_000;

    public static int Run()
    {
        UiThread.Initialize();
        using var report = new Report("input");
        foreach (int depth in new[] { 1, 50 })
        {
            using var chain = new Chain(depth);
            report.Number($"us_per_move_depth_{depth}_p50", Median(21, () => chain.Time(changeHover: false)));
            report.Number($"handlers_per_move_depth_{depth}", chain.HandlersPerMove);
        }

        using var hover = new Chain(1);
        report.Number("us_per_hover_change_p50", Median(21, () => hover.Time(changeHover: true)));
        return 0;
    }

    private static double Median(int samples, Func<double> sample)
    {
        var values = new double[samples];
        for (int i = 0; i < samples; i++)
        {
            values[i] = sample();
        }

        Array.Sort(values);
        return values[samples / 2];
    }

    // A window of 400 x 300 whose root holds a chain of nested boxes of the depth asked for, and two 10 x 10 leaves at its end.
    private sealed class Chain : IDisposable
    {
        private readonly GlyphAtlas _atlas = new(new NoGlyphs());
        private readonly WindowInput _input;
        private int _handled;
        private int _moves;

        public Chain(int depth)
        {
            var tree = new UiTree();
            NodeHandle root = tree.CreateNode(new LayoutStyle());
            NodeHandle parent = root;
            PointerHandler count = (in PointerEvent _) => _handled++;
            for (int i = 0; i < depth - 1; i++)
            {
                NodeHandle box = tree.CreateNode(new LayoutStyle { Width = Length.Px(300), Height = Length.Px(200) });
                tree.AppendChild(parent, box);
                tree.AddPointerHandler(box, PointerEventKind.Move, EventPhase.Capture, count);
                tree.AddPointerHandler(box, PointerEventKind.Move, EventPhase.Bubble, count);
                parent = box;
            }

            foreach (float x in new[] { 0f, 20f })
            {
                NodeHandle leaf = tree.CreateNode(
                    new LayoutStyle
                    {
                        Position = Position.Absolute,
                        Inset = new EdgeInsets<LengthPercentageAuto>(
                            LengthPercentageAuto.Px(x),
                            LengthPercentageAuto.Px(0),
                            LengthPercentageAuto.Auto,
                            LengthPercentageAuto.Auto
                        ),
                        Width = Length.Px(10),
                        Height = Length.Px(10),
                    }
                );
                tree.AppendChild(parent, leaf);
                tree.AddPointerHandler(leaf, PointerEventKind.Move, EventPhase.Capture, count);
                tree.AddPointerHandler(leaf, PointerEventKind.Move, EventPhase.Bubble, count);
                tree.SetHoveredStyle(leaf, new PaintStyle { Background = Color.FromRgb(0xE8E8E8) });
            }

            var frames = new WindowFrames(tree, root, new NoTarget(), _atlas, new NoScheduler(), default);
            frames.Resize(new Size(400, 300), 1);
            _input = new WindowInput(tree, frames, new NoPlatform());
        }

        public double HandlersPerMove => (double)_handled / _moves;

        // The microseconds of one move: over the first leaf, or alternately over each when the hover changes. Moves as many before,
        // unmeasured.
        public double Time(bool changeHover)
        {
            _handled = 0;
            _moves = 0;
            Move(changeHover);
            long start = Stopwatch.GetTimestamp();
            Move(changeHover);
            return Stopwatch.GetElapsedTime(start).TotalMicroseconds / InputCost.Moves;
        }

        public void Dispose() => _atlas.Dispose();

        private void Move(bool changeHover)
        {
            for (int i = 0; i < InputCost.Moves; i++)
            {
                int x = changeHover && (i & 1) == 1 ? 25 : 5;
                _input.Handle(new PointerInput(PointerInputKind.Move, x, 5, PointerButton.None, 0, 0, 0, TimeSpan.Zero, PointerType.Mouse));
                _moves++;
            }
        }
    }

    private sealed class NoTarget : IRenderTarget
    {
        public void Draw(DisplayList list, Color clearColor, GlyphAtlas atlas) { }

        public void Present() { }
    }

    // The hover's marks ask for frames, which nothing runs here: the moves time the input alone.
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
    }

    private sealed class NoGlyphs : IGlyphRasterizer
    {
        public GlyphBox Measure(in GlyphKey glyph) => throw new InvalidOperationException("No glyph is drawn.");

        public void Rasterize(in GlyphKey glyph, Span<byte> destination, int stride) => throw new InvalidOperationException("No glyph is drawn.");
    }
}
