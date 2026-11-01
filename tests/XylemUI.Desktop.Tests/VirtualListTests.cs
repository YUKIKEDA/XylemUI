using System.Numerics;
using XylemUI.Testing.XUnit;
using static XylemUI.Elements;

namespace XylemUI.Desktop.Tests;

// Virtualized lists (row P02e, design §3.4, "The shape of P02e") in a headless app: the rows made, their lengths, the anchor of the
// scroll, the rows kept outside the range, and the cost of a scroll.
public sealed class VirtualListTests
{
    /// <summary>
    /// A list makes the rows of its viewport and of the cache extent before and after it, and no other; the number of rows and of
    /// nodes laid out does not grow with the number of items.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A box 200 x 300 that scrolls vertically, with a list of rows 20 high and the default cache extent (250).
    ///
    /// Steps:
    /// - Start it with 1,000, 10,000, and 100,000 items; read the box's rows and count the nodes laid out in a layout of the whole
    ///   tree; scroll the 10,000 to 5,000 and read the rows again.
    ///
    /// Expected:
    /// - At the top, rows 0 to 27 (the viewport's 15 and the cache's 12.5 after it), the same for each number of items, with the same
    ///   number of nodes laid out. At 5,000, rows 237 to 277: the cache's 12.5 before (from 4,750) to the cache's after (5,550).
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task Rows_AreTheViewportsAndTheCaches_WhateverTheCount()
    {
        var laidOut = new List<int>();
        var rows = new List<(int First, int Last, int Count)>();
        foreach (int count in new[] { 1_000, 10_000, 100_000 })
        {
            Signal<IReadOnlyList<int>> items = Items(0, count);
            var state = new VirtualListState();
            await using HeadlessApp app = await HeadlessApp.StartAsync(() => List(items, state));
            int runs = 0;
            app.Tree.LayoutRunHook = _ => runs++;
            Invalidation.MarkDirty(app.Tree, app.RootNode, DirtyFlags.Layout);
            _ = app.PumpAndSettle();
            laidOut.Add(runs);
            rows.Add(Rows(app));
            if (count == 10_000)
            {
                state.Scroll.ScrollTo(new Vector2(0, 5_000));
                _ = app.PumpAndSettle();
                rows.Add(Rows(app));
            }
        }

        Assert.Equal([(0, 27, 28), (0, 27, 28), (237, 277, 41), (0, 27, 28)], rows);
        Assert.Equal(laidOut[0], laidOut[1]);
        Assert.Equal(laidOut[0], laidOut[2]);
    }

    /// <summary>
    /// Items put before the anchor row leave it where it shows, with its node, its state, and the focus inside it: the position moves
    /// by the rows put in.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A list of 1,000 focusable rows 20 high, scrolled to 2,000, so that row 100 is at the top; row 105 focused.
    ///
    /// Steps:
    /// - Put 10 items before the first; settle.
    ///
    /// Expected:
    /// - Row 100 is still at the viewport's top, with the same node; row 105 has the same node and the focus; the position is 2,200.
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task ItemsPutAbove_LeaveTheAnchorRowInPlace_WithItsNodeAndFocus()
    {
        Signal<IReadOnlyList<int>> items = Items(0, 1_000);
        var state = new VirtualListState();
        await using HeadlessApp app = await HeadlessApp.StartAsync(() => List(items, state, focusable: true));
        _ = app.PumpAndSettle();
        state.Scroll.ScrollTo(new Vector2(0, 2_000));
        _ = app.PumpAndSettle();
        NodeHandle top = app.Find("row100").Node;
        NodeHandle focused = app.Find("row105").Node;
        Assert.True(app.Focus(app.Find("row105")));
        _ = app.PumpAndSettle();
        float before = ShownAt(app, state, "row100");

        items.Set([.. Enumerable.Range(-10, 10), .. Enumerable.Range(0, 1_000)]);
        _ = app.PumpAndSettle();

        Assert.Equal(0, before);
        Assert.Equal(0, ShownAt(app, state, "row100"));
        Assert.Equal(top, app.Find("row100").Node);
        Assert.Equal(focused, app.Find("row105").Node);
        Assert.Equal(focused, app.Tree.Focus.Focused);
        Assert.Equal(2_200, state.Scroll.Offset.Get().Y);
    }

    /// <summary>
    /// Rows longer than their estimate do not make the list jump when it scrolls up into them: the row at the top moves down by the
    /// amount scrolled, each time, while the rows above it are measured.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A list of 10,000 rows, each fifth 60 high and the others 20, with an estimated extent of 20; scrolled to 50,000.
    ///
    /// Steps:
    /// - Note the first row that shows and where it shows; ten times, scroll up by 30 and settle, and read where that row shows.
    ///
    /// Expected:
    /// - The row shows 30 lower each time: at 30, 60, ..., 300 from where it was.
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task RowsLongerThanTheirEstimate_DoNotJump_WhenScrolledUpInto()
    {
        Signal<IReadOnlyList<int>> items = Items(0, 10_000);
        var state = new VirtualListState();
        await using HeadlessApp app = await HeadlessApp.StartAsync(() =>
            Box()
                .Width(200)
                .Height(300)
                .Overflow(Overflow.Visible, Overflow.Scroll)
                .VirtualFor(
                    items,
                    static i => i,
                    static item => Box().Height(item.Get() % 5 == 0 ? 60 : 20).TestId($"row{item.Get()}"),
                    state,
                    new VirtualOptions { EstimatedExtent = 20 }
                )
        );
        _ = app.PumpAndSettle();
        state.Scroll.ScrollTo(new Vector2(0, 50_000));
        _ = app.PumpAndSettle();
        string row = $"row{state.VisibleRange.Get().Start}";
        float start = ShownAt(app, state, row);
        var moved = new List<float>();
        for (int i = 0; i < 10; i++)
        {
            state.Scroll.ScrollTo(state.Scroll.Offset.Get() - new Vector2(0, 30));
            _ = app.PumpAndSettle();
            moved.Add(ShownAt(app, state, row) - start);
        }

        Assert.Equal([30, 60, 90, 120, 150, 180, 210, 240, 270, 300], moved);
    }

    /// <summary>
    /// When the anchor row's item goes, the row now at its index takes the viewport's top, with no offset into it; the same when every
    /// item is replaced and no key stays.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A list of 1,000 rows 20 high, scrolled to 2,010 (row 100 at the top, 10 into it).
    ///
    /// Steps:
    /// - Take out item 100; settle; read the first row that shows, where it shows, and the position. Replace the items with 5,000 to
    ///   5,999; settle; read them again.
    ///
    /// Expected:
    /// - Row 101 at the top, at 0, with the position 2,000; then row 5,100 at the top, at 0, with the position 2,000.
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task AnchorRowGone_TheRowAtItsIndexTakesTheTop()
    {
        Signal<IReadOnlyList<int>> items = Items(0, 1_000);
        var state = new VirtualListState();
        await using HeadlessApp app = await HeadlessApp.StartAsync(() => List(items, state));
        _ = app.PumpAndSettle();
        state.Scroll.ScrollTo(new Vector2(0, 2_010));
        _ = app.PumpAndSettle();

        items.Set([.. Enumerable.Range(0, 100), .. Enumerable.Range(101, 899)]);
        _ = app.PumpAndSettle();
        (int, float, float) removed = (state.VisibleRange.Get().Start, ShownAt(app, state, "row101"), state.Scroll.Offset.Get().Y);
        items.Set([.. Enumerable.Range(5_000, 1_000)]);
        _ = app.PumpAndSettle();
        (int, float, float) replaced = (state.VisibleRange.Get().Start, ShownAt(app, state, "row5100"), state.Scroll.Offset.Get().Y);

        Assert.Equal((100, 0f, 2_000f), removed);
        Assert.Equal((100, 0f, 2_000f), replaced);
    }

    /// <summary>
    /// A row that leaves the range is disposed with its scope, unless it holds the focus or the pointer's capture, or its KeepAlive
    /// signal is true; a kept row is not laid out. When what held it lets go, the next frame disposes it.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A list of 1,000 clickable rows 20 high whose scopes record their disposal, focusable but row 2, which a press then leaves
    ///   the focus; row 3 kept by a signal that is true. Row 1 focused; the pointer pressed on row 2.
    ///
    /// Steps:
    /// - Scroll to 10,000 and settle; read which of rows 0 to 4 are alive and laid out. Set row 3's signal false, release the
    ///   pointer, and settle; read them again.
    ///
    /// Expected:
    /// - Rows 1, 2, and 3 alive and not laid out; rows 0 and 4 disposed. Then row 1 alone alive: rows 2 and 3 disposed.
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task RowsOutsideTheRange_AreDisposed_UnlessHeld()
    {
        Signal<IReadOnlyList<int>> items = Items(0, 1_000);
        var state = new VirtualListState();
        var keep = Signal.Create(true);
        var disposed = new HashSet<int>();
        await using HeadlessApp app = await HeadlessApp.StartAsync(() =>
            Box()
                .Width(200)
                .Height(300)
                .Overflow(Overflow.Visible, Overflow.Scroll)
                .VirtualFor(
                    items,
                    static i => i,
                    item =>
                    {
                        int i = item.Get();
                        Scope.OnCleanup(() => disposed.Add(i));
                        Element row = Box().Height(20).OnClick(() => { }).TestId($"row{i}");
                        row = i == 2 ? row : row.Focusable();
                        return i == 3 ? row.KeepAlive(keep) : row;
                    },
                    state
                )
        );
        _ = app.PumpAndSettle();
        Element[] first = [.. Enumerable.Range(0, 5).Select(i => app.Find($"row{i}"))];
        Assert.True(app.Focus(first[1]));
        (int x, int y) = app.CenterOf(first[2]);
        app.Pointer.Move(x, y);
        app.Pointer.Down();
        _ = app.PumpAndSettle();

        state.Scroll.ScrollTo(new Vector2(0, 10_000));
        _ = app.PumpAndSettle();
        string held = Held(app, first, disposed);
        keep.Set(false);
        app.Pointer.Up();
        _ = app.PumpAndSettle();
        string after = Held(app, first, disposed);

        Assert.Equal("0:gone 1:kept 2:kept 3:kept 4:gone", held);
        Assert.Equal("0:gone 1:kept 2:gone 3:gone 4:gone", after);
    }

    /// <summary>
    /// A list with a fixed extent gives each row that length, whatever its own, and places the rows by it with the gap and the
    /// padding; its scroll range is all the items'.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A box 200 x 300 with a padding of 10 and a gap of 5, holding a list of 1,000 rows that ask for 10 high, with a fixed extent of
    ///   30.
    ///
    /// Steps:
    /// - Settle; read rows 0 and 2 and the largest position.
    ///
    /// Expected:
    /// - Row 0 at (10, 10), 180 x 30; row 2 at (10, 80); the largest position 10 + 1,000 x 35 - 5 + 10 - 300 = 34,715.
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task FixedExtent_SetsEachRowsLength_WithTheGapAndPadding()
    {
        Signal<IReadOnlyList<int>> items = Items(0, 1_000);
        var state = new VirtualListState();
        await using HeadlessApp app = await HeadlessApp.StartAsync(() =>
            Column()
                .Width(200)
                .Height(300)
                .Padding(10)
                .Gap(5)
                .Overflow(Overflow.Visible, Overflow.Scroll)
                .VirtualFor(
                    items,
                    static i => i,
                    static item => Box().Height(10).TestId($"row{item.Get()}"),
                    state,
                    new VirtualOptions { FixedExtent = 30 }
                )
        );
        _ = app.PumpAndSettle();

        Assert.Equal(new Rect(10, 10, 180, 30), app.Tree.Nodes.GetRef(app.Find("row0").Node).Layout.Rect);
        Assert.Equal(new Rect(10, 80, 180, 30), app.Tree.Nodes.GetRef(app.Find("row2").Node).Layout.Rect);
        Assert.Equal(34_715, state.Scroll.MaxOffset.Get().Y);
    }

    /// <summary>
    /// A list in a box that scrolls horizontally lays its rows along the horizontal axis, each as high as the box.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A box 300 x 200 that scrolls horizontally, holding a list of 1,000 rows 50 wide.
    ///
    /// Steps:
    /// - Scroll to 1,000 across; settle; read the rows made and row 20's box.
    ///
    /// Expected:
    /// - Rows 15 to 31 (from 750 to 1,550); row 20 at (1,000, 0), 50 x 200.
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task HorizontalList_LaysItsRowsAlongTheX()
    {
        Signal<IReadOnlyList<int>> items = Items(0, 1_000);
        var state = new VirtualListState();
        await using HeadlessApp app = await HeadlessApp.StartAsync(() =>
            Box()
                .Width(300)
                .Height(200)
                .Overflow(Overflow.Scroll, Overflow.Visible)
                .VirtualFor(items, static i => i, static item => Box().Width(50).TestId($"row{item.Get()}"), state)
        );
        _ = app.PumpAndSettle();
        state.Scroll.ScrollTo(new Vector2(1_000, 0));
        _ = app.PumpAndSettle();

        Assert.Equal((15, 31, 17), Rows(app));
        Assert.Equal(new Rect(1_000, 0, 50, 200), app.Tree.Nodes.GetRef(app.Find("row20").Node).Layout.Rect);
    }

    /// <summary>
    /// A change of the items that gives a key twice is reported, and the list keeps the items before it.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A list of 3 rows 20 high; a handler of unhandled exceptions that records them.
    ///
    /// Steps:
    /// - Set the items to 1, 1, 2; settle.
    ///
    /// Expected:
    /// - One <see cref="InvalidOperationException"/> reported; the rows are still 0 to 2.
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task KeyGivenTwice_IsReported_AndTheItemsStay()
    {
        Signal<IReadOnlyList<int>> items = Items(0, 3);
        var reported = new List<Exception>();
        Action<Exception> previous = ReactiveRuntime.UnhandledExceptionHandler;
        ReactiveRuntime.UnhandledExceptionHandler = reported.Add;
        try
        {
            await using HeadlessApp app = await HeadlessApp.StartAsync(() => List(items, new VirtualListState()));
            _ = app.PumpAndSettle();
            items.Set([1, 1, 2]);
            _ = app.PumpAndSettle();

            Assert.IsType<InvalidOperationException>(Assert.Single(reported));
            Assert.Equal((0, 2, 3), Rows(app));
        }
        finally
        {
            ReactiveRuntime.UnhandledExceptionHandler = previous;
        }
    }

    /// <summary>
    /// A wheel's scroll whose rows stay the same, delivered and drawn, allocates nothing, and moves the rows that show (design §3.4,
    /// "The frame's stages").
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A list of 10,000 rows 20 high, scrolled to 1,000; warmed up with the iterations.
    ///
    /// Steps:
    /// - Wheel 4 px down over the list, settle, and read the rows that show; wheel 4 px up. Then 1,000 iterations in the windows of
    ///   <c>PerformanceAssert.DoesNotAllocate</c>: wheel 4 px down or up by the iteration's number, which keeps rows 37 to 77, and
    ///   settle.
    ///
    /// Expected:
    /// - Rows 50 to 65 show after the first wheel (from 1,004 to 1,304); 0 bytes in every window.
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task SteadyState_WheelKeepingTheRows_AllocatesNothing()
    {
        Signal<IReadOnlyList<int>> items = Items(0, 10_000);
        var state = new VirtualListState();
        await using HeadlessApp app = await HeadlessApp.StartAsync(() => List(items, state));
        _ = app.PumpAndSettle();
        state.Scroll.ScrollTo(new Vector2(0, 1_000));
        _ = app.PumpAndSettle();
        void Step(int index)
        {
            app.Input.Handle(
                new PointerInput(
                    PointerInputKind.Wheel,
                    100,
                    150,
                    PointerButton.None,
                    PointerButtons.None,
                    KeyModifiers.None,
                    0,
                    TimeSpan.Zero,
                    PointerType.Mouse,
                    new Vector2(0, index % 2 == 0 ? 4 : -4)
                )
            );
            _ = app.PumpAndSettle();
        }

        Step(0);
        IndexRange shown = state.VisibleRange.Get();
        Step(1);
        for (int i = 0; i < 20; i++)
        {
            Step(i);
        }

        Assert.Equal(new IndexRange(50, 66), shown);
        Assert.Equal((37, 77, 41), Rows(app));
        PerformanceAssert.DoesNotAllocate(1_000, Step);
    }

    private static Signal<IReadOnlyList<int>> Items(int start, int count) => Signal.Create<IReadOnlyList<int>>([.. Enumerable.Range(start, count)]);

    // A box 200 x 300 that scrolls vertically, with a row 20 high for each item, whose test ID is "row" and the item.
    private static Element List(Signal<IReadOnlyList<int>> items, VirtualListState state, bool focusable = false) =>
        Box()
            .Width(200)
            .Height(300)
            .Overflow(Overflow.Visible, Overflow.Scroll)
            .VirtualFor(
                items,
                static i => i,
                item =>
                {
                    Element row = Box().Height(20).TestId($"row{item.Get()}");
                    return focusable ? row.Focusable() : row;
                },
                state
            );

    // The first and last index of the rows the list's box holds, and their number.
    private static (int First, int Last, int Count) Rows(HeadlessApp app)
    {
        NodeHandle box = app.Tree.Nodes.GetRef(app.RootNode).FirstChild;
        while (app.Tree.VirtualHostOf(box) is null)
        {
            box = app.Tree.Nodes.GetRef(box).FirstChild;
        }

        IVirtualHost host = app.Tree.VirtualHostOf(box)!;
        var indices = new List<int>();
        for (NodeHandle row = app.Tree.Nodes.GetRef(box).FirstChild; !row.IsNone; row = app.Tree.Nodes.GetRef(row).NextSibling)
        {
            if (host.LaysOut(row, out int index))
            {
                indices.Add(index);
            }
        }

        return (indices.Min(), indices.Max(), indices.Count);
    }

    // Where a row shows along the list's axis, from the viewport's start.
    private static float ShownAt(HeadlessApp app, VirtualListState state, string row) =>
        app.Tree.Nodes.GetRef(app.Find(row).Node).Layout.Rect.Y - state.Scroll.Offset.Get().Y;

    // For each of the first rows: gone (disposed), kept (alive, not laid out), or laid out.
    private static string Held(HeadlessApp app, Element[] rows, HashSet<int> disposed) =>
        string.Join(
            ' ',
            rows.Select(
                (row, i) =>
                    disposed.Contains(i) ? $"{i}:gone"
                    : app.Tree.VirtualHostOf(app.Tree.Nodes.GetRef(row.Node).Parent)!.LaysOut(row.Node, out _) ? $"{i}:laid"
                    : $"{i}:kept"
            )
        );
}
