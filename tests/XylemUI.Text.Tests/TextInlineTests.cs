using XylemUI.Text.LineBreak;

namespace XylemUI.Text.Tests;

// The inline objects of a text leaf (row T13, design §4.8): the child the text node takes, the room it leaves on the line, where
// the child sits by its alignment, the lines' breaks around it, the content widths, the measurement when the child changes, the
// look of its room, and the input. The text is 14 px of the test font: "n" advances 8.54, a line is 20.272 high, and its baseline
// is the ascent of 16.24 below its top (a descent of 4.032).
[Collection(UiThreadGroup.Name)]
public sealed class TextInlineTests(UiThreadFixture ui) : IDisposable
{
    private const float N = 0.61f * 14;
    private const float Line = 1.448f * 14;
    private const float Ascent = 1.16f * 14;
    private const float Descent = 0.288f * 14;
    private static readonly Color Red = Color.FromRgb(0xFF0000);
    private static readonly Color Blue = Color.FromRgb(0x0000FF);
    private readonly Font _font = TestFont.Open();
    private readonly List<string> _log = [];

    public void Dispose() => _font.Dispose();

    /// <summary>
    /// An inline object becomes a child of the text node, takes a room of its size between the glyphs, and is laid out in it; a box
    /// with no baseline has its bottom on the line's baseline.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A text "nn", a box of 30 x 10, "nn".
    ///
    /// Steps:
    /// - Mount and lay out.
    ///
    /// Expected:
    /// - The box's parent is the text node; its rectangle is (17.08, 6.24, 30, 10); the text's one line is 64.16 wide and 20.272 high,
    ///   and its U+FFFC is the third character.
    /// </remarks>
    [Fact]
    public void Inline_TakesARoomOfItsSize_AndIsLaidOutInIt() =>
        ui.Invoke(() =>
        {
            using var scene = new TextScene(_font);
            Element box = default;
            Element text = default;
            scene.Mount(() =>
            {
                box = Elements.Box().Width(30).Height(10);
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

            Assert.Equal(text.Node, scene.Tree.Nodes.GetRef(box.Node).Parent);
            AssertRect(new Rect(2 * N, Ascent - 10, 30, 10), scene.Tree.GetRect(box.Node));
            Rect leaf = scene.Tree.GetRect(text.Node);
            Assert.Equal((4 * N) + 30, leaf.Width, 3);
            Assert.Equal(Line, leaf.Height, 3);
            Assert.Equal('￼', scene.Leaf(text).Text[2]);
        });

    /// <summary>
    /// Each of the six alignments puts a room 10 high where the design gives it, and the line grows to hold it.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A text "n", a box of 10 x 10 with the alignment, "n".
    ///
    /// Steps:
    /// - Mount and lay out.
    ///
    /// Expected:
    /// - The box's top and the line's height: Baseline and AboveBaseline 6.24 and 20.272 (the box has no baseline), BelowBaseline
    ///   16.24 and 26.24, Top 0 and 20.272, Bottom 10.272 and 20.272, Middle 5.136 and 20.272 (its middle at (16.24 − 4.032) / 2
    ///   above the baseline).
    /// </remarks>
    [Theory]
    [InlineData(PlaceholderAlignment.Baseline, Ascent - 10, Line)]
    [InlineData(PlaceholderAlignment.AboveBaseline, Ascent - 10, Line)]
    [InlineData(PlaceholderAlignment.BelowBaseline, Ascent, Ascent + 10)]
    [InlineData(PlaceholderAlignment.Top, 0, Line)]
    [InlineData(PlaceholderAlignment.Bottom, Ascent - (10 - Descent), Line)]
    [InlineData(PlaceholderAlignment.Middle, Ascent - (5 + ((Ascent - Descent) / 2)), Line)]
    public void Alignment_PlacesTheRoom_AndTheLineHoldsIt(PlaceholderAlignment alignment, float top, float height) =>
        ui.Invoke(() =>
        {
            using var scene = new TextScene(_font);
            Element box = default;
            Element text = default;
            scene.Mount(() =>
            {
                box = Elements.Box().Width(10).Height(10);
                text = Elements.Text(
                    (ref w) =>
                    {
                        w.Write("n".AsSpan());
                        w.Inline(box, alignment);
                        w.Write("n".AsSpan());
                    }
                );
                return Elements.Row(text);
            });

            Assert.Equal(top, scene.Tree.GetRect(box.Node).Y, 3);
            Assert.Equal(height, scene.Tree.GetRect(text.Node).Height, 3);
        });

    /// <summary>
    /// A room higher than the text raises the line's baseline: the line is as high as the room above the baseline and the text's
    /// descent below it.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A text "n", a box of 10 x 40, "n".
    ///
    /// Steps:
    /// - Mount and lay out.
    ///
    /// Expected:
    /// - The box's top is 0; the line is 44.032 high; the leaf's first baseline is 40.
    /// </remarks>
    [Fact]
    public void Room_HigherThanTheText_GrowsTheLine() =>
        ui.Invoke(() =>
        {
            using var scene = new TextScene(_font);
            Element box = default;
            Element text = default;
            scene.Mount(() =>
            {
                box = Elements.Box().Width(10).Height(40);
                text = Elements.Text(
                    (ref w) =>
                    {
                        w.Write("n".AsSpan());
                        w.Inline(box);
                        w.Write("n".AsSpan());
                    }
                );
                return Elements.Row(text);
            });

            Assert.Equal(0, scene.Tree.GetRect(box.Node).Y, 3);
            Assert.Equal(40 + Descent, scene.Tree.GetRect(text.Node).Height, 3);
            Assert.Equal(40, scene.Tree.Nodes.GetRef(text.Node).Layout.FirstBaseline, 3);
        });

    /// <summary>
    /// With the Baseline alignment, a child with a baseline puts its first baseline on the line's.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A text "n", then a text of 28 px "n" as an inline object, then "n".
    ///
    /// Steps:
    /// - Mount and lay out.
    ///
    /// Expected:
    /// - The child's top is 0 and its baseline is the line's, 32.48 (the ascent at 28 px); the line is 40.544 high.
    /// </remarks>
    [Fact]
    public void Baseline_TakesTheChildsFirstBaseline() =>
        ui.Invoke(() =>
        {
            using var scene = new TextScene(_font);
            Element child = default;
            Element text = default;
            scene.Mount(() =>
            {
                child = Elements.Text((ref w) => w.Write("n".AsSpan())).FontSize(28);
                text = Elements.Text(
                    (ref w) =>
                    {
                        w.Write("n".AsSpan());
                        w.Inline(child);
                        w.Write("n".AsSpan());
                    }
                );
                return Elements.Row(text);
            });

            Assert.Equal(0, scene.Tree.GetRect(child.Node).Y, 3);
            Assert.Equal(1.16f * 28, scene.Tree.Nodes.GetRef(text.Node).Layout.FirstBaseline, 3);
            Assert.Equal(1.448f * 28, scene.Tree.GetRect(text.Node).Height, 3);
        });

    /// <summary>
    /// The lines may break before and after a room, whatever the text around it; a room wider than the line is placed alone and
    /// overflows it.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A text 50 wide: "nnnn", a box of 30 x 10, "nnnn", a box of 80 x 10.
    ///
    /// Steps:
    /// - Mount and lay out.
    ///
    /// Expected:
    /// - Four lines: "nnnn"; the first box at (0, 26.512) on the second line; "nnnn"; the second box at x 0 on the fourth.
    /// </remarks>
    [Fact]
    public void Lines_BreakBeforeAndAfterARoom() =>
        ui.Invoke(() =>
        {
            using var scene = new TextScene(_font);
            Element first = default;
            Element second = default;
            Element text = default;
            scene.Mount(() =>
            {
                first = Elements.Box().Width(30).Height(10);
                second = Elements.Box().Width(80).Height(10);
                text = Elements
                    .Text(
                        (ref w) =>
                        {
                            w.Write("nnnn".AsSpan());
                            w.Inline(first);
                            w.Write("nnnn".AsSpan());
                            w.Inline(second);
                        }
                    )
                    .Width(50);
                return Elements.Row(text);
            });

            TextLeaf leaf = scene.Leaf(text);
            Assert.Equal(4, leaf.LineBreaks![leaf.LineBreaks.Committed].Layout.Lines.Length);
            AssertRect(new Rect(0, Line + Ascent - 10, 30, 10), scene.Tree.GetRect(first.Node));
            AssertRect(new Rect(0, (3 * Line) + Ascent - 10, 80, 10), scene.Tree.GetRect(second.Node));
        });

    /// <summary>
    /// The content widths take a child's: the least, its least content width as one piece; the most, its most content width on the
    /// line.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A text "nn", a text "nnnn nnnn" as an inline object, "nn"; in a row of 10 wide, then of 400.
    ///
    /// Steps:
    /// - Mount, lay out, and read the text's width; widen the row; lay out and read it again.
    ///
    /// Expected:
    /// - In the row of 10, the least content width: 34.16, the child's least ("nnnn"). In the row of 400, the most: "nn" twice and
    ///   the child's one line, the width of "nnnn nnnn".
    /// </remarks>
    [Fact]
    public void ContentWidths_TakeTheChildsContentWidths() =>
        ui.Invoke(() =>
        {
            using var scene = new TextScene(_font);
            Element child = default;
            Element text = default;
            Element row = default;
            scene.Mount(() =>
            {
                child = Elements.Text((ref w) => w.Write("nnnn nnnn".AsSpan()));
                text = Elements.Text(
                    (ref w) =>
                    {
                        w.Write("nn".AsSpan());
                        w.Inline(child);
                        w.Write("nn".AsSpan());
                    }
                );
                row = Elements.Row(text).Width(10);
                return row;
            });

            Assert.Equal(4 * N, scene.Tree.GetRect(text.Node).Width, 3);
            LayoutStyle style = scene.Tree.Nodes.GetRef(row.Node).Layout.Style;
            scene.Tree.SetStyle(row.Node, style with { Width = Length.Px(400) });
            scene.Draw();
            float childWidth = scene.Tree.GetRect(child.Node).Width;
            Assert.True(childWidth > 8 * N);
            Assert.Equal((4 * N) + childWidth, scene.Tree.GetRect(text.Node).Width, 3);
        });

    /// <summary>
    /// A child that grows gives the text's least content width its new width.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A text "nn" and a box of 40 x 10, in a row of 10 wide.
    ///
    /// Steps:
    /// - Mount and lay out; make the box 50 wide; lay out.
    ///
    /// Expected:
    /// - The text is 40 wide, then 50: the box is its widest piece.
    /// </remarks>
    [Fact]
    public void ChildThatGrows_WidensTheLeastContentWidth() =>
        ui.Invoke(() =>
        {
            using var scene = new TextScene(_font);
            Element box = default;
            Element text = default;
            scene.Mount(() =>
            {
                box = Elements.Box().Width(40).Height(10);
                text = Elements.Text(
                    (ref w) =>
                    {
                        w.Write("nn".AsSpan());
                        w.Inline(box);
                    }
                );
                return Elements.Row(text).Width(10);
            });

            Assert.Equal(40, scene.Tree.GetRect(text.Node).Width, 3);
            SetWidth(scene, box, 50);
            scene.Draw();
            Assert.Equal(50, scene.Tree.GetRect(text.Node).Width, 3);
        });

    /// <summary>
    /// A child that grows past the line's width moves to a line of its own.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A text 60 wide: "nn" and a box of 30 x 10.
    ///
    /// Steps:
    /// - Mount and lay out; make the box 50 wide; lay out.
    ///
    /// Expected:
    /// - One line, then two (17.08 + 50 is over 60): the text is 20.272 high, then 40.544, and the box at x 0.
    /// </remarks>
    [Fact]
    public void ChildThatGrows_PastTheLine_BreaksIt() =>
        ui.Invoke(() =>
        {
            using var scene = new TextScene(_font);
            Element box = default;
            Element text = default;
            scene.Mount(() =>
            {
                box = Elements.Box().Width(30).Height(10);
                text = Elements.Text(
                    (ref w) =>
                    {
                        w.Write("nn".AsSpan());
                        w.Inline(box);
                    }
                ).Width(60);
                return Elements.Row(text);
            });

            Assert.Equal(Line, scene.Tree.GetRect(text.Node).Height, 3);
            SetWidth(scene, box, 50);
            scene.Draw();
            Assert.Equal(2 * Line, scene.Tree.GetRect(text.Node).Height, 3);
            Assert.Equal(0, scene.Tree.GetRect(box.Node).X, 3);
        });

    /// <summary>
    /// A child whose size changes gives the text a room of its new size; a layout of the child that keeps its size keeps the
    /// lines the text was broken into at each width.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A box whose width a style sets, holding a text of "nnnn " eight times, then a box of 30 x 10; the outer box laid out 200
    ///   wide, then 150.
    ///
    /// Steps:
    /// - Mount at 200; set 150; draw. Give the inner box a padding of 2 (its size stays); draw. Make the inner box 40 wide; draw.
    ///
    /// Expected:
    /// - After the padding: the text's cache of line breaks still holds both widths. After the width: the box is 40 wide, and the
    ///   cache holds only the width laid out since.
    /// </remarks>
    [Fact]
    public void ChildLayout_OfTheSameSize_KeepsTheLines_AndANewSizeBreaksThemAgain() =>
        ui.Invoke(() =>
        {
            using var scene = new TextScene(_font);
            Element box = default;
            Element text = default;
            Element outer = default;
            scene.Mount(() =>
            {
                box = Elements.Box().Width(30).Height(10);
                text = Elements.Text(
                    (ref w) =>
                    {
                        for (int i = 0; i < 8; i++)
                        {
                            w.Write("nnnn ".AsSpan());
                        }

                        w.Inline(box);
                    }
                );
                outer = Elements.Box(text).Width(200);
                return outer;
            });

            SetWidth(scene, outer, 150);
            scene.Draw();
            TextLeaf leaf = scene.Leaf(text);
            Assert.Equal(2, leaf.LineBreaks!.Count);

            LayoutStyle style = scene.Tree.Nodes.GetRef(box.Node).Layout.Style;
            scene.Tree.SetStyle(box.Node, style with { Padding = EdgeInsets.All(2) });
            scene.Draw();
            Assert.Equal(2, leaf.LineBreaks.Count);

            SetWidth(scene, box, 40);
            scene.Draw();
            Assert.Equal(40, scene.Tree.GetRect(box.Node).Width);
            Assert.Equal(1, leaf.LineBreaks.Count);
        });

    /// <summary>
    /// A write that runs again with the same inline object keeps it as it was; a write that leaves it out lets go of it, alive, and
    /// a later write takes it again.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A box; a text that writes "nn" in a range whose color a signal gives, then, while another signal is true, the box.
    ///
    /// Steps:
    /// - Mount; change the color; draw. Set the second signal false; draw. Set it true; draw.
    ///
    /// Expected:
    /// - After the color: the same box, the text's child. After false: the box alive with no parent, the text with no child. After
    ///   true: the box the text's child again, laid out in its room.
    /// </remarks>
    [Fact]
    public void Rewrite_KeepsTheChild_AndLetsGoOfOneLeftOut() =>
        ui.Invoke(() =>
        {
            using var scene = new TextScene(_font);
            var color = Signal.Create(Red);
            var placed = Signal.Create(true);
            Element box = default;
            Element text = default;
            scene.Mount(() =>
            {
                box = Elements.Box().Width(30).Height(10);
                text = Elements.Text(
                    (ref w) =>
                    {
                        using (w.Style(new SpanStyle(Color: color.Get())))
                        {
                            w.Write("nn".AsSpan());
                        }

                        if (placed.Get())
                        {
                            w.Inline(box);
                        }
                    }
                );
                return Elements.Row(text);
            });

            color.Set(Blue);
            scene.Draw();
            Assert.Equal(text.Node, scene.Tree.Nodes.GetRef(box.Node).Parent);

            placed.Set(false);
            scene.Draw();
            Assert.True(scene.Tree.Nodes.IsAlive(box.Node));
            Assert.True(scene.Tree.Nodes.GetRef(box.Node).Parent.IsNone);
            Assert.True(scene.Tree.Nodes.GetRef(text.Node).FirstChild.IsNone);

            placed.Set(true);
            scene.Draw();
            Assert.Equal(text.Node, scene.Tree.Nodes.GetRef(box.Node).Parent);
            Assert.Equal(2 * N, scene.Tree.GetRect(box.Node).X, 3);
        });

    /// <summary>
    /// A child's margins count in its room, and the child sits inside them.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A text "nn", a box of 30 x 10 with margins of 5, "nn".
    ///
    /// Steps:
    /// - Mount and lay out.
    ///
    /// Expected:
    /// - The room is 40 x 20 with its bottom on the baseline, which it raises to 20: the line is 24.032 high, the text 64.16 + 10
    ///   wide, and the box at (17.08 + 5, 5).
    /// </remarks>
    [Fact]
    public void Margins_CountInTheRoom() =>
        ui.Invoke(() =>
        {
            using var scene = new TextScene(_font);
            Element box = default;
            Element text = default;
            scene.Mount(() =>
            {
                box = Elements.Box().Width(30).Height(10);
                LayoutStyle style = scene.Tree.Nodes.GetRef(box.Node).Layout.Style;
                scene.Tree.SetStyle(box.Node, style with { Margin = EdgeInsets.Margin(5) });
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

            AssertRect(new Rect((2 * N) + 5, 5, 30, 10), scene.Tree.GetRect(box.Node));
            Rect leaf = scene.Tree.GetRect(text.Node);
            Assert.Equal((4 * N) + 40, leaf.Width, 3);
            Assert.Equal(20 + Descent, leaf.Height, 3);
        });

    /// <summary>
    /// A write that changes only the look keeps the rooms the children were measured to: the lines keep their heights.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A text "nn" in a range whose color a signal gives, then a box of 10 x 40.
    ///
    /// Steps:
    /// - Mount; change the color; draw; read the line's height.
    ///
    /// Expected:
    /// - The line is still 44.032 high, as the room raises it.
    /// </remarks>
    [Fact]
    public void LookRewrite_KeepsTheRooms() =>
        ui.Invoke(() =>
        {
            using var scene = new TextScene(_font);
            var color = Signal.Create(Red);
            Element text = default;
            scene.Mount(() =>
            {
                Element box = Elements.Box().Width(10).Height(40);
                text = Elements.Text(
                    (ref w) =>
                    {
                        using (w.Style(new SpanStyle(Color: color.Get())))
                        {
                            w.Write("nn".AsSpan());
                        }

                        w.Inline(box);
                    }
                );
                return Elements.Row(text);
            });

            color.Set(Blue);
            scene.Draw();
            Assert.Equal(0, TextCaret.LineAt(scene.Leaf(text), 1, out _, out float height));
            Assert.Equal(40 + Descent, height, 3);
        });

    /// <summary>
    /// A child that a write leaves out loses the focus, as a node taken out of the tree does.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - An active window; a focusable box; a text that writes it while a signal is true.
    ///
    /// Steps:
    /// - Mount; focus the box; set the signal false; draw.
    ///
    /// Expected:
    /// - The box has the focus, then nothing has.
    /// </remarks>
    [Fact]
    public void ChildLeftOut_LosesTheFocus() =>
        ui.Invoke(() =>
        {
            using var scene = new TextScene(_font);
            var placed = Signal.Create(true);
            Element box = default;
            scene.Mount(() =>
            {
                box = Elements.Box().Width(10).Height(10).Focusable();
                return Elements.Row(
                    Elements.Text(
                        (ref w) =>
                        {
                            w.Write("n".AsSpan());
                            if (placed.Get())
                            {
                                w.Inline(box);
                            }
                        }
                    )
                );
            });

            scene.Tree.Focus.SetActive(true);
            Assert.True(box.Focus());
            Assert.Equal(box.Node, scene.Tree.Focus.Focused);
            placed.Set(false);
            scene.Draw();
            Assert.True(scene.Tree.Focus.Focused.IsNone);
        });

    /// <summary>
    /// A write that puts another element in the same place, the text the same, gives the text node the new child and lets go of the
    /// old one.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - Two boxes, of 30 x 10 and 20 x 10; a text "nn" and the box a signal picks.
    ///
    /// Steps:
    /// - Mount with the first; pick the second; draw.
    ///
    /// Expected:
    /// - The second box is the text's only child, 20 wide at x 17.08; the first has no parent.
    /// </remarks>
    [Fact]
    public void Rewrite_WithAnotherChild_InTheSamePlace_TakesIt() =>
        ui.Invoke(() =>
        {
            using var scene = new TextScene(_font);
            var second = Signal.Create(false);
            Element first = default;
            Element other = default;
            Element text = default;
            scene.Mount(() =>
            {
                first = Elements.Box().Width(30).Height(10);
                other = Elements.Box().Width(20).Height(10);
                text = Elements.Text(
                    (ref w) =>
                    {
                        w.Write("nn".AsSpan());
                        w.Inline(second.Get() ? other : first);
                    }
                );
                return Elements.Row(text);
            });

            second.Set(true);
            scene.Draw();
            ref UiNode node = ref scene.Tree.Nodes.GetRef(text.Node);
            Assert.Equal(other.Node, node.FirstChild);
            Assert.Equal(other.Node, node.LastChild);
            Assert.True(scene.Tree.Nodes.GetRef(first.Node).Parent.IsNone);
            AssertRect(new Rect(2 * N, Ascent - 10, 20, 10), scene.Tree.GetRect(other.Node));
        });

    /// <summary>
    /// A write that places an element its scope removed throws: the element is gone.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A box made in a scope of its own; a text that writes it while a signal is true. The handler of the effects' exceptions
    ///   records what it is given.
    ///
    /// Steps:
    /// - Mount with false; dispose the box's scope; set true.
    ///
    /// Expected:
    /// - One <see cref="InvalidOperationException"/> reported; the text has no child.
    /// </remarks>
    [Fact]
    public void Inline_OfARemovedElement_Throws() =>
        ui.Invoke(() =>
        {
            var reported = new List<Exception>();
            Action<Exception> previous = ReactiveRuntime.UnhandledExceptionHandler;
            ReactiveRuntime.UnhandledExceptionHandler = reported.Add;
            try
            {
                using var scene = new TextScene(_font);
                var placed = Signal.Create(false);
                ScopeHandle owner = default;
                Element box = default;
                Element text = default;
                scene.Mount(() =>
                {
                    owner = Scope.Create(() => box = Elements.Box().Width(10).Height(10));
                    text = Elements.Text(
                        (ref w) =>
                        {
                            w.Write("n".AsSpan());
                            if (placed.Get())
                            {
                                w.Inline(box);
                            }
                        }
                    );
                    return Elements.Row(text);
                });

                Scope.Dispose(owner);
                Assert.False(scene.Tree.Nodes.IsAlive(box.Node));
                placed.Set(true);
                Assert.IsType<InvalidOperationException>(Assert.Single(reported));
                Assert.True(scene.Tree.Nodes.GetRef(text.Node).FirstChild.IsNone);
            }
            finally
            {
                ReactiveRuntime.UnhandledExceptionHandler = previous;
            }
        });

    /// <summary>
    /// A text node that goes away lets go of its inline objects first: the scope that made them keeps them.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A box made outside a <c>Show</c>; inside it, a text that writes the box.
    ///
    /// Steps:
    /// - Mount; set the condition false; draw.
    ///
    /// Expected:
    /// - The text is gone; the box is alive, with no parent.
    /// </remarks>
    [Fact]
    public void TextRemoved_LetsGoOfItsChildren() =>
        ui.Invoke(() =>
        {
            using var scene = new TextScene(_font);
            var shown = Signal.Create(true);
            Element box = default;
            Element text = default;
            scene.Mount(() =>
            {
                box = Elements.Box().Width(30).Height(10);
                return Elements.Column(
                    Elements.Show(
                        shown,
                        () =>
                            text = Elements.Text(
                                (ref w) =>
                                {
                                    w.Write("nn".AsSpan());
                                    w.Inline(box);
                                }
                            )
                    )
                );
            });

            shown.Set(false);
            scene.Draw();
            Assert.False(scene.Tree.Nodes.IsAlive(text.Node));
            Assert.True(scene.Tree.Nodes.IsAlive(box.Node));
            Assert.True(scene.Tree.Nodes.GetRef(box.Node).Parent.IsNone);
        });

    /// <summary>
    /// A write that places the same element twice, or an element that has another parent, throws: an element sits in one place.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A box; a text that writes it twice while a signal is true; a box inside a column; a text that writes that one while another
    ///   signal is true. The handler of the effects' exceptions records what it is given.
    ///
    /// Steps:
    /// - Mount with both false; set the first true; set the second true.
    ///
    /// Expected:
    /// - Two <see cref="InvalidOperationException"/>s reported, one after each step; the column keeps its box.
    /// </remarks>
    [Fact]
    public void Inline_PlacedTwice_OrWithAnotherParent_Throws() =>
        ui.Invoke(() =>
        {
            var reported = new List<Exception>();
            Action<Exception> previous = ReactiveRuntime.UnhandledExceptionHandler;
            ReactiveRuntime.UnhandledExceptionHandler = reported.Add;
            try
            {
                using var scene = new TextScene(_font);
                var twice = Signal.Create(false);
                var taken = Signal.Create(false);
                Element owned = default;
                Element column = default;
                scene.Mount(() =>
                {
                    Element box = Elements.Box().Width(10).Height(10);
                    owned = Elements.Box().Width(10).Height(10);
                    column = Elements.Column(owned);
                    Element first = Elements.Text(
                        (ref w) =>
                        {
                            w.Inline(box);
                            if (twice.Get())
                            {
                                w.Inline(box);
                            }
                        }
                    );
                    Element second = Elements.Text(
                        (ref w) =>
                        {
                            w.Write("n".AsSpan());
                            if (taken.Get())
                            {
                                w.Inline(owned);
                            }
                        }
                    );
                    return Elements.Column(first, second, column);
                });

                twice.Set(true);
                Assert.IsType<InvalidOperationException>(Assert.Single(reported));
                taken.Set(true);
                Assert.Equal(2, reported.Count);
                Assert.IsType<InvalidOperationException>(reported[1]);
                Assert.Equal(column.Node, scene.Tree.Nodes.GetRef(owned.Node).Parent);
            }
            finally
            {
                ReactiveRuntime.UnhandledExceptionHandler = previous;
            }
        });

    /// <summary>
    /// The room takes its range's background; it draws no glyph, and a decoration stops at it.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A text in a range of a blue background and a red underline: "nn", a box of 30 x 10, "nn".
    ///
    /// Steps:
    /// - Mount and draw.
    ///
    /// Expected:
    /// - One blue rectangle under the whole line (0 to 64.16); two glyph runs of two glyphs; two red lines, from 0 to 17.08 and from
    ///   47.08 to 64.16.
    /// </remarks>
    [Fact]
    public void Room_TakesTheBackground_AndStopsTheDecoration() =>
        ui.Invoke(() =>
        {
            using var scene = new TextScene(_font);
            var style = new SpanStyle(Background: Blue, Decoration: new TextDecoration(DecorationLines.Underline, Color: Red));
            scene.Mount(() =>
            {
                Element box = Elements.Box().Width(30).Height(10);
                return Elements.Row(
                    Elements.Text(
                        (ref w) =>
                        {
                            using (w.Style(style))
                            {
                                w.Write("nn".AsSpan());
                                w.Inline(box);
                                w.Write("nn".AsSpan());
                            }
                        }
                    )
                );
            });

            List<TextScene.Drawn> commands = scene.Commands();
            TextScene.Drawn background = Assert.Single(commands, c => c.Tag == CommandTag.FillRect);
            Assert.Equal(Blue, background.Color);
            Assert.Equal(0, background.A);
            Assert.Equal(MathF.Round((4 * N) + 30), background.C);
            Assert.Equal([2, 2], commands.Where(c => c.Tag == CommandTag.GlyphRun).Select(c => c.Glyphs));
            TextScene.Drawn[] lines = [.. commands.Where(c => c.Tag == CommandTag.Line)];
            Assert.Equal(2, lines.Length);
            Assert.All(lines, l => Assert.Equal(Red, l.Color));
            Assert.Equal((0, 2 * N), (lines[0].A, lines[0].C), new Pair());
            Assert.Equal(((2 * N) + 30, (4 * N) + 30), (lines[1].A, lines[1].C), new Pair());
        });

    /// <summary>
    /// The child takes the pointer before the text: a click on it inside a link is the child's, and does not hover or click the
    /// link; a click on the link's glyphs is the link's.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A text whose link holds "nn" and a box of 30 x 10 that logs its clicks; the link logs its enter and its click.
    ///
    /// Steps:
    /// - Move to the box's middle (32, 11) and click; move to the link's glyphs (5, 10) and click.
    ///
    /// Expected:
    /// - "box" after the first click; then "enter" and "link".
    /// </remarks>
    [Fact]
    public void Child_TakesThePointerBeforeTheLink() =>
        ui.Invoke(() =>
        {
            using var scene = new TextScene(_font);
            scene.Mount(() =>
            {
                Element box = Elements.Box().Width(30).Height(10).OnClick(() => _log.Add("box"));
                return Elements.Row(
                    Elements.Text(
                        (ref w) =>
                        {
                            using (w.Link((in PointerEvent e) => _log.Add("link"), (in PointerEvent e) => _log.Add("enter")))
                            {
                                w.Write("nn".AsSpan());
                                w.Inline(box);
                            }
                        }
                    )
                );
            });

            scene.Draw();
            Click(scene, 32, 11);
            Assert.Equal(["box"], _log);
            Click(scene, 5, 10);
            Assert.Equal(["box", "enter", "link"], _log);
        });

    /// <summary>
    /// The room is one character to the caret: a point in its left half is before it, one in its right half after it.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A text "nn", a box of 30 x 10, "nn".
    ///
    /// Steps:
    /// - Hit-test the first line at 20 and at 40.
    ///
    /// Expected:
    /// - Offsets 2 and 3.
    /// </remarks>
    [Fact]
    public void Room_IsOneCharacterToTheCaret() =>
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

            TextLeaf leaf = scene.Leaf(text);
            Assert.Equal(2, TextCaret.HitTestText(leaf, 0, 20).TextOffset);
            Assert.Equal(3, TextCaret.HitTestText(leaf, 0, 40).TextOffset);
        });

    /// <summary>
    /// A frame that writes the same inline object again, with a range whose color changes, and lays the text and its child out
    /// again, allocates nothing.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A box; a text that writes "nn" in a range whose color a signal picks, then the box; warmed up with the iteration.
    ///
    /// Steps:
    /// - 1,000 iterations in the windows of <c>SteadyStateAllocation</c>: set the signal to the iteration's number's lowest bit, give
    ///   the box a padding of the same, run the frame asked for.
    ///
    /// Expected:
    /// - 0 bytes in every window.
    /// </remarks>
    [Fact]
    public void SteadyState_RewriteAndLayout_AllocateNothing() =>
        ui.Invoke(() =>
        {
            using var scene = new TextScene(_font);
            var pick = Signal.Create(0);
            Element box = default;
            scene.Mount(() =>
            {
                box = Elements.Box().Width(30).Height(10);
                return Elements.Row(
                    Elements.Text(
                        (ref w) =>
                        {
                            using (w.Style(new SpanStyle(Color: pick.Get() == 0 ? Red : Blue)))
                            {
                                w.Write("nn".AsSpan());
                            }

                            w.Inline(box);
                        }
                    )
                );
            });

            LayoutStyle style = scene.Tree.Nodes.GetRef(box.Node).Layout.Style;
            var paddings = new[] { style with { Padding = EdgeInsets.All(0) }, style with { Padding = EdgeInsets.All(1) } };

            void Iteration(int index)
            {
                pick.Set(index & 1);
                scene.Tree.SetStyle(box.Node, paddings[index & 1]);
                scene.Draw();
            }

            for (int i = 0; i < 50; i++)
            {
                Iteration(i);
            }

            XylemUI.Tests.SteadyStateAllocation.AssertNone(1_000, Iteration);
        });

    private static void SetWidth(TextScene scene, Element element, float width)
    {
        LayoutStyle style = scene.Tree.Nodes.GetRef(element.Node).Layout.Style;
        scene.Tree.SetStyle(element.Node, style with { Width = Length.Px(width) });
    }

    private static void Click(TextScene scene, int x, int y)
    {
        scene.Mouse(PointerInputKind.Move, x, y);
        scene.Mouse(PointerInputKind.Down, x, y, PointerButton.Left);
        scene.Mouse(PointerInputKind.Up, x, y, PointerButton.Left);
    }

    private static void AssertRect(Rect expected, Rect actual)
    {
        Assert.Equal(expected.X, actual.X, 3);
        Assert.Equal(expected.Y, actual.Y, 3);
        Assert.Equal(expected.Width, actual.Width, 3);
        Assert.Equal(expected.Height, actual.Height, 3);
    }

    private sealed class Pair : IEqualityComparer<(float, float)>
    {
        public bool Equals((float, float) x, (float, float) y) => MathF.Abs(x.Item1 - y.Item1) < 1e-3f && MathF.Abs(x.Item2 - y.Item2) < 1e-3f;

        public int GetHashCode((float, float) obj) => 0;
    }
}
