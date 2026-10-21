namespace XylemUI.Text;

/// <summary>
/// How a glyph's coverage is drawn and kept (design §5.0.3, roadmap row T2).
/// </summary>
public enum GlyphCoverage : byte
{
    /// <summary>One value per pixel, the alpha of the text's color (grayscale anti-aliasing), one byte per pixel.</summary>
    Gray,

    /// <summary>
    /// One value per subpixel, red, green, and blue in that order (ClearType), four bytes per pixel with the fourth unused. It is
    /// drawn only over an opaque target: each channel blends on its own.
    /// </summary>
    Rgb,
}
