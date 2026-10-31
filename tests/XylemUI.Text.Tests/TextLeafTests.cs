using XylemUI.Text.LineBreak;
using NodeHandle = XylemUI.Handle<XylemUI.UiNode>;

namespace XylemUI.Text.Tests;

// The text leaf (row V1a, design §3.2, "Text leaves"): its size and baseline from its lines, its two caches, and what a change
// of its text or style marks. The fonts' metrics are fontTools' (hhea, and OS/2 where USE_TYPO_METRICS is set): the test font
// has an ascent of 1160, a descent of 288, and no line gap in 1000 units per em; the bundled font 1069, 293, and 0. At 20 px the
// test font's line is 23.2 + 5.76 = 28.96 px high. Advances are fontTools' too: "n" 610, a space 224, "#" 555, each kana and
// kanji 1000.
[Collection(UiThreadGroup.Name)]
public sealed class TextLeafTests(UiThreadFixture ui) : IDisposable
{
    private const float Size = 20;
    private const float Ascent = 23.2f;
    private const float Line = 28.96f;

    private static readonly string[] PaddedTexts = ["nn nn", "nn-TT and nn", "日本語 の テキスト"];
    private static readonly float[] PaddedSizes = [11f, 12.5f, 13.3f, 14f, 15.7f, 17.3f];
    private static readonly float[] Paddings = [1f, 2.5f, 3f, 4.3f, 7f];

    private readonly Font _font = TestFont.Open();

    public void Dispose() => _font.Dispose();

    /// <summary>
    /// A line is as high as the fonts' ascent, descent, and line gap, its baseline the ascent below its top, and the leaf as wide
    /// as the line.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A window of 400 x 1000: a column, then a row, then the text leaf at 20 px with the test font.
    ///
    /// Steps:
    /// - Lay out "nn".
    ///
    /// Expected:
    /// - The leaf is 24.4 x 28.96 (2 × 610 / 1000 × 20 wide), its first baseline 23.2.
    /// </remarks>
    [Fact]
    public void OneLine_TakesTheFontsHeightAndBaseline() =>
        ui.Invoke(() =>
        {
            using var context = new TextContext(new TestFontProvider(_font));
            (UiTree tree, NodeHandle leaf) = Build(context, "nn", 400);

            AssertRect(24.4f, Line, tree.GetRect(leaf));
            Assert.Equal(Ascent, tree.Nodes.GetRef(leaf).Layout.FirstBaseline, 3);
        });

    /// <summary>
    /// The lines of a text that wraps stack, each as high as one line; the first baseline is the first line's.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - As above, in a window 30 wide.
    ///
    /// Steps:
    /// - Lay out "nn nn" ("nn" is 24.4 wide, "nn nn" 53.28).
    ///
    /// Expected:
    /// - Two lines: the leaf is 30 wide (the row's width, more than its min-content 24.4) and 57.92 high; its first baseline 23.2.
    /// </remarks>
    [Fact]
    public void WrappedText_StacksItsLines() =>
        ui.Invoke(() =>
        {
            using var context = new TextContext(new TestFontProvider(_font));
            (UiTree tree, NodeHandle leaf) = Build(context, "nn nn", 30);

            AssertRect(30, 2 * Line, tree.GetRect(leaf));
            Assert.Equal(Ascent, tree.Nodes.GetRef(leaf).Layout.FirstBaseline, 3);
            LineBreakCache cache = tree.Nodes.GetRef(leaf).Text!.LineBreaks!;
            Assert.Equal(2, cache[cache.Committed].Layout.Lines.Length);
        });

    /// <summary>
    /// A line that takes two fonts takes the largest ascent and the largest descent of them.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - As above; the provider gives "#" no font, so it takes the bundled font (ascent 21.38, descent 5.86 at 20 px).
    ///
    /// Steps:
    /// - Lay out "n#".
    ///
    /// Expected:
    /// - The leaf is 23.2 + 5.86 = 29.06 high, its first baseline 23.2; it is 12.2 + 12.92 = 25.12 wide.
    /// </remarks>
    [Fact]
    public void LineOfTwoFonts_TakesTheLargestAscentAndDescent() =>
        ui.Invoke(() =>
        {
            using var context = new TextContext(new TestFontProvider(_font, noFont: "#"));
            (UiTree tree, NodeHandle leaf) = Build(context, "n#", 400);

            AssertRect(25.12f, Ascent + 5.86f, tree.GetRect(leaf));
            Assert.Equal(Ascent, tree.Nodes.GetRef(leaf).Layout.FirstBaseline, 3);
        });

    /// <summary>
    /// A line height of the style replaces the fonts' height, and the difference is split half above and half below the text.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - As above, with a line height of 2.
    ///
    /// Steps:
    /// - Lay out "nn".
    ///
    /// Expected:
    /// - The leaf is 40 high (20 × 2); its first baseline is (40 − 28.96) / 2 + 23.2 = 28.72.
    /// </remarks>
    [Fact]
    public void LineHeight_SplitsTheDifferenceHalfAboveAndBelow() =>
        ui.Invoke(() =>
        {
            using var context = new TextContext(new TestFontProvider(_font));
            (UiTree tree, NodeHandle leaf) = Build(context, "nn", 400, lineHeight: 2);

            AssertRect(24.4f, 40, tree.GetRect(leaf));
            Assert.Equal(28.72f, tree.Nodes.GetRef(leaf).Layout.FirstBaseline, 3);
        });

    /// <summary>
    /// An empty text is one line high and 0 wide, and has no shaped text; a line of only a hard line break is one line high.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - As above.
    ///
    /// Steps:
    /// - Lay out "" in one tree and "n\n\nn" in another.
    ///
    /// Expected:
    /// - The empty leaf is 0 x 28.96 with its first baseline at 23.2, and keeps no shaping result. The other is 3 lines,
    ///   86.88 high.
    /// </remarks>
    [Fact]
    public void EmptyTextAndEmptyLine_AreOneLineHigh() =>
        ui.Invoke(() =>
        {
            using var context = new TextContext(new TestFontProvider(_font));
            (UiTree empty, NodeHandle leaf) = Build(context, string.Empty, 400);
            (UiTree lines, NodeHandle three) = Build(context, "n\n\nn", 400);

            AssertRect(0, Line, empty.GetRect(leaf));
            Assert.Equal(Ascent, empty.Nodes.GetRef(leaf).Layout.FirstBaseline, 3);
            Assert.Null(empty.Nodes.GetRef(leaf).Text!.Shaped!.Result);
            Assert.Equal(3 * Line, lines.GetRect(three).Height, 3);
        });

    /// <summary>
    /// A text set to empty after it was laid out is laid out as an empty text: one line high, 0 wide, with no shaped text and no
    /// lines; set again, it is shaped again.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - "nn" laid out as above.
    ///
    /// Steps:
    /// - Set ""; lay out; read the leaf. Set "nnn"; lay out; read it.
    ///
    /// Expected:
    /// - Empty: 0 x 28.96, no shaping result, and no committed lines. Then 36.6 wide, with a shaping result of 3 glyphs.
    /// </remarks>
    [Fact]
    public void TextSetToEmpty_HasNoShapedTextOrLines() =>
        ui.Invoke(() =>
        {
            using var context = new TextContext(new TestFontProvider(_font));
            (UiTree tree, NodeHandle leaf) = Build(context, "nn", 400);
            TextLeaf text = tree.Nodes.GetRef(leaf).Text!;

            tree.SetText(leaf, string.Empty);
            LayoutEngine.RunLayoutPass(tree);
            Rect emptied = tree.GetRect(leaf);
            (ShapingResult? Result, bool Committed) empty = (text.Shaped!.Result, text.LineBreaks is { Committed: >= 0 });
            tree.SetText(leaf, "nnn");
            LayoutEngine.RunLayoutPass(tree);

            AssertRect(0, Line, emptied);
            Assert.Equal((null, false), empty);
            Assert.Equal(36.6f, tree.GetRect(leaf).Width, 3);
            Assert.Equal(3, text.Shaped!.Result!.GlyphCount);
        });

    /// <summary>
    /// The text and the style that are already there mark nothing; another text or style drops the caches and marks the layout.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - "nn" laid out as above.
    ///
    /// Steps:
    /// - Set "nn" again and the same style; then set "nnn"; lay out; then set a style of 40 px.
    ///
    /// Expected:
    /// - The same text and style leave the layout clean and the caches valid. "nnn" marks it, and its layout is 36.6 wide.
    ///   The new style marks it and drops the shaped text.
    /// </remarks>
    [Fact]
    public void SetTextAndStyle_MarkOnlyAChange() =>
        ui.Invoke(() =>
        {
            using var context = new TextContext(new TestFontProvider(_font));
            (UiTree tree, NodeHandle leaf) = Build(context, "nn", 400);
            TextLeaf text = tree.Nodes.GetRef(leaf).Text!;

            tree.SetText(leaf, "nn");
            tree.SetTextStyle(leaf, text.Style);
            Assert.True(IsLayoutClean(tree, leaf));
            Assert.True(text.Shaped!.Valid);

            tree.SetText(leaf, "nnn");
            Assert.False(IsLayoutClean(tree, leaf));
            Assert.False(text.Shaped.Valid);
            LayoutEngine.RunLayoutPass(tree);
            Assert.Equal(36.6f, tree.GetRect(leaf).Width, 3);

            tree.SetTextStyle(leaf, text.Style with { Size = 40 });
            Assert.False(IsLayoutClean(tree, leaf));
            Assert.False(text.Shaped.Valid);
        });

    /// <summary>
    /// A style out of range and a tree without a text context throw.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A tree with a text context and one without.
    ///
    /// Steps:
    /// - Create text leaves with a size of 0, NaN, and infinity, and a line height of 0 and infinity; create one in the tree
    ///   without a context; set a text on a node that is not a text leaf.
    ///
    /// Expected:
    /// - ArgumentOutOfRangeException for each value; InvalidOperationException for the tree and for the node.
    /// </remarks>
    [Fact]
    public void BadStyleOrTree_Throws() =>
        ui.Invoke(() =>
        {
            using var context = new TextContext(new TestFontProvider(_font));
            var tree = new UiTree(context);
            TextStyle style = Style();

            foreach (float size in new[] { 0f, float.NaN, float.PositiveInfinity })
            {
                Assert.Throws<ArgumentOutOfRangeException>(() => tree.CreateTextNode(new LayoutStyle(), style with { Size = size }));
            }

            foreach (float lineHeight in new[] { 0f, float.PositiveInfinity })
            {
                Assert.Throws<ArgumentOutOfRangeException>(() => tree.CreateTextNode(new LayoutStyle(), style with { LineHeight = lineHeight }));
            }

            Assert.Throws<InvalidOperationException>(() => new UiTree().CreateTextNode(new LayoutStyle(), style));
            NodeHandle box = tree.CreateNode(new LayoutStyle());
            Assert.Throws<InvalidOperationException>(() => tree.SetText(box, "n"));
        });

    /// <summary>
    /// The content widths of a text leaf are its longest piece and its longest line, which a row reads without measuring.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - As above.
    ///
    /// Steps:
    /// - Lay out "nn nn" in a window 400 wide and in one 10 wide.
    ///
    /// Expected:
    /// - 53.28 wide in one line in the wide window (max-content: 4 × 12.2 + 4.48); 24.4 wide in two lines in the narrow one
    ///   (min-content, which the row does not shrink it under). Debug builds also check both against the measurement.
    /// </remarks>
    [Fact]
    public void ContentWidths_AreTheLongestPieceAndLine() =>
        ui.Invoke(() =>
        {
            using var context = new TextContext(new TestFontProvider(_font));
            (UiTree wide, NodeHandle one) = Build(context, "nn nn", 400);
            (UiTree narrow, NodeHandle two) = Build(context, "nn nn", 10);

            AssertRect(53.28f, Line, wide.GetRect(one));
            AssertRect(24.4f, 2 * Line, narrow.GetRect(two));
        });

    /// <summary>
    /// A text leaf with padding, laid out at its own max-content width, stays on one line: the width it gets back after the
    /// padding is taken off may be one bit under its longest line (PR #301's review).
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A window 1,000 px wide: a column, then a row, then the text leaf with the test font. The row gives the leaf its
    ///   max-content width plus its padding.
    ///
    /// Steps:
    /// - Lay out each of "nn nn", "nn-TT and nn", and "日本語 の テキスト" at sizes 11, 12.5, 13.3, 14, 15.7, and 17.3 px, with
    ///   padding of 1, 2.5, 3, 4.3, and 7 px on every side: 90 leaves.
    ///
    /// Expected:
    /// - Each leaf has one line, and one entry, whose key is +∞: the width it is laid out at counts as its max-content width.
    /// </remarks>
    [Fact]
    public void PaddedLeafAtItsMaxContentWidth_StaysOnOneLine() =>
        ui.Invoke(() =>
        {
            using var context = new TextContext(new TestFontProvider(_font));
            var wrapped = new List<string>();
            foreach (string text in PaddedTexts)
            {
                foreach (float size in PaddedSizes)
                {
                    foreach (float padding in Paddings)
                    {
                        var tree = new UiTree(context);
                        NodeHandle root = tree.CreateNode(new LayoutStyle { Algorithm = LayoutAlgorithm.Flex.Column });
                        NodeHandle row = tree.CreateNode(new LayoutStyle { Algorithm = LayoutAlgorithm.Flex.Row });
                        tree.AppendChild(root, row);
                        NodeHandle leaf = tree.CreateTextNode(new LayoutStyle { Padding = EdgeInsets.All(padding) }, Style() with { Size = size });
                        tree.SetText(leaf, text);
                        tree.AppendChild(row, leaf);
                        LayOut(tree, root, 1000);
                        LineBreakCache cache = tree.Nodes.GetRef(leaf).Text!.LineBreaks!;
                        LineBreakEntry entry = cache[cache.Committed];
                        if (entry.Layout.Lines.Length != 1 || !float.IsPositiveInfinity(entry.Width) || cache.Count != 1)
                        {
                            wrapped.Add(
                                $"{text} at {size} px with {padding} px: {entry.Layout.Lines.Length} lines at {entry.Width}, {cache.Count} widths"
                            );
                        }
                    }
                }
            }

            Assert.Empty(wrapped);
        });

    /// <summary>
    /// The entry of the width the layout committed is not overwritten, however many other widths are measured.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A line break cache.
    ///
    /// Steps:
    /// - Take widths 1 to 4; commit the entry of 1; take widths 5 to 12.
    ///
    /// Expected:
    /// - Width 1 is still kept, the cache holds 4 widths, and width 2 (the oldest after 1) is gone.
    /// </remarks>
    [Fact]
    public void LineBreakCache_KeepsTheCommittedWidth()
    {
        var cache = new LineBreakCache();
        int first = -1;
        for (int width = 1; width <= 4; width++)
        {
            _ = cache.Take(width, out int index);
            if (width == 1)
            {
                first = index;
            }
        }

        cache.Commit(first);
        for (int width = 5; width <= 12; width++)
        {
            _ = cache.Take(width, out _);
        }

        Assert.True(cache.TryGet(1, out int kept));
        Assert.Equal(first, kept);
        Assert.Equal(4, cache.Count);
        Assert.False(cache.TryGet(2, out _));
    }

    /// <summary>
    /// A text leaf allocates, the first time it is measured, no more than design §3.2's memory formula for its content, with
    /// 32 bytes for each array's header and 64 for each object's.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A row in a column, holding one text leaf of "日本語のテキスト", laid out twice to warm up; a row of 2 such leaves laid out
    ///   in another tree, for the thread's flex scratch.
    ///
    /// Steps:
    /// - Measure the bytes allocated while a second leaf of "日本語のテキスト" is created, given its text, appended, and laid out.
    /// - The bytes are counted with GC.GetAllocatedBytesForCurrentThread, not PerformanceAssert: the check is what a first
    ///   measurement allocates against a formula, not a steady state that allocates nothing.
    ///
    /// Expected:
    /// - n = g = 8 and r = L = e = 1 (the formula's terms, read from the leaf); the bytes are at most the formula for its
    ///   counts (10n + 8 + 25g + 8b + 56r, 64L + 24g a width, 2n of text) plus 15 array headers, 7 object headers, and the leaf's
    ///   reference to its selection, 8 bytes (design §4.10).
    /// </remarks>
    [Fact]
    public void FirstMeasurement_AllocatesNoMoreThanTheFormula() =>
        ui.Invoke(() =>
        {
            const string Text = "日本語のテキスト";
            using var context = new TextContext(new TestFontProvider(_font));

            // The flex scratch of the thread grows to a row of 2 items once, in a tree of its own.
            (UiTree warm, NodeHandle _) = Build(context, Text, 400);
            AddLeaf(warm, warm.Nodes.GetRef(warm.Root).FirstChild, Text);
            LayOut(warm, warm.Root, 400);

            var tree = new UiTree(context);
            NodeHandle root = tree.CreateNode(new LayoutStyle { Algorithm = LayoutAlgorithm.Flex.Column });
            NodeHandle row = tree.CreateNode(new LayoutStyle { Algorithm = LayoutAlgorithm.Flex.Row });
            tree.AppendChild(root, row);
            AddLeaf(tree, row, Text);

            // Twice: the pass swaps the tree's two lists of layout roots, and each makes its array at its first request.
            LayOut(tree, root, 400);
            LayOut(tree, root, 400);

            long before = GC.GetAllocatedBytesForCurrentThread();
            NodeHandle leaf = AddLeaf(tree, row, Text);
            LayoutEngine.RunLayoutPass(tree);
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

            TextLeaf text = tree.Nodes.GetRef(leaf).Text!;
            ShapingResult shaped = text.Shaped!.Result!;
            LineBreakCache cache = text.LineBreaks!;
            int n = text.Length;
            int g = shaped.GlyphCount;
            int b = shaped.BreakCount;
            int r = shaped.Runs.Length;
            int lines = cache[cache.Committed].Layout.Lines.Length;
            int e = cache.Count;
            Assert.Equal((8, 8, 1, 1, 1), (n, g, r, lines, e));
            long formula = (10 * n) + 8 + (25 * g) + (8 * b) + (56 * r) + (e * ((64 * lines) + (24 * g))) + (2 * n);
            Assert.InRange(allocated, formula, formula + (15 * 32) + (7 * 64) + 8);
        });

    /// <summary>
    /// Laying text leaves out again allocates nothing, when their widths are cached and when new widths reuse the entries.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A column of 100 text leaves of "日本語のテキスト を 折り返す", each as wide as the window.
    ///
    /// Steps:
    /// - 200 layouts alternating 120 and 90 wide (cached widths), and 200 cycling through 6 widths from 60 to 160 (more than 4:
    ///   every layout overwrites an entry). Run both 10 times to warm up, then each through PerformanceAssert.DoesNotAllocate.
    ///
    /// Expected:
    /// - 0 bytes in each loop.
    /// </remarks>
    [Fact]
    public void SteadyState_RelayoutOfTextLeaves_AllocatesNothing() =>
        ui.Invoke(() =>
        {
            using var context = new TextContext(new TestFontProvider(_font));
            var tree = new UiTree(context);
            NodeHandle root = tree.CreateNode(new LayoutStyle { Algorithm = LayoutAlgorithm.Flex.Column });
            for (int i = 0; i < 100; i++)
            {
                _ = AddLeaf(tree, root, "日本語のテキスト を 折り返す");
            }

            float[] cached = [120, 90];
            float[] cycling = [60, 80, 100, 120, 140, 160];

            // An entry grows its arrays the first time it holds a width of more lines than before, and which entry holds which width
            // depends on the order of the layouts before. So the warm-up runs both loops, as they are measured, 10 times: the most
            // the check before PerformanceAssert ran them to reach a run that allocated nothing.
            for (int warm = 0; warm < 10; warm++)
            {
                for (int i = 0; i < 200; i++)
                {
                    LayOut(tree, root, cached[i % 2]);
                }

                for (int i = 0; i < 200; i++)
                {
                    LayOut(tree, root, cycling[i % 6]);
                }
            }

            // The helper hands its unmeasured window negative indices; the shift keeps them in the widths' cycles.
            PerformanceAssert.DoesNotAllocate(200, i => LayOut(tree, root, cached[(i + 1_200) % 2]));
            PerformanceAssert.DoesNotAllocate(200, i => LayOut(tree, root, cycling[(i + 1_200) % 6]));
        });

    /// <summary>
    /// A frame that changes a text leaf's text, or its color, draws it and allocates nothing in steady state (row V1b): the text is
    /// shaped again into the leaf's arrays, and its lines drawn into the display list.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A column of 20 text leaves in window frames with a counting target and a test rasterizer's atlas; warmed up with the loop.
    ///
    /// Steps:
    /// - 200 frames: one leaf's text between "日本語のテキスト" and "テキストの日本語", and another leaf's color between two
    ///   colors; run each frame the tree asks for.
    ///
    /// Expected:
    /// - 0 bytes on this thread (in windows of 20 frames, with the methods compiled and the collections in each, after one window
    ///   unmeasured), and every frame presented.
    /// </remarks>
    [Fact]
    public void SteadyState_FramesThatChangeText_AllocateNothing() =>
        ui.Invoke(() =>
        {
            using var context = new TextContext(new TestFontProvider(_font));
            using var atlas = new GlyphAtlas(new TestRasterizer());
            var tree = new UiTree(context);
            NodeHandle root = tree.CreateNode(new LayoutStyle { Algorithm = LayoutAlgorithm.Flex.Column });
            var leaves = new NodeHandle[20];
            for (int i = 0; i < leaves.Length; i++)
            {
                leaves[i] = AddLeaf(tree, root, "日本語のテキスト");
            }

            var target = new CountingTarget();
            var scheduler = new OneFrameScheduler();
            var frames = new WindowFrames(tree, root, target, atlas, scheduler, Color.FromRgb(0xFFFFFF));
            frames.Resize(new Size(400, 600), 1);
            Color red = Color.FromRgb(0xCC3333);
            Color blue = Color.FromRgb(0x3366CC);

            int turn = 0;

            // Each leaf takes the other text and color each time its turn comes, so every frame changes something. The turn counts
            // the calls, not the index: a window that a background GC makes run again repeats its indices, and a frame that wrote
            // the values its index wrote before would change nothing and present nothing (Issue #528).
            void Frame()
            {
                int i = turn++;
                bool odd = (i / 20) % 2 == 1;
                tree.SetText(leaves[i % 20], odd ? "テキストの日本語" : "日本語のテキスト");
                tree.SetTextColor(leaves[(i + 7) % 20], odd ? red : blue);
                scheduler.Run();
            }

            for (int i = 0; i < 400; i++)
            {
                Frame();
            }

            int presents = target.Presents;

            // The runs it returns include the warm-up window and any window a background GC made it run again.
            int runs = PerformanceAssert.DoesNotAllocate(200, _ => Frame());
            Assert.Equal(presents + runs, target.Presents);
        });

    private static TextStyle Style(float lineHeight = float.NaN) => new(FontRequest.Default, Size, TestFont.Japanese, lineHeight);

    // A column as wide as the window, holding a row that holds the leaf: the leaf takes its max-content width, shrunk to the
    // window's.
    private static (UiTree Tree, NodeHandle Leaf) Build(TextContext context, string text, float width, float lineHeight = float.NaN)
    {
        var tree = new UiTree(context);
        NodeHandle root = tree.CreateNode(new LayoutStyle { Algorithm = LayoutAlgorithm.Flex.Column });
        NodeHandle row = tree.CreateNode(new LayoutStyle { Algorithm = LayoutAlgorithm.Flex.Row });
        tree.AppendChild(root, row);
        NodeHandle leaf = tree.CreateTextNode(new LayoutStyle(), Style(lineHeight));
        tree.SetText(leaf, text);
        tree.AppendChild(row, leaf);
        LayOut(tree, root, width);
        return (tree, leaf);
    }

    private static NodeHandle AddLeaf(UiTree tree, NodeHandle parent, string text)
    {
        NodeHandle leaf = tree.CreateTextNode(new LayoutStyle(), Style());
        tree.SetText(leaf, text);
        tree.AppendChild(parent, leaf);
        return leaf;
    }

    private static void LayOut(UiTree tree, NodeHandle root, float width)
    {
        Invalidation.NotifyWindowResized(tree, root, new Size(width, 1000));
        LayoutEngine.RunLayoutPass(tree);
    }

    private static bool IsLayoutClean(UiTree tree, NodeHandle node)
    {
        DirtyState dirty = tree.Nodes.GetRef(node).Dirty;
        return ((dirty.SelfDirty | dirty.SubtreeDirty) & DirtyFlags.Layout) == 0;
    }

    private static void AssertRect(float width, float height, Rect rect)
    {
        Assert.Equal(width, rect.Width, 3);
        Assert.Equal(height, rect.Height, 3);
    }

    private sealed class CountingTarget : IRenderTarget
    {
        public int Presents { get; private set; }

        public void Draw(DisplayList list, Color clearColor, GlyphAtlas atlas) { }

        public void Present() => Presents++;
    }

    private sealed class OneFrameScheduler : IFrameScheduler
    {
        private Action? _callback;

        public void RequestFrame(Action callback) => _callback = callback;

        public void CancelFrame() => _callback = null;

        public void Run()
        {
            Action? callback = _callback;
            _callback = null;
            callback?.Invoke();
        }
    }

    // Answers every character with one font, except the characters of `noFont`, which no font renders (the bundled font takes
    // them).
    private sealed class TestFontProvider(Font font, string noFont = "") : IFontProvider
    {
        public FontMatch ResolveFont(ReadOnlySpan<char> text, int start, TextLanguage language, in FontRequest request)
        {
            ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual((uint)start, (uint)text.Length, nameof(start));
            bool none = noFont.Contains(text[start], StringComparison.Ordinal);
            int end = start + 1;
            while (end < text.Length && noFont.Contains(text[end], StringComparison.Ordinal) == none)
            {
                end++;
            }

            return new FontMatch(none ? null : font, end - start, 1);
        }
    }
}
