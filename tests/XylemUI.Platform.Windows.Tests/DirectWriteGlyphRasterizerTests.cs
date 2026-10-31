using TerraFX.Interop.DirectX;
using TerraFX.Interop.Windows;
using XylemUI.Text;

namespace XylemUI.Platform.Windows.Tests;

// Glyphs drawn by DirectWrite (design §5.0.3, roadmap row T4). The test font has no hinting instructions, so the ink follows the
// outline; the outlines were read with fontTools (tests/XylemUI.Text.Tests/Fonts/README.md): "H" (glyph 41) is 101..628 x 0..733
// units of 1000, which at 32 px is 3.23..20.10 x 0..23.46 pixels.
public sealed unsafe class DirectWriteGlyphRasterizerTests
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
    /// Rasterizing new glyphs allocates nothing once the coverage buffer has grown and the font has its face, for both kinds of
    /// coverage.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - The test font and a rasterizer that has drawn "H" at 48 px (larger than the glyphs measured) in the kind of coverage.
    ///
    /// Steps:
    /// - Measure and rasterize "H" at every size from 10 to 40 px and every step, into one buffer. Measure the bytes allocated on
    ///   this thread.
    ///
    /// Expected:
    /// - 0 bytes.
    /// </remarks>
    /// <param name="coverage">The kind of coverage.</param>
    [Theory]
    [InlineData(GlyphCoverage.Gray)]
    [InlineData(GlyphCoverage.Rgb)]
    public void Rasterize_NewGlyphs_AllocatesNothing(GlyphCoverage coverage)
    {
        using var font = new Font(FontBytes);
        using var rasterizer = new DirectWriteGlyphRasterizer();
        int bytes = GlyphAtlas.BytesPerPixel(coverage);
        byte[] buffer = new byte[64 * 64 * bytes];
        var warm = new GlyphKey(font, GlyphH, 48, 0, coverage);
        GlyphBox warmBox = rasterizer.Measure(warm);
        rasterizer.Rasterize(warm, buffer, warmBox.Width * bytes);

        // Each run is one size and step of the 31 x 4 from 10 px, in order; the 130 measured runs go through all 124, and the
        // unmeasured window's negative indices wrap to the last ones.
        const int Glyphs = 31 * GlyphPosition.SubpixelSteps;
        PerformanceAssert.DoesNotAllocate(
            130,
            i =>
            {
                int n = ((i % Glyphs) + Glyphs) % Glyphs;
                var key = new GlyphKey(font, GlyphH, 10 + (n / GlyphPosition.SubpixelSteps), n % GlyphPosition.SubpixelSteps, coverage);
                GlyphBox box = rasterizer.Measure(key);
                rasterizer.Rasterize(key, buffer, box.Width * bytes);
            }
        );
    }

    /// <summary>
    /// A ClearType glyph's box and its red, green, and blue coverage are what DirectWrite's ClearType 3x1 texture holds for the
    /// same glyph run; the fourth byte of each pixel is left as it was.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - Segoe UI (<c>segoeui.ttf</c>) opened from its file, and a rasterizer. Skipped where Windows lacks the font.
    ///
    /// Steps:
    /// - For "H" and "a" at 13, 16, and 17.5 px, steps 0 and 1: rasterize with <see cref="GlyphCoverage.Rgb"/> into a buffer of
    ///   0xEE at 4 bytes a pixel; make DirectWrite's analysis of the glyph straight (the recommended rendering mode and grid fit,
    ///   ClearType anti-aliasing, the step over 4 as the origin) and read its 3x1 texture.
    ///
    /// Expected:
    /// - The box is the texture's bounds; bytes 0, 1, 2 of each pixel are the texture's red, green, and blue; byte 3 is 0xEE.
    /// - Some pixel of "H" at 16 px has red and blue more than 30 apart (the coverage is per subpixel, not gray).
    /// </remarks>
    [Fact]
    public void Rasterize_ClearType_IsDirectWritesThreeByOneTexture()
    {
        string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Fonts), "segoeui.ttf");
        Assert.SkipUnless(File.Exists(path), "Windows has no Segoe UI (segoeui.ttf).");
        using Font font = Font.FromFile(path, 0);
        using var shaper = new TextShaper();
        using var rasterizer = new DirectWriteGlyphRasterizer();
        bool colored = false;
        foreach (string text in (string[])["H", "a"])
        {
            uint glyph = shaper.Shape(font, text, TextLanguage.FromTag("en"))[0].GlyphId;
            foreach (float size in (float[])[13, 16, 17.5f])
            {
                for (int step = 0; step < 2; step++)
                {
                    var key = new GlyphKey(font, glyph, size, step, GlyphCoverage.Rgb);
                    GlyphBox box = rasterizer.Measure(key);
                    byte[] ours = new byte[box.Width * box.Height * 4];
                    Array.Fill(ours, (byte)0xEE);
                    rasterizer.Rasterize(key, ours, box.Width * 4);

                    (RECT bounds, byte[] theirs) = DirectWriteClearType(path, (ushort)glyph, size, step);

                    Assert.Equal(new GlyphBox(bounds.left, -bounds.top, bounds.right - bounds.left, bounds.bottom - bounds.top), box);
                    for (int i = 0; i < box.Width * box.Height; i++)
                    {
                        Assert.Equal(theirs.AsSpan(i * 3, 3), ours.AsSpan(i * 4, 3));
                        Assert.Equal(0xEE, ours[(i * 4) + 3]);
                        colored |= text == "H" && size == 16 && Math.Abs(ours[i * 4] - ours[(i * 4) + 2]) > 30;
                    }
                }
            }
        }

        Assert.True(colored, "No pixel of H at 16 px has red and blue apart.");
    }

    /// <summary>
    /// An aliased rendering mode is read as the 1x1 texture for both kinds of coverage; the other modes as the ClearType 3x1
    /// texture for ClearType.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - None.
    ///
    /// Steps:
    /// - Take the texture for each kind of coverage with the aliased mode and with the natural symmetric mode.
    ///
    /// Expected:
    /// - Aliased: the 1x1 texture for Gray and for Rgb. Natural symmetric: 1x1 for Gray, ClearType 3x1 for Rgb.
    /// </remarks>
    /// <param name="coverage">The kind of coverage.</param>
    /// <param name="mode">The rendering mode.</param>
    /// <param name="expected">The texture.</param>
    [Theory]
    [InlineData(GlyphCoverage.Gray, DWRITE_RENDERING_MODE1.DWRITE_RENDERING_MODE1_ALIASED, DWRITE_TEXTURE_TYPE.DWRITE_TEXTURE_ALIASED_1x1)]
    [InlineData(GlyphCoverage.Rgb, DWRITE_RENDERING_MODE1.DWRITE_RENDERING_MODE1_ALIASED, DWRITE_TEXTURE_TYPE.DWRITE_TEXTURE_ALIASED_1x1)]
    [InlineData(GlyphCoverage.Gray, DWRITE_RENDERING_MODE1.DWRITE_RENDERING_MODE1_NATURAL_SYMMETRIC, DWRITE_TEXTURE_TYPE.DWRITE_TEXTURE_ALIASED_1x1)]
    [InlineData(GlyphCoverage.Rgb, DWRITE_RENDERING_MODE1.DWRITE_RENDERING_MODE1_NATURAL_SYMMETRIC, DWRITE_TEXTURE_TYPE.DWRITE_TEXTURE_CLEARTYPE_3x1)]
    public void TextureOf_Mode_PairsAliasedWithTheOneByOneTexture(
        GlyphCoverage coverage,
        DWRITE_RENDERING_MODE1 mode,
        DWRITE_TEXTURE_TYPE expected
    ) => Assert.Equal(expected, DirectWriteGlyphRasterizer.TextureOf(coverage, mode));

    /// <summary>
    /// A ClearType glyph in the aliased rendering mode has ink: each pixel's red, green, and blue are the glyph's grayscale value
    /// in the same mode, 0 or 255.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - The test font and a rasterizer whose rendering mode is set to aliased (no font of Windows gets it from DirectWrite).
    ///
    /// Steps:
    /// - Measure and rasterize "H" at 16 px in Gray, and in Rgb at 4 bytes a pixel into a buffer of 0xEE.
    ///
    /// Expected:
    /// - The two boxes are the same and not empty; each Rgb pixel's three bytes are the Gray value, the fourth 0xEE; every value is
    ///   0 or 255, and some are 255.
    /// </remarks>
    [Fact]
    public void Rasterize_AliasedClearType_SpreadsTheCoverageToTheThreeChannels()
    {
        using var font = new Font(FontBytes);
        using var rasterizer = new DirectWriteGlyphRasterizer { RenderingMode = DWRITE_RENDERING_MODE1.DWRITE_RENDERING_MODE1_ALIASED };
        var gray = new GlyphKey(font, GlyphH, 16, 0);
        var rgb = gray with { Coverage = GlyphCoverage.Rgb };

        GlyphBox grayBox = rasterizer.Measure(gray);
        byte[] grayPixels = new byte[grayBox.Width * grayBox.Height];
        rasterizer.Rasterize(gray, grayPixels, grayBox.Width);
        GlyphBox rgbBox = rasterizer.Measure(rgb);
        byte[] rgbPixels = new byte[rgbBox.Width * rgbBox.Height * 4];
        Array.Fill(rgbPixels, (byte)0xEE);
        rasterizer.Rasterize(rgb, rgbPixels, rgbBox.Width * 4);

        Assert.Equal(grayBox, rgbBox);
        Assert.True(grayBox.Width > 0 && grayBox.Height > 0, $"{grayBox}");
        Assert.All(grayPixels, value => Assert.True(value is 0 or 255, $"{value}"));
        Assert.Contains((byte)255, grayPixels);
        for (int i = 0; i < grayPixels.Length; i++)
        {
            Assert.Equal([grayPixels[i], grayPixels[i], grayPixels[i], 0xEE], rgbPixels.AsSpan(i * 4, 4).ToArray());
        }
    }

    // DirectWrite's ClearType 3x1 texture of one glyph, from the font file, with the rasterizer's rendering mode, grid fit, and
    // origin (DirectWriteGlyphRasterizer.Analyze), made here without the rasterizer.
    private static (RECT Bounds, byte[] Texture) DirectWriteClearType(string path, ushort glyph, float size, int step)
    {
        IDWriteFactory5* factory;
        Guid factoryId = IID.IID_IDWriteFactory5;
        Assert.True(DirectX.DWriteCreateFactory(DWRITE_FACTORY_TYPE.DWRITE_FACTORY_TYPE_SHARED, &factoryId, (IUnknown**)&factory) >= 0);
        IDWriteFontFile* file;
        fixed (char* name = path)
        {
            Assert.True(factory->CreateFontFileReference(name, null, &file) >= 0);
        }

        IDWriteFontFaceReference* reference;
        Assert.True(factory->CreateFontFaceReference(file, 0, DWRITE_FONT_SIMULATIONS.DWRITE_FONT_SIMULATIONS_NONE, &reference) >= 0);
        IDWriteFontFace3* face;
        Assert.True(reference->CreateFontFace(&face) >= 0);
        DWRITE_RENDERING_MODE1 mode;
        DWRITE_GRID_FIT_MODE gridFit;
        Assert.True(
            face->GetRecommendedRenderingMode(
                size,
                96,
                96,
                null,
                BOOL.FALSE,
                DWRITE_OUTLINE_THRESHOLD.DWRITE_OUTLINE_THRESHOLD_ANTIALIASED,
                DWRITE_MEASURING_MODE.DWRITE_MEASURING_MODE_NATURAL,
                null,
                &mode,
                &gridFit
            ) >= 0
        );
        if (mode == DWRITE_RENDERING_MODE1.DWRITE_RENDERING_MODE1_OUTLINE)
        {
            mode = DWRITE_RENDERING_MODE1.DWRITE_RENDERING_MODE1_NATURAL_SYMMETRIC;
        }

        float advance = 0;
        DWRITE_GLYPH_OFFSET offset = default;
        var run = new DWRITE_GLYPH_RUN
        {
            fontFace = (IDWriteFontFace*)face,
            fontEmSize = size,
            glyphCount = 1,
            glyphIndices = &glyph,
            glyphAdvances = &advance,
            glyphOffsets = &offset,
        };
        IDWriteGlyphRunAnalysis* analysis;
        Assert.True(
            factory->CreateGlyphRunAnalysis(
                &run,
                null,
                mode,
                DWRITE_MEASURING_MODE.DWRITE_MEASURING_MODE_NATURAL,
                gridFit,
                DWRITE_TEXT_ANTIALIAS_MODE.DWRITE_TEXT_ANTIALIAS_MODE_CLEARTYPE,
                step / (float)GlyphPosition.SubpixelSteps,
                0,
                &analysis
            ) >= 0
        );
        RECT bounds;
        Assert.True(analysis->GetAlphaTextureBounds(DWRITE_TEXTURE_TYPE.DWRITE_TEXTURE_CLEARTYPE_3x1, &bounds) >= 0);
        byte[] texture = new byte[Math.Max(0, (bounds.right - bounds.left) * (bounds.bottom - bounds.top) * 3)];
        fixed (byte* pixels = texture)
        {
            Assert.True(analysis->CreateAlphaTexture(DWRITE_TEXTURE_TYPE.DWRITE_TEXTURE_CLEARTYPE_3x1, &bounds, pixels, (uint)texture.Length) >= 0);
        }

        _ = analysis->Release();
        _ = face->Release();
        _ = reference->Release();
        _ = file->Release();
        _ = factory->Release();
        return (bounds, texture);
    }

    /// <summary>
    /// A bilevel key is drawn with DirectWrite's aliased mode: its coverage is 0 or 255 only, where the antialiased glyph has the
    /// values between (design §5.0.3, "When fonts are not smoothed").
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - The test font and a rasterizer.
    ///
    /// Steps:
    /// - For "H" at 12, 17.5, and 32 px, step 0: rasterize the bilevel key and the antialiased key.
    ///
    /// Expected:
    /// - Every byte of the bilevel coverage is 0 or 255, and some are 255.
    /// - At 17.5 px, some byte of the antialiased coverage is between 0 and 255.
    /// </remarks>
    [Fact]
    public void Rasterize_Aliased_IsZeroOrFullCoverage()
    {
        using var font = new Font(FontBytes);
        using var rasterizer = new DirectWriteGlyphRasterizer();

        foreach (float size in (float[])[12, 17.5f, 32])
        {
            byte[] aliased = Coverage(rasterizer, new GlyphKey(font, GlyphH, size, 0, Aliased: true));
            byte[] antialiased = Coverage(rasterizer, new GlyphKey(font, GlyphH, size, 0));

            Assert.All(aliased, value => Assert.True(value is 0 or 255, $"{size} px: coverage {value}."));
            Assert.Contains((byte)255, aliased);
            Assert.True(size != 17.5f || antialiased.Any(value => value is > 0 and < 255), "17.5 px: the antialiased glyph has no partial coverage.");
        }
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

    private static byte[] Coverage(DirectWriteGlyphRasterizer rasterizer, in GlyphKey key)
    {
        GlyphBox box = rasterizer.Measure(key);
        byte[] coverage = new byte[box.Width * box.Height];
        rasterizer.Rasterize(key, coverage, box.Width);
        return coverage;
    }

    private static long Ink(DirectWriteGlyphRasterizer rasterizer, in GlyphKey key)
    {
        GlyphBox box = rasterizer.Measure(key);
        byte[] coverage = new byte[box.Width * box.Height];
        rasterizer.Rasterize(key, coverage, box.Width);
        return coverage.Sum(value => (long)value);
    }
}
