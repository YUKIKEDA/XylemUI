namespace XylemUI.Text;

/// <summary>
/// How a glyph's coverage is drawn and kept (design §5.0.3, roadmap rows T2 and T6).
/// </summary>
/// <remarks>
/// Bilevel glyphs (roadmap row T8) are not a kind of their own: they are kept on the pages and drawn in the draws of
/// <see cref="Gray"/> or <see cref="Color"/>, so that turning font smoothing off adds no kind of page and leaves the atlas's memory
/// bound as it is (design §5.0.3, "When fonts are not smoothed"). <see cref="GlyphKey.Aliased"/> says a glyph is bilevel; with
/// <see cref="Rgb"/> it is refused.
/// </remarks>
public enum GlyphCoverage : byte
{
    /// <summary>One value per pixel, the alpha of the text's color (grayscale anti-aliasing), one byte per pixel.</summary>
    Gray,

    /// <summary>
    /// One value per subpixel, red, green, and blue in that order (ClearType), four bytes per pixel with the fourth unused. It is
    /// drawn only over an opaque target: each channel blends on its own.
    /// </summary>
    Rgb,

    /// <summary>
    /// The glyph's own colors (a color glyph, an emoji), premultiplied blue, green, red, and alpha, four bytes per pixel. The
    /// text's color only scales it by its alpha, and gives the color of the layers the font paints in the text's color.
    /// </summary>
    Color,
}
