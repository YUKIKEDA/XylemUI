using System.Numerics;
using TerraFX.Interop.Windows;
using Todo;
using XylemUI.Tests;
using XylemUI.Text;
using static TerraFX.Interop.Windows.Windows;

namespace XylemUI.Desktop.Tests;

// The todo sample in a real window (row V3c, design §2.7, "How it is checked"): MoonBit's V3 and the parts row V3c adds, with the
// clicks sent as the window's messages and the pixels read on WARP.
[Collection(UiThreadGroup.Name)]
public sealed class TodoTests(UiThreadFixture ui)
{
    private static readonly WindowOptions Options = new("Todo test", 420, 360);

    /// <summary>
    /// Three clicks on Add make "todos: 3"; with the list scrolled to 64, the second item keeps its y, and the pixel of an item
    /// shown by the scroll is the buttons' color (MoonBit's V3).
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - The todo window on WARP, hidden; its target reads a pixel after each draw.
    ///
    /// Steps:
    /// - Click Add three times, a frame after each. Click it seven times more, so that the ten items are taller than the list. Read
    ///   the second item's y and the first item hidden below the list's bottom. Read the pixel of that item's button, in its left
    ///   padding (3 logical pixels in, away from its text). Scroll the list to 64; run a frame; read the pixel where the scroll put it.
    ///
    /// Expected:
    /// - The heading reads "todos: 3" and "left: 3" after three clicks. Before the scroll the pixel is not the buttons' color: the
    ///   list clips the item. After the scroll, the list's position is 64, the second item's y is the same, and the pixel is
    ///   <c>TodoApp.ButtonBackground</c>.
    /// </remarks>
    [Fact]
    public void ThreeAdds_CountThree_AndTheScrolledListKeepsItsRows() =>
        ui.Invoke(() =>
        {
            ReadingTarget? reading = null;
            var model = new TodoModel();
            using var window = new DesktopWindow(
                () => TodoApp.Todo(model),
                Options,
                useWarp: true,
                target: renderer => reading = new ReadingTarget(renderer)
            );
            var app = new App(window);
            for (int i = 0; i < 3; i++)
            {
                app.Click(app.Add);
            }

            Assert.Equal(("todos: 3", "left: 3"), app.Heading());
            for (int i = 0; i < 7; i++)
            {
                app.Click(app.Add);
            }

            NodeHandle second = app.Rows()[1];
            float y = window.Tree.GetRect(second).Y;
            NodeHandle hidden = app.Rows().First(row => window.Tree.GetRect(row).Y > TodoApp.ListHeight);
            NodeHandle button = window.Tree.Nodes.GetRef(hidden).FirstChild;
            reading!.Point = app.At(button, 3);
            Invalidation.Invalidate(window.Tree, button, ChangeKind.Color);
            window.Frames.RunFrame();
            Color clipped = reading.Pixel;
            window.Tree.ScrollStateOf(app.List)!.ScrollTo(new Vector2(0, 64));
            reading.Point = app.At(button, 3);
            window.Frames.RunFrame();

            Assert.Equal(64, window.Tree.ScrollOffsetOf(app.List).Y);
            Assert.Equal(y, window.Tree.GetRect(second).Y);
            Assert.NotEqual(TodoApp.ButtonBackground, clipped);
            Assert.Equal(TodoApp.ButtonBackground, reading.Pixel);
        });

    /// <summary>
    /// Clicking an item marks it done, with a mark and a gray text, and again marks it not done; "left" follows.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - The todo window on WARP with three items added by the model.
    ///
    /// Steps:
    /// - Click the second item's button; then click it again.
    ///
    /// Expected:
    /// - After the first click: its label is "[x] Item 2", its text color <c>TodoApp.DoneColor</c>, and the heading "left: 2"; the
    ///   others keep "[ ]". After the second: "[ ] Item 2", <c>TodoApp.TextColor</c>, and "left: 3".
    /// </remarks>
    [Fact]
    public void ClickOnAnItem_MarksItDone_AndAgainNotDone() =>
        ui.Invoke(() =>
        {
            var model = new TodoModel();
            using var window = new DesktopWindow(() => TodoApp.Todo(model), Options, useWarp: true);
            var app = new App(window);
            AddThree(model, app);

            app.Click(app.ItemButton(1));

            Assert.Equal(["[ ] Item 1", "[x] Item 2", "[ ] Item 3"], app.Labels());
            Assert.Equal(TodoApp.DoneColor, window.Tree.Nodes.GetRef(app.ItemButton(1)).Visual!.Foreground);
            Assert.Equal("left: 2", app.Heading().Left);

            app.Click(app.ItemButton(1));

            Assert.Equal(["[ ] Item 1", "[ ] Item 2", "[ ] Item 3"], app.Labels());
            Assert.Equal(TodoApp.TextColor, window.Tree.Nodes.GetRef(app.ItemButton(1)).Visual!.Foreground);
            Assert.Equal("left: 3", app.Heading().Left);
        });

    /// <summary>
    /// The remove button of an item removes it, and only it.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - The todo window on WARP with three items added by the model.
    ///
    /// Steps:
    /// - Click the second item's remove button.
    ///
    /// Expected:
    /// - The rows are Item 1 and Item 3, and the heading reads "todos: 2" and "left: 2".
    /// </remarks>
    [Fact]
    public void RemoveButton_RemovesItsItem() =>
        ui.Invoke(() =>
        {
            var model = new TodoModel();
            using var window = new DesktopWindow(() => TodoApp.Todo(model), Options, useWarp: true);
            var app = new App(window);
            AddThree(model, app);

            app.Click(window.Tree.Nodes.GetRef(app.ItemButton(1)).NextSibling);

            Assert.Equal(["[ ] Item 1", "[ ] Item 3"], app.Labels());
            Assert.Equal(("todos: 2", "left: 2"), app.Heading());
        });

    /// <summary>
    /// The three filters show every item, the items not done, and the items done, and the chosen filter's button is darker.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - The todo window on WARP with three items added by the model, the second marked done by a click.
    ///
    /// Steps:
    /// - Click Active, then Done, then All.
    ///
    /// Expected:
    /// - Active shows Items 1 and 3, Done shows Item 2, and All shows the three. After each click the chosen button's background
    ///   is <c>TodoApp.ChosenBackground</c> and the other two are <c>TodoApp.ButtonBackground</c>.
    /// </remarks>
    [Fact]
    public void Filters_ShowTheItemsTheyLetThrough() =>
        ui.Invoke(() =>
        {
            var model = new TodoModel();
            using var window = new DesktopWindow(() => TodoApp.Todo(model), Options, useWarp: true);
            var app = new App(window);
            AddThree(model, app);
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
        });

    /// <summary>
    /// The order button puts the newest item first, and again the oldest first.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - The todo window on WARP with three items added by the model.
    ///
    /// Steps:
    /// - Click the order button; then click it again.
    ///
    /// Expected:
    /// - After the first click the rows are Items 3, 2, 1 and the button reads "Newest first"; after the second, Items 1, 2, 3 and
    ///   "Oldest first".
    /// </remarks>
    [Fact]
    public void OrderButton_ReversesTheList() =>
        ui.Invoke(() =>
        {
            var model = new TodoModel();
            using var window = new DesktopWindow(() => TodoApp.Todo(model), Options, useWarp: true);
            var app = new App(window);
            AddThree(model, app);

            app.Click(app.Order);
            Assert.Equal(["[ ] Item 3", "[ ] Item 2", "[ ] Item 1"], app.Labels());
            Assert.Equal("Newest first", App.Label(window.Tree, app.Order));

            app.Click(app.Order);
            Assert.Equal(["[ ] Item 1", "[ ] Item 2", "[ ] Item 3"], app.Labels());
            Assert.Equal("Oldest first", App.Label(window.Tree, app.Order));
        });

    /// <summary>
    /// The frames that draw a change of an item's mark, and a reversal of the list, allocate nothing; the changes themselves are the
    /// app's, which builds a new list.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - The todo window with 100 items added by the model, its target drawing and presenting nothing: the frame's own stages are
    ///   what is checked, and 1,120 presents on WARP took longer than the CI's job allows (PR #381). Warmed up with the same
    ///   iterations.
    ///
    /// Steps:
    /// - 1,000 iterations in the windows of <c>SteadyStateAllocation</c>: unmeasured, toggle the first item on even iterations and
    ///   reverse the list on odd ones; measured, run the frame.
    ///
    /// Expected:
    /// - 0 bytes in every window.
    /// </remarks>
    [Fact]
    public void SteadyState_MarkAndReverseFrames_AllocateNothing() =>
        ui.Invoke(() =>
        {
            var model = new TodoModel();
            using var window = new DesktopWindow(() => TodoApp.Todo(model), Options, useWarp: true, target: _ => new NoDrawTarget());
            for (int i = 0; i < 100; i++)
            {
                model.Add();
            }

            window.Frames.RunFrame();

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

            void Frame(int index) => window.Frames.RunFrame();

            for (int i = 0; i < 20; i++)
            {
                Change(i);
                Frame(i);
            }

            SteadyStateAllocation.AssertNone(1_000, Change, Frame);
        });

    // A target that draws and presents nothing, for the frames whose layout and display list are what a test checks.
    private sealed class NoDrawTarget : IRenderTarget
    {
        public void Draw(DisplayList list, Color clearColor, GlyphAtlas atlas) { }

        public void Present() { }
    }

    private static void AddThree(TodoModel model, App app)
    {
        for (int i = 0; i < 3; i++)
        {
            model.Add();
        }

        app.Window.Frames.RunFrame();
    }

    // The parts of the todo window's tree, found from its root: the column of the heading, the buttons, and the scroll view; and
    // clicks at a node's center as the window's messages, each followed by a frame.
    private sealed class App
    {
        public App(DesktopWindow window)
        {
            Window = window;
            window.Frames.RunFrame();
        }

        public DesktopWindow Window { get; }

        public NodeHandle Add => Child(Buttons, 0);

        public NodeHandle Order => Child(Buttons, 4);

        public NodeHandle List => Child(Column, 2);

        private UiTree Tree => Window.Tree;

        private NodeHandle Column => Tree.Nodes.GetRef(Tree.Root).FirstChild;

        private NodeHandle Buttons => Child(Column, 1);

        public static string Label(UiTree tree, NodeHandle button) => tree.Nodes.GetRef(tree.Nodes.GetRef(button).FirstChild).Text!.Text.ToString();

        public NodeHandle Filter(int index) => Child(Buttons, 1 + index);

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

        // A press and a release of the left button at the node's center, then a frame that lays out and draws what they changed.
        public void Click(NodeHandle node)
        {
            (int x, int y) = Center(node);
            nint point = (y << 16) | (x & 0xFFFF);
            _ = SendMessageW(Window.Window.Hwnd, WM.WM_LBUTTONDOWN, MK.MK_LBUTTON, point);
            _ = SendMessageW(Window.Window.Hwnd, WM.WM_LBUTTONUP, 0, point);
            Window.Frames.RunFrame();
        }

        // The center of a node's box in the window's physical pixels.
        public (int X, int Y) Center(NodeHandle node) => At(node, Tree.GetRect(node).Width / 2);

        // A point of a node's box at its middle height, `fromLeft` logical pixels from its left edge, in the window's physical
        // pixels: its box and its ancestors' boxes added, less the scroll position of each scroll container above it.
        public (int X, int Y) At(NodeHandle node, float fromLeft)
        {
            Rect box = Tree.GetRect(node);
            float x = box.X + fromLeft;
            float y = box.Y + (box.Height / 2);
            for (NodeHandle ancestor = Tree.Nodes.GetRef(node).Parent; !ancestor.IsNone; ancestor = Tree.Nodes.GetRef(ancestor).Parent)
            {
                Rect outer = Tree.GetRect(ancestor);
                Vector2 offset = Tree.ScrollOffsetOf(ancestor);
                x += outer.X - offset.X;
                y += outer.Y - offset.Y;
            }

            float scale = Window.Window.ScaleFactor;
            return ((int)MathF.Round(x * scale), (int)MathF.Round(y * scale));
        }

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
