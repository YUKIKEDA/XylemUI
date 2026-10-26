using System.ComponentModel;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using TerraFX.Interop.Windows;
using XylemUI.Text;
using static TerraFX.Interop.Windows.Windows;

namespace XylemUI.Platform.Windows;

/// <summary>
/// A top-level Win32 window.
/// </summary>
/// <remarks>
/// Create, use, and dispose the window on the UI thread (<see cref="UiThread"/>). Native code identifies the window only by a
/// generational id from the handle table, never by its address (design §2.12, Invariant 8).
/// </remarks>
public sealed unsafe partial class Win32Window : IDisposable
{
    private const string ClassName = "XylemUI.Window";
    private const uint WmClose = WM.WM_CLOSE;
    private const uint WmDestroy = WM.WM_DESTROY;
    private const uint WmNcCreate = WM.WM_NCCREATE;
    private const uint WmNcDestroy = WM.WM_NCDESTROY;
    private const uint WmSize = WM.WM_SIZE;
    private const uint WmSettingChange = WM.WM_SETTINGCHANGE;
    private const uint WmDpiChanged = WM.WM_DPICHANGED;
    private const uint WmWindowPosChanged = WM.WM_WINDOWPOSCHANGED;
    private const uint Style = WS.WS_OVERLAPPEDWINDOW;
    private const float BaseDpi = 96;
    private const nuint SizeMinimized = SIZE_MINIMIZED;

    private static bool _classRegistered;

    private readonly nint _id;
    private HWND _hwnd;
    private Action? _renderPendingResize;
    private bool _resizePending;
    private int _pendingWidth;
    private int _pendingHeight;
    private Action? _raiseTextRenderingChanged;
    private Action? _raiseScaleChanged;
    private HMONITOR _monitor;

    /// <summary>
    /// Initializes a new instance of the <see cref="Win32Window"/> class. The window is hidden until <see cref="Show"/> is called.
    /// </summary>
    /// <param name="title">The title bar text.</param>
    /// <param name="width">The width of the client area, in logical pixels: times <see cref="ScaleFactor"/> on the screen.</param>
    /// <param name="height">The height of the client area, in logical pixels.</param>
    /// <exception cref="InvalidOperationException">The calling thread is not the UI thread.</exception>
    /// <exception cref="Win32Exception">The window could not be created.</exception>
    /// <exception cref="InvalidOperationException">
    /// DirectWrite could not read how the window's text is drawn (<see cref="TextRendering"/>); the window was destroyed.
    /// </exception>
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
                    Style,
                    CW_USEDEFAULT,
                    CW_USEDEFAULT,
                    CW_USEDEFAULT,
                    CW_USEDEFAULT,
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

        // The window is made where Windows puts it and then sized for the scale of the monitor it is on, as GPUI and Avalonia do
        // (design §6.1.1, "Windows' display scale"). A window whose size or text rendering cannot be set is destroyed, so that no
        // native window or handle-table entry outlives the constructor that threw.
        try
        {
            ScaleFactor = GetDpiForWindow(hwnd) / BaseDpi;
            ResizeClient(hwnd, width, height);
            _monitor = MonitorFromWindow(hwnd, MONITOR.MONITOR_DEFAULTTONEAREST);
            TextRendering = TextRenderingReader(_monitor);
        }
        catch
        {
            _ = DestroyWindow(hwnd);
            throw;
        }
    }

    /// <summary>
    /// Occurs on the UI thread when <see cref="ScaleFactor"/> changed: the window moved to a monitor of another scale, or the
    /// monitor's scale changed (<c>WM_DPICHANGED</c>). The window has its new size by then.
    /// </summary>
    public event EventHandler? ScaleChanged;

    /// <summary>
    /// Occurs on the UI thread after the window has been destroyed.
    /// </summary>
    public event EventHandler? Closed;

    /// <summary>
    /// Occurs on the UI thread when <see cref="TextRendering"/> changed: a setting of Windows changed (<c>WM_SETTINGCHANGE</c>), or
    /// the window moved to a monitor whose text is drawn otherwise.
    /// The renderer and the text of the window take the new value.
    /// </summary>
    public event EventHandler? TextRenderingChanged;

    /// <summary>
    /// Gets how the window's text is drawn (design §5.0.3, "ClearType"): read for the window's monitor when the window is created,
    /// when it moves to another monitor, and when a setting of Windows changes. Every window is opaque until Mica (§13.2), so the coverage is ClearType when
    /// Windows asks for it and the monitor has a row of subpixels. When Windows does not smooth the edges of fonts, the glyphs are
    /// bilevel (<see cref="TextRenderingParameters.IsAliased"/>, design §5.0.3, "When fonts are not smoothed").
    /// </summary>
    public TextRenderingParameters TextRendering { get; private set; }

    /// <summary>
    /// Gets or sets what reads <see cref="TextRendering"/>, for the tests of the conditions without changing a setting of Windows.
    /// </summary>
    internal static Func<HMONITOR, TextRenderingParameters> TextRenderingReader { get; set; } =
        static monitor => Win32TextRendering.Read(monitor, isOpaque: true);

    /// <summary>
    /// Gets the window's scale: its monitor's DPI over 96, not rounded (1.25 at 125 %). The display list multiplies logical pixels
    /// by it (design §6.1.1, "Windows' display scale").
    /// </summary>
    public float ScaleFactor { get; private set; }

    /// <summary>
    /// Gets a value indicating whether the native window still exists.
    /// </summary>
    public bool IsAlive => _hwnd != HWND.NULL;

    /// <summary>
    /// Gets or sets the synchronous render path: called inside the window procedure with the new client size in physical pixels
    /// whenever the size changes, also while the OS runs its own modal loop during a resize drag and the message loop is stopped
    /// (design §2.11.1). It runs inside <see cref="SynchronousRenderGuard"/>. A size change that arrives while a synchronous
    /// render is running is not rendered on the spot; the latest one is rendered once after the outermost render ends.
    /// Application callbacks raised meanwhile also run after it.
    /// </summary>
    public Action<Win32Window, int, int>? SynchronousRender { get; set; }

    /// <summary>
    /// Gets the number of size changes that arrived while a synchronous render was already running and were not rendered on the
    /// spot. The latest of them is rendered after the outermost render.
    /// </summary>
    internal int DroppedNestedResizes { get; private set; }

    /// <summary>
    /// Gets the native window handle, or zero after the window has been destroyed.
    /// </summary>
    internal HWND Hwnd => _hwnd;

    /// <summary>
    /// Gets the native window handle (<c>HWND</c>), for a renderer that draws into the window; zero after it is destroyed.
    /// </summary>
    public nint Handle => _hwnd;

    /// <summary>
    /// Gets the size of the client area, in physical pixels.
    /// </summary>
    /// <exception cref="InvalidOperationException">The calling thread is not the UI thread.</exception>
    public (int Width, int Height) ClientSize
    {
        get
        {
            UiThread.VerifyAccess();
            RECT client;
            _ = GetClientRect(_hwnd, &client);
            return (client.right - client.left, client.bottom - client.top);
        }
    }

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

    private void RenderSynchronously(WPARAM wParam, LPARAM lParam)
    {
        if (SynchronousRender is null || (nuint)wParam == SizeMinimized)
        {
            return;
        }

        // WM_SIZE packs the new client width and height into the low and high words of lParam.
        int width = (int)((nuint)lParam & 0xFFFF);
        int height = (int)(((nuint)lParam >> 16) & 0xFFFF);

        if (SynchronousRenderGuard.IsActive)
        {
            // The render changed the size again, or another window's render did. Running the pipeline inside itself would render
            // twice and re-enter layout. Keep the latest size and render it once when the outermost render ends: inside the OS
            // modal loop no frame comes to do it (design §2.11.1).
            DroppedNestedResizes++;
            _pendingWidth = width;
            _pendingHeight = height;
            if (!_resizePending)
            {
                _resizePending = true;
                SynchronousRenderGuard.RunOrDefer(_renderPendingResize ??= RenderPendingResize);
            }

            return;
        }

        RenderInsideGuard(width, height);
    }

    private void RenderPendingResize()
    {
        _resizePending = false;
        if (IsAlive && SynchronousRender is not null)
        {
            RenderInsideGuard(_pendingWidth, _pendingHeight);
        }
    }

    private void RenderInsideGuard(int width, int height)
    {
        // Not a using block: when the render throws and a deferred callback throws at the end of the scope as well, the window
        // procedure's barrier must see both (a using block would let the second replace the first).
        Exception? renderError = null;
        SynchronousRenderGuard.Scope scope = SynchronousRenderGuard.Enter();
        try
        {
            SynchronousRender?.Invoke(this, width, height);
        }
        catch (Exception exception)
        {
            renderError = exception;
        }

        try
        {
            scope.Dispose();
        }
        catch (Exception deferredError) when (renderError is not null)
        {
            throw new AggregateException("The synchronous render and a deferred callback both threw.", renderError, deferredError);
        }

        if (renderError is not null)
        {
            ExceptionDispatchInfo.Throw(renderError);
        }
    }

    private void RaiseClosed() => Closed?.Invoke(this, EventArgs.Empty);

    // WM_DPICHANGED, in the order of design §6.1.1: the scale, then the text rendering of the monitor the suggested rectangle is
    // on (the window is still on the one before), then the rectangle, whose WM_SIZE renders with both.
    private void ChangeScale(HWND hwnd, WPARAM wParam, RECT* suggested)
    {
        // The new DPI is in the low word of wParam; the high word holds the same for the other axis.
        float scale = ((uint)(nuint)wParam & 0xFFFF) / BaseDpi;
        bool changed = scale != ScaleFactor;
        ScaleFactor = scale;
        _monitor = MonitorFromRect(suggested, MONITOR.MONITOR_DEFAULTTONEAREST);
        ReadTextRendering(_monitor);
        _ = SetWindowPos(
            hwnd,
            HWND.NULL,
            suggested->left,
            suggested->top,
            suggested->right - suggested->left,
            suggested->bottom - suggested->top,
            SWP.SWP_NOZORDER | SWP.SWP_NOACTIVATE
        );
        if (changed)
        {
            SynchronousRenderGuard.RunOrDefer(_raiseScaleChanged ??= () => ScaleChanged?.Invoke(this, EventArgs.Empty));
        }
    }

    // A monitor of the same scale sends no WM_DPICHANGED (design §5.0.3, "ClearType"), so a move reads the text rendering again
    // when the monitor changed. The window procedure still calls DefWindowProc, so that WM_SIZE and WM_MOVE come.
    private void MoveMonitor(HWND hwnd)
    {
        // CreateWindowExW and the constructor's SetWindowPos send WM_WINDOWPOSCHANGED before the constructor reads the monitor and
        // its text rendering inside its own try; until then there is nothing to read again (PR #236 review).
        if (_monitor == HMONITOR.NULL)
        {
            return;
        }

        HMONITOR monitor = MonitorFromWindow(hwnd, MONITOR.MONITOR_DEFAULTTONEAREST);
        if (monitor != _monitor)
        {
            _monitor = monitor;
            ReadTextRendering(monitor);
        }
    }

    // Sizes the window so that its client area is the logical size at the window's scale, with the frame of that DPI.
    private void ResizeClient(HWND hwnd, int width, int height)
    {
        var frame = new RECT { right = (int)MathF.Round(width * ScaleFactor), bottom = (int)MathF.Round(height * ScaleFactor) };
        if (AdjustWindowRectExForDpi(&frame, Style, BOOL.FALSE, 0, GetDpiForWindow(hwnd)) == BOOL.FALSE)
        {
            throw new Win32Exception(Marshal.GetLastSystemError());
        }

        const uint Flags = SWP.SWP_NOMOVE | SWP.SWP_NOZORDER | SWP.SWP_NOACTIVATE;
        if (SetWindowPos(hwnd, HWND.NULL, 0, 0, frame.right - frame.left, frame.bottom - frame.top, Flags) == BOOL.FALSE)
        {
            throw new Win32Exception(Marshal.GetLastSystemError());
        }
    }

    // Every WM_SETTINGCHANGE reads again rather than matching the setting it names: the values come from several settings (font
    // smoothing, and the gamma and contrast the ClearType tuner writes), and the message is rare.
    private void ReadTextRendering(HMONITOR monitor)
    {
        TextRenderingParameters read = TextRenderingReader(monitor);
        if (read != TextRendering)
        {
            TextRendering = read;
            SynchronousRenderGuard.RunOrDefer(_raiseTextRenderingChanged ??= () => TextRenderingChanged?.Invoke(this, EventArgs.Empty));
        }
    }

    private static void EnsureClassRegistered()
    {
        if (_classRegistered)
        {
            return;
        }

        // Before the first window, so that every window of the process is drawn in the screen's pixels at every scale (design
        // §6.1.1, "Windows' display scale"). It fails when the app's manifest declared an awareness already, or when the process
        // has read a DPI; the process then keeps the one it has.
        _ = SetProcessDpiAwarenessContext(DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2);

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
            if (id != 0 && NativeHandleTable.TryResolve(id, out Win32Window? window) && window.HandleMessage(hwnd, message, wParam, lParam))
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

    private bool HandleMessage(HWND hwnd, uint message, WPARAM wParam, LPARAM lParam)
    {
        if (MessageHook is { } hook && hook(this, message))
        {
            return true;
        }

        if (HandlePointerMessage(hwnd, message, wParam, lParam))
        {
            return true;
        }

        switch (message)
        {
            case WmClose:
                _ = DestroyWindow(hwnd);
                return true;
            case WmSize:
                RenderSynchronously(wParam, lParam);
                return false;
            case WmSettingChange:
                ReadTextRendering(_monitor);
                return false;
            case WmDpiChanged:
                ChangeScale(hwnd, wParam, (RECT*)lParam);
                return true;
            case WmWindowPosChanged:
                MoveMonitor(hwnd);
                return false;
            case WmDestroy:
                // An application callback: during a synchronous render it runs after the render returns.
                SynchronousRenderGuard.RunOrDefer(RaiseClosed);
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
