using XylemUI.Primitives;
using XylemUI.Testing.XUnit;
using static XylemUI.Elements;
using static XylemUI.Primitives.Primitive;

namespace XylemUI.Desktop.Tests;

// The keys of the parts as rows of the keymap and actions (row KM2b, design §7, "The default keys and actions"): an app binds a
// part's keys again in the part's context, the keys shown for a part's action are read from the rows, and a part keeps its
// context beside the app's.
public sealed class PartActionsTests
{
    /// <summary>
    /// An app binds a text field's action to another key, makes it by its name, and takes a key of the field away, all in the
    /// field's context.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A focused text field with the text "abc"; End put the caret at its end.
    ///
    /// Steps:
    /// - Bind <c>ctrl-d</c> to <c>TextActions.DeleteBackward</c> in <c>TextField</c>; press Ctrl+D. Bind <c>ctrl-e</c> to the
    ///   action named <c>text::MoveToStart</c> in <c>TextField</c>; press Ctrl+E, then Delete. Bind <c>backspace</c> to no
    ///   action in <c>TextField</c>; press Ctrl+End, then Backspace.
    ///
    /// Expected:
    /// - "ab" after Ctrl+D. "b" after Ctrl+E and Delete, which deleted at the start. Backspace is not handled and the text
    ///   stays "b".
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task Field_KeysAreBoundAgain_InTheFieldsContext()
    {
        var text = Signal.Create("abc");
        Element field = default;
        await using HeadlessApp app = await HeadlessApp.StartAsync(() => Row(field = TextField(text).Width(200)));
        _ = app.PumpAndSettle();
        Assert.True(app.Focus(field));
        _ = app.Keyboard.Press(Key.End);
        _ = app.PumpAndSettle();

        _ = app.Keymap.Bind("ctrl-d", new TextActions.DeleteBackward(), "TextField");
        Assert.True(app.Keyboard.Press(Key.D, KeyModifiers.Control));
        Assert.Equal("ab", text.Get());

        _ = app.Keymap.BindByName("ctrl-e", "text::MoveToStart", context: "TextField");
        Assert.True(app.Keyboard.Press(Key.E, KeyModifiers.Control));
        Assert.True(app.Keyboard.Press(Key.Delete));
        Assert.Equal("b", text.Get());

        _ = app.Keymap.Bind("backspace", null, "TextField");
        Assert.True(app.Keyboard.Press(Key.End, KeyModifiers.Control));
        Assert.False(app.Keyboard.Press(Key.Back));
        Assert.Equal("b", text.Get());
        _ = app.PumpAndSettle();
    }

    /// <summary>
    /// The keys shown for a field's action that has two keys are those Windows' menus show.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A focused text field.
    ///
    /// Steps:
    /// - Read the keys shown from the field for the actions Cut, Copy, Paste, and Redo, and for Undo, which has one key.
    ///
    /// Expected:
    /// - <c>Ctrl+X</c>, <c>Ctrl+C</c>, <c>Ctrl+V</c>, <c>Ctrl+Y</c>, <c>Ctrl+Z</c>.
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task KeyBindingFor_AFieldsAction_IsTheKeyAMenuShows()
    {
        Element field = default;
        await using HeadlessApp app = await HeadlessApp.StartAsync(() => Row(field = TextField(Signal.Create("abc")).Width(200)));
        _ = app.PumpAndSettle();
        Assert.True(app.Focus(field));
        _ = app.PumpAndSettle();

        Assert.Equal(
            ["Ctrl+X", "Ctrl+C", "Ctrl+V", "Ctrl+Y", "Ctrl+Z"],
            [
                field.KeyBindingFor<TextActions.Cut>()?.ToDisplayString(),
                field.KeyBindingFor<TextActions.Copy>()?.ToDisplayString(),
                field.KeyBindingFor<TextActions.Paste>()?.ToDisplayString(),
                field.KeyBindingFor<TextActions.Redo>()?.ToDisplayString(),
                field.KeyBindingFor<TextActions.Undo>()?.ToDisplayString(),
            ]
        );
    }

    /// <summary>
    /// A context the app gives a part's node is beside the part's: the part keeps its keys, and a condition sees both names.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A focused text field with "abc", the caret at its end, to which the app gave the context <c>Search</c>; the row above it
    ///   handles <c>Pick</c> and records its number. Rows: <c>ctrl-k</c> for <c>Pick(1)</c> in <c>Search</c>, <c>ctrl-l</c> for
    ///   <c>Pick(2)</c> in <c>TextField &amp;&amp; Search</c>.
    ///
    /// Steps:
    /// - Press Home, Ctrl+K, Ctrl+L. Take the app's context away; press End, Home, Ctrl+K.
    ///
    /// Expected:
    /// - Home moves the caret to 0, and the two rows ask for 1 and 2. With the app's context gone, End and Home still move the
    ///   caret (3, then 0), and Ctrl+K asks for nothing.
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task Field_WithAContextOfTheApp_KeepsItsOwn()
    {
        var picked = new List<int>();
        var selection = Signal.Create(default(TextSelection));
        Element field = default;
        await using HeadlessApp app = await HeadlessApp.StartAsync(() =>
        {
            field = TextField(Signal.Create("abc"), new TextFieldOptions { Selection = selection }).Width(200).KeyContext("Search");
            return Row(field).OnAction((in ActionEvent<Pick> e) => picked.Add(e.Action.Number));
        });
        _ = app.Keymap.Bind("ctrl-k", new Pick(1), "Search");
        _ = app.Keymap.Bind("ctrl-l", new Pick(2), "TextField && Search");
        _ = app.PumpAndSettle();
        Assert.True(app.Focus(field));
        _ = app.PumpAndSettle();

        _ = app.Keyboard.Press(Key.Home);
        Assert.Equal(0, selection.Get().Caret);
        _ = app.Keyboard.Press(Key.K, KeyModifiers.Control);
        _ = app.Keyboard.Press(Key.L, KeyModifiers.Control);
        Assert.Equal([1, 2], picked);

        _ = field.KeyContext(null);
        _ = app.Keyboard.Press(Key.End);
        Assert.Equal(3, selection.Get().Caret);
        _ = app.Keyboard.Press(Key.Home);
        Assert.Equal(0, selection.Get().Caret);
        Assert.False(app.Keyboard.Press(Key.K, KeyModifiers.Control));
        Assert.Equal([1, 2], picked);
        _ = app.PumpAndSettle();
    }

    /// <summary>
    /// The arrows a menu does not use do not scroll the page under it: the menu handles their actions and does nothing.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A box 100 wide whose width scrolls over a row 600 wide; the row's first box is focusable and the anchor of a menu of
    ///   one item with no submenu.
    ///
    /// Steps:
    /// - With the focus on the anchor and the menu closed, press Right. Open the menu; press Right, then Left.
    ///
    /// Expected:
    /// - The first Right scrolls the page to 50. In the open menu, Right and Left are handled, the menu stays open, and the page
    ///   stays at 50.
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task Menu_ArrowsItDoesNotUse_DoNotScrollThePage()
    {
        var page = new ScrollState();
        var open = Signal.Create(false);
        Element anchor = default;
        await using HeadlessApp app = await HeadlessApp.StartAsync(
            () =>
            {
                anchor = Box()
                    .Width(100)
                    .Height(20)
                    .Focusable()
                    .Menu(open, static () => Column(MenuItem(Box().Width(80).Height(20), static () => { })));
                return Box(Row(anchor, Box().Width(500).Height(20)).Width(600))
                    .Overflow(Overflow.Scroll, Overflow.Visible)
                    .ScrollState(page)
                    .Width(100)
                    .Height(100);
            },
            new HeadlessOptions { Width = 400, Height = 300 }
        );
        _ = app.PumpAndSettle();
        Assert.True(app.Focus(anchor));
        _ = app.PumpAndSettle();

        Assert.True(app.Keyboard.Press(Key.Right));
        _ = app.PumpAndSettle();
        Assert.Equal(50f, page.Offset.Get().X);

        open.Set(true);
        _ = app.PumpAndSettle();
        Assert.True(app.Keyboard.Press(Key.Right));
        Assert.True(app.Keyboard.Press(Key.Left));
        _ = app.PumpAndSettle();

        Assert.Single(app.Popups);
        Assert.Equal(50f, page.Offset.Get().X);
    }

    /// <summary>
    /// While the input method composes, Enter does not submit a field of one line and Esc does not close a popup: the keys are
    /// the input method's.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A focused text field of one line whose <c>OnSubmit</c> records the text.
    ///
    /// Steps:
    /// - Compose "にほ"; press Enter and Esc. Commit "日本"; press Enter.
    ///
    /// Expected:
    /// - Nothing is submitted while composing, and the composition is still there after the two keys. After the commit, Enter
    ///   submits "日本".
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task EnterAndEsc_WhileTheInputMethodComposes_AreNotTheFieldsActions()
    {
        var submitted = new List<string>();
        var text = Signal.Create(string.Empty);
        Element field = default;
        await using HeadlessApp app = await HeadlessApp.StartAsync(() =>
            Row(field = TextField(text, new TextFieldOptions { OnSubmit = submitted.Add }).Width(200))
        );
        _ = app.PumpAndSettle();
        Assert.True(app.Focus(field));
        _ = app.PumpAndSettle();

        _ = app.Ime.Compose("にほ", cursor: 2);
        _ = app.Keyboard.Press(Key.Enter);
        _ = app.Keyboard.Press(Key.Escape);
        Assert.Empty(submitted);
        Assert.NotNull(app.Tree.TextInputClientOf(field.Node)!.Composition);

        _ = app.Ime.Compose("日本", cursor: 2).Commit("日本");
        Assert.True(app.Keyboard.Press(Key.Enter));
        Assert.Equal(["日本"], submitted);
        _ = app.PumpAndSettle();
    }

    private sealed class Pick(int number) : IAction
    {
        public int Number { get; } = number;
    }
}
