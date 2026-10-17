namespace XylemUI;

/// <summary>
/// What a node and its subtree need again (design §2.4.1).
/// </summary>
internal struct DirtyState
{
    /// <summary>
    /// The stages the node itself needs: set only on the node where a change started.
    /// </summary>
    public DirtyFlags SelfDirty;

    /// <summary>
    /// The stages needed somewhere in the subtree, the node included: set on the path from the change to its boundary.
    /// </summary>
    public DirtyFlags SubtreeDirty;

    /// <summary>
    /// Advanced each time the node is marked. A layout that started at another epoch leaves the flags for the next cycle.
    /// </summary>
    public ulong Epoch;
}
