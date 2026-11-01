using System.Runtime.InteropServices;
using TerraFX.Interop.Windows;
using static TerraFX.Interop.Windows.Windows;

namespace XylemUI.Platform.Windows;

/// <content>
/// OLE's drag and drop (design §7, "Drag and drop", row DD1): the window's drop target, which hands a drag from the OS to the input
/// as a drag of <see cref="DroppedFiles"/> or <see cref="DroppedText"/>, and the drag the input hands to the OS.
/// </content>
public sealed unsafe partial class Win32Window
{
    private const uint WmTimer = WM.WM_TIMER;

    // The timer that runs the frames while OLE's DoDragDrop runs its own loop, in which the message loop's frame clock is not waited
    // on (design §2.11.1). The id is the window's own.
    private const nuint DragFrameTimer = 0xD7A6;
    private const uint DragFrameMilliseconds = 16;

    // The size of DROPFILES (shlobj_core.h): pFiles, pt, fNC, fWide.
    private const int DropFilesSize = 20;

    private OleDropTarget* _dropTarget;
    private IDropTargetHelper* _dropHelper;
    private bool _dropRegistered;

    /// <summary>
    /// Gets or sets what the drags from the OS over the window are handed to: the window's input (design §7, "Drag and drop").
    /// </summary>
    internal IExternalDropTarget? ExternalDrops { get; set; }

    /// <summary>
    /// Hands a drag that left the window to the OS (design §7, "Drag and drop", "Out to the OS"): a data object of the shell
    /// (<c>SHCreateDataObject</c>, which takes any format by <c>SetData</c>) holding the files as <c>CF_HDROP</c> and the text as
    /// <c>CF_UNICODETEXT</c>, and <c>DoDragDrop</c> with the window's drop source. A timer runs the frames meanwhile.
    /// </summary>
    /// <param name="data">What the value is outside the app.</param>
    /// <param name="allowed">The effects the source allows.</param>
    /// <returns>The effect the drop chose, or <see cref="DragEffects.None"/>.</returns>
    public DragEffects StartExternalDrag(ExternalDragData data, DragEffects allowed)
    {
        ArgumentNullException.ThrowIfNull(data);
        UiThread.VerifyAccess();
        IDataObject* dataObject = CreateDataObject(data);
        if (dataObject is null)
        {
            return DragEffects.None;
        }

        OleDropSource* source = null;
        try
        {
            source = OleDropSource.Create();
            _ = SetTimer(_hwnd, DragFrameTimer, DragFrameMilliseconds, null);
            uint effect = 0;
            int result = DoDragDrop(dataObject, (IDropSource*)source, (uint)allowed, &effect);
            return result == Ole.DragDropDrop ? (DragEffects)(effect & (uint)allowed) : DragEffects.None;
        }
        finally
        {
            _ = KillTimer(_hwnd, DragFrameTimer);
            if (source is not null)
            {
                OleDropSource.Release(source);
            }

            Com.Release(ref dataObject);
        }
    }

    /// <summary>
    /// Makes a data object of the shell (<c>SHCreateDataObject</c>) holding the files as <c>CF_HDROP</c> and the text as
    /// <c>CF_UNICODETEXT</c>; <see langword="null"/> when it cannot be made or holds neither. The caller releases it.
    /// </summary>
    /// <param name="data">The files and the text.</param>
    /// <returns>The data object with one reference, or <see langword="null"/>.</returns>
    internal static IDataObject* CreateDataObject(ExternalDragData data)
    {
        IDataObject* dataObject = null;
        Guid iid = __uuidof<IDataObject>();
        if (SHCreateDataObject(null, 0, null, null, &iid, (void**)&dataObject) < 0)
        {
            return null;
        }

        bool any = false;
        if (data.Files is { Count: > 0 } files)
        {
            any |= SetGlobal(dataObject, CF.CF_HDROP, DropFiles(files));
        }

        if (data.Text is { } text)
        {
            any |= SetGlobal(dataObject, CF.CF_UNICODETEXT, MemoryMarshal.AsBytes((text + '\0').AsSpan()).ToArray());
        }

        if (!any)
        {
            Com.Release(ref dataObject);
        }

        return dataObject;
    }

    /// <summary>
    /// Returns what a data object holds as a dropped value: <see cref="DroppedFiles"/> from <c>CF_HDROP</c>, read with
    /// <c>DragQueryFileW</c>, else <see cref="DroppedText"/> from <c>CF_UNICODETEXT</c>, else <see langword="null"/>.
    /// </summary>
    /// <param name="data">The data object.</param>
    /// <returns>The value, or <see langword="null"/>.</returns>
    internal static object? ReadDropped(IDataObject* data)
    {
        var format = new FORMATETC
        {
            cfFormat = CF.CF_HDROP,
            dwAspect = (uint)DVASPECT.DVASPECT_CONTENT,
            lindex = -1,
            tymed = (uint)TYMED.TYMED_HGLOBAL,
        };
        STGMEDIUM medium;
        if (data->GetData(&format, &medium) >= 0)
        {
            try
            {
                var drop = (HDROP)(void*)medium.hGlobal;
                uint count = DragQueryFileW(drop, uint.MaxValue, null, 0);
                var paths = new string[count];
                for (uint i = 0; i < count; i++)
                {
                    uint length = DragQueryFileW(drop, i, null, 0);
                    var path = new char[length + 1];
                    fixed (char* buffer = path)
                    {
                        _ = DragQueryFileW(drop, i, buffer, length + 1);
                    }

                    paths[i] = new string(path, 0, (int)length);
                }

                return new DroppedFiles(paths);
            }
            finally
            {
                ReleaseStgMedium(&medium);
            }
        }

        format.cfFormat = CF.CF_UNICODETEXT;
        if (data->GetData(&format, &medium) >= 0)
        {
            try
            {
                var text = (char*)GlobalLock(medium.hGlobal);
                if (text is null)
                {
                    return null;
                }

                try
                {
                    return new DroppedText(new string(text));
                }
                finally
                {
                    _ = GlobalUnlock(medium.hGlobal);
                }
            }
            finally
            {
                ReleaseStgMedium(&medium);
            }
        }

        return null;
    }

    /// <summary>
    /// Returns the bytes of a <c>CF_HDROP</c>: a <c>DROPFILES</c> of wide paths, each ended by a null, and a null after the last.
    /// </summary>
    /// <param name="files">The files' full paths.</param>
    /// <returns>The bytes.</returns>
    internal static byte[] DropFiles(IReadOnlyList<string> files)
    {
        int chars = 1;
        foreach (string file in files)
        {
            chars += file.Length + 1;
        }

        var bytes = new byte[DropFilesSize + (chars * sizeof(char))];
        BitConverter.TryWriteBytes(bytes.AsSpan(0, 4), DropFilesSize);
        BitConverter.TryWriteBytes(bytes.AsSpan(16, 4), 1);
        Span<char> text = MemoryMarshal.Cast<byte, char>(bytes.AsSpan(DropFilesSize));
        int at = 0;
        foreach (string file in files)
        {
            file.AsSpan().CopyTo(text[at..]);
            at += file.Length + 1;
        }

        return bytes;
    }

    // Puts bytes in movable global memory into the data object as one format; the object owns the memory once SetData succeeds.
    private static bool SetGlobal(IDataObject* data, ushort format, ReadOnlySpan<byte> bytes)
    {
        HGLOBAL memory = GlobalAlloc(GMEM.GMEM_MOVEABLE, (nuint)bytes.Length);
        if (memory == HGLOBAL.NULL)
        {
            return false;
        }

        void* at = GlobalLock(memory);
        if (at is null)
        {
            _ = GlobalFree(memory);
            return false;
        }

        bytes.CopyTo(new Span<byte>(at, bytes.Length));
        _ = GlobalUnlock(memory);
        var formatEtc = new FORMATETC
        {
            cfFormat = format,
            dwAspect = (uint)DVASPECT.DVASPECT_CONTENT,
            lindex = -1,
            tymed = (uint)TYMED.TYMED_HGLOBAL,
        };
        var medium = new STGMEDIUM { tymed = (uint)TYMED.TYMED_HGLOBAL };
        medium.hGlobal = memory;
        if (data->SetData(&formatEtc, &medium, BOOL.TRUE) < 0)
        {
            _ = GlobalFree(memory);
            return false;
        }

        return true;
    }

    // Registers the window's drop target, and makes the shell's helper that draws a drag's image over it. With no OLE on the thread
    // (a thread not in a single-threaded apartment), RegisterDragDrop fails and the window takes no drops.
    private void CreateDropTarget(HWND hwnd)
    {
        OleDropTarget* target = OleDropTarget.Create(_id);
        if (RegisterDragDrop(hwnd, (IDropTarget*)target) < 0)
        {
            OleDropTarget.Release(target);
            return;
        }

        _dropTarget = target;
        _dropRegistered = true;
        Guid clsid = Ole.DragDropHelper;
        Guid iid = __uuidof<IDropTargetHelper>();
        IDropTargetHelper* helper = null;
        if (CoCreateInstance(&clsid, null, (uint)CLSCTX.CLSCTX_INPROC_SERVER, &iid, (void**)&helper) >= 0)
        {
            _dropHelper = helper;
        }
    }

    private void DestroyDropTarget(HWND hwnd)
    {
        if (_dropRegistered)
        {
            _ = RevokeDragDrop(hwnd);
            _dropRegistered = false;
        }

        Com.Release(ref _dropHelper);
        if (_dropTarget is not null)
        {
            OleDropTarget.Release(_dropTarget);
            _dropTarget = null;
        }
    }

    /// <summary>
    /// A drag from the OS came over the window (<c>IDropTarget::DragEnter</c>): its value is read, and the input starts a drag of it;
    /// the shell's helper draws the drag's image.
    /// </summary>
    /// <param name="data">The drag's data object.</param>
    /// <param name="keyState">The key state.</param>
    /// <param name="point">The point on the screen.</param>
    /// <param name="allowed">The effects the OS's source allows.</param>
    /// <returns>The effect a drop would choose now.</returns>
    internal uint DropEnter(IDataObject* data, uint keyState, POINTL point, uint allowed)
    {
        uint effect = 0;
        if (ReadDropped(data) is { } value && ExternalDrops is { } target)
        {
            (int x, int y) = ClientPoint(point);
            effect = (uint)target.DropEnter(value, x, y, (DragEffects)(allowed & 7), Ole.Modifiers(keyState)) & allowed;
        }

        if (_dropHelper is not null)
        {
            var at = new POINT { x = point.x, y = point.y };
            _ = _dropHelper->DragEnter(_hwnd, data, &at, effect);
        }

        return effect;
    }

    /// <summary>
    /// The drag from the OS moved (<c>IDropTarget::DragOver</c>).
    /// </summary>
    /// <param name="keyState">The key state.</param>
    /// <param name="point">The point on the screen.</param>
    /// <param name="allowed">The effects the OS's source allows.</param>
    /// <returns>The effect a drop would choose now.</returns>
    internal uint DropOver(uint keyState, POINTL point, uint allowed)
    {
        uint effect = 0;
        if (ExternalDrops is { } target)
        {
            (int x, int y) = ClientPoint(point);
            effect = (uint)target.DropOver(x, y, Ole.Modifiers(keyState)) & allowed;
        }

        if (_dropHelper is not null)
        {
            var at = new POINT { x = point.x, y = point.y };
            _ = _dropHelper->DragOver(&at, effect);
        }

        return effect;
    }

    /// <summary>
    /// The drag from the OS left the window, or was cancelled (<c>IDropTarget::DragLeave</c>).
    /// </summary>
    internal void DropLeave()
    {
        ExternalDrops?.DropLeave();
        if (_dropHelper is not null)
        {
            _ = _dropHelper->DragLeave();
        }
    }

    /// <summary>
    /// The drag from the OS dropped on the window (<c>IDropTarget::Drop</c>).
    /// </summary>
    /// <param name="data">The drag's data object.</param>
    /// <param name="keyState">The key state.</param>
    /// <param name="point">The point on the screen.</param>
    /// <param name="allowed">The effects the OS's source allows.</param>
    /// <returns>The effect the drop chose.</returns>
    internal uint DropDrop(IDataObject* data, uint keyState, POINTL point, uint allowed)
    {
        uint effect = 0;
        if (ExternalDrops is { } target)
        {
            (int x, int y) = ClientPoint(point);
            effect = (uint)target.Drop(x, y, Ole.Modifiers(keyState)) & allowed;
        }

        if (_dropHelper is not null)
        {
            var at = new POINT { x = point.x, y = point.y };
            _ = _dropHelper->Drop(data, &at, effect);
        }

        return effect;
    }

    // While OLE's DoDragDrop runs its loop, the timer runs the frame waiting (design §2.11.1).
    private static bool HandleDragFrameTimer(uint message, WPARAM wParam)
    {
        if (message != WmTimer || (nuint)wParam != DragFrameTimer)
        {
            return false;
        }

        Win32MessageLoop.RunPendingFrame();
        return true;
    }

    // A point on the screen, in the window's client area.
    private (int X, int Y) ClientPoint(POINTL point)
    {
        var at = new POINT { x = point.x, y = point.y };
        _ = ScreenToClient(_hwnd, &at);
        return (at.x, at.y);
    }
}
