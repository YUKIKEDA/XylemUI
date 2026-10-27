namespace XylemUI;

/// <summary>
/// Text that a draw function lays out and draws itself (design §3.2, "Text from a draw function"):
/// <see cref="DrawingContext.LayoutText"/> shapes it and breaks it into lines at a width, and <see cref="DrawingContext.DrawText(TextLayout, float, float, Color)"/>
/// draws those lines. Keep one per text across frames: it holds the two caches of a text node, so that the same content, style, and
/// width allocate nothing.
/// </summary>
public sealed class TextLayout
{
    /// <summary>
    /// Initializes a new instance of the <see cref="TextLayout"/> class.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <param name="style">The style.</param>
    /// <exception cref="ArgumentOutOfRangeException">The style's size or line height is out of its range.</exception>
    /// <exception cref="ArgumentException">The style's language is the default value.</exception>
    public TextLayout(string text, TextStyle style)
    {
        ArgumentNullException.ThrowIfNull(text);
        TextStyle.Validate(in style, nameof(style));
        Leaf = new TextLeaf(style);
        _ = Leaf.SetContent(text);
    }

    /// <summary>
    /// Gets the text and its caches.
    /// </summary>
    internal TextLeaf Leaf { get; }

    /// <summary>
    /// Gets or sets a value indicating whether the lines of the current text and style are laid out, which drawing needs.
    /// </summary>
    internal bool Laid { get; set; }

    /// <summary>
    /// Replaces the text; a text equal to the one there changes nothing. Lay it out again before drawing it.
    /// </summary>
    /// <param name="text">The text.</param>
    public void SetText(ReadOnlySpan<char> text)
    {
        if (Leaf.SetContent(text))
        {
            Laid = false;
        }
    }

    /// <summary>
    /// Replaces the style; a style equal to the one there changes nothing. Lay it out again before drawing it.
    /// </summary>
    /// <param name="style">The style.</param>
    /// <exception cref="ArgumentOutOfRangeException">The size or the line height is out of its range.</exception>
    /// <exception cref="ArgumentException">The language is the default value.</exception>
    public void SetStyle(TextStyle style)
    {
        TextStyle.Validate(in style, nameof(style));
        if (Leaf.SetStyle(style))
        {
            Laid = false;
        }
    }
}
