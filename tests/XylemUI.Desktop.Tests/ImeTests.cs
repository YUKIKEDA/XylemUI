using System.Numerics;
using XylemUI.Primitives;
using XylemUI.Testing.XUnit;
using static XylemUI.Elements;
using static XylemUI.Primitives.Primitive;

namespace XylemUI.Desktop.Tests;

// The text field as the client of the OS's input method (row P02c, design §4.7) in a headless app: its composition, the
// underlines of the clauses, its commit and cancel, the maximum length and the undo of a commit, the focus, the app's writes, the
// keys while composing, the fields that take no input method, and what the field tells the input method.
public sealed class ImeTests
{
    private static readonly ImeAttributeRun Target = new(0, 2, DecorationStyle.Solid, Thick: true);

    /// <summary>
    /// A composition writes its text into the field at the selection, with the caret where the input method puts it, and its
    /// commit leaves the text; the text signal takes the committed text only.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A field of one line holding "ab", bound to a text signal, with the focus and the caret at 1.
    ///
    /// Steps:
    /// - Compose "にほんご" with the caret at 4; compose "日本語" with the caret at 3 and one thick clause over "日本"; commit
    ///   "日本語".
    ///
    /// Expected:
    /// - After the first: the field's text "aにほんごb", the composition 1 to 5, the caret at 5, the signal still "ab". After the
    ///   second: the composition 1 to 4, the caret at 4. After the commit: no composition, the signal "a日本語b", the caret at 4.
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task Compose_UpdateAndCommit_PutTheTextAtTheSelection_AndTheSignalTakesTheCommit()
    {
        var text = Signal.Create("ab");
        await using Field field = await Field.StartAsync(text);
        field.Client.Select(TextSelection.Collapsed(1));

        _ = field.App.Ime.Compose("にほんご", cursor: 4);
        (string, (int, int)?, TextSelection, string) first = (field.Text, field.Client.Composition, field.Client.Selection, text.Get());
        _ = field.App.Ime.Compose("日本語", cursor: 3, clauses: [Target]);
        ((int, int)?, TextSelection) second = (field.Client.Composition, field.Client.Selection);
        _ = field.App.Ime.Commit("日本語");

        Assert.Equal(("aにほんごb", (1, 5), TextSelection.Collapsed(5), "ab"), first);
        Assert.Equal(((1, 4), TextSelection.Collapsed(4)), second);
        Assert.Null(field.Client.Composition);
        Assert.Equal("a日本語b", text.Get());
        Assert.Equal(TextSelection.Collapsed(4), field.Client.Selection);
    }

    /// <summary>
    /// A cancel puts back the text the composition replaced and the selection it started from, and leaves nothing to undo.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A field of one line holding "abc" with the focus, all selected (a field of one line selects all when it takes the focus).
    ///
    /// Steps:
    /// - Compose "にほ" over the selection; cancel; press Control+Z.
    ///
    /// Expected:
    /// - After the cancel, the text "abc", all of it selected again, no composition; the undo changes nothing.
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task Cancel_PutsBackTheReplacedText_AndRecordsNoUndo()
    {
        var text = Signal.Create("abc");
        await using Field field = await Field.StartAsync(text);

        _ = field.App.Ime.Compose("にほ", cursor: 2).Cancel();
        string cancelled = field.Text;
        TextSelection selection = field.Client.Selection;
        _ = field.App.Keyboard.Press(Key.Z, KeyModifiers.Control);

        Assert.Equal("abc", cancelled);
        Assert.Equal(new TextSelection(0, 3), selection);
        Assert.Null(field.Client.Composition);
        Assert.Equal("abc", text.Get());
    }

    /// <summary>
    /// Each clause is a range of the leaf with the underline its attribute asks for: its shape, twice the thickness when thick,
    /// its colors, and marked as a clause's (shortened at each end, not cut at the glyphs); a clause with no line has no underline,
    /// and the text around the composition has no range.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - An empty field of one line with the focus.
    ///
    /// Steps:
    /// - Compose "日本語です" with three clauses: "日本" thick and solid, "語" dotted with a red line and a blue text, "です" with
    ///   no line.
    ///
    /// Expected:
    /// - The leaf's ranges of look: 0 to 2 a clause's solid underline, thickness 2; 2 to 3 a clause's dotted underline, thickness
    ///   1, red, and blue text; 3 to 5 no decoration.
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task Clauses_AreRangesWithTheirUnderlines()
    {
        Color red = Color.FromRgb(0xFF0000);
        Color blue = Color.FromRgb(0x0000FF);
        await using Field field = await Field.StartAsync(Signal.Create(string.Empty));

        _ = field.App.Ime.Compose(
            "日本語です",
            cursor: 5,
            clauses: [Target, new(2, 3, DecorationStyle.Dotted, Thick: false, LineColor: red, TextColor: blue), new(3, 5, null, Thick: false)]
        );
        _ = field.App.PumpAndSettle();
        TextRanges ranges = field.App.Tree.Nodes.GetRef(field.Leaf).Text!.Ranges!;
        var looks = new List<(int, int, TextDecoration?, Color?)>();
        foreach (PaintRun run in ranges.Paint)
        {
            looks.Add((run.Start, run.Start + run.Length, run.Decoration, run.Color));
        }

        var solid = TextDecoration.Clause(DecorationStyle.Solid, null, 2);
        var dotted = TextDecoration.Clause(DecorationStyle.Dotted, red, 1);
        Assert.Equal([(0, 2, solid, null), (2, 3, dotted, blue), (3, 5, null, null)], looks);
    }

    /// <summary>
    /// Two clauses side by side that look the same keep a range each, so that each underline is shortened at its own ends.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - An empty field of one line with the focus.
    ///
    /// Steps:
    /// - Compose "にほんご" with two clauses, "にほ" and "んご", both thin and dotted, as Google Japanese Input marks the text not
    ///   converted and the other clauses.
    ///
    /// Expected:
    /// - The leaf's ranges of look are 0 to 2 and 2 to 4, each a clause's dotted underline.
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task Clauses_ThatLookTheSame_KeepARangeEach()
    {
        await using Field field = await Field.StartAsync(Signal.Create(string.Empty));
        var dotted = new ImeAttributeRun(0, 2, DecorationStyle.Dotted, Thick: false);

        _ = field.App.Ime.Compose("にほんご", cursor: 4, clauses: [dotted, dotted with { Start = 2, End = 4 }]);
        _ = field.App.PumpAndSettle();
        var looks = new List<(int, int, TextDecoration?)>();
        foreach (PaintRun run in field.App.Tree.Nodes.GetRef(field.Leaf).Text!.Ranges!.Paint)
        {
            looks.Add((run.Start, run.Start + run.Length, run.Decoration));
        }

        TextDecoration line = TextDecoration.Clause(DecorationStyle.Dotted, null, 1);
        Assert.Equal([(0, 2, line), (2, 4, line)], looks);
    }

    /// <summary>
    /// While composing, the keys are the input method's: no key is a command of the field, and typed text does not go in.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - An empty field of one line with the focus, composing "にほ" with the caret at 2.
    ///
    /// Steps:
    /// - Press Left, Back, Control+A, and Enter; type "x".
    ///
    /// Expected:
    /// - Each key was stopped; the text stays "にほ", the caret at 2, and the composition 0 to 2.
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task Keys_WhileComposing_AreNoCommands()
    {
        await using Field field = await Field.StartAsync(Signal.Create(string.Empty));
        _ = field.App.Ime.Compose("にほ", cursor: 2);

        bool[] stopped =
        [
            field.App.Keyboard.Press(Key.Left),
            field.App.Keyboard.Press(Key.Back),
            field.App.Keyboard.Press(Key.A, KeyModifiers.Control),
            field.App.Keyboard.Press(Key.Enter),
        ];
        _ = field.App.Keyboard.Type("x");

        Assert.Equal([true, true, true, true], stopped);
        Assert.Equal("にほ", field.Text);
        Assert.Equal(TextSelection.Collapsed(2), field.Client.Selection);
        Assert.Equal((0, 2), field.Client.Composition);
    }

    /// <summary>
    /// The input method is on while a field has the focus and off when a node that takes no text takes it; moving the focus away
    /// commits the composition.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - An empty field of one line with the focus, and a focusable box beside it.
    ///
    /// Steps:
    /// - Compose "にほ"; focus the box; focus the field again.
    ///
    /// Expected:
    /// - Attached while the field has the focus; detached on the box, with the composition committed: the signal "にほ"; attached
    ///   again on the field.
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task Focus_AttachesTheField_AndMovingAwayCommits()
    {
        var text = Signal.Create(string.Empty);
        await using Field field = await Field.StartAsync(text, other: true);

        bool before = field.App.Ime.IsAttached;
        _ = field.App.Ime.Compose("にほ", cursor: 2);
        Assert.True(field.App.Focus(field.Other));
        bool onBox = field.App.Ime.IsAttached;
        string committed = text.Get();
        Assert.True(field.App.Focus(field.Element));

        Assert.True(before);
        Assert.False(onBox);
        Assert.Equal("にほ", committed);
        Assert.Null(field.Client.Composition);
        Assert.True(field.App.Ime.IsAttached);
    }

    /// <summary>
    /// The client stays attached while the window is not active, as the OS keeps a window's input context, and is detached when
    /// the focused field is hidden, which commits the composition.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - An empty field of one line with the focus, composing "にほ".
    ///
    /// Steps:
    /// - Make the window not active; make it active; hide the field.
    ///
    /// Expected:
    /// - Attached and still composing while the window is not active; after the hide, detached, and the signal "にほ".
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task Attachment_StaysWhileTheWindowIsInactive_AndAHiddenFieldDetaches()
    {
        var text = Signal.Create(string.Empty);
        await using Field field = await Field.StartAsync(text);
        _ = field.App.Ime.Compose("にほ", cursor: 2);

        field.App.SetActive(false);
        (bool, bool) inactive = (field.App.Ime.IsAttached, field.App.Ime.IsComposing);
        field.App.SetActive(true);
        UiTree tree = field.App.Tree;
        tree.SetStyle(field.Element.Node, tree.Nodes.GetRef(field.Element.Node).Layout.Style with { Hidden = true });
        _ = field.App.PumpAndSettle();

        Assert.Equal((true, true), inactive);
        Assert.False(field.App.Ime.IsAttached);
        Assert.Equal("にほ", text.Get());
    }

    /// <summary>
    /// An app that writes the text while the input method composes has the composition committed first, then its value put.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A field of one line holding "a" with the caret at its end, composing "にほ".
    ///
    /// Steps:
    /// - The app writes "xyz" to the text signal.
    ///
    /// Expected:
    /// - The input method composes no more; the field holds "xyz".
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task AppWrite_WhileComposing_CommitsFirst()
    {
        var text = Signal.Create("a");
        await using Field field = await Field.StartAsync(text);
        field.Client.Select(TextSelection.Collapsed(1));
        _ = field.App.Ime.Compose("にほ", cursor: 2);

        text.Set("xyz");
        _ = field.App.PumpAndSettle();

        Assert.False(field.App.Ime.IsComposing);
        Assert.Equal("xyz", field.Text);
    }

    /// <summary>
    /// The position under a point of the window is the field's, the nearest one beside the text, for an input method that asks
    /// for a press on the text being composed.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A field of one line holding "abc", 300 wide at the window's left, with the focus.
    ///
    /// Steps:
    /// - Ask the client for the position under the window's 0, 5, and under 290, 5.
    ///
    /// Expected:
    /// - 0, then 3: the end of the text, the nearest to a point past it.
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task PositionAt_IsTheFieldsPositionUnderAPoint()
    {
        await using Field field = await Field.StartAsync(Signal.Create("abc"));

        Assert.True(field.Client.PositionAt(new Vector2(0, 5), out int start));
        Assert.True(field.Client.PositionAt(new Vector2(290, 5), out int end));

        Assert.Equal((0, 3), (start, end));
    }

    /// <summary>
    /// A press in the field while the input method composes commits the composition before it moves the caret.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - An empty field of one line, 300 wide, with the focus, composing "にほ".
    ///
    /// Steps:
    /// - Click the field's center, past the end of its text.
    ///
    /// Expected:
    /// - The input method composes no more; the signal "にほ"; the caret at 2.
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task Press_WhileComposing_CommitsFirst()
    {
        var text = Signal.Create(string.Empty);
        await using Field field = await Field.StartAsync(text);
        _ = field.App.Ime.Compose("にほ", cursor: 2);

        field.App.Pointer.Click(field.Element);

        Assert.False(field.App.Ime.IsComposing);
        Assert.Equal("にほ", text.Get());
        Assert.Equal(TextSelection.Collapsed(2), field.Client.Selection);
    }

    /// <summary>
    /// A field that hides its text and a read-only field take no input method: a composition into either throws. The read-only
    /// field's client is still attached, read-only.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A field with <see cref="TextFieldOptions.Password"/>, then a field with <see cref="TextFieldOptions.ReadOnly"/>, each with
    ///   the focus.
    ///
    /// Steps:
    /// - Compose "にほ" into each.
    ///
    /// Expected:
    /// - <see cref="InvalidOperationException"/> both times; the password field's kind is Password; the read-only field's client is
    ///   read-only; neither text changed.
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task PasswordAndReadOnly_TakeNoInputMethod()
    {
        await using (Field hidden = await Field.StartAsync(Signal.Create("pw"), new TextFieldOptions { Password = true }))
        {
            _ = Assert.Throws<InvalidOperationException>(() => hidden.App.Ime.Compose("にほ", cursor: 2));
            Assert.Equal(TextInputKind.Password, hidden.Client.Kind);
            Assert.Equal("pw", hidden.Text);
        }

        await using Field readOnly = await Field.StartAsync(Signal.Create("ro"), new TextFieldOptions { ReadOnly = true });
        _ = Assert.Throws<InvalidOperationException>(() => readOnly.App.Ime.Compose("にほ", cursor: 2));
        Assert.True(readOnly.App.Ime.IsAttached);
        Assert.True(readOnly.Client.ReadOnly);
        Assert.Equal("ro", readOnly.Text);
    }

    /// <summary>
    /// A composition may go past the maximum length; its commit is cut to the room, and the cut is told to the input method, text
    /// and selection both.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A field of one line with a maximum length of 3, holding "a" with the caret at its end, with the focus.
    ///
    /// Steps:
    /// - Compose "にほんご" (5 clusters in the field while composing); clear the told changes; commit "にほんご".
    ///
    /// Expected:
    /// - While composing, the field holds "aにほんご"; after the commit, "aにほ" with the caret at 3; the input method was told one
    ///   change of the text, from 3 to 5 becoming 3 to 3, and one change of the selection.
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task MaxLength_CutsTheCommit_AndTellsTheInputMethod()
    {
        var text = Signal.Create("a");
        await using Field field = await Field.StartAsync(text, new TextFieldOptions { MaxLength = 3 });
        field.Client.Select(TextSelection.Collapsed(1));

        _ = field.App.Ime.Compose("にほんご", cursor: 4);
        string composing = field.Text;
        field.App.Ime.ClearChanges();
        _ = field.App.Ime.Commit("にほんご");

        Assert.Equal("aにほんご", composing);
        Assert.Equal("aにほ", text.Get());
        Assert.Equal(TextSelection.Collapsed(3), field.Client.Selection);
        Assert.Equal([(3, 5, 3)], field.App.Ime.TextChanges);
        Assert.Equal(1, field.App.Ime.SelectionChanges);
    }

    /// <summary>
    /// One composition is one undo, apart from the typing just before it; the text in between is never in the history.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - An empty field of one line with the focus.
    ///
    /// Steps:
    /// - Type "ab"; compose "に", then "にほ", then "日本"; commit "日本"; press Control+Z; press Control+Z.
    ///
    /// Expected:
    /// - "ab日本"; the first undo gives "ab", the second "".
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task Undo_OneCompositionIsOneStep_ApartFromTheTypingBefore()
    {
        var text = Signal.Create(string.Empty);
        await using Field field = await Field.StartAsync(text);

        _ = field.App.Keyboard.Type("ab");
        _ = field.App.Ime.Compose("に", cursor: 1).Compose("にほ", cursor: 2).Compose("日本", cursor: 2, clauses: [Target]).Commit("日本");
        string committed = text.Get();
        _ = field.App.Keyboard.Press(Key.Z, KeyModifiers.Control);
        string first = text.Get();
        _ = field.App.Keyboard.Press(Key.Z, KeyModifiers.Control);

        Assert.Equal("ab日本", committed);
        Assert.Equal("ab", first);
        Assert.Equal(string.Empty, text.Get());
    }

    /// <summary>
    /// The field tells the input method what changed without it: typing, a paste, an undo, a move of the caret by a key, and an
    /// app's write; and not what the input method changed itself.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - An empty field of one line with the focus; "z" on the clipboard.
    ///
    /// Steps:
    /// - Clear the told changes; type "ab"; press Left; press Control+V; press Control+Z; the app writes "q"; then clear, compose
    ///   "に" and commit it.
    ///
    /// Expected:
    /// - The text changes 0 to 0 becoming 0 to 1, 1 to 1 becoming 1 to 2, 1 to 1 becoming 1 to 2 (the paste), 1 to 2 becoming 1 to
    ///   1 (the undo), 0 to 2 becoming 0 to 1 (the write); a change of the selection with each step but the write, whose caret
    ///   stays at 1. After the composition, none of either.
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task Changes_WithoutTheInputMethod_AreTold_AndItsOwnAreNot()
    {
        var text = Signal.Create(string.Empty);
        await using Field field = await Field.StartAsync(text);
        _ = field.App.Clipboard.SetText("z");

        field.App.Ime.ClearChanges();
        _ = field.App.Keyboard.Type("ab");
        _ = field.App.Keyboard.Press(Key.Left);
        _ = field.App.Keyboard.Press(Key.V, KeyModifiers.Control);
        _ = field.App.Keyboard.Press(Key.Z, KeyModifiers.Control);
        text.Set("q");
        _ = field.App.PumpAndSettle();
        (int, int, int)[] told = [.. field.App.Ime.TextChanges];
        int selections = field.App.Ime.SelectionChanges;
        field.App.Ime.ClearChanges();
        _ = field.App.Ime.Compose("に", cursor: 1).Commit("に");

        Assert.Equal([(0, 0, 1), (1, 1, 2), (1, 1, 2), (1, 2, 1), (0, 2, 1)], told);
        Assert.Equal(5, selections);
        Assert.Empty(field.App.Ime.TextChanges);
        Assert.Equal(0, field.App.Ime.SelectionChanges);
    }

    /// <summary>
    /// The box of a range is where its text is in the window, for the candidate window; a move of the field on the screen is told to
    /// the input method after the frame.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A field of one line under a box 10 high, with the focus, composing "にほ".
    ///
    /// Steps:
    /// - Read the box of 0 to 2; clear the told changes; make the box 30 high and pump.
    ///
    /// Expected:
    /// - The box starts at the field's left and 10 down, and is wider than 0; after the move, one change of the layout was told,
    ///   and the box of 0 to 2 is 20 lower.
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task BoundsOf_IsInTheWindow_AndAMoveIsToldAfterTheFrame()
    {
        await using Field field = await Field.StartAsync(Signal.Create(string.Empty), above: true);
        _ = field.App.Ime.Compose("にほ", cursor: 2);
        _ = field.App.PumpAndSettle();

        Rect before = field.App.Ime.BoundsOf(0, 2)!.Value;
        field.App.Ime.ClearChanges();
        UiTree tree = field.App.Tree;
        tree.SetStyle(field.Above.Node, tree.Nodes.GetRef(field.Above.Node).Layout.Style with { Height = Length.Px(30) });
        _ = field.App.PumpAndSettle();
        Rect after = field.App.Ime.BoundsOf(0, 2)!.Value;

        Assert.Equal(0, before.X, 3);
        Assert.Equal(10, before.Y, 3);
        Assert.True(before.Width > 0);
        Assert.Equal(1, field.App.Ime.LayoutChanges);
        Assert.Equal(before.Y + 20, after.Y, 3);
    }

    // A field in a headless app, with the focus, a focusable box beside it when asked, and a box 10 high above it when asked.
    private sealed class Field : IAsyncDisposable
    {
        private Field(HeadlessApp app, Element field, Element other, Element above)
        {
            App = app;
            Element = field;
            Other = other;
            Above = above;
        }

        public HeadlessApp App { get; }

        public Element Element { get; }

        public Element Other { get; }

        public Element Above { get; }

        // The field's client of the text input, which the input method reads.
        public ITextInputClient Client => App.Tree.TextInputClientOf(Element.Node)!;

        // The field's text, the text being composed included.
        public string Text
        {
            get
            {
                var chars = new char[Client.Length];
                Client.CopyText(0, chars);
                return new string(chars);
            }
        }

        // The field's text leaf, the first child of the box it shares with the placeholder.
        public NodeHandle Leaf => App.Tree.Nodes.GetRef(App.Tree.Nodes.GetRef(Element.Node).FirstChild).FirstChild;

        public static async Task<Field> StartAsync(Signal<string> text, TextFieldOptions? options = null, bool other = false, bool above = false)
        {
            Element field = default;
            Element box = default;
            Element top = default;
            HeadlessApp app = await HeadlessApp.StartAsync(
                () =>
                {
                    field = TextField(text, options).Width(300).TestId("field");
                    Element row = other ? Row(field, box = Box().Width(20).Height(20).Focusable()) : Row(field);
                    return above ? Column(top = Box().Width(10).Height(10), row) : row;
                },
                new HeadlessOptions
                {
                    Width = 400,
                    Height = 200,
                    CaretBlink = false,
                }
            );

            _ = app.PumpAndSettle();
            Assert.True(app.Focus(field));
            _ = app.PumpAndSettle();
            return new Field(app, field, box, top);
        }

        public async ValueTask DisposeAsync()
        {
            App.Tree.Focus.Lose(Element.Node);
            _ = App.PumpAndSettle();
            await App.DisposeAsync();
        }
    }
}
