namespace XylemUI.Text.Tests;

// A rasterizer for the tests of the atlas and of placing glyphs: the box and the coverage follow from the key alone, so a test
// knows what the atlas holds and where. The shapes of real glyphs are tested with the OS's rasterizer
// (tests/XylemUI.Platform.Windows.Tests/DirectWriteGlyphRasterizerTests.cs).
//
// Glyph 1 (the test font's space) has no ink. Any other glyph is a square of round(0.8 × size) pixels, or of the side set in
// Sides, with its left edge 1 pixel right of the pen and its bottom on the baseline. Each pixel holds Value(key, row, column),
// which is never 0; for ClearType coverage, red is Value and green and blue are Channel(key, row, column, 1 or 2), and the fourth
// byte is left as it is. A glyph in ColorGlyphs is a color glyph: asked for as grayscale or ClearType, its box only says so (and
// whether it has a layer in the text's color); as Color, it is the square, blue Value, green and red Channel 1 and 2, alpha 255.
internal sealed class TestRasterizer : IGlyphRasterizer
{
    public const uint Space = 1;

    public Dictionary<uint, int> Sides { get; } = [];

    public List<GlyphKey> Measured { get; } = [];

    // Color glyphs, each with whether it has a layer in the text's color.
    public Dictionary<uint, bool> ColorGlyphs { get; } = [];

    public static byte Value(in GlyphKey key, int row, int column) =>
        (byte)(1 + ((int)((key.GlyphId * 7) + (key.SubpixelX * 31) + (key.PixelSize * 4) + (row * 3) + column) % 254));

    public static byte Channel(in GlyphKey key, int row, int column, int channel) => (byte)(1 + ((Value(key, row, column) + (channel * 85)) % 254));

    public GlyphBox Measure(in GlyphKey glyph)
    {
        ObjectDisposedException.ThrowIf(glyph.Font.IsDisposed, glyph.Font);
        Measured.Add(glyph);
        if (glyph.GlyphId == Space)
        {
            return default;
        }

        bool isColor = ColorGlyphs.TryGetValue(glyph.GlyphId, out bool usesTextColor);
        if (isColor && glyph.Coverage != GlyphCoverage.Color)
        {
            return new GlyphBox(0, 0, 0, 0, IsColor: true, usesTextColor);
        }

        int side = Sides.TryGetValue(glyph.GlyphId, out int set) ? set : (int)MathF.Round(glyph.PixelSize * 0.8f);
        return new GlyphBox(1, side, side, side, isColor, usesTextColor);
    }

    public void Rasterize(in GlyphKey glyph, Span<byte> destination, int stride)
    {
        ObjectDisposedException.ThrowIf(glyph.Font.IsDisposed, glyph.Font);
        GlyphBox box = Measure(glyph);
        Measured.RemoveAt(Measured.Count - 1);
        bool rgb = glyph.Coverage != GlyphCoverage.Gray;
        for (int row = 0; row < box.Height; row++)
        {
            for (int column = 0; column < box.Width; column++)
            {
                if (!rgb)
                {
                    destination[(row * stride) + column] = Value(glyph, row, column);
                    continue;
                }

                int at = (row * stride) + (column * 4);
                destination[at] = Value(glyph, row, column);
                destination[at + 1] = Channel(glyph, row, column, 1);
                destination[at + 2] = Channel(glyph, row, column, 2);
                if (glyph.Coverage == GlyphCoverage.Color)
                {
                    destination[at + 3] = 255;
                }
            }
        }
    }
}
