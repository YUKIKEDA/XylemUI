namespace XylemUI.Primitives;

/// <summary>
/// What a text field does beyond holding text (design §4.9): one line or many, the selection the app shares, Enter, the
/// placeholder, read-only, hidden text, the most clusters it holds, and the colors of its caret and its selection.
/// </summary>
public sealed record TextFieldOptions
{
    /// <summary>
    /// Gets a value indicating whether the field holds many lines: it wraps them and scrolls down, and Enter breaks a line. A field of
    /// one line scrolls sideways and keeps a pasted text's first line. <see langword="false"/> by default.
    /// </summary>
    public bool Multiline { get; init; }

    /// <summary>
    /// Gets the selection the app shares with the field, written both ways; <see langword="null"/> for one the field keeps.
    /// </summary>
    public Signal<TextSelection>? Selection { get; init; }

    /// <summary>
    /// Gets what Enter runs in a field of one line, with the text: the key is then stopped, and the focus stays. With none, Enter goes
    /// on to the field's ancestors.
    /// </summary>
    public Action<string>? OnSubmit { get; init; }

    /// <summary>
    /// Gets the text shown, at half the opacity, while the field is empty; <see langword="null"/> for none.
    /// </summary>
    public string? Placeholder { get; init; }

    /// <summary>
    /// Gets a value indicating whether the text cannot be changed: it can still be selected and copied.
    /// </summary>
    public bool ReadOnly { get; init; }

    /// <summary>
    /// Gets a value indicating whether the text is hidden, one ● for each grapheme cluster: it is not copied or cut, and a move by
    /// word goes to the end.
    /// </summary>
    public bool Password { get; init; }

    /// <summary>
    /// Gets the most grapheme clusters the text holds; <see langword="null"/> for no limit. Typing and pasting are cut to fit.
    /// </summary>
    public int? MaxLength { get; init; }

    /// <summary>
    /// Gets the caret's color; <see langword="null"/> for the color of the field's text.
    /// </summary>
    public Color? CaretColor { get; init; }

    /// <summary>
    /// Gets the selection's background; <see langword="null"/> for the OS's (Windows' <c>COLOR_HIGHLIGHT</c>).
    /// </summary>
    public Color? SelectionBackground { get; init; }

    /// <summary>
    /// Gets the selected text's color; <see langword="null"/> for the OS's (Windows' <c>COLOR_HIGHLIGHTTEXT</c>).
    /// </summary>
    public Color? SelectionForeground { get; init; }
}
