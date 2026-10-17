namespace XylemUI.Text.Tests;

// The CPU glyph atlas (S8a, design §5.0.2, §5.0.3). The expected boxes come from the glyph outlines read with fontTools
// (Fonts/README.md): at 32 px per em and 1000 units per em, a coordinate scales by 0.032, and the unhinted coverage covers the
// outline's box rounded out to whole pixels.
public sealed class GlyphAtlasTests
{
    private const uint GlyphH = 41;
    private const uint GlyphNichi = 240;

    /// <summary>
    /// A rasterized glyph's coverage box and bearings match its outline's box, and its ink lies inside its region.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - The test font and an empty atlas.
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
        var atlas = new GlyphAtlas(256, 256);

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
    /// The glyphs of a text are packed without overlapping, each inside the atlas.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - The test font, a shaper, and a 256 x 256 atlas.
    ///
    /// Steps:
    /// - Get every glyph of "Hello, World! 日本語のテキスト" at 24 px.
    ///
    /// Expected:
    /// - Every request succeeds; every region lies inside the atlas; no two regions with ink overlap.
    /// </remarks>
    [Fact]
    public void TryGet_ManyGlyphs_PackWithoutOverlap()
    {
        using Font font = TestFont.Open();
        using var shaper = new TextShaper();
        var atlas = new GlyphAtlas(256, 256);
        var regions = new List<AtlasGlyph>();

        foreach (ShapedGlyph shaped in shaper.Shape(font, "Hello, World! 日本語のテキスト"))
        {
            Assert.True(atlas.TryGet(font, shaped.GlyphId, 24, out AtlasGlyph glyph));
            if (glyph.Width > 0 && !regions.Contains(glyph))
            {
                regions.Add(glyph);
            }
        }

        foreach (AtlasGlyph a in regions)
        {
            Assert.InRange(a.X, 0, atlas.Width - a.Width);
            Assert.InRange(a.Y, 0, atlas.Height - a.Height);
            foreach (AtlasGlyph b in regions)
            {
                bool overlap = a != b && a.X < b.X + b.Width && b.X < a.X + a.Width && a.Y < b.Y + b.Height && b.Y < a.Y + a.Height;
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
        var atlas = new GlyphAtlas(256, 256);

        Assert.True(atlas.TryGet(font, GlyphH, 32, out AtlasGlyph h));
        Assert.True(atlas.TryGet(font, 1, 32, out AtlasGlyph space));
        Assert.Equal(2, atlas.RasterizeCount);
        Assert.Equal((h.X, h.Y, h.Width, h.Height), Assert.Single(atlas.DirtyRegions.ToArray()));
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
    /// A full atlas refuses a new glyph, and the glyphs already in it still come back.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - The test font and a 64 x 64 atlas: room for two rows of 32 px Japanese glyphs (28 high plus padding).
    ///
    /// Steps:
    /// - Get the eight glyphs of "日本語のテキスト" at 32 px until one is refused; get the first one again.
    ///
    /// Expected:
    /// - A request returns false before the eighth glyph; the first glyph still returns true with its region.
    /// </remarks>
    [Fact]
    public void TryGet_FullAtlas_RefusesNewGlyphsAndKeepsTheOldOnes()
    {
        using Font font = TestFont.Open();
        using var shaper = new TextShaper();
        var atlas = new GlyphAtlas(64, 64);
        uint[] ids = shaper.Shape(font, "日本語のテキスト").ToArray().Select(g => g.GlyphId).ToArray();

        Assert.True(atlas.TryGet(font, ids[0], 32, out AtlasGlyph first));
        int placed = 1;
        while (placed < ids.Length && atlas.TryGet(font, ids[placed], 32, out _))
        {
            placed++;
        }

        Assert.True(placed < ids.Length);
        Assert.True(atlas.TryGet(font, ids[0], 32, out AtlasGlyph again));
        Assert.Equal(first, again);
    }

    private static int MaxInside(GlyphAtlas atlas, AtlasGlyph glyph)
    {
        int max = 0;
        for (int y = glyph.Y; y < glyph.Y + glyph.Height; y++)
        {
            for (int x = glyph.X; x < glyph.X + glyph.Width; x++)
            {
                max = Math.Max(max, atlas.Pixels[(y * atlas.Width) + x]);
            }
        }

        return max;
    }

    // The ring of pixels just outside the region (the packing leaves one pixel of padding there).
    private static int MaxOnBorder(GlyphAtlas atlas, AtlasGlyph glyph)
    {
        int max = 0;
        for (int y = glyph.Y - 1; y <= glyph.Y + glyph.Height; y++)
        {
            for (int x = glyph.X - 1; x <= glyph.X + glyph.Width; x++)
            {
                bool inside = x >= glyph.X && x < glyph.X + glyph.Width && y >= glyph.Y && y < glyph.Y + glyph.Height;
                if (!inside && x >= 0 && y >= 0 && x < atlas.Width && y < atlas.Height)
                {
                    max = Math.Max(max, atlas.Pixels[(y * atlas.Width) + x]);
                }
            }
        }

        return max;
    }
}
