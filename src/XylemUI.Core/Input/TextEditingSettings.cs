namespace XylemUI;

/// <summary>
/// The OS's settings a text field draws its caret and its selection with (design §4.9, "The caret" and "Drawing the selection"):
/// the host reads them from the OS and writes them again when they change; a headless app takes fixed values (design §12.2).
/// </summary>
/// <param name="CaretBlink">The time of each half of the caret's blink; <see langword="null"/> for a caret that does not blink
/// (Windows' <c>GetCaretBlinkTime</c>, <c>INFINITE</c> for none).</param>
/// <param name="CaretWidth">The caret's width in logical pixels (Windows' <c>SPI_GETCARETWIDTH</c>).</param>
/// <param name="SelectionBackground">The selection's background (Windows' <c>COLOR_HIGHLIGHT</c>).</param>
/// <param name="SelectionForeground">The selected text's color (Windows' <c>COLOR_HIGHLIGHTTEXT</c>).</param>
/// <param name="KeyboardCues">Whether access keys are always underlined (Windows' <c>SPI_GETKEYBOARDCUES</c>); when not, they are
/// underlined after Alt and in a menu opened from the keys (design §13.1, "Access keys").</param>
/// <param name="DragWidth">How far a mouse's press may move across, in physical pixels, before a drag starts (Windows'
/// <c>SM_CXDRAG</c>, design §7, "Drag and drop").</param>
/// <param name="DragHeight">How far it may move up or down (Windows' <c>SM_CYDRAG</c>).</param>
internal readonly record struct TextEditingSettings(
    TimeSpan? CaretBlink,
    float CaretWidth,
    Color SelectionBackground,
    Color SelectionForeground,
    bool KeyboardCues = false,
    int DragWidth = 4,
    int DragHeight = 4
)
{
    /// <summary>
    /// Gets the values of a tree no host has set: Windows' defaults (a blink of 530 ms, a caret 1 wide) and the selection's colors of
    /// Windows 11's default theme.
    /// </summary>
    public static TextEditingSettings Default { get; } = new(TimeSpan.FromMilliseconds(530), 1, Color.FromRgb(0x0078D7), Color.FromRgb(0xFFFFFF));
}
