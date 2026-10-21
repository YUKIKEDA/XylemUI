namespace XylemUI.Text.Tests;

// Shaped text written as a glyph run (S8b, T4; design §5.0.3): DisplayList.DrawText puts the glyphs in the atlas and places them
// on the pen, across the line on a quarter of a pixel. The boxes come from TestRasterizer (a 32 px glyph is 26 x 26, 1 pixel
// right of the pen, its bottom on the baseline); the advances are hmtx (H 728, space 224, "日" 1000 units).
public sealed class DrawTextTests
{
    private static readonly Color Ink = Color.FromRgb(0x202020);

    /// <summary>
    /// Each glyph with ink is placed at the pen rounded to a quarter of a pixel: its quad on the whole pixel plus its bearings,
    /// with the atlas region of the glyph drawn at the quarter; a space only moves the pen; the advance of the whole text is
    /// returned.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - The test font, a shaper, the test rasterizer, an empty atlas, and an empty display list.
    ///
    /// Steps:
    /// - Shape "H H" and draw it at 32 px with the pen at x 10.25 and the baseline at 50.
    ///
    /// Expected:
    /// - The atlas was asked for "H" at step 1 (10.25 is pixel 10 and one quarter), the space at step 2 (10.25 + 728 × 0.032 =
    ///   33.546 rounds to 33.5), and "H" at step 3 (10.25 + (728 + 224) × 0.032 = 40.714 rounds to 40.75).
    /// - One glyph run of 2 glyphs, in the ink color: x 10 + 1 = 11 and 40 + 1 = 41, y 50 − 26 = 24, both 26 x 26, each with the
    ///   region of its own step.
    /// - The advance is (728 + 224 + 728) × 0.032 = 53.76.
    /// </remarks>
    [Fact]
    public void DrawText_PlacesGlyphsOnTheQuarterPixel()
    {
        using Font font = TestFont.Open();
        using var shaper = new TextShaper();
        var rasterizer = new TestRasterizer();
        var atlas = new GlyphAtlas(rasterizer);
        var list = new DisplayList();

        float advance = list.DrawText(shaper.Shape(font, "H H", TestFont.Japanese), font, 32, atlas, 10.25f, 50, Ink);

        Assert.Equal(
            [new GlyphKey(font, 41, 32, 1), new GlyphKey(font, TestRasterizer.Space, 32, 2), new GlyphKey(font, 41, 32, 3)],
            rasterizer.Measured
        );
        Assert.True(atlas.TryGet(new GlyphKey(font, 41, 32, 1), out AtlasGlyph first));
        Assert.True(atlas.TryGet(new GlyphKey(font, 41, 32, 3), out AtlasGlyph second));
        Assert.Equal(53.76f, advance, 0.001f);
        DisplayListReader reader = list.GetReader();
        Assert.True(reader.TryNext(out CommandTag tag));
        Assert.Equal(CommandTag.GlyphRun, tag);
        GlyphRunCommand run = reader.ReadGlyphRun();
        Assert.Equal(Ink, run.Color);
        Assert.Equal(
            [new GlyphInstance(11, 24, 26, 26, first.Page, first.X, first.Y), new GlyphInstance(41, 24, 26, 26, second.Page, second.X, second.Y)],
            run.Glyphs.ToArray()
        );
        Assert.False(reader.TryNext(out _));
    }

    /// <summary>
    /// The kind of coverage reaches the atlas in each glyph's key and the display list in the run.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - The test font, a shaper, the test rasterizer, an empty atlas, and an empty display list.
    ///
    /// Steps:
    /// - Draw "H" at 32 px with <see cref="GlyphCoverage.Rgb"/>, then "H" again with the default coverage.
    ///
    /// Expected:
    /// - The atlas was asked for "H" in Rgb, then in Gray; the first run is Rgb and the second Gray; both glyphs are on page 0 of
    ///   their kind.
    /// </remarks>
    [Fact]
    public void DrawText_Coverage_GoesToTheKeysAndTheRun()
    {
        using Font font = TestFont.Open();
        using var shaper = new TextShaper();
        var rasterizer = new TestRasterizer();
        var atlas = new GlyphAtlas(rasterizer);
        var list = new DisplayList();

        _ = list.DrawText(shaper.Shape(font, "H", TestFont.Japanese), font, 32, atlas, 0, 50, Ink, GlyphCoverage.Rgb);
        _ = list.DrawText(shaper.Shape(font, "H", TestFont.Japanese), font, 32, atlas, 0, 50, Ink);

        Assert.Equal([new GlyphKey(font, 41, 32, 0, GlyphCoverage.Rgb), new GlyphKey(font, 41, 32, 0)], rasterizer.Measured);
        DisplayListReader reader = list.GetReader();
        Assert.True(reader.TryNext(out _));
        GlyphRunCommand clearType = reader.ReadGlyphRun();
        Assert.Equal(GlyphCoverage.Rgb, clearType.Coverage);
        Assert.Equal(0, clearType.Glyphs[0].Page);
        Assert.True(reader.TryNext(out _));
        GlyphRunCommand gray = reader.ReadGlyphRun();
        Assert.Equal(GlyphCoverage.Gray, gray.Coverage);
        Assert.Equal(0, gray.Glyphs[0].Page);
        Assert.False(reader.TryNext(out _));
    }

    /// <summary>
    /// A size with a fraction reaches the atlas unrounded, and the advance is scaled by the same size.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - The test font, a shaper, the test rasterizer, an empty atlas, and an empty display list.
    ///
    /// Steps:
    /// - Draw "日" at 17.5 px (14 px at 125 %) with the pen at 0.
    ///
    /// Expected:
    /// - The atlas was asked for "日" at 17.5 px; the advance is 1000 × 0.0175 = 17.5.
    /// </remarks>
    [Fact]
    public void DrawText_FractionalSize_IsNotRounded()
    {
        using Font font = TestFont.Open();
        using var shaper = new TextShaper();
        var rasterizer = new TestRasterizer();
        var atlas = new GlyphAtlas(rasterizer);
        var list = new DisplayList();

        float advance = list.DrawText(shaper.Shape(font, "日", TestFont.Japanese), font, 17.5f, atlas, 0, 40, Ink);

        Assert.Equal(17.5f, Assert.Single(rasterizer.Measured).PixelSize);
        Assert.Equal(17.5f, advance, 0.001f);
    }

    /// <summary>
    /// Text with no ink appends no command, and a glyph larger than a plot is left out while the pen still moves past it.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - The test font, a shaper, the test rasterizer with "." 8 px square, an atlas of 16 px plots, and an empty display list.
    ///
    /// Steps:
    /// - Draw "  " (two spaces) at 32 px with the pen at 0.
    /// - Draw "日." at 32 px with the pen at 0: "日" (26 x 26) is larger than a plot, "." is not.
    ///
    /// Expected:
    /// - The spaces: no command, and the advance is 2 × 224 × 0.032 = 14.336.
    /// - "日.": one run with only ".", at x 32 (the advance of "日", 1000 × 0.032) plus the bearing of ".".
    /// </remarks>
    [Fact]
    public void DrawText_NoInkOrTooLarge_LeavesTheGlyphOut()
    {
        using Font font = TestFont.Open();
        using var shaper = new TextShaper();
        uint period = shaper.Shape(font, ".", TestFont.Japanese)[0].GlyphId;
        var rasterizer = new TestRasterizer();
        rasterizer.Sides[period] = 8;
        var atlas = new GlyphAtlas(rasterizer, pageSize: 64, plotSize: 16, maxPages: 1);
        var list = new DisplayList();

        float spaces = list.DrawText(shaper.Shape(font, "  ", TestFont.Japanese), font, 32, atlas, 0, 40, Ink);
        Assert.Equal(14.336f, spaces, 0.001f);
        Assert.Equal(0, list.CommandCount);

        _ = list.DrawText(shaper.Shape(font, "日.", TestFont.Japanese), font, 32, atlas, 0, 40, Ink);
        Assert.True(atlas.TryGet(new GlyphKey(font, period, 32, 0), out AtlasGlyph dot));
        DisplayListReader reader = list.GetReader();
        Assert.True(reader.TryNext(out _));
        GlyphInstance glyph = Assert.Single(reader.ReadGlyphRun().Glyphs.ToArray());
        Assert.Equal(32 + dot.BearingX, glyph.X);
        Assert.False(reader.TryNext(out _));
    }

    /// <summary>
    /// A size that is not a positive number is refused.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - The test font, a shaper, the test rasterizer, an atlas, and a display list.
    ///
    /// Steps:
    /// - Draw "H" at 0, −1, NaN, and infinity.
    ///
    /// Expected:
    /// - Each throws <see cref="ArgumentOutOfRangeException"/>.
    /// </remarks>
    [Fact]
    public void DrawText_SizeNotPositive_Throws()
    {
        using Font font = TestFont.Open();
        using var shaper = new TextShaper();
        var atlas = new GlyphAtlas(new TestRasterizer());
        var list = new DisplayList();
        ShapedGlyph[] glyphs = shaper.Shape(font, "H", TestFont.Japanese).ToArray();

        foreach (float size in new[] { 0f, -1f, float.NaN, float.PositiveInfinity })
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => list.DrawText(glyphs, font, size, atlas, 0, 40, Ink));
        }
    }
}
