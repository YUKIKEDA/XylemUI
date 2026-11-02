namespace XylemUI.Testing;

/// <summary>
/// What assistive technology reads of a node (design §12.5, <see cref="HeadlessApp.AccessibilityOf"/>).
/// </summary>
/// <param name="Role">The node's role.</param>
/// <param name="Name">The node's name: its own, its part's, or the text of its content.</param>
/// <param name="Description">The node's description.</param>
/// <param name="Value">The node's value as text.</param>
/// <param name="States">The states only the app knows.</param>
/// <param name="IsDisabled">Whether the node is disabled.</param>
/// <param name="IsFocused">Whether the node holds the focus.</param>
/// <param name="IsFocusable">Whether the node can take the focus.</param>
/// <param name="IsExpanded">Whether the node's popup is open; <see langword="null"/> for a node that anchors no popup.</param>
/// <param name="Range">The node's value in a range.</param>
/// <param name="Bounds">The node's border box in the window, in logical pixels, with the scroll positions above it.</param>
/// <param name="ChildCount">The number of the node's children in the accessibility tree.</param>
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
    int ChildCount
);
