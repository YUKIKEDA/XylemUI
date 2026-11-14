using System.Numerics;
using XylemUI.Primitives;
using XylemUI.Testing.XUnit;
using static XylemUI.Elements;
using static XylemUI.Primitives.Primitive;

namespace XylemUI.Desktop.Tests;

// The accessibility tree (row P02g, design §4.4.1, "The shape of P02g") in a headless app: its shape, the names and states of its
// nodes, the parts' roles, and how an update makes again only what changed.
public sealed partial class AccessibilityTests
{
    /// <summary>
    /// The tree has a node for each node with a role, under the nearest one above it: the nodes with no role are left out and their
    /// children go up, a hidden subtree is left out, a box that scrolls is a scroll viewer, a text no node takes as its name is a text,
    /// and the window is the root.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A column of a button "OK", a box holding a text "label", a hidden part holding a button "gone", a scroll view holding a
    ///   column of a button "A", and a text field holding "typed".
    ///
    /// Steps:
    /// - Read the roles of the root's children, and of the scroll viewer's.
    ///
    /// Expected:
    /// - The root is the window, with a button, a text, a scroll viewer, and an edit, in that order; the scroll viewer has the button
    ///   "A"; the edit's value is "typed"; no node is named "gone".
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task Tree_HasANodeForEachRole_UnderTheNearestOneAbove()
    {
        var text = Signal.Create("typed");
        await using HeadlessApp app = await HeadlessApp.StartAsync(() =>
            Column(
                Button("OK", () => { }),
                Box(Elements.Text("label")),
                Show(Signal.Create(false), () => Button("gone", () => { })),
                ScrollView(Column(Button("A", () => { }))).Height(40),
                TextField(text)
            )
        );
        _ = app.PumpAndSettle();
        AccessibilityNode root = app.AccessibilityTree().Root!;

        Assert.Equal(SemanticsRole.Window, root.Role);
        Assert.Equal([SemanticsRole.Button, SemanticsRole.Text, SemanticsRole.ScrollViewer, SemanticsRole.Edit], root.Children.Select(c => c.Role));
        Assert.Equal(["A"], root.Children[2].Children.Select(c => c.Name));
        Assert.Equal("typed", root.Children[3].Value);
        Assert.Empty(app.FindByRole(SemanticsRole.Button, "gone"));
        Assert.Equal("label", root.Children[1].Name);
    }

    /// <summary>
    /// A node whose role takes its name from its content is named by the text of its descendants with no role, joined by spaces; a
    /// name the app gives wins, and a role of None leaves the node out.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A button whose content is a row of "Save" and "all"; a button "Close" that the app names "Close the window"; a button
    ///   "Hidden" whose role the app sets to None; a scroll view of a text "Inside" whose role the app sets to None.
    ///
    /// Steps:
    /// - Find the buttons and read their names.
    ///
    /// Expected:
    /// - "Save all" and "Close the window"; no third button, and no scroll viewer. The texts "Hidden" and "Inside" are text nodes
    ///   of their own, under the window, as the children of a node with no role go up; "Save" and "all" are not.
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task Name_IsTheContentsText_UnlessTheAppGivesOne()
    {
        await using HeadlessApp app = await HeadlessApp.StartAsync(() =>
            Column(
                Button(Row(Elements.Text("Save"), Elements.Text("all")), () => { }),
                Button("Close", () => { }).Accessible(new AccessibilityInfo { Name = "Close the window" }),
                Button("Hidden", () => { }).Accessible(new AccessibilityInfo { Role = SemanticsRole.None }),
                ScrollView(Elements.Text("Inside")).Height(40).Accessible(new AccessibilityInfo { Role = SemanticsRole.None })
            )
        );
        _ = app.PumpAndSettle();

        Assert.Equal(["Save all", "Close the window"], app.FindByRole(SemanticsRole.Button).Select(b => app.AccessibilityOf(b).Name));
        Assert.Empty(app.FindByRole(SemanticsRole.ScrollViewer));
        Assert.Equal(["Hidden", "Inside"], app.FindByRole(SemanticsRole.Text).Select(t => app.AccessibilityOf(t).Name));
    }

    /// <summary>
    /// The states come from the node: disabled and focused from its pseudo-states, focusable from its input, expanded from its popup,
    /// and the app's own states from its information.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A disabled button "Off"; a button "Menu" anchoring a menu with one item; a node with the role CheckBox, the name "Agree",
    ///   and the state Checked.
    ///
    /// Steps:
    /// - Read the states; focus "Menu"; settle; read it; open its menu, which moves the focus into it; settle; read it again.
    ///
    /// Expected:
    /// - "Off" disabled; "Menu" focusable, not focused, not expanded; "Agree" checked and not expandable. Then "Menu" focused; then
    ///   expanded and no longer focused.
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task States_ComeFromThePseudoStates_ThePopup_AndTheInformation()
    {
        var open = Signal.Create(false);
        Element menu = default;
        await using HeadlessApp app = await HeadlessApp.StartAsync(() =>
            Column(
                Button("Off", () => { }).Disabled(),
                menu = Button("Menu", () => open.Set(true)).Menu(open, () => Column(MenuItem(Elements.Text("New"), () => { }))),
                Box()
                    .Accessible(
                        new AccessibilityInfo
                        {
                            Role = SemanticsRole.CheckBox,
                            Name = "Agree",
                            States = SemanticsStates.Checked,
                        }
                    )
            )
        );
        _ = app.PumpAndSettle();
        AccessibilityNodeInfo off = app.AccessibilityOf(app.FindByRole(SemanticsRole.Button, "Off")[0]);
        AccessibilityNodeInfo before = app.AccessibilityOf(menu);
        AccessibilityNodeInfo agree = app.AccessibilityOf(app.FindByRole(SemanticsRole.CheckBox)[0]);
        Assert.True(app.Focus(menu));
        _ = app.PumpAndSettle();
        bool focused = app.AccessibilityOf(menu).IsFocused;
        open.Set(true);
        _ = app.PumpAndSettle();
        AccessibilityNodeInfo after = app.AccessibilityOf(menu);

        Assert.True(off.IsDisabled);
        Assert.Equal((true, false, (bool?)false), (before.IsFocusable, before.IsFocused, before.IsExpanded));
        Assert.Equal((SemanticsStates.Checked, (bool?)null), (agree.States, agree.IsExpanded));
        Assert.True(focused);
        Assert.Equal((false, (bool?)true), (after.IsFocused, after.IsExpanded));
    }

    /// <summary>
    /// An open popup's nodes come after the window's tree, under the root; a menu's panel is a menu, its items menu items, and a
    /// node goes away when its popup closes.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A button "File" anchoring a menu of "New" and "Open".
    ///
    /// Steps:
    /// - Open the menu; settle; read the root's children and the menu's. Close it; settle; read the root's children.
    ///
    /// Expected:
    /// - Button, then menu, whose items are the menu items "New" and "Open"; after closing, the button alone.
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task Popup_IsAfterTheWindowsTree_AndGoesWhenItCloses()
    {
        var open = Signal.Create(false);
        await using HeadlessApp app = await HeadlessApp.StartAsync(() =>
            Button("File", () => open.Set(true))
                .Menu(open, () => Column(MenuItem(Elements.Text("New"), () => { }), MenuItem(Elements.Text("Open"), () => { })))
        );
        _ = app.PumpAndSettle();
        _ = app.AccessibilityTree();
        open.Set(true);
        _ = app.PumpAndSettle();
        AccessibilityNode root = app.AccessibilityTree().Root!;
        SemanticsRole[] opened = [.. root.Children.Select(c => c.Role)];
        string?[] items = [.. root.Children[^1].Children.Select(c => c.Name)];
        open.Set(false);
        _ = app.PumpAndSettle();
        SemanticsRole[] closed = [.. app.AccessibilityTree().Root!.Children.Select(c => c.Role)];

        Assert.Equal([SemanticsRole.Button, SemanticsRole.Menu], opened);
        Assert.Equal(["New", "Open"], items);
        Assert.Equal([SemanticsRole.Button], closed);
    }

    /// <summary>
    /// After the tree is made, a change makes again only the nodes it reaches, and hands the Platform only those; before it is made, a
    /// change marks nothing.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A column of 50 buttons, one of which shows a signal's text; a bridge that records what it is handed.
    ///
    /// Steps:
    /// - Change the text before the tree is made; read the marks. Make the tree; clear the record. Change the text; settle; read the
    ///   record and the nodes made again. Settle once more with no change; read the record.
    ///
    /// Expected:
    /// - No mark before the tree; then the one button updated (with its new name), at most three nodes made again; then nothing.
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task Update_MakesAgainOnlyWhatChanged()
    {
        var label = Signal.Create("one");
        Element changing = default;
        await using HeadlessApp app = await HeadlessApp.StartAsync(() =>
        {
            var buttons = new Element[50];
            for (int i = 0; i < buttons.Length; i++)
            {
                buttons[i] =
                    i == 20 ? changing = Button(Elements.Text((ref text) => text.Write(label.Get())), () => { }) : Button($"b{i}", () => { });
            }

            return Column(buttons);
        });
        _ = app.PumpAndSettle();
        label.Set("two");
        _ = app.PumpAndSettle();
        int markedBefore = app.Tree.Accessibility.MarkedCount;
        var bridge = new Recorder();
        app.Tree.Accessibility.Bridge = bridge;
        _ = app.AccessibilityTree();
        bridge.Changes.Clear();

        label.Set("three");
        _ = app.PumpAndSettle();
        List<AccessibilityChange> changed = [.. bridge.Changes];
        int refreshed = app.Tree.Accessibility.LastRefreshed;
        bridge.Changes.Clear();
        _ = app.PumpAndSettle();

        Assert.Equal(0, markedBefore);
        AccessibilityChange change = Assert.Single(changed);
        Assert.Equal(AccessibilityChangeKind.Updated, change.Kind);
        Assert.Equal("three", app.Tree.Accessibility.Find(change.Id)!.Name);
        Assert.Equal(changing.Node, app.Tree.Accessibility.Find(change.Id)!.Node);
        Assert.InRange(refreshed, 1, 3);
        Assert.Empty(bridge.Changes);
    }

    /// <summary>
    /// A scroll moves only the scroll viewer's offset for its children: the children's boxes in its coordinates stay, and the box
    /// assistive technology reads in the window moves.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A scroll view 100 high holding a column of five buttons 50 high; a bridge that records what it is handed; the tree made.
    ///
    /// Steps:
    /// - Scroll to 60; settle; read the record and the second button's box in the window.
    ///
    /// Expected:
    /// - The scroll viewer alone updated; the second button at y = 50 - 60 = -10 in the window.
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task Scroll_UpdatesTheScrollViewerAlone()
    {
        var state = new ScrollState();
        Element second = default;
        await using HeadlessApp app = await HeadlessApp.StartAsync(() =>
        {
            var buttons = new Element[5];
            for (int i = 0; i < buttons.Length; i++)
            {
                buttons[i] = Button($"b{i}", () => { }).Height(50);
            }

            second = buttons[1];
            return Column(ScrollView(Column(buttons), state).Height(100));
        });
        _ = app.PumpAndSettle();
        var bridge = new Recorder();
        app.Tree.Accessibility.Bridge = bridge;
        _ = app.AccessibilityTree();
        bridge.Changes.Clear();

        state.ScrollTo(new Vector2(0, 60));
        _ = app.PumpAndSettle();

        AccessibilityChange change = Assert.Single(bridge.Changes);
        Assert.Equal(SemanticsRole.ScrollViewer, app.Tree.Accessibility.Find(change.Id)!.Role);
        Assert.Equal(-10, app.AccessibilityOf(second).Bounds.Y);
    }

    /// <summary>
    /// A frame of a window whose accessibility tree is made, delivered and drawn, allocates nothing when it changes no name: the focus
    /// moving among two buttons, a field that has relations, a handler of a number, and a live setting, and a figure whose draw
    /// function adds nodes, marks them, and their nodes are made again with the same names, relations, and drawn nodes; the pointer
    /// moving over the buttons changes their hover, which marks nothing.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - Two buttons with a hovered look; a focusable field of the role SpinButton with the range 0 to 10 at 5, the live setting
    ///   Polite, a handler of a number, a text "Count" that names it, a text "Help" that describes it, a text "Wrong" as its error
    ///   message, and its active descendant's signal at "Help"; a focusable box of the role Figure whose draw function adds a series
    ///   of two points from information made once; the tree made; warmed up with the iterations.
    ///
    /// Steps:
    /// - 1,000 iterations in the windows of <c>PerformanceAssert.DoesNotAllocate</c>: by the iteration's number, focus the first
    ///   button and move the pointer over it, focus the second and move the pointer over it, focus the field, or focus the figure;
    ///   settle.
    ///
    /// Expected:
    /// - 0 bytes in every window.
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task SteadyState_FocusAndHoverWithTheTree_AllocatesNothing()
    {
        var look = new PaintStyle { Background = new Paint(Color.FromRgb(0x336699)) };
        var series = new AccessibilityInfo { Role = SemanticsRole.Group, Name = "series" };
        var point = new AccessibilityInfo { Role = SemanticsRole.Image, Name = "point" };
        Element first = default;
        Element other = default;
        Element field = default;
        Element figure = default;
        var active = Signal.Create<Element>(default);
        await using HeadlessApp app = await HeadlessApp.StartAsync(() =>
        {
            Element count = Elements.Text("Count");
            Element help = Elements.Text("Help");
            Element wrong = Elements.Text("Wrong");
            field = Box()
                .Width(100)
                .Height(20)
                .Focusable()
                .Accessible(
                    new AccessibilityInfo
                    {
                        Role = SemanticsRole.SpinButton,
                        Range = new SemanticsRange(0, 10, 5, Step: 1),
                        Live = SemanticsLive.Polite,
                        States = SemanticsStates.Invalid,
                    }
                )
                .OnSetRangeValue(_ => { })
                .LabelledBy(count)
                .DescribedBy(help)
                .ErrorMessage(wrong)
                .ActiveDescendant(active);
            active.Set(help);
            figure = Box()
                .Width(100)
                .Height(40)
                .Focusable()
                .Accessible(new AccessibilityInfo { Role = SemanticsRole.Figure })
                .AccessibleChildren(
                    (children, bounds) =>
                    {
                        children.Begin(1, bounds, series);
                        children.Add(2, new Rect(0, 0, 10, 10), point);
                        children.Add(3, new Rect(10, 0, 10, 10), point);
                        children.End();
                    }
                );
            return Column(
                first = Button("first", () => { }).Width(100).Height(30).Hovered(look),
                other = Button("second", () => { }).Width(100).Height(30).Hovered(look),
                count,
                help,
                wrong,
                field,
                figure
            );
        });
        _ = app.PumpAndSettle();
        _ = app.AccessibilityTree();
        (int x0, int y0) = app.CenterOf(first);
        (int x1, int y1) = app.CenterOf(other);
        void Step(int index)
        {
            switch (((index % 4) + 4) % 4)
            {
                case 0:
                    _ = app.Focus(first);
                    app.Pointer.Move(x0, y0);
                    break;
                case 1:
                    _ = app.Focus(other);
                    app.Pointer.Move(x1, y1);
                    break;
                case 2:
                    _ = app.Focus(field);
                    break;
                default:
                    _ = app.Focus(figure);
                    break;
            }

            _ = app.PumpAndSettle();
        }

        for (int i = 0; i < 40; i++)
        {
            Step(i);
        }

        PerformanceAssert.DoesNotAllocate(1_000, Step);
    }

    /// <summary>
    /// A hidden subtree is not in the tree, and comes into it when it shows; a part a condition takes out goes away, and comes back.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A column of a box holding a button "inside", hidden by its style before the tree is made, and a Show 30 high of a button
    ///   "shown" 20 high whose condition is true: taking the button out changes no node's box.
    ///
    /// Steps:
    /// - Make the tree; read which buttons it has. Show the box; settle; read. Set the condition false; settle; read. Set it true;
    ///   settle; read.
    ///
    /// Expected:
    /// - "shown"; then "inside" and "shown"; then "inside"; then "inside" and "shown".
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task HiddenAndShownParts_LeaveTheTree_AndComeBack()
    {
        var when = Signal.Create(true);
        Element box = default;
        await using HeadlessApp app = await HeadlessApp.StartAsync(() =>
            Column(box = Box(Button("inside", () => { })), Show(when, () => Button("shown", () => { }).Height(20)).Height(30))
        );
        app.Tree.SetStyle(box.Node, app.Tree.Nodes.GetRef(box.Node).Layout.Style with { Hidden = true });
        _ = app.PumpAndSettle();
        string Buttons() => string.Join(',', app.FindByRole(SemanticsRole.Button).Select(b => app.AccessibilityOf(b).Name));
        var seen = new List<string> { Buttons() };
        app.Tree.SetStyle(box.Node, app.Tree.Nodes.GetRef(box.Node).Layout.Style with { Hidden = false });
        _ = app.PumpAndSettle();
        seen.Add(Buttons());
        when.Set(false);
        _ = app.PumpAndSettle();
        seen.Add(Buttons());
        when.Set(true);
        _ = app.PumpAndSettle();
        seen.Add(Buttons());

        Assert.Equal(["shown", "inside,shown", "inside", "inside,shown"], seen);
    }

    /// <summary>
    /// A node the layout moves takes its new box, also below a node the update does not make again, and an update records each node
    /// once, also a node whose own values and box both change.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A column of a button "first" 30 high, a button "second" 30 high, and a box 200 wide with no role holding a group that
    ///   holds a button "third" as wide as the box; a bridge that records what it is handed; the tree made.
    ///
    /// Steps:
    /// - Make "first" 60 high and name it "First"; make the box 300 wide; settle; read the record and the boxes.
    ///
    /// Expected:
    /// - Each node's id once in the record, "first" and "second" among them; "second" at y = 60; "third" 300 wide, as it was 200.
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task Move_GivesTheNewBox_AndRecordsEachNodeOnce()
    {
        Element first = default;
        Element second = default;
        Element third = default;
        Element box = default;
        var group = new AccessibilityInfo { Role = SemanticsRole.Group };
        await using HeadlessApp app = await HeadlessApp.StartAsync(() =>
            Column(
                first = Button("first", () => { }).Height(30),
                second = Button("second", () => { }).Height(30),
                box = Column(Column(third = Button("third", () => { })).Accessible(group)).Width(200)
            )
        );
        _ = app.PumpAndSettle();
        float before = app.AccessibilityOf(third).Bounds.Width;
        var bridge = new Recorder();
        app.Tree.Accessibility.Bridge = bridge;
        _ = app.AccessibilityTree();
        bridge.Changes.Clear();

        app.Tree.SetStyle(first.Node, app.Tree.Nodes.GetRef(first.Node).Layout.Style with { Height = Length.Px(60) });
        app.Tree.SetAccessibility(first.Node, new AccessibilityInfo { Name = "First" });
        app.Tree.SetStyle(box.Node, app.Tree.Nodes.GetRef(box.Node).Layout.Style with { Width = Length.Px(300) });
        _ = app.PumpAndSettle();
        AccessibilityNodeId firstId = app.Tree.Accessibility.Find(first.Node)!.Id;
        AccessibilityNodeId secondId = app.Tree.Accessibility.Find(second.Node)!.Id;

        Assert.Equal(bridge.Changes.Count, bridge.Changes.Select(c => c.Id).Distinct().Count());
        Assert.Contains(bridge.Changes, c => c.Id == firstId);
        Assert.Contains(bridge.Changes, c => c.Id == secondId);
        Assert.Equal(60, app.AccessibilityOf(second).Bounds.Y);
        Assert.Equal((200f, 300f), (before, app.AccessibilityOf(third).Bounds.Width));
    }

    /// <summary>
    /// A row a list takes out leaves the tree, though no node's box changes.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A column 60 high whose rows, from the items "a" and "b", are buttons 20 high; the tree made.
    ///
    /// Steps:
    /// - Set the items to "a"; settle; read which buttons the tree has.
    ///
    /// Expected:
    /// - "a" alone.
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task RowTakenOut_LeavesTheTree_ThoughNoBoxChanges()
    {
        var items = Signal.Create<IReadOnlyList<string>>(["a", "b"]);
        await using HeadlessApp app = await HeadlessApp.StartAsync(() =>
            Column().Height(60).For(items, item => item, item => Button(item.Get(), () => { }).Height(20))
        );
        _ = app.PumpAndSettle();
        _ = app.AccessibilityTree();

        items.Set(["a"]);
        _ = app.PumpAndSettle();

        Assert.Equal(["a"], app.FindByRole(SemanticsRole.Button).Select(b => app.AccessibilityOf(b).Name));
    }

    /// <summary>
    /// A text field's value is its text, also after the text changes; a password field has none.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A column of a text field and a password field, both with the text "one"; the tree made.
    ///
    /// Steps:
    /// - Set both texts to "two"; settle; read the values of both edits.
    ///
    /// Expected:
    /// - "two" for the field, none for the password field.
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task EditValue_FollowsTheText_AndAPasswordHasNone()
    {
        var text = Signal.Create("one");
        var secret = Signal.Create("one");
        Element field = default;
        Element password = default;
        await using HeadlessApp app = await HeadlessApp.StartAsync(() =>
            Column(field = TextField(text), password = TextField(secret, new TextFieldOptions { Password = true }))
        );
        _ = app.PumpAndSettle();
        _ = app.AccessibilityTree();

        text.Set("two");
        secret.Set("two");
        _ = app.PumpAndSettle();

        Assert.Equal(("two", (string?)null), (app.AccessibilityOf(field).Value, app.AccessibilityOf(password).Value));
    }

    /// <summary>
    /// A text field's node has the field's placeholder, also when the app gives the field information of its own; a field with none
    /// has none.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A column of a text field with the placeholder "What needs doing?", which the app names "Task", and a text field with none,
    ///   both empty.
    ///
    /// Steps:
    /// - Read the placeholders of both edits.
    ///
    /// Expected:
    /// - "What needs doing?" for the first, none for the second.
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task EditPlaceholder_IsTheFieldsPlaceholder()
    {
        Element hinted = default;
        Element plain = default;
        await using HeadlessApp app = await HeadlessApp.StartAsync(() =>
            Column(
                hinted = TextField(Signal.Create(string.Empty), new TextFieldOptions { Placeholder = "What needs doing?" })
                    .Accessible(new AccessibilityInfo { Name = "Task" }),
                plain = TextField(Signal.Create(string.Empty))
            )
        );
        _ = app.PumpAndSettle();
        AccessibilityTree tree = app.AccessibilityTree();

        Assert.Equal(("What needs doing?", (string?)null), (tree.Find(hinted.Node)!.Placeholder, tree.Find(plain.Node)!.Placeholder));
    }

    /// <summary>
    /// A node that takes the focus and has no role of its own is a group, so that the focus is never on a node outside the tree; it
    /// leaves the tree when it stops taking the focus, and comes back when it takes it again.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A column of a focusable box holding a text "inside", and a box holding a text "plain" that is not focusable; the tree made.
    ///
    /// Steps:
    /// - Focus the focusable box; settle; read the root's children, the group's children, and the group's states. Make the box not
    ///   focusable; settle; read the root's children. Make it focusable again; settle; read the root's children.
    ///
    /// Expected:
    /// - A group and the text "plain" under the root; the text "inside" under the group, which is focused and focusable. Then the
    ///   texts "inside" and "plain" under the root. Then a group and "plain" again.
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task FocusableNodeWithNoRole_IsAGroup_WhileItTakesTheFocus()
    {
        Element area = default;
        await using HeadlessApp app = await HeadlessApp.StartAsync(() =>
            Column(area = Box(Elements.Text("inside")).Focusable(), Box(Elements.Text("plain")))
        );
        _ = app.PumpAndSettle();
        AccessibilityNode root = app.AccessibilityTree().Root!;

        Assert.True(area.Tree!.Focus.Focus(area.Node));
        _ = app.PumpAndSettle();
        Assert.Equal([SemanticsRole.Group, SemanticsRole.Text], root.Children.Select(c => c.Role));
        AccessibilityNode group = root.Children[0];
        Assert.Equal(["inside"], group.Children.Select(c => c.Name));
        Assert.Equal(
            AccessibilityStates.Focused | AccessibilityStates.Focusable,
            group.States & (AccessibilityStates.Focused | AccessibilityStates.Focusable)
        );

        area.Tree.SetFocusable(area.Node, false);
        _ = app.PumpAndSettle();
        Assert.Equal(["inside", "plain"], root.Children.Select(c => c.Name));

        area.Tree.SetFocusable(area.Node, true);
        _ = app.PumpAndSettle();
        Assert.Equal([SemanticsRole.Group, SemanticsRole.Text], root.Children.Select(c => c.Role));
    }

    /// <summary>
    /// A window whose tree nobody made keeps no marks and makes no node: a style change, a layout move, a pseudo-state, an app's
    /// information, a live setting, a relation, an active descendant, a handler of an action, and a draw function of nodes mark
    /// nothing, before the tree's object is made and after it is made but not started, and the draw function is not called.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A column of a button "first" 30 high, a button "second", a text "label", and a box of the role Figure; the accessibility tree
    ///   not made.
    ///
    /// Steps:
    /// - Make "first" 60 high, disable "second", give "second" a name, make "second" a Status with the live setting Assertive, make
    ///   "label" name it and describe it, set its active descendant's signal to "label" and then to "first", add a handler of Increment
    ///   to it in a scope, and give the figure a draw function that adds a node and counts its calls; settle; read whether the tree's
    ///   object is made. Read the tree's object, which makes it; make "first" 90 high, enable "second", name it "Two" with the live
    ///   setting Off, make "label" its error message, set the signal to "label", dispose the scope of the handler, add a handler of
    ///   Expand in another, and give the figure the function again; settle; read the marks, the root, and the function's calls.
    ///
    /// Expected:
    /// - The frames made no tree object; then no marks, no root, and no call of the draw function.
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task Marks_AreNone_WhileTheTreeIsNotMade()
    {
        Element first = default;
        Element second = default;
        Element label = default;
        Element figure = default;
        int drawn = 0;
        var active = Signal.Create<Element>(default);
        await using HeadlessApp app = await HeadlessApp.StartAsync(() =>
            Column(
                first = Button("first", () => { }).Height(30),
                second = Button("second", () => { }),
                label = Elements.Text("label"),
                figure = Box().Height(20).Accessible(new AccessibilityInfo { Role = SemanticsRole.Figure })
            )
        );
        _ = app.PumpAndSettle();
        void Draw(AccessibleChildren children, Rect bounds)
        {
            drawn++;
            children.Add(1, bounds, new AccessibilityInfo { Role = SemanticsRole.Image, Name = "point" });
        }

        app.Tree.SetStyle(first.Node, app.Tree.Nodes.GetRef(first.Node).Layout.Style with { Height = Length.Px(60) });
        app.Tree.SetPseudoState(second.Node, PseudoState.Disabled, true);
        app.Tree.SetAccessibility(
            second.Node,
            new AccessibilityInfo
            {
                Name = "Second",
                Role = SemanticsRole.Status,
                Live = SemanticsLive.Assertive,
            }
        );
        _ = second.LabelledBy(label).DescribedBy(label).ActiveDescendant(active);
        active.Set(label);
        active.Set(first);
        ScopeHandle handlers = Scope.Create(() => second.OnIncrement(() => { }));
        _ = figure.AccessibleChildren(Draw);
        _ = app.PumpAndSettle();

        bool made = app.Tree.HasAccessibilityTree;
        AccessibilityTree tree = app.Tree.Accessibility;
        app.Tree.SetStyle(first.Node, app.Tree.Nodes.GetRef(first.Node).Layout.Style with { Height = Length.Px(90) });
        app.Tree.SetPseudoState(second.Node, PseudoState.Disabled, false);
        app.Tree.SetAccessibility(second.Node, new AccessibilityInfo { Name = "Two", Live = SemanticsLive.Off });
        _ = second.ErrorMessage(label);
        active.Set(label);
        Scope.Dispose(handlers);
        ScopeHandle others = Scope.Create(() => second.OnExpand(() => { }));
        _ = figure.AccessibleChildren(Draw);
        _ = app.PumpAndSettle();

        Assert.False(made);
        Assert.Equal(0, tree.MarkedCount);
        Assert.Null(tree.Root);
        Assert.Equal(0, drawn);
        Scope.Dispose(others);
    }

    /// <summary>
    /// A change of the look alone, such as a hover's color, marks nothing in a window whose tree is made.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A column of two buttons; the tree made.
    ///
    /// Steps:
    /// - Invalidate the first button's color, and mark it dirty for the paint alone; read the marks.
    ///
    /// Expected:
    /// - No marks.
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task LookAlone_MarksNothing()
    {
        Element first = default;
        await using HeadlessApp app = await HeadlessApp.StartAsync(() => Column(first = Button("first", () => { }), Button("second", () => { })));
        _ = app.PumpAndSettle();
        _ = app.AccessibilityTree();

        Invalidation.Invalidate(app.Tree, first.Node, ChangeKind.Color);
        Invalidation.MarkDirty(app.Tree, first.Node, DirtyFlags.Paint);
        int marks = app.Tree.Accessibility.MarkedCount;
        _ = app.PumpAndSettle();

        Assert.Equal(0, marks);
    }

    /// <summary>
    /// A change of a node's own state makes that node again alone, not its parent's, which looks over all its children.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A column of three buttons, none focused; the tree made.
    ///
    /// Steps:
    /// - Focus the second; settle; read how many nodes the update made again, and its state.
    ///
    /// Expected:
    /// - One node; the second button focused.
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task OwnState_MakesAgainTheNodeAlone()
    {
        Element second = default;
        await using HeadlessApp app = await HeadlessApp.StartAsync(() =>
            Column(Button("first", () => { }), second = Button("second", () => { }), Button("third", () => { }))
        );
        _ = app.PumpAndSettle();
        _ = app.AccessibilityTree();

        Assert.True(app.Focus(second));
        _ = app.PumpAndSettle();

        Assert.Equal((1, true), (app.Tree.Accessibility.LastRefreshed, app.AccessibilityOf(second).IsFocused));
    }

    /// <summary>
    /// A name a role folds stops at a node that is a node of its own below it, also one whose role is given by its kind: its text is
    /// that node's.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A list item holding a text "Row" and a scroll view 20 high of a text "Inner".
    ///
    /// Steps:
    /// - Read the list item's name and the texts of the tree.
    ///
    /// Expected:
    /// - "Row"; the text "Inner" alone.
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task Folding_StopsAtAScrollViewBelow()
    {
        Element item = default;
        await using HeadlessApp app = await HeadlessApp.StartAsync(() =>
            Column(
                item = Column(Elements.Text("Row"), ScrollView(Elements.Text("Inner")).Height(20))
                    .Accessible(new AccessibilityInfo { Role = SemanticsRole.ListItem })
            )
        );
        _ = app.PumpAndSettle();

        Assert.Equal("Row", app.AccessibilityOf(item).Name);
        Assert.Equal(["Inner"], app.FindByRole(SemanticsRole.Text).Select(t => app.AccessibilityOf(t).Name));
    }

    /// <summary>
    /// A virtualized list's node has the number of items, its rows their places from 0, and the tree holds the rows the list lays
    /// out, not all the items.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A box 200 x 300 that scrolls down, a virtualized list of 1,000 items, each row 20 high; the tree made.
    ///
    /// Steps:
    /// - Read the list's node and its rows; scroll to 4,000; settle; read the rows again.
    ///
    /// Expected:
    /// - The list's count is 1,000; it holds fewer than 100 rows, each row's place is its item; after the scroll the first row's
    ///   place is at least 180, and still each row's place is its item.
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task VirtualList_GivesItsCount_AndItsRowsTheirPlaces()
    {
        Signal<IReadOnlyList<int>> items = Signal.Create<IReadOnlyList<int>>([.. Enumerable.Range(0, 1_000)]);
        var state = new VirtualListState();
        Element list = default;
        await using HeadlessApp app = await HeadlessApp.StartAsync(() =>
            list = Box()
                .Width(200)
                .Height(300)
                .Overflow(Overflow.Visible, Overflow.Scroll)
                .VirtualFor(items, static i => i, static item => Box().Height(20).TestId($"row{item.Get()}"), state)
        );
        _ = app.PumpAndSettle();
        _ = app.AccessibilityTree();
        AccessibilityNode node = app.Tree.Accessibility.Find(list.Node)!;
        (int Count, int Rows, bool Placed) before = Read(node);

        state.Scroll.ScrollTo(new Vector2(0, 4_000));
        _ = app.PumpAndSettle();
        (int Count, int Rows, bool Placed) after = Read(node);

        Assert.Equal((1_000, true), (before.Count, before.Placed));
        Assert.InRange(before.Rows, 1, 99);
        Assert.True(after.Placed);
        Assert.InRange(node.Children[0].PositionInSet, 180, 1_000);

        (int, int, bool) Read(AccessibilityNode box) =>
            (box.SizeOfSet, box.Children.Count, box.Children.All(row => app.Tree.TestIdOf(row.Node) == $"row{row.PositionInSet}"));
    }

    /// <summary>
    /// A row a virtualized list keeps outside its range stays in the tree while it holds the focus, and leaves it when the focus
    /// leaves; a row kept alive for another reason is not in the tree.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A box 200 x 300 that scrolls down, a virtualized list of 1,000 focusable rows 20 high; row 50 is kept alive by its signal;
    ///   the tree made; row 3 focused.
    ///
    /// Steps:
    /// - Scroll to 1,000, where the list makes row 50; settle. Scroll to 10,000; settle; read the list's rows. Take the focus from
    ///   row 3; settle; read them again.
    ///
    /// Expected:
    /// - Row 3 is among the rows, row 50 is not; then row 3 is not.
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task KeptRow_StaysWhileItHoldsTheFocus()
    {
        Signal<IReadOnlyList<int>> items = Signal.Create<IReadOnlyList<int>>([.. Enumerable.Range(0, 1_000)]);
        var state = new VirtualListState();
        Element list = default;
        IReadOnlySignal<bool> keep = Signal.Create(true);
        await using HeadlessApp app = await HeadlessApp.StartAsync(() =>
            list = Box()
                .Width(200)
                .Height(300)
                .Overflow(Overflow.Visible, Overflow.Scroll)
                .VirtualFor(
                    items,
                    static i => i,
                    item =>
                    {
                        Element row = Box().Height(20).Focusable().TestId($"row{item.Get()}");
                        return item.Get() == 50 ? row.KeepAlive(keep) : row;
                    },
                    state
                )
        );
        _ = app.PumpAndSettle();
        _ = app.AccessibilityTree();
        Assert.True(app.Tree.Focus.Focus(app.Find("row3").Node));
        _ = app.PumpAndSettle();

        state.Scroll.ScrollTo(new Vector2(0, 1_000));
        _ = app.PumpAndSettle();
        Assert.True(app.Tree.Accessibility.Find(app.Find("row50").Node) is not null);
        state.Scroll.ScrollTo(new Vector2(0, 10_000));
        _ = app.PumpAndSettle();
        string[] kept = Ids();
        app.Tree.Focus.Lose(app.Find("row3").Node);
        _ = app.PumpAndSettle();
        string[] left = Ids();

        Assert.Contains("row3", kept);
        Assert.DoesNotContain("row50", kept);
        Assert.DoesNotContain("row3", left);

        string[] Ids() => [.. app.Tree.Accessibility.Find(list.Node)!.Children.Select(row => app.Tree.TestIdOf(row.Node)!)];
    }

    /// <summary>
    /// The link ranges of a text leaf are Hyperlink nodes after its children, named by their text and boxed by their caret
    /// positions; a link in a button's text, which the button folds into its name, is not a node.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A column of a text "Read the docs now" whose "the docs" is a link, and a button whose text "Go here" has "here" as a link.
    ///
    /// Steps:
    /// - Make the tree; read the text's node and the button's.
    ///
    /// Expected:
    /// - The text's node has one link node, a Hyperlink named "the docs", wider than 0 and to the right of the text's start; the
    ///   button's node has no link node, and its name is "Go here".
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task Links_AreNodesOfTheirText_ButNotInAButton()
    {
        PointerHandler click = (in PointerEvent e) => { };
        Element text = default;
        Element button = default;
        await using HeadlessApp app = await HeadlessApp.StartAsync(() =>
        {
            Element goHere = Elements.Text(
                (ref w) =>
                {
                    w.Write("Go ".AsSpan());
                    using (w.Link(click))
                    {
                        w.Write("here".AsSpan());
                    }
                }
            );
            text = Elements.Text(
                (ref w) =>
                {
                    w.Write("Read ".AsSpan());
                    using (w.Link(click))
                    {
                        w.Write("the docs".AsSpan());
                    }

                    w.Write(" now".AsSpan());
                }
            );
            return Column(text, button = Button(goHere, () => { }));
        });
        _ = app.PumpAndSettle();
        _ = app.AccessibilityTree();
        AccessibilityNode textNode = app.Tree.Accessibility.Find(text.Node)!;
        AccessibilityNode buttonNode = app.Tree.Accessibility.Find(button.Node)!;
        AccessibilityNode link = Assert.Single(textNode.Links);

        Assert.Equal((SemanticsRole.Hyperlink, "the docs"), (link.Role, link.Name));
        Assert.True(link.Bounds.Width > 0 && link.Bounds.X > 0);
        Assert.Equal((0, "Go here"), (buttonNode.Links.Count + buttonNode.Children.Sum(c => c.Links.Count), buttonNode.Name));
    }

    /// <summary>
    /// A node with no focus is scrolled into view when asked, as assistive technology's ScrollIntoView asks.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A scroll view 100 high holding a box 300 high and a box "far" 20 high below it; nothing focused.
    ///
    /// Steps:
    /// - Ask to bring "far" into view; settle; read the scroll position.
    ///
    /// Expected:
    /// - The view scrolled down by 220, the least that shows "far".
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task BringIntoView_ScrollsANodeWithNoFocus()
    {
        var state = new ScrollState();
        Element far = default;
        await using HeadlessApp app = await HeadlessApp.StartAsync(() =>
            ScrollView(Column(Box().Height(300), far = Box().Height(20)), state).Height(100)
        );
        _ = app.PumpAndSettle();

        app.Tree.Focus.BringIntoView(far.Node);
        _ = app.PumpAndSettle();

        Assert.Equal(220, state.Offset.Get().Y);
    }

    /// <summary>
    /// A change of a virtualized list's number of items alone, items past its range, hands the list's node again with the new
    /// count.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A box 200 x 300 that scrolls down, a virtualized list of 1,000 rows 20 high; a bridge that records what it is handed; the
    ///   tree made.
    ///
    /// Steps:
    /// - Add 10 items at the end; settle; read the record and the list's count.
    ///
    /// Expected:
    /// - The list's node is in the record, and its count is 1,010.
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task VirtualList_CountChange_HandsTheListAgain()
    {
        Signal<IReadOnlyList<int>> items = Signal.Create<IReadOnlyList<int>>([.. Enumerable.Range(0, 1_000)]);
        Element list = default;
        await using HeadlessApp app = await HeadlessApp.StartAsync(() =>
            list = Box()
                .Width(200)
                .Height(300)
                .Overflow(Overflow.Visible, Overflow.Scroll)
                .VirtualFor(items, static i => i, static item => Box().Height(20))
        );
        _ = app.PumpAndSettle();
        var bridge = new Recorder();
        app.Tree.Accessibility.Bridge = bridge;
        _ = app.AccessibilityTree();
        bridge.Changes.Clear();

        items.Set([.. Enumerable.Range(0, 1_010)]);
        _ = app.PumpAndSettle();
        AccessibilityNode node = app.Tree.Accessibility.Find(list.Node)!;

        Assert.Contains(bridge.Changes, c => c.Id == node.Id);
        Assert.Equal(1_010, node.SizeOfSet);
    }

    /// <summary>
    /// A node is clickable to assistive technology when it has a click handler of its own in the bubble phase; a handler of another
    /// kind, or a click handler in the capture phase, does not make it so, and a node whose click handler is removed stops being so.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A column of three nodes of the role Group: "click" with a click handler in the bubble phase, "down" with a down handler in
    ///   the bubble phase, and "tunnel" with a click handler in the capture phase.
    ///
    /// Steps:
    /// - Make the tree; read whether each node is clickable. Remove the click handler of "click"; settle; read whether it is clickable.
    ///
    /// Expected:
    /// - "click" only; then not "click".
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task Clickable_IsABubbleClickHandlerOfItsOwn()
    {
        PointerHandler none = (in PointerEvent e) => { };
        Element click = default;
        Element down = default;
        Element tunnel = default;
        await using HeadlessApp app = await HeadlessApp.StartAsync(() =>
            Column(
                click = Box().Height(10).On(PointerEventKind.Click, EventPhase.Bubble, none).Accessible(Group("click")),
                down = Box().Height(10).On(PointerEventKind.Down, EventPhase.Bubble, none).Accessible(Group("down")),
                tunnel = Box().Height(10).On(PointerEventKind.Click, EventPhase.Capture, none).Accessible(Group("tunnel"))
            )
        );
        _ = app.PumpAndSettle();
        _ = app.AccessibilityTree();
        bool Clickable(Element e) => (app.Tree.Accessibility.Find(e.Node)!.States & AccessibilityStates.Clickable) != 0;

        Assert.Equal((true, false, false), (Clickable(click), Clickable(down), Clickable(tunnel)));

        Assert.True(app.Tree.RemovePointerHandler(click.Node, PointerEventKind.Click, EventPhase.Bubble, none));
        _ = app.PumpAndSettle();
        Assert.False(Clickable(click));

        static AccessibilityInfo Group(string name) => new() { Role = SemanticsRole.Group, Name = name };
    }

    /// <summary>
    /// A change of a link's text after the tree is made renames its node, and a link added later makes a node.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A text whose link's text is a signal "one", and a second link that a signal shows when true, false at first; the tree made.
    ///
    /// Steps:
    /// - Set the link's text to "two" and show the second link "more"; settle; read the text's link nodes.
    ///
    /// Expected:
    /// - Two link nodes, "two" and "more".
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task LinkNodes_FollowTheText()
    {
        PointerHandler click = (in PointerEvent e) => { };
        Signal<string> label = Signal.Create("one");
        Signal<bool> more = Signal.Create(false);
        Element text = default;
        await using HeadlessApp app = await HeadlessApp.StartAsync(() =>
            text = Elements.Text(
                (ref w) =>
                {
                    w.Write("See ".AsSpan());
                    using (w.Link(click))
                    {
                        w.Write(label.Get().AsSpan());
                    }

                    if (more.Get())
                    {
                        w.Write(" and ".AsSpan());
                        using (w.Link(click))
                        {
                            w.Write("more".AsSpan());
                        }
                    }
                }
            )
        );
        _ = app.PumpAndSettle();
        _ = app.AccessibilityTree();

        label.Set("two");
        more.Set(true);
        _ = app.PumpAndSettle();

        Assert.Equal(["two", "more"], app.Tree.Accessibility.Find(text.Node)!.Links.Select(l => l.Name));
    }

    /// <summary>
    /// A node the app excludes is left out of the tree with its subtree, and gives no text to the name of the button it is in
    /// (design §4.4.1, "Excluded nodes").
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A column of a button whose content is a row of an excluded box holding the text "x" and the text "Save", and an excluded
    ///   box holding the text "y" and a button "z".
    ///
    /// Steps:
    /// - Read the tree.
    ///
    /// Expected:
    /// - One button, named "Save"; no text node; no button "z"; nothing found by the role Text.
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task Excluded_LeavesTheSubtreeOutOfTheTreeAndOfTheName()
    {
        var excluded = new AccessibilityInfo { Excluded = true };
        await using HeadlessApp app = await HeadlessApp.StartAsync(() =>
            Column(
                Button(Row(Box(Elements.Text("x")).Accessible(excluded), Elements.Text("Save")), () => { }),
                Box(Column(Elements.Text("y"), Button("z", () => { }))).Accessible(excluded)
            )
        );
        _ = app.PumpAndSettle();

        Assert.Equal(["Save"], app.FindByRole(SemanticsRole.Button).Select(b => app.AccessibilityOf(b).Name));
        Assert.Empty(app.FindByRole(SemanticsRole.Text));
        Assert.Equal([SemanticsRole.Button], app.AccessibilityTree().Root!.Children.Select(c => c.Role));
    }

    /// <summary>
    /// A focus inside an excluded node is a fault a Debug build reports; a Release build gives the focus to the nearest ancestor that
    /// has a node, so that it is never on no node (design §4.4.1, "Excluded nodes").
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A group (a focusable box) holding an excluded box that holds a focusable box.
    ///
    /// Steps:
    /// - Focus the inner box; ask the tree for the node that holds the focus.
    ///
    /// Expected:
    /// - Debug: an invariant failure. Release: the group's node.
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task Excluded_FocusInside_IsReportedInDebugAndGoesToTheAncestorInRelease()
    {
        Element? group = null;
        Element? inner = null;
        await using HeadlessApp app = await HeadlessApp.StartAsync(() =>
        {
            Element focusable = Box().Width(10).Height(10).Focusable();
            inner = focusable;
            Element outer = Box(Box(focusable).Accessible(new AccessibilityInfo { Excluded = true })).Focusable();
            group = outer;
            return outer;
        });
        _ = app.PumpAndSettle();
        AccessibilityTree tree = app.AccessibilityTree();
        Assert.True(app.Focus(inner!.Value));
        _ = app.PumpAndSettle();

#if DEBUG
        Exception failed = Assert.ThrowsAny<Exception>(() => tree.FocusNode());
        Assert.Equal("InvariantViolationException", failed.GetType().Name);
#else
        Assert.Same(tree.Find(group!.Value.Node), tree.FocusNode());
#endif
    }

    // Records each change the tree hands it.
    private sealed class Recorder : IAccessibilityBridge
    {
        public List<AccessibilityChange> Changes { get; } = [];

        public void NodesChanged(ReadOnlySpan<AccessibilityChange> changes) => Changes.AddRange(changes);
    }
}
