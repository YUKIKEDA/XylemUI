namespace XylemUI;

/// <summary>
/// The focus scope of an open popup (design §7.2): the scopes are a stack over the window's, the top one the scope Tab goes round.
/// </summary>
internal struct FocusScope
{
    /// <summary>
    /// The node that has the scope: the popup's root.
    /// </summary>
    public NodeHandle Owner;

    /// <summary>
    /// The node in the scope that had the focus last, or none.
    /// </summary>
    public NodeHandle FocusedChild;

    /// <summary>
    /// The node that had the focus when the scope was pushed, which takes it back when the scope is popped.
    /// </summary>
    public NodeHandle RestoreTo;

    /// <summary>
    /// Whether the focus cannot leave the scope, and the pointer is blocked behind it.
    /// </summary>
    public bool IsModal;
}
