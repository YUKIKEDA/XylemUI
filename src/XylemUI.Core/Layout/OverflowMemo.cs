namespace XylemUI;

/// <summary>
/// The right and bottom edges of a container's scrollable overflow rectangle, as <see cref="LayoutEngine.ScrollableOverflowOf"/>
/// last found them, and whether they still hold (design §3.4.1, "the mark that makes a remembered value stale"). A layout that
/// commits the node marks them stale, and so does a layout root below the node, up to a scroll container; only finding them
/// again clears the mark.
/// </summary>
/// <remarks>
/// They sit in an array of their own, indexed by the node's slot, not in <see cref="MeasureCache"/>: there they made the cache 592
/// bytes a node instead of 576 (row V3b).
/// </remarks>
internal struct OverflowMemo
{
    /// <summary>
    /// The right edge, from the node's padding box's corner.
    /// </summary>
    public float Right;

    /// <summary>
    /// The bottom edge, from the same corner.
    /// </summary>
    public float Bottom;

    /// <summary>
    /// A value indicating whether <see cref="Right"/> and <see cref="Bottom"/> hold for the node's last committed layout and its
    /// subtree's. A new node, and a node in a slot used again, starts with <see langword="false"/>.
    /// </summary>
    public bool Known;
}
