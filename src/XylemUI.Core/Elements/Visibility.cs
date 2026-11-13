namespace XylemUI;

/// <summary>
/// Whether a node and its subtree are shown while the node keeps its place in the layout (design §3.2, "Nodes not shown"; CSS's
/// <c>visibility</c>, GPUI's <c>Visibility</c>).
/// </summary>
public enum Visibility : byte
{
    /// <summary>
    /// The node is shown, unless an ancestor is hidden. The default.
    /// </summary>
    Visible,

    /// <summary>
    /// The node keeps its place and its size in the layout, and it and its whole subtree are neither drawn nor hit by the pointer,
    /// take no focus, and are left out of the accessibility tree. A descendant cannot show itself again.
    /// </summary>
    Hidden,
}
