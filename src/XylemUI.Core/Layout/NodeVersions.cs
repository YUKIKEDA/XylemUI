namespace XylemUI;

/// <summary>
/// Counters that tell whether a cached result of a node is still valid (design §2.4, §11.5.2).
/// </summary>
internal struct NodeVersions
{
    /// <summary>
    /// Advanced when the layout style changes.
    /// </summary>
    public ulong LayoutStyle;

    /// <summary>
    /// Advanced when a child is added, removed, or moved.
    /// </summary>
    public ulong Children;

    /// <summary>
    /// Advanced when the node's own rectangle changes.
    /// </summary>
    public ulong Layout;

    /// <summary>
    /// Advanced when the rectangle of a node in the subtree changes, up to the nearest layout boundary.
    /// </summary>
    public ulong DescendantLayout;
}
