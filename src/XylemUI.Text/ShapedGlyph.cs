namespace XylemUI.Text;

/// <summary>
/// One glyph of shaped text, in the font's units (<see cref="Font.UnitsPerEm"/>).
/// </summary>
/// <param name="GlyphId">The glyph in the font; 0 is <c>.notdef</c>, for a character the font lacks.</param>
/// <param name="Cluster">The UTF-16 offset in the text of the first code unit of the glyph's cluster.</param>
/// <param name="XAdvance">How far the pen moves after the glyph.</param>
/// <param name="XOffset">The horizontal shift of the glyph from the pen.</param>
/// <param name="YOffset">The vertical shift of the glyph from the pen, up positive.</param>
/// <param name="UnsafeToBreak">
/// Whether a line break before the glyph's cluster changes how the text around it is shaped (HarfBuzz's
/// <c>HB_GLYPH_FLAG_UNSAFE_TO_BREAK</c>): a ligature or a kerning pair across it, or the contextual form of a joining script. The
/// line breaking shapes the text around such a break again (design §4.1, "Shaping and line breaking").
/// </param>
public readonly record struct ShapedGlyph(uint GlyphId, int Cluster, int XAdvance, int XOffset, int YOffset, bool UnsafeToBreak = false);
