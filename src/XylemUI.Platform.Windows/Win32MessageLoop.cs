using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using TerraFX.Interop.Windows;
using static TerraFX.Interop.Windows.Windows;

namespace XylemUI.Platform.Windows;

/// <summary>
/// Runs the Win32 message loop of the UI thread (design §2.11).
/// </summary>
/// <remarks>
/// While a frame is requested, the loop waits on the message queue and the frame clock together with
/// <c>MsgWaitForMultipleObjectsEx</c>, and wakes for whichever comes first. With no frame requested it waits on the message queue
/// alone and uses no CPU. <see cref="Wake"/> wakes it from any thread.
/// </remarks>
public static unsafe class Win32MessageLoop
{
    private const string WakeClassName = "XylemUI.Wake";
    private const uint WmQuit = WM.WM_QUIT;
    private const uint WmNull = WM.WM_NULL;

    private static Action<Exception> _callbackExceptionHandler = DefaultCallbackExceptionHandler;
    private static Win32FrameScheduler? _frameScheduler;
    private static UiDispatcher? _dispatcher;
    private static nint _wakeWindow;

    /// <summary>
    /// Gets or sets the handler that receives an exception thrown inside a window procedure or a frame callback. The exception is
    /// caught at the native boundary (design §2.11.1). The default writes to <see cref="Trace"/>.
    /// </summary>
    public static Action<Exception> CallbackExceptionHandler
    {
        get => _callbackExceptionHandler;
        set => _callbackExceptionHandler = value ?? throw new ArgumentNullException(nameof(value));
    }

    /// <summary>
    /// Gets the frame scheduler of the UI thread. Its callbacks run inside <see cref="Run"/>.
    /// </summary>
    /// <exception cref="InvalidOperationException">The calling thread is not the UI thread.</exception>
    public static IFrameScheduler FrameScheduler => GetFrameScheduler();

    /// <summary>
    /// Gets the UI thread's queue. Other threads post to it; <see cref="Run"/> drains it when it starts and once per iteration, and
    /// installs its synchronization context so that <c>await</c> continuations resume on the UI thread (design §2.12).
    /// </summary>
    /// <exception cref="InvalidOperationException">The calling thread is not the UI thread.</exception>
    public static UiDispatcher Dispatcher => GetDispatcher();

    /// <summary>
    /// Gets or sets a hook that runs on the UI thread once per loop iteration, after the queued messages are handled and before the
    /// loop waits. Used by tests to observe how often the loop wakes.
    /// </summary>
    internal static Action? IterationHook { get; set; }

    /// <summary>
    /// Dispatches messages and frame callbacks until <see cref="Quit"/> is called.
    /// </summary>
    /// <returns>The exit code passed to <see cref="Quit"/>.</returns>
    /// <exception cref="InvalidOperationException">The calling thread is not the UI thread.</exception>
    /// <exception cref="Win32Exception">The system could not wait for messages.</exception>
    public static int Run()
    {
        UiThread.VerifyAccess();
        EnsureWakeWindow();
        Win32FrameScheduler frames = GetFrameScheduler();
        UiDispatcher dispatcher = GetDispatcher();
        SynchronizationContext? previousContext = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(dispatcher.SynchronizationContext);
        try
        {
            return RunLoop(frames, dispatcher);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previousContext);
        }
    }

    private static int RunLoop(Win32FrameScheduler frames, UiDispatcher dispatcher)
    {
        MSG message;
        while (true)
        {
            while (PeekMessageW(&message, HWND.NULL, 0, 0, (uint)PM.PM_REMOVE))
            {
                if (message.message == WmQuit)
                {
                    return (int)message.wParam;
                }

                _ = TranslateMessage(&message);
                _ = DispatchMessageW(&message);
            }

            // Runs on the first iteration too, before the first wait: work posted before the loop started (when Wake still did
            // nothing) is not lost.
            _ = dispatcher.Drain(ReportCallbackException);

            IterationHook?.Invoke();

            HANDLE frameClock = frames.WaitHandle;
            uint handleCount = frames.HasPendingFrame ? 1u : 0u;
            uint result = MsgWaitForMultipleObjectsEx(
                handleCount,
                handleCount == 0 ? null : &frameClock,
                INFINITE,
                (uint)QS.QS_ALLINPUT,
                (uint)MWMO.MWMO_INPUTAVAILABLE
            );

            if (result == WAIT.WAIT_FAILED)
            {
                throw new Win32Exception(Marshal.GetLastSystemError());
            }

            if (handleCount == 1 && result == WAIT.WAIT_OBJECT_0)
            {
                frames.RunFrame();
            }

            // WAIT_OBJECT_0 + handleCount means input is available; the next iteration handles it.
        }
    }

    /// <summary>
    /// Makes <see cref="Run"/> return once the messages already in the queue are handled.
    /// </summary>
    /// <param name="exitCode">The value <see cref="Run"/> returns.</param>
    /// <exception cref="InvalidOperationException">The calling thread is not the UI thread.</exception>
    public static void Quit(int exitCode = 0)
    {
        UiThread.VerifyAccess();
        PostQuitMessage(exitCode);
    }

    /// <summary>
    /// Wakes the UI thread if it is waiting. Safe to call from any thread. Does nothing before the loop has started.
    /// </summary>
    public static void Wake()
    {
        nint window = Volatile.Read(ref _wakeWindow);
        if (window != 0)
        {
            _ = PostMessageW((HWND)window, WmNull, default, default);
        }
    }

    internal static void ReportCallbackException(Exception exception)
    {
        try
        {
            _callbackExceptionHandler(exception);
        }
        catch
        {
            // A handler that throws must not take the process down from inside a callback.
        }
    }

    private static UiDispatcher GetDispatcher()
    {
        UiThread.VerifyAccess();
        return _dispatcher ??= new UiDispatcher(Wake);
    }

    private static Win32FrameScheduler GetFrameScheduler()
    {
        UiThread.VerifyAccess();
        return _frameScheduler ??= new Win32FrameScheduler();
    }

    // A message-only window receives the wake messages. Unlike PostThreadMessage, a message posted to a window is not discarded
    // while a modal loop (a resize drag, a message box) runs.
    private static void EnsureWakeWindow()
    {
        if (_wakeWindow != 0)
        {
            return;
        }

        HINSTANCE instance = GetModuleHandleW(null);
        fixed (char* className = WakeClassName)
        {
            var windowClass = new WNDCLASSEXW
            {
                cbSize = (uint)sizeof(WNDCLASSEXW),
                lpfnWndProc = &WakeWindowProcedure,
                hInstance = instance,
                lpszClassName = className,
            };

            if (RegisterClassExW(&windowClass) == 0)
            {
                throw new Win32Exception(Marshal.GetLastSystemError());
            }

            HWND window = CreateWindowExW(0, className, null, 0, 0, 0, 0, 0, HWND.HWND_MESSAGE, HMENU.NULL, instance, null);
            if (window == HWND.NULL)
            {
                throw new Win32Exception(Marshal.GetLastSystemError());
            }

            Volatile.Write(ref _wakeWindow, (nint)window);
        }
    }

    [UnmanagedCallersOnly]
    private static LRESULT WakeWindowProcedure(HWND hwnd, uint message, WPARAM wParam, LPARAM lParam) =>
        DefWindowProcW(hwnd, message, wParam, lParam);

    private static void DefaultCallbackExceptionHandler(Exception exception) =>
        Trace.TraceError("Unhandled exception in a window procedure or frame callback: {0}", exception);
}
