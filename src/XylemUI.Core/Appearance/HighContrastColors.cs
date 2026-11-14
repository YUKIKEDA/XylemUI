namespace XylemUI;

/// <summary>
/// The eight system colors of a high-contrast theme that Windows 10 and 11 support (design §13.2, "OS の見た目"): the colors the user
/// chose, which a theme puts in place of its own.
/// </summary>
/// <param name="Window">The background of a window (Windows' <c>COLOR_WINDOW</c>, CSS's <c>Canvas</c>).</param>
/// <param name="WindowText">The text on <paramref name="Window"/> (<c>COLOR_WINDOWTEXT</c>, <c>CanvasText</c>).</param>
/// <param name="Hotlight">The text of a link (<c>COLOR_HOTLIGHT</c>, <c>LinkText</c>).</param>
/// <param name="GrayText">The text of a disabled part (<c>COLOR_GRAYTEXT</c>, <c>GrayText</c>).</param>
/// <param name="Highlight">The background of a selected or hovered part (<c>COLOR_HIGHLIGHT</c>, <c>Highlight</c>).</param>
/// <param name="HighlightText">The text on <paramref name="Highlight"/> (<c>COLOR_HIGHLIGHTTEXT</c>, <c>HighlightText</c>).</param>
/// <param name="ButtonFace">The background of a button (<c>COLOR_3DFACE</c>, <c>ButtonFace</c>).</param>
/// <param name="ButtonText">The text on <paramref name="ButtonFace"/> (<c>COLOR_BTNTEXT</c>, <c>ButtonText</c>).</param>
public readonly record struct HighContrastColors(
    Color Window,
    Color WindowText,
    Color Hotlight,
    Color GrayText,
    Color Highlight,
    Color HighlightText,
    Color ButtonFace,
    Color ButtonText
);
