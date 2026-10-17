using static XylemUI.Tests.LayoutTestTree;

namespace XylemUI.Tests;

// Block and Flex against rectangles computed by hand (S6b, design §3.2). Every remark gives the arithmetic.
[Collection(UiThreadGroup.Name)]
public sealed class LayoutAlgorithmTests(UiThreadFixture ui)
{
    /// <summary>
    /// Block stacks its children vertically, with padding and margins added, never collapsed; an auto width fills the content
    /// box; a leaf takes its content's size.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - root: block, padding 10, window 300 x 200.
    /// - A: leaf 50 x 20, margin 5 on every side. B: block, height 30, margin top 10.
    ///
    /// Steps:
    /// - Lay out.
    ///
    /// Expected:
    /// - A: (10 + 5, 10 + 5) = (15, 15), 50 x 20.
    /// - B: x 10; y = 15 + 20 + 5 + 10 = 50; width 300 − 20 = 280; height 30.
    /// - root: 300 x 200 (the window's tight constraint).
    /// </remarks>
    [Fact]
    public void Block_StacksChildrenWithPaddingAndMargins() =>
        ui.Invoke(() =>
        {
            var tree = new UiTree();
            NodeHandle root = tree.CreateNode(new LayoutStyle { Padding = EdgeInsets.All(10) });
            NodeHandle a = Leaf(tree, root, 50, 20, new LayoutStyle { Margin = EdgeInsets.All(5) });
            NodeHandle b = Box(tree, root, new LayoutStyle { Height = Length.Px(30), Margin = new EdgeInsets(0, 10, 0, 0) });

            LayOut(tree, root, 300, 200);

            Assert.Equal(new Rect(15, 15, 50, 20), tree.GetRect(a));
            Assert.Equal(new Rect(10, 50, 280, 30), tree.GetRect(b));
            Assert.Equal(new Rect(0, 0, 300, 200), tree.GetRect(root));
        });

    /// <summary>
    /// A row divides its free space by the grow factors.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - root: row, window 400 x 100. Children: C1 width 100; C2 leaf 0 x 10, grow 1; C3 leaf 0 x 10, grow 3.
    ///
    /// Steps:
    /// - Lay out.
    ///
    /// Expected:
    /// - Free space 400 − 100 = 300: C2 gets 75, C3 gets 225.
    /// - x: C1 0, C2 100, C3 175. Heights: C1 0 (an empty block), C2 and C3 10.
    /// </remarks>
    [Fact]
    public void Row_DividesFreeSpaceByGrow() =>
        ui.Invoke(() =>
        {
            var tree = new UiTree();
            NodeHandle root = tree.CreateNode(Row());
            NodeHandle c1 = Box(tree, root, new LayoutStyle { Width = Length.Px(100) });
            NodeHandle c2 = Leaf(tree, root, 0, 10, new LayoutStyle { FlexGrow = 1 });
            NodeHandle c3 = Leaf(tree, root, 0, 10, new LayoutStyle { FlexGrow = 3 });

            LayOut(tree, root, 400, 100);

            Assert.Equal(new Rect(0, 0, 100, 0), tree.GetRect(c1));
            Assert.Equal(new Rect(100, 0, 75, 10), tree.GetRect(c2));
            Assert.Equal(new Rect(175, 0, 225, 10), tree.GetRect(c3));
        });

    /// <summary>
    /// A row that overflows takes the overflow from its children by shrink factor times basis.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - root: row, window 200 x 50. Children: basis 150, shrink 1; basis 150, shrink 3.
    ///
    /// Steps:
    /// - Lay out.
    ///
    /// Expected:
    /// - Overflow 300 − 200 = 100, weights 150 and 450: the first loses 25 (125), the second 75 (75); x 0 and 125.
    /// </remarks>
    [Fact]
    public void Row_ShrinksByFactorTimesBasis() =>
        ui.Invoke(() =>
        {
            var tree = new UiTree();
            NodeHandle root = tree.CreateNode(Row());
            NodeHandle first = Box(tree, root, new LayoutStyle { FlexBasis = Length.Px(150), FlexShrink = 1 });
            NodeHandle second = Box(tree, root, new LayoutStyle { FlexBasis = Length.Px(150), FlexShrink = 3 });

            LayOut(tree, root, 200, 50);

            Assert.Equal(new Rect(0, 0, 125, 0), tree.GetRect(first));
            Assert.Equal(new Rect(125, 0, 75, 0), tree.GetRect(second));
        });

    /// <summary>
    /// A column with an auto height takes its content's height and fills the width its parent gives.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - root: block, window 300 x 500. column: padding 4. In it: leaf 30 x 10; leaf 40 x 20 with margin bottom 6.
    ///
    /// Steps:
    /// - Lay out.
    ///
    /// Expected:
    /// - column: (0, 0), width 300, height 4 + 10 + 20 + 6 + 4 = 44.
    /// - leaves: (4, 4) 30 x 10 and (4, 14) 40 x 20 (an auto cross size is the content's: align start).
    /// </remarks>
    [Fact]
    public void Column_TakesItsContentHeightAndFillsTheWidth() =>
        ui.Invoke(() =>
        {
            var tree = new UiTree();
            NodeHandle root = tree.CreateNode(new LayoutStyle());
            NodeHandle column = Box(tree, root, Column() with { Padding = EdgeInsets.All(4) });
            NodeHandle first = Leaf(tree, column, 30, 10);
            NodeHandle second = Leaf(tree, column, 40, 20, new LayoutStyle { Margin = new EdgeInsets(0, 0, 0, 6) });

            LayOut(tree, root, 300, 500);

            Assert.Equal(new Rect(0, 0, 300, 44), tree.GetRect(column));
            Assert.Equal(new Rect(4, 4, 30, 10), tree.GetRect(first));
            Assert.Equal(new Rect(4, 14, 40, 20), tree.GetRect(second));
        });

    /// <summary>
    /// An auto basis is measured from the content: a nested row is as wide as its children, and a sibling grows into the rest.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - root: row, window 500 x 100. inner: row with leaves 20 x 10 and 30 x 10. rest: block, grow 1.
    ///
    /// Steps:
    /// - Lay out.
    ///
    /// Expected:
    /// - inner: (0, 0), 50 x 10 (its leaves at x 0 and 20). rest: (50, 0), width 500 − 50 = 450.
    /// </remarks>
    [Fact]
    public void Row_AutoBasis_IsMeasuredFromTheContent() =>
        ui.Invoke(() =>
        {
            var tree = new UiTree();
            NodeHandle root = tree.CreateNode(Row());
            NodeHandle inner = Box(tree, root, Row());
            NodeHandle left = Leaf(tree, inner, 20, 10);
            NodeHandle right = Leaf(tree, inner, 30, 10);
            NodeHandle rest = Box(tree, root, new LayoutStyle { FlexGrow = 1 });

            LayOut(tree, root, 500, 100);

            Assert.Equal(new Rect(0, 0, 50, 10), tree.GetRect(inner));
            Assert.Equal(new Rect(0, 0, 20, 10), tree.GetRect(left));
            Assert.Equal(new Rect(20, 0, 30, 10), tree.GetRect(right));
            Assert.Equal(new Rect(50, 0, 450, 0), tree.GetRect(rest));
        });

    /// <summary>
    /// When one subtree changes, a clean sibling subtree with the same constraint keeps its cached layout and is not visited.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - root (block) → S1 (block) → leaf L1; root → S2 (block) → leaf L2. Laid out at 300 x 200. A hook records each layout.
    ///
    /// Steps:
    /// - Change L2's style to height 40; run the pass.
    ///
    /// Expected:
    /// - The layouts that ran: root, S2, L2 — not S1 or L1.
    /// - L2 is 40 high and S2 grew to 40.
    /// </remarks>
    [Fact]
    public void CleanSibling_KeepsItsCachedLayout() =>
        ui.Invoke(() =>
        {
            var tree = new UiTree();
            NodeHandle root = tree.CreateNode(new LayoutStyle());
            NodeHandle s1 = Box(tree, root, new LayoutStyle());
            NodeHandle l1 = Leaf(tree, s1, 10, 10);
            NodeHandle s2 = Box(tree, root, new LayoutStyle());
            NodeHandle l2 = Leaf(tree, s2, 10, 10);
            LayOut(tree, root, 300, 200);
            var ran = new List<NodeHandle>();
            tree.LayoutRunHook = ran.Add;

            tree.SetStyle(l2, new LayoutStyle { Height = Length.Px(40) });
            LayoutEngine.RunLayoutPass(tree);

            Assert.Equal([root, s2, l2], ran);
            Assert.Equal(40, tree.GetRect(l2).Height);
            Assert.Equal(40, tree.GetRect(s2).Height);
            Assert.DoesNotContain(s1, ran);
            Assert.DoesNotContain(l1, ran);
        });

    /// <summary>
    /// A flex container measures its children and then lays them out; a clean child hits the cache in both runs and is not
    /// visited.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - root (column) → row A (leaves A1, A2) and row B (leaves B1, B2), every leaf 10 x 10. Laid out at 300 x 200. A hook
    ///   records each run.
    ///
    /// Steps:
    /// - Change A1's style to width 30; run the pass.
    ///
    /// Expected:
    /// - Neither row B nor its leaves ran. A1 is 30 wide and A2 moved to x 30.
    /// </remarks>
    [Fact]
    public void FlexContainer_CleanChildHitsBothCaches() =>
        ui.Invoke(() =>
        {
            var tree = new UiTree();
            NodeHandle root = tree.CreateNode(Column());
            NodeHandle rowA = Box(tree, root, Row());
            NodeHandle a1 = Leaf(tree, rowA, 10, 10);
            NodeHandle a2 = Leaf(tree, rowA, 10, 10);
            NodeHandle rowB = Box(tree, root, Row());
            NodeHandle b1 = Leaf(tree, rowB, 10, 10);
            NodeHandle b2 = Leaf(tree, rowB, 10, 10);
            LayOut(tree, root, 300, 200);
            var ran = new List<NodeHandle>();
            tree.LayoutRunHook = ran.Add;

            tree.SetStyle(a1, new LayoutStyle { Width = Length.Px(30) });
            LayoutEngine.RunLayoutPass(tree);

            Assert.DoesNotContain(rowB, ran);
            Assert.DoesNotContain(b1, ran);
            Assert.DoesNotContain(b2, ran);
            Assert.Equal(30, tree.GetRect(a1).Width);
            Assert.Equal(30, tree.GetRect(a2).X);
        });

    /// <summary>
    /// A change that moves a node but keeps its size advances its version and its parent's descendant version.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - root (block) → A (leaf 10 x 10) and B (leaf 10 x 10). Laid out.
    ///
    /// Steps:
    /// - Change A's style to height 30; run the pass.
    ///
    /// Expected:
    /// - B moved from y 10 to y 30; B's layout version and root's descendant version advanced; B kept its size.
    /// </remarks>
    [Fact]
    public void MovedNode_AdvancesItsVersionAndItsParents() =>
        ui.Invoke(() =>
        {
            var tree = new UiTree();
            NodeHandle root = tree.CreateNode(new LayoutStyle());
            NodeHandle a = Leaf(tree, root, 10, 10);
            NodeHandle b = Leaf(tree, root, 10, 10);
            LayOut(tree, root, 300, 200);
            ulong bVersion = tree.Nodes.GetRef(b).Versions.Layout;
            ulong rootDescendants = tree.Nodes.GetRef(root).Versions.DescendantLayout;

            tree.SetStyle(a, new LayoutStyle { Height = Length.Px(30) });
            LayoutEngine.RunLayoutPass(tree);

            Assert.Equal(new Rect(0, 30, 10, 10), tree.GetRect(b));
            Assert.True(tree.Nodes.GetRef(b).Versions.Layout > bVersion);
            Assert.True(tree.Nodes.GetRef(root).Versions.DescendantLayout > rootDescendants);
        });
}
