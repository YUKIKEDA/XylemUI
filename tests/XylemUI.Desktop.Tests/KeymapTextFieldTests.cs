using XylemUI.Primitives;
using XylemUI.Testing.XUnit;
using static XylemUI.Elements;
using static XylemUI.Primitives.Primitive;

namespace XylemUI.Desktop.Tests;

// The keymap beside a text field (row KM1b, design §7, "The order of keys and text input"): a key that types a character goes to
// a field that takes text before the keymap, every key does while the input method composes, and the keys of an unfinished
// binding are looked up even there.
public sealed class KeymapTextFieldTests
{
    /// <summary>
    /// With the focus in a field that takes text, a key that types a character is not looked up in the keymap, and a key with
    /// Control is; with the focus on a node that takes no text, and in a read-only field, the character key is looked up too.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A row of a text field, a read-only text field, and a focusable box; the row handles <c>Pick</c> and records its number.
    ///   The keymap has <c>j</c> for <c>Pick(1)</c>, <c>space</c> for <c>Pick(2)</c>, and <c>ctrl-s</c> for <c>Pick(3)</c>.
    ///
    /// Steps:
    /// - With the focus in the field: press J, the space bar, and Ctrl+S. With the focus on the box: press J and the space bar.
    ///   With the focus in the read-only field: press J.
    ///
    /// Expected:
    /// - In the field: J and the space bar ask for nothing and are not handled by the keymap; Ctrl+S asks for <c>Pick(3)</c>. On
    ///   the box: <c>Pick(1)</c> and <c>Pick(2)</c>. In the read-only field: <c>Pick(1)</c>.
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task CharacterKey_InAFieldThatTakesText_IsNotLookedUp()
    {
        var picked = new List<int>();
        Element field = default;
        Element readOnly = default;
        Element box = default;
        await using HeadlessApp app = await HeadlessApp.StartAsync(() =>
        {
            field = TextField(Signal.Create(string.Empty)).Width(100);
            readOnly = TextField(Signal.Create("n"), new TextFieldOptions { ReadOnly = true }).Width(100);
            box = Box().Width(20).Height(20).Focusable();
            return Row(field, readOnly, box).OnAction((in ActionEvent<Pick> e) => picked.Add(e.Action.Number));
        });
        _ = app.Keymap.Bind("j", new Pick(1));
        _ = app.Keymap.Bind("space", new Pick(2));
        _ = app.Keymap.Bind("ctrl-s", new Pick(3));

        Assert.True(app.Focus(field));
        _ = app.PumpAndSettle();
        _ = app.Keyboard.Press(Key.J);
        _ = app.Keyboard.Press(Key.Space);
        Assert.Empty(picked);
        Assert.True(app.Keyboard.Press(Key.S, KeyModifiers.Control));
        Assert.Equal([3], picked);

        picked.Clear();
        Assert.True(app.Focus(box));
        _ = app.PumpAndSettle();
        Assert.True(app.Keyboard.Press(Key.J));
        Assert.True(app.Keyboard.Press(Key.Space));
        Assert.Equal([1, 2], picked);

        picked.Clear();
        Assert.True(app.Focus(readOnly));
        _ = app.PumpAndSettle();
        Assert.True(app.Keyboard.Press(Key.J));
        Assert.Equal([1], picked);
        _ = app.PumpAndSettle();
    }

    /// <summary>
    /// While the input method composes, no key is looked up in the keymap; after the commit the key is.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A focused text field in a row that handles <c>Pick</c>. The keymap has <c>ctrl-s</c> and <c>escape</c> for <c>Pick</c>.
    ///
    /// Steps:
    /// - Compose "にほ"; press Ctrl+S and Esc; commit "日本"; press Ctrl+S.
    ///
    /// Expected:
    /// - While composing, neither key asks for <c>Pick</c>. After the commit, Ctrl+S asks for <c>Pick(1)</c>, and the field holds
    ///   "日本".
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task Key_WhileTheInputMethodComposes_IsNotLookedUp()
    {
        var picked = new List<int>();
        var text = Signal.Create(string.Empty);
        Element field = default;
        await using HeadlessApp app = await HeadlessApp.StartAsync(() =>
        {
            field = TextField(text).Width(200);
            return Row(field).OnAction((in ActionEvent<Pick> e) => picked.Add(e.Action.Number));
        });
        _ = app.Keymap.Bind("ctrl-s", new Pick(1));
        _ = app.Keymap.Bind("escape", new Pick(2));
        Assert.True(app.Focus(field));
        _ = app.PumpAndSettle();

        _ = app.Ime.Compose("にほ", cursor: 2);
        _ = app.Keyboard.Press(Key.S, KeyModifiers.Control);
        _ = app.Keyboard.Press(Key.Escape);
        Assert.Empty(picked);

        _ = app.Ime.Compose("日本", cursor: 2).Commit("日本");
        Assert.True(app.Keyboard.Press(Key.S, KeyModifiers.Control));
        Assert.Equal([1], picked);
        Assert.Equal("日本", text.Get());
        _ = app.PumpAndSettle();
    }

    /// <summary>
    /// The keys of an unfinished binding are looked up in a field too: a character key that ends the binding asks for its
    /// action.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A focused text field in a row that handles <c>Pick</c>. The keymap has <c>ctrl-k s</c> for <c>Pick(1)</c>.
    ///
    /// Steps:
    /// - Press Ctrl+K, then S. Then press S alone.
    ///
    /// Expected:
    /// - Ctrl+K is kept; S asks for <c>Pick(1)</c> and is handled, so the platform sends no text for it. S alone is not looked
    ///   up.
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task CharacterKey_ThatEndsABinding_AsksForItsActionInAField()
    {
        var picked = new List<int>();
        Element field = default;
        await using HeadlessApp app = await HeadlessApp.StartAsync(() =>
        {
            field = TextField(Signal.Create(string.Empty)).Width(200);
            return Row(field).OnAction((in ActionEvent<Pick> e) => picked.Add(e.Action.Number));
        });
        _ = app.Keymap.Bind("ctrl-k s", new Pick(1));
        Assert.True(app.Focus(field));
        _ = app.PumpAndSettle();

        Assert.True(app.Keyboard.Press(Key.K, KeyModifiers.Control));
        Assert.True(app.Keyboard.Press(Key.S));
        Assert.Equal([1], picked);

        _ = app.Keyboard.Press(Key.S);
        Assert.Equal([1], picked);
        _ = app.PumpAndSettle();
    }

    private sealed class Pick(int number) : IAction
    {
        public int Number { get; } = number;
    }
}
