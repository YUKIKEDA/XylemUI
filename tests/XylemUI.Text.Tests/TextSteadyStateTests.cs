namespace XylemUI.Text.Tests;

// What a text update costs in steady state (S8a, design §2.13: whether a text update joins the Zero-Alloc gate is decided from
// this measurement).
public sealed class TextSteadyStateTests
{
    private static long _sink;

    /// <summary>
    /// The text part of a frame (shaping the text again and writing its glyph run from the atlas) allocates nothing once the
    /// glyphs are cached and the display list has grown.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - The test font, a shaper, an atlas holding the glyphs of the text, and a display list (a warm-up of 10 frames).
    ///
    /// Steps:
    /// - 1,000 frames: start an atlas frame, reset the display list, shape "Hello, World! 日本語のテキスト", and write it with
    ///   DrawText. Measure the bytes allocated on this thread.
    ///
    /// Expected:
    /// - 0 bytes; no glyph was rasterized and no stream grew during the measured loop.
    /// </remarks>
    [Fact]
    public void SteadyState_ShapeAndDrawText_AllocatesNothing()
    {
        using Font font = TestFont.Open();
        using var shaper = new TextShaper();
        var atlas = new GlyphAtlas(new TestRasterizer());
        var list = new DisplayList(commandCapacity: 1, glyphCapacity: 1);
        const string Text = "Hello, World! 日本語のテキスト";
        for (int i = 0; i < 10; i++)
        {
            Frame(shaper, atlas, list, font, Text);
        }

        int rasterized = atlas.RasterizeCount;
        int growth = list.GrowthCount;
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1_000; i++)
        {
            Frame(shaper, atlas, list, font, Text);
        }

        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal(0, allocated);
        Assert.Equal(rasterized, atlas.RasterizeCount);
        Assert.Equal(growth, list.GrowthCount);
    }

    private static void Frame(TextShaper shaper, GlyphAtlas atlas, DisplayList list, Font font, string text)
    {
        atlas.BeginFrame();
        list.Reset();
        _sink += (long)list.DrawText(shaper.Shape(font, text, TestFont.Japanese), font, 24, atlas, 0, 24, Color.FromRgb(0x202020));
    }
}
