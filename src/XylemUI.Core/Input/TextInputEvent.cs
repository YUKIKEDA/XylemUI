namespace XylemUI;

/// <summary>
/// Text typed into the window, as a handler receives it (design §4.9, "Text input on Windows"). It is routed from the focused node,
/// or from the root when no node has the focus, in the capture and the bubble phase, as a <see cref="KeyEvent"/> is. Each handler
/// gets its own copy, whose <see cref="CurrentTarget"/> is its node's.
/// </summary>
public readonly struct TextInputEvent
{
    private readonly WindowInput _input;

    /// <summary>
    /// Initializes a new instance of the <see cref="TextInputEvent"/> struct.
    /// </summary>
    /// <param name="input">The input of the window the event is routed in.</param>
    /// <param name="target">The node the route starts from.</param>
    /// <param name="currentTarget">The node of the handler called now.</param>
    /// <param name="source">The input the platform gave.</param>
    internal TextInputEvent(WindowInput input, NodeHandle target, NodeHandle currentTarget, in TextInput source)
    {
        _input = input;
        TargetNode = target;
        CurrentNode = currentTarget;
        Text = source.Text;
        Timestamp = source.Timestamp;
    }

    /// <summary>
    /// Gets the node the route starts from: the focused node, or the root when the window is not active or no node has the focus.
    /// </summary>
    public Element Target => new(_input.Tree, TargetNode);

    /// <summary>
    /// Gets the node of the handler called now.
    /// </summary>
    public Element CurrentTarget => new(_input.Tree, CurrentNode);

    /// <summary>
    /// Gets the text: one or more whole code points (a surrogate pair is never split), with no control character. A Tab is not text;
    /// it moves the focus (design §7.1).
    /// </summary>
    public string Text { get; }

    /// <summary>
    /// Gets when the platform received the input.
    /// </summary>
    public TimeSpan Timestamp { get; }

    /// <summary>
    /// Gets the node of <see cref="Target"/>.
    /// </summary>
    internal NodeHandle TargetNode { get; }

    /// <summary>
    /// Gets the node of <see cref="CurrentTarget"/>.
    /// </summary>
    internal NodeHandle CurrentNode { get; }

    /// <summary>
    /// Stops the event: no handler after this one is called, in this phase or the next.
    /// </summary>
    public void StopPropagation() => _input.StopPropagation();
}

/// <summary>
/// Handles text typed into the window (design §4.9).
/// </summary>
/// <param name="e">The event.</param>
public delegate void TextInputHandler(in TextInputEvent e);
