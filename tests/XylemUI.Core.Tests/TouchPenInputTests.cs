using System.Numerics;
using XylemUI.Text;
using static XylemUI.Tests.FrameDoubles;
using static XylemUI.Tests.LayoutTestTree;

namespace XylemUI.Tests;

// The touches and pens of a window (row I1a, design §7, "Touch and pen"): a capture and a press per pointer, no hover for a touch,
// the pen's values, the slop of a tap, and the intermediate points. The inputs are made as the Win32 window gives them: integer
// client coordinates in physical pixels, a number per pointer.
[Collection(UiThreadGroup.Name)]
public sealed class TouchPenInputTests(UiThreadFixture ui)
{
    private static readonly Color Gray = Color.FromRgb(0x808080);
    private static readonly Color Red = Color.FromRgb(0xCC3333);

    /// <summary>
    /// Each touch holds its own capture and press: the moves of one go to the node it went down on while another touch is down on
    /// another node, a node pressed by two touches stays pressed until both are up, and a touch asks the platform for no capture.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - Boxes A (10, 10, 50 x 50) and B (100, 10, 50 x 50) with a pressed look; each logs its moves, ups, and clicks with the
    ///   pointer's number.
    ///
    /// Steps:
    /// - Touch 1 goes down on A at (20, 20); touch 2 down on B at (110, 20); touch 1 moves to (120, 30), over B; touch 2 goes up;
    ///   touch 1 goes back to (20, 20) and up. Then touches 1 and 2 go down on A; 1 goes up; 2 goes up.
    ///
    /// Expected:
    /// - Move 1 on A; up 2 on B, click 2 on B; move 1 on A, up 1 on A, and no click, since touch 1 moved past the slop. A stays
    ///   pressed after the first of the two ups and not after the second. The platform captured nothing.
    /// </remarks>
    [Fact]
    public void Touches_EachHoldsItsCaptureAndPress_AndAskTheWindowForNone() =>
        ui.Invoke(() =>
        {
            var scene = new Scene();
            NodeHandle a = scene.Place(scene.Root, 10, 10, 50, 50);
            NodeHandle b = scene.Place(scene.Root, 100, 10, 50, 50);
            var log = new List<string>();
            foreach ((NodeHandle node, string name) in new[] { (a, "A"), (b, "B") })
            {
                scene.Tree.SetPressedStyle(node, new PaintStyle { Background = Red });
                scene.Tree.AddPointerHandler(
                    node,
                    PointerEventKind.Move,
                    EventPhase.Bubble,
                    (in PointerEvent e) => log.Add($"move {e.PointerId} {name}")
                );
                scene.Tree.AddPointerHandler(
                    node,
                    PointerEventKind.Up,
                    EventPhase.Bubble,
                    (in PointerEvent e) => log.Add($"up {e.PointerId} {name}")
                );
                scene.Tree.AddPointerHandler(
                    node,
                    PointerEventKind.Click,
                    EventPhase.Bubble,
                    (in PointerEvent e) => log.Add($"click {e.PointerId} {name}")
                );
            }

            scene.Draw();

            scene.TouchDown(1, 20, 20);
            scene.TouchDown(2, 110, 20);
            scene.TouchMove(1, 120, 30);
            scene.TouchUp(2, 110, 20);
            scene.TouchMove(1, 20, 20);
            scene.TouchUp(1, 20, 20);
            Assert.Equal(["move 1 A", "up 2 B", "click 2 B", "move 1 A", "up 1 A"], log);

            scene.TouchDown(1, 20, 20);
            scene.TouchDown(2, 30, 30);
            scene.TouchUp(1, 20, 20);
            Assert.True(scene.Has(a, PseudoState.Pressed));
            scene.TouchUp(2, 30, 30);
            Assert.False(scene.Has(a, PseudoState.Pressed));
            Assert.Equal((0, 0), (scene.Platform.Captures, scene.Platform.Releases));
        });

    /// <summary>
    /// A touch hovers nothing: it sends no enter or leave and sets no hovered look. A pen hovers as the mouse does, and a node
    /// stays hovered while the mouse or a pen is over it.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A box (10, 10, 50 x 50) with a hovered look that logs its enters and leaves with the pointer's type.
    ///
    /// Steps:
    /// - A touch goes down at (20, 20), moves, goes up, and ends. The mouse moves to (20, 20); a pen comes to (30, 30); the mouse
    ///   leaves the window; the pen ends at (35, 30).
    ///
    /// Expected:
    /// - Nothing from the touch, and the box is not hovered while it is down. Enter Mouse, enter Pen; the box is still hovered when
    ///   the mouse leaves; leave Mouse, then leave Pen at (35, 30) when the pen ends.
    /// </remarks>
    [Fact]
    public void Hover_TouchNone_PenAndMouseEachTheirOwn() =>
        ui.Invoke(() =>
        {
            var scene = new Scene();
            NodeHandle box = scene.Place(scene.Root, 10, 10, 50, 50);
            scene.Tree.SetHoveredStyle(box, new PaintStyle { Background = Red });
            var log = new List<string>();
            scene.Tree.AddPointerHandler(box, PointerEventKind.Enter, EventPhase.Bubble, (in PointerEvent e) => log.Add($"enter {e.PointerType}"));
            scene.Tree.AddPointerHandler(
                box,
                PointerEventKind.Leave,
                EventPhase.Bubble,
                (in PointerEvent e) => log.Add($"leave {e.PointerType} {e.Position}")
            );
            scene.Draw();

            scene.TouchDown(1, 20, 20);
            Assert.False(scene.Has(box, PseudoState.Hovered));
            scene.TouchMove(1, 25, 25);
            scene.TouchUp(1, 25, 25);
            scene.End(1, PointerType.Touch);
            Assert.Empty(log);

            scene.Send(
                new PointerInput(
                    PointerInputKind.Move,
                    20,
                    20,
                    PointerButton.None,
                    PointerButtons.None,
                    KeyModifiers.None,
                    0,
                    TimeSpan.Zero,
                    PointerType.Mouse
                )
            );
            scene.Send(Scene.Pen(PointerInputKind.Move, 30, 30));
            scene.Send(
                new PointerInput(
                    PointerInputKind.Leave,
                    0,
                    0,
                    PointerButton.None,
                    PointerButtons.None,
                    KeyModifiers.None,
                    0,
                    TimeSpan.Zero,
                    PointerType.Mouse
                )
            );
            Assert.True(scene.Has(box, PseudoState.Hovered));
            scene.End(1, PointerType.Pen, 35, 30);

            Assert.Equal(["enter Mouse", "enter Pen", $"leave Mouse {Vector2.Zero}", $"leave Pen {new Vector2(35, 30)}"], log);
            Assert.False(scene.Has(box, PseudoState.Hovered));
        });

    /// <summary>
    /// An event gives a pen's number, whether it is primary, its pressure, its tilts, its twist, and its eraser and inverted
    /// marks as the platform gave them.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A box (10, 10, 50 x 50) whose down handler keeps the event's values.
    ///
    /// Steps:
    /// - A pen of number 3, not primary, presses at (20, 20) with pressure 0.25, tilts −30 and 45, twist 270, and both marks.
    ///
    /// Expected:
    /// - 3, not primary, 0.25, −30, 45, 270, eraser, inverted, the pen.
    /// </remarks>
    [Fact]
    public void PenValues_ComeAsTheyWereGiven() =>
        ui.Invoke(() =>
        {
            var scene = new Scene();
            NodeHandle box = scene.Place(scene.Root, 10, 10, 50, 50);
            object? seen = null;
            scene.Tree.AddPointerHandler(
                box,
                PointerEventKind.Down,
                EventPhase.Bubble,
                (in PointerEvent e) =>
                    seen = (e.PointerId, e.IsPrimary, e.Pressure, e.TiltX, e.TiltY, e.Twist, e.IsEraser, e.IsInverted, e.PointerType)
            );
            scene.Draw();

            scene.Send(
                Scene.Pen(PointerInputKind.Down, 20, 20, PointerButton.Left, PointerButtons.Left) with
                {
                    PointerId = 3,
                    IsPrimary = false,
                    Pressure = 0.25f,
                    TiltX = -30,
                    TiltY = 45,
                    Twist = 270,
                    PenFlags = PenFlags.Eraser | PenFlags.Inverted,
                }
            );

            Assert.Equal((3, false, 0.25f, -30f, 45f, 270f, true, true, PointerType.Pen), seen);
        });

    /// <summary>
    /// A touch that moves 18 logical pixels or less from where it went down still clicks; one that moves past them clicks no more,
    /// even when it comes back. The slop is in logical pixels, so that it is twice the physical pixels at a scale of 2.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A window at scale 2; a box (0, 0, 100 x 100 logical) logs its clicks.
    ///
    /// Steps:
    /// - Touch down at (40, 40) physical, move to (76, 40) (36 physical, 18 logical), up. Touch down at (40, 40), move to (77, 40),
    ///   back to (40, 40), up.
    ///
    /// Expected:
    /// - One click, from the first.
    /// </remarks>
    [Fact]
    public void TouchTap_WithinTheSlop_Clicks_PastIt_DoesNot() =>
        ui.Invoke(() =>
        {
            var scene = new Scene(scale: 2);
            NodeHandle box = scene.Place(scene.Root, 0, 0, 100, 100);
            int clicks = 0;
            scene.Tree.AddPointerHandler(box, PointerEventKind.Click, EventPhase.Bubble, (in PointerEvent _) => clicks++);
            scene.Draw();

            scene.TouchDown(1, 40, 40);
            scene.TouchMove(1, 76, 40);
            scene.TouchUp(1, 76, 40);
            Assert.Equal(1, clicks);

            scene.TouchDown(1, 40, 40);
            scene.TouchMove(1, 77, 40);
            scene.TouchMove(1, 40, 40);
            scene.TouchUp(1, 40, 40);
            Assert.Equal(1, clicks);
        });

    /// <summary>
    /// A tap's click carries the count the platform gave its press, and a click made from a key gives the mouse's number, primary,
    /// no pressure, tilt, twist, or marks, and no intermediate points.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A box (10, 10, 50 x 50) whose click handler keeps the event's values and the length of its intermediate points.
    ///
    /// Steps:
    /// - Touch 2 goes down at (20, 20) with a count of 2 and up with it. Make a click from a key on the box.
    ///
    /// Expected:
    /// - 2, touch 2. Then 0, primary, 0, 0, 0, 0, no marks, 0 points, the mouse.
    /// </remarks>
    [Fact]
    public void Click_FromATapCarriesItsCount_FromAKeyTheMousesValues() =>
        ui.Invoke(() =>
        {
            var scene = new Scene();
            NodeHandle box = scene.Place(scene.Root, 10, 10, 50, 50);
            var seen = new List<object>();
            scene.Tree.AddPointerHandler(
                box,
                PointerEventKind.Click,
                EventPhase.Bubble,
                (in PointerEvent e) =>
                    seen.Add(
                        (
                            e.ClickCount,
                            e.PointerId,
                            e.IsPrimary,
                            e.Pressure,
                            e.TiltX + e.TiltY + e.Twist,
                            e.IsEraser || e.IsInverted,
                            e.GetIntermediatePoints().Length,
                            e.PointerType
                        )
                    )
            );
            scene.Draw();

            scene.Send(Scene.Touch(PointerInputKind.Down, 2, 20, 20, PointerButton.Left, PointerButtons.Left) with { ClickCount = 2 });
            scene.Send(Scene.Touch(PointerInputKind.Up, 2, 20, 20, PointerButton.Left, PointerButtons.None) with { ClickCount = 2 });
            scene.Input.ClickFromKey(box, TimeSpan.Zero);

            Assert.Equal([(2, 2, false, 0f, 0f, false, 1, PointerType.Touch), (0, 0, true, 0f, 0f, false, 0, PointerType.Mouse)], seen);
        });

    /// <summary>
    /// A touch that ends while it is down cancels its press: the node it went down on gets a cancel and loses its pressed look,
    /// and the next touch of the same number starts with nothing of it.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A box (10, 10, 50 x 50) with a pressed look that logs its cancels, ups, and clicks.
    ///
    /// Steps:
    /// - Touch 1 goes down at (20, 20) and ends with no up. A new touch 1 goes down at (150, 80), outside the box, and up.
    ///
    /// Expected:
    /// - One cancel; the box is not pressed; nothing else.
    /// </remarks>
    [Fact]
    public void TouchEnd_WhilePressed_Cancels_AndFreesItsNumber() =>
        ui.Invoke(() =>
        {
            var scene = new Scene();
            NodeHandle box = scene.Place(scene.Root, 10, 10, 50, 50);
            scene.Tree.SetPressedStyle(box, new PaintStyle { Background = Red });
            var log = new List<string>();
            scene.Tree.AddPointerHandler(box, PointerEventKind.Cancel, EventPhase.Bubble, (in PointerEvent _) => log.Add("cancel"));
            scene.Tree.AddPointerHandler(box, PointerEventKind.Up, EventPhase.Bubble, (in PointerEvent _) => log.Add("up"));
            scene.Tree.AddPointerHandler(box, PointerEventKind.Click, EventPhase.Bubble, (in PointerEvent _) => log.Add("click"));
            scene.Draw();

            scene.TouchDown(1, 20, 20);
            scene.End(1, PointerType.Touch);
            Assert.False(scene.Has(box, PseudoState.Pressed));
            scene.TouchDown(1, 150, 80);
            scene.TouchUp(1, 150, 80);

            Assert.Equal(["cancel"], log);
        });

    /// <summary>
    /// The intermediate points of a pen's move come oldest first in logical pixels, the last being the event's own; the mouse gives
    /// its own alone; the points are read once per message, and reading them after the delivery throws.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A window at scale 2; a box (0, 0, 100 x 100 logical) whose move handler copies the points and keeps the event. The
    ///   platform gives the pen's history newest first, in physical pixels: (60, 60) at 3 ms, (40, 40) at 2 ms, (20, 20) at 1 ms.
    ///
    /// Steps:
    /// - The pen moves to (60, 60) with a history of 3; the handler reads the points twice. The mouse moves to (30, 30) with a
    ///   history of 3. Read the pen's kept event's points after its delivery.
    ///
    /// Expected:
    /// - (10, 10), (20, 20), (30, 30) at 1, 2, 3 ms with their pressures; one read of the history. The mouse: (15, 15) alone.
    ///   <see cref="InvalidOperationException"/> after the delivery.
    /// </remarks>
    [Fact]
    public void IntermediatePoints_OldestFirstInLogicalPixels_OnlyDuringTheDelivery() =>
        ui.Invoke(() =>
        {
            var scene = new Scene(scale: 2);
            NodeHandle box = scene.Place(scene.Root, 0, 0, 100, 100);
            static TimeSpan Ms(int value) => TimeSpan.FromMilliseconds(value);
            scene.Platform.History = [new(60, 60, 0.75f, 0, 0, 0, Ms(3)), new(40, 40, 0.5f, 0, 0, 0, Ms(2)), new(20, 20, 0.25f, 0, 0, 0, Ms(1))];
            PointerPoint[] points = [];
            var kept = new List<PointerEvent>();
            scene.Tree.AddPointerHandler(
                box,
                PointerEventKind.Move,
                EventPhase.Bubble,
                (in PointerEvent e) =>
                {
                    points = e.GetIntermediatePoints().ToArray();
                    Assert.Equal(points.Length, e.GetIntermediatePoints().Length);
                    kept.Add(e);
                }
            );
            scene.Draw();

            scene.Send(Scene.Pen(PointerInputKind.Move, 60, 60) with { Pressure = 0.75f, Timestamp = Ms(3), HistoryCount = 3 });
            Assert.Equal(
                [
                    new PointerPoint(new Vector2(10, 10), 0.25f, 0, 0, 0, Ms(1)),
                    new PointerPoint(new Vector2(20, 20), 0.5f, 0, 0, 0, Ms(2)),
                    new PointerPoint(new Vector2(30, 30), 0.75f, 0, 0, 0, Ms(3)),
                ],
                points
            );
            Assert.Equal(1, scene.Platform.HistoryReads);

            scene.Send(
                new PointerInput(
                    PointerInputKind.Move,
                    30,
                    30,
                    PointerButton.None,
                    PointerButtons.None,
                    KeyModifiers.None,
                    0,
                    Ms(4),
                    PointerType.Mouse,
                    HistoryCount: 3
                )
            );
            Assert.Equal([new PointerPoint(new Vector2(15, 15), 0, 0, 0, 0, Ms(4))], points);
            Assert.Equal(1, scene.Platform.HistoryReads);

            PointerEvent late = kept[0];
            Assert.Throws<InvalidOperationException>(() => late.GetIntermediatePoints().Length);
        });

    /// <summary>
    /// The steady state of touches and pens allocates nothing: two touches that tap a node with a pressed look together, and a pen
    /// that moves over it with a coalesced history its handler reads, comes, and ends.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A box (10, 10, 50 x 50) with hovered and pressed looks, click handlers that count, and a move handler that reads the
    ///   intermediate points; the platform gives a history of 8; warmed up with the same iterations.
    ///
    /// Steps:
    /// - 1,000 iterations in the windows of <c>SteadyStateAllocation</c>: touches 1 and 2 down, both up, both end; the pen moves in
    ///   with a history of 8, the frame runs, the pen ends, the frame runs.
    ///
    /// Expected:
    /// - 0 bytes in every window; two clicks and eight points per iteration.
    /// </remarks>
    [Fact]
    public void SteadyState_TouchesAndPen_AllocateNothing() =>
        ui.Invoke(() =>
        {
            var scene = new Scene();
            NodeHandle box = scene.Place(scene.Root, 10, 10, 50, 50);
            scene.Tree.SetHoveredStyle(box, new PaintStyle { Background = Red });
            scene.Tree.SetPressedStyle(box, new PaintStyle { Background = Gray });
            scene.Platform.History = new PointerHistoryPoint[8];
            int clicks = 0;
            int points = 0;
            scene.Tree.AddPointerHandler(box, PointerEventKind.Click, EventPhase.Bubble, (in PointerEvent _) => clicks++);
            scene.Tree.AddPointerHandler(
                box,
                PointerEventKind.Move,
                EventPhase.Bubble,
                (in PointerEvent e) => points += e.GetIntermediatePoints().Length
            );
            scene.Draw();

            void Iteration(int index)
            {
                scene.TouchDown(1, 20, 20);
                scene.TouchDown(2, 30, 30);
                scene.TouchUp(1, 20, 20);
                scene.TouchUp(2, 30, 30);
                scene.End(1, PointerType.Touch);
                scene.End(2, PointerType.Touch);
                scene.Send(Scene.Pen(PointerInputKind.Move, 20, 20) with { PointerId = 3, HistoryCount = 8 });
                scene.Scheduler.Run();
                scene.End(3, PointerType.Pen);
                scene.Scheduler.Run();
            }

            for (int i = 0; i < 20; i++)
            {
                Iteration(i);
            }

            (clicks, points) = (0, 0);
            SteadyStateAllocation.AssertNone(1_000, Iteration);

            // The helper's unmeasured window runs 100 iterations before the 1,000.
            Assert.Equal((2_200, 8_800), (clicks, points));
        });

    // A window of 200 x 100 logical pixels whose root holds the boxes placed in it, with its frames and input.
    private sealed class Scene
    {
        private readonly float _scale;

        public Scene(float scale = 1)
        {
            _scale = scale;
            Tree = new UiTree();
            Root = Tree.CreateNode(new LayoutStyle());
            Frames = new WindowFrames(Tree, Root, Target, new GlyphAtlas(new NoGlyphs()), Scheduler, Gray);
            Input = new WindowInput(Tree, Frames, Platform);
        }

        public UiTree Tree { get; }

        public NodeHandle Root { get; }

        public CountingTarget Target { get; } = new();

        public CountingScheduler Scheduler { get; } = new();

        public FakePlatform Platform { get; } = new();

        public WindowFrames Frames { get; }

        public WindowInput Input { get; }

        // A pen of number 1, primary, at half pressure while down.
        public static PointerInput Pen(
            PointerInputKind kind,
            int x,
            int y,
            PointerButton button = PointerButton.None,
            PointerButtons buttons = PointerButtons.None
        ) => new(kind, x, y, button, buttons, KeyModifiers.None, 0, TimeSpan.Zero, PointerType.Pen, PointerId: 1, Pressure: buttons == 0 ? 0 : 0.5f);

        public NodeHandle Place(NodeHandle parent, float x, float y, float width, float height) =>
            Box(
                Tree,
                parent,
                new LayoutStyle
                {
                    Position = Position.Absolute,
                    Inset = new EdgeInsets<LengthPercentageAuto>(
                        LengthPercentageAuto.Px(x),
                        LengthPercentageAuto.Px(y),
                        LengthPercentageAuto.Auto,
                        LengthPercentageAuto.Auto
                    ),
                    Width = Length.Px(width),
                    Height = Length.Px(height),
                }
            );

        public void Draw() => Frames.Resize(new Size(200, 100), _scale);

        public bool Has(NodeHandle node, PseudoState state) => (Tree.Nodes.GetRef(node).Pseudo & state) != 0;

        public void TouchDown(int id, int x, int y) => Send(Touch(PointerInputKind.Down, id, x, y, PointerButton.Left, PointerButtons.Left));

        public void TouchMove(int id, int x, int y) => Send(Touch(PointerInputKind.Move, id, x, y, PointerButton.None, PointerButtons.Left));

        public void TouchUp(int id, int x, int y) => Send(Touch(PointerInputKind.Up, id, x, y, PointerButton.Left, PointerButtons.None));

        // The pointer of the number given left the window or lifted away: Win32's WM_POINTERLEAVE.
        public void End(int id, PointerType type, int x = 0, int y = 0) =>
            Send(
                new PointerInput(
                    PointerInputKind.Leave,
                    x,
                    y,
                    PointerButton.None,
                    PointerButtons.None,
                    KeyModifiers.None,
                    0,
                    TimeSpan.Zero,
                    type,
                    PointerId: id
                )
            );

        public void Send(in PointerInput input) => Input.Handle(input);

        public static PointerInput Touch(PointerInputKind kind, int id, int x, int y, PointerButton button, PointerButtons buttons) =>
            new(
                kind,
                x,
                y,
                button,
                buttons,
                KeyModifiers.None,
                kind == PointerInputKind.Move ? 0 : 1,
                TimeSpan.Zero,
                PointerType.Touch,
                PointerId: id,
                IsPrimary: id == 1,
                Pressure: buttons == 0 ? 0 : 0.5f
            );
    }

    // Records what the input asked of the window, and gives the history set.
    private sealed class FakePlatform : IPointerPlatform
    {
        public int Captures { get; private set; }

        public int Releases { get; private set; }

        public int HistoryReads { get; private set; }

        public PointerHistoryPoint[] History { get; set; } = [];

        public void Capture() => Captures++;

        public void ReleaseCapture() => Releases++;

        public void SetCursor(CursorKind cursor) { }

        public int ReadHistory(int pointerId, Span<PointerHistoryPoint> destination)
        {
            HistoryReads++;
            int count = Math.Min(History.Length, destination.Length);
            History.AsSpan(0, count).CopyTo(destination);
            return count;
        }
    }
}
