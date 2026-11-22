using XylemUI.Text;
using static XylemUI.Elements;
using static XylemUI.Tests.FrameDoubles;

namespace XylemUI.Tests;

// The component API of row V1d (design §2.13): the elements of the current tree, the chain, the binding of signals, the scope that
// owns the elements and runs their handlers, and the draw functions' exceptions. The text elements are tested in XylemUI.Text.Tests,
// which loads HarfBuzz.
[Collection(UiThreadGroup.Name)]
public sealed class ElementsTests(UiThreadFixture ui)
{
    private static readonly Color Gray = Color.FromRgb(0x808080);
    private static readonly Color Red = Color.FromRgb(0xCC3333);
    private static readonly Color Blue = Color.FromRgb(0x3366CC);

    /// <summary>
    /// The elements are made only while a window's root function runs: outside it, and in a handler, which runs after it, they throw.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - No tree is current; then a scene whose root function makes a box with a click handler that tries to make a box.
    ///
    /// Steps:
    /// - Call <c>Box</c>, <c>Column</c>, and <c>Row</c> with no tree current. Mount the scene and click the box.
    ///
    /// Expected:
    /// - Each call throws <see cref="InvalidOperationException"/>; the handler's call throws it too.
    /// </remarks>
    [Fact]
    public void Elements_OutsideTheRootFunction_Throw() =>
        ui.Invoke(() =>
        {
            Assert.Null(UiTree.Current);
            Assert.Throws<InvalidOperationException>(() => Box());
            Assert.Throws<InvalidOperationException>(() => Column());
            Assert.Throws<InvalidOperationException>(() => Row());

            var scene = new Scene();
            Exception? thrown = null;
            Element box = default;
            scene.Mount(() =>
                box = Box()
                    .Width(50)
                    .Height(50)
                    .OnClick(() =>
                    {
                        try
                        {
                            _ = Box();
                        }
                        catch (InvalidOperationException exception)
                        {
                            thrown = exception;
                        }
                    })
            );
            Assert.False(box.IsNone);
            scene.Click(10, 10);
            Assert.NotNull(thrown);
            scene.Dispose();
        });

    /// <summary>
    /// An element is made in the current scope, and an element made with no scope throws.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A tree set as the current tree by hand, with no current scope.
    ///
    /// Steps:
    /// - Call <c>Box</c>.
    ///
    /// Expected:
    /// - It throws <see cref="InvalidOperationException"/>, and the tree has no node.
    /// </remarks>
    [Fact]
    public void Elements_WithNoScope_Throw() =>
        ui.Invoke(() =>
        {
            var tree = new UiTree();
            UiTree.Current = tree;
            try
            {
                Assert.Throws<InvalidOperationException>(() => Box());
                Assert.Equal(0, tree.Nodes.Count);
            }
            finally
            {
                UiTree.Current = null;
            }
        });

    /// <summary>
    /// A column and a row take their children in the order given, as a flex column and a flex row; a box takes its one child; a
    /// column with no child is an empty container; a child of another tree or one that has a parent throws.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A scene, and a second tree with a box.
    ///
    /// Steps:
    /// - Make a column of three boxes, a row of two, a box of one, and an empty column. Then pass a child that has a parent, the
    ///   default element, and the second tree's box.
    ///
    /// Expected:
    /// - The children are in the order given and the algorithms are Flex column and row; the empty column has no child; each bad
    ///   child throws <see cref="ArgumentException"/>.
    /// </remarks>
    [Fact]
    public void Containers_TakeTheirChildrenInOrder_AndRefuseOthers() =>
        ui.Invoke(() =>
        {
            var scene = new Scene();
            var other = new Scene();
            Element foreign = default;
            other.Mount(() => foreign = Box());
            scene.Mount(() =>
            {
                Element a = Box();
                Element b = Box();
                Element c = Box();
                Element column = Column(a, b, c);
                Assert.Equal([a.Node, b.Node, c.Node], scene.ChildrenOf(column.Node));
                Assert.Equal(LayoutAlgorithm.Flex.Column, scene.Tree.Nodes.GetRef(column.Node).Layout.Style.Algorithm);

                Element d = Box();
                Element e = Box();
                Element row = Row(d, e);
                Assert.Equal([d.Node, e.Node], scene.ChildrenOf(row.Node));
                Assert.Equal(LayoutAlgorithm.Flex.Row, scene.Tree.Nodes.GetRef(row.Node).Layout.Style.Algorithm);

                Element inner = Box();
                Element box = Box(inner);
                Assert.Equal([inner.Node], scene.ChildrenOf(box.Node));
                Assert.Empty(scene.ChildrenOf(Column().Node));

                Assert.Throws<ArgumentException>(() => Column(a));
                Assert.Throws<ArgumentException>(() => Box(default(Element)));
                Assert.Throws<ArgumentException>(() => Row(foreign));
                return Column(column, row, box);
            });
            scene.Dispose();
            other.Dispose();
        });

    /// <summary>
    /// The chain writes each value to the node's style or look through the tree's setters and returns the same element; a gap on a
    /// node that is not a flex container and a size of text on a node that is not text throw.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A scene.
    ///
    /// Steps:
    /// - On a row: width 40, height 30, padding 4 and 2, gap 6, a border of 1 in red, the blue background, corner radius 3, opacity
    ///   0.5, the hand cursor, and the pointer blocked. Then call gap and size of text on a box.
    ///
    /// Expected:
    /// - The chain returns the row each time; the style has the sizes, the padding, the gap, and the border; the look has the
    ///   background, the border's paint, the radii, and the opacity; the input has the cursor and blocks the pointer. The two calls
    ///   on the box throw <see cref="InvalidOperationException"/>.
    /// </remarks>
    [Fact]
    public void Chain_WritesTheNode_AndReturnsTheSameElement() =>
        ui.Invoke(() =>
        {
            var scene = new Scene();
            scene.Mount(() =>
            {
                Element row = Row();
                Element same = row.Width(40)
                    .Height(30)
                    .Padding(4, 2)
                    .Gap(6)
                    .Border(1, new Paint(Red))
                    .Background(new Paint(Blue))
                    .CornerRadius(3)
                    .Opacity(0.5f)
                    .Cursor(CursorKind.Hand)
                    .BlocksPointer();
                Assert.Equal(row, same);
                ref UiNode node = ref scene.Tree.Nodes.GetRef(row.Node);
                LayoutStyle style = node.Layout.Style;
                Assert.Equal(Length.Px(40), style.Width);
                Assert.Equal(Length.Px(30), style.Height);
                Assert.Equal(LengthPercentage.Px(4), style.Padding.Left);
                Assert.Equal(LengthPercentage.Px(2), style.Padding.Top);
                var flex = (LayoutAlgorithm.Flex)style.Algorithm;
                Assert.Equal((LengthPercentage.Px(6), LengthPercentage.Px(6)), (flex.Props.RowGap, flex.Props.ColumnGap));
                Assert.Equal(LengthPercentage.Px(1), style.Border.Left);
                Assert.Equal(new Paint(Blue), node.Visual!.Background);
                Assert.Equal(new Paint(Red), node.Visual.BorderPaint);
                Assert.Equal(new CornerRadii(3), node.Visual.Radii);
                Assert.Equal(0.5f, node.Visual.Opacity);
                Assert.Equal(CursorKind.Hand, node.Input!.Cursor);
                Assert.True(node.Input.BlocksPointer);

                Element box = Box();
                Assert.Throws<InvalidOperationException>(() => box.Gap(1));
                Assert.Throws<InvalidOperationException>(() => box.FontSize(12));
                return Column(row, box);
            });
            scene.Dispose();
        });

    /// <summary>
    /// A width and a height given as fractions size the node's border box by the parent's content box, and follow the parent when
    /// its size changes (design §2.13, "The component API").
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A scene of a box 120 x 60 with a padding of 10, so its content box is 100 x 40, holding a box of width fraction 0.5 and
    ///   height fraction 0.25, laid out by the first frame.
    ///
    /// Steps:
    /// - Read the inner box's style and rectangle. Set the outer box's width to 180 and its height to 100 (a content box of
    ///   160 x 80), run the frame asked for, and read the rectangle again.
    ///
    /// Expected:
    /// - The style holds the percentages 0.5 and 0.25; the inner box is 50 x 10 at (10, 10), and 80 x 20 after the change.
    /// </remarks>
    [Fact]
    public void WidthAndHeightFraction_OfTheParentsContentBox_FollowTheParent() =>
        ui.Invoke(() =>
        {
            var scene = new Scene();
            Element outer = default;
            Element inner = default;
            scene.Mount(() =>
            {
                inner = Box().WidthFraction(0.5f).HeightFraction(0.25f);
                return outer = Box(inner).Width(120).Height(60).Padding(10);
            });
            scene.Scheduler.Run();
            LayoutStyle style = scene.Tree.Nodes.GetRef(inner.Node).Layout.Style;
            Rect before = scene.Tree.Nodes.GetRef(inner.Node).Layout.Rect;

            _ = outer.Width(180).Height(100);
            scene.Scheduler.Run();
            Rect after = scene.Tree.Nodes.GetRef(inner.Node).Layout.Rect;

            Assert.Equal((Length.Percent(0.5f), Length.Percent(0.25f)), (style.Width, style.Height));
            Assert.Equal(new Rect(10, 10, 50, 10), before);
            Assert.Equal((80f, 20f), (after.Width, after.Height));
            scene.Dispose();
        });

    /// <summary>
    /// A width or a height fraction that is negative or not a finite number throws and leaves the node as it was; 0 and a fraction
    /// above 1 are taken.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A scene, and a box of width 30 and height 20 made in its root function.
    ///
    /// Steps:
    /// - Call <c>WidthFraction</c> and <c>HeightFraction</c> with -0.25, NaN, positive infinity, and negative infinity; read the
    ///   style. Then call <c>WidthFraction(0)</c> and <c>HeightFraction(1.5f)</c>.
    ///
    /// Expected:
    /// - For each value, both calls throw <see cref="ArgumentOutOfRangeException"/> naming <c>fraction</c>, and the style still
    ///   holds 30 and 20 pixels; then it holds the percentages 0 and 1.5.
    /// </remarks>
    [Theory]
    [InlineData(-0.25f)]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    [InlineData(float.NegativeInfinity)]
    public void WidthAndHeightFraction_NegativeOrNotFinite_Throw(float fraction) =>
        ui.Invoke(() =>
        {
            var scene = new Scene();
            scene.Mount(() =>
            {
                Element box = Box().Width(30).Height(20);
                Assert.Equal("fraction", Assert.Throws<ArgumentOutOfRangeException>(() => box.WidthFraction(fraction)).ParamName);
                Assert.Equal("fraction", Assert.Throws<ArgumentOutOfRangeException>(() => box.HeightFraction(fraction)).ParamName);
                LayoutStyle kept = scene.Tree.Nodes.GetRef(box.Node).Layout.Style;
                Assert.Equal((Length.Px(30), Length.Px(20)), (kept.Width, kept.Height));

                _ = box.WidthFraction(0).HeightFraction(1.5f);
                LayoutStyle taken = scene.Tree.Nodes.GetRef(box.Node).Layout.Style;
                Assert.Equal((Length.Percent(0), Length.Percent(1.5f)), (taken.Width, taken.Height));
                return box;
            });
            scene.Dispose();
        });

    /// <summary>
    /// A width fraction and a height fraction under a parent whose height is auto: the width follows the parent's content box, and
    /// the height has no effect, since a percentage height resolves only against a definite height (design §2.13, "割合の幅と
    /// 高さ").
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A scene of a box of width 120, padding 10, and no height, holding a box of width fraction 0.5 and height fraction 0.5,
    ///   which holds a box of 20 x 20, laid out by the first frame. The shape is that of Taffy's Chrome fixture
    ///   block_percentage_height_with_min_height_parent (tests/XylemUI.Core.Tests/Fixtures/Taffy/block.xml), where a child of
    ///   height 50% under a parent of no height takes its content's 20.
    ///
    /// Steps:
    /// - Read the middle box's rectangle and the outer box's height.
    ///
    /// Expected:
    /// - The middle box is 50 x 20 at (10, 10): half of the content box's 100 in width, and its content's 20 in height. The outer
    ///   box is 40 high (20 and the padding).
    /// </remarks>
    [Fact]
    public void HeightFraction_UnderAParentOfAutoHeight_HasNoEffect() =>
        ui.Invoke(() =>
        {
            var scene = new Scene();
            Element outer = default;
            Element middle = default;
            scene.Mount(() =>
            {
                middle = Box(Box().Width(20).Height(20)).WidthFraction(0.5f).HeightFraction(0.5f);
                return outer = Box(middle).Width(120).Padding(10);
            });
            scene.Scheduler.Run();
            Rect rect = scene.Tree.Nodes.GetRef(middle.Node).Layout.Rect;
            float outerHeight = scene.Tree.Nodes.GetRef(outer.Node).Layout.Rect.Height;

            Assert.Equal(new Rect(10, 10, 50, 20), rect);
            Assert.Equal(40f, outerHeight);
            scene.Dispose();
        });

    /// <summary>
    /// A width fraction under a parent whose width comes from its content (a child of a row with no width) counts as an automatic
    /// width while the parent's width is found, and then resolves against the parent's content box (design §2.13, "割合の幅と高さ").
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A scene of a box of width 300 holding a row, which holds a box P with no width. P holds a box of 100 x 10 and a box H of
    ///   width fraction 0.5, which holds a box of 140 x 10. Laid out by the first frame.
    ///
    /// Steps:
    /// - Read P's width and H's rectangle.
    ///
    /// Expected:
    /// - Derived by hand from CSS (Sizing 3, a percentage of an indefinite size): P is a flex item of automatic width, so its width
    ///   is its content's widest child. H's percentage has no definite basis then, so H counts as automatic, its content's 140;
    ///   P is max(100, 140) = 140 wide, which fits the row's 300. P's width is then definite, and H is 0.5 x 140 = 70 wide and 10
    ///   high at (0, 10), under the first box. P is 140 wide, and H is (0, 10, 70, 10).
    /// </remarks>
    [Fact]
    public void WidthFraction_UnderAParentSizedByItsContent_CountsAsAutoAndThenResolves() =>
        ui.Invoke(() =>
        {
            var scene = new Scene();
            Element parent = default;
            Element half = default;
            scene.Mount(() =>
            {
                half = Box(Box().Width(140).Height(10)).WidthFraction(0.5f);
                parent = Box(Box().Width(100).Height(10), half);
                return Box(Row(parent)).Width(300);
            });
            scene.Scheduler.Run();

            Assert.Equal(140f, scene.Tree.Nodes.GetRef(parent.Node).Layout.Rect.Width);
            Assert.Equal(new Rect(0, 10, 70, 10), scene.Tree.Nodes.GetRef(half.Node).Layout.Rect);
            scene.Dispose();
        });

    /// <summary>
    /// A width fraction and a height fraction set a node's border box: its own padding and border are inside the fraction, not
    /// added to it.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A scene of a box 120 x 60 with a padding of 10 (a content box of 100 x 40), holding a box of width fraction 0.5 and height
    ///   fraction 0.5 with a padding of 4 and a border 2 wide, laid out by the first frame.
    ///
    /// Steps:
    /// - Read the inner box's rectangle.
    ///
    /// Expected:
    /// - 50 x 20 at (10, 10), not 62 x 32.
    /// </remarks>
    [Fact]
    public void WidthAndHeightFraction_WithOwnPaddingAndBorder_SetTheBorderBox() =>
        ui.Invoke(() =>
        {
            var scene = new Scene();
            Element inner = default;
            scene.Mount(() =>
            {
                inner = Box().WidthFraction(0.5f).HeightFraction(0.5f).Padding(4).Border(2, new Paint(Red));
                return Box(inner).Width(120).Height(60).Padding(10);
            });
            scene.Scheduler.Run();

            Assert.Equal(new Rect(10, 10, 50, 20), scene.Tree.Nodes.GetRef(inner.Node).Layout.Rect);
            scene.Dispose();
        });

    /// <summary>
    /// A smallest width widens a node whose content or width is narrower and leaves a wider one; a negative or not finite width
    /// throws and leaves the style.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A scene of a row of three boxes 10 high: one with no width and a smallest width of 60; one of width 30 with a smallest width
    ///   of 50; one of width 80 with a smallest width of 50; laid out by the first frame.
    ///
    /// Steps:
    /// - Read the three rectangles and the first box's style. Call <c>MinWidth</c> on the first box with -1, NaN, and positive
    ///   infinity, and read its style.
    ///
    /// Expected:
    /// - The boxes are 60, 50, and 80 wide, at x 0, 60, and 110; the style holds 60 pixels. Each call throws
    ///   <see cref="ArgumentOutOfRangeException"/> naming <c>width</c>, and the style still holds 60 pixels.
    /// </remarks>
    [Fact]
    public void MinWidth_NarrowerContentOrWidth_IsWidened() =>
        ui.Invoke(() =>
        {
            var scene = new Scene();
            Element[] boxes = new Element[3];
            scene.Mount(() =>
            {
                boxes[0] = Box().MinWidth(60).Height(10);
                boxes[1] = Box().Width(30).MinWidth(50).Height(10);
                boxes[2] = Box().Width(80).MinWidth(50).Height(10);
                return Row(boxes);
            });
            scene.Scheduler.Run();
            (float X, float Width)[] placed = Array.ConvertAll(
                boxes,
                box => (scene.Tree.Nodes.GetRef(box.Node).Layout.Rect.X, scene.Tree.Nodes.GetRef(box.Node).Layout.Rect.Width)
            );
            Length set = scene.Tree.Nodes.GetRef(boxes[0].Node).Layout.Style.MinWidth;

            string?[] names =
            [
                Assert.Throws<ArgumentOutOfRangeException>(() => boxes[0].MinWidth(-1)).ParamName,
                Assert.Throws<ArgumentOutOfRangeException>(() => boxes[0].MinWidth(float.NaN)).ParamName,
                Assert.Throws<ArgumentOutOfRangeException>(() => boxes[0].MinWidth(float.PositiveInfinity)).ParamName,
            ];
            Length kept = scene.Tree.Nodes.GetRef(boxes[0].Node).Layout.Style.MinWidth;

            Assert.Equal([(0f, 60f), (60f, 50f), (110f, 80f)], placed);
            Assert.Equal(Length.Px(60), set);
            Assert.Equal(["width", "width", "width"], names);
            Assert.Equal(Length.Px(60), kept);
            scene.Dispose();
        });

    /// <summary>
    /// The border's paint alone changes the paint of a node's border and leaves its width: the node is marked to be drawn again
    /// with no layout, and the same paint again changes nothing (design §13.2).
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A scene of a box with a border 2 wide in red, laid out and drawn by the first frame.
    ///
    /// Steps:
    /// - Set the border's paint to blue; read the box and run the frame asked for. Set it to blue again and read the box.
    ///
    /// Expected:
    /// - After blue: the paint is blue, the border is still 2 wide, the box is marked for paint alone, no layout root is queued, the
    ///   box is in the paint updates, and a frame is asked for. After blue again: nothing is marked and no frame is asked for.
    /// </remarks>
    [Fact]
    public void BorderPaint_ChangesThePaintAlone_AndTheSamePaintDoesNothing() =>
        ui.Invoke(() =>
        {
            var scene = new Scene();
            Element box = default;
            scene.Mount(() => box = Box().Width(20).Height(20).Border(2, new Paint(Red)));
            scene.Scheduler.Run();
            Assert.False(scene.Scheduler.Pending);

            Element same = box.BorderPaint(new Paint(Blue));
            ref UiNode node = ref scene.Tree.Nodes.GetRef(box.Node);
            (Paint? Paint, LengthPercentage Width, DirtyFlags Self, DirtyFlags Subtree) changed = (
                node.Visual!.BorderPaint,
                node.Layout.Style.Border.Left,
                node.Dirty.SelfDirty,
                node.Dirty.SubtreeDirty
            );
            bool queuedLayout = scene.Tree.LayoutRoots.Count > 0;
            bool inPaintUpdates = scene.Tree.PaintUpdates.Contains(box.Node);
            bool askedFor = scene.Scheduler.Pending;
            scene.Scheduler.Run();

            _ = box.BorderPaint(new Paint(Blue));
            DirtyFlags againSelf = scene.Tree.Nodes.GetRef(box.Node).Dirty.SelfDirty;
            bool askedForAgain = scene.Scheduler.Pending;

            Assert.Equal(box, same);
            Assert.Equal(new Paint(Blue), changed.Paint);
            Assert.Equal(
                (LengthPercentage.Px(2), DirtyFlags.Paint, DirtyFlags.None),
                (changed.Width, changed.Self, changed.Subtree & DirtyFlags.Layout)
            );
            Assert.False(queuedLayout);
            Assert.True(inPaintUpdates);
            Assert.True(askedFor);
            Assert.Equal(DirtyFlags.None, againSelf);
            Assert.False(askedForAgain);
            scene.Dispose();
        });

    /// <summary>
    /// The named lines and the automatic repetitions of the grid's API check their values (design §3.3, "The shape of P02f"),
    /// and the same placement by name is the same line.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - None.
    ///
    /// Steps:
    /// - Call each factory with a name or a number out of its range, a repetition with no track, a repetition inside another, and
    ///   an automatic repetition of no kind; make the same placements by name twice.
    ///
    /// Expected:
    /// - <see cref="ArgumentNullException"/> for a null name, <see cref="ArgumentException"/> for an empty name, <c>span</c>,
    ///   <c>auto</c>, no name, no track, and a nested repetition, and <see cref="ArgumentOutOfRangeException"/> for the numbers;
    ///   the same placement gives the same line, and the name alone a line apart from its first line.
    /// </remarks>
    [Fact]
    public void GridNamesApi_OutOfRange_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => GridLine.Named(null!));
        Assert.Throws<ArgumentException>(() => GridLine.Named(string.Empty));
        Assert.Throws<ArgumentException>(() => GridLine.Named("span"));
        Assert.Throws<ArgumentException>(() => GridLine.Named("auto", 1));
        Assert.Throws<ArgumentException>(() => GridLine.Span("span"));
        Assert.Throws<ArgumentOutOfRangeException>(() => GridLine.Named("a", 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => GridLine.Named("a", 10_001));
        Assert.Throws<ArgumentOutOfRangeException>(() => GridLine.Named("a", -10_001));
        Assert.Throws<ArgumentOutOfRangeException>(() => GridLine.Span("a", 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => GridLine.Span("a", 10_001));
        Assert.Throws<ArgumentException>(() => Track.Line());
        Assert.Throws<ArgumentException>(() => Track.Line("a", string.Empty));
        Assert.Throws<ArgumentException>(() => Track.Repeat(2, Track.Line("a")));
        Assert.Throws<ArgumentException>(() => Track.Repeat(2, Track.Repeat(2, Track.Auto)));
        Assert.Throws<ArgumentException>(() => Track.Repeat(Repeat.AutoFill));
        Assert.Throws<ArgumentOutOfRangeException>(() => Track.Repeat((Repeat)0, Track.Px(10)));

        Assert.Equal(GridLine.Named("a", 10_000), GridLine.Named("a", 10_000));
        Assert.Equal(GridLine.Span("a", 2), GridLine.Span("a", 2));
        Assert.NotEqual(GridLine.Named("a"), GridLine.Named("a", 1));
        Assert.NotEqual(GridLine.Named("a", 2), GridLine.Span("a", 2));
        Assert.Equal(GridLineKind.Named, GridLine.Named("a", -10_000).Kind);
    }

    /// <summary>
    /// The table of named placements gives each distinct placement a number until it is full, and then throws; each number is a
    /// two-byte line apart from the lines and the spans (design §3.3, "how lines are held").
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A table of its own that holds two placements, apart from the table of the process.
    ///
    /// Steps:
    /// - Add the placements "a" 1 and "b" 1, "a" 1 again, then "c" 1; make the lines of the first and last numbers of each range of
    ///   values, and read them back.
    ///
    /// Expected:
    /// - The numbers 0, 1, and 0; <see cref="InvalidOperationException"/> for the third placement; each line is named and gives
    ///   its number back: 0, 12,765 (the last above the spans), 12,766 (the first below the lines), and 25,532.
    /// </remarks>
    [Fact]
    public void GridLineNames_PastCapacity_Throws()
    {
        var table = new GridLineNames(2);
        Assert.Equal((0, 1, 0), (table.PlacementId("a", 1, false), table.PlacementId("b", 1, false), table.PlacementId("a", 1, false)));
        Assert.Throws<InvalidOperationException>(() => table.PlacementId("c", 1, false));

        foreach (int id in (int[])[0, 12_765, 12_766, GridLineNames.Capacity - 1])
        {
            GridLine line = GridLine.FromPlacement(id);
            Assert.Equal((GridLineKind.Named, id), (line.Kind, line.Value));
        }

        Assert.Equal(25_533, GridLineNames.Capacity);
    }

    /// <summary>
    /// A grid places its children by the names of its lines and fills an automatic repetition with as many tracks as fit, as the
    /// API writes them (design §3.3, "The shape of P02f").
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A window of 200 x 100 whose root holds a grid of the columns `[start] 20px repeat(auto-fill, [col] 40px)`, auto rows of
    ///   10 px, and a dense flow, with three boxes: one at the second line named col to the line named start back, one spanning
    ///   from line 1 to the second line named col, and one without lines.
    ///
    /// Steps:
    /// - Mount, which lays the window out; read the boxes' rectangles.
    ///
    /// Expected:
    /// - Four repeated columns fit in 180 px; (0, 0, 60 x 10) for the first box, columns 1 and 2 by the names; (0, 10, 60 x 10)
    ///   for the second, from line 1 to the third line, the second named col after it; (60, 0, 40 x 10) for the third, which the
    ///   dense flow puts beside the first.
    /// </remarks>
    [Fact]
    public void Grid_NamedLinesAndAutoFill_PlaceTheChildren() =>
        ui.Invoke(() =>
        {
            var scene = new Scene();
            Element first = default;
            Element second = default;
            Element third = default;
            scene.Mount(() =>
                Grid(
                        [Track.Line("start"), Track.Px(20), Track.Repeat(Repeat.AutoFill, Track.Line("col"), Track.Px(40))],
                        [],
                        first = Box().GridColumn(GridLine.Named("start"), GridLine.Named("col", 2)),
                        second = Box().GridColumn(GridLine.At(1), GridLine.Span("col", 2)).GridRow(2, span: 1),
                        third = Box()
                    )
                    .AutoRows(Track.Px(10))
                    .AutoFlow(GridAutoFlow.RowDense)
            );

            Assert.Equal(
                (new Rect(0, 0, 60, 10), new Rect(0, 10, 60, 10), new Rect(60, 0, 40, 10)),
                (scene.Tree.GetRect(first.Node), scene.Tree.GetRect(second.Node), scene.Tree.GetRect(third.Node))
            );
            scene.Dispose();
        });

    /// <summary>
    /// A grid places its children in its tracks: the explicit columns, the rows the auto-placement adds with the auto row size,
    /// the gap between, and a child's own lines and span (design §3.3, "The shape of P02f").
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A window of 200 x 100 whose root holds a grid of the columns 50 px and 1fr, no rows, auto rows of 30 px, and a gap of 4,
    ///   with three boxes: one 10 high, one 20 high in column 2, and one 10 high from column 1 across two columns.
    ///
    /// Steps:
    /// - Mount, which lays the window out; read the boxes' rectangles.
    ///
    /// Expected:
    /// - (0, 0, 50 x 10), (54, 0, 146 x 20), and (0, 34, 200 x 10): the flexible column takes the 146 left after the gap, the
    ///   third box goes to the second row, 30 and the gap below.
    /// </remarks>
    [Fact]
    public void Grid_PlacesItsChildren_InItsTracks() =>
        ui.Invoke(() =>
        {
            var scene = new Scene();
            Element first = default;
            Element second = default;
            Element third = default;
            scene.Mount(() =>
                Grid(
                        [Track.Px(50), Track.Fr(1)],
                        [],
                        first = Box().Height(10),
                        second = Box().Height(20).GridColumn(2),
                        third = Box().Height(10).GridColumn(1, span: 2)
                    )
                    .Gap(4)
                    .AutoRows(Track.Px(30))
            );

            Assert.Equal(
                (new Rect(0, 0, 50, 10), new Rect(54, 0, 146, 20), new Rect(0, 34, 200, 10)),
                (scene.Tree.GetRect(first.Node), scene.Tree.GetRect(second.Node), scene.Tree.GetRect(third.Node))
            );
            scene.Dispose();
        });

    /// <summary>
    /// A child's new lines lay the grid out again: the style change marks the grid, and the child moves.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A window of 200 x 100 whose root holds a grid of two columns of 100 px and the rows 10 px and auto, with a box 10 high in
    ///   the first cell, laid out.
    ///
    /// Steps:
    /// - Put the box in row 2, column 2; run the layout pass; read its rectangle.
    ///
    /// Expected:
    /// - (100, 10, 100 x 10): the second column of the second row, below the first row of 10.
    /// </remarks>
    [Fact]
    public void GridChild_NewLines_MoveIt() =>
        ui.Invoke(() =>
        {
            var scene = new Scene();
            Element box = default;
            scene.Mount(() => Grid([Track.Px(100), Track.Px(100)], [Track.Px(10), Track.Auto], box = Box().Height(10)));
            Assert.Equal(new Rect(0, 0, 100, 10), scene.Tree.GetRect(box.Node));

            UiTree.Current = scene.Tree;
            try
            {
                _ = box.GridRow(2).GridColumn(2);
            }
            finally
            {
                UiTree.Current = null;
            }

            LayoutEngine.RunLayoutPass(scene.Tree);
            Assert.Equal(new Rect(100, 10, 100, 10), scene.Tree.GetRect(box.Node));
            scene.Dispose();
        });

    /// <summary>
    /// The sizes, lines, and spans of the grid's API refuse values out of their ranges, the minmax() track refuses a flexible
    /// minimum and a fit-content() bound, and the grid's own properties refuse a node that is not a grid. A percentage is out of
    /// 100, and line 0 is auto.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A scene.
    ///
    /// Steps:
    /// - Call each factory with a value out of its range; call minmax() with 1fr as the minimum and fit-content(10) as the maximum;
    ///   set the auto-flow on a box and the auto rows on a row. Read a 50 % track and line 0.
    ///
    /// Expected:
    /// - <see cref="ArgumentOutOfRangeException"/> for the ranges, <see cref="ArgumentException"/> for minmax() and an empty
    ///   repetition, <see cref="InvalidOperationException"/> for the properties; the percentage is the fraction 0.5 and line 0 is
    ///   <see cref="GridLine.Auto"/>; a column flow writes the grid's property.
    /// </remarks>
    [Fact]
    public void GridApi_OutOfRange_Throws() =>
        ui.Invoke(() =>
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => Track.Px(-1));
            Assert.Throws<ArgumentOutOfRangeException>(() => Track.Fr(float.NaN));
            Assert.Throws<ArgumentOutOfRangeException>(() => Track.Percent(float.PositiveInfinity));
            Assert.Throws<ArgumentOutOfRangeException>(() => Track.FitContent(-1));
            Assert.Throws<ArgumentException>(() => Track.MinMax(Track.Fr(1), Track.Auto));
            Assert.Throws<ArgumentException>(() => Track.MinMax(Track.Auto, Track.FitContent(10)));
            Assert.Throws<ArgumentOutOfRangeException>(() => Track.Repeat(0, Track.Auto));
            Assert.Throws<ArgumentOutOfRangeException>(() => Track.Repeat(10_001, Track.Auto));
            Assert.Throws<ArgumentException>(() => Track.Repeat(1));
            Assert.Throws<ArgumentOutOfRangeException>(() => GridLine.At(10_002));
            Assert.Throws<ArgumentOutOfRangeException>(() => GridLine.At(-10_002));
            Assert.Throws<ArgumentOutOfRangeException>(() => GridLine.Span(0));
            Assert.Throws<ArgumentOutOfRangeException>(() => GridLine.Span(10_001));
            Assert.Equal(0.5f, Track.Percent(50).Max.Value);
            Assert.Equal(GridLine.Auto, GridLine.At(0));
            Assert.Equal((GridLineKind.Line, 10_001), (GridLine.At(10_001).Kind, GridLine.At(10_001).Value));
            Assert.Equal((GridLineKind.Span, 10_000), (GridLine.Span(10_000).Kind, GridLine.Span(10_000).Value));

            var scene = new Scene();
            scene.Mount(() =>
            {
                Assert.Throws<InvalidOperationException>(() => Box().AutoFlow(GridAutoFlow.Column));
                Assert.Throws<InvalidOperationException>(() => Row().AutoRows(Track.Auto));
                Element grid = Grid([Track.Auto], []).AutoFlow(GridAutoFlow.Column);
                Assert.Equal(GridAutoFlow.Column, ((LayoutAlgorithm.Grid)scene.Tree.Nodes.GetRef(grid.Node).Layout.Style.Algorithm).Props.AutoFlow);
                return grid;
            });
            scene.Dispose();
        });

    /// <summary>
    /// <c>Bind</c> writes the signal's value to the node when it is made, and once for each batch that changes the signal, however
    /// many times the batch set it; a value equal to the one written writes again through the property but changes nothing.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A box bound to a signal of colors through a property that counts its writes and writes the background.
    ///
    /// Steps:
    /// - Mount. In one batch, set the signal to red, gray, then blue.
    ///
    /// Expected:
    /// - One write at the mount; one more after the batch, of blue, which the background has.
    /// </remarks>
    [Fact]
    public void Bind_OneBatch_WritesOnce() =>
        ui.Invoke(() =>
        {
            var scene = new Scene();
            var color = Signal.Create(Gray);
            var written = new List<Color>();
            var property = new NodeProperty<Color>(
                "Counted",
                (tree, node, value) =>
                {
                    written.Add(value);
                    tree.SetBackground(node, new Paint(value));
                }
            );
            Element box = default;
            scene.Mount(() => box = Box().Bind(property, color));
            Assert.Equal([Gray], written);

            ReactiveRuntime.Batch(() =>
            {
                color.Set(Red);
                color.Set(Gray);
                color.Set(Blue);
            });
            Assert.Equal([Gray, Blue], written);
            Assert.Equal(new Paint(Blue), scene.Tree.Nodes.GetRef(box.Node).Visual!.Background);
            scene.Dispose();
        });

    /// <summary>
    /// The overloads that take a signal bind it: the background and the color of text follow the signal, and the frame draws the new
    /// background.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A box of 50 x 50 whose background and foreground take signals.
    ///
    /// Steps:
    /// - Mount and draw. Set both signals; run the frame asked for.
    ///
    /// Expected:
    /// - The look has the new background and foreground; the frame presents.
    /// </remarks>
    [Fact]
    public void SignalOverloads_FollowTheSignal() =>
        ui.Invoke(() =>
        {
            var scene = new Scene();
            var background = Signal.Create(new Paint(Gray));
            var foreground = Signal.Create(Gray);
            Element box = default;
            scene.Mount(() => box = Box().Width(50).Height(50).Background(background).Foreground(foreground));
            int presents = scene.Target.Presents;

            background.Set(new Paint(Red));
            foreground.Set(Blue);
            scene.Scheduler.Run();
            NodeVisual visual = scene.Tree.Nodes.GetRef(box.Node).Visual!;
            Assert.Equal(new Paint(Red), visual.Background);
            Assert.Equal(Blue, visual.Foreground);
            Assert.Equal(presents + 1, scene.Target.Presents);
            scene.Dispose();
        });

    /// <summary>
    /// A click's handler runs in the scope it was registered in: what it makes belongs to that scope, so disposing the scope disposes
    /// it; after the scope is current again outside the handler.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A box whose click handler reads the current scope and makes an effect that counts its runs on a signal.
    ///
    /// Steps:
    /// - Mount; click outside any scope. Set the signal. Dispose the root scope; set the signal again.
    ///
    /// Expected:
    /// - The handler saw the root scope; the scope is none again after the click. The effect ran once at the click and once for the
    ///   first set, and not after the dispose.
    /// </remarks>
    [Fact]
    public void Handler_RunsInTheScopeItWasRegisteredIn() =>
        ui.Invoke(() =>
        {
            var scene = new Scene();
            var signal = Signal.Create(0);
            ScopeHandle seen = default;
            int runs = 0;
            scene.Mount(() =>
                Box()
                    .Width(50)
                    .Height(50)
                    .OnClick(() =>
                    {
                        seen = Scope.Current;
                        _ = Effect.Create(() =>
                        {
                            _ = signal.Get();
                            runs++;
                        });
                    })
            );

            scene.Click(10, 10);
            Assert.Equal(scene.RootScope, seen);
            Assert.True(Scope.Current.IsNone);
            signal.Set(1);
            Assert.Equal(2, runs);

            scene.Dispose();
            signal.Set(2);
            Assert.Equal(2, runs);
        });

    /// <summary>
    /// A handler whose scope was disposed is not called: the one <c>OnClick</c> registered is removed with the scope, and one left on
    /// the node by the tree's own call is skipped by the delivery.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A box of the root scope. An inner scope adds a click handler with <c>OnClick</c> and one with the tree's
    ///   <c>AddPointerHandler</c>, which registers no cleanup.
    ///
    /// Steps:
    /// - Click; dispose the inner scope; click again.
    ///
    /// Expected:
    /// - The first click calls both; the second calls neither, and the node keeps one handler entry, the tree's.
    /// </remarks>
    [Fact]
    public void Handler_OfADisposedScope_IsNotCalled() =>
        ui.Invoke(() =>
        {
            var scene = new Scene();
            int chained = 0;
            int direct = 0;
            ScopeHandle inner = default;
            scene.Mount(() =>
            {
                Element box = Box().Width(50).Height(50);
                inner = Scope.Create(() =>
                {
                    _ = box.OnClick(() => chained++);
                    scene.Tree.AddPointerHandler(box.Node, PointerEventKind.Click, EventPhase.Bubble, (in PointerEvent _) => direct++);
                });
                return box;
            });

            scene.Click(10, 10);
            Assert.Equal((1, 1), (chained, direct));
            Scope.Dispose(inner);
            scene.Click(10, 10);
            Assert.Equal((1, 1), (chained, direct));
            scene.Dispose();
        });

    /// <summary>
    /// Disposing the root scope leaves nothing: the nodes the root function made are out of the tree and freed, with their handlers,
    /// and no effect or scope of it is alive.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - The scope tracker on. A column of a box with a click handler, a box bound to a signal, and an inner scope's box.
    ///
    /// Steps:
    /// - Mount and draw; read the nodes and the scopes. Dispose the root scope; run the frame asked for.
    ///
    /// Expected:
    /// - Before: the root and four nodes, and live scopes. After: the root alone with no child, no live scope of the mount, and the
    ///   frame draws the empty root.
    /// </remarks>
    [Fact]
    public void RootScope_Disposed_LeavesNothing() =>
        ui.Invoke(() =>
        {
            ScopeTracker.IsEnabled = true;
            try
            {
                int scopesBefore = LiveScopes();
                var scene = new Scene();
                var color = Signal.Create(new Paint(Gray));
                scene.Mount(() =>
                {
                    Element inner = default;
                    _ = Scope.Create(() => inner = Box());
                    return Column(Box().OnClick(() => { }), Box().Background(color), inner);
                });
                Assert.Equal(5, scene.Tree.Nodes.Count);
                Assert.True(LiveScopes() > scopesBefore);

                Scope.Dispose(scene.RootScope);
                Assert.Equal(1, scene.Tree.Nodes.Count);
                Assert.Empty(scene.ChildrenOf(scene.Root));
                Assert.Equal(scopesBefore, LiveScopes());
                int presents = scene.Target.Presents;
                scene.Scheduler.Run();
                Assert.Equal(presents + 1, scene.Target.Presents);
            }
            finally
            {
                ScopeTracker.IsEnabled = false;
            }
        });

    /// <summary>
    /// A draw function that throws is reported; the scopes it opened are closed, its node's children and siblings are drawn, and the
    /// frame presents (design §5.0, "The exceptions of draw functions").
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A column of a box whose draw function opens an opacity, fills a gray square in it, and throws, with a red child, and a blue
    ///   sibling; the reactive
    ///   runtime's handler records what it is given.
    ///
    /// Steps:
    /// - Mount and draw.
    ///
    /// Expected:
    /// - One exception reported, the one thrown; the frame presented; the list's scopes are all closed: the opacity with the gray
    ///   square, its pop, then the red child's rectangle and the blue sibling's.
    /// </remarks>
    [Fact]
    public void DrawFunction_Throws_IsReported_AndTheFrameGoesOn() =>
        ui.Invoke(() =>
        {
            var reported = new List<Exception>();
            Action<Exception> previous = ReactiveRuntime.UnhandledExceptionHandler;
            ReactiveRuntime.UnhandledExceptionHandler = reported.Add;
            try
            {
                var thrown = new InvalidOperationException("thrown by the draw function");
                var scene = new Scene();
                scene.Mount(() =>
                    Column(
                        Box(Box().Width(10).Height(10).Background(new Paint(Red)))
                            .Draw(
                                (DrawingContext context, Rect box) =>
                                {
                                    _ = context.PushOpacity(0.5f);
                                    context.FillRect(0, 0, 5, 5, Gray);
                                    throw thrown;
                                }
                            ),
                        Box().Width(20).Height(20).Background(new Paint(Blue))
                    )
                );

                Assert.Equal([thrown], reported);
                Assert.Equal(1, scene.Target.Presents);
                Assert.Equal(0, scene.Frames.List.ScopeDepth);
                Assert.Equal(
                    [CommandTag.PushOpacity, CommandTag.FillRect, CommandTag.Pop, CommandTag.FillRect, CommandTag.FillRect],
                    Tags(scene.Frames.List, out List<Color> fills)
                );
                Assert.Equal([Gray, Red, Blue], fills);
                scene.Dispose();
            }
            finally
            {
                ReactiveRuntime.UnhandledExceptionHandler = previous;
            }
        });

    /// <summary>
    /// The steady state of the component API allocates nothing: a frame that a signal changes the background in, and a click whose
    /// handler sets a signal, delivered with the enter of its scope.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A box of 50 x 50 whose background is bound to a signal and whose click handler sets it; warmed up with the iteration.
    ///
    /// Steps:
    /// - 1,000 iterations in the windows of <c>PerformanceAssert.DoesNotAllocate</c>: press and release on the box, then run the frame asked for.
    ///
    /// Expected:
    /// - 0 bytes in every window; each iteration clicked once and presented once.
    /// </remarks>
    [Fact]
    public void SteadyState_ClickThatChangesTheBackground_AllocatesNothing() =>
        ui.Invoke(() =>
        {
            var scene = new Scene();
            var red = new Paint(Red);
            var gray = new Paint(Gray);
            var background = Signal.Create(gray);
            int clicks = 0;
            scene.Mount(() =>
                Box()
                    .Width(50)
                    .Height(50)
                    .Background(background)
                    .OnClick(() =>
                    {
                        clicks++;
                        background.Set((clicks & 1) == 0 ? gray : red);
                    })
            );

            void Iteration(int index)
            {
                scene.Click(10, 10);
                scene.Scheduler.Run();
            }

            for (int i = 0; i < 20; i++)
            {
                Iteration(i);
            }

            int presents = scene.Target.Presents;
            clicks = 0;
            int runs = PerformanceAssert.DoesNotAllocate(1_000, Iteration);

            // The helper runs 100 unmeasured iterations before the 1,000, and a window again when a background GC overlapped it.
            Assert.Equal(runs, clicks);
            Assert.Equal(presents + runs, scene.Target.Presents);
            scene.Dispose();
        });

    /// <summary>
    /// An element is a tree and a node: the default one is none, and two are equal when both are.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A scene.
    ///
    /// Steps:
    /// - Make two boxes; compare them, and the first with itself and the default.
    ///
    /// Expected:
    /// - The default is none; a box equals itself and not the other box or the default.
    /// </remarks>
    [Fact]
    public void Element_EqualsByTreeAndNode() =>
        ui.Invoke(() =>
        {
            Assert.True(default(Element).IsNone);
            var scene = new Scene();
            scene.Mount(() =>
            {
                Element a = Box();
                Element b = Box();
                Element same = a;
                Assert.False(a.IsNone);
                Assert.True(a == same);
                Assert.True(a != b);
                Assert.NotEqual(default, a);
                Assert.Equal(a.GetHashCode(), same.GetHashCode());
                return Column(a, b);
            });
            scene.Dispose();
        });

    /// <summary>
    /// <c>JustifyContent(Center)</c> on a row centers its children along the main axis, and writes the unsafe center to the row's
    /// flex properties (design §2.13, "The alignment of a container").
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A window of 200 x 100 whose root holds a row of 200 x 100 with boxes of 20 x 10 and 30 x 10.
    ///
    /// Steps:
    /// - Set the row's justify-content to <see cref="JustifyContent.Center"/> while it is made; mount; read the boxes' rectangles.
    ///
    /// Expected:
    /// - The free space is 200 - 20 - 30 = 150, so the first box starts at 150 / 2 = 75 and the second at 75 + 20 = 95: (75, 0,
    ///   20 x 10) and (95, 0, 30 x 10). The row's justify-content is center with <c>Safe</c> false.
    /// </remarks>
    [Fact]
    public void JustifyContent_CenterInARow_CentersTheChildrenAlongTheMainAxis() =>
        ui.Invoke(() =>
        {
            var scene = new Scene();
            Element row = default;
            Element first = default;
            Element second = default;
            scene.Mount(() =>
                row = Row(first = Box().Width(20).Height(10), second = Box().Width(30).Height(10))
                    .Width(200)
                    .Height(100)
                    .JustifyContent(JustifyContent.Center)
            );

            Assert.Equal((new Rect(75, 0, 20, 10), new Rect(95, 0, 30, 10)), (scene.Tree.GetRect(first.Node), scene.Tree.GetRect(second.Node)));
            Assert.Equal(new ContentAlignment(AlignContentKeyword.Center, Safe: false), FlexOf(scene, row).JustifyContent);
            scene.Dispose();
        });

    /// <summary>
    /// <c>JustifyContent(SpaceBetween)</c> on a row puts the free space between its children in equal parts and none at the ends.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A window of 200 x 100 whose root holds a row of 200 x 100 with boxes of 20 x 10, 30 x 10, and 50 x 10.
    ///
    /// Steps:
    /// - Set the row's justify-content to <see cref="JustifyContent.SpaceBetween"/>; mount; read the boxes' rectangles.
    ///
    /// Expected:
    /// - The free space is 200 - 100 = 100, in two gaps of 50: the boxes start at 0, 20 + 50 = 70, and 70 + 30 + 50 = 150.
    /// </remarks>
    [Fact]
    public void JustifyContent_SpaceBetweenInARow_PutsTheFreeSpaceBetweenTheChildren() =>
        ui.Invoke(() =>
        {
            var scene = new Scene();
            Element first = default;
            Element second = default;
            Element third = default;
            scene.Mount(() =>
                Row(first = Box().Width(20).Height(10), second = Box().Width(30).Height(10), third = Box().Width(50).Height(10))
                    .Width(200)
                    .Height(100)
                    .JustifyContent(JustifyContent.SpaceBetween)
            );

            Assert.Equal(
                (new Rect(0, 0, 20, 10), new Rect(70, 0, 30, 10), new Rect(150, 0, 50, 10)),
                (scene.Tree.GetRect(first.Node), scene.Tree.GetRect(second.Node), scene.Tree.GetRect(third.Node))
            );
            scene.Dispose();
        });

    /// <summary>
    /// <c>AlignItems(Center)</c> on a column centers its children across, a child's own alignment wins, and the column's flex
    /// properties hold the unsafe center (design §2.13, "The alignment of a container").
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A window of 200 x 100 whose root holds a column of 200 x 100 with boxes of 40 x 10, 60 x 20, and 20 x 10; the third box's
    ///   own alignment is end.
    ///
    /// Steps:
    /// - Set the column's align-items to <see cref="AlignItems.Center"/>; mount; read the boxes' rectangles.
    ///
    /// Expected:
    /// - The first box at x = (200 - 40) / 2 = 80, the second at (200 - 60) / 2 = 70, the third at 200 - 20 = 180; down the column
    ///   at y = 0, 10, and 10 + 20 = 30. The column's align-items is center with <c>Safe</c> false.
    /// </remarks>
    [Fact]
    public void AlignItems_CenterInAColumn_CentersTheChildrenAcross_AndAChildsOwnAlignmentWins() =>
        ui.Invoke(() =>
        {
            var scene = new Scene();
            Element column = default;
            Element first = default;
            Element second = default;
            Element third = default;
            scene.Mount(() =>
            {
                third = Box().Width(20).Height(10);
                OwnAlignment(scene, third, AlignItemsKeyword.End);
                column = Column(first = Box().Width(40).Height(10), second = Box().Width(60).Height(20), third)
                    .Width(200)
                    .Height(100)
                    .AlignItems(AlignItems.Center);
                return column;
            });

            Assert.Equal(
                (new Rect(80, 0, 40, 10), new Rect(70, 10, 60, 20), new Rect(180, 30, 20, 10)),
                (scene.Tree.GetRect(first.Node), scene.Tree.GetRect(second.Node), scene.Tree.GetRect(third.Node))
            );
            Assert.Equal(new ItemAlignment(AlignItemsKeyword.Center, Safe: false), FlexOf(scene, column).AlignItems);
            scene.Dispose();
        });

    /// <summary>
    /// On a grid, <c>JustifyContent(Center)</c> centers the columns in the content box and <c>AlignItems(Center)</c> centers each
    /// child along its cell's height; a child's own alignment wins (design §2.13, "The alignment of a container").
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A window of 200 x 100 whose root holds a grid of 200 x 100 with three columns of 50 px and one row of 40 px, and boxes of
    ///   10 x 10, 20 x 20, and 10 x 10 in the three cells; the third box's own alignment is end.
    ///
    /// Steps:
    /// - Set the grid's justify-content and align-items to center; mount; read the boxes' rectangles.
    ///
    /// Expected:
    /// - The columns take 150, so they start at (200 - 150) / 2 = 25: the cells start at 25, 75, and 125, and each box at its
    ///   cell's start, since it has a width. Down the row of 40: the first box at (40 - 10) / 2 = 15, the second at (40 - 20) / 2 =
    ///   10, the third at 40 - 10 = 30. The grid's properties hold the two unsafe centers.
    /// </remarks>
    [Fact]
    public void JustifyContentAndAlignItems_CenterInAGrid_CenterTheTracksAndTheChildrenInTheirCells() =>
        ui.Invoke(() =>
        {
            var scene = new Scene();
            Element grid = default;
            Element first = default;
            Element second = default;
            Element third = default;
            scene.Mount(() =>
            {
                third = Box().Width(10).Height(10);
                OwnAlignment(scene, third, AlignItemsKeyword.End);
                grid = Grid(
                        [Track.Px(50), Track.Px(50), Track.Px(50)],
                        [Track.Px(40)],
                        first = Box().Width(10).Height(10),
                        second = Box().Width(20).Height(20),
                        third
                    )
                    .Width(200)
                    .Height(100)
                    .JustifyContent(JustifyContent.Center)
                    .AlignItems(AlignItems.Center);
                return grid;
            });

            Assert.Equal(
                (new Rect(25, 15, 10, 10), new Rect(75, 10, 20, 20), new Rect(125, 30, 10, 10)),
                (scene.Tree.GetRect(first.Node), scene.Tree.GetRect(second.Node), scene.Tree.GetRect(third.Node))
            );
            GridProps props = GridOf(scene, grid);
            Assert.Equal(
                (new ContentAlignment(AlignContentKeyword.Center, Safe: false), new ItemAlignment(AlignItemsKeyword.Center, Safe: false)),
                (props.JustifyContent, props.AlignItems)
            );
            scene.Dispose();
        });

    /// <summary>
    /// The alignments the methods write are unsafe (CSS's default): content larger than the room is centered past both of its
    /// edges, not moved to the start (design §2.13, "The alignment of a container").
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A window of 200 x 100 whose root holds a grid of 100 x 100 with one column of 160 px and one row of 40 px, and a box of
    ///   10 x 60 in its cell.
    ///
    /// Steps:
    /// - Set the grid's justify-content and align-items to center; mount; read the box's rectangle.
    ///
    /// Expected:
    /// - The column starts at (100 - 160) / 2 = -30 and the box at (40 - 60) / 2 = -10 in the row: (-30, -10, 10 x 60). A safe
    ///   center would give (0, 0).
    /// </remarks>
    [Fact]
    public void JustifyContentAndAlignItems_ContentLargerThanTheRoom_IsCenteredUnsafe() =>
        ui.Invoke(() =>
        {
            var scene = new Scene();
            Element box = default;
            scene.Mount(() =>
                Grid([Track.Px(160)], [Track.Px(40)], box = Box().Width(10).Height(60))
                    .Width(100)
                    .Height(100)
                    .JustifyContent(JustifyContent.Center)
                    .AlignItems(AlignItems.Center)
            );

            Assert.Equal(new Rect(-30, -10, 10, 60), scene.Tree.GetRect(box.Node));
            scene.Dispose();
        });

    /// <summary>
    /// Each public value maps to the keyword of the same name, unsafe, on a row and on a grid: the public enums have the values of
    /// <see cref="AlignContentKeyword"/> and <see cref="AlignItemsKeyword"/> in the same order.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A scene with a row and a grid of one column.
    ///
    /// Steps:
    /// - Count the values of each public enum and of its keyword enum. Set each value of <see cref="JustifyContent"/> and of
    ///   <see cref="AlignItems"/> in turn on the row and on the grid, and read the property after each.
    ///
    /// Expected:
    /// - The same counts (10 and 8); after each set, the property is the keyword whose name is the value's, with <c>Safe</c> false.
    /// </remarks>
    [Fact]
    public void JustifyContentAndAlignItems_EachValue_MapsToTheKeywordOfTheSameName() =>
        ui.Invoke(() =>
        {
            Assert.Equal((10, 10), (Enum.GetValues<JustifyContent>().Length, Enum.GetValues<AlignContentKeyword>().Length));
            Assert.Equal((8, 8), (Enum.GetValues<AlignItems>().Length, Enum.GetValues<AlignItemsKeyword>().Length));
            var scene = new Scene();
            Element row = default;
            Element grid = default;
            scene.Mount(() => Column(row = Row(), grid = Grid([Track.Px(50)], [])));

            foreach (JustifyContent value in Enum.GetValues<JustifyContent>())
            {
                var expected = new ContentAlignment(Enum.Parse<AlignContentKeyword>(value.ToString()), Safe: false);
                _ = row.JustifyContent(value);
                _ = grid.JustifyContent(value);
                Assert.Equal((expected, expected), (FlexOf(scene, row).JustifyContent, GridOf(scene, grid).JustifyContent));
            }

            foreach (AlignItems value in Enum.GetValues<AlignItems>())
            {
                var expected = new ItemAlignment(Enum.Parse<AlignItemsKeyword>(value.ToString()), Safe: false);
                _ = row.AlignItems(value);
                _ = grid.AlignItems(value);
                Assert.Equal((expected, expected), (FlexOf(scene, row).AlignItems, GridOf(scene, grid).AlignItems));
            }

            scene.Dispose();
        });

    /// <summary>
    /// The value a row or a grid already has, written again, changes nothing: no new style, no mark, no layout root, and no frame;
    /// another value marks the container for layout and asks for a frame.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A column of a row with justify-content end and align-items center, and a grid of one column of 50 px with the same two
    ///   values, laid out and drawn by the first frame.
    ///
    /// Steps:
    /// - Write justify-content end and align-items center again to each; read the styles' versions, the marks, the layout roots,
    ///   and the frame asked for. Then write justify-content center to the row and align-items start to the grid, and read them
    ///   again.
    ///
    /// Expected:
    /// - After the same values: both versions as before, neither node marked, no layout root queued, no frame asked for. After
    ///   the other values: both versions one more, the row and the grid marked for layout, a layout root queued, and a frame asked
    ///   for.
    /// </remarks>
    [Fact]
    public void JustifyContentAndAlignItems_TheSameValueAgain_ChangeNothing() =>
        ui.Invoke(() =>
        {
            var scene = new Scene();
            Element row = default;
            Element grid = default;
            scene.Mount(() =>
                Column(
                    row = Row(Box().Width(20).Height(10)).JustifyContent(JustifyContent.End).AlignItems(AlignItems.Center),
                    grid = Grid([Track.Px(50)], [], Box().Width(10).Height(10)).JustifyContent(JustifyContent.End).AlignItems(AlignItems.Center)
                )
            );
            scene.Scheduler.Run();
            Assert.False(scene.Scheduler.Pending);
            ulong rowVersion = scene.Tree.Nodes.GetRef(row.Node).Versions.LayoutStyle;
            ulong gridVersion = scene.Tree.Nodes.GetRef(grid.Node).Versions.LayoutStyle;

            _ = row.JustifyContent(JustifyContent.End).AlignItems(AlignItems.Center);
            _ = grid.JustifyContent(JustifyContent.End).AlignItems(AlignItems.Center);
            (ulong Row, ulong Grid, DirtyFlags RowSelf, DirtyFlags GridSelf, int Roots, bool Pending) same = (
                scene.Tree.Nodes.GetRef(row.Node).Versions.LayoutStyle,
                scene.Tree.Nodes.GetRef(grid.Node).Versions.LayoutStyle,
                scene.Tree.Nodes.GetRef(row.Node).Dirty.SelfDirty,
                scene.Tree.Nodes.GetRef(grid.Node).Dirty.SelfDirty,
                scene.Tree.LayoutRoots.Count,
                scene.Scheduler.Pending
            );

            _ = row.JustifyContent(JustifyContent.Center);
            _ = grid.AlignItems(AlignItems.Start);
            (ulong Row, ulong Grid, DirtyFlags RowSelf, DirtyFlags GridSelf, bool Roots, bool Pending) other = (
                scene.Tree.Nodes.GetRef(row.Node).Versions.LayoutStyle,
                scene.Tree.Nodes.GetRef(grid.Node).Versions.LayoutStyle,
                scene.Tree.Nodes.GetRef(row.Node).Dirty.SelfDirty & DirtyFlags.Layout,
                scene.Tree.Nodes.GetRef(grid.Node).Dirty.SelfDirty & DirtyFlags.Layout,
                scene.Tree.LayoutRoots.Count > 0,
                scene.Scheduler.Pending
            );

            Assert.Equal((rowVersion, gridVersion, DirtyFlags.None, DirtyFlags.None, 0, false), same);
            Assert.Equal((rowVersion + 1, gridVersion + 1, DirtyFlags.Layout, DirtyFlags.Layout, true, true), other);
            scene.Dispose();
        });

    /// <summary>
    /// <c>JustifyContent</c> and <c>AlignItems</c> work on a flex or a grid container only: on a box, a block, they throw and leave
    /// its style as it was.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A scene with a box.
    ///
    /// Steps:
    /// - Call <c>JustifyContent(Center)</c> and <c>AlignItems(Center)</c> on the box.
    ///
    /// Expected:
    /// - Each throws <see cref="InvalidOperationException"/>; the box's layout is still the block with CSS's initial values.
    /// </remarks>
    [Fact]
    public void JustifyContentAndAlignItems_OnABox_Throw() =>
        ui.Invoke(() =>
        {
            var scene = new Scene();
            Element box = default;
            scene.Mount(() => box = Box());

            Assert.Throws<InvalidOperationException>(() => box.JustifyContent(JustifyContent.Center));
            Assert.Throws<InvalidOperationException>(() => box.AlignItems(AlignItems.Center));
            Assert.Equal(LayoutAlgorithm.Block.Instance, scene.Tree.Nodes.GetRef(box.Node).Layout.Style.Algorithm);
            scene.Dispose();
        });

    /// <summary>
    /// Gap on a virtualized list writes the gap of the list's layout, between its rows (design §3.4, "The shape of P02e"), and the
    /// container alignment throws on it: the list has a gap and no container alignment.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A scene with a box that scrolls vertically and holds a virtualized list of three items.
    ///
    /// Steps:
    /// - Call <c>Gap(5)</c> on the box; then <c>JustifyContent(Center)</c>.
    ///
    /// Expected:
    /// - The box's layout is the virtualized list's with a gap of 5 pixels, and the alignment throws
    ///   <see cref="InvalidOperationException"/>.
    /// </remarks>
    [Fact]
    public void Gap_OnAVirtualizedList_WritesTheListsGap_AndAlignmentThrows() =>
        ui.Invoke(() =>
        {
            var scene = new Scene();
            Element list = default;
            IReadOnlySignal<IReadOnlyList<int>> items = Signal.Create<IReadOnlyList<int>>([1, 2, 3]);
            scene.Mount(() =>
                list = Box().Overflow(Overflow.Visible, Overflow.Scroll).Height(100).VirtualFor(items, static item => item, static _ => Box())
            );

            _ = list.Gap(5);

            Assert.Equal(
                LengthPercentage.Px(5),
                Assert.IsType<LayoutAlgorithm.Virtual>(scene.Tree.Nodes.GetRef(list.Node).Layout.Style.Algorithm).Props.Gap
            );
            Assert.Throws<InvalidOperationException>(() => list.JustifyContent(JustifyContent.Center));
            scene.Dispose();
        });

    /// <summary>
    /// A value outside the public enum throws, on a row and on a grid, and leaves the property as it was; the last values of
    /// each enum are taken.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A scene with a row and a grid of one column.
    ///
    /// Steps:
    /// - Call <c>JustifyContent</c> with 10 and 255 and <c>AlignItems</c> with 8 and 255 on each; then <c>SpaceEvenly</c> (9) and
    ///   <c>Stretch</c> (7).
    ///
    /// Expected:
    /// - The four calls with a value outside throw <see cref="ArgumentOutOfRangeException"/> on each node, and the properties are
    ///   still normal; the last two write space-evenly and stretch.
    /// </remarks>
    [Fact]
    public void JustifyContentAndAlignItems_UndefinedValue_Throws() =>
        ui.Invoke(() =>
        {
            var scene = new Scene();
            Element row = default;
            Element grid = default;
            scene.Mount(() => Column(row = Row(), grid = Grid([Track.Px(50)], [])));

            Element[] containers = [row, grid];
            foreach (Element container in containers)
            {
                Assert.Throws<ArgumentOutOfRangeException>(() => container.JustifyContent((JustifyContent)10));
                Assert.Throws<ArgumentOutOfRangeException>(() => container.JustifyContent((JustifyContent)255));
                Assert.Throws<ArgumentOutOfRangeException>(() => container.AlignItems((AlignItems)8));
                Assert.Throws<ArgumentOutOfRangeException>(() => container.AlignItems((AlignItems)255));
            }

            Assert.Equal((default(ContentAlignment), default(ItemAlignment)), (FlexOf(scene, row).JustifyContent, FlexOf(scene, row).AlignItems));
            Assert.Equal((default(ContentAlignment), default(ItemAlignment)), (GridOf(scene, grid).JustifyContent, GridOf(scene, grid).AlignItems));
            _ = row.JustifyContent(JustifyContent.SpaceEvenly).AlignItems(AlignItems.Stretch);
            Assert.Equal(
                (new ContentAlignment(AlignContentKeyword.SpaceEvenly), new ItemAlignment(AlignItemsKeyword.Stretch)),
                (FlexOf(scene, row).JustifyContent, FlexOf(scene, row).AlignItems)
            );
            scene.Dispose();
        });

    /// <summary>
    /// <c>JustifyContent</c> and <c>AlignItems</c> throw on a thread that is not the UI thread, and write nothing.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A row made and mounted on the UI thread; the test's own thread is another.
    ///
    /// Steps:
    /// - On the test's thread, call <c>JustifyContent(Center)</c> and <c>AlignItems(Center)</c> on the row; then read its flex
    ///   properties on the UI thread.
    ///
    /// Expected:
    /// - Each call throws <see cref="InvalidOperationException"/>; the row's justify-content and align-items are still normal.
    /// </remarks>
    [Fact]
    public void JustifyContentAndAlignItems_OffTheUiThread_Throw()
    {
        Scene? scene = null;
        Element row = default;
        ui.Invoke(() =>
        {
            scene = new Scene();
            scene.Mount(() => row = Row());
        });

        Assert.Throws<InvalidOperationException>(() => row.JustifyContent(JustifyContent.Center));
        Assert.Throws<InvalidOperationException>(() => row.AlignItems(AlignItems.Center));

        ui.Invoke(() =>
        {
            FlexProps props = FlexOf(scene!, row);
            Assert.Equal((default(ContentAlignment), default(ItemAlignment)), (props.JustifyContent, props.AlignItems));
            scene!.Dispose();
        });
    }

    // The flex properties of a row or a column.
    private static FlexProps FlexOf(Scene scene, Element element) =>
        ((LayoutAlgorithm.Flex)scene.Tree.Nodes.GetRef(element.Node).Layout.Style.Algorithm).Props;

    // The properties of a grid.
    private static GridProps GridOf(Scene scene, Element element) =>
        ((LayoutAlgorithm.Grid)scene.Tree.Nodes.GetRef(element.Node).Layout.Style.Algorithm).Props;

    // Gives a child its own alignment across (LayoutStyle.AlignSelf), which has no public method before row P02h.
    private static void OwnAlignment(Scene scene, Element child, AlignItemsKeyword keyword) =>
        scene.Tree.SetStyle(child.Node, scene.Tree.Nodes.GetRef(child.Node).Layout.Style with { AlignSelf = new ItemAlignment(keyword) });

    private static int LiveScopes()
    {
        int count = 0;
        ScopeTracker.ForEachActiveScope(_ => count++);
        return count;
    }

    // The tags of the list's commands in order, and the colors of its rectangles.
    private static List<CommandTag> Tags(DisplayList list, out List<Color> fills)
    {
        var tags = new List<CommandTag>();
        fills = [];
        DisplayListReader reader = list.GetReader();
        while (reader.TryNext(out CommandTag tag))
        {
            tags.Add(tag);
            switch (tag)
            {
                case CommandTag.FillRect:
                    fills.Add(reader.ReadFillRect().Color);
                    break;
                case CommandTag.PushOpacity:
                    _ = reader.ReadGroup();
                    break;
                case CommandTag.Pop:
                    reader.ReadPop();
                    break;
                default:
                    throw new InvalidOperationException($"The test reads no {tag}.");
            }
        }

        return tags;
    }

    // A window of 200 x 100 logical pixels with no text: its tree, frames, and input, and the root scope of what it mounted.
    private sealed class Scene
    {
        public Scene()
        {
            Tree = new UiTree();
            Root = Tree.CreateNode(new LayoutStyle());
            Atlas = new GlyphAtlas(new NoGlyphs());
            Frames = new WindowFrames(Tree, Root, Target, Atlas, Scheduler, Gray);
            Input = new WindowInput(Tree, Frames, new NoPlatform());
        }

        public UiTree Tree { get; }

        public NodeHandle Root { get; }

        public GlyphAtlas Atlas { get; }

        public CountingTarget Target { get; } = new();

        public CountingScheduler Scheduler { get; } = new();

        public WindowFrames Frames { get; }

        public WindowInput Input { get; }

        public ScopeHandle RootScope { get; private set; }

        // Runs the root function as the window's host does, puts its element in the root, and draws the first frame.
        public void Mount(Func<Element> root)
        {
            UiTree.Current = Tree;
            try
            {
                RootScope = Scope.Create(() => Tree.AppendChild(Root, root().Node));
            }
            finally
            {
                UiTree.Current = null;
            }

            Frames.Resize(new Size(200, 100), 1);
        }

        public void Dispose()
        {
            if (Scope.IsAlive(RootScope))
            {
                Scope.Dispose(RootScope);
            }

            Atlas.Dispose();
        }

        public List<NodeHandle> ChildrenOf(NodeHandle node)
        {
            var children = new List<NodeHandle>();
            for (NodeHandle child = Tree.Nodes.GetRef(node).FirstChild; !child.IsNone; child = Tree.Nodes.GetRef(child).NextSibling)
            {
                children.Add(child);
            }

            return children;
        }

        public void Click(int x, int y)
        {
            Input.Handle(
                new PointerInput(
                    PointerInputKind.Down,
                    x,
                    y,
                    PointerButton.Left,
                    PointerButtons.Left,
                    KeyModifiers.None,
                    1,
                    TimeSpan.Zero,
                    PointerType.Mouse
                )
            );
            Input.Handle(
                new PointerInput(
                    PointerInputKind.Up,
                    x,
                    y,
                    PointerButton.Left,
                    PointerButtons.None,
                    KeyModifiers.None,
                    1,
                    TimeSpan.Zero,
                    PointerType.Mouse
                )
            );
        }
    }

    private sealed class NoPlatform : IPointerPlatform
    {
        public void Capture() { }

        public void ReleaseCapture() { }

        public void SetCursor(CursorKind cursor) { }
    }
}
