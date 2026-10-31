using System.Numerics;

namespace XylemUI;

/// <summary>
/// What a text field gives the OS's input method (design §4.7, "The client of the text input"): its text, its selection, the text
/// being composed, where a range is on the screen, and the edits the input method asks for. The OS reads it and asks it to change,
/// as TSF's document does (<c>ITextStoreACP2</c>); a field registers it on its node (<see cref="UiTree.SetTextInputClient"/>), and
/// the tree attaches the focused node's to the platform (<see cref="ITextInputPlatform"/>). Positions are UTF-16 code units of the
/// field's text, the text being composed included.
/// </summary>
internal interface ITextInputClient
{
    /// <summary>
    /// Gets the length of the field's text, the text being composed included.
    /// </summary>
    int Length { get; }

    /// <summary>
    /// Gets the selection.
    /// </summary>
    TextSelection Selection { get; }

    /// <summary>
    /// Gets the range of the text being composed; <see langword="null"/> when nothing is.
    /// </summary>
    (int Start, int End)? Composition { get; }

    /// <summary>
    /// Gets a value indicating whether the field is read-only: the input method may read its text and may not change it.
    /// </summary>
    bool ReadOnly { get; }

    /// <summary>
    /// Gets the kind of text the field takes.
    /// </summary>
    TextInputKind Kind { get; }

    /// <summary>
    /// Selects a range of the text. A selection the input method makes is not told back to it.
    /// </summary>
    /// <param name="selection">The selection.</param>
    void Select(TextSelection selection);

    /// <summary>
    /// Copies a range of the text.
    /// </summary>
    /// <param name="start">The first code unit.</param>
    /// <param name="into">Where the code units go: as many as it holds.</param>
    void CopyText(int start, Span<char> into);

    /// <summary>
    /// Returns where a range of the text is on the screen, in the window's logical pixels, for the candidate window.
    /// </summary>
    /// <param name="start">The start of the range.</param>
    /// <param name="end">The end of the range.</param>
    /// <param name="bounds">The range's box; for an empty range, the caret's box there.</param>
    /// <returns><see langword="false"/> when the field is not laid out yet (TSF's <c>TS_E_NOLAYOUT</c>).</returns>
    bool BoundsOf(int start, int end, out Rect bounds);

    /// <summary>
    /// Returns the position of the text under a point of the window, in its logical pixels: the nearest position when the point is
    /// beside the text. The input method asks it for a press on the text being composed (TSF's <c>GetACPFromPoint</c>).
    /// </summary>
    /// <param name="point">The point.</param>
    /// <param name="position">The position.</param>
    /// <returns><see langword="false"/> when the field is not laid out yet.</returns>
    bool PositionAt(Vector2 point, out int position);

    /// <summary>
    /// Replaces a range of the text with committed text.
    /// </summary>
    /// <param name="start">The start of the range.</param>
    /// <param name="end">The end of the range.</param>
    /// <param name="text">The text.</param>
    void Replace(int start, int end, ReadOnlySpan<char> text);

    /// <summary>
    /// Replaces a range of the text with text being composed, and makes that text the composition. The range is the one given,
    /// not the selection, also when nothing is composed yet (as TSF's <c>SetText</c>).
    /// </summary>
    /// <param name="start">The start of the range.</param>
    /// <param name="end">The end of the range.</param>
    /// <param name="text">The text being composed.</param>
    /// <param name="cursor">The caret, in <paramref name="text"/>.</param>
    /// <param name="clauses">The clauses and how to draw them, in <paramref name="text"/>.</param>
    void Compose(int start, int end, ReadOnlySpan<char> text, int cursor, ReadOnlySpan<ImeAttributeRun> clauses);

    /// <summary>
    /// Ends the composition: the text being composed stays as it is, committed (a cancel replaces it first).
    /// </summary>
    void EndComposition();
}

/// <summary>
/// The OS's input method seen from the tree and the field (design §4.7, "What the app tells the OS"): the tree attaches the
/// focused node's client, and the field tells what changed without the input method, which keeps a copy of the field's text. The
/// host puts it on the tree (<see cref="UiTree.SetTextInput"/>).
/// </summary>
internal interface ITextInputPlatform
{
    /// <summary>
    /// Attaches the client of the node that took the focus. The tree calls it.
    /// </summary>
    /// <param name="client">The client.</param>
    void Attach(ITextInputClient client);

    /// <summary>
    /// Detaches the client attached: the focus went to a node with none. A composition is committed first. The tree calls it.
    /// </summary>
    void Detach();

    /// <summary>
    /// Tells that the text from <paramref name="start"/> to <paramref name="oldEnd"/> became the text from <paramref name="start"/>
    /// to <paramref name="newEnd"/>. The field calls it.
    /// </summary>
    /// <param name="start">Where the change starts.</param>
    /// <param name="oldEnd">Where it ended before.</param>
    /// <param name="newEnd">Where it ends now.</param>
    void TextChanged(int start, int oldEnd, int newEnd);

    /// <summary>
    /// Tells that the selection changed. The field calls it.
    /// </summary>
    void SelectionChanged();

    /// <summary>
    /// Tells that where the text is on the screen changed. The field calls it while it draws, in the middle of building the display
    /// list: a platform that tells the OS (TSF's <c>OnLayoutChange</c>) does so after the frame, outside its document's lock.
    /// </summary>
    void LayoutChanged();

    /// <summary>
    /// Asks the input method to commit the text being composed, before the app writes the field's text. The field calls it; the
    /// client's <see cref="ITextInputClient.EndComposition"/> comes before it returns.
    /// </summary>
    void CommitComposition();
}

/// <summary>
/// The kind of text a field takes, which the input method and the touch keyboard read (design §4.7, "The kinds of input").
/// </summary>
public enum TextInputKind : byte
{
    /// <summary>
    /// Any text.
    /// </summary>
    Text,

    /// <summary>
    /// A URL.
    /// </summary>
    Url,

    /// <summary>
    /// An email address.
    /// </summary>
    Email,

    /// <summary>
    /// A number.
    /// </summary>
    Number,

    /// <summary>
    /// A telephone number.
    /// </summary>
    Telephone,

    /// <summary>
    /// A password, which takes no input method.
    /// </summary>
    Password,
}

/// <summary>
/// A clause of the text being composed and how to draw it, as the input method's display attribute says (TSF's
/// <c>TF_DISPLAYATTRIBUTE</c>, design §4.7, "Drawing the text being composed").
/// </summary>
/// <param name="Start">The start of the clause, in the text being composed.</param>
/// <param name="End">The end of the clause, in the text being composed.</param>
/// <param name="Line">The shape of its underline; <see langword="null"/> draws none.</param>
/// <param name="Thick">Whether the underline is thick: twice the normal one.</param>
/// <param name="LineColor">The underline's color; <see langword="null"/> takes the color the clause's text is drawn in.</param>
/// <param name="TextColor">The text's color; <see langword="null"/> takes the field's.</param>
public readonly record struct ImeAttributeRun(
    int Start,
    int End,
    DecorationStyle? Line,
    bool Thick,
    Color? LineColor = null,
    Color? TextColor = null
);
