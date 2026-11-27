using static XylemUI.Elements;

namespace XylemUI.Text.Tests;

// The text style a container gives the text leaves below it (row TS1, design §2.13 and §3.2, "The text style a container gives"):
// the order a leaf's fields are taken in, the walk a container's change makes, the leaves it marks, the subtrees put under a parent,
// and the setters' allocations. A leaf is marked when its style changed: ChangeKind.Font sets DirtyFlags.Text, which nothing else
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
