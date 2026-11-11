namespace XylemUI;

/// <summary>
/// An action, as a handler receives it (design §7, "Shortcuts and actions"). It is routed from the focused node, or from the root
/// when no node has the focus, in the capture and the bubble phase, on the route a key takes.
/// </summary>
/// <typeparam name="TAction">The type of the action.</typeparam>
/// <remarks>
/// In the bubble phase a handler that returns has handled the action, and no handler after it is called;
/// <see cref="Propagate"/> passes it on. In the capture phase the action goes on unless a handler calls
/// <see cref="StopPropagation"/>.
/// </remarks>
public readonly struct ActionEvent<TAction>
    where TAction : class, IAction
{
    private readonly ActionDispatch _dispatch;

    /// <summary>
    /// Initializes a new instance of the <see cref="ActionEvent{TAction}"/> struct.
    /// </summary>
    /// <param name="dispatch">The dispatch of the tree the action is routed in.</param>
    /// <param name="action">The action.</param>
    /// <param name="target">The node the route starts from.</param>
    /// <param name="currentTarget">The node of the handler called now.</param>
    /// <param name="phase">The phase of the route the handler is called in.</param>
    internal ActionEvent(ActionDispatch dispatch, TAction action, NodeHandle target, NodeHandle currentTarget, EventPhase phase)
    {
        _dispatch = dispatch;
        Action = action;
        TargetNode = target;
        CurrentNode = currentTarget;
        Phase = phase;
    }

    /// <summary>
    /// Gets the action.
    /// </summary>
    public TAction Action { get; }

    /// <summary>
    /// Gets the phase of the route the handler is called in.
    /// </summary>
    public EventPhase Phase { get; }

    /// <summary>
    /// Gets the node the route starts from: the focused node, or the root.
    /// </summary>
    public Element Target => new(_dispatch.Tree, TargetNode);

    /// <summary>
    /// Gets the node of the handler called now.
    /// </summary>
    public Element CurrentTarget => new(_dispatch.Tree, CurrentNode);

    /// <summary>
    /// Gets the node of <see cref="Target"/>.
    /// </summary>
    internal NodeHandle TargetNode { get; }

    /// <summary>
    /// Gets the node of <see cref="CurrentTarget"/>.
    /// </summary>
    internal NodeHandle CurrentNode { get; }

    /// <summary>
    /// Passes the action on to the handlers after this one: the handler did not handle it. It matters in the bubble phase, where
    /// a handler that returns without it ends the route. It is called inside the handler: after the handler returned, it would
    /// change the route of whatever action is dispatched then.
    /// </summary>
    public void Propagate() => _dispatch.Propagate();

    /// <summary>
    /// Ends the route: no handler after this one is called, and the action is handled. It matters in the capture phase, where
    /// the action goes on without it. It is called inside the handler, as <see cref="Propagate"/> is.
    /// </summary>
    public void StopPropagation() => _dispatch.Stop();
}

/// <summary>
/// A handler of an action (design §7, "Shortcuts and actions").
/// </summary>
/// <typeparam name="TAction">The type of the action.</typeparam>
/// <param name="e">The action and where it is on its route.</param>
public delegate void ActionHandler<TAction>(in ActionEvent<TAction> e)
    where TAction : class, IAction;
