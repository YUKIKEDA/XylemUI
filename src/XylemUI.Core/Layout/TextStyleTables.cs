namespace XylemUI;

/// <summary>
/// The text styles the containers of a tree give the text leaves below them (design §3.2, "The text style a container gives"), in
/// an object of its own that the tree makes when a container first gets one: a tree with none, and a box with none, pay nothing
/// but the tree's reference to it.
/// </summary>
/// <remarks>
/// The styles are kept by the node's handle, as the scroll states and the test IDs are, and not in <see cref="UiNode"/> or
/// <see cref="NodeVisual"/>: few nodes hold one (a themed part's row, a container the app styles), and every node or every node
/// with a look would grow by a reference for them. The node's <see cref="UiNode.TextStyleMarks"/> says whether it is in the table,
/// so a walk looks up only the nodes that hold one. A freed node's entries are taken out with it (<see cref="Forget"/>), so a slot
/// used again starts with none.
/// </remarks>
internal sealed class TextStyleTables
{
    /// <summary>
    /// Gets the text style each container holds, by the container: the fields it set and their values.
    /// </summary>
    public Dictionary<NodeHandle, InheritedTextStyle> Held { get; } = [];

    /// <summary>
    /// Gets what each subtree taken out from under a container that gives a text style had taken from its ancestors, by the root of
    /// the subtree: its text leaves keep those values until it is put under a parent again, where the subtree is resolved again only
    /// when the new ancestors give other values. A row that a list takes out and puts back in the same place is not walked again.
    /// </summary>
    public Dictionary<NodeHandle, InheritedTextStyle> Detached { get; } = [];

    /// <summary>
    /// Gets the line height each text leaf set itself, in logical pixels, by the leaf: only the leaves that set one pay for it, and
    /// every text leaf stays as large as before row TS1 (Issue #960). Read when the leaf's <see cref="UiNode.TextOwnFields"/> has
    /// <see cref="TextStyleFields.LineHeight"/>; a leaf whose own line height is the multiple in its style, from a whole style written
    /// to it, has no entry.
    /// </summary>
    public Dictionary<NodeHandle, float> OwnLineHeights { get; } = [];

    /// <summary>
    /// Gets or sets the number of text leaves resolved again (<see cref="UiTree.TextLeavesResolved"/>).
    /// </summary>
    public long Resolved { get; set; }

    /// <summary>
    /// Takes a freed node out of the tables.
    /// </summary>
    /// <param name="node">The node.</param>
    /// <param name="holds">Whether the node held a text style.</param>
    public void Forget(NodeHandle node, bool holds)
    {
        if (holds)
        {
            _ = Held.Remove(node);
        }

        if (Detached.Count > 0)
        {
            _ = Detached.Remove(node);
        }

        if (OwnLineHeights.Count > 0)
        {
            _ = OwnLineHeights.Remove(node);
        }
    }
}
