namespace XylemUI.Text.Tests;

// The CPU glyph atlas: pages, plots, and LRU eviction, with pages of each kind of coverage (S8a, S8b, T4, T2; design
// §5.0.2, §5.0.3). The glyphs come from
// TestRasterizer, whose boxes and coverage follow from the key: at 24 px a glyph is 19 x 19, at 32 px 26 x 26.
public sealed class GlyphAtlasTests
{
    private const uint GlyphH = 41;
    private const uint GlyphNichi = 240;

    /// <summary>
    /// A glyph's bearings are its box's, and the rasterizer writes its coverage into its region, row after row at the page's
    /// stride, with the padding around it left at 0.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - The test font, the test rasterizer, and an empty atlas of one 256 px page.
    ///
    /// Steps:
    /// - Get "H" (glyph 41) at 32 px and "日" (glyph 240) at 24 px.
    ///
    /// Expected:
    /// - "H": 26 x 26, bearings (1, 26); "日": 19 x 19, bearings (1, 19).
    /// - Every pixel of each region is the test rasterizer's value for that key, row, and column; the ring of pixels around each
    ///   region is 0.
    /// </remarks>
    [Fact]
    public void TryGet_Coverage_IsWrittenIntoTheRegionAtThePageStride()
    {
        using Font font = TestFont.Open();
        var atlas = new GlyphAtlas(new TestRasterizer(), pageSize: 256, plotSize: 128, maxPages: 1);
        var keyH = new GlyphKey(font, GlyphH, 32, 0);
        var keyNichi = new GlyphKey(font, GlyphNichi, 24, 0);

        Assert.True(atlas.TryGet(keyH, out AtlasGlyph h));
        Assert.True(atlas.TryGet(keyNichi, out AtlasGlyph nichi));

        Assert.Equal((26, 26, 1, 26), (h.Width, h.Height, h.BearingX, h.BearingY));
        Assert.Equal((19, 19, 1, 19), (nichi.Width, nichi.Height, nichi.BearingX, nichi.BearingY));
        foreach ((GlyphKey key, AtlasGlyph glyph) in new[] { (keyH, h), (keyNichi, nichi) })
        {
            ReadOnlySpan<byte> pixels = atlas.GetPagePixels(GlyphCoverage.Gray, glyph.Page);
            for (int row = 0; row < glyph.Height; row++)
            {
                for (int column = 0; column < glyph.Width; column++)
                {
                    Assert.Equal(TestRasterizer.Value(key, row, column), pixels[((glyph.Y + row) * atlas.PageSize) + glyph.X + column]);
                }
            }

            Assert.Equal(0, MaxOnBorder(atlas, glyph));
        }
    }

    /// <summary>
    /// The cache is keyed by the subpixel step and by the exact size: the same glyph at another quarter of a pixel, or at a size
    /// with a fraction, is rasterized on its own with that key, and found again.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - The test font, the test rasterizer, and an empty atlas.
    ///
    /// Steps:
    /// - Get "H" at 17.5 px with steps 0 to 3, then at 17 px and 18 px with step 0; then all six again.
    ///
    /// Expected:
    /// - Six rasterizations, each measured with its own key (17.5 reaches the rasterizer unrounded), and six regions whose
    ///   pixels differ; the repeated requests rasterize nothing.
    /// </remarks>
    [Fact]
    public void TryGet_SubpixelStepAndFractionalSize_AreCachedSeparately()
    {
        using Font font = TestFont.Open();
        var rasterizer = new TestRasterizer();
        var atlas = new GlyphAtlas(rasterizer, pageSize: 256, plotSize: 128, maxPages: 1);
        GlyphKey[] keys =
        [
            new(font, GlyphH, 17.5f, 0),
            new(font, GlyphH, 17.5f, 1),
            new(font, GlyphH, 17.5f, 2),
            new(font, GlyphH, 17.5f, 3),
            new(font, GlyphH, 17f, 0),
            new(font, GlyphH, 18f, 0),
        ];

        var first = new List<AtlasGlyph>();
        foreach (GlyphKey key in keys)
        {
            Assert.True(atlas.TryGet(key, out AtlasGlyph glyph));
            first.Add(glyph);
        }

        Assert.Equal(keys, rasterizer.Measured);
        Assert.Equal(6, first.Select(glyph => (glyph.X, glyph.Y)).Distinct().Count());
        Assert.Equal(
            6,
            first.Select(glyph => atlas.GetPagePixels(GlyphCoverage.Gray, glyph.Page)[(glyph.Y * atlas.PageSize) + glyph.X]).Distinct().Count()
        );
        foreach (GlyphKey key in keys)
        {
            Assert.True(atlas.TryGet(key, out _));
        }

        Assert.Equal(6, atlas.RasterizeCount);
    }

    /// <summary>
    /// A key the atlas cannot rasterize is refused before the rasterizer sees it.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - The test font, the test rasterizer, and an atlas.
    ///
    /// Steps:
    /// - Get "H" with no font, at sizes 0, −1, and NaN, and with subpixel steps −1 and 4.
    ///
    /// Expected:
    /// - Each throws <see cref="ArgumentException"/>; the rasterizer measured nothing.
    /// </remarks>
    [Fact]
    public void TryGet_KeyOutOfRange_Throws()
    {
        using Font font = TestFont.Open();
        var rasterizer = new TestRasterizer();
        var atlas = new GlyphAtlas(rasterizer);

        GlyphKey[] keys =
        [
            new(null!, GlyphH, 32, 0),
            new(font, GlyphH, 0, 0),
            new(font, GlyphH, -1, 0),
            new(font, GlyphH, float.NaN, 0),
            new(font, GlyphH, 32, -1),
            new(font, GlyphH, 32, 4),
            new(font, GlyphH, 32, 0, (GlyphCoverage)3),
        ];
        foreach (GlyphKey key in keys)
        {
            Assert.Throws<ArgumentException>(() => atlas.TryGet(key, out _));
        }

        Assert.Empty(rasterizer.Measured);
    }

    /// <summary>
    /// The glyphs of a text are packed without overlapping, each inside its page and its plot.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - The test font, a shaper, and an atlas of 256 px pages with 64 px plots.
    ///
    /// Steps:
    /// - Get every glyph of "Hello, World! 日本語のテキスト" at 24 px.
    ///
    /// Expected:
    /// - Every request succeeds; every region lies inside its page and inside one plot; no two regions on a page overlap.
    /// </remarks>
    [Fact]
    public void TryGet_ManyGlyphs_PackWithoutOverlap()
    {
        using Font font = TestFont.Open();
        using var shaper = new TextShaper();
        var atlas = new GlyphAtlas(new TestRasterizer(), pageSize: 256, plotSize: 64, maxPages: 2);
        var regions = new List<AtlasGlyph>();

        foreach (ShapedGlyph shaped in shaper.Shape(font, "Hello, World! 日本語のテキスト", TestFont.Japanese))
        {
            Assert.True(atlas.TryGet(new GlyphKey(font, shaped.GlyphId, 24, 0), out AtlasGlyph glyph));
            if (glyph.Width > 0 && !regions.Contains(glyph))
            {
                regions.Add(glyph);
            }
        }

        foreach (AtlasGlyph a in regions)
        {
            Assert.InRange(a.X, 0, atlas.PageSize - a.Width);
            Assert.InRange(a.Y, 0, atlas.PageSize - a.Height);
            Assert.Equal(a.X / atlas.PlotSize, (a.X + a.Width - 1) / atlas.PlotSize);
            Assert.Equal(a.Y / atlas.PlotSize, (a.Y + a.Height - 1) / atlas.PlotSize);
            foreach (AtlasGlyph b in regions)
            {
                bool overlap =
                    a != b && a.Page == b.Page && a.X < b.X + b.Width && b.X < a.X + a.Width && a.Y < b.Y + b.Height && b.Y < a.Y + a.Height;
                Assert.False(overlap, $"{a} overlaps {b}");
            }
        }
    }

    /// <summary>
    /// A glyph asked for again comes from the cache: the same region, no new rasterization, no new dirty region. A glyph with no
    /// ink takes no dirty region.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - The test font and an empty atlas.
    ///
    /// Steps:
    /// - Get "H" and the space (glyph 1) at 32 px; clear the dirty regions; get both again, and "H" at 16 px.
    ///
    /// Expected:
    /// - The first two requests rasterized twice and left one dirty region (the space has no ink), the one of "H".
    /// - The repeated requests returned the same regions without rasterizing; "H" at 16 px is a new glyph, rasterized once, with
    ///   one new dirty region.
    /// </remarks>
    [Fact]
    public void TryGet_Again_ComesFromTheCache()
    {
        using Font font = TestFont.Open();
        var atlas = new GlyphAtlas(new TestRasterizer(), pageSize: 256, plotSize: 128, maxPages: 1);

        Assert.True(atlas.TryGet(new GlyphKey(font, GlyphH, 32, 0), out AtlasGlyph h));
        Assert.True(atlas.TryGet(new GlyphKey(font, 1, 32, 0), out AtlasGlyph space));
        Assert.Equal(2, atlas.RasterizeCount);
        Assert.Equal(new AtlasRegion(h.Page, h.X, h.Y, h.Width, h.Height), Assert.Single(atlas.DirtyRegions.ToArray()));
        Assert.Equal(0, space.Width);
        atlas.ClearDirtyRegions();

        Assert.True(atlas.TryGet(new GlyphKey(font, GlyphH, 32, 0), out AtlasGlyph again));
        Assert.True(atlas.TryGet(new GlyphKey(font, 1, 32, 0), out _));
        Assert.Equal(h, again);
        Assert.Equal(2, atlas.RasterizeCount);
        Assert.Empty(atlas.DirtyRegions.ToArray());

        Assert.True(atlas.TryGet(new GlyphKey(font, GlyphH, 16, 0), out AtlasGlyph small));
        Assert.NotEqual(h, small);
        Assert.Equal(3, atlas.RasterizeCount);
        Assert.Single(atlas.DirtyRegions.ToArray());
    }

    /// <summary>
    /// The cache is keyed by font, glyph, and size: the same glyph in another font or at another size is rasterized on its own,
    /// into its own region.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - Two fonts opened from the same bytes (two ids) and an empty atlas.
    ///
    /// Steps:
    /// - Get "H" in font A at 32 px, in font B at 32 px, and in font A at 16 px; then all three again.
    ///
    /// Expected:
    /// - Three rasterizations and three different regions; the repeated requests rasterize nothing and return the same regions.
    /// </remarks>
    [Fact]
    public void TryGet_SameGlyphInAnotherFontOrSize_IsCachedSeparately()
    {
        using Font a = TestFont.Open();
        using Font b = TestFont.Open();
        var atlas = new GlyphAtlas(new TestRasterizer(), pageSize: 256, plotSize: 128, maxPages: 1);

        Assert.True(atlas.TryGet(new GlyphKey(a, GlyphH, 32, 0), out AtlasGlyph a32));
        Assert.True(atlas.TryGet(new GlyphKey(b, GlyphH, 32, 0), out AtlasGlyph b32));
        Assert.True(atlas.TryGet(new GlyphKey(a, GlyphH, 16, 0), out AtlasGlyph a16));
        Assert.Equal(3, atlas.RasterizeCount);
        Assert.Equal(3, new[] { (a32.X, a32.Y), (b32.X, b32.Y), (a16.X, a16.Y) }.Distinct().Count());

        Assert.True(atlas.TryGet(new GlyphKey(a, GlyphH, 32, 0), out AtlasGlyph a32Again));
        Assert.True(atlas.TryGet(new GlyphKey(b, GlyphH, 32, 0), out AtlasGlyph b32Again));
        Assert.True(atlas.TryGet(new GlyphKey(a, GlyphH, 16, 0), out AtlasGlyph a16Again));
        Assert.Equal(3, atlas.RasterizeCount);
        Assert.Equal((a32, b32, a16), (a32Again, b32Again, a16Again));
    }

    /// <summary>
    /// A glyph with no ink takes no room in the atlas: the next glyph lands where it would have without it. It is rasterized
    /// once, like any glyph.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - The test font and an empty atlas.
    ///
    /// Steps:
    /// - Get the space (glyph 1) at 32 px twice, then "H" at 32 px.
    ///
    /// Expected:
    /// - The space has a 0 x 0 region and was rasterized once; no page was made for it; "H" is at (1, 1) of page 0, the first
    ///   spot after the padding.
    /// </remarks>
    [Fact]
    public void TryGet_BlankGlyph_TakesNoRoom()
    {
        using Font font = TestFont.Open();
        var atlas = new GlyphAtlas(new TestRasterizer(), pageSize: 256, plotSize: 128, maxPages: 1);

        Assert.True(atlas.TryGet(new GlyphKey(font, 1, 32, 0), out AtlasGlyph space));
        Assert.True(atlas.TryGet(new GlyphKey(font, 1, 32, 0), out _));
        Assert.Equal(0, atlas.GetPageCount(GlyphCoverage.Gray));
        Assert.True(atlas.TryGet(new GlyphKey(font, GlyphH, 32, 0), out AtlasGlyph h));

        Assert.Equal((0, 0), (space.Width, space.Height));
        Assert.Equal(2, atlas.RasterizeCount);
        Assert.Equal((0, 1, 1), (h.Page, h.X, h.Y));
    }

    /// <summary>
    /// A glyph larger than a plot does not fit the atlas, and asking for it takes no room.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - The test font and an atlas of 16 px plots.
    ///
    /// Steps:
    /// - Get "日" at 32 px (26 x 26, larger than a plot), twice; then "." at 12 px (10 x 10).
    ///
    /// Expected:
    /// - Both requests for "日" return false; "." lands at (1, 1) of page 0.
    /// </remarks>
    [Fact]
    public void TryGet_GlyphLargerThanAPlot_DoesNotFit()
    {
        using Font font = TestFont.Open();
        using var shaper = new TextShaper();
        var atlas = new GlyphAtlas(new TestRasterizer(), pageSize: 64, plotSize: 16, maxPages: 1);
        uint period = shaper.Shape(font, ".", TestFont.Japanese)[0].GlyphId;

        Assert.False(atlas.TryGet(new GlyphKey(font, GlyphNichi, 32, 0), out _));
        Assert.False(atlas.TryGet(new GlyphKey(font, GlyphNichi, 32, 0), out _));
        Assert.True(atlas.TryGet(new GlyphKey(font, period, 12, 0), out AtlasGlyph dot));
        Assert.Equal((0, 1, 1), (dot.Page, dot.X, dot.Y));
    }

    /// <summary>
    /// When the atlas is full, the plot used longest ago and not in the current frame is emptied and reused; its glyph is
    /// rasterized again when it is next asked for. A glyph found in the cache counts as a use of its plot.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - The test font and an atlas of one 64 px page with 32 px plots (4 plots), at most 1 page. A glyph at 24 px (19 x 19) leaves
    ///   no room for a second one in a plot, so the 4 plots hold 4 glyphs: G0..G3.
    ///
    /// Steps:
    /// - Frame 1: get G0, G1, G2, G3. Frame 2: get G2 and G3 (from the cache). Frame 3: get G0 (from the cache), then G4.
    ///
    /// Expected:
    /// - G4 took G1's region: G1's plot was last used in frame 1, G2's and G3's in frame 2, and G0's in frame 3 (the current
    ///   one); one eviction; one page still.
    /// - The evicted plot is dirty as a whole (32 x 32) and its pixels outside G4 are 0.
    /// - Getting G1 again rasterizes it again; G0, G2, and G3 are still cached.
    /// </remarks>
    [Fact]
    public void TryGet_FullAtlas_EvictsTheLeastRecentlyUsedPlot()
    {
        using Font font = TestFont.Open();
        var atlas = new GlyphAtlas(new TestRasterizer(), pageSize: 64, plotSize: 32, maxPages: 1);
        uint[] g = JapaneseGlyphs(font);
        var regions = new AtlasGlyph[4];
        for (int i = 0; i < 4; i++)
        {
            Assert.True(atlas.TryGet(new GlyphKey(font, g[i], 24, 0), out regions[i]));
        }

        atlas.BeginFrame();
        _ = atlas.TryGet(new GlyphKey(font, g[2], 24, 0), out _);
        _ = atlas.TryGet(new GlyphKey(font, g[3], 24, 0), out _);
        atlas.BeginFrame();
        atlas.ClearDirtyRegions();
        _ = atlas.TryGet(new GlyphKey(font, g[0], 24, 0), out _);
        int rasterized = atlas.RasterizeCount;
        Assert.True(atlas.TryGet(new GlyphKey(font, g[4], 24, 0), out AtlasGlyph g4));

        Assert.Equal((regions[1].Page, regions[1].X, regions[1].Y), (g4.Page, g4.X, g4.Y));
        Assert.Equal(1, atlas.EvictionCount);
        Assert.Equal(1, atlas.GetPageCount(GlyphCoverage.Gray));
        Assert.Contains(new AtlasRegion(0, regions[1].X - 1, regions[1].Y - 1, 32, 32), atlas.DirtyRegions.ToArray());
        Assert.Equal(0, MaxInPlotOutside(atlas, g4));

        atlas.BeginFrame();
        foreach (int i in new[] { 0, 2, 3 })
        {
            _ = atlas.TryGet(new GlyphKey(font, g[i], 24, 0), out _);
        }

        Assert.Equal(rasterized + 1, atlas.RasterizeCount);
        Assert.True(atlas.TryGet(new GlyphKey(font, g[1], 24, 0), out _));
        Assert.Equal(rasterized + 2, atlas.RasterizeCount);
    }

    /// <summary>
    /// When every plot was used in the current frame, a page is added beyond the maximum; it is dropped once a frame no longer
    /// uses it, and its glyphs are forgotten.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - The test font and an atlas of one 64 px page with 32 px plots, at most 1 page.
    ///
    /// Steps:
    /// - Frame 1: get G0..G4 (5 glyphs for 4 plots, all in the same frame). Frame 2: get G0..G3 only. Frame 3: get G4, then G0..G7.
    ///
    /// Expected:
    /// - After frame 1, 2 pages and no eviction (G4 is on page 1).
    /// - Frame 2 starts with 2 pages (page 1 was used in frame 1); frame 3 starts with 1 page (page 1 was not used in frame 2).
    /// - Getting G4 in frame 3 rasterizes it again, on page 0 by evicting a plot not used in frame 3.
    /// - Getting G0..G7 in frame 3 (8 glyphs for 4 plots) adds page 1 again, with another id than the dropped one; page 0
    ///   keeps its id.
    /// </remarks>
    [Fact]
    public void TryGet_EveryPlotUsedThisFrame_AddsAPageThatIsDroppedLater()
    {
        using Font font = TestFont.Open();
        var atlas = new GlyphAtlas(new TestRasterizer(), pageSize: 64, plotSize: 32, maxPages: 1);
        uint[] g = JapaneseGlyphs(font);
        for (int i = 0; i < 5; i++)
        {
            Assert.True(atlas.TryGet(new GlyphKey(font, g[i], 24, 0), out _));
        }

        Assert.True(atlas.TryGet(new GlyphKey(font, g[4], 24, 0), out AtlasGlyph g4));
        Assert.Equal(1, g4.Page);
        Assert.Equal((2, 0), (atlas.GetPageCount(GlyphCoverage.Gray), atlas.EvictionCount));
        (long page0, long page1) = (atlas.GetPageId(GlyphCoverage.Gray, 0), atlas.GetPageId(GlyphCoverage.Gray, 1));

        atlas.BeginFrame();
        Assert.Equal(2, atlas.GetPageCount(GlyphCoverage.Gray));
        for (int i = 0; i < 4; i++)
        {
            _ = atlas.TryGet(new GlyphKey(font, g[i], 24, 0), out _);
        }

        atlas.BeginFrame();
        Assert.Equal(1, atlas.GetPageCount(GlyphCoverage.Gray));
        int rasterized = atlas.RasterizeCount;
        Assert.True(atlas.TryGet(new GlyphKey(font, g[4], 24, 0), out AtlasGlyph again));
        Assert.Equal(rasterized + 1, atlas.RasterizeCount);
        Assert.Equal(0, again.Page);

        for (int i = 0; i < 8; i++)
        {
            _ = atlas.TryGet(new GlyphKey(font, g[i], 24, 0), out _);
        }

        Assert.Equal(2, atlas.GetPageCount(GlyphCoverage.Gray));
        Assert.Equal(page0, atlas.GetPageId(GlyphCoverage.Gray, 0));
        Assert.NotEqual(page1, atlas.GetPageId(GlyphCoverage.Gray, 1));
    }

    /// <summary>
    /// The atlas never holds more than PageLimit pages: when every plot was used in the current frame and the limit is reached,
    /// a new glyph is refused, also for a caller that never starts a frame.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - The test font and an atlas of one 64 px page with 32 px plots, at most 1 page (PageLimit 2: 8 plots). The 8 Japanese
    ///   glyphs of the test font at 24 px (19 x 19) take a plot each.
    ///
    /// Steps:
    /// - Without calling BeginFrame, get the 8 Japanese glyphs, then "W" (glyph 56) at 24 px, twice.
    ///
    /// Expected:
    /// - PageLimit is 2; the 8 glyphs fit on 2 pages; "W" is refused both times (RefusedCount 2, and it was rasterized both
    ///   times, since a refused glyph is not cached), the atlas stays at 2 pages, and nothing was evicted.
    /// - After BeginFrame, "W" fits by evicting a plot of the frame before.
    /// </remarks>
    [Fact]
    public void TryGet_AtThePageLimit_RefusesTheGlyph()
    {
        using Font font = TestFont.Open();
        var atlas = new GlyphAtlas(new TestRasterizer(), pageSize: 64, plotSize: 32, maxPages: 1);
        uint[] g = JapaneseGlyphs(font);
        Assert.Equal(8, g.Length);
        foreach (uint glyph in g)
        {
            Assert.True(atlas.TryGet(new GlyphKey(font, glyph, 24, 0), out _));
        }

        Assert.Equal(2, atlas.PageLimit);
        Assert.Equal(2, atlas.GetPageCount(GlyphCoverage.Gray));
        int rasterized = atlas.RasterizeCount;
        Assert.False(atlas.TryGet(new GlyphKey(font, 56, 24, 0), out _));
        Assert.False(atlas.TryGet(new GlyphKey(font, 56, 24, 0), out _));
        Assert.Equal((2, 0), (atlas.GetPageCount(GlyphCoverage.Gray), atlas.EvictionCount));
        Assert.Equal(2, atlas.RefusedCount);
        Assert.Equal(rasterized + 2, atlas.RasterizeCount);

        atlas.BeginFrame();
        Assert.True(atlas.TryGet(new GlyphKey(font, 56, 24, 0), out _));
        Assert.Equal(1, atlas.EvictionCount);
    }

    /// <summary>
    /// A ClearType glyph goes to a page of its own kind, four bytes a pixel: red, green, and blue from the rasterizer at the
    /// page's stride, the fourth byte 0; its dirty region names the kind.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - The test font, the test rasterizer, and an empty atlas of 256 px grayscale pages with 128 px plots.
    ///
    /// Steps:
    /// - Get "H" at 32 px with <see cref="GlyphCoverage.Rgb"/>.
    ///
    /// Expected:
    /// - One Rgb page and no Gray page; the page is 128 px, half the grayscale page, and 128 × 128 × 4 bytes.
    /// - Every pixel of the region holds the rasterizer's red, green, and blue, and 0 in the fourth byte; the ring around the region
    ///   is 0.
    /// - One dirty region, on page 0 of the Rgb kind, the glyph's region.
    /// </remarks>
    [Fact]
    public void TryGet_ClearType_IsWrittenOnItsOwnPagesAtFourBytesAPixel()
    {
        using Font font = TestFont.Open();
        var atlas = new GlyphAtlas(new TestRasterizer(), pageSize: 256, plotSize: 128, maxPages: 1);
        var key = new GlyphKey(font, GlyphH, 32, 0, GlyphCoverage.Rgb);

        Assert.True(atlas.TryGet(key, out AtlasGlyph h));

        Assert.Equal((1, 0), (atlas.GetPageCount(GlyphCoverage.Rgb), atlas.GetPageCount(GlyphCoverage.Gray)));
        ReadOnlySpan<byte> pixels = atlas.GetPagePixels(GlyphCoverage.Rgb, h.Page);
        Assert.Equal((128, 128 * 128 * 4), (atlas.GetPageSize(GlyphCoverage.Rgb), pixels.Length));
        int stride = atlas.GetPageSize(GlyphCoverage.Rgb) * 4;
        for (int y = h.Y - 1; y <= h.Y + h.Height; y++)
        {
            for (int x = h.X - 1; x <= h.X + h.Width; x++)
            {
                (int row, int column) = (y - h.Y, x - h.X);
                bool inside = row >= 0 && column >= 0 && row < h.Height && column < h.Width;
                byte[] expected = inside
                    ?
                    [
                        TestRasterizer.Value(key, row, column),
                        TestRasterizer.Channel(key, row, column, 1),
                        TestRasterizer.Channel(key, row, column, 2),
                        0,
                    ]
                    : [0, 0, 0, 0];
                Assert.Equal(expected, pixels.Slice((y * stride) + (x * 4), 4).ToArray());
            }
        }

        Assert.Equal(new AtlasRegion(0, h.X, h.Y, h.Width, h.Height, GlyphCoverage.Rgb), Assert.Single(atlas.DirtyRegions.ToArray()));
    }

    /// <summary>
    /// The same glyph in grayscale and in ClearType is two glyphs of the atlas, each rasterized once, each on page 0 of its kind.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - The test font, the test rasterizer, and an empty atlas.
    ///
    /// Steps:
    /// - Get "H" at 32 px in Gray, then in Rgb, then both again.
    ///
    /// Expected:
    /// - The rasterizer measured the Gray key and the Rgb key, once each; both regions are on page 0 (of their own kind); the
    ///   second requests return the same regions.
    /// </remarks>
    [Fact]
    public void TryGet_GrayAndClearType_AreCachedSeparately()
    {
        using Font font = TestFont.Open();
        var rasterizer = new TestRasterizer();
        var atlas = new GlyphAtlas(rasterizer);
        var gray = new GlyphKey(font, GlyphH, 32, 0);
        var rgb = gray with { Coverage = GlyphCoverage.Rgb };

        Assert.True(atlas.TryGet(gray, out AtlasGlyph first));
        Assert.True(atlas.TryGet(rgb, out AtlasGlyph second));
        Assert.True(atlas.TryGet(gray, out AtlasGlyph firstAgain));
        Assert.True(atlas.TryGet(rgb, out AtlasGlyph secondAgain));

        Assert.Equal([gray, rgb], rasterizer.Measured);
        Assert.Equal((0, 0), (first.Page, second.Page));
        Assert.Equal((first, second), (firstAgain, secondAgain));
    }

    /// <summary>
    /// Each kind's pages are bounded and evicted on their own: ClearType glyphs that fill their kind evict ClearType plots and
    /// never a grayscale one; after the window changes its kind, the glyphs of the old kind stay until their own kind needs room.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - The test font and an atlas of 64 px grayscale pages with 32 px plots (4 plots), at most 1 grayscale page, so at most 4
    ///   ClearType pages of 32 px (1 plot each). At 24 px a glyph fills a plot.
    ///
    /// Steps:
    /// - Frame 1: get G0..G3 in Gray. Frame 2: get G0..G4 in Rgb (as if the window turned to ClearType). Frame 3: get G0..G3 in
    ///   Gray, then G4 in Gray.
    ///
    /// Expected:
    /// - Frame 2: 5 Rgb pages, one beyond the Rgb maximum of 4 (an Rgb page is 32 px, one plot; 5 glyphs, every plot used in the
    ///   frame), no eviction, and the Gray page kept.
    /// - Frame 3: G0..G3 in Gray come from the cache (no rasterizing); G4 in Gray evicts a Gray plot (the eviction count is 1).
    /// </remarks>
    [Fact]
    public void TryGet_KindsAreBoundedOnTheirOwn()
    {
        using Font font = TestFont.Open();
        var atlas = new GlyphAtlas(new TestRasterizer(), pageSize: 64, plotSize: 32, maxPages: 1);
        uint[] g = JapaneseGlyphs(font);
        for (int i = 0; i < 4; i++)
        {
            Assert.True(atlas.TryGet(new GlyphKey(font, g[i], 24, 0), out _));
        }

        atlas.BeginFrame();
        for (int i = 0; i < 5; i++)
        {
            Assert.True(atlas.TryGet(new GlyphKey(font, g[i], 24, 0, GlyphCoverage.Rgb), out _));
        }

        Assert.Equal((5, 1, 0), (atlas.GetPageCount(GlyphCoverage.Rgb), atlas.GetPageCount(GlyphCoverage.Gray), atlas.EvictionCount));

        atlas.BeginFrame();
        int rasterized = atlas.RasterizeCount;
        for (int i = 0; i < 4; i++)
        {
            Assert.True(atlas.TryGet(new GlyphKey(font, g[i], 24, 0), out _));
        }

        Assert.Equal(rasterized, atlas.RasterizeCount);
        atlas.BeginFrame();
        Assert.True(atlas.TryGet(new GlyphKey(font, g[4], 24, 0), out _));
        Assert.Equal(1, atlas.EvictionCount);
    }

    /// <summary>
    /// A ClearType page is half as wide and high as a grayscale page, so a page of either kind takes the same bytes and has a
    /// quarter of the plots, and the atlas keeps four times as many ClearType pages: the same bytes and plots for each kind. A plot
    /// that does not divide half the page is refused.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - The test rasterizer.
    ///
    /// Steps:
    /// - Make the default atlas (1024 px pages, 256 px plots, 4 pages), and atlases of 1024 px pages with 1024 px and 384 px plots.
    ///
    /// Expected:
    /// - Default: Gray 1024 px, at most 4 pages, limit 8; Rgb 512 px, at most 16 pages, limit 32; a page of each is 1,048,576 bytes.
    /// - The other two throw <see cref="ArgumentOutOfRangeException"/>.
    /// </remarks>
    [Fact]
    public void Constructor_ClearTypePages_AreHalfAsWideAndFourTimesAsMany()
    {
        var atlas = new GlyphAtlas(new TestRasterizer());

        Assert.Equal(
            (1024, 4, 8),
            (atlas.GetPageSize(GlyphCoverage.Gray), atlas.GetMaxPages(GlyphCoverage.Gray), atlas.GetPageLimit(GlyphCoverage.Gray))
        );
        Assert.Equal(
            (512, 16, 32),
            (atlas.GetPageSize(GlyphCoverage.Rgb), atlas.GetMaxPages(GlyphCoverage.Rgb), atlas.GetPageLimit(GlyphCoverage.Rgb))
        );
        Assert.Equal(1024 * 1024, 512 * 512 * GlyphAtlas.BytesPerPixel(GlyphCoverage.Rgb));
        Assert.Throws<ArgumentOutOfRangeException>(() => new GlyphAtlas(new TestRasterizer(), pageSize: 1024, plotSize: 1024));
        Assert.Throws<ArgumentOutOfRangeException>(() => new GlyphAtlas(new TestRasterizer(), pageSize: 1024, plotSize: 384));
    }

    /// <summary>
    /// A color glyph asked for as grayscale or ClearType, at any subpixel step, is kept once as a color glyph on a whole pixel:
    /// each key that stands for it is measured once, and the color glyph is rasterized once, on a color page.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - The test font, the test rasterizer with "H" (glyph 41) as a color glyph without a layer in the text's color, and an empty
    ///   atlas.
    ///
    /// Steps:
    /// - Get "H" at 32 px as Gray at step 1, as Gray at step 3, as Rgb at step 2, then as Gray at step 1 again, in red text.
    ///
    /// Expected:
    /// - The rasterizer measured the three keys and the color key (step 0, Color, no text color) once each, in that order (the color
    ///   key after the first); one glyph was rasterized; one color page and no other page.
    /// - Every request returned the same region, of the color kind.
    /// </remarks>
    [Fact]
    public void TryGet_ColorGlyph_IsKeptOnceAsColor()
    {
        using Font font = TestFont.Open();
        var rasterizer = new TestRasterizer();
        rasterizer.ColorGlyphs.Add(GlyphH, false);
        var atlas = new GlyphAtlas(rasterizer);
        uint red = Color(0xFF, 0x20, 0x20, 0xFF);
        var gray1 = new GlyphKey(font, GlyphH, 32, 1);
        var gray3 = new GlyphKey(font, GlyphH, 32, 3);
        var rgb2 = new GlyphKey(font, GlyphH, 32, 2, GlyphCoverage.Rgb);

        AtlasGlyph[] glyphs = [Get(atlas, gray1, red), Get(atlas, gray3, red), Get(atlas, rgb2, red), Get(atlas, gray1, red)];

        var color = new GlyphKey(font, GlyphH, 32, 0, GlyphCoverage.Color);
        Assert.Equal([gray1, color, gray3, rgb2], rasterizer.Measured);
        Assert.Equal(1, atlas.RasterizeCount);
        Assert.Equal(
            (1, 0, 0),
            (atlas.GetPageCount(GlyphCoverage.Color), atlas.GetPageCount(GlyphCoverage.Gray), atlas.GetPageCount(GlyphCoverage.Rgb))
        );
        Assert.All(glyphs, glyph => Assert.Equal(glyphs[0], glyph));
        Assert.Equal(GlyphCoverage.Color, glyphs[0].Coverage);
    }

    /// <summary>
    /// A color glyph with a layer in the text's color is one glyph per opaque text color: the text's alpha is not in its key.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - The test font, the test rasterizer with "H" as a color glyph with a layer in the text's color, and an empty atlas.
    ///
    /// Steps:
    /// - Get "H" at 32 px as Gray in red, in red with alpha 128, and in blue.
    ///
    /// Expected:
    /// - Red and translucent red give the same glyph, whose key holds opaque red; blue gives another glyph, whose key holds opaque
    ///   blue; two glyphs were rasterized.
    /// </remarks>
    [Fact]
    public void TryGet_ColorGlyphInTextColor_IsOneGlyphPerOpaqueColor()
    {
        using Font font = TestFont.Open();
        var rasterizer = new TestRasterizer();
        rasterizer.ColorGlyphs.Add(GlyphH, true);
        var atlas = new GlyphAtlas(rasterizer);
        var key = new GlyphKey(font, GlyphH, 32, 0);
        uint red = Color(0xFF, 0x20, 0x20, 0xFF);
        uint translucentRed = Color(0xFF, 0x20, 0x20, 128);
        uint blue = Color(0x20, 0x20, 0xFF, 0xFF);

        AtlasGlyph inRed = Get(atlas, key, red);
        AtlasGlyph inTranslucentRed = Get(atlas, key, translucentRed);
        AtlasGlyph inBlue = Get(atlas, key, blue);

        Assert.Equal(inRed, inTranslucentRed);
        Assert.NotEqual(inRed, inBlue);
        Assert.Equal(2, atlas.RasterizeCount);
        Assert.Equal(
            [red, blue],
            rasterizer.Measured.Where(measured => measured.Coverage == GlyphCoverage.Color).Select(measured => measured.TextColor)
        );
    }

    private static AtlasGlyph Get(GlyphAtlas atlas, GlyphKey key, uint textColor)
    {
        Assert.True(atlas.TryGet(key, textColor, out AtlasGlyph glyph));
        return glyph;
    }

    // A color packed as the display list packs it: red in the low byte, then green, blue, and alpha.
    private static uint Color(byte red, byte green, byte blue, byte alpha) => red | ((uint)green << 8) | ((uint)blue << 16) | ((uint)alpha << 24);

    private static uint[] JapaneseGlyphs(Font font)
    {
        using var shaper = new TextShaper();
        return shaper.Shape(font, "日本語のテキスト", TestFont.Japanese).ToArray().Select(glyph => glyph.GlyphId).Distinct().ToArray();
    }

    // The ring of pixels just outside the region (the packing leaves one pixel of padding there).
    private static int MaxOnBorder(GlyphAtlas atlas, AtlasGlyph glyph)
    {
        ReadOnlySpan<byte> pixels = atlas.GetPagePixels(GlyphCoverage.Gray, glyph.Page);
        int max = 0;
        for (int y = glyph.Y - 1; y <= glyph.Y + glyph.Height; y++)
        {
            for (int x = glyph.X - 1; x <= glyph.X + glyph.Width; x++)
            {
                bool inside = x >= glyph.X && x < glyph.X + glyph.Width && y >= glyph.Y && y < glyph.Y + glyph.Height;
                if (!inside && x >= 0 && y >= 0 && x < atlas.PageSize && y < atlas.PageSize)
                {
                    max = Math.Max(max, pixels[(y * atlas.PageSize) + x]);
                }
            }
        }

        return max;
    }

    // The pixels of the glyph's plot outside the glyph: 0 after an eviction cleared the plot.
    private static int MaxInPlotOutside(GlyphAtlas atlas, AtlasGlyph glyph)
    {
        ReadOnlySpan<byte> pixels = atlas.GetPagePixels(GlyphCoverage.Gray, glyph.Page);
        int plotX = glyph.X / atlas.PlotSize * atlas.PlotSize;
        int plotY = glyph.Y / atlas.PlotSize * atlas.PlotSize;
        int max = 0;
        for (int y = plotY; y < plotY + atlas.PlotSize; y++)
        {
            for (int x = plotX; x < plotX + atlas.PlotSize; x++)
            {
                bool inside = x >= glyph.X && x < glyph.X + glyph.Width && y >= glyph.Y && y < glyph.Y + glyph.Height;
                if (!inside)
                {
                    max = Math.Max(max, pixels[(y * atlas.PageSize) + x]);
                }
            }
        }

        return max;
    }
}
