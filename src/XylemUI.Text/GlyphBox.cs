namespace XylemUI.Text;

/// <summary>
/// The box of a glyph's ink, in whole pixels, relative to the pen on the baseline.
/// </summary>
/// <param name="Left">The left edge, right of the pen.</param>
/// <param name="Top">The top edge, above the baseline.</param>
/// <param name="Width">The width; 0 for a glyph with no ink.</param>
/// <param name="Height">The height; 0 for a glyph with no ink.</param>
/// <param name="IsColor">
/// Whether the glyph is a color glyph: the atlas keeps it as <see cref="GlyphCoverage.Color"/>, whatever kind it was asked for in.
/// </param>
/// <param name="UsesTextColor">Whether the color glyph has a layer painted in the text's color.</param>
public readonly record struct GlyphBox(int Left, int Top, int Width, int Height, bool IsColor = false, bool UsesTextColor = false);
