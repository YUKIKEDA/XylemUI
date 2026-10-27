using System.Numerics;

namespace XylemUI;

/// <summary>
/// Delivers the pointer's input of one window to the handlers of its tree (design §7, "Pointer input"): tests the hit regions of
/// the display list the window's frames built last, routes each event in the capture and the bubble phase, captures the pointer
/// while a button is down, makes clicks, keeps the hovered and pressed nodes, and chooses the cursor.
/// </summary>
/// <remarks>
/// It belongs to the UI thread, as the tree does. The platform calls <see cref="Handle"/> from its window procedure; the frames call
/// <see cref="Rehit"/> after a frame that built the display list. Each call runs as one batch, so effects run once after it. The
/// routes and the hovered nodes are arrays it reuses: the steady state allocates nothing.
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
    /// Stops the event being delivered (<see cref="PointerEvent.StopPropagation"/>).
    /// </summary>
    internal void StopPropagation() => _stopped = true;

    private void HandleInBatch(in PointerInput input)
    {
        switch (input.Kind)
        {
            case PointerInputKind.Move:
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
    private void Route(PointerEventKind kind, NodeHandle target, in PointerInput input, bool bubbleOnly)
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
                    Invoke(kind, EventPhase.Capture, target, _route[i], input);
                }
            }

            for (int i = start; i < end && !_stopped; i++)
            {
                Invoke(kind, EventPhase.Bubble, target, _route[i], input);

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
    private void Invoke(PointerEventKind kind, EventPhase? phase, NodeHandle target, NodeHandle node, in PointerInput input)
    {
        if (!_tree.Nodes.IsAlive(node) || _tree.Nodes.GetRef(node).Input is not { } nodeInput)
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
                nodeInput.HitBuild == _frames.Builds && nodeInput.HitBuild > 0
                    ? _frames.List.PointInRegion(nodeInput.HitEntry, physical) / scale
                    : new Vector2(float.NaN);
            var e = new PointerEvent(this, kind, target, node, physical / scale, local, input);

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

        delta = new Vector2(horizontal ? delta.X : 0, vertical ? delta.Y : 0);
        Size max = _tree.MaxScrollOffsetOf(node);
        Vector2 current = _tree.ScrollOffsetOf(node);
        Vector2 next = Vector2.Clamp(current + delta, Vector2.Zero, new Vector2(max.Width, max.Height));
        return next != current && _tree.ScrollStateFor(node).SetOffset(next);
    }

    // The node the last display list puts at the position; none when that node was removed since.
    private NodeHandle HitAt(in PointerInput input)
    {
        NodeHandle hit = _frames.List.HitTest(input.X, input.Y);
        return _tree.Nodes.IsAlive(hit) ? hit : default;
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
