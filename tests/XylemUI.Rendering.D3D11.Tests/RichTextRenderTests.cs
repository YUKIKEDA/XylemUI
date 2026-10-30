using System.Numerics;
using XylemUI.Platform.Windows;
using XylemUI.Text;
using NodeHandle = XylemUI.Handle<XylemUI.UiNode>;

namespace XylemUI.Rendering.D3D11.Tests;

// The ranges of a text leaf on the D3D11 renderer (row T12, design §4.8 and §12.1): the backgrounds and the four shapes of the
// decoration lines against a reference built on the CPU, the gap an underline leaves where a glyph crosses it, and the colors of
// the ranges against the same glyphs written straight into a display list. At scale 1 in the test font (1000 units per em).
[Collection(UiThreadGroup.Name)]
[Trait("Category", "Gpu")]
public sealed class RichTextRenderTests(UiThreadFixture ui)
{
    private static readonly byte[] FontBytes = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fonts", "XylemUITestSans.ttf"));
    private static readonly TextLanguage Japanese = TextLanguage.FromTag("ja");
    private static readonly Color Ink = Color.FromRgb(0x2040C0);
    private static readonly Color Red = Color.FromRgb(0xCC3333);
    private static readonly Color Back = Color.FromRgb(0xE8E8E8);
    private static readonly Color Green = Color.FromRgb(0x33AA66);

    /// <summary>
    /// A range's background fills the line under it with its edges on pixels, and its lines are drawn where the font puts them, in
    /// their four shapes, over the background: the pixels are those of a reference built from the font's metrics.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A WARP renderer; four text leaves at (10, 10), (10, 60), (10, 110), and (10, 150), each of eight spaces at 20 px, which
    ///   have no ink, in one range: a light background with a solid red underline; a double red overline; a dotted red line through;
    ///   a dashed red underline.
    ///
    /// Steps:
    /// - Run the frames' resize, draw the list, and read the pixels of each leaf's box grown by 2.
    ///
    /// Expected:
    /// - Each pixel is within 1 of the reference over the clear color: the background's rectangle with its edges rounded to pixels,
    ///   under lines with flat ends from the leaf's left to the end of its eight advances, as thick as the font's line, at the
    ///   font's underline center, at the ascent and 3 px below it, at the strikeout's center with dots of 20 / 14 px and gaps of
    ///   1.5 of them, and at the underline center with dashes of 4 and gaps of 2 of them.
    /// </remarks>
    [Fact]
    public void Decorations_AreDrawnAtTheFontsLines() =>
        ui.Invoke(() =>
        {
            using var window = new Win32Window("rich text", 300, 200);
            (int width, int height) = window.ClientSize;
            using var renderer = D3D11Renderer.Create(window.Handle, width, height, useWarp: true);
            using var rasterizer = new DirectWriteGlyphRasterizer();
            using var atlas = new GlyphAtlas(rasterizer);
            using var font = new Font(FontBytes);
            using var context = new TextContext(new DrawStageRenderTests.OneFont(font));
            const string Spaces = "        ";
            var tree = new UiTree(context);
            NodeHandle root = tree.CreateNode(new LayoutStyle());
            (float Top, SpanStyle Style)[] rows =
            [
                (10, new SpanStyle(Background: Back, Decoration: new TextDecoration(DecorationLines.Underline, Color: Red))),
                (60, new SpanStyle(Decoration: new TextDecoration(DecorationLines.Overline, DecorationStyle.Double, Red))),
                (110, new SpanStyle(Decoration: new TextDecoration(DecorationLines.LineThrough, DecorationStyle.Dotted, Red))),
                (150, new SpanStyle(Decoration: new TextDecoration(DecorationLines.Underline, DecorationStyle.Dashed, Red))),
            ];
            foreach ((float top, SpanStyle style) in rows)
            {
                _ = Leaf(tree, root, Spaces, 20, top, (0, Spaces.Length, style));
            }

            var frames = new WindowFrames(
                tree,
                root,
                new DrawStageRenderTests.NoTarget(),
                atlas,
                new DrawStageRenderTests.NoScheduler(),
                DrawStageRenderTests.Clear
            );
            frames.Resize(new Size(width, height), 1);
            Color[] pixels = DrawStageRenderTests.Draw(renderer, frames.List, width, height, atlas);

            float units = 20f / font.UnitsPerEm;
            DecorationMetrics metrics = font.Decorations;
            float advance = context.Shaper.Shape(font, " ", Japanese)[0].XAdvance * units;
            float right = 10 + (8 * advance);
            float ascent = font.Ascent * units;
            float line = (font.Ascent + font.Descent + font.LineGap) * units;
            float baseline = (font.LineGap * units / 2) + ascent;
            float underline = metrics.UnderlineSize!.Value * units;
            float under = -metrics.UnderlineOffset!.Value * units;
            float strike = metrics.StrikeoutSize!.Value * units;
            float through = -metrics.StrikeoutOffset!.Value * units;
            float dot = 20f / 14;
            Vector4 red = ShapeReference.Premultiply(Red);

            Vector4 Line(Vector2 p, float y, float thickness, Dash? dash) =>
                red
                * CurveReference.Coverage(
                    CurveReference.Line(p, new Vector2(10, y), new Vector2(right, y), thickness / 2, LineCap.Flat, dash),
                    thickness
                );

            Vector4 Reference(Vector2 p)
            {
                float rowTop =
                    p.Y < 50 ? 10
                    : p.Y < 100 ? 60
                    : p.Y < 140 ? 110
                    : 150;
                float b = rowTop + baseline;
                return rowTop switch
                {
                    10 => Over(
                        Line(p, b + under, underline, null),
                        ShapeReference.Premultiply(Back) * ShapeReference.Coverage(p, Snapped(10, rowTop, right - 10, line), default)
                    ),
                    60 => Line(p, b - ascent, underline, null) + Line(p, b - ascent + 3, underline, null),
                    110 => Line(p, b + through, strike, new Dash(dot, 1.5f * dot)),
                    _ => Line(p, b + under, underline, new Dash(4 * dot, 2 * dot)),
                };
            }

            foreach ((float top, _) in rows)
            {
                for (int y = (int)top - 2; y < (int)(top + line) + 2; y++)
                {
                    for (int x = 8; x < (int)right + 2; x++)
                    {
                        Color expected = ShapeReference.Over(Reference(new Vector2(x + 0.5f, y + 0.5f)), DrawStageRenderTests.Clear);
                        Color actual = pixels[(y * width) + x];
                        int difference = Math.Max(
                            Math.Abs(expected.R - actual.R),
                            Math.Max(Math.Abs(expected.G - actual.G), Math.Abs(expected.B - actual.B))
                        );
                        Assert.True(difference <= 1, $"pixel ({x}, {y}): expected {expected}, got {actual}");
                    }
                }
            }
        });

    /// <summary>
    /// An underline thick enough to reach above the baseline leaves a gap where the stems of "n" cross it, and is drawn past them.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A WARP renderer; a text leaf at (10, 10) of "n" at 40 px in ink, in a range of letter spacing 60 with a green underline 8
    ///   times as thick as the font's.
    ///
    /// Steps:
    /// - Run the frames' resize, draw the list, and read the pixels on the underline's center row.
    ///
    /// Expected:
    /// - Under the glyph (10 to 34.4) and its thickness on each side no pixel is green; from 60 to 90 every pixel is.
    /// </remarks>
    [Fact]
    public void Underline_LeavesAGapWhereAGlyphCrossesIt() =>
        ui.Invoke(() =>
        {
            using var window = new Win32Window("skip ink", 300, 200);
            (int width, int height) = window.ClientSize;
            using var renderer = D3D11Renderer.Create(window.Handle, width, height, useWarp: true);
            using var rasterizer = new DirectWriteGlyphRasterizer();
            using var atlas = new GlyphAtlas(rasterizer);
            using var font = new Font(FontBytes);
            using var context = new TextContext(new DrawStageRenderTests.OneFont(font));
            var tree = new UiTree(context);
            NodeHandle root = tree.CreateNode(new LayoutStyle());
            var style = new SpanStyle(LetterSpacing: 60, Decoration: new TextDecoration(DecorationLines.Underline, Color: Green, Thickness: 8));
            _ = Leaf(tree, root, "n", 40, 10, (0, 1, style));
            var frames = new WindowFrames(
                tree,
                root,
                new DrawStageRenderTests.NoTarget(),
                atlas,
                new DrawStageRenderTests.NoScheduler(),
                DrawStageRenderTests.Clear
            );
            frames.Resize(new Size(width, height), 1);
            Color[] pixels = DrawStageRenderTests.Draw(renderer, frames.List, width, height, atlas);

            float units = 40f / font.UnitsPerEm;
            DecorationMetrics metrics = font.Decorations;
            float baseline = 10 + (font.LineGap * units / 2) + (font.Ascent * units);
            int row = (int)(baseline - (metrics.UnderlineOffset!.Value * units));
            for (int x = 10; x < 34; x++)
            {
                Assert.NotEqual(Green, pixels[(row * width) + x]);
            }

            for (int x = 60; x < 90; x++)
            {
                Assert.Equal(Green, pixels[(row * width) + x]);
            }
        });

    /// <summary>
    /// The ranges of the look are drawn in their colors at the glyphs' places: the pixels are those of the same glyphs written
    /// straight into a display list, each piece in its color.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A WARP renderer; a text leaf at (10, 10) of "nn日nn" at 20 px in ink, "日" in a range of red.
    ///
    /// Steps:
    /// - Run the frames' resize, draw the list, and read the pixels. Shape "nn", "日", and "nn" and DrawText each at its pen on the
    ///   baseline, the second in red, into another list; draw it and read the pixels.
    ///
    /// Expected:
    /// - The same pixels.
    /// </remarks>
    [Fact]
    public void RangeColors_AreTheGlyphsInTheirColors() =>
        ui.Invoke(() =>
        {
            using var window = new Win32Window("range colors", 300, 200);
            (int width, int height) = window.ClientSize;
            using var renderer = D3D11Renderer.Create(window.Handle, width, height, useWarp: true);
            using var rasterizer = new DirectWriteGlyphRasterizer();
            using var atlas = new GlyphAtlas(rasterizer);
            using var font = new Font(FontBytes);
            using var context = new TextContext(new DrawStageRenderTests.OneFont(font));
            var tree = new UiTree(context);
            NodeHandle root = tree.CreateNode(new LayoutStyle());
            _ = Leaf(tree, root, "nn日nn", 20, 10, (2, 3, new SpanStyle(Color: Red)));
            var frames = new WindowFrames(
                tree,
                root,
                new DrawStageRenderTests.NoTarget(),
                atlas,
                new DrawStageRenderTests.NoScheduler(),
                DrawStageRenderTests.Clear
            );
            frames.Resize(new Size(width, height), 1);
            Color[] viaTree = DrawStageRenderTests.Draw(renderer, frames.List, width, height, atlas);

            float units = 20f / font.UnitsPerEm;
            float baseline = 10 + (font.LineGap * units / 2) + (font.Ascent * units);
            var direct = new DisplayList();
            direct.Reset(1);
            float pen = 10;
            foreach ((string part, Color color) in (ReadOnlySpan<(string, Color)>)[("nn", Ink), ("日", Red), ("nn", Ink)])
            {
                ReadOnlySpan<ShapedGlyph> glyphs = context.Shaper.Shape(font, part, Japanese);
                _ = direct.DrawText(glyphs, font, 20, atlas, pen, baseline, color);
                foreach (ShapedGlyph glyph in context.Shaper.Shape(font, part, Japanese))
                {
                    pen += glyph.XAdvance * units;
                }
            }

            direct.ResolveGlyphs(atlas);
            Color[] written = DrawStageRenderTests.Draw(renderer, direct, width, height, atlas);
            Assert.Equal(written, viaTree);
        });

    // A text leaf at (10, top) in ink, with ranges one after another, as a writer's scopes write them.
    private static NodeHandle Leaf(
        UiTree tree,
        NodeHandle parent,
        string text,
        float size,
        float top,
        params (int Start, int End, SpanStyle Style)[] spans
    )
    {
        NodeHandle leaf = tree.CreateTextNode(
            new LayoutStyle
            {
                Position = Position.Absolute,
                Inset = new EdgeInsets<LengthPercentageAuto>(
                    LengthPercentageAuto.Px(10),
                    LengthPercentageAuto.Px(top),
                    LengthPercentageAuto.Auto,
                    LengthPercentageAuto.Auto
                ),
            },
            new TextStyle(FontRequest.Default, size, Japanese, float.NaN)
        );
        tree.SetText(leaf, text);
        tree.SetTextColor(leaf, Ink);
        var writer = new TextRangeWriter();
        writer.Reset();
        foreach ((int start, int end, SpanStyle style) in spans)
        {
            writer.Open(start, style, -1);
            writer.Close(end);
        }

        writer.Finish(text.Length);
        _ = tree.Nodes.GetRef(leaf).Text!.TakeRanges(writer, out _);
        tree.AppendChild(parent, leaf);
        return leaf;
    }

    /// <summary>
    /// An inline object is drawn over its room after the text: the range's background goes under the room, the underline stops at
    /// it, and the child's own background fills its box where its alignment puts it (row T13, design §4.8).
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A WARP renderer; a text leaf at (10, 10) at 20 px of two spaces, a room, and two spaces, in a range of a light background
    ///   and a red underline; in the room, a box of 12 x 8 with a green background, aligned by its bottom on the baseline.
    ///
    /// Steps:
    /// - Run the frames' resize, draw the list, and read the pixels of the leaf's box grown by 2.
    ///
    /// Expected:
    /// - Each pixel is within 1 of the reference over the clear color: the background's rectangle with its edges on pixels; the
    ///   underline at the font's underline center from the leaf's left to the room and from the room's right to the end of the last
    ///   space; over both, the green box with its edges on pixels, 12 wide from the end of the second space, its bottom on the
    ///   baseline.
    /// </remarks>
    [Fact]
    public void InlineObject_IsDrawnInItsRoom() =>
        ui.Invoke(() =>
        {
            using var window = new Win32Window("inline object", 300, 200);
            (int width, int height) = window.ClientSize;
            using var renderer = D3D11Renderer.Create(window.Handle, width, height, useWarp: true);
            using var rasterizer = new DirectWriteGlyphRasterizer();
            using var atlas = new GlyphAtlas(rasterizer);
            using var font = new Font(FontBytes);
            using var context = new TextContext(new DrawStageRenderTests.OneFont(font));
            var tree = new UiTree(context);
            NodeHandle root = tree.CreateNode(new LayoutStyle());
            const string Text = "  ￼  ";
            var style = new SpanStyle(Background: Back, Decoration: new TextDecoration(DecorationLines.Underline, Color: Red));
            NodeHandle leaf = Leaf(tree, root, Text, 20, 10, (0, Text.Length, style));
            NodeHandle box = tree.CreateNode(new LayoutStyle { Width = Length.Px(12), Height = Length.Px(8) });
            tree.SetBackground(box, new Paint(Green));
            var writer = new TextRangeWriter { Tree = tree, Owner = leaf };
            writer.Reset();
            writer.Open(0, style, -1);
            writer.AddInline(2, new Element(tree, box), PlaceholderAlignment.AboveBaseline);
            writer.Close(Text.Length);
            writer.Finish(Text.Length);
            _ = tree.Nodes.GetRef(leaf).Text!.TakeRanges(writer, out _);
            tree.AppendChild(leaf, box);
            var frames = new WindowFrames(
                tree,
                root,
                new DrawStageRenderTests.NoTarget(),
                atlas,
                new DrawStageRenderTests.NoScheduler(),
                DrawStageRenderTests.Clear
            );
            frames.Resize(new Size(width, height), 1);
            Color[] pixels = DrawStageRenderTests.Draw(renderer, frames.List, width, height, atlas);

            float units = 20f / font.UnitsPerEm;
            float advance = context.Shaper.Shape(font, " ", Japanese)[0].XAdvance * units;
            float room = 10 + (2 * advance);
            float right = room + 12 + (2 * advance);
            float line = (font.Ascent + font.Descent + font.LineGap) * units;
            float baseline = 10 + (font.LineGap * units / 2) + (font.Ascent * units);
            float underline = font.Decorations.UnderlineSize!.Value * units;
            float under = baseline - (font.Decorations.UnderlineOffset!.Value * units);
            Vector4 red = ShapeReference.Premultiply(Red);

            Vector4 Line(Vector2 p, float from, float to) =>
                red
                * CurveReference.Coverage(
                    CurveReference.Line(p, new Vector2(from, under), new Vector2(to, under), underline / 2, LineCap.Flat, null),
                    underline
                );

            Vector4 Reference(Vector2 p)
            {
                Vector4 back = ShapeReference.Premultiply(Back) * ShapeReference.Coverage(p, Snapped(10, 10, right - 10, line), default);
                Vector4 lines = Over(Line(p, room + 12, right), Over(Line(p, 10, room), back));
                return Over(ShapeReference.Premultiply(Green) * ShapeReference.Coverage(p, Snapped(room, baseline - 8, 12, 8), default), lines);
            }

            for (int y = 8; y < (int)(10 + line) + 2; y++)
            {
                for (int x = 8; x < (int)right + 2; x++)
                {
                    Color expected = ShapeReference.Over(Reference(new Vector2(x + 0.5f, y + 0.5f)), DrawStageRenderTests.Clear);
                    Color actual = pixels[(y * width) + x];
                    int difference = Math.Max(
                        Math.Abs(expected.R - actual.R),
                        Math.Max(Math.Abs(expected.G - actual.G), Math.Abs(expected.B - actual.B))
                    );
                    Assert.True(difference <= 1, $"pixel ({x}, {y}): expected {expected}, got {actual}");
                }
            }
        });

    // A rectangle with its edges on the nearest pixels, as FillRect puts them at scale 1.
    private static Vector4 Snapped(float x, float y, float width, float height)
    {
        float left = MathF.Round(x);
        float top = MathF.Round(y);
        return new Vector4(left, top, MathF.Round(x + width) - left, MathF.Round(y + height) - top);
    }

    // A premultiplied color over another.
    private static Vector4 Over(Vector4 top, Vector4 under) => top + (under * (1 - top.W));
}
