using System.Numerics;
using XylemUI.Text;
using static XylemUI.Tests.FrameDoubles;
using static XylemUI.Tests.LayoutTestTree;

namespace XylemUI.Tests;

// A touch's and a pen's drag of a scroll container and its fling (row I1b, design §7, "Touch and pen"): when a drag turns into
// a scroll, how it moves, the velocity of Flutter's VelocityTracker, and the fling of Flutter's ClampingScrollSimulation run frame
// by frame on a clock the test moves. The inputs are made as the Win32 window gives them: integer client coordinates in physical
// pixels, a number per pointer, and the time the window received them.
[Collection(UiThreadGroup.Name)]
public sealed class TouchScrollTests(UiThreadFixture ui)
{
    private static readonly Color Gray = Color.FromRgb(0x808080);

    /// <summary>
    /// The velocity tracker gives Flutter's velocities for the pointer events of Flutter's own test, within its tolerance of 0.1 %.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - The events of Flutter's <c>velocity_tracker_data.dart</c>, and the 13 velocities and the interrupted one its
    ///   <c>velocity_tracker_test.dart</c> expects.
    ///
    /// Steps:
    /// - Add each down and move to one tracker, as Flutter's test does; at each up, read the estimate at the up's time.
    ///
    /// Expected:
    /// - Each velocity is within 0.1 % of Flutter's on both axes.
    /// </remarks>
    [Fact]
    public void VelocityTracker_FlutterEvents_GiveFlutterVelocities()
    {
        (double X, double Y)[] expected =
        [
            (219.59280094228163, 1304.701682306001),
            (355.71046950050845, 967.2112857054104),
            (12.657970884022308, -36.90447839251946),
            (714.1399654786744, -2561.534447931869),
            (-19.668121066218564, -2910.105747052462),
            (646.8690114934209, 2976.977762577527),
            (396.6988447819592, 2106.225572911095),
            (298.31594440044495, -3660.8315955215294),
            (-1.7334232785165882, -3288.13174127454),
            (384.6361280392334, -2645.6612524779835),
            (176.37900397918557, 2711.2542876273264),
            (396.9328560260098, 4280.651578291764),
            (-71.51939428321249, 3716.7385187526947),
        ];

        Assert.Equal(expected, Velocities(VelocityTrackerData.Events), Within);
        Assert.Equal([(649.5, 3890.3)], Velocities(VelocityTrackerData.Interrupted), Within);

        static List<(double X, double Y)> Velocities((char Kind, long Milliseconds, double X, double Y)[] events)
        {
            var tracker = new VelocityTracker();
            var velocities = new List<(double X, double Y)>();
            foreach ((char kind, long milliseconds, double x, double y) in events)
            {
                TimeSpan time = TimeSpan.FromMilliseconds(milliseconds);
                if (kind == 'U')
                {
                    (double vx, double vy, _, _) = tracker.Estimate(time);
                    velocities.Add((vx, vy));
                }
                else
                {
                    tracker.Add(time, x, y);
                }
            }

            return velocities;
        }
    }

    /// <summary>
    /// The fling is Flutter's <c>ClampingScrollSimulation</c>: it starts where it is made, travels its distance in its duration,
    /// and its distance is the longer form of Android's that Flutter's own assertion checks; the fastest fling ends within 3 seconds.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - Flings from 100 at 50, 1,000, and −8,000 logical pixels a second.
    ///
    /// Steps:
    /// - Read each fling's position at 0, at its duration, and past it; compute Android's distance as
    ///   <c>scroll_simulation.dart</c> lines 239 to 244 do.
    ///
    /// Expected:
    /// - 100 at 0; 100 plus the distance at and past the duration; the distance equals Android's to 1e-9 with the velocity's sign;
    ///   the duration of 8,000 is under 3 seconds (Flutter's test, <c>scroll_simulation_test.dart</c> lines 31 to 35).
    /// </remarks>
    [Fact]
    public void ClampingFling_IsFluttersSimulation()
    {
        double rate = Math.Log(0.78) / Math.Log(0.9);
        double coefficient = 9.80665 * 39.37 * 160.0 * 0.84;
        foreach (double velocity in new[] { 50.0, 1_000.0, -8_000.0 })
        {
            var fling = new ClampingFling(100, velocity);
            double logVelocity = Math.Log(Math.Abs(velocity) / (0.015 * coefficient / 0.35));
            double android = 0.015 * coefficient * Math.Exp(logVelocity * rate / (rate - 1.0));

            Assert.Equal(100, fling.At(0), 9);
            Assert.Equal(100 + fling.Distance, fling.At(fling.Duration), 9);
            Assert.Equal(100 + fling.Distance, fling.At(fling.Duration * 2), 9);
            Assert.Equal(Math.Sign(velocity) * android, fling.Distance, 9);
        }

        Assert.InRange(new ClampingFling(0, 8_000).Duration, 0, 3);
    }

    /// <summary>
    /// A touch's drag turns into a scroll when its move along the container's axis passes 18 logical pixels, and moves the
    /// position from that point on; a move across the axis does not count. The node pressed gets a cancel and no click.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A scale of 2; a box of 200 x 100 logical pixels whose height scrolls, holding a column 1,000 high; an item at its top
    ///   with handlers that log its down, cancel, up, and click, and the box's moves.
    ///
    /// Steps:
    /// - Touch down at (100, 60) physical; move to (180, 60), 40 logical across; move to (180, 24), 18 logical up; move to
    ///   (180, 22); move to (180, 2); release 100 ms later; run three frames.
    ///
    /// Expected:
    /// - The position is 0 until the move to 22 (19 logical up), which starts the drag without moving it; the move to 2 moves it
    ///   10. The item gets the down and the moves before the drag, then a cancel after the box's move that starts it; the moves
    ///   and the release after it go to the box alone, and no click comes. The slow release does not fling.
    /// </remarks>
    [Fact]
    public void Drag_PastTheSlopAlongTheAxis_ScrollsFromThatPoint() =>
        ui.Invoke(() =>
        {
            var scene = new Scene(scale: 2);
            var log = new List<string>();
            scene.Log(scene.Item, log, "item");
            scene.Tree.AddPointerHandler(scene.Container, PointerEventKind.Move, EventPhase.Bubble, (in PointerEvent _) => log.Add("box move"));
            scene.Draw();

            scene.Touch(PointerInputKind.Down, 1, 100, 60, 0);
            scene.Touch(PointerInputKind.Move, 1, 180, 60, 10);
            scene.Touch(PointerInputKind.Move, 1, 180, 24, 20);
            Assert.Equal(0, scene.Offset.Y);
            scene.Touch(PointerInputKind.Move, 1, 180, 22, 30);
            Assert.Equal(0, scene.Offset.Y);
            scene.Touch(PointerInputKind.Move, 1, 180, 2, 40);
            Assert.Equal(10, scene.Offset.Y);
            scene.Touch(PointerInputKind.Up, 1, 180, 2, 140);
            float released = scene.Offset.Y;
            scene.Frames(3);

            Assert.Equal(["item down", "item move", "box move", "item move", "box move", "item move", "box move", "item cancel", "box move"], log);
            Assert.Equal(released, scene.Offset.Y);
        });

    /// <summary>
    /// The innermost container with the drag's axis takes it: a vertical drag over a container that scrolls across moves the
    /// container outside it that scrolls down.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - The box of 200 x 100 whose height scrolls; inside its column, at its top, a box of 200 x 50 whose width scrolls, holding a
    ///   row 1,000 wide.
    ///
    /// Steps:
    /// - Touch down at (50, 20) on the inner box; move to (50, 0), 20 up; move to (50, -10).
    ///
    /// Expected:
    /// - The outer position is 10 and the inner one 0.
    /// </remarks>
    [Fact]
    public void Drag_AcrossAnInnerContainer_MovesTheOuterOne() =>
        ui.Invoke(() =>
        {
            var scene = new Scene();
            NodeHandle inner = Box(scene.Tree, scene.Content, Scene.At(0, 0, 200, 50) with { OverflowX = Overflow.Scroll });
            _ = Box(scene.Tree, inner, new LayoutStyle { Width = Length.Px(1_000), Height = Length.Px(50) });
            scene.Draw();

            scene.Touch(PointerInputKind.Down, 1, 50, 20, 0);
            scene.Touch(PointerInputKind.Move, 1, 50, 0, 10);
            scene.Touch(PointerInputKind.Move, 1, 50, -10, 20);

            Assert.Equal(10, scene.Offset.Y);
            Assert.Equal(Vector2.Zero, scene.Tree.ScrollOffsetOf(inner));
        });

    /// <summary>
    /// A node inside the container that stops the move keeps the drag for itself: the container does not scroll, and the press
    /// still clicks. A pen's drag elsewhere scrolls; the mouse's does not.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - The box whose height scrolls; its item stops each move in the bubble phase and logs its click.
    ///
    /// Steps:
    /// - Pen down at (50, 20) on the item; move to (50, -20), 40 up; release. The mouse down at (50, 90) below the item, moved 40 up,
    ///   moved 10 more, released. The pen down at (50, 90), moved 30 and 10 up,
    ///   released. The same pen drag with a move handler on the box that stops the move.
    ///
    /// Expected:
    /// - The position stays 0, and the release over the item clicks: the slop of a tap is a touch's, and a pen clicks as the mouse
    ///   does (design §7, "Touch and pen"). The mouse's drag moves nothing. The pen's drag outside the item scrolls 10; with the box's
    ///   handler stopping the move, the same drag moves nothing more.
    /// </remarks>
    [Fact]
    public void Drag_StoppedByANodeInside_DoesNotScroll() =>
        ui.Invoke(() =>
        {
            var scene = new Scene();
            int clicks = 0;
            scene.Tree.AddPointerHandler(scene.Item, PointerEventKind.Move, EventPhase.Bubble, (in PointerEvent e) => e.StopPropagation());
            scene.Tree.AddPointerHandler(scene.Item, PointerEventKind.Click, EventPhase.Bubble, (in PointerEvent _) => clicks++);
            scene.Draw();

            scene.Pen(PointerInputKind.Down, 1, 50, 20, 0);
            scene.Pen(PointerInputKind.Move, 1, 50, -20, 10);
            scene.Pen(PointerInputKind.Up, 1, 50, 20, 20);
            Assert.Equal(0, scene.Offset.Y);
            Assert.Equal(1, clicks);

            scene.Mouse(PointerInputKind.Down, 50, 90, 1_000);
            scene.Mouse(PointerInputKind.Move, 50, 50, 1_010);
            scene.Mouse(PointerInputKind.Move, 50, 40, 1_020);
            scene.Mouse(PointerInputKind.Up, 50, 50, 1_200);
            Assert.Equal(0, scene.Offset.Y);

            scene.Pen(PointerInputKind.Down, 1, 50, 90, 2_000);
            scene.Pen(PointerInputKind.Move, 1, 50, 60, 2_010);
            scene.Pen(PointerInputKind.Move, 1, 50, 50, 2_020);
            scene.Pen(PointerInputKind.Up, 1, 50, 50, 2_200);
            Assert.Equal(10, scene.Offset.Y);

            // The box's own handler stops the move before the box takes it, as it stops a wheel.
            scene.Tree.AddPointerHandler(scene.Container, PointerEventKind.Move, EventPhase.Bubble, (in PointerEvent e) => e.StopPropagation());
            scene.Pen(PointerInputKind.Down, 1, 50, 90, 3_000);
            scene.Pen(PointerInputKind.Move, 1, 50, 60, 3_010);
            scene.Pen(PointerInputKind.Move, 1, 50, 50, 3_020);
            scene.Pen(PointerInputKind.Up, 1, 50, 50, 3_200);
            Assert.Equal(10, scene.Offset.Y);
        });

    /// <summary>
    /// With several fingers on the container, the last one down drives it, and when it lifts the first one still down drives it
    /// (Flutter's latestPointer); the fingers that do not drive move nothing, and a finger that joins sends no down to the nodes
    /// inside.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - The box whose height scrolls, its item logging its downs.
    ///
    /// Steps:
    /// - Finger 1 down at (50, 40) on the item and up to (50, 10), which starts the drag; fingers 2 and 3 down at (60, 30) and
    ///   (70, 30) over the item. Finger 1 moves 10 up, finger 2 moves 10 up, finger 3 moves 5 up. Finger 3 lifts; finger 2 moves 10 up; finger 1
    ///   moves 7 up.
    ///
    /// Expected:
    /// - The position is 0 after the drag starts, then 0, 0, 5; after finger 3 lifts, 5 then 12. The item logged finger 1's down
    ///   and its cancel, and no down of the fingers that joined.
    /// </remarks>
    [Fact]
    public void Drag_SeveralFingers_TheLastDrives_ThenTheFirst() =>
        ui.Invoke(() =>
        {
            var scene = new Scene();
            var log = new List<string>();
            scene.Log(scene.Item, log, "item");
            scene.Draw();

            scene.Touch(PointerInputKind.Down, 1, 50, 40, 0);
            scene.Touch(PointerInputKind.Move, 1, 50, 10, 10);
            scene.Touch(PointerInputKind.Down, 2, 60, 30, 20);
            scene.Touch(PointerInputKind.Down, 3, 70, 30, 30);
            scene.Touch(PointerInputKind.Move, 1, 50, 0, 40);
            scene.Touch(PointerInputKind.Move, 2, 60, 20, 50);
            Assert.Equal(0, scene.Offset.Y);
            scene.Touch(PointerInputKind.Move, 3, 70, 25, 60);
            Assert.Equal(5, scene.Offset.Y);
            scene.Touch(PointerInputKind.Up, 3, 70, 25, 70);
            scene.Touch(PointerInputKind.Move, 2, 60, 10, 80);
            Assert.Equal(5, scene.Offset.Y);
            scene.Touch(PointerInputKind.Move, 1, 50, -7, 90);
            Assert.Equal(12, scene.Offset.Y);

            Assert.Equal(["item down", "item cancel"], log.Where(static e => e != "item move").ToList());
        });

    /// <summary>
    /// A release that moves fast flings the container: its position at each frame is the fling's at the frame's time, counted from
    /// the first frame, and the frames stop when the fling's time is over.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - The box whose height scrolls, its column 10,000 high so that no end is reached.
    ///
    /// Steps:
    /// - Touch down at (50, 90); move up 10 logical pixels every 10 ms ten times; release with the last move. Run the frames, the
    ///   clock 16 ms on before each one, until none is asked for.
    ///
    /// Expected:
    /// - A frame is asked for at the release. The first frame keeps the position; each frame after it is at the fling from the
    ///   release's position at 1,000 pixels a second, at the frame's time since the first; the last frame is past the fling's
    ///   duration and at its end.
    /// </remarks>
    [Fact]
    public void Fling_FollowsTheSimulationFrameByFrame_AndEnds() =>
        ui.Invoke(() =>
        {
            var scene = new Scene(contentHeight: 10_000);
            scene.Draw();
            float released = scene.Swipe(fromY: 90, steps: 10, step: -10, milliseconds: 10);
            Assert.True(scene.Scheduler.Pending);

            var fling = new ClampingFling(released, 1_000);
            TimeSpan first = scene.Time.Now + TimeSpan.FromMilliseconds(16);
            int frames = 0;
            while (scene.Scheduler.Pending)
            {
                scene.Time.Now += TimeSpan.FromMilliseconds(16);
                scene.Scheduler.Run();
                double t = (scene.Time.Now - first).TotalSeconds;
                Assert.Equal((float)fling.At(t), scene.Offset.Y, 3);
                frames++;
                Assert.InRange(frames, 1, 1_000);
            }

            Assert.True((scene.Time.Now - first).TotalSeconds >= fling.Duration);
            Assert.Equal((float)(released + fling.Distance), scene.Offset.Y, 3);
        });

    /// <summary>
    /// A release flings only when Flutter's conditions hold: a velocity past 50 and a movement past the slop over the positions
    /// the velocity read, and room on the way; the velocity is cut to 8,000, and a fling stops at the end of the range.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A scale of 10, so that a tenth of a logical pixel is a whole physical pixel; the box whose height scrolls, its column
    ///   1,000 high (largest position 900).
    ///
    /// Steps:
    /// - Each drag starts with three moves of 10 up, 1 ms apart, then waits 100 ms, so that the velocity reads only the moves after
    ///   it. Then: five moves of 0.4 up every 10 ms (40 pixels a second); eight moves of 2 up every 1 ms (2,000 pixels a second, 14
    ///   pixels over the positions read); from position 30, ten moves of 10 down every 10 ms, which reach 0. Run three frames after each release.
    ///   Last, from position 600, five moves of 6 up every 0.3 ms (20,000 pixels a second, 24 pixels over the positions read), and
    ///   the frames run to the end.
    ///
    /// Expected:
    /// - The first three keep their position over a frame and ask for none after it. The last one's second frame is at the
    ///   fling of 8,000 from the release; after the frame that reaches 900, and the one its position asked
    ///   for, none is asked for, in less than half the fling's duration.
    /// </remarks>
    [Fact]
    public void Fling_OnlyPastFluttersConditions_AndStopsAtTheEnd() =>
        ui.Invoke(() =>
        {
            var scene = new Scene(scale: 10);
            scene.Draw();

            // A fling that did not start asks for no frame after the one the release's own move asked for: a fling's first frame,
            // even one that moves nothing, asks for the next.
            float released = scene.Swipe(fromY: 90, steps: 5, step: -0.4f, milliseconds: 10, start: 0, preDrag: true);
            Assert.Equal((released, false), (scene.FramesAfter(1), scene.Scheduler.Pending));
            released = scene.Swipe(fromY: 90, steps: 8, step: -2, milliseconds: 1, start: 1_000, preDrag: true);
            Assert.Equal((released, false), (scene.FramesAfter(1), scene.Scheduler.Pending));
            scene.ScrollTo(30);
            scene.Frames(1);
            released = scene.Swipe(fromY: 10, steps: 10, step: 10, milliseconds: 10, start: 2_000);
            Assert.Equal(0, released);
            Assert.Equal((0f, false), (scene.FramesAfter(1), scene.Scheduler.Pending));

            scene.ScrollTo(600);
            scene.Frames(1);
            released = scene.Swipe(fromY: 90, steps: 5, step: -6, milliseconds: 0.3, start: 3_000, preDrag: true);
            scene.Frames(2);
            Assert.Equal((float)new ClampingFling(released, 8_000).At(0.016), scene.Offset.Y, 2);
            for (int i = 0; i < 1_000 && scene.Offset.Y < 900; i++)
            {
                scene.Frames(1);
            }

            // The frame that reaches the end is the fling's last, long before its time is over: the position it wrote asks for one more
            // frame, which draws nothing new and asks for none.
            Assert.Equal(900, scene.Offset.Y);
            scene.Frames(1);
            Assert.False(scene.Scheduler.Pending);
            Assert.True(scene.Time.Now.TotalMilliseconds - 3_000 < new ClampingFling(released, 8_000).Duration * 1_000 / 2);
        });

    /// <summary>
    /// A press over a flinging container stops the fling and sends the nodes inside no down, up, or click; its finger can drag
    /// the container again. A wheel over a flinging container stops it and scrolls.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - The box whose height scrolls, its column 10,000 high, its item logging its down, up, and click, and the box its down.
    ///
    /// Steps:
    /// - Fling at 1,000; run two frames; touch down on the item, and run a frame; drag that finger 30 up and release slowly. Fling
    ///   again; run two frames; turn the wheel 50 down and run a frame.
    ///
    /// Expected:
    /// - The touch stops the fling: after the frame that was asked for, none is, and the position stays. The box logs the down,
    ///   the item nothing. The drag moves the position 10 past the slop (the move to −20 starts it, the move to −30 moves it). After the wheel, the position is the fling's last plus 50, and no frame is asked
    ///   for.
    /// </remarks>
    [Fact]
    public void Fling_StoppedByAPressOrAWheel() =>
        ui.Invoke(() =>
        {
            var scene = new Scene(contentHeight: 10_000);
            var log = new List<string>();
            scene.Log(scene.Item, log, "item");
            scene.Tree.AddPointerHandler(scene.Container, PointerEventKind.Down, EventPhase.Bubble, (in PointerEvent _) => log.Add("box down"));
            scene.Draw();

            scene.Swipe(fromY: 90, steps: 10, step: -10, milliseconds: 10);
            log.Clear();
            scene.Frames(2);
            float flung = scene.Offset.Y;
            scene.Touch(PointerInputKind.Down, 2, 50, 10, scene.Milliseconds);
            scene.Frames(1);
            Assert.False(scene.Scheduler.Pending);
            Assert.Equal(flung, scene.Offset.Y);
            Assert.Equal(["box down"], log);
            scene.Touch(PointerInputKind.Move, 2, 50, -20, scene.Milliseconds + 10);
            scene.Touch(PointerInputKind.Move, 2, 50, -30, scene.Milliseconds + 20);
            scene.Touch(PointerInputKind.Up, 2, 50, -30, scene.Milliseconds + 200);
            Assert.Equal(flung + 10, scene.Offset.Y);
            Assert.Equal(["box down"], log);

            scene.Swipe(fromY: 90, steps: 10, step: -10, milliseconds: 10, start: scene.Milliseconds + 1_000);
            scene.Frames(2);
            float before = scene.Offset.Y;
            scene.Input.Handle(
                new PointerInput(
                    PointerInputKind.Wheel,
                    50,
                    50,
                    PointerButton.None,
                    PointerButtons.None,
                    KeyModifiers.None,
                    0,
                    scene.Time.Now,
                    PointerType.Mouse,
                    new Vector2(0, 50)
                )
            );
            scene.Frames(1);
            Assert.Equal(before + 50, scene.Offset.Y);
            Assert.False(scene.Scheduler.Pending);
        });

    /// <summary>
    /// A container at the end the drag moves it to does not take the drag: the node pressed keeps its press and its moves.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - The box whose height scrolls, at position 0; its item logs its down, cancel, and moves.
    ///
    /// Steps:
    /// - Touch down at (50, 10) on the item; move to (50, 40) and to (50, 45), down.
    ///
    /// Expected:
    /// - The item logs the down and both moves, and no cancel; the position stays 0.
    /// </remarks>
    [Fact]
    public void Drag_TowardTheEndItIsAt_IsNotTaken() =>
        ui.Invoke(() =>
        {
            var scene = new Scene();
            var log = new List<string>();
            scene.Log(scene.Item, log, "item");
            scene.Draw();

            scene.Touch(PointerInputKind.Down, 1, 50, 10, 0);
            scene.Touch(PointerInputKind.Move, 1, 50, 40, 10);
            scene.Touch(PointerInputKind.Move, 1, 50, 45, 20);

            Assert.Equal(["item down", "item move", "item move"], log);
            Assert.Equal(0, scene.Offset.Y);
        });

    /// <summary>
    /// A press starts its positions again: its velocity reads none of the press before it, even when it comes within 40 ms of
    /// that press's last move.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - The box whose height scrolls, its column 10,000 high.
    ///
    /// Steps:
    /// - Fling at 1,000 and run a frame. 10 ms after the fling's last move, touch down again at (50, 50), which stops the fling;
    ///   10 ms later move to (50, 120), down, which starts a drag, and release there; run three frames.
    ///
    /// Expected:
    /// - The new press has two positions, too few for a velocity, so the position stays where the release left it. With the
    ///   positions of the press before, which end 10 ms earlier, the velocity would read a movement of 50 and fling.
    /// </remarks>
    [Fact]
    public void Velocity_AnotherPress_ReadsNoneOfThePressBefore() =>
        ui.Invoke(() =>
        {
            var scene = new Scene(contentHeight: 10_000);
            scene.Draw();
            _ = scene.Swipe(fromY: 90, steps: 10, step: -10, milliseconds: 10);
            scene.Frames(1);
            double last = 100;

            scene.Touch(PointerInputKind.Down, 1, 50, 50, last + 10);
            scene.Touch(PointerInputKind.Move, 1, 50, 120, last + 20);
            scene.Touch(PointerInputKind.Up, 1, 50, 120, last + 20);
            float released = scene.Offset.Y;

            Assert.Equal(released, scene.FramesAfter(3));
        });

    /// <summary>
    /// The steady state of a drag and its fling allocates nothing: the touch's down, the moves that start and drive the drag, the
    /// release that flings, and the frames of the fling to its end.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - The box whose height scrolls, its column 10,000 high; warmed up with the same iterations.
    ///
    /// Steps:
    /// - 1,000 iterations in the windows of <c>SteadyStateAllocation</c>: put the position at 0; swipe up at 1,000 pixels a second;
    ///   run the frames until none is asked for.
    ///
    /// Expected:
    /// - 0 bytes in every window, and each iteration flings past 250 (the drag ends near 80, and the fling travels about 194).
    /// </remarks>
    [Fact]
    public void SteadyState_DragAndFling_AllocateNothing() =>
        ui.Invoke(() =>
        {
            var scene = new Scene(contentHeight: 10_000);
            scene.Draw();
            int flings = 0;

            void Iteration(int index)
            {
                scene.ScrollTo(0);
                _ = scene.Swipe(fromY: 90, steps: 10, step: -10, milliseconds: 10, start: scene.Milliseconds + 1_000);
                for (int i = 0; i < 1_000 && scene.Scheduler.Pending; i++)
                {
                    scene.Time.Now += TimeSpan.FromMilliseconds(16);
                    scene.Scheduler.Run();
                }

                if (scene.Offset.Y > 250)
                {
                    flings++;
                }
            }

            for (int i = 0; i < 20; i++)
            {
                Iteration(i);
            }

            flings = 0;
            SteadyStateAllocation.AssertNone(1_000, Iteration);
            Assert.Equal(1_100, flings);
        });

    private static bool Within((double X, double Y) actual, (double X, double Y) expected) =>
        Math.Abs((actual.X - expected.X) / expected.X) < 0.001 && Math.Abs((actual.Y - expected.Y) / expected.Y) < 0.001;

    // A window of 200 x 100 logical pixels: a box of the window's size whose height scrolls, holding a column of the height given,
    // with an item 200 x 50 at its top; the frames run on a clock the test moves.
    private sealed class Scene
    {
        private readonly float _scale;

        public Scene(float scale = 1, float contentHeight = 1_000)
        {
            _scale = scale;
            Root = Tree.CreateNode(new LayoutStyle());
            Container = Box(Tree, Root, At(0, 0, 200, 100) with { OverflowY = Overflow.Scroll });
            Content = Box(Tree, Container, new LayoutStyle { Width = Length.Px(200), Height = Length.Px(contentHeight) });
            Item = Box(Tree, Content, At(0, 0, 200, 50));
            FrameRunner = new WindowFrames(Tree, Root, Target, new GlyphAtlas(new NoGlyphs()), Scheduler, Gray, time: Time);
            Input = new WindowInput(Tree, FrameRunner, new NoPlatform());
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

        public void Draw() => FrameRunner.Resize(new Size(200, 100), _scale);

        // The node's down, cancel, up, click, and moves, in the log under the name given.
        public void Log(NodeHandle node, List<string> log, string name)
        {
            foreach (PointerEventKind kind in new[] { PointerEventKind.Down, PointerEventKind.Cancel, PointerEventKind.Up, PointerEventKind.Click })
            {
                string entry = $"{name} {kind.ToString().ToLowerInvariant()}";
                Tree.AddPointerHandler(node, kind, EventPhase.Bubble, (in PointerEvent _) => log.Add(entry));
            }

            string move = $"{name} move";
            Tree.AddPointerHandler(node, PointerEventKind.Move, EventPhase.Bubble, (in PointerEvent _) => log.Add(move));
        }

        public void ScrollTo(float y) => Tree.ScrollStateFor(Container).ScrollTo(new Vector2(0, y));

        // Runs frames as Frames does, and returns the position after them.
        public float FramesAfter(int count)
        {
            Frames(count);
            return Offset.Y;
        }

        public void Frames(int count)
        {
            for (int i = 0; i < count; i++)
            {
                Time.Now += TimeSpan.FromMilliseconds(16);
                Scheduler.Run();
            }
        }

        // A touch of number 1 down at (50, fromY) at the time given, moved `step` logical pixels on y `steps` times, one every
        // `milliseconds`, and released with the last move. With preDrag, three moves of 10 up 1 ms apart first turn it into a drag,
        // and 100 ms pass before the others, so that the velocity reads only them. Returns the position at the release.
        public float Swipe(float fromY, int steps, float step, double milliseconds, double start = 0, bool preDrag = false)
        {
            double now = start;
            float y = fromY;
            Touch(PointerInputKind.Down, 1, 50, (int)MathF.Round(y * _scale), now);
            if (preDrag)
            {
                for (int i = 0; i < 3; i++)
                {
                    now += 1;
                    y -= 10;
                    Touch(PointerInputKind.Move, 1, 50, (int)MathF.Round(y * _scale), now);
                }

                now += 100;
            }

            for (int i = 0; i < steps; i++)
            {
                now += milliseconds;
                y += step;
                Touch(PointerInputKind.Move, 1, 50, (int)MathF.Round(y * _scale), now);
            }

            Time.Now = TimeSpan.FromMilliseconds(now);
            float released = Offset.Y;
            Touch(PointerInputKind.Up, 1, 50, (int)MathF.Round(y * _scale), now);
            return released;
        }

        public void Touch(PointerInputKind kind, int id, int x, int y, double milliseconds) => Send(kind, id, x, y, milliseconds, PointerType.Touch);

        public void Pen(PointerInputKind kind, int id, int x, int y, double milliseconds) => Send(kind, id, x, y, milliseconds, PointerType.Pen);

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
                    IsPrimary: id == 1,
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

    private sealed class NoPlatform : IPointerPlatform
    {
        public void Capture() { }

        public void ReleaseCapture() { }

        public void SetCursor(CursorKind cursor) { }
    }
}
