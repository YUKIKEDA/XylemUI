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
    /// Gets the area's context menu (design §4.10): the panel of a menu, made from the commands of the area each time the menu opens,
    /// at the pointer on the up of the right button, and at the end of the selection on Shift+F10 or the application key.
    /// <see langword="null"/> for no menu.
    /// </summary>
    public Func<SelectionMenuCommands, Element>? ContextMenu { get; init; }
}
