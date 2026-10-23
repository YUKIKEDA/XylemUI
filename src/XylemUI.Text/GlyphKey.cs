namespace XylemUI.Text;

/// <summary>
/// One glyph at one size, one horizontal subpixel step, one kind of coverage, antialiased or bilevel: what an <see cref="IGlyphRasterizer"/> draws
/// and what the <see cref="GlyphAtlas"/> keeps (design §5.0.3).
/// </summary>
/// <param name="Font">The font.</param>
/// <param name="GlyphId">The glyph, from shaping.</param>
/// <param name="PixelSize">
/// The pixels per em: the text's size in logical pixels times the display's scale, not rounded (14 px at 125 % is 17.5).
/// </param>
/// <param name="SubpixelX">
/// How far right of a whole pixel the glyph is drawn, in quarters of a pixel: 0 to <see cref="GlyphPosition.SubpixelSteps"/> − 1.
/// </param>
/// <param name="Coverage">Grayscale, ClearType, or color; each is a different glyph in the atlas.</param>
/// <param name="TextColor">
/// For a color glyph with a layer painted in the text's color, that color, opaque, packed as red in the low byte, then green and
/// blue, and 255 in the high byte; 0 for every other glyph. The text's alpha is applied when the glyph is drawn, not here (design
/// §5.0.3, "Color glyphs").
/// </param>
/// <param name="Aliased">
/// Whether the glyph is drawn bilevel, with coverage 0 or 255 only, because Windows does not smooth the edges of fonts (design
/// §5.0.3, "When fonts are not smoothed", roadmap row T8). A bilevel glyph has subpixel step 0 and grayscale or color coverage;
/// it is a different glyph from the antialiased one.
/// </param>
public readonly record struct GlyphKey(
    Font Font,
    uint GlyphId,
    float PixelSize,
    int SubpixelX,
    GlyphCoverage Coverage = GlyphCoverage.Gray,
    uint TextColor = 0,
    bool Aliased = false
);
