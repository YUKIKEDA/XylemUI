namespace XylemUI;

/// <summary>
/// The pseudo-states of a node (design §2.13): bits that the input sets, and that choose which <see cref="PaintStyle"/> the draw
/// stage lays over the node's look.
/// </summary>
[Flags]
internal enum PseudoState : byte
{
    /// <summary>
    /// No pseudo-state.
    /// </summary>
    None = 0,

    /// <summary>
    /// The pointer is over the node or one of its descendants.
    /// </summary>
    Hovered = 1 << 0,

    /// <summary>
    /// The left button went down on the node or one of its descendants and is not up yet.
    /// </summary>
    Pressed = 1 << 1,

    /// <summary>
    /// The node has the focus, and the window is active.
    /// </summary>
    Focused = 1 << 2,

    /// <summary>
    /// The node is <see cref="Focused"/>, and the last input was the keyboard: the state that shows a focus ring.
    /// </summary>
    FocusVisible = 1 << 3,

    /// <summary>
    /// The node or one of its descendants is <see cref="Focused"/>.
    /// </summary>
    FocusWithin = 1 << 4,

    /// <summary>
    /// The node or an ancestor is disabled (<see cref="NodeInput.Disabled"/>): its handlers and its descendants' are not called,
    /// and it takes no focus.
    /// </summary>
    Disabled = 1 << 5,
}
