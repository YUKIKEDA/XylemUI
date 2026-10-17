namespace XylemUI.Text;

/// <summary>
/// Where a rasterized glyph is in a <see cref="GlyphAtlas"/>, and how it sits on the pen position, in pixels.
/// </summary>
/// <param name="X">The left edge of the glyph's coverage in the atlas.</param>
/// <param name="Y">The top edge of the glyph's coverage in the atlas.</param>
/// <param name="Width">The coverage width; 0 for a glyph with no ink (a space).</param>
/// <param name="Height">The coverage height.</param>
/// <param name="BearingX">The left edge of the coverage, right of the pen.</param>
/// <param name="BearingY">The top edge of the coverage, above the baseline.</param>
public readonly record struct AtlasGlyph(int X, int Y, int Width, int Height, int BearingX, int BearingY);
