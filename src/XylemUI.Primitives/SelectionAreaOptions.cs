namespace XylemUI.Primitives;

/// <summary>
/// The colors of a selection area's selection (design §4.10).
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
}
