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

    /// <summary>
    /// A change inside a hidden subtree leaves no mark there, and shows when the subtree is shown again (PR #125's review). The
    /// change marks the path up to the hidden node; the next layout of its parent finds that mark, walks into the subtree
    /// though it is hidden already, and takes the marks off. The subtree's last layouts no longer hold, so showing it lays it
    /// out with the new content.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - root: row, window 300 x 100. Row H holds row M, which holds leaf L, whose content is 40 x 10 with content widths;
    ///   leaf C 30 x 10 after H. H is hidden and laid out: C at x 0.
    ///
    /// Steps:
    /// - Change L's content to 60 x 10 and invalidate it; run the pass; change C (a sibling) and run the pass; show H and run
    ///   the pass.
    ///
    /// Expected:
    /// - While H is hidden: L and M have empty rectangles and no layout mark, and C is at x 0.
    /// - Shown: H, M, and L are 60 wide, and C is at x 60.
    /// </remarks>
    [Fact]
    public void Hidden_KeepsAChangeInsideForWhenItIsShown() =>
        ui.Invoke(() =>
        {
            var tree = new UiTree();
            NodeHandle root = tree.CreateNode(Row());
            NodeHandle h = Box(tree, root, Row() with { Hidden = true });
            NodeHandle m = Box(tree, h, Row());
            float width = 40;
            NodeHandle l = tree.CreateNode(
                new LayoutStyle(),
                (_, _) => MeasureResult.Of(width, 10),
                _ => new IntrinsicWidths(width, width, DependsOnHeight: false)
            );
            tree.AppendChild(m, l);
            NodeHandle c = Leaf(tree, root, 30, 10);
            LayOut(tree, root, 300, 100);
            Assert.Equal(0, tree.GetRect(c).X);

            width = 60;
            Invalidation.Invalidate(tree, l, ChangeKind.TextContent);
            LayoutEngine.RunLayoutPass(tree);
            tree.SetStyle(c, new LayoutStyle { Width = Length.Px(30) });
            LayoutEngine.RunLayoutPass(tree);
            Assert.Equal(default, tree.GetRect(l));
            Assert.Equal(default, tree.GetRect(m));
            Assert.True(IsLayoutClean(tree, l));
            Assert.True(IsLayoutClean(tree, m));
            Assert.Equal(0, tree.GetRect(c).X);

            tree.SetStyle(h, Row());
            LayoutEngine.RunLayoutPass(tree);
            Assert.Equal(60, tree.GetRect(h).Width);
            Assert.Equal(60, tree.GetRect(m).Width);
            Assert.Equal(60, tree.GetRect(l).Width);
            Assert.Equal(60, tree.GetRect(c).X);
        });

    /// <summary>
    /// An absolutely positioned root is placed by its insets against the window, and again when the window's size changes.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - root: absolute, right 10, bottom 20, 100 x 50.
    ///
    /// Steps:
    /// - Lay it out in a window of 300 x 200, then of 400 x 300.
    ///
    /// Expected:
    /// - (300 - 10 - 100, 200 - 20 - 50) = (190, 130), then (290, 230); 100 x 50 both times.
    /// </remarks>
    [Fact]
    public void AbsoluteRoot_IsPlacedAgainstTheWindow() =>
        ui.Invoke(() =>
        {
            var tree = new UiTree();
            NodeHandle root = tree.CreateNode(Absolute(right: 10, bottom: 20) with { Width = Length.Px(100), Height = Length.Px(50) });

            LayOut(tree, root, 300, 200);
            Assert.Equal(new Rect(190, 130, 100, 50), tree.GetRect(root));

            LayOut(tree, root, 400, 300);
            Assert.Equal(new Rect(290, 230, 100, 50), tree.GetRect(root));
        });

    /// <summary>
    /// A block's hidden child takes no room: the next child starts where the one before it ended, and the hidden one comes back
    /// between them.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - root: block, window 300 x 200. Leaves A 50 x 10, B 50 x 20, C 50 x 30 stack from the top: B at y 10, C at y 30.
    ///
    /// Steps:
    /// - Hide B and run the pass; show it and run the pass.
    ///
    /// Expected:
    /// - Hidden: B empty, C at y 10. Shown: B at y 10, C at y 30.
    /// </remarks>
    [Fact]
    public void Hidden_InABlock_TakesNoRoom() =>
        ui.Invoke(() =>
        {
            var tree = new UiTree();
            NodeHandle root = tree.CreateNode(new LayoutStyle());
            Leaf(tree, root, 50, 10);
            NodeHandle b = Leaf(tree, root, 50, 20);
            NodeHandle c = Leaf(tree, root, 50, 30);
            LayOut(tree, root, 300, 200);

            tree.SetStyle(b, new LayoutStyle { Hidden = true });
            LayoutEngine.RunLayoutPass(tree);
            Assert.Equal(default, tree.GetRect(b));
            Assert.Equal(10, tree.GetRect(c).Y);

            tree.SetStyle(b, new LayoutStyle());
            LayoutEngine.RunLayoutPass(tree);
            Assert.Equal(10, tree.GetRect(b).Y);
            Assert.Equal(30, tree.GetRect(c).Y);
        });

    /// <summary>
    /// A clean absolutely positioned child keeps its place only while its container gives it the same inputs (row L7): a new
    /// container size, a new border, or a new static position places it again.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - root: column, window 300 x 300. Block B, 200 x 100 with a padding of 5, holds leaf F 50 x 10 in the flow, leaf X
    ///   40 x 20 absolute at right 10 and bottom 10, and leaf S 30 x 10 absolute with auto insets (its static position: below
    ///   F). Laid out: X at (200 - 10 - 40, 100 - 10 - 20) = (150, 70) against the padding box, S at (5, 5 + 10) = (5, 15).
    ///
    /// Steps:
    /// - Make B 150 high and run the pass (the container size alone: in a block, a new height moves no static position). Then
    ///   give B a border of 5 and no padding and run the pass:
    ///   the content box and S's static position stay, and only the border moves the padding box. Then make F 30 high and
    ///   run the pass (S's static position down). Then make B's `justify-items` center and run the pass (S's static position
    ///   across). X and S are clean in every step.
    ///
    /// Expected:
    /// - 150 high: X at (150, 150 - 10 - 20) = (150, 120). Border 5: X at (5 + 190 - 10 - 40, 5 + 140 - 10 - 20) = (145, 115),
    ///   S still at (5, 15).
    ///   F 30 high: S at (5, 5 + 30) = (5, 35), X where it was. Centered: S at (5 + (190 - 30) / 2, 35) = (85, 35).
    /// </remarks>
    [Fact]
    public void CleanAbsoluteChild_WithNewInputs_IsPlacedAgain() =>
        ui.Invoke(() =>
        {
            var tree = new UiTree();
            NodeHandle root = tree.CreateNode(Column());
            NodeHandle block = Box(tree, root, Fixed(200, 100) with { Padding = EdgeInsets.All(5) });
            NodeHandle f = Leaf(tree, block, 50, 10, new LayoutStyle { Height = Length.Px(10) });
            NodeHandle x = Leaf(tree, block, 40, 20, Absolute(right: 10, bottom: 10));
            NodeHandle s = Leaf(tree, block, 30, 10, new LayoutStyle { Position = Position.Absolute });
            LayOut(tree, root, 300, 300);
            Assert.Equal(new Rect(150, 70, 40, 20), tree.GetRect(x));
            Assert.Equal(new Rect(5, 15, 30, 10), tree.GetRect(s));

            tree.SetStyle(block, Fixed(200, 150) with { Padding = EdgeInsets.All(5) });
            LayoutEngine.RunLayoutPass(tree);
            Assert.Equal(new Rect(150, 120, 40, 20), tree.GetRect(x));

            tree.SetStyle(block, Fixed(200, 150) with { Border = EdgeInsets.All(5) });
            LayoutEngine.RunLayoutPass(tree);
            Assert.Equal(new Rect(145, 115, 40, 20), tree.GetRect(x));
            Assert.Equal(new Rect(5, 15, 30, 10), tree.GetRect(s));

            tree.SetStyle(f, new LayoutStyle { Height = Length.Px(30) });
            LayoutEngine.RunLayoutPass(tree);
            Assert.Equal(new Rect(5, 35, 30, 10), tree.GetRect(s));
            Assert.Equal(new Rect(145, 115, 40, 20), tree.GetRect(x));

            var centered = new LayoutAlgorithm.Block(new BlockProps { JustifyItems = new AlignItems(AlignItemsKeyword.Center) });
            tree.SetStyle(block, Fixed(200, 150) with { Border = EdgeInsets.All(5), Algorithm = centered });
            LayoutEngine.RunLayoutPass(tree);
            Assert.Equal(new Rect(85, 35, 30, 10), tree.GetRect(s));
        });

    /// <summary>
    /// A clean absolutely positioned child whose content its container measured keeps the mark of that read when it keeps its
    /// place (row L7): a later change of its content still goes on to the container.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - root: column, window 300 x 300. Row R, 200 x 100, holds leaves A and B, absolute at right 10 and bottom 10, whose
    ///   content (with content widths) R measures: A 40 x 20, B 30 x 10. Laid out: A at (150, 70).
    ///
    /// Steps:
    /// - Make B's content 50 x 10, invalidate it, and run the pass: R is laid out again, and A, clean with the same inputs,
    ///   keeps its place. Then make A's content 60 x 20, invalidate it, and run the pass.
    ///
    /// Expected:
    /// - A at (200 - 10 - 60, 70) = (130, 70), 60 x 20: the change went on to R, which placed A again.
    /// </remarks>
    [Fact]
    public void KeptAbsoluteChild_StillPassesAContentChangeToItsContainer() =>
        ui.Invoke(() =>
        {
            var tree = new UiTree();
            NodeHandle root = tree.CreateNode(Column());
            NodeHandle row = Box(tree, root, Row() with { Width = Length.Px(200), Height = Length.Px(100) });
            float widthA = 40;
            float widthB = 30;
            NodeHandle a = tree.CreateNode(
                Absolute(right: 10, bottom: 10),
                (_, _) => MeasureResult.Of(widthA, 20),
                _ => new IntrinsicWidths(widthA, widthA, DependsOnHeight: false)
            );
            NodeHandle b = tree.CreateNode(
                Absolute(right: 10, bottom: 10),
                (_, _) => MeasureResult.Of(widthB, 10),
                _ => new IntrinsicWidths(widthB, widthB, DependsOnHeight: false)
            );
            tree.AppendChild(row, a);
            tree.AppendChild(row, b);
            LayOut(tree, root, 300, 300);
            Assert.Equal(new Rect(150, 70, 40, 20), tree.GetRect(a));

            widthB = 50;
            Invalidation.Invalidate(tree, b, ChangeKind.TextContent);
            LayoutEngine.RunLayoutPass(tree);
            Assert.Equal(new Rect(140, 80, 50, 10), tree.GetRect(b));

            widthA = 60;
            Invalidation.Invalidate(tree, a, ChangeKind.TextContent);
            LayoutEngine.RunLayoutPass(tree);

            Assert.Equal(new Rect(130, 70, 60, 20), tree.GetRect(a));
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
