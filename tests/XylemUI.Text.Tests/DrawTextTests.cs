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
    /// A color glyph in a line has a run of its own, between the runs of the other glyphs, and sits on the nearest whole pixel; it
    /// is asked for with the text's color.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - The test font, a shaper, the test rasterizer with "日" (glyph 240) as a color glyph, an empty atlas, and an empty display
    ///   list.
    ///
    /// Steps:
    /// - Draw "H日H" at 32 px with the pen at 10.25 and the baseline at 50, in ink.
    ///
    /// Expected:
    /// - Three runs: Gray ("H"), Color ("日"), Gray ("H"), all in ink. "日" is at 10.25 + 728 × 0.032 = 33.546, which rounds to 34:
    ///   x 34 + 1 = 35; its region is the color glyph at step 0.
    /// </remarks>
    [Fact]
    public void DrawText_ColorGlyph_HasItsOwnRunOnAWholePixel()
    {
        using Font font = TestFont.Open();
        using var shaper = new TextShaper();
        var rasterizer = new TestRasterizer();
        rasterizer.ColorGlyphs.Add(240, false);
        var atlas = new GlyphAtlas(rasterizer);
        var list = new DisplayList();

        _ = list.DrawText(shaper.Shape(font, "H日H", TestFont.Japanese), font, 32, atlas, 10.25f, 50, Ink);

        Assert.True(atlas.TryGet(new GlyphKey(font, 240, 32, 0, GlyphCoverage.Color), out AtlasGlyph nichi));
        DisplayListReader reader = list.GetReader();
        var kinds = new List<GlyphCoverage>();
        GlyphInstance color = default;
        while (reader.TryNext(out _))
        {
            GlyphRunCommand run = reader.ReadGlyphRun();
            Assert.Equal(Ink, run.Color);
            Assert.Single(run.Glyphs.ToArray());
            kinds.Add(run.Coverage);
            color = run.Coverage == GlyphCoverage.Color ? run.Glyphs[0] : color;
        }

        Assert.Equal([GlyphCoverage.Gray, GlyphCoverage.Color, GlyphCoverage.Gray], kinds);
        Assert.Equal(new GlyphInstance(35, 50 - nichi.BearingY, nichi.Width, nichi.Height, nichi.Page, nichi.X, nichi.Y), color);
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

    /// <summary>
    /// Bilevel text puts each glyph on the nearest whole pixel with subpixel step 0, so a glyph at two fractional pens is one
    /// glyph of the atlas, and keeps the advances: it is as wide as antialiased text (design §5.0.3, "When fonts are not
    /// smoothed").
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - The test font, a shaper, the test rasterizer, an empty atlas, and an empty display list.
    ///
    /// Steps:
    /// - Shape "H H" and draw it bilevel at 32 px with the pen at x 10.25 and the baseline at 50; draw it again antialiased.
    ///
    /// Expected:
    /// - The atlas was asked for "H" bilevel at step 0 and the space bilevel at step 0, once each (the second "H", at 40.714, is
    ///   the same glyph as the first, at 10.25), then for the antialiased glyphs.
    /// - The bilevel run is Gray, with "H" at x round(10.25) + 1 = 11 and round(40.714) + 1 = 42, y 50 − 26 = 24, both with the
    ///   region of the one bilevel glyph.
    /// - Both draws return the advance (728 + 224 + 728) × 0.032 = 53.76.
    /// </remarks>
    [Fact]
    public void DrawText_Aliased_PlacesGlyphsOnWholePixelsWithTheSameAdvance()
    {
        using Font font = TestFont.Open();
        using var shaper = new TextShaper();
        var rasterizer = new TestRasterizer();
        var atlas = new GlyphAtlas(rasterizer);
        var list = new DisplayList();
        ShapedGlyph[] glyphs = shaper.Shape(font, "H H", TestFont.Japanese).ToArray();

        float aliased = list.DrawText(glyphs, font, 32, atlas, 10.25f, 50, Ink, aliased: true);
        float antialiased = list.DrawText(glyphs, font, 32, atlas, 10.25f, 50, Ink);

        var h = new GlyphKey(font, 41, 32, 0, Aliased: true);
        Assert.Equal([h, new GlyphKey(font, TestRasterizer.Space, 32, 0, Aliased: true)], rasterizer.Measured.Take(2));
        Assert.DoesNotContain(rasterizer.Measured.Skip(2), key => key.Aliased);
        Assert.True(atlas.TryGet(h, out AtlasGlyph glyph));
        DisplayListReader reader = list.GetReader();
        Assert.True(reader.TryNext(out _));
        GlyphRunCommand run = reader.ReadGlyphRun();
        Assert.Equal(GlyphCoverage.Gray, run.Coverage);
        Assert.Equal(
            [new GlyphInstance(11, 24, 26, 26, glyph.Page, glyph.X, glyph.Y), new GlyphInstance(42, 24, 26, 26, glyph.Page, glyph.X, glyph.Y)],
            run.Glyphs.ToArray()
        );
        Assert.Equal(53.76f, aliased, 0.001f);
        Assert.Equal(antialiased, aliased);
    }

    /// <summary>
    /// Bilevel text with ClearType coverage is refused: the window gives bilevel text grayscale coverage.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - The test font, a shaper, the test rasterizer, an atlas, and a display list.
    ///
    /// Steps:
    /// - Draw "H" bilevel with <see cref="GlyphCoverage.Rgb"/>.
    ///
    /// Expected:
    /// - It throws <see cref="ArgumentException"/> for the parameter <c>aliased</c>; the rasterizer measured nothing.
    /// </remarks>
    [Fact]
    public void DrawText_AliasedClearType_Throws()
    {
        using Font font = TestFont.Open();
        using var shaper = new TextShaper();
        var rasterizer = new TestRasterizer();
        var atlas = new GlyphAtlas(rasterizer);
        var list = new DisplayList();
        ShapedGlyph[] glyphs = shaper.Shape(font, "H", TestFont.Japanese).ToArray();

        ArgumentException thrown = Assert.Throws<ArgumentException>(() =>
            list.DrawText(glyphs, font, 32, atlas, 0, 40, Ink, GlyphCoverage.Rgb, aliased: true)
        );

        Assert.Equal("aliased", thrown.ParamName);
        Assert.Empty(rasterizer.Measured);
    }
}
