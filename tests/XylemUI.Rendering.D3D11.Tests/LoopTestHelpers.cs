using XylemUI.Platform.Windows;

namespace XylemUI.Rendering.D3D11.Tests;

// Helpers for tests that run the real Win32 message loop: RunLoop and LoopWatchdog, copied from XylemUI.Platform.Windows.Tests
// for the swap chain frame clock (S7). They keep a test from hanging the run (.cursor/rules/testing.mdc).
public static class LoopTestHelpers
{
    // Runs the loop until isDone returns true at an iteration, or the watchdog expires. start runs on the UI thread first.
    public static void RunLoop(LoopWatchdog watchdog, Func<bool> isDone, Action start)
    {
        Win32MessageLoop.IterationHook = () =>
        {
            if (watchdog.Expired || isDone())
            {
                Win32MessageLoop.Quit();
            }
        };

        try
        {
            start();
            _ = Win32MessageLoop.Run();
        }
        finally
        {
            Win32MessageLoop.IterationHook = null;
            Win32MessageLoop.FrameScheduler.CancelFrame();
        }
    }
}

// Wakes the loop and marks the test failed if it is still running after the timeout.
public sealed class LoopWatchdog : IDisposable
{
    private readonly ManualResetEventSlim _finished = new();
    private readonly Thread _thread;
    private volatile bool _expired;

    public LoopWatchdog()
    {
        _thread = new Thread(() =>
        {
            if (!_finished.Wait(TimeSpan.FromSeconds(5)))
            {
                _expired = true;
                Win32MessageLoop.Wake();
            }
        })
        {
            IsBackground = true,
        };
        _thread.Start();
    }

    public bool Expired => _expired;

    public void Dispose()
    {
        _finished.Set();
        _thread.Join();
        _finished.Dispose();
    }
}
