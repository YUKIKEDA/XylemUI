using System.Numerics;

namespace XylemUI;

/// <summary>
/// The focus of one window's tree (design §7, "Keys and the focus", and §7.2): the focused node, whether the window is active, and
/// whether the last input was the keyboard. It keeps the focus pseudo-states, routes the focus events, and moves the focus in the
/// order of Tab (§7.1). Row K1 has the one scope of the root; the scopes of dialogs and popups come with row P02.
/// </summary>
/// <remarks>
/// It belongs to the UI thread, as the tree does, which makes it (<see cref="UiTree.Focus"/>). Each change runs as one batch. The
/// routes are an array it reuses: the steady state allocates nothing.
/// </remarks>
internal sealed class FocusManager
{
    // A handler that keeps moving the focus from the focus events is not followed past a few moves (as the window's rehit).
    private const int PendingMoves = 4;

    // The nodes of the routes being delivered, innermost first; a route delivered from a handler goes after the one around it.
    private NodeHandle[] _route = new NodeHandle[16];
    private int _routeCount;
    private bool _stopped;

    // Whether focus events are being sent, and the move a handler asked for meanwhile, which waits for them to end.
    private bool _sending;
    private bool _hasPending;
    private NodeHandle _pending;

    // The node Tab or Focus moved the focus to, which the next frame scrolls into view.
    private NodeHandle _reveal;

    /// <summary>
    /// Initializes a new instance of the <see cref="FocusManager"/> class.
    /// </summary>
    /// <param name="tree">The tree.</param>
    public FocusManager(UiTree tree) => Tree = tree;

    /// <summary>
    /// Gets the tree.
    /// </summary>
    public UiTree Tree { get; }

    /// <summary>
    /// Gets the focused node, kept while the window is not active; none when no node has the focus.
    /// </summary>
    public NodeHandle Focused { get; private set; }

    /// <summary>
    /// Gets a value indicating whether the window is active. Only <see cref="SetActive"/> changes it: Core reads no state of the OS
    /// (design §12.4). A window is not active until the platform says so.
    /// </summary>
    public bool IsWindowActive { get; private set; }

    /// <summary>
    /// Gets a value indicating whether the last input was the keyboard: a key's down sets it, and the pointer's down and move clear
    /// it. The focused node is focus-visible while it is set.
    /// </summary>
    public bool LastInputWasKeyboard { get; private set; }

    /// <summary>
    /// Gets the node the keys are routed from: the focused node while the window is active, the root otherwise.
    /// </summary>
    public NodeHandle KeyTarget => IsWindowActive && Tree.Nodes.IsAlive(Focused) ? Focused : Tree.Root;

    /// <summary>
    /// Returns whether a node can take the focus: it is focusable, in the window's tree, and neither it nor an ancestor is hidden.
    /// </summary>
    /// <param name="node">The node.</param>
    /// <returns>Whether it can take the focus.</returns>
    public bool CanFocus(NodeHandle node)
    {
        if (!Tree.Nodes.IsAlive(node))
        {
            return false;
        }

        ref UiNode n = ref Tree.Nodes.GetRef(node);
        if (!n.IsAttached || n.Input is not { Focusable: true } || (n.Pseudo & PseudoState.Disabled) != 0)
        {
            return false;
        }

        for (NodeHandle at = node; !at.IsNone; at = Tree.Nodes.GetRef(at).Parent)
        {
            if (Tree.Nodes.GetRef(at).Layout.Style.Hidden)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Gives a node the focus (<see cref="ElementExtensions.Focus"/>); while the window is not active, the node is kept and takes the
    /// focus when it is active again. A move asked for while the focus events are sent waits for them to end.
    /// </summary>
    /// <param name="node">The node.</param>
    /// <returns>Whether the node can take the focus; nothing changes when it cannot.</returns>
    public bool Focus(NodeHandle node)
    {
        if (!CanFocus(node))
        {
            return false;
        }

        Request(node);
        AskToReveal(node);
        return true;
    }

    /// <summary>
    /// Scrolls the focused node into view when a move from Tab or <see cref="Focus"/> asked for it (design §7, "Bring into view"):
    /// from the node up, each scroll container moves its position on the axes that scroll (<see cref="Overflow.Hidden"/> or
    /// <see cref="Overflow.Scroll"/>) by the least that brings the node's border box into its viewport, or puts the node's start at
    /// the viewport's start when the node is larger; the containers outside go on from the position the inner one moved to. The
    /// frame calls it after the layout pass has written the ranges (design §3.4.1).
    /// </summary>
    public void RevealPending()
    {
        NodeHandle node = _reveal;
        _reveal = default;
        if (node.IsNone || node != Focused || !Tree.Nodes.IsAlive(node))
        {
            return;
        }

        Rect rect = Tree.Nodes.GetRef(node).Layout.Rect;
        var start = new Vector2(rect.X, rect.Y);
        var size = new Vector2(rect.Width, rect.Height);
        for (NodeHandle parent = Tree.Nodes.GetRef(node).Parent; !parent.IsNone; parent = Tree.Nodes.GetRef(parent).Parent)
        {
            // start is in the parent's border box, before the parent's own scroll position.
            ref readonly LayoutState layout = ref Tree.Nodes.GetRef(parent).Layout;
            Overflow x = layout.Style.OverflowX;
            Overflow y = layout.Style.OverflowY;
            Vector2 origin = new(layout.Rect.X, layout.Rect.Y);
            if (x >= Overflow.Hidden || y >= Overflow.Hidden)
            {
                var edge = new Vector2(layout.Border.Left, layout.Border.Top);
                Size viewport = Tree.ViewportOf(parent);
                Vector2 offset = Tree.ScrollOffsetOf(parent);
                var next = new Vector2(
                    x >= Overflow.Hidden ? Reveal(start.X, size.X, offset.X, edge.X, viewport.Width) : offset.X,
                    y >= Overflow.Hidden ? Reveal(start.Y, size.Y, offset.Y, edge.Y, viewport.Height) : offset.Y
                );
                next = Vector2.Clamp(next, Vector2.Zero, Tree.ScrollRangeOf(parent));
                if (next != offset)
                {
                    _ = Tree.ScrollStateFor(parent).SetOffset(next);
                }

                start -= next;
            }

            start += origin;
        }
    }

    // The scroll position that shows [start, start + length) of the content in the viewport [view, view + viewLength): unchanged
    // when it shows already, the least move otherwise, and the start at the viewport's start for a node larger than the viewport.
    private static float Reveal(float start, float length, float offset, float view, float viewLength)
    {
        float shown = start - offset;
        if (length > viewLength || shown < view)
        {
            return start - view;
        }

        return shown + length > view + viewLength ? start + length - view - viewLength : offset;
    }

    // Asks the next frame to scroll the node into view, once the move is done (Tab and Focus; not a press, design §7).
    private void AskToReveal(NodeHandle node)
    {
        _reveal = node;
        Tree.NoteWork();
    }

    /// <summary>
    /// Gives the focus to the first node that can take it from a pressed node up to the root, before the press is routed (design
    /// §7, "Focus on a press"). A press where no node can take it leaves the focus, and so does one that reaches a node that keeps the
    /// focus on a press before a node that can take it, that node itself included: a focusable button with the mark does not take the
    /// focus (design §4.9, "A press that keeps the focus").
    /// </summary>
    /// <param name="pressed">The node the press is routed from.</param>
    public void FocusFromPress(NodeHandle pressed)
    {
        for (NodeHandle node = pressed; !node.IsNone; node = Tree.Nodes.GetRef(node).Parent)
        {
            if (Tree.Nodes.GetRef(node).Input is { KeepsFocusOnPress: true })
            {
                return;
            }

            if (CanFocus(node))
            {
                Request(node);
                return;
            }
        }
    }

    /// <summary>
    /// Moves the focus in the order of Tab (design §7.1): to the first node after the focused one, or before it, in the order of
    /// (<see cref="NodeInput.TabIndex"/>, the tree's order), past the end to the other end. With no focus, from the first or the
    /// last node. One walk of the tree that skips hidden subtrees; it allocates nothing.
    /// </summary>
    /// <param name="backward">Whether to move to the node before (Shift+Tab).</param>
    /// <returns>Whether a node can take the focus; with none, nothing changes.</returns>
    public bool MoveInTabOrder(bool backward)
    {
        NodeHandle root = Tree.Root;
        if (root.IsNone)
        {
            return false;
        }

        NodeHandle current = Tree.Nodes.IsAlive(Focused) && CanFocus(Focused) ? Focused : default;
        int currentIndex = current.IsNone ? 0 : Tree.Nodes.GetRef(current).Input!.TabIndex;
        bool passed = false;
        NodeHandle best = default;
        int bestIndex = 0;
        NodeHandle end = default;
        int endIndex = 0;
        NodeHandle node = root;
        while (!node.IsNone)
        {
            ref UiNode n = ref Tree.Nodes.GetRef(node);
            if (!n.Layout.Style.Hidden && (n.Pseudo & PseudoState.Disabled) == 0)
            {
                if (n.Input is { Focusable: true } input)
                {
                    int index = input.TabIndex;
                    bool isCurrent = node == current;
                    passed |= isCurrent;
                    if (!backward)
                    {
                        // The smallest pair after the current one; the first of the smallest index past the end.
                        bool after = index > currentIndex || (index == currentIndex && passed && !isCurrent);
                        if (after && (best.IsNone || index < bestIndex))
                        {
                            (best, bestIndex) = (node, index);
                        }

                        if (end.IsNone || index < endIndex)
                        {
                            (end, endIndex) = (node, index);
                        }
                    }
                    else
                    {
                        // The largest pair before the current one; the last of the largest index past the start.
                        bool before = index < currentIndex || (index == currentIndex && !passed);
                        if (before && (best.IsNone || index >= bestIndex))
                        {
                            (best, bestIndex) = (node, index);
                        }

                        if (end.IsNone || index >= endIndex)
                        {
                            (end, endIndex) = (node, index);
                        }
                    }
                }

                if (!n.FirstChild.IsNone)
                {
                    node = n.FirstChild;
                    continue;
                }
            }

            node = NextAfterSubtree(node, root);
        }

        NodeHandle next = current.IsNone || best.IsNone ? end : best;
        if (next.IsNone)
        {
            return false;
        }

        Request(next);
        AskToReveal(next);
        return true;
    }

    /// <summary>
    /// Sets whether the window is active (design §7, "The window's activation"): when it stops being active, the focused node gets a
    /// focus-out and loses its focus states but is kept; when it is active again, the node kept takes them back with a focus-in. A
    /// node kept that is removed, hidden, or made not focusable meanwhile empties the focus then (<see cref="Lose"/>), with no event.
    /// </summary>
    /// <param name="active">Whether the window is active.</param>
    public void SetActive(bool active)
    {
        if (active == IsWindowActive)
        {
            return;
        }

        ReactiveRuntime.EnterBatch();
        try
        {
            IsWindowActive = active;
            NodeHandle focused = Focused;
            if (!active)
            {
                if (Tree.Nodes.IsAlive(focused))
                {
                    SetStates(focused, on: false, keepWithin: default);
                    Send(FocusEventKind.FocusOut, focused);
                }
            }
            else if (Tree.Nodes.IsAlive(focused))
            {
                // A node that stops being able to take the focus loses it at once (Lose), active or not, so the node kept can.
                SetStates(focused, on: true, keepWithin: default);
                Send(FocusEventKind.FocusIn, focused);
            }
        }
        finally
        {
            ReactiveRuntime.ExitBatch();
        }
    }

    /// <summary>
    /// Records whether the last input was the keyboard, and shows or hides the focused node's ring with it.
    /// </summary>
    /// <param name="keyboard">Whether the input was the keyboard.</param>
    public void SetLastInputWasKeyboard(bool keyboard)
    {
        if (keyboard == LastInputWasKeyboard)
        {
            return;
        }

        LastInputWasKeyboard = keyboard;
        if (IsWindowActive && Tree.Nodes.IsAlive(Focused))
        {
            Tree.SetPseudoState(Focused, PseudoState.FocusVisible, keyboard);
        }
    }

    /// <summary>
    /// Empties the focus when the focused node is <paramref name="node"/> or one of its descendants, with a focus-out from the
    /// focused node while the window is active: the node is being removed or hidden, or stopped being focusable (design §7, "Removed,
    /// hidden, or disabled"). The keys then go to the root, and the next Tab starts from the first node.
    /// </summary>
    /// <param name="node">The node removed, hidden, or no longer focusable.</param>
    public void Lose(NodeHandle node)
    {
        NodeHandle focused = Focused;
        if (!Tree.Nodes.IsAlive(focused) || !IsSelfOrDescendant(focused, node))
        {
            return;
        }

        if (_hasPending && IsSelfOrDescendant(_pending, node))
        {
            _hasPending = false;
        }

        ReactiveRuntime.EnterBatch();
        try
        {
            Focused = default;
            if (IsWindowActive)
            {
                SetStates(focused, on: false, keepWithin: default);
                Send(FocusEventKind.FocusOut, focused);
            }
        }
        finally
        {
            ReactiveRuntime.ExitBatch();
        }
    }

    /// <summary>
    /// Stops the focus event being delivered (<see cref="FocusEvent.StopPropagation"/>).
    /// </summary>
    internal void StopPropagation() => _stopped = true;

    // The node after a subtree in the tree's order: the next sibling of the node or of its nearest ancestor that has one, below the
    // root; none at the end.
    private NodeHandle NextAfterSubtree(NodeHandle node, NodeHandle root)
    {
        while (node != root)
        {
            ref UiNode n = ref Tree.Nodes.GetRef(node);
            if (!n.NextSibling.IsNone)
            {
                return n.NextSibling;
            }

            node = n.Parent;
        }

        return default;
    }

    // Moves the focus to a node that can take it, now or after the focus events being sent.
    private void Request(NodeHandle node)
    {
        if (_sending)
        {
            _pending = node;
            _hasPending = true;
            return;
        }

        ReactiveRuntime.EnterBatch();
        try
        {
            MoveTo(node);
            for (int again = 0; _hasPending && again < PendingMoves; again++)
            {
                _hasPending = false;
                if (CanFocus(_pending))
                {
                    MoveTo(_pending);
                }
            }

            _hasPending = false;
        }
        finally
        {
            ReactiveRuntime.ExitBatch();
        }
    }

    // The move itself: the states and the signals change first, then the old node gets its focus-out and the new one its focus-in,
    // while the window is active (Avalonia's KeyboardDevice: LostFocus, then GotFocus).
    private void MoveTo(NodeHandle node)
    {
        NodeHandle previous = Focused;
        if (node == previous)
        {
            return;
        }

        Focused = node;
        if (!IsWindowActive)
        {
            return;
        }

        bool hadPrevious = Tree.Nodes.IsAlive(previous);
        if (hadPrevious)
        {
            SetStates(previous, on: false, keepWithin: node);
        }

        SetStates(node, on: true, keepWithin: default);
        if (hadPrevious)
        {
            Send(FocusEventKind.FocusOut, previous);
        }

        if (Tree.Nodes.IsAlive(node) && Focused == node)
        {
            Send(FocusEventKind.FocusIn, node);
        }
    }

    // Sets or clears the focus states of a node and the focus within of its ancestors, and its IsFocused signal. Clearing keeps the
    // focus within of the ancestors of keepWithin, the node that takes the focus, so that their looks are not marked twice.
    private void SetStates(NodeHandle node, bool on, NodeHandle keepWithin)
    {
        Tree.SetPseudoState(node, PseudoState.Focused, on);
        Tree.SetPseudoState(node, PseudoState.FocusVisible, on && LastInputWasKeyboard);
        for (NodeHandle at = node; !at.IsNone; at = Tree.Nodes.GetRef(at).Parent)
        {
            if (on || keepWithin.IsNone || !IsSelfOrDescendant(keepWithin, at))
            {
                Tree.SetPseudoState(at, PseudoState.FocusWithin, on);
            }
        }

        Tree.FocusSignalOf(node)?.Set(on);
    }

    // Routes a focus event from a node: the capture phase from the root down, then the bubble phase back up, until a handler stops
    // it. Moves of the focus asked for meanwhile wait (design §7, "Focus events", as the window's rehit, PR #327's review).
    private void Send(FocusEventKind kind, NodeHandle target)
    {
        int start = _routeCount;
        bool stopped = _stopped;
        bool sending = _sending;
        _stopped = false;
        _sending = true;
        try
        {
            for (NodeHandle node = target; !node.IsNone; node = Tree.Nodes.GetRef(node).Parent)
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
                Invoke(kind, EventPhase.Capture, target, _route[i]);
            }

            for (int i = start; i < end && !_stopped; i++)
            {
                Invoke(kind, EventPhase.Bubble, target, _route[i]);
            }
        }
        finally
        {
            _routeCount = start;
            _stopped = stopped;
            _sending = sending;
        }
    }

    // Calls the focus handlers of one node for the kind and phase, in the scope each was added in; an exception goes to the handler
    // of unhandled exceptions, and the next handler is called (as the pointer's).
    private void Invoke(FocusEventKind kind, EventPhase phase, NodeHandle target, NodeHandle node)
    {
        // A disabled node's handlers and its descendants' are not called (design §7, "Disabled").
        if (!Tree.Nodes.IsAlive(node))
        {
            return;
        }

        ref UiNode n = ref Tree.Nodes.GetRef(node);
        if (n.Input is not { } input || (n.Pseudo & PseudoState.Disabled) != 0)
        {
            return;
        }

        foreach (FocusHandlerEntry entry in input.FocusHandlers)
        {
            if (entry.Kind != kind || entry.Phase != phase)
            {
                continue;
            }

            ScopeRef previous = default;
            if (!entry.Scope.IsNone && !Scope.TryEnter(entry.Scope, out previous))
            {
                continue;
            }

            var e = new FocusEvent(this, kind, target, node);
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

    // Whether node is ancestor itself or one of its descendants.
    private bool IsSelfOrDescendant(NodeHandle node, NodeHandle ancestor)
    {
        for (; !node.IsNone; node = Tree.Nodes.GetRef(node).Parent)
        {
            if (node == ancestor)
            {
                return true;
            }
        }

        return false;
    }
}
