using System.Numerics;

namespace XylemUI;

// A touch's and a pen's drag of a scroll container, and its fling (row I1b, design §7, "Touch and pen"): the drag starts when a
// press's move, bubbling, reaches a scroll container past 18 logical pixels along the container's axes, and the release's
// velocity, from Flutter's VelocityTracker, flings it with Flutter's ClampingScrollSimulation, one frame at a time.
internal sealed partial class WindowInput
{
    // Flutter's kMinFlingVelocity and kMaxFlingVelocity (gestures/constants.dart lines 90 and 95), in logical pixels a second.
    private const double MinFlingVelocity = 50;
    private const double MaxFlingVelocity = 8_000;

    // The drags and flings running, few at a time, and the ones that ended, kept to be used again.
    private readonly List<ScrollDrag> _drags = new(4);
    private readonly Stack<ScrollDrag> _spareDrags = new(4);

    /// <summary>
    /// Moves the flings one frame on (design §7, "Touch and pen"): each container's position becomes the fling's at the frame's
    /// time, counted from the first frame of the fling; a fling ends when its time is over or the position reaches an end of the
    /// range. The frames call it after the scroll ranges are written.
    /// </summary>
    /// <param name="frameTime">The frame's time (<see cref="WindowFrames.FrameTime"/>).</param>
    /// <returns>Whether a fling goes on, and asks for the next frame.</returns>
    internal bool AdvanceFlings(TimeSpan frameTime)
    {
        bool more = false;
        for (int i = _drags.Count - 1; i >= 0; i--)
        {
            ScrollDrag drag = _drags[i];
            if (!drag.Flinging)
            {
                continue;
            }

            if (!_tree.Nodes.IsAlive(drag.Container))
            {
                Release(drag);
                continue;
            }

            if (!drag.Started)
            {
                drag.Started = true;
                drag.Start = frameTime;
            }

            double t = (frameTime - drag.Start).TotalSeconds;
            ScrollState state = _tree.ScrollStateFor(drag.Container);
            Vector2 current = state.OffsetValue;
            Vector2 max = state.MaxOffsetValue;
            Vector2 next = current;
            if (drag.FlingX)
            {
                next.X = Step(ref drag.FlingX, drag.X, t, max.X);
            }

            if (drag.FlingY)
            {
                next.Y = Step(ref drag.FlingY, drag.Y, t, max.Y);
            }

            if (next != current)
            {
                _ = state.SetOffset(next);
            }

            if (drag.FlingX || drag.FlingY)
            {
                more = true;
            }
            else
            {
                Release(drag);
            }
        }

        return more;
    }

    // One axis of a fling at t seconds: its position, kept in the range; the axis stops when its time is over or the range cut it
    // (Flutter's BallisticScrollActivity goes idle when the position does not move all the way, scroll_activity.dart lines 620 to
    // 621).
    private static float Step(ref bool running, in ClampingFling fling, double t, float max)
    {
        double position = fling.At(t);
        double kept = Math.Clamp(position, 0, max);
        if (kept != position || t >= fling.Duration)
        {
            running = false;
        }

        return (float)kept;
    }

    // A pointer's move: its position and hover, the tap's slop, the velocity's position, the route, and, for the finger that
    // drives a drag that ran before this move, the container's position by the move's difference (Flutter's latestPointer). The
    // move that starts a drag moves nothing: the pointer had no drag before it.
    private void Move(PointerState pointer, in PointerInput input)
    {
        int previousX = pointer.Last.X;
        int previousY = pointer.Last.Y;
        bool driving = pointer.Drag is { Flinging: false };
        Point(pointer, input);
        if (pointer.Pressing && !pointer.PastSlop && pointer.Type == PointerType.Touch)
        {
            pointer.PastSlop = Vector2.Distance(new Vector2(input.X, input.Y), pointer.DownAt) > TouchSlop * _frames.Scale;
        }

        Track(pointer, input, reset: false);
        Route(PointerEventKind.Move, Target(pointer), input, bubbleOnly: false);
        if (driving && pointer.Drag is { } drag && drag.Active == input.PointerId)
        {
            Vector2 moved = new Vector2(input.X - previousX, input.Y - previousY) / _frames.Scale;
            _ = ScrollBy(drag.Container, new Vector2(drag.Horizontal ? -moved.X : 0, drag.Vertical ? -moved.Y : 0));
        }
    }

    // A touch's or a pen's position in contact, in logical pixels, for the velocity of a drag; a press of the left button starts the
    // positions again. Flutter adds the down and each move of a pointer (monodrag.dart lines 656 to 667).
    private void Track(PointerState pointer, in PointerInput input, bool reset)
    {
        if (pointer.Type == PointerType.Mouse || (input.Buttons & PointerButtons.Left) == 0)
        {
            return;
        }

        VelocityTracker tracker = pointer.Tracker ??= new VelocityTracker();
        if (reset && input.Button == PointerButton.Left)
        {
            tracker.Reset();
        }

        float scale = _frames.Scale;
        tracker.Add(input.Timestamp, input.X / scale, input.Y / scale);
    }

    // A press over a scroll container that is flinging or dragged: the flings under it stop, and the press goes to the innermost
    // such container, not to the nodes inside (Flutter ignores their pointers while it drags and flings, scroll_activity.dart line
    // 538). A touch or a pen on a container another finger drags joins the drag and drives it. Returns whether it took the press.
    private bool HoldScroll(PointerState pointer, in PointerInput input)
    {
        if (_drags.Count == 0)
        {
            return false;
        }

        NodeHandle holder = default;
        ScrollDrag? dragged = null;
        for (NodeHandle node = pointer.Hit; !node.IsNone; node = _tree.Nodes.GetRef(node).Parent)
        {
            if (FindDrag(node) is not { } drag)
            {
                continue;
            }

            if (holder.IsNone)
            {
                holder = node;
                dragged = drag.Flinging ? null : drag;
            }

            if (drag.Flinging)
            {
                Release(drag);
            }
        }

        if (holder.IsNone)
        {
            return false;
        }

        if (CapturedNode(pointer).IsNone)
        {
            pointer.Capture = holder;
            if (pointer.Type == PointerType.Mouse)
            {
                _platform.Capture();
            }
        }

        pointer.ClickOrigin = default;
        if (input.Button == PointerButton.Left)
        {
            pointer.Pressing = true;
            pointer.PastSlop = true;
            pointer.DownAt = new Vector2(input.X, input.Y);
        }

        if (dragged is not null && pointer.Type != PointerType.Mouse && pointer.Drag is null)
        {
            dragged.Add(input.PointerId);
            dragged.Active = input.PointerId;
            pointer.Drag = dragged;
        }

        Route(PointerEventKind.Down, CapturedNode(pointer), input, bubbleOnly: false);
        return true;
    }

    // The pointer of a move that may turn into a drag: a touch or a pen in contact, pressed, in no drag yet.
    private PointerState? DragCandidate(in PointerInput input)
    {
        PointerState pointer = _pointers[input.PointerId];
        return pointer.Type != PointerType.Mouse && pointer.Pressing && pointer.Drag is null && (input.Buttons & PointerButtons.Left) != 0
            ? pointer
            : null;
    }

    // Whether the node, a scroll container the move bubbles through, takes the pointer's drag: the move from the press, along the
    // container's axes, is past the slop, and the container can still move that way. The press's node gets a cancel and no click,
    // and the container takes the capture. The drag starts from here: this move does not move the position (Flutter's
    // DragStartBehavior.start, monodrag.dart lines 806 to 808).
    private bool TryStartDrag(NodeHandle node, PointerState pointer, in PointerInput input)
    {
        ref readonly LayoutStyle style = ref _tree.Nodes.GetRef(node).Layout.Style;
        bool horizontal = style.OverflowX == Overflow.Scroll;
        bool vertical = style.OverflowY == Overflow.Scroll;
        if ((!horizontal && !vertical) || FindDrag(node) is not null)
        {
            return false;
        }

        float scale = _frames.Scale;
        float dx = (input.X - pointer.DownAt.X) / scale;
        float dy = (input.Y - pointer.DownAt.Y) / scale;
        float distance =
            horizontal && vertical ? MathF.Sqrt((dx * dx) + (dy * dy))
            : vertical ? Math.Abs(dy)
            : Math.Abs(dx);
        if (distance <= TouchSlop)
        {
            return false;
        }

        // A finger that moves down moves the position up.
        Vector2 offset = _tree.ScrollOffsetOf(node);
        Vector2 max = _tree.ScrollStateOf(node)?.MaxOffsetValue ?? _tree.ScrollRangeOf(node);
        bool canMove =
            (vertical && ((dy < 0 && offset.Y < max.Y) || (dy > 0 && offset.Y > 0)))
            || (horizontal && ((dx < 0 && offset.X < max.X) || (dx > 0 && offset.X > 0)));
        if (!canMove)
        {
            return false;
        }

        NodeHandle pressed = CapturedNode(pointer);
        pointer.ClickOrigin = default;
        pointer.Pressing = false;
        ClearPressed(pointer);
        if (!pressed.IsNone)
        {
            Route(PointerEventKind.Cancel, pressed, input, bubbleOnly: false);
        }

        pointer.Capture = node;
        ScrollDrag drag = _spareDrags.Count > 0 ? _spareDrags.Pop() : new ScrollDrag();
        drag.Begin(node, horizontal, vertical, input.PointerId);
        _drags.Add(drag);
        pointer.Drag = drag;
        return true;
    }

    // A pointer leaves its drag: a release, or a capture lost. The drag passes to the first finger still on it (monodrag.dart lines
    // 781 to 782); the last one's release flings the container when Flutter's conditions hold.
    private void EndDrag(PointerState pointer, in PointerInput input, bool fling)
    {
        ScrollDrag drag = pointer.Drag!;
        pointer.Drag = null;
        drag.Remove(input.PointerId);
        if (drag.Count > 0)
        {
            if (drag.Active == input.PointerId)
            {
                drag.Active = drag.First;
            }

            return;
        }

        if (fling && pointer.Tracker is { } tracker && StartFling(drag, tracker.Estimate(input.Timestamp)))
        {
            _frames.RequestFrame();
            return;
        }

        Release(drag);
    }

    // Whether the release's estimate flings the drag's container (design §7, "Touch and pen"): for one axis, its velocity past 50
    // and its offset past the slop, the velocity cut to 8,000 (monodrag.dart lines 942 to 960); for both axes, the same of their
    // lengths (PanGestureRecognizer, lines 1063 to 1077). Each axis then flings unless its velocity is under the tolerance or it is
    // at the end it moves to (ClampingScrollPhysics, scroll_physics.dart lines 442 and 914 to 922).
    private bool StartFling(ScrollDrag drag, (double VelocityX, double VelocityY, double OffsetX, double OffsetY) estimate)
    {
        (double vx, double vy, double ox, double oy) = estimate;
        if (!drag.Horizontal)
        {
            (vx, ox) = (0, 0);
        }

        if (!drag.Vertical)
        {
            (vy, oy) = (0, 0);
        }

        double speed = Math.Sqrt((vx * vx) + (vy * vy));
        if (speed <= MinFlingVelocity || Math.Sqrt((ox * ox) + (oy * oy)) <= TouchSlop)
        {
            return false;
        }

        if (drag.Horizontal && drag.Vertical)
        {
            if (speed > MaxFlingVelocity)
            {
                (vx, vy) = (vx * MaxFlingVelocity / speed, vy * MaxFlingVelocity / speed);
            }
        }
        else
        {
            vx = Math.Clamp(vx, -MaxFlingVelocity, MaxFlingVelocity);
            vy = Math.Clamp(vy, -MaxFlingVelocity, MaxFlingVelocity);
        }

        // The position moves against the finger.
        Vector2 offset = _tree.ScrollOffsetOf(drag.Container);
        Vector2 max = _tree.ScrollStateOf(drag.Container)?.MaxOffsetValue ?? _tree.ScrollRangeOf(drag.Container);
        double tolerance = 1.0 / (0.050 * _frames.Scale);
        drag.FlingX = drag.Horizontal && Flings(-vx, offset.X, max.X, tolerance);
        drag.FlingY = drag.Vertical && Flings(-vy, offset.Y, max.Y, tolerance);
        if (!drag.FlingX && !drag.FlingY)
        {
            return false;
        }

        drag.X = drag.FlingX ? new ClampingFling(offset.X, -vx) : default;
        drag.Y = drag.FlingY ? new ClampingFling(offset.Y, -vy) : default;
        drag.Flinging = true;
        drag.Started = false;
        return true;
    }

    private static bool Flings(double velocity, float position, float max, double tolerance) =>
        Math.Abs(velocity) >= tolerance && !(velocity > 0 && position >= max) && !(velocity < 0 && position <= 0);

    // A wheel or a key on a flinging container stops the fling before it scrolls (Flutter's pointerScroll goes idle,
    // scroll_position_with_single_context.dart line 224).
    private void StopFling(NodeHandle node)
    {
        if (_drags.Count > 0 && FindDrag(node) is { Flinging: true } drag)
        {
            Release(drag);
        }
    }

    private ScrollDrag? FindDrag(NodeHandle node)
    {
        foreach (ScrollDrag drag in _drags)
        {
            if (drag.Container == node)
            {
                return drag;
            }
        }

        return null;
    }

    private void Release(ScrollDrag drag)
    {
        _ = _drags.Remove(drag);
        drag.Flinging = false;
        _spareDrags.Push(drag);
    }

    // The drag of one scroll container: its axes, the pointers on it in the order they came, the one that drives it, and its fling
    // once the last is released.
    private sealed class ScrollDrag
    {
        private int[] _pointers = new int[4];

        public NodeHandle Container;
        public bool Horizontal;
        public bool Vertical;
        public int Active;
        public bool Flinging;
        public bool Started;
        public TimeSpan Start;
        public bool FlingX;
        public bool FlingY;
        public ClampingFling X;
        public ClampingFling Y;

        public int Count { get; private set; }

        public int First => _pointers[0];

        public void Begin(NodeHandle container, bool horizontal, bool vertical, int pointer)
        {
            Container = container;
            Horizontal = horizontal;
            Vertical = vertical;
            Count = 0;
            Flinging = false;
            Add(pointer);
            Active = pointer;
        }

        public void Add(int pointer)
        {
            if (Count == _pointers.Length)
            {
                Array.Resize(ref _pointers, Count * 2);
            }

            _pointers[Count++] = pointer;
        }

        public void Remove(int pointer)
        {
            int at = Array.IndexOf(_pointers, pointer, 0, Count);
            if (at < 0)
            {
                return;
            }

            Array.Copy(_pointers, at + 1, _pointers, at, Count - at - 1);
            Count--;
        }
    }
}
