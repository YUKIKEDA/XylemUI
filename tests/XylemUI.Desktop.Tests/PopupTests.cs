using System.Numerics;
using XylemUI.Testing.XUnit;
using static XylemUI.Elements;

namespace XylemUI.Desktop.Tests;

// Popups over a headless window (row P02d, design §7.2 and §7.3): a render root of their own opened and closed by a signal, placed
// against the anchor, tested for the pointer before the window, closed by a press outside, Esc, the window's deactivation, and a
// scroll, with their focus scopes, the modal trap, the route of keys to the anchor, the mark that passes the pointer, nested popups,
// the motion, and the window's list kept when only a popup changes. The window is 800 × 600 at a scale of 1.
public sealed class PopupTests
{
    /// <summary>
    /// The signal opens the popup in a root of its own, placed below the anchor from its left edge, and closing it takes the root out
    /// of the tree and disposes its content's scope.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A column: a 40 px tall spacer, then a 100 × 30 anchor at (0, 40) with a popup of a 200 × 100 box, closed.
    ///
    /// Steps:
    /// - Open it and settle; read the popups and the content's box; close it and settle.
    ///
    /// Expected:
    /// - Open: one popup, whose root has no parent; the content's center at (100, 120), below the anchor (y 70 to 170). Closed: no
    ///   popup, the root and the content freed.
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task Popup_SignalOpensAndCloses_InARootOfItsOwn_BelowTheAnchor()
    {
        var open = Signal.Create(false);
        Element content = default;
        await using HeadlessApp app = await HeadlessApp.StartAsync(() =>
            Column(Box().Height(40), Box().Width(100).Height(30).Popup(open, () => content = Box().Width(200).Height(100)))
        );

        open.Set(true);
        _ = app.PumpAndSettle();
        IReadOnlyList<Element> popups = app.Popups;
        bool rootHasNoParent = app.Tree.Nodes.GetRef(popups[0].Node).Parent.IsNone;
        (int X, int Y) center = app.CenterOf(content);
        open.Set(false);
        _ = app.PumpAndSettle();

        Assert.Single(popups);
        Assert.True(rootHasNoParent);
        Assert.Equal((100, 120), center);
        Assert.Empty(app.Popups);
        Assert.False(app.Tree.Nodes.IsAlive(popups[0].Node));
        Assert.False(app.Tree.Nodes.IsAlive(content.Node));
    }

    /// <summary>
    /// A popup that does not fit below its anchor flips above it, and one that does not fit to the right slides into the window.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A column: a 540 px tall spacer, then a row of a 650 px spacer and a 100 × 30 anchor at (650, 540) with a popup of a
    ///   200 × 100 box.
    ///
    /// Steps:
    /// - Open the popup and settle; read the content's center.
    ///
    /// Expected:
    /// - Below would end at 670, past 600, and above fits: the content spans y 440 to 540. From x 650 it would end at 850, past
    ///   800, and the flip to the left of the anchor's right edge (550 to 750) fits, so it flips: the center is (650, 490).
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task Popup_NearTheBottomRight_FlipsUpAndLeft()
    {
        var open = Signal.Create(true);
        Element content = default;
        await using HeadlessApp app = await HeadlessApp.StartAsync(() =>
            Column(Box().Height(540), Row(Box().Width(650), Box().Width(100).Height(30).Popup(open, () => content = Box().Width(200).Height(100))))
        );
        _ = app.PumpAndSettle();

        Assert.Equal((650, 490), app.CenterOf(content));
    }

    /// <summary>
    /// The pointer tests the popup before the window: a click on the popup's content over a button of the window clicks the
    /// content, and a click outside the popup closes it and goes on to the window's node under it.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A window button covering 0 to 300 × 0 to 300 that counts clicks, with a 100 × 30 anchor at its top left whose popup is a
    ///   200 × 100 box that counts clicks.
    ///
    /// Steps:
    /// - Open; click the content's center (100, 80); click (250, 250), outside the popup.
    ///
    /// Expected:
    /// - The first click: the content 1, the window 0, the popup still open. The second: the popup closed, its signal false, and
    ///   the window's button clicked once.
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task Press_HitsThePopupFirst_AndOutsideClosesIt_AndReachesTheWindow()
    {
        var open = Signal.Create(true);
        int window = 0;
        int popup = 0;
        await using HeadlessApp app = await HeadlessApp.StartAsync(() =>
            Box(Box().Width(100).Height(30).Popup(open, () => Box().Width(200).Height(100).OnClick(() => popup++)))
                .Width(300)
                .Height(300)
                .OnClick(() => window++)
        );
        _ = app.PumpAndSettle();

        app.Pointer.Click(100, 80);
        (int, int, int) first = (popup, window, app.Popups.Count);
        app.Pointer.Click(250, 250);
        _ = app.PumpAndSettle();

        Assert.Equal((1, 0, 1), first);
        Assert.Empty(app.Popups);
        Assert.False(open.Get());
        Assert.Equal(1, window);
    }

    /// <summary>
    /// A popup that consumes outside presses closes on one, and the press stops there.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - As above, with <see cref="PopupOptions.ConsumeOutsidePress"/>.
    ///
    /// Steps:
    /// - Click (250, 250).
    ///
    /// Expected:
    /// - The popup is closed, and the window's button is not clicked.
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task Press_Outside_WithConsume_ClosesAndStops()
    {
        var open = Signal.Create(true);
        int window = 0;
        await using HeadlessApp app = await HeadlessApp.StartAsync(() =>
            Box(Box().Width(100).Height(30).Popup(open, () => Box().Width(200).Height(100), new PopupOptions { ConsumeOutsidePress = true }))
                .Width(300)
                .Height(300)
                .OnClick(() => window++)
        );
        _ = app.PumpAndSettle();

        app.Pointer.Click(250, 250);
        _ = app.PumpAndSettle();

        Assert.Empty(app.Popups);
        Assert.Equal(0, window);
    }

    /// <summary>
    /// A modal popup blocks the pointer behind it, keeps the focus in, goes round its own nodes on Tab, does not close on a press of
    /// its backdrop by default, closes on Esc, and gives the focus back to the node focused when it opened.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A window with a focusable 100 × 30 opener that counts clicks and a focusable 100 × 30 other node; the opener has a modal popup
    ///   filling the window, holding a column of two focusable 50 × 50 boxes.
    ///
    /// Steps:
    /// - Focus the opener; open and settle; focus the first box; click (400, 300), on the backdrop over the window; give the other
    ///   node the focus; press Tab twice; press Esc and settle.
    ///
    /// Expected:
    /// - The click reaches no window node and keeps the popup open; focusing the other node returns false; Tab moves to the second
    ///   box, then back to the first; Esc closes the popup, writes false, and the opener has the focus again.
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task Modal_BlocksAndTraps_TabGoesRound_EscClosesAndGivesTheFocusBack()
    {
        var open = Signal.Create(false);
        int clicks = 0;
        Element opener = default;
        Element other = default;
        Element first = default;
        Element second = default;
        await using HeadlessApp app = await HeadlessApp.StartAsync(() =>
            Column(
                opener = Box()
                    .Width(100)
                    .Height(30)
                    .Focusable()
                    .OnClick(() => clicks++)
                    .Popup(
                        open,
                        () => Column(first = Box().Width(50).Height(50).Focusable(), second = Box().Width(50).Height(50).Focusable()),
                        new PopupOptions { IsModal = true, Placement = PopupPlacement.Fill }
                    ),
                other = Box().Width(100).Height(30).Focusable().OnClick(() => clicks++)
            )
        );
        Assert.True(app.Focus(opener));
        open.Set(true);
        _ = app.PumpAndSettle();
        Assert.True(app.Focus(first));

        app.Pointer.Click(400, 300);
        int popupsAfterPress = app.Popups.Count;
        bool focusedOther = app.Focus(other);
        _ = app.Keyboard.Press(Key.Tab);
        bool onSecond = app.Tree.Focus.Focused == second.Node;
        _ = app.Keyboard.Press(Key.Tab);
        bool backOnFirst = app.Tree.Focus.Focused == first.Node;
        _ = app.Keyboard.Press(Key.Escape);
        _ = app.PumpAndSettle();

        Assert.Equal(0, clicks);
        Assert.Equal(1, popupsAfterPress);
        Assert.False(focusedOther);
        Assert.True(onSecond);
        Assert.True(backOnFirst);
        Assert.Empty(app.Popups);
        Assert.False(open.Get());
        Assert.Equal(opener.Node, app.Tree.Focus.Focused);
    }

    /// <summary>
    /// A key the popup's focused node does not stop goes on to the anchor and its ancestors; the pointer's events stop at the popup's
    /// root.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A window column whose key-down handler counts F5, and whose pointer-down handler counts presses; its 100 × 30 anchor has a
    ///   popup of a focusable 200 × 100 box.
    ///
    /// Steps:
    /// - Open; focus the box; press F5; press the box's center.
    ///
    /// Expected:
    /// - The column counts the key once and no press.
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task Keys_GoOnToTheAnchor_ThePointerStopsAtTheRoot()
    {
        var open = Signal.Create(true);
        int keys = 0;
        int presses = 0;
        Element box = default;
        await using HeadlessApp app = await HeadlessApp.StartAsync(() =>
            Column(Box().Width(100).Height(30).Popup(open, () => box = Box().Width(200).Height(100).Focusable()))
                .On(
                    KeyEventKind.Down,
                    EventPhase.Bubble,
                    (in KeyEvent e) =>
                    {
                        if (e.Key == Key.F5)
                        {
                            keys++;
                        }
                    }
                )
                .On(PointerEventKind.Down, EventPhase.Bubble, (in PointerEvent _) => presses++)
        );
        _ = app.PumpAndSettle();
        Assert.True(app.Focus(box));

        _ = app.Keyboard.Press(Key.F5);
        app.Pointer.Click(box);
        _ = app.PumpAndSettle();

        Assert.Equal(1, keys);
        Assert.Equal(0, presses);
    }

    /// <summary>
    /// A subtree marked to take no pointer writes no hit region, and the press reaches the node behind it.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A 300 × 300 box that counts clicks, holding a 100 × 100 box that counts clicks and takes no pointer.
    ///
    /// Steps:
    /// - Click (50, 50).
    ///
    /// Expected:
    /// - The outer box counts 1, the inner 0.
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task HitTestVisibleFalse_PassesThePointerBehind()
    {
        int outer = 0;
        int inner = 0;
        await using HeadlessApp app = await HeadlessApp.StartAsync(() =>
            Box(Box().Width(100).Height(100).OnClick(() => inner++).HitTestVisible(false)).Width(300).Height(300).OnClick(() => outer++)
        );
        _ = app.PumpAndSettle();

        app.Pointer.Click(50, 50);

        Assert.Equal((1, 0), (outer, inner));
    }

    /// <summary>
    /// A popup opened from a popup is its child: a press in the child does not close the parent, opening a sibling closes the
    /// other, and closing the parent closes its child.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A 100 × 30 anchor with a popup of a column of two 100 × 30 items, each with a popup of a 100 × 30 box to its right.
    ///
    /// Steps:
    /// - Open the parent and the first item's child; click the child's center; open the second item's child; close the parent.
    ///
    /// Expected:
    /// - After the click, two popups: the parent and the first child. Opening the second closes the first: two again, the first's
    ///   signal false. Closing the parent: none, and the second's signal false.
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task NestedPopups_PressInChildKeepsParent_SiblingClosesSibling_ParentClosesChild()
    {
        var parent = Signal.Create(true);
        var a = Signal.Create(false);
        var b = Signal.Create(false);
        Element childA = default;
        PopupOptions right = new() { Placement = PopupPlacement.Right };
        await using HeadlessApp app = await HeadlessApp.StartAsync(() =>
            Box()
                .Width(100)
                .Height(30)
                .Popup(
                    parent,
                    () =>
                        Column(
                            Box().Width(100).Height(30).Popup(a, () => childA = Box().Width(100).Height(30), right),
                            Box().Width(100).Height(30).Popup(b, () => Box().Width(100).Height(30), right)
                        )
                )
        );
        _ = app.PumpAndSettle();
        a.Set(true);
        _ = app.PumpAndSettle();

        app.Pointer.Click(childA);
        int afterPress = app.Popups.Count;
        b.Set(true);
        _ = app.PumpAndSettle();
        (int, bool) afterSibling = (app.Popups.Count, a.Get());
        parent.Set(false);
        _ = app.PumpAndSettle();

        Assert.Equal(2, afterPress);
        Assert.Equal((2, false), afterSibling);
        Assert.Empty(app.Popups);
        Assert.False(b.Get());
    }

    /// <summary>
    /// A popup whose signal is true when its anchor is made, in the content of a popup being made, opens once that popup has opened,
    /// as its child: the parent's opening does not close it as a sibling.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A 100 × 30 anchor with an open popup of a 100 × 30 box, which has an open popup of its own to its right; both signals true
    ///   from the start.
    ///
    /// Steps:
    /// - Settle; read the popups and the second's parent.
    ///
    /// Expected:
    /// - Two popups; the second's parent is the first; both signals true.
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task NestedPopup_OpenWhenMade_OpensAsTheChildOfItsPopup()
    {
        var outer = Signal.Create(true);
        var inner = Signal.Create(true);
        PopupOptions right = new() { Placement = PopupPlacement.Right };
        await using HeadlessApp app = await HeadlessApp.StartAsync(() =>
            Box().Width(100).Height(30).Popup(outer, () => Box().Width(100).Height(30).Popup(inner, () => Box().Width(100).Height(30), right))
        );
        _ = app.PumpAndSettle();

        IReadOnlyList<PopupHandle> open = app.Tree.Popups.Opened;
        Assert.Equal(2, open.Count);
        Assert.Same(open[0], open[1].Parent);
        Assert.Equal((true, true), (outer.Get(), inner.Get()));
    }

    /// <summary>
    /// A popup with a motion stays while it leaves, takes neither the pointer nor the focus meanwhile, and goes when the motion ends.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A 300 × 300 box that counts clicks, with a 100 × 30 anchor whose popup of a focusable 200 × 100 box that counts clicks fades
    ///   in 100 ms.
    ///
    /// Steps:
    /// - Open and settle; close it and pump 50 ms; give the content the focus; click the content's center (100, 80); settle.
    ///
    /// Expected:
    /// - Halfway out, the popup is still there, the content does not take the focus, and the click reaches the window's box, not the
    ///   content; after the settle, no popup.
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task Motion_LeavingPopup_StaysAndTakesNoPointer_UntilItEnds()
    {
        var open = Signal.Create(true);
        int window = 0;
        int popup = 0;
        Element content = default;
        var options = new PopupOptions { Motion = ShowMotion.Fade(TimeSpan.FromMilliseconds(100)) };
        await using HeadlessApp app = await HeadlessApp.StartAsync(() =>
            Box(Box().Width(100).Height(30).Popup(open, () => content = Box().Width(200).Height(100).Focusable().OnClick(() => popup++), options))
                .Width(300)
                .Height(300)
                .OnClick(() => window++)
        );
        _ = app.PumpAndSettle();

        open.Set(false);
        app.Pump(TimeSpan.FromMilliseconds(50));
        int leaving = app.Popups.Count;
        bool focused = app.Focus(content);
        app.Pointer.Click(100, 80);
        _ = app.PumpAndSettle();

        Assert.Equal(1, leaving);
        Assert.False(focused);
        Assert.Equal((1, 0), (window, popup));
        Assert.Empty(app.Popups);
    }

    /// <summary>
    /// A popup that is not transient sets the open state on its anchor while it is open, and clears it as it starts to close, before
    /// its motion ends; a transient popup sets none; an anchor with two popups open keeps it until the second starts to close.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - An anchor with a popup that leaves in 100 ms; a second anchor with a transient popup; a third anchor with a modal popup and a
    ///   popup that is not modal. All closed.
    ///
    /// Steps:
    /// - Open the first and settle; close it and pump 50 ms; settle. Open the second and settle. Close it; open the third's modal,
    ///   then its other popup, and settle; close the other popup and settle; close the modal and settle. Read the open state of
    ///   each anchor after each step.
    ///
    /// Expected:
    /// - The first: open, then not open while its popup is still leaving (one popup), then not open. The second: not open while its
    ///   popup is out. The third: open with both, still open with the modal alone, not open after it.
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task Open_IsOnTheAnchorWhileItsPopupIsOpen_AndGoesAsItStartsToClose()
    {
        var first = Signal.Create(false);
        var second = Signal.Create(false);
        var modal = Signal.Create(false);
        var other = Signal.Create(false);
        Element a = default;
        Element b = default;
        Element c = default;
        var leaving = new PopupOptions { Motion = ShowMotion.Fade(TimeSpan.FromMilliseconds(100)) };
        var transient = new PopupOptions { IsTransient = true };
        await using HeadlessApp app = await HeadlessApp.StartAsync(() =>
        {
            a = Box().Width(100).Height(30).Popup(first, () => Box().Width(50).Height(20), leaving);
            b = Box().Width(100).Height(30).Popup(second, () => Box().Width(50).Height(20), transient);
            c = Box().Width(100).Height(30).Popup(modal, () => Box().Width(50).Height(20).Focusable(), new PopupOptions { IsModal = true });
            _ = c.Popup(other, () => Box().Width(50).Height(20));
            return Column(a, b, c);
        });
        _ = app.PumpAndSettle();
        bool IsOpen(Element e) => (app.Tree.Nodes.GetRef(e.Node).Pseudo & PseudoState.Open) != 0;
        var steps = new List<(bool Open, int Popups)>();

        first.Set(true);
        _ = app.PumpAndSettle();
        steps.Add((IsOpen(a), app.Popups.Count));
        first.Set(false);
        app.Pump(TimeSpan.FromMilliseconds(50));
        steps.Add((IsOpen(a), app.Popups.Count));
        _ = app.PumpAndSettle();
        steps.Add((IsOpen(a), app.Popups.Count));

        second.Set(true);
        _ = app.PumpAndSettle();
        steps.Add((IsOpen(b), app.Popups.Count));
        second.Set(false);
        _ = app.PumpAndSettle();

        modal.Set(true);
        other.Set(true);
        _ = app.PumpAndSettle();
        steps.Add((IsOpen(c), app.Popups.Count));
        other.Set(false);
        _ = app.PumpAndSettle();
        steps.Add((IsOpen(c), app.Popups.Count));
        modal.Set(false);
        _ = app.PumpAndSettle();
        steps.Add((IsOpen(c), app.Popups.Count));

        Assert.Equal([(true, 1), (false, 1), (false, 0), (false, 1), (true, 2), (true, 1), (false, 0)], steps);
    }

    /// <summary>
    /// A popup closes when its anchor's part is disposed, when the window stops being active, and when a scroll container its anchor
    /// is in scrolls.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A Show of an anchor with a popup; a second anchor with a popup, inside a 200 px tall scroll container of 1,000 px of content.
    ///
    /// Steps:
    /// - Open the first and settle; hide the Show and settle. Open the second, settle, deactivate the window. Activate it, open the
    ///   second again, settle; scroll the container by a wheel over it and settle.
    ///
    /// Expected:
    /// - No popup after each, and the second popup's signal false after the deactivation and after the scroll.
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task Popup_ClosesWithItsAnchorsScope_OnDeactivation_AndOnScroll()
    {
        var shown = Signal.Create(true);
        var first = Signal.Create(true);
        var second = Signal.Create(false);
        Element scroller = default;
        await using HeadlessApp app = await HeadlessApp.StartAsync(() =>
        {
            Element inner = Column(Box().Width(100).Height(30).Popup(second, () => Box().Width(50).Height(50)), Box().Height(1_000));
            scroller = Box(inner).Height(200).Overflow(Overflow.Scroll, Overflow.Scroll);
            return Column(Show(shown, () => Box().Width(100).Height(30).Popup(first, () => Box().Width(50).Height(50))), scroller);
        });
        _ = app.PumpAndSettle();
        int withShow = app.Popups.Count;
        shown.Set(false);
        _ = app.PumpAndSettle();
        int afterHide = app.Popups.Count;

        app.SetActive(true);
        second.Set(true);
        _ = app.PumpAndSettle();
        app.SetActive(false);
        _ = app.PumpAndSettle();
        (int, bool) afterDeactivation = (app.Popups.Count, second.Get());

        app.SetActive(true);
        second.Set(true);
        _ = app.PumpAndSettle();
        _ = app.Tree.ScrollStateFor(scroller.Node).SetOffset(new Vector2(0, 40));
        _ = app.PumpAndSettle();

        Assert.Equal(1, withShow);
        Assert.Equal(0, afterHide);
        Assert.Equal((0, false), afterDeactivation);
        Assert.Empty(app.Popups);
        Assert.False(second.Get());
    }

    /// <summary>
    /// A change inside a popup alone lays out and draws the popup again, and neither lays out the window's tree nor builds its list.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A 100 × 30 anchor with an open popup of a box around a text; the box's background and the text follow signals, the text
    ///   at first "a".
    ///
    /// Steps:
    /// - Settle; read the window's builds, the presents, and the box's width; change the background and settle; change the text to
    ///   "abcdef" and settle.
    ///
    /// Expected:
    /// - After each change, one more present and the same number of builds of the window's list. After the text, the box is wider,
    ///   and the frame had no layout request in the window's tree: it would have built the window's list.
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task ChangeInsideThePopup_DrawsItsListAlone()
    {
        var open = Signal.Create(true);
        var paint = Signal.Create(new Paint(Color.FromRgb(0xff0000)));
        var label = Signal.Create("a");
        await using HeadlessApp app = await HeadlessApp.StartAsync(() =>
            Box().Width(100).Height(30).Popup(open, () => Box(Elements.Text(() => label.Get())).Background(paint))
        );
        _ = app.PumpAndSettle();
        int builds = app.Frames.MainBuilds;
        int presents = app.Frames.PresentCount;
        UiTree tree = app.Tree;
        NodeHandle box = tree.Nodes.GetRef(tree.Nodes.GetRef(app.Popups[0].Node).FirstChild).FirstChild;
        float before = tree.GetRect(box).Width;

        paint.Set(new Paint(Color.FromRgb(0x00ff00)));
        _ = app.PumpAndSettle();

        Assert.Equal(presents + 1, app.Frames.PresentCount);
        Assert.Equal(builds, app.Frames.MainBuilds);

        label.Set("abcdef");
        _ = app.PumpAndSettle();

        Assert.True(tree.GetRect(box).Width > before);
        Assert.Equal(presents + 2, app.Frames.PresentCount);
        Assert.Equal(builds, app.Frames.MainBuilds);
    }

    /// <summary>
    /// With a popup open, the frames of a hover that moves between its items and of Tab that moves the focus between them allocate
    /// nothing (design §7.3, "Checks").
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A 100 × 30 anchor with an open popup of a column of two focusable 200 × 30 items, each with a hovered and a focused look;
    ///   the first focused; warmed up with the iterations.
    ///
    /// Steps:
    /// - 1,000 iterations in the windows of <c>PerformanceAssert.DoesNotAllocate</c>: move the pointer over the first item or the
    ///   second by the iteration's number, press Tab, and settle.
    ///
    /// Expected:
    /// - 0 bytes in every window.
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task SteadyState_HoverAndTabInAPopup_AllocateNothing()
    {
        var open = Signal.Create(true);
        var look = new PaintStyle { Background = new Paint(Color.FromRgb(0x336699)) };
        Element first = default;
        await using HeadlessApp app = await HeadlessApp.StartAsync(() =>
            Box()
                .Width(100)
                .Height(30)
                .Popup(
                    open,
                    () =>
                        Column(
                            first = Box().Width(200).Height(30).Focusable().Hovered(look).Focused(look),
                            Box().Width(200).Height(30).Focusable().Hovered(look).Focused(look)
                        )
                )
        );
        _ = app.PumpAndSettle();
        Assert.True(app.Focus(first));
        void Step(int index)
        {
            app.Pointer.Move(100, index % 2 == 0 ? 45 : 75);
            _ = app.Keyboard.Press(Key.Tab);
            _ = app.PumpAndSettle();
        }

        for (int i = 0; i < 20; i++)
        {
            Step(i);
        }

        PerformanceAssert.DoesNotAllocate(1_000, Step);
    }

    /// <summary>
    /// A modal popup set to close on its backdrop closes on a press there and keeps open on a press of its content.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A 100 × 30 anchor with a modal popup filling the window, holding a 200 × 100 box that blocks the pointer, with
    ///   <see cref="PopupOptions.DismissOnOutsidePress"/> true.
    ///
    /// Steps:
    /// - Click the box's center (100, 50); click (400, 300), the backdrop.
    ///
    /// Expected:
    /// - Open after the first click; closed after the second, its signal false.
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task Modal_DismissOnBackdrop_ClosesOnTheBackdropOnly()
    {
        var open = Signal.Create(true);
        var options = new PopupOptions
        {
            IsModal = true,
            Placement = PopupPlacement.Fill,
            DismissOnOutsidePress = true,
        };
        await using HeadlessApp app = await HeadlessApp.StartAsync(() =>
            Box().Width(100).Height(30).Popup(open, () => Box(Box().Width(200).Height(100).BlocksPointer()), options)
        );
        _ = app.PumpAndSettle();

        app.Pointer.Click(100, 50);
        int afterContent = app.Popups.Count;
        app.Pointer.Click(400, 300);
        _ = app.PumpAndSettle();

        Assert.Equal(1, afterContent);
        Assert.Empty(app.Popups);
        Assert.False(open.Get());
    }

    /// <summary>
    /// A popup that closes gives the focus to the node the window's scope had focused last when the node focused at its opening is
    /// gone.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A window column of a Show of a focusable box A, a focusable box B, and a 100 × 30 anchor with a popup of a focusable box P.
    ///
    /// Steps:
    /// - Focus A; open; focus B, then P; hide the Show, which removes A; close the popup.
    ///
    /// Expected:
    /// - B has the focus.
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task Close_WhenTheOpeningFocusIsGone_GivesTheFocusToTheWindowsLastFocused()
    {
        var shown = Signal.Create(true);
        var open = Signal.Create(false);
        Element a = default;
        Element b = default;
        Element p = default;
        await using HeadlessApp app = await HeadlessApp.StartAsync(() =>
            Column(
                Show(shown, () => a = Box().Width(50).Height(30).Focusable()),
                b = Box().Width(50).Height(30).Focusable(),
                Box().Width(100).Height(30).Popup(open, () => p = Box().Width(50).Height(30).Focusable())
            )
        );
        Assert.True(app.Focus(a));
        open.Set(true);
        _ = app.PumpAndSettle();
        Assert.True(app.Focus(b));
        Assert.True(app.Focus(p));
        shown.Set(false);
        _ = app.PumpAndSettle();
        open.Set(false);
        _ = app.PumpAndSettle();

        Assert.Equal(b.Node, app.Tree.Focus.Focused);
    }

    /// <summary>
    /// A modal popup that opens while a node of the window holds the pointer's capture takes the capture away: the node gets a
    /// cancel.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A 100 × 30 anchor that counts cancels, with a modal popup filling the window.
    ///
    /// Steps:
    /// - Press the anchor and hold; open the popup and settle.
    ///
    /// Expected:
    /// - One cancel, and the window's input holds no capture.
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task Modal_Opening_TakesTheCaptureFromTheNodesBehind()
    {
        var open = Signal.Create(false);
        int cancels = 0;
        await using HeadlessApp app = await HeadlessApp.StartAsync(() =>
            Box()
                .Width(100)
                .Height(30)
                .On(PointerEventKind.Cancel, EventPhase.Bubble, (in PointerEvent _) => cancels++)
                .Popup(open, () => Box(), new PopupOptions { IsModal = true, Placement = PopupPlacement.Fill })
        );
        _ = app.PumpAndSettle();
        app.Pointer.Move(50, 15);
        app.Pointer.Down();

        open.Set(true);
        _ = app.PumpAndSettle();
        app.Pointer.Up();
        _ = app.PumpAndSettle();

        Assert.Equal(1, cancels);
        Assert.True(app.Input.Capture.IsNone);
    }

    /// <summary>
    /// A draw function's hit regions in a subtree that takes no pointer are dropped.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A 300 × 300 box that counts clicks, holding a 100 × 100 box that counts clicks, writes its own hit region from its draw
    ///   function, and takes no pointer.
    ///
    /// Steps:
    /// - Click (50, 50).
    ///
    /// Expected:
    /// - The outer box counts 1, the inner 0.
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task HitTestVisibleFalse_DropsTheDrawFunctionsRegions()
    {
        int outer = 0;
        int inner = 0;
        await using HeadlessApp app = await HeadlessApp.StartAsync(() =>
            Box(
                    Box()
                        .Width(100)
                        .Height(100)
                        .Draw((context, bounds) => context.HitRegion(bounds.X, bounds.Y, bounds.Width, bounds.Height, default))
                        .OnClick(() => inner++)
                        .HitTestVisible(false)
                )
                .Width(300)
                .Height(300)
                .OnClick(() => outer++)
        );
        _ = app.PumpAndSettle();

        app.Pointer.Click(50, 50);

        Assert.Equal((1, 0), (outer, inner));
    }
}
