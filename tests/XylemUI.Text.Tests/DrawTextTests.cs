namespace XylemUI.Text.Tests;

// Shaped text written as a glyph run (S8b, design §5.0.3): DisplayList.DrawText puts the glyphs in the atlas and places them on
// the pen in whole pixels. The box and bearings of "H" at 32 px are those of GlyphAtlasTests (from the outline, with fontTools);
// the advances are hmtx (H 728, space 224, "日" 1000 units).
public sealed class DrawTextTests
{
    private static readonly Color Ink = Color.FromRgb(0x202020);

    /// <summary>
    /// Each glyph with ink is placed at the rounded pen position plus its bearings, with its atlas region; a space only moves the
    /// pen; the advance of the whole text is returned.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - The test font, a shaper, an empty atlas, and an empty display list.
    ///
    /// Steps:
    /// - Shape "H H" and draw it at 32 px with the pen at x 10.25 and the baseline at 50.
    ///
    /// Expected:
    /// - One glyph run of 2 glyphs, in the ink color. The first "H": x round(10.25) + 3 = 13, y 50 − 24 = 26; the second: pen
    ///   (728 + 224) × 0.032 = 30.464, x round(40.714) + 3 = 44, y 26. Both 18 x 24, with the region the atlas gives for "H".
    /// - The advance is (728 + 224 + 728) × 0.032 = 53.76.
    /// </remarks>
    [Fact]
    public void DrawText_PlacesGlyphsOnTheRoundedPen()
    {
        using Font font = TestFont.Open();
        using var shaper = new TextShaper();
        var atlas = new GlyphAtlas();
        var list = new DisplayList();

        float advance = list.DrawText(shaper.Shape(font, "H H", TestFont.Japanese), font, 32, atlas, 10.25f, 50, Ink);

        Assert.True(atlas.TryGet(font, 41, 32, out AtlasGlyph h));
        Assert.Equal(53.76f, advance, 0.001f);
        DisplayListReader reader = list.GetReader();
        Assert.True(reader.TryNext(out CommandTag tag));
        Assert.Equal(CommandTag.GlyphRun, tag);
        GlyphRunCommand run = reader.ReadGlyphRun();
        Assert.Equal(Ink, run.Color);
        Assert.Equal(
            [new GlyphInstance(13, 26, 18, 24, h.Page, h.X, h.Y), new GlyphInstance(44, 26, 18, 24, h.Page, h.X, h.Y)],
            run.Glyphs.ToArray()
        );
        Assert.False(reader.TryNext(out _));
    }

    /// <summary>
    /// Text with no ink appends no command, and a glyph larger than a plot is left out while the pen still moves past it.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - The test font, a shaper, an atlas of 16 px plots, and an empty display list.
    ///
    /// Steps:
    /// - Draw "  " (two spaces) at 32 px with the pen at 0.
    /// - Draw "日." at 32 px with the pen at 0: "日" (22 x 28) is larger than a plot, "." is not.
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
        var atlas = new GlyphAtlas(pageSize: 64, plotSize: 16, maxPages: 1);
        var list = new DisplayList();

        float spaces = list.DrawText(shaper.Shape(font, "  ", TestFont.Japanese), font, 32, atlas, 0, 40, Ink);
        Assert.Equal(14.336f, spaces, 0.001f);
        Assert.Equal(0, list.CommandCount);

        _ = list.DrawText(shaper.Shape(font, "日.", TestFont.Japanese), font, 32, atlas, 0, 40, Ink);
        uint period = shaper.Shape(font, ".", TestFont.Japanese)[0].GlyphId;
        Assert.True(atlas.TryGet(font, period, 32, out AtlasGlyph dot));
        DisplayListReader reader = list.GetReader();
        Assert.True(reader.TryNext(out _));
        GlyphInstance glyph = Assert.Single(reader.ReadGlyphRun().Glyphs.ToArray());
        Assert.Equal(32 + dot.BearingX, glyph.X);
        Assert.False(reader.TryNext(out _));
    }
}
