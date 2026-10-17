namespace XylemUI;

/// <summary>
/// One glyph of a glyph run: where its coverage is in the glyph atlas and where it goes on the target, in whole pixels (the
/// coverage is copied one texel to one pixel, design §5.0.1, §5.0.3).
/// </summary>
/// <param name="X">The left edge on the target, in physical pixels.</param>
/// <param name="Y">The top edge on the target.</param>
/// <param name="Width">The width of the coverage, in pixels.</param>
/// <param name="Height">The height of the coverage.</param>
/// <param name="Page">The atlas page that holds the coverage.</param>
/// <param name="AtlasX">The left edge of the coverage in the page.</param>
/// <param name="AtlasY">The top edge of the coverage in the page.</param>
public readonly record struct GlyphInstance(int X, int Y, int Width, int Height, int Page, int AtlasX, int AtlasY);
