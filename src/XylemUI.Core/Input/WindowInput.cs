using System.Numerics;
using System.Runtime.CompilerServices;

namespace XylemUI;

/// <summary>
/// Delivers the input of one window to the handlers of its tree (design §7, "Pointer input" and "Keys and the focus"): tests the hit
/// regions of the display list the window's frames built last, routes each event in the capture and the bubble phase, captures the
/// pointer while a button is down, makes clicks, keeps the hovered and pressed nodes, chooses the cursor, routes the keys from the
/// focused node, and hands the focus its changes (<see cref="UiTree.Focus"/>).
/// </summary>
/// <remarks>
/// It belongs to the UI thread, as the tree does. The platform calls <c>Handle</c> and <see cref="SetActive"/> from its window
/// procedure; the frames call <see cref="Rehit"/> after a frame that built the display list. Each call runs as one batch, so effects
/// run once after it. The routes and the hovered nodes are arrays it reuses: the steady state allocates nothing.
/// </remarks>
internal sealed class WindowInput
{
    private readonly UiTree _tree;
    private readonly WindowFrames _frames;
    private readonly IPointerPlatform _platform;

    // The nodes of the routes being delivered, innermost first; a route delivered from a handler goes after the one around it.
    private NodeHandle[] _route = new NodeHandle[16];
    private int _routeCount;

    // The hovered nodes, innermost first, and the ones the hover is being changed to.
    private NodeHandle[] _hovered = new NodeHandle[16];
    private int _hoveredCount;
    private NodeHandle[] _nextHovered = new NodeHandle[16];

    // The nodes with the pressed state, innermost first.
    private NodeHandle[] _pressed = new NodeHandle[16];
    private int _pressedCount;

    private NodeHandle _capture;
    private NodeHandle _clickOrigin;
    private NodeHandle _hit;
    private PointerInput _last;
    private bool _inside;
    private bool _stopped;

    // Whether enters and leaves are being sent, and whether a hover test came meanwhile and waits for them to end (PR #327's review).
    private bool _hovering;
    private bool _hoverPending;
    private CursorKind _cursor;

    /// <summary>
    /// Initializes a new instance of the <see cref="WindowInput"/> class, and has <paramref name="frames"/> call
    /// <see cref="Rehit"/> after each frame that builds the display list.
    /// </summary>
    /// <param name="tree">The window's tree.</param>
    /// <param name="frames">The window's frames, whose last display list the hit tests read.</param>
    /// <param name="platform">The window, which captures the pointer and shows the cursor.</param>
    public WindowInput(UiTree tree, WindowFrames frames, IPointerPlatform platform)
    {
        ArgumentNullException.ThrowIfNull(tree);
        ArgumentNullException.ThrowIfNull(frames);
        ArgumentNullException.ThrowIfNull(platform);
        _tree = tree;
        _frames = frames;
        _platform = platform;
        frames.Input = this;
    }

    /// <summary>
    /// Gets the window's tree.
    /// </summary>
    internal UiTree Tree => _tree;

    /// <summary>
    /// Gets the node that holds the capture of the pointer; none while no button is down.
    /// </summary>
    internal NodeHandle Capture => _capture;

    /// <summary>
    /// Gets the cursor chosen last.
    /// </summary>
    internal CursorKind Cursor => _cursor;

    /// <summary>
    /// Delivers one input of the pointer.
    /// </summary>
    /// <param name="input">The input, in the window's physical pixels.</param>
    public void Handle(in PointerInput input)
    {
        ReactiveRuntime.EnterBatch();
        try
        {
            HandleInBatch(input);
        }
        finally
        {
            ReactiveRuntime.ExitBatch();
        }
    }

    /// <summary>
    /// Delivers one input of the keyboard (design §7, "Keys and the focus"): routes it from the focused node, or from the root when
    /// the window is not active or no node has the focus, in the capture and the bubble phase; then, for a down no handler stopped,
    /// moves the focus on Tab (no modifier) and Shift+Tab.
    /// </summary>
    /// <param name="input">The input.</param>
    /// <returns>Whether the key was handled: a handler stopped it, or Tab moved the focus. The platform passes the others to the OS.</returns>
    public bool Handle(in KeyInput input)
    {
        ReactiveRuntime.EnterBatch();
        try
        {
            return HandleKeyInBatch(input);
        }
        finally
        {
            ReactiveRuntime.ExitBatch();
        }
    }

    /// <summary>
    /// Sets whether the window is active (design §7, "The window's activation"); the platform calls it from its activation message.
    /// </summary>
    /// <param name="active">Whether the window is active.</param>
    public void SetActive(bool active) => _tree.Focus.SetActive(active);

    /// <summary>
    /// Tests the pointer's last position again against the display list just built, when the pointer is in the window: the nodes
    /// under a pointer that did not move may have changed. Sends the enters and leaves and updates the hovered state and the cursor;
    /// sends no move.
    /// </summary>
    internal void Rehit()
    {
        if (!_inside)
        {
            return;
        }

        ReactiveRuntime.EnterBatch();
        try
        {
            UpdateHover(_last);
        }
        finally
        {
            ReactiveRuntime.ExitBatch();
        }
    }

    /// <summary>
    /// Routes the click a key makes on a node (design §7, "The button's keys"): a <see cref="PointerEventKind.Click"/> in the bubble
    /// phase from the node, which the pointer's click handlers take as theirs. It has a <see cref="PointerEvent.ClickCount"/> of 0,
    /// which tells it from the pointer's, no position (<see cref="float.NaN"/>), no button and no modifier, and the mouse as its type.
    /// </summary>
    /// <param name="node">The node.</param>
    /// <param name="timestamp">When the key's input was received.</param>
    internal void ClickFromKey(NodeHandle node, TimeSpan timestamp)
    {
        var input = new PointerInput(
            PointerInputKind.Up,
            0,
            0,
            PointerButton.None,
            PointerButtons.None,
            KeyModifiers.None,
            0,
            timestamp,
            PointerType.Mouse
        );
        Route(PointerEventKind.Click, node, input, bubbleOnly: true, fromKey: true);
    }

    /// <summary>
    /// Stops the event being delivered (<see cref="PointerEvent.StopPropagation"/>).
    /// </summary>
    internal void StopPropagation() => _stopped = true;

    private bool HandleKeyInBatch(in KeyInput input)
    {
        FocusManager focus = _tree.Focus;
        if (input.Kind == KeyEventKind.Down)
        {
            focus.SetLastInputWasKeyboard(true);
        }

        if (RouteKey(focus.KeyTarget, input))
        {
            return true;
        }

        // Tab is the root's default action, after the bubble phase (Avalonia's KeyboardNavigationHandler); a Tab with Control, Alt, or
        // Meta does not move the focus.
        if (input.Kind == KeyEventKind.Down && input.Key == Key.Tab && (input.Modifiers & ~KeyModifiers.Shift) == 0)
        {
            return focus.MoveInTabOrder(backward: input.Modifiers == KeyModifiers.Shift);
        }

        return false;
    }

    private void HandleInBatch(in PointerInput input)
    {
        switch (input.Kind)
        {
            case PointerInputKind.Move:
                _tree.Focus.SetLastInputWasKeyboard(false);
                Point(input);
                Route(PointerEventKind.Move, Target(), input, bubbleOnly: false);
                break;
            case PointerInputKind.Down:
                Down(input);
                break;
            case PointerInputKind.Up:
                Up(input);
                break;
            case PointerInputKind.Leave:
                // While a button is down the window keeps the pointer's moves, which decide the hover outside it.
                if (CapturedNode().IsNone)
                {
                    // Through UpdateHover, so that a leave that comes while enters and leaves are sent waits for them too (a
                    // handler that pumps messages, Issue #329).
                    _inside = false;
                    _hit = default;
                    UpdateHover(input);
                }

                break;
            case PointerInputKind.CaptureLost:
                CaptureLost(input);
                break;
            case PointerInputKind.Wheel:
                // From the node under the pointer even while a button is captured (Flutter, GPUI; design §3.4.1).
                Point(input);
                Route(PointerEventKind.Wheel, _hit, input, bubbleOnly: false);
                break;
            default:
                break;
        }
    }

    // A new position: the hit node, the hover, and the cursor.
    private void Point(in PointerInput input)
    {
        _inside = true;
        _last = input;
        UpdateHover(input);
    }

    private void Down(in PointerInput input)
    {
        _tree.Focus.SetLastInputWasKeyboard(false);
        Point(input);
        NodeHandle target = Target();
        if (CapturedNode().IsNone && !target.IsNone)
        {
            _capture = target;
            _platform.Capture();
        }

        if (input.Button == PointerButton.Left && !target.IsNone)
        {
            _clickOrigin = target;
            SetPressed(target);

            // Before the route, from the node the press starts at (design §7, "Focus on a press", Avalonia's FocusManager).
            _tree.Focus.FocusFromPress(target);
        }

        Route(PointerEventKind.Down, target, input, bubbleOnly: false);
    }

    private void Up(in PointerInput input)
    {
        Point(input);
        Route(PointerEventKind.Up, Target(), input, bubbleOnly: false);
        if (input.Button == PointerButton.Left)
        {
            ClearPressed();
            NodeHandle origin = _clickOrigin;
            _clickOrigin = default;

            // A click when the release is over the node pressed or one of its descendants (Avalonia's Button, GPUI's div). The node
            // hit is the one the release's hover test found; a handler of the up may have removed it since (PR #327's review).
            NodeHandle released = _tree.Nodes.IsAlive(_hit) ? _hit : default;
            if (_tree.Nodes.IsAlive(origin) && IsSelfOrDescendant(released, origin))
            {
                Route(PointerEventKind.Click, origin, input, bubbleOnly: true);
            }
        }

        if (input.Buttons == PointerButtons.None && !_capture.IsNone)
        {
            _capture = default;
            _platform.ReleaseCapture();

            // The hover followed the capture until now.
            UpdateHover(input);
        }
    }

    private void CaptureLost(in PointerInput input)
    {
        NodeHandle captured = CapturedNode();
        _capture = default;
        _clickOrigin = default;
        ClearPressed();
        if (!captured.IsNone)
        {
            Route(PointerEventKind.Cancel, captured, input, bubbleOnly: false);
        }

        if (_inside)
        {
            UpdateHover(_last);
        }
    }

    // The node a routed event starts from: the capture's while there is one, the node hit otherwise.
    private NodeHandle Target() => CapturedNode() is { IsNone: false } captured ? captured : _hit;

    // The capture's node, or none; a capture whose node was removed is released.
    private NodeHandle CapturedNode()
    {
        if (!_capture.IsNone && !_tree.Nodes.IsAlive(_capture))
        {
            _capture = default;
            _platform.ReleaseCapture();
        }

        return _capture;
    }

    // Tests the position, and changes the hover to the node hit and its ancestors; while captured, to the capture's node and its
    // ancestors when the node hit is it or a descendant, and to nothing otherwise (Avalonia's PointerOverPreProcessor).
    // A test that comes while enters and leaves are sent (a handler that runs a frame synchronously, whose rehit lands here, or that
    // pumps messages) waits until they end, and then runs at the pointer's last position: the chains it would change are being read.
    private void UpdateHover(in PointerInput input)
    {
        if (_hovering)
        {
            _hoverPending = true;
            return;
        }

        TestHover(input);

        // A handler that keeps changing the tree under the pointer is not followed past a few tests.
        for (int again = 0; _hoverPending && again < 4; again++)
        {
            _hoverPending = false;
            TestHover(_last);
        }

        _hoverPending = false;
    }

    // The hover at the position, or none when the pointer left the window.
    private void TestHover(in PointerInput input)
    {
        if (!_inside)
        {
            _hit = default;
            SetHover(0, input);
            return;
        }

        _hit = HitAt(input);
        NodeHandle captured = CapturedNode();
        NodeHandle start =
            captured.IsNone ? _hit
            : IsSelfOrDescendant(_hit, captured) ? captured
            : default;
        int count = 0;
        for (NodeHandle node = start; !node.IsNone; node = _tree.Nodes.GetRef(node).Parent)
        {
            if (count == _nextHovered.Length)
            {
                Array.Resize(ref _nextHovered, count * 2);
            }

            _nextHovered[count++] = node;
        }

        SetHover(count, input);
    }

    // Changes the hover to the first count nodes of _nextHovered: a leave for each node that left, innermost first, then an enter
    // for each that entered, outermost first (Flutter's MouseTracker). Both chains end at the root, so the nodes they share are
    // their common tail.
    private void SetHover(int count, in PointerInput input)
    {
        int shared = 0;
        while (shared < _hoveredCount && shared < count && _hovered[_hoveredCount - 1 - shared] == _nextHovered[count - 1 - shared])
        {
            shared++;
        }

        int left = _hoveredCount - shared;
        int entered = count - shared;
        if (left > 0 || entered > 0)
        {
            _hovering = true;
            try
            {
                SendHoverChange(left, entered, count, input);
            }
            finally
            {
                _hovering = false;
            }
        }

        UpdateCursor();
    }

    // The leaves of the nodes left, then the enters of the nodes entered, with the chain changed between them.
    private void SendHoverChange(int left, int entered, int count, in PointerInput input)
    {
        for (int i = 0; i < left; i++)
        {
            NodeHandle node = _hovered[i];
            if (_tree.Nodes.IsAlive(node))
            {
                _tree.SetPseudoState(node, PseudoState.Hovered, on: false);
                Direct(PointerEventKind.Leave, node, input);
            }
        }

        if (_hovered.Length < count)
        {
            Array.Resize(ref _hovered, _nextHovered.Length);
        }

        Array.Copy(_nextHovered, _hovered, count);
        _hoveredCount = count;
        for (int i = entered - 1; i >= 0; i--)
        {
            NodeHandle node = _hovered[i];
            if (_tree.Nodes.IsAlive(node))
            {
                _tree.SetPseudoState(node, PseudoState.Hovered, on: true);
                Direct(PointerEventKind.Enter, node, input);
            }
        }
    }

    // The pressed state on the node and its ancestors, kept to clear them on the release.
    private void SetPressed(NodeHandle target)
    {
        ClearPressed();
        for (NodeHandle node = target; !node.IsNone; node = _tree.Nodes.GetRef(node).Parent)
        {
            if (_pressedCount == _pressed.Length)
            {
                Array.Resize(ref _pressed, _pressedCount * 2);
            }

            _pressed[_pressedCount++] = node;
            _tree.SetPseudoState(node, PseudoState.Pressed, on: true);
        }
    }

    private void ClearPressed()
    {
        for (int i = 0; i < _pressedCount; i++)
        {
            if (_tree.Nodes.IsAlive(_pressed[i]))
            {
                _tree.SetPseudoState(_pressed[i], PseudoState.Pressed, on: false);
            }
        }

        _pressedCount = 0;
    }

    // The cursor of the first node from the capture's node, or the node hit, up to the root that sets one; the arrow when none does.
    private void UpdateCursor()
    {
        CursorKind cursor = CursorKind.Arrow;
        for (NodeHandle node = Target(); !node.IsNone; node = _tree.Nodes.GetRef(node).Parent)
        {
            if (_tree.Nodes.GetRef(node).Input?.Cursor is { } set)
            {
                cursor = set;
                break;
            }
        }

        if (cursor != _cursor)
        {
            _cursor = cursor;
            _platform.SetCursor(cursor);
        }
    }

    // Delivers a routed event from target: the capture phase from the root down, then the bubble phase back up, until a handler
    // stops it. The route is made before the first handler runs.
    private void Route(PointerEventKind kind, NodeHandle target, in PointerInput input, bool bubbleOnly, bool fromKey = false)
    {
        if (target.IsNone || !_tree.Nodes.IsAlive(target))
        {
            return;
        }

        int start = _routeCount;
        bool stopped = _stopped;
        _stopped = false;
        try
        {
            for (NodeHandle node = target; !node.IsNone; node = _tree.Nodes.GetRef(node).Parent)
            {
                if (_routeCount == _route.Length)
                {
                    Array.Resize(ref _route, _routeCount * 2);
                }

                _route[_routeCount++] = node;
            }

            int end = _routeCount;
            if (!bubbleOnly)
            {
                for (int i = end - 1; i >= start && !_stopped; i--)
                {
                    Invoke(kind, EventPhase.Capture, target, _route[i], input, fromKey);
                }
            }

            for (int i = start; i < end && !_stopped; i++)
            {
                Invoke(kind, EventPhase.Bubble, target, _route[i], input, fromKey);

                // After the node's own handlers, which can stop the wheel before it scrolls anything.
                if (kind == PointerEventKind.Wheel && !_stopped && Scroll(_route[i], input))
                {
                    _stopped = true;
                }
            }
        }
        finally
        {
            _routeCount = start;
            _stopped = stopped;
        }
    }

    // Delivers a key from target as Route delivers the pointer's events, on the same array of routes. Returns whether a handler
    // stopped it.
    private bool RouteKey(NodeHandle target, in KeyInput input)
    {
        if (target.IsNone || !_tree.Nodes.IsAlive(target))
        {
            return false;
        }

        int start = _routeCount;
        bool stopped = _stopped;
        _stopped = false;
        try
        {
            for (NodeHandle node = target; !node.IsNone; node = _tree.Nodes.GetRef(node).Parent)
            {
                if (_routeCount == _route.Length)
                {
                    Array.Resize(ref _route, _routeCount * 2);
                }

                _route[_routeCount++] = node;
            }

            int end = _routeCount;

            // Only a down of a scrolling key with no modifier scrolls: decided once, so the other keys pay nothing for it per node.
            bool scrolls = input.Kind == KeyEventKind.Down && input.Modifiers == KeyModifiers.None && IsScrollKey(input.Key);
            for (int i = end - 1; i >= start && !_stopped; i--)
            {
                InvokeKey(EventPhase.Capture, target, _route[i], input);
            }

            for (int i = start; i < end && !_stopped; i++)
            {
                InvokeKey(EventPhase.Bubble, target, _route[i], input);

                // After the node's own handlers, as the wheel: a part that takes the arrows stops them first (design §7).
                if (scrolls && !_stopped && ScrollByKey(_route[i], input))
                {
                    _stopped = true;
                }
            }

            return _stopped;
        }
        finally
        {
            _routeCount = start;
            _stopped = stopped;
        }
    }

    // Calls the key handlers of one node for the input's kind and the phase, as Invoke calls the pointer's.
    private void InvokeKey(EventPhase phase, NodeHandle target, NodeHandle node, in KeyInput input)
    {
        if (EnabledInput(node) is not { } nodeInput)
        {
            return;
        }

        foreach (KeyHandlerEntry entry in nodeInput.KeyHandlers)
        {
            if (entry.Kind != input.Kind || entry.Phase != phase)
            {
                continue;
            }

            ScopeRef previous = default;
            if (!entry.Scope.IsNone && !Scope.TryEnter(entry.Scope, out previous))
            {
                continue;
            }

            var e = new KeyEvent(this, target, node, input);
            try
            {
                entry.Handler(in e);
            }
            catch (Exception exception) when (exception is not InvariantViolationException)
            {
                ReactiveRuntime.ReportException(exception);
            }
            finally
            {
                if (!entry.Scope.IsNone)
                {
                    Scope.Exit(previous);
                }
            }

            if (_stopped)
            {
                return;
            }
        }
    }

    // Delivers an enter or a leave to the node's own handlers, of either phase.
    private void Direct(PointerEventKind kind, NodeHandle node, in PointerInput input)
    {
        bool stopped = _stopped;
        _stopped = false;
        try
        {
            Invoke(kind, null, node, node, input);
        }
        finally
        {
            _stopped = stopped;
        }
    }

    // Calls the handlers of one node for the kind and phase. A handler's exception goes to the handler of unhandled exceptions,
    // and the next handler is called (design §7, as the effects of §2.2).
    private void Invoke(PointerEventKind kind, EventPhase? phase, NodeHandle target, NodeHandle node, in PointerInput input, bool fromKey = false)
    {
        if (EnabledInput(node) is not { } nodeInput)
        {
            return;
        }

        HandlerEntry[] handlers = nodeInput.Handlers;
        foreach (HandlerEntry entry in handlers)
        {
            if (entry.Kind != kind || (phase is { } p && entry.Phase != p))
            {
                continue;
            }

            float scale = _frames.Scale;
            var physical = new Vector2(input.X, input.Y);
            Vector2 local =
                fromKey ? new Vector2(float.NaN)
                : nodeInput.HitBuild == _frames.Builds && nodeInput.HitBuild > 0 ? _frames.List.PointInRegion(nodeInput.HitEntry, physical) / scale
                : new Vector2(float.NaN);
            var e = new PointerEvent(this, kind, target, node, fromKey ? new Vector2(float.NaN) : physical / scale, local, input);

            // In the scope the handler was added in, entered with no closure (design §2.13); a scope disposed since calls nothing.
            ScopeRef previous = default;
            if (!entry.Scope.IsNone && !Scope.TryEnter(entry.Scope, out previous))
            {
                continue;
            }

            try
            {
                entry.Handler(in e);
            }
            catch (Exception exception) when (exception is not InvariantViolationException)
            {
                ReactiveRuntime.ReportException(exception);
            }
            finally
            {
                if (!entry.Scope.IsNone)
                {
                    Scope.Exit(previous);
                }
            }

            if (_stopped)
            {
                return;
            }
        }
    }

    // A wheel's scrolling of a scroll container on the route (design §3.4.1): the axes whose overflow is Scroll take the amount, a
    // mouse's vertical wheel with Shift held going across (Flutter's and Avalonia's scrollers); the position is kept in the range
    // the last layout gave. Returns whether the position changed: then the wheel stops here; otherwise it goes on to the scroll
    // containers outside (scroll chaining). A container without a state gets one the first time it scrolls.
    private bool Scroll(NodeHandle node, in PointerInput input)
    {
        if (!_tree.Nodes.IsAlive(node))
        {
            return false;
        }

        ref readonly LayoutStyle style = ref _tree.Nodes.GetRef(node).Layout.Style;
        bool horizontal = style.OverflowX == Overflow.Scroll;
        bool vertical = style.OverflowY == Overflow.Scroll;
        if (!horizontal && !vertical)
        {
            return false;
        }

        Vector2 delta = input.WheelDelta;
        if (input.PointerType == PointerType.Mouse && (input.Modifiers & KeyModifiers.Shift) != 0 && delta.X == 0)
        {
            delta = new Vector2(delta.Y, 0);
        }

        return ScrollBy(node, new Vector2(horizontal ? delta.X : 0, vertical ? delta.Y : 0));
    }

    // A key's scrolling of a scroll container on the route (design §7, "Keyboard scrolling"): with no modifier, the arrows by 50 on
    // the axes that scroll, Page Up and Page Down by 0.8 of the viewport's height, and Home and End to the vertical ends, or to the
    // horizontal ones when only the width scrolls. No animation. Returns whether the position changed; otherwise the key goes on to
    // the containers outside, as the wheel does.
    private bool ScrollByKey(NodeHandle node, in KeyInput input)
    {
        const float Line = 50;
        const float Page = 0.8f;
        if (!_tree.Nodes.IsAlive(node))
        {
            return false;
        }

        ref UiNode n = ref _tree.Nodes.GetRef(node);
        if ((n.Pseudo & PseudoState.Disabled) != 0)
        {
            return false;
        }

        ref readonly LayoutStyle style = ref n.Layout.Style;
        bool horizontal = style.OverflowX == Overflow.Scroll;
        bool vertical = style.OverflowY == Overflow.Scroll;
        if (!horizontal && !vertical)
        {
            return false;
        }

        float end = float.PositiveInfinity;
        Vector2 delta = input.Key switch
        {
            Key.Up when vertical => new Vector2(0, -Line),
            Key.Down when vertical => new Vector2(0, Line),
            Key.Left when horizontal => new Vector2(-Line, 0),
            Key.Right when horizontal => new Vector2(Line, 0),
            Key.PageUp when vertical => new Vector2(0, -Page * _tree.ViewportOf(node).Height),
            Key.PageDown when vertical => new Vector2(0, Page * _tree.ViewportOf(node).Height),
            Key.Home => vertical ? new Vector2(0, -end) : new Vector2(-end, 0),
            Key.End => vertical ? new Vector2(0, end) : new Vector2(end, 0),
            _ => default,
        };
        return delta != default && ScrollBy(node, delta);
    }

    private static bool IsScrollKey(Key key) =>
        key is Key.Up or Key.Down or Key.Left or Key.Right or Key.PageUp or Key.PageDown or Key.Home or Key.End;

    // Moves a container's position by delta, kept in the range the last layout gave; a container without a state gets one the
    // first time it scrolls. Returns whether the position changed. The state is looked up once: looking it up for the position,
    // the range, and the write each made a wheel 0.02 µs longer than main's (PR for Issue #359).
    private bool ScrollBy(NodeHandle node, Vector2 delta)
    {
        ScrollState? state = _tree.ScrollStateOf(node);
        Vector2 current = state?.OffsetValue ?? Vector2.Zero;
        Vector2 max = state?.MaxOffsetValue ?? _tree.ScrollRangeOf(node);
        Vector2 next = Vector2.Clamp(current + delta, Vector2.Zero, max);
        return next != current && (state ?? _tree.ScrollStateFor(node)).SetOffset(next);
    }

    // The node the last display list puts at the position, or its nearest ancestor that is not disabled: a disabled node still takes
    // the pointer from the nodes behind it, and its enabled ancestor gets the route, the hover, the press, and the cursor (design §7,
    // "Disabled", as Avalonia's hit test skips disabled elements). None when that node was removed since.
    private NodeHandle HitAt(in PointerInput input)
    {
        NodeHandle hit = _frames.List.HitTest(input.X, input.Y);
        if (!_tree.Nodes.IsAlive(hit))
        {
            return default;
        }

        return (_tree.Nodes.GetRef(hit).Pseudo & PseudoState.Disabled) == 0 ? hit : EnabledAncestor(hit);
    }

    // The nearest ancestor of a disabled node that is not disabled, apart from HitAt, which every move calls.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private NodeHandle EnabledAncestor(NodeHandle node)
    {
        while (!node.IsNone && (_tree.Nodes.GetRef(node).Pseudo & PseudoState.Disabled) != 0)
        {
            node = _tree.Nodes.GetRef(node).Parent;
        }

        return node;
    }

    // Whether a node is alive and not disabled: a disabled node's handlers and its descendants' are not called (design §7).
    // The input part of a node that is alive and not disabled, with one read of the node; null otherwise, and for a node with none.
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private NodeInput? EnabledInput(NodeHandle node)
    {
        if (!_tree.Nodes.IsAlive(node))
        {
            return null;
        }

        ref UiNode n = ref _tree.Nodes.GetRef(node);
        return (n.Pseudo & PseudoState.Disabled) == 0 ? n.Input : null;
    }

    // Whether node is ancestor itself or one of its descendants.
    private bool IsSelfOrDescendant(NodeHandle node, NodeHandle ancestor)
    {
        for (; !node.IsNone; node = _tree.Nodes.GetRef(node).Parent)
        {
            if (node == ancestor)
            {
                return true;
            }
        }

        return false;
    }
}
