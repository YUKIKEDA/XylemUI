namespace XylemUI.Text.Tests;

// What a text update costs in steady state (S8a, design §2.13: whether a text update joins the Zero-Alloc gate is decided from
// this measurement).
public sealed class TextSteadyStateTests
{
    private static long _sink;

    /// <summary>
    /// Shaping a text again and looking its glyphs up in the atlas allocates nothing once the glyphs are cached.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - The test font, a shaper, and an atlas holding the glyphs of the text (one warm-up pass of 10 iterations).
    ///
    /// Steps:
    /// - 1,000 times: shape "Hello, World! 日本語のテキスト", get every glyph from the atlas, and add up the advances. Measure the
    ///   bytes allocated on this thread.
    ///
    /// Expected:
    /// - 0 bytes, and no glyph was rasterized during the measured loop.
    /// </remarks>
    [Fact]
    public void SteadyState_ShapeAndLookUp_AllocatesNothing()
    {
        using Font font = TestFont.Open();
        using var shaper = new TextShaper();
        var atlas = new GlyphAtlas();
        const string Text = "Hello, World! 日本語のテキスト";
        for (int i = 0; i < 10; i++)
        {
            Update(shaper, atlas, font, Text);
        }

        int rasterized = atlas.RasterizeCount;
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1_000; i++)
        {
            Update(shaper, atlas, font, Text);
        }

        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal(0, allocated);
        Assert.Equal(rasterized, atlas.RasterizeCount);
    }

    private static void Update(TextShaper shaper, GlyphAtlas atlas, Font font, string text)
    {
        foreach (ShapedGlyph glyph in shaper.Shape(font, text))
        {
            _ = atlas.TryGet(font, glyph.GlyphId, 24, out AtlasGlyph region);
            _sink += glyph.XAdvance + region.Width;
        }
    }
}
