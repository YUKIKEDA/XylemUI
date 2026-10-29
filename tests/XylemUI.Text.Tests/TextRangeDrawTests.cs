namespace XylemUI.Text.Tests;

// How the ranges of a text leaf are drawn (row T12, design §4.8): the glyph runs cut at the look's ranges with their colors, the
// backgrounds from the line's top to its bottom, and the decoration lines at the font's positions in their four shapes, the
// underline skipping the outlines. The text is 14 px: "n" advances 8.54 px.
[Collection(UiThreadGroup.Name)]
public sealed class TextRangeDrawTests(UiThreadFixture ui) : IDisposable
{
    private const float Size = 14;
    private const float N = 0.61f * Size;
    private static readonly Color Red = Color.FromRgb(0xFF0000);
    private static readonly Color Blue = Color.FromRgb(0x0000FF);
    private readonly Font _font = TestFont.Open();

    public void Dispose() => _font.Dispose();

    /// <summary>
    /// A range of a color of its own is a glyph run of its own, in that color; the text around it keeps the leaf's.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A text "nn", "nn" in a range of red, and "nn".
    ///
    /// Steps:
    /// - Mount and draw.
    ///
    /// Expected:
    /// - Three glyph runs of 2 glyphs: the first and the last of one color that is not red, the middle red.
    /// </remarks>
    [Fact]
    public void Draw_RangeOfAColor_IsAGlyphRunInIt() =>
        ui.Invoke(() =>
        {
            using var scene = new TextScene(_font);
            scene.Mount(() =>
                Elements.Text(
                    (ref w) =>
                    {
                        w.Write("nn".AsSpan());
                        using (w.Style(new SpanStyle(Color: Red)))
                        {
                            w.Write("nn".AsSpan());
                        }

                        w.Write("nn".AsSpan());
                    }
                )
            );

            List<TextScene.Drawn> runs = scene.Commands();
            Assert.All(runs, c => Assert.Equal(CommandTag.GlyphRun, c.Tag));
            Assert.Equal([2, 2, 2], runs.Select(c => c.Glyphs));
            Assert.Equal(Red, runs[1].Color);
            Assert.NotEqual(Red, runs[0].Color);
            Assert.Equal(runs[0].Color, runs[2].Color);
        });

    /// <summary>
    /// A range's background is drawn before the text, under the range's glyphs, from the line's top to its bottom.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A text "nn", then "nn" in a range of a blue background.
    ///
    /// Steps:
    /// - Mount and draw.
    ///
    /// Expected:
    /// - First a blue rectangle from (17.08, 0) to (34.16, the line's height of 1.448 em), its edges on the nearest pixels (design
    ///   §6.1.1); then one glyph run.
    /// </remarks>
    [Fact]
    public void Draw_Background_FillsTheLineUnderTheRange() =>
        ui.Invoke(() =>
        {
            using var scene = new TextScene(_font);
            scene.Mount(() =>
                Elements.Text(
                    (ref w) =>
                    {
                        w.Write("nn".AsSpan());
                        using (w.Style(new SpanStyle(Background: Blue)))
                        {
                            w.Write("nn".AsSpan());
                        }
                    }
                )
            );

            List<TextScene.Drawn> commands = scene.Commands();
            Assert.Equal([CommandTag.FillRect, CommandTag.GlyphRun], commands.Select(c => c.Tag));
            TextScene.Drawn rect = commands[0];
            Assert.Equal(Blue, rect.Color);
            Assert.Equal(MathF.Round(2 * N), rect.A);
            Assert.Equal(0, rect.B);
            Assert.Equal(MathF.Round(4 * N) - MathF.Round(2 * N), rect.C);
            Assert.Equal(MathF.Round(LineHeight()), rect.D);
        });

    /// <summary>
    /// An underline is drawn after the text, at the font's underline position and thickness, across the range, in the decoration's
    /// color; with no color of its own, in the range's text color. Hyphens do not reach it, so it is whole.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A text of two ranges: "--" underlined in blue, and "--" in red underlined with no color.
    ///
    /// Steps:
    /// - Mount and draw.
    ///
    /// Expected:
    /// - Two glyph runs, then two solid lines: the first blue from 0, the second red from the end of the first range, each two
    ///   hyphens long, with its center at the baseline less the font's underline offset, as SkParagraph and Avalonia center it, and
    ///   its width the font's underline size.
    /// </remarks>
    [Fact]
    public void Draw_Underline_IsAtTheFontsPosition() =>
        ui.Invoke(() =>
        {
            using var scene = new TextScene(_font);
            scene.Mount(() =>
                Elements.Text(
                    (ref w) =>
                    {
                        using (w.Style(new SpanStyle(Decoration: new TextDecoration(DecorationLines.Underline, Color: Blue))))
                        {
                            w.Write("--".AsSpan());
                        }

                        using (w.Style(new SpanStyle(Color: Red, Decoration: new TextDecoration(DecorationLines.Underline))))
                        {
                            w.Write("--".AsSpan());
                        }
                    }
                )
            );

            float hyphens = 2 * Advance("-");
            float units = Size / _font.UnitsPerEm;
            DecorationMetrics metrics = _font.Decorations;
            List<TextScene.Drawn> commands = scene.Commands();
            Assert.Equal([CommandTag.GlyphRun, CommandTag.GlyphRun, CommandTag.Line, CommandTag.Line], commands.Select(c => c.Tag));
            Assert.Equal([Blue, Red], commands.Skip(2).Select(c => c.Color));
            Assert.Equal([0, hyphens], commands.Skip(2).Select(c => c.A), new Near());
            float thickness = metrics.UnderlineSize!.Value * units;
            Assert.All(
                commands.Skip(2),
                line =>
                {
                    Assert.Equal(line.A + hyphens, line.C, 3);
                    Assert.Equal(Baseline() - (metrics.UnderlineOffset!.Value * units), line.B, 3);
                    Assert.Equal(line.B, line.D);
                    Assert.Equal(thickness, line.Width, 3);
                    Assert.Null(line.Dash);
                }
            );
        });

    /// <summary>
    /// A double line is two lines 3 px apart; a dotted one a line of dots of a fourteenth of the size and gaps of one and a half;
    /// a dashed one dashes of four and gaps of two; both with flat ends and the line's thickness.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - Three texts "nn" with a line through: double, dotted, and dashed.
    ///
    /// Steps:
    /// - Mount and draw.
    ///
    /// Expected:
    /// - Four lines from 0 to 17.08, as wide as the font's strikeout size: the double text's two solid ones, the second 3 px below
    ///   the first, the dotted one with the dash (1, 1.5), and the dashed one with (4, 2).
    /// </remarks>
    [Fact]
    public void Draw_LineShapes_DoubleDottedDashed() =>
        ui.Invoke(() =>
        {
            using var scene = new TextScene(_font);
            scene.Mount(() =>
                Elements.Column(
                    Through(DecorationStyle.Double),
                    Elements.Box().Height(50),
                    Through(DecorationStyle.Dotted),
                    Elements.Box().Height(50),
                    Through(DecorationStyle.Dashed)
                )
            );

            float units = Size / _font.UnitsPerEm;
            float thickness = _font.Decorations.StrikeoutSize!.Value * units;
            TextScene.Drawn[] decorations = [.. scene.Commands().Where(c => c.Tag != CommandTag.GlyphRun)];
            Assert.Equal([CommandTag.Line, CommandTag.Line, CommandTag.Line, CommandTag.Line], decorations.Select(c => c.Tag));
            Assert.Equal(decorations[0].B + 3, decorations[1].B, 3);
            Assert.Equal([null, null, new Dash(1, 1.5f), new Dash(4, 2)], decorations.Select(c => c.Dash));
            foreach (TextScene.Drawn line in decorations)
            {
                Assert.Equal(0, line.A, 3);
                Assert.Equal(2 * N, line.C, 3);
                Assert.Equal(line.B, line.D);
                Assert.Equal(thickness, line.Width, 3);
            }
        });

    /// <summary>
    /// An underline thick enough to reach the stems of "n" is cut where they cross it, and a line through of the same thickness is
    /// not.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - Three texts "nn", 70 px apart: underlined at the font's thickness, underlined 12 times as thick, and with a line through 12 times as thick.
    ///
    /// Steps:
    /// - Mount and draw.
    ///
    /// Expected:
    /// - The underline of the font's thickness is whole: "n" stands on the baseline, above it. The thick underline's pieces together are narrower than the text; the thick line through is one line as wide as it.
    /// </remarks>
    [Fact]
    public void Draw_Underline_SkipsTheOutlines() =>
        ui.Invoke(() =>
        {
            using var scene = new TextScene(_font);
            scene.Mount(() =>
                Elements.Column(
                    Decorated(new TextDecoration(DecorationLines.Underline)),
                    Elements.Box().Height(50),
                    Decorated(new TextDecoration(DecorationLines.Underline, Thickness: 12)),
                    Elements.Box().Height(50),
                    Decorated(new TextDecoration(DecorationLines.LineThrough, Thickness: 12))
                )
            );

            TextScene.Drawn[] lines = [.. scene.Commands().Where(c => c.Tag == CommandTag.Line)];
            float Length(float from, float to) => lines.Where(l => l.B >= from && l.B < to).Sum(l => l.C - l.A);
            Assert.Equal(2 * N, Length(0, 50), 3);
            Assert.InRange(Length(50, 120), 0, (2 * N) - 1);
            Assert.Single(lines, l => l.B >= 120);
            Assert.Equal(2 * N, Length(120, 300), 3);
        });

    private static Element Through(DecorationStyle style) => Decorated(new TextDecoration(DecorationLines.LineThrough, style));

    private static Element Decorated(TextDecoration decoration) =>
        Elements.Text(
            (ref w) =>
            {
                using (w.Style(new SpanStyle(Decoration: decoration)))
                {
                    w.Write("nn".AsSpan());
                }
            }
        );

    private float Advance(string text)
    {
        using var shaper = new TextShaper();
        return shaper.Shape(_font, text, TestFont.Japanese)[0].XAdvance * Size / _font.UnitsPerEm;
    }

    private float LineHeight() => (_font.Ascent + _font.Descent + _font.LineGap) * Size / _font.UnitsPerEm;

    private float Baseline() => ((_font.LineGap / 2f) + _font.Ascent) * Size / _font.UnitsPerEm;

    private sealed class Near : IEqualityComparer<float>
    {
        public bool Equals(float x, float y) => MathF.Abs(x - y) < 1e-3f;

        public int GetHashCode(float obj) => 0;
    }
}
