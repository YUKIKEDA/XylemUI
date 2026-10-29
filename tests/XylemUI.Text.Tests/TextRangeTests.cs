using static XylemUI.Elements;

namespace XylemUI.Text.Tests;

// The ranges a text writer writes (row T12, design §4.8): nesting and inheritance, the two flat lists, the links, the scopes
// left open, and which change marks the leaf for layout and which only for drawing.
[Collection(UiThreadGroup.Name)]
public sealed class TextRangeTests(UiThreadFixture ui) : IDisposable
{
    private static readonly Color Red = Color.FromRgb(0xFF0000);
    private static readonly Color Blue = Color.FromRgb(0x0000FF);
    private readonly Font _font = TestFont.Open();

    public void Dispose() => _font.Dispose();

    /// <summary>
    /// Nested ranges take the fields they leave <see langword="null"/> from the range around them, and the writer flattens them
    /// into a list of the layout's ranges and one of the look's, joining neighbours that are the same.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A builder that writes "a", then in a range of size 20 and red "b", in a range of blue inside it "c", "d" after the inner
    ///   range, and "e" after the outer one.
    ///
    /// Steps:
    /// - Mount.
    ///
    /// Expected:
    /// - The layout's ranges are (0, 1) with no size, (1, 3) of size 20, (4, 1) with no size: the blue range has the outer size,
    ///   so it joins it.
    /// - The look's ranges are (0, 1) with no color, (1, 1) red, (2, 1) blue, (3, 1) red, (4, 1) with no color, none of them a
    ///   link.
    /// </remarks>
    [Fact]
    public void Writer_NestedRanges_InheritAndFlatten() =>
        ui.Invoke(() =>
        {
            using var scene = new TextScene(_font);
            Element text = default;
            scene.Mount(() =>
                text = Elements.Text(
                    (ref w) =>
                    {
                        w.Write("a".AsSpan());
                        using (w.Style(new SpanStyle(Size: 20, Color: Red)))
                        {
                            w.Write("b".AsSpan());
                            using (w.Style(new SpanStyle(Color: Blue)))
                            {
                                w.Write("c".AsSpan());
                            }

                            w.Write("d".AsSpan());
                        }

                        w.Write("e".AsSpan());
                    }
                )
            );

            TextRanges ranges = scene.Leaf(text).Ranges!;
            Assert.Equal(
                [
                    new LayoutRun(0, 1, null, null, null, null, null),
                    new LayoutRun(1, 3, null, 20, null, null, null),
                    new LayoutRun(4, 1, null, null, null, null, null),
                ],
                ranges.Layout.ToArray()
            );
            Assert.Equal(
                [
                    new PaintRun(0, 1, null, null, null, null, -1),
                    new PaintRun(1, 1, Red, null, null, null, -1),
                    new PaintRun(2, 1, Blue, null, null, null, -1),
                    new PaintRun(3, 1, Red, null, null, null, -1),
                    new PaintRun(4, 1, null, null, null, null, -1),
                ],
                ranges.Paint.ToArray()
            );
            Assert.Empty(ranges.Links.ToArray());
        });

    /// <summary>
    /// A link's range points to its link, a range inside it keeps the link, and a link inside a link takes the inner one; the
    /// links keep their handlers and hovered style.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A builder that writes, in a link A with a hovered style of a red underline, "a", a range of blue with "b", a link B with
    ///   "c", and "d".
    ///
    /// Steps:
    /// - Mount.
    ///
    /// Expected:
    /// - Two links, A then B, with their handlers and A's hovered style.
    /// - The look's ranges are (0, 1) of link 0, (1, 1) blue of link 0, (2, 1) of link 1, (3, 1) of link 0.
    /// </remarks>
    [Fact]
    public void Writer_Links_PointToTheInnermost() =>
        ui.Invoke(() =>
        {
            using var scene = new TextScene(_font);
            PointerHandler a = (in PointerEvent e) => { };
            PointerHandler b = (in PointerEvent e) => { };
            PointerHandler enter = (in PointerEvent e) => { };
            var hovered = new SpanStyle(Decoration: new TextDecoration(DecorationLines.Underline, Color: Red));
            Element text = default;
            scene.Mount(() =>
                text = Elements.Text(
                    (ref w) =>
                    {
                        using (w.Link(a, onEnter: enter, hovered: hovered))
                        {
                            w.Write("a".AsSpan());
                            using (w.Style(new SpanStyle(Color: Blue)))
                            {
                                w.Write("b".AsSpan());
                            }

                            using (w.Link(b))
                            {
                                w.Write("c".AsSpan());
                            }

                            w.Write("d".AsSpan());
                        }
                    }
                )
            );

            TextRanges ranges = scene.Leaf(text).Ranges!;
            Assert.Equal([new TextLink(a, enter, null, hovered), new TextLink(b, null, null, default)], ranges.Links.ToArray());
            Assert.Equal(
                [
                    new PaintRun(0, 1, null, null, null, null, 0),
                    new PaintRun(1, 1, Blue, null, null, null, 0),
                    new PaintRun(2, 1, null, null, null, null, 1),
                    new PaintRun(3, 1, null, null, null, null, 0),
                ],
                ranges.Paint.ToArray()
            );
        });

    /// <summary>
    /// A builder that ends with a range still open throws <see cref="InvalidOperationException"/>, which goes to the handler of
    /// the effects' exceptions, and the leaf keeps the text it had; the next write starts clean.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A builder that writes "nn" and, while a signal is true, opens a range it does not close; the handler of the effects'
    ///   exceptions records what it is given.
    ///
    /// Steps:
    /// - Mount with false; set true; set false.
    ///
    /// Expected:
    /// - After true: one <see cref="InvalidOperationException"/> reported, and the leaf has no ranges. After false: nothing more
    ///   reported, and the leaf has no ranges.
    /// </remarks>
    [Fact]
    public void Writer_ScopeLeftOpen_Throws() =>
        ui.Invoke(() =>
        {
            var reported = new List<Exception>();
            Action<Exception> previous = ReactiveRuntime.UnhandledExceptionHandler;
            ReactiveRuntime.UnhandledExceptionHandler = reported.Add;
            try
            {
                using var scene = new TextScene(_font);
                var open = Signal.Create(false);
                Element text = default;
                scene.Mount(() =>
                    text = Elements.Text(
                        (ref w) =>
                        {
                            w.Write("nn".AsSpan());
                            if (open.Get())
                            {
                                _ = w.Style(new SpanStyle(Color: Red));
                            }
                        }
                    )
                );

                open.Set(true);
                Assert.IsType<InvalidOperationException>(Assert.Single(reported));
                Assert.Null(scene.Leaf(text).Ranges);
                open.Set(false);
                Assert.Single(reported);
                Assert.Null(scene.Leaf(text).Ranges);
            }
            finally
            {
                ReactiveRuntime.UnhandledExceptionHandler = previous;
            }
        });

    /// <summary>
    /// A change of a range's color only marks the leaf for drawing; a change of a range's size marks it for layout; the same
    /// ranges again mark nothing; and a write with no range drops the leaf's ranges and lays it out.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A builder that writes "nn" in a range whose style a signal picks: red, blue, size 20, or none (no range at all).
    ///
    /// Steps:
    /// - Mount and draw with red. Set blue; read the marks; draw. Set blue again (a new value of the signal of the same style);
    ///   read. Set size 20; read; draw. Set none; read.
    ///
    /// Expected:
    /// - Blue: a paint update and no layout request. The same: neither. Size 20: a layout request. None: a layout request and no
    ///   ranges.
    /// </remarks>
    [Fact]
    public void Leaf_ChangeOfTheLookOnly_IsNotLaidOut() =>
        ui.Invoke(() =>
        {
            using var scene = new TextScene(_font);
            var style = Signal.Create<SpanStyle?>(new SpanStyle(Color: Red), EqualityComparer<SpanStyle?>.Create((x, y) => false));
            Element text = default;
            scene.Mount(() =>
                text = Elements.Text(
                    (ref w) =>
                    {
                        if (style.Get() is { } s)
                        {
                            using (w.Style(s))
                            {
                                w.Write("nn".AsSpan());
                            }
                        }
                        else
                        {
                            w.Write("nn".AsSpan());
                        }
                    }
                )
            );

            style.Set(new SpanStyle(Color: Blue));
            Assert.Empty(scene.Tree.LayoutRoots);
            Assert.NotEmpty(scene.Tree.PaintUpdates);
            scene.Scheduler.Run();

            style.Set(new SpanStyle(Color: Blue));
            Assert.Empty(scene.Tree.LayoutRoots);
            Assert.Empty(scene.Tree.PaintUpdates);

            style.Set(new SpanStyle(Size: 20));
            Assert.NotEmpty(scene.Tree.LayoutRoots);
            scene.Scheduler.Run();

            style.Set(null);
            Assert.NotEmpty(scene.Tree.LayoutRoots);
            Assert.Null(scene.Leaf(text).Ranges);
        });

    /// <summary>
    /// The ranges of a leaf take design §4.8's bytes: 136 + 80 L + 60 P + 144 K, for arrays of L ranges of the layout, P of the
    /// look, and K links, each 2 long at first and doubled when full.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - The sizes of a range of the layout, of the look, and of a link, read with <c>Unsafe.SizeOf</c>: 80, 60, and 144 bytes.
    ///
    /// Steps:
    /// - Measure the bytes allocated while a <c>TextRanges</c> is made and given 3 links and 5 pieces of text, each of a size and
    ///   a color of its own.
    ///
    /// Expected:
    /// - The sizes are as above; the arrays are 8, 8, and 4 long, and the bytes are the formula for them, to the 8 bytes each array
    ///   is rounded to.
    /// </remarks>
    [Fact]
    public void Ranges_Memory_IsTheFormula()
    {
        Assert.Equal(
            (80, 60, 144),
            (
                System.Runtime.CompilerServices.Unsafe.SizeOf<LayoutRun>(),
                System.Runtime.CompilerServices.Unsafe.SizeOf<PaintRun>(),
                System.Runtime.CompilerServices.Unsafe.SizeOf<TextLink>()
            )
        );
        PointerHandler click = (in PointerEvent e) => { };
        var link = new TextLink(click, null, null, default);
        _ = new TextRanges();

        long before = GC.GetAllocatedBytesForCurrentThread();
        var ranges = new TextRanges();
        for (int i = 0; i < 3; i++)
        {
            _ = ranges.AddLink(link);
        }

        for (int i = 0; i < 5; i++)
        {
            ranges.Add(i, 1, new SpanStyle(Size: 10 + i, Color: Color.FromRgb((uint)i)), -1);
        }

        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal((5, 5, 3), (ranges.Layout.Length, ranges.Paint.Length, ranges.Links.Length));

        // The links' array grew from 2 to 4, and the others from 2 to 4 and 8; the arrays they left were allocated too.
        long kept = 136 + (80 * 8) + (60 * 8) + (144 * 4);
        long left = (24 + (80 * 2)) + (24 + (80 * 4)) + (24 + (60 * 2)) + (24 + (60 * 4)) + (24 + (144 * 2));
        Assert.InRange(allocated, kept + left, kept + left + (8 * 8));
    }

    /// <summary>
    /// A frame that a builder writes the same ranges in, or ranges whose looks swap, allocates nothing, the drawing of their colors,
    /// backgrounds, and decorations included.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A builder that writes "clicks " in a link and a number in a range whose style a signal picks: red, underlined and struck
    ///   through, or blue on red, with a dashed overline; warmed up with the iteration.
    ///
    /// Steps:
    /// - 1,000 iterations in the windows of <c>SteadyStateAllocation</c>: set the signal to the iteration's number modulo 2, run
    ///   the frame asked for.
    ///
    /// Expected:
    /// - 0 bytes in every window.
    /// </remarks>
    [Fact]
    public void SteadyState_RangesRewritten_AllocateNothing() =>
        ui.Invoke(() =>
        {
            using var scene = new TextScene(_font);
            var pick = Signal.Create(0);
            PointerHandler click = (in PointerEvent e) => { };
            var red = new SpanStyle(Color: Red, Decoration: new TextDecoration(DecorationLines.Underline | DecorationLines.LineThrough));
            var blue = new SpanStyle(Color: Blue, Background: Red, Decoration: new TextDecoration(DecorationLines.Overline, DecorationStyle.Dashed));
            scene.Mount(() =>
                Elements.Text(
                    (ref w) =>
                    {
                        using (w.Link(click))
                        {
                            w.Write("clicks ".AsSpan());
                        }

                        using (w.Style(pick.Get() == 0 ? red : blue))
                        {
                            w.Write(42);
                        }
                    }
                )
            );

            void Iteration(int index)
            {
                pick.Set(index % 2);
                scene.Scheduler.Run();
            }

            for (int i = 0; i < 200; i++)
            {
                Iteration(i);
            }

            XylemUI.Tests.SteadyStateAllocation.AssertNone(1_000, Iteration);
        });
}
