using static XylemUI.Tests.LayoutTestTree;

namespace XylemUI.Tests;

// A change of a leaf's content that its parent may not need to see (design §2.4.1, row L5): the leaf is laid out again, and
// the parent only when a value it read changed. Every remark gives the arithmetic.
[Collection(UiThreadGroup.Name)]
public sealed class LayoutContentChangeTests(UiThreadFixture ui)
{
    /// <summary>
    /// A change of content that keeps every value the parent read stops at the leaf, though the parent read its content.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - root: column, window 300 x 100. Row R holds leaf A, whose content measures 40 x 10 and has content widths (as a text
    ///   leaf has), and leaf B, 30 x 10. Laid out: A at x 0, 40 x 10 (its max-content basis, which R read); B at x 40.
    ///
    /// Steps:
    /// - Change A's content to another of the same size, invalidate it (a text change), and run the pass, recording the nodes
    ///   whose algorithm or measure function runs.
    ///
    /// Expected:
    /// - Only A runs: R, B, and the root do not. The rectangles are the same, and the tree is clean.
    /// - A is in the paint updates, though no ancestor was marked (PR #109's re-review).
    /// </remarks>
    [Fact]
    public void ContentChange_ThatKeepsEveryValueTheParentRead_StopsAtTheLeaf() =>
        ui.Invoke(() =>
        {
            var tree = new UiTree();
            NodeHandle root = tree.CreateNode(Column());
            NodeHandle row = Box(tree, root, Row());
            var content = new Size(40, 10);
            NodeHandle a = tree.CreateNode(
                new LayoutStyle(),
                (_, _) => MeasureResult.Of(content.Width, content.Height),
                _ => new IntrinsicWidths(content.Width, content.Width, DependsOnHeight: false)
            );
            tree.AppendChild(row, a);
            NodeHandle b = Leaf(tree, row, 30, 10);
            LayOut(tree, root, 300, 100);
            Assert.True(Invalidation.ContentReadByParent(tree, in tree.Nodes.GetRef(a)));

            var ran = new List<NodeHandle>();
            tree.LayoutRunHook = ran.Add;
            content = new Size(40, 10);
            Invalidation.Invalidate(tree, a, ChangeKind.TextContent);
            LayoutEngine.RunLayoutPass(tree);

            Assert.NotEmpty(ran);
            Assert.All(ran, node => Assert.Equal(a, node));
            Assert.Equal(new Rect(0, 0, 40, 10), tree.GetRect(a));
            Assert.Equal(new Rect(40, 0, 30, 10), tree.GetRect(b));
            Assert.True(IsLayoutClean(tree, row));
            Assert.Empty(tree.LayoutRoots);
            Assert.Empty(tree.ContentSnapshots);
            Assert.Contains(a, tree.PaintUpdates);
        });

    /// <summary>
    /// A change of content that changes a value the parent read goes on to the parent in the same pass.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - The tree of <see cref="ContentChange_ThatKeepsEveryValueTheParentRead_StopsAtTheLeaf"/>, laid out.
    ///
    /// Steps:
    /// - Make A's content 60 x 10, invalidate it, and run the pass once.
    ///
    /// Expected:
    /// - R runs. A is 60 wide, B moved to x 60, and the tree is clean with no request left.
    /// </remarks>
    [Fact]
    public void ContentChange_ThatChangesAValueTheParentRead_LaysOutTheParent() =>
        ui.Invoke(() =>
        {
            var tree = new UiTree();
            NodeHandle root = tree.CreateNode(Column());
            NodeHandle row = Box(tree, root, Row());
            var content = new Size(40, 10);
            NodeHandle a = tree.CreateNode(
                new LayoutStyle(),
                (_, _) => MeasureResult.Of(content.Width, content.Height),
                _ => new IntrinsicWidths(content.Width, content.Width, DependsOnHeight: false)
            );
            tree.AppendChild(row, a);
            NodeHandle b = Leaf(tree, row, 30, 10);
            LayOut(tree, root, 300, 100);

            var ran = new List<NodeHandle>();
            tree.LayoutRunHook = ran.Add;
            content = new Size(60, 10);
            Invalidation.Invalidate(tree, a, ChangeKind.TextContent);
            LayoutEngine.RunLayoutPass(tree);

            Assert.Contains(row, ran);
            Assert.Equal(new Rect(0, 0, 60, 10), tree.GetRect(a));
            Assert.Equal(new Rect(60, 0, 30, 10), tree.GetRect(b));
            Assert.True(IsLayoutClean(tree, root));
            Assert.Empty(tree.LayoutRoots);
            Assert.Empty(tree.ContentSnapshots);
        });

    /// <summary>
    /// Two changes before one pass compare with the values of the last layout: content changed and changed back stops at the
    /// leaf.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - The tree of <see cref="ContentChange_ThatKeepsEveryValueTheParentRead_StopsAtTheLeaf"/>, laid out.
    ///
    /// Steps:
    /// - Make A's content 60 x 10 and invalidate it; make it 40 x 10 again and invalidate it; run the pass.
    ///
    /// Expected:
    /// - Only A runs, and the rectangles are those of the last layout.
    /// </remarks>
    [Fact]
    public void ContentChange_TwiceBeforeThePass_ComparesWithTheLastLayout() =>
        ui.Invoke(() =>
        {
            var tree = new UiTree();
            NodeHandle root = tree.CreateNode(Column());
            NodeHandle row = Box(tree, root, Row());
            var content = new Size(40, 10);
            NodeHandle a = tree.CreateNode(
                new LayoutStyle(),
                (_, _) => MeasureResult.Of(content.Width, content.Height),
                _ => new IntrinsicWidths(content.Width, content.Width, DependsOnHeight: false)
            );
            tree.AppendChild(row, a);
            NodeHandle b = Leaf(tree, row, 30, 10);
            LayOut(tree, root, 300, 100);

            var ran = new List<NodeHandle>();
            tree.LayoutRunHook = ran.Add;
            content = new Size(60, 10);
            Invalidation.Invalidate(tree, a, ChangeKind.TextContent);
            content = new Size(40, 10);
            Invalidation.Invalidate(tree, a, ChangeKind.Font);
            LayoutEngine.RunLayoutPass(tree);

            Assert.All(ran, node => Assert.Equal(a, node));
            Assert.Equal(new Rect(0, 0, 40, 10), tree.GetRect(a));
            Assert.Equal(new Rect(40, 0, 30, 10), tree.GetRect(b));
        });

    /// <summary>
    /// A leaf laid out by an ancestor's request before its own still goes on to its parent: the parent's layout read the new
    /// content, but a value the parent keeps was taken from the old one, and the grandparent reads it in the same pass (the
    /// random test found it with several changes before one pass, PR #112's review).
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - root: column, window 300 x 100. Row R, 100 wide, holds leaf X, 80 wide by its style, and block B, whose basis is its
    ///   max-content width; both may shrink to 0. B holds leaf L, whose content is 40 x 10 with content widths. Laid out: the
    ///   bases (80 + 40) overflow R by 20, which each item gives in proportion to its basis: X 66.67, B 33.33.
    ///
    /// Steps:
    /// - Make L's content 60 wide and invalidate it (row L5's path); make X 60 wide (SetStyle, MarkDirty's path to the root).
    ///   Run the pass. The root's request goes first: R takes B's basis from what it kept of L's old content (40), so B gets a
    ///   new width and lays L out with its new content before L's own request.
    ///
    /// Expected:
    /// - As a layout from the start: the bases (60 + 60) overflow by 20, 10 each: X 50 wide, B 50 wide at x 50.
    /// </remarks>
    [Fact]
    public void ContentChange_OfALeafAnAncestorLaidOutFirst_GoesOnToTheParent() =>
        ui.Invoke(() =>
        {
            var tree = new UiTree();
            NodeHandle root = tree.CreateNode(Column());
            NodeHandle row = Box(tree, root, Row() with { Width = Length.Px(100) });
            var noMinimum = Length.Px(0);
            NodeHandle x = Leaf(tree, row, 0, 10, new LayoutStyle { Width = Length.Px(80), MinWidth = noMinimum });
            NodeHandle block = Box(tree, row, new LayoutStyle { MinWidth = noMinimum });
            float width = 40;
            NodeHandle l = tree.CreateNode(
                new LayoutStyle(),
                (_, _) => MeasureResult.Of(width, 10),
                _ => new IntrinsicWidths(width, width, DependsOnHeight: false)
            );
            tree.AppendChild(block, l);
            LayOut(tree, root, 300, 100);
            Assert.Equal(33.33f, tree.GetRect(block).Width, 0.01f);

            width = 60;
            Invalidation.Invalidate(tree, l, ChangeKind.TextContent);
            tree.SetStyle(x, new LayoutStyle { Width = Length.Px(60), MinWidth = noMinimum });
            LayoutEngine.RunLayoutPass(tree);

            Assert.Equal(new Rect(0, 0, 50, 10), tree.GetRect(x));
            Assert.Equal(new Rect(50, 0, 50, 10), tree.GetRect(block));
            Assert.Empty(tree.ContentSnapshots);
        });

    /// <summary>
    /// A leaf whose measurements overflowed its cache while its parent kept its results cannot be compared: a change of its
    /// content goes on to the parent, even at the same size (PR #109's review).
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - root: column, window 300 x 100. Row R holds leaf A, grow 1, whose content measures 40 x 10 at any width it is not
    ///   given and has content widths 40, and leaf B, 30 x 10. Laid out: A 270 wide (300 - 30). A change of A at the same size
    ///   stops at A.
    ///
    /// Steps:
    /// - Resize the window to 310, 320, 330, and 340 wide. Each time R is laid out again in the same cache generation (it is
    ///   clean, and only its input changed), and measures A's height at A's new width: a key A has not kept, so A's 4 entries
    ///   overflow.
    /// - Change A's content to another of the same size, invalidate it, and run the pass.
    ///
    /// Expected:
    /// - R runs. A is 310 wide (340 - 30) and B at x 310.
    /// </remarks>
    [Fact]
    public void ContentChange_AfterAnOverwrittenMeasurement_GoesOnToTheParent() =>
        ui.Invoke(() =>
        {
            var tree = new UiTree();
            NodeHandle root = tree.CreateNode(Column());
            NodeHandle row = Box(tree, root, Row());
            NodeHandle a = tree.CreateNode(
                new LayoutStyle { FlexGrow = 1 },
                (known, _) => MeasureResult.Of(Maybe.Or(known.Width, 40), 10),
                _ => new IntrinsicWidths(40, 40, DependsOnHeight: false)
            );
            tree.AppendChild(row, a);
            NodeHandle b = Leaf(tree, row, 30, 10);
            LayOut(tree, root, 300, 100);
            var ran = new List<NodeHandle>();
            tree.LayoutRunHook = ran.Add;
            Invalidation.Invalidate(tree, a, ChangeKind.TextContent);
            LayoutEngine.RunLayoutPass(tree);
            Assert.DoesNotContain(row, ran);
            Assert.Equal(new Rect(0, 0, 270, 10), tree.GetRect(a));

            foreach (float width in new float[] { 310, 320, 330, 340 })
            {
                LayOut(tree, root, width, 100);
            }

            ran.Clear();
            Invalidation.Invalidate(tree, a, ChangeKind.TextContent);
            LayoutEngine.RunLayoutPass(tree);

            Assert.Contains(row, ran);
            Assert.Equal(new Rect(0, 0, 310, 10), tree.GetRect(a));
            Assert.Equal(new Rect(310, 0, 30, 10), tree.GetRect(b));
        });
}
