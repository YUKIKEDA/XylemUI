using static XylemUI.Tests.LayoutTestTree;

namespace XylemUI.Tests;

// The children out of a container's flow (design §3.2, row L2) as XylemUI lays them out again: what Chrome's results cannot
// check. Every remark gives the arithmetic.
[Collection(UiThreadGroup.Name)]
public sealed class LayoutPositionedTests(UiThreadFixture ui)
{
    /// <summary>
    /// A node made hidden takes no room and its subtree gets empty rectangles; shown again, it is laid out as before.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - root: row, window 300 x 100. Leaf A 50 x 10; row B holding leaf L 40 x 10; leaf C 30 x 10. Laid out: A at 0, B at 50
    ///   (40 wide), C at 90.
    ///
    /// Steps:
    /// - Hide B (SetStyle) and run the pass; then show it again and run the pass.
    ///
    /// Expected:
    /// - Hidden: B and L have empty rectangles at the origin, and C moved to x 50.
    /// - Shown: B is at 50, 40 wide, L at 0 inside it, and C back at 90.
    /// </remarks>
    [Fact]
    public void Hidden_TakesNoRoomAndComesBack() =>
        ui.Invoke(() =>
        {
            var tree = new UiTree();
            NodeHandle root = tree.CreateNode(Row());
            Leaf(tree, root, 50, 10);
            NodeHandle b = Box(tree, root, Row());
            NodeHandle l = Leaf(tree, b, 40, 10);
            NodeHandle c = Leaf(tree, root, 30, 10);
            LayOut(tree, root, 300, 100);
            Assert.Equal(50, tree.GetRect(b).X);

            tree.SetStyle(b, Row() with { Hidden = true });
            LayoutEngine.RunLayoutPass(tree);
            Assert.Equal(default, tree.GetRect(b));
            Assert.Equal(default, tree.GetRect(l));
            Assert.Equal(50, tree.GetRect(c).X);

            tree.SetStyle(b, Row());
            LayoutEngine.RunLayoutPass(tree);
            Assert.Equal(new Rect(50, 0, 40, 100), tree.GetRect(b));
            Assert.Equal(new Rect(0, 0, 40, 100), tree.GetRect(l));
            Assert.Equal(90, tree.GetRect(c).X);
        });

    /// <summary>
    /// An absolutely positioned child is no flex item: the items after it start where the one before it ended.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - root: row, window 300 x 100. Leaf A 50 x 10; leaf X 40 x 20, absolute, left 10, top 5; leaf B 30 x 10.
    ///
    /// Steps:
    /// - Lay the tree out.
    ///
    /// Expected:
    /// - B at x 50 (A's end), and X at (10, 5), 40 x 20.
    /// </remarks>
    [Fact]
    public void AbsoluteChild_TakesNoRoomInTheFlow() =>
        ui.Invoke(() =>
        {
            var tree = new UiTree();
            NodeHandle root = tree.CreateNode(Row());
            Leaf(tree, root, 50, 10);
            NodeHandle x = Leaf(tree, root, 40, 20, Absolute(left: 10, top: 5));
            NodeHandle b = Leaf(tree, root, 30, 10);

            LayOut(tree, root, 300, 100);

            Assert.Equal(50, tree.GetRect(b).X);
            Assert.Equal(new Rect(10, 5, 40, 20), tree.GetRect(x));
        });

    /// <summary>
    /// A change of the content of an absolutely positioned child placed from its right and bottom moves it: the parent places
    /// it again from its new size.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - root: column, window 300 x 300. Row R, 200 x 100, holds leaf X, absolute, right 10, bottom 10, whose content is
    ///   40 x 20 with content widths. Laid out: X at (200 - 10 - 40, 100 - 10 - 20) = (150, 70).
    ///
    /// Steps:
    /// - Make X's content 60 x 20, invalidate it (a text change), and run the pass.
    ///
    /// Expected:
    /// - X at (130, 70), 60 x 20.
    /// </remarks>
    [Fact]
    public void AbsoluteChild_WhoseContentChanges_IsPlacedAgain() =>
        ui.Invoke(() =>
        {
            var tree = new UiTree();
            NodeHandle root = tree.CreateNode(Column());
            NodeHandle row = Box(tree, root, Row() with { Width = Length.Px(200), Height = Length.Px(100) });
            float width = 40;
            NodeHandle x = tree.CreateNode(
                Absolute(right: 10, bottom: 10),
                (_, _) => MeasureResult.Of(width, 20),
                _ => new IntrinsicWidths(width, width, DependsOnHeight: false)
            );
            tree.AppendChild(row, x);
            LayOut(tree, root, 300, 300);
            Assert.Equal(new Rect(150, 70, 40, 20), tree.GetRect(x));

            width = 60;
            Invalidation.Invalidate(tree, x, ChangeKind.TextContent);
            LayoutEngine.RunLayoutPass(tree);

            Assert.Equal(new Rect(130, 70, 60, 20), tree.GetRect(x));
        });

    /// <summary>
    /// A relatively positioned child is shifted by its insets after the layout, and its siblings stay where the flow put them.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - root: row, window 300 x 100. Leaf A 50 x 10, relative, left 5, top 3; leaf B 30 x 10.
    ///
    /// Steps:
    /// - Lay the tree out.
    ///
    /// Expected:
    /// - A at (5, 3); B at x 50.
    /// </remarks>
    [Fact]
    public void RelativeInset_ShiftsOnlyTheChild() =>
        ui.Invoke(() =>
        {
            var tree = new UiTree();
            NodeHandle root = tree.CreateNode(Row());
            NodeHandle a = Leaf(tree, root, 50, 10, new LayoutStyle { Inset = Insets(left: 5, top: 3) });
            NodeHandle b = Leaf(tree, root, 30, 10);

            LayOut(tree, root, 300, 100);

            Assert.Equal(5, tree.GetRect(a).X);
            Assert.Equal(3, tree.GetRect(a).Y);
            Assert.Equal(50, tree.GetRect(b).X);
        });

    private static LayoutStyle Absolute(float left = float.NaN, float top = float.NaN, float right = float.NaN, float bottom = float.NaN) =>
        new() { Position = Position.Absolute, Inset = Insets(left, top, right, bottom) };

    private static EdgeInsets<LengthPercentageAuto> Insets(
        float left = float.NaN,
        float top = float.NaN,
        float right = float.NaN,
        float bottom = float.NaN
    ) => new(Side(left), Side(top), Side(right), Side(bottom));

    private static LengthPercentageAuto Side(float pixels) => float.IsNaN(pixels) ? LengthPercentageAuto.Auto : LengthPercentageAuto.Px(pixels);
}
