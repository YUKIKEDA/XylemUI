namespace XylemUI;

/// <summary>
/// What a node of the tree has to do with the text styles containers give (design §3.2, "The text style a container gives").
/// </summary>
[Flags]
internal enum TextStyleMarks : byte
{
    /// <summary>
    /// Neither the node nor an ancestor holds a text style.
    /// </summary>
    None = 0,

    /// <summary>
    /// The node holds a text style for the text leaves below it, kept in the tree's table (<see cref="TextStyleTables.Held"/>).
    /// </summary>
    Holds = 1 << 0,

    /// <summary>
    /// The node or an ancestor holds a text style, or the node is in a subtree taken out from under one and not put back yet
    /// (<see cref="TextStyleTables.Detached"/>). A node put under a parent without it takes nothing, and the walk up from a node
    /// stops at the first node without it.
    /// </summary>
    Inside = 1 << 1,
}
