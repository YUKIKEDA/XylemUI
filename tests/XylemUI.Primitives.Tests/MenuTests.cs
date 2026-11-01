using XylemUI.Testing;
using XylemUI.Testing.XUnit;
using static XylemUI.Elements;
using static XylemUI.Primitives.Primitive;

namespace XylemUI.Primitives.Tests;

// The menus (row P02d, design §13.1, "Menu and Dialog"): a menu opened from a node, its items, its keys, its submenus, and the
// context menu, over a headless window of 800 × 600. The items are boxes of 200 × 30 in a column, so that no test needs text.
public sealed class MenuTests
{
    /// <summary>
    /// A menu opens below its anchor and focuses its first enabled item.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A 100 × 30 anchor at (0, 0) with a closed menu of three items, the first disabled.
    ///
    /// Steps:
    /// - Open the menu and settle.
    ///
    /// Expected:
    /// - One popup; the first item's center at (100, 45), below the anchor; the second item focused.
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task Menu_Opens_BelowTheAnchor_FocusingTheFirstEnabledItem()
    {
        var open = Signal.Create(false);
        var items = new Element[3];
        await using HeadlessApp app = await HeadlessApp.StartAsync(() => Box().Width(100).Height(30).Menu(open, () => Panel(items, disabled: 0)));

        open.Set(true);
        _ = app.PumpAndSettle();

        _ = Assert.Single(app.Popups);
        Assert.Equal((100, 45), app.CenterOf(items[0]));
        Assert.Equal(items[1].Node, app.Tree.Focus.Focused);
    }

    /// <summary>
    /// The keys of the table of design §13.1: Up and Down move to the item before and after, round at the ends, past the disabled
    /// items; Home and End move to the first and the last.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - An open menu of four items, the second disabled; the first focused.
    ///
    /// Steps:
    /// - Down, Down, Down, Up, End, Home; read the focused item after each.
    ///
    /// Expected:
    /// - The third, the fourth, the first, the fourth, the fourth, the first.
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task MenuKeys_MoveRoundTheEnabledItems()
    {
        var open = Signal.Create(true);
        var items = new Element[4];
        await using HeadlessApp app = await HeadlessApp.StartAsync(() => Box().Width(100).Height(30).Menu(open, () => Panel(items, disabled: 1)));
        _ = app.PumpAndSettle();
        var focused = new List<NodeHandle>();

        foreach (Key key in new[] { Key.Down, Key.Down, Key.Down, Key.Up, Key.End, Key.Home })
        {
            _ = app.Keyboard.Press(key);
            _ = app.PumpAndSettle();
            focused.Add(app.Tree.Focus.Focused);
        }

        Assert.Equal([items[2].Node, items[3].Node, items[0].Node, items[3].Node, items[3].Node, items[0].Node], focused);
    }

    /// <summary>
    /// Enter, Space, and a press choose an item: the menu closes and gives the focus back to the anchor, and then the action runs.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A focusable 100 × 30 anchor, focused, with a menu of two items whose actions record the focused node and the popups open.
    ///
    /// Steps:
    /// - Three times: open the menu and settle; choose the first item with Enter, then Space, then a press of its center; settle.
    ///
    /// Expected:
    /// - Each action ran once, with the anchor focused and no popup open; the signal is false after each.
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task MenuItem_Chosen_ClosesTheMenu_ThenRuns()
    {
        var open = Signal.Create(false);
        Element anchor = default;
        Element first = default;
        HeadlessApp? running = null;
        var seen = new List<(NodeHandle Focused, int Popups)>();
        await using HeadlessApp app = await HeadlessApp.StartAsync(() =>
            anchor = Box()
                .Width(100)
                .Height(30)
                .Focusable()
                .Menu(
                    open,
                    () =>
                        Column(
                            first = MenuItem(Box().Width(200).Height(30), () => seen.Add((running!.Tree.Focus.Focused, running.Popups.Count))),
                            MenuItem(Box().Width(200).Height(30), () => { })
                        )
                )
        );
        running = app;
        _ = app.Focus(anchor);
        var opened = new List<bool>();

        for (int way = 0; way < 3; way++)
        {
            open.Set(true);
            _ = app.PumpAndSettle();
            if (way == 2)
            {
                app.Pointer.Click(first);
            }
            else
            {
                _ = app.Keyboard.Press(way == 0 ? Key.Enter : Key.Space);
            }

            _ = app.PumpAndSettle();
            opened.Add(open.Get());
        }

        Assert.Equal([(anchor.Node, 0), (anchor.Node, 0), (anchor.Node, 0)], seen);
        Assert.Equal([false, false, false], opened);
    }

    /// <summary>
    /// A disabled item is not chosen by a press, and the menu stays open; Esc then closes it and gives the focus back.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A focusable anchor, focused, with an open menu of two items, the first disabled, whose actions count.
    ///
    /// Steps:
    /// - Press the first item's center and settle; press Esc and settle.
    ///
    /// Expected:
    /// - After the press: no action ran, one popup. After Esc: no popup, the signal false, the anchor focused.
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task MenuItem_Disabled_IsNotChosen_AndEscCloses()
    {
        var open = Signal.Create(false);
        Element anchor = default;
        Element first = default;
        int chosen = 0;
        await using HeadlessApp app = await HeadlessApp.StartAsync(() =>
            anchor = Box()
                .Width(100)
                .Height(30)
                .Focusable()
                .Menu(
                    open,
                    () =>
                        Column(
                            first = MenuItem(Box().Width(200).Height(30), () => chosen++, enabled: false),
                            MenuItem(Box().Width(200).Height(30), () => chosen++)
                        )
                )
        );
        _ = app.Focus(anchor);
        open.Set(true);
        _ = app.PumpAndSettle();

        app.Pointer.Click(first);
        _ = app.PumpAndSettle();
        int afterPress = app.Popups.Count;
        _ = app.Keyboard.Press(Key.Escape);
        _ = app.PumpAndSettle();

        Assert.Equal(0, chosen);
        Assert.Equal(1, afterPress);
        Assert.Empty(app.Popups);
        Assert.False(open.Get());
        Assert.Equal(anchor.Node, app.Tree.Focus.Focused);
    }

    /// <summary>
    /// The keys of a submenu: Right opens it to the right of its item and focuses its first item; Left closes it and gives the focus
    /// back to the item; Enter opens it again, and Esc closes the submenu alone.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - An open menu at (0, 30) of a submenu item over an item; the submenu holds two items. The submenu item is focused.
    ///
    /// Steps:
    /// - Right; Left; Enter; Esc; settle after each, and read the popups and the focused node.
    ///
    /// Expected:
    /// - Right: two popups, the submenu's first item centered at (300, 45), focused. Left: one popup, the submenu item focused. Enter:
    ///   two popups, the submenu's first item focused. Esc: one popup, the submenu item focused.
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task SubmenuKeys_RightOpensAndFocuses_LeftAndEscCloseItAlone()
    {
        var open = Signal.Create(true);
        Element submenu = default;
        var inner = new Element[2];
        await using HeadlessApp app = await HeadlessApp.StartAsync(() =>
            Box()
                .Width(100)
                .Height(30)
                .Menu(
                    open,
                    () =>
                        Column(
                            submenu = SubmenuItem(Box().Width(200).Height(30), () => Panel(inner, disabled: -1)),
                            MenuItem(Box().Width(200).Height(30), () => { })
                        )
                )
        );
        _ = app.PumpAndSettle();

        // The submenu makes its items each time it opens: the focused node is named against the items open then.
        var seen = new List<(int Popups, string Focused)>();
        (int X, int Y) innerCenter = default;

        foreach (Key key in new[] { Key.Right, Key.Left, Key.Enter, Key.Escape })
        {
            _ = app.Keyboard.Press(key);
            _ = app.PumpAndSettle();
            NodeHandle focused = app.Tree.Focus.Focused;
            seen.Add(
                (
                    app.Popups.Count,
                    focused == submenu.Node ? "submenu item"
                    : focused == inner[0].Node ? "first inner"
                    : "other"
                )
            );
            if (key == Key.Right)
            {
                innerCenter = app.CenterOf(inner[0]);
            }
        }

        Assert.Equal((300, 45), innerCenter);
        Assert.Equal([(2, "first inner"), (1, "submenu item"), (2, "first inner"), (1, "submenu item")], seen);
    }

    /// <summary>
    /// The pointer opens a submenu when it comes over its item, without moving the focus into it; it closes when the pointer comes
    /// over another item of the menu; an item chosen in it closes both menus.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A focusable anchor, focused, with an open menu at (0, 30) of a submenu item over an item; the submenu holds two items, whose
    ///   first counts.
    ///
    /// Steps:
    /// - Move the pointer over the submenu item; then over the item below it; then back over the submenu item, and press the
    ///   submenu's first item. Settle after each.
    ///
    /// Expected:
    /// - Over the submenu item: two popups, the submenu item focused. Over the item below: one popup, that item focused. After the
    ///   press: no popup, the action ran once, the anchor focused.
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task Submenu_PointerOpensIt_ASiblingClosesIt_AChoiceClosesBoth()
    {
        var open = Signal.Create(false);
        Element anchor = default;
        Element submenu = default;
        Element below = default;
        Element innerFirst = default;
        int chosen = 0;
        await using HeadlessApp app = await HeadlessApp.StartAsync(() =>
            anchor = Box()
                .Width(100)
                .Height(30)
                .Focusable()
                .Menu(
                    open,
                    () =>
                        Column(
                            submenu = SubmenuItem(
                                Box().Width(200).Height(30),
                                () =>
                                    Column(
                                        innerFirst = MenuItem(Box().Width(200).Height(30), () => chosen++),
                                        MenuItem(Box().Width(200).Height(30), () => { })
                                    )
                            ),
                            below = MenuItem(Box().Width(200).Height(30), () => { })
                        )
                )
        );
        _ = app.Focus(anchor);
        open.Set(true);
        _ = app.PumpAndSettle();

        app.Pointer.Move(100, 45);
        _ = app.PumpAndSettle();
        (int Popups, NodeHandle Focused) over = (app.Popups.Count, app.Tree.Focus.Focused);
        app.Pointer.Move(100, 75);
        _ = app.PumpAndSettle();
        (int Popups, NodeHandle Focused) sibling = (app.Popups.Count, app.Tree.Focus.Focused);
        app.Pointer.Move(100, 45);
        _ = app.PumpAndSettle();
        app.Pointer.Click(innerFirst);
        _ = app.PumpAndSettle();

        Assert.Equal((2, submenu.Node), over);
        Assert.Equal((1, below.Node), sibling);
        Assert.Empty(app.Popups);
        Assert.Equal(1, chosen);
        Assert.Equal(anchor.Node, app.Tree.Focus.Focused);
    }

    /// <summary>
    /// A context menu opens at the pointer on the right button's up over its node, and below the node on Shift+F10 and on the up of
    /// the application key while the node has the focus.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A column: a 100 px spacer, then a focusable 300 × 50 target at (0, 100) with a context menu of one item.
    ///
    /// Steps:
    /// - Click the right button at (50, 110), settle, and read the item's center; Esc. Focus the target; press Shift+F10, settle, read
    ///   the center; Esc. Press the application key, settle, read the center.
    ///
    /// Expected:
    /// - At the pointer: (150, 125). Below the target: (100, 165), twice.
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task ContextMenu_OpensAtThePointer_AndBelowTheNodeFromTheKeys()
    {
        Element target = default;
        Element item = default;
        await using HeadlessApp app = await HeadlessApp.StartAsync(() =>
            Column(
                Box().Height(100),
                target = Box().Width(300).Height(50).Focusable().ContextMenu(() => item = MenuItem(Box().Width(200).Height(30), () => { }))
            )
        );
        var centers = new List<(int X, int Y)>();

        app.Pointer.Click(50, 110, PointerButton.Right);
        _ = app.PumpAndSettle();
        centers.Add(app.CenterOf(item));
        _ = app.Keyboard.Press(Key.Escape);
        _ = app.Focus(target);
        _ = app.Keyboard.Press(Key.F10, KeyModifiers.Shift);
        _ = app.PumpAndSettle();
        centers.Add(app.CenterOf(item));
        _ = app.Keyboard.Press(Key.Escape);
        _ = app.Keyboard.Press(Key.Apps);
        _ = app.PumpAndSettle();
        centers.Add(app.CenterOf(item));

        Assert.Equal([(150, 125), (100, 165), (100, 165)], centers);
    }

    /// <summary>
    /// While a menu is open, a hover that moves between its items and the keys that move the focus between them neither lay out the
    /// window's tree nor build its display list (design §7.3, "Checks").
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - An open menu of three items, each with a hovered and a focused look; settled.
    ///
    /// Steps:
    /// - Read the window's builds; move the pointer over the second item and the third; press Down and Up; settle after each.
    ///
    /// Expected:
    /// - The window's builds are the same; the focus moved (the third item after the moves, then the first and the third).
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task MenuMoves_DoNotBuildTheWindowsList()
    {
        var open = Signal.Create(true);
        var items = new Element[3];
        await using HeadlessApp app = await HeadlessApp.StartAsync(() =>
            Box().Width(100).Height(30).Menu(open, () => Panel(items, disabled: -1, looks: true))
        );
        _ = app.PumpAndSettle();
        int builds = app.Frames.MainBuilds;
        var focused = new List<NodeHandle>();

        app.Pointer.Move(100, 75);
        _ = app.PumpAndSettle();
        app.Pointer.Move(100, 105);
        _ = app.PumpAndSettle();
        focused.Add(app.Tree.Focus.Focused);
        foreach (Key key in new[] { Key.Down, Key.Up })
        {
            _ = app.Keyboard.Press(key);
            _ = app.PumpAndSettle();
            focused.Add(app.Tree.Focus.Focused);
        }

        Assert.Equal(builds, app.Frames.MainBuilds);
        Assert.Equal([items[2].Node, items[0].Node, items[2].Node], focused);
    }

    /// <summary>
    /// With a menu open, the frames of a hover that moves between its items and of the keys that move the focus between them allocate
    /// nothing (design §7.3, "Checks").
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - An open menu of three items, each with a hovered and a focused look; warmed up with the iterations.
    ///
    /// Steps:
    /// - 1,000 iterations in the windows of <c>PerformanceAssert.DoesNotAllocate</c>: move the pointer over the first item or the
    ///   second by the iteration's number, press Down, and settle.
    ///
    /// Expected:
    /// - No allocation.
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task SteadyState_HoverAndKeysInAMenu_AllocateNothing()
    {
        var open = Signal.Create(true);
        var items = new Element[3];
        await using HeadlessApp app = await HeadlessApp.StartAsync(() =>
            Box().Width(100).Height(30).Menu(open, () => Panel(items, disabled: -1, looks: true))
        );
        _ = app.PumpAndSettle();

        void Step(int i)
        {
            app.Pointer.Move(100, 45 + (30 * (i & 1)));
            _ = app.Keyboard.Press(Key.Down);
            _ = app.PumpAndSettle();
        }

        for (int i = 0; i < 100; i++)
        {
            Step(i);
        }

        _ = PerformanceAssert.DoesNotAllocate(1_000, Step);
    }

    // A column of menu items of 200 × 30, the one at disabled not enabled; with looks, each has a hovered and a focused background.
    internal static Element Panel(Element[] items, int disabled, bool looks = false)
    {
        for (int i = 0; i < items.Length; i++)
        {
            Element box = Box().Width(200).Height(30);
            Element item = MenuItem(box, () => { }, enabled: i != disabled);
            if (looks)
            {
                item = item.Hovered(new PaintStyle { Background = new Paint(Color.FromRgb(0xdddddd)) })
                    .Focused(new PaintStyle { Background = new Paint(Color.FromRgb(0xbbbbbb)) });
            }

            items[i] = item;
        }

        return Column(items);
    }
}
