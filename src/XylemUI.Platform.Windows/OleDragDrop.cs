using System.Runtime.InteropServices;
using TerraFX.Interop.Windows;
using static TerraFX.Interop.Windows.Windows;

namespace XylemUI.Platform.Windows;

/// <summary>
/// The values of OLE's drag and drop the window uses, from the Windows SDK's headers.
/// </summary>
internal static class Ole
{
    /// <summary>
    /// <c>DRAGDROP_S_DROP</c> (winerror.h): the drop happened.
    /// </summary>
    public const int DragDropDrop = 0x00040100;

    /// <summary>
    /// <c>DRAGDROP_S_CANCEL</c> (winerror.h): the drag was cancelled.
    /// </summary>
    public const int DragDropCancel = 0x00040101;

    /// <summary>
    /// <c>DRAGDROP_S_USEDEFAULTCURSORS</c> (winerror.h): OLE shows its default cursors.
    /// </summary>
    public const int DragDropUseDefaultCursors = 0x00040102;

    /// <summary>
    /// <c>MK_LBUTTON</c> (winuser.h).
    /// </summary>
    public const uint LeftButton = 0x0001;

    /// <summary>
    /// <c>MK_SHIFT</c> (winuser.h).
    /// </summary>
    public const uint Shift = 0x0004;

    /// <summary>
    /// <c>MK_CONTROL</c> (winuser.h).
    /// </summary>
    public const uint Control = 0x0008;

    /// <summary>
    /// <c>MK_ALT</c> (oleidl.h): the key state OLE's drop target gets with Alt held.
    /// </summary>
    public const uint Alt = 0x0020;

    /// <summary>
    /// Gets <c>CLSID_DragDropHelper</c> (shlguid.h): the shell's helper that draws the drag's image over a drop target.
    /// </summary>
    public static Guid DragDropHelper { get; } = new("4657278A-411B-11D2-839A-00C04FD918D0");

    /// <summary>
    /// Returns the modifier keys of an OLE key state.
    /// </summary>
    /// <param name="keyState">The key state (<c>grfKeyState</c>).</param>
    /// <returns>The modifiers.</returns>
    public static KeyModifiers Modifiers(uint keyState) =>
        ((keyState & Shift) != 0 ? KeyModifiers.Shift : KeyModifiers.None)
        | ((keyState & Control) != 0 ? KeyModifiers.Control : KeyModifiers.None)
        | ((keyState & Alt) != 0 ? KeyModifiers.Alt : KeyModifiers.None);
}

/// <summary>
/// A window's OLE drop target (design §7, "Drag and drop", "From the OS"): a native object whose first field points to its table
/// of <see cref="UnmanagedCallersOnlyAttribute"/> functions, <c>IUnknown</c> and <c>IDropTarget</c> (oleidl.h), as
/// <see cref="ViewportEventHandler"/> is. It counts its references with <see cref="Interlocked"/> and is freed when the count reaches
/// 0; OLE holds one from <c>RegisterDragDrop</c> to <c>RevokeDragDrop</c>.
/// </summary>
/// <remarks>
/// Each callback resolves the window by its id in <see cref="NativeHandleTable"/> and catches every exception at its entry
/// (.cursor/rules/interop.mdc).
/// </remarks>
internal unsafe struct OleDropTarget
{
    private static readonly void** Table = CreateTable();

    private void** _table;
    private int _references;
    private nint _windowId;

    /// <summary>
    /// Allocates a drop target with one reference, its owner's.
    /// </summary>
    /// <param name="windowId">The window's id in <see cref="NativeHandleTable"/>.</param>
    /// <returns>The drop target.</returns>
    public static OleDropTarget* Create(nint windowId)
    {
        var target = (OleDropTarget*)NativeMemory.AllocZeroed((nuint)sizeof(OleDropTarget));
        target->_table = Table;
        target->_references = 1;
        target->_windowId = windowId;
        return target;
    }

    /// <summary>
    /// Releases the owner's reference.
    /// </summary>
    /// <param name="target">The drop target.</param>
    public static void Release(OleDropTarget* target) => _ = ReleaseThis(target);

    private static uint ReleaseThis(OleDropTarget* self)
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
        // IUnknown, then IDropTarget in oleidl.h. One table for the process, never freed.
        void** table = (void**)NativeMemory.Alloc((nuint)(7 * sizeof(void*)));
        table[0] = (delegate* unmanaged<OleDropTarget*, Guid*, void**, int>)&QueryInterface;
        table[1] = (delegate* unmanaged<OleDropTarget*, uint>)&AddRef;
        table[2] = (delegate* unmanaged<OleDropTarget*, uint>)&ReleaseCallback;
        table[3] = (delegate* unmanaged<OleDropTarget*, IDataObject*, uint, POINTL, uint*, int>)&DragEnter;
        table[4] = (delegate* unmanaged<OleDropTarget*, uint, POINTL, uint*, int>)&DragOver;
        table[5] = (delegate* unmanaged<OleDropTarget*, int>)&DragLeave;
        table[6] = (delegate* unmanaged<OleDropTarget*, IDataObject*, uint, POINTL, uint*, int>)&Drop;
        return table;
    }

    // The reference counting and the interface queries call nothing that throws, so they have no catch.
    [UnmanagedCallersOnly]
    private static int QueryInterface(OleDropTarget* self, Guid* iid, void** result)
    {
        if (*iid == IID.IID_IUnknown || *iid == __uuidof<IDropTarget>())
        {
            *result = self;
            _ = Interlocked.Increment(ref self->_references);
            return S.S_OK;
        }

        *result = null;
        return E.E_NOINTERFACE;
    }

    [UnmanagedCallersOnly]
    private static uint AddRef(OleDropTarget* self) => (uint)Interlocked.Increment(ref self->_references);

    [UnmanagedCallersOnly]
    private static uint ReleaseCallback(OleDropTarget* self) => ReleaseThis(self);

    [UnmanagedCallersOnly]
    private static int DragEnter(OleDropTarget* self, IDataObject* data, uint keyState, POINTL point, uint* effect)
    {
        try
        {
            *effect = NativeHandleTable.TryResolve(self->_windowId, out Win32Window? window) ? window.DropEnter(data, keyState, point, *effect) : 0;
        }
        catch (Exception exception)
        {
            *effect = 0;
            Win32MessageLoop.ReportCallbackException(exception);
        }

        return S.S_OK;
    }

    [UnmanagedCallersOnly]
    private static int DragOver(OleDropTarget* self, uint keyState, POINTL point, uint* effect)
    {
        try
        {
            *effect = NativeHandleTable.TryResolve(self->_windowId, out Win32Window? window) ? window.DropOver(keyState, point, *effect) : 0;
        }
        catch (Exception exception)
        {
            *effect = 0;
            Win32MessageLoop.ReportCallbackException(exception);
        }

        return S.S_OK;
    }

    [UnmanagedCallersOnly]
    private static int DragLeave(OleDropTarget* self)
    {
        try
        {
            if (NativeHandleTable.TryResolve(self->_windowId, out Win32Window? window))
            {
                window.DropLeave();
            }
        }
        catch (Exception exception)
        {
            Win32MessageLoop.ReportCallbackException(exception);
        }

        return S.S_OK;
    }

    [UnmanagedCallersOnly]
    private static int Drop(OleDropTarget* self, IDataObject* data, uint keyState, POINTL point, uint* effect)
    {
        try
        {
            *effect = NativeHandleTable.TryResolve(self->_windowId, out Win32Window? window) ? window.DropDrop(data, keyState, point, *effect) : 0;
        }
        catch (Exception exception)
        {
            *effect = 0;
            Win32MessageLoop.ReportCallbackException(exception);
        }

        return S.S_OK;
    }
}

/// <summary>
/// The source of a drag the window hands to the OS (design §7, "Drag and drop", "Out to the OS"): <c>IUnknown</c> and
/// <c>IDropSource</c> (oleidl.h). Esc cancels, the left button's release drops, and OLE draws its default cursors, as Avalonia's
/// <c>OleDragSource.cs</c> does (lines 23 to 38); a second button does not cancel, since the left button alone starts the drag.
/// </summary>
internal unsafe struct OleDropSource
{
    private static readonly void** Table = CreateTable();

    private void** _table;
    private int _references;

    /// <summary>
    /// Allocates a drop source with one reference, its owner's.
    /// </summary>
    /// <returns>The drop source.</returns>
    public static OleDropSource* Create()
    {
        var source = (OleDropSource*)NativeMemory.AllocZeroed((nuint)sizeof(OleDropSource));
        source->_table = Table;
        source->_references = 1;
        return source;
    }

    /// <summary>
    /// Releases the owner's reference.
    /// </summary>
    /// <param name="source">The drop source.</param>
    public static void Release(OleDropSource* source) => _ = ReleaseThis(source);

    /// <summary>
    /// Returns what OLE does next for a key state: cancel on Esc, drop once the left button is up, go on otherwise.
    /// </summary>
    /// <param name="escapePressed">Whether Esc was pressed.</param>
    /// <param name="keyState">The key state.</param>
    /// <returns><c>DRAGDROP_S_CANCEL</c>, <c>DRAGDROP_S_DROP</c>, or <c>S_OK</c>.</returns>
    internal static int Continue(bool escapePressed, uint keyState) =>
        escapePressed ? Ole.DragDropCancel
        : (keyState & Ole.LeftButton) == 0 ? Ole.DragDropDrop
        : S.S_OK;

    private static uint ReleaseThis(OleDropSource* self)
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
        // IUnknown, then IDropSource in oleidl.h.
        void** table = (void**)NativeMemory.Alloc((nuint)(5 * sizeof(void*)));
        table[0] = (delegate* unmanaged<OleDropSource*, Guid*, void**, int>)&QueryInterface;
        table[1] = (delegate* unmanaged<OleDropSource*, uint>)&AddRef;
        table[2] = (delegate* unmanaged<OleDropSource*, uint>)&ReleaseCallback;
        table[3] = (delegate* unmanaged<OleDropSource*, BOOL, uint, int>)&QueryContinueDrag;
        table[4] = (delegate* unmanaged<OleDropSource*, uint, int>)&GiveFeedback;
        return table;
    }

    [UnmanagedCallersOnly]
    private static int QueryInterface(OleDropSource* self, Guid* iid, void** result)
    {
        if (*iid == IID.IID_IUnknown || *iid == __uuidof<IDropSource>())
        {
            *result = self;
            _ = Interlocked.Increment(ref self->_references);
            return S.S_OK;
        }

        *result = null;
        return E.E_NOINTERFACE;
    }

    [UnmanagedCallersOnly]
    private static uint AddRef(OleDropSource* self) => (uint)Interlocked.Increment(ref self->_references);

    [UnmanagedCallersOnly]
    private static uint ReleaseCallback(OleDropSource* self) => ReleaseThis(self);

    [UnmanagedCallersOnly]
    private static int QueryContinueDrag(OleDropSource* self, BOOL escapePressed, uint keyState) => Continue(escapePressed != BOOL.FALSE, keyState);

    [UnmanagedCallersOnly]
    private static int GiveFeedback(OleDropSource* self, uint effect) => Ole.DragDropUseDefaultCursors;
}
