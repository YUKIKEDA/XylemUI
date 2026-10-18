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

    /// <summary>
    /// A row that reads the baseline of an item of fixed size marks the item, though no measuring run reads it: a change of the
    /// baseline inside the item reaches the row, and the sibling moves (design §2.4.1, PR #91's review).
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - root: row, `align-items: baseline`, window 300 x 100. A: a block 50 x 40 (fixed, so a structural boundary; minimum
    ///   width 0 and shrink 0, so that no automatic minimum measures it) → leaf L, 10 x 10 fixed, first baseline b (b = 8 at
    ///   first). B: a leaf 50 x 20 with the same minimum and shrink, first baseline 8. Laid out: both baselines are 8, so A and
    ///   B are at y 0.
    ///
    /// Steps:
    /// - Set b to 18 and invalidate L's content; run the pass.
    ///
    /// Expected:
    /// - A's baseline is 18: B at y 18 − 8 = 10, the same as a layout of the tree from the start.
    /// </remarks>
    [Fact]
    public void ReadBaseline_OfAnItemOfFixedSize_ReachesTheSiblings() =>
        ui.Invoke(() =>
        {
            var tree = new UiTree();
            NodeHandle root = tree.CreateNode(BaselineRow());
            float baseline = 8;
            NodeHandle a = Box(tree, root, FixedItem(50, 40));
            NodeHandle leaf = tree.CreateNode(FixedItem(10, 10), (_, _) => new MeasureResult(new Size(10, 10), baseline));
            tree.AppendChild(a, leaf);
            NodeHandle b = tree.CreateNode(FixedItem(50, 20), static (_, _) => new MeasureResult(new Size(50, 20), 8));
            tree.AppendChild(root, b);
            LayOut(tree, root, 300, 100);
            Assert.Equal((0f, 0f), (tree.GetRect(a).Y, tree.GetRect(b).Y));

            baseline = 18;
            Invalidation.Invalidate(tree, leaf, ChangeKind.TextContent);
            LayoutEngine.RunLayoutPass(tree);

            Assert.Equal((0f, 10f), (tree.GetRect(a).Y, tree.GetRect(b).Y));
        });

    /// <summary>
    /// A column takes its first item's baseline and marks the item; the row that reads the column's baseline marks the column:
    /// a change of the baseline of the column's first item reaches the row (design §2.4.1, PR #91's review).
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - root: row, `align-items: baseline`, window 300 x 100. C: a column 50 x 40 (fixed, minimum width 0, shrink 0) → leaf L,
    ///   50 x 10 fixed, first baseline b (b = 8 at first). B: a leaf 50 x 20 with the same minimum and shrink, first baseline 8.
    ///   Laid out: C's baseline is L's, 8, so C and B are at y 0.
    ///
    /// Steps:
    /// - Set b to 18 and invalidate L's content; run the pass.
    ///
    /// Expected:
    /// - C's baseline is 18: B at y 10.
    /// </remarks>
    [Fact]
    public void ReadBaseline_OfAColumnsFirstItem_ReachesTheRow() =>
        ui.Invoke(() =>
        {
            var tree = new UiTree();
            NodeHandle root = tree.CreateNode(BaselineRow());
            float baseline = 8;
            NodeHandle column = Box(
                tree,
                root,
                FixedItem(50, 40) with
                {
                    Algorithm = new LayoutAlgorithm.Flex(new FlexProps { Direction = FlexDirection.Column }),
                }
            );
            NodeHandle leaf = tree.CreateNode(FixedItem(50, 10), (_, _) => new MeasureResult(new Size(50, 10), baseline));
            tree.AppendChild(column, leaf);
            NodeHandle b = tree.CreateNode(FixedItem(50, 20), static (_, _) => new MeasureResult(new Size(50, 20), 8));
            tree.AppendChild(root, b);
            LayOut(tree, root, 300, 100);
            Assert.Equal((0f, 0f), (tree.GetRect(column).Y, tree.GetRect(b).Y));

            baseline = 18;
            Invalidation.Invalidate(tree, leaf, ChangeKind.TextContent);
            LayoutEngine.RunLayoutPass(tree);

            Assert.Equal((0f, 10f), (tree.GetRect(column).Y, tree.GetRect(b).Y));
        });

    /// <summary>
    /// When the row that read a baseline is laid out from its cache, it reads nothing again, and the mark of the earlier read
    /// still holds: a later change of the baseline reaches the row (design §2.4.1, as for a measurement).
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - root: column, window 300 x 300 → P: row, `align-items: baseline` → A: a block 50 x 40 (fixed, minimum width 0, shrink
    ///   0) → leaf L, 10 x 10 fixed, first baseline b (b = 8 at first); P → B: a leaf 50 x 20 with the same minimum and shrink,
    ///   first baseline 8; root → sibling S, a leaf 10 x 10. Laid out. A hook records each run.
    ///
    /// Steps:
    /// - Change S's style to height 20 and run the pass: root lays out again, and P hits its cache.
    /// - Set b to 18, invalidate L's content, and run the pass.
    ///
    /// Expected:
    /// - In the first pass P did not run.
    /// - After the second, B is at y 10 in P.
    /// </remarks>
    [Fact]
    public void ReadBaseline_AfterTheParentsCacheHit_StillReachesTheParent() =>
        ui.Invoke(() =>
        {
            var tree = new UiTree();
            NodeHandle root = tree.CreateNode(Column());
            NodeHandle parent = Box(tree, root, BaselineRow());
            float baseline = 8;
            NodeHandle a = Box(tree, parent, FixedItem(50, 40));
            NodeHandle leaf = tree.CreateNode(FixedItem(10, 10), (_, _) => new MeasureResult(new Size(10, 10), baseline));
            tree.AppendChild(a, leaf);
            NodeHandle b = tree.CreateNode(FixedItem(50, 20), static (_, _) => new MeasureResult(new Size(50, 20), 8));
            tree.AppendChild(parent, b);
            NodeHandle sibling = Leaf(tree, root, 10, 10);
            LayOut(tree, root, 300, 300);
            var ran = new List<NodeHandle>();
            tree.LayoutRunHook = ran.Add;

            tree.SetStyle(sibling, new LayoutStyle { Height = Length.Px(20) });
            LayoutEngine.RunLayoutPass(tree);
            Assert.DoesNotContain(parent, ran);

            baseline = 18;
            Invalidation.Invalidate(tree, leaf, ChangeKind.TextContent);
            LayoutEngine.RunLayoutPass(tree);

            Assert.Equal(10, tree.GetRect(b).Y);
        });

    /// <summary>
    /// A leaf's content widths are dropped when its content changes, so the new widths reach the row (design §3.2, row L4).
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - root: row, window 300 x 100 → leaf A, content w x 10 with content widths (w, w) that do not depend on the height
    ///   (w = 30 at first) → leaf B, 20 x 10. Laid out: B at x 30.
    ///
    /// Steps:
    /// - Set w to 50, invalidate A's content, and run the pass.
    ///
    /// Expected:
    /// - B at x 50.
    /// </remarks>
    [Fact]
    public void ContentWidths_OfALeafWhoseContentChanges_AreDropped() =>
        ui.Invoke(() =>
        {
            var tree = new UiTree();
            NodeHandle root = tree.CreateNode(Row());
            float width = 30;
            NodeHandle a = tree.CreateNode(
                new LayoutStyle(),
                (known, _) => MeasureResult.Of(Maybe.Or(known.Width, width), 10),
                _ => new IntrinsicWidths(width, width, DependsOnHeight: false)
            );
            tree.AppendChild(root, a);
            NodeHandle b = Leaf(tree, root, 20, 10);
            LayOut(tree, root, 300, 100);
            Assert.Equal(30, tree.GetRect(b).X);

            width = 50;
            Invalidation.Invalidate(tree, a, ChangeKind.TextContent);
            LayoutEngine.RunLayoutPass(tree);

            Assert.Equal(50, tree.GetRect(b).X);
        });

    /// <summary>
    /// Content widths that depend on the height are kept for the height they were asked for: a new height asks again (design
    /// §3.2, row L4, Chromium's min/max sizes cache).
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - root: row (items stretched to its height) → leaf A, whose content is twice as wide as it is high, with content widths
    ///   (2h, 2h) that depend on the height h → leaf B, 20 x 10. Laid out in a window 300 x 100: A is 200 wide, B at x 200.
    ///
    /// Steps:
    /// - Resize the window to 300 x 50 and run the pass.
    ///
    /// Expected:
    /// - A is 100 wide and B at x 100.
    /// </remarks>
    [Fact]
    public void ContentWidths_ThatDependOnTheHeight_AreAskedAgainForANewHeight() =>
        ui.Invoke(() =>
        {
            var tree = new UiTree();
            NodeHandle root = tree.CreateNode(Row());
            NodeHandle a = tree.CreateNode(
                new LayoutStyle(),
                static (known, _) => MeasureResult.Of(Maybe.Or(known.Width, 2 * known.Height), known.Height),
                static height => new IntrinsicWidths(2 * height, 2 * height, DependsOnHeight: true)
            );
            tree.AppendChild(root, a);
            NodeHandle b = Leaf(tree, root, 20, 10);
            LayOut(tree, root, 300, 100);
            Assert.Equal((200f, 200f), (tree.GetRect(a).Width, tree.GetRect(b).X));

            LayOut(tree, root, 300, 50);

            Assert.Equal((100f, 100f), (tree.GetRect(a).Width, tree.GetRect(b).X));
        });

    /// <summary>
    /// Content widths that do not depend on the height are computed once, though the flex steps ask for the min-content and the
    /// max-content width under several cross sizes (design §3.2, row L4).
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - root: column, window 300 x 300 → row → three leaves 10 x 10 whose content widths (10, 10) count their calls.
    ///
    /// Steps:
    /// - Lay out.
    ///
    /// Expected:
    /// - Each leaf's content widths were computed once (three calls).
    /// </remarks>
    [Fact]
    public void ContentWidths_ThatDoNotDependOnTheHeight_AreComputedOnce() =>
        ui.Invoke(() =>
        {
            var tree = new UiTree();
            NodeHandle root = tree.CreateNode(Column());
            NodeHandle row = Box(tree, root, Row());
            int calls = 0;
            for (int i = 0; i < 3; i++)
            {
                NodeHandle leaf = tree.CreateNode(
                    new LayoutStyle(),
                    static (known, _) => MeasureResult.Of(Maybe.Or(known.Width, 10), Maybe.Or(known.Height, 10)),
                    _ =>
                    {
                        calls++;
                        return new IntrinsicWidths(10, 10, DependsOnHeight: false);
                    }
                );
                tree.AppendChild(row, leaf);
            }

            LayOut(tree, root, 300, 300);

            Assert.Equal(3, calls);
        });

    /// <summary>
    /// A clean child laid out again at the same size, but with that size now a basis for percentages, is laid out again: the
    /// cached result checks whether the known size is definite, not only its value (design §3.2, CSS Flexbox §9.8).
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - root: block, window 300 x 300 → column C with an automatic height → item I, a block whose height is its content's →
    ///   F, 100 high, and L, 50 % high. Laid out: I is 100 high, but its height comes from its content, so it is not definite
    ///   and L's 50 % is automatic (L is 0 high).
    ///
    /// Steps:
    /// - Set C's height to 100 and run the pass. I is clean, and gets the same height, 100, now definite.
    ///
    /// Expected:
    /// - L is 50 high.
    /// </remarks>
    [Fact]
    public void CleanChild_WhoseKnownSizeBecomesDefinite_IsLaidOutAgain() =>
        ui.Invoke(() =>
        {
            var tree = new UiTree();
            NodeHandle root = tree.CreateNode(new LayoutStyle());
            NodeHandle column = Box(tree, root, Column());
            NodeHandle item = Box(tree, column, new LayoutStyle());
            Box(tree, item, new LayoutStyle { Height = Length.Px(100) });
            NodeHandle leaf = Box(tree, item, new LayoutStyle { Height = Length.Percent(0.5f) });
            LayOut(tree, root, 300, 300);
            Assert.Equal((100f, 0f), (tree.GetRect(item).Height, tree.GetRect(leaf).Height));

            tree.SetStyle(column, Column() with { Height = Length.Px(100) });
            LayoutEngine.RunLayoutPass(tree);

            Assert.Equal((100f, 50f), (tree.GetRect(item).Height, tree.GetRect(leaf).Height));
        });

    /// <summary>
    /// A container whose style changes solves the base size of a clean item again: its <c>align-items</c> decides whether the
    /// item is stretched, and so the height its width is measured at (design §3.2, row L4).
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - root: row, window 300 x 100 → leaf A, twice as wide as the height it is given (20 wide with none) → leaf B, 20 x 10.
    ///   Laid out: A is stretched to 100 high, so 200 wide, and B is at x 200.
    ///
    /// Steps:
    /// - Set the root's <c>align-items</c> to <c>start</c> and run the pass. A is clean.
    ///
    /// Expected:
    /// - A is not stretched: 20 wide, and B at x 20.
    /// </remarks>
    [Fact]
    public void BaseSize_OfACleanItem_FollowsTheContainersStyle() =>
        ui.Invoke(() =>
        {
            var tree = new UiTree();
            NodeHandle root = tree.CreateNode(Row());
            tree.AppendChild(root, tree.CreateNode(new LayoutStyle(), TwiceAsWide));
            NodeHandle b = Leaf(tree, root, 20, 10);
            LayOut(tree, root, 300, 100);
            Assert.Equal(200, tree.GetRect(b).X);

            tree.SetStyle(
                root,
                new LayoutStyle { Algorithm = new LayoutAlgorithm.Flex(new FlexProps { AlignItems = new AlignItems(AlignItemsKeyword.Start) }) }
            );
            LayoutEngine.RunLayoutPass(tree);

            Assert.Equal(20, tree.GetRect(b).X);
        });

    /// <summary>
    /// A container whose cross size alone changes (a sibling grew) solves the base size of a clean stretched item again: the
    /// item is measured at the container's cross size (design §3.2, row L4).
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - root: column, window 300 x 300, <c>align-items: start</c> → row R → leaf A, twice as wide as the height it is given
    ///   (10 high with none) → leaf C, 20 wide and h high (h = 30 at first). Laid out: R is 30 high, A is stretched to 30 high,
    ///   so 60 wide.
    ///
    /// Steps:
    /// - Set h to 50, invalidate C's content, and run the pass. A is clean, and R is laid out at a new height in the same
    ///   space.
    ///
    /// Expected:
    /// - A is 50 high and 100 wide.
    /// </remarks>
    [Fact]
    public void BaseSize_OfACleanItem_FollowsTheContainersCrossSize() =>
        ui.Invoke(() =>
        {
            var tree = new UiTree();
            NodeHandle root = tree.CreateNode(
                new LayoutStyle
                {
                    Algorithm = new LayoutAlgorithm.Flex(
                        new FlexProps { Direction = FlexDirection.Column, AlignItems = new AlignItems(AlignItemsKeyword.Start) }
                    ),
                }
            );
            NodeHandle row = Box(tree, root, Row());
            NodeHandle a = tree.CreateNode(new LayoutStyle(), TwiceAsWide);
            tree.AppendChild(row, a);
            float height = 30;
            NodeHandle c = tree.CreateNode(new LayoutStyle(), (known, _) => MeasureResult.Of(20, Maybe.Or(known.Height, height)));
            tree.AppendChild(row, c);
            LayOut(tree, root, 300, 300);
            Assert.Equal((60f, 30f), (tree.GetRect(a).Width, tree.GetRect(a).Height));

            height = 50;
            Invalidation.Invalidate(tree, c, ChangeKind.TextContent);
            LayoutEngine.RunLayoutPass(tree);

            Assert.Equal((100f, 50f), (tree.GetRect(a).Width, tree.GetRect(a).Height));
        });

    /// <summary>
    /// A container whose main size changes solves the base size of a clean item with a percentage basis again (design §3.2,
    /// row L4).
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - root: row, window 300 x 100 → leaf A, basis 50 %, content 10 x 10 → leaf B, 20 x 10. Laid out: A is 150 wide, B at x
    ///   150.
    ///
    /// Steps:
    /// - Resize the window to 200 x 100 and run the pass.
    ///
    /// Expected:
    /// - A is 100 wide, B at x 100.
    /// </remarks>
    [Fact]
    public void BaseSize_OfACleanItemWithAPercentage_FollowsTheContainersMainSize() =>
        ui.Invoke(() =>
        {
            var tree = new UiTree();
            NodeHandle root = tree.CreateNode(Row());
            Leaf(tree, root, 10, 10, new LayoutStyle { FlexBasis = Length.Percent(0.5f) });
            NodeHandle b = Leaf(tree, root, 20, 10);
            LayOut(tree, root, 300, 100);
            Assert.Equal(150, tree.GetRect(b).X);

            LayOut(tree, root, 200, 100);

            Assert.Equal(100, tree.GetRect(b).X);
        });

    /// <summary>
    /// A container that takes a clean item's base size from its last run marks the item as read again, as measuring it would:
    /// a later change of the item's content still reaches the container (design §2.4.1, row L4).
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - root: row, window 300 x 100 → leaf A, content w x 10 with content widths (w, w) (w = 30 at first), stretched to the
    ///   row, so both its sides are decided → leaf C, 20 x h (h = 10 at first) → leaf B, 20 x 10. Laid out: B at x 50.
    ///
    /// Steps:
    /// - Set h to 20, invalidate C's content, and run the pass: the row is solved again, and A's base size comes from the
    ///   last run.
    /// - Set w to 50, invalidate A's content, and run the pass.
    ///
    /// Expected:
    /// - B at x 70.
    /// </remarks>
    [Fact]
    public void BaseSize_TakenFromTheLastRun_KeepsTheReadMark() =>
        ui.Invoke(() =>
        {
            var tree = new UiTree();
            NodeHandle root = tree.CreateNode(Row());
            float width = 30;
            NodeHandle a = tree.CreateNode(
                new LayoutStyle(),
                (known, _) => MeasureResult.Of(Maybe.Or(known.Width, width), Maybe.Or(known.Height, 10)),
                _ => new IntrinsicWidths(width, width, DependsOnHeight: false)
            );
            tree.AppendChild(root, a);
            float height = 10;
            NodeHandle c = tree.CreateNode(new LayoutStyle(), (known, _) => MeasureResult.Of(20, Maybe.Or(known.Height, height)));
            tree.AppendChild(root, c);
            NodeHandle b = Leaf(tree, root, 20, 10);
            LayOut(tree, root, 300, 100);
            Assert.Equal(50, tree.GetRect(b).X);

            height = 20;
            Invalidation.Invalidate(tree, c, ChangeKind.TextContent);
            LayoutEngine.RunLayoutPass(tree);
            width = 50;
            Invalidation.Invalidate(tree, a, ChangeKind.TextContent);
            LayoutEngine.RunLayoutPass(tree);

            Assert.Equal(70, tree.GetRect(b).X);
        });

    // A leaf twice as wide as the height it is given, 20 wide and 10 high when none is given.
    private static readonly MeasureFunc TwiceAsWide = static (known, _) =>
        MeasureResult.Of(Maybe.Or(known.Width, float.IsNaN(known.Height) ? 20 : 2 * known.Height), Maybe.Or(known.Height, 10));

    private static LayoutStyle BaselineRow() =>
        new() { Algorithm = new LayoutAlgorithm.Flex(new FlexProps { AlignItems = new AlignItems(AlignItemsKeyword.Baseline) }) };

    // A fixed size, with no automatic minimum and no shrink, so that no measuring run reads the node.
    private static LayoutStyle FixedItem(float width, float height) =>
        new()
        {
            Width = Length.Px(width),
            Height = Length.Px(height),
            MinWidth = LengthPercentageAuto.Px(0),
            FlexShrink = 0,
        };

    private static LayoutStyle Flexible() => new() { FlexBasis = Length.Px(0), FlexGrow = 1 };

    private static float MinContentOr(AvailableSize available, float minContent) =>
        available.Width.Kind == AvailableSpaceKind.MinContent ? minContent : 0;
}
