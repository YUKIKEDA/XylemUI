using static XylemUI.Elements;

namespace XylemUI.Text.Tests;

// The text style a container gives the text leaves below it (row TS1, design §2.13 and §3.2, "The text style a container gives"):
// the order a leaf's fields are taken in, the walk a container's change makes, the leaves it marks, the subtrees taken out and put
// under a parent, the language a leaf keeps, the entries a freed node takes out, and the setters' allocations. A leaf is marked when its style changed: ChangeKind.Font sets DirtyFlags.Text, which nothing else
// sets on a leaf whose text stays.
[Collection(UiThreadGroup.Name)]
public sealed class InheritedTextStyleTests(UiThreadFixture ui) : IDisposable
{
    private static readonly FontWeight Light = new(300);
    private static readonly FontWeight Black = new(900);

    private readonly Font _font = TestFont.Open();

    public void Dispose() => _font.Dispose();

    /// <summary>
    /// Each field of a leaf takes the leaf's own value, else the nearest container's, else a farther container's, else the default
    /// of text, field by field; a size the leaf takes turns a line height in pixels into a multiple of it.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A column (far) of size 20, weight 700, the family "Far", a line height of 30 px, italic, and expanded, around a row (near)
    ///   of weight 300 and a line height of 24 px, around a text with no field of its own (a) and a text of its own size 10 and weight
    ///   900 (b); beside the column, a text outside both (c). The near row's values are set before it is put in the far column, and
    ///   the far column's after.
    ///
    /// Steps:
    /// - Mount; read the three styles.
    ///
    /// Expected:
    /// - a: "Far", 300, italic, expanded, 20, and 24 / 20. b: "Far", 900, italic, expanded, 10, and 24 / 10. c: the defaults, the OS's
    ///   UI font (no family), 400, normal, normal, 14, and the fonts' line height (NaN).
    /// </remarks>
    [Fact]
    public void Leaf_TakesItsOwn_ThenTheNearest_ThenAFarther_ThenTheDefault() =>
        ui.Invoke(() =>
        {
            using var scene = new TextScene(_font);
            Element a = default;
            Element b = default;
            Element c = default;
            scene.Mount(() =>
            {
                a = Elements.Text("a");
                b = Elements.Text("b").FontSize(10).FontWeight(Black);
                Element near = Row(a, b).FontWeight(Light).LineHeight(24);
                Element far = Column(near)
                    .FontSize(20)
                    .FontWeight(FontWeight.Bold)
                    .FontFamily("Far")
                    .LineHeight(30)
                    .FontSlant(FontSlant.Italic)
                    .FontStretch(FontStretch.Expanded);
                c = Elements.Text("c");
                return Column(far, c);
            });

            Assert.Equal(("Far", 300, FontSlant.Italic, FontStretch.Expanded, 20f, 24f / 20f), Fields(scene, a));
            Assert.Equal(("Far", 900, FontSlant.Italic, FontStretch.Expanded, 10f, 24f / 10f), Fields(scene, b));
            Assert.Equal(((string?)null, 400, FontSlant.Normal, FontStretch.Normal, 14f, float.NaN), Fields(scene, c));
        });

    /// <summary>
    /// A container's change walks its subtree, stops at a leaf's own field and below a container that holds the field, and marks
    /// only the leaves whose style changed: a leaf with its own weight is marked when the size changes.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A column of size 16 around a text with no field of its own (plain), a text of its own size 10 (own size), a text of its own
    ///   weight 700 (own weight), and a row of size 30 around a text with no field of its own (deep); mounted and drawn.
    ///
    /// Steps:
    /// - Set the column's size to 20; read which leaves are marked, the leaves the walk resolved, and the sizes; draw. Set the size
    ///   20 again; read the same; draw. Set the column's weight to 900; read the same.
    ///
    /// Expected:
    /// - Before: no leaf marked. Size 20: plain and own weight marked, own size and deep not; three leaves resolved (the row's
    ///   leaf is not walked); sizes 20, 10, 20, and 30. Size 20 again: no leaf marked, none resolved. Weight 900: plain, own size, and
    ///   deep marked, own weight not; four leaves resolved (the row holds no weight).
    /// </remarks>
    [Fact]
    public void ContainerChange_MarksTheLeavesThatChanged_AndStopsBelowTheSameField() =>
        ui.Invoke(() =>
        {
            using var scene = new TextScene(_font);
            Element column = default;
            Element[] leaves = new Element[4];
            scene.Mount(() =>
            {
                leaves[0] = Elements.Text("a");
                leaves[1] = Elements.Text("b").FontSize(10);
                leaves[2] = Elements.Text("c").FontWeight(FontWeight.Bold);
                leaves[3] = Elements.Text("d");
                return column = Column(leaves[0], leaves[1], leaves[2], Row(leaves[3]).FontSize(30)).FontSize(16);
            });
            scene.Draw();
            bool[] before = Marks(scene, leaves);

            long resolved = scene.Tree.TextLeavesResolved;
            _ = column.FontSize(20);
            bool[] sized = Marks(scene, leaves);
            long sizeWalk = scene.Tree.TextLeavesResolved - resolved;
            float[] sizes = Array.ConvertAll(leaves, leaf => scene.Leaf(leaf).Style.Size);
            scene.Draw();

            resolved = scene.Tree.TextLeavesResolved;
            _ = column.FontSize(20);
            bool[] same = Marks(scene, leaves);
            long sameWalk = scene.Tree.TextLeavesResolved - resolved;
            scene.Draw();

            resolved = scene.Tree.TextLeavesResolved;
            _ = column.FontWeight(Black);
            bool[] weighted = Marks(scene, leaves);
            long weightWalk = scene.Tree.TextLeavesResolved - resolved;

            Assert.Equal([false, false, false, false], before);
            Assert.Equal([true, false, true, false], sized);
            Assert.Equal(3, sizeWalk);
            Assert.Equal([20f, 10f, 20f, 30f], sizes);
            Assert.Equal([false, false, false, false], same);
            Assert.Equal(0, sameWalk);
            Assert.Equal([true, true, false, true], weighted);
            Assert.Equal(4, weightWalk);
        });

    /// <summary>
    /// A change of a farther container stops below a nearer one that holds the field and reaches the leaves through one that does
    /// not; a line height in pixels follows the size the leaf takes.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A column (far) of size 14 and a line height of 20 px, around a row (near) of weight 300 around a text with no field of its
    ///   own (a) and a text of its own size 10 (b); mounted and drawn.
    ///
    /// Steps:
    /// - Set far's size to 16; read a's and b's size and line height, and the marks; draw. Set near's line height to 24; read the
    ///   same; draw. Set far's line height to 40; read the leaves resolved and the marks.
    ///
    /// Expected:
    /// - Far's size: a 16 and 20 / 16, marked; b 10 and 20 / 10, not marked. Near's line height: a 24 / 16 and b 24 / 10, both marked.
    ///   Far's line height: no leaf resolved (near holds the line height now) and none marked.
    /// </remarks>
    [Fact]
    public void LineHeightInPixels_FollowsTheSize_AndAFartherChangeStopsAtANearerHolder() =>
        ui.Invoke(() =>
        {
            using var scene = new TextScene(_font);
            Element far = default;
            Element near = default;
            Element[] leaves = new Element[2];
            scene.Mount(() =>
            {
                leaves[0] = Elements.Text("a");
                leaves[1] = Elements.Text("b").FontSize(10);
                near = Row(leaves).FontWeight(Light);
                return far = Column(near).FontSize(14).LineHeight(20);
            });
            scene.Draw();

            _ = far.FontSize(16);
            (float, float)[] sized = Array.ConvertAll(leaves, leaf => SizeAndLine(scene, leaf));
            bool[] sizeMarks = Marks(scene, leaves);
            scene.Draw();

            _ = near.LineHeight(24);
            (float, float)[] lined = Array.ConvertAll(leaves, leaf => SizeAndLine(scene, leaf));
            bool[] lineMarks = Marks(scene, leaves);
            scene.Draw();

            long resolved = scene.Tree.TextLeavesResolved;
            _ = far.LineHeight(40);
            long farWalk = scene.Tree.TextLeavesResolved - resolved;
            bool[] farMarks = Marks(scene, leaves);

            Assert.Equal([(16f, 20f / 16f), (10f, 20f / 10f)], sized);
            Assert.Equal([true, false], sizeMarks);
            Assert.Equal([(16f, 24f / 16f), (10f, 24f / 10f)], lined);
            Assert.Equal([true, true], lineMarks);
            Assert.Equal(0, farWalk);
            Assert.Equal([false, false], farMarks);
        });

    /// <summary>
    /// A value equal to the one there changes nothing, on a container and on a leaf; a leaf given the value it took becomes its
    /// own, and keeps it when the container changes; <c>FontFamily(null)</c> on a leaf is the OS's UI font as its own value.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A column of size 20 and the family "Far" around a text of its own size 10 (own) and a text with no field of its own (taken);
    ///   mounted and drawn.
    ///
    /// Steps:
    /// - Set the column's family "Far" again, own's size 10 again, and taken's size 20 (the size it takes); read the marks and the
    ///   frames asked for. Set taken's family to null; read its mark; draw. Set the column's size to 25; read taken's size and
    ///   family, and the marks.
    ///
    /// Expected:
    /// - No leaf marked and no frame asked for after the equal values. Taken is marked by its family of null. After the column's
    ///   change, taken keeps 20 and has no family, and neither text is marked: both have their own size.
    /// </remarks>
    [Fact]
    public void EqualValue_ChangesNothing_AndALeafsValueIsItsOwn() =>
        ui.Invoke(() =>
        {
            using var scene = new TextScene(_font);
            Element column = default;
            Element[] leaves = new Element[2];
            scene.Mount(() =>
            {
                leaves[0] = Elements.Text("a").FontSize(10);
                leaves[1] = Elements.Text("b");
                return column = Column(leaves).FontSize(20).FontFamily("Far");
            });
            scene.Draw();
            int requests = scene.Scheduler.Requests;

            _ = column.FontFamily("Far");
            _ = leaves[0].FontSize(10);
            _ = leaves[1].FontSize(20);
            bool[] equal = Marks(scene, leaves);
            int equalRequests = scene.Scheduler.Requests - requests;

            _ = leaves[1].FontFamily(null);
            bool familyMark = Marks(scene, leaves)[1];
            scene.Draw();
            _ = column.FontSize(25);
            TextStyle taken = scene.Leaf(leaves[1]).Style;

            Assert.Equal([false, false], equal);
            Assert.Equal(0, equalRequests);
            Assert.True(familyMark);
            Assert.Equal(20f, taken.Size);
            Assert.Null(taken.Font.Family);
            Assert.Equal([false, false], Marks(scene, leaves));
        });

    /// <summary>
    /// A subtree put in the tree takes the styles of its new ancestors: a row a list adds, the part <c>Show</c> swaps in, and a
    /// subtree moved to another parent; a row the list moves within the container is not walked again and not marked.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A column of size 20 holding a list of the items 1, 2, and 3 (each row a text), and a <c>Show</c> of a signal, true, whose
    ///   part and fallback are texts; mounted and drawn.
    ///
    /// Steps:
    /// - Set the items to 3, 1, 2 (the list moves a row); read the leaves resolved and the rows' marks; draw. Set the items to 3, 1,
    ///   2, 4; read the new row's size. Set the signal to false; read the fallback's size.
    ///
    /// Expected:
    /// - The move resolves no leaf and marks no row, which keep size 20. The new row and the fallback are at 20.
    /// </remarks>
    [Fact]
    public void SubtreePutInTheTree_TakesItsNewAncestors_AndAMovedRowIsNotWalked() =>
        ui.Invoke(() =>
        {
            using var scene = new TextScene(_font);
            Signal<IReadOnlyList<int>> items = Signal.Create<IReadOnlyList<int>>([1, 2, 3]);
            Signal<bool> shown = Signal.Create(true);
            var rows = new Dictionary<int, Element>();
            Element fallback = default;
            scene.Mount(() =>
                Column(
                        Column().For(items, static item => item, item => rows[item.Get()] = Elements.Text("n")),
                        Show(shown, () => Elements.Text("shown"), () => fallback = Elements.Text("fallback"))
                    )
                    .FontSize(20)
            );
            scene.Draw();

            long resolved = scene.Tree.TextLeavesResolved;
            items.Set([3, 1, 2]);
            long moveWalk = scene.Tree.TextLeavesResolved - resolved;
            bool[] moveMarks = Marks(scene, [rows[1], rows[2], rows[3]]);
            float[] moved = [Size(scene, rows[1]), Size(scene, rows[2]), Size(scene, rows[3])];
            scene.Draw();

            items.Set([3, 1, 2, 4]);
            float added = Size(scene, rows[4]);
            shown.Set(false);
            float swapped = Size(scene, fallback);

            Assert.Equal(0, moveWalk);
            Assert.Equal([false, false, false], moveMarks);
            Assert.Equal([20f, 20f, 20f], moved);
            Assert.Equal(20f, added);
            Assert.Equal(20f, swapped);
        });

    /// <summary>
    /// A subtree taken out keeps what it took until it is put under a parent again: under a parent that gives other values it is
    /// resolved again, put back where it was it is not walked, and a change made inside it meanwhile keeps the values it took.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A column of two boxes: the first of size 20 around a row around a text with no field of its own; the second with no text
    ///   style; mounted and drawn.
    ///
    /// Steps:
    /// - Take the row out of the first box; read the text's size. Put it in the second box; read the size and the mark. Take it out
    ///   and put it back in the first; read the size. Take it out; set the row's weight to 700; put it back in the first; read the
    ///   leaves resolved by the putting back, the size, and the weight. Take it out; set the first box's size to 25; put it back;
    ///   read the size.
    ///
    /// Expected:
    /// - 20 while out; 14 (the default) in the second box, marked; 20 back in the first. After the weight: no leaf resolved by the
    ///   putting back, 20 and 700. After the box's change: 25.
    /// </remarks>
    [Fact]
    public void SubtreeTakenOut_KeepsWhatItTook_UntilItIsPutUnderAParent() =>
        ui.Invoke(() =>
        {
            using var scene = new TextScene(_font);
            Element first = default;
            Element second = default;
            Element row = default;
            Element text = default;
            scene.Mount(() =>
            {
                text = Elements.Text("n");
                row = Row(text);
                first = Box(row).FontSize(20);
                second = Box();
                return Column(first, second);
            });
            scene.Draw();
            UiTree tree = scene.Tree;

            tree.DetachAfter(first.Node, default, row.Node);
            float outside = Size(scene, text);
            tree.AppendChild(second.Node, row.Node);
            float underSecond = Size(scene, text);
            bool marked = Marks(scene, [text])[0];
            tree.DetachAfter(second.Node, default, row.Node);
            tree.AppendChild(first.Node, row.Node);
            float back = Size(scene, text);

            tree.DetachAfter(first.Node, default, row.Node);
            _ = row.FontWeight(FontWeight.Bold);
            long resolved = tree.TextLeavesResolved;
            tree.AppendChild(first.Node, row.Node);
            long putBack = tree.TextLeavesResolved - resolved;
            (float Size, int Weight) weighted = (Size(scene, text), scene.Leaf(text).Style.Font.Weight.Value);

            tree.DetachAfter(first.Node, default, row.Node);
            _ = first.FontSize(25);
            tree.AppendChild(first.Node, row.Node);
            float changed = Size(scene, text);

            Assert.Equal(20f, outside);
            Assert.Equal(14f, underSecond);
            Assert.True(marked);
            Assert.Equal(20f, back);
            Assert.Equal(0, putBack);
            Assert.Equal((20f, 700), weighted);
            Assert.Equal(25f, changed);
        });

    /// <summary>
    /// The text style setters allocate nothing once a container holds a style: a change of a container's fields and of a leaf's own
    /// fields, each walking and marking the leaves it changes.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A column around a row around two texts, one with no field of its own and one of its own size; the column given each of the
    ///   six fields once, and the iteration run four times with the frame each asked for.
    ///
    /// Steps:
    /// - 1,000 runs in the windows of <c>PerformanceAssert.DoesNotAllocate</c>, its unmeasured window and the windows it runs again
    ///   included, each after the frame the run before asked for (unmeasured): by the run's index, even or odd, set the column's
    ///   size 16 or 18, weight 600 or 700, family null or "Other", slant normal or italic, stretch normal or condensed, and line
    ///   height 20 or 22 px, and the second text's size 10 or 12 and line height 15 or 17 px.
    ///
    /// Expected:
    /// - 0 bytes in every window; at the end the first text has the odd runs' values, those of the last run (index 999): 18, 700,
    ///   "Other", and 22 / 18.
    /// </remarks>
    [Fact]
    public void Setters_OfAContainerAndALeaf_AllocateNothing() =>
        ui.Invoke(() =>
        {
            using var scene = new TextScene(_font);
            Element column = default;
            Element taken = default;
            Element own = default;
            scene.Mount(() =>
            {
                taken = Elements.Text("a");
                own = Elements.Text("b").FontSize(10);
                return column = Column(Row(taken, own))
                    .FontSize(16)
                    .FontWeight(new FontWeight(600))
                    .FontFamily(null)
                    .FontSlant(FontSlant.Normal)
                    .FontStretch(FontStretch.Normal)
                    .LineHeight(20);
            });
            var semibold = new FontWeight(600);

            void Iteration(int index)
            {
                bool even = (index & 1) == 0;
                _ = column
                    .FontSize(even ? 16 : 18)
                    .FontWeight(even ? semibold : FontWeight.Bold)
                    .FontFamily(even ? null : "Other")
                    .FontSlant(even ? FontSlant.Normal : FontSlant.Italic)
                    .FontStretch(even ? FontStretch.Normal : FontStretch.Condensed)
                    .LineHeight(even ? 20 : 22);
                _ = own.FontSize(even ? 10 : 12).LineHeight(even ? 15 : 17);
            }

            for (int i = 0; i < 4; i++)
            {
                scene.Draw();
                Iteration(i);
            }

            _ = PerformanceAssert.DoesNotAllocate(1_000, _ => scene.Draw(), Iteration);

            // The last run's index is 999, an odd one.
            TextStyle last = scene.Leaf(taken).Style;
            Assert.Equal((18f, 700, "Other", 22f / 18f), (last.Size, last.Font.Weight.Value, last.Font.Family, last.LineHeight));
        });

    /// <summary>
    /// A leaf's own setter resolves its other fields from its ancestors, where the nearer of two containers that hold the same field
    /// wins (the walk up from the leaf).
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A column (far) of size 20, weight 700, and a line height of 30 px around a row (near) of weight 300 and a line height of
    ///   24 px around a text of its own size 10 (b); mounted.
    ///
    /// Steps:
    /// - Set b's slant to normal, its own; read b's style.
    ///
    /// Expected:
    /// - b: weight 300 and a line height of 24 / 10 (near's, not far's 700 and 30 / 10), size 10, slant normal.
    /// </remarks>
    [Fact]
    public void LeafSetter_UnderTwoHoldersOfAField_TakesTheNearerValue() =>
        ui.Invoke(() =>
        {
            using var scene = new TextScene(_font);
            Element b = default;
            scene.Mount(() =>
            {
                b = Elements.Text("b").FontSize(10);
                Element near = Row(b).FontWeight(Light).LineHeight(24);
                return Column(near).FontSize(20).FontWeight(FontWeight.Bold).LineHeight(30);
            });

            _ = b.FontSlant(FontSlant.Normal);

            Assert.Equal(((string?)null, 300, FontSlant.Normal, FontStretch.Normal, 10f, 24f / 10f), Fields(scene, b));
        });

    /// <summary>
    /// A leaf's own line height in pixels becomes a multiple of the size the leaf takes, whether the size is inherited or its own,
    /// and follows a container's change of the size; a whole style written to the leaf drops the pixels.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A column of size 16 around a text of its own line height 30 px (taken) and a text of its own size 10 and line height 30 px
    ///   (own); mounted and drawn.
    ///
    /// Steps:
    /// - Read both line heights. Set the column's size to 15; read them and the marks. Set taken's size to 12; read its line height.
    ///   Write taken's whole style with a line height of 2 (a multiple); set its size to 24; read its line height. Set the column's
    ///   size to 18; read taken's size and line height.
    ///
    /// Expected:
    /// - 30 / 16 and 30 / 10; after the column's 15, 30 / 15 (marked) and 30 / 10 (not marked); after taken's own 12, 30 / 12; after
    ///   the whole style and the size 24, 2 (not 30 / 24); after the column's 18, still 24 and 2.
    /// </remarks>
    [Fact]
    public void OwnLineHeightInPixels_FollowsTheSizeTheLeafTakes_AndAWholeStyleDropsIt() =>
        ui.Invoke(() =>
        {
            using var scene = new TextScene(_font);
            Element column = default;
            Element[] leaves = new Element[2];
            scene.Mount(() =>
            {
                leaves[0] = Elements.Text("a").LineHeight(30);
                leaves[1] = Elements.Text("b").FontSize(10).LineHeight(30);
                return column = Column(leaves).FontSize(16);
            });
            scene.Draw();
            float[] first = Array.ConvertAll(leaves, leaf => scene.Leaf(leaf).Style.LineHeight);

            _ = column.FontSize(15);
            float[] followed = Array.ConvertAll(leaves, leaf => scene.Leaf(leaf).Style.LineHeight);
            bool[] marks = Marks(scene, leaves);
            _ = leaves[0].FontSize(12);
            float ownSize = scene.Leaf(leaves[0]).Style.LineHeight;

            scene.Tree.SetTextStyle(leaves[0].Node, scene.Leaf(leaves[0]).Style with { LineHeight = 2 });
            _ = leaves[0].FontSize(24);
            float whole = scene.Leaf(leaves[0]).Style.LineHeight;
            _ = column.FontSize(18);
            (float, float) afterColumn = SizeAndLine(scene, leaves[0]);

            Assert.Equal([30f / 16f, 30f / 10f], first);
            Assert.Equal([30f / 15f, 30f / 10f], followed);
            Assert.Equal([true, false], marks);
            Assert.Equal(30f / 12f, ownSize);
            Assert.Equal(2f, whole);
            Assert.Equal((24f, 2f), afterColumn);
        });

    /// <summary>
    /// The rows a virtualized list keeps when it scrolls are taken out of its box and put back with the values they took, and are
    /// not resolved again; only the rows it makes are, and they take the column's size.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A column of size 20 around a box 200 x 100 that scrolls down, with a virtualized list of 1,000 texts; mounted and drawn.
    ///
    /// Steps:
    /// - Scroll to 100 and draw; read the leaves resolved, the rows the list made meanwhile, the rows in the box, and the rows'
    ///   sizes.
    ///
    /// Expected:
    /// - The list made at least one row and kept at least one; the leaves resolved are the rows it made, one each; every row is at
    ///   size 20.
    /// </remarks>
    [Fact]
    public void VirtualList_RelinkedInAStyledColumn_ResolvesOnlyTheRowsItMakes() =>
        ui.Invoke(() =>
        {
            using var scene = new TextScene(_font);
            Signal<IReadOnlyList<int>> items = Signal.Create<IReadOnlyList<int>>([.. Enumerable.Range(0, 1_000)]);
            var state = new VirtualListState();
            int made = 0;
            Element box = default;
            scene.Mount(() =>
                Column(
                        box = Box()
                            .Width(200)
                            .Height(100)
                            .Overflow(Overflow.Visible, Overflow.Scroll)
                            .VirtualFor(
                                items,
                                static item => item,
                                _ =>
                                {
                                    made++;
                                    return Elements.Text("n");
                                },
                                state
                            )
                    )
                    .FontSize(20)
            );
            scene.Draw();

            int madeBefore = made;
            long resolved = scene.Tree.TextLeavesResolved;
            state.Scroll.ScrollTo(new System.Numerics.Vector2(0, 100));
            scene.Draw();
            scene.Draw();
            int madeByScroll = made - madeBefore;
            long resolvedByScroll = scene.Tree.TextLeavesResolved - resolved;
            var sizes = new List<float>();
            for (Handle<UiNode> row = scene.Tree.Nodes.GetRef(box.Node).FirstChild; !row.IsNone; row = scene.Tree.Nodes.GetRef(row).NextSibling)
            {
                sizes.Add(scene.Tree.Nodes.GetRef(row).Text!.Style.Size);
            }

            Assert.InRange(madeByScroll, 1, sizes.Count - 1);
            Assert.Equal((long)madeByScroll, resolvedByScroll);
            Assert.All(sizes, size => Assert.Equal(20f, size));
        });

    /// <summary>
    /// A size or a line height that is 0, negative, or not finite, and a slant or a stretch that is not a value of its enum, throw
    /// on a container and on a leaf, and change neither.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A column of size 16 and a line height of 20 px around a text of its own size 10; mounted.
    ///
    /// Steps:
    /// - On the column and on the text: <c>FontSize</c> and <c>LineHeight</c> with 0, -1, NaN, positive infinity, and negative
    ///   infinity; <c>FontSlant</c> with 3 and 255; <c>FontStretch</c> with 0 and 10. Read the column's held style and the text's
    ///   style before and after.
    ///
    /// Expected:
    /// - Each call throws <see cref="ArgumentOutOfRangeException"/>; the held style and the text's style are as before.
    /// </remarks>
    [Fact]
    public void Setters_OutOfRange_ThrowOnAContainerAndALeaf_AndChangeNothing() =>
        ui.Invoke(() =>
        {
            using var scene = new TextScene(_font);
            Element column = default;
            Element text = default;
            scene.Mount(() =>
            {
                text = Elements.Text("a").FontSize(10);
                return column = Column(text).FontSize(16).LineHeight(20);
            });
            InheritedTextStyle heldBefore = scene.Tree.HeldTextStyleOf(column.Node);
            TextStyle styleBefore = scene.Leaf(text).Style;

            foreach (Element element in new[] { column, text })
            {
                foreach (float bad in new[] { 0f, -1f, float.NaN, float.PositiveInfinity, float.NegativeInfinity })
                {
                    Assert.Throws<ArgumentOutOfRangeException>(() => element.FontSize(bad));
                    Assert.Throws<ArgumentOutOfRangeException>(() => element.LineHeight(bad));
                }

                Assert.Throws<ArgumentOutOfRangeException>(() => element.FontSlant((FontSlant)3));
                Assert.Throws<ArgumentOutOfRangeException>(() => element.FontSlant((FontSlant)255));
                Assert.Throws<ArgumentOutOfRangeException>(() => element.FontStretch((FontStretch)0));
                Assert.Throws<ArgumentOutOfRangeException>(() => element.FontStretch((FontStretch)10));
            }

            Assert.True(heldBefore.SameAs(scene.Tree.HeldTextStyleOf(column.Node)));
            Assert.Equal(styleBefore, scene.Leaf(text).Style);
        });

    /// <summary>
    /// Resolving a leaf's style keeps its language, its own and the one the locale gives it, on a container's change and on the
    /// leaf's own setter; a container takes no language, and <c>Language</c> on it throws and changes nothing.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A scene whose locale is "ja"; a column of size 16 around a text of its own language "en" (own) and a text with no
    ///   language (taken); mounted and drawn.
    ///
    /// Steps:
    /// - Read both languages. Set the column's size to 20; read both languages and sizes. Set own's size to 12; read its language.
    ///   Call <c>Language("en")</c> on the column; read the column's held style and both languages.
    ///
    /// Expected:
    /// - "en" and "ja" before; after the column's 20, "en" and "ja" at size 20; after own's 12, "en". The column's
    ///   <c>Language</c> throws <see cref="InvalidOperationException"/>; the column still holds the size 20 alone, and the languages
    ///   are "en" and "ja".
    /// </remarks>
    [Fact]
    public void Resolve_KeepsTheLeafsLanguage_AndAContainerTakesNone() =>
        ui.Invoke(() =>
        {
            using var scene = new TextScene(_font);
            scene.UiLocale = Locale.Parse("ja");
            TextLanguage english = TextLanguage.FromTag("en");
            Element column = default;
            Element[] leaves = new Element[2];
            scene.Mount(() =>
            {
                leaves[0] = Elements.Text("a").Language(english);
                leaves[1] = Elements.Text("b");
                return column = Column(leaves).FontSize(16);
            });
            scene.Draw();
            TextLanguage[] before = Array.ConvertAll(leaves, leaf => scene.Leaf(leaf).Style.Language);

            _ = column.FontSize(20);
            (TextLanguage, float)[] resolved = Array.ConvertAll(leaves, leaf => (scene.Leaf(leaf).Style.Language, Size(scene, leaf)));
            _ = leaves[0].FontSize(12);
            TextLanguage ownSetter = scene.Leaf(leaves[0]).Style.Language;
            InheritedTextStyle heldBefore = scene.Tree.HeldTextStyleOf(column.Node);

            Assert.Throws<InvalidOperationException>(() => column.Language(english));
            InheritedTextStyle heldAfter = scene.Tree.HeldTextStyleOf(column.Node);
            TextLanguage[] after = Array.ConvertAll(leaves, leaf => scene.Leaf(leaf).Style.Language);

            Assert.Equal([english, TestFont.Japanese], before);
            Assert.Equal([(english, 20f), (TestFont.Japanese, 20f)], resolved);
            Assert.Equal(english, ownSetter);
            Assert.Equal((TextStyleFields.Size, 20f), (heldAfter.Fields, heldAfter.Size));
            Assert.True(heldBefore.SameAs(in heldAfter));
            Assert.Equal([english, TestFont.Japanese], after);
        });

    /// <summary>
    /// A freed node takes its entries out of the text style tables: a container that holds a style, a subtree taken out of a styled
    /// container, and a leaf with its own line height in pixels; the nodes made in the freed slots hold no style.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A column of size 20 around a box of stretch expanded (holder) around a text of its own line height 30 px (lined), and a row
    ///   of weight 300 (row) around a text with no field of its own; mounted and drawn.
    ///
    /// Steps:
    /// - Read the entries of the tables (held, detached, own line heights). Take the row out of the column; read them. Remove the
    ///   row; read them. Remove the holder; read them. Make four nodes; read their slots, their held styles, and their marks.
    ///
    /// Expected:
    /// - (3, 0, 1) at first; (3, 1, 1) with the row taken out; (2, 0, 1) after the row's removal; (1, 0, 0) after the holder's. The
    ///   four nodes take the four freed slots, and each holds no field and has no mark and no field of its own.
    /// </remarks>
    [Fact]
    public void Remove_TakesTheNodesOutOfTheTables_AndTheFreedSlotsStartWithNone() =>
        ui.Invoke(() =>
        {
            using var scene = new TextScene(_font);
            Element column = default;
            Element holder = default;
            Element lined = default;
            Element row = default;
            Element inRow = default;
            scene.Mount(() =>
            {
                lined = Elements.Text("a").LineHeight(30);
                holder = Box(lined).FontStretch(FontStretch.Expanded);
                inRow = Elements.Text("b");
                row = Row(inRow).FontWeight(Light);
                return column = Column(holder, row).FontSize(20);
            });
            scene.Draw();
            UiTree tree = scene.Tree;
            int[] freed = [holder.Node.Index, lined.Node.Index, row.Node.Index, inRow.Node.Index];

            (int, int, int) first = tree.TextStyleEntries;
            tree.DetachAfter(column.Node, holder.Node, row.Node);
            (int, int, int) takenOut = tree.TextStyleEntries;
            tree.Remove(row.Node);
            (int, int, int) rowRemoved = tree.TextStyleEntries;
            tree.Remove(holder.Node);
            (int, int, int) holderRemoved = tree.TextStyleEntries;

            var made = new Handle<UiNode>[4];
            for (int i = 0; i < made.Length; i++)
            {
                made[i] = tree.CreateNode(new LayoutStyle());
            }

            Assert.Equal((3, 0, 1), first);
            Assert.Equal((3, 1, 1), takenOut);
            Assert.Equal((2, 0, 1), rowRemoved);
            Assert.Equal((1, 0, 0), holderRemoved);
            Assert.Equal(freed.Order(), made.Select(node => node.Index).Order());
            Assert.All(
                made,
                node =>
                    Assert.Equal(
                        (TextStyleFields.None, TextStyleMarks.None, TextStyleFields.None),
                        (tree.HeldTextStyleOf(node).Fields, tree.Nodes.GetRef(node).TextStyleMarks, tree.Nodes.GetRef(node).TextOwnFields)
                    )
            );
        });

    /// <summary>
    /// A leaf's own setter while the leaf is out of the tree resolves its other fields from what it took when it was taken out,
    /// alone or as part of a subtree, and the leaf put under a parent then takes the parent's.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A column of size 20 around a text with no field of its own (lone) and a row of weight 300 (row) around another (in row);
    ///   mounted and drawn.
    ///
    /// Steps:
    /// - Take lone and then the row out of the column. Set lone's slant and in row's slant to italic; read their styles. Put lone in
    ///   the row; read its style.
    ///
    /// Expected:
    /// - Lone: size 20, weight 400, italic (what it took, not the defaults' 14). In row: size 20, weight 300, italic. Lone in the
    ///   row: size 20, weight 300, italic.
    /// </remarks>
    [Fact]
    public void LeafSetter_WhileOutOfTheTree_TakesWhatItTookWhenItWasTakenOut() =>
        ui.Invoke(() =>
        {
            using var scene = new TextScene(_font);
            Element first = default;
            Element lone = default;
            Element row = default;
            Element inRow = default;
            scene.Mount(() =>
            {
                lone = Elements.Text("a");
                inRow = Elements.Text("b");
                row = Row(inRow).FontWeight(Light);
                return Column(first = Column(lone, row).FontSize(20));
            });
            scene.Draw();
            UiTree tree = scene.Tree;

            tree.DetachAfter(first.Node, default, lone.Node);
            tree.DetachAfter(first.Node, default, row.Node);
            _ = lone.FontSlant(FontSlant.Italic);
            _ = inRow.FontSlant(FontSlant.Italic);
            (string?, int, FontSlant, FontStretch, float, float) aloneOut = Fields(scene, lone);
            (string?, int, FontSlant, FontStretch, float, float) inRowOut = Fields(scene, inRow);
            tree.AppendChild(row.Node, lone.Node);
            (string?, int, FontSlant, FontStretch, float, float) putInRow = Fields(scene, lone);

            Assert.Equal(((string?)null, 400, FontSlant.Italic, FontStretch.Normal, 20f, float.NaN), aloneOut);
            Assert.Equal(((string?)null, 300, FontSlant.Italic, FontStretch.Normal, 20f, float.NaN), inRowOut);
            Assert.Equal(((string?)null, 300, FontSlant.Italic, FontStretch.Normal, 20f, float.NaN), putInRow);
        });

    /// <summary>
    /// The children a parent lets go of all at once keep what they took, and each is resolved again where it is put: under a
    /// parent that gives other values it takes them, and put back under the same parent it is not walked.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A column around a column of size 20 (styled) around two texts with no field of their own (a and b), and a box with no text
    ///   style (other); mounted and drawn.
    ///
    /// Steps:
    /// - Take every child out of styled at once; read both sizes. Put a in other; read its size and mark. Put b back in styled;
    ///   read the leaves resolved by it and its size. Set styled's size to 25; read both sizes.
    ///
    /// Expected:
    /// - 20 and 20 while out. a in other: 14 (the default), marked. b back: no leaf resolved, 20. After 25: a 14, b 25.
    /// </remarks>
    [Fact]
    public void DetachChildren_KeepsWhatEachTook_UntilItIsPutUnderAParent() =>
        ui.Invoke(() =>
        {
            using var scene = new TextScene(_font);
            Element styled = default;
            Element other = default;
            Element a = default;
            Element b = default;
            scene.Mount(() =>
            {
                a = Elements.Text("a");
                b = Elements.Text("b");
                styled = Column(a, b).FontSize(20);
                other = Box();
                return Column(styled, other);
            });
            scene.Draw();
            UiTree tree = scene.Tree;

            tree.DetachChildren(styled.Node);
            float[] outside = [Size(scene, a), Size(scene, b)];
            tree.AppendChild(other.Node, a.Node);
            (float Size, bool Marked) underOther = (Size(scene, a), Marks(scene, [a])[0]);
            long resolved = tree.TextLeavesResolved;
            tree.AppendChild(styled.Node, b.Node);
            long putBack = tree.TextLeavesResolved - resolved;
            float back = Size(scene, b);
            _ = styled.FontSize(25);
            float[] changed = [Size(scene, a), Size(scene, b)];

            Assert.Equal([20f, 20f], outside);
            Assert.Equal((14f, true), underOther);
            Assert.Equal(0, putBack);
            Assert.Equal(20f, back);
            Assert.Equal([14f, 25f], changed);
        });

    /// <summary>
    /// An inline object of a text takes what the text's ancestors give, not the text's own values, and follows a change of them.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A column of size 20 around a text of its own size 10 (outer) whose content is "n", an inline text "n" with no field of its
    ///   own (inline), and "n"; mounted and drawn.
    ///
    /// Steps:
    /// - Read both sizes. Set the column's size to 25; read both sizes and inline's mark.
    ///
    /// Expected:
    /// - Outer 10 and inline 20; after the column's 25, outer 10 and inline 25, marked.
    /// </remarks>
    [Fact]
    public void InlineObject_TakesTheTextsAncestors_NotTheTextsOwnValues() =>
        ui.Invoke(() =>
        {
            using var scene = new TextScene(_font);
            Element column = default;
            Element[] leaves = new Element[2];
            scene.Mount(() =>
            {
                Element inline = leaves[1] = Elements.Text("n");
                leaves[0] = Elements
                    .Text(
                        (ref w) =>
                        {
                            w.Write("n".AsSpan());
                            w.Inline(inline);
                            w.Write("n".AsSpan());
                        }
                    )
                    .FontSize(10);
                return column = Column(leaves[0]).FontSize(20);
            });
            scene.Draw();
            float[] first = Array.ConvertAll(leaves, leaf => Size(scene, leaf));

            _ = column.FontSize(25);
            float[] changed = Array.ConvertAll(leaves, leaf => Size(scene, leaf));

            Assert.Equal([10f, 20f], first);
            Assert.Equal([10f, 25f], changed);
            Assert.True(Marks(scene, leaves)[1]);
        });

    /// <summary>
    /// A line height in pixels from a nearer container becomes a multiple of the size a farther container gives, and follows a
    /// change of that size.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A column (far) of size 16 around a row (near) of a line height of 24 px around a text with no field of its own (a) and a
    ///   text of its own size 10 (b); mounted and drawn.
    ///
    /// Steps:
    /// - Read both sizes and line heights. Set far's size to 20; read them and the marks.
    ///
    /// Expected:
    /// - a 16 and 24 / 16, b 10 and 24 / 10. After far's 20: a 20 and 24 / 20, marked; b 10 and 24 / 10, not marked.
    /// </remarks>
    [Fact]
    public void LineHeightInPixelsOfANearer_FollowsTheSizeOfAFarther() =>
        ui.Invoke(() =>
        {
            using var scene = new TextScene(_font);
            Element far = default;
            Element[] leaves = new Element[2];
            scene.Mount(() =>
            {
                leaves[0] = Elements.Text("a");
                leaves[1] = Elements.Text("b").FontSize(10);
                return far = Column(Row(leaves).LineHeight(24)).FontSize(16);
            });
            scene.Draw();
            (float, float)[] first = Array.ConvertAll(leaves, leaf => SizeAndLine(scene, leaf));

            _ = far.FontSize(20);
            (float, float)[] sized = Array.ConvertAll(leaves, leaf => SizeAndLine(scene, leaf));

            Assert.Equal([(16f, 24f / 16f), (10f, 24f / 10f)], first);
            Assert.Equal([(20f, 24f / 20f), (10f, 24f / 10f)], sized);
            Assert.Equal([true, false], Marks(scene, leaves));
        });

    // The family, weight, slant, stretch, size, and line height of a text's style.
    private static (string? Family, int Weight, FontSlant Slant, FontStretch Stretch, float Size, float LineHeight) Fields(
        TextScene scene,
        Element text
    )
    {
        TextStyle style = scene.Leaf(text).Style;
        return (style.Font.Family, style.Font.Weight.Value, style.Font.Slant, style.Font.Stretch, style.Size, style.LineHeight);
    }

    private static (float Size, float LineHeight) SizeAndLine(TextScene scene, Element text)
    {
        TextStyle style = scene.Leaf(text).Style;
        return (style.Size, style.LineHeight);
    }

    private static float Size(TextScene scene, Element text) => scene.Leaf(text).Style.Size;

    // Whether each text is marked for a change of its style since the last frame.
    private static bool[] Marks(TextScene scene, Element[] texts) =>
        Array.ConvertAll(texts, text => (scene.Tree.Nodes.GetRef(text.Node).Dirty.SelfDirty & DirtyFlags.Text) != 0);
}
