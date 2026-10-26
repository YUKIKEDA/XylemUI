namespace XylemUI;

/// <summary>
/// The pseudo-states of a node (design §2.13): bits that the pointer's input sets, and that choose which <see cref="PaintStyle"/>
/// the draw stage lays over the node's look. Focused and Disabled come with row K1.
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
}
