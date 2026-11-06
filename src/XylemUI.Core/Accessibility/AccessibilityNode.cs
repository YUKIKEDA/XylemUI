using System.Numerics;

namespace XylemUI;

/// <summary>
/// The id of a node of the accessibility tree (design §6): a number given when the node is made, never given again, so that a node
/// made for a node of the UI tree that reuses a freed slot has another (AccessKit's node id is 64 bits).
/// </summary>
/// <param name="Value">The number.</param>
internal readonly record struct AccessibilityNodeId(ulong Value);

/// <summary>
/// What became of a node of the accessibility tree in an update (design §6).
/// </summary>
internal enum AccessibilityChangeKind : byte
{
    /// <summary>The node was made: everything of it is new.</summary>
    Added,

    /// <summary>The node's values or children changed.</summary>
    Updated,

    /// <summary>The node went away.</summary>
    Removed,
}

/// <summary>
/// A node of the accessibility tree that an update made, changed, or took away (design §6, <c>IAccessibilityBridge</c>).
/// </summary>
/// <param name="Id">The node's id.</param>
/// <param name="Kind">What became of it.</param>
internal readonly record struct AccessibilityChange(AccessibilityNodeId Id, AccessibilityChangeKind Kind);

/// <summary>
/// The states of a node of the accessibility tree, read from its pseudo-states, its input, and its information (design §4.4.1).
/// </summary>
[Flags]
internal enum AccessibilityStates : ushort
{
    /// <summary>No state.</summary>
    None = 0,

    /// <summary>The node is disabled (its pseudo-state).</summary>
    Disabled = 1,

    /// <summary>The node holds the focus.</summary>
    Focused = 2,

    /// <summary>The node can take the focus.</summary>
    Focusable = 4,

    /// <summary>The node anchors a popup it opens.</summary>
    Expandable = 8,

    /// <summary>The node's popup is open (its pseudo-state <c>Open</c>).</summary>
    Expanded = 16,

    /// <summary>The node is checked.</summary>
    Checked = 32,

    /// <summary>The node is checked in part.</summary>
    Mixed = 64,

    /// <summary>The node is selected.</summary>
    Selected = 128,

    /// <summary>The node's value cannot be changed.</summary>
    ReadOnly = 256,

    /// <summary>The node needs a value.</summary>
    Required = 512,
}

/// <summary>
/// A node of the accessibility tree (design §4.4.1, "The shape of P02g"): what it is, for which node of the UI tree, where it is in
/// its parent's coordinates, and its children. The Platform reads it and never changes it (Invariant 10).
/// </summary>
/// <param name="id">The node's id.</param>
/// <param name="node">The node of the UI tree it stands for.</param>
internal sealed class AccessibilityNode(AccessibilityNodeId id, NodeHandle node)
{
    /// <summary>
    /// Gets the node's id.
    /// </summary>
    public AccessibilityNodeId Id { get; } = id;

    /// <summary>
    /// Gets the node of the UI tree it stands for.
    /// </summary>
    public NodeHandle Node { get; } = node;

    /// <summary>
    /// Gets or sets the node's parent; <see langword="null"/> for the root.
    /// </summary>
    public AccessibilityNode? Parent { get; set; }

    /// <summary>
    /// Gets or sets the node's role.
    /// </summary>
    public SemanticsRole Role { get; set; }

    /// <summary>
    /// Gets or sets the node's name.
    /// </summary>
    public string? Name { get; set; }

    /// <summary>
    /// Gets or sets the node's description.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Gets or sets the node's value as text.
    /// </summary>
    public string? Value { get; set; }

    /// <summary>
    /// Gets or sets the node's states.
    /// </summary>
    public AccessibilityStates States { get; set; }

    /// <summary>
    /// Gets or sets the node's value in a range.
    /// </summary>
    public SemanticsRange? Range { get; set; }

    /// <summary>
    /// Gets or sets the node's border box in its parent's coordinates: from the parent's border box, before the parent moves its
    /// children by its scroll position.
    /// </summary>
    public Rect Bounds { get; set; }

    /// <summary>
    /// Gets or sets how far the node moves its children: minus its scroll position, for a box that scrolls.
    /// </summary>
    public Vector2 ChildOffset { get; set; }

    /// <summary>
    /// Gets or sets the test ID of the node, which is its automation ID.
    /// </summary>
    public string? AutomationId { get; set; }

    /// <summary>
    /// Gets the node's children, in the order of the tree.
    /// </summary>
    public List<AccessibilityNode> Children { get; } = [];

    /// <summary>
    /// Gets or sets the number of the update that last recorded the node, so that an update records it once.
    /// </summary>
    internal int Stamp { get; set; }

    /// <summary>
    /// Gets or sets the node's depth in the tree, found when an update orders its work from the top down.
    /// </summary>
    internal int Depth { get; set; }
}
