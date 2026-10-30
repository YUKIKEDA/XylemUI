namespace XylemUI.Text.Tests;

// What a text update costs in steady state (S8a, design §2.13: whether a text update joins the Zero-Alloc gate is decided from
// this measurement).
public sealed class TextSteadyStateTests
{
    private static long _sink;

    /// <summary>
    /// The text part of a frame (shaping the text again, writing its glyph run from the atlas, and resolving the list's glyphs)
    /// allocates nothing once the glyphs are cached and the display list has grown.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - The test font, a shaper, an atlas made with a function that creates test rasterizers, with 4 threads, holding the glyphs of
    ///   the text (its workers started in the first frame), and a display list (a warm-up of 10 frames).
    ///
    /// Steps:
    /// - 1,000 frames, in the windows of <c>PerformanceAssert.DoesNotAllocate</c>: start an atlas frame, reset the display list, shape
    ///   "Hello, World! 日本語のテキスト", write it with DrawText, and resolve the list's glyphs.
    ///
    /// Expected:
    /// - 0 bytes on this thread in every window; no glyph was rasterized and no stream grew during the measured loop. A failure
    ///   lists each window's bytes, the methods compiled on this thread, and the collections (Issue #92).
    /// </remarks>
    [Fact]
    public void SteadyState_ShapeAndDrawText_AllocatesNothing()
    {
        using Font font = TestFont.Open();
        using var shaper = new TextShaper();
        using var atlas = new GlyphAtlas(() => new TestRasterizer(), threads: 4);
        var list = new DisplayList(commandCapacity: 1, glyphCapacity: 1);
        const string Text = "Hello, World! 日本語のテキスト";
        for (int i = 0; i < 10; i++)
        {
            Frame(shaper, atlas, list, font, Text);
        }

        int rasterized = atlas.RasterizeCount;
        int growth = list.GrowthCount;
        PerformanceAssert.DoesNotAllocate(1_000, _ => Frame(shaper, atlas, list, font, Text));

        Assert.NotNull(atlas.RunningWorkers);
        Assert.Equal(rasterized, atlas.RasterizeCount);
        Assert.Equal(growth, list.GrowthCount);
    }

    private static void Frame(TextShaper shaper, GlyphAtlas atlas, DisplayList list, Font font, string text)
    {
        atlas.BeginFrame();
        list.Reset();
        _sink += (long)list.DrawText(shaper.Shape(font, text, TestFont.Japanese), font, 24, atlas, 0, 24, Color.FromRgb(0x202020));
        list.ResolveGlyphs(atlas);
    }
}
