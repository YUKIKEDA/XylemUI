using XylemUI.Text.LineBreak;

namespace XylemUI.Text.Tests;

// The selection a text leaf draws (row P02b2, design §4.10, "Drawing"): the rectangles behind the selected glyphs, line by line,
// the selected glyphs in the selection's color, over a range's look, and a change that draws again with no layout. The text is
// 14 px: "n" advances 8.54 px.
[Collection(UiThreadGroup.Name)]
public sealed class TextSelectionDrawTests(UiThreadFixture ui) : IDisposable
{
    private const float Size = 14;
    private const float N = 0.61f * Size;
    private static readonly Color Red = Color.FromRgb(0xFF0000);
    private static readonly Color Blue = Color.FromRgb(0x0000FF);
    private static readonly Color Green = Color.FromRgb(0x00FF00);
    private readonly Font _font = TestFont.Open();

    public void Dispose() => _font.Dispose();

    /// <summary>
    /// A selection fills its background behind the selected glyphs, from the line's top to its bottom, and draws them in its text
    /// color as a glyph run of their own; the glyphs around it keep the leaf's color.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A leaf of "nnnnnn", selected from 2 to 4 in blue on red.
    ///
    /// Steps:
    /// - Mount, select, and draw.
    ///
    /// Expected:
    /// - First a red rectangle from (17.08, 0) to (34.16, the line's height), its edges on the nearest pixels; then three glyph runs
    ///   of 2 glyphs, the middle one blue and the other two of the leaf's color.
    /// </remarks>
    [Fact]
    public void Draw_Selection_FillsBehindAndColorsTheGlyphs() =>
        ui.Invoke(() =>
        {
            using var scene = new TextScene(_font);
            Element text = default;
            scene.Mount(() => text = Elements.Text("nnnnnn"));
            scene.Tree.SetTextSelection(text.Node, 2, 4, Red, Blue);
            scene.Draw();

            List<TextScene.Drawn> commands = scene.Commands();
            Assert.Equal([CommandTag.FillRect, CommandTag.GlyphRun, CommandTag.GlyphRun, CommandTag.GlyphRun], commands.Select(c => c.Tag));
            TextScene.Drawn rect = commands[0];
            Assert.Equal((Red, MathF.Round(2 * N), 0f), (rect.Color, rect.A, rect.B));
            Assert.Equal((MathF.Round(4 * N) - MathF.Round(2 * N), MathF.Round(LineHeight())), (rect.C, rect.D));
            Assert.Equal([2, 2, 2], commands.Skip(1).Select(c => c.Glyphs));
            Assert.Equal(Blue, commands[2].Color);
            Assert.NotEqual(Blue, commands[1].Color);
            Assert.Equal(commands[1].Color, commands[3].Color);
        });

    /// <summary>
    /// A selection across a hard line break fills a rectangle on each line it reaches, under the selected glyphs of that line; a line
    /// it does not reach draws as before, one glyph run of the leaf's color.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A leaf of "nn\nnn\nnn" (three lines), selected from 1 to 4: the second "n" of the first line and the first of the second.
    ///
    /// Steps:
    /// - Mount, select, and draw.
    ///
    /// Expected:
    /// - Two red rectangles: one from x = 8.54 on the first line to the end of the hard break's glyph (2 n and the break's advance,
    ///   read from the leaf's layout), as P02b's field drew it with a range's background; and one from x = 0 on the second line, one
    ///   "n" wide; no rectangle on the third line, whose "nn" is one glyph run of the leaf's color.
    /// </remarks>
    [Fact]
    public void Draw_SelectionAcrossLines_FillsEachLineItReaches() =>
        ui.Invoke(() =>
        {
            using var scene = new TextScene(_font);
            Element text = default;
            scene.Mount(() => text = Elements.Text("nn\nnn\nnn"));
            scene.Tree.SetTextSelection(text.Node, 1, 4, Red, Blue);
            scene.Draw();

            List<TextScene.Drawn> rects = scene.Commands().FindAll(c => c.Tag == CommandTag.FillRect);
            float line = LineHeight();
            float end = MathF.Round((2 * N) + Advance(scene.Leaf(text), 2));
            Assert.Equal(2, rects.Count);
            Assert.Equal((MathF.Round(N), 0f, end - MathF.Round(N)), (rects[0].A, rects[0].B, rects[0].C));
            Assert.Equal((0f, MathF.Round(line), MathF.Round(N)), (rects[1].A, rects[1].B, rects[1].C));
            TextScene.Drawn last = scene.Commands()[^1];
            Assert.Equal((CommandTag.GlyphRun, 2), (last.Tag, last.Glyphs));
            Assert.NotEqual(Blue, last.Color);
        });

    /// <summary>
    /// Over a range's look, the selection's background is drawn after the range's, and the selected glyphs take the selection's
    /// text color in place of the range's.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A leaf of "nnnn" in a range of green text on a blue background, selected from 1 to 3 in blue on red.
    ///
    /// Steps:
    /// - Mount, select, and draw.
    ///
    /// Expected:
    /// - Blue rectangles of the range behind the first and the last "n", the red one behind the middle two after the blue one before
    ///   it; glyph runs of 1, 2, and 1 glyphs: green, blue, green.
    /// </remarks>
    [Fact]
    public void Draw_SelectionOverARange_TakesTheSelectionsLook() =>
        ui.Invoke(() =>
        {
            using var scene = new TextScene(_font);
            Element text = default;
            scene.Mount(() =>
                text = Elements.Text(
                    (ref w) =>
                    {
                        using (w.Style(new SpanStyle(Color: Green, Background: Blue)))
                        {
                            w.Write("nnnn".AsSpan());
                        }
                    }
                )
            );
            scene.Tree.SetTextSelection(text.Node, 1, 3, Red, Blue);
            scene.Draw();

            List<TextScene.Drawn> commands = scene.Commands();
            List<TextScene.Drawn> rects = commands.FindAll(c => c.Tag == CommandTag.FillRect);
            List<TextScene.Drawn> runs = commands.FindAll(c => c.Tag == CommandTag.GlyphRun);
            Assert.Equal([Blue, Blue, Red, Blue], rects.Select(c => c.Color));
            Assert.Equal([0f, MathF.Round(N), MathF.Round(N), MathF.Round(3 * N)], rects.Select(c => c.A));
            Assert.Equal([1, 2, 1], runs.Select(c => c.Glyphs));
            Assert.Equal([Green, Blue, Green], runs.Select(c => c.Color));
        });

    /// <summary>
    /// A change of the selection draws again with no layout; the same selection again marks nothing; an empty one draws none.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A leaf of "nnnn", mounted and drawn.
    ///
    /// Steps:
    /// - Select 1 to 2; read the marks; draw. Select 1 to 2 again; read the marks. Select 3 to 3; draw.
    ///
    /// Expected:
    /// - The first selection asks for a frame and no layout; the same one asks for neither; the empty one leaves no rectangle and
    ///   one glyph run.
    /// </remarks>
    [Fact]
    public void Leaf_SelectionChange_DrawsWithNoLayout() =>
        ui.Invoke(() =>
        {
            using var scene = new TextScene(_font);
            Element text = default;
            scene.Mount(() => text = Elements.Text("nnnn"));
            scene.Draw();

            scene.Tree.SetTextSelection(text.Node, 1, 2, Red, Blue);
            (bool Laid, bool Painted) first = (scene.Tree.LayoutRoots.Count > 0, scene.Tree.PaintUpdates.Count > 0);
            scene.Draw();
            scene.Tree.SetTextSelection(text.Node, 1, 2, Red, Blue);
            (bool Laid, bool Painted) same = (scene.Tree.LayoutRoots.Count > 0, scene.Tree.PaintUpdates.Count > 0);
            scene.Tree.SetTextSelection(text.Node, 3, 3, Red, Blue);
            scene.Draw();

            Assert.Equal((false, true), first);
            Assert.Equal((false, false), same);
            Assert.Equal([CommandTag.GlyphRun], scene.Commands().Select(c => c.Tag));
        });

    /// <summary>
    /// A frame whose selection moves allocates nothing: the leaf keeps the selection it made the first time.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A leaf of "nn\nnnnn\nnn", warmed up with the iteration.
    ///
    /// Steps:
    /// - 1,000 iterations in the windows of <c>PerformanceAssert.DoesNotAllocate</c>: select 1 to 5 or 2 to 8 by the iteration's
    ///   number, run the frame asked for.
    ///
    /// Expected:
    /// - 0 bytes in every window.
    /// </remarks>
    [Fact]
    public void SteadyState_SelectionMoved_AllocatesNothing() =>
        ui.Invoke(() =>
        {
            using var scene = new TextScene(_font);
            Element text = default;
            scene.Mount(() => text = Elements.Text("nn\nnnnn\nnn"));

            void Iteration(int index)
            {
                bool odd = index % 2 == 1;
                scene.Tree.SetTextSelection(text.Node, odd ? 2 : 1, odd ? 8 : 5, Red, Blue);
                scene.Scheduler.Run();
            }

            for (int i = 0; i < 200; i++)
            {
                Iteration(i);
            }

            PerformanceAssert.DoesNotAllocate(1_000, Iteration);
        });

    /// <summary>
    /// A selection that reaches the leaf's end fills to the end of its last glyph.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A leaf of "nn\nnnn" (two lines), selected from 4 to its end, 6.
    ///
    /// Steps:
    /// - Mount, select, and draw.
    ///
    /// Expected:
    /// - One red rectangle on the second line, from x = 8.54 to the end of the last "n", 3 n; the first line draws none.
    /// </remarks>
    [Fact]
    public void Draw_SelectionToTheLeafsEnd_FillsToTheLastGlyph() =>
        ui.Invoke(() =>
        {
            using var scene = new TextScene(_font);
            Element text = default;
            scene.Mount(() => text = Elements.Text("nn\nnnn"));
            scene.Tree.SetTextSelection(text.Node, 4, 6, Red, Blue);
            scene.Draw();

            TextScene.Drawn rect = Assert.Single(scene.Commands().FindAll(c => c.Tag == CommandTag.FillRect));
            Assert.Equal((MathF.Round(N), MathF.Round(LineHeight()), MathF.Round(3 * N) - MathF.Round(N)), (rect.A, rect.B, rect.C));
        });

    /// <summary>
    /// The room of an inline object is not selected (design §4.8, "Caret and selection", and §4.10): a selection across it fills
    /// the glyphs on its two sides and leaves the room's width without a rectangle.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A row holding a leaf of "nn", an inline box 30 wide, and "nn"; selected from 1 to 4 (the second "n", the room, the third
    ///   "n").
    ///
    /// Steps:
    /// - Mount, select, and draw.
    ///
    /// Expected:
    /// - Two red rectangles: one under the second "n", from 8.54 to 17.08, and one under the third, from 17.08 + 30 to 25.62 + 30;
    ///   none over the room between them.
    /// </remarks>
    [Fact]
    public void Draw_SelectionAcrossAnInlineObject_LeavesItsRoom() =>
        ui.Invoke(() =>
        {
            using var scene = new TextScene(_font);
            Element text = default;
            scene.Mount(() =>
            {
                Element box = Elements.Box().Width(30).Height(10);
                text = Elements.Text(
                    (ref w) =>
                    {
                        w.Write("nn".AsSpan());
                        w.Inline(box);
                        w.Write("nn".AsSpan());
                    }
                );
                return Elements.Row(text);
            });
            scene.Tree.SetTextSelection(text.Node, 1, 4, Red, Blue);
            scene.Draw();

            List<TextScene.Drawn> rects = scene.Commands().FindAll(c => c.Tag == CommandTag.FillRect && c.Color == Red);
            Assert.Equal(
                [
                    (MathF.Round(N), MathF.Round(2 * N) - MathF.Round(N)),
                    (MathF.Round((2 * N) + 30), MathF.Round((3 * N) + 30) - MathF.Round((2 * N) + 30)),
                ],
                rects.Select(c => (c.A, c.C))
            );
        });

    /// <summary>
    /// A selection with no colors of its own draws in Windows' colors as the tree reads them at each frame: after the settings
    /// change, the same selection draws in the new colors, with no new selection set.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A leaf of "nnnn", selected from 1 to 3 with no colors.
    ///
    /// Steps:
    /// - Draw; read the rectangle and the selected glyph run's color. Set the tree's settings to a green background and a red text
    ///   (as WM_SYSCOLORCHANGE does); draw; read them again.
    ///
    /// Expected:
    /// - First the default settings' colors, then green and red.
    /// </remarks>
    [Fact]
    public void Draw_SelectionWithNoColors_TakesTheOsColorsOfTheFrame() =>
        ui.Invoke(() =>
        {
            using var scene = new TextScene(_font);
            Element text = default;
            scene.Mount(() => text = Elements.Text("nnnn"));
            scene.Tree.SetTextSelection(text.Node, 1, 3, null, null);
            scene.Draw();
            (Color Back, Color Ink) before = Colors(scene.Commands());
            scene.Tree.SetTextEditing(TextEditingSettings.Default with { SelectionBackground = Green, SelectionForeground = Red });
            scene.Draw();

            TextEditingSettings defaults = TextEditingSettings.Default;
            Assert.Equal((defaults.SelectionBackground, defaults.SelectionForeground), before);
            Assert.Equal((Green, Red), Colors(scene.Commands()));
        });

    // The selection's rectangle and the color of the middle glyph run (the selected one) of a leaf drawn in three runs.
    private static (Color Back, Color Ink) Colors(List<TextScene.Drawn> commands) =>
        (Assert.Single(commands.FindAll(c => c.Tag == CommandTag.FillRect)).Color, commands.FindAll(c => c.Tag == CommandTag.GlyphRun)[1].Color);

    // The advance of the glyphs of a cluster in the leaf's committed layout.
    private static float Advance(TextLeaf leaf, int cluster)
    {
        LineBreakCache cache = leaf.LineBreaks!;
        float advance = 0;
        foreach (LaidGlyph glyph in cache[cache.Committed].Layout.Glyphs)
        {
            if (glyph.Cluster == cluster)
            {
                advance += glyph.Advance;
            }
        }

        return advance;
    }

    private float LineHeight() => (_font.Ascent + _font.Descent + _font.LineGap) * Size / _font.UnitsPerEm;
}
