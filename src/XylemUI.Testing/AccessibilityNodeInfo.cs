namespace XylemUI.Testing;

/// <summary>
/// What assistive technology reads of a node (design §12.5, <see cref="HeadlessApp.AccessibilityOf"/>).
/// </summary>
/// <param name="Role">The node's role.</param>
/// <param name="Name">The node's name: its own, the text of the nodes that name it, its part's, or the text of its content.</param>
/// <param name="Description">The node's description, with the text of its error message and of the nodes that describe it.</param>
/// <param name="Value">The node's value as text.</param>
/// <param name="States">The states only the app knows.</param>
/// <param name="IsDisabled">Whether the node is disabled.</param>
/// <param name="IsFocused">Whether the node holds the focus.</param>
/// <param name="IsFocusable">Whether the node can take the focus.</param>
/// <param name="IsExpanded">
/// Whether the node is open: from its popup for a popup's anchor, else from its states; <see langword="null"/> for a node that does
/// not open.
/// </param>
/// <param name="Range">The node's value in a range.</param>
/// <param name="Bounds">The node's border box in the window, in logical pixels, with the scroll positions above it.</param>
/// <param name="ChildCount">The number of the node's children in the accessibility tree.</param>
/// <param name="Live">How a change of the node's name is read out, its role's or its parent's when it gives none; Off when none does.</param>
/// <param name="Orientation">Which way the node's items run.</param>
/// <param name="Level">The node's level, from 1; 0 for none.</param>
/// <param name="PositionInSet">The node's place in its set, from 1; 0 for none.</param>
/// <param name="SizeOfSet">The number of the items of the set the node holds; 0 for none.</param>
/// <param name="LabelledBy">The nodes that name the node, those in the tree.</param>
/// <param name="DescribedBy">The nodes that describe the node, those in the tree.</param>
/// <param name="Controls">The nodes the node controls, those in the tree.</param>
/// <param name="ErrorMessage">The node of the node's error message, while it is in the tree; <c>default</c> for none.</param>
/// <param name="ActiveDescendant">
/// The node active inside the node, while it is in the tree; <c>default</c> for none, and for a node a draw function added.
/// </param>
public sealed record AccessibilityNodeInfo(
    SemanticsRole Role,
    string? Name,
    string? Description,
    string? Value,
    SemanticsStates States,
    bool IsDisabled,
    bool IsFocused,
    bool IsFocusable,
    bool? IsExpanded,
    SemanticsRange? Range,
    Rect Bounds,
    int ChildCount,
    SemanticsLive Live,
    SemanticsOrientation Orientation,
    int Level,
    int PositionInSet,
    int SizeOfSet,
    IReadOnlyList<Element> LabelledBy,
    IReadOnlyList<Element> DescribedBy,
    IReadOnlyList<Element> Controls,
    Element ErrorMessage,
    Element ActiveDescendant
);
