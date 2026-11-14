using System.Globalization;
using System.Numerics;
using XylemUI.Testing.XUnit;
using static XylemUI.Elements;
using static XylemUI.Primitives.Primitive;

namespace XylemUI.Desktop.Tests;

// Row AX3 (Issue #902, design §4.4.1, "How it changes"): a change of a list of children makes again the node that holds the
// children's nodes, not the node above it, so that a virtualized list's row swap costs what the list holds, not what the window
// holds; and a row swap whose rows hold no node a relation names keeps the map of the targets' ancestors.
public sealed partial class AccessibilityTests
{
    /// <summary>
    /// After each scroll of a virtualized list, the tree kept by the updates is the tree made from scratch: rows swapped, a row kept
    /// out of the range for its focus, a row kept alive out of the range, a relation into a row and one out of a row, names folded
    /// from the text below a row, role-less boxes flattened inside the rows, and the nodes a draw function adds in each row.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A column of a field of the role Edit, a text "outside", and a virtualized list of 1,000 focusable rows 20 high in a box
    ///   200 x 100 that scrolls. The list is in the column itself (<paramref name="place"/> "window"), in two boxes with no role
    ///   ("boxes"), or in a box of the role Group ("group"). Each row holds a box with the text "row N" and a box holding a figure
    ///   whose draw function adds one point; row 3 is kept alive by a signal, row 5 is described by "outside". The tree made; the
    ///   field labelled by the text of row 3; row 2 focused.
    ///
    /// Steps:
    /// - Scroll to 20 (one row out, one in); settle; compare. Scroll to 4,000; settle; compare. Take the focus from row 2; settle;
    ///   compare. Scroll to 0; settle; compare. Each comparison writes every node of the tree, with every value it hands the
    ///   Platform and the nodes of the UI tree its relations name, and the same for a tree made from scratch on the same UI tree.
    ///
    /// Expected:
    /// - The list's last row is another node after the first scroll; the two trees are the same at each comparison.
    /// </remarks>
    /// <param name="place">Where the list is.</param>
    /// <returns>A task that completes when the test ends.</returns>
    [UiTheory]
    [InlineData("window")]
    [InlineData("boxes")]
    [InlineData("group")]
    public async Task RowSwap_LeavesTheTreeAsOneMadeFromScratch(string place)
    {
        Signal<IReadOnlyList<int>> items = Signal.Create<IReadOnlyList<int>>([.. Enumerable.Range(0, 1_000)]);
        IReadOnlySignal<bool> keep = Signal.Create(true);
        var point = new AccessibilityInfo { Role = SemanticsRole.Image, Name = "point" };
        var state = new VirtualListState();
        Element field = default;
        Element list = default;
        await using HeadlessApp app = await HeadlessApp.StartAsync(() =>
        {
            Element outside = Elements.Text("outside");
            field = Box().Width(50).Height(10).Accessible(new AccessibilityInfo { Role = SemanticsRole.Edit });
            list = Box()
                .Width(200)
                .Height(100)
                .Overflow(Overflow.Visible, Overflow.Scroll)
                .VirtualFor(
                    items,
                    static i => i,
                    item =>
                    {
                        // A row's item never changes, so it is read once.
                        int value = item.Get();
                        Element figure = Box()
                            .Width(10)
                            .Height(5)
                            .Accessible(new AccessibilityInfo { Role = SemanticsRole.Figure })
                            .AccessibleChildren((children, bounds) => children.Add(1, bounds, point));
                        Element row = Row(Box(Elements.Text($"row {value}").TestId($"text{value}")), Box(figure))
                            .Height(20)
                            .Focusable()
                            .TestId($"row{value}");
                        if (value == 5)
                        {
                            _ = row.DescribedBy(outside);
                        }

                        return value == 3 ? row.KeepAlive(keep) : row;
                    },
                    state
                );
            Element placed = place switch
            {
                "boxes" => Box(Box(list)),
                "group" => Box(list).Accessible(new AccessibilityInfo { Role = SemanticsRole.Group, Name = "group" }),
                _ => list,
            };
            return Column(field, outside, placed);
        });
        _ = app.PumpAndSettle();
        _ = app.AccessibilityTree();
        _ = field.LabelledBy(app.Find("text3"));
        Assert.True(app.Focus(app.Find("row2")));
        _ = app.PumpAndSettle();
        NodeHandle last = app.Tree.Nodes.GetRef(list.Node).LastChild;

        state.Scroll.ScrollTo(new Vector2(0, 20));
        _ = app.PumpAndSettle();
        Assert.NotEqual(last, app.Tree.Nodes.GetRef(list.Node).LastChild);
        AssertSameAsATreeMadeFromScratch(app);
        state.Scroll.ScrollTo(new Vector2(0, 4_000));
        _ = app.PumpAndSettle();
        AssertSameAsATreeMadeFromScratch(app);
        app.Tree.Focus.Lose(app.Find("row2").Node);
        _ = app.PumpAndSettle();
        AssertSameAsATreeMadeFromScratch(app);
        state.Scroll.ScrollTo(Vector2.Zero);
        _ = app.PumpAndSettle();
        AssertSameAsATreeMadeFromScratch(app);
    }

    /// <summary>
    /// A frame that swaps a row of a virtualized list in boxes with no role makes again the list's node alone, not the window's, which
    /// holds hundreds of buttons (design §4.4.1, "How it changes", row AX3).
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A column of two boxes with no role holding a virtualized list of 1,000 rows 20 high (each a box with a text) in a box
    ///   200 x 100 that scrolls, and a row of 300 buttons; the tree made.
    ///
    /// Steps:
    /// - Scroll to 20; run one frame; read the nodes the update made again and whether the list's last row is another node; settle.
    ///
    /// Expected:
    /// - The last row is another node; 1 node made again, the list's. Before row AX3, the box's mark also made the window's node
    ///   again: 2.
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task RowSwap_MakesAgainTheListAlone_HoweverManyNodesAreAroundIt()
    {
        Signal<IReadOnlyList<int>> items = Signal.Create<IReadOnlyList<int>>([.. Enumerable.Range(0, 1_000)]);
        var state = new VirtualListState();
        Element list = default;
        await using HeadlessApp app = await HeadlessApp.StartAsync(() =>
        {
            var buttons = new Element[300];
            for (int i = 0; i < buttons.Length; i++)
            {
                buttons[i] = Button($"b{i}", () => { });
            }

            list = Box()
                .Width(200)
                .Height(100)
                .Overflow(Overflow.Visible, Overflow.Scroll)
                .VirtualFor(items, static i => i, static item => Box(Elements.Text(() => $"row {item.Get()}")).Height(20), state);
            return Column(Box(Box(list)), Row(buttons));
        });
        _ = app.PumpAndSettle();
        _ = app.AccessibilityTree();
        NodeHandle last = app.Tree.Nodes.GetRef(list.Node).LastChild;

        state.Scroll.ScrollTo(new Vector2(0, 20));
        app.Pump(TimeSpan.FromMilliseconds(16));
        int refreshed = app.Tree.Accessibility.LastRefreshed;
        bool swapped = app.Tree.Nodes.GetRef(list.Node).LastChild != last;
        _ = app.PumpAndSettle();

        Assert.True(swapped);
        Assert.Equal(1, refreshed);
    }

    /// <summary>
    /// Each change of a node's list of children makes again the node that holds its children's nodes alone, and a change of a
    /// child's style the child's node and that node, never the window's above them (design §4.4.1, "How it changes", row AX3).
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A column of a box with no role holding a box "holder" 100 x 20 of the role Group, and a row of 300 buttons; two nodes with no
    ///   size, of the role Button, named "child" and "other", made apart from the tree; the tree made.
    ///
    /// Steps:
    /// - Append "child" to the holder; insert "other" first; take "other" out after none; give "child" a least width of 5; take every
    ///   child out; append "other"; remove "other". After each, settle and read the nodes the last update made again and the names of
    ///   the holder's children.
    ///
    /// Expected:
    /// - (1, "child"), (1, "other child"), (1, "child"), (2, "child"), (1, ""), (1, "other"), (1, ""). Before row AX3, each mark of the
    ///   holder also made the window's node again: one more in each.
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task ChildListChanges_MakeAgainTheNodeThatHoldsTheChildren_Alone()
    {
        Element holder = default;
        await using HeadlessApp app = await HeadlessApp.StartAsync(() =>
        {
            var buttons = new Element[300];
            for (int i = 0; i < buttons.Length; i++)
            {
                buttons[i] = Button($"b{i}", () => { });
            }

            holder = Box().Width(100).Height(20).Accessible(new AccessibilityInfo { Role = SemanticsRole.Group, Name = "holder" });
            return Column(Box(holder), Row(buttons));
        });
        _ = app.PumpAndSettle();
        UiTree ui = app.Tree;

        // Made and named before the tree is made, so that their marks, with no parent yet, do not reach the window's node.
        NodeHandle child = ui.CreateNode(new LayoutStyle());
        ui.SetAccessibility(child, new AccessibilityInfo { Role = SemanticsRole.Button, Name = "child" });
        NodeHandle other = ui.CreateNode(new LayoutStyle());
        ui.SetAccessibility(other, new AccessibilityInfo { Role = SemanticsRole.Button, Name = "other" });
        AccessibilityTree tree = app.AccessibilityTree();
        var read = new List<(int, string)>();

        ui.AppendChild(holder.Node, child);
        Read();
        ui.InsertAfter(holder.Node, default, other);
        Read();
        ui.DetachAfter(holder.Node, default, other);
        Read();
        ui.SetStyle(child, ui.Nodes.GetRef(child).Layout.Style with { MinWidth = Length.Px(5) });
        Read();
        ui.DetachChildren(holder.Node);
        Read();
        ui.AppendChild(holder.Node, other);
        Read();
        ui.Remove(other);
        Read();
        ui.Remove(child);

        Assert.Equal([(1, "child"), (1, "other child"), (1, "child"), (2, "child"), (1, string.Empty), (1, "other"), (1, string.Empty)], read);

        void Read()
        {
            _ = app.PumpAndSettle();
            read.Add((tree.LastRefreshed, string.Join(' ', tree.Find(holder.Node)!.Children.Select(c => c.Name))));
        }
    }

    /// <summary>
    /// A row swap of a virtualized list whose rows hold no node a relation names keeps the map of the targets' ancestors: it is not
    /// made again for each frame that changes the tree's links (row AX3).
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A column of 20 fields of the role Edit, each labelled by a text of its own after them, and a virtualized list of 1,000 rows
    ///   20 high in a box 200 x 100 that scrolls; the tree made; scrolled to 20 and settled, which makes the map.
    ///
    /// Steps:
    /// - Read the number of maps made. Scroll to 40, 60, and 80, settling after each, and read whether each swapped a row. Read the
    ///   number of maps made.
    ///
    /// Expected:
    /// - Each scroll swapped a row; the number of maps made is the same. Before row AX3, each swap changed the tree's link version,
    ///   and each made the map again: 3 more.
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task RowSwap_WithNoTargetInTheRows_KeepsTheMapOfTheTargetsAncestors()
    {
        Signal<IReadOnlyList<int>> items = Signal.Create<IReadOnlyList<int>>([.. Enumerable.Range(0, 1_000)]);
        var state = new VirtualListState();
        Element list = default;
        await using HeadlessApp app = await HeadlessApp.StartAsync(() =>
        {
            var fields = new Element[20];
            var labels = new Element[20];
            for (int i = 0; i < fields.Length; i++)
            {
                labels[i] = Elements.Text($"label {i}");
                fields[i] = Box().Width(50).Height(10).Accessible(new AccessibilityInfo { Role = SemanticsRole.Edit }).LabelledBy(labels[i]);
            }

            list = Box()
                .Width(200)
                .Height(100)
                .Overflow(Overflow.Visible, Overflow.Scroll)
                .VirtualFor(items, static i => i, static item => Box(Elements.Text(() => $"row {item.Get()}")).Height(20), state);
            return Column(Row(fields), Row(labels), list);
        });
        _ = app.PumpAndSettle();
        AccessibilityTree tree = app.AccessibilityTree();
        state.Scroll.ScrollTo(new Vector2(0, 20));
        _ = app.PumpAndSettle();

        int before = tree.TargetMapsMade;
        var swapped = new List<bool>();
        for (int y = 40; y <= 80; y += 20)
        {
            NodeHandle last = app.Tree.Nodes.GetRef(list.Node).LastChild;
            state.Scroll.ScrollTo(new Vector2(0, y));
            _ = app.PumpAndSettle();
            swapped.Add(app.Tree.Nodes.GetRef(list.Node).LastChild != last);
        }

        Assert.Equal([true, true, true], swapped);
        Assert.Equal(before, tree.TargetMapsMade);
    }

    /// <summary>
    /// The name a field takes from a node with no node of its own follows that node's new ancestor once a box above it moves: the map
    /// of the targets' ancestors is made again when a node that holds a target is put under a parent, not only the target itself.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A box "a" holding a box "wrapper" that holds a box "inner" with no role holding a text "Hello", an empty box "b", and a field of
    ///   the role Edit labelled by "inner"; the tree made.
    ///
    /// Steps:
    /// - Hide and show "b" by its visibility, settling after each, which makes the map. Take "wrapper" out of "a" and put it into "b";
    ///   settle; read the field's name. Hide "b" by its visibility; settle; read it. Show "b"; settle; read it.
    ///
    /// Expected:
    /// - "Hello", none, "Hello".
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task Name_FromATargetBelowAMovedNode_FollowsItsNewAncestor()
    {
        Element a = default;
        Element b = default;
        Element wrapper = default;
        Element field = default;
        await using HeadlessApp app = await HeadlessApp.StartAsync(() =>
        {
            Element inner = Box(Elements.Text("Hello"));
            wrapper = Box(inner);
            a = Box(wrapper);
            b = Box();
            field = Box().Width(50).Height(10).Accessible(new AccessibilityInfo { Role = SemanticsRole.Edit }).LabelledBy(inner);
            return Column(a, b, field);
        });
        _ = app.PumpAndSettle();
        _ = app.AccessibilityTree();
        SetHidden(b, true);
        SetHidden(b, false);
        var names = new List<string?>();

        app.Tree.DetachChildren(a.Node);
        app.Tree.AppendChild(b.Node, wrapper.Node);
        _ = app.PumpAndSettle();
        names.Add(app.AccessibilityOf(field).Name);
        SetHidden(b, true);
        names.Add(app.AccessibilityOf(field).Name);
        SetHidden(b, false);
        names.Add(app.AccessibilityOf(field).Name);

        Assert.Equal(new string?[] { "Hello", null, "Hello" }, names);

        void SetHidden(Element box, bool value)
        {
            app.Tree.SetVisibility(box.Node, value ? Visibility.Hidden : Visibility.Visible);
            _ = app.PumpAndSettle();
        }
    }

    /// <summary>
    /// A scroll of a virtualized list that keeps its rows, with the accessibility tree made, allocates nothing: the list's node is made
    /// again with its new offset for its children.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A virtualized list of 10,000 rows 20 high (each a box with a text) in a box 200 x 300 that scrolls, scrolled to 1,000; the
    ///   tree made; warmed up with the iterations.
    ///
    /// Steps:
    /// - 1,000 iterations in the windows of <c>PerformanceAssert.DoesNotAllocate</c>: scroll to 1,004 or 1,000 by the iteration's
    ///   number, which keeps the rows, and settle.
    ///
    /// Expected:
    /// - 0 bytes in every window.
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task SteadyState_ScrollKeepingTheRowsWithTheTree_AllocatesNothing()
    {
        Signal<IReadOnlyList<int>> items = Signal.Create<IReadOnlyList<int>>([.. Enumerable.Range(0, 10_000)]);
        var state = new VirtualListState();
        await using HeadlessApp app = await HeadlessApp.StartAsync(() =>
            Box()
                .Width(200)
                .Height(300)
                .Overflow(Overflow.Visible, Overflow.Scroll)
                .VirtualFor(items, static i => i, static item => Box(Elements.Text(() => $"row {item.Get()}")).Height(20), state)
        );
        _ = app.PumpAndSettle();
        state.Scroll.ScrollTo(new Vector2(0, 1_000));
        _ = app.PumpAndSettle();
        _ = app.AccessibilityTree();
        void Step(int index)
        {
            state.Scroll.ScrollTo(new Vector2(0, (index & 1) == 0 ? 1_004 : 1_000));
            _ = app.PumpAndSettle();
        }

        for (int i = 0; i < 40; i++)
        {
            Step(i);
        }

        PerformanceAssert.DoesNotAllocate(1_000, Step);
    }

    // Compares the tree the updates kept with a tree made from scratch on the same UI tree, node by node.
    private static void AssertSameAsATreeMadeFromScratch(HeadlessApp app)
    {
        AccessibilityTree kept = app.AccessibilityTree();
        var made = new AccessibilityTree(app.Tree);
        made.Start();

        Assert.Equal(ShapeOf(made), ShapeOf(kept));
    }

    // The whole tree as lines, one a node in preorder (children, then link nodes, then drawn nodes), with every value a node hands the
    // Platform. The ids differ between two trees, so a node and the nodes its relations name are written as the nodes of the UI tree
    // they stand for; a list of ids never held and an empty one are the same.
    private static List<string> ShapeOf(AccessibilityTree tree)
    {
        var lines = new List<string>();
        Add(tree.Root!, 0);
        return lines;

        void Add(AccessibilityNode node, int depth)
        {
            lines.Add(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"{new string(' ', depth * 2)}{Key(node)} parent={(node.Parent is null ? "-" : Key(node.Parent))} {node.Role} name={node.Name} description={node.Description} value={node.Value} placeholder={node.Placeholder} states={node.States} range={node.Range} bounds={node.Bounds} offset={node.ChildOffset} scroll={node.ScrollRange} place={node.PositionInSet} count={node.SizeOfSet} test={node.AutomationId} level={node.Level} live={node.Live} orientation={node.Orientation} labelled={Ids(node.LabelledBy)} described={Ids(node.DescribedBy)} controls={Ids(node.Controls)} error={Id(node.ErrorMessage)} active={Id(node.ActiveDescendant)}"
                )
            );
            foreach (AccessibilityNode child in node.Children)
            {
                Add(child, depth + 1);
            }

            foreach (AccessibilityNode link in node.Links)
            {
                Add(link, depth + 1);
            }

            if (node.SyntheticCount > 0)
            {
                foreach (AccessibilityNode drawn in node.Synthetic)
                {
                    Add(drawn, depth + 1);
                }
            }
        }

        string Ids(List<AccessibilityNodeId>? ids) => ids is not { Count: > 0 } ? "-" : string.Join(',', ids.Select(Id));

        string Id(AccessibilityNodeId id) =>
            id.Value == 0 ? "-"
            : tree.Find(id) is { } node ? Key(node)
            : "gone";

        static string Key(AccessibilityNode node) =>
            string.Create(CultureInfo.InvariantCulture, $"{node.Node.Index}.{node.Node.Generation}/{node.ChildKey}/{node.LinkIndex}");
    }
}
