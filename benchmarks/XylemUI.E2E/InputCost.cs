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
// walks a tree of 10,101 nodes whose 10,000 leaves are focusable (design §7.1), whose root is also disabled and enabled again (row
// K1b). A key that scrolls moves a scroll container from a focused leaf in it, and Enter makes a click from that leaf. A pen's move goes
// through the same chains as the mouse's; a touch's tap is a down, an up, and the end of the touch on the first leaf; and a pen's move
// brings 8 coalesced points that the first leaf's handler reads (row I1a, design §7, "Touch and pen"). A touch's drag moves the wheel's
// scroll container 1 pixel up and down by turns; a fling's frame moves a container 100,000 high one step of a fling at 8,000 pixels a
// second; and a frame reads its time from the system's clock (row I1b).
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

        foreach (int depth in new[] { 1, 50 })
        {
            using var pens = new Chain(depth, scoped: false);
            report.Number($"us_per_pen_move_depth_{depth}_p50", Median(21, () => pens.Time(changeHover: false, PointerType.Pen)));
        }

        using (var taps = new Chain(1, scoped: false))
        {
            report.Number("us_per_touch_tap_p50", Median(21, taps.TimeTaps));
        }

        using (var points = new Chain(1, scoped: false, readPoints: true))
        {
            report.Number("us_per_pen_move_8_points_p50", Median(21, points.TimePoints));
        }

        using var wheel = new WheelScroll();
        report.Number("us_per_wheel_p50", Median(21, wheel.Time));
        report.Number("us_per_touch_drag_move_p50", Median(21, wheel.TimeDrag));
        report.Number("us_per_touchpad_pan_update_p50", Median(21, wheel.TimePan));
        using (var fling = new WheelScroll(100_000))
        {
            report.Number("us_per_fling_frame_p50", Median(21, fling.TimeFling));
        }

        report.Number("us_per_frame_time_read_p50", Median(21, TimeFrameClock));
        foreach (int depth in new[] { 1, 50 })
        {
            using var keys = new KeyChain(depth);
            report.Number($"us_per_key_depth_{depth}_p50", Median(21, keys.Time));
            report.Number($"key_handlers_per_key_depth_{depth}", keys.HandlersPerKey);
        }

        using var tabs = new TabTree();
        report.Number("us_per_tab_10101_nodes_p50", Median(21, tabs.Time));
        report.Number("us_per_disabled_change_10101_nodes_p50", Median(21, tabs.TimeDisabled));

        using var keyScroll = new KeyScroll();
        report.Number("us_per_scroll_key_p50", Median(21, keyScroll.TimeScroll));
        report.Number("us_per_key_click_p50", Median(21, keyScroll.TimeClick));
        return 0;
    }

    // The microseconds of one read of the frame's time from the system's clock (WindowFrames.RunFrame), 10,000 reads.
    private static double TimeFrameClock()
    {
        TimeSpan last = default;
        long start = Stopwatch.GetTimestamp();
        for (int i = 0; i < Moves; i++)
        {
            last = TimeProvider.System.GetElapsedTime(0);
        }

        double microseconds = Stopwatch.GetElapsedTime(start).TotalMicroseconds / Moves;
        return last == default ? -1 : microseconds;
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
        private readonly bool _readPoints;
        private WindowInput _input = null!;
        private ScopeHandle _scope;
        private int _handled;
        private int _moves;
        private int _clicks;
        private int _points;

        public Chain(int depth, bool scoped, bool readPoints = false)
        {
            _readPoints = readPoints;
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
        public double Time(bool changeHover, PointerType type = PointerType.Mouse)
        {
            _handled = 0;
            _moves = 0;
            Move(changeHover, type);
            long start = Stopwatch.GetTimestamp();
            Move(changeHover, type);
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

        // The microseconds of one touch's tap on the first leaf: its down, its up, which clicks, and its end. Taps as many before,
        // unmeasured.
        public double TimeTaps()
        {
            Tap();
            long start = Stopwatch.GetTimestamp();
            Tap();
            double microseconds = Stopwatch.GetElapsedTime(start).TotalMicroseconds / InputCost.Moves;
            if (_clicks != 2 * InputCost.Moves)
            {
                throw new InvalidOperationException($"The leaf counted {_clicks} taps of {2 * InputCost.Moves}.");
            }

            _clicks = 0;
            return microseconds;
        }

        // The microseconds of one pen's move over the first leaf with 8 coalesced points, which its handler reads. Moves as many
        // before, unmeasured.
        public double TimePoints()
        {
            _points = 0;
            MovePen(8);
            long start = Stopwatch.GetTimestamp();
            MovePen(8);
            double microseconds = Stopwatch.GetElapsedTime(start).TotalMicroseconds / InputCost.Moves;
            if (_points != 2 * 8 * InputCost.Moves)
            {
                throw new InvalidOperationException($"The leaf read {_points} points of {2 * 8 * InputCost.Moves}.");
            }

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
                    if (_readPoints)
                    {
                        tree.AddPointerHandler(
                            leaf,
                            PointerEventKind.Move,
                            EventPhase.Bubble,
                            (in PointerEvent e) => _points += e.GetIntermediatePoints().Length
                        );
                    }
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

        private void Move(bool changeHover, PointerType type)
        {
            int id = type == PointerType.Mouse ? 0 : 1;
            for (int i = 0; i < InputCost.Moves; i++)
            {
                int x = changeHover && (i & 1) == 1 ? 25 : 5;
                _input.Handle(new PointerInput(PointerInputKind.Move, x, 5, PointerButton.None, 0, 0, 0, TimeSpan.Zero, type, PointerId: id));
                _moves++;
            }
        }

        private void Tap()
        {
            for (int i = 0; i < InputCost.Moves; i++)
            {
                _input.Handle(Touch(PointerInputKind.Down, PointerButton.Left, PointerButtons.Left));
                _input.Handle(Touch(PointerInputKind.Up, PointerButton.Left, PointerButtons.None));
                _input.Handle(Touch(PointerInputKind.Leave, PointerButton.None, PointerButtons.None));
            }
        }

        private static PointerInput Touch(PointerInputKind kind, PointerButton button, PointerButtons buttons) =>
            new(kind, 5, 5, button, buttons, 0, 1, TimeSpan.Zero, PointerType.Touch, PointerId: 1, Pressure: buttons == 0 ? 0 : 0.5f);

        private void MovePen(int points)
        {
            for (int i = 0; i < InputCost.Moves; i++)
            {
                _input.Handle(
                    new PointerInput(
                        PointerInputKind.Move,
                        5,
                        5,
                        PointerButton.None,
                        0,
                        0,
                        0,
                        TimeSpan.Zero,
                        PointerType.Pen,
                        PointerId: 1,
                        HistoryCount: points
                    )
                );
            }
        }
    }

    // A window of 400 x 300 whose root is a scroll container as large, holding a column 3,000 high of one box 10 high.
    private sealed class WheelScroll : IDisposable
    {
        private readonly GlyphAtlas _atlas = new(new NoGlyphs());
        private readonly WindowInput _input;
        private readonly ScrollState _state;

        public WheelScroll(float height = 3_000)
        {
            var tree = new UiTree();
            NodeHandle root = tree.CreateNode(new LayoutStyle { OverflowY = Overflow.Scroll });
            NodeHandle content = tree.CreateNode(new LayoutStyle { Height = Length.Px(height) });
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

        // The microseconds of one move of a touch that drags the container, 1 pixel up and down by turns. Moves as many before,
        // unmeasured. The drag starts with a move of 50 up and a move of 10, and ends with a slow release, which does not fling.
        public double TimeDrag()
        {
            Touch(PointerInputKind.Down, 150, 0);
            Touch(PointerInputKind.Move, 100, 1);
            Touch(PointerInputKind.Move, 90, 2);
            Drag(90);
            long start = Stopwatch.GetTimestamp();
            Drag(90);
            double microseconds = Stopwatch.GetElapsedTime(start).TotalMicroseconds / InputCost.Moves;
            Touch(PointerInputKind.Up, 90, 1_000);
            Touch(PointerInputKind.Leave, 90, 1_000);
            if (_state.OffsetValue.Y != 10)
            {
                throw new InvalidOperationException($"The drag left the position at {_state.OffsetValue.Y}.");
            }

            _state.ScrollTo(default);
            return microseconds;
        }

        // The microseconds of one update of a touchpad's pan that the container took, 1 pixel up and down by turns (row I1c). Updates
        // as many before, unmeasured. The pan starts with an update of 20 up, past the slop, and an update of 10, and ends at once, so
        // slowly that it does not fling.
        public double TimePan()
        {
            Pan(PointerInputKind.PanStart, 0, 0);
            Pan(PointerInputKind.PanUpdate, -20, 1);
            Pan(PointerInputKind.PanUpdate, -10, 2);
            Panning();
            long start = Stopwatch.GetTimestamp();
            Panning();
            double microseconds = Stopwatch.GetElapsedTime(start).TotalMicroseconds / InputCost.Moves;
            Pan(PointerInputKind.PanEnd, 0, 1_000);
            if (_state.OffsetValue.Y != 10)
            {
                throw new InvalidOperationException($"The pan left the position at {_state.OffsetValue.Y}.");
            }

            _state.ScrollTo(default);
            return microseconds;
        }

        // The microseconds of one frame's step of a fling at 8,000 pixels a second, 0.05 ms apart so that it neither ends nor reaches
        // the end of the range. Steps as many before, unmeasured.
        public double TimeFling()
        {
            _state.ScrollTo(default);
            Touch(PointerInputKind.Down, 200, 0);
            for (int i = 1; i <= 10; i++)
            {
                Touch(PointerInputKind.Move, 200 - (10 * i), i);
            }

            Touch(PointerInputKind.Up, 100, 10);
            Touch(PointerInputKind.Leave, 100, 10);
            var time = TimeSpan.FromMilliseconds(10);
            for (int i = 0; i < InputCost.Moves; i++)
            {
                time += TimeSpan.FromMilliseconds(0.05);
                _ = _input.AdvanceFlings(time);
            }

            long start = Stopwatch.GetTimestamp();
            for (int i = 0; i < InputCost.Moves; i++)
            {
                time += TimeSpan.FromMilliseconds(0.05);
                if (!_input.AdvanceFlings(time))
                {
                    throw new InvalidOperationException("The fling ended.");
                }
            }

            double microseconds = Stopwatch.GetElapsedTime(start).TotalMicroseconds / InputCost.Moves;
            _state.ScrollTo(default);
            Touch(PointerInputKind.Down, 100, time.TotalMilliseconds + 1_000);
            Touch(PointerInputKind.Up, 100, time.TotalMilliseconds + 2_000);
            Touch(PointerInputKind.Leave, 100, time.TotalMilliseconds + 2_000);
            return microseconds;
        }

        public void Dispose() => _atlas.Dispose();

        private void Drag(int y)
        {
            for (int i = 0; i < InputCost.Moves; i++)
            {
                Touch(PointerInputKind.Move, (i & 1) == 0 ? y - 1 : y, 3);
            }
        }

        private void Panning()
        {
            for (int i = 0; i < InputCost.Moves; i++)
            {
                Pan(PointerInputKind.PanUpdate, (i & 1) == 0 ? -1 : 1, 3);
            }
        }

        private void Pan(PointerInputKind kind, float dy, double milliseconds) =>
            _input.Handle(
                new PointerInput(
                    kind,
                    5,
                    150,
                    PointerButton.None,
                    PointerButtons.None,
                    0,
                    0,
                    TimeSpan.FromMilliseconds(milliseconds),
                    PointerType.Touchpad,
                    new System.Numerics.Vector2(0, dy),
                    PointerId: 2
                )
            );

        private void Touch(PointerInputKind kind, int y, double milliseconds)
        {
            bool down = kind is PointerInputKind.Down or PointerInputKind.Move;
            _input.Handle(
                new PointerInput(
                    kind,
                    5,
                    y,
                    kind is PointerInputKind.Down or PointerInputKind.Up ? PointerButton.Left : PointerButton.None,
                    down ? PointerButtons.Left : PointerButtons.None,
                    0,
                    kind is PointerInputKind.Down or PointerInputKind.Up ? 1 : 0,
                    TimeSpan.FromMilliseconds(milliseconds),
                    PointerType.Touch,
                    PointerId: 1,
                    Pressure: down ? 0.5f : 0
                )
            );
        }

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

        // The microseconds of disabling the root and enabling it again, each of which sets the state of the 10,101 nodes (row K1b).
        // Times as many before, unmeasured.
        public double TimeDisabled()
        {
            const int Changes = 100;
            NodeHandle root = _tree.Root;
            for (int i = 0; i < Changes; i++)
            {
                _tree.SetDisabled(root, (i & 1) == 0);
            }

            long start = Stopwatch.GetTimestamp();
            for (int i = 0; i < Changes; i++)
            {
                _tree.SetDisabled(root, (i & 1) == 0);
            }

            double microseconds = Stopwatch.GetElapsedTime(start).TotalMicroseconds / Changes;
            if ((_tree.Nodes.GetRef(root).Pseudo & PseudoState.Disabled) != 0)
            {
                throw new InvalidOperationException("The root stayed disabled.");
            }

            return microseconds;
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

    // A window of 400 x 300 whose root is a scroll container as large, holding a column 3,000 high with a focused leaf: Down and Up
    // alternate, each moving the position by 50 (row K1b, design §7, "Keyboard scrolling"). The leaf's Enter makes a click, as the
    // button's does.
    private sealed class KeyScroll : IDisposable
    {
        private readonly GlyphAtlas _atlas = new(new NoGlyphs());
        private readonly WindowInput _input;
        private readonly UiTree _tree;
        private readonly NodeHandle _root;
        private int _clicks;

        public KeyScroll()
        {
            _tree = new UiTree();
            _root = _tree.CreateNode(new LayoutStyle { OverflowY = Overflow.Scroll });
            NodeHandle content = _tree.CreateNode(new LayoutStyle { Height = Length.Px(3_000) });
            NodeHandle leaf = _tree.CreateNode(new LayoutStyle { Height = Length.Px(10) });
            _tree.AppendChild(_root, content);
            _tree.AppendChild(content, leaf);
            _tree.SetFocusable(leaf, true);
            _tree.AddKeyHandler(
                leaf,
                KeyEventKind.Down,
                EventPhase.Bubble,
                static (in KeyEvent e) =>
                {
                    if (e.Key == Key.Enter)
                    {
                        e.StopPropagation();
                        e.Input.ClickFromKey(e.CurrentNode, e.Timestamp);
                    }
                }
            );
            _tree.AddPointerHandler(leaf, PointerEventKind.Click, EventPhase.Bubble, (in PointerEvent _) => _clicks++);
            var frames = new WindowFrames(_tree, _root, new NoTarget(), _atlas, new NoScheduler(), default);
            frames.Resize(new Size(400, 300), 1);
            _input = new WindowInput(_tree, frames, new NoPlatform());
            _input.SetActive(true);
            if (!_tree.Focus.Focus(leaf))
            {
                throw new InvalidOperationException("The leaf did not take the focus.");
            }
        }

        // The microseconds of one key that moves the position. Sends as many before, unmeasured.
        public double TimeScroll()
        {
            Press(scroll: true);
            long start = Stopwatch.GetTimestamp();
            Press(scroll: true);
            double microseconds = Stopwatch.GetElapsedTime(start).TotalMicroseconds / InputCost.Moves;
            if (_tree.ScrollOffsetOf(_root).Y != 0)
            {
                throw new InvalidOperationException($"The keys left the position at {_tree.ScrollOffsetOf(_root).Y}.");
            }

            return microseconds;
        }

        // The microseconds of one click from Enter. Clicks as many before, unmeasured.
        public double TimeClick()
        {
            _clicks = 0;
            Press(scroll: false);
            long start = Stopwatch.GetTimestamp();
            Press(scroll: false);
            double microseconds = Stopwatch.GetElapsedTime(start).TotalMicroseconds / InputCost.Moves;
            if (_clicks != 2 * InputCost.Moves)
            {
                throw new InvalidOperationException($"The leaf counted {_clicks} clicks of {2 * InputCost.Moves}.");
            }

            return microseconds;
        }

        public void Dispose() => _atlas.Dispose();

        private void Press(bool scroll)
        {
            for (int i = 0; i < InputCost.Moves; i++)
            {
                Key key =
                    !scroll ? Key.Enter
                    : (i & 1) == 0 ? Key.Down
                    : Key.Up;
                if (!_input.Handle(new KeyInput(KeyEventKind.Down, key, PhysicalKey.None, default, KeyModifiers.None, false, TimeSpan.Zero)))
                {
                    throw new InvalidOperationException($"The key {key} was not handled.");
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

        public void FramePresented() { }
    }

    private sealed class NoPlatform : IPointerPlatform
    {
        public void Capture() { }

        public void ReleaseCapture() { }

        public void SetCursor(CursorKind cursor) { }

        // A history of as many points as asked, as Win32 gives a pen's coalesced moves.
        public int ReadHistory(int pointerId, Span<PointerHistoryPoint> destination)
        {
            destination.Clear();
            return destination.Length;
        }
    }

    private sealed class NoGlyphs : IGlyphRasterizer
    {
        public GlyphBox Measure(in GlyphKey glyph) => throw new InvalidOperationException("No glyph is drawn.");

        public void Rasterize(in GlyphKey glyph, Span<byte> destination, int stride) => throw new InvalidOperationException("No glyph is drawn.");
    }
}
