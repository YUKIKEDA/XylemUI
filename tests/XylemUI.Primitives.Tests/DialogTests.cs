using XylemUI.Testing;
using XylemUI.Testing.XUnit;
using static XylemUI.Elements;
using static XylemUI.Primitives.Primitive;

namespace XylemUI.Primitives.Tests;

// The modal dialog (row P02d, design §13.1, "Dialog"): over a headless window of 800 × 600, the focus kept in it, the pointer kept
// from the nodes behind it, its backdrop, and the focus it gives back.
public sealed class DialogTests
{
    /// <summary>
    /// A dialog focuses its first focusable node when it opens, keeps the focus in it, and goes round its nodes with Tab; Esc closes
    /// it, writes false to its signal, and gives the focus back to the node focused before.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A focusable opener, focused, and a closed dialog of a column of two focusable 100 × 40 boxes.
    ///
    /// Steps:
    /// - Open the dialog and settle; Tab, Tab; try to focus the opener; Esc and settle. Read the focused node after each.
    ///
    /// Expected:
    /// - Open: the first box. Tab: the second, then the first. The opener refuses the focus, and the first box keeps it. Esc: no
    ///   popup, the signal false, the opener focused.
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task Dialog_KeepsTheFocus_AndGivesItBackOnEsc()
    {
        var open = Signal.Create(false);
        Element opener = default;
        Element first = default;
        Element second = default;
        await using HeadlessApp app = await HeadlessApp.StartAsync(() =>
            Column(
                opener = Box().Width(100).Height(30).Focusable(),
                Dialog(open, () => Column(first = Box().Width(100).Height(40).Focusable(), second = Box().Width(100).Height(40).Focusable()))
            )
        );
        _ = app.Focus(opener);

        open.Set(true);
        _ = app.PumpAndSettle();
        var focused = new List<NodeHandle> { app.Tree.Focus.Focused };
        for (int tab = 0; tab < 2; tab++)
        {
            _ = app.Keyboard.Press(Key.Tab);
            _ = app.PumpAndSettle();
            focused.Add(app.Tree.Focus.Focused);
        }

        bool openerTook = app.Focus(opener);
        focused.Add(app.Tree.Focus.Focused);
        _ = app.Keyboard.Press(Key.Escape);
        _ = app.PumpAndSettle();

        Assert.Equal([first.Node, second.Node, first.Node, first.Node], focused);
        Assert.False(openerTook);
        Assert.Empty(app.Popups);
        Assert.False(open.Get());
        Assert.Equal(opener.Node, app.Tree.Focus.Focused);
    }

    /// <summary>
    /// A dialog's content sits at the center of the window, and the nodes behind the backdrop take no pointer. A press on the
    /// backdrop closes it only with <see cref="DialogOptions.DismissOnBackdropPress"/>; a press on the content never does.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A 200 × 200 box at (0, 0) whose clicks count, and two open dialogs one after the other, each of a 100 × 40 box: the first
    ///   with the default options, the second with <see cref="DialogOptions.DismissOnBackdropPress"/>.
    ///
    /// Steps:
    /// - With the first open: read the content's center; click (10, 10) and the content's center, settling after each. Close it; open
    ///   the second; click (10, 10) and settle.
    ///
    /// Expected:
    /// - The center at (400, 300). The first stays open after both clicks. The second closes, and writes false to its signal. The box
    ///   behind counted no click.
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task Dialog_Backdrop_TakesThePointer_AndClosesOnlyWhenAsked()
    {
        var first = Signal.Create(true);
        var second = Signal.Create(false);
        Element content = default;
        int behind = 0;
        await using HeadlessApp app = await HeadlessApp.StartAsync(() =>
            Column(
                Box().Width(200).Height(200).OnClick(() => behind++),
                Dialog(first, () => content = Box().Width(100).Height(40)),
                Dialog(second, () => Box().Width(100).Height(40), new DialogOptions { DismissOnBackdropPress = true })
            )
        );
        _ = app.PumpAndSettle();

        (int X, int Y) center = app.CenterOf(content);
        app.Pointer.Click(10, 10);
        _ = app.PumpAndSettle();
        app.Pointer.Click(center.X, center.Y);
        _ = app.PumpAndSettle();
        bool firstStayed = first.Get() && app.Popups.Count == 1;
        first.Set(false);
        second.Set(true);
        _ = app.PumpAndSettle();
        app.Pointer.Click(10, 10);
        _ = app.PumpAndSettle();

        Assert.Equal((400, 300), center);
        Assert.True(firstStayed);
        Assert.False(second.Get());
        Assert.Empty(app.Popups);
        Assert.Equal(0, behind);
    }

    /// <summary>
    /// A dialog with nothing focusable empties the focus, so that no key goes to the node behind it; Esc still closes it.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A focusable opener, focused, whose Enter counts, and a closed dialog of a 100 × 40 box that takes no focus.
    ///
    /// Steps:
    /// - Open the dialog and settle; press Enter; press Esc and settle.
    ///
    /// Expected:
    /// - While open, no node focused, and Enter not counted. After Esc: no popup, the opener focused.
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task Dialog_NothingFocusable_EmptiesTheFocus()
    {
        var open = Signal.Create(false);
        Element opener = default;
        int enters = 0;
        await using HeadlessApp app = await HeadlessApp.StartAsync(() =>
            Column(
                opener = Box()
                    .Width(100)
                    .Height(30)
                    .Focusable()
                    .On(KeyEventKind.Down, EventPhase.Bubble, (in KeyEvent e) => enters += e.Key == Key.Enter ? 1 : 0),
                Dialog(open, () => Box().Width(100).Height(40))
            )
        );
        _ = app.Focus(opener);

        open.Set(true);
        _ = app.PumpAndSettle();
        bool none = app.Tree.Focus.Focused.IsNone;
        _ = app.Keyboard.Press(Key.Enter);
        _ = app.Keyboard.Press(Key.Escape);
        _ = app.PumpAndSettle();

        Assert.True(none);
        Assert.Equal(0, enters);
        Assert.Empty(app.Popups);
        Assert.Equal(opener.Node, app.Tree.Focus.Focused);
    }
}
