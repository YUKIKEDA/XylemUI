using System.Numerics;
using TerraFX.Interop.Windows;
using static TerraFX.Interop.Windows.Windows;

namespace XylemUI.Platform.Windows;

/// <summary>
/// What TSF's document needs from the TSF manager: the sink of <c>ITextStoreACPSink</c> that the manager advised, or a test's.
/// </summary>
internal interface ITsfSink
{
    /// <summary>
    /// Grants a lock: the manager reads and edits the document in it (<c>OnLockGranted</c>).
    /// </summary>
    /// <param name="flags">The lock (<c>TS_LF_READ</c> or <c>TS_LF_READWRITE</c>).</param>
    /// <returns>The manager's result.</returns>
    int OnLockGranted(uint flags);

    /// <summary>
    /// Tells that the text changed without the manager (<c>OnTextChange</c>).
    /// </summary>
    /// <param name="start">Where the change starts.</param>
    /// <param name="oldEnd">Where it ended before.</param>
    /// <param name="newEnd">Where it ends now.</param>
    void OnTextChange(int start, int oldEnd, int newEnd);

    /// <summary>
    /// Tells that the selection changed without the manager (<c>OnSelectionChange</c>).
    /// </summary>
    void OnSelectionChange();

    /// <summary>
    /// Tells that the text moved on the screen (<c>OnLayoutChange</c>).
    /// </summary>
    void OnLayoutChange();
}

/// <summary>
/// TSF's document of the focused text field (design §4.7): the text store's calls on the field's client, its locks, and the
/// changes the field tells, held while a lock is granted and told after it.
/// </summary>
/// <param name="client">The field's client of the text input.</param>
/// <param name="toScreen">Turns a box in the window's logical pixels into the screen's physical pixels.</param>
/// <param name="screen">Returns the window's client area in the screen's physical pixels.</param>
/// <param name="fromScreen">Turns a point of the screen's physical pixels into the window's logical pixels.</param>
internal sealed unsafe class TsfDocument(ITextInputClient client, Func<Rect, RECT> toScreen, Func<RECT> screen, Func<POINT, Vector2> fromScreen)
{
    /// <summary>
    /// The one view of the document (<c>TsViewCookie</c>).
    /// </summary>
    public const uint View = 1;

    // textstor.h (MAKE_HRESULT with FACILITY_ITF), and the flags of locks, status, and insertion.
    private const int NoLock = unchecked((int)0x80040201);
    private const int NoLayout = unchecked((int)0x80040206);
    private const int Synchronous = unchecked((int)0x80040208);
    private const int ReadOnlyError = unchecked((int)0x80040209);
    private const int Async = 0x00040300;
    private const uint LockSync = 0x1;
    private const uint LockRead = 0x2;
    private const uint LockReadWrite = 0x6;
    private const uint DynamicReadOnly = 0x1;
    private const uint StaticNoHiddenText = 0x8;
    private const uint InsertQueryOnly = 0x2;
    private const int ActiveEnd = 2; // TS_AE_END

    private readonly List<(int Start, int OldEnd, int NewEnd)> _textChanges = [];
    private uint _lock;
    private uint _pending;
    private bool _selectionChanged;
    private bool _layoutChanged;
    private ITfCompositionView* _view;
    private ImeAttributeRun[] _clauses = [];

    /// <summary>
    /// Gets the field's client.
    /// </summary>
    public ITextInputClient Client { get; } = client;

    /// <summary>
    /// Gets or sets the sink the manager advised; <see langword="null"/> before it does.
    /// </summary>
    public ITsfSink? Sink { get; set; }

    /// <summary>
    /// Gets a value indicating whether the manager holds a lock.
    /// </summary>
    public bool Locked => _lock != 0;

    /// <summary>
    /// Gets a value indicating whether a composition is going on.
    /// </summary>
    public bool Composing { get; private set; }

    /// <summary>
    /// Takes the manager's sink (<c>ITextStoreACP2::AdviseSink</c>).
    /// </summary>
    /// <param name="iid">The sink's interface.</param>
    /// <param name="sink">The sink.</param>
    /// <returns>The result.</returns>
    public int AdviseSink(Guid* iid, IUnknown* sink)
    {
        if (*iid != __uuidof<ITextStoreACPSink>())
        {
            return E.E_INVALIDARG;
        }

        ITextStoreACPSink* acp;
        int result = sink->QueryInterface(__uuidof<ITextStoreACPSink>(), (void**)&acp);
        if (result < 0)
        {
            return result;
        }

        (Sink as AcpSink)?.Dispose();
        Sink = new AcpSink(acp);
        return S.S_OK;
    }

    /// <summary>
    /// Drops the manager's sink.
    /// </summary>
    public void UnadviseSink()
    {
        (Sink as AcpSink)?.Dispose();
        Sink = null;
    }

    /// <summary>
    /// Grants a lock, or answers that it comes later or not at all (<c>ITextStoreACP2::RequestLock</c>, design §4.7): while the
    /// manager holds one, a synchronous request is refused and an asynchronous one is granted after it; the changes the field
    /// told meanwhile are told after the lock.
    /// </summary>
    /// <param name="flags">The lock and whether it is synchronous.</param>
    /// <param name="session">Receives the manager's result, <c>TS_S_ASYNC</c>, or <c>TS_E_SYNCHRONOUS</c>.</param>
    /// <returns>The result.</returns>
    public int RequestLock(uint flags, int* session)
    {
        if (Sink is not { } sink)
        {
            return E.E_UNEXPECTED;
        }

        bool sync = (flags & LockSync) != 0;
        if (_lock != 0)
        {
            if (sync)
            {
                *session = Synchronous;
            }
            else
            {
                _pending |= flags & LockReadWrite;
                *session = Async;
            }

            return S.S_OK;
        }

        _lock = flags & LockReadWrite;
        try
        {
            *session = sink.OnLockGranted(_lock);
            while (_pending != 0)
            {
                _lock = _pending;
                _pending = 0;
                _ = sink.OnLockGranted(_lock);
            }
        }
        finally
        {
            _lock = 0;
        }

        TellHeld();
        return S.S_OK;
    }

    /// <summary>
    /// Answers the document's status (<c>ITextStoreACP2::GetStatus</c>): read-only for a read-only field, and no hidden text.
    /// </summary>
    /// <param name="status">Receives the status.</param>
    /// <returns>The result.</returns>
    public int GetStatus(TS_STATUS* status)
    {
        status->dwDynamicFlags = Client.ReadOnly ? DynamicReadOnly : 0;
        status->dwStaticFlags = StaticNoHiddenText;
        return S.S_OK;
    }

    /// <summary>
    /// Answers the selection (<c>ITextStoreACP2::GetSelection</c>).
    /// </summary>
    /// <param name="count">How many selections the manager takes.</param>
    /// <param name="selection">Receives the selection.</param>
    /// <param name="fetched">Receives how many were written.</param>
    /// <returns>The result.</returns>
    public int GetSelection(uint count, TS_SELECTION_ACP* selection, uint* fetched)
    {
        if (_lock == 0)
        {
            return NoLock;
        }

        *fetched = 0;
        if (count > 0)
        {
            TextSelection value = Client.Selection;
            selection->acpStart = value.Start;
            selection->acpEnd = value.End;
            selection->style.ase = (TsActiveSelEnd)ActiveEnd;
            selection->style.fInterimChar = false;
            *fetched = 1;
        }

        return S.S_OK;
    }

    /// <summary>
    /// Sets the selection (<c>ITextStoreACP2::SetSelection</c>), which is not told back.
    /// </summary>
    /// <param name="count">How many selections come.</param>
    /// <param name="selection">The selection.</param>
    /// <returns>The result.</returns>
    public int SetSelection(uint count, TS_SELECTION_ACP* selection)
    {
        if ((_lock & LockReadWrite) != LockReadWrite)
        {
            return NoLock;
        }

        if (count > 0)
        {
            Client.Select(new TextSelection(selection->acpStart, selection->acpEnd));
        }

        return S.S_OK;
    }

    /// <summary>
    /// Copies a range of the text (<c>ITextStoreACP2::GetText</c>): as much as the buffer holds, as one plain run.
    /// </summary>
    /// <param name="start">The first position.</param>
    /// <param name="end">The end, or -1 for the end of the text.</param>
    /// <param name="text">The buffer of characters.</param>
    /// <param name="textLength">The length of the buffer of characters.</param>
    /// <param name="textFetched">Receives how many characters were copied.</param>
    /// <param name="runs">The buffer of runs.</param>
    /// <param name="runCount">The length of the buffer of runs.</param>
    /// <param name="runsFetched">Receives how many runs were written.</param>
    /// <param name="next">Receives the position after the last character copied.</param>
    /// <returns>The result.</returns>
    public int GetText(
        int start,
        int end,
        char* text,
        uint textLength,
        uint* textFetched,
        TS_RUNINFO* runs,
        uint runCount,
        uint* runsFetched,
        int* next
    )
    {
        if (_lock == 0)
        {
            return NoLock;
        }

        int length = Client.Length;
        if (start < 0 || start > length)
        {
            return unchecked((int)0x80040200); // TS_E_INVALIDPOS
        }

        int stop = end < 0 ? length : Math.Min(end, length);
        int count = Math.Max(0, Math.Min(stop - start, (int)textLength));
        if (text is not null && count > 0)
        {
            Client.CopyText(start, new Span<char>(text, count));
        }

        *textFetched = (uint)count;
        if (runs is not null && runCount > 0 && count > 0)
        {
            runs->uCount = (uint)count;
            runs->type = TsRunType.TS_RT_PLAIN;
            *runsFetched = 1;
        }
        else
        {
            *runsFetched = 0;
        }

        *next = start + count;
        return S.S_OK;
    }

    /// <summary>
    /// Replaces a range (<c>ITextStoreACP2::SetText</c>): text being composed while a composition goes on, committed text
    /// otherwise. Not told back.
    /// </summary>
    /// <param name="start">The start of the range.</param>
    /// <param name="end">The end of the range.</param>
    /// <param name="text">The text.</param>
    /// <param name="length">Its length.</param>
    /// <param name="change">Receives the change.</param>
    /// <returns>The result.</returns>
    public int SetText(int start, int end, char* text, uint length, TS_TEXTCHANGE* change)
    {
        if ((_lock & LockReadWrite) != LockReadWrite)
        {
            return NoLock;
        }

        if (Client.ReadOnly)
        {
            return ReadOnlyError;
        }

        Write(start, end, new ReadOnlySpan<char>(text, (int)length));
        if (change is not null)
        {
            *change = new TS_TEXTCHANGE
            {
                acpStart = start,
                acpOldEnd = end,
                acpNewEnd = start + (int)length,
            };
        }

        return S.S_OK;
    }

    /// <summary>
    /// Replaces the selection (<c>ITextStoreACP2::InsertTextAtSelection</c>), or with <c>TS_IAS_QUERYONLY</c> only answers where.
    /// </summary>
    /// <param name="flags">The insertion's flags.</param>
    /// <param name="text">The text.</param>
    /// <param name="length">Its length.</param>
    /// <param name="start">Receives where the text starts.</param>
    /// <param name="end">Receives where it ends.</param>
    /// <param name="change">Receives the change, or is null.</param>
    /// <returns>The result.</returns>
    public int InsertTextAtSelection(uint flags, char* text, uint length, int* start, int* end, TS_TEXTCHANGE* change)
    {
        if ((_lock & LockReadWrite) != LockReadWrite)
        {
            return NoLock;
        }

        TextSelection selection = Client.Selection;
        if ((flags & InsertQueryOnly) != 0)
        {
            *start = selection.Start;
            *end = selection.End;
            return S.S_OK;
        }

        if (Client.ReadOnly)
        {
            return ReadOnlyError;
        }

        Write(selection.Start, selection.End, new ReadOnlySpan<char>(text, (int)length));
        *start = selection.Start;
        *end = selection.Start + (int)length;
        if (change is not null)
        {
            *change = new TS_TEXTCHANGE
            {
                acpStart = selection.Start,
                acpOldEnd = selection.End,
                acpNewEnd = *end,
            };
        }

        return S.S_OK;
    }

    /// <summary>
    /// Answers the length of the text (<c>ITextStoreACP2::GetEndACP</c>).
    /// </summary>
    /// <param name="end">Receives it.</param>
    /// <returns>The result.</returns>
    public int GetEndAcp(int* end)
    {
        if (_lock == 0)
        {
            return NoLock;
        }

        *end = Client.Length;
        return S.S_OK;
    }

    /// <summary>
    /// Answers where a range is on the screen (<c>ITextStoreACP2::GetTextExt</c>): the field's box of it, in the screen's physical
    /// pixels; <c>TS_E_NOLAYOUT</c> before the field is laid out, and the field tells the layout when it is.
    /// </summary>
    /// <param name="start">The start of the range.</param>
    /// <param name="end">The end of the range.</param>
    /// <param name="bounds">Receives the box.</param>
    /// <param name="clipped">Receives whether the box is clipped: never.</param>
    /// <returns>The result.</returns>
    public int GetTextExt(int start, int end, RECT* bounds, BOOL* clipped)
    {
        if (_lock == 0)
        {
            return NoLock;
        }

        if (!Client.BoundsOf(start, end, out Rect box))
        {
            return NoLayout;
        }

        *bounds = toScreen(box);
        *clipped = false;
        return S.S_OK;
    }

    /// <summary>
    /// Answers the position under a point of the screen (<c>ITextStoreACP2::GetACPFromPoint</c>): the nearest one when the point
    /// is beside the text; <c>TS_E_NOLAYOUT</c> before the field is laid out.
    /// </summary>
    /// <param name="point">The point, in the screen's physical pixels.</param>
    /// <param name="position">Receives the position.</param>
    /// <returns>The result.</returns>
    public int GetAcpFromPoint(POINT* point, int* position)
    {
        if (_lock == 0)
        {
            return NoLock;
        }

        if (!Client.PositionAt(fromScreen(*point), out int at))
        {
            return NoLayout;
        }

        *position = at;
        return S.S_OK;
    }

    /// <summary>
    /// Answers the window's client area on the screen (<c>ITextStoreACP2::GetScreenExt</c>).
    /// </summary>
    /// <param name="bounds">Receives it.</param>
    /// <returns>The result.</returns>
    public int GetScreenExt(RECT* bounds)
    {
        *bounds = screen();
        return S.S_OK;
    }

    /// <summary>
    /// Starts a composition (<c>OnStartComposition</c>): the document accepts it, and keeps its view to read its range.
    /// </summary>
    /// <param name="view">The composition's view.</param>
    /// <returns>Whether the document accepts it.</returns>
    public bool StartComposition(ITfCompositionView* view)
    {
        ReleaseView();
        if (view is not null)
        {
            _ = view->AddRef();
            _view = view;
        }

        Composing = true;
        return true;
    }

    /// <summary>
    /// Ends the composition (<c>OnEndComposition</c>): the field commits what it holds.
    /// </summary>
    public void EndComposition()
    {
        ReleaseView();
        Composing = false;
        _clauses = [];
        Client.EndComposition();
    }

    /// <summary>
    /// Reads the composition's range and the display attributes of its clauses when an edit ends (<c>ITfTextEditSink::OnEndEdit</c>),
    /// and gives them to the field.
    /// </summary>
    /// <param name="context">The document's context.</param>
    /// <param name="readOnly">The edit cookie to read with.</param>
    public void EndEdit(ITfContext* context, uint readOnly)
    {
        if (!Composing || _view is null || Client.Composition is null)
        {
            return;
        }

        ITfRange* range = null;
        if (_view->GetRange(&range) < 0 || range is null)
        {
            return;
        }

        try
        {
            if (!TsfAttributes.Extent(range, out int start, out int length))
            {
                return;
            }

            _clauses = TsfAttributes.Clauses(context, readOnly, start, length);
            var text = new char[length];
            Client.CopyText(start, text);
            TextSelection selection = Client.Selection;
            int cursor = Math.Clamp(selection.Caret - start, 0, length);
            Client.Compose(start, start + length, text, cursor, _clauses);
        }
        finally
        {
            _ = range->Release();
        }
    }

    /// <summary>
    /// Tells the manager that the text changed without it, after the lock when one is held (design §4.7).
    /// </summary>
    /// <param name="start">Where the change starts.</param>
    /// <param name="oldEnd">Where it ended before.</param>
    /// <param name="newEnd">Where it ends now.</param>
    public void TextChanged(int start, int oldEnd, int newEnd)
    {
        if (_lock != 0)
        {
            _textChanges.Add((start, oldEnd, newEnd));
            return;
        }

        Sink?.OnTextChange(start, oldEnd, newEnd);
    }

    /// <summary>
    /// Tells the manager that the selection changed without it, after the lock when one is held.
    /// </summary>
    public void SelectionChanged()
    {
        if (_lock != 0)
        {
            _selectionChanged = true;
            return;
        }

        Sink?.OnSelectionChange();
    }

    /// <summary>
    /// Tells the manager that the text moved on the screen, after the lock when one is held.
    /// </summary>
    public void LayoutChanged()
    {
        if (_lock != 0)
        {
            _layoutChanged = true;
            return;
        }

        Sink?.OnLayoutChange();
    }

    /// <summary>
    /// Releases what the document holds of TSF.
    /// </summary>
    public void Close()
    {
        ReleaseView();
        UnadviseSink();
    }

    // A replacement from the manager: text being composed while a composition goes on, committed text otherwise.
    private void Write(int start, int end, ReadOnlySpan<char> text)
    {
        if (Composing)
        {
            Client.Compose(start, end, text, text.Length, _clauses);
        }
        else
        {
            Client.Replace(start, end, text);
        }
    }

    // The changes the field told while the manager held a lock, told now, in order.
    private void TellHeld()
    {
        if (Sink is not { } sink)
        {
            _textChanges.Clear();
            (_selectionChanged, _layoutChanged) = (false, false);
            return;
        }

        for (int i = 0; i < _textChanges.Count; i++)
        {
            (int start, int oldEnd, int newEnd) = _textChanges[i];
            sink.OnTextChange(start, oldEnd, newEnd);
        }

        _textChanges.Clear();
        if (_selectionChanged)
        {
            _selectionChanged = false;
            sink.OnSelectionChange();
        }

        if (_layoutChanged)
        {
            _layoutChanged = false;
            sink.OnLayoutChange();
        }
    }

    private void ReleaseView()
    {
        if (_view is not null)
        {
            _ = _view->Release();
            _view = null;
        }
    }

    // The manager's sink of ITextStoreACPSink.
    private sealed class AcpSink(ITextStoreACPSink* sink) : ITsfSink, IDisposable
    {
        private ITextStoreACPSink* _sink = sink;

        public int OnLockGranted(uint flags) => _sink->OnLockGranted(flags);

        public void OnTextChange(int start, int oldEnd, int newEnd)
        {
            var change = new TS_TEXTCHANGE
            {
                acpStart = start,
                acpOldEnd = oldEnd,
                acpNewEnd = newEnd,
            };
            _ = _sink->OnTextChange(0, &change);
        }

        public void OnSelectionChange() => _ = _sink->OnSelectionChange();

        public void OnLayoutChange() => _ = _sink->OnLayoutChange(TsLayoutCode.TS_LC_CHANGE, View);

        public void Dispose() => Com.Release(ref _sink);
    }
}
