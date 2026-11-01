using System.Numerics;

namespace XylemUI;

// Drag and drop in the app (row DD1, design §7, "Drag and drop"): a drag source's press is armed; a mouse's move past the OS's drag
// size, or a touch's or a pen's press held still for 500 ms, starts the drag, which takes the capture to the source, opens the
// preview, and from then on hit tests each move for the drop target, until a release drops it or Esc, the window's deactivation,
// or a lost capture cancels it. One drag at a time in a window.
internal sealed partial class WindowInput
{
    // Flutter's EdgeDraggingAutoScroller: the overscroll counted at most (scrollable_helpers.dart line 256) and the speed for each
    // logical pixel of it, a reorderable list's default (reorderable_list.dart line 611).
    private const float AutoScrollMax = 20;
    private const float AutoScrollScalar = 50;

    // Flutter's kLongPressTimeout (gestures/constants.dart line 29), and the tooltip's long press (design §13.1).
    private static readonly TimeSpan LongPressDelay = TimeSpan.FromMilliseconds(500);

    private readonly DragArm _arm = new();
    private DragSession? _dragSession;

    /// <summary>
    /// Gets a value indicating whether a drag runs in the window.
    /// </summary>
    internal bool Dragging => _dragSession is not null;

    /// <summary>
    /// Arms a drag from a drag source's press: the left button's down that reached the source in the bubble phase (design §7, "Drag
    /// and drop", "A press a part takes"). The innermost source the press reaches is armed; an outer one is not.
    /// </summary>
    /// <param name="e">The down.</param>
    /// <param name="source">The drag source.</param>
    /// <param name="entry">What it was made with.</param>
    internal void ArmDrag(in PointerEvent e, NodeHandle source, DragSourceEntry entry)
    {
        if (_dragSession is not null || e.Button != PointerButton.Left || (_arm.Active && _arm.PointerId == e.PointerId))
        {
            return;
        }

        PointerState pointer = _pointers[e.PointerId];
        _arm.Active = true;
        _arm.PointerId = e.PointerId;
        _arm.Source = source;
        _arm.Entry = entry;
        _arm.DownAt = new Vector2(pointer.Last.X, pointer.Last.Y);
        _arm.Mouse = e.PointerType == PointerType.Mouse;
        if (!_arm.Mouse)
        {
            _arm.Ui = SynchronizationContext.Current;
            if (_arm.Timer is null)
            {
                _arm.Timer = _tree.Time.CreateTimer(
                    static state => ((WindowInput)state!).LongPressTick(),
                    this,
                    LongPressDelay,
                    Timeout.InfiniteTimeSpan
                );
            }
            else
            {
                _ = _arm.Timer.Change(LongPressDelay, Timeout.InfiniteTimeSpan);
            }
        }
    }

    /// <summary>
    /// Moves the capture of a pointer to a node, or releases it with none (design §7, "Drag and drop", "The explicit capture"): the
    /// node that had it gets a cancel along its route. The capture still ends when every button is up.
    /// </summary>
    /// <param name="pointerId">The pointer.</param>
    /// <param name="node">The node, or none.</param>
    internal void CapturePointer(int pointerId, NodeHandle node)
    {
        if (pointerId < 0 || pointerId >= _pointers.Length || !_pointers[pointerId].Live)
        {
            return;
        }

        PointerState pointer = _pointers[pointerId];
        NodeHandle old = CapturedNode(pointer);
        if (old == node || (!node.IsNone && !_tree.Nodes.IsAlive(node)))
        {
            return;
        }

        pointer.Capture = node;
        if (!old.IsNone)
        {
            Route(PointerEventKind.Cancel, old, pointer.Last, bubbleOnly: false);
        }
    }

    /// <summary>
    /// Moves the drag's auto-scroll one frame on (design §7, "Drag and drop", "Auto-scroll at the edge"): while the preview's box, or
    /// the pointer's point with no preview, is past an edge of the scroll container last under the pointer, the container moves that
    /// way by the overscroll, at most 20 logical pixels, times 50 a second.
    /// </summary>
    /// <param name="frameTime">The frame's time.</param>
    /// <returns>Whether it scrolls on, and asks for the next frame.</returns>
    internal bool AdvanceDragScroll(TimeSpan frameTime)
    {
        if (_dragSession is not { } drag || drag.Scroll.IsNone || !_tree.Nodes.IsAlive(drag.Scroll))
        {
            return false;
        }

        ref readonly LayoutStyle style = ref _tree.Nodes.GetRef(drag.Scroll).Layout.Style;
        Rect box = _frames.WindowBox(drag.Scroll);
        Rect dragged = DraggedBox(drag);
        float dx = style.OverflowX == Overflow.Scroll ? Overscroll(dragged.X, dragged.X + dragged.Width, box.X, box.X + box.Width) : 0;
        float dy = style.OverflowY == Overflow.Scroll ? Overscroll(dragged.Y, dragged.Y + dragged.Height, box.Y, box.Y + box.Height) : 0;
        if (dx == 0 && dy == 0)
        {
            drag.ScrolledAt = null;
            return false;
        }

        // The first frame past the edge starts the clock; each next one moves by the speed over the time since the one before.
        if (drag.ScrolledAt is { } before)
        {
            float seconds = (float)(frameTime - before).TotalSeconds;
            if (ScrollBy(drag.Scroll, new Vector2(dx, dy) * AutoScrollScalar * seconds))
            {
                Retarget(drag);
            }
        }

        drag.ScrolledAt = frameTime;
        return true;
    }

    // How far a box [start, end] is past the viewport [low, high] on one axis, at most AutoScrollMax: negative before it, positive
    // after it, 0 inside.
    private static float Overscroll(float start, float end, float low, float high) =>
        start < low ? -Math.Min(low - start, AutoScrollMax)
        : end > high ? Math.Min(end - high, AutoScrollMax)
        : 0;

    // The effect a drop chooses from the effects both sides allow: Alt for Link, Control for Copy, Move with neither (Avalonia's
    // InProcessDragSource.cs lines 83 to 96); one both do not allow gives way to the first of Move, Copy, and Link both allow.
    private static DragEffects ChooseEffect(DragEffects both, KeyModifiers modifiers)
    {
        DragEffects wanted =
            (modifiers & KeyModifiers.Alt) != 0 ? DragEffects.Link
            : (modifiers & KeyModifiers.Control) != 0 ? DragEffects.Copy
            : DragEffects.Move;
        if ((both & wanted) != 0)
        {
            return wanted;
        }

        return (both & DragEffects.Move) != 0 ? DragEffects.Move
            : (both & DragEffects.Copy) != 0 ? DragEffects.Copy
            : (both & DragEffects.Link) != 0 ? DragEffects.Link
            : DragEffects.None;
    }

    // A pointer's move while a drag is armed or runs: a mouse past the drag size starts the drag; a touch or a pen past the slop
    // before its long press disarms it; a running drag moves.
    private void DragMove(PointerState pointer, in PointerInput input)
    {
        if (_dragSession is { } drag)
        {
            if (drag.PointerId == input.PointerId)
            {
                drag.Last = input;
                drag.Modifiers = input.Modifiers;
                Retarget(drag);
            }

            return;
        }

        if (!_arm.Active || _arm.PointerId != input.PointerId)
        {
            return;
        }

        var at = new Vector2(input.X, input.Y);
        if (_arm.Mouse)
        {
            TextEditingSettings settings = _tree.TextEditing;
            if (Math.Abs(at.X - _arm.DownAt.X) > settings.DragWidth || Math.Abs(at.Y - _arm.DownAt.Y) > settings.DragHeight)
            {
                StartDrag(pointer, input);
            }
        }
        else if (Vector2.Distance(at, _arm.DownAt) > TouchSlop * _frames.Scale)
        {
            Disarm();
        }
    }

    // A pointer's release: the running drag drops; an armed one is disarmed.
    private void DragUp(in PointerInput input)
    {
        if (_dragSession is { } drag && drag.PointerId == input.PointerId && input.Button == PointerButton.Left)
        {
            drag.Last = input;
            drag.Modifiers = input.Modifiers;
            EndDragSession(drop: true);
        }

        if (_arm.Active && _arm.PointerId == input.PointerId)
        {
            Disarm();
        }
    }

    // The pointer's capture was lost, or the window stopped being active: the drag is cancelled, and an armed one disarmed.
    private void CancelDrag(int pointerId)
    {
        if (_dragSession is { } drag && (pointerId < 0 || drag.PointerId == pointerId))
        {
            EndDragSession(drop: false);
        }

        if (_arm.Active && (pointerId < 0 || _arm.PointerId == pointerId))
        {
            Disarm();
        }
    }

    // A key while a drag runs: Esc cancels it and is taken; a change of the modifier keys chooses the effect again.
    private bool DragKey(in KeyInput input)
    {
        if (_dragSession is not { } drag)
        {
            return false;
        }

        if (input.Kind == KeyEventKind.Down && input.Key == Key.Escape)
        {
            EndDragSession(drop: false);
            return true;
        }

        if (input.Modifiers != drag.Modifiers)
        {
            drag.Modifiers = input.Modifiers;
            Retarget(drag);
        }

        return false;
    }

    private void Disarm()
    {
        _arm.Active = false;
        _arm.Source = default;
        _arm.Entry = null;
        _ = _arm.Timer?.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
    }

    // The long press's timer may fire on another thread: its work runs on the UI thread.
    private void LongPressTick()
    {
        if (_arm.Ui is { } ui && SynchronizationContext.Current != ui)
        {
            ui.Post(static state => ((WindowInput)state!).LongPressElapsed(), this);
            return;
        }

        LongPressElapsed();
    }

    private void LongPressElapsed()
    {
        if (!_arm.Active || _arm.Mouse || _dragSession is not null)
        {
            return;
        }

        PointerState pointer = _pointers[_arm.PointerId];
        if (!pointer.Live || (pointer.Last.Buttons & PointerButtons.Left) == 0)
        {
            Disarm();
            return;
        }

        StartDrag(pointer, pointer.Last);
    }

    // The armed press starts a drag: the source gives its value, takes the capture, and its press gives no click; the preview opens.
    private void StartDrag(PointerState pointer, in PointerInput input)
    {
        DragSourceEntry entry = _arm.Entry!;
        NodeHandle source = _arm.Source;
        Vector2 down = _arm.DownAt / _frames.Scale;
        Disarm();
        if (!_tree.Nodes.IsAlive(source) || entry.Take() is not { } value)
        {
            return;
        }

        Rect box = _frames.WindowBox(source);
        var drag = new DragSession(input.PointerId, value, entry, source, down - new Vector2(box.X, box.Y), input);
        drag.Modifiers = input.Modifiers;
        _dragSession = drag;
        pointer.ClickOrigin = default;
        ClearPressed(pointer);
        CapturePointer(input.PointerId, source);
        entry.PreviewOpen?.Set(true);
        Retarget(drag);
        _tree.NoteWork();
    }

    // Hit tests the drag's last position for the drop target, sends it the enter, the over, or the leave, and moves the preview.
    private void Retarget(DragSession drag)
    {
        Vector2 position = new Vector2(drag.Last.X, drag.Last.Y) / _frames.Scale;
        NodeHandle hit = HitAt(drag.Last);
        NodeHandle target = default;
        DropTargetEntry? entry = null;
        DragEffects effect = DragEffects.None;
        NodeHandle scroll = default;
        Dictionary<NodeHandle, DropTargetEntry>? targets = _tree.DropTargets;
        for (NodeHandle node = hit; !node.IsNone; node = _tree.Nodes.GetRef(node).Parent)
        {
            ref UiNode n = ref _tree.Nodes.GetRef(node);
            if (scroll.IsNone && n.Layout.Style.IsScrollContainer)
            {
                scroll = node;
            }

            if (
                target.IsNone
                && targets is not null
                && (n.Pseudo & PseudoState.Disabled) == 0
                && targets.TryGetValue(node, out DropTargetEntry? candidate)
                && candidate.Takes(drag.Value)
            )
            {
                DragEffects chosen = ChooseEffect(drag.Entry.Options.Allowed & candidate.Accepts, drag.Modifiers);
                if (chosen != DragEffects.None)
                {
                    (target, entry, effect) = (node, candidate, chosen);
                }
            }
        }

        if (!scroll.IsNone)
        {
            drag.Scroll = scroll;
        }

        if (target != drag.Target)
        {
            LeaveTarget(drag);
            drag.Target = target;
            drag.TargetEntry = entry;
            drag.Effect = effect;
            if (entry is not null)
            {
                _tree.SetPseudoState(target, PseudoState.DragOver, on: true);
                entry.Send(DropCall.Enter, drag.Value, position - TopLeft(target), effect, drag.Modifiers);
            }
        }
        else if (entry is not null)
        {
            drag.Effect = effect;
            entry.Send(DropCall.Over, drag.Value, position - TopLeft(target), effect, drag.Modifiers);
        }

        MovePreview(drag, position);
    }

    // The drag's target, if any and still there, gets a leave and loses its drag-over state.
    private void LeaveTarget(DragSession drag)
    {
        if (drag.TargetEntry is { } old && _tree.Nodes.IsAlive(drag.Target))
        {
            _tree.SetPseudoState(drag.Target, PseudoState.DragOver, on: false);
            Vector2 position = new Vector2(drag.Last.X, drag.Last.Y) / _frames.Scale;
            old.Send(DropCall.Leave, drag.Value, position - TopLeft(drag.Target), drag.Effect, drag.Modifiers);
        }

        drag.Target = default;
        drag.TargetEntry = null;
        drag.Effect = DragEffects.None;
    }

    // Ends the drag once, by whichever way out (design §7, "Drag and drop", "The ways in and out"): the target gets the drop or a
    // leave, the source its end with the effect, the preview closes, and the drag-over state goes.
    private void EndDragSession(bool drop)
    {
        if (_dragSession is not { } drag)
        {
            return;
        }

        _dragSession = null;
        DragEffects effect = DragEffects.None;
        if (drop && drag.TargetEntry is { } entry && _tree.Nodes.IsAlive(drag.Target))
        {
            effect = drag.Effect;
            _tree.SetPseudoState(drag.Target, PseudoState.DragOver, on: false);
            Vector2 position = new Vector2(drag.Last.X, drag.Last.Y) / _frames.Scale;
            entry.Send(DropCall.Drop, drag.Value, position - TopLeft(drag.Target), effect, drag.Modifiers);
        }
        else
        {
            LeaveTarget(drag);
        }

        drag.Entry.PreviewOpen?.Set(false);
        drag.Entry.End(effect);
        _tree.NoteWork();
    }

    // Puts the preview's top left where the pointer is, less where the source was pressed.
    private void MovePreview(DragSession drag, Vector2 position)
    {
        if (drag.Entry.Preview is { } preview)
        {
            _tree.Popups.MoveDragImage(preview.Popup, position - drag.Grab);
        }
    }

    // The box the auto-scroll tests: the preview's, or the pointer's point with no preview, in the window's logical pixels.
    private Rect DraggedBox(DragSession drag)
    {
        if (drag.Entry.Preview?.Popup is { } popup && _tree.Nodes.IsAlive(popup.Frame))
        {
            return _tree.Nodes.GetRef(popup.Frame).Layout.Rect;
        }

        Vector2 position = new Vector2(drag.Last.X, drag.Last.Y) / _frames.Scale;
        return new Rect(position.X, position.Y, 0, 0);
    }

    private Vector2 TopLeft(NodeHandle node)
    {
        Rect box = _frames.WindowBox(node);
        return new Vector2(box.X, box.Y);
    }

    // A press that may start a drag: one at a time in a window, its timer kept for the next.
    private sealed class DragArm
    {
        public bool Active;
        public int PointerId;
        public NodeHandle Source;
        public DragSourceEntry? Entry;
        public Vector2 DownAt;
        public bool Mouse;
        public SynchronizationContext? Ui;
        public ITimer? Timer;
    }

    // The drag that runs.
    private sealed class DragSession(int pointerId, object value, DragSourceEntry entry, NodeHandle source, Vector2 grab, PointerInput last)
    {
        public int PointerId { get; } = pointerId;

        public object Value { get; } = value;

        public DragSourceEntry Entry { get; } = entry;

        public NodeHandle Source { get; } = source;

        public Vector2 Grab { get; } = grab;

        public PointerInput Last { get; set; } = last;

        public KeyModifiers Modifiers { get; set; }

        public NodeHandle Target { get; set; }

        public DropTargetEntry? TargetEntry { get; set; }

        public DragEffects Effect { get; set; }

        public NodeHandle Scroll { get; set; }

        public TimeSpan? ScrolledAt { get; set; }
    }
}
