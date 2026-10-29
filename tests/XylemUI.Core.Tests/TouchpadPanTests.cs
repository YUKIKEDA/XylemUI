using System.Numerics;
using XylemUI.Text;
using static XylemUI.Tests.FrameDoubles;
using static XylemUI.Tests.LayoutTestTree;

namespace XylemUI.Tests;

// A touchpad's pan (row I1c, design §7, "Touch and pen"): the start, updates, and end the platform makes from DirectManipulation,
// injected as PointerInput (design §12.4). It scrolls by a touch's rules (row I1b) and keeps no input from the nodes inside.
[Collection(UiThreadGroup.Name)]
public sealed class TouchpadPanTests(UiThreadFixture ui)
{
    private static readonly Color Gray = Color.FromRgb(0x808080);

    /// <summary>
    /// A pan scrolls the container once its total along the container's axis is past 18 logical pixels, from that point on; the
    /// node under the start gets the start and the updates before that, then the container gets them, and no node gets a cancel or
    /// a hover.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A box 200 x 100 whose height scrolls over 1,000, with an item at its top that logs its pan events, cancels, and enters.
    ///
    /// Steps:
    /// - Start a pan at (50, 40); update it by −10, −10 (a total of −20), and −5 on y; end it.
    ///
    /// Expected:
    /// - After −10 the position is 0; after −20 it is still 0, the container taking the pan; after −5 it is 5.
    /// - The item logged the start and the first two updates, then no more; no cancel and no enter.
    /// - Each update's <see cref="PointerEvent.PanDelta"/> is its amount, its <see cref="PointerEvent.WheelDelta"/> 0, and its
    ///   type the touchpad.
    /// </remarks>
    [Fact]
    public void Pan_PastSlopAlongTheAxis_ScrollsFromThere() =>
        ui.Invoke(() =>
        {
            var scene = new Scene();
            var log = new List<string>();
            var deltas = new List<(Vector2 Pan, Vector2 Wheel, PointerType Type)>();
            scene.Log(scene.Item, log);
            scene.Tree.AddPointerHandler(
                scene.Item,
                PointerEventKind.PanUpdate,
                EventPhase.Bubble,
                (in PointerEvent e) => deltas.Add((e.PanDelta, e.WheelDelta, e.PointerType))
            );
            scene.Draw();

            scene.Pan(PointerInputKind.PanStart, default, 0);
            scene.Pan(PointerInputKind.PanUpdate, new Vector2(0, -10), 16);
            float first = scene.Offset.Y;
            scene.Pan(PointerInputKind.PanUpdate, new Vector2(0, -10), 32);
            float taken = scene.Offset.Y;
            scene.Pan(PointerInputKind.PanUpdate, new Vector2(0, -5), 48);
            float moved = scene.Offset.Y;
            scene.Pan(PointerInputKind.PanEnd, default, 300);

            Assert.Equal((0f, 0f, 5f), (first, taken, moved));
            Assert.Equal(["item panstart", "item panupdate", "item panupdate"], log);
            Assert.Equal(
                [(new Vector2(0, -10), Vector2.Zero, PointerType.Touchpad), (new Vector2(0, -10), Vector2.Zero, PointerType.Touchpad)],
                deltas
            );
        });

    /// <summary>
    /// The slop is measured along each container's own axes: a horizontal pan moves a horizontal box inside a vertical one, and a
    /// vertical pan moves the vertical box through it.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - The vertical box of the scene, with a box 200 x 50 at its top whose width scrolls over 1,000.
    ///
    /// Steps:
    /// - A pan at (50, 20) by −20 then −10 on x; a second pan there by −20 then −10 on y.
    ///
    /// Expected:
    /// - The first pan moves the inner box to 10 across and the outer box nowhere; the second moves the outer box to 10 down and the
    ///   inner box no further.
    /// </remarks>
    [Fact]
    public void Pan_AcrossAndDown_MovesTheContainerOfEachAxis() =>
        ui.Invoke(() =>
        {
            var scene = new Scene();
            NodeHandle inner = Box(scene.Tree, scene.Content, Scene.At(0, 0, 200, 50) with { OverflowX = Overflow.Scroll });
            _ = Box(scene.Tree, inner, new LayoutStyle { Width = Length.Px(1_000), Height = Length.Px(50) });
            scene.Draw();

            scene.Pan(PointerInputKind.PanStart, default, 0, y: 20);
            scene.Pan(PointerInputKind.PanUpdate, new Vector2(-20, 0), 16, y: 20);
            scene.Pan(PointerInputKind.PanUpdate, new Vector2(-10, 0), 32, y: 20);
            scene.Pan(PointerInputKind.PanEnd, default, 300, y: 20);
            Vector2 innerAfterAcross = scene.Tree.ScrollOffsetOf(inner);
            float outerAfterAcross = scene.Offset.Y;

            scene.Pan(PointerInputKind.PanStart, default, 400, y: 20);
            scene.Pan(PointerInputKind.PanUpdate, new Vector2(0, -20), 416, y: 20);
            scene.Pan(PointerInputKind.PanUpdate, new Vector2(0, -10), 432, y: 20);
            scene.Pan(PointerInputKind.PanEnd, default, 700, y: 20);

            Assert.Equal((new Vector2(10, 0), 0f), (innerAfterAcross, outerAfterAcross));
            Assert.Equal((new Vector2(10, 0), 10f), (scene.Tree.ScrollOffsetOf(inner), scene.Offset.Y));
        });

    /// <summary>
    /// A node inside that stops a pan update keeps the container from taking the pan, as a slider keeps a touch's move.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - The scene, with the item's bubble handler of the update stopping it.
    ///
    /// Steps:
    /// - A pan at (50, 40) by −20 and −20 on y.
    ///
    /// Expected:
    /// - The position stays 0.
    /// </remarks>
    [Fact]
    public void Pan_StoppedByANodeInside_DoesNotScroll() =>
        ui.Invoke(() =>
        {
            var scene = new Scene();
            scene.Tree.AddPointerHandler(scene.Item, PointerEventKind.PanUpdate, EventPhase.Bubble, (in PointerEvent e) => e.StopPropagation());
            scene.Draw();

            scene.Pan(PointerInputKind.PanStart, default, 0);
            scene.Pan(PointerInputKind.PanUpdate, new Vector2(0, -20), 16);
            scene.Pan(PointerInputKind.PanUpdate, new Vector2(0, -20), 32);
            scene.Pan(PointerInputKind.PanEnd, default, 48);

            Assert.Equal(0f, scene.Offset.Y);
        });

    /// <summary>
    /// The end flings the container with the velocity of the pan's totals and the frames' times, by the formula of a touch's
    /// fling; the fling's frames move the position until it stops.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - The scene. A velocity tracker that gets the same totals and times as the pan, for the expected fling.
    ///
    /// Steps:
    /// - A pan at (50, 40): six updates of −20 on y, 16 ms apart; the end 16 ms after the last. Then two frames, and frames until
    ///   none is asked for.
    ///
    /// Expected:
    /// - The second frame after the end is at the fling of that velocity from the position at the end, 16 ms in.
    /// - The frames end, with the position past the end's.
    /// </remarks>
    [Fact]
    public void PanEnd_FlingsWithThePansVelocity() =>
        ui.Invoke(() =>
        {
            var scene = new Scene();
            scene.Draw();
            var tracker = new VelocityTracker();
            scene.Pan(PointerInputKind.PanStart, default, 0);
            tracker.Add(TimeSpan.Zero, 0, 0);
            for (int i = 1; i <= 6; i++)
            {
                scene.Pan(PointerInputKind.PanUpdate, new Vector2(0, -20), i * 16);
                tracker.Add(TimeSpan.FromMilliseconds(i * 16), 0, -20 * i);
            }

            scene.Time.Now = TimeSpan.FromMilliseconds(112);
            float released = scene.Offset.Y;
            scene.Pan(PointerInputKind.PanEnd, default, 112);
            double velocity = tracker.Estimate(TimeSpan.FromMilliseconds(112)).VelocityY;

            scene.Frames(2);
            Assert.Equal((float)new ClampingFling(released, -velocity).At(0.016), scene.Offset.Y, 2);
            for (int i = 0; i < 1_000 && scene.Scheduler.Pending; i++)
            {
                scene.Frames(1);
            }

            Assert.False(scene.Scheduler.Pending);
            Assert.True(scene.Offset.Y > released);
        });

    /// <summary>
    /// A touchpad's fling keeps no input out: the mouse's press and click reach the node inside and the fling goes on; a touch's
    /// press stops it and reaches the node too; a new pan stops it.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - The scene, its content (under every position while it scrolls) logging its downs and clicks.
    ///
    /// Steps:
    /// - Fling with a pan; one frame; a mouse press and release at (50, 20); one frame.
    /// - Fling again; a touch press and release there; one frame.
    /// - Fling again; a new pan's start there; one frame.
    ///
    /// Expected:
    /// - The mouse's down and click reached the content, and the position moved in the frame after them.
    /// - The touch's down and click reached the content, and the position did not move after them.
    /// - The position did not move after the new pan's start.
    /// </remarks>
    [Fact]
    public void TouchpadFling_KeepsNoInputOut_AndATouchOrAPanStopsIt() =>
        ui.Invoke(() =>
        {
            var scene = new Scene();
            var log = new List<string>();
            scene.Log(scene.Content, log);
            scene.Draw();

            scene.Fling(0);
            scene.Frames(1);
            scene.Mouse(PointerInputKind.Down, 50, 20, scene.Milliseconds);
            scene.Mouse(PointerInputKind.Up, 50, 20, scene.Milliseconds);
            float beforeMouseFrame = scene.Offset.Y;
            scene.Frames(1);
            bool mouseLeftItGoing = scene.Offset.Y > beforeMouseFrame;
            string[] mouseLog = [.. log];
            log.Clear();

            scene.ScrollTo(0);
            scene.Fling(scene.Milliseconds + 100);
            scene.Frames(1);
            scene.Touch(PointerInputKind.Down, 50, 20, scene.Milliseconds);
            scene.Touch(PointerInputKind.Up, 50, 20, scene.Milliseconds);
            float afterTouch = scene.Offset.Y;
            scene.Frames(1);
            bool touchStopped = scene.Offset.Y == afterTouch;
            string[] touchLog = [.. log];

            scene.ScrollTo(0);
            scene.Fling(scene.Milliseconds + 100);
            scene.Frames(1);
            scene.Pan(PointerInputKind.PanStart, default, scene.Milliseconds, y: 20);
            float afterPan = scene.Offset.Y;
            scene.Frames(1);

            // The content gets the pans' events and the mouse's enter too; only the presses are read here.
            Assert.Equal(["item down", "item click"], mouseLog.Where(static e => e is "item down" or "item click"));
            Assert.True(mouseLeftItGoing);
            Assert.Equal(["item down", "item click"], touchLog.Where(static e => e is "item down" or "item click"));
            Assert.True(touchStopped);
            Assert.Equal(afterPan, scene.Offset.Y);
        });

    /// <summary>
    /// A container that a touch drags does not take a pan, and the pan does not join the touch's drag.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - The scene.
    ///
    /// Steps:
    /// - A touch at (50, 90) moves up by 20 (the container takes it) and by 10 more. With the touch still down, a pan at (50, 40) by
    ///   −40 on y.
    ///
    /// Expected:
    /// - The position is the touch's 10, and stays 10 after the pan.
    /// </remarks>
    [Fact]
    public void Pan_OnAContainerATouchDrags_DoesNotMoveIt() =>
        ui.Invoke(() =>
        {
            var scene = new Scene();
            scene.Draw();

            scene.Touch(PointerInputKind.Down, 50, 90, 0);
            scene.Touch(PointerInputKind.Move, 50, 70, 10);
            scene.Touch(PointerInputKind.Move, 50, 60, 20);
            float dragged = scene.Offset.Y;
            scene.Pan(PointerInputKind.PanStart, default, 30);
            scene.Pan(PointerInputKind.PanUpdate, new Vector2(0, -20), 46);
            scene.Pan(PointerInputKind.PanUpdate, new Vector2(0, -20), 62);
            scene.Pan(PointerInputKind.PanEnd, default, 78);

            Assert.Equal((10f, 10f), (dragged, scene.Offset.Y));
        });

    /// <summary>
    /// The frames ask the platform for its gestures at their start, and ask for the next frame while it says one goes on.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - The scene, whose platform says a gesture goes on for its first three calls, and delivers one pan update of −30 in its
    ///   first call after a start.
    ///
    /// Steps:
    /// - Start a pan; run frames until none is asked for.
    ///
    /// Expected:
    /// - The platform was called four times, each with the frame's time; the frame that delivered the update scrolled before its
    ///   layout pass (the position is 12 after the first frame).
    /// </remarks>
    [Fact]
    public void Frames_AskForGestures_WhileTheyGoOn() =>
        ui.Invoke(() =>
        {
            var scene = new Scene();
            scene.Draw();
            scene.Pan(PointerInputKind.PanStart, default, 0);
            scene.Pan(PointerInputKind.PanUpdate, new Vector2(0, -19), 0);
            scene.Platform.Times.Clear();
            scene.Platform.Goes = 3;
            scene.Platform.Deliver = () => scene.Pan(PointerInputKind.PanUpdate, new Vector2(0, -12), scene.Milliseconds);
            scene.FrameRunner.RequestFrame();

            scene.Frames(1);
            float afterFirst = scene.Offset.Y;
            for (int i = 0; i < 10 && scene.Scheduler.Pending; i++)
            {
                scene.Frames(1);
            }

            Assert.Equal(12f, afterFirst);
            Assert.Equal([16.0, 32.0, 48.0, 64.0], scene.Platform.Times);
        });

    /// <summary>
    /// A pan, its scrolling, and its fling allocate nothing once warm.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - The scene, warmed by the helper's unmeasured iterations.
    ///
    /// Steps:
    /// - 1,000 times: back to 0, a fling by a pan, and its frames until none is asked for.
    ///
    /// Expected:
    /// - No bytes were allocated, and each iteration flung.
    /// </remarks>
    [Fact]
    public void SteadyState_PanAndFling_AllocateNothing() =>
        ui.Invoke(() =>
        {
            var scene = new Scene(contentHeight: 100_000);
            scene.Platform.Records = false;
            scene.Draw();
            int flings = 0;
            void Iteration(int index)
            {
                scene.ScrollTo(0);
                scene.Fling(scene.Milliseconds + 100);
                if (scene.Scheduler.Pending)
                {
                    flings++;
                }

                for (int i = 0; i < 1_000 && scene.Scheduler.Pending; i++)
                {
                    scene.Frames(1);
                }
            }

            SteadyStateAllocation.AssertNone(1_000, Iteration);
            Assert.Equal(1_100, flings);
        });

    // A window of 200 x 100 logical pixels: a box of the window's size whose height scrolls, holding a column of the height given,
    // with an item 200 x 50 at its top; the frames run on a clock the test moves.
    private sealed class Scene
    {
        public Scene(float contentHeight = 1_000)
        {
            Root = Tree.CreateNode(new LayoutStyle());
            Container = Box(Tree, Root, At(0, 0, 200, 100) with { OverflowY = Overflow.Scroll });
            Content = Box(Tree, Container, new LayoutStyle { Width = Length.Px(200), Height = Length.Px(contentHeight) });
            Item = Box(Tree, Content, At(0, 0, 200, 50));
            FrameRunner = new WindowFrames(Tree, Root, Target, new GlyphAtlas(new NoGlyphs()), Scheduler, Gray, time: Time);
            Input = new WindowInput(Tree, FrameRunner, Platform);
            FrameRunner.Input = Input;
        }

        public UiTree Tree { get; } = new();

        public NodeHandle Root { get; }

        public NodeHandle Container { get; }

        public NodeHandle Content { get; }

        public NodeHandle Item { get; }

        public CountingTarget Target { get; } = new();

        public CountingScheduler Scheduler { get; } = new();

        public StepTime Time { get; } = new();

        public GesturePlatform Platform { get; } = new();

        public WindowFrames FrameRunner { get; }

        public WindowInput Input { get; }

        public Vector2 Offset => Tree.ScrollOffsetOf(Container);

        public double Milliseconds => Time.Now.TotalMilliseconds;

        public static LayoutStyle At(float x, float y, float width, float height) =>
            new()
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
            };

        public void Draw() => FrameRunner.Resize(new Size(200, 100), 1);

        // The node's pan events, downs, cancels, clicks, and enters, in the log as "item <kind>".
        public void Log(NodeHandle node, List<string> log)
        {
            foreach (
                PointerEventKind kind in new[]
                {
                    PointerEventKind.PanStart,
                    PointerEventKind.PanUpdate,
                    PointerEventKind.PanEnd,
                    PointerEventKind.Down,
                    PointerEventKind.Cancel,
                    PointerEventKind.Click,
                    PointerEventKind.Enter,
                }
            )
            {
                string entry = $"item {kind.ToString().ToLowerInvariant()}";
                Tree.AddPointerHandler(node, kind, EventPhase.Bubble, (in PointerEvent _) => log.Add(entry));
            }

            log.Clear();
        }

        public void ScrollTo(float y) => Tree.ScrollStateFor(Container).ScrollTo(new Vector2(0, y));

        public void Frames(int count)
        {
            for (int i = 0; i < count; i++)
            {
                Time.Now += TimeSpan.FromMilliseconds(16);
                Scheduler.Run();
            }
        }

        // A pan at (50, 40) from the time given: five updates of −40 on y, 16 ms apart, and the end with the last; the clock is at the end.
        public void Fling(double start)
        {
            Pan(PointerInputKind.PanStart, default, start);
            for (int i = 1; i <= 5; i++)
            {
                Pan(PointerInputKind.PanUpdate, new Vector2(0, -40), start + (i * 16));
            }

            Time.Now = TimeSpan.FromMilliseconds(start + 80);
            Pan(PointerInputKind.PanEnd, default, start + 80);
        }

        public void Pan(PointerInputKind kind, Vector2 delta, double milliseconds, int y = 40) =>
            Input.Handle(
                new PointerInput(
                    kind,
                    50,
                    y,
                    PointerButton.None,
                    PointerButtons.None,
                    KeyModifiers.None,
                    0,
                    TimeSpan.FromMilliseconds(milliseconds),
                    PointerType.Touchpad,
                    Delta: delta,
                    PointerId: 2
                )
            );

        public void Touch(PointerInputKind kind, int x, int y, double milliseconds) => Send(kind, 1, x, y, milliseconds, PointerType.Touch);

        public void Mouse(PointerInputKind kind, int x, int y, double milliseconds) => Send(kind, 0, x, y, milliseconds, PointerType.Mouse);

        private void Send(PointerInputKind kind, int id, int x, int y, double milliseconds, PointerType type)
        {
            bool down = kind is PointerInputKind.Down or PointerInputKind.Move;
            Input.Handle(
                new PointerInput(
                    kind,
                    x,
                    y,
                    kind == PointerInputKind.Move ? PointerButton.None : PointerButton.Left,
                    down ? PointerButtons.Left : PointerButtons.None,
                    KeyModifiers.None,
                    kind == PointerInputKind.Move ? 0 : 1,
                    TimeSpan.FromMilliseconds(milliseconds),
                    type,
                    PointerId: id,
                    Pressure: down ? 0.5f : 0
                )
            );
        }
    }

    // A clock that moves only when the test moves it.
    private sealed class StepTime : TimeProvider
    {
        public TimeSpan Now { get; set; }

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public override long GetTimestamp() => Now.Ticks;
    }

    // A platform whose gestures go on for a number of calls, delivering what the test gives in the first, and records the frames'
    // times it was called with.
    private sealed class GesturePlatform : IPointerPlatform
    {
        public int Goes { get; set; }

        public Action? Deliver { get; set; }

        public List<double> Times { get; } = [];

        public void Capture() { }

        public void ReleaseCapture() { }

        public void SetCursor(CursorKind cursor) { }

        // Off for the allocation test, whose frames would grow the list.
        public bool Records { get; set; } = true;

        public bool UpdateGestures(TimeSpan frameTime)
        {
            if (Records)
            {
                Times.Add(frameTime.TotalMilliseconds);
            }

            Action? deliver = Deliver;
            Deliver = null;
            deliver?.Invoke();
            return Goes-- > 0;
        }
    }
}
