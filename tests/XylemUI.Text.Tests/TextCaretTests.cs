namespace XylemUI.Text.Tests;

// The text a leaf laid out, by position (row T12, design §4.3): the line at a height, the caret at a point and the point of a
// caret, and the cluster under a point. The text is 14 px: "n" advances 8.54 px, a space 3.136 px, and a line is 20.272 px high.
[Collection(UiThreadGroup.Name)]
public sealed class TextCaretTests(UiThreadFixture ui) : IDisposable
{
    private const float N = 0.61f * 14;
    private const float Space = 0.224f * 14;
    private const float Line = 1.448f * 14;
    private readonly Font _font = TestFont.Open();

    public void Dispose() => _font.Dispose();

    /// <summary>
    /// The line at a height is the one that holds it; above every line the first, below them the last.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A text "nn\nnn": two lines.
    ///
    /// Steps:
    /// - Find the line at 5, 25, -5, and 500.
    ///
    /// Expected:
    /// - 0 with its top 0, 1 with its top 20.272, 0, and 1; each 20.272 high.
    /// </remarks>
    [Fact]
    public void LineAt_Height_IsTheLineThatHoldsIt() =>
        ui.Invoke(() =>
        {
            TextLeaf leaf = Mount("nn\nnn", out TextScene scene);
            using (scene)
            {
                Assert.Equal(0, TextCaret.LineAt(leaf, 5, out float top, out float height));
                Assert.Equal((0f, Line), (top, height));
                Assert.Equal(1, TextCaret.LineAt(leaf, 25, out top, out _));
                Assert.Equal(Line, top, 3);
                Assert.Equal(0, TextCaret.LineAt(leaf, -5, out _, out _));
                Assert.Equal(1, TextCaret.LineAt(leaf, 500, out _, out _));
            }
        });

    /// <summary>
    /// A point in a cluster's left half is before it, in its right half after it; past the line its end, before the hard line
    /// break, upstream; on the next line, its positions from its own top.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A text "nn\nnn".
    ///
    /// Steps:
    /// - Hit-test line 0 at 2, 6, and 100; line 1 at 0.
    ///
    /// Expected:
    /// - 0 downstream at x 0; 1 downstream at 8.54; 2 upstream at 17.08; 3 downstream at x 0 and y 20.272. Each caret is 20.272
    ///   high and 0 wide.
    /// </remarks>
    [Fact]
    public void HitTestText_Point_IsTheNearestClusterEdge() =>
        ui.Invoke(() =>
        {
            TextLeaf leaf = Mount("nn\nnn", out TextScene scene);
            using (scene)
            {
                Assert.Equal(new CaretPosition(0, Affinity.Downstream, new Rect(0, 0, 0, Line)), Round(TextCaret.HitTestText(leaf, 0, 2)));
                Assert.Equal(new CaretPosition(1, Affinity.Downstream, new Rect(Round(N), 0, 0, Line)), Round(TextCaret.HitTestText(leaf, 0, 6)));
                Assert.Equal(new CaretPosition(2, Affinity.Upstream, new Rect(Round(2 * N), 0, 0, Line)), Round(TextCaret.HitTestText(leaf, 0, 100)));
                Assert.Equal(new CaretPosition(3, Affinity.Downstream, new Rect(0, Round(Line), 0, Line)), Round(TextCaret.HitTestText(leaf, 1, 0)));
                Assert.Throws<ArgumentOutOfRangeException>(() => TextCaret.HitTestText(leaf, 2, 0));
            }
        });

    /// <summary>
    /// The caret of a position where a wrapped line ends and the next starts is at the first's end upstream and at the second's
    /// start downstream; the text's end is after its last cluster; a position outside the text throws.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A text "nn nn" 30 px wide: the lines "nn " and "nn".
    ///
    /// Steps:
    /// - Find the caret of 3 upstream, 3 downstream, 1 downstream, and 5 downstream; then of 6.
    ///
    /// Expected:
    /// - (20.216, 0), (0, 20.272), (8.54, 0), (17.08, 20.272); 6 throws <see cref="ArgumentOutOfRangeException"/>.
    /// </remarks>
    [Fact]
    public void CaretGeometry_AtAWrap_TakesTheAffinity() =>
        ui.Invoke(() =>
        {
            TextLeaf leaf = Mount("nn nn", out TextScene scene, width: 30);
            using (scene)
            {
                Assert.Equal(new Rect(Round((2 * N) + Space), 0, 0, Line), Round(TextCaret.CaretGeometry(leaf, 3, Affinity.Upstream)).Rect);
                Assert.Equal(new Rect(0, Round(Line), 0, Line), Round(TextCaret.CaretGeometry(leaf, 3, Affinity.Downstream)).Rect);
                Assert.Equal(new Rect(Round(N), 0, 0, Line), Round(TextCaret.CaretGeometry(leaf, 1, Affinity.Downstream)).Rect);
                Assert.Equal(new Rect(Round(2 * N), Round(Line), 0, Line), Round(TextCaret.CaretGeometry(leaf, 5, Affinity.Downstream)).Rect);
                Assert.Throws<ArgumentOutOfRangeException>(() => TextCaret.CaretGeometry(leaf, 6, Affinity.Downstream));
            }
        });

    /// <summary>
    /// The cluster under a point is the one whose box, from its line's top to its bottom and over its advance, holds it; a point
    /// outside every box has none.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A text "nn\nnn".
    ///
    /// Steps:
    /// - Find the cluster at (9, 5), (5, 25), (5, -1), (-1, 5), and (200, 5).
    ///
    /// Expected:
    /// - 1, 3, and -1 for the other three.
    /// </remarks>
    [Fact]
    public void ClusterAt_Point_IsTheClusterWhoseBoxHoldsIt() =>
        ui.Invoke(() =>
        {
            TextLeaf leaf = Mount("nn\nnn", out TextScene scene);
            using (scene)
            {
                Assert.Equal(1, TextCaret.ClusterAt(leaf, 9, 5));
                Assert.Equal(3, TextCaret.ClusterAt(leaf, 5, 25));
                Assert.Equal(-1, TextCaret.ClusterAt(leaf, 5, -1));
                Assert.Equal(-1, TextCaret.ClusterAt(leaf, -1, 5));
                Assert.Equal(-1, TextCaret.ClusterAt(leaf, 200, 5));
            }
        });

    private static float Round(float value) => MathF.Round(value, 3);

    private static CaretPosition Round(CaretPosition caret) => caret with { Rect = Round(caret.Rect) };

    private static Rect Round(Rect rect) => new(Round(rect.X), Round(rect.Y), Round(rect.Width), rect.Height);

    private TextLeaf Mount(string text, out TextScene scene, float width = float.NaN)
    {
        scene = new TextScene(_font);
        Element node = default;
        scene.Mount(() => node = float.IsNaN(width) ? Elements.Text(text) : Elements.Text(text).Width(width));
        return scene.Leaf(node);
    }
}
