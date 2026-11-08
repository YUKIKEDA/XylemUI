using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using TerraFX.Interop.Windows;
using static TerraFX.Interop.Windows.Windows;

namespace XylemUI.Platform.Windows;

/// <summary>
/// The Win32 frame clock that <see cref="Win32MessageLoop"/> waits on together with the message queue (design §2.11).
/// </summary>
/// <remarks>
/// <para>
/// With a swap chain, the clock is its frame latency waitable object (<see cref="SetFrameClock(HANDLE)"/>, S7): it is signaled when the
/// swap chain can take the next frame, so frames follow the display. Before a swap chain exists, it is a high-resolution
/// waitable timer of about 60 Hz (see the period below), which runs only while a frame is requested. Either way the loop waits
/// on the clock only while a frame is pending, so an idle UI thread has nothing to wake it.
/// </para>
/// <para>
/// The swap chain's object counts like a semaphore: a wait takes one, a present gives it back. A frame callback that returns
/// without presenting (nothing changed, the window is minimized, an error) leaves it at zero, and it stays so whoever asks for
/// the next frame. The scheduler keeps whether the last callback presented (<see cref="FramePresented"/>): after one that did
/// not, the next frame waits on the timer instead, however and whenever it is asked for, and a callback that presents goes back
/// to the object (design §2.11, "Frames that do not present"). A touchpad contact that rests asks for frames that draw nothing,
/// and DirectManipulation needs one each frame (design §7, I1c). The wait on the object keeps a timeout of
/// <see cref="SwapChainWaitTimeoutMilliseconds"/>, so that an object that stays unsignaled after a present stops no frame.
/// </para>
/// <para>
/// A present that did not wait on the object (the synchronous resize path, a frame run on the timeout) gives back one signal more
/// than was taken, and every frame after it would then present one ahead and show a refresh later. The scheduler counts the
/// presents less the signals taken; when the object signals while that count is above zero, the frame is not run, and the loop
/// waits again: the frame the extra present queued is the one the display shows at that refresh (design §2.11, "Presents that
/// did not wait").
/// </para>
/// <para>
/// The object keeps a bounded number of signals (40 on the measuring machine after more presents that did not wait, PR #680's
/// review), so a long resize drag can count more than it holds. A wait that times out right after a signal was taken without a
/// frame shows that: the count goes back to zero, and the present of the frame run on that timeout is not counted, since it is
/// the only one in flight and its signal is a frame's own. Without this, each such present's own signal was taken without a
/// frame, the next wait timed out again, and the frames stayed at about 16 a second.
/// </para>
/// <para>
/// With a reader of the last displayed refresh (<see cref="SetFrameClock(HANDLE, LastRefreshReader?, double)"/>), a signal of
/// the object does not run the frame at once. The object signals right after the last frame was shown; a frame drawn then waits
/// about a refresh for the display, and a key that comes in that refresh waits for the frame after. The scheduler finds the next
/// refresh from the last displayed one and the period, waits on a one-shot high-resolution timer until
/// <see cref="LateStartMarginMilliseconds"/> before it, handling messages, and runs the frame then (design §2.11, "Frames that
/// start late"). The period is derived from two refreshes at least <see cref="PeriodRefreshes"/> apart. The counts can go back
/// (the series changed when the window moved to MPO on the measuring machine, spike #687): the scheduler anchors again then, and
/// when the period is out of range. A frame asked for while no frame was pending, outside a callback (a key on a static screen,
/// which DWM composes: starting it late made input to display longer, Issue #694), and a frame after a long callback, after a
/// timeout or the timer, with unreadable statistics, or with a last displayed refresh more than
/// <see cref="StaleMilliseconds"/> old run at once.
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

    /// <summary>
    /// How long before the next refresh a frame that starts late runs: the drawing of the measured scene (CPU 0.2 ms, GPU 0.1 ms)
    /// and the share of DWM and the driver. 2 ms made the p95 of input to display 43.1 ms (spike #687).
    /// </summary>
    internal const double LateStartMarginMilliseconds = 4;

    /// <summary>A callback longer than this (half the margin, <c>Present</c> included) makes the next frame run at once.</summary>
    internal const double LongCallbackMilliseconds = 2;

    /// <summary>A last displayed refresh older than this makes the frame run at once: the period's error grows with the gap.</summary>
    internal const double StaleMilliseconds = 1000;

    /// <summary>A computed wait this short or shorter is not waited.</summary>
    internal const double ShortestWaitMilliseconds = 0.5;

    /// <summary>How late a late start's wake may be before <see cref="LateWakes"/> counts it.</summary>
    internal const double LateWakeAllowanceMilliseconds = 1;

    /// <summary>How many refreshes apart the two refreshes that give the period are.</summary>
    internal const uint PeriodRefreshes = 30;

    private const double ShortestPeriodMilliseconds = 4;
    private const double LongestPeriodMilliseconds = 50;

    private readonly HANDLE _timer;

    // The one-shot timer that a frame which starts late waits on.
    private readonly HANDLE _lateTimer;
    private HANDLE _swapChainClock;
    private Action? _pending;
    private bool _timerRunning;

    // Whether the callback running has presented, and whether the last one that ran did not: the swap chain's object is then
    // unsignaled until a present, so the clock is the timer.
    private bool _presented;
    private bool _lastDidNotPresent;

    // The presents reported on the swap chain's clock less the signals taken from its object: above zero after a present that
    // did not wait, below zero after a frame that took a signal and did not present.
    private int _owed;

    // Whether the last signal was taken without a frame, and whether the frame running was run on a timeout that came right
    // after one (its present is then not counted).
    private bool _lastSignalTaken;
    private bool _presentNotOwed;

    // The late start: the reader of the last displayed refresh, and whether the loop waits on the late start's timer now (the
    // signal for the pending frame is taken).
    private LastRefreshReader? _lastRefresh;
    private bool _lateWaiting;
    private long _lateStartAt;

    // The period in counter ticks (zero before it is derived; the monitor's rate is used then) and the refresh it is derived from.
    private double _nominalPeriod = Stopwatch.Frequency / 60.0;
    private double _period;
    private long _anchorTime;
    private uint _anchorCount;
    private bool _anchored;

    // The length of the last callback, in counter ticks.
    private long _lastCallbackTicks;

    // Whether a callback runs now, and whether the pending frame was asked for while none was pending and outside a callback (a
    // key on a static screen): such a frame runs at once (design §2.11, "Frames that start late", Issue #694).
    private bool _inCallback;
    private bool _requestedFromIdle;

    /// <summary>
    /// Initializes a new instance of the <see cref="Win32FrameScheduler"/> class with its high-resolution timer as the clock.
    /// </summary>
    /// <exception cref="Win32Exception">The timer could not be created.</exception>
    public Win32FrameScheduler()
    {
        _timer = CreateWaitableTimerExW(null, null, (uint)CREATE.CREATE_WAITABLE_TIMER_HIGH_RESOLUTION, (uint)TIMER_ALL_ACCESS);
        if (_timer == HANDLE.NULL)
        {
            throw new Win32Exception(Marshal.GetLastSystemError());
        }

        _lateTimer = CreateWaitableTimerExW(null, null, (uint)CREATE.CREATE_WAITABLE_TIMER_HIGH_RESOLUTION, (uint)TIMER_ALL_ACCESS);
        if (_lateTimer == HANDLE.NULL)
        {
            int error = Marshal.GetLastSystemError();
            _ = CloseHandle(_timer);
            throw new Win32Exception(error);
        }
    }

    /// <summary>Gets a value indicating whether a callback is waiting for the next frame.</summary>
    public bool HasPendingFrame => _pending is not null;

    /// <summary>Gets a value indicating whether the frame clock is running. It runs only while a frame is pending.</summary>
    internal bool IsClockRunning => _timerRunning;

    /// <summary>Gets the handle the message loop waits on while a frame is pending.</summary>
    public HANDLE WaitHandle =>
        _lateWaiting ? _lateTimer
        : UsesTimer ? _timer
        : _swapChainClock;

    /// <summary>Gets how long the loop waits on <see cref="WaitHandle"/> before it runs the pending frame anyway.</summary>
    public uint WaitTimeoutMilliseconds => _lateWaiting || UsesTimer ? INFINITE : SwapChainWaitTimeoutMilliseconds;

    /// <summary>Gets the number of frames run because the wait on the swap chain's object timed out, for the benchmarks.</summary>
    internal long ClockTimeouts { get; private set; }

    /// <summary>
    /// Gets the number of signals of the swap chain's object taken without running a frame, one for each present that did not
    /// wait (see the remarks), for the benchmarks.
    /// </summary>
    internal long SignalsOwed { get; private set; }

    /// <summary>Gets a value indicating whether the loop waits on the late start's timer now, for the benchmarks.</summary>
    internal bool IsLateWaiting => _lateWaiting;

    /// <summary>Gets the number of frames that waited for their late start, for the benchmarks.</summary>
    internal long LateStarts { get; private set; }

    /// <summary>
    /// Gets the number of late starts whose timer woke the loop more than <see cref="LateWakeAllowanceMilliseconds"/> after the
    /// start it was set for, and the latest such wake in milliseconds, for the benchmarks.
    /// </summary>
    internal (long Count, double LatestMilliseconds) LateWakes { get; private set; }

    /// <summary>Gets or sets a value indicating whether frames start late; the benchmarks turn it off to compare.</summary>
    internal static bool LateStartEnabled { get; set; } = true;

    /// <summary>Gets the refresh period derived from the statistics, in milliseconds; zero before it is derived.</summary>
    internal double DerivedPeriodMilliseconds => _period * 1000.0 / Stopwatch.Frequency;

    /// <summary>Gets or sets the clock of the late start, in <see cref="Stopwatch"/> ticks; the tests set a fake one.</summary>
    internal Func<long> Now { get; set; } = Stopwatch.GetTimestamp;

    // The timer is the clock without a swap chain, and after a callback that did not present.
    private bool UsesTimer => _swapChainClock == HANDLE.NULL || _lastDidNotPresent;

    /// <summary>
    /// Uses a swap chain's frame latency waitable object as the clock, or the timer again when it is zero. A pending frame
    /// carries over to the new clock.
    /// </summary>
    /// <param name="waitableObject">The swap chain's frame latency waitable object, or zero for the timer.</param>
    public void SetFrameClock(HANDLE waitableObject) => SetFrameClock(waitableObject, null, 60);

    /// <summary>
    /// Uses a swap chain's frame latency waitable object as the clock, with a reader of its last displayed refresh so that frames
    /// start late (see the remarks), or the timer again when the object is zero. A pending frame carries over to the new clock.
    /// </summary>
    /// <param name="waitableObject">The swap chain's frame latency waitable object, or zero for the timer.</param>
    /// <param name="lastRefresh">
    /// The reader of the swap chain's last displayed refresh, or <see langword="null"/> to run each frame when the object signals.
    /// </param>
    /// <param name="refreshRate">The refresh rate of the window's monitor in Hz, used until the period is derived.</param>
    public void SetFrameClock(HANDLE waitableObject, LastRefreshReader? lastRefresh, double refreshRate)
    {
        UiThread.VerifyAccess();
        StopTimer();
        StopLateStart();
        _swapChainClock = waitableObject;
        _lastRefresh = waitableObject == HANDLE.NULL ? null : lastRefresh;
        _nominalPeriod = Stopwatch.Frequency / (refreshRate > 0 ? refreshRate : 60);
        _period = 0;
        _anchored = false;
        _lastCallbackTicks = 0;

        // A new swap chain's object starts signaled: no present is owed to it.
        _lastDidNotPresent = false;
        _owed = 0;
        _lastSignalTaken = false;
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
        if (_pending is null)
        {
            _requestedFromIdle = !_inCallback;
        }

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
        StopLateStart();
    }

    /// <inheritdoc />
    /// <remarks>
    /// A present outside a callback (the synchronous resize path, design §2.11.1) gives the swap chain's object back too, so the
    /// clock goes back to it at once.
    /// </remarks>
    public void FramePresented()
    {
        // A present outside a callback while the late start waits (the synchronous resize path, which a WM_SIZE runs inside the
        // OS's modal loop) uses the signal that wait took: the wait ends, and the pending frame waits on the object again
        // (PR #695's review).
        if (!_inCallback && _lateWaiting)
        {
            StopLateStart();
        }

        _presented = true;
        _lastDidNotPresent = false;
        if (_swapChainClock != HANDLE.NULL && !_presentNotOwed)
        {
            _owed++;
        }
    }

    /// <summary>
    /// Stops the clock and closes the timer.
    /// </summary>
    public void Dispose()
    {
        StopTimer();
        StopLateStart();
        _ = CloseHandle(_timer);
        _ = CloseHandle(_lateTimer);
    }

    /// <summary>
    /// Takes the result of the loop's wait on <see cref="WaitHandle"/> and runs the pending callback, unless the swap chain's
    /// object signaled for a present that did not wait: that signal is taken and the frame stays pending.
    /// </summary>
    /// <param name="waitResult"><c>WAIT_OBJECT_0</c> when the handle was signaled, <c>WAIT_TIMEOUT</c> when the wait timed out.</param>
    internal void RunFrame(uint waitResult)
    {
        // The late start's timer came: the signal for this frame was taken when the wait began.
        if (_lateWaiting)
        {
            _lateWaiting = false;
            LateStarts++;
            double late = (Now() - _lateStartAt) * 1000.0 / Stopwatch.Frequency;
            if (late > LateWakeAllowanceMilliseconds)
            {
                LateWakes = (LateWakes.Count + 1, Math.Max(LateWakes.LatestMilliseconds, late));
            }

            RunFrame();
            return;
        }

        bool signaled = false;
        if (!UsesTimer)
        {
            if (waitResult == WAIT.WAIT_OBJECT_0)
            {
                _owed--;
                if (_owed >= 0)
                {
                    SignalsOwed++;
                    _lastSignalTaken = true;
                    return;
                }

                signaled = true;
            }
            else
            {
                ClockTimeouts++;

                // The object held fewer signals than were counted (see the class remarks).
                if (_lastSignalTaken)
                {
                    _owed = 0;
                    _presentNotOwed = true;
                }
            }
        }

        _lastSignalTaken = false;
        if (signaled && StartLate())
        {
            return;
        }

        RunFrame();
        _presentNotOwed = false;
    }

    /// <summary>
    /// Runs the pending callback, with no wait taken for it (a window's timer while an OS loop runs, design §2.11.1).
    /// </summary>
    internal void RunFrame()
    {
        // A frame run while the late start waits (a window's timer in an OS loop, RunPendingFrame) uses the signal that wait took.
        StopLateStart();
        Action? callback = _pending;
        _pending = null;
        if (callback is not null)
        {
            _presented = false;
            _requestedFromIdle = false;
            _inCallback = true;
            long start = Now();
            try
            {
                callback();
            }
            catch (Exception exception)
            {
                Win32MessageLoop.ReportCallbackException(exception);
            }
            finally
            {
                _inCallback = false;
            }

            _lastCallbackTicks = Now() - start;

            _lastDidNotPresent = !_presented;
        }

        // The callback may have asked for the next frame; keep the timer running only then, and only while it is the clock. A
        // request made later starts it (RequestFrame).
        if (_pending is null || !UsesTimer)
        {
            StopTimer();
        }
        else if (!_timerRunning)
        {
            StartTimer();
        }
    }

    /// <summary>
    /// Returns how long to wait from <paramref name="now"/> until <paramref name="margin"/> before the first refresh after it, the
    /// refreshes coming every <paramref name="period"/> from <paramref name="refreshTime"/>. All values are in counter ticks.
    /// </summary>
    /// <param name="now">The counter now.</param>
    /// <param name="refreshTime">The counter at a displayed refresh.</param>
    /// <param name="period">The refresh period.</param>
    /// <param name="margin">The margin.</param>
    /// <returns>The wait; zero or less when that start is already past.</returns>
    internal static double WaitUntilLateStart(long now, long refreshTime, double period, double margin)
    {
        double next = refreshTime + ((Math.Floor((now - refreshTime) / period) + 1) * period);
        return next - margin - now;
    }

    // Arms the late start's timer for the frame whose signal was just taken, and returns whether the loop waits on it now.
    private bool StartLate()
    {
        if (!LateStartEnabled || _lastRefresh is null || _pending is null || _requestedFromIdle)
        {
            return false;
        }

        double frequency = Stopwatch.Frequency;
        if (_lastCallbackTicks > LongCallbackMilliseconds * frequency / 1000)
        {
            return false;
        }

        if (!_lastRefresh(out long refreshTime, out uint refreshCount) || refreshTime == 0)
        {
            return false;
        }

        UpdatePeriod(refreshTime, refreshCount);
        long now = Now();
        if (now - refreshTime > StaleMilliseconds * frequency / 1000)
        {
            return false;
        }

        double period = _period > 0 ? _period : _nominalPeriod;
        double wait = WaitUntilLateStart(now, refreshTime, period, LateStartMarginMilliseconds * frequency / 1000);
        double waitMilliseconds = wait * 1000 / frequency;
        if (waitMilliseconds <= ShortestWaitMilliseconds)
        {
            return false;
        }

        LARGE_INTEGER dueTime = default;
        dueTime.QuadPart = -(long)(waitMilliseconds * 10_000);
        if (!SetWaitableTimer(_lateTimer, &dueTime, 0, null, null, BOOL.FALSE))
        {
            return false;
        }

        _lateWaiting = true;
        _lateStartAt = now + (long)wait;
        return true;
    }

    // Derives the period from the anchor and this refresh, or anchors again when the count went back or the period is out of
    // range (see the remarks).
    private void UpdatePeriod(long refreshTime, uint refreshCount)
    {
        if (!_anchored || refreshCount < _anchorCount)
        {
            (_anchorTime, _anchorCount, _anchored) = (refreshTime, refreshCount, true);
            return;
        }

        uint refreshes = refreshCount - _anchorCount;
        if (refreshes < PeriodRefreshes)
        {
            return;
        }

        double period = (double)(refreshTime - _anchorTime) / refreshes;
        double periodMilliseconds = period * 1000 / Stopwatch.Frequency;
        if (periodMilliseconds is > ShortestPeriodMilliseconds and < LongestPeriodMilliseconds)
        {
            _period = period;
        }
        else
        {
            (_anchorTime, _anchorCount) = (refreshTime, refreshCount);
        }
    }

    private void StopLateStart()
    {
        if (_lateWaiting)
        {
            _ = CancelWaitableTimer(_lateTimer);
            _lateWaiting = false;
        }
    }

    // The swap chain's object signals by itself; only the timer has to be armed.
    private void StartTimer()
    {
        if (!UsesTimer)
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
