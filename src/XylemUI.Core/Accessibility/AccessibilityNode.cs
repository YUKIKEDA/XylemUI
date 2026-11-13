using System.Numerics;
using System.Runtime.InteropServices;

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
internal enum AccessibilityStates : uint
{
    /// <summary>No state.</summary>
    None = 0,

    /// <summary>The node is disabled (its pseudo-state).</summary>
    Disabled = 1,

    /// <summary>The node holds the focus.</summary>
    Focused = 2,

    /// <summary>The node can take the focus.</summary>
    Focusable = 4,

    /// <summary>
    /// The node opens and closes: it anchors a popup it opens, or the app's states say so (<see cref="SemanticsStates.Expandable"/>).
    /// </summary>
    Expandable = 8,

    /// <summary>The node is open: its popup (its pseudo-state <c>Open</c>), or the app's states say so.</summary>
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

    /// <summary>The node is a text field of many lines (AccessKit's <c>MultilineTextInput</c>).</summary>
    Multiline = 1024,

    /// <summary>The node is a password field (AccessKit's <c>PasswordInput</c>).</summary>
    Password = 2048,

    /// <summary>The node has a click handler of its own, in the bubble phase: assistive technology can click it.</summary>
    Clickable = 4096,

    /// <summary>The node scrolls and clips its children: AccessKit leaves out the nodes outside its box.</summary>
    ClipsChildren = 8192,

    /// <summary>The node's value is not valid.</summary>
    Invalid = 16384,

    /// <summary>More than one of the node's items can be selected.</summary>
    Multiselectable = 32768,

    /// <summary>The node is changing.</summary>
    Busy = 65536,

    /// <summary>The node keeps the rest of the window from being used.</summary>
    Modal = 131072,

    /// <summary>The node has a handler of assistive technology's Increment (<see cref="ElementExtensions.OnIncrement"/>).</summary>
    Increments = 262144,

    /// <summary>The node has a handler of assistive technology's Decrement (<see cref="ElementExtensions.OnDecrement"/>).</summary>
    Decrements = 524288,

    /// <summary>The node has a handler of a number assistive technology sets (<see cref="ElementExtensions.OnSetRangeValue"/>).</summary>
    SetsRangeValue = 1048576,

    /// <summary>The node has a handler of assistive technology's Expand or Collapse (<see cref="ElementExtensions.OnExpand"/>).</summary>
    ExpandsByHandler = 2097152,
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
    /// Gets or sets the text a text field shows while it is empty.
    /// </summary>
    public string? Placeholder { get; set; }

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
    /// Gets or sets the largest scroll position of a box that scrolls, or zero for a node that does not.
    /// </summary>
    public Size ScrollRange { get; set; }

    /// <summary>
    /// Gets or sets the node's place in its set, from 0 as AccessKit counts it: the app's <see cref="AccessibilityInfo.PositionInSet"/>
    /// less 1, else a virtualized list's row's place; -1 for none.
    /// </summary>
    public int PositionInSet { get; set; } = -1;

    /// <summary>
    /// Gets or sets the number of the items of the set the node holds: the app's <see cref="AccessibilityInfo.SizeOfSet"/>, else the
    /// number of a virtualized list's items, for its box's node; -1 for none.
    /// </summary>
    public int SizeOfSet { get; set; } = -1;

    /// <summary>
    /// Gets or sets the node's level, from 0 as AccessKit counts it: the app's <see cref="AccessibilityInfo.Level"/> less 1; -1 for
    /// none.
    /// </summary>
    public int Level { get; set; } = -1;

    /// <summary>
    /// Gets or sets how a change of the node's name is read out: the app's or its part's, else its role's own;
    /// <see cref="SemanticsLive.Inherit"/> when none, which AccessKit takes from the node above.
    /// </summary>
    public SemanticsLive Live { get; set; }

    /// <summary>
    /// Gets or sets which way the node's items run; <see cref="SemanticsOrientation.None"/> for none given.
    /// </summary>
    public SemanticsOrientation Orientation { get; set; }

    /// <summary>
    /// Gets the nodes that name this one (<see cref="ElementExtensions.LabelledBy"/>), those in the tree, in the order given;
    /// <see langword="null"/> for a node that never had one.
    /// </summary>
    public List<AccessibilityNodeId>? LabelledBy { get; private set; }

    /// <summary>
    /// Gets the nodes that describe this one (<see cref="ElementExtensions.DescribedBy"/>), those in the tree, in the order given;
    /// <see langword="null"/> for a node that never had one.
    /// </summary>
    public List<AccessibilityNodeId>? DescribedBy { get; private set; }

    /// <summary>
    /// Gets the nodes this one controls (<see cref="ElementExtensions.Controls"/>), those in the tree, in the order given;
    /// <see langword="null"/> for a node that never had one.
    /// </summary>
    public List<AccessibilityNodeId>? Controls { get; private set; }

    /// <summary>
    /// Gets or sets the node that holds this one's error message (<see cref="ElementExtensions.ErrorMessage"/>), while it is in the
    /// tree; <c>default</c> for none.
    /// </summary>
    public AccessibilityNodeId ErrorMessage { get; set; }

    /// <summary>
    /// Gets or sets the node that is active inside this one, which holds the focus (<c>ActiveDescendant</c>), while it is in the
    /// tree; <c>default</c> for none.
    /// </summary>
    public AccessibilityNodeId ActiveDescendant { get; set; }

    /// <summary>
    /// Gets the nodes a draw function adds to this node (<see cref="ElementExtensions.AccessibleChildren"/>), which come after its
    /// children and its link nodes.
    /// </summary>
    public List<AccessibilityNode> Synthetic { get; } = [];

    /// <summary>
    /// Gets or sets the key a draw function gave the node, for a node it added (<see cref="AccessibleChildren"/>);
    /// <see langword="null"/> for another node.
    /// </summary>
    public int? ChildKey { get; set; }

    /// <summary>
    /// Gets or sets the place of a node a draw function added in its parent's list of them, which tells when the list changed.
    /// </summary>
    internal int SyntheticIndex { get; set; }

    /// <summary>
    /// Gets or sets the index of the link in its text leaf's ranges, for the node of a link range, or -1 for another node.
    /// </summary>
    public int LinkIndex { get; set; } = -1;

    /// <summary>
    /// Gets the nodes of a text leaf's link ranges, which come after its children (design §4.8).
    /// </summary>
    public List<AccessibilityNode> Links { get; } = [];

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

    /// <summary>
    /// Writes the ids of the nodes of a relation; returns whether they changed.
    /// </summary>
    /// <param name="kind">The relation.</param>
    /// <param name="ids">The ids, in order; empty for none.</param>
    /// <returns>Whether the node's ids of the relation changed.</returns>
    internal bool SetRelation(AccessibilityRelation kind, ReadOnlySpan<AccessibilityNodeId> ids)
    {
        List<AccessibilityNodeId>? list = kind switch
        {
            AccessibilityRelation.LabelledBy => LabelledBy,
            AccessibilityRelation.DescribedBy => DescribedBy,
            _ => Controls,
        };
        if (list is null ? ids.IsEmpty : CollectionsMarshal.AsSpan(list).SequenceEqual(ids))
        {
            return false;
        }

        if (list is null)
        {
            list = [];
            switch (kind)
            {
                case AccessibilityRelation.LabelledBy:
                    LabelledBy = list;
                    break;
                case AccessibilityRelation.DescribedBy:
                    DescribedBy = list;
                    break;
                default:
                    Controls = list;
                    break;
            }
        }

        list.Clear();
        list.AddRange(ids);
        return true;
    }
}

/// <summary>
/// A relation of a node of the accessibility tree that names other nodes (design §4.4.1, "Relations").
/// </summary>
internal enum AccessibilityRelation : byte
{
    /// <summary>The nodes that name the node.</summary>
    LabelledBy,

    /// <summary>The nodes that describe the node.</summary>
    DescribedBy,

    /// <summary>The nodes the node controls.</summary>
    Controls,
}
