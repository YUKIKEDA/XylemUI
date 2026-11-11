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

            return _stopped;
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
                if (entry.ActionType == type)
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Reads the count of the changes that can change whether an action is available, so that the reading computation runs again
    /// after the next one.
    /// </summary>
    public void TrackAvailability() => _ = (_epoch ??= Signal.Create(0)).Get();

    /// <summary>
    /// Records a change that can change whether an action is available: the focus, the window's activation, a node's handlers or
    /// disabled state, or the links of the tree.
    /// </summary>
    public void AvailabilityChanged()
    {
        _epoch?.Set(unchecked(++_epochValue));
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
            if (entry.ActionType != type || entry.Phase != phase)
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
    protected ActionHandlerEntry(Type actionType, EventPhase phase, Delegate handler, ScopeRef scope)
    {
        ActionType = actionType;
        Phase = phase;
        Handler = handler;
        Scope = scope;
    }

    /// <summary>
    /// Gets the type of the action.
    /// </summary>
    public Type ActionType { get; }

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
}

/// <summary>
/// An action handler of a node, with the type of its action.
/// </summary>
/// <typeparam name="TAction">The type of the action.</typeparam>
internal sealed class ActionHandlerEntry<TAction> : ActionHandlerEntry
    where TAction : class, IAction
{
    private readonly ActionHandler<TAction> _handler;

    /// <summary>
    /// Initializes a new instance of the <see cref="ActionHandlerEntry{TAction}"/> class.
    /// </summary>
    /// <param name="phase">The phase of the route.</param>
    /// <param name="handler">The handler.</param>
    /// <param name="scope">The scope the handler runs in.</param>
    public ActionHandlerEntry(EventPhase phase, ActionHandler<TAction> handler, ScopeRef scope)
        : base(typeof(TAction), phase, handler, scope) => _handler = handler;

    /// <inheritdoc/>
    public override void Invoke(ActionDispatch dispatch, IAction action, NodeHandle target, NodeHandle node)
    {
        var e = new ActionEvent<TAction>(dispatch, (TAction)action, target, node, Phase);
        _handler(in e);
    }
}
