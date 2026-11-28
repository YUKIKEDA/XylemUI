using static XylemUI.Elements;

namespace XylemUI.Text.Tests;

// The color of a text style (row T15, design §3.2, "The fields of TextStyle"): a leaf keeps the key its layout reads and the color
// apart, so a change of the color alone draws the leaf again and keeps its caches, a change of the key lays it out again, and a
// text layout drawn with no color takes the color its draw function's node resolves. A leaf is laid out again when ChangeKind.Font
// marks it: that sets DirtyFlags.Text and DirtyFlags.Layout, which a change of color does not.
[Collection(UiThreadGroup.Name)]
public sealed class TextStyleTests(UiThreadFixture ui) : IDisposable
{
    private static readonly Color Ink = Color.FromRgb(0x2040C0);
    private static readonly Color Red = Color.FromRgb(0xCC3333);
    private static readonly Color Green = Color.FromRgb(0x33AA66);

    private readonly Font _font = TestFont.Open();

    public void Dispose() => _font.Dispose();

    /// <summary>
    /// A style made with the four fields of the style before row T15 has no color, and a color is part of the style's equality.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - None.
    ///
    /// Steps:
    /// - Make a style with the font, the size, the language, and the line height alone; make it again with red, by `with` and by
    ///   the constructor.
    ///
    /// Expected:
    /// - The first has no color; the two red ones are equal and red, and neither equals the first.
    /// </remarks>
    [Fact]
    public void TextStyle_OfFourFields_HasNoColor()
    {
        TextStyle plain = Style();
        TextStyle withRed = plain with { Color = Red };
        var madeRed = new TextStyle(FontRequest.Default, 20, TestFont.Japanese, float.NaN, Red);

        Assert.Null(plain.Color);
        Assert.Equal(Red, withRed.Color);
        Assert.Equal(madeRed, withRed);
        Assert.NotEqual(plain, withRed);
    }

    /// <summary>
    /// A style whose color alone differs marks the leaf to be drawn again with no layout, keeps its shaped text and its lines, and
    /// draws in the new color; the same style again marks nothing.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A text "nn" given the style of 20 px with no color, drawn.
    ///
    /// Steps:
    /// - Write the style with red; read the leaf's marks, its caches, and whether a frame is asked for; draw. Write the same red
    ///   style again; read whether a frame is asked for.
    ///
    /// Expected:
    /// - After red: the paint mark added, and no layout or text mark added (the marks the scene's draw keeps are taken off first);
    ///   the shaped text still valid, the same entries of lines and the same committed entry; a frame asked for; the leaf's color
    ///   red, and one glyph run of 2 glyphs in red. After the same style: no frame asked for, and no mark added.
    /// </remarks>
    [Fact]
    public void SetTextStyle_ColorAlone_MarksPaintOnly_AndKeepsTheCaches() =>
        ui.Invoke(() =>
        {
            using var scene = new TextScene(_font);
            Element text = default;
            scene.Mount(() => text = Elements.Text("nn"));
            scene.Tree.SetTextStyle(text.Node, Style());
            scene.Draw();
            TextLeaf leaf = scene.Leaf(text);
            (int Count, int Committed) lines = (leaf.LineBreaks!.Count, leaf.LineBreaks.Committed);

            // The scene's draw keeps the leaf's own marks (as in row TS1's scenes): they are taken off before each write is read.
            Unmark(scene, text);
            scene.Tree.SetTextStyle(text.Node, Style() with { Color = Red });
            DirtyFlags marks = Marks(scene, text);
            (bool Shaped, int Count, int Committed) caches = (leaf.Shaped!.Valid, leaf.LineBreaks.Count, leaf.LineBreaks.Committed);
            bool asked = scene.Scheduler.Pending;
            scene.Draw();
            List<TextScene.Drawn> drawn = scene.Commands();

            Unmark(scene, text);
            scene.Tree.SetTextStyle(text.Node, Style() with { Color = Red });

            Assert.Equal(DirtyFlags.Paint, marks & (DirtyFlags.Paint | DirtyFlags.Layout | DirtyFlags.Text));
            Assert.Equal((true, lines.Count, lines.Committed), caches);
            Assert.True(lines.Committed >= 0);
            Assert.True(asked);
            Assert.Equal(Red, leaf.Color);
            Assert.Equal([new TextScene.Drawn(CommandTag.GlyphRun, 0, 0, 0, 0, Red, 2, null)], drawn);
            Assert.True(leaf.Shaped.Valid);
            Assert.False(scene.Scheduler.Pending);
            Assert.Equal(DirtyFlags.None, Marks(scene, text));
        });

    /// <summary>
    /// A style whose font, size, language, or line height differs, each alone, marks the leaf's layout and drops its shaped text,
    /// and keeps the color the style gives.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A text "nn" given the style of 20 px in red, drawn. The cases change one field of the key each: the font's weight to
    ///   bold (0), the size to 30 (1), the language to English (2), the line height to 2 (3).
    ///
    /// Steps:
    /// - Write the style with the case's field changed and the color red; read the marks, the shaped text, and the color.
    ///
    /// Expected:
    /// - The layout and text marks; the shaped text not valid; the color red.
    /// </remarks>
    /// <param name="field">The field of the key that changes.</param>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void SetTextStyle_KeyField_MarksLayout(int field) =>
        ui.Invoke(() =>
        {
            using var scene = new TextScene(_font);
            Element text = default;
            scene.Mount(() => text = Elements.Text("nn"));
            TextStyle red = Style() with { Color = Red };
            scene.Tree.SetTextStyle(text.Node, red);
            scene.Draw();
            TextStyle changed = field switch
            {
                0 => red with { Font = FontRequest.Default with { Weight = FontWeight.Bold } },
                1 => red with { Size = 30 },
                2 => red with { Language = TextLanguage.FromTag("en") },
                _ => red with { LineHeight = 2 },
            };

            scene.Tree.SetTextStyle(text.Node, changed);

            DirtyFlags marks = Marks(scene, text);
            Assert.Equal(DirtyFlags.Layout | DirtyFlags.Text, marks & (DirtyFlags.Layout | DirtyFlags.Text));
            Assert.False(scene.Leaf(text).Shaped!.Valid);
            Assert.Equal(Red, scene.Leaf(text).Color);
        });

    /// <summary>
    /// A style with no color takes the ancestors' foreground again after one with a color, and marks paint only.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A column whose foreground is ink, around a text "nn" given the style of 20 px in red, drawn.
    ///
    /// Steps:
    /// - Read the glyph run's color. Write the same style with no color; read the marks; draw; read the color.
    ///
    /// Expected:
    /// - Red first; then no layout mark, the leaf's color null, and the glyph run in ink.
    /// </remarks>
    [Fact]
    public void SetTextStyle_NullColor_TakesTheAncestorsForegroundAgain() =>
        ui.Invoke(() =>
        {
            using var scene = new TextScene(_font);
            Element text = default;
            scene.Mount(() => Column(text = Elements.Text("nn")).Foreground(Ink));
            scene.Tree.SetTextStyle(text.Node, Style() with { Color = Red });
            scene.Draw();
            Color before = scene.Commands()[0].Color;

            scene.Tree.SetTextStyle(text.Node, Style());
            DirtyFlags marks = Marks(scene, text);
            scene.Draw();

            Assert.Equal(Red, before);
            Assert.Equal(DirtyFlags.None, marks & DirtyFlags.Layout);
            Assert.Null(scene.Leaf(text).Color);
            Assert.Equal(Ink, scene.Commands()[0].Color);
        });

    /// <summary>
    /// A leaf's style read with TextStyleOf holds the leaf's color, so a write back of it with one font field changed by
    /// <c>with</c> (as <c>Primitive.Icon</c> writes) keeps the color a <c>Foreground</c> gave the leaf.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A column whose foreground is ink, around a text "nn" whose own foreground is red, drawn.
    ///
    /// Steps:
    /// - Read the leaf's style; write it back with the font's weight bold; draw; read the leaf's color and the glyph run's.
    ///
    /// Expected:
    /// - The style read is red; after the write the leaf is bold, its color red, and the glyph run red.
    /// </remarks>
    [Fact]
    public void TextStyleOf_WrittenBackWithAFontField_KeepsTheForeground() =>
        ui.Invoke(() =>
        {
            using var scene = new TextScene(_font);
            Element text = default;
            scene.Mount(() => Column(text = Elements.Text("nn").Foreground(Red)).Foreground(Ink));
            TextStyle read = scene.Tree.TextStyleOf(text.Node);

            scene.Tree.SetTextStyle(text.Node, read with { Font = read.Font with { Weight = FontWeight.Bold } });
            scene.Draw();

            Assert.Equal(Red, read.Color);
            Assert.Equal(FontWeight.Bold.Value, scene.Leaf(text).Style.Font.Weight.Value);
            Assert.Equal(Red, scene.Leaf(text).Color);
            Assert.Equal(Red, scene.Commands()[0].Color);
        });

    /// <summary>
    /// SetTextColor and SetTextStyle write the same color, so the later of the two wins.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A column whose foreground is ink, around a text "nn" given the style of 20 px in red, drawn.
    ///
    /// Steps:
    /// - Set the text color green; draw; read the color. Write the red style again; draw; read it. Set the text color red; draw;
    ///   write the red style again; read whether a frame is asked for. Set the text color null; draw; read the color.
    ///
    /// Expected:
    /// - Green, then red; no frame asked for by the red style over the red text color; then ink.
    /// </remarks>
    [Fact]
    public void SetTextColorAndSetTextStyle_TheLaterWins() =>
        ui.Invoke(() =>
        {
            using var scene = new TextScene(_font);
            Element text = default;
            scene.Mount(() => Column(text = Elements.Text("nn")).Foreground(Ink));
            TextStyle red = Style() with { Color = Red };
            scene.Tree.SetTextStyle(text.Node, red);
            scene.Draw();

            scene.Tree.SetTextColor(text.Node, Green);
            scene.Draw();
            Color afterColor = scene.Commands()[0].Color;
            scene.Tree.SetTextStyle(text.Node, red);
            scene.Draw();
            Color afterStyle = scene.Commands()[0].Color;
            scene.Tree.SetTextColor(text.Node, Red);
            scene.Draw();
            scene.Tree.SetTextStyle(text.Node, red);
            bool asked = scene.Scheduler.Pending;
            scene.Tree.SetTextColor(text.Node, null);
            scene.Draw();

            Assert.Equal(Green, afterColor);
            Assert.Equal(Red, afterStyle);
            Assert.False(asked);
            Assert.Equal(Ink, scene.Commands()[0].Color);
        });

    /// <summary>
    /// A text layout given a style whose color alone differs keeps its lines and its caches and draws at once in the new color;
    /// one whose key differs is laid out again before it is drawn.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A box whose draw function runs the steps on a layout of "nn" at 20 px with no color, under a column whose foreground is
    ///   ink.
    ///
    /// Steps:
    /// - Set the first style again and lay the layout out. Set its style with red; read whether it is laid out, its shaped text, and its committed entry; draw it
    ///   at (0, 0). Set its style with red at 30 px; read whether it is laid out.
    ///
    /// Expected:
    /// - After red: laid out, the shaped text valid, the same committed entry, and one glyph run in red. After 30 px: not laid out.
    /// </remarks>
    [Fact]
    public void TextLayoutSetStyle_ColorAlone_KeepsTheLinesAndCaches() =>
        ui.Invoke(() =>
        {
            var layout = new TextLayout("nn", Style());
            int committed = -2;
            (bool Laid, bool Shaped, int Committed) afterColor = default;
            bool laidAfterSize = true;

            using var scene = new TextScene(_font);
            scene.Mount(() =>
                Column(
                        Box()
                            .Width(100)
                            .Height(50)
                            .Draw(
                                (DrawingContext context, Rect box) =>
                                {
                                    // Back to the first style, should the frames draw the box more than once.
                                    layout.SetStyle(Style());
                                    _ = context.LayoutText(layout, float.PositiveInfinity);
                                    committed = layout.Leaf.LineBreaks!.Committed;
                                    layout.SetStyle(Style() with { Color = Red });
                                    afterColor = (layout.Laid, layout.Leaf.Shaped!.Valid, layout.Leaf.LineBreaks.Committed);
                                    context.DrawText(layout, 0, 0);
                                    layout.SetStyle(Style() with { Size = 30, Color = Red });
                                    laidAfterSize = layout.Laid;
                                }
                            )
                    )
                    .Foreground(Ink)
            );

            Assert.True(committed >= 0);
            Assert.Equal((true, true, committed), afterColor);
            Assert.False(laidAfterSize);
            Assert.Equal([Red], scene.Commands().ConvertAll(drawn => drawn.Color));
        });

    /// <summary>
    /// DrawText with no color draws a layout in its style's color, and a layout with no color in the color the draw function's node
    /// resolves: its ancestors' foreground, its own, and its pseudo-states' over it; a color given still wins over the style's.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A column whose foreground is ink, around a box whose draw function lays out and draws a red layout at (0, 0), a layout with
    ///   no color at (0, 30), and the red layout again in green, given, at (0, 60); the box's hovered style has a green foreground.
    ///
    /// Steps:
    /// - Mount and read the glyph runs' colors. Set the box's own foreground red; draw; read them. Hover the box; draw; read them.
    ///
    /// Expected:
    /// - Red, ink, green; then red, red, green; then red, green, green.
    /// </remarks>
    [Fact]
    public void DrawText_WithNoColorGiven_TakesTheStyleColor_ThenTheNodesResolvedColor() =>
        ui.Invoke(() =>
        {
            var red = new TextLayout("nn", Style() with { Color = Red });
            var plain = new TextLayout("nn", Style());
            Element box = default;

            using var scene = new TextScene(_font);
            scene.Mount(() =>
                Column(
                        box = Box()
                            .Width(100)
                            .Height(90)
                            .Draw(
                                (DrawingContext context, Rect bounds) =>
                                {
                                    _ = context.LayoutText(red, float.PositiveInfinity);
                                    _ = context.LayoutText(plain, float.PositiveInfinity);
                                    context.DrawText(red, 0, 0);
                                    context.DrawText(plain, 0, 30);
                                    context.DrawText(red, 0, 60, Green);
                                }
                            )
                    )
                    .Foreground(Ink)
            );
            scene.Tree.SetHoveredStyle(box.Node, new PaintStyle { Foreground = Green });
            scene.Draw();
            List<Color> inherited = scene.Commands().ConvertAll(drawn => drawn.Color);

            _ = box.Foreground(Red);
            scene.Draw();
            List<Color> own = scene.Commands().ConvertAll(drawn => drawn.Color);

            scene.Tree.SetPseudoState(box.Node, PseudoState.Hovered, true);
            scene.Draw();
            List<Color> hovered = scene.Commands().ConvertAll(drawn => drawn.Color);

            Assert.Equal([Red, Ink, Green], inherited);
            Assert.Equal([Red, Red, Green], own);
            Assert.Equal([Red, Green, Green], hovered);
        });

    /// <summary>
    /// Writing a style whose color alone changes, and the frames that draw it and a text layout with no color, allocate nothing.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A column whose foreground is ink, around a text "nn" given the style of 20 px and a box whose draw function lays out and
    ///   draws a layout of "nn" with no color; the iteration run four times to warm up.
    ///
    /// Steps:
    /// - 1,000 runs in the windows of <c>PerformanceAssert.DoesNotAllocate</c>, its unmeasured window and the windows it runs again
    ///   included: by the run's index, even or odd, write the text's style in red or green, and draw the frame it asks for.
    ///
    /// Expected:
    /// - 0 bytes in every window; at the end the text is green, the color of the last run (index 999).
    /// </remarks>
    [Fact]
    public void SetTextStyle_ColorAlone_AndItsFrames_AllocateNothing() =>
        ui.Invoke(() =>
        {
            var layout = new TextLayout("nn", Style());
            using var scene = new TextScene(_font);
            Element text = default;
            scene.Mount(() =>
                Column(
                        text = Elements.Text("nn"),
                        Box()
                            .Width(100)
                            .Height(30)
                            .Draw(
                                (DrawingContext context, Rect box) =>
                                {
                                    _ = context.LayoutText(layout, float.PositiveInfinity);
                                    context.DrawText(layout, 0, 0);
                                }
                            )
                    )
                    .Foreground(Ink)
            );
            TextStyle red = Style() with { Color = Red };
            TextStyle green = Style() with { Color = Green };

            void Iteration(int index)
            {
                scene.Tree.SetTextStyle(text.Node, (index & 1) == 0 ? red : green);
                scene.Draw();
            }

            for (int i = 0; i < 4; i++)
            {
                Iteration(i);
            }

            _ = PerformanceAssert.DoesNotAllocate(1_000, Iteration);

            // The last run's index is 999, an odd one.
            Assert.Equal(Green, scene.Leaf(text).Color);
        });

    private static TextStyle Style() => new(FontRequest.Default, 20, TestFont.Japanese, float.NaN);

    // Takes the leaf's own marks off, which the scene's draw keeps.
    private static void Unmark(TextScene scene, Element text)
    {
        ref DirtyState dirty = ref scene.Tree.Nodes.GetRef(text.Node).Dirty;
        dirty.SelfDirty = DirtyFlags.None;
        dirty.SubtreeDirty = DirtyFlags.None;
    }

    private static DirtyFlags Marks(TextScene scene, Element text)
    {
        DirtyState dirty = scene.Tree.Nodes.GetRef(text.Node).Dirty;
        return dirty.SelfDirty | dirty.SubtreeDirty;
    }
}
