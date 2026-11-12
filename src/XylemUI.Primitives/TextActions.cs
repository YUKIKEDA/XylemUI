namespace XylemUI.Primitives;

/// <summary>
/// An action of a text field (design §4.9, the table of the field's keys; §7, "The default keys and actions"). The field that has
/// the focus handles it; a field that cannot do it, as a read-only one an edit, does not handle it.
/// </summary>
public abstract class TextAction : IAction
{
    /// <summary>
    /// Initializes a new instance of the <see cref="TextAction"/> class.
    /// </summary>
    private protected TextAction() { }

    /// <summary>
    /// Gets what the field does.
    /// </summary>
    internal abstract TextCommand Command { get; }

    /// <summary>
    /// Gets a value indicating whether the command goes forward: right, down, or to the end.
    /// </summary>
    internal virtual bool Forward => false;

    /// <summary>
    /// Gets a value indicating whether a move extends the selection.
    /// </summary>
    internal virtual bool Select => false;
}

/// <summary>
/// What a <see cref="TextAction"/> does, apart from its direction and whether it selects.
/// </summary>
internal enum TextCommand
{
    /// <summary>Moves by a character.</summary>
    Character,

    /// <summary>Moves by a word.</summary>
    Word,

    /// <summary>Moves to an end of the line.</summary>
    Line,

    /// <summary>Moves to an end of the text.</summary>
    Document,

    /// <summary>Moves by a line.</summary>
    Vertical,

    /// <summary>Moves by a page.</summary>
    Page,

    /// <summary>Deletes a character.</summary>
    Delete,

    /// <summary>Deletes a word.</summary>
    DeleteWord,

    /// <summary>Cuts the selection.</summary>
    Cut,

    /// <summary>Copies the selection.</summary>
    Copy,

    /// <summary>Pastes the clipboard's text.</summary>
    Paste,

    /// <summary>Selects the whole text.</summary>
    SelectAll,

    /// <summary>Takes an edit back.</summary>
    Undo,

    /// <summary>Makes an edit again.</summary>
    Redo,

    /// <summary>Breaks the line.</summary>
    Newline,

    /// <summary>Submits the text.</summary>
    Submit,
}

/// <summary>
/// The actions of a text field, by the names of design §7's table.
/// </summary>
public static class TextActions
{
    /// <summary>
    /// Moves the caret one character to the left, or to the start of the selection; the default of the left arrow.
    /// </summary>
    [Action("text::MoveLeft")]
    public sealed class MoveLeft : TextAction
    {
        /// <inheritdoc/>
        internal override TextCommand Command => TextCommand.Character;
    }

    /// <summary>
    /// Moves the caret one character to the right, or to the end of the selection; the default of the right arrow.
    /// </summary>
    [Action("text::MoveRight")]
    public sealed class MoveRight : TextAction
    {
        /// <inheritdoc/>
        internal override TextCommand Command => TextCommand.Character;

        /// <inheritdoc/>
        internal override bool Forward => true;
    }

    /// <summary>
    /// Extends the selection one character to the left; the default of Shift and the left arrow.
    /// </summary>
    [Action("text::SelectLeft")]
    public sealed class SelectLeft : TextAction
    {
        /// <inheritdoc/>
        internal override TextCommand Command => TextCommand.Character;

        /// <inheritdoc/>
        internal override bool Select => true;
    }

    /// <summary>
    /// Extends the selection one character to the right; the default of Shift and the right arrow.
    /// </summary>
    [Action("text::SelectRight")]
    public sealed class SelectRight : TextAction
    {
        /// <inheritdoc/>
        internal override TextCommand Command => TextCommand.Character;

        /// <inheritdoc/>
        internal override bool Forward => true;

        /// <inheritdoc/>
        internal override bool Select => true;
    }

    /// <summary>
    /// Moves the caret to the start of the word on its left; the default of Control and the left arrow.
    /// </summary>
    [Action("text::MoveWordLeft")]
    public sealed class MoveWordLeft : TextAction
    {
        /// <inheritdoc/>
        internal override TextCommand Command => TextCommand.Word;
    }

    /// <summary>
    /// Moves the caret to the start of the word on its right; the default of Control and the right arrow.
    /// </summary>
    [Action("text::MoveWordRight")]
    public sealed class MoveWordRight : TextAction
    {
        /// <inheritdoc/>
        internal override TextCommand Command => TextCommand.Word;

        /// <inheritdoc/>
        internal override bool Forward => true;
    }

    /// <summary>
    /// Extends the selection to the start of the word on the left; the default of Control, Shift, and the left arrow.
    /// </summary>
    [Action("text::SelectWordLeft")]
    public sealed class SelectWordLeft : TextAction
    {
        /// <inheritdoc/>
        internal override TextCommand Command => TextCommand.Word;

        /// <inheritdoc/>
        internal override bool Select => true;
    }

    /// <summary>
    /// Extends the selection to the start of the word on the right; the default of Control, Shift, and the right arrow.
    /// </summary>
    [Action("text::SelectWordRight")]
    public sealed class SelectWordRight : TextAction
    {
        /// <inheritdoc/>
        internal override TextCommand Command => TextCommand.Word;

        /// <inheritdoc/>
        internal override bool Forward => true;

        /// <inheritdoc/>
        internal override bool Select => true;
    }

    /// <summary>
    /// Moves the caret to the start of its line; the default of Home.
    /// </summary>
    [Action("text::MoveToLineStart")]
    public sealed class MoveToLineStart : TextAction
    {
        /// <inheritdoc/>
        internal override TextCommand Command => TextCommand.Line;
    }

    /// <summary>
    /// Moves the caret to the end of its line; the default of End.
    /// </summary>
    [Action("text::MoveToLineEnd")]
    public sealed class MoveToLineEnd : TextAction
    {
        /// <inheritdoc/>
        internal override TextCommand Command => TextCommand.Line;

        /// <inheritdoc/>
        internal override bool Forward => true;
    }

    /// <summary>
    /// Extends the selection to the start of the line; the default of Shift and Home.
    /// </summary>
    [Action("text::SelectToLineStart")]
    public sealed class SelectToLineStart : TextAction
    {
        /// <inheritdoc/>
        internal override TextCommand Command => TextCommand.Line;

        /// <inheritdoc/>
        internal override bool Select => true;
    }

    /// <summary>
    /// Extends the selection to the end of the line; the default of Shift and End.
    /// </summary>
    [Action("text::SelectToLineEnd")]
    public sealed class SelectToLineEnd : TextAction
    {
        /// <inheritdoc/>
        internal override TextCommand Command => TextCommand.Line;

        /// <inheritdoc/>
        internal override bool Forward => true;

        /// <inheritdoc/>
        internal override bool Select => true;
    }

    /// <summary>
    /// Moves the caret to the start of the text; the default of Control and Home.
    /// </summary>
    [Action("text::MoveToStart")]
    public sealed class MoveToStart : TextAction
    {
        /// <inheritdoc/>
        internal override TextCommand Command => TextCommand.Document;
    }

    /// <summary>
    /// Moves the caret to the end of the text; the default of Control and End.
    /// </summary>
    [Action("text::MoveToEnd")]
    public sealed class MoveToEnd : TextAction
    {
        /// <inheritdoc/>
        internal override TextCommand Command => TextCommand.Document;

        /// <inheritdoc/>
        internal override bool Forward => true;
    }

    /// <summary>
    /// Extends the selection to the start of the text; the default of Control, Shift, and Home.
    /// </summary>
    [Action("text::SelectToStart")]
    public sealed class SelectToStart : TextAction
    {
        /// <inheritdoc/>
        internal override TextCommand Command => TextCommand.Document;

        /// <inheritdoc/>
        internal override bool Select => true;
    }

    /// <summary>
    /// Extends the selection to the end of the text; the default of Control, Shift, and End.
    /// </summary>
    [Action("text::SelectToEnd")]
    public sealed class SelectToEnd : TextAction
    {
        /// <inheritdoc/>
        internal override TextCommand Command => TextCommand.Document;

        /// <inheritdoc/>
        internal override bool Forward => true;

        /// <inheritdoc/>
        internal override bool Select => true;
    }

    /// <summary>
    /// Moves the caret up a line in a field of many lines; the default of the up arrow there.
    /// </summary>
    [Action("text::MoveUp")]
    public sealed class MoveUp : TextAction
    {
        /// <inheritdoc/>
        internal override TextCommand Command => TextCommand.Vertical;
    }

    /// <summary>
    /// Moves the caret down a line in a field of many lines; the default of the down arrow there.
    /// </summary>
    [Action("text::MoveDown")]
    public sealed class MoveDown : TextAction
    {
        /// <inheritdoc/>
        internal override TextCommand Command => TextCommand.Vertical;

        /// <inheritdoc/>
        internal override bool Forward => true;
    }

    /// <summary>
    /// Extends the selection up a line in a field of many lines; the default of Shift and the up arrow there.
    /// </summary>
    [Action("text::SelectUp")]
    public sealed class SelectUp : TextAction
    {
        /// <inheritdoc/>
        internal override TextCommand Command => TextCommand.Vertical;

        /// <inheritdoc/>
        internal override bool Select => true;
    }

    /// <summary>
    /// Extends the selection down a line in a field of many lines; the default of Shift and the down arrow there.
    /// </summary>
    [Action("text::SelectDown")]
    public sealed class SelectDown : TextAction
    {
        /// <inheritdoc/>
        internal override TextCommand Command => TextCommand.Vertical;

        /// <inheritdoc/>
        internal override bool Forward => true;

        /// <inheritdoc/>
        internal override bool Select => true;
    }

    /// <summary>
    /// Moves the caret up a page in a field of many lines; the default of Page Up there.
    /// </summary>
    [Action("text::MovePageUp")]
    public sealed class MovePageUp : TextAction
    {
        /// <inheritdoc/>
        internal override TextCommand Command => TextCommand.Page;
    }

    /// <summary>
    /// Moves the caret down a page in a field of many lines; the default of Page Down there.
    /// </summary>
    [Action("text::MovePageDown")]
    public sealed class MovePageDown : TextAction
    {
        /// <inheritdoc/>
        internal override TextCommand Command => TextCommand.Page;

        /// <inheritdoc/>
        internal override bool Forward => true;
    }

    /// <summary>
    /// Extends the selection up a page in a field of many lines; the default of Shift and Page Up there.
    /// </summary>
    [Action("text::SelectPageUp")]
    public sealed class SelectPageUp : TextAction
    {
        /// <inheritdoc/>
        internal override TextCommand Command => TextCommand.Page;

        /// <inheritdoc/>
        internal override bool Select => true;
    }

    /// <summary>
    /// Extends the selection down a page in a field of many lines; the default of Shift and Page Down there.
    /// </summary>
    [Action("text::SelectPageDown")]
    public sealed class SelectPageDown : TextAction
    {
        /// <inheritdoc/>
        internal override TextCommand Command => TextCommand.Page;

        /// <inheritdoc/>
        internal override bool Forward => true;

        /// <inheritdoc/>
        internal override bool Select => true;
    }

    /// <summary>
    /// Deletes the selection, or the character before the caret; the default of Backspace.
    /// </summary>
    [Action("text::DeleteBackward")]
    public sealed class DeleteBackward : TextAction
    {
        /// <inheritdoc/>
        internal override TextCommand Command => TextCommand.Delete;
    }

    /// <summary>
    /// Deletes the selection, or the character after the caret; the default of Delete.
    /// </summary>
    [Action("text::DeleteForward")]
    public sealed class DeleteForward : TextAction
    {
        /// <inheritdoc/>
        internal override TextCommand Command => TextCommand.Delete;

        /// <inheritdoc/>
        internal override bool Forward => true;
    }

    /// <summary>
    /// Deletes the selection, or back to the start of the word; the default of Control and Backspace.
    /// </summary>
    [Action("text::DeleteWordBackward")]
    public sealed class DeleteWordBackward : TextAction
    {
        /// <inheritdoc/>
        internal override TextCommand Command => TextCommand.DeleteWord;
    }

    /// <summary>
    /// Deletes the selection, or on to the start of the next word; the default of Control and Delete.
    /// </summary>
    [Action("text::DeleteWordForward")]
    public sealed class DeleteWordForward : TextAction
    {
        /// <inheritdoc/>
        internal override TextCommand Command => TextCommand.DeleteWord;

        /// <inheritdoc/>
        internal override bool Forward => true;
    }

    /// <summary>
    /// Moves the selection to the clipboard; the default of Control+X and Shift+Delete.
    /// </summary>
    [Action("text::Cut")]
    public sealed class Cut : TextAction
    {
        /// <inheritdoc/>
        internal override TextCommand Command => TextCommand.Cut;
    }

    /// <summary>
    /// Copies the selection to the clipboard; the default of Control+C and Control+Insert.
    /// </summary>
    [Action("text::Copy")]
    public sealed class Copy : TextAction
    {
        /// <inheritdoc/>
        internal override TextCommand Command => TextCommand.Copy;
    }

    /// <summary>
    /// Types the clipboard's text; the default of Control+V and Shift+Insert.
    /// </summary>
    [Action("text::Paste")]
    public sealed class Paste : TextAction
    {
        /// <inheritdoc/>
        internal override TextCommand Command => TextCommand.Paste;
    }

    /// <summary>
    /// Selects the whole text; the default of Control+A.
    /// </summary>
    [Action("text::SelectAll")]
    public sealed class SelectAll : TextAction
    {
        /// <inheritdoc/>
        internal override TextCommand Command => TextCommand.SelectAll;
    }

    /// <summary>
    /// Takes the last edit back; the default of Control+Z.
    /// </summary>
    [Action("text::Undo")]
    public sealed class Undo : TextAction
    {
        /// <inheritdoc/>
        internal override TextCommand Command => TextCommand.Undo;
    }

    /// <summary>
    /// Makes the edit taken back again; the default of Control+Y and Control+Shift+Z.
    /// </summary>
    [Action("text::Redo")]
    public sealed class Redo : TextAction
    {
        /// <inheritdoc/>
        internal override TextCommand Command => TextCommand.Redo;
    }

    /// <summary>
    /// Breaks the line in a field of many lines; the default of Enter and Shift+Enter there.
    /// </summary>
    [Action("text::InsertNewline")]
    public sealed class InsertNewline : TextAction
    {
        /// <inheritdoc/>
        internal override TextCommand Command => TextCommand.Newline;
    }

    /// <summary>
    /// Calls the <c>OnSubmit</c> of a field of one line; the default of Enter there. A field with no <c>OnSubmit</c> does not handle it.
    /// </summary>
    [Action("text::Submit")]
    public sealed class Submit : TextAction
    {
        /// <inheritdoc/>
        internal override TextCommand Command => TextCommand.Submit;
    }
}
