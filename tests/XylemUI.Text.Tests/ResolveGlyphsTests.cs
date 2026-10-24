using System.Globalization;

namespace XylemUI.Text.Tests;

// DrawText leaves room for the glyphs the atlas does not have, and ResolveGlyphs fills it (design §5.0.3, "Glyphs seen for the
// first time", roadmap row T5).
public sealed class ResolveGlyphsTests
{
    private const uint Nichi = 240;
    private static readonly Color Ink = Color.FromRgb(0x202020);
    private static readonly Color HalfRed = new(0xCC, 0x33, 0x33, 128);
    private static readonly Color Blue = Color.FromRgb(0x3366CC);

    /// <summary>
    /// A glyph not in the atlas is left for ResolveGlyphs: it is not rasterized while the list is written, and the list cannot be
    /// read until it is resolved.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - The test font, a shaper, the test rasterizer, an empty atlas, and an empty display list.
    ///
    /// Steps:
    /// - Draw "H日" at 32 px; read the list; resolve its glyphs; read it again.
    ///
    /// Expected:
    /// - After DrawText: HasPendingGlyphs is true, nothing was measured, and GetReader fails the invariant (checked in Debug
    ///   builds; a release build ends the process there).
    /// - After ResolveGlyphs: HasPendingGlyphs is false, "H" and "日" were measured, and the list reads one run of 2 glyphs.
    /// </remarks>
    [Fact]
    public void DrawText_NewGlyphs_AreLeftUntilResolved()
    {
        using Font font = TestFont.Open();
        using var shaper = new TextShaper();
        var rasterizer = new TestRasterizer();
        var atlas = new GlyphAtlas(rasterizer);
        var list = new DisplayList();

        _ = list.DrawText(shaper.Shape(font, "H日", TestFont.Japanese), font, 32, atlas, 0, 50, Ink);

        Assert.True(list.HasPendingGlyphs);
        Assert.Empty(rasterizer.Measured);
#if DEBUG
        Exception failed = Assert.ThrowsAny<Exception>(() => list.GetReader());
        Assert.Equal("InvariantViolationException", failed.GetType().Name);
#endif

        list.ResolveGlyphs(atlas);

        Assert.False(list.HasPendingGlyphs);
        Assert.Equal([41u, Nichi], rasterizer.Measured.Select(key => key.GlyphId));
        DisplayListReader reader = list.GetReader();
        Assert.True(reader.TryNext(out CommandTag tag));
        Assert.Equal(CommandTag.GlyphRun, tag);
        Assert.Equal(2, reader.ReadGlyphRun().Glyphs.Length);
        Assert.False(reader.TryNext(out _));
    }

    /// <summary>
    /// The resolved list is the list written when every glyph is in the atlas: glyphs with no ink or refused leave their run, and a
    /// run splits where a glyph is a color glyph.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - The test font, a shaper, the test rasterizer with "日" as a color glyph and "." 300 px square (larger than a 256 px plot),
    ///   an atlas, and a display list.
    /// - A first frame drew "H 日.H", so its glyphs are in the atlas; "A" and "B" are not.
    ///
    /// Steps:
    /// - Frame 2: a rectangle, "H 日.H" in ink at 32 px (pen 10.25, baseline 50), a rectangle, "AB日" in half red (pen 5.5,
    ///   baseline 90); resolve. Frame 3: the same, with every glyph in the atlas; resolve.
    ///
    /// Expected:
    /// - Frame 2 reads: the rectangle; Gray "H", Color "日", Gray "H" in ink (the space and "." left out); the rectangle; Gray "A
    ///   B", Color "日" in half red. Each draw returns the advance of its whole text.
    /// - Frame 3 reads the same commands and glyph instances, and measured only "." again (the atlas remembers a refusal for one
    ///   frame).
    /// </remarks>
    [Fact]
    public void ResolveGlyphs_SameListAsWithEveryGlyphCached()
    {
        using Font font = TestFont.Open();
        using var shaper = new TextShaper();
        var rasterizer = new TestRasterizer();
        rasterizer.ColorGlyphs.Add(Nichi, false);
        uint period = shaper.Shape(font, ".", TestFont.Japanese)[0].GlyphId;
        rasterizer.Sides[period] = 300;
        var atlas = new GlyphAtlas(rasterizer);
        var list = new DisplayList();
        _ = list.DrawText(shaper.Shape(font, "H 日.H", TestFont.Japanese), font, 32, atlas, 10.25f, 50, Ink);
        list.ResolveGlyphs(atlas);

        (List<string> second, float[] advances) = Frame(shaper, atlas, list, font);
        rasterizer.Measured.Clear();
        (List<string> third, float[] again) = Frame(shaper, atlas, list, font);

        Assert.Equal(
            [
                "FillRect",
                "GlyphRun Gray 202020FF 1",
                "GlyphRun Color 202020FF 1",
                "GlyphRun Gray 202020FF 1",
                "FillRect",
                "GlyphRun Gray CC333380 2",
                "GlyphRun Color CC333380 1",
            ],
            second.Select(command => string.Join(' ', command.Split(' ').Take(command.StartsWith("FillRect", StringComparison.Ordinal) ? 1 : 4)))
        );
        Assert.Equal(second, third);
        Assert.Equal(advances, again);
        Assert.Equal([period], rasterizer.Measured.Select(key => key.GlyphId));
        Assert.False(list.HasPendingGlyphs);
    }

    /// <summary>
    /// Shapes and shadows keep their place and their values when ResolveGlyphs writes the list again (row R4).
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - The test font, a shaper, the test rasterizer, an empty atlas, and an empty display list.
    ///
    /// Steps:
    /// - Write a rounded rectangle with a border, "H" (left for ResolveGlyphs), and a shadow; resolve the list's glyphs; read it.
    ///
    /// Expected:
    /// - Shape, GlyphRun, Shadow, in that order, with the shape's and the shadow's values as written.
    /// </remarks>
    [Fact]
    public void ResolveGlyphs_ShapesAndShadows_KeepTheirPlaces()
    {
        using Font font = TestFont.Open();
        using var shaper = new TextShaper();
        var atlas = new GlyphAtlas(new TestRasterizer());
        var list = new DisplayList();
        var shadow = new BoxShadow(new Color(0, 0, 0, 100), new System.Numerics.Vector2(1, 2), 6, 1);

        list.FillRect(0, 0, 40, 20, Blue, new CornerRadii(4), new Border(new BorderWidths(1), Ink));
        _ = list.DrawText(shaper.Shape(font, "H", TestFont.Japanese), font, 32, atlas, 0, 50, Ink);
        list.DrawShadow(0, 60, 40, 20, new CornerRadii(4), shadow);
        list.ResolveGlyphs(atlas);

        DisplayListReader reader = list.GetReader();
        Assert.True(reader.TryNext(out CommandTag first));
        Assert.Equal(CommandTag.Shape, first);
        Assert.Equal(new ShapeCommand(0, 0, 40, 20, Blue, -1, new CornerRadii(4), new BorderWidths(1), Ink, -1), reader.ReadShape());
        Assert.True(reader.TryNext(out CommandTag second));
        Assert.Equal(CommandTag.GlyphRun, second);
        Assert.Single(reader.ReadGlyphRun().Glyphs.ToArray());
        Assert.True(reader.TryNext(out CommandTag third));
        Assert.Equal(CommandTag.Shadow, third);
        Assert.Equal(new ShadowCommand(0, 60, 40, 20, new CornerRadii(4), shadow.Color, shadow.Offset, 3, 1, false), reader.ReadShadow());
        Assert.False(reader.TryNext(out _));
    }

    /// <summary>
    /// ResolveGlyphs with nothing pending leaves the list as it is.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - The test font, a shaper, the test rasterizer, an atlas, and a display list with "HH" resolved.
    ///
    /// Steps:
    /// - Resolve again.
    ///
    /// Expected:
    /// - The same commands, glyph instances, and GrowthCount.
    /// </remarks>
    [Fact]
    public void ResolveGlyphs_NothingPending_LeavesTheList()
    {
        using Font font = TestFont.Open();
        using var shaper = new TextShaper();
        var atlas = new GlyphAtlas(new TestRasterizer());
        var list = new DisplayList();
        _ = list.DrawText(shaper.Shape(font, "HH", TestFont.Japanese), font, 32, atlas, 0, 50, Ink);
        list.ResolveGlyphs(atlas);
        List<string> before = Read(list);
        int growth = list.GrowthCount;

        list.ResolveGlyphs(atlas);

        Assert.Equal(before, Read(list));
        Assert.Equal(growth, list.GrowthCount);
    }

    /// <summary>
    /// Reset forgets the glyphs left for ResolveGlyphs.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - The test font, a shaper, the test rasterizer, an empty atlas, and a display list with "H" drawn and not resolved.
    ///
    /// Steps:
    /// - Reset the list and read it.
    ///
    /// Expected:
    /// - No glyph is pending, the list reads no command, and nothing was measured.
    /// </remarks>
    [Fact]
    public void Reset_ForgetsThePendingGlyphs()
    {
        using Font font = TestFont.Open();
        using var shaper = new TextShaper();
        var rasterizer = new TestRasterizer();
        var atlas = new GlyphAtlas(rasterizer);
        var list = new DisplayList();
        _ = list.DrawText(shaper.Shape(font, "H", TestFont.Japanese), font, 32, atlas, 0, 50, Ink);

        list.Reset();

        Assert.False(list.HasPendingGlyphs);
        Assert.Empty(Read(list));
        Assert.Empty(rasterizer.Measured);
    }

    // Frames 2 and 3 of ResolveGlyphs_SameListAsWithEveryGlyphCached, read back, with the advances of the draws.
    private static (List<string> Commands, float[] Advances) Frame(TextShaper shaper, GlyphAtlas atlas, DisplayList list, Font font)
    {
        atlas.BeginFrame();
        list.Reset();
        list.FillRect(0, 0, 10, 10, Blue);
        float first = list.DrawText(shaper.Shape(font, "H 日.H", TestFont.Japanese), font, 32, atlas, 10.25f, 50, Ink);
        list.FillRect(0, 60, 10, 10, Blue);
        float second = list.DrawText(shaper.Shape(font, "AB日", TestFont.Japanese), font, 32, atlas, 5.5f, 90, HalfRed);
        list.ResolveGlyphs(atlas);
        return (Read(list), [first, second]);
    }

    // Each command as "FillRect x y w h color" or "GlyphRun kind color count" and its glyph instances.
    private static List<string> Read(DisplayList list)
    {
        var commands = new List<string>();
        DisplayListReader reader = list.GetReader();
        while (reader.TryNext(out CommandTag tag))
        {
            if (tag == CommandTag.FillRect)
            {
                FillRectCommand rect = reader.ReadFillRect();
                commands.Add(string.Create(CultureInfo.InvariantCulture, $"FillRect {rect.X} {rect.Y} {rect.Width} {rect.Height} {Hex(rect.Color)}"));
                continue;
            }

            GlyphRunCommand run = reader.ReadGlyphRun();
            commands.Add($"GlyphRun {run.Coverage} {Hex(run.Color)} {run.Glyphs.Length} {string.Join(' ', run.Glyphs.ToArray())}");
        }

        return commands;
    }

    private static string Hex(Color color) => string.Create(CultureInfo.InvariantCulture, $"{color.R:X2}{color.G:X2}{color.B:X2}{color.A:X2}");
}
