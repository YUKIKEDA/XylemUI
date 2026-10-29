using static XylemUI.Elements;
using NodeHandle = XylemUI.Handle<XylemUI.UiNode>;

namespace XylemUI.Text.Tests;

// The text of the component API (row V1d, design §2.13): the three overloads of Text and their defaults, the writer of
// TextBuilder, and the text a draw function lays out itself (TextLayout, design §3.2, "Text from a draw function"). The fonts'
// metrics are those of TextLeafTests: "n" advances 610 of 1000 units per em.
[Collection(UiThreadGroup.Name)]
public sealed class TextElementTests(UiThreadFixture ui) : IDisposable
{
    private readonly Font _font = TestFont.Open();

    public void Dispose() => _font.Dispose();

    /// <summary>
    /// <c>Text</c> with no style is at 14 px, Flutter's default size, in the tree's UI language, with the font's line height, the
    /// default font, and no color of its own, so that it takes its ancestors'.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A scene whose tree's UI language is Japanese.
    ///
    /// Steps:
    /// - Mount <c>Text("nn")</c>.
    ///
    /// Expected:
    /// - The leaf has the text, size 14, the language "ja", a line height that is not a number, the default font, and no color.
    /// </remarks>
    [Fact]
    public void Text_Defaults_AreFlutterSizeAndTheTreeLanguage() =>
        ui.Invoke(() =>
        {
            using var scene = new TextScene(_font);
            scene.Tree.UiLanguage = TestFont.Japanese;
            Element text = default;
            scene.Mount(() => text = Elements.Text("nn"));
            TextLeaf leaf = scene.Leaf(text);
            Assert.Equal("nn", leaf.Text.ToString());
            Assert.Equal(14, leaf.Style.Size);
            Assert.Equal(Elements.DefaultFontSize, leaf.Style.Size);
            Assert.Equal(TestFont.Japanese, leaf.Style.Language);
            Assert.True(float.IsNaN(leaf.Style.LineHeight));
            Assert.Equal(FontRequest.Default, leaf.Style.Font);
            Assert.Null(leaf.Color);
        });

    /// <summary>
    /// <c>Text(Func&lt;string&gt;)</c> writes the text again when a signal it read changes.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A text that reads a signal of numbers.
    ///
    /// Steps:
    /// - Mount; set the signal to 7.
    ///
    /// Expected:
    /// - "n0" at the mount, "n7" after.
    /// </remarks>
    [Fact]
    public void Text_OfAFunction_FollowsItsSignals() =>
        ui.Invoke(() =>
        {
            using var scene = new TextScene(_font);
            var count = Signal.Create(0);
            Element text = default;
            scene.Mount(() => text = Elements.Text(() => "n" + count.Get().ToString(System.Globalization.CultureInfo.InvariantCulture)));
            Assert.Equal("n0", scene.Leaf(text).Text.ToString());
            count.Set(7);
            Assert.Equal("n7", scene.Leaf(text).Text.ToString());
        });

    /// <summary>
    /// <c>TextBuilder</c> writes into the node's buffer: an interpolated string keeps the length of every part after the parts
    /// written before it, and a different text marks the leaf for layout.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A text written by a builder from two signals: a span, an interpolated string with a literal, a number, and a string, and a
    ///   number written alone.
    ///
    /// Steps:
    /// - Mount and draw; set the first signal to 12.
    ///
    /// Expected:
    /// - "> clicks: 3 of n|5" at the mount, written once; "> clicks: 12 of n|5" after, written twice, with a layout request.
    /// </remarks>
    [Fact]
    public void TextBuilder_ComparesItsText_AndKeepsTheInterpolatedLength() =>
        ui.Invoke(() =>
        {
            using var scene = new TextScene(_font);
            var count = Signal.Create(3);
            var other = Signal.Create(5);
            int writes = 0;
            Element text = default;
            scene.Mount(() =>
                text = Elements.Text(
                    (ref text) =>
                    {
                        writes++;
                        text.Write("> ".AsSpan());
                        text.Write($"clicks: {count.Get()} of {"n"}|");
                        text.Write(other.Get());
                    }
                )
            );
            Assert.Equal("> clicks: 3 of n|5", scene.Leaf(text).Text.ToString());
            Assert.Equal(1, writes);

            count.Set(12);
            Assert.Equal("> clicks: 12 of n|5", scene.Leaf(text).Text.ToString());
            Assert.Equal(2, writes);
            Assert.NotEmpty(scene.Tree.LayoutRoots);
        });

    /// <summary>
    /// A builder that writes the same text again leaves the leaf alone: the effect runs, and nothing is marked.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A text written by a builder that reads a signal and writes "nn" whatever its value.
    ///
    /// Steps:
    /// - Mount and draw; set the signal.
    ///
    /// Expected:
    /// - The builder ran twice; the tree has no layout request and no paint update, and asked for no frame.
    /// </remarks>
    [Fact]
    public void TextBuilder_SameText_MarksNothing() =>
        ui.Invoke(() =>
        {
            using var scene = new TextScene(_font);
            var signal = Signal.Create(0);
            int writes = 0;
            scene.Mount(() =>
                Elements.Text(
                    (ref text) =>
                    {
                        _ = signal.Get();
                        writes++;
                        text.Write("nn".AsSpan());
                    }
                )
            );
            int requests = scene.Scheduler.Requests;

            signal.Set(1);
            Assert.Equal(2, writes);
            Assert.Empty(scene.Tree.LayoutRoots);
            Assert.Empty(scene.Tree.PaintUpdates);
            Assert.Equal(requests, scene.Scheduler.Requests);
        });

    /// <summary>
    /// A text written by a builder grows its buffers as it needs, and a long text keeps every character.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A builder that writes a number of "n" given by a signal, then a number.
    ///
    /// Steps:
    /// - Mount with 1; set 300, then 2.
    ///
    /// Expected:
    /// - The text is that many "n" and the number each time.
    /// </remarks>
    [Fact]
    public void TextBuilder_LongText_GrowsTheBuffer() =>
        ui.Invoke(() =>
        {
            using var scene = new TextScene(_font);
            var length = Signal.Create(1);
            Element text = default;
            scene.Mount(() =>
                text = Elements.Text(
                    (ref text) =>
                    {
                        for (int i = 0; i < length.Get(); i++)
                        {
                            text.Write("n".AsSpan());
                        }

                        text.Write(123_456_789L);
                    }
                )
            );
            Assert.Equal("n123456789", scene.Leaf(text).Text.ToString());
            length.Set(300);
            Assert.Equal(new string('n', 300) + "123456789", scene.Leaf(text).Text.ToString());
            length.Set(2);
            Assert.Equal("nn123456789", scene.Leaf(text).Text.ToString());
        });

    /// <summary>
    /// A builder that makes another text of a builder while it writes (in the root function, where an effect runs as it is made)
    /// gives each its own buffer: both texts are whole.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A text whose builder writes "out", makes a text whose builder writes "inner" the first time it runs, and writes "er".
    ///
    /// Steps:
    /// - Mount.
    ///
    /// Expected:
    /// - The outer text is "outer" and the inner one "inner".
    /// </remarks>
    [Fact]
    public void TextBuilder_InsideAnother_WritesBothWhole() =>
        ui.Invoke(() =>
        {
            using var scene = new TextScene(_font);
            Element inner = default;
            Element outer = default;
            scene.Mount(() =>
            {
                outer = Elements.Text(
                    (ref text) =>
                    {
                        text.Write("out".AsSpan());
                        if (inner.IsNone)
                        {
                            inner = Elements.Text((ref nested) => nested.Write("inner".AsSpan()));
                        }

                        text.Write("er".AsSpan());
                    }
                );
                return Column(outer, inner);
            });
            Assert.Equal("outer", scene.Leaf(outer).Text.ToString());
            Assert.Equal("inner", scene.Leaf(inner).Text.ToString());
        });

    /// <summary>
    /// A frame that a builder writes a new number in allocates nothing.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A text "clicks: N" written by a builder from a signal; warmed up with the iteration.
    ///
    /// Steps:
    /// - 1,000 iterations in the windows of <c>SteadyStateAllocation</c>: set the signal to the iteration's number modulo 100, run
    ///   the frame asked for.
    ///
    /// Expected:
    /// - 0 bytes in every window, and the text has the last number.
    /// </remarks>
    [Fact]
    public void SteadyState_BuilderRewritesTheNumber_AllocatesNothing() =>
        ui.Invoke(() =>
        {
            using var scene = new TextScene(_font);
            var count = Signal.Create(0);
            Element text = default;
            scene.Mount(() => text = Elements.Text((ref text) => text.Write($"clicks: {count.Get()}")));

            void Iteration(int index)
            {
                count.Set((index + 1_000) % 100);
                scene.Scheduler.Run();
            }

            for (int i = 0; i < 200; i++)
            {
                Iteration(i);
            }

            XylemUI.Tests.SteadyStateAllocation.AssertNone(1_000, Iteration);
            Assert.Equal("clicks: 99", scene.Leaf(text).Text.ToString());
        });

    /// <summary>
    /// A text layout breaks its lines at the width given and draws them from the point given, as a text leaf of that width would.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A box whose draw function lays out "nn nn" at 20 px, at the width of the first word and at no limit, and draws it at
    ///   (10, 5) and at (30, 5).
    ///
    /// Steps:
    /// - Mount and draw.
    ///
    /// Expected:
    /// - At the word's width the size is 24.4 x 57.92 (two lines of 28.96); with no limit 53.28 x 28.96 (610 + 610 + 224 + 610 + 610 = 2,664
    ///   units). The two runs drawn are the same but 20 px apart.
    /// </remarks>
    [Fact]
    public void TextLayout_BreaksAtTheWidth_AndDrawsAtThePoint() =>
        ui.Invoke(() =>
        {
            using var scene = new TextScene(_font);
            var layout = new TextLayout("nn nn", new TextStyle(FontRequest.Default, 20, TestFont.Japanese, float.NaN));
            Size narrow = default;
            Size wide = default;
            scene.Mount(() =>
                Box()
                    .Width(200)
                    .Height(100)
                    .Draw(
                        (DrawingContext context, Rect box) =>
                        {
                            narrow = context.LayoutText(layout, 24.4f);
                            wide = context.LayoutText(layout, float.PositiveInfinity);
                            context.DrawText(layout, 10, 5, Color.FromRgb(0x000000));
                            context.DrawText(layout, 30, 5, Color.FromRgb(0x000000));
                        }
                    )
            );

            Assert.Equal(24.4f, narrow.Width, 3);
            Assert.Equal(57.92f, narrow.Height, 3);
            Assert.Equal(53.28f, wide.Width, 3);
            Assert.Equal(28.96f, wide.Height, 3);
            List<GlyphInstance[]> runs = scene.GlyphRuns();
            Assert.Equal(2, runs.Count);
            Assert.Equal(runs[0].Length, runs[1].Length);
            for (int i = 0; i < runs[0].Length; i++)
            {
                Assert.Equal(runs[0][i].X + 20, runs[1][i].X);
                Assert.Equal(runs[0][i].Y, runs[1][i].Y);
            }
        });

    /// <summary>
    /// A text layout is drawn only after it is laid out, a change of its text or style asks for a layout again, and the same text
    /// or style changes nothing; its style is checked as a text leaf's is.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A box whose draw function runs the steps; a tree with no text tools for the last.
    ///
    /// Steps:
    /// - Draw before laying out. Lay out; set the same text and the same style; draw. Set another text; draw. Lay out; set
    ///   another style; draw. Make a layout of size 0. On the tree with no text tools, lay out.
    ///
    /// Expected:
    /// - Each draw after a change and the first throw <see cref="InvalidOperationException"/>; the draw after the same text and
    ///   style draws. Size 0 throws <see cref="ArgumentOutOfRangeException"/>. The tree with no text tools throws
    ///   <see cref="InvalidOperationException"/>.
    /// </remarks>
    [Fact]
    public void TextLayout_DrawsOnlyWhenLaidOut() =>
        ui.Invoke(() =>
        {
            TextStyle style = new(FontRequest.Default, 20, TestFont.Japanese, float.NaN);
            var layout = new TextLayout("nn", style);
            var results = new List<string>();

            using var scene = new TextScene(_font);
            scene.Mount(() =>
                Box()
                    .Width(100)
                    .Height(50)
                    .Draw(
                        (DrawingContext context, Rect box) =>
                        {
                            DrawingContext c = context;
                            Color black = Color.FromRgb(0x000000);
                            try
                            {
                                c.DrawText(layout, 0, 0, black);
                                results.Add("first: drawn");
                            }
                            catch (InvalidOperationException)
                            {
                                results.Add("first: throws");
                            }

                            _ = c.LayoutText(layout, 100);
                            layout.SetText("nn");
                            layout.SetStyle(style);
                            c.DrawText(layout, 0, 0, black);
                            results.Add("same: drawn");
                            layout.SetText("n");
                            try
                            {
                                c.DrawText(layout, 0, 0, black);
                                results.Add("text: drawn");
                            }
                            catch (InvalidOperationException)
                            {
                                results.Add("text: throws");
                            }

                            _ = c.LayoutText(layout, 100);
                            layout.SetStyle(style with { Size = 30 });
                            try
                            {
                                c.DrawText(layout, 0, 0, black);
                                results.Add("style: drawn");
                            }
                            catch (InvalidOperationException)
                            {
                                results.Add("style: throws");
                            }
                        }
                    )
            );
            Assert.Equal(["first: throws", "same: drawn", "text: throws", "style: throws"], results);
            Assert.Throws<ArgumentOutOfRangeException>(() => new TextLayout("n", style with { Size = 0 }));

            var bare = new UiTree();
            NodeHandle root = bare.CreateNode(new LayoutStyle { Width = Length.Px(10), Height = Length.Px(10) });
            Exception? thrown = null;
            bare.SetDraw(
                root,
                (DrawingContext context, Rect box) =>
                {
                    try
                    {
                        _ = context.LayoutText(layout, 10);
                    }
                    catch (InvalidOperationException exception)
                    {
                        thrown = exception;
                    }
                }
            );
            using var atlas = new GlyphAtlas(new TestRasterizer());
            var frames = new WindowFrames(bare, root, new NoTarget(), atlas, new TextScene.Frames(), Color.FromRgb(0xFFFFFF));
            frames.Resize(new Size(10, 10), 1);
            Assert.NotNull(thrown);
        });

    /// <summary>
    /// A text leaf whose draw function throws still draws its text: the draw function comes before the leaf's text, and the
    /// exception is reported (design §5.0, "The exceptions of draw functions").
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A text "nn" whose draw function throws; the reactive runtime's handler records what it is given.
    ///
    /// Steps:
    /// - Mount and draw.
    ///
    /// Expected:
    /// - One exception reported, the one thrown; the list has one glyph run of two glyphs.
    /// </remarks>
    [Fact]
    public void DrawFunction_OfATextLeafThrows_DrawsItsText() =>
        ui.Invoke(() =>
        {
            var reported = new List<Exception>();
            Action<Exception> previous = ReactiveRuntime.UnhandledExceptionHandler;
            ReactiveRuntime.UnhandledExceptionHandler = reported.Add;
            try
            {
                using var scene = new TextScene(_font);
                var thrown = new InvalidOperationException("thrown by the draw function");
                scene.Mount(() => Elements.Text("nn").Draw((DrawingContext context, Rect box) => throw thrown));
                Assert.Equal([thrown], reported);
                List<GlyphInstance[]> runs = scene.GlyphRuns();
                Assert.Single(runs);
                Assert.Equal(2, runs[0].Length);
            }
            finally
            {
                ReactiveRuntime.UnhandledExceptionHandler = previous;
            }
        });
}
