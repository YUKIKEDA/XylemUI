using System.Numerics;
using System.Runtime.CompilerServices;

namespace XylemUI;

/// <summary>
/// Delivers the input of one window to the handlers of its tree (design §7, "Pointer input", "Touch and pen", and "Keys and the
/// focus"): tests the hit regions of the display list the window's frames built last, routes each event in the capture and the
/// bubble phase, captures each pointer while it presses, makes clicks, keeps the hovered and pressed nodes of each pointer, chooses
/// the cursor, routes the keys from the focused node, and hands the focus its changes (<see cref="UiTree.Focus"/>).
/// </summary>
/// <remarks>
/// It belongs to the UI thread, as the tree does. The platform calls <c>Handle</c> and <see cref="SetActive"/> from its window
/// procedure; the frames call <see cref="Rehit"/> after a frame that built the display list. Each call runs as one batch, so effects
/// run once after it. The routes, the pointers' states, and the hovered nodes are arrays it reuses: the steady state allocates
/// nothing.
/// </remarks>
internal sealed partial class WindowInput
{
    // A touch's tap stays a click while it moves no more than this from its press, in logical pixels (Flutter's kTouchSlop,
    // gestures/constants.dart line 65; design §7, "Touch and pen").
    private const float TouchSlop = 18;

    // The most coalesced points a message's history gives (Avalonia's MaxPointerHistorySize, WindowImpl.cs line 113).
    private const int MaxHistory = 512;

    private readonly UiTree _tree;
    private readonly WindowFrames _frames;
    private readonly IPointerPlatform _platform;

    // The nodes of the routes being delivered, innermost first; a route delivered from a handler goes after the one around it.
    private NodeHandle[] _route = new NodeHandle[16];
    private int _routeCount;

    // The nodes a pointer's hover is being changed to, innermost first.
    private NodeHandle[] _nextHovered = new NodeHandle[16];

    // The state of each pointer, by its number: the mouse is 0, touches and pens take 1 and up from the platform (design §7, "Touch
    // and pen"). A slot is kept when its pointer leaves, and taken again by the next pointer of that number.
    private PointerState[] _pointers = [new PointerState()];

    // The pointers whose state is live: 1 while the mouse is the only one.
    private int _livePointers;

    // The serial of the pointer message being delivered, 0 when none; the intermediate points of its events are read only while
    // it lasts, as Win32 keeps the history of the thread's last pointer message only.
    private int _dispatch;
    private int _nextDispatch;
    private int _pointsDispatch;
    private int _pointsCount;
    private PointerHistoryPoint[] _history = new PointerHistoryPoint[16];
    private PointerPoint[] _points = new PointerPoint[16];
    private bool _stopped;

    // Whether Alt is down with no other key or press since it went down: its up then selects the menu bar (Avalonia's
    // _ignoreAltUp, AccessKeyHandler.cs lines 216 to 218 and 256 to 258).
    private bool _altAlone;

    // Whether enters and leaves are being sent, and whether a hover test came meanwhile and waits for them to end (PR #327's review).
    private bool _hovering;
    private bool _hoverPending;
    private CursorKind _cursor;

    // The pointer whose position chooses the cursor: the mouse, or the pen that moved last.
    private int _cursorPointer;

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
        tree.Popups.ModalOpened = ReleaseCapturesBehind;
    }

    /// <summary>
    /// Gets the window's tree.
    /// </summary>
    internal UiTree Tree => _tree;

    /// <summary>
    /// Gets the frames whose display list the input hit-tests (a selection area finds its leaves' points in it, design §4.10).
    /// </summary>
    internal WindowFrames Frames => _frames;

    /// <summary>
    /// Gets the node that holds the capture of the mouse; none while no button is down.
    /// </summary>
    internal NodeHandle Capture => _pointers[0].Capture;

    /// <summary>
    /// Gets the cursor chosen last.
    /// </summary>
    internal CursorKind Cursor => _cursor;

    /// <summary>
    /// Delivers one input of a pointer.
    /// </summary>
    /// <param name="input">The input, in the window's physical pixels.</param>
    public void Handle(in PointerInput input)
    {
        ReactiveRuntime.EnterBatch();
        _dispatch = ++_nextDispatch;
        try
        {
            HandleInBatch(input);
        }
        finally
        {
            // A message pumped by a handler ends the delivery around it too: Win32 then holds that message's history.
            _dispatch = 0;
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
    /// Delivers text typed into the window (design §4.9, "Text input on Windows"): routes it from the focused node, or from the root
    /// when the window is not active or no node has the focus, in the capture and the bubble phase, as a key.
    /// </summary>
    /// <param name="input">The input.</param>
    /// <returns>Whether a handler stopped the text.</returns>
    public bool Handle(in TextInput input)
    {
        ReactiveRuntime.EnterBatch();
        try
        {
            return RouteText(_tree.Focus.KeyTarget, input);
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
    public void SetActive(bool active)
    {
        // A drag ends with the window's activation (design §7, "Drag and drop").
        if (!active)
        {
            CancelDrag(pointerId: -1);
        }

        // A popup that is not modal closes when the window stops being active (design §7.3, "Closing").
        if (!active && _tree.Popups.Count > 0)
        {
            ReactiveRuntime.EnterBatch();
            try
            {
                _tree.Popups.CloseNonModal();
            }
            finally
            {
                ReactiveRuntime.ExitBatch();
            }
        }

        _tree.Focus.SetActive(active);
    }

    /// <summary>
    /// Tests each hovering pointer's last position again against the display list just built, when the pointer is in the window:
    /// the nodes under a pointer that did not move may have changed. Sends the enters and leaves and updates the hovered state and
    /// the cursor; sends no move.
    /// </summary>
    internal void Rehit()
    {
        ReactiveRuntime.EnterBatch();
        try
        {
            for (int i = 0; i < _pointers.Length; i++)
            {
                PointerState pointer = _pointers[i];
                if (pointer.Live && pointer.Inside && pointer.Hovers)
                {
                    UpdateHover(pointer, pointer.Last);
                }
            }
        }
        finally
        {
            ReactiveRuntime.ExitBatch();
        }
    }

    /// <summary>
    /// Routes the click a key makes on a node (design §7, "The button's keys"): a <see cref="PointerEventKind.Click"/> in the bubble
    /// phase from the node, which the pointer's click handlers take as theirs. It has a <see cref="PointerEvent.ClickCount"/> of 0,
    /// which tells it from the pointer's, no position (<see cref="float.NaN"/>), no button and no modifier, the mouse as its type,
    /// pointer 0, primary, and no intermediate point.
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

    /// <summary>
    /// Returns the intermediate points of an event (design §7, "Touch and pen"): the points its message brought, oldest first, the
    /// last being the event's own. They are read from the platform the first time an event of the message asks, into an array this
    /// reuses. An event that no pointer message made (a key's click, a rehit's enter or leave) has none.
    /// </summary>
    /// <param name="e">The event.</param>
    /// <returns>The points, valid until the delivery ends.</returns>
    /// <exception cref="InvalidOperationException">The delivery of the event's message has ended.</exception>
    internal ReadOnlySpan<PointerPoint> IntermediatePoints(scoped in PointerEvent e)
    {
        if (e.Dispatch == 0)
        {
            return default;
        }

        if (e.Dispatch != _dispatch)
        {
            throw new InvalidOperationException("The intermediate points of a pointer event are read while the event is delivered, in its handler.");
        }

        if (_pointsDispatch != _dispatch)
        {
            _pointsDispatch = _dispatch;
            _pointsCount = ReadPoints(e);
        }

        return _points.AsSpan(0, _pointsCount);
    }

    private int ReadPoints(in PointerEvent e)
    {
        var own = new PointerPoint(e.Position, e.Pressure, e.TiltX, e.TiltY, e.Twist, e.Timestamp);
        int wanted = Math.Min(e.HistoryCount, MaxHistory);
        if (e.PointerType == PointerType.Mouse || wanted <= 1)
        {
            _points[0] = own;
            return 1;
        }

        if (_history.Length < wanted)
        {
            Array.Resize(ref _history, Math.Max(wanted, _history.Length * 2));
            Array.Resize(ref _points, _history.Length);
        }

        // Win32 gives the history newest first; the points go oldest first.
        int read = Math.Clamp(_platform.ReadHistory(e.PointerId, _history.AsSpan(0, wanted)), 0, wanted);
        if (read == 0)
        {
            _points[0] = own;
            return 1;
        }

        float scale = _frames.Scale;
        for (int i = 0; i < read; i++)
        {
            PointerHistoryPoint h = _history[read - 1 - i];
            _points[i] = new PointerPoint(new Vector2(h.X, h.Y) / scale, h.Pressure, h.TiltX, h.TiltY, h.Twist, h.Timestamp);
        }

        return read;
    }

    private bool HandleKeyInBatch(in KeyInput input)
    {
        FocusManager focus = _tree.Focus;

        // While a drag runs, Esc cancels it and a change of the modifiers chooses its effect again (design §7, "Drag and drop").
        if (Dragging && DragKey(input))
        {
            return true;
        }

        if (input.Kind == KeyEventKind.Down)
        {
            focus.SetLastInputWasKeyboard(true);

            // Alt shows the access keys (design §13.1, "Access keys"), as Windows' menus do.
            if (input.Key is Key.LeftAlt or Key.RightAlt)
            {
                _tree.ShowAccessKeyCues();
                if (!input.IsRepeat)
                {
                    _altAlone = (input.Modifiers & ~KeyModifiers.Alt) == 0;
                }
            }
            else
            {
                _altAlone = false;
            }
        }

        bool altUpAlone = false;
        if (input.Kind == KeyEventKind.Up && input.Key is Key.LeftAlt or Key.RightAlt)
        {
            altUpAlone = _altAlone;
            _altAlone = false;
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

        // Esc closes the front popup, after no handler stopped it (design §7.3, "Closing").
        if (input.Kind == KeyEventKind.Down && input.Key == Key.Escape && input.Modifiers == KeyModifiers.None && _tree.Popups.Count > 0)
        {
            return _tree.Popups.CloseFront();
        }

        // Alt and a letter or a digit is the window's access key, after no handler stopped it (design §13.1, "The window's access
        // keys"); Control and Alt together (AltGr) are not.
        if (
            input.Kind == KeyEventKind.Down
            && (input.Modifiers & (KeyModifiers.Alt | KeyModifiers.Control)) == KeyModifiers.Alt
            && _tree.AccessKeys is { } accessKeys
            && input.KeyChar.IsBmp
            && System.Text.Rune.IsLetterOrDigit(input.KeyChar)
        )
        {
            return accessKeys(this, (char)input.KeyChar.Value, input.Timestamp);
        }

        // Alt alone and F10 select the menu bar or leave it, after no handler stopped them (design §13.1, "The menu bar"). A down of
        // Alt that leaves it does not select it again on its up (Avalonia's AccessKeyHandler.cs lines 203 to 214).
        if (_tree.MenuBarKeys is { } menuBar)
        {
            if (input.Kind == KeyEventKind.Down && input.Key is Key.LeftAlt or Key.RightAlt)
            {
                if (!input.IsRepeat && menuBar(this, MenuBarKey.AltDown))
                {
                    _altAlone = false;
                    return true;
                }
            }
            else if (input.Kind == KeyEventKind.Down && input.Key == Key.F10 && input.Modifiers == KeyModifiers.None)
            {
                return menuBar(this, MenuBarKey.F10);
            }
            else if (altUpAlone)
            {
                return menuBar(this, MenuBarKey.AltUp);
            }
        }

        return false;
    }

    private void HandleInBatch(in PointerInput input)
    {
        PointerState pointer = PointerOf(input);
        switch (input.Kind)
        {
            case PointerInputKind.Move:
                _tree.Focus.SetLastInputWasKeyboard(false);
                Move(pointer, input);
                break;
            case PointerInputKind.Down:
                Down(pointer, input);
                break;
            case PointerInputKind.Up:
                Up(pointer, input);
                break;
            case PointerInputKind.Leave:
                Leave(pointer, input);
                break;
            case PointerInputKind.CaptureLost:
                CaptureLost(pointer, input);
                break;
            case PointerInputKind.Wheel:
                // From the node under the pointer even while a button is captured (Flutter, GPUI; design §3.4.1).
                Point(pointer, input);
                Route(PointerEventKind.Wheel, pointer.Hit, input, bubbleOnly: false);
                break;
            case PointerInputKind.PanStart:
                PanStart(pointer, input);
                break;
            case PointerInputKind.PanUpdate:
                PanUpdate(pointer, input);
                break;
            case PointerInputKind.PanEnd:
                PanEnd(pointer, input);
                break;
            default:
                break;
        }
    }

    // The state of the input's pointer, made the first time its number comes; a number taken again starts with its slot cleared.
    private PointerState PointerOf(in PointerInput input)
    {
        int id = input.PointerId;
        if (id >= _pointers.Length)
        {
            int old = _pointers.Length;
            Array.Resize(ref _pointers, Math.Max(id + 1, old * 2));
            for (int i = old; i < _pointers.Length; i++)
            {
                _pointers[i] = new PointerState();
            }
        }

        PointerState pointer = _pointers[id];
        if (!pointer.Live)
        {
            pointer.Live = true;
            _livePointers++;
            pointer.Type = input.PointerType;
            pointer.Inside = false;
            pointer.Capture = default;
            pointer.ClickOrigin = default;
            pointer.Hit = default;
            pointer.HoveredCount = 0;
            pointer.PressedCount = 0;
            pointer.Pressing = false;
            pointer.PastSlop = false;
            pointer.Drag = null;
            pointer.LinkLeaf = default;
            pointer.Link = -1;
            pointer.LinkLeave = null;
            pointer.PressedLinkLeaf = default;
            pointer.PressedLink = -1;
        }

        return pointer;
    }

    // A new position: the hit node, the hover, and the cursor.
    private void Point(PointerState pointer, in PointerInput input)
    {
        pointer.Inside = true;
        pointer.Last = input;
        if (pointer.Hovers)
        {
            _cursorPointer = input.PointerId;
        }

        UpdateHover(pointer, input);
    }

    private void Down(PointerState pointer, in PointerInput input)
    {
        if (_arm.Active && _arm.PointerId == input.PointerId)
        {
            Disarm();
        }

        _tree.Focus.SetLastPress(input.PointerType);
        _tree.HideAccessKeyCues();
        _altAlone = false;
        Point(pointer, input);

        // A press outside a popup closes it, and goes on to the node under it unless the popup consumes it (design §7.3, "Closing").
        if (_tree.Popups.Count > 0 && PressOutsidePopups(pointer, input))
        {
            return;
        }

        Track(pointer, input, reset: true);
        if (HoldScroll(pointer, input))
        {
            return;
        }

        NodeHandle target = Target(pointer);
        if (CapturedNode(pointer).IsNone && !target.IsNone)
        {
            pointer.Capture = target;

            // Win32 captures a touch and a pen in contact to the window by itself; only the mouse asks.
            if (pointer.Type == PointerType.Mouse)
            {
                _platform.Capture();
            }
        }

        if (input.Button == PointerButton.Left && !target.IsNone)
        {
            pointer.ClickOrigin = target;
            pointer.Pressing = true;
            pointer.PastSlop = false;
            pointer.DownAt = new Vector2(input.X, input.Y);
            pointer.PressedLinkLeaf = pointer.LinkLeaf;
            pointer.PressedLink = pointer.Link;
            SetPressed(pointer, target);

            // Before the route, from the node the press starts at (design §7, "Focus on a press", Avalonia's FocusManager).
            _tree.Focus.FocusFromPress(target);
        }

        Route(PointerEventKind.Down, target, input, bubbleOnly: false);
    }

    private void Up(PointerState pointer, in PointerInput input)
    {
        Point(pointer, input);

        // The drop goes before the release's route, to the capture's source (design §7, "Drag and drop").
        DragUp(input);
        Route(PointerEventKind.Up, Target(pointer), input, bubbleOnly: false);
        if (input.Button == PointerButton.Left)
        {
            ClearPressed(pointer);
            NodeHandle origin = pointer.ClickOrigin;
            bool tapped = !pointer.PastSlop;
            pointer.ClickOrigin = default;
            pointer.Pressing = false;

            // A click when the release is over the node pressed or one of its descendants (Avalonia's Button, GPUI's div), and, for
            // a touch, when it did not move past the slop (Flutter's tap). The node hit is the one the release's hover test found; a
            // handler of the up may have removed it since (PR #327's review).
            NodeHandle released = _tree.Nodes.IsAlive(pointer.Hit) ? pointer.Hit : default;
            bool linkClicked = pointer.PressedLink >= 0 && ClickLink(pointer, tapped, input);
            if (!linkClicked && tapped && _tree.Nodes.IsAlive(origin) && IsSelfOrDescendant(released, origin))
            {
                Route(PointerEventKind.Click, origin, input, bubbleOnly: true);
            }
        }

        if (pointer.Drag is not null && input.Button == PointerButton.Left)
        {
            EndDrag(pointer, input, fling: true);
        }

        if (input.Buttons == PointerButtons.None && !pointer.Capture.IsNone)
        {
            pointer.Capture = default;
            if (pointer.Type == PointerType.Mouse)
            {
                _platform.ReleaseCapture();
            }

            // The hover followed the capture until now.
            UpdateHover(pointer, input);
        }
    }

    // The pointer left the window, or (a touch or a pen) ended: its hover ends. A touch or a pen gives its number back, which the
    // next pointer of that number takes with a cleared slot.
    private void Leave(PointerState pointer, in PointerInput input)
    {
        if (pointer.Type == PointerType.Mouse)
        {
            // While a button is down the window keeps the mouse's moves, which decide the hover outside it.
            if (CapturedNode(pointer).IsNone)
            {
                // Through UpdateHover, so that a leave that comes while enters and leaves are sent waits for them too (a handler that
                // pumps messages, Issue #329).
                pointer.Inside = false;
                pointer.Hit = default;
                UpdateHover(pointer, input);
            }

            return;
        }

        if (!pointer.Capture.IsNone || pointer.PressedCount > 0)
        {
            CaptureLost(pointer, input);
        }

        pointer.Inside = false;
        pointer.Hit = default;
        UpdateHover(pointer, input);
        pointer.Live = false;
        _livePointers--;
        if (_cursorPointer == input.PointerId)
        {
            _cursorPointer = 0;
            UpdateCursor();
        }
    }

    private void CaptureLost(PointerState pointer, in PointerInput input)
    {
        CancelDrag(input.PointerId);
        if (pointer.Drag is not null)
        {
            EndDrag(pointer, input, fling: false);
        }

        NodeHandle captured = CapturedNode(pointer);
        pointer.Capture = default;
        pointer.ClickOrigin = default;
        pointer.Pressing = false;
        pointer.PressedLink = -1;
        ClearPressed(pointer);
        if (!captured.IsNone)
        {
            Route(PointerEventKind.Cancel, captured, input, bubbleOnly: false);
        }

        if (pointer.Inside)
        {
            UpdateHover(pointer, pointer.Last);
        }
    }

    // The node a routed event starts from: the capture's while there is one, the node hit otherwise.
    private NodeHandle Target(PointerState pointer) => CapturedNode(pointer) is { IsNone: false } captured ? captured : pointer.Hit;

    // The capture's node, or none; a capture whose node was removed is released.
    private NodeHandle CapturedNode(PointerState pointer)
    {
        if (!pointer.Capture.IsNone && !_tree.Nodes.IsAlive(pointer.Capture))
        {
            pointer.Capture = default;
            if (pointer.Type == PointerType.Mouse)
            {
                _platform.ReleaseCapture();
            }
        }

        return pointer.Capture;
    }

    // Tests the position, and changes the pointer's hover to the node hit and its ancestors; while captured, to the capture's node
    // and its ancestors when the node hit is it or a descendant, and to nothing otherwise (Avalonia's PointerOverPreProcessor). A
    // touch keeps no hover: only its node hit changes. A test that comes while enters and leaves are sent (a handler that runs a
    // frame synchronously, whose rehit lands here, or that pumps messages) waits until they end, and then runs at the pointer's
    // last position: the chains it would change are being read.
    private void UpdateHover(PointerState pointer, in PointerInput input)
    {
        if (!pointer.Hovers)
        {
            pointer.Hit = pointer.Inside ? HitAt(input) : default;
            _ = FindLink(pointer, input);
            return;
        }

        if (_hovering)
        {
            pointer.HoverPending = true;
            _hoverPending = true;
            return;
        }

        TestHover(pointer, input);
        if (_hoverPending)
        {
            TestPendingHovers();
        }
    }

    // The tests that waited while enters and leaves were sent; apart from UpdateHover, which every move and wheel calls.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private void TestPendingHovers()
    {
        // A handler that keeps changing the tree under the pointer is not followed past a few tests.
        for (int again = 0; _hoverPending && again < 4; again++)
        {
            _hoverPending = false;
            for (int i = 0; i < _pointers.Length; i++)
            {
                PointerState waiting = _pointers[i];
                if (waiting.HoverPending)
                {
                    waiting.HoverPending = false;
                    TestHover(waiting, waiting.Last);
                }
            }
        }

        _hoverPending = false;
        for (int i = 0; i < _pointers.Length; i++)
        {
            _pointers[i].HoverPending = false;
        }
    }

    // The pointer's hover at the position, or none when it left the window.
    private void TestHover(PointerState pointer, in PointerInput input)
    {
        if (!pointer.Inside)
        {
            pointer.Hit = default;
            _ = FindLink(pointer, input);
            SetHover(pointer, 0, input);
            return;
        }

        pointer.Hit = HitAt(input);
        bool enteredLink = FindLink(pointer, input);
        NodeHandle captured = CapturedNode(pointer);
        NodeHandle start =
            captured.IsNone ? pointer.Hit
            : IsSelfOrDescendant(pointer.Hit, captured) ? captured
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

        SetHover(pointer, count, input);
        if (enteredLink)
        {
            EnterLink(pointer, input);
        }
    }

    // Changes the pointer's hover to the first count nodes of _nextHovered: a leave for each node that left, innermost first, then
    // an enter for each that entered, outermost first (Flutter's MouseTracker). Both chains end at the root, so the nodes they share
    // are their common tail.
    private void SetHover(PointerState pointer, int count, in PointerInput input)
    {
        int shared = 0;
        while (
            shared < pointer.HoveredCount && shared < count && pointer.Hovered[pointer.HoveredCount - 1 - shared] == _nextHovered[count - 1 - shared]
        )
        {
            shared++;
        }

        int left = pointer.HoveredCount - shared;
        int entered = count - shared;
        if (left > 0 || entered > 0)
        {
            _hovering = true;
            try
            {
                SendHoverChange(pointer, left, entered, count, input);
            }
            finally
            {
                _hovering = false;
            }
        }

        UpdateCursor();
    }

    // The leaves of the nodes left, then the enters of the nodes entered, with the chain changed between them. A node keeps the
    // hovered state while another pointer's hover holds it (design §7, "Touch and pen").
    private void SendHoverChange(PointerState pointer, int left, int entered, int count, in PointerInput input)
    {
        for (int i = 0; i < left; i++)
        {
            NodeHandle node = pointer.Hovered[i];
            if (_tree.Nodes.IsAlive(node))
            {
                if (!HeldByAnother(pointer, node, pressed: false))
                {
                    _tree.SetPseudoState(node, PseudoState.Hovered, on: false);
                }

                Direct(PointerEventKind.Leave, node, input);
            }
        }

        if (pointer.Hovered.Length < count)
        {
            Array.Resize(ref pointer.Hovered, _nextHovered.Length);
        }

        Array.Copy(_nextHovered, pointer.Hovered, count);
        pointer.HoveredCount = count;
        for (int i = entered - 1; i >= 0; i--)
        {
            NodeHandle node = pointer.Hovered[i];
            if (_tree.Nodes.IsAlive(node))
            {
                _tree.SetPseudoState(node, PseudoState.Hovered, on: true);
                Direct(PointerEventKind.Enter, node, input);
            }
        }
    }

    // Whether a pointer other than this one holds the node in its hover, or in its pressed chain.
    private bool HeldByAnother(PointerState pointer, NodeHandle node, bool pressed)
    {
        // The mouse alone, the usual case: no other pointer to look at.
        if (_livePointers <= 1)
        {
            return false;
        }

        for (int p = 0; p < _pointers.Length; p++)
        {
            PointerState other = _pointers[p];
            if (other == pointer || !other.Live)
            {
                continue;
            }

            NodeHandle[] chain = pressed ? other.Pressed : other.Hovered;
            int length = pressed ? other.PressedCount : other.HoveredCount;
            for (int i = 0; i < length; i++)
            {
                if (chain[i] == node)
                {
                    return true;
                }
            }
        }

        return false;
    }

    // The pressed state on the node and its ancestors, kept to clear them on the release.
    private void SetPressed(PointerState pointer, NodeHandle target)
    {
        ClearPressed(pointer);
        for (NodeHandle node = target; !node.IsNone; node = _tree.Nodes.GetRef(node).Parent)
        {
            if (pointer.PressedCount == pointer.Pressed.Length)
            {
                Array.Resize(ref pointer.Pressed, pointer.PressedCount * 2);
            }

            pointer.Pressed[pointer.PressedCount++] = node;
            _tree.SetPseudoState(node, PseudoState.Pressed, on: true);
        }
    }

    private void ClearPressed(PointerState pointer)
    {
        int count = pointer.PressedCount;
        pointer.PressedCount = 0;
        for (int i = 0; i < count; i++)
        {
            NodeHandle node = pointer.Pressed[i];
            if (_tree.Nodes.IsAlive(node) && !HeldByAnother(pointer, node, pressed: true))
            {
                _tree.SetPseudoState(node, PseudoState.Pressed, on: false);
            }
        }
    }

    // The cursor of the first node from the capture's node, or the node hit, of the pointer that chooses it (the mouse, or the pen
    // that moved last) up to the root that sets one; the arrow when none does.
    private void UpdateCursor()
    {
        PointerState pointer = _pointers[_cursorPointer < _pointers.Length && _pointers[_cursorPointer].Live ? _cursorPointer : 0];

        // Over a link, the hand, before the cursors of its leaf and the leaf's ancestors (design §4.8, as Flutter's TextSpan).
        CursorKind cursor = pointer.Link >= 0 ? CursorKind.Hand : CursorKind.Arrow;
        for (NodeHandle node = pointer.Link >= 0 ? default : Target(pointer); !node.IsNone; node = _tree.Nodes.GetRef(node).Parent)
        {
            if (_tree.Nodes.GetRef(node).Input?.Cursor is { } set)
            {
                cursor = set;
                break;
            }

            // A text leaf a selection area selects counts as one with the I-beam (design §4.10, §7 "Cursor shape").
            if (_tree.Nodes.GetRef(node).Text is not null && _tree.IsSelectableText(node))
            {
                cursor = CursorKind.IBeam;
                break;
            }
        }

        if (cursor != _cursor)
        {
            _cursor = cursor;
            _platform.SetCursor(cursor);
        }
    }

    // The popups a press outside them closes, before the press is routed. Returns whether the press stops there; when it goes on, the
    // node under it is found again without the popups closed, as their roots left the tree. A press that stops is not tracked: its up
    // goes to the node under the pointer as an up with no press, which makes no click, as the press set no click origin.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private bool PressOutsidePopups(PointerState pointer, in PointerInput input)
    {
        NodeHandle hit = pointer.Hit;
        PopupHandle? pressed = hit.IsNone ? null : _tree.Popups.PopupContaining(hit);
        bool onBackdrop = pressed is not null && hit == pressed.Root;
        int open = _tree.Popups.Count;
        if (_tree.Popups.PressOutside(pressed, onBackdrop))
        {
            return true;
        }

        if (_tree.Popups.Count != open)
        {
            UpdateHover(pointer, input);
        }

        return false;
    }

    // A modal popup opened: a pointer captured by a node behind it loses the capture (design §7.3, "Closing"). A signal's effect opens
    // it, outside the batches of the input, so the cancels run in one of their own.
    private void ReleaseCapturesBehind(NodeHandle modalRoot)
    {
        ReactiveRuntime.EnterBatch();
        try
        {
            for (int i = 0; i < _pointers.Length; i++)
            {
                PointerState pointer = _pointers[i];
                NodeHandle captured = pointer.Live ? CapturedNode(pointer) : default;
                if (!captured.IsNone && _tree.Popups.PopupContaining(captured)?.Root != modalRoot)
                {
                    if (pointer.Type == PointerType.Mouse)
                    {
                        _platform.ReleaseCapture();
                    }

                    CaptureLost(pointer, pointer.Last);
                }
            }
        }
        finally
        {
            ReactiveRuntime.ExitBatch();
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
            PointerState? dragger =
                kind == PointerEventKind.Move ? DragCandidate(input)
                : kind == PointerEventKind.PanUpdate ? PanCandidate(input)
                : null;
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

                // A touch's or a pen's drag turns into a scroll the same way, after the container's handlers (row I1b), and so does a
                // touchpad's pan (row I1c).
                if (
                    dragger is not null
                    && !_stopped
                    && (kind == PointerEventKind.Move ? TryStartDrag(_route[i], dragger, input) : TryStartPan(_route[i], dragger, input))
                )
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
            // A popup's keys go on to its anchor and the anchor's ancestors (design §7.3, "The route of events").
            for (NodeHandle node = target; !node.IsNone; node = _tree.KeyParent(node))
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

    // Delivers text from target as RouteKey delivers a key, on the same array of routes. Returns whether a handler stopped it.
    private bool RouteText(NodeHandle target, in TextInput input)
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
            for (NodeHandle node = target; !node.IsNone; node = _tree.KeyParent(node))
            {
                if (_routeCount == _route.Length)
                {
                    Array.Resize(ref _route, _routeCount * 2);
                }

                _route[_routeCount++] = node;
            }

            int end = _routeCount;
            for (int i = end - 1; i >= start && !_stopped; i--)
            {
                InvokeText(EventPhase.Capture, target, _route[i], input);
            }

            for (int i = start; i < end && !_stopped; i++)
            {
                InvokeText(EventPhase.Bubble, target, _route[i], input);
            }

            return _stopped;
        }
        finally
        {
            _routeCount = start;
            _stopped = stopped;
        }
    }

    // Calls the text input handlers of one node for the phase, as InvokeKey calls the key handlers.
    private void InvokeText(EventPhase phase, NodeHandle target, NodeHandle node, in TextInput input)
    {
        if (EnabledInput(node) is not { } nodeInput)
        {
            return;
        }

        foreach (TextHandlerEntry entry in nodeInput.TextHandlers)
        {
            if (entry.Phase != phase)
            {
                continue;
            }

            ScopeRef previous = default;
            if (!entry.Scope.IsNone && !Scope.TryEnter(entry.Scope, out previous))
            {
                continue;
            }

            var e = new TextInputEvent(this, target, node, input);
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
                : _frames.ListOf(nodeInput.HitBuild) is { } list ? list.PointInRegion(nodeInput.HitEntry, physical) / scale
                : new Vector2(float.NaN);
            var e = new PointerEvent(
                this,
                kind,
                target,
                node,
                fromKey ? new Vector2(float.NaN) : physical / scale,
                local,
                input,
                fromKey ? 0 : _dispatch
            );

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

        // A fling of the container stops first, as Flutter's pointerScroll goes idle (row I1b).
        StopFling(node);

        Vector2 delta = input.Delta;
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

        // A fling of the container stops first, as Flutter's pointerScroll goes idle (row I1b).
        StopFling(node);

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
        NodeHandle hit = _tree.Popups.Count > 0 ? HitPopups(input) : default;
        if (hit.IsNone)
        {
            hit = _frames.List.HitTest(input.X, input.Y);
        }

        if (!_tree.Nodes.IsAlive(hit))
        {
            return default;
        }

        return (_tree.Nodes.GetRef(hit).Pseudo & PseudoState.Disabled) == 0 ? hit : EnabledAncestor(hit);
    }

    // The front node of the popups' lists at the position, the front popup first (design §7.3, "Hit testing"); none when no popup's
    // region holds it. A popup that leaves takes no pointer.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private NodeHandle HitPopups(in PointerInput input)
    {
        IReadOnlyList<PopupHandle> open = _tree.Popups.Opened;
        for (int i = open.Count - 1; i >= 0; i--)
        {
            PopupHandle popup = open[i];
            if (popup.Closing || popup.List.Build == 0)
            {
                continue;
            }

            NodeHandle hit = popup.List.HitTest(input.X, input.Y);
            if (!hit.IsNone)
            {
                return hit;
            }
        }

        return default;
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

    // The input state of one pointer (design §7, "Touch and pen"): its capture, the node its press started at, its node hit, its
    // last input, its hover, and its pressed chain.
    private sealed class PointerState
    {
        public bool Live;
        public PointerType Type;
        public bool Inside;
        public NodeHandle Capture;
        public NodeHandle ClickOrigin;
        public NodeHandle Hit;
        public PointerInput Last;
        public NodeHandle[] Hovered = new NodeHandle[16];
        public int HoveredCount;
        public NodeHandle[] Pressed = new NodeHandle[16];
        public int PressedCount;
        public bool HoverPending;
        public bool Pressing;
        public bool PastSlop;
        public Vector2 DownAt;

        // A touch's or a pen's positions since its press, for the velocity of a drag; made at its first press (row I1b).
        public VelocityTracker? Tracker;

        // The drag of a scroll container the pointer takes part in, or none (row I1b).
        public ScrollDrag? Drag;

        // A touchpad's pan since its start, in logical pixels (row I1c).
        public Vector2 PanTotal;

        // The link the pointer is over (its leaf, and its index in the leaf's ranges or -1) and that link's leave; and the link the
        // left button was pressed over (row T12).
        public NodeHandle LinkLeaf;
        public int Link = -1;
        public PointerHandler? LinkLeave;
        public NodeHandle PressedLinkLeaf;
        public int PressedLink = -1;

        // Whether the pointer has a hover: the mouse and pens do; a touch and a touchpad's pan do not (design §7, "Touch and pen").
        public bool Hovers => Type is PointerType.Mouse or PointerType.Pen;
    }
}
