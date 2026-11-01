using XylemUI.Primitives;
using XylemUI.Testing.XUnit;
using static XylemUI.Elements;
using static XylemUI.Primitives.Primitive;

namespace XylemUI.Desktop.Tests;

// The window's access keys and the label (row P02d3, design §13.1, "The window's access keys, the label, and the menu bar") in a
// headless app: what an access key does by the target its access text finds, where it looks, and the keys it takes.
public sealed class WindowAccessKeyTests
{
    /// <summary>
    /// Alt and the key of an access text acts by its nearest target: a button is clicked, a label focuses its target, a focusable
    /// node takes the focus, and a text with neither does nothing.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A column: a button of "_Save" whose clicks count; a label of "_Name" beside the focusable box it is for; a focusable box holding "_Other";
    ///   and "_Dead" in a plain box. A focusable box first, focused.
    ///
    /// Steps:
    /// - Alt+S; Alt+N and read the focus; Alt+O and read the focus; focus the first box; Alt+D and read whether it was handled.
    ///
    /// Expected:
    /// - One click. The labelled box focused, then the other box. Alt+D not handled, and the first box still focused.
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task AccessKey_ActsByItsTarget()
    {
        int saves = 0;
        Element start = default;
        Element named = default;
        Element other = default;
        await using HeadlessApp app = await HeadlessApp.StartAsync(() =>
            Column(
                start = Box().Width(50).Height(20).Focusable(),
                Button(AccessText("_Save"), () => saves++),
                Row(Label(AccessText("_Name"), named = Box().Width(50).Height(20).Focusable()), named),
                other = Box(AccessText("_Other")).Focusable(),
                Box(AccessText("_Dead"))
            )
        );
        _ = app.PumpAndSettle();
        _ = app.Focus(start);

        _ = app.Keyboard.Press(Key.S, KeyModifiers.Alt);
        _ = app.Keyboard.Press(Key.N, KeyModifiers.Alt);
        NodeHandle afterName = app.Tree.Focus.Focused;
        _ = app.Keyboard.Press(Key.O, KeyModifiers.Alt);
        NodeHandle afterOther = app.Tree.Focus.Focused;
        _ = app.Focus(start);
        bool dead = app.Keyboard.Press(Key.D, KeyModifiers.Alt);
        _ = app.PumpAndSettle();

        Assert.Equal(1, saves);
        Assert.Equal(named.Node, afterName);
        Assert.Equal(other.Node, afterOther);
        Assert.False(dead);
        Assert.Equal(start.Node, app.Tree.Focus.Focused);
    }

    /// <summary>
    /// A press of the left button on a label gives the focus to its target.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A row: a 100 × 20 label of "_Name" and a focusable box, its target.
    ///
    /// Steps:
    /// - Click the label.
    ///
    /// Expected:
    /// - The box focused.
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task Label_Press_FocusesItsTarget()
    {
        Element label = default;
        Element box = default;
        await using HeadlessApp app = await HeadlessApp.StartAsync(() =>
            Row(label = Label(AccessText("_Name"), box = Box().Width(50).Height(20).Focusable()).Width(100).Height(20), box)
        );
        _ = app.PumpAndSettle();

        app.Pointer.Click(label);
        _ = app.PumpAndSettle();

        Assert.Equal(box.Node, app.Tree.Focus.Focused);
    }

    /// <summary>
    /// While a modal dialog is open, the access keys look inside it alone; an open menu's letters come before the window's.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A button "_Open" whose clicks count, a dialog of a button "_OK" whose clicks count, and a focusable anchor with a menu of an
    ///   item "_Open" that records its choice.
    ///
    /// Steps:
    /// - Open the dialog; Alt+O; close it with Esc. Focus the anchor; open the menu; Alt+O.
    ///
    /// Expected:
    /// - The dialog's OK clicked, the window's Open not. Then the menu's item chosen, the window's Open still not clicked.
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task AccessKey_InsideTheModal_AndAfterTheMenu()
    {
        var dialog = Signal.Create(false);
        var menu = Signal.Create(false);
        int opens = 0;
        int oks = 0;
        int chosen = 0;
        Element anchor = default;
        await using HeadlessApp app = await HeadlessApp.StartAsync(() =>
            Column(
                Button(AccessText("_Open"), () => opens++),
                Dialog(dialog, () => Button(AccessText("_OK"), () => oks++)),
                anchor = Box().Width(100).Height(20).Focusable().Menu(menu, () => MenuItem(AccessText("_Open"), () => chosen++))
            )
        );
        _ = app.PumpAndSettle();

        dialog.Set(true);
        _ = app.PumpAndSettle();
        _ = app.Keyboard.Press(Key.O, KeyModifiers.Alt);
        _ = app.Keyboard.Press(Key.Escape);
        _ = app.PumpAndSettle();
        _ = app.Focus(anchor);
        menu.Set(true);
        _ = app.PumpAndSettle();
        _ = app.Keyboard.Press(Key.O, KeyModifiers.Alt);
        _ = app.PumpAndSettle();

        Assert.Equal((0, 1, 1), (opens, oks, chosen));
    }

    /// <summary>
    /// While a menu is open or a menu bar is selected, Alt and a letter that none of their items takes is not the window's access key:
    /// the button behind them is not clicked.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A button "_Save" whose clicks count, a focusable anchor with a menu of an item "_Open", and a menu bar of "_File" with a
    ///   menu of an item "_New".
    ///
    /// Steps:
    /// - Focus the anchor; open its menu; Alt+S, reading whether it was handled; Esc. F10; Alt+S, reading whether it was handled;
    ///   F10; Alt+S.
    ///
    /// Expected:
    /// - Both Alt+S in the menu and in the selected bar not handled; the button clicked once, by the last Alt+S.
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task AccessKey_InAMenuOrASelectedBar_DoesNotReachTheWindow()
    {
        var menu = Signal.Create(false);
        int saves = 0;
        Element anchor = default;
        await using HeadlessApp app = await HeadlessApp.StartAsync(() =>
            Column(
                MenuBar(MenuBarItem(AccessText("_File"), () => Column(MenuItem(AccessText("_New"), () => { })))),
                Button(AccessText("_Save"), () => saves++),
                anchor = Box().Width(100).Height(20).Focusable().Menu(menu, () => Column(MenuItem(AccessText("_Open"), () => { })))
            )
        );
        _ = app.PumpAndSettle();
        _ = app.Focus(anchor);
        menu.Set(true);
        _ = app.PumpAndSettle();

        bool inMenu = app.Keyboard.Press(Key.S, KeyModifiers.Alt);
        _ = app.Keyboard.Press(Key.Escape);
        _ = app.PumpAndSettle();
        _ = app.Keyboard.Press(Key.F10);
        bool inBar = app.Keyboard.Press(Key.S, KeyModifiers.Alt);
        int whileIn = saves;
        _ = app.Keyboard.Press(Key.F10);
        _ = app.Keyboard.Press(Key.S, KeyModifiers.Alt);
        _ = app.PumpAndSettle();

        Assert.False(inMenu);
        Assert.False(inBar);
        Assert.Equal((0, 1), (whileIn, saves));
    }

    /// <summary>
    /// The window's access keys need Alt, without Control; several targets of one key take the focus in turn, round the end, and are
    /// not clicked; disabled and hidden ones are passed over; a focused text field leaves Alt and its letter to them.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A text field, focused, and buttons "_Apply" and "_Abort" whose clicks count, a disabled button "_Alpha", and a button "_Ace"
    ///   that a false signal does not show.
    ///
    /// Steps:
    /// - Press A; Control+Alt+A; Control+Alt with the letter "a" given, as a layout whose AltGr types a letter sends; Alt+A three
    ///   times, reading the focused button after each.
    ///
    /// Expected:
    /// - A and the two Control+Alt keys do nothing with the buttons: the field keeps the focus. Alt+A focuses "_Apply", "_Abort", "_Apply"; no button is clicked.
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task AccessKey_NeedsAlt_CyclesShared_AndPassesOverDisabledAndHidden()
    {
        int clicks = 0;
        Element field = default;
        Element apply = default;
        Element abort = default;
        await using HeadlessApp app = await HeadlessApp.StartAsync(() =>
            Column(
                field = TextField(Signal.Create(string.Empty)).Width(200),
                apply = Button(AccessText("_Apply"), () => clicks++),
                abort = Button(AccessText("_Abort"), () => clicks++),
                Button(AccessText("_Alpha"), () => clicks++).Disabled(),
                Show(Signal.Create(false), () => Button(AccessText("_Ace"), () => clicks++))
            )
        );
        _ = app.PumpAndSettle();
        _ = app.Focus(field);

        _ = app.Keyboard.Press(Key.A);
        _ = app.Keyboard.Press(Key.A, KeyModifiers.Control | KeyModifiers.Alt);

        // A layout whose AltGr types a letter (Polish AltGr+A is "ą"): Control and Alt with the letter "a" is still no access key.
        _ = app.Input.Handle(
            new KeyInput(
                KeyEventKind.Down,
                Key.A,
                PhysicalKey.None,
                new System.Text.Rune('a'),
                KeyModifiers.Control | KeyModifiers.Alt,
                false,
                app.Time.Elapsed
            )
        );
        NodeHandle afterPlain = app.Tree.Focus.Focused;
        var focused = new List<string>();
        for (int i = 0; i < 3; i++)
        {
            _ = app.Keyboard.Press(Key.A, KeyModifiers.Alt);
            NodeHandle now = app.Tree.Focus.Focused;
            focused.Add(
                now == apply.Node ? "apply"
                : now == abort.Node ? "abort"
                : "other"
            );
        }

        _ = app.PumpAndSettle();

        Assert.Equal(field.Node, afterPlain);
        Assert.Equal(["apply", "abort", "apply"], focused);
        Assert.Equal(0, clicks);
    }
}
