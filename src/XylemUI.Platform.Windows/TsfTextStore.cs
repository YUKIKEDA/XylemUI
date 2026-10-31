using System.Runtime.InteropServices;
using TerraFX.Interop.Windows;
using static TerraFX.Interop.Windows.Windows;

namespace XylemUI.Platform.Windows;

/// <summary>
/// TSF's document of one text field (design §4.7): the native object TSF calls, with two interfaces. <c>ITextStoreACP2</c> is the
/// document itself; <c>ITfContextOwnerCompositionSink</c> is on the same object because TSF asks the object it was given in
/// <c>ITfDocumentMgr::CreateContext</c> for it (Microsoft Learn, ITfContextOwnerCompositionSink). Each call resolves the managed
/// document by its id in <see cref="NativeHandleTable"/> and hands it the call.
/// </summary>
/// <remarks>
/// Hand-written vtables, as the other COM the OS calls back (.cursor/rules/interop.mdc, Issue #535). Every callback catches what
/// the managed side throws, reports it, and returns <c>E_FAIL</c>: no managed exception crosses into native code.
/// </remarks>
internal unsafe struct TsfTextStore
{
    private const int EFail = unchecked((int)0x80004005);
    private static readonly void** StoreTable = CreateStoreTable();
    private static readonly void** CompositionTable = CreateCompositionTable();

    private void** _storeTable;
    private void** _compositionTable;
    private int _references;
    private nint _documentId;

    /// <summary>
    /// Allocates a store with one reference, its owner's.
    /// </summary>
    /// <param name="documentId">The managed document's id in <see cref="NativeHandleTable"/>.</param>
    /// <returns>The store, as TSF takes it.</returns>
    public static ITextStoreACP2* Create(nint documentId)
    {
        var store = (TsfTextStore*)NativeMemory.AllocZeroed((nuint)sizeof(TsfTextStore));
        store->_storeTable = StoreTable;
        store->_compositionTable = CompositionTable;
        store->_references = 1;
        store->_documentId = documentId;
        return (ITextStoreACP2*)store;
    }

    /// <summary>
    /// Releases the owner's reference.
    /// </summary>
    /// <param name="store">The store.</param>
    public static void Release(ITextStoreACP2* store) => _ = ReleaseThis((TsfTextStore*)store);

    private static uint ReleaseThis(TsfTextStore* self)
    {
        int left = Interlocked.Decrement(ref self->_references);
        if (left == 0)
        {
            NativeMemory.Free(self);
        }

        return (uint)left;
    }

    // The second interface's pointer is its field; the object starts one pointer before it.
    private static TsfTextStore* FromComposition(void* face) => (TsfTextStore*)((void**)face - 1);

    private static int QueryInterfaceThis(TsfTextStore* self, Guid* iid, void** result)
    {
        if (*iid == IID.IID_IUnknown || *iid == __uuidof<ITextStoreACP2>())
        {
            *result = self;
        }
        else if (*iid == __uuidof<ITfContextOwnerCompositionSink>())
        {
            *result = &self->_compositionTable;
        }
        else
        {
            *result = null;
            return E.E_NOINTERFACE;
        }

        _ = Interlocked.Increment(ref self->_references);
        return S.S_OK;
    }

    private static void** CreateStoreTable()
    {
        // IUnknown, then ITextStoreACP2's 25 methods in textstor.h. One table for the process, never freed.
        void** table = (void**)NativeMemory.Alloc((nuint)(28 * sizeof(void*)));
        table[0] = (delegate* unmanaged<TsfTextStore*, Guid*, void**, int>)&QueryInterface;
        table[1] = (delegate* unmanaged<TsfTextStore*, uint>)&AddRef;
        table[2] = (delegate* unmanaged<TsfTextStore*, uint>)&ReleaseCallback;
        table[3] = (delegate* unmanaged<TsfTextStore*, Guid*, IUnknown*, uint, int>)&AdviseSink;
        table[4] = (delegate* unmanaged<TsfTextStore*, IUnknown*, int>)&UnadviseSink;
        table[5] = (delegate* unmanaged<TsfTextStore*, uint, int*, int>)&RequestLock;
        table[6] = (delegate* unmanaged<TsfTextStore*, TS_STATUS*, int>)&GetStatus;
        table[7] = (delegate* unmanaged<TsfTextStore*, int, int, uint, int*, int*, int>)&QueryInsert;
        table[8] = (delegate* unmanaged<TsfTextStore*, uint, uint, TS_SELECTION_ACP*, uint*, int>)&GetSelection;
        table[9] = (delegate* unmanaged<TsfTextStore*, uint, TS_SELECTION_ACP*, int>)&SetSelection;
        table[10] = (delegate* unmanaged<TsfTextStore*, int, int, char*, uint, uint*, TS_RUNINFO*, uint, uint*, int*, int>)&GetText;
        table[11] = (delegate* unmanaged<TsfTextStore*, uint, int, int, char*, uint, TS_TEXTCHANGE*, int>)&SetText;
        table[12] = (delegate* unmanaged<TsfTextStore*, int, int, void**, int>)&GetFormattedText;
        table[13] = (delegate* unmanaged<TsfTextStore*, int, Guid*, Guid*, void**, int>)&GetEmbedded;
        table[14] = (delegate* unmanaged<TsfTextStore*, Guid*, void*, int*, int>)&QueryInsertEmbedded;
        table[15] = (delegate* unmanaged<TsfTextStore*, uint, int, int, void*, TS_TEXTCHANGE*, int>)&InsertEmbedded;
        table[16] = (delegate* unmanaged<TsfTextStore*, uint, char*, uint, int*, int*, TS_TEXTCHANGE*, int>)&InsertTextAtSelection;
        table[17] = (delegate* unmanaged<TsfTextStore*, uint, void*, int*, int*, TS_TEXTCHANGE*, int>)&InsertEmbeddedAtSelection;
        table[18] = (delegate* unmanaged<TsfTextStore*, uint, uint, Guid*, int>)&RequestSupportedAttrs;
        table[19] = (delegate* unmanaged<TsfTextStore*, int, uint, Guid*, uint, int>)&RequestAttrsAtPosition;
        table[20] = (delegate* unmanaged<TsfTextStore*, int, uint, Guid*, uint, int>)&RequestAttrsTransitioningAtPosition;
        table[21] = (delegate* unmanaged<TsfTextStore*, int, int, uint, Guid*, uint, int*, int*, int*, int>)&FindNextAttrTransition;
        table[22] = (delegate* unmanaged<TsfTextStore*, uint, TS_ATTRVAL*, uint*, int>)&RetrieveRequestedAttrs;
        table[23] = (delegate* unmanaged<TsfTextStore*, int*, int>)&GetEndACP;
        table[24] = (delegate* unmanaged<TsfTextStore*, uint*, int>)&GetActiveView;
        table[25] = (delegate* unmanaged<TsfTextStore*, uint, POINT*, uint, int*, int>)&GetACPFromPoint;
        table[26] = (delegate* unmanaged<TsfTextStore*, uint, int, int, RECT*, BOOL*, int>)&GetTextExt;
        table[27] = (delegate* unmanaged<TsfTextStore*, uint, RECT*, int>)&GetScreenExt;
        return table;
    }

    private static void** CreateCompositionTable()
    {
        // IUnknown, then ITfContextOwnerCompositionSink in msctf.h.
        void** table = (void**)NativeMemory.Alloc((nuint)(6 * sizeof(void*)));
        table[0] = (delegate* unmanaged<void*, Guid*, void**, int>)&CompositionQueryInterface;
        table[1] = (delegate* unmanaged<void*, uint>)&CompositionAddRef;
        table[2] = (delegate* unmanaged<void*, uint>)&CompositionRelease;
        table[3] = (delegate* unmanaged<void*, ITfCompositionView*, BOOL*, int>)&OnStartComposition;
        table[4] = (delegate* unmanaged<void*, ITfCompositionView*, ITfRange*, int>)&OnUpdateComposition;
        table[5] = (delegate* unmanaged<void*, ITfCompositionView*, int>)&OnEndComposition;
        return table;
    }

    private static int Fail(Exception exception)
    {
        Win32MessageLoop.ReportCallbackException(exception);
        return EFail;
    }

    // The reference counting and the interface queries call nothing that throws, so they have no catch.
    [UnmanagedCallersOnly]
    private static int QueryInterface(TsfTextStore* self, Guid* iid, void** result) => QueryInterfaceThis(self, iid, result);

    [UnmanagedCallersOnly]
    private static uint AddRef(TsfTextStore* self) => (uint)Interlocked.Increment(ref self->_references);

    [UnmanagedCallersOnly]
    private static uint ReleaseCallback(TsfTextStore* self) => ReleaseThis(self);

    [UnmanagedCallersOnly]
    private static int CompositionQueryInterface(void* face, Guid* iid, void** result) => QueryInterfaceThis(FromComposition(face), iid, result);

    [UnmanagedCallersOnly]
    private static uint CompositionAddRef(void* face) => (uint)Interlocked.Increment(ref FromComposition(face)->_references);

    [UnmanagedCallersOnly]
    private static uint CompositionRelease(void* face) => ReleaseThis(FromComposition(face));

    [UnmanagedCallersOnly]
    private static int AdviseSink(TsfTextStore* self, Guid* iid, IUnknown* sink, uint mask)
    {
        try
        {
            return Document(self) is { } document ? document.AdviseSink(iid, sink) : E.E_UNEXPECTED;
        }
        catch (Exception exception)
        {
            return Fail(exception);
        }
    }

    [UnmanagedCallersOnly]
    private static int UnadviseSink(TsfTextStore* self, IUnknown* sink)
    {
        try
        {
            Document(self)?.UnadviseSink();
            return S.S_OK;
        }
        catch (Exception exception)
        {
            return Fail(exception);
        }
    }

    [UnmanagedCallersOnly]
    private static int RequestLock(TsfTextStore* self, uint flags, int* session)
    {
        try
        {
            return Document(self) is { } document ? document.RequestLock(flags, session) : E.E_UNEXPECTED;
        }
        catch (Exception exception)
        {
            return Fail(exception);
        }
    }

    [UnmanagedCallersOnly]
    private static int GetStatus(TsfTextStore* self, TS_STATUS* status)
    {
        try
        {
            return Document(self) is { } document ? document.GetStatus(status) : E.E_UNEXPECTED;
        }
        catch (Exception exception)
        {
            return Fail(exception);
        }
    }

    [UnmanagedCallersOnly]
    private static int QueryInsert(TsfTextStore* self, int start, int end, uint length, int* resultStart, int* resultEnd)
    {
        *resultStart = start;
        *resultEnd = end;
        return S.S_OK;
    }

    [UnmanagedCallersOnly]
    private static int GetSelection(TsfTextStore* self, uint index, uint count, TS_SELECTION_ACP* selection, uint* fetched)
    {
        try
        {
            return Document(self) is { } document ? document.GetSelection(count, selection, fetched) : E.E_UNEXPECTED;
        }
        catch (Exception exception)
        {
            return Fail(exception);
        }
    }

    [UnmanagedCallersOnly]
    private static int SetSelection(TsfTextStore* self, uint count, TS_SELECTION_ACP* selection)
    {
        try
        {
            return Document(self) is { } document ? document.SetSelection(count, selection) : E.E_UNEXPECTED;
        }
        catch (Exception exception)
        {
            return Fail(exception);
        }
    }

    [UnmanagedCallersOnly]
    private static int GetText(
        TsfTextStore* self,
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
        try
        {
            return Document(self) is { } document
                ? document.GetText(start, end, text, textLength, textFetched, runs, runCount, runsFetched, next)
                : E.E_UNEXPECTED;
        }
        catch (Exception exception)
        {
            return Fail(exception);
        }
    }

    [UnmanagedCallersOnly]
    private static int SetText(TsfTextStore* self, uint flags, int start, int end, char* text, uint length, TS_TEXTCHANGE* change)
    {
        try
        {
            return Document(self) is { } document ? document.SetText(start, end, text, length, change) : E.E_UNEXPECTED;
        }
        catch (Exception exception)
        {
            return Fail(exception);
        }
    }

    [UnmanagedCallersOnly]
    private static int GetFormattedText(TsfTextStore* self, int start, int end, void** data) => E.E_NOTIMPL;

    [UnmanagedCallersOnly]
    private static int GetEmbedded(TsfTextStore* self, int position, Guid* service, Guid* iid, void** result) => E.E_NOTIMPL;

    [UnmanagedCallersOnly]
    private static int QueryInsertEmbedded(TsfTextStore* self, Guid* service, void* format, int* insertable)
    {
        *insertable = 0;
        return S.S_OK;
    }

    [UnmanagedCallersOnly]
    private static int InsertEmbedded(TsfTextStore* self, uint flags, int start, int end, void* data, TS_TEXTCHANGE* change) => E.E_NOTIMPL;

    [UnmanagedCallersOnly]
    private static int InsertTextAtSelection(TsfTextStore* self, uint flags, char* text, uint length, int* start, int* end, TS_TEXTCHANGE* change)
    {
        try
        {
            return Document(self) is { } document ? document.InsertTextAtSelection(flags, text, length, start, end, change) : E.E_UNEXPECTED;
        }
        catch (Exception exception)
        {
            return Fail(exception);
        }
    }

    [UnmanagedCallersOnly]
    private static int InsertEmbeddedAtSelection(TsfTextStore* self, uint flags, void* data, int* start, int* end, TS_TEXTCHANGE* change) =>
        E.E_NOTIMPL;

    // The document has one attribute for TSF to read, the input scope, the same at every position.
    [UnmanagedCallersOnly]
    private static int RequestSupportedAttrs(TsfTextStore* self, uint flags, uint count, Guid* attributes)
    {
        try
        {
            return Document(self) is { } document ? document.RequestAttributes(count, attributes) : E.E_UNEXPECTED;
        }
        catch (Exception exception)
        {
            return Fail(exception);
        }
    }

    [UnmanagedCallersOnly]
    private static int RequestAttrsAtPosition(TsfTextStore* self, int position, uint count, Guid* attributes, uint flags)
    {
        try
        {
            return Document(self) is { } document ? document.RequestAttributes(count, attributes) : E.E_UNEXPECTED;
        }
        catch (Exception exception)
        {
            return Fail(exception);
        }
    }

    [UnmanagedCallersOnly]
    private static int RequestAttrsTransitioningAtPosition(TsfTextStore* self, int position, uint count, Guid* attributes, uint flags)
    {
        try
        {
            return Document(self) is { } document ? document.RequestTransitions() : E.E_UNEXPECTED;
        }
        catch (Exception exception)
        {
            return Fail(exception);
        }
    }

    [UnmanagedCallersOnly]
    private static int FindNextAttrTransition(
        TsfTextStore* self,
        int start,
        int halt,
        uint count,
        Guid* attributes,
        uint flags,
        int* next,
        int* found,
        int* offset
    )
    {
        *next = halt;
        *found = 0;
        *offset = 0;
        return S.S_OK;
    }

    [UnmanagedCallersOnly]
    private static int RetrieveRequestedAttrs(TsfTextStore* self, uint count, TS_ATTRVAL* values, uint* fetched)
    {
        try
        {
            return Document(self) is { } document ? document.RetrieveRequestedAttrs(count, values, fetched) : E.E_UNEXPECTED;
        }
        catch (Exception exception)
        {
            return Fail(exception);
        }
    }

    [UnmanagedCallersOnly]
    private static int GetEndACP(TsfTextStore* self, int* end)
    {
        try
        {
            return Document(self) is { } document ? document.GetEndAcp(end) : E.E_UNEXPECTED;
        }
        catch (Exception exception)
        {
            return Fail(exception);
        }
    }

    [UnmanagedCallersOnly]
    private static int GetActiveView(TsfTextStore* self, uint* view)
    {
        *view = TsfDocument.View;
        return S.S_OK;
    }

    // The nearest position, whatever the flags ask: the field's hit test always answers one.
    [UnmanagedCallersOnly]
    private static int GetACPFromPoint(TsfTextStore* self, uint view, POINT* point, uint flags, int* position)
    {
        try
        {
            return Document(self) is { } document ? document.GetAcpFromPoint(point, position) : E.E_UNEXPECTED;
        }
        catch (Exception exception)
        {
            return Fail(exception);
        }
    }

    [UnmanagedCallersOnly]
    private static int GetTextExt(TsfTextStore* self, uint view, int start, int end, RECT* bounds, BOOL* clipped)
    {
        try
        {
            return Document(self) is { } document ? document.GetTextExt(start, end, bounds, clipped) : E.E_UNEXPECTED;
        }
        catch (Exception exception)
        {
            return Fail(exception);
        }
    }

    [UnmanagedCallersOnly]
    private static int GetScreenExt(TsfTextStore* self, uint view, RECT* bounds)
    {
        try
        {
            return Document(self) is { } document ? document.GetScreenExt(bounds) : E.E_UNEXPECTED;
        }
        catch (Exception exception)
        {
            return Fail(exception);
        }
    }

    [UnmanagedCallersOnly]
    private static int OnStartComposition(void* face, ITfCompositionView* view, BOOL* accept)
    {
        try
        {
            *accept = Document(FromComposition(face))?.StartComposition(view) == true;
            return S.S_OK;
        }
        catch (Exception exception)
        {
            return Fail(exception);
        }
    }

    // The range and the clauses are read when the edit ends (TsfEditSink), when the text and its attributes are final.
    [UnmanagedCallersOnly]
    private static int OnUpdateComposition(void* face, ITfCompositionView* view, ITfRange* range) => S.S_OK;

    [UnmanagedCallersOnly]
    private static int OnEndComposition(void* face, ITfCompositionView* view)
    {
        try
        {
            Document(FromComposition(face))?.EndComposition();
            return S.S_OK;
        }
        catch (Exception exception)
        {
            return Fail(exception);
        }
    }

    private static TsfDocument? Document(TsfTextStore* self) =>
        NativeHandleTable.TryResolve(self->_documentId, out TsfDocument? document) ? document : null;
}

/// <summary>
/// The sink of a document's edits (<c>ITfTextEditSink</c>, design §4.7): when an edit session ends, the document reads the
/// composition's range and the display attributes of its clauses. TSF takes it through <c>ITfSource::AdviseSink</c>, so it is an
/// object of its own.
/// </summary>
internal unsafe struct TsfEditSink
{
    private static readonly void** Table = CreateTable();

    private void** _table;
    private int _references;
    private nint _documentId;

    /// <summary>
    /// Allocates a sink with one reference, its owner's.
    /// </summary>
    /// <param name="documentId">The managed document's id in <see cref="NativeHandleTable"/>.</param>
    /// <returns>The sink.</returns>
    public static ITfTextEditSink* Create(nint documentId)
    {
        var sink = (TsfEditSink*)NativeMemory.AllocZeroed((nuint)sizeof(TsfEditSink));
        sink->_table = Table;
        sink->_references = 1;
        sink->_documentId = documentId;
        return (ITfTextEditSink*)sink;
    }

    /// <summary>
    /// Releases the owner's reference.
    /// </summary>
    /// <param name="sink">The sink.</param>
    public static void Release(ITfTextEditSink* sink) => _ = ReleaseThis((TsfEditSink*)sink);

    private static uint ReleaseThis(TsfEditSink* self)
    {
        int left = Interlocked.Decrement(ref self->_references);
        if (left == 0)
        {
            NativeMemory.Free(self);
        }

        return (uint)left;
    }

    private static void** CreateTable()
    {
        // IUnknown, then ITfTextEditSink in msctf.h.
        void** table = (void**)NativeMemory.Alloc((nuint)(4 * sizeof(void*)));
        table[0] = (delegate* unmanaged<TsfEditSink*, Guid*, void**, int>)&QueryInterface;
        table[1] = (delegate* unmanaged<TsfEditSink*, uint>)&AddRef;
        table[2] = (delegate* unmanaged<TsfEditSink*, uint>)&ReleaseCallback;
        table[3] = (delegate* unmanaged<TsfEditSink*, ITfContext*, uint, void*, int>)&OnEndEdit;
        return table;
    }

    [UnmanagedCallersOnly]
    private static int QueryInterface(TsfEditSink* self, Guid* iid, void** result)
    {
        if (*iid == IID.IID_IUnknown || *iid == __uuidof<ITfTextEditSink>())
        {
            *result = self;
            _ = Interlocked.Increment(ref self->_references);
            return S.S_OK;
        }

        *result = null;
        return E.E_NOINTERFACE;
    }

    [UnmanagedCallersOnly]
    private static uint AddRef(TsfEditSink* self) => (uint)Interlocked.Increment(ref self->_references);

    [UnmanagedCallersOnly]
    private static uint ReleaseCallback(TsfEditSink* self) => ReleaseThis(self);

    [UnmanagedCallersOnly]
    private static int OnEndEdit(TsfEditSink* self, ITfContext* context, uint readOnly, void* record)
    {
        try
        {
            if (NativeHandleTable.TryResolve(self->_documentId, out TsfDocument? document))
            {
                document.EndEdit(context, readOnly);
            }

            return S.S_OK;
        }
        catch (Exception exception)
        {
            Win32MessageLoop.ReportCallbackException(exception);
            return unchecked((int)0x80004005);
        }
    }
}
