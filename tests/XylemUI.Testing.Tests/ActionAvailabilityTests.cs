using XylemUI.Testing.XUnit;
using static XylemUI.Elements;

namespace XylemUI.Testing.Tests;

// Whether an action would be handled now, the keys of an action with values, and reading and dispatching from a node that is
// not the focused one (row KM2c, design §7, "Menu items that name actions").
public sealed class ActionAvailabilityTests
{
    /// <summary>
    /// A scrolling action can be dispatched while a scroll container on the route would move, and the computation that asked
    /// runs again when the container's position changes; asked by its type alone, the tree's action always can.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A scroll view of 100 x 100 with a state, around a column of 400 that scrolls down only; a row of it has the focus.
    /// - Three computations made in the root function read <c>CanDispatch</c> of a page down, a page up, and a line left.
    ///
    /// Steps:
    /// - Read the three, and <c>CanDispatch</c> of each of the eight scrolling actions. Dispatch <c>scroll::ToEnd</c> and read
    ///   them all again. Read <c>CanDispatch&lt;ScrollActions.LineLeft&gt;()</c>.
    ///
    /// Expected:
    /// - At the start: down yes, up no, left no; of the eight, the line down, the page down, and the end. At the end: down no,
    ///   up yes, left no; of the eight, the line up, the page up, and the start. By the type alone: yes.
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task CanDispatch_AScrollingAction_IsWhetherAContainerWouldMove()
    {
        var state = new ScrollState();
        Element row = default;
        Computed<bool>? down = null;
        Computed<bool>? up = null;
        Computed<bool>? left = null;
        await using HeadlessApp app = await HeadlessApp.StartAsync(() =>
        {
            row = Box().Width(100).Height(20).Focusable();
            Element list = Column(row, Box().Width(100).Height(380));
            down = Computed.Create(() => row.CanDispatch(new ScrollActions.PageDown()));
            up = Computed.Create(() => row.CanDispatch(new ScrollActions.PageUp()));
            left = Computed.Create(() => row.CanDispatch(new ScrollActions.LineLeft()));
            return ScrollView(list, state).Width(100).Height(100);
        });
        _ = app.PumpAndSettle();
        Assert.True(app.Focus(row));
        _ = app.PumpAndSettle();

        IAction[] all =
        [
            new ScrollActions.LineUp(),
            new ScrollActions.LineDown(),
            new ScrollActions.LineLeft(),
            new ScrollActions.LineRight(),
            new ScrollActions.PageUp(),
            new ScrollActions.PageDown(),
            new ScrollActions.ToStart(),
            new ScrollActions.ToEnd(),
        ];
        (bool, bool, bool) atStart = (down!.Get(), up!.Get(), left!.Get());
        bool[] allAtStart = [.. all.Select(action => row.CanDispatch(action))];
        Assert.True(row.Dispatch(new ScrollActions.ToEnd()));
        _ = app.PumpAndSettle();
        (bool, bool, bool) atEnd = (down.Get(), up.Get(), left.Get());
        bool[] allAtEnd = [.. all.Select(action => row.CanDispatch(action))];

        Assert.Equal((true, false, false), atStart);
        Assert.Equal((false, true, false), atEnd);
        Assert.Equal([false, true, false, false, false, true, false, true], allAtStart);
        Assert.Equal([true, false, false, false, true, false, true, false], allAtEnd);
        Assert.True(row.CanDispatch<ScrollActions.LineLeft>());
        _ = app.PumpAndSettle();
    }

    /// <summary>
    /// A scroll container with no state answers from its boxes, and the computation that asked runs again when the container
    /// gets its state the first time it scrolls.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A column of 400 in a box of 100 x 100 whose height scrolls, with no scroll state; a row of the column has the focus.
    /// - A computation made in the root function reads <c>CanDispatch</c> of <c>scroll::ToEnd</c>.
    ///
    /// Steps:
    /// - Read it. Dispatch <c>scroll::ToEnd</c> and read it again.
    ///
    /// Expected:
    /// - Yes, then no.
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task CanDispatch_AContainerWithNoState_IsReadAgainWhenItFirstScrolls()
    {
        Element row = default;
        Computed<bool>? toEnd = null;
        await using HeadlessApp app = await HeadlessApp.StartAsync(() =>
        {
            row = Box().Width(100).Height(20).Focusable();
            toEnd = Computed.Create(() => row.CanDispatch(new ScrollActions.ToEnd()));
            return Box(Column(row, Box().Width(100).Height(380))).Width(100).Height(100).Overflow(Overflow.Visible, Overflow.Scroll);
        });
        _ = app.PumpAndSettle();
        Assert.True(app.Focus(row));
        _ = app.PumpAndSettle();

        bool before = toEnd!.Get();
        Assert.True(row.Dispatch(new ScrollActions.ToEnd()));
        _ = app.PumpAndSettle();

        Assert.True(before);
        Assert.False(toEnd.Get());
        _ = app.PumpAndSettle();
    }

    /// <summary>
    /// The computation that asked whether a scrolling action can be dispatched runs again when the container's range changes.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A scroll view of 100 x 100 with a state, around a column of a focused row of 20 and a box of 60: the content fits. A
    ///   computation made in the root function reads <c>CanDispatch</c> of <c>scroll::LineDown</c>. No text, so that the test
    ///   runs where the shaping library is not built.
    ///
    /// Steps:
    /// - Read it. Give the box the height 380 in its style, which changes no link of the tree and no handler, settle, and read
    ///   it again.
    ///
    /// Expected:
    /// - No while the content fits; yes when it is higher than the view.
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task CanDispatch_AScrollingAction_IsReadAgainWhenTheRangeChanges()
    {
        var state = new ScrollState();
        Element row = default;
        Element rest = default;
        Computed<bool>? down = null;
        await using HeadlessApp app = await HeadlessApp.StartAsync(() =>
        {
            row = Box().Width(100).Height(20).Focusable();
            down = Computed.Create(() => row.CanDispatch(new ScrollActions.LineDown()));
            rest = Box().Width(100).Height(60);
            return ScrollView(Column(row, rest), state).Width(100).Height(100);
        });
        _ = app.PumpAndSettle();
        Assert.True(app.Focus(row));
        _ = app.PumpAndSettle();

        bool fits = down!.Get();
        UiTree tree = app.Tree;
        tree.SetStyle(rest.Node, tree.Nodes.GetRef(rest.Node).Layout.Style with { Height = Length.Px(380) });
        _ = app.PumpAndSettle();

        Assert.False(fits);
        Assert.True(down.Get());
        _ = app.PumpAndSettle();
    }

    /// <summary>
    /// A move of the focus can be dispatched while a node of the top focus scope can take the focus, and <c>popup::Dismiss</c>
    /// while a popup is open; the computations that asked run again when that changes.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - Two focusable boxes, both disabled by one signal, and a popup of one of them, closed. No node has the focus.
    /// - Three computations made in the root function read <c>CanDispatch</c> of <c>focus::Next</c>, <c>focus::Previous</c>, and
    ///   <c>popup::Dismiss</c>.
    ///
    /// Steps:
    /// - Read the three. Enable the boxes and read the two moves. Open the popup, whose content takes no focus and which fades
    ///   out over 100 ms, settle, and read the dismissal and the move forward; close it, read the dismissal after 50 ms, settle,
    ///   and read both again.
    ///
    /// Expected:
    /// - No, no, no. Yes and yes with the boxes enabled. With the popup open: the dismissal yes, and the move no, since the
    ///   popup's scope has no node to move to. While the popup leaves, it is still there and the dismissal is no. After it
    ///   closed: the dismissal no, and the move yes.
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task CanDispatch_AMoveOfTheFocusAndADismissal_AreWhetherTheyWouldChangeSomething()
    {
        var disabled = Signal.Create(true);
        var open = Signal.Create(false);
        Element first = default;
        Computed<bool>? next = null;
        Computed<bool>? previous = null;
        Computed<bool>? dismiss = null;
        await using HeadlessApp app = await HeadlessApp.StartAsync(() =>
        {
            first = Box().Width(20).Height(20).Focusable().Disabled(disabled);
            Element second = Box().Width(20).Height(20).Focusable().Disabled(disabled);
            _ = first.Popup(
                open,
                static () => Box().Width(30).Height(30),
                new PopupOptions { Motion = ShowMotion.Fade(TimeSpan.FromMilliseconds(100)) }
            );
            next = Computed.Create(() => first.CanDispatch(new FocusActions.Next()));
            previous = Computed.Create(() => first.CanDispatch(new FocusActions.Previous()));
            dismiss = Computed.Create(() => first.CanDispatch(new PopupActions.Dismiss()));
            return Column(first, second);
        });
        _ = app.PumpAndSettle();

        (bool, bool, bool) none = (next!.Get(), previous!.Get(), dismiss!.Get());
        disabled.Set(false);
        _ = app.PumpAndSettle();
        (bool, bool) enabled = (next.Get(), previous.Get());
        open.Set(true);
        _ = app.PumpAndSettle();
        (bool, bool) whileOpen = (dismiss.Get(), next.Get());
        open.Set(false);
        app.Pump(TimeSpan.FromMilliseconds(50));
        (int, bool) leaving = (app.Popups.Count, dismiss.Get());
        _ = app.PumpAndSettle();

        Assert.Equal((false, false, false), none);
        Assert.Equal((1, false), leaving);
        Assert.Equal((true, true), enabled);
        Assert.Equal((true, false), whileOpen);
        Assert.Equal((false, true), (dismiss.Get(), next.Get()));
        _ = app.PumpAndSettle();
    }

    /// <summary>
    /// The computation that asked whether the focus can move runs again when a node stops or starts taking the focus: it is made
    /// not focusable, or hidden.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - One focusable box in a column. No node has the focus. A computation made in the root function reads <c>CanDispatch</c>
    ///   of <c>focus::Next</c>.
    ///
    /// Steps:
    /// - Read it. Make the box not focusable, read, and make it focusable again. Hide it, read, and show it again. Read after
    ///   each undoing too.
    ///
    /// Expected:
    /// - Yes at the start and after each undoing; no while the box is not focusable or hidden.
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task CanDispatch_AMoveOfTheFocus_IsReadAgainWhenANodeStopsTakingTheFocus()
    {
        Element box = default;
        Computed<bool>? next = null;
        await using HeadlessApp app = await HeadlessApp.StartAsync(() =>
        {
            box = Box().Width(20).Height(20).Focusable();
            next = Computed.Create(() => box.CanDispatch(new FocusActions.Next()));
            return Column(box);
        });
        _ = app.PumpAndSettle();
        UiTree tree = app.Tree;
        var seen = new List<bool> { next!.Get() };
        void Step(Action change)
        {
            change();
            _ = app.PumpAndSettle();
            seen.Add(next.Get());
        }

        Step(() => tree.SetFocusable(box.Node, false));
        Step(() => tree.SetFocusable(box.Node, true));
        Step(() => tree.SetStyle(box.Node, tree.Nodes.GetRef(box.Node).Layout.Style with { Hidden = true }));
        Step(() => tree.SetStyle(box.Node, tree.Nodes.GetRef(box.Node).Layout.Style with { Hidden = false }));

        Assert.Equal([true, false, true, false, true], seen);
    }

    /// <summary>
    /// The computation that asked whether the focus can move runs again when a node that takes the focus is put in the tree,
    /// also one that was made focusable long before.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A column of a box that takes no focus. Two focusable boxes made in the root function and kept, with no parent. A
    ///   computation made in the root function reads <c>CanDispatch</c> of <c>focus::Next</c>.
    ///
    /// Steps:
    /// - Settle and read it. Append the first kept box to the column, and read. Insert the second kept box after the column's
    ///   first child, and read: the first kept box was made not focusable before, so only the second can take the focus.
    ///
    /// Expected:
    /// - No with no focusable node in the tree; yes after the first box is appended; yes after the second is inserted, with the
    ///   first not focusable any more (a read in between says no).
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task CanDispatch_AMoveOfTheFocus_IsReadAgainWhenAFocusableNodeIsPutInTheTree()
    {
        Element plain = default;
        Element column = default;
        Element appended = default;
        Element inserted = default;
        Computed<bool>? next = null;
        await using HeadlessApp app = await HeadlessApp.StartAsync(() =>
        {
            plain = Box().Width(20).Height(20);
            appended = Box().Width(20).Height(20).Focusable();
            inserted = Box().Width(20).Height(20).Focusable();
            next = Computed.Create(() => plain.CanDispatch(new FocusActions.Next()));
            return column = Column(plain);
        });
        _ = app.PumpAndSettle();
        UiTree tree = app.Tree;

        bool none = next!.Get();
        tree.AppendChild(column.Node, appended.Node);
        bool afterAppend = next.Get();
        tree.SetFocusable(appended.Node, false);
        bool between = next.Get();
        tree.InsertAfter(column.Node, plain.Node, inserted.Node);
        bool afterInsert = next.Get();
        _ = app.PumpAndSettle();

        Assert.False(none);
        Assert.True(afterAppend);
        Assert.False(between);
        Assert.True(afterInsert);
    }

    /// <summary>
    /// A read at a node that is given is not made again when the focus moves, and a read at the focused node is.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A box with one handler of <c>Shape</c> and its derived types, whose answer counts how often it is asked and is yes, and
    ///   two focusable boxes beside it; the first has the focus. Two computations made in the root function read
    ///   <c>CanDispatch</c> of a <c>Circle</c>: one from the box with the handler, one from the focused node; an effect reads both.
    ///
    /// Steps:
    /// - Settle and read the count. Focus the second box, settle, and read the count.
    ///
    /// Expected:
    /// - The count is not 0 after the start: the read from the box asked; the read from the focused node finds no handler to ask.
    ///   It is the same after the focus moved, and the read from the focused node ran once more.
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task ReadAtANode_IsNotMadeAgain_WhenTheFocusMoves()
    {
        int asked = 0;
        int ran = 0;
        Element first = default;
        Element second = default;
        await using HeadlessApp app = await HeadlessApp.StartAsync(() =>
        {
            Element side = Box()
                .Width(10)
                .Height(10)
                .OnActionsOf(
                    (in ActionEvent<Shape> _) => { },
                    _ =>
                    {
                        asked++;
                        return true;
                    }
                );
            first = Box().Width(10).Height(10).Focusable();
            second = Box().Width(10).Height(10).Focusable();
            Computed<bool> fromSide = Computed.Create(() => first.CanDispatch(new Circle(), side));
            Computed<bool> fromFocus = Computed.Create(() =>
            {
                ran++;
                return first.CanDispatch(new Circle());
            });
            _ = Effect.Create(() => _ = fromSide.Get() | fromFocus.Get());
            return Column(side, first, second);
        });
        _ = app.PumpAndSettle();
        Assert.True(app.Focus(first));
        _ = app.PumpAndSettle();

        (int, int) before = (asked, ran);
        Assert.True(app.Focus(second));
        _ = app.PumpAndSettle();

        Assert.NotEqual(0, before.Item1);
        Assert.Equal((before.Item1, before.Item2 + 1), (asked, ran));
    }

    /// <summary>
    /// The keys of a named action with arguments are those of a row whose action has the same arguments, compared by the code the
    /// source generator wrote for the type, a record or a class; an action of a type with no name, and a read by the type alone,
    /// find a row of the type.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A focused box. Rows: <c>ctrl-1</c> and <c>ctrl-2</c> for the record <c>test::Zoom</c> with the level 1 and 2;
    ///   <c>ctrl-4</c> and <c>ctrl-5</c> for the class <c>test::Grow</c> by 1 with the unit "px" and "em"; <c>ctrl-3</c> for
    ///   <c>Pick(1)</c>, a type with no name.
    ///
    /// Steps:
    /// - Read <c>KeyBindingFor</c> of the zoom with the level 1, 2, and 3; of the grow by 1 in "em", by 1 in "px", and by 2; of
    ///   <c>Pick(9)</c>; and <c>KeyBindingFor&lt;AvailabilityZoom&gt;()</c>.
    ///
    /// Expected:
    /// - Ctrl+1, Ctrl+2, none; Ctrl+5, Ctrl+4, none; Ctrl+3; Ctrl+2, the row added later.
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task KeyBindingFor_AnActionWithArguments_IsARowWithTheSameArguments()
    {
        Element box = default;
        await using HeadlessApp app = await HeadlessApp.StartAsync(() => box = Box().Width(10).Height(10).Focusable());
        Assert.True(app.Focus(box));
        _ = app.Keymap.Bind("ctrl-1", new AvailabilityZoom { Level = 1 });
        _ = app.Keymap.Bind("ctrl-2", new AvailabilityZoom { Level = 2 });
        _ = app.Keymap.Bind("ctrl-3", new Pick(1));
        _ = app.Keymap.Bind("ctrl-4", new AvailabilityGrow { By = 1 });
        _ = app.Keymap.Bind("ctrl-5", new AvailabilityGrow { By = 1, Unit = "em" });

        Assert.Equal("Ctrl+1", box.KeyBindingFor(new AvailabilityZoom { Level = 1 })?.ToDisplayString());
        Assert.Equal("Ctrl+2", box.KeyBindingFor(new AvailabilityZoom { Level = 2 })?.ToDisplayString());
        Assert.Null(box.KeyBindingFor(new AvailabilityZoom { Level = 3 }));
        Assert.Equal("Ctrl+5", box.KeyBindingFor(new AvailabilityGrow { By = 1, Unit = "em" })?.ToDisplayString());
        Assert.Equal("Ctrl+4", box.KeyBindingFor(new AvailabilityGrow { By = 1 })?.ToDisplayString());
        Assert.Null(box.KeyBindingFor(new AvailabilityGrow { By = 2 }));
        Assert.Equal("Ctrl+3", box.KeyBindingFor(new Pick(9))?.ToDisplayString());
        Assert.Equal("Ctrl+2", box.KeyBindingFor<AvailabilityZoom>()?.ToDisplayString());
        _ = app.PumpAndSettle();
    }

    /// <summary>
    /// The keys, whether an action can be dispatched, and the dispatch are read and routed from the node given; from the
    /// focused node when none is given; and from where a route starts with no focus when the node given is no longer in the tree.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A box with the context <c>Side</c> and a handler of <c>Pick</c>, a focused box beside it with a handler of
    ///   <c>Square</c>, both in the column the root function returns, which has a handler of <c>Circle</c>, and a node made in
    ///   a scope that was disposed. A row: <c>ctrl-p</c> for <c>Pick(1)</c> in <c>Side</c>.
    ///
    /// Steps:
    /// - From the focused box, with no node, with the first box, and with the removed node: read <c>KeyBindingFor</c> and
    ///   <c>CanDispatch</c> of <c>Pick(1)</c>, and dispatch it. With the removed node, read <c>CanDispatch</c> of a
    ///   <c>Square</c> and of a <c>Circle</c>, and dispatch each; read the <c>Square</c> with no node and with the first box.
    ///
    /// Expected:
    /// - <c>Pick(1)</c>: with no node and with the removed one, no keys, cannot, not handled; with the first box, Ctrl+P, can,
    ///   handled once. With the removed node, the route starts at the column: the <c>Square</c> of the focused box cannot and
    ///   is not handled, and the <c>Circle</c> can and is handled by the column. The <c>Square</c>: can with no node; cannot with
    ///   the first box.
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task ReadsAndDispatch_FromANode_StartThere()
    {
        var picked = new List<int>();
        Element side = default;
        Element focused = default;
        Element gone = default;
        await using HeadlessApp app = await HeadlessApp.StartAsync(() =>
        {
            side = Box().Width(10).Height(10).KeyContext("Side").OnAction((in ActionEvent<Pick> e) => picked.Add(e.Action.Number));
            focused = Box().Width(10).Height(10).Focusable().OnAction((in ActionEvent<Square> _) => picked.Add(-1));
            ScopeHandle scope = Scope.Create(() => gone = Box());
            Scope.Dispose(scope);
            return Column(side, focused).OnAction((in ActionEvent<Circle> _) => picked.Add(-2));
        });
        Assert.True(app.Focus(focused));
        _ = app.Keymap.Bind("ctrl-p", new Pick(1), "Side");
        var pick = new Pick(1);

        Assert.Null(focused.KeyBindingFor(pick));
        Assert.False(focused.CanDispatch(pick));
        Assert.False(focused.Dispatch(pick, from: null));
        Assert.Null(focused.KeyBindingFor(pick, gone));
        Assert.False(focused.CanDispatch(pick, gone));
        Assert.False(focused.Dispatch(pick, gone));
        Assert.False(focused.CanDispatch(new Square(), gone));
        Assert.False(focused.Dispatch(new Square(), gone));
        Assert.True(focused.CanDispatch(new Square()));
        Assert.True(focused.CanDispatch(new Circle(), gone));
        Assert.True(focused.Dispatch(new Circle(), gone));
        Assert.False(focused.CanDispatch(new Square(), side));
        Assert.Equal("Ctrl+P", focused.KeyBindingFor(pick, side)?.ToDisplayString());
        Assert.True(focused.CanDispatch(pick, side));
        Assert.True(focused.Dispatch(pick, side));
        Assert.Equal([-2, 1], picked);
        _ = app.PumpAndSettle();
    }

    /// <summary>
    /// A part's handler of a base type answers whether it can take an action now: an action it cannot take goes on to the
    /// handlers above, the computation that asked runs again when a signal the answer read changes, and a read by the type alone
    /// does not ask.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A focused box with one handler of <c>Shape</c> and its derived types, which can take a <c>Circle</c> while a signal is
    ///   true and never a <c>Square</c>; its parent has a handler of <c>Round</c>, a type derived from <c>Circle</c>.
    /// - Two computations made in the root function read <c>CanDispatch</c> of a <c>Circle</c> and of a <c>Round</c>.
    ///
    /// Steps:
    /// - Read <c>CanDispatch</c> of a <c>Circle</c>, a <c>Square</c>, and a <c>Round</c>; set the signal to false and read the
    ///   <c>Circle</c> and the <c>Round</c> again. Read <c>CanDispatch&lt;Square&gt;()</c>.
    ///
    /// Expected:
    /// - Yes, no, yes. With the signal false: the <c>Circle</c> no; the <c>Round</c> still yes, from the parent's handler. By the
    ///   type alone: yes.
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task OnActionsOf_WithAnAnswer_SaysWhetherItCanTakeAnActionNow()
    {
        var allowed = Signal.Create(true);
        Element box = default;
        Computed<bool>? circle = null;
        Computed<bool>? round = null;
        await using HeadlessApp app = await HeadlessApp.StartAsync(() =>
        {
            box = Box().Width(10).Height(10).Focusable().OnActionsOf((in ActionEvent<Shape> _) => { }, shape => shape is Circle && allowed.Get());
            circle = Computed.Create(() => box.CanDispatch(new Circle()));
            round = Computed.Create(() => box.CanDispatch(new Round()));
            return Column(box).OnAction((in ActionEvent<Round> _) => { });
        });
        Assert.True(app.Focus(box));

        (bool, bool, bool) first = (circle!.Get(), box.CanDispatch(new Square()), round!.Get());
        allowed.Set(false);
        _ = app.PumpAndSettle();

        Assert.Equal((true, false, true), first);
        Assert.False(circle.Get());
        Assert.True(round.Get());
        Assert.True(box.CanDispatch<Square>());
        _ = app.PumpAndSettle();
    }

    private class Shape : IAction;

    private class Circle : Shape;

    private sealed class Square : Shape;

    private sealed class Round : Circle;

    private sealed class Pick(int number) : IAction
    {
        public int Number { get; } = number;
    }
}

/// <summary>
/// A named action with an argument, written as a record: the source generator compares its level.
/// </summary>
[Action("test::Zoom")]
internal sealed record AvailabilityZoom : IAction
{
    /// <summary>
    /// Gets the level.
    /// </summary>
    public int Level { get; init; }
}

/// <summary>
/// A named action with two arguments, written as a class: the source generator compares both.
/// </summary>
[Action("test::Grow")]
internal sealed class AvailabilityGrow : IAction
{
    /// <summary>
    /// Gets or sets the amount.
    /// </summary>
    public int By { get; set; }

    /// <summary>
    /// Gets or sets the unit of the amount.
    /// </summary>
    public string Unit { get; set; } = "px";
}
