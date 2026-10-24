namespace XylemUI.Text;

/// <summary>
/// One glyph asked of <see cref="GlyphAtlas.GetMany"/>: its key and the text color it is drawn in (design §5.0.3, "Glyphs seen for
/// the first time").
/// </summary>
/// <param name="Key">The glyph, its size, its subpixel step, and its kind of coverage.</param>
/// <param name="TextColor">The text's color, packed as red in the low byte, then green, blue, and alpha.</param>
public readonly record struct GlyphRequest(GlyphKey Key, uint TextColor);
