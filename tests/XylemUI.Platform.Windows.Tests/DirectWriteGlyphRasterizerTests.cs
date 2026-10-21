using XylemUI.Text;

namespace XylemUI.Platform.Windows.Tests;

// Glyphs drawn by DirectWrite (design §5.0.3, roadmap row T4). The test font has no hinting instructions, so the ink follows the
// outline; the outlines were read with fontTools (tests/XylemUI.Text.Tests/Fonts/README.md): "H" (glyph 41) is 101..628 x 0..733
// units of 1000, which at 32 px is 3.23..20.10 x 0..23.46 pixels.
public sealed class DirectWriteGlyphRasterizerTests
{
    private const uint GlyphH = 41;
    private const uint Space = 1;
    private static readonly byte[] FontBytes = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fonts", "XylemUITestSans.ttf"));

    /// <summary>
    /// The ink box of a glyph is its outline rounded out to whole pixels, within one pixel on each side: one more for the
    /// anti-aliasing of an edge on a pixel boundary, one less for a sliver of the outline that gets no coverage (DirectWrite left
    /// out the 0.10 px of "H" past x 20 at 32 px; that it samples coverage is our reading, not checked).
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - The test font and a rasterizer.
    ///
    /// Steps:
    /// - Measure "H" at 32 px, step 0.
    ///
    /// Expected:
    /// - Left 2 to 4, right 20 to 22, top 23 to 25, bottom −1 to 1 (the outline, 3.23..20.10 x 0..23.46, rounded out is 3..21 x
    ///   0..24).
    /// </remarks>
    [Fact]
    public void Measure_Box_IsTheOutlineWithinAPixel()
    {
        using var font = new Font(FontBytes);
        using var rasterizer = new DirectWriteGlyphRasterizer();

        GlyphBox box = rasterizer.Measure(new GlyphKey(font, GlyphH, 32, 0));

        bool covers =
            box.Left is >= 2 and <= 4
            && box.Left + box.Width is >= 20 and <= 22
            && box.Top is >= 23 and <= 25
            && box.Top - box.Height is >= -1 and <= 1;
        Assert.True(covers, $"{box}");
    }

    /// <summary>
    /// Each subpixel step draws the glyph a quarter of a pixel further right: the center of its ink moves by the step over 4.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - The test font and a rasterizer.
    ///
    /// Steps:
    /// - Rasterize "H" at 32 px at steps 0 to 3, and take the horizontal center of the coverage, from the pen.
    ///
    /// Expected:
    /// - The center at step s is the center at step 0 plus s / 4, within 0.05 of a pixel.
    /// </remarks>
    [Fact]
    public void Rasterize_SubpixelStep_MovesTheInkAQuarterPixelEach()
    {
        using var font = new Font(FontBytes);
        using var rasterizer = new DirectWriteGlyphRasterizer();

        double first = Center(rasterizer, new GlyphKey(font, GlyphH, 32, 0));
        for (int step = 1; step < GlyphPosition.SubpixelSteps; step++)
        {
            double center = Center(rasterizer, new GlyphKey(font, GlyphH, 32, step));
            Assert.InRange(center - first, (step / 4.0) - 0.05, (step / 4.0) + 0.05);
        }
    }

    /// <summary>
    /// A size with a fraction draws between the whole sizes around it.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - The test font and a rasterizer.
    ///
    /// Steps:
    /// - Rasterize "H" at 17, 17.5, and 18 px, and sum the coverage of each.
    ///
    /// Expected:
    /// - The sums grow strictly from 17 to 17.5 to 18 px.
    /// </remarks>
    [Fact]
    public void Rasterize_FractionalSize_DrawsBetweenTheWholeSizes()
    {
        using var font = new Font(FontBytes);
        using var rasterizer = new DirectWriteGlyphRasterizer();

        long small = Ink(rasterizer, new GlyphKey(font, GlyphH, 17, 0));
        long middle = Ink(rasterizer, new GlyphKey(font, GlyphH, 17.5f, 0));
        long large = Ink(rasterizer, new GlyphKey(font, GlyphH, 18, 0));

        Assert.True(small < middle && middle < large, $"ink {small}, {middle}, {large}");
    }

    /// <summary>
    /// The coverage is written row after row at the stride, and the bytes between the rows are left as they were.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - The test font and a rasterizer.
    ///
    /// Steps:
    /// - Rasterize "H" at 32 px into a packed buffer (the stride is the width), and into a buffer of 0xEE with a stride of the
    ///   width plus 3.
    ///
    /// Expected:
    /// - Each row of the second is the row of the first; the 3 bytes after each row are still 0xEE; the coverage has ink.
    /// </remarks>
    [Fact]
    public void Rasterize_Stride_LeavesTheGapsAlone()
    {
        using var font = new Font(FontBytes);
        using var rasterizer = new DirectWriteGlyphRasterizer();
        var key = new GlyphKey(font, GlyphH, 32, 0);
        GlyphBox box = rasterizer.Measure(key);
        byte[] packed = new byte[box.Width * box.Height];
        int stride = box.Width + 3;
        byte[] spaced = new byte[stride * box.Height];
        Array.Fill(spaced, (byte)0xEE);

        rasterizer.Rasterize(key, packed, box.Width);
        rasterizer.Rasterize(key, spaced, stride);

        Assert.Contains(packed, value => value > 200);
        for (int row = 0; row < box.Height; row++)
        {
            Assert.Equal(packed.AsSpan(row * box.Width, box.Width), spaced.AsSpan(row * stride, box.Width));
            Assert.All(spaced.AsSpan((row * stride) + box.Width, 3).ToArray(), value => Assert.Equal(0xEE, value));
        }
    }

    /// <summary>
    /// A glyph with no ink has an empty box and writes nothing; a destination too small for the box is refused.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - The test font and a rasterizer.
    ///
    /// Steps:
    /// - Measure and rasterize the space (glyph 1) at 32 px into a buffer of 0xEE; rasterize "H" at 32 px into a buffer one byte
    ///   short of its box.
    ///
    /// Expected:
    /// - The space's box has no width or no height, and the buffer is unchanged; "H" throws <see cref="ArgumentException"/>.
    /// </remarks>
    [Fact]
    public void Rasterize_NoInkOrTooSmall_WritesNothingOrThrows()
    {
        using var font = new Font(FontBytes);
        using var rasterizer = new DirectWriteGlyphRasterizer();
        byte[] buffer = [0xEE, 0xEE, 0xEE, 0xEE];

        GlyphBox space = rasterizer.Measure(new GlyphKey(font, Space, 32, 0));
        rasterizer.Rasterize(new GlyphKey(font, Space, 32, 0), buffer, 2);
        var h = new GlyphKey(font, GlyphH, 32, 0);
        GlyphBox box = rasterizer.Measure(h);

        Assert.True(space.Width == 0 || space.Height == 0, $"{space}");
        Assert.All(buffer, value => Assert.Equal(0xEE, value));
        Assert.Throws<ArgumentException>(() => rasterizer.Rasterize(h, new byte[(box.Width * box.Height) - 1], box.Width));
    }

    /// <summary>
    /// A face of an OS font collection other than the first draws from the font's bytes in place.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - Yu Gothic Regular (<c>YuGothR.ttc</c>), mapped by <see cref="Font.FromFile"/> at face 1 (Yu Gothic UI), and a
    ///   rasterizer. Skipped where Windows lacks the font.
    ///
    /// Steps:
    /// - Rasterize "日" (its glyph from the font's cmap, through shaping) at 21 px.
    ///
    /// Expected:
    /// - The coverage has ink.
    /// </remarks>
    [Fact]
    public void Rasterize_FaceOfACollection_DrawsFromTheMappedFile()
    {
        string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Fonts), "YuGothR.ttc");
        Assert.SkipUnless(File.Exists(path), "Windows has no Yu Gothic (YuGothR.ttc).");
        using Font font = Font.FromFile(path, 1);
        using var shaper = new TextShaper();
        using var rasterizer = new DirectWriteGlyphRasterizer();
        uint glyph = shaper.Shape(font, "日", TextLanguage.FromTag("ja"))[0].GlyphId;

        Assert.True(Ink(rasterizer, new GlyphKey(font, glyph, 21, 0)) > 0);
    }

    /// <summary>
    /// A disposed font and a disposed rasterizer are refused.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - The test font and a rasterizer that has drawn "H" from it.
    ///
    /// Steps:
    /// - Dispose the font and measure "H" at 24 px; dispose the rasterizer and measure "H" of another font.
    ///
    /// Expected:
    /// - Both throw <see cref="ObjectDisposedException"/>.
    /// </remarks>
    [Fact]
    public void Measure_Disposed_Throws()
    {
        var font = new Font(FontBytes);
        using var other = new Font(FontBytes);
        var rasterizer = new DirectWriteGlyphRasterizer();
        _ = rasterizer.Measure(new GlyphKey(font, GlyphH, 32, 0));

        font.Dispose();
        Assert.Throws<ObjectDisposedException>(() => rasterizer.Measure(new GlyphKey(font, GlyphH, 24, 0)));
        rasterizer.Dispose();
        Assert.Throws<ObjectDisposedException>(() => rasterizer.Measure(new GlyphKey(other, GlyphH, 24, 0)));
    }

    /// <summary>
    /// Rasterizing new glyphs allocates nothing once the coverage buffer has grown and the font has its face.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - The test font and a rasterizer that has drawn "H" at 48 px (larger than the glyphs measured).
    ///
    /// Steps:
    /// - Measure and rasterize "H" at every size from 10 to 40 px and every step, into one buffer. Measure the bytes allocated on
    ///   this thread.
    ///
    /// Expected:
    /// - 0 bytes.
    /// </remarks>
    [Fact]
    public void Rasterize_NewGlyphs_AllocatesNothing()
    {
        using var font = new Font(FontBytes);
        using var rasterizer = new DirectWriteGlyphRasterizer();
        byte[] buffer = new byte[64 * 64];
        var warm = new GlyphKey(font, GlyphH, 48, 0);
        GlyphBox warmBox = rasterizer.Measure(warm);
        rasterizer.Rasterize(warm, buffer, warmBox.Width);

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int size = 10; size <= 40; size++)
        {
            for (int step = 0; step < GlyphPosition.SubpixelSteps; step++)
            {
                var key = new GlyphKey(font, GlyphH, size, step);
                GlyphBox box = rasterizer.Measure(key);
                rasterizer.Rasterize(key, buffer, box.Width);
            }
        }

        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }

    // The horizontal center of the glyph's coverage, in pixels right of the pen.
    private static double Center(DirectWriteGlyphRasterizer rasterizer, in GlyphKey key)
    {
        GlyphBox box = rasterizer.Measure(key);
        byte[] coverage = new byte[box.Width * box.Height];
        rasterizer.Rasterize(key, coverage, box.Width);
        double sum = 0;
        double weighted = 0;
        for (int i = 0; i < coverage.Length; i++)
        {
            sum += coverage[i];
            weighted += coverage[i] * ((i % box.Width) + 0.5);
        }

        return box.Left + (weighted / sum);
    }

    private static long Ink(DirectWriteGlyphRasterizer rasterizer, in GlyphKey key)
    {
        GlyphBox box = rasterizer.Measure(key);
        byte[] coverage = new byte[box.Width * box.Height];
        rasterizer.Rasterize(key, coverage, box.Width);
        return coverage.Sum(value => (long)value);
    }
}
