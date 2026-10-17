using static XylemUI.Tests.LayoutTestTree;

namespace XylemUI.Tests;

// What Chrome's results cannot check: the incremental layout of XylemUI (design §2.4.1, §3.2) — the mark of a child whose
// content the parent read, the caches, and the parent size in the cache key. Every remark gives the arithmetic.
[Collection(UiThreadGroup.Name)]
public sealed class LayoutIncrementalTests(UiThreadFixture ui)
{
    /// <summary>
    /// A flex item whose size the parent decided still passes a change of its content to the parent when the parent read the
    /// content for the item's automatic minimum size: the siblings move.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - root: row, window 300 x 50. Three items, basis 0, grow 1, each a leaf whose min-content width is a variable (first
    ///   50, the others 0) and whose max-content width is 0. Laid out: each item 100 wide.
    ///
    /// Steps:
    /// - Set the first item's min-content width to 150 and invalidate it (a content change); run the pass.
    ///
    /// Expected:
    /// - The first item's automatic minimum is 150: it is 150 wide, and the free space left, 150, goes to the others, 75 each
    ///   at x 150 and 225.
    /// </remarks>
    [Fact]
    public void ReadContent_OfAnExactItem_ReachesTheParent() =>
        ui.Invoke(() =>
        {
            var tree = new UiTree();
            NodeHandle root = tree.CreateNode(Row());
            float minContent = 50;
            NodeHandle first = tree.CreateNode(Flexible(), (_, available) => MeasureResult.Of(MinContentOr(available, minContent), 10));
            tree.AppendChild(root, first);
            NodeHandle second = Leaf(tree, root, 0, 10, Flexible());
            NodeHandle third = Leaf(tree, root, 0, 10, Flexible());
            LayOut(tree, root, 300, 50);
            Assert.Equal(100, tree.GetRect(first).Width);
            Assert.True(tree.Nodes.GetRef(first).Layout.CachedComputedBoundary);

            minContent = 150;
            Invalidation.Invalidate(tree, first, ChangeKind.TextContent);
            LayoutEngine.RunLayoutPass(tree);

            Assert.Equal(new Rect(0, 0, 150, 50), tree.GetRect(first));
            Assert.Equal(new Rect(150, 0, 75, 50), tree.GetRect(second));
            Assert.Equal(new Rect(225, 0, 75, 50), tree.GetRect(third));
        });

    /// <summary>
    /// A container sized by its content, laid out with both sides decided by its parent, passes a change inside to the parent,
    /// which measured it.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - root: row, window 400 x 100. column C (auto basis, so measured) holds leaf L, 20 x 10 at first; sibling R, grow 1.
    ///   Laid out: C 20 wide, R at x 20.
    ///
    /// Steps:
    /// - Make L 60 wide and invalidate it; run the pass.
    ///
    /// Expected:
    /// - C is 60 wide (its new content) and R moved to x 60, 340 wide.
    /// </remarks>
    [Fact]
    public void ReadContent_OfAContentSizedContainer_ReachesTheParent() =>
        ui.Invoke(() =>
        {
            var tree = new UiTree();
            NodeHandle root = tree.CreateNode(Row());
            NodeHandle column = Box(tree, root, Column());
            float width = 20;
            NodeHandle leaf = tree.CreateNode(new LayoutStyle(), (_, _) => MeasureResult.Of(width, 10));
            tree.AppendChild(column, leaf);
            NodeHandle rest = Box(tree, root, new LayoutStyle { FlexGrow = 1 });
            LayOut(tree, root, 400, 100);
            Assert.Equal(20, tree.GetRect(column).Width);

            width = 60;
            Invalidation.Invalidate(tree, leaf, ChangeKind.TextContent);
            LayoutEngine.RunLayoutPass(tree);

            Assert.Equal(60, tree.GetRect(column).Width);
            Assert.Equal(new Rect(60, 0, 340, 100), tree.GetRect(rest));
        });

    /// <summary>
    /// When a parent's measurement comes from its cache, the parent does not read its children again, and the marks of the
    /// earlier read still hold: a later change inside a child reaches the parent (design §2.4.1, Issue #76).
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - root: column, window 300 x 300 → P (row, auto) → leaf C, 10 wide and h high (h = 10 at first); root → sibling S,
    ///   a leaf 10 x 10. Laid out. A hook records each run.
    ///
    /// Steps:
    /// - Change S's style to height 20 and run the pass: root lays out again, and P's measurement hits P's cache.
    /// - Set h to 40, invalidate C, and run the pass.
    ///
    /// Expected:
    /// - In the first pass P did not run.
    /// - After the second, P is 40 high and S moved to y 40.
    /// </remarks>
    [Fact]
    public void ReadContent_AfterTheParentsCacheHit_StillReachesTheParent() =>
        ui.Invoke(() =>
        {
            var tree = new UiTree();
            NodeHandle root = tree.CreateNode(Column());
            NodeHandle parent = Box(tree, root, Row());
            float height = 10;
            NodeHandle child = tree.CreateNode(new LayoutStyle(), (_, _) => MeasureResult.Of(10, height));
            tree.AppendChild(parent, child);
            NodeHandle sibling = Leaf(tree, root, 10, 10);
            LayOut(tree, root, 300, 300);
            var ran = new List<NodeHandle>();
            tree.LayoutRunHook = ran.Add;

            tree.SetStyle(sibling, new LayoutStyle { Height = Length.Px(20) });
            LayoutEngine.RunLayoutPass(tree);
            Assert.DoesNotContain(parent, ran);

            height = 40;
            Invalidation.Invalidate(tree, child, ChangeKind.TextContent);
            LayoutEngine.RunLayoutPass(tree);

            Assert.Equal(40, tree.GetRect(parent).Height);
            Assert.Equal(40, tree.GetRect(sibling).Y);
        });

    /// <summary>
    /// A column whose height is limited by its maximum shrinks its children into it (the S6b defect, design §3.2: the column
    /// stopped at the limit, but its children overflowed it).
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - root: block, window 300 x 200 → column, max height 100 → two leaves 10 x 80, minimum height 0 (so that their
    ///   automatic minimum, their content's 80, does not stop the shrink).
    ///
    /// Steps:
    /// - Lay out.
    ///
    /// Expected:
    /// - column: 300 x 100. Overflow 160 − 100 = 60, equal weights: each leaf loses 30, 50 high, at y 0 and 50.
    /// </remarks>
    [Fact]
    public void Column_LimitedByItsMaximum_ShrinksItsChildren() =>
        ui.Invoke(() =>
        {
            var tree = new UiTree();
            NodeHandle root = tree.CreateNode(new LayoutStyle());
            NodeHandle column = Box(tree, root, Column() with { MaxHeight = LengthPercentageAuto.Px(100) });
            var noMinimum = new LayoutStyle { MinHeight = LengthPercentageAuto.Px(0) };
            NodeHandle first = Leaf(tree, column, 10, 80, noMinimum);
            NodeHandle second = Leaf(tree, column, 10, 80, noMinimum);

            LayOut(tree, root, 300, 200);

            Assert.Equal(new Rect(0, 0, 300, 100), tree.GetRect(column));
            Assert.Equal(new Rect(0, 0, 300, 50), tree.GetRect(first));
            Assert.Equal(new Rect(0, 50, 300, 50), tree.GetRect(second));
        });

    /// <summary>
    /// A node whose padding is a percentage is laid out again when its parent's width changes, although its own input did not
    /// change otherwise: the parent width is in its cache key.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - root: block, window 400 x 300 → B, block, width 100, padding 10 % on every side → leaf. Laid out: the leaf at x 40.
    ///
    /// Steps:
    /// - Resize the window to 200 x 300; run the pass.
    ///
    /// Expected:
    /// - The padding is 10 % of 200 = 20: the leaf is at (20, 20).
    /// </remarks>
    [Fact]
    public void PercentOfTheParent_IsInTheCacheKey() =>
        ui.Invoke(() =>
        {
            var tree = new UiTree();
            NodeHandle root = tree.CreateNode(new LayoutStyle());
            NodeHandle box = Box(
                tree,
                root,
                new LayoutStyle { Width = Length.Px(100), Padding = EdgeInsets<LengthPercentage>.All(LengthPercentage.Percent(0.1f)) }
            );
            NodeHandle leaf = Leaf(tree, box, 10, 10);
            LayOut(tree, root, 400, 300);
            Assert.Equal(40, tree.GetRect(leaf).X);

            LayOut(tree, root, 200, 300);

            Assert.Equal((20f, 20f), (tree.GetRect(leaf).X, tree.GetRect(leaf).Y));
        });

    /// <summary>
    /// A node that uses no percentage keeps its cached layout when only its parent's size changes.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - root: block, window 400 x 300 → B, block, width 100, padding 10 px → leaf. Laid out. A hook records each run.
    ///
    /// Steps:
    /// - Resize the window to 200 x 300; run the pass.
    ///
    /// Expected:
    /// - root ran; B and the leaf did not (B's input is the same: known width 100, and no percentage of the parent).
    /// </remarks>
    [Fact]
    public void NoPercentOfTheParent_KeepsTheCacheOnAParentResize() =>
        ui.Invoke(() =>
        {
            var tree = new UiTree();
            NodeHandle root = tree.CreateNode(new LayoutStyle());
            NodeHandle box = Box(tree, root, new LayoutStyle { Width = Length.Px(100), Padding = EdgeInsets.All(10) });
            NodeHandle leaf = Leaf(tree, box, 10, 10);
            LayOut(tree, root, 400, 300);
            var ran = new List<NodeHandle>();
            tree.LayoutRunHook = ran.Add;

            LayOut(tree, root, 200, 300);

            Assert.Contains(root, ran);
            Assert.DoesNotContain(box, ran);
            Assert.DoesNotContain(leaf, ran);
        });

    /// <summary>
    /// The mark of a read lapses once the parent lays out again without reading the content: a change inside then stops at
    /// the child, a computed boundary again.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - The tree of <see cref="ReadContent_OfAnExactItem_ReachesTheParent"/>, laid out: the row read the first item's
    ///   min-content width for its automatic minimum.
    ///
    /// Steps:
    /// - Give the first item a minimum width of 0 (the row now needs no content of it: its basis is 0 and its minimum is set);
    ///   run the pass. Then invalidate the first item's content.
    ///
    /// Expected:
    /// - The only layout root is the first item; the row is not marked.
    /// </remarks>
    [Fact]
    public void ReadMark_Lapses_WhenTheParentNoLongerReads() =>
        ui.Invoke(() =>
        {
            var tree = new UiTree();
            NodeHandle root = tree.CreateNode(Row());
            NodeHandle first = tree.CreateNode(Flexible(), (_, available) => MeasureResult.Of(MinContentOr(available, 50), 10));
            tree.AppendChild(root, first);
            _ = Leaf(tree, root, 0, 10, Flexible());
            LayOut(tree, root, 300, 50);

            tree.SetStyle(first, Flexible() with { MinWidth = LengthPercentageAuto.Px(0) });
            LayoutEngine.RunLayoutPass(tree);
            Invalidation.Invalidate(tree, first, ChangeKind.TextContent);

            Assert.Equal(first, Assert.Single(tree.LayoutRoots).Node);
            Assert.False(tree.Nodes.GetRef(root).Dirty.SubtreeDirty.HasFlag(DirtyFlags.Layout));
        });

    /// <summary>
    /// Marking a node dirty drops the measurements it cached for other inputs: a later input that hit one of them would see
    /// the content before the change.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - root: row → column C (auto basis, measured) → leaf L, w wide (w = 20) and 10 high. Laid out at 400 x 100 and then
    ///   at 400 x 200, so C holds measurements for a cross size of 100 and of 200.
    ///
    /// Steps:
    /// - Set w to 60 and invalidate L; lay out at 400 x 200. Then lay out at 400 x 100 again.
    ///
    /// Expected:
    /// - C is 60 wide at the end: its measurement for the cross size 100 was taken before the change and was dropped.
    /// </remarks>
    [Fact]
    public void DirtyNode_DropsItsMeasurementsForOtherInputs() =>
        ui.Invoke(() =>
        {
            var tree = new UiTree();
            NodeHandle root = tree.CreateNode(Row());
            NodeHandle column = Box(tree, root, Column());
            float width = 20;
            NodeHandle leaf = tree.CreateNode(new LayoutStyle(), (_, _) => MeasureResult.Of(width, 10));
            tree.AppendChild(column, leaf);
            LayOut(tree, root, 400, 100);
            LayOut(tree, root, 400, 200);

            width = 60;
            Invalidation.Invalidate(tree, leaf, ChangeKind.TextContent);
            LayOut(tree, root, 400, 200);
            LayOut(tree, root, 400, 100);

            Assert.Equal(60, tree.GetRect(column).Width);
        });

    /// <summary>
    /// A measurement taken for one axis serves only that axis: its other axis means nothing. After the parent's direction
    /// changes, the same input asks for the other axis, and the node is measured again.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - root: row, laid out in a max-content viewport → block B (minimum height 0, so that no automatic minimum measures it
    ///   again) → leaf 10 x 30. The row measured B's width only (B's height in that result is 0, the block's early return for
    ///   a width).
    ///
    /// Steps:
    /// - Make the root a column; run the pass. The column measures B's height with the same input.
    ///
    /// Expected:
    /// - B is 30 high, and so is the root.
    /// </remarks>
    [Fact]
    public void MeasurementOfOneAxis_DoesNotServeTheOther() =>
        ui.Invoke(() =>
        {
            var tree = new UiTree();
            NodeHandle root = tree.CreateNode(Row());
            NodeHandle block = Box(tree, root, new LayoutStyle { MinHeight = LengthPercentageAuto.Px(0) });
            _ = Leaf(tree, block, 10, 30);
            Invalidation.NotifyViewport(tree, root, AvailableSize.MaxContent);
            LayoutEngine.RunLayoutPass(tree);

            tree.SetStyle(root, Column());
            LayoutEngine.RunLayoutPass(tree);

            Assert.Equal(30, tree.GetRect(block).Height);
            Assert.Equal(30, tree.GetRect(root).Height);
        });

    /// <summary>
    /// A change of an item's content that moves where a wrapping row breaks reaches the row: the siblings move to other lines.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - root: row, wrap, window 100 x 100. Three leaves, each w wide (w = 40 for all at first) and 10 high, minimum width 0,
    ///   no shrink. Laid out: lines [A, B] and [C]; B at (40, 0), C at (0, 10).
    ///
    /// Steps:
    /// - Make A 70 wide and invalidate its content; run the pass.
    ///
    /// Expected:
    /// - 70 + 40 > 100, so A is alone on the first line: B at (0, 10), C at (40, 10).
    /// </remarks>
    [Fact]
    public void ReadContent_ThatMovesALineBreak_ReachesTheSiblings() =>
        ui.Invoke(() =>
        {
            var tree = new UiTree();
            NodeHandle root = tree.CreateNode(
                new LayoutStyle
                {
                    Algorithm = new LayoutAlgorithm.Flex(
                        new FlexProps { Wrap = FlexWrap.Wrap, AlignContent = new AlignContent(AlignContentKeyword.FlexStart) }
                    ),
                }
            );
            var item = new LayoutStyle { MinWidth = LengthPercentageAuto.Px(0), FlexShrink = 0 };
            float width = 40;
            NodeHandle a = tree.CreateNode(item, (_, _) => MeasureResult.Of(width, 10));
            tree.AppendChild(root, a);
            NodeHandle b = Leaf(tree, root, 40, 10, item);
            NodeHandle c = Leaf(tree, root, 40, 10, item);
            LayOut(tree, root, 100, 100);
            Assert.Equal((40f, 0f), (tree.GetRect(b).X, tree.GetRect(b).Y));
            Assert.Equal((0f, 10f), (tree.GetRect(c).X, tree.GetRect(c).Y));

            width = 70;
            Invalidation.Invalidate(tree, a, ChangeKind.TextContent);
            LayoutEngine.RunLayoutPass(tree);

            Assert.Equal((0f, 10f), (tree.GetRect(b).X, tree.GetRect(b).Y));
            Assert.Equal((40f, 10f), (tree.GetRect(c).X, tree.GetRect(c).Y));
        });

    /// <summary>
    /// A change of an item's first baseline reaches the row that aligns its items by baseline: the sibling moves.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - root: row, `align-items: baseline`, window 200 x 100. A: a leaf 20 wide and 30 high whose first baseline is b (b = 10
    ///   at first). B: a leaf 20 x 20, no baseline (its border box's bottom, 20, stands for it). Laid out: the largest baseline
    ///   is 20, so A is at y 10 and B at y 0.
    ///
    /// Steps:
    /// - Set b to 25 and invalidate A's content; run the pass.
    ///
    /// Expected:
    /// - The largest baseline is 25: A at y 0, B at y 25 − 20 = 5.
    /// </remarks>
    [Fact]
    public void ReadBaseline_OfAnItem_ReachesTheSiblings() =>
        ui.Invoke(() =>
        {
            var tree = new UiTree();
            NodeHandle root = tree.CreateNode(
                new LayoutStyle { Algorithm = new LayoutAlgorithm.Flex(new FlexProps { AlignItems = new AlignItems(AlignItemsKeyword.Baseline) }) }
            );
            float baseline = 10;
            NodeHandle a = tree.CreateNode(new LayoutStyle(), (_, _) => new MeasureResult(new Size(20, 30), baseline));
            tree.AppendChild(root, a);
            NodeHandle b = Leaf(tree, root, 20, 20);
            LayOut(tree, root, 200, 100);
            Assert.Equal((10f, 0f), (tree.GetRect(a).Y, tree.GetRect(b).Y));

            baseline = 25;
            Invalidation.Invalidate(tree, a, ChangeKind.TextContent);
            LayoutEngine.RunLayoutPass(tree);

            Assert.Equal((0f, 5f), (tree.GetRect(a).Y, tree.GetRect(b).Y));
        });

    private static LayoutStyle Flexible() => new() { FlexBasis = Length.Px(0), FlexGrow = 1 };

    private static float MinContentOr(AvailableSize available, float minContent) =>
        available.Width.Kind == AvailableSpaceKind.MinContent ? minContent : 0;
}
