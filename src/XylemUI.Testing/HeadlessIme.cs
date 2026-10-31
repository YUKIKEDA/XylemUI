namespace XylemUI.Testing;

/// <summary>
/// The OS's input method of a window that is not there (design §12.4, "IME"): it sends the focused field the calls an input method
/// sends, in the order TSF's document sends them, and keeps what the field told it. It does not convert: a test gives the text
/// being composed and the clauses.
/// </summary>
public sealed class HeadlessIme : ITextInputPlatform
{
    private readonly HeadlessApp _app;
    private readonly List<(int Start, int OldEnd, int NewEnd)> _textChanges = [];
    private ITextInputClient? _client;
    private string _replaced = string.Empty;

    /// <summary>
    /// Initializes a new instance of the <see cref="HeadlessIme"/> class for an app's window.
    /// </summary>
    /// <param name="app">The app.</param>
    internal HeadlessIme(HeadlessApp app) => _app = app;

    /// <summary>
    /// Gets a value indicating whether a node that takes text has the focus, so that the input method is on.
    /// </summary>
    public bool IsAttached => _client is not null;

    /// <summary>
    /// Gets a value indicating whether a composition is going on.
    /// </summary>
    public bool IsComposing => _client?.Composition is not null;

    /// <summary>
    /// Gets the changes of the text the field told without the input method, in order: from where, where they ended before, and
    /// where they end now.
    /// </summary>
    public IReadOnlyList<(int Start, int OldEnd, int NewEnd)> TextChanges => _textChanges;

    /// <summary>
    /// Gets the number of times the field told that its selection changed without the input method.
    /// </summary>
    public int SelectionChanges { get; private set; }

    /// <summary>
    /// Gets the number of times the field told that its text moved on the screen.
    /// </summary>
    public int LayoutChanges { get; private set; }

    /// <summary>
    /// Composes text: the composition's text, or the selection's when nothing is composed yet, becomes <paramref name="text"/>, with
    /// the caret and the clauses given.
    /// </summary>
    /// <param name="text">The text being composed.</param>
    /// <param name="cursor">The caret, in <paramref name="text"/>.</param>
    /// <param name="clauses">The clauses and how to draw them, in <paramref name="text"/>.</param>
    /// <returns>This input method, for the next call.</returns>
    /// <exception cref="InvalidOperationException">No node that takes text has the focus, or the focused field is read-only or
    /// hides its text, which takes no input method (design §4.7).</exception>
    public HeadlessIme Compose(string text, int cursor, ReadOnlySpan<ImeAttributeRun> clauses = default)
    {
        ArgumentNullException.ThrowIfNull(text);
        ITextInputClient client = Writable();
        (int start, int end) = client.Composition ?? (client.Selection.Start, client.Selection.End);
        if (client.Composition is null)
        {
            _replaced = Copy(client, start, end);
        }

        client.Compose(start, end, text, cursor, clauses);
        _ = _app.DrainQueue();
        return this;
    }

    /// <summary>
    /// Commits text: in place of the composition, which then ends, or at the selection when nothing is composed.
    /// </summary>
    /// <param name="text">The committed text.</param>
    /// <returns>This input method, for the next call.</returns>
    /// <exception cref="InvalidOperationException">No node that takes text has the focus, or the focused field takes no input
    /// method.</exception>
    public HeadlessIme Commit(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        ITextInputClient client = Writable();
        if (client.Composition is { } composition)
        {
            client.Replace(composition.Start, composition.End, text);
            client.EndComposition();
        }
        else
        {
            client.Replace(client.Selection.Start, client.Selection.End, text);
        }

        _ = _app.DrainQueue();
        return this;
    }

    /// <summary>
    /// Cancels the composition: its text goes back to the text it replaced, and it ends.
    /// </summary>
    /// <returns>This input method, for the next call.</returns>
    /// <exception cref="InvalidOperationException">No node that takes text has the focus, or the focused field takes no input
    /// method.</exception>
    public HeadlessIme Cancel()
    {
        ITextInputClient client = Writable();
        if (client.Composition is { } composition)
        {
            client.Replace(composition.Start, composition.End, _replaced);
            client.EndComposition();
        }

        _ = _app.DrainQueue();
        return this;
    }

    /// <summary>
    /// Returns where a range of the focused field's text is in the window, in logical pixels: where the candidate window goes.
    /// </summary>
    /// <param name="start">The start of the range.</param>
    /// <param name="end">The end of the range.</param>
    /// <returns>The range's box; <see langword="null"/> when the field is not laid out yet.</returns>
    /// <exception cref="InvalidOperationException">No node that takes text has the focus.</exception>
    public Rect? BoundsOf(int start, int end)
    {
        _app.VerifyRunning();
        ITextInputClient client = _client ?? throw new InvalidOperationException("No node that takes text has the focus.");
        return client.BoundsOf(start, end, out Rect bounds) ? bounds : null;
    }

    /// <summary>
    /// Forgets what the field told so far.
    /// </summary>
    public void ClearChanges()
    {
        _textChanges.Clear();
        SelectionChanges = 0;
        LayoutChanges = 0;
    }

    void ITextInputPlatform.Attach(ITextInputClient client) => _client = client;

    void ITextInputPlatform.Detach()
    {
        // The input method commits what it composes when the focus leaves (design §4.7).
        ITextInputClient? client = _client;
        _client = null;
        if (client?.Composition is not null)
        {
            client.EndComposition();
        }
    }

    void ITextInputPlatform.TextChanged(int start, int oldEnd, int newEnd) => _textChanges.Add((start, oldEnd, newEnd));

    void ITextInputPlatform.SelectionChanged() => SelectionChanges++;

    void ITextInputPlatform.LayoutChanged() => LayoutChanges++;

    void ITextInputPlatform.CommitComposition()
    {
        if (_client?.Composition is not null)
        {
            _client.EndComposition();
        }
    }

    private static string Copy(ITextInputClient client, int start, int end)
    {
        var text = new char[end - start];
        client.CopyText(start, text);
        return new string(text);
    }

    // The focused client, which an input method may write: one that is not read-only and does not hide its text.
    private ITextInputClient Writable()
    {
        _app.VerifyRunning();
        ITextInputClient client = _client ?? throw new InvalidOperationException("No node that takes text has the focus.");
        if (client.ReadOnly || client.Kind == TextInputKind.Password)
        {
            throw new InvalidOperationException("The focused field takes no input method: it is read-only or hides its text.");
        }

        return client;
    }
}
