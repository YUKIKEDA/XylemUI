namespace XylemUI.Text;

/// <summary>
/// One glyph at one size and one horizontal subpixel step: what an <see cref="IGlyphRasterizer"/> draws and what the
/// <see cref="GlyphAtlas"/> keeps (design §5.0.3).
/// </summary>
/// <param name="Font">The font.</param>
/// <param name="GlyphId">The glyph, from shaping.</param>
/// <param name="PixelSize">
/// The pixels per em: the text's size in logical pixels times the display's scale, not rounded (14 px at 125 % is 17.5).
/// </param>
/// <param name="SubpixelX">
/// How far right of a whole pixel the glyph is drawn, in quarters of a pixel: 0 to <see cref="GlyphPosition.SubpixelSteps"/> − 1.
/// </param>
public readonly record struct GlyphKey(Font Font, uint GlyphId, float PixelSize, int SubpixelX);
