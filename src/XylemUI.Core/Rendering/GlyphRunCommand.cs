using XylemUI.Text;

namespace XylemUI;

/// <summary>
/// A run of glyphs in one color, read from a <see cref="DisplayList"/>. The coverage of each glyph, from the glyph atlas, is
/// the alpha of the color over the target: one alpha per pixel (grayscale) or one per subpixel (ClearType, design §5.0.3).
/// </summary>
/// <param name="color">The color of the text.</param>
/// <param name="glyphs">The glyphs; the span is valid until the display list is written again.</param>
/// <param name="coverage">The kind of the glyphs' coverage, which says among which atlas pages their pages are.</param>
public readonly ref struct GlyphRunCommand(Color color, ReadOnlySpan<GlyphInstance> glyphs, GlyphCoverage coverage)
{
    /// <summary>
    /// Gets the color of the text.
    /// </summary>
    public Color Color { get; } = color;

    /// <summary>
    /// Gets the glyphs, in the order they were written.
    /// </summary>
    public ReadOnlySpan<GlyphInstance> Glyphs { get; } = glyphs;

    /// <summary>
    /// Gets the kind of the glyphs' coverage.
    /// </summary>
    public GlyphCoverage Coverage { get; } = coverage;
}
