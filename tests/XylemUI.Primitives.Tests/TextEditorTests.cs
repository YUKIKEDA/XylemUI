namespace XylemUI.Primitives.Tests;

// The text field's editing model (row P02b, design §4.9): moves by cluster and by word, deletion, the selection's ends, the most
// clusters, the pasted text of a single-line field, a hidden text's words, and the undo's groups. It reads no font, so it runs on
// every OS.
public sealed class TextEditorTests
{
    private static readonly TimeSpan Start = TimeSpan.FromSeconds(1);

    /// <summary>
    /// A move by character goes by grapheme cluster: over a surrogate pair and a combining mark as one; a selection's end is moved to
    /// a cluster's start.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - The text "a😀éb": a, an emoji (two code units), e with a combining acute, b.
    ///
    /// Steps:
    /// - From 0, the positions one cluster forward, one after another; from the end, one back, one after another. Select from 0 to
    ///   2 (inside the emoji's pair).
    ///
    /// Expected:
    /// - 1, 3, 5, 6 forward; 5, 3, 1, 0 back; the selection's caret at 1.
    /// </remarks>
    [Fact]
    public void Character_GoesByGraphemeCluster_AndASelectionSnapsToOne()
    {
        var editor = new TextEditor(multiline: false);
        editor.Reset("a😀éb");

        var forward = new List<int>();
        for (int at = 0; at < editor.Text.Length; )
        {
            at = editor.Character(at, forward: true);
            forward.Add(at);
        }

        var back = new List<int>();
        for (int at = editor.Text.Length; at > 0; )
        {
            at = editor.Character(at, forward: false);
            back.Add(at);
        }

        editor.Select(new TextSelection(0, 2));

        Assert.Equal([1, 3, 5, 6], forward);
        Assert.Equal([5, 3, 1, 0], back);
        Assert.Equal(new TextSelection(0, 1), editor.Selection);
    }

    /// <summary>
    /// A move by word stops at the next word boundary whose word is not spaces and punctuation alone (Flutter's
    /// <c>moveByWordBoundary</c>); katakana in a row is one word, kanji are one each.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - The text "hello, world  カタカナ漢字".
    ///
    /// Steps:
    /// - From 0, the positions one word forward, one after another; from the end, one word back, one after another.
    ///
    /// Expected:
    /// - Forward: 5 (after "hello"), 12 (after "world"), 18 (after the katakana), 19, 20. Back: 19, 18, 14, 7, 0.
    /// </remarks>
    [Fact]
    public void Word_SkipsSpacesAndPunctuation_AndTakesKatakanaTogether()
    {
        var editor = new TextEditor(multiline: false);
        editor.Reset("hello, world  カタカナ漢字");

        var forward = new List<int>();
        for (int at = 0; at < editor.Text.Length; )
        {
            at = editor.Word(at, forward: true);
            forward.Add(at);
        }

        var back = new List<int>();
        for (int at = editor.Text.Length; at > 0; )
        {
            at = editor.Word(at, forward: false);
            back.Add(at);
        }

        Assert.Equal([5, 12, 18, 19, 20], forward);
        Assert.Equal([19, 18, 14, 7, 0], back);
    }

    /// <summary>
    /// Backspace and Delete delete a cluster, or the selection; with Control, to the word's boundary; a hidden text's word moves and
    /// deletions go to its end.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - The text "one two three", the caret at its end.
    ///
    /// Steps:
    /// - Backspace; Control+Backspace; select "one" and Delete; then hide the text, put the caret at 1, and Control+Delete.
    ///
    /// Expected:
    /// - "one two thre"; "one two "; " two "; with the hidden text, " " is left of " two ": the deletion took the rest.
    /// </remarks>
    [Fact]
    public void Delete_ClusterWordOrSelection_AndAHiddenTextsWordIsTheRest()
    {
        var editor = new TextEditor(multiline: false);
        editor.Reset("one two three");

        _ = editor.Delete(forward: false, word: false, Start);
        string afterOne = editor.Text;
        _ = editor.Delete(forward: false, word: true, Start);
        string afterWord = editor.Text;
        editor.Select(new TextSelection(0, 3));
        _ = editor.Delete(forward: true, word: false, Start);
        string afterSelection = editor.Text;
        editor.Password = true;
        editor.MoveTo(1, extend: false);
        _ = editor.Delete(forward: true, word: true, Start);

        Assert.Equal(("one two thre", "one two ", " two "), (afterOne, afterWord, afterSelection));
        Assert.Equal(" ", editor.Text);
    }

    /// <summary>
    /// The most clusters cut typing and pasting to fit, a replaced selection counted out; a single-line field keeps a pasted text's
    /// first line, and a multi-line one all of it.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A single-line editor with at most 4 clusters, empty; and a multi-line one with none.
    ///
    /// Steps:
    /// - Type "ab"; paste "c😀de"; select "a" and type "xyz". Paste "first\r\nsecond" into the multi-line one and into an empty
    ///   single-line one with no limit.
    ///
    /// Expected:
    /// - "ab"; "abc😀"; "xbc😀" (one cluster of room); the multi-line text holds both lines, the single-line one "first".
    /// </remarks>
    [Fact]
    public void MaxLengthAndSingleLinePaste_CutTheText()
    {
        var limited = new TextEditor(multiline: false) { MaxLength = 4 };
        limited.Reset(string.Empty);
        _ = limited.Type("ab", Start);
        string typed = limited.Text;
        _ = limited.Paste("c😀de", Start);
        string pasted = limited.Text;
        limited.Select(new TextSelection(0, 1));
        _ = limited.Type("xyz", Start);

        var many = new TextEditor(multiline: true);
        many.Reset(string.Empty);
        _ = many.Paste("first\r\nsecond", Start);
        var one = new TextEditor(multiline: false);
        one.Reset(string.Empty);
        _ = one.Paste("first\r\nsecond", Start);

        Assert.Equal(("ab", "abc😀", "xbc😀"), (typed, pasted, limited.Text));
        Assert.Equal(("first\r\nsecond", "first"), (many.Text, one.Text));
    }

    /// <summary>
    /// Edits less than 300 ms apart are one group of undo; a move, a paste, and a cut each start a group; an edit after an undo
    /// forgets the redo; a text set from outside forgets the undo.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - An empty editor.
    ///
    /// Steps:
    /// - Type "a", "b", "c" 100 ms apart; type "d" 400 ms later; move the caret to 0 and type "x"; paste "P". Undo four times, redo
    ///   once, type "z"; then reset the text from outside.
    ///
    /// Expected:
    /// - After each undo: "xabcd" (the paste), "abcd" (the "x"), "abc" (the "d"), "" (the "abc"); the redo gives "abc"; "z" leaves
    ///   no redo; the reset leaves no undo.
    /// </remarks>
    [Fact]
    public void Undo_GroupsEditsCloseInTime_AndAMoveOrAPasteStartsAGroup()
    {
        var editor = new TextEditor(multiline: false);
        editor.Reset(string.Empty);
        _ = editor.Type("a", Start);
        _ = editor.Type("b", Start + TimeSpan.FromMilliseconds(100));
        _ = editor.Type("c", Start + TimeSpan.FromMilliseconds(200));
        _ = editor.Type("d", Start + TimeSpan.FromMilliseconds(600));
        editor.MoveTo(0, extend: false);
        _ = editor.Type("x", Start + TimeSpan.FromMilliseconds(650));
        _ = editor.Paste("P", Start + TimeSpan.FromMilliseconds(700));

        var undone = new List<string>();
        for (int i = 0; i < 4; i++)
        {
            _ = editor.Undo();
            undone.Add(editor.Text);
        }

        _ = editor.Redo();
        string redone = editor.Text;
        _ = editor.Type("z", Start + TimeSpan.FromSeconds(5));
        bool redoAfterEdit = editor.CanRedo;
        editor.Reset("outside");

        Assert.Equal(["xabcd", "abcd", "abc", string.Empty], undone);
        Assert.Equal("abc", redone);
        Assert.False(redoAfterEdit);
        Assert.False(editor.CanUndo);
        Assert.Equal(TextSelection.Collapsed(7), editor.Selection);
    }

    /// <summary>
    /// A double click's word is the word boundaries around the position.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - The text "hello world".
    ///
    /// Steps:
    /// - The word at 2, at 5 (the space), and at 8.
    ///
    /// Expected:
    /// - 0 to 5, 5 to 6, 6 to 11.
    /// </remarks>
    [Fact]
    public void WordAt_IsTheBoundariesAroundThePosition()
    {
        var editor = new TextEditor(multiline: false);
        editor.Reset("hello world");

        Assert.Equal(new TextSelection(0, 5), editor.WordAt(2));
        Assert.Equal(new TextSelection(5, 6), editor.WordAt(5));
        Assert.Equal(new TextSelection(6, 11), editor.WordAt(8));
    }
}
