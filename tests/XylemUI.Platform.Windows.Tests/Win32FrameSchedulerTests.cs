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

    private static (HANDLE Handle, uint Timeout, bool TimerRunning) State(Win32FrameScheduler scheduler) =>
        (scheduler.WaitHandle, scheduler.WaitTimeoutMilliseconds, scheduler.IsClockRunning);
}
