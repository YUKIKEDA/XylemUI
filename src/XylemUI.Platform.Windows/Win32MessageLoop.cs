using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using TerraFX.Interop.Windows;
using static TerraFX.Interop.Windows.Windows;

namespace XylemUI.Platform.Windows;

/// <summary>
/// Runs the Win32 message loop of the UI thread.
/// </summary>
public static unsafe class Win32MessageLoop
{
    private static Action<Exception> _callbackExceptionHandler = DefaultCallbackExceptionHandler;

    /// <summary>
    /// Gets or sets the handler that receives an exception thrown inside a window procedure. The exception is caught at the native
    /// boundary and the message falls back to the default window procedure (design §2.11.1). The default writes to <see cref="Trace"/>.
    /// </summary>
    public static Action<Exception> CallbackExceptionHandler
    {
        get => _callbackExceptionHandler;
        set => _callbackExceptionHandler = value ?? throw new ArgumentNullException(nameof(value));
    }

    /// <summary>
    /// Dispatches messages until <see cref="Quit"/> is called.
    /// </summary>
    /// <returns>The exit code passed to <see cref="Quit"/>.</returns>
    /// <exception cref="InvalidOperationException">The calling thread is not the UI thread.</exception>
    /// <exception cref="Win32Exception">The system could not retrieve a message.</exception>
    public static int Run()
    {
        UiThread.VerifyAccess();
        MSG message;
        while (true)
        {
            int result = GetMessageW(&message, HWND.NULL, 0, 0);
            if (result == 0)
            {
                return (int)message.wParam;
            }

            if (result == -1)
            {
                throw new Win32Exception(Marshal.GetLastSystemError());
            }

            _ = TranslateMessage(&message);
            _ = DispatchMessageW(&message);
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

    internal static void ReportCallbackException(Exception exception)
    {
        try
        {
            _callbackExceptionHandler(exception);
        }
        catch
        {
            // A handler that throws must not take the process down from inside a window procedure.
        }
    }

    private static void DefaultCallbackExceptionHandler(Exception exception) =>
        Trace.TraceError("Unhandled exception in a window procedure: {0}", exception);
}
