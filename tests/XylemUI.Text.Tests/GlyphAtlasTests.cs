namespace XylemUI.Text.Tests;

// The CPU glyph atlas: pages, plots, and LRU eviction (S8a, S8b; design §5.0.2, §5.0.3). The expected boxes come from the glyph
// outlines read with fontTools (Fonts/README.md): at 32 px per em and 1000 units per em, a coordinate scales by 0.032, and the
// unhinted coverage covers the outline's box rounded out to whole pixels.
public sealed class GlyphAtlasTests
{
    private const uint GlyphH = 41;
    private const uint GlyphNichi = 240;

    /// <summary>
    /// A rasterized glyph's coverage box and bearings match its outline's box, and its ink lies inside its region.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - The test font and an empty atlas of one 256 px page.
    ///
    /// Steps:
    /// - Get "H" (glyph 41, outline 101..628 x 0..733 units) and "日" (glyph 240, outline 176..832 x −69..772) at 32 px.
    ///
    /// Expected:
    /// - "H": 18 x 24, bearings (3, 24) (x 3.23..20.10 → 3..21, y 0..23.46 → 0..24).
    /// - "日": 22 x 28, bearings (5, 25) (x 5.63..26.62 → 5..27, y −2.21..24.70 → −3..25).
    /// - Each region has ink (a pixel above 200), and the pixels around it, inside the padding, are 0.
    /// </remarks>
    [Fact]
    public void TryGet_GlyphBoxAndBearings_MatchTheOutline()
    {
        using Font font = TestFont.Open();
        var atlas = new GlyphAtlas(pageSize: 256, plotSize: 256, maxPages: 1);

        Assert.True(atlas.TryGet(font, GlyphH, 32, out AtlasGlyph h));
        Assert.True(atlas.TryGet(font, GlyphNichi, 32, out AtlasGlyph nichi));

        Assert.Equal((18, 24, 3, 24), (h.Width, h.Height, h.BearingX, h.BearingY));
        Assert.Equal((22, 28, 5, 25), (nichi.Width, nichi.Height, nichi.BearingX, nichi.BearingY));
        foreach (AtlasGlyph glyph in new[] { h, nichi })
        {
            Assert.True(MaxInside(atlas, glyph) > 200);
            Assert.Equal(0, MaxOnBorder(atlas, glyph));
        }
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
        var atlas = new GlyphAtlas(pageSize: 256, plotSize: 64, maxPages: 2);
        var regions = new List<AtlasGlyph>();

        foreach (ShapedGlyph shaped in shaper.Shape(font, "Hello, World! 日本語のテキスト", TestFont.Japanese))
        {
            Assert.True(atlas.TryGet(font, shaped.GlyphId, 24, out AtlasGlyph glyph));
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
        var atlas = new GlyphAtlas(pageSize: 256, plotSize: 256, maxPages: 1);

        Assert.True(atlas.TryGet(font, GlyphH, 32, out AtlasGlyph h));
        Assert.True(atlas.TryGet(font, 1, 32, out AtlasGlyph space));
        Assert.Equal(2, atlas.RasterizeCount);
        Assert.Equal(new AtlasRegion(h.Page, h.X, h.Y, h.Width, h.Height), Assert.Single(atlas.DirtyRegions.ToArray()));
        Assert.Equal(0, space.Width);
        atlas.ClearDirtyRegions();

        Assert.True(atlas.TryGet(font, GlyphH, 32, out AtlasGlyph again));
        Assert.True(atlas.TryGet(font, 1, 32, out _));
        Assert.Equal(h, again);
        Assert.Equal(2, atlas.RasterizeCount);
        Assert.Empty(atlas.DirtyRegions.ToArray());

        Assert.True(atlas.TryGet(font, GlyphH, 16, out AtlasGlyph small));
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
        var atlas = new GlyphAtlas(pageSize: 256, plotSize: 256, maxPages: 1);

        Assert.True(atlas.TryGet(a, GlyphH, 32, out AtlasGlyph a32));
        Assert.True(atlas.TryGet(b, GlyphH, 32, out AtlasGlyph b32));
        Assert.True(atlas.TryGet(a, GlyphH, 16, out AtlasGlyph a16));
        Assert.Equal(3, atlas.RasterizeCount);
        Assert.Equal(3, new[] { (a32.X, a32.Y), (b32.X, b32.Y), (a16.X, a16.Y) }.Distinct().Count());

        Assert.True(atlas.TryGet(a, GlyphH, 32, out AtlasGlyph a32Again));
        Assert.True(atlas.TryGet(b, GlyphH, 32, out AtlasGlyph b32Again));
        Assert.True(atlas.TryGet(a, GlyphH, 16, out AtlasGlyph a16Again));
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
        var atlas = new GlyphAtlas(pageSize: 256, plotSize: 256, maxPages: 1);

        Assert.True(atlas.TryGet(font, 1, 32, out AtlasGlyph space));
        Assert.True(atlas.TryGet(font, 1, 32, out _));
        Assert.Equal(0, atlas.PageCount);
        Assert.True(atlas.TryGet(font, GlyphH, 32, out AtlasGlyph h));

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
    /// - Get "日" at 32 px (22 x 28, larger than a plot), twice; then "." at 12 px.
    ///
    /// Expected:
    /// - Both requests for "日" return false; "." lands at (1, 1) of page 0.
    /// </remarks>
    [Fact]
    public void TryGet_GlyphLargerThanAPlot_DoesNotFit()
    {
        using Font font = TestFont.Open();
        using var shaper = new TextShaper();
        var atlas = new GlyphAtlas(pageSize: 64, plotSize: 16, maxPages: 1);
        uint period = shaper.Shape(font, ".", TestFont.Japanese)[0].GlyphId;

        Assert.False(atlas.TryGet(font, GlyphNichi, 32, out _));
        Assert.False(atlas.TryGet(font, GlyphNichi, 32, out _));
        Assert.True(atlas.TryGet(font, period, 12, out AtlasGlyph dot));
        Assert.Equal((0, 1, 1), (dot.Page, dot.X, dot.Y));
    }

    /// <summary>
    /// When the atlas is full, the plot used longest ago and not in the current frame is emptied and reused; its glyph is
    /// rasterized again when it is next asked for. A glyph found in the cache counts as a use of its plot.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - The test font and an atlas of one 64 px page with 32 px plots (4 plots), at most 1 page. A Japanese glyph at 24 px (14..24
    ///   wide, 20..23 high) leaves no room for a second one in a plot, so the 4 plots hold 4 glyphs: G0..G3.
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
        var atlas = new GlyphAtlas(pageSize: 64, plotSize: 32, maxPages: 1);
        uint[] g = JapaneseGlyphs(font);
        var regions = new AtlasGlyph[4];
        for (int i = 0; i < 4; i++)
        {
            Assert.True(atlas.TryGet(font, g[i], 24, out regions[i]));
        }

        atlas.BeginFrame();
        _ = atlas.TryGet(font, g[2], 24, out _);
        _ = atlas.TryGet(font, g[3], 24, out _);
        atlas.BeginFrame();
        atlas.ClearDirtyRegions();
        _ = atlas.TryGet(font, g[0], 24, out _);
        int rasterized = atlas.RasterizeCount;
        Assert.True(atlas.TryGet(font, g[4], 24, out AtlasGlyph g4));

        Assert.Equal((regions[1].Page, regions[1].X, regions[1].Y), (g4.Page, g4.X, g4.Y));
        Assert.Equal(1, atlas.EvictionCount);
        Assert.Equal(1, atlas.PageCount);
        Assert.Contains(new AtlasRegion(0, regions[1].X - 1, regions[1].Y - 1, 32, 32), atlas.DirtyRegions.ToArray());
        Assert.Equal(0, MaxInPlotOutside(atlas, g4));

        atlas.BeginFrame();
        foreach (int i in new[] { 0, 2, 3 })
        {
            _ = atlas.TryGet(font, g[i], 24, out _);
        }

        Assert.Equal(rasterized + 1, atlas.RasterizeCount);
        Assert.True(atlas.TryGet(font, g[1], 24, out _));
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
        var atlas = new GlyphAtlas(pageSize: 64, plotSize: 32, maxPages: 1);
        uint[] g = JapaneseGlyphs(font);
        for (int i = 0; i < 5; i++)
        {
            Assert.True(atlas.TryGet(font, g[i], 24, out _));
        }

        Assert.True(atlas.TryGet(font, g[4], 24, out AtlasGlyph g4));
        Assert.Equal(1, g4.Page);
        Assert.Equal((2, 0), (atlas.PageCount, atlas.EvictionCount));
        (long page0, long page1) = (atlas.GetPageId(0), atlas.GetPageId(1));

        atlas.BeginFrame();
        Assert.Equal(2, atlas.PageCount);
        for (int i = 0; i < 4; i++)
        {
            _ = atlas.TryGet(font, g[i], 24, out _);
        }

        atlas.BeginFrame();
        Assert.Equal(1, atlas.PageCount);
        int rasterized = atlas.RasterizeCount;
        Assert.True(atlas.TryGet(font, g[4], 24, out AtlasGlyph again));
        Assert.Equal(rasterized + 1, atlas.RasterizeCount);
        Assert.Equal(0, again.Page);

        for (int i = 0; i < 8; i++)
        {
            _ = atlas.TryGet(font, g[i], 24, out _);
        }

        Assert.Equal(2, atlas.PageCount);
        Assert.Equal(page0, atlas.GetPageId(0));
        Assert.NotEqual(page1, atlas.GetPageId(1));
    }

    /// <summary>
    /// The atlas never holds more than PageLimit pages: when every plot was used in the current frame and the limit is reached,
    /// a new glyph is refused, also for a caller that never starts a frame.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - The test font and an atlas of one 64 px page with 32 px plots, at most 1 page (PageLimit 2: 8 plots). The 8 Japanese
    ///   glyphs of the test font at 24 px take a plot each ("ト", the last, leaves 15 px of a plot's width free).
    ///
    /// Steps:
    /// - Without calling BeginFrame, get the 8 Japanese glyphs, then "W" (glyph 56, about 22 px wide) at 24 px, twice.
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
        var atlas = new GlyphAtlas(pageSize: 64, plotSize: 32, maxPages: 1);
        uint[] g = JapaneseGlyphs(font);
        Assert.Equal(8, g.Length);
        foreach (uint glyph in g)
        {
            Assert.True(atlas.TryGet(font, glyph, 24, out _));
        }

        Assert.Equal(2, atlas.PageLimit);
        Assert.Equal(2, atlas.PageCount);
        int rasterized = atlas.RasterizeCount;
        Assert.False(atlas.TryGet(font, 56, 24, out _));
        Assert.False(atlas.TryGet(font, 56, 24, out _));
        Assert.Equal((2, 0), (atlas.PageCount, atlas.EvictionCount));
        Assert.Equal(2, atlas.RefusedCount);
        Assert.Equal(rasterized + 2, atlas.RasterizeCount);

        atlas.BeginFrame();
        Assert.True(atlas.TryGet(font, 56, 24, out _));
        Assert.Equal(1, atlas.EvictionCount);
    }

    private static uint[] JapaneseGlyphs(Font font)
    {
        using var shaper = new TextShaper();
        return shaper.Shape(font, "日本語のテキスト", TestFont.Japanese).ToArray().Select(glyph => glyph.GlyphId).Distinct().ToArray();
    }

    private static int MaxInside(GlyphAtlas atlas, AtlasGlyph glyph)
    {
        ReadOnlySpan<byte> pixels = atlas.GetPagePixels(glyph.Page);
        int max = 0;
        for (int y = glyph.Y; y < glyph.Y + glyph.Height; y++)
        {
            for (int x = glyph.X; x < glyph.X + glyph.Width; x++)
            {
                max = Math.Max(max, pixels[(y * atlas.PageSize) + x]);
            }
        }

        return max;
    }

    // The ring of pixels just outside the region (the packing leaves one pixel of padding there).
    private static int MaxOnBorder(GlyphAtlas atlas, AtlasGlyph glyph)
    {
        ReadOnlySpan<byte> pixels = atlas.GetPagePixels(glyph.Page);
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
        ReadOnlySpan<byte> pixels = atlas.GetPagePixels(glyph.Page);
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
