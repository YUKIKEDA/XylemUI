namespace XylemUI;

/// <summary>
/// The keys that select the menu bar or leave it (design §13.1, "The menu bar"), as the window hands them to
/// <see cref="UiTree.MenuBarKeys"/> after no handler stopped them.
/// </summary>
internal enum MenuBarKey : byte
{
    /// <summary>
    /// Alt went down: it leaves a menu bar that is selected or has a menu open.
    /// </summary>
    AltDown,

    /// <summary>
    /// Alt went up with no other key or press while it was down: it selects the menu bar.
    /// </summary>
    AltUp,

    /// <summary>
    /// F10 went down with no modifier: it selects the menu bar, or leaves it.
    /// </summary>
    F10,
}
