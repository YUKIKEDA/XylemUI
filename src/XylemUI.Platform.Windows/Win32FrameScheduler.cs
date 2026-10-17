using System.ComponentModel;
using System.Runtime.InteropServices;
using TerraFX.Interop.Windows;
using static TerraFX.Interop.Windows.Windows;

namespace XylemUI.Platform.Windows;

/// <summary>
/// The Win32 frame clock that <see cref="Win32MessageLoop"/> waits on together with the message queue (design §2.11).
/// </summary>
/// <remarks>
/// <para>
/// With a swap chain, the clock is its frame latency waitable object (<see cref="SetFrameClock"/>, S7): it is signaled when the
/// swap chain can take the next frame, so frames follow the display. Before a swap chain exists, it is a high-resolution
/// waitable timer of about 60 Hz (see the period below), which runs only while a frame is requested. Either way the loop waits
/// on the clock only while a frame is pending, so an idle UI thread has nothing to wake it.
/// </para>
/// <para>
/// The swap chain's object counts like a semaphore: a wait takes one, a present gives it back. A frame callback that returns
/// without presenting (nothing changed, the window is minimized, an error) leaves it at zero, so the wait has a timeout of
/// <see cref="SwapChainWaitTimeoutMilliseconds"/>, after which the frame runs anyway. The next frame is late, never lost.
/// </para>
/// </remarks>
internal sealed unsafe class Win32FrameScheduler : IFrameScheduler, IDisposable
{
    // About 60 Hz, as a stand-in until S7. SetWaitableTimer takes the first due time in 100 ns units (negative is relative) and the
    // period in whole milliseconds, so the first frame comes after 16.667 ms and later ones every 16 ms (62.5 Hz).
    private const long FirstDueTime100ns = -166_667;
    private const int PeriodMilliseconds = 16;

    /// <summary>
    /// The longest the loop waits on a swap chain's object for a pending frame: three frames at 60 Hz.
    /// </summary>
    internal const uint SwapChainWaitTimeoutMilliseconds = 50;

    private readonly HANDLE _timer;
    private HANDLE _swapChainClock;
    private Action? _pending;
    private bool _timerRunning;

    public Win32FrameScheduler()
    {
        _timer = CreateWaitableTimerExW(null, null, (uint)CREATE.CREATE_WAITABLE_TIMER_HIGH_RESOLUTION, (uint)TIMER_ALL_ACCESS);
        if (_timer == HANDLE.NULL)
        {
            throw new Win32Exception(Marshal.GetLastSystemError());
        }
    }

    /// <summary>Gets a value indicating whether a callback is waiting for the next frame.</summary>
    public bool HasPendingFrame => _pending is not null;

    /// <summary>Gets a value indicating whether the frame clock is running. It runs only while a frame is pending.</summary>
    internal bool IsClockRunning => _timerRunning;

    /// <summary>Gets the handle the message loop waits on while a frame is pending.</summary>
    public HANDLE WaitHandle => _swapChainClock != HANDLE.NULL ? _swapChainClock : _timer;

    /// <summary>Gets how long the loop waits on <see cref="WaitHandle"/> before it runs the pending frame anyway.</summary>
    public uint WaitTimeoutMilliseconds => _swapChainClock != HANDLE.NULL ? SwapChainWaitTimeoutMilliseconds : INFINITE;

    /// <summary>
    /// Uses a swap chain's frame latency waitable object as the clock, or the timer again when it is zero. A pending frame
    /// carries over to the new clock.
    /// </summary>
    public void SetFrameClock(HANDLE waitableObject)
    {
        UiThread.VerifyAccess();
        StopTimer();
        _swapChainClock = waitableObject;
        if (_pending is not null)
        {
            StartTimer();
        }
    }

    /// <inheritdoc />
    public void RequestFrame(Action callback)
    {
        ArgumentNullException.ThrowIfNull(callback);
        UiThread.VerifyAccess();
        _pending = callback;
        if (!_timerRunning)
        {
            StartTimer();
        }
    }

    /// <inheritdoc />
    public void CancelFrame()
    {
        UiThread.VerifyAccess();
        _pending = null;
        StopTimer();
    }

    public void Dispose()
    {
        StopTimer();
        _ = CloseHandle(_timer);
    }

    /// <summary>
    /// Runs the pending callback. Called by the message loop when <see cref="WaitHandle"/> is signaled.
    /// </summary>
    internal void RunFrame()
    {
        Action? callback = _pending;
        _pending = null;
        if (callback is not null)
        {
            try
            {
                callback();
            }
            catch (Exception exception)
            {
                Win32MessageLoop.ReportCallbackException(exception);
            }
        }

        // The callback may have asked for the next frame; keep the timer running only then.
        if (_pending is null)
        {
            StopTimer();
        }
    }

    // The swap chain's object signals by itself; only the timer has to be armed.
    private void StartTimer()
    {
        if (_swapChainClock != HANDLE.NULL)
        {
            return;
        }

        LARGE_INTEGER dueTime = default;
        dueTime.QuadPart = FirstDueTime100ns;
        if (!SetWaitableTimer(_timer, &dueTime, PeriodMilliseconds, null, null, BOOL.FALSE))
        {
            throw new Win32Exception(Marshal.GetLastSystemError());
        }

        _timerRunning = true;
    }

    private void StopTimer()
    {
        if (_timerRunning)
        {
            _ = CancelWaitableTimer(_timer);
            _timerRunning = false;
        }
    }
}
