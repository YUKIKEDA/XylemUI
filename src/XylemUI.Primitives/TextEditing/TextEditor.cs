using System.Text;
using XylemUI.Text.Unicode;

namespace XylemUI.Primitives;

/// <summary>
/// The text of a text field and its selection, with the edits that change them and their undo (design §4.9). It knows nothing of
/// the lines on the screen: a move by line is the field's, which sets the caret here.
/// </summary>
/// <param name="multiline">Whether the field holds more than one line: a single-line field keeps a pasted text's first line.</param>
internal sealed class TextEditor(bool multiline)
{
    // Edits less than this apart join one group of undo (zed's text.rs lines 225 to 230).
    private static readonly TimeSpan GroupInterval = TimeSpan.FromMilliseconds(300);

    private readonly List<UndoGroup> _undo = [];
    private readonly List<UndoGroup> _redo = [];

    // Whether the next edit may join the last group: an edit of typing or deletion, with no move or other edit between.
    private bool _groupOpen;
    private TimeSpan _lastEdit;

    /// <summary>
    /// Gets the text.
    /// </summary>
    public string Text { get; private set; } = string.Empty;

    /// <summary>
    /// Gets the selection.
    /// </summary>
    public TextSelection Selection { get; private set; }

    /// <summary>
    /// Gets or sets the most grapheme clusters the text holds; <see langword="null"/> for no limit. Typing and pasting are cut to
    /// fit; a text set from outside is not.
    /// </summary>
    public int? MaxLength { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the text is hidden: a move or a deletion by word goes to the text's end, as in
    /// Flutter's <c>obscureText</c>, so that the words do not show.
    /// </summary>
    public bool Password { get; set; }

    /// <summary>
    /// Gets a value indicating whether there is an edit to undo.
    /// </summary>
    public bool CanUndo => _undo.Count > 0;

    /// <summary>
    /// Gets a value indicating whether there is an edit to redo.
    /// </summary>
    public bool CanRedo => _redo.Count > 0;

    /// <summary>
    /// Gets the selected text.
    /// </summary>
    public string SelectedText => Text.Substring(Selection.Start, Selection.Length);

    /// <summary>
    /// Replaces the text from outside the field (the app wrote the signal): the caret goes to the end, and the undo is forgotten
    /// (Avalonia's <c>TextBox</c>, lines 652 to 679).
    /// </summary>
    /// <param name="text">The text.</param>
    public void Reset(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        Text = text;
        Selection = TextSelection.Collapsed(text.Length);
        _undo.Clear();
        _redo.Clear();
        _groupOpen = false;
    }

    /// <summary>
    /// Sets the selection, its ends clamped to the text and moved to the boundaries of grapheme clusters; a move ends the group of
    /// undo.
    /// </summary>
    /// <param name="selection">The selection.</param>
    public void Select(TextSelection selection)
    {
        var next = new TextSelection(Snap(selection.Anchor), Snap(selection.Caret));
        if (next != Selection)
        {
            Selection = next;
            _groupOpen = false;
        }
    }

    /// <summary>
    /// Moves the caret to a position, or extends the selection to it.
    /// </summary>
    /// <param name="offset">The position.</param>
    /// <param name="extend">Whether the anchor stays.</param>
    public void MoveTo(int offset, bool extend) => Select(new TextSelection(extend ? Selection.Anchor : offset, offset));

    /// <summary>
    /// Selects the whole text.
    /// </summary>
    public void SelectAll() => Select(new TextSelection(0, Text.Length));

    /// <summary>
    /// Returns the position one grapheme cluster before or after a position.
    /// </summary>
    /// <param name="offset">The position.</param>
    /// <param name="forward">Whether to go forward.</param>
    /// <returns>The position; the text's end when there is none further.</returns>
    public int Character(int offset, bool forward) =>
        forward ? (offset >= Text.Length ? Text.Length : Graphemes.NextBoundary(Text, offset))
        : offset <= 0 ? 0
        : Graphemes.PreviousBoundary(Text, offset);

    /// <summary>
    /// Returns the position one word before or after a position: the next boundary of UAX #29 whose word is not spaces and
    /// punctuation alone, as Flutter's <c>moveByWordBoundary</c> (<c>text_painter.dart</c> lines 228 to 268), and the text's end for a
    /// hidden text.
    /// </summary>
    /// <param name="offset">The position.</param>
    /// <param name="forward">Whether to go forward.</param>
    /// <returns>Where the move by word ends.</returns>
    public int Word(int offset, bool forward)
    {
        if (Password)
        {
            return forward ? Text.Length : 0;
        }

        if (forward)
        {
            // From the text's start: NextBoundary starts at a boundary.
            for (int start = 0; start < Text.Length; )
            {
                int end = Words.NextBoundary(Text, start);
                if (end > offset && !IsSpaceOrPunctuation(start, end))
                {
                    return end;
                }

                start = end;
            }

            return Text.Length;
        }

        for (int end = offset; end > 0; )
        {
            int start = Words.PreviousBoundary(Text, end);
            if (!IsSpaceOrPunctuation(start, end))
            {
                return start;
            }

            end = start;
        }

        return 0;
    }

    /// <summary>
    /// Returns the word around a position, for a double click: the boundaries of UAX #29 on either side.
    /// </summary>
    /// <param name="offset">The position.</param>
    /// <returns>The word as a selection from its start to its end.</returns>
    public TextSelection WordAt(int offset) => TextUnits.WordAt(Text, offset);

    /// <summary>
    /// Types text at the selection, which it replaces. Typing less than 300 ms after the last edit joins its group of undo.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <param name="now">The time of the input.</param>
    /// <returns>Whether the text changed.</returns>
    public bool Type(string text, TimeSpan now) => Replace(text, now, joins: true);

    /// <summary>
    /// Pastes text at the selection, in a group of its own. A single-line field keeps the text before its first line break, as
    /// Avalonia's <c>TextBox</c> does (lines 1257 to 1279).
    /// </summary>
    /// <param name="text">The text.</param>
    /// <param name="now">The time of the input.</param>
    /// <returns>Whether the text changed.</returns>
    public bool Paste(string text, TimeSpan now)
    {
        ArgumentNullException.ThrowIfNull(text);
        _groupOpen = false;
        bool changed = Replace(multiline ? text : FirstLine(text), now, joins: false);
        _groupOpen = false;
        return changed;
    }

    /// <summary>
    /// Deletes the selection, or with none the cluster or the word before or after the caret.
    /// </summary>
    /// <param name="forward">Whether to delete after the caret (Delete) rather than before it (Backspace).</param>
    /// <param name="word">Whether to delete to the word's boundary (with Control).</param>
    /// <param name="now">The time of the input.</param>
    /// <returns>Whether the text changed.</returns>
    public bool Delete(bool forward, bool word, TimeSpan now)
    {
        if (Selection.IsCollapsed)
        {
            int caret = Selection.Caret;
            int to = word ? Word(caret, forward) : Character(caret, forward);
            if (to == caret)
            {
                return false;
            }

            Selection = new TextSelection(caret, to);
        }

        return Replace(string.Empty, now, joins: true);
    }

    /// <summary>
    /// Deletes the selection, in a group of its own, and returns what it deleted: what a cut puts on the clipboard.
    /// </summary>
    /// <param name="now">The time of the input.</param>
    /// <returns>The text deleted; <see langword="null"/> when nothing was selected.</returns>
    public string? Cut(TimeSpan now)
    {
        if (Selection.IsCollapsed)
        {
            return null;
        }

        string cut = SelectedText;
        _groupOpen = false;
        _ = Replace(string.Empty, now, joins: false);
        _groupOpen = false;
        return cut;
    }

    /// <summary>
    /// Undoes the last group of edits.
    /// </summary>
    /// <returns>Whether there was one.</returns>
    public bool Undo() => Step(_undo, _redo, back: true);

    /// <summary>
    /// Redoes the last group of edits undone.
    /// </summary>
    /// <returns>Whether there was one.</returns>
    public bool Redo() => Step(_redo, _undo, back: false);

    // The number of grapheme clusters of a part of the text.
    private static int Clusters(ReadOnlySpan<char> text)
    {
        int count = 0;
        for (int position = 0; position < text.Length; count++)
        {
            position = Graphemes.NextBoundary(text, position);
        }

        return count;
    }

    // The text before its first hard line break (UAX #14's BK, CR, LF, and NL).
    private static string FirstLine(string text)
    {
        for (int i = 0; i < text.Length; i++)
        {
            if (
                UnicodeData.LineBreak(UnicodeData.Properties(text[i]))
                is LineBreakClass.BK
                    or LineBreakClass.CR
                    or LineBreakClass.LF
                    or LineBreakClass.NL
            )
            {
                return text[..i];
            }
        }

        return text;
    }

    // The text cut to its first clusters, as many as given.
    private static string Take(string text, int clusters)
    {
        int end = 0;
        for (int i = 0; i < clusters && end < text.Length; i++)
        {
            end = Graphemes.NextBoundary(text, end);
        }

        return text[..end];
    }

    // A position at a boundary of a grapheme cluster: the start of the cluster it falls in.
    private int Snap(int offset)
    {
        offset = Math.Clamp(offset, 0, Text.Length);
        return offset == 0 || offset == Text.Length || Graphemes.IsBoundary(Text, offset) ? offset : Graphemes.PreviousBoundary(Text, offset);
    }

    // Whether a part of the text is spaces and punctuation alone (Flutter's \p{Space_Separator}\p{Punctuation}).
    private bool IsSpaceOrPunctuation(int start, int end)
    {
        foreach (Rune rune in Text.AsSpan(start, end - start).EnumerateRunes())
        {
            if (!Rune.IsWhiteSpace(rune) && !Rune.IsPunctuation(rune))
            {
                return false;
            }
        }

        return true;
    }

    // Replaces the selection with text, cut to the room MaxLength leaves, and records the edit for undo: in the last group when
    // `joins` and it is still open, else in a group of its own.
    private bool Replace(string text, TimeSpan now, bool joins)
    {
        TextSelection before = Selection;
        if (MaxLength is { } max && text.Length > 0)
        {
            int room = max - (Clusters(Text) - Clusters(Text.AsSpan(before.Start, before.Length)));
            text = room <= 0 ? string.Empty : Take(text, room);
        }

        string removed = Text.Substring(before.Start, before.Length);
        if (removed.Length == 0 && text.Length == 0)
        {
            return false;
        }

        Text = string.Concat(Text.AsSpan(0, before.Start), text, Text.AsSpan(before.End));
        Selection = TextSelection.Collapsed(before.Start + text.Length);
        var edit = new Edit(before.Start, removed, text, before, Selection);
        if (joins && _groupOpen && _undo.Count > 0 && now - _lastEdit < GroupInterval)
        {
            _undo[^1].Edits.Add(edit);
        }
        else
        {
            _undo.Add(new UndoGroup([edit]));
        }

        _redo.Clear();
        _groupOpen = joins;
        _lastEdit = now;
        return true;
    }

    // Moves the last group from one list to the other, applying its edits back or again.
    private bool Step(List<UndoGroup> from, List<UndoGroup> to, bool back)
    {
        if (from.Count == 0)
        {
            return false;
        }

        UndoGroup group = from[^1];
        from.RemoveAt(from.Count - 1);
        if (back)
        {
            for (int i = group.Edits.Count - 1; i >= 0; i--)
            {
                Edit edit = group.Edits[i];
                Text = string.Concat(Text.AsSpan(0, edit.Start), edit.Removed, Text.AsSpan(edit.Start + edit.Inserted.Length));
                Selection = edit.Before;
            }
        }
        else
        {
            foreach (Edit edit in group.Edits)
            {
                Text = string.Concat(Text.AsSpan(0, edit.Start), edit.Inserted, Text.AsSpan(edit.Start + edit.Removed.Length));
                Selection = edit.After;
            }
        }

        to.Add(group);
        _groupOpen = false;
        return true;
    }

    // One edit: what it replaced where, and the selection before and after it.
    private readonly record struct Edit(int Start, string Removed, string Inserted, TextSelection Before, TextSelection After);

    // The edits one undo takes back.
    private sealed record UndoGroup(List<Edit> Edits);
}
