using TerraFX.Interop.Windows;
using static TerraFX.Interop.Windows.Windows;

namespace XylemUI.Platform.Windows.Tests;

// Which handle the scheduler waits on after a frame that did not present (design §2.11, "Frames that do not present", I1c). An
// event stands in for the swap chain's frame latency waitable object, and the test runs the frames itself, so nothing waits.
[Collection(UiThreadGroup.Name)]
public sealed unsafe class Win32FrameSchedulerTests(UiThreadFixture ui)
{
    /// <summary>
    /// After a callback that did not present, the next frame waits on the timer, also when it is asked for after the callback
    /// returned; a callback that presents goes back to the swap chain's object.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A scheduler whose frame clock is an event in place of a swap chain's object.
    ///
    /// Steps:
    /// - Run a frame that presents nothing and asks for no frame. Then ask for a frame from outside any callback.
    /// - Run that frame: it presents and asks for the next one.
    /// - Run that one: it asks for the next one and presents nothing.
    ///
    /// Expected:
    /// - Before any frame, a request waits on the object with the 50 ms timeout, and the timer is stopped.
    /// - After the frame that did not present, the timer is stopped while nothing is pending; the later request starts it, and
    ///   the loop waits on it with no timeout.
    /// - After the frame that presented, the request it made waits on the object, and the timer is stopped.
    /// - After the last frame, the request it made waits on the timer, which runs.
    /// </remarks>
    [Fact]
    public void FrameWithoutPresent_NextFrameWaitsOnTheTimer_WhoeverAsks() =>
        ui.Invoke(() =>
        {
            HANDLE clock = CreateEventW(null, BOOL.TRUE, BOOL.TRUE, null);
            Assert.NotEqual(HANDLE.NULL, clock);
            using var scheduler = new Win32FrameScheduler();
            try
            {
                scheduler.SetFrameClock(clock);
                Action nothing = () => { };
                Action presentsAndAsks = null!;
                Action asksAndPresentsNothing = () => scheduler.RequestFrame(nothing);
                presentsAndAsks = () =>
                {
                    scheduler.FramePresented();
                    scheduler.RequestFrame(asksAndPresentsNothing);
                };

                scheduler.RequestFrame(nothing);
                Assert.Equal((clock, Win32FrameScheduler.SwapChainWaitTimeoutMilliseconds, false), State(scheduler));
                scheduler.RunFrame();
                Assert.False(scheduler.IsClockRunning);

                scheduler.RequestFrame(presentsAndAsks);
                Assert.NotEqual(clock, scheduler.WaitHandle);
                Assert.Equal((INFINITE, true), (scheduler.WaitTimeoutMilliseconds, scheduler.IsClockRunning));

                scheduler.RunFrame();
                Assert.Equal((clock, Win32FrameScheduler.SwapChainWaitTimeoutMilliseconds, false), State(scheduler));

                scheduler.RunFrame();
                Assert.NotEqual(clock, scheduler.WaitHandle);
                Assert.Equal((INFINITE, true), (scheduler.WaitTimeoutMilliseconds, scheduler.IsClockRunning));
            }
            finally
            {
                scheduler.CancelFrame();
                _ = CloseHandle(clock);
            }
        });

    /// <summary>
    /// A present outside any callback (the synchronous resize path) gives the clock back to the swap chain's object at once.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A scheduler whose frame clock is an event in place of a swap chain's object, after a frame that did not present.
    ///
    /// Steps:
    /// - Report a present from outside any callback, then ask for a frame.
    ///
    /// Expected:
    /// - The request waits on the object, and the timer is stopped.
    /// </remarks>
    [Fact]
    public void PresentOutsideACallback_ReturnsToTheSwapChainClock() =>
        ui.Invoke(() =>
        {
            HANDLE clock = CreateEventW(null, BOOL.TRUE, BOOL.TRUE, null);
            Assert.NotEqual(HANDLE.NULL, clock);
            using var scheduler = new Win32FrameScheduler();
            try
            {
                scheduler.SetFrameClock(clock);
                scheduler.RequestFrame(() => { });
                scheduler.RunFrame();

                scheduler.FramePresented();
                scheduler.RequestFrame(() => { });
                Assert.Equal((clock, Win32FrameScheduler.SwapChainWaitTimeoutMilliseconds, false), State(scheduler));
            }
            finally
            {
                scheduler.CancelFrame();
                _ = CloseHandle(clock);
            }
        });

    /// <summary>
    /// After a present that did not wait (the synchronous resize path), the next signal of the swap chain's object is taken
    /// without running the frame, and the one after it runs the frame (design §2.11, "Presents that did not wait").
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A scheduler whose frame clock is an event in place of a swap chain's object.
    ///
    /// Steps:
    /// - Report a present from outside any callback, then ask for a frame that presents and asks for the next one.
    /// - Give the scheduler a signaled wait, then another, then a third.
    ///
    /// Expected:
    /// - The first signal runs no frame: the callback has not run, a frame is still pending, and one signal was taken for the
    ///   present.
    /// - The second signal runs the frame once.
    /// - The third runs the frame the second asked for: the present inside a frame that waited owes nothing.
    /// </remarks>
    [Fact]
    public void PresentThatDidNotWait_NextSignalRunsNoFrame() =>
        ui.Invoke(() =>
        {
            HANDLE clock = CreateEventW(null, BOOL.TRUE, BOOL.TRUE, null);
            Assert.NotEqual(HANDLE.NULL, clock);
            using var scheduler = new Win32FrameScheduler();
            try
            {
                scheduler.SetFrameClock(clock);
                int runs = 0;
                Action frame = null!;
                frame = () =>
                {
                    runs++;
                    scheduler.FramePresented();
                    scheduler.RequestFrame(frame);
                };

                scheduler.FramePresented();
                scheduler.RequestFrame(frame);
                scheduler.RunFrame(WAIT.WAIT_OBJECT_0);
                Assert.Equal((0, true, 1L), (runs, scheduler.HasPendingFrame, scheduler.SignalsOwed));

                scheduler.RunFrame(WAIT.WAIT_OBJECT_0);
                Assert.Equal(1, runs);

                scheduler.RunFrame(WAIT.WAIT_OBJECT_0);
                Assert.Equal((2, 1L), (runs, scheduler.SignalsOwed));
            }
            finally
            {
                scheduler.CancelFrame();
                _ = CloseHandle(clock);
            }
        });

    /// <summary>
    /// A frame run on the wait's timeout presents without having waited, so the next signal runs no frame; the frames after it
    /// run on every signal.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A scheduler whose frame clock is an event in place of a swap chain's object.
    ///
    /// Steps:
    /// - Ask for a frame that presents and asks for the next one, and run it on a timed-out wait.
    /// - Give the scheduler three signaled waits.
    ///
    /// Expected:
    /// - The timed-out wait runs the frame and is counted as a timeout.
    /// - The first signal runs no frame and is counted as owed; the next two run a frame each.
    /// </remarks>
    [Fact]
    public void FrameOnTimeout_NextSignalRunsNoFrame() =>
        ui.Invoke(() =>
        {
            HANDLE clock = CreateEventW(null, BOOL.TRUE, BOOL.TRUE, null);
            Assert.NotEqual(HANDLE.NULL, clock);
            using var scheduler = new Win32FrameScheduler();
            try
            {
                scheduler.SetFrameClock(clock);
                int runs = 0;
                Action frame = null!;
                frame = () =>
                {
                    runs++;
                    scheduler.FramePresented();
                    scheduler.RequestFrame(frame);
                };

                scheduler.RequestFrame(frame);
                scheduler.RunFrame(WAIT.WAIT_TIMEOUT);
                Assert.Equal((1, 1L), (runs, scheduler.ClockTimeouts));

                scheduler.RunFrame(WAIT.WAIT_OBJECT_0);
                Assert.Equal((1, 1L), (runs, scheduler.SignalsOwed));

                scheduler.RunFrame(WAIT.WAIT_OBJECT_0);
                scheduler.RunFrame(WAIT.WAIT_OBJECT_0);
                Assert.Equal((3, 1L), (runs, scheduler.SignalsOwed));
            }
            finally
            {
                scheduler.CancelFrame();
                _ = CloseHandle(clock);
            }
        });

    /// <summary>
    /// A frame that took a signal and did not present, then one on the timer that presents, owe nothing: the next signal runs a
    /// frame.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A scheduler whose frame clock is an event in place of a swap chain's object.
    ///
    /// Steps:
    /// - Run a frame that presents nothing and asks for the next one, on a signaled wait.
    /// - Run that one, now on the timer, where it presents and asks for one more.
    /// - Give the scheduler a signaled wait.
    ///
    /// Expected:
    /// - The frame on the timer runs (the clock is the timer after the frame without a present).
    /// - The last signal runs the third frame, and no signal was counted as owed.
    /// </remarks>
    [Fact]
    public void FrameWithoutPresentThenTimer_OwesNothing() =>
        ui.Invoke(() =>
        {
            HANDLE clock = CreateEventW(null, BOOL.TRUE, BOOL.TRUE, null);
            Assert.NotEqual(HANDLE.NULL, clock);
            using var scheduler = new Win32FrameScheduler();
            try
            {
                scheduler.SetFrameClock(clock);
                int runs = 0;
                Action last = () => runs++;
                Action presents = () =>
                {
                    runs++;
                    scheduler.FramePresented();
                    scheduler.RequestFrame(last);
                };
                Action presentsNothing = () =>
                {
                    runs++;
                    scheduler.RequestFrame(presents);
                };

                scheduler.RequestFrame(presentsNothing);
                scheduler.RunFrame(WAIT.WAIT_OBJECT_0);
                Assert.NotEqual(clock, scheduler.WaitHandle);

                scheduler.RunFrame(WAIT.WAIT_OBJECT_0);
                Assert.Equal(clock, scheduler.WaitHandle);

                scheduler.RunFrame(WAIT.WAIT_OBJECT_0);
                Assert.Equal((3, 0L), (runs, scheduler.SignalsOwed));
            }
            finally
            {
                scheduler.CancelFrame();
                _ = CloseHandle(clock);
            }
        });

    private static (HANDLE Handle, uint Timeout, bool TimerRunning) State(Win32FrameScheduler scheduler) =>
        (scheduler.WaitHandle, scheduler.WaitTimeoutMilliseconds, scheduler.IsClockRunning);
}
