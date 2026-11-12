namespace XylemUI;

/// <summary>
/// Routes the actions of one tree (design §7, "Shortcuts and actions"): from a node to the root on the route a key takes, the
/// capture phase from the root down and then the bubble phase from the node up.
/// </summary>
/// <remarks>
/// A dispatch allocates nothing: the route is kept in an array that grows and is used again, and a handler that dispatches another
/// action uses the part of the array after its own route.
/// </remarks>
internal sealed class ActionDispatch
{
    private NodeHandle[] _route = new NodeHandle[16];
    private int _routeCount;

    // Set by a handler: the bubble phase goes on after it (Propagate), or the route ends at it (Stop).
    private bool _propagate;
    private bool _stopped;

    // Counts the changes that can change whether an action is available; made when an availability is first read, so a tree
    // that reads none pays nothing for the changes.
    private Signal<int>? _epoch;
    private int _epochValue;

    // Counts the moves of the focus and the changes of the window's activation, apart from the rest: a reader at a node it
    // names, as a menu's item at its context node, has nothing to read again when the focus moves (row KM2c: 10 items read their
    // keys again on each frame that moved the focus in their menu, 880 bytes a frame).
    private Signal<int>? _focusEpoch;
    private int _focusEpochValue;

    // Counts the popups that open and start to leave, for popup::Dismiss alone: counted with the rest, a menu that opens made
    // each of its items read a second time.
    private Signal<int>? _popupEpoch;
    private int _popupEpochValue;

    /// <summary>
    /// Initializes a new instance of the <see cref="ActionDispatch"/> class.
    /// </summary>
    /// <param name="tree">The tree whose actions are routed.</param>
    public ActionDispatch(UiTree tree) => Tree = tree;

    /// <summary>
    /// Gets the tree whose actions are routed.
    /// </summary>
    public UiTree Tree { get; }

    /// <summary>
    /// Gets or sets what the tree does for an action no handler on the route took (design §7, "The default keys and
    /// actions"); it returns whether the action is handled. The window's input sets it.
    /// </summary>
    public Func<NodeHandle, IAction, bool>? Fallback { get; set; }

    /// <summary>
    /// Gets or sets what answers whether <see cref="Fallback"/> would handle an action now.
    /// </summary>
    public Func<NodeHandle, IAction, bool>? FallbackCan { get; set; }

    /// <summary>
    /// Routes an action from a node.
    /// </summary>
    /// <param name="target">The node the route starts from.</param>
    /// <param name="action">The action.</param>
    /// <returns>Whether a handler handled the action.</returns>
    public bool Dispatch(NodeHandle target, IAction action)
    {
        if (target.IsNone || !Tree.Nodes.IsAlive(target))
        {
            return false;
        }

        Type type = action.GetType();
        int start = _routeCount;
        bool stopped = _stopped;
        bool propagate = _propagate;
        _stopped = false;
        ReactiveRuntime.EnterBatch();
        try
        {
            int end = Collect(target);
            for (int i = end - 1; i >= start && !_stopped; i--)
            {
                Invoke(EventPhase.Capture, type, action, target, _route[i]);
            }

            for (int i = start; i < end && !_stopped; i++)
            {
                Invoke(EventPhase.Bubble, type, action, target, _route[i]);
            }

            return _stopped || (Fallback is { } fallback && fallback(target, action));
        }
        finally
        {
            _routeCount = start;
            _stopped = stopped;
            _propagate = propagate;
            ReactiveRuntime.ExitBatch();
        }
    }

    /// <summary>
    /// Returns whether a dispatch of an action of a type from a node would find a handler: an enabled node of the route has one, in
    /// either phase.
    /// </summary>
    /// <param name="target">The node the route would start from.</param>
    /// <param name="type">The type of the action.</param>
    /// <returns>Whether a handler would be called.</returns>
    public bool IsAvailable(NodeHandle target, Type type)
    {
        if (target.IsNone || !Tree.Nodes.IsAlive(target))
        {
            return false;
        }

        for (NodeHandle node = target; !node.IsNone; node = Tree.KeyParent(node))
        {
            if (EnabledInput(node) is not { } input)
            {
                continue;
            }

            foreach (ActionHandlerEntry entry in input.ActionHandlers)
            {
                if (entry.Handles(type))
                {
                    return true;
                }
            }
        }

        // The tree takes an action of its own with no handler; whether it changes anything then is not asked here.
        return Fallback is not null && type.IsSubclassOf(typeof(CoreAction));
    }

    /// <summary>
    /// Returns whether a dispatch of an action from a node would be handled now (design §7, "Menu items that name actions"): an
    /// enabled node of the route has a handler for it that can take this action, or the tree's own handling would change
    /// something.
    /// </summary>
    /// <param name="target">The node the route would start from.</param>
    /// <param name="action">The action.</param>
    /// <returns>Whether the action would be handled.</returns>
    public bool IsAvailable(NodeHandle target, IAction action)
    {
        if (target.IsNone || !Tree.Nodes.IsAlive(target))
        {
            return false;
        }

        Type type = action.GetType();
        for (NodeHandle node = target; !node.IsNone; node = Tree.KeyParent(node))
        {
            if (EnabledInput(node) is not { } input)
            {
                continue;
            }

            foreach (ActionHandlerEntry entry in input.ActionHandlers)
            {
                if (entry.Handles(type) && entry.CanHandle(action))
                {
                    return true;
                }
            }
        }

        return FallbackCan is { } can && can(target, action);
    }

    /// <summary>
    /// Reads the count of the changes that can change whether an action is available, so that the reading computation runs again
    /// after the next one.
    /// </summary>
    /// <param name="atFocus">Whether the route that is read starts from the focused node, so that a move of the focus changes it.</param>
    public void TrackAvailability(bool atFocus = true)
    {
        _ = (_epoch ??= Signal.Create(0)).Get();
        if (atFocus)
        {
            TrackFocus();
        }
    }

    /// <summary>
    /// Reads the count of the moves of the focus, so that the reading computation runs again after the next one.
    /// </summary>
    public void TrackFocus() => _ = (_focusEpoch ??= Signal.Create(0)).Get();

    /// <summary>
    /// Records a change that can change whether an action is available on a route, wherever it starts: a node's handlers, context,
    /// or disabled state, the links of the tree, or the node a route starts from with no focus.
    /// </summary>
    public void AvailabilityChanged()
    {
        _epoch?.Set(unchecked(++_epochValue));
    }

    /// <summary>
    /// Reads the count of the popups that opened or started to leave, so that the reading computation runs again after the next.
    /// </summary>
    public void TrackPopups() => _ = (_popupEpoch ??= Signal.Create(0)).Get();

    /// <summary>
    /// Records that a popup opened or started to leave: whether there is one to close is another answer.
    /// </summary>
    public void PopupsChanged()
    {
        _popupEpoch?.Set(unchecked(++_popupEpochValue));
    }

    /// <summary>
    /// Records a move of the focus or a change of the window's activation: the routes that start from the focused node are others.
    /// </summary>
    public void FocusChanged()
    {
        _focusEpoch?.Set(unchecked(++_focusEpochValue));
    }

    /// <summary>
    /// Lets the bubble phase go on after the handler that is called now.
    /// </summary>
    internal void Propagate() => _propagate = true;

    /// <summary>
    /// Ends the route at the handler that is called now.
    /// </summary>
    internal void Stop() => _stopped = true;

    // Appends the route from target to the root, and returns its end.
    private int Collect(NodeHandle target)
    {
        // A popup's actions go on to its anchor and the anchor's ancestors, as its keys do (design §7.3, "The route of events").
        for (NodeHandle node = target; !node.IsNone; node = Tree.KeyParent(node))
        {
            if (_routeCount == _route.Length)
            {
                Array.Resize(ref _route, _routeCount * 2);
            }

            _route[_routeCount++] = node;
        }

        return _routeCount;
    }

    // Calls the handlers of one node for the action's type and the phase.
    private void Invoke(EventPhase phase, Type type, IAction action, NodeHandle target, NodeHandle node)
    {
        if (EnabledInput(node) is not { } input)
        {
            return;
        }

        foreach (ActionHandlerEntry entry in input.ActionHandlers)
        {
            if (entry.Phase != phase || !entry.Handles(type))
            {
                continue;
            }

            ScopeRef previous = default;
            if (!entry.Scope.IsNone && !Scope.TryEnter(entry.Scope, out previous))
            {
                continue;
            }

            // The capture phase goes on unless the handler stops it; the bubble phase ends unless the handler passes it on.
            _propagate = phase == EventPhase.Capture;
            try
            {
                entry.Invoke(this, action, target, node);
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

            if (!_propagate)
            {
                _stopped = true;
            }

            if (_stopped)
            {
                return;
            }
        }
    }

    // The input of a node that is alive and not disabled; null otherwise. A disabled node's handlers are not called, as its key
    // handlers are not.
    private NodeInput? EnabledInput(NodeHandle node)
    {
        if (!Tree.Nodes.IsAlive(node))
        {
            return null;
        }

        ref UiNode n = ref Tree.Nodes.GetRef(node);
        return (n.Pseudo & PseudoState.Disabled) == 0 ? n.Input : null;
    }
}

/// <summary>
/// An action handler of a node: the type of the action and the phase it is called for.
/// </summary>
internal abstract class ActionHandlerEntry
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ActionHandlerEntry"/> class.
    /// </summary>
    /// <param name="actionType">The type of the action.</param>
    /// <param name="phase">The phase of the route.</param>
    /// <param name="handler">The handler's delegate.</param>
    /// <param name="scope">The scope the handler runs in.</param>
    /// <param name="derived">Whether the handler is also called for the types derived from the action's.</param>
    protected ActionHandlerEntry(Type actionType, EventPhase phase, Delegate handler, ScopeRef scope, bool derived)
    {
        ActionType = actionType;
        Derived = derived;
        Phase = phase;
        Handler = handler;
        Scope = scope;
    }

    /// <summary>
    /// Gets the type of the action.
    /// </summary>
    public Type ActionType { get; }

    /// <summary>
    /// Gets a value indicating whether the handler is also called for the types derived from <see cref="ActionType"/>: a part
    /// with many actions of one base type, as a text field, has one handler for them all (design §7, "The default keys and
    /// actions").
    /// </summary>
    public bool Derived { get; }

    /// <summary>
    /// Returns whether the handler is called for an action of a type.
    /// </summary>
    /// <param name="type">The type of the action that is dispatched.</param>
    /// <returns>Whether the type is the handler's, or derived from it when the handler takes those.</returns>
    public bool Handles(Type type) => ActionType == type || (Derived && type.IsSubclassOf(ActionType));

    /// <summary>
    /// Gets the phase of the route.
    /// </summary>
    public EventPhase Phase { get; }

    /// <summary>
    /// Gets the handler's delegate, which a removal compares.
    /// </summary>
    public Delegate Handler { get; }

    /// <summary>
    /// Gets the scope the handler runs in.
    /// </summary>
    public ScopeRef Scope { get; }

    /// <summary>
    /// Calls the handler.
    /// </summary>
    /// <param name="dispatch">The dispatch that routes the action.</param>
    /// <param name="action">The action, of <see cref="ActionType"/>.</param>
    /// <param name="target">The node the route starts from.</param>
    /// <param name="node">The node of the handler.</param>
    public abstract void Invoke(ActionDispatch dispatch, IAction action, NodeHandle target, NodeHandle node);

    /// <summary>
    /// Returns whether the handler can take an action of its type now; a handler that was given nothing to answer with can.
    /// </summary>
    /// <param name="action">The action.</param>
    /// <returns>Whether the handler can take it.</returns>
    public abstract bool CanHandle(IAction action);
}

/// <summary>
/// An action handler of a node, with the type of its action.
/// </summary>
/// <typeparam name="TAction">The type of the action.</typeparam>
internal sealed class ActionHandlerEntry<TAction> : ActionHandlerEntry
    where TAction : class, IAction
{
    private readonly ActionHandler<TAction> _handler;
    private readonly Func<TAction, bool>? _can;

    /// <summary>
    /// Initializes a new instance of the <see cref="ActionHandlerEntry{TAction}"/> class.
    /// </summary>
    /// <param name="phase">The phase of the route.</param>
    /// <param name="handler">The handler.</param>
    /// <param name="scope">The scope the handler runs in.</param>
    /// <param name="derived">Whether the handler is also called for the types derived from the action's.</param>
    /// <param name="can">What answers whether the handler can take an action now; <see langword="null"/> when it always can.</param>
    public ActionHandlerEntry(EventPhase phase, ActionHandler<TAction> handler, ScopeRef scope, bool derived = false, Func<TAction, bool>? can = null)
        : base(typeof(TAction), phase, handler, scope, derived)
    {
        _handler = handler;
        _can = can;
    }

    /// <inheritdoc/>
    public override bool CanHandle(IAction action) => _can is null || _can((TAction)action);

    /// <inheritdoc/>
    public override void Invoke(ActionDispatch dispatch, IAction action, NodeHandle target, NodeHandle node)
    {
        var e = new ActionEvent<TAction>(dispatch, (TAction)action, target, node, Phase);
        _handler(in e);
    }
}
