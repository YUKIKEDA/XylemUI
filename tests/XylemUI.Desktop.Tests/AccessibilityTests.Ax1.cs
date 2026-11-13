using System.Numerics;
using XylemUI.Primitives;
using XylemUI.Testing.XUnit;
using static XylemUI.Elements;
using static XylemUI.Primitives.Primitive;

namespace XylemUI.Desktop.Tests;

// What row AX1 adds to the accessibility tree (design §4.4.1, "The shape AX1 adds"), read in a headless app: the roles, states, ranges,
// actions, relations, live settings, levels, places, counts, and the nodes of a draw function.
public sealed partial class AccessibilityTests
{
    /// <summary>
    /// Each role row AX1 adds takes its name from its content, holds no child, or holds its content as children, as the table of
    /// roles says.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A column of a box for each of the 41 roles added after Separator, each holding a text "inner".
    ///
    /// Steps:
    /// - Read each box's node: its name and its children.
    ///
    /// Expected:
    /// - Status, Alert, Switch, MenuItemCheckBox, MenuItemRadio, ListBoxItem, TreeItem, Cell, GridCell, ColumnHeader, RowHeader,
    ///   Heading, and Disclosure are named "inner" with no child; Meter, SpinButton, SearchBox, ScrollBar, DateInput, TimeInput, and
    ///   DateTimeInput have no name and no child; every other role has no name and one Text child "inner".
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task AddedRoles_FoldNamesAndHoldChildren_AsTheTableSays()
    {
        SemanticsRole[] added = [.. Enum.GetValues<SemanticsRole>().Where(static r => r > SemanticsRole.Separator)];
        SemanticsRole[] folding =
        [
            SemanticsRole.Status,
            SemanticsRole.Alert,
            SemanticsRole.Switch,
            SemanticsRole.MenuItemCheckBox,
            SemanticsRole.MenuItemRadio,
            SemanticsRole.ListBoxItem,
            SemanticsRole.TreeItem,
            SemanticsRole.Cell,
            SemanticsRole.GridCell,
            SemanticsRole.ColumnHeader,
            SemanticsRole.RowHeader,
            SemanticsRole.Heading,
            SemanticsRole.Disclosure,
        ];
        SemanticsRole[] childless =
        [
            SemanticsRole.Meter,
            SemanticsRole.SpinButton,
            SemanticsRole.SearchBox,
            SemanticsRole.ScrollBar,
            SemanticsRole.DateInput,
            SemanticsRole.TimeInput,
            SemanticsRole.DateTimeInput,
        ];
        var boxes = new Dictionary<SemanticsRole, Element>();
        await using HeadlessApp app = await HeadlessApp.StartAsync(() =>
        {
            Element[] items = [.. added.Select(r => boxes[r] = Box(Elements.Text("inner")).Accessible(new AccessibilityInfo { Role = r }))];
            return Column(items);
        });
        _ = app.PumpAndSettle();
        AccessibilityTree tree = app.AccessibilityTree();

        Assert.Equal(41, added.Length);
        foreach (SemanticsRole role in added)
        {
            AccessibilityNode node = tree.Find(boxes[role].Node)!;
            if (folding.Contains(role))
            {
                Assert.Equal((role, "inner", 0), (role, node.Name, node.Children.Count));
            }
            else if (childless.Contains(role))
            {
                Assert.Equal((role, (string?)null, 0), (role, node.Name, node.Children.Count));
            }
            else
            {
                Assert.Equal((role, (string?)null, 1), (role, node.Name, node.Children.Count));
                Assert.Equal((SemanticsRole.Text, "inner"), (node.Children[0].Role, node.Children[0].Name));
            }
        }
    }

    /// <summary>
    /// A live setting of Inherit is Assertive for an alert, Polite for a status and a log, and not written for another role; a node
    /// below takes its parent's, and Off on an alert is Off.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A column of an Alert, a Status, a Log holding a text "line", a Group, and an Alert with the live setting Off.
    ///
    /// Steps:
    /// - Read each node's live setting, and what <c>AccessibilityOf</c> gives for the text "line" and the group.
    ///
    /// Expected:
    /// - Assertive, Polite, Polite, Inherit (not written), Off; the text "line" reads Polite from its log, and the group Off.
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task Live_Inherit_TakesTheRolesDefault_AndOffWins()
    {
        Element alert = default;
        Element status = default;
        Element log = default;
        Element line = default;
        Element group = default;
        Element quiet = default;
        await using HeadlessApp app = await HeadlessApp.StartAsync(() =>
            Column(
                alert = Box().Height(10).Accessible(new AccessibilityInfo { Role = SemanticsRole.Alert, Name = "a" }),
                status = Box().Height(10).Accessible(new AccessibilityInfo { Role = SemanticsRole.Status, Name = "s" }),
                log = Box(line = Elements.Text("line")).Accessible(new AccessibilityInfo { Role = SemanticsRole.Log }),
                group = Box().Height(10).Accessible(new AccessibilityInfo { Role = SemanticsRole.Group, Name = "g" }),
                quiet = Box().Height(10).Accessible(new AccessibilityInfo { Role = SemanticsRole.Alert, Live = SemanticsLive.Off })
            )
        );
        _ = app.PumpAndSettle();
        AccessibilityTree tree = app.AccessibilityTree();

        Assert.Equal(
            [SemanticsLive.Assertive, SemanticsLive.Polite, SemanticsLive.Polite, SemanticsLive.Inherit, SemanticsLive.Off],
            new[] { alert, status, log, group, quiet }.Select(e => tree.Find(e.Node)!.Live)
        );
        Assert.Equal((SemanticsLive.Polite, SemanticsLive.Off), (app.AccessibilityOf(line).Live, app.AccessibilityOf(group).Live));
    }

    /// <summary>
    /// The states a part gives and the states the app gives join: the app's Invalid does not take away the part's Selected.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A box whose part gives the role ListBoxItem and the state Selected, and whose app information gives the state Invalid.
    ///
    /// Steps:
    /// - Read its states.
    ///
    /// Expected:
    /// - Selected and Invalid.
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task States_OfThePartAndOfTheApp_Join()
    {
        Element item = default;
        await using HeadlessApp app = await HeadlessApp.StartAsync(() =>
        {
            item = Box().Height(10);
            item.Tree!.SetPartAccessibility(
                item.Node,
                new AccessibilityInfo
                {
                    Role = SemanticsRole.ListBoxItem,
                    Name = "pear",
                    States = SemanticsStates.Selected,
                }
            );
            return item.Accessible(new AccessibilityInfo { States = SemanticsStates.Invalid });
        });
        _ = app.PumpAndSettle();

        Assert.Equal(SemanticsStates.Selected | SemanticsStates.Invalid, app.AccessibilityOf(item).States);
    }

    /// <summary>
    /// A node that anchors no popup is open or closed by the app's Expandable and Expanded, and Expanded alone is not read; a popup's
    /// anchor is open or closed by its popup, whatever the app's states say.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A tree item that is Expandable, one that is Expandable and Expanded, both with a handler of Expand; one that is Expanded
    ///   only; a button "File" that anchors a menu, closed, whose app information says Expandable and Expanded.
    ///
    /// Steps:
    /// - Read whether each is open; open the menu; settle; read "File" again.
    ///
    /// Expected:
    /// - Closed, open, no open state, closed; then "File" open.
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task OpenState_ComesFromTheTwoBits_OrFromThePopupOfAnAnchor()
    {
        var open = Signal.Create(false);
        Element closed = default;
        Element opened = default;
        Element only = default;
        Element anchor = default;
        await using HeadlessApp app = await HeadlessApp.StartAsync(() =>
        {
            closed = Item("closed", SemanticsStates.Expandable).OnExpand(() => { });
            opened = Item("opened", SemanticsStates.Expandable | SemanticsStates.Expanded).OnExpand(() => { });
            only = Item("only", SemanticsStates.Expanded);
            anchor = Button("File", () => open.Set(true))
                .Menu(open, () => Column(MenuItem(Elements.Text("New"), () => { })))
                .Accessible(new AccessibilityInfo { States = SemanticsStates.Expandable | SemanticsStates.Expanded });
            return Column(closed, opened, only, anchor);
        });
        _ = app.PumpAndSettle();
        bool?[] before = [.. new[] { closed, opened, only, anchor }.Select(e => app.AccessibilityOf(e).IsExpanded)];
        open.Set(true);
        _ = app.PumpAndSettle();

        Assert.Equal(new bool?[] { false, true, null, false }, before);
        Assert.True(app.AccessibilityOf(anchor).IsExpanded);

        static Element Item(string name, SemanticsStates states) =>
            Box()
                .Height(10)
                .Accessible(
                    new AccessibilityInfo
                    {
                        Role = SemanticsRole.TreeItem,
                        Name = name,
                        States = states,
                    }
                );
    }

    /// <summary>
    /// A step or a large step that is negative, NaN, or infinite, and a level, place, or count that is negative, throw
    /// <see cref="ArgumentOutOfRangeException"/>, in a constructor or an initializer and in a with expression; 0 and positive values
    /// are taken.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - The values -1, NaN, +infinity, and -infinity for the steps; -1 for the counts.
    ///
    /// Steps:
    /// - Make a range with each as Step and as LargeStep, and set each through a with expression of a valid range. Make information
    ///   with -1 as Level, PositionInSet, and SizeOfSet, and set each through a with expression. Make a range with the steps 0 and 2.5,
    ///   and information with 0 and 3.
    ///
    /// Expected:
    /// - Every bad value throws; the good ones are kept.
    /// </remarks>
    [Fact]
    public void StepsAndCounts_RefuseBadValues_InTheConstructorAndInWith()
    {
        var valid = new SemanticsRange(0, 10, 5);
        foreach (double bad in new[] { -1, double.NaN, double.PositiveInfinity, double.NegativeInfinity })
        {
            _ = Assert.Throws<ArgumentOutOfRangeException>(() => new SemanticsRange(0, 10, 5, Step: bad));
            _ = Assert.Throws<ArgumentOutOfRangeException>(() => new SemanticsRange(0, 10, 5, LargeStep: bad));
            _ = Assert.Throws<ArgumentOutOfRangeException>(() => valid with { Step = bad });
            _ = Assert.Throws<ArgumentOutOfRangeException>(() => valid with { LargeStep = bad });
        }

        var info = new AccessibilityInfo();
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => new AccessibilityInfo { Level = -1 });
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => new AccessibilityInfo { PositionInSet = -1 });
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => new AccessibilityInfo { SizeOfSet = -1 });
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => info with { Level = -1 });
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => info with { PositionInSet = -1 });
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => info with { SizeOfSet = -1 });

        SemanticsRange good = valid with { Step = 2.5, LargeStep = 0 };
        AccessibilityInfo counted = info with { Level = 3, PositionInSet = 0, SizeOfSet = 3 };
        Assert.Equal((2.5, 0.0, 3, 0, 3), (good.Step, good.LargeStep, counted.Level, counted.PositionInSet, counted.SizeOfSet));
    }

    /// <summary>
    /// A level and a place are written to the node less 1, as AccessKit counts them, and 0 is not written; a count is written as it
    /// is, on the container; <c>AccessibilityOf</c> gives them back from 1.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A list of the role List with the count 7 holding an item with the place 3, a heading with the level 2, and a group with
    ///   none of them.
    ///
    /// Steps:
    /// - Read the nodes and what <c>AccessibilityOf</c> gives.
    ///
    /// Expected:
    /// - The heading's node level 1, the item's place 2, the list's count 7; the group's -1 for each. AccessibilityOf: 2, 3, 7, and 0.
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task LevelAndPlace_AreWrittenLessOne_AndZeroIsNone()
    {
        Element list = default;
        Element item = default;
        Element heading = default;
        Element group = default;
        await using HeadlessApp app = await HeadlessApp.StartAsync(() =>
        {
            item = Box()
                .Height(10)
                .Accessible(
                    new AccessibilityInfo
                    {
                        Role = SemanticsRole.ListItem,
                        Name = "c",
                        PositionInSet = 3,
                    }
                );
            list = Box(item).Accessible(new AccessibilityInfo { Role = SemanticsRole.List, SizeOfSet = 7 });
            heading = Box()
                .Height(10)
                .Accessible(
                    new AccessibilityInfo
                    {
                        Role = SemanticsRole.Heading,
                        Name = "Title",
                        Level = 2,
                    }
                );
            group = Box().Height(10).Accessible(new AccessibilityInfo { Role = SemanticsRole.Group, Name = "g" });
            return Column(list, heading, group);
        });
        _ = app.PumpAndSettle();
        AccessibilityTree tree = app.AccessibilityTree();
        AccessibilityNode none = tree.Find(group.Node)!;

        Assert.Equal((1, 2, 7), (tree.Find(heading.Node)!.Level, tree.Find(item.Node)!.PositionInSet, tree.Find(list.Node)!.SizeOfSet));
        Assert.Equal((-1, -1, -1), (none.Level, none.PositionInSet, none.SizeOfSet));
        Assert.Equal(
            (2, 3, 7, 0, 0, 0),
            (
                app.AccessibilityOf(heading).Level,
                app.AccessibilityOf(item).PositionInSet,
                app.AccessibilityOf(list).SizeOfSet,
                app.AccessibilityOf(group).Level,
                app.AccessibilityOf(group).PositionInSet,
                app.AccessibilityOf(group).SizeOfSet
            )
        );
    }

    /// <summary>
    /// The place and the count the app gives win over those a virtualized list gives its rows and its box.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A box 200 x 100 that scrolls down with a virtualized list of 1,000 rows 20 high, whose box the app gives the count 5000 and
    ///   whose rows the app gives the place of their item plus 11.
    ///
    /// Steps:
    /// - Read the list's node and its first row's.
    ///
    /// Expected:
    /// - The count 5000; the first row's place 10 (from 0), not 0.
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task VirtualList_AppsPlaceAndCount_Win()
    {
        Signal<IReadOnlyList<int>> items = Signal.Create<IReadOnlyList<int>>([.. Enumerable.Range(0, 1_000)]);
        Element list = default;
        await using HeadlessApp app = await HeadlessApp.StartAsync(() =>
            list = Box()
                .Width(200)
                .Height(100)
                .Overflow(Overflow.Visible, Overflow.Scroll)
                .Accessible(new AccessibilityInfo { SizeOfSet = 5_000 })
                .VirtualFor(
                    items,
                    static i => i,
                    static item =>
                    {
                        // A row's item does not change: its place is read once, when the row is made.
                        int place = item.Get() + 11;
                        return Box().Height(20).Accessible(new AccessibilityInfo { PositionInSet = place });
                    }
                )
        );
        _ = app.PumpAndSettle();
        AccessibilityNode node = app.AccessibilityTree().Find(list.Node)!;

        Assert.Equal((5_000, 10), (node.SizeOfSet, node.Children[0].PositionInSet));
    }

    /// <summary>
    /// Relations bind to nodes made after the node that holds them, once both are made before the call.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A field of the role Edit made first, then a text "Name", a text "Help", and a tab panel; the field labelled by "Name",
    ///   described by "Help", controlling the panel, and with "Help" as its error message.
    ///
    /// Steps:
    /// - Read the field's relations.
    ///
    /// Expected:
    /// - LabelledBy "Name", DescribedBy "Help", Controls the panel, ErrorMessage "Help".
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task Relations_ToNodesMadeLater_AreBound()
    {
        Element field = default;
        Element label = default;
        Element help = default;
        Element panel = default;
        await using HeadlessApp app = await HeadlessApp.StartAsync(() =>
        {
            field = Box().Width(50).Height(10).Accessible(new AccessibilityInfo { Role = SemanticsRole.Edit });
            label = Elements.Text("Name");
            help = Elements.Text("Help");
            panel = Box().Height(10).Accessible(new AccessibilityInfo { Role = SemanticsRole.TabPanel });
            _ = field.LabelledBy(label).DescribedBy(help).Controls(panel).ErrorMessage(help);
            return Column(field, label, help, panel);
        });
        _ = app.PumpAndSettle();
        AccessibilityNodeInfo info = app.AccessibilityOf(field);

        Assert.Equal([label], info.LabelledBy);
        Assert.Equal([help], info.DescribedBy);
        Assert.Equal([panel], info.Controls);
        Assert.Equal(help, info.ErrorMessage);
    }

    /// <summary>
    /// A node a relation names is not handed on while it is gone, hidden, excluded, or out of a virtualized list's range, and is
    /// handed on again when it comes back.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A field of the role Edit that controls, in order: a text "gone" a Show holds, a text "hidden" in a box, a text "excluded",
    ///   and row 0 of a virtualized list of 1,000 rows 20 high in a box 100 high, which a signal keeps alive; the tree made.
    ///
    /// Steps:
    /// - Read the field's controlled nodes. Turn the Show off; settle; read. Hide the box; settle; read; show it; settle; read.
    ///   Exclude "excluded"; settle; read; include it; settle; read. Scroll the list to 4,000; settle; read; scroll it to 0; settle;
    ///   read.
    ///
    /// Expected:
    /// - 4 nodes; then 3 ("gone" dropped); 2 and 3 ("hidden"); 2 and 3 ("excluded"); 2 and 3 (row 0).
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task Relations_DropANodeGoneHiddenExcludedOrOutOfRange_AndTakeItBack()
    {
        var shown = Signal.Create(true);
        IReadOnlySignal<bool> keep = Signal.Create(true);
        Signal<IReadOnlyList<int>> items = Signal.Create<IReadOnlyList<int>>([.. Enumerable.Range(0, 1_000)]);
        var state = new VirtualListState();
        Element field = default;
        Element gone = default;
        Element box = default;
        Element hidden = default;
        Element excluded = default;
        await using HeadlessApp app = await HeadlessApp.StartAsync(() =>
            Column(
                field = Box().Width(50).Height(10).Accessible(new AccessibilityInfo { Role = SemanticsRole.Edit }),
                Show(shown, () => gone = Elements.Text("gone")),
                box = Box(hidden = Elements.Text("hidden")),
                excluded = Elements.Text("excluded"),
                Box()
                    .Width(200)
                    .Height(100)
                    .Overflow(Overflow.Visible, Overflow.Scroll)
                    .VirtualFor(
                        items,
                        static i => i,
                        item =>
                        {
                            Element row = Box().Height(20).Accessible(new AccessibilityInfo { Name = "row" }).TestId($"row{item.Get()}");
                            return item.Get() == 0 ? row.KeepAlive(keep) : row;
                        },
                        state
                    )
            )
        );
        _ = app.PumpAndSettle();
        _ = field.Controls(gone).Controls(hidden).Controls(excluded).Controls(app.Find("row0"));
        _ = app.AccessibilityTree();
        var counts = new List<int> { Count() };

        shown.Set(false);
        Step();
        SetHidden(true);
        SetHidden(false);
        app.Tree.SetAccessibility(excluded.Node, new AccessibilityInfo { Excluded = true });
        Step();
        app.Tree.SetAccessibility(excluded.Node, new AccessibilityInfo());
        Step();
        state.Scroll.ScrollTo(new Vector2(0, 4_000));
        Step();
        state.Scroll.ScrollTo(Vector2.Zero);
        Step();

        Assert.Equal([4, 3, 2, 3, 2, 3, 2, 3], counts);

        int Count() => app.AccessibilityOf(field).Controls.Count;
        void Step()
        {
            _ = app.PumpAndSettle();
            counts.Add(Count());
        }

        void SetHidden(bool value)
        {
            app.Tree.SetStyle(box.Node, app.Tree.Nodes.GetRef(box.Node).Layout.Style with { Hidden = value });
            Step();
        }
    }

    /// <summary>
    /// A relation to a node of another tree, to no node, or to the node itself throws <see cref="ArgumentException"/>; a second error
    /// message replaces the first.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A field of the role Edit, a text "first", and a text "second"; a node of another tree.
    ///
    /// Steps:
    /// - Label the field by the other tree's node, describe it by itself, and make it control no node (default). Make "first" its
    ///   error message, then "second"; read its error message.
    ///
    /// Expected:
    /// - Three exceptions; the error message is "second".
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task Relations_ToAnotherTreeOrItself_Throw_AndASecondErrorMessageReplaces()
    {
        Element field = default;
        Element first = default;
        Element second = default;
        await using HeadlessApp app = await HeadlessApp.StartAsync(() =>
            Column(
                field = Box().Width(50).Height(10).Accessible(new AccessibilityInfo { Role = SemanticsRole.Edit }),
                first = Elements.Text("first"),
                second = Elements.Text("second")
            )
        );
        _ = app.PumpAndSettle();
        var other = new UiTree();
        var stranger = new Element(other, other.CreateNode(new LayoutStyle()));

        _ = Assert.Throws<ArgumentException>(() => field.LabelledBy(stranger));
        _ = Assert.Throws<ArgumentException>(() => field.DescribedBy(field));
        _ = Assert.Throws<ArgumentException>(() => field.Controls(default));
        _ = field.ErrorMessage(first).ErrorMessage(second);
        _ = app.PumpAndSettle();

        Assert.Equal(second, app.AccessibilityOf(field).ErrorMessage);
    }

    /// <summary>
    /// A name is the app's name, else the text of the nodes that name it joined by a space, else its part's name, else the text of
    /// its content; a node with a part's name and a label takes the label's text, and a change of the label's text renames it.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A text "Label" from a signal and a text "Two". Boxes whose part gives the role Button and the name "Part": one labelled by
    ///   "Label", one labelled by it with the app's name "App", one with neither, one labelled by "Label" and "Two". A button "Fold",
    ///   and a button "Inner" labelled by "Label".
    ///
    /// Steps:
    /// - Read the names; set the signal to "Renamed"; settle; read them again.
    ///
    /// Expected:
    /// - "Label", "App", "Part", "Label Two", "Fold", "Label"; then "Renamed", "App", "Part", "Renamed Two", "Fold", "Renamed".
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task Name_IsTheAppsTheLabelsThePartsOrTheContents_InThatOrder()
    {
        var text = Signal.Create("Label");
        Element[] named = [];
        await using HeadlessApp app = await HeadlessApp.StartAsync(() =>
        {
            Element label = Elements.Text((ref w) => w.Write(text.Get()));
            Element two = Elements.Text("Two");
            Element labelled = Part().LabelledBy(label);
            Element own = Part().Accessible(new AccessibilityInfo { Name = "App" }).LabelledBy(label);
            Element part = Part();
            Element both = Part().LabelledBy(label).LabelledBy(two);
            Element fold = Button("Fold", () => { });
            Element inner = Button("Inner", () => { }).LabelledBy(label);
            named = [labelled, own, part, both, fold, inner];
            return Column([label, two, .. named]);
        });
        _ = app.PumpAndSettle();
        string?[] before = Names();
        text.Set("Renamed");
        _ = app.PumpAndSettle();

        Assert.Equal(["Label", "App", "Part", "Label Two", "Fold", "Label"], before);
        Assert.Equal(["Renamed", "App", "Part", "Renamed Two", "Fold", "Renamed"], Names());

        string?[] Names() => [.. named.Select(e => app.AccessibilityOf(e).Name)];

        static Element Part()
        {
            Element box = Box().Width(20).Height(10);
            box.Tree!.SetPartAccessibility(box.Node, new AccessibilityInfo { Role = SemanticsRole.Button, Name = "Part" });
            return box;
        }
    }

    /// <summary>
    /// A description starts with the text of the error message only while the node is invalid, then the app's description and the
    /// text of the nodes that describe it, joined by a space; a node invalid with no error message gets no word of it.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A field of the role Edit, described by the app as "Password", from a signal of its information, with the error message "Too
    ///   short" and described by "8 or more"; a field "Code" invalid with no error message.
    ///
    /// Steps:
    /// - Read the first field's description; make it invalid; settle; read it; make it valid; settle; read it. Read the second's.
    ///
    /// Expected:
    /// - "Password 8 or more"; "Too short Password 8 or more"; "Password 8 or more"; "Code".
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task Description_StartsWithTheErrorMessage_OnlyWhileInvalid()
    {
        var valid = new AccessibilityInfo { Role = SemanticsRole.Edit, Description = "Password" };
        var info = Signal.Create(valid);
        Element field = default;
        Element alone = default;
        await using HeadlessApp app = await HeadlessApp.StartAsync(() =>
        {
            Element error = Elements.Text("Too short");
            Element help = Elements.Text("8 or more");
            field = Box().Width(50).Height(10).Accessible(info).ErrorMessage(error).DescribedBy(help);
            alone = Box()
                .Width(50)
                .Height(10)
                .Accessible(
                    new AccessibilityInfo
                    {
                        Role = SemanticsRole.Edit,
                        Description = "Code",
                        States = SemanticsStates.Invalid,
                    }
                );
            return Column(field, error, help, alone);
        });
        _ = app.PumpAndSettle();
        var seen = new List<string?> { app.AccessibilityOf(field).Description };
        info.Set(valid with { States = SemanticsStates.Invalid });
        _ = app.PumpAndSettle();
        seen.Add(app.AccessibilityOf(field).Description);
        info.Set(valid);
        _ = app.PumpAndSettle();
        seen.Add(app.AccessibilityOf(field).Description);

        Assert.Equal(["Password 8 or more", "Too short Password 8 or more", "Password 8 or more"], seen);
        Assert.Equal("Code", app.AccessibilityOf(alone).Description);
    }

    /// <summary>
    /// Writing the signal of an active descendant makes again the node that holds it alone, and the descendant may be in a popup.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A button "File" anchoring a menu of "New" and "Open", whose active descendant is a signal; the tree made; the menu open; a
    ///   bridge that records what it is handed.
    ///
    /// Steps:
    /// - Set the signal to "New"; settle; read the record, the nodes made again, and the active descendant. Set it to "Open"; settle;
    ///   read the active descendant. Set it to default; settle; read it.
    ///
    /// Expected:
    /// - One node updated, the button's, which alone is made again, with the active descendant "New"; then "Open"; then none.
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task ActiveDescendant_Signal_MakesTheHolderAloneAgain_AlsoIntoAPopup()
    {
        var open = Signal.Create(false);
        var active = Signal.Create<Element>(default);
        Element anchor = default;
        Element first = default;
        Element second = default;
        await using HeadlessApp app = await HeadlessApp.StartAsync(() =>
            anchor = Button("File", () => open.Set(true))
                .Menu(open, () => Column(first = MenuItem(Elements.Text("New"), () => { }), second = MenuItem(Elements.Text("Open"), () => { })))
                .ActiveDescendant(active)
        );
        _ = app.PumpAndSettle();
        _ = app.AccessibilityTree();
        open.Set(true);
        _ = app.PumpAndSettle();
        var bridge = new Recorder();
        app.Tree.Accessibility.Bridge = bridge;

        active.Set(first);
        _ = app.PumpAndSettle();
        AccessibilityChange change = Assert.Single(bridge.Changes);
        int refreshed = app.Tree.Accessibility.LastRefreshed;
        Element atFirst = app.AccessibilityOf(anchor).ActiveDescendant;
        active.Set(second);
        _ = app.PumpAndSettle();
        Element atSecond = app.AccessibilityOf(anchor).ActiveDescendant;
        active.Set(default);
        _ = app.PumpAndSettle();

        Assert.Equal((app.Tree.Accessibility.Find(anchor.Node)!.Id, AccessibilityChangeKind.Updated), (change.Id, change.Kind));
        Assert.Equal(1, refreshed);
        Assert.Equal((first, second), (atFirst, atSecond));
        Assert.True(app.AccessibilityOf(anchor).ActiveDescendant.IsNone);
    }

    /// <summary>
    /// Adding or removing a handler of an action marks the node's own node alone: one node is made again.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A column of a button and a spin button with the range 0 to 10 at 5 and no handler; the tree made; a bridge that records.
    ///
    /// Steps:
    /// - Add a handler of Increment in a scope; read the marks; settle; read the record and the nodes made again. Dispose the scope;
    ///   settle; read them again.
    ///
    /// Expected:
    /// - One mark; the spin button alone updated and made again, no longer read-only; then the same, read-only again.
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task Handlers_AddedOrRemoved_MarkTheNodeAlone()
    {
        Element spin = default;
        await using HeadlessApp app = await HeadlessApp.StartAsync(() => Column(Button("b", () => { }), spin = Spin("spin")));
        _ = app.PumpAndSettle();
        _ = app.AccessibilityTree();
        var bridge = new Recorder();
        app.Tree.Accessibility.Bridge = bridge;
        AccessibilityNodeId id = app.Tree.Accessibility.Find(spin.Node)!.Id;

        ScopeHandle scope = Scope.Create(() => spin.OnIncrement(() => { }));
        int marks = app.Tree.Accessibility.MarkedCount;
        _ = app.PumpAndSettle();
        (int, AccessibilityNodeId, bool) added = (
            app.Tree.Accessibility.LastRefreshed,
            Assert.Single(bridge.Changes).Id,
            (app.AccessibilityOf(spin).States & SemanticsStates.ReadOnly) != 0
        );
        bridge.Changes.Clear();
        Scope.Dispose(scope);
        _ = app.PumpAndSettle();

        Assert.Equal(1, marks);
        Assert.Equal((1, id, false), added);
        Assert.Equal((1, id), (app.Tree.Accessibility.LastRefreshed, Assert.Single(bridge.Changes).Id));
        Assert.True((app.AccessibilityOf(spin).States & SemanticsStates.ReadOnly) != 0);
    }

    /// <summary>
    /// A disabled node's handlers of the actions are not called; once enabled, they are.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A disabled spin button with the range 0 to 10 at 5 and handlers of Increment, Decrement, a number, Expand, and Collapse that
    ///   count their calls; the tree made.
    ///
    /// Steps:
    /// - Ask the tree for Increment, Decrement, the number 3, Expand, and Collapse; enable it; settle; ask for Increment.
    ///
    /// Expected:
    /// - No call while disabled; then one Increment.
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task Handlers_OfADisabledNode_AreNotCalled()
    {
        int calls = 0;
        int increments = 0;
        Element spin = default;
        await using HeadlessApp app = await HeadlessApp.StartAsync(() =>
            spin = Spin("spin")
                .OnIncrement(() => increments++)
                .OnDecrement(() => calls++)
                .OnSetRangeValue(_ => calls++)
                .OnExpand(() => calls++)
                .OnCollapse(() => calls++)
                .Disabled()
        );
        _ = app.PumpAndSettle();
        AccessibilityTree tree = app.AccessibilityTree();
        AccessibilityNode node = tree.Find(spin.Node)!;

        tree.Increment(node);
        tree.Decrement(node);
        tree.SetRangeValue(node, 3);
        tree.SetExpanded(node, open: true);
        tree.SetExpanded(node, open: false);
        int whileDisabled = calls + increments;
        app.Tree.SetDisabled(spin.Node, false);
        _ = app.PumpAndSettle();
        tree.Increment(node);

        Assert.Equal((0, 1), (whileDisabled, increments));
    }

    /// <summary>
    /// A number set on a node drops NaN and is brought into the range before its handler takes it.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A spin button with the range 0 to 100 at 50 and a handler of a number that records it; the tree made.
    ///
    /// Steps:
    /// - Set NaN, 150, -5, 42, and +infinity.
    ///
    /// Expected:
    /// - The handler took 100, 0, 42, and 100.
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task SetRangeValue_DropsNaN_AndClampsIntoTheRange()
    {
        var taken = new List<double>();
        Element spin = default;
        await using HeadlessApp app = await HeadlessApp.StartAsync(() => spin = Spin("spin", 0, 100, 50).OnSetRangeValue(taken.Add));
        _ = app.PumpAndSettle();
        AccessibilityTree tree = app.AccessibilityTree();
        AccessibilityNode node = tree.Find(spin.Node)!;

        foreach (double value in new[] { double.NaN, 150, -5, 42, double.PositiveInfinity })
        {
            tree.SetRangeValue(node, value);
        }

        Assert.Equal(new double[] { 100, 0, 42, 100 }, taken);
    }

    /// <summary>
    /// With no handler of a number, a number larger than the value calls the handler of Increment once, a smaller one that of
    /// Decrement once, and the same calls neither; a direction with no handler is dropped.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A spin button "both" with the range 0 to 10 at 5 and handlers of Increment and Decrement that count; a spin button "up" with
    ///   the same range and a handler of Increment alone; the tree made.
    ///
    /// Steps:
    /// - On "both", set 6, 9, 2, 5, and NaN. On "up", set 1, then 8.
    ///
    /// Expected:
    /// - "both": 2 increments and 1 decrement. "up": 1 increment, and no failure for 1.
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task SetRangeValue_WithoutItsHandler_StepsOnceTowardTheNumber()
    {
        int increments = 0;
        int decrements = 0;
        int ups = 0;
        Element both = default;
        Element up = default;
        await using HeadlessApp app = await HeadlessApp.StartAsync(() =>
            Column(both = Spin("both").OnIncrement(() => increments++).OnDecrement(() => decrements++), up = Spin("up").OnIncrement(() => ups++))
        );
        _ = app.PumpAndSettle();
        AccessibilityTree tree = app.AccessibilityTree();
        AccessibilityNode bothNode = tree.Find(both.Node)!;
        AccessibilityNode upNode = tree.Find(up.Node)!;

        foreach (double value in new[] { 6, 9, 2, 5, double.NaN })
        {
            tree.SetRangeValue(bothNode, value);
        }

        tree.SetRangeValue(upNode, 1);
        tree.SetRangeValue(upNode, 8);

        Assert.Equal((2, 1, 1), (increments, decrements, ups));
    }

    /// <summary>
    /// A node with a range and no handler of Increment, Decrement, or a number is read-only; a handler of any of them makes it
    /// writable, and a node with no range is not made read-only.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - Spin buttons with the range 0 to 10 at 5: one with no handler, one with a handler of Increment, one with a handler of a
    ///   number; a group with no range and no handler.
    ///
    /// Steps:
    /// - Read whether each is read-only.
    ///
    /// Expected:
    /// - True, false, false, false.
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task Range_WithNoHandler_IsReadOnly()
    {
        Element[] nodes = [];
        await using HeadlessApp app = await HeadlessApp.StartAsync(() =>
        {
            nodes =
            [
                Spin("none"),
                Spin("increment").OnIncrement(() => { }),
                Spin("number").OnSetRangeValue(_ => { }),
                Box().Height(10).Accessible(new AccessibilityInfo { Role = SemanticsRole.Group, Name = "g" }),
            ];
            return Column(nodes);
        });
        _ = app.PumpAndSettle();

        Assert.Equal([true, false, false, false], nodes.Select(e => (app.AccessibilityOf(e).States & SemanticsStates.ReadOnly) != 0));
    }

    /// <summary>
    /// A node that is Expandable with no popup and no handler of Expand or Collapse is a fault a Debug build reports when the tree
    /// writes it; a Release build drops Expand on it.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A tree item that is Expandable, with no popup and no handler.
    ///
    /// Steps:
    /// - Make the tree. In Release, ask it for Expand on the item.
    ///
    /// Expected:
    /// - Debug: an invariant failure. Release: no failure, and the item stays closed.
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task Expandable_WithNoPopupAndNoHandler_IsReportedInDebug()
    {
        Element item = default;
        await using HeadlessApp app = await HeadlessApp.StartAsync(() =>
            item = Box()
                .Height(10)
                .Accessible(
                    new AccessibilityInfo
                    {
                        Role = SemanticsRole.TreeItem,
                        Name = "folder",
                        States = SemanticsStates.Expandable,
                    }
                )
        );
        _ = app.PumpAndSettle();

#if DEBUG
        Exception failed = Assert.ThrowsAny<Exception>(() => app.AccessibilityTree());
        Assert.Equal("InvariantViolationException", failed.GetType().Name);
#else
        AccessibilityTree tree = app.AccessibilityTree();
        tree.SetExpanded(tree.Find(item.Node)!, open: true);
        _ = app.PumpAndSettle();
        Assert.False(app.AccessibilityOf(item).IsExpanded);
#endif
    }

    /// <summary>
    /// A draw function of nodes is called only while the tree is made; a node of the same key keeps its id when the function runs
    /// again, the function runs again when a signal it read changes, and an active descendant given by a key points at that node.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A focusable figure 100 x 50 whose draw function counts its calls and adds a series of key 1 at (5, 5) holding a point of key
    ///   2 at (15, 15) named by a signal "a", then a point of key 3; its active descendant is a signal of a key, null at first.
    ///
    /// Steps:
    /// - Settle; read the calls. Make the tree; read the nodes. Set the key to 2; settle; read the active descendant. Set the name to
    ///   "b"; settle; read the calls and the nodes. Set the key to 9; settle; read the active descendant.
    ///
    /// Expected:
    /// - No call before the tree. The series holds the point "a" at (10, 10) in its coordinates, then the point of key 3. The active
    ///   descendant is the point of key 2. After the rename, one more call, the point named "b" with the same id. The key 9 is none.
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task DrawnNodes_AreMadeWithTheTree_KeepTheirIds_AndFollowTheirSignals()
    {
        int calls = 0;
        var name = Signal.Create("a");
        var key = Signal.Create<int?>(null);
        var series = new AccessibilityInfo { Role = SemanticsRole.Group, Name = "series" };
        Element figure = default;
        await using HeadlessApp app = await HeadlessApp.StartAsync(() =>
            figure = Box()
                .Width(100)
                .Height(50)
                .Focusable()
                .Accessible(new AccessibilityInfo { Role = SemanticsRole.Figure })
                .AccessibleChildren(
                    (children, bounds) =>
                    {
                        calls++;
                        children.Begin(1, new Rect(5, 5, 50, 40), series);
                        children.Add(2, new Rect(15, 15, 5, 5), new AccessibilityInfo { Role = SemanticsRole.Image, Name = name.Get() });
                        children.End();
                        children.Add(3, new Rect(60, 10, 5, 5), new AccessibilityInfo { Role = SemanticsRole.Image, Name = "c" });
                    }
                )
                .ActiveDescendant(key)
        );
        _ = app.PumpAndSettle();
        int before = calls;
        AccessibilityNode node = app.AccessibilityTree().Find(figure.Node)!;
        AccessibilityNode point = node.Synthetic[0].Children[0];
        (string?, Rect, int?) seen = (point.Name, point.Bounds, node.Synthetic[1].ChildKey);
        key.Set(2);
        _ = app.PumpAndSettle();
        AccessibilityNodeId active = node.ActiveDescendant;
        int beforeRename = calls;
        name.Set("b");
        _ = app.PumpAndSettle();
        AccessibilityNode renamed = node.Synthetic[0].Children[0];
        int afterRename = calls;
        key.Set(9);
        _ = app.PumpAndSettle();

        Assert.Equal(0, before);
        Assert.Equal(("a", new Rect(10, 10, 5, 5), (int?)3), seen);
        Assert.Equal(point.Id, active);
        Assert.Equal((beforeRename + 1, point.Id, "b"), (afterRename, renamed.Id, renamed.Name));
        Assert.Equal(default(AccessibilityNodeId), node.ActiveDescendant);
    }

    /// <summary>
    /// A relation to a node that was removed throws <see cref="ArgumentException"/>.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A field of the role Edit, and a text "gone" a Show holds, its condition true.
    ///
    /// Steps:
    /// - Turn the Show off; settle; label the field by "gone".
    ///
    /// Expected:
    /// - An exception.
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task Relations_ToARemovedNode_Throw()
    {
        var shown = Signal.Create(true);
        Element field = default;
        Element gone = default;
        await using HeadlessApp app = await HeadlessApp.StartAsync(() =>
        {
            field = Box().Width(50).Height(10).Accessible(new AccessibilityInfo { Role = SemanticsRole.Edit });
            return Column(field, Show(shown, () => gone = Elements.Text("gone")));
        });
        _ = app.PumpAndSettle();
        shown.Set(false);
        _ = app.PumpAndSettle();

        _ = Assert.Throws<ArgumentException>(() => field.LabelledBy(gone));
    }

    /// <summary>
    /// A holder and the node it names, made and removed again and again while the tree is made, leave nothing in the tables: no
    /// relation, no holder kept for the node named, and no watch.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A Show, true, of a column of a text "Name" and a field of the role Edit labelled by it, whose active descendant is a signal at
    ///   the text; the tree made.
    ///
    /// Steps:
    /// - 20 times: turn the Show off and on, settling after each. Read how many nodes named the tree keeps. Turn it off; settle; read
    ///   the tables.
    ///
    /// Expected:
    /// - One node named while shown; then no relation in the tree's table, no node named kept, and no watch.
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task Relations_MadeAndRemovedManyTimes_LeaveNoTables()
    {
        var shown = Signal.Create(true);
        var active = Signal.Create<Element>(default);
        await using HeadlessApp app = await HeadlessApp.StartAsync(() =>
            Column(
                Show(
                    shown,
                    () =>
                    {
                        Element label = Elements.Text("Name");
                        Element field = Box()
                            .Width(50)
                            .Height(10)
                            .Accessible(new AccessibilityInfo { Role = SemanticsRole.Edit })
                            .LabelledBy(label)
                            .ActiveDescendant(active);
                        active.Set(label);
                        return Column(label, field);
                    }
                )
            )
        );
        _ = app.PumpAndSettle();
        AccessibilityTree tree = app.AccessibilityTree();
        for (int i = 0; i < 20; i++)
        {
            shown.Set(false);
            _ = app.PumpAndSettle();
            shown.Set(true);
            _ = app.PumpAndSettle();
        }

        int whileShown = tree.RelationTargetCount;
        shown.Set(false);
        _ = app.PumpAndSettle();

        Assert.Equal((1, 0, 0, 0), (whileShown, tree.RelationTargetCount, tree.WatchCount, app.Tree.AccessibilityRelationHolders.Count));
    }

    /// <summary>
    /// The name a field takes from a node with no node of its own follows that node being hidden and shown by a change of an
    /// ancestor of it.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A box "outer" holding a box with no role that holds a text "Hello", and a field of the role Edit labelled by the inner box.
    ///
    /// Steps:
    /// - Read the field's name; hide "outer"; settle; read it; show "outer"; settle; read it.
    ///
    /// Expected:
    /// - "Hello", none, "Hello".
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task Name_FromATargetWithNoNode_FollowsAnAncestorHidingIt()
    {
        Element outer = default;
        Element field = default;
        await using HeadlessApp app = await HeadlessApp.StartAsync(() =>
        {
            Element target = Box(Elements.Text("Hello"));
            outer = Box(target);
            field = Box().Width(50).Height(10).Accessible(new AccessibilityInfo { Role = SemanticsRole.Edit }).LabelledBy(target);
            return Column(outer, field);
        });
        _ = app.PumpAndSettle();
        var names = new List<string?> { app.AccessibilityOf(field).Name };
        SetHidden(true);
        SetHidden(false);

        Assert.Equal(new string?[] { "Hello", null, "Hello" }, names);

        void SetHidden(bool value)
        {
            app.Tree.SetStyle(outer.Node, app.Tree.Nodes.GetRef(outer.Node).Layout.Style with { Hidden = value });
            _ = app.PumpAndSettle();
            names.Add(app.AccessibilityOf(field).Name);
        }
    }

    /// <summary>
    /// A node whose information stops giving a level, a live setting, an orientation, and steps is written back to the defaults, and
    /// keeps its side object; a node that never took one has none.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A slider whose information is a signal: the level 3, the live setting Assertive, the orientation Vertical, and the range 0
    ///   to 10 at 5 with the steps 1 and 2; and a button "plain". The tree made.
    ///
    /// Steps:
    /// - Read the slider's node. Set the signal to information of the role Slider with the range 0 to 10 at 5 and nothing else; settle;
    ///   read the node again, and the button's.
    ///
    /// Expected:
    /// - First the level 2 (from 0), Assertive, Vertical, the steps 1 and 2. Then -1, Inherit, None, the steps 0 and 0, and a side
    ///   object still there. The button has no side object.
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task AddedValues_GoneFromTheInformation_GoBackToTheDefaults()
    {
        var plainInfo = new AccessibilityInfo
        {
            Role = SemanticsRole.Slider,
            Name = "s",
            Range = new SemanticsRange(0, 10, 5),
        };
        var info = Signal.Create(
            plainInfo with
            {
                Level = 3,
                Live = SemanticsLive.Assertive,
                Orientation = SemanticsOrientation.Vertical,
                Range = new SemanticsRange(0, 10, 5, Step: 1, LargeStep: 2),
            }
        );
        Element slider = default;
        Element plain = default;
        await using HeadlessApp app = await HeadlessApp.StartAsync(() =>
        {
            slider = Box().Width(50).Height(10).Accessible(info);
            plain = Button("plain", () => { });
            return Column(slider, plain);
        });
        _ = app.PumpAndSettle();
        AccessibilityTree tree = app.AccessibilityTree();
        AccessibilityNode node = tree.Find(slider.Node)!;
        (int, SemanticsLive, SemanticsOrientation, double, double) before = Read();

        info.Set(plainInfo);
        _ = app.PumpAndSettle();

        Assert.Equal((2, SemanticsLive.Assertive, SemanticsOrientation.Vertical, 1.0, 2.0), before);
        Assert.Equal((-1, SemanticsLive.Inherit, SemanticsOrientation.None, 0.0, 0.0), Read());
        Assert.NotNull(node.More);
        Assert.Null(tree.Find(plain.Node)!.More);

        (int, SemanticsLive, SemanticsOrientation, double, double) Read() =>
            (node.Level, node.Live, node.Orientation, node.Range!.Value.Step, node.Range!.Value.LargeStep);
    }

    // A spin button with a range and no handler: read-only until one is added.
    private static Element Spin(string name, double minimum = 0, double maximum = 10, double value = 5) =>
        Box()
            .Width(40)
            .Height(10)
            .Accessible(
                new AccessibilityInfo
                {
                    Role = SemanticsRole.SpinButton,
                    Name = name,
                    Range = new SemanticsRange(minimum, maximum, value),
                }
            );
}
