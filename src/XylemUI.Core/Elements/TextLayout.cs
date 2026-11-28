namespace XylemUI;

/// <summary>
/// Text that a draw function lays out and draws itself (design §3.2, "Text from a draw function"):
/// <see cref="DrawingContext.LayoutText"/> shapes it and breaks it into lines at a width, and <see cref="DrawingContext.DrawText(TextLayout, float, float)"/>
/// draws those lines in the style's color. Keep one per text across frames: it holds the two caches of a text node, so that the same
/// content, style, and width allocate nothing.
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
        Leaf = new TextLeaf(in style);
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
    /// Replaces the style; a style equal to the one there changes nothing. A style whose font, size, language, or line height
    /// differs is laid out again before it is drawn; one whose color alone differs keeps its lines and their caches, and is drawn
    /// in the new color.
    /// </summary>
    /// <param name="style">The style.</param>
    /// <exception cref="ArgumentOutOfRangeException">The size or the line height is out of its range.</exception>
    /// <exception cref="ArgumentException">The language is the default value.</exception>
    public void SetStyle(TextStyle style)
    {
        TextStyle.Validate(in style, nameof(style));
        if (Leaf.SetStyle(in style) == TextStyleChange.Layout)
        {
            Laid = false;
        }
    }
}
