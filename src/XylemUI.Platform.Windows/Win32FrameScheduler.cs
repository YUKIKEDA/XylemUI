using System.ComponentModel;
using System.Runtime.InteropServices;
using TerraFX.Interop.Windows;
using static TerraFX.Interop.Windows.Windows;

namespace XylemUI.Platform.Windows;

/// <summary>
/// The Win32 frame clock that <see cref="Win32MessageLoop"/> waits on together with the message queue (design §2.11).
/// </summary>
/// <remarks>
/// Until a swap chain exists (roadmap S7), the clock is a high-resolution waitable timer with the period of a 60 Hz display. The
/// production handle, DXGI's frame latency waitable object, is waited on the same way. The timer runs only while a frame is
/// requested, so an idle UI thread has nothing to wake it.
/// </remarks>
internal sealed unsafe class Win32FrameScheduler : IFrameScheduler, IDisposable
{
    // 60 Hz. SetWaitableTimer takes the first due time in 100 ns units (negative is relative) and the period in milliseconds.
    private const long FirstDueTime100ns = -166_667;
    private const int PeriodMilliseconds = 16;

    private readonly HANDLE _timer;
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

    /// <summary>Gets the handle the message loop waits on while a frame is pending.</summary>
    public HANDLE WaitHandle => _timer;

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

    private void StartTimer()
    {
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
