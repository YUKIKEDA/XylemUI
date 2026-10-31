using System.Numerics;
using XylemUI.Primitives;
using XylemUI.Testing.XUnit;
using static XylemUI.Elements;
using static XylemUI.Primitives.Primitive;

namespace XylemUI.Desktop.Tests;

// The text field (row P02b, design §4.9) in a headless app: the values it writes, the keys of its table, the clipboard, Enter, the
// focus, AltGr, the pointer's clicks, the scrolling, the placeholder, the limit, the drawing of the selection, and the caret's
// blink. Its text is the test font's "n" at 14 px, 8.54 px wide, on lines 20.272 px high.
public sealed class TextFieldTests
{
    private const float N = 0.61f * 14;

    /// <summary>
    /// Typing writes the text signal and moves the caret; a write from the app moves the caret to the end and forgets the undo.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A field of one line, bound to a text signal and a selection signal, with the focus.
    ///
    /// Steps:
    /// - Type "abc"; write "xyz" from the app; type "1"; press Control+Z twice.
    ///
    /// Expected:
    /// - "abc" with the caret at 3; after the write and "1", "xyz1" with the caret at 4; the first undo gives "xyz", the second
    ///   leaves it (the app's write forgot what came before).
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task Typing_WritesTheSignals_AndAnAppsWriteForgetsTheUndo()
    {
        var text = Signal.Create(string.Empty);
        var selection = Signal.Create(default(TextSelection));
        await using Field field = await Field.StartAsync(text, new TextFieldOptions { Selection = selection });

        field.App.Keyboard.Type("abc");
        (string, TextSelection) typed = (text.Get(), selection.Get());
        text.Set("xyz");
        field.App.Keyboard.Type("1");
        string afterWrite = text.Get();
        TextSelection caret = selection.Get();
        _ = field.App.Keyboard.Press(Key.Z, KeyModifiers.Control);
        string undone = text.Get();
        _ = field.App.Keyboard.Press(Key.Z, KeyModifiers.Control);

        Assert.Equal(("abc", TextSelection.Collapsed(3)), typed);
        Assert.Equal(("xyz1", TextSelection.Collapsed(4)), (afterWrite, caret));
        Assert.Equal("xyz", undone);
        Assert.Equal("xyz", text.Get());
    }

    /// <summary>
    /// The moves of the keys' table: by cluster, by word with Control, to the line's ends with Home and End and to the text's with
    /// Control, Shift to extend, and a selection collapsed to its end by an arrow; Control+A selects all. Up, Down, PageUp, and
    /// PageDown in a field of one line are not stopped.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A field of one line holding "one two three", with the focus (which selected all).
    ///
    /// Steps:
    /// - Left (the selection collapses to its start); End; Control+Left; Shift+Control+Left; Right; Home; Shift+End; Control+A;
    ///   End; Shift+Left; Shift+Right; Shift+Home; Control+Home; Control+End. Then Up, Down, PageUp, and PageDown.
    ///
    /// Expected:
    /// - Carets at 0, 13, 8; the selection 8 to 4; the caret at 8 (Right collapses to the end); 0; the selection 0 to 13; again 0
    ///   to 13; the caret at 13; the selection 13 to 12; the caret at 13; the selection 13 to 0; the carets at 0 and 13. Each of
    ///   these keys was stopped. None of the four vertical keys was stopped, and the caret stays at 13.
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task MoveKeys_GoByClusterWordAndEnd_AndShiftExtends()
    {
        var selection = Signal.Create(default(TextSelection));
        await using Field field = await Field.StartAsync(Signal.Create("one two three"), new TextFieldOptions { Selection = selection });
        var steps = new List<TextSelection>();
        var stopped = new List<bool>();
        void Press(Key key, KeyModifiers modifiers = KeyModifiers.None)
        {
            stopped.Add(field.App.Keyboard.Press(key, modifiers));
            steps.Add(selection.Get());
        }

        Press(Key.Left);
        Press(Key.End);
        Press(Key.Left, KeyModifiers.Control);
        Press(Key.Left, KeyModifiers.Control | KeyModifiers.Shift);
        Press(Key.Right);
        Press(Key.Home);
        Press(Key.End, KeyModifiers.Shift);
        Press(Key.A, KeyModifiers.Control);
        Press(Key.End);
        Press(Key.Left, KeyModifiers.Shift);
        Press(Key.Right, KeyModifiers.Shift);
        Press(Key.Home, KeyModifiers.Shift);
        Press(Key.Home, KeyModifiers.Control);
        Press(Key.End, KeyModifiers.Control);
        var vertical = new List<bool>();
        foreach (Key key in new[] { Key.Up, Key.Down, Key.PageUp, Key.PageDown })
        {
            vertical.Add(field.App.Keyboard.Press(key));
        }

        Assert.Equal(
            [
                TextSelection.Collapsed(0),
                TextSelection.Collapsed(13),
                TextSelection.Collapsed(8),
                new TextSelection(8, 4),
                TextSelection.Collapsed(8),
                TextSelection.Collapsed(0),
                new TextSelection(0, 13),
                new TextSelection(0, 13),
                TextSelection.Collapsed(13),
                new TextSelection(13, 12),
                TextSelection.Collapsed(13),
                new TextSelection(13, 0),
                TextSelection.Collapsed(0),
                TextSelection.Collapsed(13),
            ],
            steps
        );
        Assert.All(stopped, Assert.True);
        Assert.Equal([false, false, false, false], vertical);
        Assert.Equal(TextSelection.Collapsed(13), selection.Get());
    }

    /// <summary>
    /// Backspace and Delete delete a cluster or the selection, with Control to the word's boundary; a read-only field takes no
    /// deletion and no typing.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A field of one line holding "one two", with the focus and the caret at the end; and a read-only one holding "fixed".
    ///
    /// Steps:
    /// - Backspace; Control+Backspace; Home and Delete; Control+Delete. In the read-only field: Backspace, type "x".
    ///
    /// Expected:
    /// - "one tw", "one ", "ne ", " "; the read-only field stays "fixed".
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task DeleteKeys_DeleteClusterWordOrSelection_AndReadOnlyTakesNone()
    {
        var text = Signal.Create("one two");
        await using (Field field = await Field.StartAsync(text))
        {
            _ = field.App.Keyboard.Press(Key.End);
            var after = new List<string>();
            _ = field.App.Keyboard.Press(Key.Back);
            after.Add(text.Get());
            _ = field.App.Keyboard.Press(Key.Back, KeyModifiers.Control);
            after.Add(text.Get());
            _ = field.App.Keyboard.Press(Key.Home);
            _ = field.App.Keyboard.Press(Key.Delete);
            after.Add(text.Get());
            _ = field.App.Keyboard.Press(Key.Delete, KeyModifiers.Control);
            after.Add(text.Get());
            Assert.Equal(["one tw", "one ", "ne ", " "], after);
        }

        var fixedText = Signal.Create("fixed");
        await using Field readOnly = await Field.StartAsync(fixedText, new TextFieldOptions { ReadOnly = true });
        _ = readOnly.App.Keyboard.Press(Key.End);
        _ = readOnly.App.Keyboard.Press(Key.Back);
        readOnly.App.Keyboard.Type("x");
        Assert.Equal("fixed", fixedText.Get());
    }

    /// <summary>
    /// Copy, cut, and paste go through the app's clipboard; a field of one line keeps a pasted text's first line; a hidden text is
    /// neither copied nor cut.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A field of one line holding "hello world", with the focus; then a hidden one holding "secret".
    ///
    /// Steps:
    /// - Select "hello" (Home, then Shift+Control+Right); Control+C; Control+X; put "one\ntwo" on the clipboard and Control+V. In the
    ///   hidden field, Control+A and Control+C, and Control+X.
    ///
    /// Expected:
    /// - The clipboard holds "hello" after the copy and the cut; the text is " world" after the cut and "one world" after the paste.
    ///   The hidden field leaves the clipboard and its text as they were.
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task Clipboard_CopyCutPaste_AndAHiddenTextIsNotCopied()
    {
        var text = Signal.Create("hello world");
        await using (Field field = await Field.StartAsync(text))
        {
            _ = field.App.Keyboard.Press(Key.Home);
            _ = field.App.Keyboard.Press(Key.Right, KeyModifiers.Control | KeyModifiers.Shift);
            _ = field.App.Keyboard.Press(Key.C, KeyModifiers.Control);
            string? copied = field.App.Clipboard.Text;
            _ = field.App.Keyboard.Press(Key.X, KeyModifiers.Control);
            (string? Clipboard, string Text) cut = (field.App.Clipboard.Text, text.Get());
            field.App.Clipboard.Text = "one\ntwo";
            _ = field.App.Keyboard.Press(Key.V, KeyModifiers.Control);

            Assert.Equal("hello", copied);
            Assert.Equal(("hello", " world"), cut);
            Assert.Equal("one world", text.Get());
        }

        var secret = Signal.Create("secret");
        await using Field hidden = await Field.StartAsync(secret, new TextFieldOptions { Password = true });
        hidden.App.Clipboard.Text = "before";
        _ = hidden.App.Keyboard.Press(Key.A, KeyModifiers.Control);
        _ = hidden.App.Keyboard.Press(Key.C, KeyModifiers.Control);
        _ = hidden.App.Keyboard.Press(Key.X, KeyModifiers.Control);
        Assert.Equal(("before", "secret"), (hidden.App.Clipboard.Text, secret.Get()));
    }

    /// <summary>
    /// Control+Insert copies, Shift+Delete cuts, and Shift+Insert pastes, as Control+C, Control+X, and Control+V do.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A field of one line holding "hello world", with the focus.
    ///
    /// Steps:
    /// - Select "hello" (Home, then Shift+Control+Right); Control+Insert; Shift+Delete; put "hi" on the clipboard and Shift+Insert.
    ///
    /// Expected:
    /// - The clipboard holds "hello" after the copy, and after the cut with the text " world"; the text is "hi world" after the
    ///   paste. Each of the three keys was stopped.
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task Clipboard_InsertAndDeleteKeys_CopyCutAndPaste()
    {
        var text = Signal.Create("hello world");
        await using Field field = await Field.StartAsync(text);
        _ = field.App.Keyboard.Press(Key.Home);
        _ = field.App.Keyboard.Press(Key.Right, KeyModifiers.Control | KeyModifiers.Shift);

        bool copyStopped = field.App.Keyboard.Press(Key.Insert, KeyModifiers.Control);
        string? copied = field.App.Clipboard.Text;
        bool cutStopped = field.App.Keyboard.Press(Key.Delete, KeyModifiers.Shift);
        (string? Clipboard, string Text) cut = (field.App.Clipboard.Text, text.Get());
        field.App.Clipboard.Text = "hi";
        bool pasteStopped = field.App.Keyboard.Press(Key.Insert, KeyModifiers.Shift);

        Assert.Equal("hello", copied);
        Assert.Equal(("hello", " world"), cut);
        Assert.Equal("hi world", text.Get());
        Assert.Equal((true, true, true), (copyStopped, cutStopped, pasteStopped));
    }

    /// <summary>
    /// Control+Z undoes, and Control+Y and Control+Shift+Z each redo what it undid.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - An empty field of one line, with the focus.
    ///
    /// Steps:
    /// - Type "ab"; Control+Z; Control+Y; Control+Z; Control+Shift+Z.
    ///
    /// Expected:
    /// - "", "ab", "", "ab"; each of the four keys was stopped.
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task UndoRedo_ControlYAndControlShiftZ_Redo()
    {
        var text = Signal.Create(string.Empty);
        await using Field field = await Field.StartAsync(text);
        field.App.Keyboard.Type("ab");
        _ = field.App.PumpAndSettle();
        var after = new List<string>();
        var stopped = new List<bool>();
        void Press(Key key, KeyModifiers modifiers)
        {
            stopped.Add(field.App.Keyboard.Press(key, modifiers));
            after.Add(text.Get());
        }

        Press(Key.Z, KeyModifiers.Control);
        Press(Key.Y, KeyModifiers.Control);
        Press(Key.Z, KeyModifiers.Control);
        Press(Key.Z, KeyModifiers.Control | KeyModifiers.Shift);

        Assert.Equal([string.Empty, "ab", string.Empty, "ab"], after);
        Assert.All(stopped, Assert.True);
    }

    /// <summary>
    /// Enter in a field of one line runs <see cref="TextFieldOptions.OnSubmit"/> with the text and is stopped, or goes on with
    /// none; Shift+Enter there goes on. In a field of many lines, Enter and Shift+Enter break the line, and Control+Enter goes on.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A field of one line holding "go" with a submit that logs; one with none; a field of many lines holding "a".
    ///
    /// Steps:
    /// - Press Shift+Enter, then Enter, in the field with the submit; Enter in the one with none. In the field of many lines, after
    ///   Control+End: Enter, Shift+Enter, Control+Enter.
    ///
    /// Expected:
    /// - Shift+Enter was not stopped and logged nothing; Enter logged "go" and was stopped; the press in the field with none was
    ///   not. In the field of many lines, Enter and Shift+Enter were stopped and Control+Enter was not; it holds "a\n\n".
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task Enter_SubmitsOrGoesOn_AndBreaksAMultilineField()
    {
        var log = new List<string>();
        await using (Field submitting = await Field.StartAsync(Signal.Create("go"), new TextFieldOptions { OnSubmit = log.Add }))
        {
            Assert.False(submitting.App.Keyboard.Press(Key.Enter, KeyModifiers.Shift));
            Assert.Empty(log);
            Assert.True(submitting.App.Keyboard.Press(Key.Enter));
        }

        await using (Field plain = await Field.StartAsync(Signal.Create("go")))
        {
            Assert.False(plain.App.Keyboard.Press(Key.Enter));
        }

        var many = Signal.Create("a");
        await using Field multiline = await Field.StartAsync(many, new TextFieldOptions { Multiline = true });
        _ = multiline.App.Keyboard.Press(Key.End, KeyModifiers.Control);
        bool enter = multiline.App.Keyboard.Press(Key.Enter);
        bool shiftEnter = multiline.App.Keyboard.Press(Key.Enter, KeyModifiers.Shift);
        bool controlEnter = multiline.App.Keyboard.Press(Key.Enter, KeyModifiers.Control);

        Assert.Equal(["go"], log);
        Assert.Equal((true, true, false), (enter, shiftEnter, controlEnter));
        Assert.Equal("a\n\n", many.Get());
    }

    /// <summary>
    /// A key that prefers its character (AltGr) is no command: Control+Alt+A with the mark does not select all, and the text it types
    /// goes in.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A field of one line holding "ab", with the focus and the caret at the end.
    ///
    /// Steps:
    /// - Send a down of A with Control and Alt and PrefersCharacter (as AltGr+A); type "ą".
    ///
    /// Expected:
    /// - The down is not stopped, the selection stays the caret at 2, and the text is "abą".
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task AltGr_KeyThatPrefersItsCharacter_IsNoCommand()
    {
        var text = Signal.Create("ab");
        var selection = Signal.Create(default(TextSelection));
        await using Field field = await Field.StartAsync(text, new TextFieldOptions { Selection = selection });
        _ = field.App.Keyboard.Press(Key.End);

        bool stopped = field.App.Input.Handle(
            new KeyInput(
                KeyEventKind.Down,
                Key.A,
                PhysicalKey.None,
                default,
                KeyModifiers.Control | KeyModifiers.Alt,
                false,
                field.App.Time.Elapsed,
                PrefersCharacter: true
            )
        );
        TextSelection after = selection.Get();
        field.App.Keyboard.Type("ą");

        Assert.False(stopped);
        Assert.Equal(TextSelection.Collapsed(2), after);
        Assert.Equal("abą", text.Get());
    }

    /// <summary>
    /// The pointer: a click puts the caret at the nearest cluster edge, Shift+click extends, a double click selects the word, and a
    /// triple click selects all of a field of one line.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A field of one line holding "nnnn nnnn" at the window's top left, with the focus.
    ///
    /// Steps:
    /// - Click at x = 2.4 n; Shift+click at x = 7.6 n; double click at x = 1 n; triple click.
    ///
    /// Expected:
    /// - The caret at 2; the selection 2 to 8 (7.6 n is past the space's middle: 4 n + 3.136 px, rounded to 8); the word 0 to 4; all,
    ///   0 to 9.
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task Pointer_ClickShiftClickDoubleAndTripleClick_Select()
    {
        var selection = Signal.Create(default(TextSelection));
        await using Field field = await Field.StartAsync(Signal.Create("nnnn nnnn"), new TextFieldOptions { Selection = selection });
        var steps = new List<TextSelection>();

        field.Click(2.4f * N, 10);
        steps.Add(selection.Get());
        field.Click(7.6f * N, 10, KeyModifiers.Shift);
        steps.Add(selection.Get());
        field.Click(1 * N, 10, clicks: 2);
        steps.Add(selection.Get());
        field.Click(1 * N, 10, clicks: 3);
        steps.Add(selection.Get());

        Assert.Equal([TextSelection.Collapsed(2), new TextSelection(2, 8), new TextSelection(0, 4), new TextSelection(0, 9)], steps);
    }

    /// <summary>
    /// In a field of many lines, Up and Down move a line at the x the first such move started from, found on the lines' tops; Up on
    /// the first line goes to the start, and Down on the last to the end.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A field of many lines 40 px wide holding "nnn nnn nnn", which wraps into three lines of "nnn" (each line's space hangs at
    ///   its end), with the focus and the caret at 1.
    ///
    /// Steps:
    /// - Down; Down; Up; Up; Up; then Control+End, and Down.
    ///
    /// Expected:
    /// - Carets at 5, 9, 5, 1, 0; then 11 and 11.
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task UpDown_MultilineField_MoveALineAtTheSameX()
    {
        var selection = Signal.Create(default(TextSelection));
        await using Field field = await Field.StartAsync(
            Signal.Create("nnn nnn nnn"),
            new TextFieldOptions { Multiline = true, Selection = selection },
            width: 40
        );
        selection.Set(TextSelection.Collapsed(1));
        var carets = new List<int>();
        foreach (Key key in new[] { Key.Down, Key.Down, Key.Up, Key.Up, Key.Up })
        {
            _ = field.App.Keyboard.Press(key);
            carets.Add(selection.Get().Caret);
        }

        _ = field.App.Keyboard.Press(Key.End, KeyModifiers.Control);
        carets.Add(selection.Get().Caret);
        _ = field.App.Keyboard.Press(Key.Down);
        carets.Add(selection.Get().Caret);

        Assert.Equal([5, 9, 5, 1, 0, 11, 11], carets);
    }

    /// <summary>
    /// In a field of many lines, PageDown and PageUp move by the field's height at the same x, and Shift with Down and Up extends
    /// the selection.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A field of many lines 40 px wide and 41 px high (two lines of 20.272 px) holding "nnn nnn nnn", which wraps into three
    ///   lines of "nnn", with the focus and the caret at 1.
    ///
    /// Steps:
    /// - PageDown; PageUp; Shift+Down; Shift+Up.
    ///
    /// Expected:
    /// - The caret at 9 (41 px down from the first line's middle is the third line); at 1; the selection 1 to 5; the caret at 1.
    ///   Each key was stopped.
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task PageAndShiftVertical_MultilineField_MoveAPageAndExtend()
    {
        var selection = Signal.Create(default(TextSelection));
        await using Field field = await Field.StartAsync(
            Signal.Create("nnn nnn nnn"),
            new TextFieldOptions { Multiline = true, Selection = selection },
            width: 40,
            height: 41
        );
        selection.Set(TextSelection.Collapsed(1));
        var steps = new List<TextSelection>();
        var stopped = new List<bool>();
        void Press(Key key, KeyModifiers modifiers = KeyModifiers.None)
        {
            stopped.Add(field.App.Keyboard.Press(key, modifiers));
            steps.Add(selection.Get());
        }

        Press(Key.PageDown);
        Press(Key.PageUp);
        Press(Key.Down, KeyModifiers.Shift);
        Press(Key.Up, KeyModifiers.Shift);

        Assert.Equal([TextSelection.Collapsed(9), TextSelection.Collapsed(1), new TextSelection(1, 5), TextSelection.Collapsed(1)], steps);
        Assert.All(stopped, Assert.True);
    }

    /// <summary>
    /// A field of one line narrower than its text scrolls sideways to keep the caret in view, to the nearest offset.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A field of one line 50 px wide holding twenty "n", with the focus.
    ///
    /// Steps:
    /// - Press End; pump the frames; read the field's scroll offset. Press Home; pump; read it.
    ///
    /// Expected:
    /// - After End: 20 n + the caret's width less 50; after Home: 0.
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task Scroll_SingleLine_KeepsTheCaretInView()
    {
        await using Field field = await Field.StartAsync(Signal.Create(new string('n', 20)), width: 50);

        _ = field.App.Keyboard.Press(Key.End);
        _ = field.App.PumpAndSettle();
        float end = field.ScrollOffset.X;
        _ = field.App.Keyboard.Press(Key.Home);
        _ = field.App.PumpAndSettle();

        Assert.Equal((20 * N) + 1 - 50, end, 2);
        Assert.Equal(0, field.ScrollOffset.X);
    }

    /// <summary>
    /// The placeholder shows while the field is empty; the most clusters cut the typing.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - An empty field with the placeholder "Name" and at most 3 clusters, with the focus.
    ///
    /// Steps:
    /// - Read whether the placeholder is hidden; type "abcd"; read it again.
    ///
    /// Expected:
    /// - Shown, then hidden; the text is "abc".
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task PlaceholderAndMaxLength_FollowTheText()
    {
        var text = Signal.Create(string.Empty);
        await using Field field = await Field.StartAsync(text, new TextFieldOptions { Placeholder = "Name", MaxLength = 3 });
        Element placeholder = field.App.Find("Name");

        bool hiddenBefore = field.App.Tree.Nodes.GetRef(placeholder.Node).Layout.Style.Hidden;
        field.App.Keyboard.Type("abcd");
        bool hiddenAfter = field.App.Tree.Nodes.GetRef(placeholder.Node).Layout.Style.Hidden;

        Assert.False(hiddenBefore);
        Assert.True(hiddenAfter);
        Assert.Equal("abc", text.Get());
    }

    /// <summary>
    /// The placeholder is drawn where the text starts, inside the field's padding; an empty field that has the focus draws its caret
    /// there, as high as a line, and again once a typed text is deleted.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - An empty field of one line with the placeholder "Name", a red caret, and a padding of 8 by 6, with the focus.
    ///
    /// Steps:
    /// - Read the rectangles of the box the text and the placeholder share, of the placeholder, and of the text leaf (each in its
    ///   parent); and the red rectangles of the last frame. Type "ab" and pump, so that it is shaped; press Backspace twice, pump, and read the red rectangles again.
    ///
    /// Expected:
    /// - The shared box is at (8, 6) in the field, the placeholder and the leaf at (0, 0) in it; the caret is one rectangle at (8, 6)
    ///   in the window (the field is at its top left), 1 px wide and 20 px high: the test font's line at 14 px, 20.272 px, snapped to
    ///   the pixels as the display list snaps a rectangle. After the deletion, the same one rectangle, and the leaf keeps no shaped
    ///   text, so that it draws none of the deleted glyphs.
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task EmptyField_PlaceholderAndCaret_AtTheTextsStart()
    {
        Color red = Color.FromRgb(0xFF0000);
        await using Field field = await Field.StartAsync(
            Signal.Create(string.Empty),
            new TextFieldOptions { Placeholder = "Name", CaretColor = red },
            padding: true
        );
        _ = field.App.PumpAndSettle();
        UiTree tree = field.App.Tree;
        Rect content = tree.GetRect(tree.Nodes.GetRef(field.Element.Node).FirstChild);
        Rect placeholder = tree.GetRect(field.App.Find("Name").Node);
        Rect leaf = tree.GetRect(field.Leaf);
        List<FillRectCommand> carets = field.Fills().FindAll(fill => fill.Color == red);

        Assert.Equal((8f, 6f), (content.X, content.Y));
        Assert.Equal((0f, 0f), (placeholder.X, placeholder.Y));
        Assert.Equal((0f, 0f), (leaf.X, leaf.Y));
        FillRectCommand caret = Assert.Single(carets);
        Assert.Equal((8f, 6f, 1f, 20f), (caret.X, caret.Y, caret.Width, caret.Height));

        field.App.Keyboard.Type("ab");
        _ = field.App.PumpAndSettle();
        _ = field.App.Keyboard.Press(Key.Back);
        _ = field.App.Keyboard.Press(Key.Back);
        _ = field.App.PumpAndSettle();
        FillRectCommand erased = Assert.Single(field.Fills().FindAll(fill => fill.Color == red));
        Assert.Equal((8f, 6f, 1f, 20f), (erased.X, erased.Y, erased.Width, erased.Height));
        Assert.Null(tree.Nodes.GetRef(field.Leaf).Text!.Shaped!.Result);
    }

    /// <summary>
    /// A disabled field (<c>.Disabled()</c>, design §7) takes no focus and does nothing when pressed; disabled while it has the
    /// focus, it loses it and takes no typing.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A field of one line holding "ab" with the focus, bound to a selection signal, whose disabled mark is a signal.
    ///
    /// Steps:
    /// - Set the mark and pump; type "x". Click the field at its start; ask to focus it.
    ///
    /// Expected:
    /// - The field lost the focus; the text stays "ab"; the selection stays 0 to 2 (all, from when it took the focus); the focus
    ///   is refused.
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task Disabled_TakesNoFocusAndNoPress()
    {
        var text = Signal.Create("ab");
        var selection = Signal.Create(default(TextSelection));
        var disabled = Signal.Create(false);
        await using Field field = await Field.StartAsync(text, new TextFieldOptions { Selection = selection }, disabled: disabled);

        disabled.Set(true);
        _ = field.App.PumpAndSettle();
        bool focused = (field.App.Tree.Nodes.GetRef(field.Element.Node).Pseudo & PseudoState.Focused) != 0;
        field.App.Keyboard.Type("x");
        field.Click(1, 10);

        Assert.False(focused);
        Assert.Equal("ab", text.Get());
        Assert.Equal(new TextSelection(0, 2), selection.Get());
        Assert.False(field.App.Focus(field.Element));
    }

    /// <summary>
    /// The selection is drawn with Windows' colors over the selected text while the field has the focus, and kept but not drawn
    /// while it has none; a field of one line selects all when it takes the focus.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A field of one line holding "abcd" and a focusable box beside it; the field has the focus.
    ///
    /// Steps:
    /// - Read the text leaf's selection and the rectangles of the last frame; focus the box; read them and the field's selection;
    ///   focus the field again.
    ///
    /// Expected:
    /// - Focused: the leaf's selection from 0 to 4 with the background and the color of the headless app's selection, and a
    ///   rectangle of that background in the frame; unfocused: no selection and no such rectangle, the field's selection still 0 to
    ///   4; focused again: the leaf's selection and its rectangle again.
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task Selection_DrawnWhileFocused_KeptWhileNot()
    {
        var selection = Signal.Create(default(TextSelection));
        await using Field field = await Field.StartAsync(Signal.Create("abcd"), new TextFieldOptions { Selection = selection }, other: true);

        TextEditingSettings settings = TextEditingSettings.Default;
        bool Highlighted() => field.Fills().Exists(fill => fill.Color == settings.SelectionBackground);

        _ = field.App.PumpAndSettle();
        (List<(int Start, int End, Color? Background, Color? Color)> Runs, bool Drawn) focused = (field.SelectionRuns(), Highlighted());
        Assert.True(field.App.Focus(field.Other));
        _ = field.App.PumpAndSettle();
        (List<(int Start, int End, Color? Background, Color? Color)> Runs, bool Drawn) unfocused = (field.SelectionRuns(), Highlighted());
        TextSelection kept = selection.Get();
        Assert.True(field.App.Focus(field.Element));
        _ = field.App.PumpAndSettle();

        Assert.Equal([(0, 4, settings.SelectionBackground, settings.SelectionForeground)], focused.Runs);
        Assert.True(focused.Drawn);
        Assert.Empty(unfocused.Runs);
        Assert.False(unfocused.Drawn);
        Assert.Equal(new TextSelection(0, 4), kept);
        Assert.Equal([(0, 4, settings.SelectionBackground, settings.SelectionForeground)], field.SelectionRuns());
        Assert.True(Highlighted());
    }

    /// <summary>
    /// With <see cref="HeadlessOptions.CaretBlink"/>, the caret blinks at Windows' interval on the virtual clock while the field has
    /// the focus, each change one frame, and stops when it loses it, so that the app settles.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A blinking headless app with a field of one line and a focusable box; the field has the focus, the caret at the end.
    ///
    /// Steps:
    /// - Pump 530 ms of the clock twice, counting the presented frames; focus the box; pump until settled.
    ///
    /// Expected:
    /// - One frame for each half of the blink; the app settles and ends with no timer due.
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task CaretBlink_FramesOnTheClock_AndStopsWithTheFocus()
    {
        await using Field field = await Field.StartAsync(Signal.Create("ab"), other: true, blink: true);
        _ = field.App.Keyboard.Press(Key.End);
        field.App.Pump(TimeSpan.FromMilliseconds(100));
        int before = field.App.PresentCount;

        field.App.Pump(TimeSpan.FromMilliseconds(530));
        int first = field.App.PresentCount;
        field.App.Pump(TimeSpan.FromMilliseconds(530));
        int second = field.App.PresentCount;
        Assert.True(field.App.Focus(field.Other));
        _ = field.App.PumpAndSettle();

        Assert.Equal((before + 1, before + 2), (first, second));
    }

    /// <summary>
    /// The frames that only draw a focused field allocate nothing: a blink of the caret, with the selection unchanged (design §11.3).
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A blinking headless app with a field of one line holding "abc def" with the focus, the caret at the end, warmed up with 10
    ///   blinks.
    ///
    /// Steps:
    /// - 100 iterations in the windows of <c>PerformanceAssert.DoesNotAllocate</c>: pump 530 ms of the clock, a blink and its frame.
    ///
    /// Expected:
    /// - 0 bytes in every window; each iteration presented one frame.
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task SteadyState_BlinkFrames_AllocateNothing()
    {
        await using Field field = await Field.StartAsync(Signal.Create("abc def"), blink: true);
        _ = field.App.Keyboard.Press(Key.End);
        void Blink(int index) => field.App.Pump(TimeSpan.FromMilliseconds(530));
        for (int i = 0; i < 10; i++)
        {
            Blink(i);
        }

        int presents = field.App.PresentCount;
        int runs = PerformanceAssert.DoesNotAllocate(100, Blink);

        Assert.Equal(presents + runs, field.App.PresentCount);
    }

    // A headless app of one text field at the top left, with the focus, and a focusable box beside it when asked for.
    private sealed class Field : IAsyncDisposable
    {
        private Field(HeadlessApp app, Element field, Element other)
        {
            App = app;
            Element = field;
            Other = other;
        }

        public HeadlessApp App { get; }

        public Element Element { get; }

        public Element Other { get; }

        public Vector2 ScrollOffset => App.Tree.ScrollOffsetOf(Element.Node);

        public static async Task<Field> StartAsync(
            Signal<string> text,
            TextFieldOptions? options = null,
            float width = 300,
            float? height = null,
            bool other = false,
            bool blink = false,
            IReadOnlySignal<bool>? disabled = null,
            bool padding = false
        )
        {
            Element field = default;
            Element box = default;
            HeadlessApp app = await HeadlessApp.StartAsync(
                () =>
                {
                    field = TextField(text, options).Width(width).TestId("field");
                    if (height is { } fixedHeight)
                    {
                        field = field.Height(fixedHeight);
                    }

                    if (disabled is not null)
                    {
                        field = field.Disabled(disabled);
                    }

                    if (padding)
                    {
                        field = field.Padding(8, 6);
                    }

                    return other ? Row(field, box = Box().Width(20).Height(20).Focusable()) : Row(field);
                },
                new HeadlessOptions
                {
                    Width = 400,
                    Height = 200,
                    CaretBlink = blink,
                }
            );

            // A blinking caret keeps its timer while the field has the focus, so the app is settled before the focus is given.
            _ = app.PumpAndSettle();
            Assert.True(app.Focus(field));
            app.Pump(TimeSpan.FromTicks(TimeSpan.TicksPerSecond / 60));
            return new Field(app, field, box);
        }

        // Presses and releases the left button at a point of the field (the window's scale is 1), as the click of a count.
        public void Click(float x, float y, KeyModifiers modifiers = KeyModifiers.None, int clicks = 1)
        {
            App.Pointer.Move((int)MathF.Round(x), (int)MathF.Round(y), modifiers);
            App.Pointer.Down(modifiers: modifiers, clickCount: clicks);
            App.Pointer.Up(modifiers: modifiers);
        }

        // The field's text leaf, the first child of the box it shares with the placeholder.
        public NodeHandle Leaf => App.Tree.Nodes.GetRef(App.Tree.Nodes.GetRef(Element.Node).FirstChild).FirstChild;

        // The rectangles the last frame filled.
        public List<FillRectCommand> Fills()
        {
            var fills = new List<FillRectCommand>();
            DisplayListReader reader = App.Frames.List.GetReader();
            while (reader.TryNext(out CommandTag tag))
            {
                switch (tag)
                {
                    case CommandTag.FillRect:
                        fills.Add(reader.ReadFillRect());
                        break;
                    case CommandTag.GlyphRun:
                        _ = reader.ReadGlyphRun();
                        break;
                    case CommandTag.Shape:
                        _ = reader.ReadShape();
                        break;
                    case CommandTag.Pop:
                        reader.ReadPop();
                        break;
                    case CommandTag.Shadow:
                        _ = reader.ReadShadow();
                        break;
                    case CommandTag.PushOpacity:
                        _ = reader.ReadGroup();
                        break;
                    case CommandTag.PushState:
                        _ = reader.ReadState(out _);
                        break;
                    default:
                        _ = reader.ReadCurve();
                        break;
                }
            }

            return fills;
        }

        // The selection the field's text leaf draws (design §4.10, "Drawing"): its start, end, background, and color; none when
        // it is empty.
        public List<(int Start, int End, Color? Background, Color? Color)> SelectionRuns()
        {
            if (App.Tree.Nodes.GetRef(Leaf).Text!.Selection is not { IsEmpty: false } selection)
            {
                return [];
            }

            return [(selection.Start, selection.End, selection.Background, selection.Foreground)];
        }

        public async ValueTask DisposeAsync()
        {
            // A blinking caret keeps a timer: the field gives up the focus first, which stops it.
            App.Tree.Focus.Lose(Element.Node);
            _ = App.PumpAndSettle();
            await App.DisposeAsync();
        }
    }
}
