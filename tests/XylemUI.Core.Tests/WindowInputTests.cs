using System.Numerics;
using XylemUI.Text;
using static XylemUI.Tests.FrameDoubles;
using static XylemUI.Tests.LayoutTestTree;

namespace XylemUI.Tests;

// The pointer's input of a window (row V1c, design §7, "Pointer input"): the routes, the capture, clicks, the hover and the
// pseudo-states, the cursor, and what the hit regions of the draw stage make of them. The inputs are made as Win32 gives them:
// integer client coordinates in physical pixels.
[Collection(UiThreadGroup.Name)]
public sealed class WindowInputTests(UiThreadFixture ui)
{
    private static readonly Color Gray = Color.FromRgb(0x808080);
    private static readonly Color Red = Color.FromRgb(0xCC3333);

    /// <summary>
    /// A routed event goes down the capture phase from the root to the node hit, then up the bubble phase, and a handler that
    /// stops it ends both phases.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A parent box (0, 0, 100 x 100) holds a child box (10, 10, 50 x 50); each has a down handler in both phases that logs.
    ///
    /// Steps:
    /// - Press the left button at (20, 20); release. Then have the child's capture handler stop the event, and press again.
    ///
    /// Expected:
    /// - The first press logs parent capture, child capture, child bubble, parent bubble. The second logs parent capture and
    ///   child capture only.
    /// </remarks>
    [Fact]
    public void Route_CaptureThenBubble_StopPropagationEndsBothPhases() =>
        ui.Invoke(() =>
        {
            var scene = new Scene();
            NodeHandle parent = scene.Place(scene.Root, 0, 0, 100, 100);
            NodeHandle child = scene.Place(parent, 10, 10, 50, 50);
            var log = new List<string>();
            bool stop = false;
            scene.Tree.AddPointerHandler(parent, PointerEventKind.Down, EventPhase.Capture, (in PointerEvent _) => log.Add("parent capture"));
            scene.Tree.AddPointerHandler(parent, PointerEventKind.Down, EventPhase.Bubble, (in PointerEvent _) => log.Add("parent bubble"));
            scene.Tree.AddPointerHandler(
                child,
                PointerEventKind.Down,
                EventPhase.Capture,
                (in PointerEvent e) =>
                {
                    log.Add("child capture");
                    if (stop)
                    {
                        e.StopPropagation();
                    }
                }
            );
            scene.Tree.AddPointerHandler(child, PointerEventKind.Down, EventPhase.Bubble, (in PointerEvent _) => log.Add("child bubble"));
            scene.Draw();

            scene.Press(20, 20);
            scene.Release(20, 20);
            Assert.Equal(["parent capture", "child capture", "child bubble", "parent bubble"], log);
            log.Clear();
            stop = true;
            scene.Press(20, 20);
            Assert.Equal(["parent capture", "child capture"], log);
        });

    /// <summary>
    /// A press captures the pointer to the node hit: the moves and the release outside it still go to it, and the capture is
    /// released with the last button.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A box (10, 10, 50 x 50) logs its move and up events; the window is 200 x 100.
    ///
    /// Steps:
    /// - Press the left button at (20, 20); move to (150, 90) and to (-30, 5), outside the box and the window; release at (-30, 5).
    ///
    /// Expected:
    /// - The box gets both moves and the up, at the positions given; the platform captured once and released once, after the up.
    /// </remarks>
    [Fact]
    public void Capture_MovesAndReleaseOutside_GoToThePressedNode() =>
        ui.Invoke(() =>
        {
            var scene = new Scene();
            NodeHandle box = scene.Place(scene.Root, 10, 10, 50, 50);
            var log = new List<string>();
            scene.Tree.AddPointerHandler(box, PointerEventKind.Move, EventPhase.Bubble, (in PointerEvent e) => log.Add($"move {e.Position}"));
            scene.Tree.AddPointerHandler(
                box,
                PointerEventKind.Up,
                EventPhase.Bubble,
                (in PointerEvent e) => log.Add($"up {e.Position} {scene.Platform.Releases}")
            );
            scene.Draw();

            scene.Press(20, 20);
            Assert.Equal(1, scene.Platform.Captures);
            scene.Move(150, 90, PointerButtons.Left);
            scene.Move(-30, 5, PointerButtons.Left);
            scene.Release(-30, 5);

            Assert.Equal([$"move {new Vector2(150, 90)}", $"move {new Vector2(-30, 5)}", $"up {new Vector2(-30, 5)} 0"], log);
            Assert.Equal(1, scene.Platform.Releases);
            Assert.True(scene.Input.Capture.IsNone);
        });

    /// <summary>
    /// A click goes to the node the left button went down on when the release is over it or one of its descendants, and bubbles;
    /// a release elsewhere, and the right button, make none.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A parent box (0, 0, 100 x 100) with a click handler; inside it a button box (10, 10, 50 x 50) with a click handler and a
    ///   child box (20, 20, 10 x 10) with a down handler, so that it writes a hit region; a sibling box (120, 10, 50 x 50) with a
    ///   down handler.
    ///
    /// Steps:
    /// - Press at (15, 15) on the button, release at (35, 35) on its child. Press on the button, release at (130, 20) on the
    ///   sibling. Press and release the right button on the button.
    ///
    /// Expected:
    /// - Only the first makes a click: the button's handler, then the parent's.
    /// </remarks>
    [Fact]
    public void Click_ReleaseOverThePressedNodeOrADescendant_Bubbles() =>
        ui.Invoke(() =>
        {
            var scene = new Scene();
            NodeHandle parent = scene.Place(scene.Root, 0, 0, 100, 100);
            NodeHandle button = scene.Place(parent, 10, 10, 50, 50);
            NodeHandle inner = scene.Place(button, 10, 10, 10, 10);
            NodeHandle sibling = scene.Place(scene.Root, 120, 10, 50, 50);
            var log = new List<string>();
            scene.Tree.AddPointerHandler(parent, PointerEventKind.Click, EventPhase.Bubble, (in PointerEvent e) => log.Add("parent"));
            scene.Tree.AddPointerHandler(button, PointerEventKind.Click, EventPhase.Bubble, (in PointerEvent e) => log.Add("button"));
            scene.Tree.AddPointerHandler(inner, PointerEventKind.Down, EventPhase.Bubble, (in PointerEvent _) => { });
            scene.Tree.AddPointerHandler(sibling, PointerEventKind.Down, EventPhase.Bubble, (in PointerEvent _) => { });
            scene.Draw();

            scene.Press(15, 15);
            scene.Release(35, 35);
            Assert.Equal(["button", "parent"], log);
            log.Clear();
            scene.Press(15, 15);
            scene.Release(130, 20);
            scene.Press(15, 15, PointerButton.Right);
            scene.Release(15, 15, PointerButton.Right);
            Assert.Empty(log);
        });

    /// <summary>
    /// When another window takes the capture, the pressed node gets a cancel, its pressed state ends, and no click follows.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A box (10, 10, 50 x 50) with cancel and click handlers.
    ///
    /// Steps:
    /// - Press at (20, 20); report the capture lost; release at (20, 20).
    ///
    /// Expected:
    /// - One cancel and no click; the box is not pressed after the loss, and the window released no capture of its own.
    /// </remarks>
    [Fact]
    public void CaptureLost_SendsCancel_AndNoClick() =>
        ui.Invoke(() =>
        {
            var scene = new Scene();
            NodeHandle box = scene.Place(scene.Root, 10, 10, 50, 50);
            var log = new List<string>();
            scene.Tree.AddPointerHandler(box, PointerEventKind.Cancel, EventPhase.Bubble, (in PointerEvent _) => log.Add("cancel"));
            scene.Tree.AddPointerHandler(box, PointerEventKind.Click, EventPhase.Bubble, (in PointerEvent _) => log.Add("click"));
            scene.Draw();

            scene.Press(20, 20);
            Assert.True(scene.Has(box, PseudoState.Pressed));
            scene.Send(PointerInputKind.CaptureLost, 0, 0, PointerButton.None, PointerButtons.Left);
            Assert.False(scene.Has(box, PseudoState.Pressed));
            scene.Release(20, 20);

            Assert.Equal(["cancel"], log);
            Assert.Equal(0, scene.Platform.Releases);
        });

    /// <summary>
    /// A captured node that is removed while the button is down drops the capture quietly: the release reaches no handler and
    /// throws nothing.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A box (10, 10, 50 x 50) with an up handler that logs.
    ///
    /// Steps:
    /// - Press at (20, 20); free the box's slot in the arena (no removal API exists before row V1d); release at (20, 20).
    ///
    /// Expected:
    /// - Nothing is logged, and the window's capture is released.
    /// </remarks>
    [Fact]
    public void Capture_NodeRemoved_IsReleasedQuietly() =>
        ui.Invoke(() =>
        {
            var scene = new Scene();
            NodeHandle box = scene.Place(scene.Root, 10, 10, 50, 50);
            var log = new List<string>();
            scene.Tree.AddPointerHandler(box, PointerEventKind.Up, EventPhase.Bubble, (in PointerEvent _) => log.Add("up"));
            scene.Draw();

            scene.Press(20, 20);
            scene.Tree.Nodes.Free(box);
            scene.Release(20, 20);

            Assert.Empty(log);
            Assert.Equal(1, scene.Platform.Releases);
            Assert.True(scene.Input.Capture.IsNone);
        });

    /// <summary>
    /// Moving between nodes sends the leaves of the nodes left, innermost first, then the enters of the nodes entered, outermost
    /// first; the nodes both chains share get neither.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A parent box (0, 0, 100 x 100) holds a child box (10, 10, 50 x 50); a sibling box (120, 10, 50 x 50) holds its own child
    ///   (130, 20, 10 x 10). Each logs its enter and leave; the root logs its own.
    ///
    /// Steps:
    /// - Move to (20, 20); to (25, 25); to (135, 25); leave the window.
    ///
    /// Expected:
    /// - Enter root, parent, child; nothing; leave child, parent, enter sibling, its child; leave its child, sibling, root.
    /// </remarks>
    [Fact]
    public void Hover_MoveBetweenNodes_LeavesInnerFirstThenEntersOuterFirst() =>
        ui.Invoke(() =>
        {
            var scene = new Scene();
            NodeHandle parent = scene.Place(scene.Root, 0, 0, 100, 100);
            NodeHandle child = scene.Place(parent, 10, 10, 50, 50);
            NodeHandle sibling = scene.Place(scene.Root, 120, 10, 50, 50);
            NodeHandle inner = scene.Place(sibling, 10, 10, 10, 10);
            var log = new List<string>();
            foreach (
                (NodeHandle node, string name) in new[]
                {
                    (scene.Root, "root"),
                    (parent, "parent"),
                    (child, "child"),
                    (sibling, "sibling"),
                    (inner, "inner"),
                }
            )
            {
                scene.Tree.AddPointerHandler(node, PointerEventKind.Enter, EventPhase.Bubble, (in PointerEvent e) => log.Add($"enter {name}"));
                scene.Tree.AddPointerHandler(node, PointerEventKind.Leave, EventPhase.Bubble, (in PointerEvent e) => log.Add($"leave {name}"));
            }

            scene.Draw();
            scene.Move(20, 20);
            Assert.Equal(["enter root", "enter parent", "enter child"], log);
            log.Clear();
            scene.Move(25, 25);
            Assert.Empty(log);
            scene.Move(135, 25);
            Assert.Equal(["leave child", "leave parent", "enter sibling", "enter inner"], log);
            log.Clear();
            scene.Send(PointerInputKind.Leave, 0, 0, PointerButton.None, PointerButtons.None);
            Assert.Equal(["leave inner", "leave sibling", "leave root"], log);
        });

    /// <summary>
    /// The hovered state is on the node hit and its ancestors, the pressed state on the pressed node and its ancestors until the
    /// release, even outside; a change marks only the nodes with a look for that state.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A parent box (0, 0, 100 x 100) with a down handler and no state looks; inside it a button box (10, 10, 50 x 50) with a
    ///   hovered and a pressed background.
    ///
    /// Steps:
    /// - Move to (20, 20); press; move to (150, 50), outside both; release there.
    ///
    /// Expected:
    /// - After the move both are hovered and only the button is in the paint updates. After the press both are pressed. Outside,
    ///   neither is hovered (the hover follows the capture's node only over it) and both stay pressed. After the release neither is
    ///   pressed.
    /// </remarks>
    [Fact]
    public void PseudoStates_HoverAndPress_MarkOnlyTheNodesWithALook() =>
        ui.Invoke(() =>
        {
            var scene = new Scene();
            NodeHandle parent = scene.Place(scene.Root, 0, 0, 100, 100);
            NodeHandle button = scene.Place(parent, 10, 10, 50, 50);
            scene.Tree.AddPointerHandler(parent, PointerEventKind.Down, EventPhase.Bubble, (in PointerEvent _) => { });
            scene.Tree.SetHoveredStyle(button, new PaintStyle { Background = Red });
            scene.Tree.SetPressedStyle(button, new PaintStyle { Background = Gray });
            scene.Draw();

            scene.Move(20, 20);
            Assert.True(scene.Has(button, PseudoState.Hovered) && scene.Has(parent, PseudoState.Hovered));
            Assert.Equal([button], scene.Tree.PaintUpdates);
            scene.Draw();
            scene.Press(20, 20);
            Assert.True(scene.Has(button, PseudoState.Pressed) && scene.Has(parent, PseudoState.Pressed));
            scene.Move(150, 50, PointerButtons.Left);
            Assert.False(scene.Has(button, PseudoState.Hovered) || scene.Has(parent, PseudoState.Hovered));
            Assert.True(scene.Has(button, PseudoState.Pressed) && scene.Has(parent, PseudoState.Pressed));
            scene.Release(150, 50);
            Assert.False(scene.Has(button, PseudoState.Pressed) || scene.Has(parent, PseudoState.Pressed));
        });

    /// <summary>
    /// The cursor is the first one set from the node hit up to the root, from the capture's node while it holds the capture, and the
    /// arrow where none is set.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A parent box (0, 0, 100 x 100) with the hand cursor holds a child box (10, 10, 50 x 50) with a down handler and no cursor;
    ///   a sibling box (120, 10, 50 x 50) with the text cursor.
    ///
    /// Steps:
    /// - Move to (20, 20); to (130, 20); to (190, 90), over nothing. Press at (20, 20) and move to (130, 20) with the button down.
    ///
    /// Expected:
    /// - Hand, text, arrow; hand on the press, and still hand over the sibling while the child holds the capture.
    /// </remarks>
    [Fact]
    public void Cursor_NearestSetFromTheNodeHit_CaptureFirst() =>
        ui.Invoke(() =>
        {
            var scene = new Scene();
            NodeHandle parent = scene.Place(scene.Root, 0, 0, 100, 100);
            NodeHandle child = scene.Place(parent, 10, 10, 50, 50);
            NodeHandle sibling = scene.Place(scene.Root, 120, 10, 50, 50);
            scene.Tree.SetCursor(parent, CursorKind.Hand);
            scene.Tree.AddPointerHandler(child, PointerEventKind.Down, EventPhase.Bubble, (in PointerEvent _) => { });
            scene.Tree.SetCursor(sibling, CursorKind.IBeam);
            scene.Draw();

            scene.Move(20, 20);
            scene.Move(130, 20);
            scene.Move(190, 90);
            scene.Press(20, 20);
            scene.Move(130, 20, PointerButtons.Left);

            Assert.Equal([CursorKind.Hand, CursorKind.IBeam, CursorKind.Arrow, CursorKind.Hand], scene.Platform.Cursors);
        });

    /// <summary>
    /// A handler that throws hands its exception to the handler of unhandled exceptions, and the next handler of the route is
    /// called.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A parent box holds a child box; the child's down handler throws, the parent's logs.
    ///
    /// Steps:
    /// - Press on the child.
    ///
    /// Expected:
    /// - The exception is reported once, and the parent's handler runs.
    /// </remarks>
    [Fact]
    public void Handler_Throws_IsReportedAndTheRouteGoesOn() =>
        ui.Invoke(() =>
        {
            var scene = new Scene();
            NodeHandle parent = scene.Place(scene.Root, 0, 0, 100, 100);
            NodeHandle child = scene.Place(parent, 10, 10, 50, 50);
            var log = new List<string>();
            scene.Tree.AddPointerHandler(
                child,
                PointerEventKind.Down,
                EventPhase.Bubble,
                (in PointerEvent _) => throw new InvalidOperationException("thrown")
            );
            scene.Tree.AddPointerHandler(parent, PointerEventKind.Down, EventPhase.Bubble, (in PointerEvent _) => log.Add("parent"));
            scene.Draw();
            var reported = new List<Exception>();
            Action<Exception> previous = ReactiveRuntime.UnhandledExceptionHandler;
            ReactiveRuntime.UnhandledExceptionHandler = reported.Add;
            try
            {
                scene.Press(20, 20);
            }
            finally
            {
                ReactiveRuntime.UnhandledExceptionHandler = previous;
            }

            Assert.Equal("thrown", Assert.Single(reported).Message);
            Assert.Equal(["parent"], log);
        });

    /// <summary>
    /// After a frame that moved a node from under a pointer that did not move, the window's frames test the pointer again: the node
    /// gets its leave and loses its hovered state, and the node now under the pointer enters.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A box (10, 10, 50 x 50) and another (100, 10, 50 x 50) log their enters and leaves. The pointer moved to (20, 20).
    ///
    /// Steps:
    /// - Move the first box to (100, 60) and the second to (10, 10) by their styles; run the frame the tree asks for.
    ///
    /// Expected:
    /// - The frame logs the first box's leave, then the second's enter; the second is hovered.
    /// </remarks>
    [Fact]
    public void Rehit_NodesMovedUnderAStillPointer_SendsLeaveAndEnter() =>
        ui.Invoke(() =>
        {
            var scene = new Scene();
            NodeHandle first = scene.Place(scene.Root, 10, 10, 50, 50);
            NodeHandle second = scene.Place(scene.Root, 100, 10, 50, 50);
            var log = new List<string>();
            foreach ((NodeHandle node, string name) in new[] { (first, "first"), (second, "second") })
            {
                scene.Tree.AddPointerHandler(node, PointerEventKind.Enter, EventPhase.Bubble, (in PointerEvent e) => log.Add($"enter {name}"));
                scene.Tree.AddPointerHandler(node, PointerEventKind.Leave, EventPhase.Bubble, (in PointerEvent e) => log.Add($"leave {name}"));
            }

            scene.Draw();
            scene.Move(20, 20);
            log.Clear();
            scene.Tree.SetStyle(first, Scene.At(100, 60, 50, 50));
            scene.Tree.SetStyle(second, Scene.At(10, 10, 50, 50));
            scene.Scheduler.Run();

            Assert.Equal(["leave first", "enter second"], log);
            Assert.True(scene.Has(second, PseudoState.Hovered));
        });

    /// <summary>
    /// A leave handler that runs a frame at once (a window's synchronous resize) does not have the frame's rehit change the hover
    /// while the leaves and enters are sent: the rehit waits for them, then tests the pointer's position again (PR #327's review).
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A box A (10, 10, 50 x 50) and a box B (100, 10, 50 x 50), each with a hovered background, log their enters and leaves.
    ///   A's leave handler swaps the two boxes' places by their styles and runs the frames' resize, which draws and rehits before it
    ///   returns. The pointer moved to (20, 20), over A.
    ///
    /// Steps:
    /// - Move to (110, 20), over B.
    ///
    /// Expected:
    /// - Leave A, enter B; then, from the waiting rehit, A is under the pointer again: leave B, enter A. Each once; A is hovered and
    ///   B is not.
    /// </remarks>
    [Fact]
    public void Rehit_FromALeaveHandler_WaitsForTheChangeBeingSent() =>
        ui.Invoke(() =>
        {
            var scene = new Scene();
            NodeHandle a = scene.Place(scene.Root, 10, 10, 50, 50);
            NodeHandle b = scene.Place(scene.Root, 100, 10, 50, 50);
            var log = new List<string>();
            bool swapped = false;
            foreach ((NodeHandle node, string name) in new[] { (a, "A"), (b, "B") })
            {
                scene.Tree.SetHoveredStyle(node, new PaintStyle { Background = Red });
                scene.Tree.AddPointerHandler(node, PointerEventKind.Enter, EventPhase.Bubble, (in PointerEvent e) => log.Add($"enter {name}"));
                scene.Tree.AddPointerHandler(
                    node,
                    PointerEventKind.Leave,
                    EventPhase.Bubble,
                    (in PointerEvent e) =>
                    {
                        log.Add($"leave {name}");
                        if (name == "A" && !swapped)
                        {
                            swapped = true;
                            scene.Tree.SetStyle(a, Scene.At(100, 10, 50, 50));
                            scene.Tree.SetStyle(b, Scene.At(10, 10, 50, 50));
                            scene.Frames.Resize(new Size(200, 100), 1);
                        }
                    }
                );
            }

            scene.Draw();
            scene.Move(20, 20);
            log.Clear();
            scene.Move(110, 20);

            Assert.Equal(["leave A", "enter B", "leave B", "enter A"], log);
            Assert.True(scene.Has(a, PseudoState.Hovered));
            Assert.False(scene.Has(b, PseudoState.Hovered));
        });

    /// <summary>
    /// A leave of the window that comes while enters and leaves are sent (a handler that pumps messages) waits for them, then empties
    /// the hover: each node gets one leave, and none stays hovered (Issue #329).
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A box A (10, 10, 50 x 50) and a box B (100, 10, 50 x 50) log their enters and leaves. A's leave handler hands the input a
    ///   leave of the window, as a message pumped inside it would. The pointer moved to (20, 20), over A.
    ///
    /// Steps:
    /// - Move to (110, 20), over B.
    ///
    /// Expected:
    /// - Leave A, enter B, then, from the waiting leave of the window, leave B. Neither box is hovered.
    /// </remarks>
    [Fact]
    public void WindowLeave_FromALeaveHandler_WaitsForTheChangeBeingSent() =>
        ui.Invoke(() =>
        {
            var scene = new Scene();
            NodeHandle a = scene.Place(scene.Root, 10, 10, 50, 50);
            NodeHandle b = scene.Place(scene.Root, 100, 10, 50, 50);
            var log = new List<string>();
            bool left = false;
            foreach ((NodeHandle node, string name) in new[] { (a, "A"), (b, "B") })
            {
                scene.Tree.AddPointerHandler(node, PointerEventKind.Enter, EventPhase.Bubble, (in PointerEvent e) => log.Add($"enter {name}"));
                scene.Tree.AddPointerHandler(
                    node,
                    PointerEventKind.Leave,
                    EventPhase.Bubble,
                    (in PointerEvent e) =>
                    {
                        log.Add($"leave {name}");
                        if (name == "A" && !left)
                        {
                            left = true;
                            scene.Send(PointerInputKind.Leave, 0, 0, PointerButton.None, PointerButtons.None);
                        }
                    }
                );
            }

            scene.Draw();
            scene.Move(20, 20);
            log.Clear();
            scene.Move(110, 20);

            Assert.Equal(["leave A", "enter B", "leave B"], log);
            Assert.False(scene.Has(a, PseudoState.Hovered));
            Assert.False(scene.Has(b, PseudoState.Hovered));
        });

    /// <summary>
    /// A handler's local position is in its own node's coordinates before the node's transform, from the top left of its border
    /// box, in logical pixels, at a window scale that is not 1; the window position is the physical one over the scale.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - At a scale of 2, a box (20, 10, 40 x 20) scaled by 2 about its center (40, 20) holds a child at (5, 5) of it, 10 x 10
    ///   (25, 15 to 35, 25 before the scale); both have a down handler.
    ///
    /// Steps:
    /// - Press at the physical point (40, 40): logical (20, 20), which the scale about (40, 20) moves back to (30, 20).
    ///
    /// Expected:
    /// - The child is the target, and both handlers get the position (20, 20). The child's local position is (5, 5), (30, 20) less
    ///   its top left; the box's is (10, 10).
    /// </remarks>
    [Fact]
    public void LocalPosition_InsideATransformAtScaleTwo_IsInTheNodesCoordinates() =>
        ui.Invoke(() =>
        {
            var scene = new Scene(scale: 2);
            NodeHandle box = scene.Place(scene.Root, 20, 10, 40, 20);
            NodeHandle child = scene.Place(box, 5, 5, 10, 10);
            scene.Tree.SetTransform(box, Matrix3x2.CreateScale(2));
            var seen = new List<(NodeHandle Node, Vector2 Position, Vector2 Local, NodeHandle Target)>();
            foreach (NodeHandle node in new[] { box, child })
            {
                scene.Tree.AddPointerHandler(
                    node,
                    PointerEventKind.Down,
                    EventPhase.Bubble,
                    (in PointerEvent e) => seen.Add((e.CurrentNode, e.Position, e.LocalPosition, e.TargetNode))
                );
            }

            scene.Draw();
            scene.Press(40, 40);

            Assert.Equal(2, seen.Count);
            Assert.Equal((child, new Vector2(20, 20), new Vector2(5, 5), child), seen[0]);
            Assert.Equal((box, new Vector2(20, 20), new Vector2(10, 10), child), seen[1]);
        });

    /// <summary>
    /// A node that blocks the pointer with no handler hides the node behind it, and the event goes to its own ancestors; without
    /// the block the node behind gets it.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A back box (10, 10, 50 x 50) with a down handler; after it, in front, an overlay box (0, 0, 100 x 100) with no handler,
    ///   under a holder box (0, 0, 100 x 100) with a down handler.
    ///
    /// Steps:
    /// - Press at (20, 20). Set the overlay to block the pointer, draw, and press again.
    ///
    /// Expected:
    /// - First the back box gets the down and the holder none; then the holder gets it and the back box none.
    /// </remarks>
    [Fact]
    public void BlocksPointer_NoHandler_HidesTheNodeBehindAndRoutesToItsAncestors() =>
        ui.Invoke(() =>
        {
            var scene = new Scene();
            NodeHandle back = scene.Place(scene.Root, 10, 10, 50, 50);
            NodeHandle holder = scene.Place(scene.Root, 0, 0, 100, 100);
            NodeHandle overlay = scene.Place(holder, 0, 0, 100, 100);
            var log = new List<string>();
            scene.Tree.AddPointerHandler(back, PointerEventKind.Down, EventPhase.Bubble, (in PointerEvent _) => log.Add("back"));
            scene.Tree.AddPointerHandler(holder, PointerEventKind.Down, EventPhase.Bubble, (in PointerEvent _) => log.Add("holder"));

            // The holder writes its own hit region, in front of the back box: the press would reach it without the overlay. A
            // holder whose region is behind the back box shows the overlay's block alone.
            scene.Tree.SetStyle(holder, Scene.At(0, 0, 5, 5));
            scene.Draw();
            scene.Press(20, 20);
            scene.Release(20, 20);
            scene.Tree.SetBlocksPointer(overlay, true);
            scene.Draw();
            scene.Press(20, 20);

            Assert.Equal(["back", "holder"], log);
        });

    /// <summary>
    /// A subtree of opacity 0 draws nothing and is still hit, as Flutter and Avalonia hit it.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A box (10, 10, 50 x 50) with a background, opacity 0, and a down handler.
    ///
    /// Steps:
    /// - Draw; read the display list; press at (20, 20).
    ///
    /// Expected:
    /// - The list holds no command and one hit region, and the box gets the down.
    /// </remarks>
    [Fact]
    public void OpacityZero_DrawsNothing_AndIsHit() =>
        ui.Invoke(() =>
        {
            var scene = new Scene();
            NodeHandle box = scene.Place(scene.Root, 10, 10, 50, 50);
            scene.Tree.SetBackground(box, new Paint(Red));
            scene.Tree.SetOpacity(box, 0);
            int downs = 0;
            scene.Tree.AddPointerHandler(box, PointerEventKind.Down, EventPhase.Bubble, (in PointerEvent _) => downs++);
            scene.Draw();

            Assert.Equal(0, scene.Frames.List.CommandCount);
            Assert.Equal(1, scene.Frames.List.HitEntries.Length);
            scene.Press(20, 20);
            Assert.Equal(1, downs);
        });

    /// <summary>
    /// The steady state of the pointer allocates nothing: moving in and out of a node with a hovered look (its enter, its leave, and
    /// the frames that draw it), pressing and releasing on it (a click), and the frames' rehit.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A box (10, 10, 50 x 50) with a hovered background and enter, leave, and click handlers that count; warmed up with the
    ///   same iterations until one allocates nothing.
    ///
    /// Steps:
    /// - 1,000 iterations in the windows of <c>PerformanceAssert.DoesNotAllocate</c>: move in, run the frame asked for, press, release, move
    ///   out, run the frame.
    ///
    /// Expected:
    /// - 0 bytes in every window; the iterations count one enter, one leave, one click, and two presents each.
    /// </remarks>
    [Fact]
    public void SteadyState_HoverPressAndRehit_AllocateNothing() =>
        ui.Invoke(() =>
        {
            var scene = new Scene();
            NodeHandle box = scene.Place(scene.Root, 10, 10, 50, 50);
            scene.Tree.SetHoveredStyle(box, new PaintStyle { Background = Red });
            int enters = 0;
            int leaves = 0;
            int clicks = 0;
            scene.Tree.AddPointerHandler(box, PointerEventKind.Enter, EventPhase.Bubble, (in PointerEvent _) => enters++);
            scene.Tree.AddPointerHandler(box, PointerEventKind.Leave, EventPhase.Bubble, (in PointerEvent _) => leaves++);
            scene.Tree.AddPointerHandler(box, PointerEventKind.Click, EventPhase.Bubble, (in PointerEvent _) => clicks++);
            scene.Draw();

            void Iteration(int index)
            {
                scene.Move(20, 20);
                scene.Scheduler.Run();
                scene.Press(20, 20);
                scene.Release(20, 20);
                scene.Move(150, 80);
                scene.Scheduler.Run();
            }

            for (int i = 0; i < 20; i++)
            {
                Iteration(i);
            }

            int before = scene.Frames.PresentCount;
            (enters, leaves, clicks) = (0, 0, 0);
            int runs = PerformanceAssert.DoesNotAllocate(1_000, Iteration);

            // The helper runs 100 unmeasured iterations before the 1,000, and a window again when a background GC overlapped it.
            Assert.Equal((runs, runs, runs), (enters, leaves, clicks));
            Assert.Equal(before + (2 * runs), scene.Frames.PresentCount);
        });

    /// <summary>
    /// A node holds its input and its pseudo-states without growing past 576 bytes, and the state looks live apart from the box's
    /// look, so that a node with a background alone pays for neither (PR #319's review); the focus looks live apart from the hovered
    /// and pressed ones (row K1a).
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - The types as built.
    ///
    /// Steps:
    /// - Read <c>Unsafe.SizeOf</c> of <see cref="UiNode"/> and <see cref="PaintStyle"/>; allocate one <see cref="NodeVisual"/>, one
    ///   <see cref="PseudoStyles"/>, and one <see cref="FocusStyles"/> and read what each allocated.
    /// - The bytes are counted with GC.GetAllocatedBytesForCurrentThread, not PerformanceAssert: each count is the size of one
    ///   object made once, not a steady state that allocates nothing.
    ///
    /// Expected:
    /// - The node is at most 576 bytes, a style at most 104, a box look at most 192, the state looks at most 336 (with the disabled look, row K1b), and the focus looks
    ///   at most 328.
    /// </remarks>
    [Fact]
    public void Sizes_NodeAndLooks_StayWithinTheirBounds()
    {
        Assert.InRange(System.Runtime.CompilerServices.Unsafe.SizeOf<UiNode>(), 0, 576);
        Assert.InRange(System.Runtime.CompilerServices.Unsafe.SizeOf<PaintStyle>(), 0, 104);
        Assert.InRange(System.Runtime.CompilerServices.Unsafe.SizeOf<PointerInput>(), 0, 64);
        Assert.InRange(Allocated(static () => new NodeVisual()), 0, 192);
        Assert.InRange(Allocated(static () => new PseudoStyles()), 0, 336);
        Assert.InRange(Allocated(static () => new FocusStyles()), 0, 328);
    }

    private static long Allocated(Func<object> make)
    {
        _ = make();
        long before = GC.GetAllocatedBytesForCurrentThread();
        object made = make();
        long bytes = GC.GetAllocatedBytesForCurrentThread() - before;
        GC.KeepAlive(made);
        return bytes;
    }

    // A window of 200 x 100 logical pixels whose root holds the boxes placed in it, with its frames and input.
    private sealed class Scene
    {
        private readonly float _scale;

        public Scene(float scale = 1)
        {
            _scale = scale;
            Tree = new UiTree();
            Root = Tree.CreateNode(new LayoutStyle());
            Atlas = new GlyphAtlas(new NoGlyphs());
            Frames = new WindowFrames(Tree, Root, Target, Atlas, Scheduler, Gray);
            Input = new WindowInput(Tree, Frames, Platform);
        }

        public UiTree Tree { get; }

        public NodeHandle Root { get; }

        public GlyphAtlas Atlas { get; }

        public CountingTarget Target { get; } = new();

        public CountingScheduler Scheduler { get; } = new();

        public FakePlatform Platform { get; } = new();

        public WindowFrames Frames { get; }

        public WindowInput Input { get; }

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

        // A box at (x, y) of the parent's box, of the size given.
        public NodeHandle Place(NodeHandle parent, float x, float y, float width, float height) => Box(Tree, parent, At(x, y, width, height));

        // Lays out and draws: the first time by the resize, then by the frame the tree asked for.
        public void Draw()
        {
            if (Frames.PresentCount == 0)
            {
                Frames.Resize(new Size(200, 100), _scale);
            }
            else
            {
                Scheduler.Run();
            }
        }

        public bool Has(NodeHandle node, PseudoState state) => (Tree.Nodes.GetRef(node).Pseudo & state) != 0;

        public void Move(int x, int y, PointerButtons buttons = PointerButtons.None) =>
            Send(PointerInputKind.Move, x, y, PointerButton.None, buttons);

        public void Press(int x, int y, PointerButton button = PointerButton.Left) => Send(PointerInputKind.Down, x, y, button, Mask(button));

        public void Release(int x, int y, PointerButton button = PointerButton.Left) => Send(PointerInputKind.Up, x, y, button, PointerButtons.None);

        public void Send(PointerInputKind kind, int x, int y, PointerButton button, PointerButtons buttons) =>
            Input.Handle(new PointerInput(kind, x, y, button, buttons, KeyModifiers.None, 1, TimeSpan.Zero, PointerType.Mouse));

        private static PointerButtons Mask(PointerButton button) =>
            button switch
            {
                PointerButton.Left => PointerButtons.Left,
                PointerButton.Right => PointerButtons.Right,
                _ => PointerButtons.Middle,
            };
    }

    // Records what the input asked of the window.
    private sealed class FakePlatform : IPointerPlatform
    {
        public int Captures { get; private set; }

        public int Releases { get; private set; }

        public List<CursorKind> Cursors { get; } = [];

        public void Capture() => Captures++;

        public void ReleaseCapture() => Releases++;

        public void SetCursor(CursorKind cursor) => Cursors.Add(cursor);
    }
}
