using XylemUI.Text;
using static XylemUI.Tests.LayoutTestTree;

namespace XylemUI.Tests;

// The frames of a window (row V1b, design §5.0, "The draw stage and the frame drive"): which frames draw and present, what asks for
// the next one, and the border widths the layout commits for the draw stage.
[Collection(UiThreadGroup.Name)]
public sealed class WindowFramesTests(UiThreadFixture ui)
{
    private static readonly Color Gray = Color.FromRgb(0x808080);
    private static readonly Color Red = Color.FromRgb(0xCC3333);

    /// <summary>
    /// A frame draws and presents only when it has a layout request or a paint update: a frame after nothing changed presents
    /// nothing, and a change of color presents once without a layout.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A tree of a root (200 x 100) with one box child (50 x 20, gray background); a counting target and scheduler.
    ///
    /// Steps:
    /// - Resize to 200 x 100 (a frame runs at once). Run 3 frames. Set the child's background red; run the frame the tree asked
    ///   for; run another.
    ///
    /// Expected:
    /// - The resize presents once. The 3 frames present nothing and the tree asks for no frame. The color asks for one frame,
    ///   which presents once and lays nothing out; the frame after it presents nothing.
    /// </remarks>
    [Fact]
    public void Frame_WithNothingChanged_PresentsNothing() =>
        ui.Invoke(() =>
        {
            (UiTree tree, NodeHandle root, NodeHandle box) = Boxes();
            var target = new CountingTarget();
            var scheduler = new CountingScheduler();
            using var atlas = new GlyphAtlas(new NoGlyphs());
            var frames = new WindowFrames(tree, root, target, atlas, scheduler, Gray);
            tree.SetBackground(box, new Paint(Gray));
            frames.Resize(new Size(200, 100), 1);
            Assert.Equal(1, target.Presents);
            scheduler.Requests = 0;

            for (int i = 0; i < 3; i++)
            {
                frames.RunFrame();
            }

            Assert.Equal(1, target.Presents);
            Assert.Equal(0, scheduler.Requests);

            int layouts = 0;
            tree.LayoutRunHook = _ => layouts++;
            tree.SetBackground(box, new Paint(Red));
            Assert.Equal(1, scheduler.Requests);
            scheduler.Run();
            Assert.Equal(2, target.Presents);
            Assert.Equal(0, layouts);
            frames.RunFrame();
            Assert.Equal(2, target.Presents);
        });

    /// <summary>
    /// A change made while a frame is drawn asks for the next frame, and that frame draws it: the paint updates are taken before
    /// the display list is built.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - As above; the root's draw function sets the child's background red the first time it runs after a flag is set.
    ///
    /// Steps:
    /// - Resize. Set the flag; set the root's background (a frame is asked for); run it.
    ///
    /// Expected:
    /// - That frame presents, and the tree asks for one more frame; it presents too, and the frame after it presents nothing.
    /// </remarks>
    [Fact]
    public void ChangeWhileDrawing_AsksForTheNextFrame() =>
        ui.Invoke(() =>
        {
            (UiTree tree, NodeHandle root, NodeHandle box) = Boxes();
            var target = new CountingTarget();
            var scheduler = new CountingScheduler();
            using var atlas = new GlyphAtlas(new NoGlyphs());
            var frames = new WindowFrames(tree, root, target, atlas, scheduler, Gray);
            bool change = false;
            tree.SetDraw(
                root,
                (_, _) =>
                {
                    if (change)
                    {
                        change = false;
                        tree.SetBackground(box, new Paint(Red));
                    }
                }
            );
            frames.Resize(new Size(200, 100), 1);
            scheduler.Requests = 0;

            change = true;
            tree.SetBackground(root, new Paint(Gray));
            scheduler.Run();
            Assert.Equal(2, target.Presents);
            Assert.Equal(2, scheduler.Requests);
            scheduler.Run();
            Assert.Equal(3, target.Presents);
            frames.RunFrame();
            Assert.Equal(3, target.Presents);
        });

    /// <summary>
    /// A frame allocates nothing in steady state: one that changes a color, one that resizes the window, and one with nothing to do.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - 100 box children of a column root, each with a background, corner radii, a border paint, and a border of 1 px; warmed up
    ///   with the loops below.
    ///
    /// Steps:
    /// - 200 frames each: one child's background between two colors; the window between two sizes; nothing changed.
    ///
    /// Expected:
    /// - 0 bytes on this thread in each loop (in windows of 20 frames, with the methods compiled and the collections in each).
    /// </remarks>
    [Fact]
    public void SteadyState_Frames_AllocateNothing() =>
        ui.Invoke(() =>
        {
            var tree = new UiTree();
            NodeHandle root = tree.CreateNode(Column());
            var children = new NodeHandle[100];
            for (int i = 0; i < children.Length; i++)
            {
                children[i] = Box(tree, root, Fixed(40, 4) with { Border = EdgeInsets.All(1) });
                tree.SetBackground(children[i], new Paint(Gray));
                tree.SetCornerRadii(children[i], new CornerRadii(2));
                tree.SetBorderPaint(children[i], new Paint(Red));
            }

            var target = new CountingTarget();
            var scheduler = new CountingScheduler();
            using var atlas = new GlyphAtlas(new NoGlyphs());
            var frames = new WindowFrames(tree, root, target, atlas, scheduler, Gray);
            frames.Resize(new Size(200, 500), 1);

            // The allocation check hands its unmeasured window negative indices; the shift keeps them in range.
            void Colors(int index)
            {
                int i = index + 1_000;
                tree.SetBackground(children[i % 100], new Paint((i / 100) % 2 == 0 ? Red : Gray));
                scheduler.Run();
            }

            void Sizes(int index) => frames.Resize(new Size((index + 1_000) % 2 == 0 ? 200 : 180, 500), 1);

            void Nothing(int i) => frames.RunFrame();

            foreach (Action<int> loop in new Action<int>[] { Colors, Sizes, Nothing })
            {
                for (int i = 0; i < 200; i++)
                {
                    loop(i);
                }
            }

            SteadyStateAllocation.AssertNone(200, Colors);
            SteadyStateAllocation.AssertNone(200, Sizes);
            SteadyStateAllocation.AssertNone(200, Nothing);
        });

    /// <summary>
    /// A leaf whose measurement keeps throwing does not keep the frames going: the request the layout pass puts back asks for no
    /// frame, and is tried by the next frame something else asks for. A resize asks for no frame beyond the one it runs.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A root with a box child and a leaf whose measure function throws while a flag is set; a handler that records reports; a
    ///   counting target and scheduler. Laid out once (Resize) with the flag clear.
    ///
    /// Steps:
    /// - Resize again once the frames ran; check the scheduler's requests after it. Set the flag; invalidate the leaf; run the frame it asked for. Run the
    ///   scheduler again. Then set the box's background and run the frame it asked for.
    ///
    /// Expected:
    /// - The second resize asked for no frame beyond the one it ran. The failing frame was reported once and asked for no further frame (the second run does
    ///   nothing). The background asks for one frame, which presents and tries the leaf again (a second report is counted, not
    ///   reported).
    /// </remarks>
    [Fact]
    public void LeafThatKeepsThrowing_DoesNotKeepTheFramesGoing() =>
        ui.Invoke(() =>
        {
            (UiTree tree, NodeHandle root, NodeHandle box) = Boxes();
            bool fail = false;
            NodeHandle leaf = tree.CreateNode(
                new LayoutStyle(),
                (_, _) => fail ? throw new InvalidOperationException("Thrown from a measure function on purpose.") : MeasureResult.Of(10, 10)
            );
            tree.AppendChild(root, leaf);
            var target = new CountingTarget();
            var scheduler = new CountingScheduler();
            using var atlas = new GlyphAtlas(new NoGlyphs());
            var frames = new WindowFrames(tree, root, target, atlas, scheduler, Gray);
            var reported = new List<Exception>();
            Action<Exception> previous = ReactiveRuntime.UnhandledExceptionHandler;
            ReactiveRuntime.UnhandledExceptionHandler = reported.Add;
            try
            {
                frames.Resize(new Size(200, 100), 1);
                scheduler.Run();
                scheduler.Requests = 0;
                frames.Resize(new Size(180, 100), 1);
                Assert.Equal(0, scheduler.Requests);

                fail = true;
                Invalidation.Invalidate(tree, leaf, ChangeKind.Size);
                Assert.Equal(1, scheduler.Requests);
                scheduler.Run();
                Assert.Single(reported);
                Assert.Equal(1, scheduler.Requests);
                int presents = target.Presents;
                scheduler.Run();
                Assert.Equal(presents, target.Presents);

                tree.SetBackground(box, new Paint(Red));
                Assert.Equal(2, scheduler.Requests);
                scheduler.Run();
                Assert.Equal(presents + 1, target.Presents);
                Assert.Single(reported);
                Assert.Equal(1, tree.SuppressedLayoutExceptions);
                Assert.Equal(2, scheduler.Requests);
            }
            finally
            {
                ReactiveRuntime.UnhandledExceptionHandler = previous;
            }
        });

    /// <summary>
    /// A new way of drawing text (the window's ClearType or smoothing setting changed) asks for a frame, which draws the tree
    /// again; the same value asks for none.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - As in the first test, laid out once.
    ///
    /// Steps:
    /// - Set the frames' text rendering to the value it has; then to one with bilevel glyphs; run the frame asked for.
    ///
    /// Expected:
    /// - The same value asks for no frame; the new one asks for one, which presents.
    /// </remarks>
    [Fact]
    public void NewTextRendering_DrawsTheTreeAgain() =>
        ui.Invoke(() =>
        {
            (UiTree tree, NodeHandle root, NodeHandle box) = Boxes();
            tree.SetBackground(box, new Paint(Gray));
            var target = new CountingTarget();
            var scheduler = new CountingScheduler();
            using var atlas = new GlyphAtlas(new NoGlyphs());
            var frames = new WindowFrames(tree, root, target, atlas, scheduler, Gray);
            frames.Resize(new Size(200, 100), 1);
            int presents = target.Presents;

            frames.TextRendering = frames.TextRendering;
            Assert.Equal(0, scheduler.Requests);
            frames.TextRendering = frames.TextRendering with { IsAliased = true };
            Assert.Equal(1, scheduler.Requests);
            scheduler.Run();
            Assert.Equal(presents + 1, target.Presents);
        });

    /// <summary>
    /// The border widths the layout commits are the ones it placed the content inside, wherever it resolves a percentage against
    /// another basis: the root, a block's child, a flex item, an absolutely positioned child, and a block whose containing block
    /// has no width (the root in a max-content viewport).
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - Each node has a border of 10 % and a child of 10 x 10 at the start of its content box. The block tree is laid out in a
    ///   window of 300 x 200; the max-content root in a viewport of max-content.
    ///
    /// Steps:
    /// - Lay out; read each node's committed border and its child's position.
    ///
    /// Expected:
    /// - Each child sits at the node's committed left and top border (padding is 0), and the border is not 0.
    /// </remarks>
    [Fact]
    public void CommittedBorder_IsWhereTheLayoutPlacedTheContent() =>
        ui.Invoke(() =>
        {
            var percent = EdgeInsets<LengthPercentage>.All(LengthPercentage.Percent(0.1f));
            var tree = new UiTree();
            NodeHandle root = tree.CreateNode(new LayoutStyle { Border = percent });
            NodeHandle block = Box(tree, root, new LayoutStyle { Width = Length.Px(100), Border = percent });
            NodeHandle row = Box(tree, root, new LayoutStyle { Algorithm = LayoutAlgorithm.Flex.Row, Width = Length.Px(150) });
            NodeHandle item = Box(tree, row, new LayoutStyle { Width = Length.Px(60), Border = percent });
            NodeHandle absolute = Box(
                tree,
                root,
                new LayoutStyle
                {
                    Position = Position.Absolute,
                    Width = Length.Px(80),
                    Border = percent,
                }
            );
            NodeHandle[] nodes = [root, block, item, absolute];
            foreach (NodeHandle node in nodes)
            {
                _ = Leaf(tree, node, 10, 10);
            }

            LayOut(tree, root, 300, 200);

            var free = new UiTree();
            NodeHandle undecided = free.CreateNode(new LayoutStyle { Width = Length.Px(120), Border = percent });
            _ = Leaf(free, undecided, 10, 10);
            Invalidation.NotifyViewport(free, undecided, AvailableSize.MaxContent);
            LayoutEngine.RunLayoutPass(free);

            foreach ((UiTree t, NodeHandle node) in nodes.Select(n => (tree, n)).Append((free, undecided)))
            {
                ref UiNode n = ref t.Nodes.GetRef(node);
                Rect child = t.GetRect(n.FirstChild);
                Assert.True(n.Layout.Border.Left > 0);
                Assert.Equal(n.Layout.Border.Left, child.X);
                Assert.Equal(n.Layout.Border.Top, child.Y);
            }
        });

    private static (UiTree Tree, NodeHandle Root, NodeHandle Box) Boxes()
    {
        var tree = new UiTree();
        NodeHandle root = tree.CreateNode(new LayoutStyle());
        NodeHandle box = Box(tree, root, Fixed(50, 20));
        return (tree, root, box);
    }

    private sealed class CountingTarget : IRenderTarget
    {
        public int Presents { get; private set; }

        public void Draw(DisplayList list, Color clearColor, GlyphAtlas atlas) { }

        public void Present() => Presents++;
    }

    // Holds the one callback a frame scheduler holds, and runs it when asked.
    private sealed class CountingScheduler : IFrameScheduler
    {
        private Action? _callback;

        public int Requests { get; set; }

        public void RequestFrame(Action callback)
        {
            Requests++;
            _callback = callback;
        }

        public void CancelFrame() => _callback = null;

        public void Run()
        {
            Action? callback = _callback;
            _callback = null;
            callback?.Invoke();
        }
    }

    // The trees here draw no text, so no glyph is asked for.
    private sealed class NoGlyphs : IGlyphRasterizer
    {
        public GlyphBox Measure(in GlyphKey glyph) => throw new InvalidOperationException("No glyph is drawn in these tests.");

        public void Rasterize(in GlyphKey glyph, Span<byte> destination, int stride) =>
            throw new InvalidOperationException("No glyph is drawn in these tests.");
    }
}
