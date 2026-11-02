using System.Numerics;
using XylemUI.Primitives;
using XylemUI.Testing.XUnit;
using static XylemUI.Elements;
using static XylemUI.Primitives.Primitive;

namespace XylemUI.Desktop.Tests;

// The accessibility tree (row P02g, design §4.4.1, "The shape of P02g") in a headless app: its shape, the names and states of its
// nodes, the parts' roles, and how an update makes again only what changed.
public sealed class AccessibilityTests
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
    /// A frame of a window whose accessibility tree is made, delivered and drawn, allocates nothing when it changes no name: the
    /// pointer moving over two buttons changes their hover, which marks them, and their nodes are made again with the same values.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - Two buttons with a hovered look; the tree made; warmed up with the iterations.
    ///
    /// Steps:
    /// - 1,000 iterations in the windows of <c>PerformanceAssert.DoesNotAllocate</c>: move the pointer over the first button or the
    ///   second by the iteration's number, and settle.
    ///
    /// Expected:
    /// - 0 bytes in every window.
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task SteadyState_HoverWithTheTree_AllocatesNothing()
    {
        var look = new PaintStyle { Background = new Paint(Color.FromRgb(0x336699)) };
        Element first = default;
        Element other = default;
        await using HeadlessApp app = await HeadlessApp.StartAsync(() =>
            Column(
                first = Button("first", () => { }).Width(100).Height(30).Hovered(look),
                other = Button("second", () => { }).Width(100).Height(30).Hovered(look)
            )
        );
        _ = app.PumpAndSettle();
        _ = app.AccessibilityTree();
        (int x0, int y0) = app.CenterOf(first);
        (int x1, int y1) = app.CenterOf(other);
        void Step(int index)
        {
            if (index % 2 == 0)
            {
                app.Pointer.Move(x0, y0);
            }
            else
            {
                app.Pointer.Move(x1, y1);
            }

            _ = app.PumpAndSettle();
        }

        for (int i = 0; i < 20; i++)
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
    /// A window whose tree nobody made keeps no marks: a style change, a layout move, a pseudo-state, and an app's information mark
    /// nothing.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A column of a button "first" 30 high and a button "second"; the accessibility tree not made.
    ///
    /// Steps:
    /// - Make "first" 60 high, disable "second", and give "second" a name; settle; read the marks and the tree's root.
    ///
    /// Expected:
    /// - No marks, and no root.
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task Marks_AreNone_WhileTheTreeIsNotMade()
    {
        Element first = default;
        Element second = default;
        await using HeadlessApp app = await HeadlessApp.StartAsync(() =>
            Column(first = Button("first", () => { }).Height(30), second = Button("second", () => { }))
        );
        _ = app.PumpAndSettle();

        app.Tree.SetStyle(first.Node, app.Tree.Nodes.GetRef(first.Node).Layout.Style with { Height = Length.Px(60) });
        app.Tree.SetPseudoState(second.Node, PseudoState.Disabled, true);
        app.Tree.SetAccessibility(second.Node, new AccessibilityInfo { Name = "Second" });
        _ = app.PumpAndSettle();

        Assert.Equal(0, app.Tree.Accessibility.MarkedCount);
        Assert.Null(app.Tree.Accessibility.Root);
    }

    // Records each change the tree hands it.
    private sealed class Recorder : IAccessibilityBridge
    {
        public List<AccessibilityChange> Changes { get; } = [];

        public void NodesChanged(ReadOnlySpan<AccessibilityChange> changes) => Changes.AddRange(changes);

        public void NotifyFocusChange(AccessibilityNodeId node) { }
    }
}
