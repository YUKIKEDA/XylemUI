namespace XylemUI;

/// <summary>
/// A run of glyphs in one color, read from a <see cref="DisplayList"/>. The coverage of each glyph, from the glyph atlas, is
/// the alpha of the color over the target (grayscale anti-aliasing, design §5.0.3).
/// </summary>
/// <param name="color">The color of the text.</param>
/// <param name="glyphs">The glyphs; the span is valid until the display list is written again.</param>
public readonly ref struct GlyphRunCommand(Color color, ReadOnlySpan<GlyphInstance> glyphs)
{
    /// <summary>
    /// Gets the color of the text.
    /// </summary>
    public Color Color { get; } = color;

    /// <summary>
    /// Gets the glyphs, in the order they were written.
    /// </summary>
    public ReadOnlySpan<GlyphInstance> Glyphs { get; } = glyphs;
}
