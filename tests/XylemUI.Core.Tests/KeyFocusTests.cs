using XylemUI.Text;
using static XylemUI.Tests.FrameDoubles;
using static XylemUI.Tests.LayoutTestTree;

namespace XylemUI.Tests;

// The keys and the focus of a window (row K1a, design §7, "Keys and the focus", §7.1, §7.2): the route of the keys, Tab, the focus
// on a press, the focus pseudo-states, the window's activation, the focus events, and the nodes that lose the focus. The keys are
// injected as KeyInput and the activation with SetActive, the entry of §12.4.
[Collection(UiThreadGroup.Name)]
public sealed partial class KeyFocusTests(UiThreadFixture ui)
{
    private static readonly Color Gray = Color.FromRgb(0x808080);
    private static readonly Color Red = Color.FromRgb(0xCC3333);
    private static readonly Color Blue = Color.FromRgb(0x3333CC);
    private static readonly Color Green = Color.FromRgb(0x33CC33);

    /// <summary>
    /// A key goes down the capture phase from the root to the focused node, then up the bubble phase; a handler that stops it ends
    /// both phases, and the input reports it handled.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A parent box holds a focusable child; each has a key-down handler in both phases that logs. The window is active, and the
    ///   child has the focus.
    ///
    /// Steps:
    /// - Send a down of A; then have the child's capture handler stop the event, and send it again.
    ///
    /// Expected:
    /// - The first logs root capture, parent capture, child capture, child bubble, parent bubble, root bubble, and is not handled. The
    ///   second logs root capture, parent capture, child capture, and is handled.
    /// </remarks>
    [Fact]
    public void Route_CaptureThenBubble_StopPropagationEndsBothPhases() =>
        ui.Invoke(() =>
        {
            var scene = new Scene();
            NodeHandle parent = scene.Place(scene.Root, 0, 0, 100, 100);
            NodeHandle child = scene.Focusable(parent, 10, 10, 50, 50);
            var log = new List<string>();
            bool stop = false;
            scene.LogKeys(scene.Root, "root", log);
            scene.LogKeys(parent, "parent", log);
            scene.Tree.AddKeyHandler(
                child,
                KeyEventKind.Down,
                EventPhase.Capture,
                (in KeyEvent e) =>
                {
                    log.Add("child capture");
                    if (stop)
                    {
                        e.StopPropagation();
                    }
                }
            );
            scene.Tree.AddKeyHandler(child, KeyEventKind.Down, EventPhase.Bubble, (in KeyEvent _) => log.Add("child bubble"));
            scene.Start();
            Assert.True(scene.Tree.Focus.Focus(child));

            Assert.False(scene.Key(Key.A));
            Assert.Equal(["root capture", "parent capture", "child capture", "child bubble", "parent bubble", "root bubble"], log);
            log.Clear();
            stop = true;
            Assert.True(scene.Key(Key.A));
            Assert.Equal(["root capture", "parent capture", "child capture"], log);
        });

    /// <summary>
    /// With no focused node, and while the window is not active, a key goes from the root; the event carries the key's values.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A focusable box; the root logs the target and the values of each key-down in the bubble phase.
    ///
    /// Steps:
    /// - Make the window active and send a down of Enter with Shift, repeated, and the character U+000D left out. Focus the box, make
    ///   the window not active, and send a down of A.
    ///
    /// Expected:
    /// - Both start at the root; the first has Enter, the physical Enter, Shift, the repeat, and no character.
    /// </remarks>
    [Fact]
    public void Route_NoFocusOrNotActive_StartsAtTheRoot() =>
        ui.Invoke(() =>
        {
            var scene = new Scene();
            NodeHandle box = scene.Focusable(scene.Root, 10, 10, 50, 50);
            var log = new List<string>();
            scene.Tree.AddKeyHandler(
                scene.Root,
                KeyEventKind.Down,
                EventPhase.Bubble,
                (in KeyEvent e) =>
                    log.Add($"{e.Target == new Element(scene.Tree, scene.Root)} {e.Key} {e.PhysicalKey} {e.Modifiers} {e.IsRepeat} {e.KeyChar.Value}")
            );
            scene.Start();

            _ = scene.Input.Handle(new KeyInput(KeyEventKind.Down, Key.Enter, PhysicalKey.Enter, default, KeyModifiers.Shift, true, TimeSpan.Zero));
            Assert.True(scene.Tree.Focus.Focus(box));
            scene.Input.SetActive(false);
            _ = scene.Key(Key.A);

            Assert.Equal(["True Enter Enter Shift True 0", "True A None None False 0"], log);
        });

    /// <summary>
    /// Tab goes through the focusable nodes in the order of (TabIndex, the tree's order), past the end to the start, and Shift+Tab
    /// goes back; hidden subtrees and nodes that are not focusable are skipped.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - In the tree's order: a (index 0), b (index −1), c (0), a box that is not focusable, d (1), and a hidden box that holds a
    ///   focusable e (−5). The window is active and nothing has the focus.
    ///
    /// Steps:
    /// - Tab five times; then Shift+Tab twice. Empty the focus by focusing nothing (a new scene), and Shift+Tab once.
    ///
    /// Expected:
    /// - Tab: b, a, c, d, b. Shift+Tab: d, c. From no focus, Shift+Tab: d. Each is handled.
    /// </remarks>
    [Fact]
    public void Tab_TabIndexThenTreeOrder_WrapsAndShiftGoesBack() =>
        ui.Invoke(() =>
        {
            (Scene scene, NodeHandle a, NodeHandle b, NodeHandle c, NodeHandle d) = OrderScene();
            var order = new List<NodeHandle>();
            for (int i = 0; i < 5; i++)
            {
                Assert.True(scene.Tab());
                order.Add(scene.Tree.Focus.Focused);
            }

            for (int i = 0; i < 2; i++)
            {
                Assert.True(scene.Tab(shift: true));
                order.Add(scene.Tree.Focus.Focused);
            }

            Assert.Equal([b, a, c, d, b, d, c], order);
            (Scene fresh, _, _, _, NodeHandle last) = OrderScene();
            Assert.True(fresh.Tab(shift: true));
            Assert.Equal(last, fresh.Tree.Focus.Focused);
        });

    /// <summary>
    /// Tab does not move with Control, Alt, or a Windows key, nor when a handler stopped it, nor when nothing can take the focus; then
    /// the key is not handled and goes to the OS.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - Two focusable boxes, the first focused; a root capture handler that stops the keys while a flag is set. A second scene with
    ///   no focusable node.
    ///
    /// Steps:
    /// - Send Control+Tab, Alt+Tab, Meta+Tab, Control+Shift+Tab; set the flag and send Tab. In the second scene, send Tab.
    ///
    /// Expected:
    /// - The focus stays on the first box. The four with a modifier and the second scene's Tab are not handled; the stopped Tab is.
    /// </remarks>
    [Fact]
    public void Tab_WithModifiersStoppedOrNoTarget_DoesNotMove() =>
        ui.Invoke(() =>
        {
            var scene = new Scene();
            NodeHandle first = scene.Focusable(scene.Root, 0, 0, 50, 50);
            _ = scene.Focusable(scene.Root, 60, 0, 50, 50);
            bool stop = false;
            scene.Tree.AddKeyHandler(
                scene.Root,
                KeyEventKind.Down,
                EventPhase.Capture,
                (in KeyEvent e) =>
                {
                    if (stop)
                    {
                        e.StopPropagation();
                    }
                }
            );
            scene.Start();
            Assert.True(scene.Tree.Focus.Focus(first));

            Assert.False(scene.Key(Key.Tab, KeyModifiers.Control));
            Assert.False(scene.Key(Key.Tab, KeyModifiers.Alt));
            Assert.False(scene.Key(Key.Tab, KeyModifiers.Meta));
            Assert.False(scene.Key(Key.Tab, KeyModifiers.Control | KeyModifiers.Shift));
            stop = true;
            Assert.True(scene.Tab());
            Assert.Equal(first, scene.Tree.Focus.Focused);

            var empty = new Scene();
            _ = empty.Place(empty.Root, 0, 0, 50, 50);
            empty.Start();
            Assert.False(empty.Tab());
            Assert.True(empty.Tree.Focus.Focused.IsNone);
        });

    /// <summary>
    /// A left press gives the focus to the nearest node that takes it from the node pressed up, before the press is routed; a press
    /// where no node takes it, and the right button, leave the focus.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A focusable card (0, 0, 100 x 100) holds a box (10, 10, 50 x 50) with a down handler that records the focused node; a
    ///   focusable button (120, 0, 50 x 50). The window is active.
    ///
    /// Steps:
    /// - Press and release at (20, 20), on the box. Press at (130, 10), on the button, with the right button. Press and release at
    ///   (190, 90), over nothing.
    ///
    /// Expected:
    /// - The box's handler saw the card focused; the card keeps the focus after the right press and after the press over nothing.
    /// </remarks>
    [Fact]
    public void Press_FocusesTheNearestFocusableAncestor_BeforeTheRoute() =>
        ui.Invoke(() =>
        {
            var scene = new Scene();
            NodeHandle card = scene.Focusable(scene.Root, 0, 0, 100, 100);
            NodeHandle box = scene.Place(card, 10, 10, 50, 50);
            _ = scene.Focusable(scene.Root, 120, 0, 50, 50);
            NodeHandle seen = default;
            scene.Tree.AddPointerHandler(box, PointerEventKind.Down, EventPhase.Capture, (in PointerEvent _) => seen = scene.Tree.Focus.Focused);
            scene.Start();

            scene.Press(20, 20);
            scene.Release(20, 20);
            Assert.Equal(card, seen);
            scene.Press(130, 10, PointerButton.Right);
            scene.Release(130, 10, PointerButton.Right);
            scene.Press(190, 90);
            scene.Release(190, 90);
            Assert.Equal(card, scene.Tree.Focus.Focused);
        });

    /// <summary>
    /// The focused node has the focused state and its ancestors the focus within; the focus ring shows after a key and not after a
    /// press, and a key brings it back; a change marks only the nodes with a look for that state.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A parent box with a focus-within look holds a focusable button with focused and focus-visible looks, and a focusable sibling
    ///   with no look; drawn once. The window is active.
    ///
    /// Steps:
    /// - Tab to the button; draw. Press the sibling; draw. Press the button; send a down of Shift.
    ///
    /// Expected:
    /// - After Tab: the button focused and focus-visible, the parent and the root focus-within; the button and the parent are the
    ///   paint updates. After the press on the sibling: the button has no focus state, the parent none, the sibling focused but not
    ///   focus-visible; the paint updates are the button and the parent. After the press on the button: focused, not focus-visible;
    ///   after Shift: focus-visible.
    /// </remarks>
    [Fact]
    public void PseudoStates_FocusAndRing_MarkOnlyTheNodesWithALook() =>
        ui.Invoke(() =>
        {
            var scene = new Scene();
            NodeHandle parent = scene.Place(scene.Root, 0, 0, 100, 100);
            NodeHandle button = scene.Focusable(parent, 10, 10, 50, 50);
            NodeHandle sibling = scene.Focusable(scene.Root, 120, 0, 50, 50);
            scene.Tree.SetFocusWithinStyle(parent, new PaintStyle { Background = Green });
            scene.Tree.SetFocusedStyle(button, new PaintStyle { Background = Red });
            scene.Tree.SetFocusVisibleStyle(button, new PaintStyle { Background = Blue });
            scene.Start();

            Assert.True(scene.Tab());
            Assert.True(scene.Has(button, PseudoState.Focused | PseudoState.FocusVisible | PseudoState.FocusWithin));
            Assert.True(scene.Has(parent, PseudoState.FocusWithin) && scene.Has(scene.Root, PseudoState.FocusWithin));
            Assert.Equal([button, parent], scene.Tree.PaintUpdates);
            scene.Draw();

            scene.Press(130, 10);
            scene.Release(130, 10);
            Assert.Equal(PseudoState.None, scene.Tree.Nodes.GetRef(button).Pseudo & ~PseudoState.Hovered);
            Assert.False(scene.Has(parent, PseudoState.FocusWithin));
            Assert.True(scene.Has(sibling, PseudoState.Focused) && !scene.Has(sibling, PseudoState.FocusVisible));
            Assert.Equal([button, parent], scene.Tree.PaintUpdates);
            scene.Draw();

            scene.Press(20, 20);
            scene.Release(20, 20);
            Assert.True(scene.Has(button, PseudoState.Focused) && !scene.Has(button, PseudoState.FocusVisible));
            _ = scene.Key(Key.LeftShift);
            Assert.True(scene.Has(button, PseudoState.FocusVisible));
        });

    /// <summary>
    /// The focus looks lay over the box's background in the display list: focus within, then focused, then focus visible, the later
    /// the stronger.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A focusable box (10, 10, 50 x 50) with a gray background, a focus-within look of green, a focused look of red, and a
    ///   focus-visible look of blue. The window is active.
    ///
    /// Steps:
    /// - Draw; Tab to the box and draw; press it and draw.
    ///
    /// Expected:
    /// - The box's rectangle is gray, then blue (focus visible over focused), then red (focused, without the ring after the press).
    /// </remarks>
    [Fact]
    public void DisplayList_FocusLooks_LayOverTheBackgroundInOrder() =>
        ui.Invoke(() =>
        {
            var scene = new Scene();
            NodeHandle box = scene.Focusable(scene.Root, 10, 10, 50, 50);
            scene.Tree.SetBackground(box, Gray);
            scene.Tree.SetFocusWithinStyle(box, new PaintStyle { Background = Green });
            scene.Tree.SetFocusedStyle(box, new PaintStyle { Background = Red });
            scene.Tree.SetFocusVisibleStyle(box, new PaintStyle { Background = Blue });
            scene.Start();
            var colors = new List<Color> { scene.BoxColor() };

            Assert.True(scene.Tab());
            scene.Draw();
            colors.Add(scene.BoxColor());
            scene.Press(20, 20);
            scene.Release(20, 20);
            scene.Draw();
            colors.Add(scene.BoxColor());

            Assert.Equal([Gray, Blue, Red], colors);
        });

    /// <summary>
    /// When the window stops being active, the focused node gets a focus-out and loses its states but is kept; when it is active
    /// again, it gets them back with a focus-in. A node focused meanwhile takes the focus then; one that cannot any more leaves the
    /// focus empty.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - Two focusable boxes, a and b, whose focus handlers log; a has the focus, and its IsFocused signal is read.
    ///
    /// Steps:
    /// - Make the window not active; active; not active, focus b; active. Not active; hide b; active.
    ///
    /// Expected:
    /// - "out a", then "in a" with the states and the signal back; nothing logged for focusing b while not active, then "in b" on
    ///   activation; after hiding b while not active, activation logs nothing and the focus is empty. Each focus-out leaves the node
    ///   kept as focused while it is not active.
    /// </remarks>
    [Fact]
    public void Activation_KeepsTheFocusAndGivesItBack() =>
        ui.Invoke(() =>
        {
            var scene = new Scene();
            NodeHandle a = scene.Focusable(scene.Root, 0, 0, 50, 50);
            NodeHandle b = scene.Focusable(scene.Root, 60, 0, 50, 50);
            var log = new List<string>();
            scene.LogFocus(a, "a", log);
            scene.LogFocus(b, "b", log);
            scene.Start();
            Assert.True(scene.Tree.Focus.Focus(a));
            IReadOnlySignal<bool> focused = scene.Tree.IsFocusedSignal(a);
            log.Clear();

            scene.Input.SetActive(false);
            Assert.Equal(a, scene.Tree.Focus.Focused);
            Assert.False(scene.Has(a, PseudoState.Focused) || scene.Has(scene.Root, PseudoState.FocusWithin));
            Assert.False(focused.Get());
            scene.Input.SetActive(true);
            Assert.True(scene.Has(a, PseudoState.Focused) && focused.Get());
            Assert.Equal(["out a", "in a"], log);
            log.Clear();

            scene.Input.SetActive(false);
            Assert.True(scene.Tree.Focus.Focus(b));
            Assert.Equal(["out a"], log);
            scene.Input.SetActive(true);
            Assert.Equal(["out a", "in b"], log);
            log.Clear();

            scene.Input.SetActive(false);
            scene.Hide(b);
            scene.Input.SetActive(true);
            Assert.Equal(["out b"], log);
            Assert.True(scene.Tree.Focus.Focused.IsNone);
        });

    /// <summary>
    /// A move of the focus sends the old node a focus-out and then the new one a focus-in, each routed from its node in the capture and
    /// the bubble phase, after the states and the signals changed.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A parent box holds focusable boxes a and b; the parent logs both kinds in both phases with the target, and a's handler of the
    ///   focus-out and b's of the focus-in log whether a and b are focused (state and signal). The window is active; a has the focus.
    ///
    /// Steps:
    /// - Tab from a to b.
    ///
    /// Expected:
    /// - "out capture a", a's check "a False False, b True True", "out bubble a", "in capture b", b's check with the same values,
    ///   "in bubble b": both events see both nodes' states and signals already changed.
    /// </remarks>
    [Fact]
    public void FocusEvents_OutThenIn_RoutedAfterTheStatesChanged() =>
        ui.Invoke(() =>
        {
            var scene = new Scene();
            NodeHandle parent = scene.Place(scene.Root, 0, 0, 200, 100);
            NodeHandle a = scene.Focusable(parent, 0, 0, 50, 50);
            NodeHandle b = scene.Focusable(parent, 60, 0, 50, 50);
            var log = new List<string>();
            string Name(Element e) => e == new Element(scene.Tree, a) ? "a" : "b";
            foreach (FocusEventKind kind in new[] { FocusEventKind.FocusIn, FocusEventKind.FocusOut })
            {
                string word = kind == FocusEventKind.FocusIn ? "in" : "out";
                scene.Tree.AddFocusHandler(parent, kind, EventPhase.Capture, (in FocusEvent e) => log.Add($"{word} capture {Name(e.Target)}"));
                scene.Tree.AddFocusHandler(parent, kind, EventPhase.Bubble, (in FocusEvent e) => log.Add($"{word} bubble {Name(e.Target)}"));
            }

            scene.Start();
            Assert.True(scene.Tree.Focus.Focus(a));
            IReadOnlySignal<bool> aFocused = scene.Tree.IsFocusedSignal(a);
            IReadOnlySignal<bool> bFocused = scene.Tree.IsFocusedSignal(b);
            void Check(in FocusEvent e) =>
                log.Add($"a {scene.Has(a, PseudoState.Focused)} {aFocused.Get()}, b {scene.Has(b, PseudoState.Focused)} {bFocused.Get()}");
            scene.Tree.AddFocusHandler(a, FocusEventKind.FocusOut, EventPhase.Bubble, Check);
            scene.Tree.AddFocusHandler(b, FocusEventKind.FocusIn, EventPhase.Bubble, Check);
            log.Clear();

            Assert.True(scene.Tab());

            Assert.Equal(
                ["out capture a", "a False False, b True True", "out bubble a", "in capture b", "a False False, b True True", "in bubble b"],
                log
            );
        });

    /// <summary>
    /// A move of the focus asked for while the focus events are sent waits for them to end, so the events being sent see the focus
    /// they were sent for.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - Focusable boxes a, b, and c whose focus handlers log; b's focus-in handler focuses c, and logs the focused node after it.
    ///   The window is active; a has the focus.
    ///
    /// Steps:
    /// - Tab from a to b.
    ///
    /// Expected:
    /// - "out a", "in b", b's log "focused b", then "out b", "in c"; c ends with the focus.
    /// </remarks>
    [Fact]
    public void FocusEvents_FocusFromAHandler_WaitsForTheEventsBeingSent() =>
        ui.Invoke(() =>
        {
            var scene = new Scene();
            NodeHandle a = scene.Focusable(scene.Root, 0, 0, 50, 50);
            NodeHandle b = scene.Focusable(scene.Root, 60, 0, 50, 50);
            NodeHandle c = scene.Focusable(scene.Root, 120, 0, 50, 50);
            var log = new List<string>();
            scene.LogFocus(a, "a", log);
            scene.LogFocus(b, "b", log);
            scene.LogFocus(c, "c", log);
            scene.Tree.AddFocusHandler(
                b,
                FocusEventKind.FocusIn,
                EventPhase.Bubble,
                (in FocusEvent _) =>
                {
                    Assert.True(scene.Tree.Focus.Focus(c));
                    log.Add($"focused {(scene.Tree.Focus.Focused == b ? "b" : "other")}");
                }
            );
            scene.Start();
            Assert.True(scene.Tree.Focus.Focus(a));
            log.Clear();

            Assert.True(scene.Tab());

            Assert.Equal(["out a", "in b", "focused b", "out b", "in c"], log);
            Assert.Equal(c, scene.Tree.Focus.Focused);
        });

    /// <summary>
    /// The focused node loses the focus with a focus-out when it or an ancestor is removed (sent while it is still in the tree), when
    /// an ancestor is hidden, and when it stops being focusable; the keys then go to the root and the next Tab starts from the first
    /// node.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A container holds focusable boxes a and b; a third focusable box c is outside it. The focus handlers log, a's focus-out with
    ///   whether a is still in the tree. The window is active.
    ///
    /// Steps:
    /// - Focus c, then make it not focusable. Focus b, hide the container. Show the container, focus a, remove the container. Send a
    ///   key; Tab.
    ///
    /// Expected:
    /// - "out c", "out b", "out a in the tree"; the focus is empty after each; the key goes from the root; Tab focuses the first
    ///   focusable node left, which is c made focusable again.
    /// </remarks>
    [Fact]
    public void Lose_RemovedHiddenOrUnfocusable_EmptiesTheFocusWithAFocusOut() =>
        ui.Invoke(() =>
        {
            var scene = new Scene();
            NodeHandle container = scene.Place(scene.Root, 0, 0, 200, 50);
            NodeHandle a = scene.Focusable(container, 0, 0, 50, 50);
            NodeHandle b = scene.Focusable(container, 60, 0, 50, 50);
            NodeHandle c = scene.Focusable(scene.Root, 0, 60, 50, 30);
            var log = new List<string>();
            scene.Tree.AddFocusHandler(
                a,
                FocusEventKind.FocusOut,
                EventPhase.Bubble,
                (in FocusEvent e) => log.Add($"out a {(scene.Tree.Nodes.GetRef(e.TargetNode).IsAttached ? "in the tree" : "out")}")
            );
            scene.LogFocus(b, "b", log);
            scene.LogFocus(c, "c", log);
            NodeHandle keyTarget = default;
            scene.Tree.AddKeyHandler(scene.Root, KeyEventKind.Down, EventPhase.Bubble, (in KeyEvent e) => keyTarget = e.TargetNode);
            scene.Start();

            Assert.True(scene.Tree.Focus.Focus(c));
            scene.Tree.SetFocusable(c, false);
            Assert.True(scene.Tree.Focus.Focused.IsNone);
            Assert.True(scene.Tree.Focus.Focus(b));
            scene.Hide(container);
            Assert.True(scene.Tree.Focus.Focused.IsNone);
            scene.Show(container);
            Assert.True(scene.Tree.Focus.Focus(a));
            scene.Tree.Remove(container);
            Assert.True(scene.Tree.Focus.Focused.IsNone);
            Assert.Equal(["in c", "out c", "in b", "out b", "out a in the tree"], log.Where(l => !l.StartsWith("in a", StringComparison.Ordinal)));

            _ = scene.Key(Key.A);
            Assert.Equal(scene.Root, keyTarget);
            scene.Tree.SetFocusable(c, true);
            Assert.True(scene.Tab());
            Assert.Equal(c, scene.Tree.Focus.Focused);
        });

    /// <summary>
    /// <c>Focus</c> takes only a node that can take the focus: focusable, in the window's tree, and neither it nor an ancestor hidden;
    /// for the others it returns false and changes nothing.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A box that is not focusable; a focusable box under a hidden parent; a focusable node not in the tree; a focusable box that
    ///   is itself hidden; a focusable box. The window is active.
    ///
    /// Steps:
    /// - Call Focus on each, in that order.
    ///
    /// Expected:
    /// - False for the first four with the focus still empty; true for the last, which then has the focus.
    /// </remarks>
    [Fact]
    public void Focus_OnlyANodeThatCanTakeIt_ReturnsWhetherItDid() =>
        ui.Invoke(() =>
        {
            var scene = new Scene();
            NodeHandle plain = scene.Place(scene.Root, 0, 0, 20, 20);
            NodeHandle hiddenParent = scene.Place(scene.Root, 30, 0, 20, 20);
            NodeHandle underHidden = scene.Focusable(hiddenParent, 0, 0, 10, 10);
            NodeHandle detached = scene.Tree.CreateNode(new LayoutStyle());
            scene.Tree.SetFocusable(detached, true);
            NodeHandle selfHidden = scene.Focusable(scene.Root, 60, 0, 20, 20);
            NodeHandle good = scene.Focusable(scene.Root, 90, 0, 20, 20);
            scene.Start();
            scene.Hide(hiddenParent);
            scene.Hide(selfHidden);

            Assert.Equal(
                [false, false, false, false, true],
                new[] { plain, underHidden, detached, selfHidden, good }.Select(n => scene.Tree.Focus.Focus(n))
            );
            Assert.Equal(good, scene.Tree.Focus.Focused);
        });

    /// <summary>
    /// The steady state of the keys and the focus allocates nothing: a key routed from a focused node 50 deep, Tab between two nodes
    /// with focus looks, and the frames that draw each change.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A chain of 50 boxes whose innermost is focusable and has a key handler in both phases that counts; beside it two focusable
    ///   boxes with focused and focus-visible looks. The window is active; warmed up with the same iterations.
    ///
    /// Steps:
    /// - 1,000 iterations in the windows of <c>SteadyStateAllocation</c>: focus the innermost box, send a down and an up of A, Tab
    ///   twice (to the first box and the second), and run the frame asked for.
    ///
    /// Expected:
    /// - 0 bytes in every window; the handlers counted six calls each iteration: the down and the up of A and the first Tab, which
    ///   starts at the innermost box, in two phases each.
    /// </remarks>
    [Fact]
    public void SteadyState_KeysTabAndFocusFrames_AllocateNothing() =>
        ui.Invoke(() =>
        {
            var scene = new Scene();
            NodeHandle deep = scene.Root;
            for (int i = 0; i < 50; i++)
            {
                deep = scene.Place(deep, 0, 0, 10, 10);
            }

            scene.Tree.SetFocusable(deep, true);
            scene.Tree.SetTabIndex(deep, 1);
            int calls = 0;
            foreach (KeyEventKind kind in new[] { KeyEventKind.Down, KeyEventKind.Up })
            {
                scene.Tree.AddKeyHandler(deep, kind, EventPhase.Capture, (in KeyEvent _) => calls++);
                scene.Tree.AddKeyHandler(deep, kind, EventPhase.Bubble, (in KeyEvent _) => calls++);
            }

            for (int x = 100; x <= 150; x += 50)
            {
                NodeHandle box = scene.Focusable(scene.Root, x, 0, 40, 40);
                scene.Tree.SetFocusedStyle(box, new PaintStyle { Background = Red });
                scene.Tree.SetFocusVisibleStyle(box, new PaintStyle { Background = Blue });
            }

            scene.Start();

            void Iteration(int index)
            {
                _ = scene.Tree.Focus.Focus(deep);
                _ = scene.Key(Key.A);
                _ = scene.Key(Key.A, kind: KeyEventKind.Up);
                _ = scene.Tab();
                _ = scene.Tab();
                scene.Scheduler.Run();
            }

            for (int i = 0; i < 20; i++)
            {
                Iteration(i);
            }

            calls = 0;
            SteadyStateAllocation.AssertNone(1_000, Iteration);
            Assert.Equal(1_100 * 6, calls);
        });

    // The focusable a, b, c, d of the order test in a started scene, with nothing focused.
    private static (Scene Scene, NodeHandle A, NodeHandle B, NodeHandle C, NodeHandle D) OrderScene()
    {
        var scene = new Scene();
        NodeHandle a = scene.Focusable(scene.Root, 0, 0, 20, 20);
        NodeHandle b = scene.Focusable(scene.Root, 30, 0, 20, 20, tabIndex: -1);
        NodeHandle c = scene.Focusable(scene.Root, 60, 0, 20, 20);
        _ = scene.Place(scene.Root, 90, 0, 20, 20);
        NodeHandle d = scene.Focusable(scene.Root, 120, 0, 20, 20, tabIndex: 1);
        NodeHandle hidden = scene.Place(scene.Root, 150, 0, 20, 20);
        _ = scene.Focusable(hidden, 0, 0, 10, 10, tabIndex: -5);
        scene.Start();
        scene.Hide(hidden);
        return (scene, a, b, c, d);
    }

    // A window of 200 x 100 logical pixels whose root holds the boxes placed in it, with its frames and input.
    private sealed partial class Scene
    {
        public Scene()
        {
            Tree = new UiTree();
            Root = Tree.CreateNode(new LayoutStyle());
            Atlas = new GlyphAtlas(new NoGlyphs());
            Frames = new WindowFrames(Tree, Root, Target, Atlas, Scheduler, Gray);
            Input = new WindowInput(Tree, Frames, new NoPlatform());
        }

        public UiTree Tree { get; }

        public NodeHandle Root { get; }

        public GlyphAtlas Atlas { get; }

        public CountingTarget Target { get; } = new();

        public CountingScheduler Scheduler { get; } = new();

        public WindowFrames Frames { get; }

        public WindowInput Input { get; }

        // A box at (x, y) of the parent's box, of the size given.
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

        public NodeHandle Focusable(NodeHandle parent, float x, float y, float width, float height, int tabIndex = 0)
        {
            NodeHandle node = Place(parent, x, y, width, height);
            Tree.SetFocusable(node, true);
            Tree.SetTabIndex(node, tabIndex);
            return node;
        }

        // Lays out and draws the first time, which makes the root, and makes the window active.
        public void Start()
        {
            Frames.Resize(new Size(200, 100), 1);
            Input.SetActive(true);
        }

        // Runs the frame the tree asked for.
        public void Draw() => Scheduler.Run();

        public void Hide(NodeHandle node) => Tree.SetStyle(node, Tree.Nodes.GetRef(node).Layout.Style with { Hidden = true });

        public void Show(NodeHandle node) => Tree.SetStyle(node, Tree.Nodes.GetRef(node).Layout.Style with { Hidden = false });

        public bool Has(NodeHandle node, PseudoState state) => (Tree.Nodes.GetRef(node).Pseudo & state) == state;

        public bool Key(Key key, KeyModifiers modifiers = KeyModifiers.None, KeyEventKind kind = KeyEventKind.Down) =>
            Input.Handle(new KeyInput(kind, key, PhysicalKey.None, default, modifiers, false, TimeSpan.Zero));

        public bool Tab(bool shift = false) => Key(XylemUI.Key.Tab, shift ? KeyModifiers.Shift : KeyModifiers.None);

        public void Press(int x, int y, PointerButton button = PointerButton.Left) =>
            Send(PointerInputKind.Down, x, y, button, button == PointerButton.Left ? PointerButtons.Left : PointerButtons.Right);

        public void Release(int x, int y, PointerButton button = PointerButton.Left) => Send(PointerInputKind.Up, x, y, button, PointerButtons.None);

        // Logs each key-down in both phases as "<name> capture" and "<name> bubble".
        public void LogKeys(NodeHandle node, string name, List<string> log)
        {
            Tree.AddKeyHandler(node, KeyEventKind.Down, EventPhase.Capture, (in KeyEvent _) => log.Add($"{name} capture"));
            Tree.AddKeyHandler(node, KeyEventKind.Down, EventPhase.Bubble, (in KeyEvent _) => log.Add($"{name} bubble"));
        }

        // Logs the node's own focus-ins and focus-outs as "in <name>" and "out <name>".
        public void LogFocus(NodeHandle node, string name, List<string> log)
        {
            Tree.AddFocusHandler(node, FocusEventKind.FocusIn, EventPhase.Bubble, (in FocusEvent e) => Own(e, "in"));
            Tree.AddFocusHandler(node, FocusEventKind.FocusOut, EventPhase.Bubble, (in FocusEvent e) => Own(e, "out"));

            void Own(in FocusEvent e, string word)
            {
                if (e.TargetNode == node)
                {
                    log.Add($"{word} {name}");
                }
            }
        }

        // The color of the last rectangle filled at the box of the focus tests, (10, 10, 50 x 50).
        public Color BoxColor()
        {
            Color color = default;
            DisplayListReader reader = Frames.List.GetReader();
            while (reader.TryNext(out CommandTag tag))
            {
                switch (tag)
                {
                    case CommandTag.FillRect:
                        FillRectCommand fill = reader.ReadFillRect();
                        if (fill.X == 10 && fill.Y == 10)
                        {
                            color = fill.Color;
                        }

                        break;
                    case CommandTag.PushState:
                        _ = reader.ReadState(out _);
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

            return color;
        }

        private void Send(PointerInputKind kind, int x, int y, PointerButton button, PointerButtons buttons) =>
            Input.Handle(new PointerInput(kind, x, y, button, buttons, KeyModifiers.None, 1, TimeSpan.Zero, PointerType.Mouse));
    }

    // A window that captures nothing and shows no cursor.
    private sealed class NoPlatform : IPointerPlatform
    {
        public void Capture() { }

        public void ReleaseCapture() { }

        public void SetCursor(CursorKind cursor) { }
    }
}
