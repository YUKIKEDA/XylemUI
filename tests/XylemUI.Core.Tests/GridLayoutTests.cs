using static XylemUI.Tests.LayoutTestTree;

namespace XylemUI.Tests;

// Grid layouts that Chrome's XML tests cannot write (design §3.3, "The shape of P02f"): CSS drops the declaration, or computes
// one value from another, before the layout sees it.
[Collection(UiThreadGroup.Name)]
public sealed class GridLayoutTests(UiThreadFixture ui)
{
    /// <summary>
    /// A track list with an automatic repetition and a track without a length or a percentage is ignored, as CSS ignores it: the
    /// grid has no explicit column (CSS Grid §7.2.3.2). Chrome drops such a declaration when it parses it, so no XML test can
    /// hold it.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A column of 300 x 100 holding a grid whose columns are `auto repeat(auto-fill, 50px)`, with two leaves of 10 x 10.
    ///
    /// Steps:
    /// - Lay it out; read the leaves' rectangles.
    ///
    /// Expected:
    /// - (0, 0, 300 x 10) and (0, 10, 300 x 10): one implicit auto column, each leaf in a row of its own.
    /// </remarks>
    [Fact]
    public void AutoRepetition_WithAnAutoTrack_IgnoresTheTrackList() =>
        ui.Invoke(() =>
        {
            var tree = new UiTree();
            NodeHandle root = tree.CreateNode(Column());
            NodeHandle grid = Box(
                tree,
                root,
                new LayoutStyle
                {
                    Algorithm = new LayoutAlgorithm.Grid(new GridProps { Columns = [Track.Auto, Track.Repeat(Repeat.AutoFill, Track.Px(50))] }),
                }
            );
            NodeHandle first = Leaf(tree, grid, 10, 10);
            NodeHandle second = Leaf(tree, grid, 10, 10);

            LayOut(tree, root, 300, 100);

            Assert.Equal((new Rect(0, 0, 300, 10), new Rect(0, 10, 300, 10)), (tree.GetRect(first), tree.GetRect(second)));
        });

    /// <summary>
    /// The names of an ignored track list name no line: a child placed by such a name goes to the implicit lines after the
    /// explicit grid, which has no track, as CSS drops the whole declaration (PR #658's review).
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A column of 300 x 100 holding a grid whose columns are `[a] auto repeat(auto-fill, 50px)`, with one leaf of 10 x 10
    ///   placed at the line named a.
    ///
    /// Steps:
    /// - Lay it out; read the leaf's rectangle.
    ///
    /// Expected:
    /// - (145, 0, 155 x 10): no line is named a, so the leaf starts at the first implicit line after the empty explicit grid,
    ///   in the second of two auto columns; the 290 left after the leaf's 10 is shared between them. With the names of the
    ///   ignored list, the leaf would take the only column: (0, 0, 300 x 10).
    /// </remarks>
    [Fact]
    public void IgnoredTrackList_NamesNoLine() =>
        ui.Invoke(() =>
        {
            var tree = new UiTree();
            NodeHandle root = tree.CreateNode(Column());
            NodeHandle grid = Box(
                tree,
                root,
                new LayoutStyle
                {
                    Algorithm = new LayoutAlgorithm.Grid(
                        new GridProps { Columns = [Track.Line("a"), Track.Auto, Track.Repeat(Repeat.AutoFill, Track.Px(50))] }
                    ),
                }
            );
            NodeHandle leaf = Leaf(tree, grid, 10, 10, new LayoutStyle { GridColumn = new GridPlacement(GridLine.Named("a"), GridLine.Auto) });

            LayOut(tree, root, 300, 100);

            Assert.Equal(new Rect(145, 0, 155, 10), tree.GetRect(leaf));
        });

    /// <summary>
    /// An item that scrolls horizontally only has a minimum contribution of 0 to its columns (CSS Grid §6.6). CSS computes a
    /// visible overflow next to a hidden one to auto, so no XML test has a box hidden in one axis and visible in the other.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A column of 300 x 100 holding a grid 50 wide with one auto column, and an item whose overflow is hidden horizontally
    ///   and visible vertically, holding a leaf of 100 x 10.
    ///
    /// Steps:
    /// - Lay it out; read the item's rectangle.
    ///
    /// Expected:
    /// - (0, 0, 50 x 10): the column fits the grid, as the item's minimum contribution is 0, not the leaf's 100.
    /// </remarks>
    [Fact]
    public void ItemScrollingHorizontallyOnly_HasNoMinimumContribution() =>
        ui.Invoke(() =>
        {
            var tree = new UiTree();
            NodeHandle root = tree.CreateNode(Column());
            NodeHandle grid = Box(
                tree,
                root,
                new LayoutStyle { Width = Length.Px(50), Algorithm = new LayoutAlgorithm.Grid(new GridProps { Columns = [Track.Auto] }) }
            );
            NodeHandle item = Box(tree, grid, new LayoutStyle { OverflowX = Overflow.Hidden });
            _ = Leaf(tree, item, 100, 10, Fixed(100, 10));

            LayOut(tree, root, 300, 100);

            Assert.Equal(new Rect(0, 0, 50, 10), tree.GetRect(item));
        });
}
