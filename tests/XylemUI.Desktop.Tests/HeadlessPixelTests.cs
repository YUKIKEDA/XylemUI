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
    /// - Click the field; three times, type "Item n" in it and click Add; read the heading. Add seven items more the same way. Read
    ///   the second item's y, and the pixel of the first item below the list's bottom, 3 logical pixels into its button (away from
    ///   its text). Scroll the list to 64; pump; read them again; compare the frame with "todo-scrolled.png".
    ///
    /// Expected:
    /// - "todos: 3" after three items. Before the scroll the pixel is not the buttons' color; after it, the list's position is 64,
    ///   the second item's y is the same, the pixel is <c>TodoApp.ButtonBackground</c>, and the frame matches the golden.
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task Todo_ScrolledListShowsTheClippedRow_AndMatchesItsGolden()
    {
        var model = new TodoModel();
        await using HeadlessApp app = await HeadlessApp.StartAsync(() => TodoApp.Todo(model), Options(420, 360));
        Element add = app.Find("Add");
        app.Pointer.Click(app.Find(TodoApp.Placeholder));
        void AddItem(int number)
        {
            app.Keyboard.Type($"Item {number}");
            app.Pointer.Click(add);
            _ = app.PumpAndSettle();
        }

        for (int i = 1; i <= 3; i++)
        {
            AddItem(i);
        }

        _ = app.Find("todos: 3");
        for (int i = 4; i <= 10; i++)
        {
            AddItem(i);
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

    /// <summary>
    /// A selection across two text leaves of an area draws its background behind the selected glyphs of each, line by line, and
    /// the selected glyphs in its text color; the frame matches its golden.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A selection area around a column of "Selected text" and "across two leaves" in black at 14 px on white, in a headless app
    ///   of 200 x 60 on WARP, with the headless app's selection colors.
    ///
    /// Steps:
    /// - Drag from x = 30 on the first leaf to x = 60 on the second; read the pixel at the start of the first leaf (left of the
    ///   selection) and at its end; compare the frame with "selection-across-leaves.png".
    ///
    /// Expected:
    /// - The pixel left of the selection is white; the one at x = 40 is the selection's background; the frame matches
    ///   the golden.
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task SelectionArea_AcrossTwoLeaves_MatchesItsGolden()
    {
        await using HeadlessApp app = await HeadlessApp.StartAsync(
            () =>
                Primitives
                    .Primitive.SelectionArea(Column(Elements.Text("Selected text"), Elements.Text("across two leaves")))
                    .Background(White)
                    .Foreground(Color.FromRgb(0x000000)),
            Options(200, 60)
        );
        app.Pointer.Move(30, 10);
        app.Pointer.Down();
        app.Pointer.Move(60, 30);
        app.Pointer.Up();
        _ = app.PumpAndSettle();

        Assert.Equal(White, Pixel(app, (2, 2)));
        Assert.Equal(TextEditingSettings.Default.SelectionBackground, Pixel(app, (40, 2)));
        await app.MatchesGoldenAsync("selection-across-leaves.png");
    }

    /// <summary>
    /// A composition draws each clause's underline as its attribute says, the thick one twice the normal, not cut at the glyphs,
    /// each shortened at its ends so that two clauses side by side show a gap (design §4.7); the frame matches its golden.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A field of one line in black at 14 px on white, in a headless app of 200 x 40 on WARP, with the focus.
    ///
    /// Steps:
    /// - Compose "日本語ab" with "日本" thick and solid, and "語" and "ab" each thin and solid, two clauses that look the same; find
    ///   the row of each underline under the middle of its clause; read the pixels of those rows 3 and 1 pixels left of the end of
    ///   "日本" and of "語"; compare the frame with "ime-composition.png".
    ///
    /// Expected:
    /// - At both ends, the pixel 3 left of the end is dark (the line), the one 1 left of it is lighter (the gap); the frame matches
    ///   the golden.
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task Composition_ClausesAndTheirGaps_MatchTheirGolden()
    {
        Element field = default;
        await using HeadlessApp app = await HeadlessApp.StartAsync(
            () =>
                Row(field = Primitives.Primitive.TextField(Signal.Create(string.Empty)).Width(180))
                    .Background(White)
                    .Foreground(Color.FromRgb(0x000000)),
            Options(200, 40) with
            {
                CaretBlink = false,
            }
        );
        _ = app.PumpAndSettle();
        Assert.True(app.Focus(field));
        _ = app.Ime.Compose(
            "日本語ab",
            cursor: 5,
            clauses:
            [
                new(0, 2, DecorationStyle.Solid, Thick: true),
                new(2, 3, DecorationStyle.Solid, Thick: false),
                new(3, 5, DecorationStyle.Solid, Thick: false),
            ]
        );
        _ = app.PumpAndSettle();

        foreach ((int start, int end) in (ReadOnlySpan<(int, int)>)[(0, 2), (2, 3)])
        {
            Rect clause = app.Ime.BoundsOf(start, end)!.Value;
            int right = (int)MathF.Round(clause.X + clause.Width);
            int middle = (int)MathF.Round(clause.X + (clause.Width / 2));
            int row = LowestDark(app, middle, (int)clause.Y, (int)MathF.Ceiling(clause.Y + clause.Height) + 4);

            Assert.True(Darkness(Pixel(app, (right - 3, row))) > 64);
            Assert.True(Darkness(Pixel(app, (right - 1, row))) < Darkness(Pixel(app, (right - 3, row))));
        }

        await app.MatchesGoldenAsync("ime-composition.png");
    }

    // How dark a pixel is, from 0 (white) to 255 (black), by its green channel.
    private static int Darkness(Color color) => 255 - color.G;

    // The lowest row from top to bottom (exclusive) where a column's pixel is dark: the underline under a glyph.
    private static int LowestDark(HeadlessApp app, int x, int top, int bottom)
    {
        int found = -1;
        for (int y = Math.Max(0, top); y < bottom; y++)
        {
            if (Darkness(Pixel(app, (x, y))) > 128)
            {
                found = y;
            }
        }

        Assert.True(found >= 0);
        return found;
    }

    /// <summary>
    /// The todo field's context menu is drawn over the window at the pointer, with its panel's background, and matches its golden
    /// (row P02d, design §7.3, "Checks").
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - The todo list in a headless app of 420 x 360 on WARP, with "Buy milk" typed in the field and an empty clipboard.
    ///
    /// Steps:
    /// - Click the right button at (100, 60), in the field; read the pixel 4 logical pixels into the panel; compare the frame with
    ///   "todo-menu.png".
    ///
    /// Expected:
    /// - One popup; the pixel is <c>TodoApp.PanelBackground</c>; the frame matches the golden.
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task Todo_FieldContextMenu_MatchesItsGolden()
    {
        await using HeadlessApp app = await HeadlessApp.StartAsync(Todo.TodoApp.Todo, Options(420, 360));
        app.Pointer.Click(app.Find(TodoApp.Placeholder));
        _ = app.Keyboard.Type("Buy milk");
        _ = app.PumpAndSettle();

        app.Pointer.Click(100, 60, PointerButton.Right);
        _ = app.PumpAndSettle();

        _ = Assert.Single(app.Popups);
        Assert.Equal(TodoApp.PanelBackground, Pixel(app, (104, 62)));
        await app.MatchesGoldenAsync("todo-menu.png");
    }

    /// <summary>
    /// The dialog that asks before an item is removed is drawn over a backdrop that darkens the whole window, and matches its golden
    /// (row P02d, design §7.3, "Checks").
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - The todo list in a headless app of 420 x 360 on WARP, with the item "Buy milk".
    ///
    /// Steps:
    /// - Click the item's remove button; read the pixel at (2, 2); compare the frame with "todo-dialog.png".
    ///
    /// Expected:
    /// - One popup; the pixel is the window's white under the half-black backdrop, 127 or 128 in each channel; the frame matches the
    ///   golden.
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task Todo_RemoveDialog_MatchesItsGolden()
    {
        var model = new TodoModel();
        await using HeadlessApp app = await HeadlessApp.StartAsync(() => TodoApp.Todo(model), Options(420, 360));
        _ = model.Add("Buy milk");
        _ = app.PumpAndSettle();

        app.Pointer.Click(app.Find("×"));
        _ = app.PumpAndSettle();
        Color corner = Pixel(app, (2, 2));

        _ = Assert.Single(app.Popups);
        Assert.InRange(corner.R, 127, 128);
        Assert.Equal((corner.R, corner.R), (corner.G, corner.B));
        await app.MatchesGoldenAsync("todo-dialog.png");
    }

    /// <summary>
    /// A tooltip is drawn over the window 20 px below the pointer, with its content's background, and matches its golden (row
    /// P02d2, design §13.1, "Tooltip").
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A headless app of 240 x 120 on WARP: on white, a 100 x 30 gray box at (10, 10) with a tooltip of the text "Saves the
    ///   file" on a dark background with a padding of 6 by 3.
    ///
    /// Steps:
    /// - Move the pointer to (40, 25); pump 400 ms; read the pixel at (42, 47), inside the tooltip's padding; compare the frame with
    ///   "tooltip.png".
    ///
    /// Expected:
    /// - One popup; the pixel is the tooltip's background; the frame matches the golden.
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task Tooltip_BelowThePointer_MatchesItsGolden()
    {
        Color dark = Color.FromRgb(0x2B3245);
        await using HeadlessApp app = await HeadlessApp.StartAsync(
            () =>
                Box(
                        Primitives.Primitive.Tooltip(
                            Box().Width(100).Height(30).Background(Color.FromRgb(0xC0C0C0)),
                            () => Elements.Text("Saves the file").Padding(6, 3).Background(dark).Foreground(White)
                        )
                    )
                    .Padding(10)
                    .Background(White),
            Options(240, 120)
        );
        _ = app.PumpAndSettle();

        app.Pointer.Move(40, 25);
        app.Pump(TimeSpan.FromMilliseconds(400));
        app.Pump(TimeSpan.Zero);

        _ = Assert.Single(app.Popups);
        Assert.Equal(dark, Pixel(app, (42, 47)));
        await app.MatchesGoldenAsync("tooltip.png");
    }

    /// <summary>
    /// A menu whose items hold access texts underlines their keys while the window shows the access keys, and matches its golden
    /// (row P02d2, design §13.1, "Access keys").
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A headless app of 200 x 140 on WARP with Windows' "underline access keys" setting on: on white, a 120 x 24 gray box at
    ///   (10, 10) with an open menu of "_Open", "_Save as", and "E_xit" in a dark panel.
    ///
    /// Steps:
    /// - Settle; compare the frame with "menu-access-keys.png".
    ///
    /// Expected:
    /// - One popup; the frame matches the golden, with "O", "S", and "x" underlined.
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task Menu_AccessKeys_MatchesItsGolden()
    {
        Color dark = Color.FromRgb(0x2B3245);
        var open = Signal.Create(true);
        await using HeadlessApp app = await HeadlessApp.StartAsync(
            () =>
                Box(
                        Primitives.Primitive.Menu(
                            Box().Width(120).Height(24).Background(Color.FromRgb(0xC0C0C0)),
                            open,
                            () =>
                                Column(
                                        Primitives.Primitive.MenuItem(Primitives.Primitive.AccessText("_Open").Padding(8, 3), () => { }),
                                        Primitives.Primitive.MenuItem(Primitives.Primitive.AccessText("_Save as").Padding(8, 3), () => { }),
                                        Primitives.Primitive.MenuItem(Primitives.Primitive.AccessText("E_xit").Padding(8, 3), () => { })
                                    )
                                    .Width(120)
                                    .Padding(4)
                                    .Background(dark)
                                    .Foreground(White)
                        )
                    )
                    .Padding(10)
                    .Background(White),
            Options(200, 140) with
            {
                KeyboardCues = true,
            }
        );
        _ = app.PumpAndSettle();

        _ = Assert.Single(app.Popups);
        await app.MatchesGoldenAsync("menu-access-keys.png");
    }

    /// <summary>
    /// An open select draws its list below its box in the panel the app gives, with the focused row's look, and matches its golden
    /// (row P02d2, design §13.1, "Select").
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A headless app of 240 x 160 on WARP: on white, a select of "Apple", "Banana", "Cherry", its value "Banana", 140 wide at
    ///   (10, 10), each option's look its text with a padding of 8 by 4 and a highlight when focused; the box gray, the panel dark
    ///   with a padding of 4.
    ///
    /// Steps:
    /// - Click the box; read the pixel 2 px into the panel; compare the frame with "select-open.png".
    ///
    /// Expected:
    /// - One popup; the pixel is the panel's background; the frame matches the golden.
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task Select_Open_MatchesItsGolden()
    {
        Color dark = Color.FromRgb(0x2B3245);
        var value = Signal.Create("Banana");
        Element select = default;
        await using HeadlessApp app = await HeadlessApp.StartAsync(
            () =>
                Box(
                        select = Primitives
                            .Primitive.Select(
                                value,
                                ["Apple", "Banana", "Cherry"],
                                option => Elements.Text(option).Padding(8, 4).Focused(new PaintStyle { Background = Color.FromRgb(0x3F4A66) }),
                                new Primitives.SelectOptions<string> { Panel = list => list.Padding(4).Background(dark).Foreground(White) }
                            )
                            .Width(140)
                            .Background(Color.FromRgb(0xC0C0C0))
                    )
                    .Padding(10)
                    .Background(White),
            Options(240, 160)
        );
        _ = app.PumpAndSettle();

        app.Pointer.Click(select);
        _ = app.PumpAndSettle();

        _ = Assert.Single(app.Popups);
        Rect box = app.Tree.GetRect(select.Node);
        Assert.Equal(dark, Pixel(app, (12, (int)(10 + box.Height) + 2)));
        await app.MatchesGoldenAsync("select-open.png");
    }

    /// <summary>
    /// A selected menu bar draws its items in a row with the access keys underlined and the first item highlighted, with no menu open,
    /// and matches its golden (row P02d3, design §13.1, "The menu bar").
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A headless app of 160 x 60 on WARP: on white, a gray menu bar at (10, 10) of "_File" and "_Edit", each with a padding of 8
    ///   by 3 and a highlight when focused, and a menu each of "_New" and "E_xit".
    ///
    /// Steps:
    /// - F10; settle; compare the frame with "menu-bar-selected.png".
    ///
    /// Expected:
    /// - No popup; the frame matches the golden, with "F" and "E" underlined and "File" highlighted.
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task MenuBar_Selected_MatchesItsGolden()
    {
        Color dark = Color.FromRgb(0x2B3245);
        var focused = new PaintStyle { Background = Color.FromRgb(0x8090B0) };
        await using HeadlessApp app = await HeadlessApp.StartAsync(
            () =>
                Box(
                        Primitives
                            .Primitive.MenuBar(
                                Primitives
                                    .Primitive.MenuBarItem(Primitives.Primitive.AccessText("_File").Padding(8, 3), () => FilePanel(dark))
                                    .Focused(focused),
                                Primitives
                                    .Primitive.MenuBarItem(Primitives.Primitive.AccessText("_Edit").Padding(8, 3), () => FilePanel(dark))
                                    .Focused(focused)
                            )
                            .Background(Color.FromRgb(0xC0C0C0))
                    )
                    .Padding(10)
                    .Background(White),
            Options(160, 60)
        );
        _ = app.PumpAndSettle();

        _ = app.Keyboard.Press(Key.F10);
        _ = app.PumpAndSettle();

        Assert.Empty(app.Popups);
        await app.MatchesGoldenAsync("menu-bar-selected.png");
    }

    private static Element FilePanel(Color dark) =>
        Column(
                Primitives.Primitive.MenuItem(Primitives.Primitive.AccessText("_New").Padding(8, 3), () => { }),
                Primitives.Primitive.MenuItem(Primitives.Primitive.AccessText("E_xit").Padding(8, 3), () => { })
            )
            .Width(100)
            .Padding(4)
            .Background(dark)
            .Foreground(White);

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
