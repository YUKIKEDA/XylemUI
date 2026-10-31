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
    /// - Type "  Buy milk  " and press Enter; type "Call Bob" and click Add; type "   " and press Enter, then click Add.
    ///
    /// Expected:
    /// - After Enter: the rows "[ ] Buy milk", the field empty. After the click: "[ ] Buy milk" and "[ ] Call Bob", the field empty
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
        _ = headless.Keyboard.Press(Key.Enter);
        app.Settle();
        (List<string> Rows, string Field) entered = (app.Labels(), model.Draft.Get());
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
        Assert.Equal(["[ ] Buy milk", "[ ] Call Bob"], clicked.Rows);
        Assert.Equal((string.Empty, true), (clicked.Field, clicked.Focused));
        Assert.Equal(["[ ] Buy milk", "[ ] Call Bob"], app.Labels());
        Assert.Equal("   ", model.Draft.Get());
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
    /// The remove button of an item removes it, and only it.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - The todo app, headless, with three items added by the model.
    ///
    /// Steps:
    /// - Click the second item's remove button.
    ///
    /// Expected:
    /// - The rows are Item 1 and Item 3, and the heading reads "todos: 2" and "left: 2".
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task RemoveButton_RemovesItsItem()
    {
        var model = new TodoModel();
        await using HeadlessApp headless = await HeadlessApp.StartAsync(() => TodoApp.Todo(model), Headless);
        var app = App.Of(headless, model);
        AddThree(app);

        app.Click(app.Tree.Nodes.GetRef(app.ItemButton(1)).NextSibling);

        Assert.Equal(["[ ] Item 1", "[ ] Item 3"], app.Labels());
        Assert.Equal(("todos: 2", "left: 2"), app.Heading());
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
    /// The frames that draw a change of an item's mark, and a reversal of the list, allocate nothing; the changes themselves are the
    /// app's, which builds a new list.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - The todo app, headless, with 100 items added by the model, settled. Warmed up with the same iterations.
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
        for (int i = 0; i < 100; i++)
        {
            _ = model.Add($"Item {i + 1}");
        }

        _ = headless.PumpAndSettle();

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

        private NodeHandle Column => Tree.Nodes.GetRef(Tree.Root).FirstChild;

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
            for (NodeHandle row = Tree.Nodes.GetRef(Child(List, 0)).FirstChild; !row.IsNone; row = Tree.Nodes.GetRef(row).NextSibling)
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
