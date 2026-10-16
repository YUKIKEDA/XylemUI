using TerraFX.Interop.Windows;
using static TerraFX.Interop.Windows.Windows;

namespace XylemUI.Platform.Windows.Tests;

[Collection(UiThreadGroup.Name)]
public sealed class Win32WindowTests(UiThreadFixture ui)
{
    private const uint ProbeMessage = WM.WM_USER + 1;

    /// <summary>
    /// The window procedure finds the managed window from the id native code holds, after a compacting collection.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A hidden window with a message hook that records the window it was called for.
    ///
    /// Steps:
    /// - Force a blocking, compacting gen-2 collection.
    /// - Send a probe message to the native handle.
    ///
    /// Expected:
    /// - The hook ran once, for the same <see cref="Win32Window"/> instance.
    /// </remarks>
    [Fact]
    public void WindowProcedure_AfterCompactingCollection_ResolvesTheWindow() =>
        ui.Invoke(() =>
        {
            using var window = new Win32Window("probe", 200, 100);
            var seen = new List<Win32Window>();
            window.MessageHook = (w, message) =>
            {
                if (message != ProbeMessage)
                {
                    return false;
                }

                seen.Add(w);
                return true;
            };

            GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
            _ = SendMessageW(window.Hwnd, ProbeMessage, default, default);

            Assert.Same(window, Assert.Single(seen));
        });

    /// <summary>
    /// An exception thrown inside the window procedure is caught at the native boundary and reported, and the window survives.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A hidden window whose message hook throws for a probe message.
    /// - A callback exception handler that records what it receives.
    ///
    /// Steps:
    /// - Send the probe message.
    ///
    /// Expected:
    /// - SendMessageW returns, and the process is still running.
    /// - The handler received the thrown exception.
    /// - The window still exists.
    /// </remarks>
    [Fact]
    public void WindowProcedure_HandlerThrows_ReportsAndKeepsTheWindow() =>
        ui.Invoke(() =>
        {
            Action<Exception> previous = Win32MessageLoop.CallbackExceptionHandler;
            var reported = new List<Exception>();
            Win32MessageLoop.CallbackExceptionHandler = reported.Add;
            try
            {
                using var window = new Win32Window("throws", 200, 100);
                var thrown = new InvalidOperationException("thrown inside the window procedure");
                window.MessageHook = (_, message) => message == ProbeMessage ? throw thrown : false;

                _ = SendMessageW(window.Hwnd, ProbeMessage, default, default);

                Assert.Same(thrown, Assert.Single(reported));
                Assert.True(window.IsAlive);
                Assert.True(IsWindow(window.Hwnd));
            }
            finally
            {
                Win32MessageLoop.CallbackExceptionHandler = previous;
            }
        });

    /// <summary>
    /// Disposing a window destroys it, raises <see cref="Win32Window.Closed"/>, and stops the procedure from resolving it.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A hidden window with a <see cref="Win32Window.Closed"/> handler.
    ///
    /// Steps:
    /// - Dispose the window.
    ///
    /// Expected:
    /// - Closed was raised once.
    /// - <see cref="Win32Window.IsAlive"/> is false and the old native handle is no longer a window.
    /// </remarks>
    [Fact]
    public void Dispose_LiveWindow_DestroysAndRaisesClosed() =>
        ui.Invoke(() =>
        {
            var window = new Win32Window("closes", 200, 100);
            HWND hwnd = window.Hwnd;
            int closed = 0;
            window.Closed += (_, _) => closed++;

            window.Dispose();

            Assert.Equal(1, closed);
            Assert.False(window.IsAlive);
            Assert.False(IsWindow(hwnd));
        });

    /// <summary>
    /// Creating a window off the UI thread throws instead of creating a window that belongs to the wrong thread.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - The fixture's thread is the UI thread; the test runs on another thread.
    ///
    /// Steps:
    /// - Construct a <see cref="Win32Window"/> on the test thread.
    ///
    /// Expected:
    /// - <see cref="InvalidOperationException"/>.
    /// </remarks>
    [Fact]
    public void Constructor_OffTheUiThread_Throws()
    {
        Assert.False(UiThread.CheckAccess());
        Assert.Throws<InvalidOperationException>(() => new Win32Window("wrong thread", 200, 100));
    }
}
