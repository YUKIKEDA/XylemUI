using System.Numerics;
using static XylemUI.Tests.LayoutTestTree;

namespace XylemUI.Tests;

// Row K1b (design §7, "Keys and the focus"): the disabled state, keyboard scrolling, and bringing the focused node into view. The
// button's keys are in XylemUI.Primitives.Tests.
public sealed partial class KeyFocusTests
{
    /// <summary>
    /// The disabled state follows a node's own mark and its ancestors': it reaches the whole subtree, stays on a descendant with a
    /// mark of its own when the ancestor's is cleared, and comes to a subtree appended to a disabled parent.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A parent holds a child, which holds a grandchild with its own mark. The window is started.
    ///
    /// Steps:
    /// - Mark the parent; read the three. Clear the parent's mark; read them. Mark the parent again and append a new box to it.
    ///
    /// Expected:
    /// - All three disabled; then only the grandchild; then the new box disabled too. The parent is marked to be drawn again.
    /// </remarks>
    [Fact]
    public void Disabled_FollowsTheOwnAndTheAncestorsMarks() =>
        ui.Invoke(() =>
        {
            var scene = new Scene();
            NodeHandle parent = scene.Place(scene.Root, 0, 0, 100, 100);
            NodeHandle child = scene.Place(parent, 0, 0, 50, 50);
            NodeHandle grandchild = scene.Place(child, 0, 0, 20, 20);
            scene.Tree.SetDisabled(grandchild, true);
            scene.Start();

            scene.Tree.SetDisabled(parent, true);
            Assert.True(
                scene.Has(parent, PseudoState.Disabled) && scene.Has(child, PseudoState.Disabled) && scene.Has(grandchild, PseudoState.Disabled)
            );
            Assert.Contains(parent, scene.Tree.PaintUpdates);
            scene.Tree.SetDisabled(parent, false);
            Assert.Equal(
                (false, false, true),
                (scene.Has(parent, PseudoState.Disabled), scene.Has(child, PseudoState.Disabled), scene.Has(grandchild, PseudoState.Disabled))
            );
            scene.Tree.SetDisabled(parent, true);
            NodeHandle appended = scene.Place(parent, 60, 0, 20, 20);
            Assert.True(scene.Has(appended, PseudoState.Disabled));
        });

    /// <summary>
    /// A disabled node's handlers are not called; the pointer it takes still does not reach the node behind it, and the route, the
    /// hover, and the cursor start at its nearest ancestor that is not disabled.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A box behind (0, 0, 100 x 100) with a down handler; over it a parent (0, 0, 100 x 100) with a down handler and the hand cursor,
    ///   holding a disabled child (10, 10, 50 x 50) with a down handler, the text cursor, and a hovered look. Drawn once.
    ///
    /// Steps:
    /// - Move to (20, 20) and press there.
    ///
    /// Expected:
    /// - Only the parent's handler ran, with the parent as the target; the child is not hovered and the parent is; the cursor is the
    ///   hand.
    /// </remarks>
    [Fact]
    public void Disabled_CallsNoHandler_AndTheRouteStartsAtTheEnabledAncestor() =>
        ui.Invoke(() =>
        {
            var scene = new Scene();
            NodeHandle behind = scene.Place(scene.Root, 0, 0, 100, 100);
            NodeHandle parent = scene.Place(scene.Root, 0, 0, 100, 100);
            NodeHandle child = scene.Place(parent, 10, 10, 50, 50);
            var log = new List<string>();
            scene.Tree.AddPointerHandler(behind, PointerEventKind.Down, EventPhase.Bubble, (in PointerEvent _) => log.Add("behind"));
            scene.Tree.AddPointerHandler(
                parent,
                PointerEventKind.Down,
                EventPhase.Bubble,
                (in PointerEvent e) => log.Add(e.TargetNode == parent ? "parent from parent" : "parent from other")
            );
            scene.Tree.AddPointerHandler(child, PointerEventKind.Down, EventPhase.Bubble, (in PointerEvent _) => log.Add("child"));
            scene.Tree.SetCursor(parent, CursorKind.Hand);
            scene.Tree.SetCursor(child, CursorKind.IBeam);
            scene.Tree.SetHoveredStyle(child, new PaintStyle { Background = Red });
            scene.Tree.SetDisabled(child, true);
            scene.Start();

            scene.Move(20, 20);
            scene.Press(20, 20);

            Assert.Equal(["parent from parent"], log);
            Assert.False(scene.Has(child, PseudoState.Hovered));
            Assert.True(scene.Has(parent, PseudoState.Hovered));
            Assert.Equal(CursorKind.Hand, scene.Input.Cursor);
        });

    /// <summary>
    /// A node disabled while it holds the pointer's capture gets no more events: the moves routed to it call none of its handlers.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A box (10, 10, 50 x 50) with a move handler that counts; drawn once.
    ///
    /// Steps:
    /// - Press at (20, 20); move to (30, 30); disable the box; move to (40, 40); release.
    ///
    /// Expected:
    /// - One move counted, the one before the box was disabled.
    /// </remarks>
    [Fact]
    public void Disabled_WhileCaptured_GetsNoMoreEvents() =>
        ui.Invoke(() =>
        {
            var scene = new Scene();
            NodeHandle box = scene.Place(scene.Root, 10, 10, 50, 50);
            int moves = 0;
            scene.Tree.AddPointerHandler(box, PointerEventKind.Move, EventPhase.Bubble, (in PointerEvent _) => moves++);
            scene.Start();

            scene.Press(20, 20);
            scene.Move(30, 30, PointerButtons.Left);
            scene.Tree.SetDisabled(box, true);
            scene.Move(40, 40, PointerButtons.Left);
            scene.Release(40, 40);

            Assert.Equal(1, moves);
        });

    /// <summary>
    /// A disabled node takes no focus: <c>Focus</c> refuses it, Tab skips a disabled subtree, and the focused node loses the focus when
    /// an ancestor becomes disabled, with a focus-out that only the enabled ancestors' handlers see.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - Focusable a; a container holding focusable b, whose own focus-out handler logs; focusable c. The root logs each focus-out
    ///   with its target. The window is active.
    ///
    /// Steps:
    /// - Focus b; disable the container. Tab twice. Call Focus on b.
    ///
    /// Expected:
    /// - The root logged the focus-out of b and b's own handler did not run; the focus was empty; Tab gives a, then c; Focus on b is
    ///   false.
    /// </remarks>
    [Fact]
    public void Disabled_TakesNoFocus_AndLosesItWithAFocusOut() =>
        ui.Invoke(() =>
        {
            var scene = new Scene();
            NodeHandle a = scene.Focusable(scene.Root, 0, 0, 20, 20);
            NodeHandle container = scene.Place(scene.Root, 30, 0, 50, 50);
            NodeHandle b = scene.Focusable(container, 0, 0, 20, 20);
            NodeHandle c = scene.Focusable(scene.Root, 90, 0, 20, 20);
            var log = new List<string>();
            scene.Tree.AddFocusHandler(b, FocusEventKind.FocusOut, EventPhase.Bubble, (in FocusEvent _) => log.Add("b's own"));
            scene.Tree.AddFocusHandler(
                scene.Root,
                FocusEventKind.FocusOut,
                EventPhase.Bubble,
                (in FocusEvent e) => log.Add(e.TargetNode == b ? "root: out b" : "root: out other")
            );
            scene.Start();
            Assert.True(scene.Tree.Focus.Focus(b));

            scene.Tree.SetDisabled(container, true);
            Assert.True(scene.Tree.Focus.Focused.IsNone);
            Assert.True(scene.Tab());
            NodeHandle first = scene.Tree.Focus.Focused;
            Assert.True(scene.Tab());
            NodeHandle second = scene.Tree.Focus.Focused;

            Assert.Equal(["root: out b", "root: out other"], log);
            Assert.Equal((a, c), (first, second));
            Assert.False(scene.Tree.Focus.Focus(b));
        });

    /// <summary>
    /// A scroll container on the key's route takes the keys no handler stopped and that have no modifier: the arrows by 50, the page
    /// keys by 0.8 of the viewport, Home and End to the ends; a key that does not move it, a key with a modifier, and a key a handler
    /// stopped go on.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A container (0, 0, 200 x 100) whose height scrolls holds a content box 1,000 high with a focusable leaf at its top; the leaf
    ///   has the focus, and a bubble down handler on the container itself, which runs before its scrolling, stops the keys while a flag is set.
    ///
    /// Steps:
    /// - Send Down, a repeated Down, Page Down, End, Home, Up (at 0), Shift+Down, and, with the flag set, Down.
    ///
    /// Expected:
    /// - Positions 50, 100, 180, 900, 0, 0, 0, 0; the keys are handled, but for Up at 0 and Shift+Down; the stopped Down is handled and
    ///   does not move.
    /// </remarks>
    [Fact]
    public void KeyScroll_ArrowsPagesAndEnds_MoveTheContainer() =>
        ui.Invoke(() =>
        {
            var scene = new Scene();
            (NodeHandle container, NodeHandle content) = scene.Scroller(scene.Root, 0, 0, 200, 100, 1_000);
            NodeHandle leaf = scene.Focusable(content, 0, 0, 20, 20);
            bool stop = false;
            scene.Tree.AddKeyHandler(
                container,
                KeyEventKind.Down,
                EventPhase.Bubble,
                (in KeyEvent e) =>
                {
                    if (stop)
                    {
                        e.StopPropagation();
                    }
                }
            );
            scene.Start();
            Assert.True(scene.Tree.Focus.Focus(leaf));
            scene.Draw();
            var results = new List<(bool, float)>();
            void Send(Key key, KeyModifiers modifiers = KeyModifiers.None, bool repeat = false)
            {
                bool handled = scene.Input.Handle(new KeyInput(KeyEventKind.Down, key, PhysicalKey.None, default, modifiers, repeat, TimeSpan.Zero));
                results.Add((handled, scene.Tree.ScrollOffsetOf(container).Y));
            }

            Send(Key.Down);
            Send(Key.Down, repeat: true);
            Send(Key.PageDown);
            Send(Key.End);
            Send(Key.Home);
            Send(Key.Up);
            Send(Key.Down, KeyModifiers.Shift);
            stop = true;
            Send(Key.Down);

            Assert.Equal([(true, 50f), (true, 100f), (true, 180f), (true, 900f), (true, 0f), (false, 0f), (false, 0f), (true, 0f)], results);
        });

    /// <summary>
    /// A key that an inner scroll container cannot move further goes on to the container outside it, as the wheel does.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - An outer container (0, 0, 200 x 100, content 1,000 high) holds an inner one (0, 0, 200 x 100, content 150 high) with a focusable
    ///   leaf; the leaf has the focus.
    ///
    /// Steps:
    /// - Send Down twice.
    ///
    /// Expected:
    /// - The first moves the inner container to 50, its end; the second moves the outer one to 50.
    /// </remarks>
    [Fact]
    public void KeyScroll_AtTheEnd_GoesOnToTheOuterContainer() =>
        ui.Invoke(() =>
        {
            var scene = new Scene();
            (NodeHandle outer, NodeHandle outerContent) = scene.Scroller(scene.Root, 0, 0, 200, 100, 1_000);
            (NodeHandle inner, NodeHandle innerContent) = scene.Scroller(outerContent, 0, 0, 200, 100, 150);
            NodeHandle leaf = scene.Focusable(innerContent, 0, 0, 20, 20);
            scene.Start();
            Assert.True(scene.Tree.Focus.Focus(leaf));
            scene.Draw();

            _ = scene.Key(Key.Down);
            var first = (scene.Tree.ScrollOffsetOf(inner).Y, scene.Tree.ScrollOffsetOf(outer).Y);
            _ = scene.Key(Key.Down);
            var second = (scene.Tree.ScrollOffsetOf(inner).Y, scene.Tree.ScrollOffsetOf(outer).Y);

            Assert.Equal((50f, 0f), first);
            Assert.Equal((50f, 50f), second);
        });

    /// <summary>
    /// A move of the focus by Tab or <c>Focus</c> scrolls the focused node into view at the next frame, by the least the container
    /// needs, and to its start for a node larger than the viewport; a press moves nothing.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A container (0, 0, 200 x 100, content 1,000 high) holds focusable leaves: p at y = 0 (30 high), q at 90 (30 high), r at 200
    ///   (30 high), and s at 400 (300 high). The window is active.
    ///
    /// Steps:
    /// - Press q at (5, 95); run a frame. Tab from q to r; run a frame. Focus p; run a frame. Focus s; run a frame.
    ///
    /// Expected:
    /// - 0 after the press (q stays partly hidden), 130 after Tab (r's bottom at the viewport's bottom), 0 for p, 400 for s (its top at
    ///   the viewport's top).
    /// </remarks>
    [Fact]
    public void Reveal_AfterTabOrFocus_ByTheLeast_NotAfterAPress() =>
        ui.Invoke(() =>
        {
            var scene = new Scene();
            (NodeHandle container, NodeHandle content) = scene.Scroller(scene.Root, 0, 0, 200, 100, 1_000);
            NodeHandle p = scene.Focusable(content, 0, 0, 20, 30);
            _ = scene.Focusable(content, 0, 90, 20, 30);
            _ = scene.Focusable(content, 0, 200, 20, 30);
            NodeHandle s = scene.Focusable(content, 0, 400, 20, 300);
            scene.Start();
            var offsets = new List<float>();
            void Frame()
            {
                scene.Draw();
                offsets.Add(scene.Tree.ScrollOffsetOf(container).Y);
            }

            scene.Press(5, 95);
            scene.Release(5, 95);
            Frame();
            Assert.True(scene.Tab());
            Frame();
            Assert.True(scene.Tree.Focus.Focus(p));
            Frame();
            Assert.True(scene.Tree.Focus.Focus(s));
            Frame();

            Assert.Equal([0f, 130f, 0f, 400f], offsets);
        });

    /// <summary>
    /// Bringing a node into view moves each scroll container from the node up, the outer one from the position the inner one moved to.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - An outer container (0, 0, 200 x 100, content 1,000 high) holds, at y = 150, an inner container (200 x 100, content 1,000 high)
    ///   with a focusable leaf at y = 300 (20 high). The window is active.
    ///
    /// Steps:
    /// - Focus the leaf; run a frame.
    ///
    /// Expected:
    /// - The inner container at 220 (the leaf's bottom at its viewport's bottom); the outer at 150 (the leaf, at 230 to 250 in the
    ///   outer content, at the outer viewport's bottom).
    /// </remarks>
    [Fact]
    public void Reveal_NestedContainers_MoveInnerThenOuter() =>
        ui.Invoke(() =>
        {
            var scene = new Scene();
            (NodeHandle outer, NodeHandle outerContent) = scene.Scroller(scene.Root, 0, 0, 200, 100, 1_000);
            (NodeHandle inner, NodeHandle innerContent) = scene.Scroller(outerContent, 0, 150, 200, 100, 1_000);
            NodeHandle leaf = scene.Focusable(innerContent, 0, 300, 20, 20);
            scene.Start();

            Assert.True(scene.Tree.Focus.Focus(leaf));
            scene.Draw();

            Assert.Equal((220f, 150f), (scene.Tree.ScrollOffsetOf(inner).Y, scene.Tree.ScrollOffsetOf(outer).Y));
        });

    /// <summary>
    /// The disabled look lays over the box's background and the other looks in the display list, and goes with the mark.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A box (10, 10, 50 x 50) with a gray background, a hovered look of red, and a disabled look of blue; the pointer over it.
    ///
    /// Steps:
    /// - Draw; disable the box and draw; enable it and draw.
    ///
    /// Expected:
    /// - Red (hovered), blue (disabled, and no longer hovered after the frame's hover test), red again.
    /// </remarks>
    [Fact]
    public void DisplayList_DisabledLook_LaysOverTheOthers() =>
        ui.Invoke(() =>
        {
            var scene = new Scene();
            NodeHandle box = scene.Place(scene.Root, 10, 10, 50, 50);
            scene.Tree.SetBackground(box, Gray);
            scene.Tree.SetHoveredStyle(box, new PaintStyle { Background = Red });
            scene.Tree.SetDisabledStyle(box, new PaintStyle { Background = Blue });
            scene.Start();
            scene.Move(20, 20);
            scene.Draw();
            var colors = new List<Color> { scene.BoxColor() };

            scene.Tree.SetDisabled(box, true);
            scene.Draw();
            colors.Add(scene.BoxColor());
            scene.Tree.SetDisabled(box, false);
            scene.Draw();
            scene.Draw();
            colors.Add(scene.BoxColor());

            Assert.Equal([Red, Blue, Red], colors);
        });

    /// <summary>
    /// Scrolling by keys and bringing a node into view allocate nothing in the steady state: the key's route and the scroll, the
    /// focus's move, and the frames that draw them.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A container (0, 0, 200 x 100, content 1,000 high) with focusable leaves at y = 0 and y = 600; warmed up with the same
    ///   iterations.
    ///
    /// Steps:
    /// - 1,000 iterations in the windows of <c>PerformanceAssert.DoesNotAllocate</c>: focus the leaf at 0 and run a frame, send Down and run a
    ///   frame, focus the leaf at 600 and run a frame.
    ///
    /// Expected:
    /// - 0 bytes in every window; the container ends each iteration at 520.
    /// </remarks>
    [Fact]
    public void SteadyState_KeyScrollAndRevealFrames_AllocateNothing() =>
        ui.Invoke(() =>
        {
            var scene = new Scene();
            (NodeHandle container, NodeHandle content) = scene.Scroller(scene.Root, 0, 0, 200, 100, 1_000);
            NodeHandle top = scene.Focusable(content, 0, 0, 20, 20);
            NodeHandle low = scene.Focusable(content, 0, 600, 20, 20);
            scene.Start();

            void Iteration(int index)
            {
                _ = scene.Tree.Focus.Focus(top);
                scene.Draw();
                _ = scene.Key(Key.Down);
                scene.Draw();
                _ = scene.Tree.Focus.Focus(low);
                scene.Draw();
            }

            for (int i = 0; i < 20; i++)
            {
                Iteration(i);
            }

            PerformanceAssert.DoesNotAllocate(1_000, Iteration);
            Assert.Equal(520f, scene.Tree.ScrollOffsetOf(container).Y);
        });

    /// <summary>
    /// A click a key makes allocates nothing: the key's route and the click routed from the node, as the button's Enter makes it.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A focused box with a key-down handler that makes a click from the key (<c>WindowInput.ClickFromKey</c>, as
    ///   <c>Primitive.Button</c> does) and a click handler that counts; warmed up with the same iterations.
    ///
    /// Steps:
    /// - 1,000 iterations in the windows of <c>PerformanceAssert.DoesNotAllocate</c>: send a down of Enter.
    ///
    /// Expected:
    /// - 0 bytes in every window; one click each iteration.
    /// </remarks>
    [Fact]
    public void SteadyState_ClickFromAKey_AllocatesNothing() =>
        ui.Invoke(() =>
        {
            var scene = new Scene();
            NodeHandle box = scene.Focusable(scene.Root, 10, 10, 50, 50);
            int clicks = 0;
            scene.Tree.AddKeyHandler(
                box,
                KeyEventKind.Down,
                EventPhase.Bubble,
                static (in KeyEvent e) =>
                {
                    e.StopPropagation();
                    e.Input.ClickFromKey(e.CurrentNode, e.Timestamp);
                }
            );
            scene.Tree.AddPointerHandler(box, PointerEventKind.Click, EventPhase.Bubble, (in PointerEvent _) => clicks++);
            scene.Start();
            Assert.True(scene.Tree.Focus.Focus(box));

            void Iteration(int index) => _ = scene.Key(Key.Enter);

            for (int i = 0; i < 20; i++)
            {
                Iteration(i);
            }

            clicks = 0;
            int runs = PerformanceAssert.DoesNotAllocate(1_000, Iteration);

            // The helper runs 100 unmeasured iterations before the 1,000, and a window again when a background GC overlapped it.
            Assert.Equal(runs, clicks);
        });

    private sealed partial class Scene
    {
        // A container at (x, y) of the parent whose height scrolls, and its content box, in the flow, of the content's height.
        public (NodeHandle Container, NodeHandle Content) Scroller(
            NodeHandle parent,
            float x,
            float y,
            float width,
            float height,
            float contentHeight
        )
        {
            NodeHandle container = Place(parent, x, y, width, height);
            Tree.SetStyle(container, Tree.Nodes.GetRef(container).Layout.Style with { OverflowY = Overflow.Scroll });
            NodeHandle content = Box(Tree, container, new LayoutStyle { Width = Length.Px(width), Height = Length.Px(contentHeight) });
            return (container, content);
        }

        public void Move(int x, int y, PointerButtons buttons = PointerButtons.None) =>
            Send(PointerInputKind.Move, x, y, PointerButton.None, buttons);
    }
}
