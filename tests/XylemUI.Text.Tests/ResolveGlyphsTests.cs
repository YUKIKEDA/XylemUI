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
    /// Brushed text keeps its gradient and box on every run ResolveGlyphs splits it into, and its color glyphs' layers in the text's
    /// color are asked for in the brush's first color made opaque (design §5.0, "The API of lines, ellipses, arcs, and the paint of
    /// text").
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - The test font, a shaper, the test rasterizer with "日" as a color glyph with a layer in the text's color, an empty atlas, a
    ///   display list at scale 2, and a gradient from red with alpha 100 to blue.
    ///
    /// Steps:
    /// - Draw "H日H" with the gradient over the box (5, 6, 70, 20), every glyph left for ResolveGlyphs; resolve; read the list.
    ///
    /// Expected:
    /// - Three runs (Gray, Color, Gray), each with brush 0 and the box (10, 12, 140, 40) in physical pixels, in the brush's first
    ///   color made opaque; three shape rows.
    /// - The atlas has "日" for that opaque color and not for the brush's last color (blue).
    /// </remarks>
    [Fact]
    public void ResolveGlyphs_BrushedText_KeepsItsGradientOnEveryRun()
    {
        using Font font = TestFont.Open();
        using var shaper = new TextShaper();
        var rasterizer = new TestRasterizer();
        rasterizer.ColorGlyphs.Add(Nichi, true);
        var atlas = new GlyphAtlas(rasterizer);
        var list = new DisplayList();
        list.Reset(2);
        var faded = new Color(0xCC, 0x33, 0x33, 100);
        GradientBrush brush = GradientBrush.Linear(90, [new(0, faded), new(1, Blue)]);

        _ = list.DrawText(shaper.Shape(font, "H日H", TestFont.Japanese), font, 16, atlas, 5, 25, brush, 5, 6, 70, 20);
        list.ResolveGlyphs(atlas);

        Assert.Equal(3, list.ShapeCount);
        var runs = new List<(GlyphCoverage, int, System.Numerics.Vector4, Color)>();
        DisplayListReader reader = list.GetReader();
        while (reader.TryNext(out _))
        {
            GlyphRunCommand run = reader.ReadGlyphRun();
            runs.Add((run.Coverage, run.Brush, run.Box, run.Color));
        }

        var box = new System.Numerics.Vector4(10, 12, 140, 40);
        Color opaque = Color.FromRgb(0xCC3333);
        Assert.Equal([(GlyphCoverage.Gray, 0, box, opaque), (GlyphCoverage.Color, 0, box, opaque), (GlyphCoverage.Gray, 0, box, opaque)], runs);
        bool Has(Color textColor) =>
            Enumerable
                .Range(0, 4)
                .Any(step => atlas.Lookup(new GlyphKey(font, Nichi, 32, step, GlyphCoverage.Gray), textColor.Packed, out _) == GlyphLookup.Found);
        Assert.True(Has(opaque));
        Assert.False(Has(Blue));
    }

    /// <summary>
    /// Brushed text refuses a box whose edges are not numbers or whose width or height is not positive.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - The test font, a shaper, an atlas, a display list, and a gradient.
    ///
    /// Steps:
    /// - Draw "H" with the gradient over the case's box.
    ///
    /// Expected:
    /// - ArgumentOutOfRangeException, and nothing is written.
    /// </remarks>
    /// <param name="x">The box's left edge.</param>
    /// <param name="width">The box's width.</param>
    /// <param name="height">The box's height.</param>
    [Theory]
    [InlineData(0f, 0f, 10f)]
    [InlineData(0f, 10f, -1f)]
    [InlineData(float.NaN, 10f, 10f)]
    [InlineData(0f, float.PositiveInfinity, 10f)]
    public void DrawText_BrushedWithABadBox_Throws(float x, float width, float height)
    {
        using Font font = TestFont.Open();
        using var shaper = new TextShaper();
        var atlas = new GlyphAtlas(new TestRasterizer());
        var list = new DisplayList();
        GradientBrush brush = GradientBrush.Linear(90, [new(0, Ink), new(1, Blue)]);

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            list.DrawText(shaper.Shape(font, "H", TestFont.Japanese), font, 16, atlas, 0, 20, brush, x, 0, width, height)
        );
        Assert.Equal(0, list.CommandCount);
    }

    /// <summary>
    /// Text in a group is grayscale even when asked for ClearType, makes its group and every group around it a layer, and adds
    /// the glyphs ResolveGlyphs places to the bounds of the groups around it (row R4c).
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - The test font, a shaper, the test rasterizer, an empty atlas, and a display list at scale 1.
    ///
    /// Steps:
    /// - Push 0.5; fill (0, 0, 10, 10); push 0.5; draw "H" at 32 px in ClearType (pen 100, baseline 60), left for ResolveGlyphs; pop;
    ///   pop; resolve; read.
    ///
    /// Expected:
    /// - Both groups are layers; the run is grayscale; the inner group's bounds are the glyph's quad, and the outer one's reach from
    ///   (0, 0) to the glyph's quad.
    /// </remarks>
    [Fact]
    public void ResolveGlyphs_TextInGroups_IsGrayAndWidensTheirBounds()
    {
        using Font font = TestFont.Open();
        using var shaper = new TextShaper();
        var atlas = new GlyphAtlas(new TestRasterizer());
        var list = new DisplayList();

        list.PushOpacity(0.5f);
        list.FillRect(0, 0, 10, 10, Blue);
        list.PushOpacity(0.5f);
        _ = list.DrawText(shaper.Shape(font, "H", TestFont.Japanese), font, 32, atlas, 100, 60, Ink, GlyphCoverage.Rgb);
        list.Pop();
        list.Pop();
        list.ResolveGlyphs(atlas);

        DisplayListReader reader = list.GetReader();
        Assert.True(reader.TryNext(out _));
        GroupCommand outer = reader.ReadGroup();
        Assert.True(reader.TryNext(out _));
        _ = reader.ReadFillRect();
        Assert.True(reader.TryNext(out _));
        GroupCommand inner = reader.ReadGroup();
        Assert.True(reader.TryNext(out _));
        GlyphRunCommand run = reader.ReadGlyphRun();
        GlyphInstance glyph = run.Glyphs[0];
        Assert.Equal(GlyphCoverage.Gray, run.Coverage);
        Assert.True(outer.Layer && inner.Layer);
        Assert.Equal(new GroupCommand(glyph.X, glyph.Y, glyph.Width, glyph.Height, 0.5f, true), inner);
        Assert.Equal(new GroupCommand(0, 0, glyph.X + glyph.Width, glyph.Y + glyph.Height, 0.5f, true), outer);
    }

    /// <summary>
    /// Text in a transform keeps its state when ResolveGlyphs splits its run where a color glyph is: every run lies between the
    /// state's PushState and its Pop; and the glyphs it places, in the text's pixels, widen the group around them through the
    /// transform (row R4d).
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - The test font, a shaper, the test rasterizer with 日 as a color glyph, an empty atlas, and a display list at scale 1.
    ///
    /// Steps:
    /// - Push the scale by 2; push 0.5; draw "H日H" at 16 px (pen 50, baseline 30), left for ResolveGlyphs; pop; pop; resolve; read.
    ///
    /// Expected:
    /// - A state of text scale 2, a group, three runs (grayscale, color, grayscale), and two Pops; the group is a layer whose bounds
    ///   are the union of the glyphs' quads, which are in the text's pixels (half the transform's), turned into physical pixels by
    ///   the scale: the same numbers.
    /// </remarks>
    [Fact]
    public void ResolveGlyphs_TextInATransform_KeepsItsStateAndWidensThroughIt()
    {
        using Font font = TestFont.Open();
        using var shaper = new TextShaper();
        var rasterizer = new TestRasterizer();
        rasterizer.ColorGlyphs.Add(Nichi, false);
        var atlas = new GlyphAtlas(rasterizer);
        var list = new DisplayList();

        list.PushTransform(System.Numerics.Matrix3x2.CreateScale(2));
        list.PushOpacity(0.5f);
        _ = list.DrawText(shaper.Shape(font, "H日H", TestFont.Japanese), font, 16, atlas, 50, 30, Ink);
        list.Pop();
        list.Pop();
        list.ResolveGlyphs(atlas);

        DisplayListReader reader = list.GetReader();
        var seen = new List<string>();
        GroupCommand group = default;
        float left = float.MaxValue;
        float top = float.MaxValue;
        float right = float.MinValue;
        float bottom = float.MinValue;
        while (reader.TryNext(out CommandTag tag))
        {
            switch (tag)
            {
                case CommandTag.PushState:
                    seen.Add($"state {reader.ReadState(out _).TextScale}");
                    break;
                case CommandTag.PushOpacity:
                    group = reader.ReadGroup();
                    seen.Add("group");
                    break;
                case CommandTag.GlyphRun:
                    GlyphRunCommand run = reader.ReadGlyphRun();
                    seen.Add(run.Coverage.ToString());
                    foreach (GlyphInstance glyph in run.Glyphs)
                    {
                        (left, top) = (MathF.Min(left, glyph.X), MathF.Min(top, glyph.Y));
                        (right, bottom) = (MathF.Max(right, glyph.X + glyph.Width), MathF.Max(bottom, glyph.Y + glyph.Height));
                    }

                    break;
                default:
                    reader.ReadPop();
                    seen.Add("pop");
                    break;
            }
        }

        Assert.Equal(["state 2", "group", "Gray", "Color", "Gray", "pop", "pop"], seen);
        Assert.True(group.Layer);
        Assert.Equal(new GroupCommand(left, top, right - left, bottom - top, 0.5f, true), group);
    }

    /// <summary>
    /// Text inside a scale of 0 writes nothing and returns the advance it would have without the scale (row R4d).
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - The test font, a shaper, the test rasterizer, an atlas, and two display lists at scale 1.
    ///
    /// Steps:
    /// - Draw "H日" at 16 px into the first list; into the second, inside the scale by 0.
    ///
    /// Expected:
    /// - The second's advance is the first's; it has no glyph and nothing pending, and no command between its PushState and Pop
    ///   (its scope is left out, as it is empty).
    /// </remarks>
    [Fact]
    public void DrawText_InAScaleOfZero_WritesNothing()
    {
        using Font font = TestFont.Open();
        using var shaper = new TextShaper();
        var atlas = new GlyphAtlas(new TestRasterizer());
        var plain = new DisplayList();
        var flat = new DisplayList();

        float advance = plain.DrawText(shaper.Shape(font, "H日", TestFont.Japanese), font, 16, atlas, 5, 20, Ink);
        flat.PushTransform(System.Numerics.Matrix3x2.CreateScale(0));
        float flattened = flat.DrawText(shaper.Shape(font, "H日", TestFont.Japanese), font, 16, atlas, 5, 20, Ink);
        flat.Pop();

        Assert.Equal(advance, flattened);
        Assert.Equal(0, flat.GlyphCount);
        Assert.False(flat.HasPendingGlyphs);
        Assert.Equal(0, flat.CommandCount);
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
