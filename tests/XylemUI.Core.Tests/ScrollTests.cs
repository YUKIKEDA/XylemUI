using System.Numerics;
using XylemUI.Text;
using static XylemUI.Elements;
using static XylemUI.Tests.FrameDoubles;

namespace XylemUI.Tests;

// The scroll container of row V2 (design §3.4.1, "The scroll container"): a scroll position moves the children in the draw stage
// and the hit tests with no layout, the layout's range is written to the state after the pass, and the wheel scrolls the
// containers it is routed through. Its layout is checked against Chrome by Taffy's XML tests (TaffyFixtureTests).
[Collection(UiThreadGroup.Name)]
public sealed class ScrollTests(UiThreadFixture ui)
{
    private static readonly Color Gray = Color.FromRgb(0x808080);
    private static readonly Color Red = Color.FromRgb(0xCC3333);

    /// <summary>
    /// A scroll position moves the children in the next frame with no layout: their rectangles stay, and their commands are
    /// drawn the position higher, inside a clip of the scroll view's box.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A scroll view of 200 x 100 holding a column of ten boxes 50 high, each with a background; the tree's layout runs are
    ///   counted.
    ///
    /// Steps:
    /// - Mount; scroll to (0, 120); run the frame asked for.
    ///
    /// Expected:
    /// - No layout run; the boxes' rectangles are those before; the frame presented; the list has a clip of the scroll view's box,
    ///   and the first box's rectangle is drawn at y = −120.
    /// </remarks>
    [Fact]
    public void ScrollTo_MovesTheChildren_WithNoLayout()
    {
        ui.Invoke(() =>
        {
            var scene = new Scene();
            var state = new ScrollState();
            Element[] boxes = new Element[10];
            scene.Mount(() =>
            {
                for (int i = 0; i < boxes.Length; i++)
                {
                    boxes[i] = Box().Height(50).Background(new Paint(Gray));
                }

                return ScrollView(Column(boxes), state).Width(200).Height(100);
            });
            Rect[] before = boxes.Select(b => scene.Tree.Nodes.GetRef(b.Node).Layout.Rect).ToArray();
            int runs = 0;
            scene.Tree.LayoutRunHook = _ => runs++;
            int presents = scene.Target.Presents;

            state.ScrollTo(new Vector2(0, 120));
            scene.Scheduler.Run();

            Assert.Equal(0, runs);
            Assert.Equal(before, boxes.Select(b => scene.Tree.Nodes.GetRef(b.Node).Layout.Rect).ToArray());
            Assert.Equal(presents + 1, scene.Target.Presents);
            (List<Vector4> clips, List<FillRectCommand> fills) = Read(scene.Frames.List);
            Assert.Contains(new Vector4(0, 0, 200, 100), clips);
            Assert.Equal(-120, fills[0].Y);
            Assert.Equal(new Vector2(0, 120), state.Offset.Get());
            scene.Dispose();
        });
    }

    /// <summary>
    /// The layout's range is written to the state after the pass, once for a batch of changes, and a content that shrinks brings
    /// the position back into the range.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A scroll view of 200 x 100 holding a column of ten boxes 50 high; an effect counts its reads of the largest position.
    ///
    /// Steps:
    /// - Mount; read the range. Scroll to the end. Make the last box 0 high; run the frame asked for.
    ///
    /// Expected:
    /// - The largest position is (0, 400) and the viewport 200 x 100; the effect ran once at the mount. After the box shrank, the
    ///   largest position is (0, 350), the position is (0, 350), and the effect ran once more.
    /// </remarks>
    [Fact]
    public void Range_IsWrittenAfterTheLayout_AndKeepsThePositionInIt()
    {
        ui.Invoke(() =>
        {
            var scene = new Scene();
            var state = new ScrollState();
            Element[] boxes = new Element[10];
            int reads = 0;
            scene.Mount(() =>
            {
                for (int i = 0; i < boxes.Length; i++)
                {
                    boxes[i] = Box().Height(50);
                }

                return ScrollView(Column(boxes), state).Width(200).Height(100);
            });
            _ = Effect.Create(() =>
            {
                _ = state.MaxOffset.Get();
                reads++;
            });
            Assert.Equal(new Vector2(0, 400), state.MaxOffset.Get());
            Assert.Equal(new Size(200, 100), state.Viewport.Get());
            Assert.Equal(1, reads);

            state.ScrollTo(new Vector2(0, 1_000));
            Assert.Equal(new Vector2(0, 400), state.Offset.Get());
            _ = boxes[9].Height(0);
            scene.Scheduler.Run();

            Assert.Equal(new Vector2(0, 350), state.MaxOffset.Get());
            Assert.Equal(new Vector2(0, 350), state.Offset.Get());
            Assert.Equal(2, reads);
            scene.Dispose();
        });
    }

    /// <summary>
    /// A child scrolled into view is hit where it is drawn, and a child outside the scroll view's box is not hit there.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A window of 200 x 200 with a scroll view of 200 x 100 at its top, holding a column of ten boxes 50 high whose clicks are
    ///   counted by their index.
    ///
    /// Steps:
    /// - Click at (10, 150), below the scroll view, where the fourth box lies before it is clipped. Scroll to (0, 100); run the
    ///   frame; click at (10, 60).
    ///
    /// Expected:
    /// - The first click hits no box; the second clicks the fourth box (index 3), drawn at y = 50 to 100.
    /// </remarks>
    [Fact]
    public void Hit_FollowsThePosition_AndTheClip()
    {
        ui.Invoke(() =>
        {
            var scene = new Scene(height: 200);
            var state = new ScrollState();
            var clicked = new List<int>();
            scene.Mount(() =>
            {
                Element[] boxes = new Element[10];
                for (int i = 0; i < boxes.Length; i++)
                {
                    int index = i;
                    boxes[i] = Box().Height(50).OnClick(() => clicked.Add(index));
                }

                return ScrollView(Column(boxes), state).Width(200).Height(100);
            });

            scene.Click(10, 150);
            Assert.Empty(clicked);

            state.ScrollTo(new Vector2(0, 100));
            scene.Scheduler.Run();
            scene.Click(10, 60);
            Assert.Equal([3], clicked);
            scene.Dispose();
        });
    }

    /// <summary>
    /// The wheel scrolls the scroll container under the pointer by its amount, kept in the range, and the next frame draws it.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A scroll view of 200 x 100 holding a column of ten boxes 50 high (largest position 400).
    ///
    /// Steps:
    /// - Send a wheel of (0, 100) at (10, 10); run the frame. Send five more.
    ///
    /// Expected:
    /// - The position is (0, 100) and the frame presented; then (0, 400).
    /// </remarks>
    [Fact]
    public void Wheel_ScrollsTheContainerUnderThePointer()
    {
        ui.Invoke(() =>
        {
            var scene = new Scene();
            var state = new ScrollState();
            scene.Mount(() => ScrollView(Column(Boxes(10)), state).Width(200).Height(100));
            int presents = scene.Target.Presents;

            scene.Wheel(10, 10, new Vector2(0, 100));
            scene.Scheduler.Run();
            Assert.Equal(new Vector2(0, 100), state.Offset.Get());
            Assert.Equal(presents + 1, scene.Target.Presents);

            for (int i = 0; i < 5; i++)
            {
                scene.Wheel(10, 10, new Vector2(0, 100));
            }

            Assert.Equal(new Vector2(0, 400), state.Offset.Get());
            scene.Dispose();
        });
    }

    /// <summary>
    /// A scroll container the app gave no state keeps its range between wheels: the state the first wheel attaches gets the range
    /// at once, with no layout pass to write it (PR #348's review).
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A box of 200 x 100 whose height scrolls, with no state, holding a column of ten boxes 50 high (largest position 400).
    ///
    /// Steps:
    /// - Send a wheel of (0, 100) at (10, 10); run the frame; send another.
    ///
    /// Expected:
    /// - The state attached to the box is at (0, 200), and its largest position is (0, 400).
    /// </remarks>
    [Fact]
    public void Wheel_ContainerWithoutAState_KeepsItsRangeBetweenWheels()
    {
        ui.Invoke(() =>
        {
            var scene = new Scene();
            Element view = default;
            scene.Mount(() => view = Box(Column(Boxes(10))).Overflow(Overflow.Visible, Overflow.Scroll).Width(200).Height(100));

            scene.Wheel(10, 10, new Vector2(0, 100));
            scene.Scheduler.Run();
            scene.Wheel(10, 10, new Vector2(0, 100));

            ScrollState state = scene.Tree.ScrollStateOf(view.Node)!;
            Assert.Equal(new Vector2(0, 200), state.Offset.Get());
            Assert.Equal(new Vector2(0, 400), state.MaxOffset.Get());
            scene.Dispose();
        });
    }

    /// <summary>
    /// A position set before the first layout is kept until the state has a range, which then keeps it in itself; a state attached
    /// to a node already laid out gets the node's range at once.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - Two scroll views of 200 x 100 in a column, each holding a column of ten boxes 50 high (largest position 400); a state for
    ///   the first, and one for the second that is not attached at the mount.
    ///
    /// Steps:
    /// - Scroll the first state to (0, 120) before the mount and again to (0, 1,000) in the root function; mount. Attach the
    ///   second state to the second view; scroll it to (0, 50).
    ///
    /// Expected:
    /// - Before the mount the first state is at (0, 120); after it, at (0, 400). The second state's largest position is (0, 400)
    ///   once attached, and it is at (0, 50).
    /// </remarks>
    [Fact]
    public void ScrollTo_BeforeTheFirstLayout_IsKeptUntilTheRange()
    {
        ui.Invoke(() =>
        {
            var scene = new Scene(height: 200);
            var first = new ScrollState();
            var second = new ScrollState();
            Element secondView = default;
            first.ScrollTo(new Vector2(0, 120));
            Assert.Equal(new Vector2(0, 120), first.Offset.Get());
            scene.Mount(() =>
            {
                Element view = ScrollView(Column(Boxes(10)), first).Width(200).Height(100);
                first.ScrollTo(new Vector2(0, 1_000));
                secondView = ScrollView(Column(Boxes(10))).Width(200).Height(100);
                return Column(view, secondView);
            });
            Assert.Equal(new Vector2(0, 400), first.Offset.Get());

            _ = secondView.ScrollState(second);
            Assert.Equal(new Vector2(0, 400), second.MaxOffset.Get());
            second.ScrollTo(new Vector2(0, 50));
            Assert.Equal(new Vector2(0, 50), second.Offset.Get());
            scene.Dispose();
        });
    }

    /// <summary>
    /// A scroll container at the end of its range lets the wheel go on to the one outside it (scroll chaining), and one that moves
    /// stops it there.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - An outer scroll view of 200 x 100 holding a column of an inner scroll view of 200 x 50 (its column of two boxes 50 high,
    ///   largest position 50) and four boxes 50 high (outer largest position 150).
    ///
    /// Steps:
    /// - Send a wheel of (0, 30) at (10, 10), over the inner view; again; again.
    ///
    /// Expected:
    /// - Inner 30, outer 0; inner 50, outer 0 (the inner one moved, kept at its end); inner 50, outer 30.
    /// </remarks>
    [Fact]
    public void Wheel_AtTheEnd_GoesOnToTheOuterContainer()
    {
        ui.Invoke(() =>
        {
            var scene = new Scene();
            var outer = new ScrollState();
            var inner = new ScrollState();
            scene.Mount(() => ScrollView(Column([ScrollView(Column(Boxes(2)), inner).Height(50), .. Boxes(4)]), outer).Width(200).Height(100));

            scene.Wheel(10, 10, new Vector2(0, 30));
            Assert.Equal((30f, 0f), (inner.Offset.Get().Y, outer.Offset.Get().Y));
            scene.Wheel(10, 10, new Vector2(0, 30));
            Assert.Equal((50f, 0f), (inner.Offset.Get().Y, outer.Offset.Get().Y));
            scene.Wheel(10, 10, new Vector2(0, 30));
            Assert.Equal((50f, 30f), (inner.Offset.Get().Y, outer.Offset.Get().Y));
            scene.Dispose();
        });
    }

    /// <summary>
    /// A mouse's vertical wheel with Shift held scrolls across; a touch's does not turn, and an axis that is hidden is not scrolled
    /// by the wheel but is by its state.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A box of 100 x 100 that scrolls across (overflow Scroll and Visible) holding a row 200 wide of four boxes 50 wide; a box of 100 x
    ///   100 whose height is Hidden holding a column of four boxes 50 high.
    ///
    /// Steps:
    /// - Over the first, a mouse's wheel of (0, 30) with Shift; a touch's with Shift. Over the second, a wheel of (0, 30); then
    ///   scroll its state to (0, 30).
    ///
    /// Expected:
    /// - The first is at (30, 0) after the mouse's, and still after the touch's. The second is at 0 after the wheel and at (0, 30)
    ///   after its state.
    /// </remarks>
    [Fact]
    public void Wheel_ShiftGoesAcross_AndHiddenDoesNotScroll()
    {
        ui.Invoke(() =>
        {
            var scene = new Scene();
            var across = new ScrollState();
            var hidden = new ScrollState();
            scene.Mount(() =>
                Row(
                    Box(Row(Boxes(4, wide: true)).Width(200)).Overflow(Overflow.Scroll, Overflow.Visible).ScrollState(across).Width(100).Height(100),
                    Box(Column(Boxes(4))).Overflow(Overflow.Visible, Overflow.Hidden).ScrollState(hidden).Width(100).Height(100)
                )
            );

            scene.Wheel(10, 10, new Vector2(0, 30), KeyModifiers.Shift);
            Assert.Equal(new Vector2(30, 0), across.Offset.Get());
            scene.Wheel(10, 10, new Vector2(0, 30), KeyModifiers.Shift, PointerType.Touch);
            Assert.Equal(new Vector2(30, 0), across.Offset.Get());

            scene.Wheel(110, 10, new Vector2(0, 30));
            Assert.Equal(Vector2.Zero, hidden.Offset.Get());
            hidden.ScrollTo(new Vector2(0, 30));
            Assert.Equal(new Vector2(0, 30), hidden.Offset.Get());
            scene.Dispose();
        });
    }

    /// <summary>
    /// A handler of the wheel that stops it keeps the scroll containers around it from scrolling.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A scroll view of 200 x 100 holding a column of ten boxes 50 high, the first of which stops the wheel in the bubble phase.
    ///
    /// Steps:
    /// - Send a wheel of (0, 100) over the first box; then over the second.
    ///
    /// Expected:
    /// - The position stays 0 after the first; it is (0, 100) after the second.
    /// </remarks>
    [Fact]
    public void Wheel_StoppedByAHandler_DoesNotScroll()
    {
        ui.Invoke(() =>
        {
            var scene = new Scene();
            var state = new ScrollState();
            scene.Mount(() =>
            {
                Element[] boxes = Boxes(10);
                _ = boxes[0].On(PointerEventKind.Wheel, EventPhase.Bubble, (in PointerEvent e) => e.StopPropagation());
                return ScrollView(Column(boxes), state).Width(200).Height(100);
            });

            scene.Wheel(10, 10, new Vector2(0, 100));
            Assert.Equal(Vector2.Zero, state.Offset.Get());
            scene.Wheel(10, 60, new Vector2(0, 100));
            Assert.Equal(new Vector2(0, 100), state.Offset.Get());
            scene.Dispose();
        });
    }

    /// <summary>
    /// A scroll state follows the node it is attached to: attached to another node it leaves the first, and a node taken out of
    /// the tree leaves its state.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - Two scroll containers of the root scope.
    ///
    /// Steps:
    /// - Attach a state to the first, then to the second. Dispose the root scope.
    ///
    /// Expected:
    /// - After the second attach, the first has no state and the second has it. After the dispose, the state is attached to no tree.
    /// </remarks>
    [Fact]
    public void State_FollowsItsNode()
    {
        ui.Invoke(() =>
        {
            var scene = new Scene();
            var state = new ScrollState();
            Element first = default;
            Element second = default;
            scene.Mount(() =>
            {
                first = Box(Box()).Overflow(Overflow.Hidden, Overflow.Hidden).ScrollState(state);
                second = Box(Box()).Overflow(Overflow.Hidden, Overflow.Hidden).ScrollState(state);
                return Column(first, second);
            });

            Assert.Null(scene.Tree.ScrollStateOf(first.Node));
            Assert.Same(state, scene.Tree.ScrollStateOf(second.Node));
            scene.Dispose();
            Assert.Null(state.Tree);
        });
    }

    /// <summary>
    /// The wheel's steady state allocates nothing: scrolling down and up through a scroll view and the frames that draw it.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A scroll view of 200 x 100 holding a column of ten boxes 50 high with backgrounds; warmed up with the iteration.
    ///
    /// Steps:
    /// - 1,000 iterations in the windows of <c>SteadyStateAllocation</c>: a wheel of (0, 50), the frame, a wheel of (0, −50), the
    ///   frame.
    ///
    /// Expected:
    /// - 0 bytes in every window; each iteration presented twice.
    /// </remarks>
    [Fact]
    public void SteadyState_WheelFrames_AllocateNothing()
    {
        ui.Invoke(() =>
        {
            var scene = new Scene();
            scene.Mount(() => ScrollView(Column(Boxes(10, background: true))).Width(200).Height(100));

            void Iteration(int index)
            {
                scene.Wheel(10, 10, new Vector2(0, 50));
                scene.Scheduler.Run();
                scene.Wheel(10, 10, new Vector2(0, -50));
                scene.Scheduler.Run();
            }

            for (int i = 0; i < 20; i++)
            {
                Iteration(i);
            }

            int presents = scene.Target.Presents;
            SteadyStateAllocation.AssertNone(1_000, Iteration);

            // The helper's unmeasured window runs 100 iterations before the 1,000.
            Assert.Equal(presents + 2_200, scene.Target.Presents);
            scene.Dispose();
        });
    }

    private static Element[] Boxes(int count, bool wide = false, bool background = false)
    {
        var boxes = new Element[count];
        for (int i = 0; i < count; i++)
        {
            boxes[i] = wide ? Box().Width(50).Height(50) : Box().Height(50);
            if (background)
            {
                _ = boxes[i].Background(new Paint((i & 1) == 0 ? Gray : Red));
            }
        }

        return boxes;
    }

    // The clip rectangles of the list's states (left, top, right, bottom) and its rectangles, in order.
    private static (List<Vector4> Clips, List<FillRectCommand> Fills) Read(DisplayList list)
    {
        var clips = new List<Vector4>();
        var fills = new List<FillRectCommand>();
        DisplayListReader reader = list.GetReader();
        while (reader.TryNext(out CommandTag tag))
        {
            switch (tag)
            {
                case CommandTag.FillRect:
                    fills.Add(reader.ReadFillRect());
                    break;
                case CommandTag.PushState:
                    clips.Add(reader.ReadState(out _).ClipRect);
                    break;
                case CommandTag.Pop:
                    reader.ReadPop();
                    break;
                case CommandTag.Shape:
                    _ = reader.ReadShape();
                    break;
                default:
                    throw new InvalidOperationException($"The test reads no {tag}.");
            }
        }

        return (clips, fills);
    }

    // A window of 200 x the height given, in logical pixels at scale 1, with no text: its tree, frames, and input.
    private sealed class Scene(float height = 100) : IDisposable
    {
        private readonly GlyphAtlas _atlas = new(new NoGlyphs());
        private ScopeHandle _scope;

        public UiTree Tree { get; } = new();

        public CountingTarget Target { get; } = new();

        public CountingScheduler Scheduler { get; } = new();

        public WindowFrames Frames { get; private set; } = null!;

        public WindowInput Input { get; private set; } = null!;

        public void Mount(Func<Element> root)
        {
            NodeHandle rootNode = Tree.CreateNode(new LayoutStyle());
            Frames = new WindowFrames(Tree, rootNode, Target, _atlas, Scheduler, Gray);
            Input = new WindowInput(Tree, Frames, new NoPlatform());
            UiTree.Current = Tree;
            try
            {
                _scope = Scope.Create(() => Tree.AppendChild(rootNode, root().Node));
            }
            finally
            {
                UiTree.Current = null;
            }

            Frames.Resize(new Size(200, height), 1);
        }

        public void Click(int x, int y)
        {
            Send(PointerInputKind.Down, x, y, PointerButton.Left, PointerButtons.Left, default, KeyModifiers.None, PointerType.Mouse);
            Send(PointerInputKind.Up, x, y, PointerButton.Left, PointerButtons.None, default, KeyModifiers.None, PointerType.Mouse);
        }

        public void Wheel(int x, int y, Vector2 delta, KeyModifiers modifiers = KeyModifiers.None, PointerType type = PointerType.Mouse) =>
            Send(PointerInputKind.Wheel, x, y, PointerButton.None, PointerButtons.None, delta, modifiers, type);

        public void Dispose()
        {
            if (Scope.IsAlive(_scope))
            {
                Scope.Dispose(_scope);
            }

            _atlas.Dispose();
        }

        private void Send(
            PointerInputKind kind,
            int x,
            int y,
            PointerButton button,
            PointerButtons buttons,
            Vector2 delta,
            KeyModifiers modifiers,
            PointerType type
        ) =>
            Input.Handle(
                new PointerInput(kind, x, y, button, buttons, modifiers, kind == PointerInputKind.Wheel ? 0 : 1, TimeSpan.Zero, type, delta)
            );
    }

    private sealed class NoPlatform : IPointerPlatform
    {
        public void Capture() { }

        public void ReleaseCapture() { }

        public void SetCursor(CursorKind cursor) { }
    }
}
