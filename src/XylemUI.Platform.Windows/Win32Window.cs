using System.ComponentModel;
using System.Runtime.InteropServices;
using TerraFX.Interop.Windows;
using static TerraFX.Interop.Windows.Windows;

namespace XylemUI.Platform.Windows;

/// <summary>
/// A top-level Win32 window.
/// </summary>
/// <remarks>
/// Create, use, and dispose the window on the UI thread (<see cref="UiThread"/>). Native code identifies the window only by a
/// generational id from the handle table, never by its address (design §2.12, Invariant 8).
/// </remarks>
public sealed unsafe class Win32Window : IDisposable
{
    private const string ClassName = "XylemUI.Window";
    private const uint WmClose = WM.WM_CLOSE;
    private const uint WmDestroy = WM.WM_DESTROY;
    private const uint WmNcCreate = WM.WM_NCCREATE;
    private const uint WmNcDestroy = WM.WM_NCDESTROY;

    private static bool _classRegistered;

    private readonly nint _id;
    private HWND _hwnd;

    /// <summary>
    /// Initializes a new instance of the <see cref="Win32Window"/> class. The window is hidden until <see cref="Show"/> is called.
    /// </summary>
    /// <param name="title">The title bar text.</param>
    /// <param name="width">The outer width in physical pixels.</param>
    /// <param name="height">The outer height in physical pixels.</param>
    /// <exception cref="InvalidOperationException">The calling thread is not the UI thread.</exception>
    /// <exception cref="Win32Exception">The window could not be created.</exception>
    public Win32Window(string title, int width, int height)
    {
        ArgumentNullException.ThrowIfNull(title);
        UiThread.VerifyAccess();
        EnsureClassRegistered();

        _id = NativeHandleTable.Register(this);
        HWND hwnd;
        fixed (char* className = ClassName)
        {
            fixed (char* windowName = title)
            {
                hwnd = CreateWindowExW(
                    0,
                    className,
                    windowName,
                    (uint)WS.WS_OVERLAPPEDWINDOW,
                    CW_USEDEFAULT,
                    CW_USEDEFAULT,
                    width,
                    height,
                    HWND.NULL,
                    HMENU.NULL,
                    GetModuleHandleW(null),
                    (void*)_id
                );
            }
        }

        if (hwnd == HWND.NULL)
        {
            int error = Marshal.GetLastSystemError();
            NativeHandleTable.Release(_id);
            throw new Win32Exception(error);
        }

        _hwnd = hwnd;
    }

    /// <summary>
    /// Occurs on the UI thread after the window has been destroyed.
    /// </summary>
    public event EventHandler? Closed;

    /// <summary>
    /// Gets a value indicating whether the native window still exists.
    /// </summary>
    public bool IsAlive => _hwnd != HWND.NULL;

    /// <summary>
    /// Gets the native window handle, or zero after the window has been destroyed.
    /// </summary>
    internal HWND Hwnd => _hwnd;

    /// <summary>
    /// Gets or sets a hook that sees every message before the window handles it. Returning <see langword="true"/> marks the
    /// message as handled. Used by tests to run code inside the window procedure.
    /// </summary>
    internal Func<Win32Window, uint, bool>? MessageHook { get; set; }

    /// <summary>
    /// Shows the window.
    /// </summary>
    /// <exception cref="InvalidOperationException">The calling thread is not the UI thread.</exception>
    /// <exception cref="ObjectDisposedException">The window has been destroyed.</exception>
    public void Show()
    {
        UiThread.VerifyAccess();
        ObjectDisposedException.ThrowIf(!IsAlive, this);
        _ = ShowWindow(_hwnd, SW.SW_SHOW);
    }

    /// <summary>
    /// Asks the window to close, as the close button does. The window is destroyed when the message loop handles the request.
    /// </summary>
    /// <exception cref="InvalidOperationException">The calling thread is not the UI thread.</exception>
    public void RequestClose()
    {
        UiThread.VerifyAccess();
        if (IsAlive)
        {
            _ = PostMessageW(_hwnd, WmClose, default, default);
        }
    }

    /// <summary>
    /// Destroys the window at once if it still exists.
    /// </summary>
    public void Dispose()
    {
        UiThread.VerifyAccess();
        if (IsAlive)
        {
            _ = DestroyWindow(_hwnd);
        }
    }

    private static void EnsureClassRegistered()
    {
        if (_classRegistered)
        {
            return;
        }

        fixed (char* className = ClassName)
        {
            var windowClass = new WNDCLASSEXW
            {
                cbSize = (uint)sizeof(WNDCLASSEXW),
                style = (uint)(CS.CS_HREDRAW | CS.CS_VREDRAW),
                lpfnWndProc = &WindowProcedure,
                hInstance = GetModuleHandleW(null),
                hCursor = LoadCursorW(HINSTANCE.NULL, IDC.IDC_ARROW),
                lpszClassName = className,
            };

            if (RegisterClassExW(&windowClass) == 0)
            {
                throw new Win32Exception(Marshal.GetLastSystemError());
            }
        }

        _classRegistered = true;
    }

    // Every message for every XylemUI window enters here. No managed exception may leave this method (Invariant 13).
    [UnmanagedCallersOnly]
    private static LRESULT WindowProcedure(HWND hwnd, uint message, WPARAM wParam, LPARAM lParam)
    {
        try
        {
            if (message == WmNcCreate)
            {
                // The id passed to CreateWindowExW comes back here before any message that needs the window object.
                var create = (CREATESTRUCTW*)lParam;
                _ = SetWindowLongPtrW(hwnd, GWLP.GWLP_USERDATA, (nint)create->lpCreateParams);
            }

            nint id = GetWindowLongPtrW(hwnd, GWLP.GWLP_USERDATA);
            if (id != 0 && NativeHandleTable.TryResolve(id, out Win32Window? window) && window.HandleMessage(hwnd, message))
            {
                return 0;
            }
        }
        catch (Exception exception)
        {
            Win32MessageLoop.ReportCallbackException(exception);
        }

        return DefWindowProcW(hwnd, message, wParam, lParam);
    }

    private bool HandleMessage(HWND hwnd, uint message)
    {
        if (MessageHook is { } hook && hook(this, message))
        {
            return true;
        }

        switch (message)
        {
            case WmClose:
                _ = DestroyWindow(hwnd);
                return true;
            case WmDestroy:
                Closed?.Invoke(this, EventArgs.Empty);
                return true;
            case WmNcDestroy:
                // The last message for this window: no message after it can resolve the id.
                _ = SetWindowLongPtrW(hwnd, GWLP.GWLP_USERDATA, 0);
                NativeHandleTable.Release(_id);
                _hwnd = HWND.NULL;
                return false;
            default:
                return false;
        }
    }
}
