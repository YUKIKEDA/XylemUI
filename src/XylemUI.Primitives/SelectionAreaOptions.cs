namespace XylemUI.Primitives;

/// <summary>
/// The colors of a selection area's selection, and its context menu (design §4.10).
/// </summary>
public sealed record SelectionAreaOptions
{
    /// <summary>
    /// Gets the selection's background; <see langword="null"/> for the OS's (Windows' <c>COLOR_HIGHLIGHT</c>).
    /// </summary>
    public Color? SelectionBackground { get; init; }

    /// <summary>
    /// Gets the selected text's color; <see langword="null"/> for the OS's (Windows' <c>COLOR_HIGHLIGHTTEXT</c>).
    /// </summary>
    public Color? SelectionForeground { get; init; }

    /// <summary>
    /// Gets the area's context menu (design §4.10): the panel of a menu, made each time the menu opens, at the pointer on the up of
    /// the right button, and at the end of the selection on Shift+F10 or the application key. Its items name the area's actions
    /// (<see cref="Primitive.MenuItem(IAction, Func{Computed{KeyBinding}, Element})"/> with <see cref="TextActions.Copy"/> and
    /// <see cref="TextActions.SelectAll"/>): the area answers whether it can take each now. <see langword="null"/> for no menu.
    /// </summary>
    public Func<Element>? ContextMenu { get; init; }
}
