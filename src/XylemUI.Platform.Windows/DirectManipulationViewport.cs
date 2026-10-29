using System.Runtime.InteropServices;
using TerraFX.Interop.DirectX;
using TerraFX.Interop.Windows;
using static TerraFX.Interop.Windows.Windows;

namespace XylemUI.Platform.Windows;

/// <summary>
/// A window's DirectManipulation viewport for a touchpad's pan (design §7, "Touch and pen", row I1c): made as Flutter's
/// <c>direct_manipulation.cc</c> makes it (lines 179 to 214), without scaling, and torn down in Chromium's order
/// (<c>direct_manipulation_helper_win.cc</c> lines 290 to 301).
/// </summary>
/// <remarks>
/// It belongs to the UI thread of its window, which calls its handler back (Microsoft's <c>AddEventHandler</c> page). The handler
/// finds the window by its id in <see cref="NativeHandleTable"/>, so native code holds no managed address.
/// </remarks>
internal sealed unsafe class DirectManipulationViewport : ITouchpadViewport
{
    private readonly HWND _hwnd;
    private IDirectManipulationManager* _manager;
    private IDirectManipulationUpdateManager* _update;
    private IDirectManipulationViewport* _viewport;
    private ViewportEventHandler* _handler;
    private uint _cookie;
    private bool _activated;

    private DirectManipulationViewport(HWND hwnd) => _hwnd = hwnd;

    /// <summary>
    /// Makes the viewport of a window, or returns <see langword="null"/> when DirectManipulation cannot be made (no COM on the
    /// thread, an OS without it): the window then goes on without, and its touchpad scrolls by the wheel's messages (Flutter's
    /// <c>direct_manipulation.cc</c> lines 14 to 21).
    /// </summary>
    /// <param name="hwnd">The window.</param>
    /// <param name="windowId">The window's id in <see cref="NativeHandleTable"/>, which the handler resolves.</param>
    /// <param name="width">The client width, in physical pixels.</param>
    /// <param name="height">The client height, in physical pixels.</param>
    /// <returns>The viewport, or <see langword="null"/>.</returns>
    public static DirectManipulationViewport? TryCreate(HWND hwnd, nint windowId, int width, int height)
    {
        var made = new DirectManipulationViewport(hwnd);
        if (made.Create(windowId, width, height))
        {
            return made;
        }

        made.Dispose();
        return null;
    }

    /// <inheritdoc />
    public void SetContact(uint pointerId) => _ = _viewport->SetContact(pointerId);

    /// <inheritdoc />
    public void Update() => _ = _update->Update(null);

    /// <inheritdoc />
    public void Resize(int width, int height)
    {
        var rect = new RECT { right = width, bottom = height };
        _ = _viewport->SetViewportRect(&rect);
    }

    /// <inheritdoc />
    public (float X, float Y) ReadTranslation()
    {
        IDirectManipulationContent* content = null;
        Guid iid = __uuidof<IDirectManipulationContent>();
        if (_viewport->GetPrimaryContent(&iid, (void**)&content) < 0)
        {
            return (0, 0);
        }

        try
        {
            return Translation(content);
        }
        finally
        {
            Com.Release(ref content);
        }
    }

    /// <inheritdoc />
    public void Reset()
    {
        RECT rect;
        if (_viewport->GetViewportRect(&rect) >= 0)
        {
            _ = _viewport->ZoomToRect(rect.left, rect.top, rect.right, rect.bottom, BOOL.FALSE);
        }
    }

    /// <summary>
    /// Stops the viewport, removes the handler, abandons the viewport, and deactivates the manager, in Chromium's order, and
    /// releases each reference.
    /// </summary>
    public void Dispose()
    {
        if (_viewport is not null)
        {
            _ = _viewport->Stop();
            if (_cookie != 0)
            {
                _ = _viewport->RemoveEventHandler(_cookie);
                _cookie = 0;
            }

            _ = _viewport->Abandon();
        }

        if (_activated)
        {
            _ = _manager->Deactivate(_hwnd);
            _activated = false;
        }

        Com.Release(ref _viewport);
        Com.Release(ref _update);
        Com.Release(ref _manager);
        if (_handler is not null)
        {
            ViewportEventHandler.Release(_handler);
            _handler = null;
        }
    }

    // The translation of a content's transform: a 3 x 2 matrix whose fifth and sixth values move it (Flutter's
    // direct_manipulation.cc lines 49 to 63).
    internal static (float X, float Y) Translation(IDirectManipulationContent* content)
    {
        float* matrix = stackalloc float[6];
        return content->GetContentTransform(matrix, 6) >= 0 ? (matrix[4], matrix[5]) : (0, 0);
    }

    private bool Create(nint windowId, int width, int height)
    {
        Guid clsid = __uuidof<DirectManipulationManager>();
        Guid managerIid = __uuidof<IDirectManipulationManager>();
        IDirectManipulationManager* manager = null;
        if (CoCreateInstance(&clsid, null, (uint)CLSCTX.CLSCTX_INPROC_SERVER, &managerIid, (void**)&manager) < 0)
        {
            return false;
        }

        _manager = manager;
        Guid updateIid = __uuidof<IDirectManipulationUpdateManager>();
        IDirectManipulationUpdateManager* update = null;
        if (_manager->GetUpdateManager(&updateIid, (void**)&update) < 0)
        {
            return false;
        }

        _update = update;
        Guid viewportIid = __uuidof<IDirectManipulationViewport>();
        IDirectManipulationViewport* viewport = null;
        if (_manager->CreateViewport(null, _hwnd, &viewportIid, (void**)&viewport) < 0)
        {
            return false;
        }

        _viewport = viewport;

        // No SCALING: the zoom comes with row I2. No RAILS: a box that scrolls both ways moves along the pan's vector, as a touch's
        // drag does (design §7, "Touch and pen", I1c).
        DIRECTMANIPULATION_CONFIGURATION configuration =
            DIRECTMANIPULATION_CONFIGURATION.DIRECTMANIPULATION_CONFIGURATION_INTERACTION
            | DIRECTMANIPULATION_CONFIGURATION.DIRECTMANIPULATION_CONFIGURATION_TRANSLATION_X
            | DIRECTMANIPULATION_CONFIGURATION.DIRECTMANIPULATION_CONFIGURATION_TRANSLATION_Y
            | DIRECTMANIPULATION_CONFIGURATION.DIRECTMANIPULATION_CONFIGURATION_TRANSLATION_INERTIA;
        if (
            _viewport->ActivateConfiguration(configuration) < 0
            || _viewport->SetViewportOptions(DIRECTMANIPULATION_VIEWPORT_OPTIONS.DIRECTMANIPULATION_VIEWPORT_OPTIONS_MANUALUPDATE) < 0
        )
        {
            return false;
        }

        _handler = ViewportEventHandler.Create(windowId);
        uint cookie;
        if (_viewport->AddEventHandler(_hwnd, (IDirectManipulationViewportEventHandler*)_handler, &cookie) < 0)
        {
            return false;
        }

        _cookie = cookie;
        var rect = new RECT { right = width, bottom = height };
        if (_viewport->SetViewportRect(&rect) < 0 || _manager->Activate(_hwnd) < 0)
        {
            return false;
        }

        _activated = true;
        if (_viewport->Enable() < 0)
        {
            return false;
        }

        _ = _update->Update(null);
        return true;
    }
}

/// <summary>
/// The window's handler of its viewport's events (design §7, "Touch and pen", I1c): one native object with two interfaces,
/// <c>IDirectManipulationViewportEventHandler</c> and <c>IDirectManipulationInteractionEventHandler</c> (Windows 8.1), each a field
/// that points to its own table of <see cref="UnmanagedCallersOnlyAttribute"/> functions. It counts its references with
/// <see cref="Interlocked"/>, as the interfaces' pages ask, and is freed when the count reaches 0.
/// </summary>
/// <remarks>
/// Each callback resolves the window by its id and catches every exception at its entry (.cursor/rules/interop.mdc).
/// </remarks>
internal unsafe struct ViewportEventHandler
{
    private static readonly void** ViewportTable = CreateViewportTable();
    private static readonly void** InteractionTable = CreateInteractionTable();

    private void** _viewportTable;
    private void** _interactionTable;
    private int _references;
    private nint _windowId;

    /// <summary>
    /// Allocates a handler with one reference, its owner's.
    /// </summary>
    /// <param name="windowId">The window's id in <see cref="NativeHandleTable"/>.</param>
    /// <returns>The handler.</returns>
    public static ViewportEventHandler* Create(nint windowId)
    {
        var handler = (ViewportEventHandler*)NativeMemory.AllocZeroed((nuint)sizeof(ViewportEventHandler));
        handler->_viewportTable = ViewportTable;
        handler->_interactionTable = InteractionTable;
        handler->_references = 1;
        handler->_windowId = windowId;
        return handler;
    }

    /// <summary>
    /// Releases the owner's reference.
    /// </summary>
    /// <param name="handler">The handler.</param>
    public static void Release(ViewportEventHandler* handler) => _ = ReleaseThis(handler);

    private static uint ReleaseThis(ViewportEventHandler* self)
    {
        int left = Interlocked.Decrement(ref self->_references);
        if (left == 0)
        {
            NativeMemory.Free(self);
        }

        return (uint)left;
    }

    // The second interface's pointer is its field; the object starts one pointer before it.
    private static ViewportEventHandler* FromInteraction(void* face) => (ViewportEventHandler*)((void**)face - 1);

    private static int QueryInterfaceThis(ViewportEventHandler* self, Guid* iid, void** result)
    {
        if (*iid == IID.IID_IUnknown || *iid == __uuidof<IDirectManipulationViewportEventHandler>())
        {
            *result = self;
        }
        else if (*iid == __uuidof<IDirectManipulationInteractionEventHandler>())
        {
            *result = &self->_interactionTable;
        }
        else
        {
            *result = null;
            return E.E_NOINTERFACE;
        }

        _ = Interlocked.Increment(ref self->_references);
        return S.S_OK;
    }

    private static void** CreateViewportTable()
    {
        // IUnknown, then IDirectManipulationViewportEventHandler in directmanipulation.h. One table for the process, never freed.
        void** table = (void**)NativeMemory.Alloc((nuint)(6 * sizeof(void*)));
        table[0] = (delegate* unmanaged<ViewportEventHandler*, Guid*, void**, int>)&QueryInterface;
        table[1] = (delegate* unmanaged<ViewportEventHandler*, uint>)&AddRef;
        table[2] = (delegate* unmanaged<ViewportEventHandler*, uint>)&ReleaseCallback;
        table[3] = (delegate* unmanaged<ViewportEventHandler*, void*, int, int, int>)&OnViewportStatusChanged;
        table[4] = (delegate* unmanaged<ViewportEventHandler*, void*, int>)&OnViewportUpdated;
        table[5] = (delegate* unmanaged<ViewportEventHandler*, void*, IDirectManipulationContent*, int>)&OnContentUpdated;
        return table;
    }

    private static void** CreateInteractionTable()
    {
        // IUnknown, then IDirectManipulationInteractionEventHandler.
        void** table = (void**)NativeMemory.Alloc((nuint)(4 * sizeof(void*)));
        table[0] = (delegate* unmanaged<void*, Guid*, void**, int>)&InteractionQueryInterface;
        table[1] = (delegate* unmanaged<void*, uint>)&InteractionAddRef;
        table[2] = (delegate* unmanaged<void*, uint>)&InteractionRelease;
        table[3] = (delegate* unmanaged<void*, void*, int, int>)&OnInteraction;
        return table;
    }

    // The reference counting and the interface queries call nothing that throws, so they have no catch.
    [UnmanagedCallersOnly]
    private static int QueryInterface(ViewportEventHandler* self, Guid* iid, void** result) => QueryInterfaceThis(self, iid, result);

    [UnmanagedCallersOnly]
    private static uint AddRef(ViewportEventHandler* self) => (uint)Interlocked.Increment(ref self->_references);

    [UnmanagedCallersOnly]
    private static uint ReleaseCallback(ViewportEventHandler* self) => ReleaseThis(self);

    [UnmanagedCallersOnly]
    private static int InteractionQueryInterface(void* face, Guid* iid, void** result) => QueryInterfaceThis(FromInteraction(face), iid, result);

    [UnmanagedCallersOnly]
    private static uint InteractionAddRef(void* face) => (uint)Interlocked.Increment(ref FromInteraction(face)->_references);

    [UnmanagedCallersOnly]
    private static uint InteractionRelease(void* face) => ReleaseThis(FromInteraction(face));

    // The viewport's pointer is not read, and the statuses come as their int values: the short types keep the table's casts on one
    // line.
    [UnmanagedCallersOnly]
    private static int OnViewportStatusChanged(ViewportEventHandler* self, void* viewport, int current, int previous)
    {
        try
        {
            if (NativeHandleTable.TryResolve(self->_windowId, out Win32Window? window))
            {
                window.TouchpadStatusChanged((DIRECTMANIPULATION_STATUS)current, (DIRECTMANIPULATION_STATUS)previous);
            }
        }
        catch (Exception exception)
        {
            Win32MessageLoop.ReportCallbackException(exception);
        }

        return S.S_OK;
    }

    [UnmanagedCallersOnly]
    private static int OnViewportUpdated(ViewportEventHandler* self, void* viewport) => S.S_OK;

    [UnmanagedCallersOnly]
    private static int OnContentUpdated(ViewportEventHandler* self, void* viewport, IDirectManipulationContent* content)
    {
        try
        {
            if (NativeHandleTable.TryResolve(self->_windowId, out Win32Window? window))
            {
                (float x, float y) = DirectManipulationViewport.Translation(content);
                window.TouchpadContentUpdated(x, y);
            }
        }
        catch (Exception exception)
        {
            Win32MessageLoop.ReportCallbackException(exception);
        }

        return S.S_OK;
    }

    [UnmanagedCallersOnly]
    private static int OnInteraction(void* face, void* viewport, int type)
    {
        var interaction = (DIRECTMANIPULATION_INTERACTION_TYPE)type;
        try
        {
            if (NativeHandleTable.TryResolve(FromInteraction(face)->_windowId, out Win32Window? window))
            {
                if (interaction == DIRECTMANIPULATION_INTERACTION_TYPE.DIRECTMANIPULATION_INTERACTION_BEGIN)
                {
                    window.TouchpadInteraction(begin: true);
                }
                else if (interaction == DIRECTMANIPULATION_INTERACTION_TYPE.DIRECTMANIPULATION_INTERACTION_END)
                {
                    window.TouchpadInteraction(begin: false);
                }
            }
        }
        catch (Exception exception)
        {
            Win32MessageLoop.ReportCallbackException(exception);
        }

        return S.S_OK;
    }
}
