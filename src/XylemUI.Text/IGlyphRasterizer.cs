namespace XylemUI.Text;

/// <summary>
/// Draws the coverage of one glyph for the <see cref="GlyphAtlas"/>: the OS's rasterizer where there is one (DirectWrite on
/// Windows), FreeType on Linux (design §5.0.3).
/// </summary>
/// <remarks>
/// The atlas calls <see cref="Measure"/> to learn the size of the glyph's ink, takes room for it, and then calls
/// <see cref="Rasterize"/> with the same key to write the coverage straight into that room; for the new glyphs of a frame, it measures
/// them all before it rasterizes any (design §5.0.3, "Glyphs seen for the first time"). An instance is used on one thread at a
/// time; distinct instances of an implementation must work on distinct threads at once, as an atlas made with a function that
/// creates rasterizers gives each of its threads its own.
/// </remarks>
public interface IGlyphRasterizer
{
    /// <summary>
    /// Returns the box of the glyph's ink, in whole pixels, with the pen on a whole pixel and the glyph shifted right by its
    /// subpixel step, for the key's kind of coverage. For a color glyph asked for as grayscale or ClearType, it only says that the
    /// glyph is a color glyph (<see cref="GlyphBox.IsColor"/>), and whether it has a layer in the text's color; the atlas then asks for
    /// the <see cref="GlyphCoverage.Color"/> key.
    /// </summary>
    /// <param name="glyph">The glyph.</param>
    /// <returns>The box; its width or height is 0 for a glyph with no ink (a space).</returns>
    /// <exception cref="ObjectDisposedException">The glyph's font was disposed.</exception>
    GlyphBox Measure(in GlyphKey glyph);

    /// <summary>
    /// Writes the glyph's coverage into <paramref name="destination"/>: the rows of the box <see cref="Measure"/> returns, each
    /// <paramref name="stride"/> bytes after the last. <see cref="GlyphCoverage.Gray"/> is one byte per pixel;
    /// <see cref="GlyphCoverage.Rgb"/> is four, red, green, and blue coverage and a fourth byte left as it is;
    /// <see cref="GlyphCoverage.Color"/> is four, premultiplied blue, green, red, and alpha.
    /// </summary>
    /// <param name="glyph">The glyph.</param>
    /// <param name="destination">The room for the box, from its top-left pixel.</param>
    /// <param name="stride">The bytes from the start of one row to the start of the next.</param>
    /// <exception cref="ObjectDisposedException">The glyph's font was disposed.</exception>
    /// <exception cref="ArgumentException"><paramref name="destination"/> is too short for the box at <paramref name="stride"/>.</exception>
    void Rasterize(in GlyphKey glyph, Span<byte> destination, int stride);
}
