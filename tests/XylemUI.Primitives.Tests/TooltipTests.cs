using System.Numerics;
using XylemUI.Testing;
using XylemUI.Testing.XUnit;
using static XylemUI.Elements;
using static XylemUI.Primitives.Primitive;

namespace XylemUI.Primitives.Tests;

// The tooltip (row P02d2, design §13.1, "Tooltip") over a headless window of 800 × 600: when it shows and hides on the virtual
// clock, where it goes, that one shows at a time, that it pushes no focus scope and closes no sibling popup, and the long press. The
// anchors are 100 × 30 boxes and the tooltips' content 80 × 20 boxes. Time moves with Pump, which fires the timers that fall due.
public sealed class TooltipTests
{
    private static readonly TimeSpan Ms = TimeSpan.FromMilliseconds(1);

    /// <summary>
    /// A tooltip shows 400 ms after the pointer came over its anchor, 20 px below the pointer, and the pointer's moves meanwhile
    /// carry where it shows.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A 100 × 30 anchor at (0, 0) with a tooltip.
    ///
    /// Steps:
    /// - Move the pointer to (20, 10), then to (50, 15); pump 399 ms; read the popups; pump 1 ms; read the popups and the content's
    ///   top left.
    ///
    /// Expected:
    /// - None at 399 ms; one at 400 ms, its content at (50, 35).
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task Tooltip_ShowsAfterTheDelay_BelowThePointer()
    {
        Element tip = default;
        await using HeadlessApp app = await HeadlessApp.StartAsync(() => Box().Width(100).Height(30).Tooltip(() => tip = Box().Width(80).Height(20)));
        _ = app.PumpAndSettle();

        app.Pointer.Move(20, 10);
        app.Pointer.Move(50, 15);
        app.Pump(399 * Ms);
        int early = app.Popups.Count;
        app.Pump(Ms);

        Assert.Equal(0, early);
        _ = Assert.Single(app.Popups);
        Assert.Equal(new Vector2(50, 35), TopLeft(app, tip));
    }

    /// <summary>
    /// A show delay of 0 shows the tooltip as the pointer comes over the anchor, with no time passing.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A 100 × 30 anchor at (0, 0) with a tooltip whose <see cref="TooltipOptions.ShowDelay"/> is 0.
    ///
    /// Steps:
    /// - Move the pointer to (50, 15); pump 0 ms.
    ///
    /// Expected:
    /// - One popup.
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task Tooltip_ShowDelayZero_ShowsAtOnce()
    {
        await using HeadlessApp app = await HeadlessApp.StartAsync(() =>
            Box().Width(100).Height(30).Tooltip(() => Box().Width(80).Height(20), new TooltipOptions { ShowDelay = TimeSpan.Zero })
        );
        _ = app.PumpAndSettle();

        app.Pointer.Move(50, 15);
        app.Pump(TimeSpan.Zero);

        _ = Assert.Single(app.Popups);
    }

    /// <summary>
    /// A tooltip hides 100 ms after the pointer left both its anchor and the tooltip; the pointer can move into the tooltip, and a
    /// tooltip does not hide with time.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A 100 × 30 anchor at (0, 0) with a tooltip, shown with the pointer at (50, 15) (its content at (50, 35) to (130, 55)).
    ///
    /// Steps:
    /// - Pump 10 s. Move to (60, 40), into the tooltip, and pump 200 ms. Move to (300, 300), away from both; pump 99 ms; pump 1 ms.
    ///
    /// Expected:
    /// - Shown after 10 s and inside it; shown 99 ms after leaving; hidden at 100 ms.
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task Tooltip_HidesAfterThePointerLeftBoth_AndTheTooltipTakesThePointer()
    {
        await using HeadlessApp app = await HeadlessApp.StartAsync(() => Box().Width(100).Height(30).Tooltip(() => Box().Width(80).Height(20)));
        await Shown(app, 50, 15);
        var seen = new List<int>();

        app.Pump(TimeSpan.FromSeconds(10));
        seen.Add(app.Popups.Count);
        app.Pointer.Move(60, 40);
        app.Pump(200 * Ms);
        seen.Add(app.Popups.Count);
        app.Pointer.Move(300, 300);
        app.Pump(99 * Ms);
        seen.Add(app.Popups.Count);
        app.Pump(Ms);
        seen.Add(app.Popups.Count);

        Assert.Equal([1, 1, 1, 0], seen);
    }

    /// <summary>
    /// A press, a wheel over the anchor, and Esc each hide a tooltip at once.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A 100 × 30 anchor at (0, 0) with a tooltip.
    ///
    /// Steps:
    /// - Three times, show it with the pointer at (50, 15); then press at (50, 15); a wheel at (50, 15); Esc. Pump 0 ms after each.
    ///
    /// Expected:
    /// - No popup after each.
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task Tooltip_HidesOnAPress_AWheel_AndEsc()
    {
        await using HeadlessApp app = await HeadlessApp.StartAsync(() => Box().Width(100).Height(30).Tooltip(() => Box().Width(80).Height(20)));
        var after = new List<int>();

        foreach (int way in new[] { 0, 1, 2 })
        {
            // Away first, past the 100 ms after a close that would show the next one at once.
            app.Pointer.Move(300, 300);
            app.Pump(200 * Ms);
            await Shown(app, 50, 15);
            if (way == 0)
            {
                app.Pointer.Down();
                app.Pointer.Up();
            }
            else if (way == 1)
            {
                Send(app, PointerInputKind.Wheel, 50, 15, PointerType.Mouse);
            }
            else
            {
                _ = app.Keyboard.Press(Key.Escape);
            }

            app.Pump(TimeSpan.Zero);
            after.Add(app.Popups.Count);
        }

        Assert.Equal([0, 0, 0], after);
    }

    /// <summary>
    /// One tooltip shows at a time: moving from a shown tooltip's anchor to another anchor shows the other at once, and an anchor
    /// entered within 100 ms of a close shows at once too; after that, the full delay.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A row of two 100 × 30 anchors, A at (0, 0) and B at (100, 0), each with a tooltip.
    ///
    /// Steps:
    /// - Show A's with the pointer at (50, 15). Move to (150, 15) over B; pump 0 ms. Move to (500, 500); pump 100 ms (B's hides);
    ///   move to (50, 15) within 100 ms; pump 0 ms. Move to (500, 500); pump 300 ms; move to (150, 15); pump 0 ms; pump 400 ms.
    ///
    /// Expected:
    /// - Over B: one popup, B's. Back over A within 100 ms: one, A's. After 300 ms away: none at once, B's after 400 ms.
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task Tooltips_OneAtATime_AndTheNextShowsAtOnce()
    {
        Element a = default;
        Element b = default;
        await using HeadlessApp app = await HeadlessApp.StartAsync(() =>
            Row(
                Box().Width(100).Height(30).Tooltip(() => a = Box().Width(80).Height(20).TestId("a")),
                Box().Width(100).Height(30).Tooltip(() => b = Box().Width(80).Height(20).TestId("b"))
            )
        );
        await Shown(app, 50, 15);
        var seen = new List<string>();
        string Shown_() => string.Join(",", app.FindAll("a").Count > 0 ? "a" : string.Empty, app.FindAll("b").Count > 0 ? "b" : string.Empty);

        app.Pointer.Move(150, 15);
        app.Pump(TimeSpan.Zero);
        seen.Add(Shown_());
        app.Pointer.Move(500, 500);
        app.Pump(100 * Ms);
        app.Pointer.Move(50, 15);
        app.Pump(TimeSpan.Zero);
        seen.Add(Shown_());
        app.Pointer.Move(500, 500);
        app.Pump(300 * Ms);
        app.Pointer.Move(150, 15);
        app.Pump(TimeSpan.Zero);
        seen.Add(Shown_());
        app.Pump(400 * Ms);
        seen.Add(Shown_());

        Assert.Equal([",b", "a,", ",", ",b"], seen);
    }

    /// <summary>
    /// A placement given puts the tooltip against its anchor, not the pointer.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A 100 × 30 anchor at (0, 0) with a tooltip placed <see cref="PopupPlacement.Below"/>.
    ///
    /// Steps:
    /// - Show it with the pointer at (50, 15); read the content's top left.
    ///
    /// Expected:
    /// - (0, 30).
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task Tooltip_WithAPlacement_GoesAgainstTheAnchor()
    {
        Element tip = default;
        await using HeadlessApp app = await HeadlessApp.StartAsync(() =>
            Box().Width(100).Height(30).Tooltip(() => tip = Box().Width(80).Height(20), new TooltipOptions { Placement = PopupPlacement.Below })
        );
        await Shown(app, 50, 15);

        Assert.Equal(new Vector2(0, 30), TopLeft(app, tip));
    }

    /// <summary>
    /// A shown tooltip pushes no focus scope: Tab goes on in the window. It closes no sibling popup when it shows, and a popup that
    /// opens closes it.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A column: a focusable 100 × 30 box, focused, with a tooltip and a closed menu of one item; and a focusable 100 × 30 box below.
    ///   The menu's item has a tooltip and a closed submenu of one item.
    ///
    /// Steps:
    /// - Show the first box's tooltip; press Tab and read the focus. Open the menu (its item focused) and read the popups. Open the
    ///   submenu with Right; show the item's tooltip with the pointer over the item; read the popups.
    ///
    /// Expected:
    /// - Tab focuses the second box. Opening the menu closes the tooltip: one popup. The item's tooltip keeps the submenu: three popups
    ///   (the menu, the submenu, the tooltip).
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task Tooltip_PushesNoScope_ClosesNoSibling_AndAnOpeningPopupClosesIt()
    {
        var open = Signal.Create(false);
        Element first = default;
        Element second = default;
        await using HeadlessApp app = await HeadlessApp.StartAsync(() =>
            Column(
                first = Box()
                    .Width(100)
                    .Height(30)
                    .Focusable()
                    .Tooltip(() => Box().Width(80).Height(20))
                    .Menu(
                        open,
                        () =>
                            SubmenuItem(Box().Width(200).Height(30), () => MenuItem(Box().Width(200).Height(30), () => { }))
                                .Tooltip(() => Box().Width(80).Height(20))
                    ),
                second = Box().Width(100).Height(30).Focusable()
            )
        );
        _ = app.Focus(first);
        await Shown(app, 50, 15);

        _ = app.Keyboard.Press(Key.Tab);
        bool tabbed = app.Tree.Focus.Focused == second.Node;
        _ = app.Focus(first);
        open.Set(true);
        _ = app.PumpAndSettle();
        int afterMenu = app.Popups.Count;
        _ = app.Keyboard.Press(Key.Right);
        _ = app.PumpAndSettle();

        // The menu's item is at (0, 30) to (200, 60); the pointer stays over it past the delay.
        app.Pointer.Move(100, 45);
        app.Pump(400 * Ms);
        app.Pump(TimeSpan.Zero);

        Assert.True(tabbed);
        Assert.Equal(1, afterMenu);
        Assert.Equal(3, app.Popups.Count);
    }

    /// <summary>
    /// A touch's long press shows the tooltip at once after 500 ms, and the release hides it; a touch that moves past 18 px shows
    /// none.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A 100 × 30 anchor at (0, 0) with a tooltip.
    ///
    /// Steps:
    /// - A touch down at (50, 15); pump 499 ms; pump 1 ms; read the popups; release; pump 0 ms. A touch down at (10, 15); move to
    ///   (40, 15); pump 600 ms; release.
    ///
    /// Expected:
    /// - None at 499 ms, one at 500 ms, none after the release. None for the touch that moved.
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task Tooltip_LongPress_ShowsUntilTheRelease()
    {
        await using HeadlessApp app = await HeadlessApp.StartAsync(() => Box().Width(100).Height(30).Tooltip(() => Box().Width(80).Height(20)));
        _ = app.PumpAndSettle();
        var seen = new List<int>();

        Send(app, PointerInputKind.Down, 50, 15, PointerType.Touch);
        app.Pump(499 * Ms);
        seen.Add(app.Popups.Count);
        app.Pump(Ms);
        seen.Add(app.Popups.Count);
        Send(app, PointerInputKind.Up, 50, 15, PointerType.Touch);
        app.Pump(TimeSpan.Zero);
        seen.Add(app.Popups.Count);
        Send(app, PointerInputKind.Down, 10, 15, PointerType.Touch);
        Send(app, PointerInputKind.Move, 40, 15, PointerType.Touch);
        app.Pump(600 * Ms);
        seen.Add(app.Popups.Count);
        Send(app, PointerInputKind.Up, 40, 15, PointerType.Touch);
        _ = app.PumpAndSettle();

        Assert.Equal([0, 1, 0, 0], seen);
    }

    /// <summary>
    /// A touch's press on a shown tooltip hides it, and starts no long press that would show it again.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A 100 × 30 anchor at (0, 0) with a tooltip, shown with the mouse at (50, 15) (its content at (50, 35) to (130, 55)).
    ///
    /// Steps:
    /// - A touch down at (60, 40), on the tooltip; pump 0 ms; pump 600 ms; release.
    ///
    /// Expected:
    /// - No popup after the down, nor 600 ms later.
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task Tooltip_TouchPressOnIt_HidesIt_AndShowsItNoMore()
    {
        await using HeadlessApp app = await HeadlessApp.StartAsync(() => Box().Width(100).Height(30).Tooltip(() => Box().Width(80).Height(20)));
        await Shown(app, 50, 15);
        var seen = new List<int>();

        Send(app, PointerInputKind.Down, 60, 40, PointerType.Touch);
        app.Pump(TimeSpan.Zero);
        seen.Add(app.Popups.Count);
        app.Pump(600 * Ms);
        seen.Add(app.Popups.Count);
        Send(app, PointerInputKind.Up, 60, 40, PointerType.Touch);
        _ = app.PumpAndSettle();

        Assert.Equal([0, 0], seen);
    }

    /// <summary>
    /// A disabled anchor shows no tooltip.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A disabled 100 × 30 anchor at (0, 0) with a tooltip.
    ///
    /// Steps:
    /// - Move the pointer to (50, 15); pump 1 s.
    ///
    /// Expected:
    /// - No popup.
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task Tooltip_OnADisabledAnchor_DoesNotShow()
    {
        await using HeadlessApp app = await HeadlessApp.StartAsync(() =>
            Box().Width(100).Height(30).Disabled().Tooltip(() => Box().Width(80).Height(20))
        );
        _ = app.PumpAndSettle();

        app.Pointer.Move(50, 15);
        app.Pump(TimeSpan.FromSeconds(1));

        Assert.Empty(app.Popups);
    }

    // Moves the pointer to a point over an anchor and waits the delay, so that its tooltip shows.
    private static Task Shown(HeadlessApp app, int x, int y)
    {
        _ = app.PumpAndSettle();
        app.Pointer.Move(x, y);
        app.Pump(400 * Ms);
        app.Pump(TimeSpan.Zero);
        Assert.Single(app.Popups);
        return Task.CompletedTask;
    }

    // An input of a kind and a pointer type the headless pointer does not send (a touch, a wheel), at a point.
    private static void Send(HeadlessApp app, PointerInputKind kind, int x, int y, PointerType type)
    {
        // A touch holds the left button from its down to its up.
        bool held = type == PointerType.Touch && kind is PointerInputKind.Down or PointerInputKind.Move;
        app.Input.Handle(
            new PointerInput(
                kind,
                x,
                y,
                kind is PointerInputKind.Down or PointerInputKind.Up ? PointerButton.Left : PointerButton.None,
                held ? PointerButtons.Left : PointerButtons.None,
                KeyModifiers.None,
                kind == PointerInputKind.Down ? 1 : 0,
                app.Time.Elapsed,
                type,
                Delta: kind == PointerInputKind.Wheel ? new Vector2(0, 50) : default
            )
        );
    }

    private static Vector2 TopLeft(HeadlessApp app, Element element)
    {
        UiTree tree = app.Tree;
        Rect rect = tree.Nodes.GetRef(element.Node).Layout.Rect;
        var point = new Vector2(rect.X, rect.Y);
        for (NodeHandle parent = tree.Nodes.GetRef(element.Node).Parent; !parent.IsNone; parent = tree.Nodes.GetRef(parent).Parent)
        {
            Rect box = tree.Nodes.GetRef(parent).Layout.Rect;
            point += new Vector2(box.X, box.Y) - tree.ScrollOffsetOf(parent);
        }

        return point;
    }
}
