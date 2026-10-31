using XylemUI.Primitives;
using XylemUI.Testing.XUnit;
using static XylemUI.Elements;
using static XylemUI.Primitives.Primitive;

namespace XylemUI.Desktop.Tests;

// The selection area (row P02b2, design §4.10) in a headless app: the order of the leaves, the points between them, the clicks, the
// drag by units, the keys, when the selection goes, the copy, links, the parts left out, the pressable parts, the cursor, the
// scrolling while dragging, and the frames that allocate nothing. Its text is the test font's "n" at 14 px, 8.54 px wide, on lines
// 20.272 px high, at the window's top left.
public sealed class SelectionAreaTests
{
    private const float N = 0.61f * 14;
    private const float Line = 20.272f;

    /// <summary>
    /// A drag selects from the press to the pointer across the leaves in the order of the tree: the first from the press, those
    /// between in full, the last to the pointer; a copy joins the leaves' parts with a line break.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - An area around a column of three leaves of "nnnn".
    ///
    /// Steps:
    /// - Press at x = 1.4 n on the first; move to x = 2.4 n on the third; release; press Control+C.
    ///
    /// Expected:
    /// - The leaves' selections are 1 to 4, 0 to 4, and 0 to 2; the clipboard holds "nnn\nnnnn\nnn".
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task Drag_AcrossLeaves_SelectsInTreeOrder_AndCopiesWithLineBreaks()
    {
        Element[] leaves = new Element[3];
        await using Area area = await Area.StartAsync(() =>
            SelectionArea(Column(leaves[0] = Elements.Text("nnnn"), leaves[1] = Elements.Text("nnnn"), leaves[2] = Elements.Text("nnnn")))
        );

        area.Drag((1.4f * N, 10), (2.4f * N, (2 * Line) + 10));
        _ = area.App.Keyboard.Press(Key.C, KeyModifiers.Control);

        Assert.Equal([(1, 4), (0, 4), (0, 2)], area.Selections(leaves));
        Assert.Equal("nnn\nnnnn\nnn", area.App.Clipboard.Text);
    }

    /// <summary>
    /// A point on no leaf takes the end of the last leaf before it in the order of the tree: below a leaf, or on its line and to its
    /// right.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - An area around a column of two leaves of "nnnn", 20 px apart.
    ///
    /// Steps:
    /// - Press at x = 1.4 n on the first; move into the gap below it, left of the text; read; move to x = 200 on the second's line,
    ///   right of it; read.
    ///
    /// Expected:
    /// - In the gap: the first 1 to 4, the second none. Right of the second: the first 1 to 4, the second 0 to 4.
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task Drag_PointOnNoLeaf_TakesTheEndOfTheLeafBefore()
    {
        Element[] leaves = new Element[2];
        await using Area area = await Area.StartAsync(() =>
            SelectionArea(Column(leaves[0] = Elements.Text("nnnn"), leaves[1] = Elements.Text("nnnn")).Gap(20))
        );

        area.Press(1.4f * N, 10);
        area.MoveTo(2, Line + 10);
        List<(int, int)> gap = area.Selections(leaves);
        area.MoveTo(200, Line + 20 + 10);
        List<(int, int)> right = area.Selections(leaves);
        area.Release();

        Assert.Equal([(1, 4), (0, 0)], gap);
        Assert.Equal([(1, 4), (0, 4)], right);
    }

    /// <summary>
    /// The clicks of Flutter's Windows: a double click selects the word, a triple click the paragraph, a fourth stays the
    /// paragraph; a click puts the anchor and Shift+click extends from it.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - An area around a leaf of "nn nn\nnn nn" (two paragraphs).
    ///
    /// Steps:
    /// - Double click at x = 1 n on the first line; triple click there; click there a fourth time; click at x = 1.4 n, then
    ///   Shift+click at x = 22 on the second line (just past the space, which the test font makes 3.136 px wide: the second line's
    ///   second "nn" starts at 20.216 px).
    ///
    /// Expected:
    /// - 0 to 2; 0 to 5; 0 to 5; then 1 to 9.
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task Clicks_WordParagraphAndShiftClick_Select()
    {
        Element[] leaves = new Element[1];
        await using Area area = await Area.StartAsync(() => SelectionArea(leaves[0] = Elements.Text("nn nn\nnn nn")));
        var steps = new List<(int, int)>();

        area.Click(N, 10, clicks: 2);
        steps.Add(area.Selections(leaves)[0]);
        area.Click(N, 10, clicks: 3);
        steps.Add(area.Selections(leaves)[0]);
        area.Click(N, 10, clicks: 4);
        steps.Add(area.Selections(leaves)[0]);
        area.Click(1.4f * N, 10);
        area.Click(22, Line + 10, KeyModifiers.Shift);
        steps.Add(area.Selections(leaves)[0]);

        Assert.Equal([(0, 2), (0, 5), (0, 5), (1, 9)], steps);
    }

    /// <summary>
    /// A drag after a double click extends by whole words, from the word pressed.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - An area around a leaf of "nn nn nn".
    ///
    /// Steps:
    /// - Double click at x = 25 (in the second word, which starts at 20.216 px after the 3.136 px space) and hold; move to x = 45 (in
    ///   the third, from 40.432 px); move to x = 4 (in the first).
    ///
    /// Expected:
    /// - 3 to 8, then 0 to 5: the second word stays selected, and the selection takes the word the pointer is in.
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task Drag_AfterADoubleClick_ExtendsByWords()
    {
        Element[] leaves = new Element[1];
        await using Area area = await Area.StartAsync(() => SelectionArea(leaves[0] = Elements.Text("nn nn nn")));

        area.Press(25, 10, clicks: 2);
        area.MoveTo(45, 10);
        (int, int) forward = area.Selections(leaves)[0];
        area.MoveTo(4, 10);
        (int, int) back = area.Selections(leaves)[0];
        area.Release();

        Assert.Equal((3, 8), forward);
        Assert.Equal((0, 5), back);
    }

    /// <summary>
    /// Control+A selects every leaf of the area, and Control+C and Control+Insert copy; the three keys are stopped. Control+A in a
    /// text field inside the area is the field's.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - An area around a column of two leaves of "nn" and "nnn", with the focus (a click on the first).
    ///
    /// Steps:
    /// - Control+A; Control+Insert; read the clipboard; set it to "x"; Control+C.
    ///
    /// Expected:
    /// - The leaves are 0 to 2 and 0 to 3; the clipboard holds "nn\nnnn" after each copy; each key was stopped.
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task Keys_ControlASelectsAll_AndControlCOrInsertCopies()
    {
        Element[] leaves = new Element[2];
        await using Area area = await Area.StartAsync(() => SelectionArea(Column(leaves[0] = Elements.Text("nn"), leaves[1] = Elements.Text("nnn"))));
        area.Click(N, 10);

        bool all = area.App.Keyboard.Press(Key.A, KeyModifiers.Control);
        bool insert = area.App.Keyboard.Press(Key.Insert, KeyModifiers.Control);
        string? first = area.App.Clipboard.Text;
        area.App.Clipboard.Text = "x";
        bool copy = area.App.Keyboard.Press(Key.C, KeyModifiers.Control);

        Assert.Equal([(0, 2), (0, 3)], area.Selections(leaves));
        Assert.Equal(("nn\nnnn", "nn\nnnn"), (first, area.App.Clipboard.Text));
        Assert.Equal((true, true, true), (all, insert, copy));
    }

    /// <summary>
    /// The selection goes when the area loses the focus inside the window, and stays when the window goes inactive.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A row of an area around a leaf of "nnnn" and a focusable box.
    ///
    /// Steps:
    /// - Drag from x = 1 n to 3 n; make the window inactive; read; make it active; focus the box; read.
    ///
    /// Expected:
    /// - Inactive: 1 to 3 still; after the box took the focus: none.
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task Selection_GoesWithTheFocus_NotWithTheWindow()
    {
        Element[] leaves = new Element[1];
        Element box = default;
        await using Area area = await Area.StartAsync(() =>
            Row(SelectionArea(leaves[0] = Elements.Text("nnnn")), box = Box().Width(20).Height(20).Focusable())
        );

        area.Drag((N, 10), (3 * N, 10));
        area.App.SetActive(false);
        _ = area.App.PumpAndSettle();
        (int, int) inactive = area.Selections(leaves)[0];
        area.App.SetActive(true);
        Assert.True(area.App.Focus(box));
        _ = area.App.PumpAndSettle();

        Assert.Equal((1, 3), inactive);
        Assert.Equal((0, 0), area.Selections(leaves)[0]);
    }

    /// <summary>
    /// A copy leaves out the room of an inline object, and the drag does not select it.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - An area around a leaf of "nn", an inline box 30 wide, and "nn".
    ///
    /// Steps:
    /// - Click on the leaf; Control+A; Control+C.
    ///
    /// Expected:
    /// - The clipboard holds "nnnn".
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task Copy_InlineObject_IsLeftOut()
    {
        await using Area area = await Area.StartAsync(() =>
        {
            Element box = Box().Width(30).Height(10);
            return SelectionArea(
                Row(
                    Elements.Text(
                        (ref w) =>
                        {
                            w.Write("nn".AsSpan());
                            w.Inline(box);
                            w.Write("nn".AsSpan());
                        }
                    )
                )
            );
        });

        area.Click(N, 10);
        _ = area.App.Keyboard.Press(Key.A, KeyModifiers.Control);
        _ = area.App.Keyboard.Press(Key.C, KeyModifiers.Control);

        Assert.Equal("nnnn", area.App.Clipboard.Text);
    }

    /// <summary>
    /// A press on a link is the link's: it starts no selection, and its release clicks the link; a drag started elsewhere selects
    /// over the link.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - An area around a leaf of "nn" and "nn" in a link whose handler counts.
    ///
    /// Steps:
    /// - Click at x = 3 n (on the link); read; drag from x = 1 n to x = 4 n.
    ///
    /// Expected:
    /// - After the click: no selection and one click of the link; after the drag: 1 to 4, and still one click.
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task Link_PressIsTheLinks_AndADragSelectsOverIt()
    {
        Element[] leaves = new Element[1];
        int clicks = 0;
        PointerHandler click = (in PointerEvent e) => clicks++;
        await using Area area = await Area.StartAsync(() =>
            SelectionArea(
                leaves[0] = Elements.Text(
                    (ref w) =>
                    {
                        w.Write("nn".AsSpan());
                        using (w.Link(click))
                        {
                            w.Write("nn".AsSpan());
                        }
                    }
                )
            )
        );

        area.Click(3 * N, 10);
        ((int, int) Selection, int Clicks) pressed = (area.Selections(leaves)[0], clicks);
        area.Drag((N, 10), (4 * N, 10));

        Assert.Equal(((0, 0), 1), pressed);
        Assert.Equal(((1, 4), 1), (area.Selections(leaves)[0], clicks));
    }

    /// <summary>
    /// The text under <c>.Unselectable()</c> and a text field's text are left out of the selection and of the copy.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - An area around a column of "n", "nn" under <c>.Unselectable()</c>, a text field holding "nnn", and "nnnn".
    ///
    /// Steps:
    /// - Click on the first leaf; Control+A; Control+C.
    ///
    /// Expected:
    /// - The clipboard holds "n\nnnnn"; the unselectable leaf and the field's leaf have no selection.
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task Unselectable_AndATextField_AreLeftOut()
    {
        Element[] leaves = new Element[2];
        Element unselectable = default;
        Element field = default;
        await using Area area = await Area.StartAsync(() =>
            SelectionArea(
                Column(
                    leaves[0] = Elements.Text("n"),
                    unselectable = Elements.Text("nn").Unselectable(),
                    field = TextField(Signal.Create("nnn")).Width(100),
                    leaves[1] = Elements.Text("nnnn")
                )
            )
        );

        area.Click(0.5f * N, 10);
        _ = area.App.Keyboard.Press(Key.A, KeyModifiers.Control);
        _ = area.App.Keyboard.Press(Key.C, KeyModifiers.Control);
        NodeHandle fieldLeaf = area.App.Tree.Nodes.GetRef(area.App.Tree.Nodes.GetRef(field.Node).FirstChild).FirstChild;

        Assert.Equal("n\nnnnn", area.App.Clipboard.Text);
        Assert.Equal([(0, 1), (0, 4)], area.Selections(leaves));
        Assert.Equal((0, 0), area.SelectionOf(unselectable.Node));
        Assert.Equal((0, 0), area.SelectionOf(fieldLeaf));
    }

    /// <summary>
    /// A press that a focusable descendant takes the focus for, or that keeps the focus elsewhere, starts no selection.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - An area around a column of a leaf of "nnnn", a button of "nn", and a box of "nn" that keeps the focus on press.
    ///
    /// Steps:
    /// - Drag from x = 1 n to 3 n on the button's text; read; drag the same on the box's text; read.
    ///
    /// Expected:
    /// - No leaf has a selection after either drag.
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task PressablePart_StartsNoSelection()
    {
        Element[] leaves = new Element[3];
        await using Area area = await Area.StartAsync(() =>
            SelectionArea(
                Column(
                    leaves[0] = Elements.Text("nnnn"),
                    Button(leaves[1] = Elements.Text("nn"), () => { }),
                    Box(leaves[2] = Elements.Text("nn")).KeepsFocusOnPress()
                )
            )
        );

        area.Drag((N, Line + 10), (3 * N, Line + 10));
        List<(int, int)> button = area.Selections(leaves);
        area.Drag((N, (2 * Line) + 10), (3 * N, (2 * Line) + 10));

        Assert.Equal([(0, 0), (0, 0), (0, 0)], button);
        Assert.Equal([(0, 0), (0, 0), (0, 0)], area.Selections(leaves));
    }

    /// <summary>
    /// The cursor is the I-beam over a leaf the area selects, the arrow between leaves and over an unselectable leaf.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - An area around a column of a leaf of "nnnn" and an unselectable one, 20 px apart.
    ///
    /// Steps:
    /// - Move over the first leaf; into the gap; over the second.
    ///
    /// Expected:
    /// - I-beam, arrow, arrow.
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task Cursor_IBeamOverSelectableLeavesOnly()
    {
        await using Area area = await Area.StartAsync(() =>
            SelectionArea(Column(Elements.Text("nnnn"), Elements.Text("nnnn").Unselectable()).Gap(20))
        );
        var cursors = new List<CursorKind>();
        foreach (float y in new[] { 10f, Line + 10, Line + 20 + 10 })
        {
            area.MoveTo(N, y);
            cursors.Add(area.App.Cursor);
        }

        Assert.Equal([CursorKind.IBeam, CursorKind.Arrow, CursorKind.Arrow], cursors);
    }

    /// <summary>
    /// A drag past the edge of the scroll container around the area scrolls it on the frame clock, at 30 logical pixels a second
    /// for each pixel past the edge, while the pointer stays still; the release stops it.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A scroll view 40 px high around an area around a column of ten leaves of "nnnn".
    ///
    /// Steps:
    /// - Press at x = 1 n on the first leaf; move to y = 60 (20 px past the bottom); run 7 frames of 1/60 s on the clock; read the
    ///   offset and the selection's last leaf; release; run 7 frames; read the offset.
    ///
    /// Expected:
    /// - The first frame starts the scrolling, and each of the 6 after moves it 20 × 30 × 1/60 = 10 px: 60 px; the selection
    ///   reaches a leaf below the third; the offset does not move after the release.
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task Drag_PastTheScrollEdge_ScrollsOnTheClock()
    {
        Element[] leaves = new Element[10];
        Element view = default;
        await using Area area = await Area.StartAsync(() =>
        {
            for (int i = 0; i < leaves.Length; i++)
            {
                leaves[i] = Elements.Text("nnnn");
            }

            return view = ScrollView(SelectionArea(Column(leaves))).Height(40);
        });

        area.Press(N, 10);
        area.App.Pointer.Move((int)N, 60);
        Frames(area.App, 7);
        float scrolled = area.App.Tree.ScrollOffsetOf(view.Node).Y;
        int last = area.Selections(leaves).FindLastIndex(s => s.End > 0);
        area.Release();
        Frames(area.App, 7);

        Assert.Equal(60, scrolled, 2);
        Assert.True(last > 2, $"The selection's last leaf is {last}.");
        Assert.Equal(scrolled, area.App.Tree.ScrollOffsetOf(view.Node).Y);
    }

    /// <summary>
    /// The frames of a drag allocate nothing (design §11.3, §4.10 "Cost"), nor those of a press that starts a selection, once the
    /// area's list of leaves and the leaves' selections exist.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - An area around a column of three leaves of "nnnn", pressed on the first and dragged; warmed up with the iterations.
    ///
    /// Steps:
    /// - 1,000 iterations in the windows of <c>PerformanceAssert.DoesNotAllocate</c>: move to the third leaf or back to the second
    ///   by the iteration's number, and run the frame. Then 200 iterations of a release, a press on the first leaf, and the frame.
    ///
    /// Expected:
    /// - 0 bytes in every window of both.
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task SteadyState_DragAndPressFrames_AllocateNothing()
    {
        await using Area area = await Area.StartAsync(() =>
            SelectionArea(Column(Elements.Text("nnnn"), Elements.Text("nnnn"), Elements.Text("nnnn")))
        );
        area.Press(N, 10);
        void Drag(int index)
        {
            area.App.Pointer.Move((int)(2 * N), (int)(index % 2 == 0 ? (2 * Line) + 10 : Line + 10));
            _ = area.App.PumpAndSettle();
        }

        void Press(int index)
        {
            area.App.Pointer.Up();
            area.App.Pointer.Move((int)N, 10);
            area.App.Pointer.Down();
            _ = area.App.PumpAndSettle();
        }

        for (int i = 0; i < 20; i++)
        {
            Drag(i);
        }

        PerformanceAssert.DoesNotAllocate(1_000, Drag);
        for (int i = 0; i < 20; i++)
        {
            Press(i);
        }

        PerformanceAssert.DoesNotAllocate(200, Press);
        area.Release();
    }

    // Runs frames of 1/60 s on the virtual clock, each with the frame it asks for.
    private static void Frames(HeadlessApp app, int count)
    {
        for (int i = 0; i < count; i++)
        {
            app.Pump(TimeSpan.FromTicks(TimeSpan.TicksPerSecond / 60));
        }
    }

    // A headless app of a content at the window's top left, with the pointer's gestures each followed by the frames they ask for.
    private sealed class Area : IAsyncDisposable
    {
        private Area(HeadlessApp app) => App = app;

        public HeadlessApp App { get; }

        public static async Task<Area> StartAsync(Func<Element> content)
        {
            HeadlessApp app = await HeadlessApp.StartAsync(content, new HeadlessOptions { Width = 400, Height = 300 });
            _ = app.PumpAndSettle();
            return new Area(app);
        }

        public void Press(float x, float y, KeyModifiers modifiers = KeyModifiers.None, int clicks = 1)
        {
            App.Pointer.Move((int)MathF.Round(x), (int)MathF.Round(y), modifiers);
            App.Pointer.Down(modifiers: modifiers, clickCount: clicks);
            _ = App.PumpAndSettle();
        }

        public void MoveTo(float x, float y)
        {
            App.Pointer.Move((int)MathF.Round(x), (int)MathF.Round(y));
            _ = App.PumpAndSettle();
        }

        public void Release()
        {
            App.Pointer.Up();
            _ = App.PumpAndSettle();
        }

        public void Click(float x, float y, KeyModifiers modifiers = KeyModifiers.None, int clicks = 1)
        {
            Press(x, y, modifiers, clicks);
            App.Pointer.Up(modifiers: modifiers);
            _ = App.PumpAndSettle();
        }

        public void Drag((float X, float Y) from, (float X, float Y) to)
        {
            Press(from.X, from.Y);
            MoveTo(to.X, to.Y);
            Release();
        }

        // Each leaf's selection, from its start to its end; 0 to 0 when it has none.
        public List<(int Start, int End)> Selections(Element[] leaves) => leaves.Select(leaf => SelectionOf(leaf.Node)).ToList();

        public (int Start, int End) SelectionOf(NodeHandle leaf) =>
            App.Tree.Nodes.GetRef(leaf).Text!.Selection is { IsEmpty: false } selection ? (selection.Start, selection.End) : (0, 0);

        public async ValueTask DisposeAsync()
        {
            // A drag that a test left held keeps scrolling on the clock; the release ends it.
            App.Pointer.Up();
            _ = App.PumpAndSettle();
            await App.DisposeAsync();
        }
    }
}
