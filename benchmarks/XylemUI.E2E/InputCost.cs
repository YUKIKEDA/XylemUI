using System.Diagnostics;
using XylemUI.Text;
using NodeHandle = XylemUI.Handle<XylemUI.UiNode>;

namespace XylemUI.E2E;

// The cost of delivering the pointer's input (row V1c, design §7, "Pointer input"), with no GPU: a move over a leaf at the end of a
// chain of nested boxes, each with a move handler in both phases, through a route of depth 1 and of 50; and a move between two leaves
// that changes the hover (a leave and an enter, and the hovered state of the leaves). The display list is built once, as a frame
// builds it; the moves change nothing that asks for a frame, so each one is the input's work alone. `scoped` adds the handlers in a
// scope, as the component API does (row V1d, design §2.13): each call enters it and leaves it. A click is a press and a release
// on the first leaf, whose click handler counts. A wheel (row V2) is routed from a leaf in a scroll container 300 high whose content
// is 3,000 high, alternately down and up by 50 pixels, so that each one moves the container's scroll position (design §3.4.1). A key
// (row K1a) is routed from a focused leaf at the end of a chain of depth 1 and 50, each box with a key handler in both phases; a Tab
// walks a tree of 10,101 nodes whose 10,000 leaves are focusable (design §7.1).
internal static class InputCost
{
    private const int Moves = 10_000;

    public static int Run()
    {
        UiThread.Initialize();
        using var report = new Report("input");
        foreach (bool scoped in new[] { false, true })
        {
            string suffix = scoped ? "_scoped" : string.Empty;
            foreach (int depth in new[] { 1, 50 })
            {
                using var chain = new Chain(depth, scoped);
                report.Number($"us_per_move_depth_{depth}{suffix}_p50", Median(21, () => chain.Time(changeHover: false)));
                report.Number($"handlers_per_move_depth_{depth}{suffix}", chain.HandlersPerMove);
            }

            using var hover = new Chain(1, scoped);
            report.Number($"us_per_hover_change{suffix}_p50", Median(21, () => hover.Time(changeHover: true)));
            report.Number($"us_per_click{suffix}_p50", Median(21, hover.TimeClicks));
        }

        using var wheel = new WheelScroll();
        report.Number("us_per_wheel_p50", Median(21, wheel.Time));
        foreach (int depth in new[] { 1, 50 })
        {
            using var keys = new KeyChain(depth);
            report.Number($"us_per_key_depth_{depth}_p50", Median(21, keys.Time));
            report.Number($"key_handlers_per_key_depth_{depth}", keys.HandlersPerKey);
        }

        using var tabs = new TabTree();
        report.Number("us_per_tab_10101_nodes_p50", Median(21, tabs.Time));
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
        private WindowInput _input = null!;
        private ScopeHandle _scope;
        private int _handled;
        private int _moves;
        private int _clicks;

        public Chain(int depth, bool scoped)
        {
            var tree = new UiTree();
            if (scoped)
            {
                _scope = Scope.Create(() => _input = Build(tree, depth));
            }
            else
            {
                _input = Build(tree, depth);
            }
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

        // The microseconds of one click, a press and a release on the first leaf. Clicks as many before, unmeasured.
        public double TimeClicks()
        {
            Click();
            long start = Stopwatch.GetTimestamp();
            Click();
            double microseconds = Stopwatch.GetElapsedTime(start).TotalMicroseconds / InputCost.Moves;
            if (_clicks != 2 * InputCost.Moves)
            {
                throw new InvalidOperationException($"The leaf counted {_clicks} clicks of {2 * InputCost.Moves}.");
            }

            _clicks = 0;
            return microseconds;
        }

        public void Dispose()
        {
            if (!_scope.IsNone)
            {
                Scope.Dispose(_scope);
            }

            _atlas.Dispose();
        }

        private WindowInput Build(UiTree tree, int depth)
        {
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
                if (x == 0)
                {
                    tree.AddPointerHandler(leaf, PointerEventKind.Click, EventPhase.Bubble, (in PointerEvent _) => _clicks++);
                }
            }

            var frames = new WindowFrames(tree, root, new NoTarget(), _atlas, new NoScheduler(), default);
            frames.Resize(new Size(400, 300), 1);
            return new WindowInput(tree, frames, new NoPlatform());
        }

        private void Click()
        {
            for (int i = 0; i < InputCost.Moves; i++)
            {
                _input.Handle(
                    new PointerInput(PointerInputKind.Down, 5, 5, PointerButton.Left, PointerButtons.Left, 0, 1, TimeSpan.Zero, PointerType.Mouse)
                );
                _input.Handle(
                    new PointerInput(PointerInputKind.Up, 5, 5, PointerButton.Left, PointerButtons.None, 0, 1, TimeSpan.Zero, PointerType.Mouse)
                );
            }
        }

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

    // A window of 400 x 300 whose root is a scroll container as large, holding a column 3,000 high of one box 10 high.
    private sealed class WheelScroll : IDisposable
    {
        private readonly GlyphAtlas _atlas = new(new NoGlyphs());
        private readonly WindowInput _input;
        private readonly ScrollState _state;

        public WheelScroll()
        {
            var tree = new UiTree();
            NodeHandle root = tree.CreateNode(new LayoutStyle { OverflowY = Overflow.Scroll });
            NodeHandle content = tree.CreateNode(new LayoutStyle { Height = Length.Px(3_000) });
            NodeHandle leaf = tree.CreateNode(new LayoutStyle { Height = Length.Px(10) });
            tree.AppendChild(root, content);
            tree.AppendChild(content, leaf);
            var frames = new WindowFrames(tree, root, new NoTarget(), _atlas, new NoScheduler(), default);
            frames.Resize(new Size(400, 300), 1);
            _input = new WindowInput(tree, frames, new NoPlatform());
            _state = tree.ScrollStateFor(root);
        }

        // The microseconds of one wheel that moves the position. Wheels as many before, unmeasured.
        public double Time()
        {
            Turn();
            long start = Stopwatch.GetTimestamp();
            Turn();
            double microseconds = Stopwatch.GetElapsedTime(start).TotalMicroseconds / InputCost.Moves;
            if (_state.OffsetValue.Y != 0)
            {
                throw new InvalidOperationException($"The wheels left the position at {_state.OffsetValue.Y}.");
            }

            return microseconds;
        }

        public void Dispose() => _atlas.Dispose();

        private void Turn()
        {
            for (int i = 0; i < InputCost.Moves; i++)
            {
                var delta = new System.Numerics.Vector2(0, (i & 1) == 0 ? 50 : -50);
                _input.Handle(new PointerInput(PointerInputKind.Wheel, 5, 5, PointerButton.None, 0, 0, 0, TimeSpan.Zero, PointerType.Mouse, delta));
            }
        }
    }

    // A window of 400 x 300 whose root holds a chain of nested boxes of the depth asked for, each with a key-down handler in both
    // phases, ending at a focusable leaf with the focus: a key goes through the whole chain (row K1a, design §7, "Keys and the focus").
    private sealed class KeyChain : IDisposable
    {
        private readonly GlyphAtlas _atlas = new(new NoGlyphs());
        private readonly WindowInput _input;
        private int _handled;
        private int _keys;

        public KeyChain(int depth)
        {
            var tree = new UiTree();
            NodeHandle root = tree.CreateNode(new LayoutStyle());
            NodeHandle node = root;
            KeyHandler count = (in KeyEvent _) => _handled++;
            for (int i = 0; i < depth; i++)
            {
                NodeHandle box = tree.CreateNode(new LayoutStyle { Width = Length.Px(300), Height = Length.Px(200) });
                tree.AppendChild(node, box);
                tree.AddKeyHandler(box, KeyEventKind.Down, EventPhase.Capture, count);
                tree.AddKeyHandler(box, KeyEventKind.Down, EventPhase.Bubble, count);
                node = box;
            }

            tree.SetFocusable(node, true);
            var frames = new WindowFrames(tree, root, new NoTarget(), _atlas, new NoScheduler(), default);
            frames.Resize(new Size(400, 300), 1);
            _input = new WindowInput(tree, frames, new NoPlatform());
            _input.SetActive(true);
            if (!tree.Focus.Focus(node))
            {
                throw new InvalidOperationException("The leaf of the chain did not take the focus.");
            }
        }

        public double HandlersPerKey => (double)_handled / _keys;

        // The microseconds of one key's down. Sends as many before, unmeasured.
        public double Time()
        {
            _handled = 0;
            _keys = 0;
            Press();
            long start = Stopwatch.GetTimestamp();
            Press();
            return Stopwatch.GetElapsedTime(start).TotalMicroseconds / InputCost.Moves;
        }

        public void Dispose() => _atlas.Dispose();

        private void Press()
        {
            for (int i = 0; i < InputCost.Moves; i++)
            {
                _ = _input.Handle(new KeyInput(KeyEventKind.Down, Key.A, PhysicalKey.KeyA, default, KeyModifiers.None, false, TimeSpan.Zero));
                _keys++;
            }
        }
    }

    // A window of 400 x 300 whose root holds 100 rows of 100 focusable leaves, 10,101 nodes: each Tab walks the tree once (design
    // §7.1) and moves the focus to the next leaf, with its focus-out, focus-in, and states.
    private sealed class TabTree : IDisposable
    {
        private const int Tabs = 1_000;
        private readonly GlyphAtlas _atlas = new(new NoGlyphs());
        private readonly WindowInput _input;
        private readonly UiTree _tree;

        public TabTree()
        {
            _tree = new UiTree();
            NodeHandle root = _tree.CreateNode(new LayoutStyle());
            for (int row = 0; row < 100; row++)
            {
                NodeHandle line = _tree.CreateNode(new LayoutStyle());
                _tree.AppendChild(root, line);
                for (int column = 0; column < 100; column++)
                {
                    NodeHandle leaf = _tree.CreateNode(new LayoutStyle { Width = Length.Px(2), Height = Length.Px(2) });
                    _tree.AppendChild(line, leaf);
                    _tree.SetFocusable(leaf, true);
                }
            }

            var frames = new WindowFrames(_tree, root, new NoTarget(), _atlas, new NoScheduler(), default);
            frames.Resize(new Size(400, 300), 1);
            _input = new WindowInput(_tree, frames, new NoPlatform());
            _input.SetActive(true);
        }

        // The microseconds of one Tab. Tabs as many before, unmeasured.
        public double Time()
        {
            Tab();
            long start = Stopwatch.GetTimestamp();
            Tab();
            return Stopwatch.GetElapsedTime(start).TotalMicroseconds / Tabs;
        }

        public void Dispose() => _atlas.Dispose();

        private void Tab()
        {
            for (int i = 0; i < Tabs; i++)
            {
                NodeHandle before = _tree.Focus.Focused;
                if (!_input.Handle(new KeyInput(KeyEventKind.Down, Key.Tab, PhysicalKey.Tab, default, KeyModifiers.None, false, TimeSpan.Zero)))
                {
                    throw new InvalidOperationException("Tab moved no focus.");
                }

                if (_tree.Focus.Focused == before)
                {
                    throw new InvalidOperationException("Tab left the focus where it was.");
                }
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
