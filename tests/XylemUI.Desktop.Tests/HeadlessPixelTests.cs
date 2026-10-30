using System.Numerics;
using Counter;
using Todo;
using XylemUI.Testing.D3D11;
using XylemUI.Testing.XUnit;
using static XylemUI.Elements;

namespace XylemUI.Desktop.Tests;

// The pixels of the samples and of a scroll view, drawn on WARP in a headless app with no window (design §12.3, row P02a2), and the
// first goldens: the counter and the todo list.
public sealed class HeadlessPixelTests
{
    private static readonly Color White = Color.FromRgb(0xFFFFFF);

    /// <summary>
    /// The pixel at the center of the counter's button is the background the sample gives it, and the counter matches its golden.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - The counter in a headless app of 320 x 200 on WARP.
    ///
    /// Steps:
    /// - Read the pixel at the center of the button (the parent of the text "+1"); compare the frame with "counter.png".
    ///
    /// Expected:
    /// - The pixel is <c>CounterApp.ButtonBackground</c>; the frame matches the golden.
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task Counter_ButtonCenterIsItsBackground_AndMatchesItsGolden()
    {
        await using HeadlessApp app = await HeadlessApp.StartAsync(CounterApp.Counter, Options(320, 200));
        NodeHandle button = app.Tree.Nodes.GetRef(app.Find("+1").Node).Parent;

        Assert.Equal(CounterApp.ButtonBackground, Pixel(app, app.CenterOf(new Element(app.Tree, button))));
        await app.MatchesGoldenAsync("counter.png");
    }

    /// <summary>
    /// With ten items, the list clips the one below its bottom; scrolled to 64, the second item keeps its y and the clipped one shows
    /// in the buttons' color; the scrolled list matches its golden (MoonBit's V3).
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - The todo list in a headless app of 420 x 360 on WARP.
    ///
    /// Steps:
    /// - Click Add three times; read the heading. Click it seven times more. Read the second item's y, and the pixel of the first item
    ///   below the list's bottom, 3 logical pixels into its button (away from its text). Scroll the list to 64; pump; read them again;
    ///   compare the frame with "todo-scrolled.png".
    ///
    /// Expected:
    /// - "todos: 3" after three clicks. Before the scroll the pixel is not the buttons' color; after it, the list's position is 64,
    ///   the second item's y is the same, the pixel is <c>TodoApp.ButtonBackground</c>, and the frame matches the golden.
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task Todo_ScrolledListShowsTheClippedRow_AndMatchesItsGolden()
    {
        var model = new TodoModel();
        await using HeadlessApp app = await HeadlessApp.StartAsync(() => TodoApp.Todo(model), Options(420, 360));
        Element add = app.Find("Add");
        for (int i = 0; i < 3; i++)
        {
            app.Pointer.Click(add);
            _ = app.PumpAndSettle();
        }

        _ = app.Find("todos: 3");
        for (int i = 0; i < 7; i++)
        {
            app.Pointer.Click(add);
            _ = app.PumpAndSettle();
        }

        UiTree tree = app.Tree;
        List<NodeHandle> rows = Rows(app);
        float y = tree.GetRect(rows[1]).Y;
        NodeHandle hidden = rows.First(row => tree.GetRect(row).Y > TodoApp.ListHeight);
        NodeHandle button = tree.Nodes.GetRef(hidden).FirstChild;
        Color clipped = Pixel(app, InButton(app, button));
        NodeHandle list = tree.Nodes.GetRef(rows[0]).Parent;
        list = tree.Nodes.GetRef(list).Parent;
        tree.ScrollStateOf(list)!.ScrollTo(new Vector2(0, 64));
        _ = app.PumpAndSettle();

        Assert.Equal(64, tree.ScrollOffsetOf(list).Y);
        Assert.Equal(y, tree.GetRect(rows[1]).Y);
        Assert.NotEqual(TodoApp.ButtonBackground, clipped);
        Assert.Equal(TodoApp.ButtonBackground, Pixel(app, InButton(app, button)));
        await app.MatchesGoldenAsync("todo-scrolled.png");
    }

    /// <summary>
    /// A box below the scroll view's height is clipped, and drawn where the view shows it once it scrolls.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A headless app of 200 x 200 on WARP: a scroll view 100 high holding a column of five boxes 50 high, the first red, the
    ///   fourth blue, the others gray; the window behind is white.
    ///
    /// Steps:
    /// - Read the pixels at (20, 25) and (20, 175). Scroll to (0, 150); pump; read (20, 25).
    ///
    /// Expected:
    /// - Red at (20, 25) and white at (20, 175), where the fourth box would be without the clip; blue at (20, 25) after the scroll.
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task ScrollView_ClippedBoxIsDrawnOnceScrolledIntoView()
    {
        Color first = Color.FromRgb(0xCC3333);
        Color below = Color.FromRgb(0x3366CC);
        var state = new ScrollState();
        await using HeadlessApp app = await HeadlessApp.StartAsync(
            () =>
            {
                var boxes = new Element[5];
                for (int i = 0; i < 5; i++)
                {
                    boxes[i] = Box()
                        .Height(50)
                        .Background(
                            new Paint(
                                i == 0 ? first
                                : i == 3 ? below
                                : Color.FromRgb(0x808080)
                            )
                        );
                }

                return Column(ScrollView(Column(boxes), state).Height(100));
            },
            Options(200, 200)
        );

        Assert.Equal(first, Pixel(app, (20, 25)));
        Assert.Equal(White, Pixel(app, (20, 175)));
        state.ScrollTo(new Vector2(0, 150));
        _ = app.PumpAndSettle();
        Assert.Equal(below, Pixel(app, (20, 25)));
    }

    private static HeadlessOptions Options(float width, float height) =>
        new()
        {
            Width = width,
            Height = height,
            Renderer = () => new WarpRenderer(),
        };

    // The pixel of the frame drawn last at a point in physical pixels (the scale is 1).
    private static Color Pixel(HeadlessApp app, (int X, int Y) point)
    {
        Color[] frame = app.Capture(out int width, out _);
        return frame[(point.Y * width) + point.X];
    }

    // A point 3 logical pixels into a button from its left, at its middle height: away from its text.
    private static (int X, int Y) InButton(HeadlessApp app, NodeHandle button)
    {
        (int x, int y) = app.CenterOf(new Element(app.Tree, button));
        Rect box = app.Tree.GetRect(button);
        return (x - (int)MathF.Round(box.Width / 2) + 3, y);
    }

    // The rows of the todo list: the children of the column the scroll view holds, found from the first item's text.
    private static List<NodeHandle> Rows(HeadlessApp app)
    {
        UiTree tree = app.Tree;
        NodeHandle label = app.FindAll("[ ] Item 1")[0].Node;
        NodeHandle row = tree.Nodes.GetRef(tree.Nodes.GetRef(label).Parent).Parent;
        var rows = new List<NodeHandle>();
        for (NodeHandle r = tree.Nodes.GetRef(tree.Nodes.GetRef(row).Parent).FirstChild; !r.IsNone; r = tree.Nodes.GetRef(r).NextSibling)
        {
            rows.Add(r);
        }

        return rows;
    }
}
