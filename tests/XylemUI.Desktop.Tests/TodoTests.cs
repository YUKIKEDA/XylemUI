using Todo;
using XylemUI.Testing.XUnit;
using XylemUI.Text;

namespace XylemUI.Desktop.Tests;

// The todo sample (row V3c, design §2.7, "How it is checked"): MoonBit's V3 and the parts row V3c adds, and the field that names
// an item (row P02b, design §4.9, "Todo"), in a headless app (design §12, row P02a) with no OS window. The pixels of the scrolled list and its golden are in HeadlessPixelTests (row P02a2).
public sealed class TodoTests
{
    private static readonly HeadlessOptions Headless = new() { Width = 420, Height = 360 };

    /// <summary>
    /// Enter in the field and the Add button each add an item of the text typed, without its leading and trailing spaces, and empty
    /// the field; a text of spaces alone adds nothing; the button leaves the focus in the field.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - The todo app, headless, with no item; the field clicked, so that it has the focus.
    ///
    /// Steps:
    /// - Type "  Buy milk  ", let the frames shape it, and press Enter; type "Call Bob" and click Add; type "   " and press Enter, then click Add.
    ///
    /// Expected:
    /// - After Enter: the rows "[ ] Buy milk", the field empty, and its text leaf keeps no shaped text (it drew "  Buy milk  "
    ///   when the shaped text of the last text stood for the empty one). After the click: "[ ] Buy milk" and "[ ] Call Bob", the field empty
    ///   and still focused. After the spaces: the same two rows, and the field keeps "   ".
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task Field_EnterOrAdd_AddsTheTrimmedName_AndEmptiesTheField()
    {
        var model = new TodoModel();
        await using HeadlessApp headless = await HeadlessApp.StartAsync(() => TodoApp.Todo(model), Headless);
        var app = App.Of(headless, model);
        app.Click(app.Field);

        headless.Keyboard.Type("  Buy milk  ");
        app.Settle();
        _ = headless.Keyboard.Press(Key.Enter);
        app.Settle();
        (List<string> Rows, string Field) entered = (app.Labels(), model.Draft.Get());
        bool drawsTheOldName =
            app.Tree.Nodes.GetRef(app.Tree.Nodes.GetRef(app.Tree.Nodes.GetRef(app.Field).FirstChild).FirstChild).Text!.Shaped!.Result is not null;
        headless.Keyboard.Type("Call Bob");
        app.Click(app.Add);
        (List<string> Rows, string Field, bool Focused) clicked = (
            app.Labels(),
            model.Draft.Get(),
            (app.Tree.Nodes.GetRef(app.Field).Pseudo & PseudoState.Focused) != 0
        );
        headless.Keyboard.Type("   ");
        _ = headless.Keyboard.Press(Key.Enter);
        app.Click(app.Add);

        Assert.Equal(["[ ] Buy milk"], entered.Rows);
        Assert.Equal(string.Empty, entered.Field);
        Assert.False(drawsTheOldName);
        Assert.Equal(["[ ] Buy milk", "[ ] Call Bob"], clicked.Rows);
        Assert.Equal((string.Empty, true), (clicked.Field, clicked.Focused));
        Assert.Equal(["[ ] Buy milk", "[ ] Call Bob"], app.Labels());
        Assert.Equal("   ", model.Draft.Get());
    }

    /// <summary>
    /// The list is a selection area (row P02b2): a click on the heading's text gives it the focus, Control+A selects the text of
    /// the heading, the buttons, and the items, and Control+C copies it a leaf to a line; the field's text is left out.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - The todo app, headless, with the items "Buy milk" and "Call Bob" added by the model, and "draft" typed in the field.
    ///
    /// Steps:
    /// - Click on the heading's "todos: 2"; Control+A; Control+C.
    ///
    /// Expected:
    /// - The clipboard holds the heading's two texts, the buttons' labels, and each item's label and its remove button's "×", one
    ///   to a line, in the order of the tree; not "draft".
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task List_SelectAllAndCopy_TakesTheTextButTheField()
    {
        var model = new TodoModel();
        await using HeadlessApp headless = await HeadlessApp.StartAsync(() => TodoApp.Todo(model), Headless);
        var app = App.Of(headless, model);
        _ = model.Add("Buy milk");
        _ = model.Add("Call Bob");
        model.Draft.Set("draft");
        app.Settle();

        headless.Pointer.Click(headless.Find("todos: 2"));
        _ = headless.Keyboard.Press(Key.A, KeyModifiers.Control);
        _ = headless.Keyboard.Press(Key.C, KeyModifiers.Control);
        app.Settle();

        Assert.Equal("todos: 2\nleft: 2\nAdd\nAll\nActive\nDone\nOldest first\n[ ] Buy milk\n×\n[ ] Call Bob\n×", headless.Clipboard.Text);
    }

    /// <summary>
    /// Clicking an item marks it done, with a mark and a gray text, and again marks it not done; "left" follows.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - The todo app, headless, with three items added by the model.
    ///
    /// Steps:
    /// - Click the second item's button; then click it again.
    ///
    /// Expected:
    /// - After the first click: its label is "[x] Item 2", its text color <c>TodoApp.DoneColor</c>, and the heading "left: 2"; the
    ///   others keep "[ ]". After the second: "[ ] Item 2", <c>TodoApp.TextColor</c>, and "left: 3".
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task ClickOnAnItem_MarksItDone_AndAgainNotDone()
    {
        var model = new TodoModel();
        await using HeadlessApp headless = await HeadlessApp.StartAsync(() => TodoApp.Todo(model), Headless);
        var app = App.Of(headless, model);
        AddThree(app);

        app.Click(app.ItemButton(1));

        Assert.Equal(["[ ] Item 1", "[x] Item 2", "[ ] Item 3"], app.Labels());
        Assert.Equal(TodoApp.DoneColor, app.Tree.Nodes.GetRef(app.ItemButton(1)).Visual!.Foreground);
        Assert.Equal("left: 2", app.Heading().Left);

        app.Click(app.ItemButton(1));

        Assert.Equal(["[ ] Item 1", "[ ] Item 2", "[ ] Item 3"], app.Labels());
        Assert.Equal(TodoApp.TextColor, app.Tree.Nodes.GetRef(app.ItemButton(1)).Visual!.Foreground);
        Assert.Equal("left: 3", app.Heading().Left);
    }

    /// <summary>
    /// The remove button of an item opens a dialog that asks about it (row P02d): Cancel and Esc keep the item, and Remove removes it,
    /// and only it.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - The todo app, headless, with three items added by the model.
    ///
    /// Steps:
    /// - Click the second item's remove button; read the popups, the question, and the focus; click Cancel. Click the remove button
    ///   again and press Esc. Click it again and click Remove.
    ///
    /// Expected:
    /// - The dialog is the one popup, asks "Remove "Item 2"?", and its Remove button has the focus. After Cancel and after Esc: no
    ///   popup and the three items. After Remove: no popup, the rows are Item 1 and Item 3, and the heading reads "todos: 2" and
    ///   "left: 2".
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task RemoveButton_AsksInADialog_ThenRemovesOrKeepsItsItem()
    {
        var model = new TodoModel();
        await using HeadlessApp headless = await HeadlessApp.StartAsync(() => TodoApp.Todo(model), Headless);
        var app = App.Of(headless, model);
        AddThree(app);
        NodeHandle remove = app.Tree.Nodes.GetRef(app.ItemButton(1)).NextSibling;

        app.Click(remove);
        int asked = headless.Popups.Count;
        NodeHandle question = headless.Find("Remove \"Item 2\"?").Node;
        NodeHandle confirm = app.Tree.Nodes.GetRef(headless.Find("Remove").Node).Parent;
        bool confirmFocused = app.Tree.Focus.Focused == confirm;
        app.Click(headless.Find("Cancel").Node);
        var kept = new List<(int Popups, int Items)> { (headless.Popups.Count, app.Labels().Count) };
        app.Click(remove);
        _ = headless.Keyboard.Press(Key.Escape);
        app.Settle();
        kept.Add((headless.Popups.Count, app.Labels().Count));
        app.Click(remove);
        app.Click(headless.Find("Remove").Node);

        Assert.Equal(1, asked);
        Assert.False(question.IsNone);
        Assert.True(confirmFocused);
        Assert.Equal([(0, 3), (0, 3)], kept);
        Assert.Empty(headless.Popups);
        Assert.Equal(["[ ] Item 1", "[ ] Item 3"], app.Labels());
        Assert.Equal(("todos: 2", "left: 2"), app.Heading());
    }

    /// <summary>
    /// The field's context menu (row P02d): the right button opens it, the paste is grayed out with an empty clipboard, and its
    /// select-all selects the field's text, which Control+C then copies.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - The todo app, headless, with "buy" typed in the field, and an empty clipboard.
    ///
    /// Steps:
    /// - Click the right button in the field; read the popups and whether the paste's item can take the focus; click Select all;
    ///   press Control+C.
    ///
    /// Expected:
    /// - One popup; the paste cannot take the focus. After the select-all, no popup and the field focused; the clipboard holds
    ///   "buy".
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task FieldContextMenu_SelectAllThenCopy()
    {
        var model = new TodoModel();
        await using HeadlessApp headless = await HeadlessApp.StartAsync(() => TodoApp.Todo(model), Headless);
        var app = App.Of(headless, model);
        app.Click(app.Field);
        _ = headless.Keyboard.Type("buy");
        app.Settle();

        headless.Pointer.Click(new Element(app.Tree, app.Field), PointerButton.Right);
        app.Settle();
        int popups = headless.Popups.Count;
        bool pasteTakesFocus = app.Tree.Focus.CanFocus(app.Tree.Nodes.GetRef(headless.Find("Paste").Node).Parent);
        app.Click(headless.Find("Select all").Node);
        bool fieldFocused = app.Tree.Focus.Focused == app.Field;
        _ = headless.Keyboard.Press(Key.C, KeyModifiers.Control);

        Assert.Equal(1, popups);
        Assert.False(pasteTakesFocus);
        Assert.Empty(headless.Popups);
        Assert.True(fieldFocused);
        Assert.Equal("buy", headless.Clipboard.Text);
    }

    /// <summary>
    /// The three filters show every item, the items not done, and the items done, and the chosen filter's button is darker.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - The todo app, headless, with three items added by the model, the second marked done by a click.
    ///
    /// Steps:
    /// - Click Active, then Done, then All.
    ///
    /// Expected:
    /// - Active shows Items 1 and 3, Done shows Item 2, and All shows the three. After each click the chosen button's background
    ///   is <c>TodoApp.ChosenBackground</c> and the other two are <c>TodoApp.ButtonBackground</c>.
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task Filters_ShowTheItemsTheyLetThrough()
    {
        var model = new TodoModel();
        await using HeadlessApp headless = await HeadlessApp.StartAsync(() => TodoApp.Todo(model), Headless);
        var app = App.Of(headless, model);
        AddThree(app);
        app.Click(app.ItemButton(1));

        app.Click(app.Filter(1));
        Assert.Equal(["[ ] Item 1", "[ ] Item 3"], app.Labels());
        Assert.Equal([false, true, false], app.FiltersChosen());

        app.Click(app.Filter(2));
        Assert.Equal(["[x] Item 2"], app.Labels());
        Assert.Equal([false, false, true], app.FiltersChosen());

        app.Click(app.Filter(0));
        Assert.Equal(["[ ] Item 1", "[x] Item 2", "[ ] Item 3"], app.Labels());
        Assert.Equal([true, false, false], app.FiltersChosen());
    }

    /// <summary>
    /// The order button puts the newest item first, and again the oldest first.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - The todo app, headless, with three items added by the model.
    ///
    /// Steps:
    /// - Click the order button; then click it again.
    ///
    /// Expected:
    /// - After the first click the rows are Items 3, 2, 1 and the button reads "Newest first"; after the second, Items 1, 2, 3 and
    ///   "Oldest first".
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task OrderButton_ReversesTheList()
    {
        var model = new TodoModel();
        await using HeadlessApp headless = await HeadlessApp.StartAsync(() => TodoApp.Todo(model), Headless);
        var app = App.Of(headless, model);
        AddThree(app);

        app.Click(app.Order);
        Assert.Equal(["[ ] Item 3", "[ ] Item 2", "[ ] Item 1"], app.Labels());
        Assert.Equal("Newest first", App.Label(app.Tree, app.Order));

        app.Click(app.Order);
        Assert.Equal(["[ ] Item 1", "[ ] Item 2", "[ ] Item 3"], app.Labels());
        Assert.Equal("Oldest first", App.Label(app.Tree, app.Order));
    }

    /// <summary>
    /// An item dragged onto another row takes its place, and the rows between shift by one (row DD1, design §7, "Drag and drop"); the
    /// press on the item's button gives no click, so the item is not marked.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - The todo app, headless, with three items added by the model.
    ///
    /// Steps:
    /// - Press the first row's button, move 10 px right, move to the third row's button, release; then drag the third row (now Item
    ///   1) onto the first.
    ///
    /// Expected:
    /// - After the first drag the rows are Items 2, 3, 1, none marked; after the second, Items 1, 2, 3.
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task DragAnItem_MovesItToTheRowItIsDroppedOn()
    {
        var model = new TodoModel();
        await using HeadlessApp headless = await HeadlessApp.StartAsync(() => TodoApp.Todo(model), Headless);
        var app = App.Of(headless, model);
        AddThree(app);

        DragRow(headless, app, 0, 2);
        List<string> first = app.Labels();
        DragRow(headless, app, 2, 0);

        Assert.Equal(["[ ] Item 2", "[ ] Item 3", "[ ] Item 1"], first);
        Assert.Equal(["[ ] Item 1", "[ ] Item 2", "[ ] Item 3"], app.Labels());
    }

    /// <summary>
    /// Text dropped from the OS on the list becomes an item, and so does each file dropped on the app, by its name; an item dragged
    /// out of the window goes to the OS as its name (row DD1, design §7, "Drag and drop").
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - The todo app, headless, with three items added by the model.
    ///
    /// Steps:
    /// - Drop the text "Milk" from the OS on the list's center; drop two files, "C:\a\Report.txt" and "D:\b.png", on the list's
    ///   center. Drag the first row's button 10 px, then left of the window.
    ///
    /// Expected:
    /// - The rows: Items 1, 2, 3, "Milk", "Report.txt", "b.png". One drag handed to the OS, with the text "Item 1" and Move and Copy
    ///   allowed.
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task DropsFromTheOs_AddItems_AndAnItemDragsOutAsItsName()
    {
        var model = new TodoModel();
        await using HeadlessApp headless = await HeadlessApp.StartAsync(() => TodoApp.Todo(model), Headless);
        var app = App.Of(headless, model);
        AddThree(app);
        (int x, int y) = headless.CenterOf(new Element(app.Tree, app.List));

        DragEffects both = DragEffects.Copy | DragEffects.Move;
        _ = headless.Input.DropEnter(new DroppedText("Milk"), x, y, both, KeyModifiers.None);
        _ = headless.Input.Drop(x, y, KeyModifiers.None);
        _ = headless.Input.DropEnter(new DroppedFiles([@"C:\a\Report.txt", @"D:\b.png"]), x, y, both, KeyModifiers.None);
        _ = headless.Input.Drop(x, y, KeyModifiers.None);
        _ = headless.PumpAndSettle();
        List<string> labels = app.Labels();
        (int bx, int by) = headless.CenterOf(new Element(app.Tree, app.ItemButton(0)));
        headless.Pointer.Move(bx, by);
        headless.Pointer.Down();
        headless.Pointer.Move(bx + 10, by);
        headless.Pointer.Move(-10, by);
        _ = headless.PumpAndSettle();

        Assert.Equal(["[ ] Item 1", "[ ] Item 2", "[ ] Item 3", "[ ] Milk", "[ ] Report.txt", "[ ] b.png"], labels);
        (ExternalDragData data, DragEffects allowed) = Assert.Single(headless.Platform.ExternalDrags);
        Assert.Equal("Item 1", data.Text);
        Assert.Equal(both, allowed);
    }

    /// <summary>
    /// The frames that draw a change of an item's mark, and a reversal of the list, allocate nothing; the changes themselves are the
    /// app's, which builds a new list. The items all fit in the range the virtualized list makes, so that a reversal makes no row: a
    /// row made runs its render, which allocates (design §3.4).
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - The todo app, headless, with 10 items added by the model, all of them in the list's viewport and cache extent, settled.
    ///   Warmed up with the same iterations.
    ///
    /// Steps:
    /// - 1,000 iterations in the windows of <c>PerformanceAssert.DoesNotAllocate</c>: unmeasured, toggle the first item on even
    ///   iterations and reverse the list on odd ones; measured, pump until settled, which runs the frame on the virtual clock.
    ///
    /// Expected:
    /// - 0 bytes in every window.
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task SteadyState_MarkAndReverseFrames_AllocateNothing()
    {
        var model = new TodoModel();
        await using HeadlessApp headless = await HeadlessApp.StartAsync(() => TodoApp.Todo(model), Headless);
        for (int i = 0; i < 10; i++)
        {
            _ = model.Add($"Item {i + 1}");
        }

        _ = headless.PumpAndSettle();
        Assert.Equal(10, App.Of(headless, model).Rows().Count);

        void Change(int index)
        {
            if ((index & 1) == 0)
            {
                model.Toggle(1);
            }
            else
            {
                model.Reverse();
            }
        }

        void Frame(int index) => _ = headless.PumpAndSettle();

        for (int i = 0; i < 20; i++)
        {
            Change(i);
            Frame(i);
        }

        PerformanceAssert.DoesNotAllocate(1_000, Change, Frame);
    }

    private static void AddThree(App app)
    {
        for (int i = 0; i < 3; i++)
        {
            _ = app.Model.Add($"Item {i + 1}");
        }

        app.Settle();
    }

    // The parts of the todo app's tree, found from its root: the column of the heading, the field and its button, the buttons, and
    // the scroll view; and
    // clicks at a node's center with the headless app's pointer, each followed by the frames that lay out and draw what they
    // changed.
    // Drags a row by its button onto another row's button.
    private static void DragRow(HeadlessApp headless, App app, int from, int to)
    {
        (int x, int y) = headless.CenterOf(new Element(app.Tree, app.ItemButton(from)));
        headless.Pointer.Move(x, y);
        headless.Pointer.Down();
        headless.Pointer.Move(x + 10, y);
        (int tx, int ty) = headless.CenterOf(new Element(app.Tree, app.ItemButton(to)));
        headless.Pointer.Move(tx, ty);
        headless.Pointer.Up();
        _ = headless.PumpAndSettle();
    }

    private sealed class App
    {
        private readonly Action<NodeHandle> _click;
        private readonly Action _settle;

        private App(UiTree tree, TodoModel model, Action<NodeHandle> click, Action settle)
        {
            Tree = tree;
            Model = model;
            _click = click;
            _settle = settle;
        }

        public UiTree Tree { get; }

        public TodoModel Model { get; }

        public NodeHandle Field => Child(Entry, 0);

        public NodeHandle Add => Child(Entry, 1);

        public NodeHandle Order => Child(Buttons, 3);

        public NodeHandle List => Child(Column, 3);

        // The column inside the selection area at the root.
        private NodeHandle Column => Tree.Nodes.GetRef(Tree.Nodes.GetRef(Tree.Root).FirstChild).FirstChild;

        private NodeHandle Entry => Child(Column, 1);

        private NodeHandle Buttons => Child(Column, 2);

        public static App Of(HeadlessApp headless, TodoModel model) =>
            new(
                headless.Tree,
                model,
                node =>
                {
                    headless.Pointer.Click(new Element(headless.Tree, node));
                    _ = headless.PumpAndSettle();
                },
                () => _ = headless.PumpAndSettle()
            );

        public static string Label(UiTree tree, NodeHandle button) => tree.Nodes.GetRef(tree.Nodes.GetRef(button).FirstChild).Text!.Text.ToString();

        public void Click(NodeHandle node) => _click(node);

        public void Settle() => _settle();

        public NodeHandle Filter(int index) => Child(Buttons, index);

        public (string Count, string Left) Heading()
        {
            NodeHandle heading = Child(Column, 0);
            NodeHandle count = Child(heading, 0);
            return (Tree.Nodes.GetRef(count).Text!.Text.ToString(), Tree.Nodes.GetRef(Child(heading, 1)).Text!.Text.ToString());
        }

        public List<NodeHandle> Rows()
        {
            var rows = new List<NodeHandle>();
            for (NodeHandle row = Tree.Nodes.GetRef(List).FirstChild; !row.IsNone; row = Tree.Nodes.GetRef(row).NextSibling)
            {
                rows.Add(row);
            }

            return rows;
        }

        public NodeHandle ItemButton(int row) => Tree.Nodes.GetRef(Rows()[row]).FirstChild;

        public List<string> Labels() => Rows().Select(row => Label(Tree, Tree.Nodes.GetRef(row).FirstChild)).ToList();

        public List<bool> FiltersChosen() =>
            Enumerable.Range(0, 3).Select(i => Tree.Nodes.GetRef(Filter(i)).Visual!.Background == (Paint)TodoApp.ChosenBackground).ToList();

        private NodeHandle Child(NodeHandle parent, int index)
        {
            NodeHandle child = Tree.Nodes.GetRef(parent).FirstChild;
            for (int i = 0; i < index; i++)
            {
                child = Tree.Nodes.GetRef(child).NextSibling;
            }

            return child;
        }
    }
}
