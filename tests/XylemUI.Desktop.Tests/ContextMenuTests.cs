using System.Numerics;
using XylemUI.Primitives;
using XylemUI.Testing.XUnit;
using static XylemUI.Elements;
using static XylemUI.Primitives.Primitive;

namespace XylemUI.Desktop.Tests;

// The context menus of the text field and the selection area (row P02d, design §4.9 and §4.10, "The context menu") in a headless
// app of 400 × 200: the commands and when they are enabled, where the menu opens, what the right button does to the selection and
// the focus, and what a chosen command does. The text is the test font's "n" at 14 px, 8.54 px wide, on lines 20.272 px high, at
// the window's top left. The menu's items are boxes of 200 × 30.
public sealed class ContextMenuTests
{
    private const float N = 0.61f * 14;
    private const float Line = 20.272f;

    /// <summary>
    /// The field's commands are enabled as Flutter enables them on Windows: the cut with a selection in a field neither read-only
    /// nor a password, the copy with a selection in a field not a password, the paste with text on the clipboard in a field not
    /// read-only, and the select-all with text not all selected.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - Five fields, each opened alone and focused (a field of one line then selects all): "nn" with the caret moved to the end and
    ///   an empty clipboard; "nn" with "x" on the clipboard; the same read-only; the same a password; "" with an empty clipboard.
    ///
    /// Steps:
    /// - Press Shift+F10 in each, settle, and read the commands the menu was made with.
    ///
    /// Expected:
    /// - Cut, copy, paste, select-all: no, no, no, yes; yes, yes, yes, no; no, yes, no, no; no, no, yes, no; no, no, no, no.
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task FieldCommands_AreEnabledAsFlutterEnablesThem()
    {
        (string Text, TextFieldOptions Options, string? Clipboard, bool End)[] cases =
        [
            ("nn", new TextFieldOptions(), null, true),
            ("nn", new TextFieldOptions(), "x", false),
            ("nn", new TextFieldOptions { ReadOnly = true }, "x", false),
            ("nn", new TextFieldOptions { Password = true }, "x", false),
            (string.Empty, new TextFieldOptions(), null, false),
        ];
        var enabled = new List<(bool, bool, bool, bool)>();

        foreach ((string text, TextFieldOptions options, string? clipboard, bool end) in cases)
        {
            TextMenuCommands? seen = null;
            await using HeadlessApp app = await StartField(Signal.Create(text), options with { ContextMenu = c => Panel(seen = c) });
            app.Clipboard.Text = clipboard;
            if (end)
            {
                _ = app.Keyboard.Press(Key.End);
            }

            _ = app.Keyboard.Press(Key.F10, KeyModifiers.Shift);
            _ = app.PumpAndSettle();
            enabled.Add((seen!.Cut.Enabled, seen.Copy.Enabled, seen.Paste.Enabled, seen.SelectAll.Enabled));
            _ = app.Keyboard.Press(Key.Escape);
            _ = app.PumpAndSettle();
        }

        Assert.Equal(
            [
                (false, false, false, true),
                (true, true, true, false),
                (false, true, false, false),
                (false, false, true, false),
                (false, false, false, false),
            ],
            enabled
        );
    }

    /// <summary>
    /// The right button's down keeps a selection it falls in, and puts the caret where it falls otherwise, or when the field had no
    /// focus, which the field then takes; the up opens the menu at the pointer.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A field of "nnnn", focused (all selected), with a focusable box beside it, a selection signal, and a context menu.
    ///
    /// Steps:
    /// - Click the right button at x = 1.4 n (y 10); Esc. End, then the right button at 1.4 n; Esc. Shift+Left twice, then the right
    ///   button at 0.6 n; Esc. Focus the box, then the right button at 2.6 n. Read the selection after each click, and where the menu
    ///   opened after the first.
    ///
    /// Expected:
    /// - (0, 4) kept; the caret at 1; the caret at 1 (outside (2, 4)); the field focused with the caret at 3. The menu's top left at
    ///   the pointer (12, 10).
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task FieldRightButton_KeepsOrMovesTheSelection_AndOpensAtThePointer()
    {
        var selection = Signal.Create(default(TextSelection));
        Element first = default;
        Element other = default;
        await using HeadlessApp app = await StartField(
            Signal.Create("nnnn"),
            new TextFieldOptions { Selection = selection, ContextMenu = c => first = Panel(c) },
            box => other = box
        );
        var seen = new List<TextSelection>();
        Vector2 opened = default;
        void Right(float x)
        {
            app.Pointer.Click((int)MathF.Round(x), 10, PointerButton.Right);
            _ = app.PumpAndSettle();
            seen.Add(selection.Get());
        }

        Right(1.4f * N);
        opened = TopLeft(app, first);
        _ = app.Keyboard.Press(Key.Escape);
        _ = app.Keyboard.Press(Key.End);
        Right(1.4f * N);
        _ = app.Keyboard.Press(Key.Escape);
        _ = app.Keyboard.Press(Key.End);
        _ = app.Keyboard.Press(Key.Left, KeyModifiers.Shift);
        _ = app.Keyboard.Press(Key.Left, KeyModifiers.Shift);
        Right(0.6f * N);
        _ = app.Keyboard.Press(Key.Escape);
        _ = app.Focus(other);
        Right(2.6f * N);

        Assert.Equal([new TextSelection(0, 4), TextSelection.Collapsed(1), TextSelection.Collapsed(1), TextSelection.Collapsed(3)], seen);
        Assert.Equal(new Vector2(12, 10), opened);
    }

    /// <summary>
    /// Shift+F10 and the up of the application key open the field's menu at the bottom left of the caret's box.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A field of "nnnn", focused, with a context menu; the caret moved to 2.
    ///
    /// Steps:
    /// - Press Shift+F10, settle, and read where the menu opened; Esc; press the application key, settle, and read it again.
    ///
    /// Expected:
    /// - Both at (2 n, the line's height).
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task FieldMenuKeys_OpenAtTheCaret()
    {
        Element first = default;
        await using HeadlessApp app = await StartField(Signal.Create("nnnn"), new TextFieldOptions { ContextMenu = c => first = Panel(c) });
        _ = app.Keyboard.Press(Key.Home);
        _ = app.Keyboard.Press(Key.Right);
        _ = app.Keyboard.Press(Key.Right);
        var opened = new List<Vector2>();

        foreach (Key key in new[] { Key.F10, Key.Apps })
        {
            _ = app.Keyboard.Press(key, key == Key.F10 ? KeyModifiers.Shift : KeyModifiers.None);
            _ = app.PumpAndSettle();
            opened.Add(TopLeft(app, first));
            _ = app.Keyboard.Press(Key.Escape);
            _ = app.PumpAndSettle();
        }

        foreach (Vector2 at in opened)
        {
            Assert.Equal(2 * N, at.X, 0.01f);
            Assert.Equal(Line, at.Y, 0.01f);
        }
    }

    /// <summary>
    /// A chosen command runs after the menu gave the focus back to the field, as its key does: the cut puts the text on the
    /// clipboard and Control+Z brings it back; the paste writes the clipboard's text.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A field of "nnnn", focused (all selected), with a menu of the cut, the copy, the paste, and the select-all, in that order.
    ///
    /// Steps:
    /// - Shift+F10 (the cut is focused) and Enter; read the text, the clipboard, and the focus; Control+Z. Put "ab" on the clipboard;
    ///   Shift+F10 (the paste is the first enabled) and Enter.
    ///
    /// Expected:
    /// - After the cut: "", "nnnn" on the clipboard, the field focused. After Control+Z: "nnnn". After the paste: "nnnnab".
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task FieldChosenCommand_RunsAsItsKeyDoes()
    {
        var text = Signal.Create("nnnn");
        Element field = default;
        await using HeadlessApp app = await StartField(text, new TextFieldOptions { ContextMenu = c => Panel(c) }, field: f => field = f);

        _ = app.Keyboard.Press(Key.F10, KeyModifiers.Shift);
        _ = app.PumpAndSettle();
        _ = app.Keyboard.Press(Key.Enter);
        _ = app.PumpAndSettle();
        (string Text, string? Clipboard, bool Focused) cut = (text.Get(), app.Clipboard.Text, app.Tree.Focus.Focused == field.Node);
        _ = app.Keyboard.Press(Key.Z, KeyModifiers.Control);
        string undone = text.Get();
        app.Clipboard.Text = "ab";
        _ = app.Keyboard.Press(Key.End);
        _ = app.Keyboard.Press(Key.F10, KeyModifiers.Shift);
        _ = app.PumpAndSettle();
        _ = app.Keyboard.Press(Key.Enter);
        _ = app.PumpAndSettle();

        Assert.Equal((string.Empty, "nnnn", true), cut);
        Assert.Equal("nnnn", undone);
        Assert.Equal("nnnnab", text.Get());
    }

    /// <summary>
    /// The right button's down commits a composition before the menu opens, as a press of the field does (design §4.7).
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - An empty field, focused, with a context menu; the input method composes "nn".
    ///
    /// Steps:
    /// - Click the right button at (4, 10) and settle.
    ///
    /// Expected:
    /// - No composition; the text is "nn"; the menu is open.
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task FieldRightButton_CommitsAComposition()
    {
        var text = Signal.Create(string.Empty);
        await using HeadlessApp app = await StartField(text, new TextFieldOptions { ContextMenu = c => Panel(c) });
        _ = app.Ime.Compose("nn", 2);
        _ = app.PumpAndSettle();

        app.Pointer.Click(4, 10, PointerButton.Right);
        _ = app.PumpAndSettle();

        Assert.False(app.Ime.IsComposing);
        Assert.Equal("nn", text.Get());
        _ = Assert.Single(app.Popups);
    }

    /// <summary>
    /// The right button keeps the area's selection, which the area keeps while the focus is in its menu; the copy chosen there puts
    /// the selection on the clipboard and gives the focus back to the area.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - An area around a column of two leaves of "nnnn", with a context menu of the copy and the select-all.
    ///
    /// Steps:
    /// - Drag from x = 1.4 n on the first leaf to 2.4 n on the second; click the right button at (n, 5) and settle; read the commands,
    ///   the leaves' selections, and the focus; press Enter (the copy is focused) and settle.
    ///
    /// Expected:
    /// - Both commands enabled; the selections (1, 4) and (0, 2) kept; the focus in the menu. After Enter: "nnn\nnn" on the
    ///   clipboard, the area focused, the selections kept.
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task AreaRightButton_KeepsTheSelection_AndTheCopyCopiesIt()
    {
        SelectionMenuCommands? seen = null;
        Element area = default;
        Element top = default;
        Element bottom = default;
        await using HeadlessApp app = await StartArea(() =>
            area = SelectionArea(
                Column(top = Elements.Text("nnnn"), bottom = Elements.Text("nnnn")),
                new SelectionAreaOptions { ContextMenu = c => AreaPanel(seen = c) }
            )
        );
        Drag(app, (1.4f * N, Line / 2), (2.4f * N, Line * 1.5f));

        app.Pointer.Click((int)MathF.Round(N), 5, PointerButton.Right);
        _ = app.PumpAndSettle();
        (bool, bool) enabled = (seen!.Copy.Enabled, seen.SelectAll.Enabled);
        ((int, int), (int, int)) kept = (SelectionOf(app, top), SelectionOf(app, bottom));
        bool inMenu = app.Tree.Popups.PopupContaining(app.Tree.Focus.Focused) is not null;
        _ = app.Keyboard.Press(Key.Enter);
        _ = app.PumpAndSettle();

        Assert.Equal((true, true), enabled);
        Assert.Equal(((1, 4), (0, 2)), kept);
        Assert.True(inMenu);
        Assert.Equal("nnn\nnn", app.Clipboard.Text);
        Assert.Equal(area.Node, app.Tree.Focus.Focused);
        Assert.Equal(((1, 4), (0, 2)), (SelectionOf(app, top), SelectionOf(app, bottom)));
    }

    /// <summary>
    /// Shift+F10 opens the area's menu at the bottom left of the selection's end, and below the area with no selection; with no
    /// selection, the copy is not enabled.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - An area around a column of two leaves of "nnnn", focused, with a context menu.
    ///
    /// Steps:
    /// - Press Shift+F10 with no selection, settle, and read where the menu opened and the copy; Esc. Drag from x = 1.4 n on the first
    ///   leaf to 2.4 n on the second; press Shift+F10, settle, and read where the menu opened.
    ///
    /// Expected:
    /// - With none: (0, 2 lines), the copy not enabled. With the selection: (2 n, 2 lines), at its end.
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task AreaMenuKey_OpensAtTheSelectionsEnd_OrBelowTheArea()
    {
        SelectionMenuCommands? seen = null;
        Element area = default;
        Element first = default;
        await using HeadlessApp app = await StartArea(() =>
            area = SelectionArea(
                Column(Elements.Text("nnnn"), Elements.Text("nnnn")),
                new SelectionAreaOptions { ContextMenu = c => first = AreaPanel(seen = c) }
            )
        );
        _ = app.Focus(area);

        _ = app.Keyboard.Press(Key.F10, KeyModifiers.Shift);
        _ = app.PumpAndSettle();
        Vector2 below = TopLeft(app, first);
        bool copyWithNone = seen!.Copy.Enabled;
        _ = app.Keyboard.Press(Key.Escape);
        Drag(app, (1.4f * N, Line / 2), (2.4f * N, Line * 1.5f));
        _ = app.Keyboard.Press(Key.F10, KeyModifiers.Shift);
        _ = app.PumpAndSettle();
        Vector2 atEnd = TopLeft(app, first);

        Assert.Equal(0, below.X, 0.01f);
        Assert.Equal(2 * Line, below.Y, 0.01f);
        Assert.False(copyWithNone);
        Assert.Equal(2 * N, atEnd.X, 0.01f);
        Assert.Equal(2 * Line, atEnd.Y, 0.01f);
    }

    // A field of 300 px in a row with a focusable 20 × 20 box, settled and focused.
    private static async Task<HeadlessApp> StartField(
        Signal<string> text,
        TextFieldOptions options,
        Action<Element>? box = null,
        Action<Element>? field = null
    )
    {
        Element made = default;
        HeadlessApp app = await HeadlessApp.StartAsync(
            () =>
            {
                made = TextField(text, options).Width(300);
                field?.Invoke(made);
                Element other = Box().Width(20).Height(20).Focusable();
                box?.Invoke(other);
                return Row(made, other);
            },
            new HeadlessOptions { Width = 400, Height = 200 }
        );
        _ = app.PumpAndSettle();
        Assert.True(app.Focus(made));
        _ = app.PumpAndSettle();
        return app;
    }

    private static async Task<HeadlessApp> StartArea(Func<Element> content)
    {
        HeadlessApp app = await HeadlessApp.StartAsync(content, new HeadlessOptions { Width = 400, Height = 300 });
        _ = app.PumpAndSettle();
        return app;
    }

    private static void Drag(HeadlessApp app, (float X, float Y) from, (float X, float Y) to)
    {
        app.Pointer.Move((int)MathF.Round(from.X), (int)MathF.Round(from.Y));
        app.Pointer.Down();
        _ = app.PumpAndSettle();
        app.Pointer.Move((int)MathF.Round(to.X), (int)MathF.Round(to.Y));
        _ = app.PumpAndSettle();
        app.Pointer.Up();
        _ = app.PumpAndSettle();
    }

    // The panel of a field's menu: the cut, the copy, the paste, and the select-all, each enabled by its command.
    private static Element Panel(TextMenuCommands c) => Column(Item(c.Cut), Item(c.Copy), Item(c.Paste), Item(c.SelectAll));

    // The panel of an area's menu: the copy and the select-all.
    private static Element AreaPanel(SelectionMenuCommands c) => Column(Item(c.Copy), Item(c.SelectAll));

    private static Element Item(MenuCommand command) => MenuItem(Box().Width(200).Height(30), command.Run, command.Enabled);

    private static (int Start, int End) SelectionOf(HeadlessApp app, Element leaf) =>
        app.Tree.Nodes.GetRef(leaf.Node).Text!.Selection is { IsEmpty: false } selection ? (selection.Start, selection.End) : (0, 0);

    // The top left of a node's box in the window's logical pixels.
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
