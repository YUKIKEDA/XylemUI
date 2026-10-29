using System.Numerics;

namespace XylemUI;

// A touchpad's pan (row I1c, design §7, "Touch and pen"): the platform hands DirectManipulation's two-finger pan over as a start,
// updates, and an end, at the cursor where it started. It scrolls by the rules of a touch's drag (row I1b): a scroll container on the
// route takes it past 18 logical pixels along its axes, and its end flings the container with the pan's velocity. Unlike a touch,
// it presses nothing, changes no hover, and keeps no input from the nodes inside while it pans or flings (Flutter's
// shouldIgnorePointer is false for a trackpad, scroll_activity.dart line 538).
internal sealed partial class WindowInput
{
    /// <summary>
    /// Moves the platform's gestures one frame on (<see cref="IPointerPlatform.UpdateGestures"/>): the platform delivers the
    /// touchpad's pan inputs that came since the last frame, which may scroll.
    /// </summary>
    /// <param name="frameTime">The frame's time (<see cref="WindowFrames.FrameTime"/>).</param>
    /// <returns>Whether a gesture goes on and asks for the next frame.</returns>
    internal bool UpdateGestures(TimeSpan frameTime) => _platform.UpdateGestures(frameTime);

    // The pan starts where the cursor is, and its route stays there (Flutter tests the start's position only,
    // gestures/binding.dart lines 407 to 438). The flings of the containers under it stop (scrollable.dart lines 863 to 866), and the
    // velocity starts again from the start's 0.
    private void PanStart(PointerState pointer, in PointerInput input)
    {
        pointer.Inside = true;
        pointer.Last = input;
        pointer.Hit = HitAt(input);
        pointer.PanTotal = Vector2.Zero;
        VelocityTracker tracker = pointer.Tracker ??= new VelocityTracker();
        tracker.Reset();
        tracker.Add(input.Timestamp, 0, 0);
        if (_drags.Count > 0)
        {
            for (NodeHandle node = pointer.Hit; !node.IsNone; node = _tree.Nodes.GetRef(node).Parent)
            {
                StopFling(node);
            }
        }

        Route(PointerEventKind.PanStart, pointer.Hit, input, bubbleOnly: false);
    }

    // An update of the pan: the velocity's position is the pan's total since its start (monodrag.dart lines 661 to 666); the route,
    // whose bubble lets a container take the pan; and, for a pan a container took before this update, the container's position by
    // the update's amount, against it. The update that starts a drag moves nothing, as a touch's.
    private void PanUpdate(PointerState pointer, in PointerInput input)
    {
        bool driving = pointer.Drag is { Flinging: false };
        pointer.PanTotal += input.Delta;
        pointer.Tracker?.Add(input.Timestamp, pointer.PanTotal.X, pointer.PanTotal.Y);
        Route(PointerEventKind.PanUpdate, Target(pointer), input, bubbleOnly: false);
        if (driving && pointer.Drag is { } drag)
        {
            Vector2 moved = input.Delta;
            _ = ScrollBy(drag.Container, new Vector2(drag.Horizontal ? -moved.X : 0, drag.Vertical ? -moved.Y : 0));
        }
    }

    // The fingers left: the end goes where the updates went, a container that took the pan flings by the rules of a touch's
    // release, and the pointer's number goes back to the platform's.
    private void PanEnd(PointerState pointer, in PointerInput input)
    {
        Route(PointerEventKind.PanEnd, Target(pointer), input, bubbleOnly: false);
        if (pointer.Drag is not null)
        {
            EndDrag(pointer, input, fling: true);
        }

        pointer.Capture = default;
        pointer.Inside = false;
        pointer.Hit = default;
        if (pointer.Live)
        {
            pointer.Live = false;
            _livePointers--;
        }
    }

    // The pointer of a pan update that may turn into a scroll: a touchpad's pan that no container took yet.
    private PointerState? PanCandidate(in PointerInput input)
    {
        PointerState pointer = _pointers[input.PointerId];
        return pointer.Type == PointerType.Touchpad && pointer.Drag is null ? pointer : null;
    }

    // Whether the node, a scroll container the update bubbles through, takes the pan: the pan's total along the container's axes is
    // past the slop, as a touch's move from its press (Flutter's computeHitSlop gives a trackpad the touch's 18, gestures/events.dart
    // lines 2552 to 2562), and the container can still move that way.
    private bool TryStartPan(NodeHandle node, PointerState pointer, in PointerInput input)
    {
        if (!TakeScroll(node, pointer, input, pointer.PanTotal.X, pointer.PanTotal.Y, cancelPress: false))
        {
            return false;
        }

        pointer.Drag!.FromTouchpad = true;
        return true;
    }
}
