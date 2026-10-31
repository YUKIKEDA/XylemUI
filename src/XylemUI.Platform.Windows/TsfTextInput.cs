using System.Numerics;
using TerraFX.Interop.Windows;
using static TerraFX.Interop.Windows.Windows;

namespace XylemUI.Platform.Windows;

/// <summary>
/// The OS's input method of a window through TSF (design §4.7): one thread manager for the UI thread, and a document for the
/// focused text field, attached to the window while the field has the focus. A field that hides its text, and a node that takes
/// no text, have the input method off: no document and no input context, so that IMM32's path does not take over either (spike
/// #510).
/// </summary>
internal sealed unsafe class TsfTextInput : ITextInputPlatform, IDisposable
{
    private const uint InputContextDefault = 0x10; // IACE_DEFAULT
    private const uint PopAll = 0x1; // TF_POPF_ALL

    // msctf.h; checked in spike #510.
    private static readonly Guid ThreadMgrClass = new(0x529a9e6b, 0x6587, 0x4f23, 0xab, 0x9e, 0x9c, 0x7d, 0x68, 0x3e, 0x3c, 0x50);
    private static readonly Guid CategoryMgrClass = new(0xa4b544a1, 0x438d, 0x4b41, 0x93, 0x25, 0x86, 0x95, 0x23, 0xe2, 0xd6, 0xc7);
    private static readonly Guid DisplayAttributeMgrClass = new(0x3ce74de4, 0x53d3, 0x4d74, 0x8b, 0x83, 0x43, 0x1b, 0x38, 0x28, 0xba, 0x53);

    // The notice goes to the document it was posted for, which a later focus does not change; a document detached meanwhile has
    // no sink and tells nothing.
    private static readonly SendOrPostCallback TellLayout = static state =>
    {
        var document = (TsfDocument)state!;
        document.LayoutPosted = false;
        document.LayoutChanged();
    };

    private readonly Win32Window _window;
    private ITfThreadMgr* _threads;
    private ITfCategoryMgr* _categories;
    private ITfDisplayAttributeMgr* _displayAttributes;
    private uint _clientId;
    private Document? _attached;

    private TsfTextInput(Win32Window window) => _window = window;

    /// <summary>
    /// Gets the focused field's document; <see langword="null"/> while none is attached.
    /// </summary>
    internal TsfDocument? Attached => _attached?.Value;

    /// <summary>
    /// Makes the input method of a window, on the UI thread; <see langword="null"/> when TSF cannot be made, and the window keeps
    /// the committed text of <c>WM_CHAR</c> alone (design §4.7, "Degradation").
    /// </summary>
    /// <param name="window">The window.</param>
    /// <returns>The input method, or <see langword="null"/>.</returns>
    public static TsfTextInput? TryCreate(Win32Window window)
    {
        var input = new TsfTextInput(window);
        if (!input.Start())
        {
            input.Dispose();
            return null;
        }

        return input;
    }

    /// <inheritdoc/>
    public void Attach(ITextInputClient client)
    {
        Detach();
        if (client.Kind == TextInputKind.Password)
        {
            // A hidden text shows nothing to an input method's windows (design §4.7, "Hidden and read-only fields").
            Off();
            return;
        }

        var document = new TsfDocument(client, ToScreen, Screen, FromScreen, new TsfAttributes.TsfManagers(_categories, _displayAttributes));
        nint id = NativeHandleTable.Register(document);
        ITextStoreACP2* store = TsfTextStore.Create(id);
        ITfTextEditSink* editSink = TsfEditSink.Create(id);
        ITfDocumentMgr* manager = null;
        ITfContext* context = null;
        ITfSource* source = null;
        uint cookie = 0;
        try
        {
            Com.ThrowIfFailed(_threads->CreateDocumentMgr(&manager), "ITfThreadMgr::CreateDocumentMgr");
            uint editCookie;
            Com.ThrowIfFailed(manager->CreateContext(_clientId, 0, (IUnknown*)store, &context, &editCookie), "ITfDocumentMgr::CreateContext");
            Com.ThrowIfFailed(manager->Push(context), "ITfDocumentMgr::Push");
            Com.ThrowIfFailed(context->QueryInterface(__uuidof<ITfSource>(), (void**)&source), "ITfContext::QueryInterface(ITfSource)");
            Com.ThrowIfFailed(source->AdviseSink(__uuidof<ITfTextEditSink>(), (IUnknown*)editSink, &cookie), "ITfSource::AdviseSink");
        }
        catch
        {
            Release(new Document(document, id, store, editSink, manager, context, source, cookie));
            throw;
        }

        _attached = new Document(document, id, store, editSink, manager, context, source, cookie);
        ITfDocumentMgr* previous = null;
        Com.ThrowIfFailed(_threads->AssociateFocus((HWND)_window.Handle, manager, &previous), "ITfThreadMgr::AssociateFocus");
        Com.Release(ref previous);
        _ = ImmAssociateContextEx((HWND)_window.Handle, HIMC.NULL, InputContextDefault);
    }

    /// <inheritdoc/>
    public void Detach()
    {
        if (_attached is not { } attached)
        {
            Off();
            return;
        }

        CommitComposition();
        _attached = null;
        Off();
        Release(attached);
    }

    /// <inheritdoc/>
    public void TextChanged(int start, int oldEnd, int newEnd) => _attached?.Value.TextChanged(start, oldEnd, newEnd);

    /// <inheritdoc/>
    public void SelectionChanged() => _attached?.Value.SelectionChanged();

    /// <inheritdoc/>
    public void LayoutChanged()
    {
        // The field tells it while it draws; the manager is told after the frame, as it asks the field for boxes again at once.
        if (_attached?.Value is not { LayoutPosted: false } document)
        {
            return;
        }

        if (SynchronizationContext.Current is { } ui)
        {
            document.LayoutPosted = true;
            ui.Post(TellLayout, document);
        }
        else
        {
            document.LayoutChanged();
        }
    }

    /// <inheritdoc/>
    public void CommitComposition()
    {
        if (_attached is not { } attached || !attached.Value.Composing)
        {
            return;
        }

        ITfContextOwnerCompositionServices* services;
        if (attached.Context->QueryInterface(__uuidof<ITfContextOwnerCompositionServices>(), (void**)&services) >= 0)
        {
            // The composition ends inside the call (OnEndComposition comes before it returns, spike #510).
            _ = services->TerminateComposition(null);
            _ = services->Release();
        }

        if (attached.Value.Composing)
        {
            attached.Value.EndComposition();
        }
    }

    /// <summary>
    /// Detaches the document and ends the thread manager. Called before the window is destroyed: an <c>AssociateFocus</c> on a
    /// destroyed window fails (spike #510).
    /// </summary>
    public void Dispose()
    {
        if (_threads is not null)
        {
            Detach();
            _ = _threads->Deactivate();
        }

        Com.Release(ref _displayAttributes);
        Com.Release(ref _categories);
        Com.Release(ref _threads);
    }

    private static void Release(Document attached)
    {
        if (attached.Source is not null)
        {
            if (attached.Cookie != 0)
            {
                _ = attached.Source->UnadviseSink(attached.Cookie);
            }

            _ = attached.Source->Release();
        }

        if (attached.Manager is not null)
        {
            _ = attached.Manager->Pop(PopAll);
            _ = attached.Manager->Release();
        }

        if (attached.Context is not null)
        {
            _ = attached.Context->Release();
        }

        attached.Value.Close();
        TsfEditSink.Release(attached.EditSink);
        TsfTextStore.Release(attached.Store);
        NativeHandleTable.Release(attached.Id);
    }

    private bool Start()
    {
        ITfThreadMgr* threads = null;
        Guid threadClass = ThreadMgrClass;
        Guid threadInterface = __uuidof<ITfThreadMgr>();
        if (CoCreateInstance(&threadClass, null, (uint)CLSCTX.CLSCTX_INPROC_SERVER, &threadInterface, (void**)&threads) < 0)
        {
            return false;
        }

        _threads = threads;
        uint clientId;
        if (_threads->Activate(&clientId) < 0)
        {
            Com.Release(ref _threads);
            return false;
        }

        _clientId = clientId;
        ITfCategoryMgr* categories = null;
        Guid categoryClass = CategoryMgrClass;
        Guid categoryInterface = __uuidof<ITfCategoryMgr>();
        if (CoCreateInstance(&categoryClass, null, (uint)CLSCTX.CLSCTX_INPROC_SERVER, &categoryInterface, (void**)&categories) >= 0)
        {
            _categories = categories;
        }

        ITfDisplayAttributeMgr* displayAttributes = null;
        Guid displayClass = DisplayAttributeMgrClass;
        Guid displayInterface = __uuidof<ITfDisplayAttributeMgr>();
        if (CoCreateInstance(&displayClass, null, (uint)CLSCTX.CLSCTX_INPROC_SERVER, &displayInterface, (void**)&displayAttributes) >= 0)
        {
            _displayAttributes = displayAttributes;
        }

        // Without the two managers the clauses come with no attributes, and the field draws the text being composed plainly. Each
        // window's input method holds its own, and hands them to its documents.
        return true;
    }

    // The input method off for the window: no document, and no IMM32 input context (spike #510).
    private void Off()
    {
        if (_threads is null)
        {
            return;
        }

        ITfDocumentMgr* previous = null;
        _ = _threads->AssociateFocus((HWND)_window.Handle, null, &previous);
        Com.Release(ref previous);
        _ = ImmAssociateContextEx((HWND)_window.Handle, HIMC.NULL, 0);
    }

    // A box in the window's logical pixels as the screen's physical pixels.
    private RECT ToScreen(Rect box)
    {
        float scale = _window.ScaleFactor;
        var topLeft = new POINT((int)MathF.Floor(box.X * scale), (int)MathF.Floor(box.Y * scale));
        var bottomRight = new POINT((int)MathF.Ceiling((box.X + box.Width) * scale), (int)MathF.Ceiling((box.Y + box.Height) * scale));
        _ = ClientToScreen((HWND)_window.Handle, &topLeft);
        _ = ClientToScreen((HWND)_window.Handle, &bottomRight);
        return new RECT(topLeft.x, topLeft.y, bottomRight.x, bottomRight.y);
    }

    // A point of the screen's physical pixels as the window's logical pixels.
    private Vector2 FromScreen(POINT point)
    {
        _ = ScreenToClient((HWND)_window.Handle, &point);
        float scale = _window.ScaleFactor;
        return new Vector2(point.x / scale, point.y / scale);
    }

    private RECT Screen()
    {
        RECT client;
        _ = GetClientRect((HWND)_window.Handle, &client);
        var topLeft = new POINT(client.left, client.top);
        var bottomRight = new POINT(client.right, client.bottom);
        _ = ClientToScreen((HWND)_window.Handle, &topLeft);
        _ = ClientToScreen((HWND)_window.Handle, &bottomRight);
        return new RECT(topLeft.x, topLeft.y, bottomRight.x, bottomRight.y);
    }

    // The attached document with what TSF holds of it.
    private readonly struct Document(
        TsfDocument value,
        nint id,
        ITextStoreACP2* store,
        ITfTextEditSink* editSink,
        ITfDocumentMgr* manager,
        ITfContext* context,
        ITfSource* source,
        uint cookie
    )
    {
        public TsfDocument Value { get; } = value;

        public nint Id { get; } = id;

        public ITextStoreACP2* Store { get; } = store;

        public ITfTextEditSink* EditSink { get; } = editSink;

        public ITfDocumentMgr* Manager { get; } = manager;

        public ITfContext* Context { get; } = context;

        public ITfSource* Source { get; } = source;

        public uint Cookie { get; } = cookie;
    }
}
