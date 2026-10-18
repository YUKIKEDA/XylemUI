namespace XylemUI;

/// <summary>
/// A node of the UI tree, stored by value in the tree's arena and linked by handles (design §2.5). S6b holds the links and the
/// layout parts; text, paint, input, and semantics join with their rows.
/// </summary>
internal struct UiNode
{
    /// <summary>
    /// The parent; none for the root.
    /// </summary>
    public NodeHandle Parent;

    /// <summary>
    /// The first child; none for a leaf.
    /// </summary>
    public NodeHandle FirstChild;

    /// <summary>
    /// The last child, so that appending does not walk the siblings; none for a leaf.
    /// </summary>
    public NodeHandle LastChild;

    /// <summary>
    /// The next sibling; none for the last child.
    /// </summary>
    public NodeHandle NextSibling;

    /// <summary>
    /// The number of ancestors: 0 for the root. Orders the layout roots, shallowest first.
    /// </summary>
    public int Depth;

    /// <summary>
    /// The node hangs from the tree's root. A node that is not attached yet (created, styled, and not appended) keeps its dirty
    /// flags but makes no layout root; appending it marks its new parent.
    /// </summary>
    public bool IsAttached;

    /// <summary>
    /// What the node and its subtree need again.
    /// </summary>
    public DirtyState Dirty;

    /// <summary>
    /// The layout part.
    /// </summary>
    public LayoutState Layout;

    /// <summary>
    /// The child whose first baseline gives this container's in the last layout run, read when a parent reads this node's
    /// (design §2.4.1); none for a leaf, whose <see cref="LayoutState.FirstBaseline"/> is its own. Read it with
    /// <see cref="LayoutState.BaselineFromFirstChildWithOne"/>, which says how the child gives the baseline. It belongs to the
    /// layout part, but sits here: the runtime does not load a struct field of <see cref="Layout"/>'s type that holds a handle
    /// of <see cref="UiNode"/> (a <see cref="TypeLoadException"/>).
    /// </summary>
    public NodeHandle BaselineChild;

    /// <summary>
    /// The versions that validate cached results.
    /// </summary>
    public NodeVersions Versions;
}
