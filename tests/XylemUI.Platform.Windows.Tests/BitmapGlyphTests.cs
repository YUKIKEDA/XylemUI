using XylemUI.Text;

namespace XylemUI.Platform.Windows.Tests;

// PNG bitmap color glyphs (design §5.0.3, "PNG bitmaps", roadmap row T9), drawn by DirectWriteGlyphRasterizer from the bitmap
// test font (tests/XylemUI.Text.Tests/Fonts/README.md): strikes of 20 and 64 ppem, "H" red at 20 and green at 64, "O" with no
// outline, blue at 20, and "I" with a broken PNG. Each picture is a square of 0.8 em (16 and 51 pixels) with a transparent border
// of one pixel, its left edge 1 pixel right of the pen and its bottom on the baseline.
public sealed class BitmapGlyphTests
{
    private const uint GlyphH = 41;
    private const uint GlyphI = 42;
    private const uint GlyphO = 48;
    private static readonly byte[] FontBytes = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fonts", "XylemUITestBitmap.ttf"));

    /// <summary>
    /// A glyph whose PNG reads is a color glyph, with an outline or without one; a glyph whose PNG is broken is not, and is its
    /// outline.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - The bitmap test font and a rasterizer.
    ///
    /// Steps:
    /// - Measure "H", "O", and "I" at 20 px as grayscale keys.
    ///
    /// Expected:
    /// - "H" and "O" are color glyphs without a layer in the text's color; "I" is not a color glyph, and its box has ink.
    /// </remarks>
    [Fact]
    public void Measure_PngGlyph_IsAColorGlyphUnlessBroken()
    {
        using var font = new Font(FontBytes);
        using var rasterizer = new DirectWriteGlyphRasterizer();

        GlyphBox h = rasterizer.Measure(new GlyphKey(font, GlyphH, 20, 0));
        GlyphBox o = rasterizer.Measure(new GlyphKey(font, GlyphO, 20, 0));
        GlyphBox i = rasterizer.Measure(new GlyphKey(font, GlyphI, 20, 0));

        Assert.Equal((true, false), (h.IsColor, h.UsesTextColor));
        Assert.Equal((true, false), (o.IsColor, o.UsesTextColor));
        Assert.False(i.IsColor);
        Assert.True(i.Width > 0 && i.Height > 0, $"{i}");
    }

    /// <summary>
    /// A PNG glyph is the picture of the strike DirectWrite returns for the size rounded up, scaled by the size over the strike,
    /// its origin scaled the same way: down by the average over each pixel's area, up bilinearly.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - The bitmap test font and a rasterizer.
    ///
    /// Steps:
    /// - Measure and rasterize "H" as a color key at 20, 10, 21, and 100 px, and "O" at 20 px, at 4 bytes a pixel.
    ///
    /// Expected:
    /// - 20 px: the 20 ppem strike as it is: 16 x 16, left 1, top 16; red (BGRA 0, 0, 255, 255) inside, the border clear.
    /// - 10 px: the 20 ppem strike at 0.5: 8 x 8, left round(0.5) = 1, top 8; red at the center.
    /// - 21 px: the 64 ppem strike at 21 / 64: round(51 × 0.328) = 17 a side, left round(0.33) = 0, top 17; green at the center.
    /// - 100 px: the 64 ppem strike at 100 / 64 = 1.5625: round(79.7) = 80 a side, left round(1.56) = 2, top 80; green at the
    ///   center.
    /// - "O" at 20 px: blue (BGRA 255, 0, 0, 255) inside, though it has no outline.
    /// </remarks>
    [Fact]
    public void Rasterize_PngGlyph_IsTheStrikeScaled()
    {
        using var font = new Font(FontBytes);
        using var rasterizer = new DirectWriteGlyphRasterizer();

        (GlyphBox at20, byte[] pixels20) = Draw(rasterizer, new GlyphKey(font, GlyphH, 20, 0, GlyphCoverage.Color));
        (GlyphBox at10, byte[] pixels10) = Draw(rasterizer, new GlyphKey(font, GlyphH, 10, 0, GlyphCoverage.Color));
        (GlyphBox at21, byte[] pixels21) = Draw(rasterizer, new GlyphKey(font, GlyphH, 21, 0, GlyphCoverage.Color));
        (GlyphBox at100, byte[] pixels100) = Draw(rasterizer, new GlyphKey(font, GlyphH, 100, 0, GlyphCoverage.Color));
        (_, byte[] blue) = Draw(rasterizer, new GlyphKey(font, GlyphO, 20, 0, GlyphCoverage.Color));

        Assert.Equal(new GlyphBox(1, 16, 16, 16, IsColor: true), at20);
        Assert.Equal((0, 0, 255, 255), Pixel(pixels20, 16, 5, 5));
        Assert.Equal((0, 0, 0, 0), Pixel(pixels20, 16, 0, 5));
        Assert.Equal(new GlyphBox(1, 8, 8, 8, IsColor: true), at10);
        Assert.Equal((0, 0, 255, 255), Pixel(pixels10, 8, 4, 4));
        Assert.Equal(new GlyphBox(0, 17, 17, 17, IsColor: true), at21);
        Assert.Equal((0, 255, 0, 255), Pixel(pixels21, 17, 8, 8));
        Assert.Equal(new GlyphBox(2, 80, 80, 80, IsColor: true), at100);
        Assert.Equal((0, 255, 0, 255), Pixel(pixels100, 80, 40, 40));
        Assert.Equal((255, 0, 0, 255), Pixel(blue, 16, 5, 5));
    }

    /// <summary>
    /// A bilevel key draws the same picture: a PNG glyph is a picture, not made bilevel (design §5.0.3, "PNG bitmaps").
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - The bitmap test font and a rasterizer.
    ///
    /// Steps:
    /// - Rasterize "H" at 10 px as a color key, antialiased and bilevel.
    ///
    /// Expected:
    /// - The same box and the same bytes; some alpha is between 0 and 255 (the scaled border).
    /// </remarks>
    [Fact]
    public void Rasterize_AliasedPngGlyph_IsTheSamePicture()
    {
        using var font = new Font(FontBytes);
        using var rasterizer = new DirectWriteGlyphRasterizer();

        (GlyphBox box, byte[] pixels) = Draw(rasterizer, new GlyphKey(font, GlyphH, 10, 0, GlyphCoverage.Color));
        (GlyphBox aliasedBox, byte[] aliased) = Draw(rasterizer, new GlyphKey(font, GlyphH, 10, 0, GlyphCoverage.Color, Aliased: true));

        Assert.Equal(box, aliasedBox);
        Assert.Equal(pixels, aliased);
        Assert.Contains(Enumerable.Range(0, pixels.Length / 4), i => pixels[(i * 4) + 3] is > 0 and < 255);
    }

    /// <summary>
    /// In the atlas, a PNG glyph asked for as grayscale is kept once on a color page, and nothing goes to the grayscale pages.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - The bitmap test font, a rasterizer, and an empty atlas.
    ///
    /// Steps:
    /// - Get "H" at 20 px as grayscale keys at steps 0 and 2.
    ///
    /// Expected:
    /// - Both give the same glyph, of the color kind, 16 x 16; one color page and no grayscale page; one glyph rasterized.
    /// </remarks>
    [Fact]
    public void TryGet_PngGlyph_IsKeptOnAColorPage()
    {
        using var font = new Font(FontBytes);
        using var rasterizer = new DirectWriteGlyphRasterizer();
        var atlas = new GlyphAtlas(rasterizer);

        Assert.True(atlas.TryGet(new GlyphKey(font, GlyphH, 20, 0), out AtlasGlyph first));
        Assert.True(atlas.TryGet(new GlyphKey(font, GlyphH, 20, 2), out AtlasGlyph second));

        Assert.Equal(first, second);
        Assert.Equal((GlyphCoverage.Color, 16, 16), (first.Coverage, first.Width, first.Height));
        Assert.Equal((1, 0), (atlas.GetPageCount(GlyphCoverage.Color), atlas.GetPageCount(GlyphCoverage.Gray)));
        Assert.Equal(1, atlas.RasterizeCount);
    }

    private static (GlyphBox Box, byte[] Pixels) Draw(DirectWriteGlyphRasterizer rasterizer, GlyphKey key)
    {
        GlyphBox box = rasterizer.Measure(key);
        byte[] pixels = new byte[box.Width * box.Height * 4];
        rasterizer.Rasterize(key, pixels, box.Width * 4);
        return (box, pixels);
    }

    private static (int Blue, int Green, int Red, int Alpha) Pixel(byte[] pixels, int width, int x, int y)
    {
        int at = ((y * width) + x) * 4;
        return (pixels[at], pixels[at + 1], pixels[at + 2], pixels[at + 3]);
    }
}
