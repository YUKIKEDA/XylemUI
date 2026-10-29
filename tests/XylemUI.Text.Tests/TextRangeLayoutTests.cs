using XylemUI.Text.LineBreak;

namespace XylemUI.Text.Tests;

// How the ranges of a text leaf shape and lay out (row T12, design §4.8): each range of the layout at its size, language,
// letter spacing, and features; the line height from the largest size on the line; and ranges of the look that do not split the
// shaping. The test font's "n" advances 610 of 1000 units per em, and a line of it is 1.448 em high.
[Collection(UiThreadGroup.Name)]
public sealed class TextRangeLayoutTests(UiThreadFixture ui) : IDisposable
{
    private const float N = 0.61f;
    private const float LineEm = 1.448f;
    private readonly Font _font = TestFont.Open();

    public void Dispose() => _font.Dispose();

    /// <summary>
    /// A range of a size of its own is shaped at it, apart from the text around it, and its line is as high as its font at that
    /// size.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A text of 14 px: "nn", then "nn" in a range of 28 px.
    ///
    /// Steps:
    /// - Mount and lay out.
    ///
    /// Expected:
    /// - Two font runs, of 14 and 28 px; the glyphs advance 8.54, 8.54, 17.08, 17.08; the size is 51.24 x 40.544 (one line of the
    ///   28 px font).
    /// </remarks>
    [Fact]
    public void Range_OfASize_IsShapedAtIt() =>
        ui.Invoke(() =>
        {
            using var scene = new TextScene(_font);
            Element text = default;
            scene.Mount(() =>
                text = Elements.Text(
                    (ref w) =>
                    {
                        w.Write("nn".AsSpan());
                        using (w.Style(new SpanStyle(Size: 28)))
                        {
                            w.Write("nn".AsSpan());
                        }
                    }
                )
            );

            TextLeaf leaf = scene.Leaf(text);
            ShapingResult shaped = leaf.Shaped!.Result!;
            Assert.Equal([14f, 28f], shaped.Styles.ToArray().Select(s => s.Size));
            float[] advances = [.. Committed(leaf).Glyphs.ToArray().Select(g => g.Advance)];
            Assert.Equal([14 * N, 14 * N, 28 * N, 28 * N], advances, new Near());
            Size size = Size(leaf);
            Assert.Equal((2 * 14 * N) + (2 * 28 * N), size.Width, 3);
            Assert.Equal(28 * LineEm, size.Height, 3);
        });

    /// <summary>
    /// A write that changes a range's size and keeps the text shapes the leaf again at the new size.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A text "nn" in a range whose size a signal gives: 20 px.
    ///
    /// Steps:
    /// - Mount; set 28; run the frame.
    ///
    /// Expected:
    /// - The glyphs advance 12.2 at 20 px, then 17.08 at 28 px.
    /// </remarks>
    [Fact]
    public void Range_NewSize_ShapesAgain() =>
        ui.Invoke(() =>
        {
            using var scene = new TextScene(_font);
            var size = Signal.Create(20f);
            Element text = default;
            scene.Mount(() =>
                text = Elements.Text(
                    (ref w) =>
                    {
                        using (w.Style(new SpanStyle(Size: size.Get())))
                        {
                            w.Write("nn".AsSpan());
                        }
                    }
                )
            );

            Assert.Equal(20 * N, Committed(scene.Leaf(text)).Glyphs[0].Advance, 3);
            size.Set(28);
            scene.Draw();
            Assert.Equal(28 * N, Committed(scene.Leaf(text)).Glyphs[0].Advance, 3);
        });

    /// <summary>
    /// Letter spacing is added to each cluster's advance, the last one's too, and counts in the width.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A text of 14 px: "n", then "nnn" in a range of letter spacing 2.
    ///
    /// Steps:
    /// - Mount and lay out.
    ///
    /// Expected:
    /// - The glyphs advance 8.54, 10.54, 10.54, 10.54; the width is 4 x 8.54 + 6.
    /// </remarks>
    [Fact]
    public void Range_OfLetterSpacing_AddsToEachCluster() =>
        ui.Invoke(() =>
        {
            using var scene = new TextScene(_font);
            Element text = default;
            scene.Mount(() =>
                text = Elements.Text(
                    (ref w) =>
                    {
                        w.Write("n".AsSpan());
                        using (w.Style(new SpanStyle(LetterSpacing: 2)))
                        {
                            w.Write("nnn".AsSpan());
                        }
                    }
                )
            );

            TextLeaf leaf = scene.Leaf(text);
            float[] advances = [.. Committed(leaf).Glyphs.ToArray().Select(g => g.Advance)];
            Assert.Equal([14 * N, (14 * N) + 2, (14 * N) + 2, (14 * N) + 2], advances, new Near());
            Assert.Equal((4 * 14 * N) + 6, Size(leaf).Width, 3);
        });

    /// <summary>
    /// With a line height of the style, a line is the factor times the largest size on it: a line with a larger range is higher,
    /// and a line without one is the leaf's size times the factor.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A text of 14 px and line height 2: "n", "n" in a range of 28 px, a hard line break, and "n".
    ///
    /// Steps:
    /// - Mount and lay out.
    ///
    /// Expected:
    /// - Two lines, 56 and 28 high: 84 in all.
    /// </remarks>
    [Fact]
    public void LineHeight_TakesTheLargestSizeOnTheLine() =>
        ui.Invoke(() =>
        {
            using var scene = new TextScene(_font);
            Element text = default;
            scene.Mount(() =>
            {
                text = Elements.Text(
                    (ref w) =>
                    {
                        w.Write("n".AsSpan());
                        using (w.Style(new SpanStyle(Size: 28)))
                        {
                            w.Write("n".AsSpan());
                        }

                        w.Write("\nn".AsSpan());
                    }
                );
                TextStyle style = scene.Tree.Nodes.GetRef(text.Node).Text!.Style;
                scene.Tree.SetTextStyle(text.Node, style with { LineHeight = 2 });
                return text;
            });

            TextLeaf leaf = scene.Leaf(text);
            Assert.Equal(2, Committed(leaf).Lines.Length);
            Assert.Equal(84, Size(leaf).Height, 3);
        });

    /// <summary>
    /// A range's features go to the shaper: with kerning off, a kerned pair takes the advances it has apart.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - Two texts of "-T": one plain, which the font kerns, and one in a range with the feature <c>kern</c> at 0.
    ///
    /// Steps:
    /// - Mount and lay out.
    ///
    /// Expected:
    /// - The plain text is narrower than the other, whose width is the advances of "-" and "T" shaped apart.
    /// </remarks>
    [Fact]
    public void Range_OfFeatures_ShapesWithThem() =>
        ui.Invoke(() =>
        {
            using var scene = new TextScene(_font);
            var noKerning = new FontFeatures(FontFeature.Create("kern", 0));
            Element plain = default;
            Element unkerned = default;
            scene.Mount(() =>
            {
                plain = Elements.Text((ref w) => w.Write("-T".AsSpan()));
                unkerned = Elements.Text(
                    (ref w) =>
                    {
                        using (w.Style(new SpanStyle(Features: noKerning)))
                        {
                            w.Write("-T".AsSpan());
                        }
                    }
                );
                return Elements.Column(plain, unkerned);
            });

            using var shaper = new TextShaper();
            float apart = 0;
            foreach (string part in (string[])["-", "T"])
            {
                apart += shaper.Shape(_font, part, TestFont.Japanese)[0].XAdvance * 14f / _font.UnitsPerEm;
            }

            Assert.True(Size(scene.Leaf(plain)).Width < apart - 0.1f);
            Assert.Equal(apart, Size(scene.Leaf(unkerned)).Width, 3);
        });

    /// <summary>
    /// A range's language goes to its font runs; ranges of the look alone do not split the shaping.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A text in the tree's Japanese: "nn", "nn" in a range of red, and "nn" in a range of English.
    ///
    /// Steps:
    /// - Mount and lay out.
    ///
    /// Expected:
    /// - Two font runs: (0, 4) in Japanese and (4, 2) in English.
    /// </remarks>
    [Fact]
    public void Ranges_OfTheLookOnly_DoNotSplitTheShaping() =>
        ui.Invoke(() =>
        {
            using var scene = new TextScene(_font);
            scene.Tree.UiLanguage = TestFont.Japanese;
            TextLanguage english = TextLanguage.FromTag("en");
            Element text = default;
            scene.Mount(() =>
                text = Elements.Text(
                    (ref w) =>
                    {
                        w.Write("nn".AsSpan());
                        using (w.Style(new SpanStyle(Color: Color.FromRgb(0xFF0000))))
                        {
                            w.Write("nn".AsSpan());
                        }

                        using (w.Style(new SpanStyle(Language: english)))
                        {
                            w.Write("nn".AsSpan());
                        }
                    }
                )
            );

            ShapingResult shaped = scene.Leaf(text).Shaped!.Result!;
            Assert.Equal([(0, 4), (4, 2)], shaped.Runs.ToArray().Select(r => (r.Start, r.Length)));
            Assert.Equal([TestFont.Japanese, english], shaped.Styles.ToArray().Select(s => s.Language));
        });

    /// <summary>
    /// A feature's tag is four printable ASCII characters, packed big-endian.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - None.
    ///
    /// Steps:
    /// - Create "liga" with no value, and "ss01" with 2; create "lig", "ligat", and "ligé".
    ///
    /// Expected:
    /// - "liga" is 0x6C696761 with the value 1; "ss01" is 0x73733031 with 2; the other three throw
    ///   <see cref="ArgumentException"/>.
    /// </remarks>
    [Fact]
    public void FontFeature_Create_PacksTheTag()
    {
        Assert.Equal(new FontFeature(0x6C696761, 1), FontFeature.Create("liga"));
        Assert.Equal(new FontFeature(0x73733031, 2), FontFeature.Create("ss01", 2));
        Assert.Throws<ArgumentException>(() => FontFeature.Create("lig"));
        Assert.Throws<ArgumentException>(() => FontFeature.Create("ligat"));
        Assert.Throws<ArgumentException>(() => FontFeature.Create("ligé"));
    }

    private static LineLayoutResult Committed(TextLeaf leaf) => leaf.LineBreaks![leaf.LineBreaks.Committed].Layout;

    private static Size Size(TextLeaf leaf) => leaf.LineBreaks!.ResultOf(leaf.LineBreaks.Committed).Size;

    private sealed class Near : IEqualityComparer<float>
    {
        public bool Equals(float x, float y) => MathF.Abs(x - y) < 1e-3f;

        public int GetHashCode(float obj) => 0;
    }
}
