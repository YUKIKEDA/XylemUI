namespace XylemUI;

/// <summary>
/// A node to lay out from, and where its constraint comes from (design §2.4.1).
/// </summary>
internal struct LayoutRequest
{
    /// <summary>
    /// The node: a dirty boundary or the root.
    /// </summary>
    public NodeHandle Node;

    /// <summary>
    /// The constraint, when the source of the request carries one (a window resize); otherwise the node's last constraint.
    /// </summary>
    public AvailableSpace? AvailableSpace;

    /// <summary>
    /// Why the node is laid out. A window resize replaces a content change for the same node.
    /// </summary>
    public LayoutReason Reason;
}
